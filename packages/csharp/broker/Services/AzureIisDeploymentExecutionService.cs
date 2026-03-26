using System.IO.Compression;
using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

public sealed class AzureIisDeploymentExecutionService
{
    private readonly IAzureIisDeploymentRequestBuilder _builder;
    private readonly IAzureIisDeploymentSecretResolver _secretResolver;
    private readonly IProcessRunner _processRunner;
    private readonly AzureIisDeploymentHealthCheckService _healthChecks;
    private readonly ISharedContextService _sharedContextService;
    private readonly BrokerDb _db;

    public AzureIisDeploymentExecutionService(
        IAzureIisDeploymentRequestBuilder builder,
        IAzureIisDeploymentSecretResolver secretResolver,
        IProcessRunner processRunner,
        AzureIisDeploymentHealthCheckService healthChecks,
        ISharedContextService sharedContextService,
        BrokerDb db)
    {
        _builder = builder;
        _secretResolver = secretResolver;
        _processRunner = processRunner;
        _healthChecks = healthChecks;
        _sharedContextService = sharedContextService;
        _db = db;
    }

    public async Task<AzureIisDeploymentExecutionEnvelope> ExecuteAsync(
        string toolId,
        AzureIisDeploymentBuildInput input,
        bool dryRun,
        CancellationToken cancellationToken = default)
    {
        var built = _builder.TryBuild(toolId, input);
        if (!built.Success || built.Request == null)
            return AzureIisDeploymentExecutionEnvelope.Fail(built.Error ?? "deployment_request_build_failed");

        var request = built.Request;
        var requestRoot = Path.GetDirectoryName(request.PublishOutputPath)!;
        Directory.CreateDirectory(requestRoot);
        if (Directory.Exists(request.PublishOutputPath))
            Directory.Delete(request.PublishOutputPath, recursive: true);
        Directory.CreateDirectory(request.PublishOutputPath);
        if (File.Exists(request.PackagePath))
            File.Delete(request.PackagePath);

        var publishRun = await _processRunner.RunAsync(
            new ProcessRunSpec
            {
                FileName = "dotnet",
                Arguments = AzureIisDeploymentPreviewService.BuildPublishArgs(request),
                WorkingDirectory = Path.GetDirectoryName(request.ProjectFile)
            },
            cancellationToken);
        if (publishRun.ExitCode != 0)
        {
            return FailWithEvidence(
                request,
                "deployment_publish_failed",
                AzureIisDeploymentResult.Fail(request.RequestId, request.TargetId, "publish", publishRun.StandardError));
        }

        ZipFile.CreateFromDirectory(request.PublishOutputPath, request.PackagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        var scriptPath = Path.Combine(requestRoot, "deploy.ps1");
        var scriptPreview = AzureIisPowerShellScriptBuilder.Build(request);
        File.WriteAllText(scriptPath, scriptPreview);

        if (dryRun)
        {
            return OkWithEvidence(
                request,
                AzureIisDeploymentResult.Ok(
                    request.RequestId,
                    request.TargetId,
                    "dry_run",
                    "Azure VM IIS deployment package prepared.",
                    publishOutputPath: request.PublishOutputPath,
                    packagePath: request.PackagePath,
                    deploymentScriptPath: scriptPath,
                    scriptPreview: scriptPreview,
                    detailsJson: JsonSerializer.Serialize(new
                    {
                        publish_stdout = publishRun.StandardOutput,
                        publish_stderr = publishRun.StandardError,
                        publish_command = $"dotnet {AzureIisDeploymentPreviewService.BuildPublishArgs(request)}"
                    })));
        }

        var secret = _secretResolver.Resolve(request.SecretRef);
        if (secret == null)
        {
            return FailWithEvidence(
                request,
                "deployment_secret_not_found",
                AzureIisDeploymentResult.Fail(request.RequestId, request.TargetId, "secret_resolution", $"Secret not found: {request.SecretRef}"));
        }

        var deployRun = await _processRunner.RunAsync(
            new ProcessRunSpec
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                WorkingDirectory = requestRoot,
                EnvironmentVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["B4A_DEPLOY_USERNAME"] = secret.UserName,
                    ["B4A_DEPLOY_PASSWORD"] = secret.Password
                }
            },
            cancellationToken);
        if (deployRun.ExitCode != 0)
        {
            return FailWithEvidence(
                request,
                "deployment_remote_failed",
                AzureIisDeploymentResult.Fail(request.RequestId, request.TargetId, "remote_deploy", deployRun.StandardError));
        }

