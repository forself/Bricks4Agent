using System.IO.Compression;
using System.Text.Json;
using BrokerCore.Contracts;

namespace Broker.Services;

public sealed class AzureIisDeploymentExecutionService
{
    private readonly IAzureIisDeploymentRequestBuilder _builder;
    private readonly IAzureIisDeploymentSecretResolver _secretResolver;
    private readonly IProcessRunner _processRunner;
    private readonly AzureIisDeploymentHealthCheckService _healthChecks;

    public AzureIisDeploymentExecutionService(
        IAzureIisDeploymentRequestBuilder builder,
        IAzureIisDeploymentSecretResolver secretResolver,
        IProcessRunner processRunner,
        AzureIisDeploymentHealthCheckService healthChecks)
    {
        _builder = builder;
        _secretResolver = secretResolver;
        _processRunner = processRunner;
        _healthChecks = healthChecks;
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
            return AzureIisDeploymentExecutionEnvelope.Fail(
                "deployment_publish_failed",
                AzureIisDeploymentResult.Fail(request.RequestId, request.TargetId, "publish", publishRun.StandardError));
        }

        ZipFile.CreateFromDirectory(request.PublishOutputPath, request.PackagePath, CompressionLevel.Optimal, includeBaseDirectory: false);
        var scriptPath = Path.Combine(requestRoot, "deploy.ps1");
        var scriptPreview = AzureIisPowerShellScriptBuilder.Build(request);
        File.WriteAllText(scriptPath, scriptPreview);

        if (dryRun)
        {
            return AzureIisDeploymentExecutionEnvelope.Ok(
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
            return AzureIisDeploymentExecutionEnvelope.Fail(
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
            return AzureIisDeploymentExecutionEnvelope.Fail(
                "deployment_remote_failed",
                AzureIisDeploymentResult.Fail(request.RequestId, request.TargetId, "remote_deploy", deployRun.StandardError));
        }

        var healthCheck = await _healthChecks.CheckAsync(request, cancellationToken);
        if (healthCheck.Attempted && !healthCheck.Success)
        {
            return AzureIisDeploymentExecutionEnvelope.Fail(
                "deployment_health_check_failed",
                AzureIisDeploymentResult.Fail(
                    request.RequestId,
                    request.TargetId,
                    "health_check",
                    string.IsNullOrWhiteSpace(healthCheck.Error)
                        ? $"Health check failed: {healthCheck.Url} returned {(healthCheck.StatusCode?.ToString() ?? "unknown")}."
                        : $"Health check failed: {healthCheck.Error}"));
        }

        return AzureIisDeploymentExecutionEnvelope.Ok(
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