        var healthCheck = await _healthChecks.CheckAsync(request, cancellationToken);
        if (healthCheck.Attempted && !healthCheck.Success)
        {
            return FailWithEvidence(
                request,
                "deployment_health_check_failed",
                AzureIisDeploymentResult.Fail(
                    request.RequestId,
                    request.TargetId,
                    "health_check",
                    string.IsNullOrWhiteSpace(healthCheck.Error)
                        ? $"Health check failed: {healthCheck.Url} returned {(healthCheck.StatusCode?.ToString() ?? "unknown")}."
                        : $"Health check failed: {healthCheck.Error}"));
        }

        return OkWithEvidence(
            request,
            AzureIisDeploymentResult.Ok(
                request.RequestId,
                request.TargetId,
                "deployed",
                "Azure VM IIS deployment completed.",
                publishOutputPath: request.PublishOutputPath,
                packagePath: request.PackagePath,
                deploymentScriptPath: scriptPath,
                scriptPreview: scriptPreview,
                detailsJson: JsonSerializer.Serialize(new
                {
                    publish_stdout = publishRun.StandardOutput,
                    publish_stderr = publishRun.StandardError,
                    deploy_stdout = deployRun.StandardOutput,
                    deploy_stderr = deployRun.StandardError,
                    health_check = new
                    {
                        attempted = healthCheck.Attempted,
                        success = healthCheck.Success,
                        url = healthCheck.Url,
                        status_code = healthCheck.StatusCode,
                        body_snippet = healthCheck.BodySnippet,
                        error = healthCheck.Error
                    }
                })));
    }

    public IReadOnlyList<DeploymentExecutionEvidenceSummary> ListRecentExecutions(int limit = 20, string? targetId = null)
    {
        if (limit <= 0)
            limit = 20;

        var sql = """
            SELECT *
              FROM shared_context_entries
             WHERE document_id LIKE 'deployment.execution.%'
        """;
        var args = new Dictionary<string, object?>
        {
            ["limit"] = limit
        };

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            sql += " AND key = @key";
            args["key"] = $"deployment.target.{targetId}";
        }

        sql += " ORDER BY created_at DESC LIMIT @limit";
        var entries = _db.Query<SharedContextEntry>(sql, args);
        var items = new List<DeploymentExecutionEvidenceSummary>();
        foreach (var entry in entries)
        {
            var detail = TryReadEvidenceEntry(entry);
            if (detail == null)
                continue;

            items.Add(new DeploymentExecutionEvidenceSummary
            {
                DocumentId = entry.DocumentId,
                RequestId = detail.RequestId,
                TargetId = detail.TargetId,
                Stage = detail.Stage,
                Success = detail.Success,
                Message = detail.Message,
                CreatedAt = entry.CreatedAt
            });
        }

        return items;
    }

    public DeploymentExecutionEvidenceDetail? ReadExecution(string documentId)
    {
        var entry = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        return entry == null ? null : TryReadEvidenceEntry(entry);
    }

    private AzureIisDeploymentExecutionEnvelope OkWithEvidence(AzureIisDeploymentRequest request, AzureIisDeploymentResult result)
    {
        WriteEvidence(request, result);
        return AzureIisDeploymentExecutionEnvelope.Ok(request, result);
    }

    private AzureIisDeploymentExecutionEnvelope FailWithEvidence(AzureIisDeploymentRequest request, string error, AzureIisDeploymentResult result)
    {
        WriteEvidence(request, result);
        return AzureIisDeploymentExecutionEnvelope.Fail(error, result);
    }

    private string WriteEvidence(AzureIisDeploymentRequest request, AzureIisDeploymentResult result)
    {
        var documentId = $"deployment.execution.{request.RequestId}";
        var content = JsonSerializer.Serialize(new
        {
            request_id = request.RequestId,
            target_id = request.TargetId,
            project_file = request.ProjectFile,
            deployment_mode = request.DeploymentMode,
            application_path = request.ApplicationPath,
            health_check_url = _healthChecks.BuildHealthCheckUrl(request),
            success = result.Success,
            stage = result.Stage,
            message = result.Message,
            publish_output_path = result.PublishOutputPath,
            package_path = result.PackagePath,
            deployment_script_path = result.DeploymentScriptPath,
            details_json = result.DetailsJson,
            created_at = DateTimeOffset.UtcNow
        });

        _sharedContextService.Write(
            request.PrincipalId,
            documentId,
            $"deployment.target.{request.TargetId}",
            content,
            "application/evidence",
            "{\"read\":[\"*\"],\"write\":[\"system:deployment-runtime\", \"*\"]}",
            request.TaskId);

        return documentId;
    }

    private static DeploymentExecutionEvidenceDetail? TryReadEvidenceEntry(SharedContextEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ContentRef))
            return null;

        try
        {
            using var document = JsonDocument.Parse(entry.ContentRef);
            var root = document.RootElement;
            return new DeploymentExecutionEvidenceDetail
            {
                RequestId = ReadString(root, "request_id"),
                TargetId = ReadString(root, "target_id"),
                ProjectFile = ReadString(root, "project_file"),
                DeploymentMode = ReadString(root, "deployment_mode"),
                ApplicationPath = ReadOptionalString(root, "application_path"),
                HealthCheckUrl = ReadOptionalString(root, "health_check_url"),
                Success = ReadBool(root, "success"),
                Stage = ReadString(root, "stage"),
                Message = ReadString(root, "message"),
                PublishOutputPath = ReadString(root, "publish_output_path"),
                PackagePath = ReadString(root, "package_path"),
                DeploymentScriptPath = ReadString(root, "deployment_script_path"),
                DetailsJson = ReadString(root, "details_json"),
                CreatedAt = ReadDateTimeOffset(root, "created_at")
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string? ReadOptionalString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset ReadDateTimeOffset(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return default;

        return DateTimeOffset.TryParse(value.GetString(), out var parsed) ? parsed : default;
    }
}

public sealed class AzureIisDeploymentExecutionEnvelope
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public AzureIisDeploymentRequest? Request { get; set; }
    public AzureIisDeploymentResult? Result { get; set; }

    public static AzureIisDeploymentExecutionEnvelope Ok(AzureIisDeploymentRequest request, AzureIisDeploymentResult result)
        => new()
        {
            Success = true,
            Request = request,
            Result = result
        };

    public static AzureIisDeploymentExecutionEnvelope Fail(string error, AzureIisDeploymentResult? result = null)
        => new()
        {
            Success = false,
            Error = error,
            Result = result
        };
}

public sealed class DeploymentExecutionEvidenceSummary
{
    public string DocumentId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public sealed class DeploymentExecutionEvidenceDetail
{
    public string RequestId { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string ProjectFile { get; set; } = string.Empty;
    public string DeploymentMode { get; set; } = string.Empty;
    public string? ApplicationPath { get; set; }
    public string? HealthCheckUrl { get; set; }
    public bool Success { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string PublishOutputPath { get; set; } = string.Empty;
    public string PackagePath { get; set; } = string.Empty;
    public string DeploymentScriptPath { get; set; } = string.Empty;
    public string DetailsJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
}
