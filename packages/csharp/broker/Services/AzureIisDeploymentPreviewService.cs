using System.Text.Json;
using BrokerCore.Contracts;

namespace Broker.Services;

public sealed class AzureIisDeploymentPreviewService
{
    private readonly IAzureIisDeploymentRequestBuilder _builder;
    private readonly AzureIisDeploymentHealthCheckService _healthChecks;

    public AzureIisDeploymentPreviewService(IAzureIisDeploymentRequestBuilder builder, AzureIisDeploymentHealthCheckService healthChecks)
    {
        _builder = builder;
        _healthChecks = healthChecks;
    }

    public AzureIisDeploymentPreviewResult Preview(string toolId, AzureIisDeploymentBuildInput input)
    {
        var built = _builder.TryBuild(toolId, input);
        if (!built.Success || built.Request == null)
            return AzureIisDeploymentPreviewResult.Fail(built.Error ?? "deployment_request_build_failed");

        var request = built.Request;
        var publishArgs = BuildPublishArgs(request);
        var scriptPreview = AzureIisPowerShellScriptBuilder.Build(request);
        var result = AzureIisDeploymentResult.Ok(
            request.RequestId,
            request.TargetId,
            "preview",
            "Azure VM IIS deployment request prepared.",
            publishOutputPath: request.PublishOutputPath,
            packagePath: request.PackagePath,
            scriptPreview: scriptPreview,
            detailsJson: JsonSerializer.Serialize(new
            {
                publish_command = $"dotnet {publishArgs}",
                target = new
                {
                    request.VmHost,
                    request.Port,
                    request.UseSsl,
                    request.SiteName,
                    request.DeploymentMode,
                    request.ApplicationPath,
                    request.AppPoolName,
                    request.PhysicalPath,
                    request.HealthCheckPath,
                    request.HealthCheckBaseUrl,
                    health_check_url = _healthChecks.BuildHealthCheckUrl(request)
                }
            }));

        return AzureIisDeploymentPreviewResult.Ok(request, result);
    }

    public static string BuildPublishArgs(AzureIisDeploymentRequest request)
    {
        var parts = new List<string>
        {
            "publish",
            Quote(request.ProjectFile),
            "-c",
            request.Configuration,
            "-o",
            Quote(request.PublishOutputPath)
        };

        if (!string.IsNullOrWhiteSpace(request.RuntimeIdentifier))
        {
            parts.Add("-r");
            parts.Add(request.RuntimeIdentifier);
        }

        if (request.SelfContained)
            parts.Add("--self-contained");

        return string.Join(" ", parts);
    }

    private static string Quote(string value)
        => $"\"{value}\"";
}

public static class AzureIisPowerShellScriptBuilder
{
    public static string Build(AzureIisDeploymentRequest request)
    {
        var lines = new List<string>
        {
            "$ErrorActionPreference = 'Stop'",
            "$userName = $env:B4A_DEPLOY_USERNAME",
            "$password = $env:B4A_DEPLOY_PASSWORD",
            "if ([string]::IsNullOrWhiteSpace($userName) -or [string]::IsNullOrWhiteSpace($password)) {",
            "    throw 'Missing deployment credentials in environment.'",
            "}",
            "$securePassword = ConvertTo-SecureString $password -AsPlainText -Force",
            "$credential = New-Object System.Management.Automation.PSCredential($userName, $securePassword)",
            "$sessionArgs = @{",
            $"    ComputerName = '{EscapeSingleQuotes(request.VmHost)}'",
            "    Credential = $credential",
            "    Authentication = 'Default'",
            "}",
            $"if ({request.UseSsl.ToString().ToLowerInvariant()}) {{",
            "    $sessionArgs.UseSSL = $true",
            "}",
            $"if ({request.Port} -gt 0) {{",
            $"    $sessionArgs.Port = {request.Port}",
            "}",
            "$session = New-PSSession @sessionArgs",
            "try {",
            $"    $physicalPath = '{EscapeSingleQuotes(request.PhysicalPath)}'",
            $"    $siteName = '{EscapeSingleQuotes(request.SiteName)}'",
            $"    $deploymentMode = '{EscapeSingleQuotes(request.DeploymentMode)}'",
            $"    $applicationPath = '{EscapeSingleQuotes(request.ApplicationPath)}'",
            $"    $appPoolName = '{EscapeSingleQuotes(request.AppPoolName)}'",
            "    Invoke-Command -Session $session -ArgumentList $physicalPath, $siteName, $deploymentMode, $applicationPath, $appPoolName -ScriptBlock {",
            "        param($physicalPath, $siteName, $deploymentMode, $applicationPath, $appPoolName)",
            "        Import-Module WebAdministration",
            "        if (!(Test-Path $physicalPath)) { New-Item -Path $physicalPath -ItemType Directory -Force | Out-Null }",
            "        if ($appPoolName -and (Test-Path (\"IIS:\\AppPools\\\" + $appPoolName))) { Stop-WebAppPool -Name $appPoolName -ErrorAction SilentlyContinue }"
        };

        if (request.RestartSite && string.Equals(request.DeploymentMode, "site_root", StringComparison.Ordinal))
        {
            lines.Add("        if ($siteName -and (Test-Path (\"IIS:\\Sites\\\" + $siteName))) { Stop-Website -Name $siteName -ErrorAction SilentlyContinue }");
        }

        if (request.CleanupTarget)
        {
            lines.Add("        Get-ChildItem -Path $physicalPath -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue");
        }

        lines.AddRange(
        [
            "    }",
            $"    Copy-Item -ToSession $session -Path '{EscapeSingleQuotes(request.PackagePath)}' -Destination ($physicalPath + '\\deploy.zip') -Force",
            "    Invoke-Command -Session $session -ArgumentList $physicalPath, $siteName, $deploymentMode, $applicationPath, $appPoolName -ScriptBlock {",
            "        param($physicalPath, $siteName, $deploymentMode, $applicationPath, $appPoolName)",
            "        Import-Module WebAdministration",
            "        Expand-Archive -Path ($physicalPath + '\\deploy.zip') -DestinationPath $physicalPath -Force",
            "        Remove-Item -Path ($physicalPath + '\\deploy.zip') -Force -ErrorAction SilentlyContinue",
            "        if ($deploymentMode -eq 'iis_application') {",
            "            $applicationName = $applicationPath.TrimStart('/').Replace('/', '\\')",
            "            $iisApplicationPath = ('IIS:\\Sites\\' + $siteName + '\\' + $applicationName)",
            "            if (!(Test-Path $iisApplicationPath)) {",
            "                if ($appPoolName) {",
            "                    New-WebApplication -Site $siteName -Name $applicationName -PhysicalPath $physicalPath -ApplicationPool $appPoolName | Out-Null",
            "                }",
            "                else {",
            "                    New-WebApplication -Site $siteName -Name $applicationName -PhysicalPath $physicalPath | Out-Null",
            "                }",
            "            }",
            "            else {",
            "                Set-ItemProperty -Path $iisApplicationPath -Name physicalPath -Value $physicalPath",
            "                if ($appPoolName) { Set-ItemProperty -Path $iisApplicationPath -Name applicationPool -Value $appPoolName }",
            "            }",
            "        }",
            "        if ($appPoolName -and (Test-Path (\"IIS:\\AppPools\\\" + $appPoolName))) { Start-WebAppPool -Name $appPoolName }"
        ]);

        if (request.RestartSite && string.Equals(request.DeploymentMode, "site_root", StringComparison.Ordinal))
        {
            lines.Add("        if ($siteName -and (Test-Path (\"IIS:\\Sites\\\" + $siteName))) { Start-Website -Name $siteName }");
        }

        lines.AddRange(
        [
            "    }",
            "}",
            "finally {",
            "    if ($session) { Remove-PSSession -Session $session }",
            "}"
        ]);

        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeSingleQuotes(string value)
    {
        return value.Replace("'", "''");
    }
}

public sealed class AzureIisDeploymentPreviewResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public AzureIisDeploymentRequest? Request { get; set; }
    public AzureIisDeploymentResult? Result { get; set; }

    public static AzureIisDeploymentPreviewResult Ok(AzureIisDeploymentRequest request, AzureIisDeploymentResult result)
        => new()
        {
            Success = true,
            Request = request,
            Result = result
        };

    public static AzureIisDeploymentPreviewResult Fail(string error)
        => new()
        {
            Success = false,
            Error = error
        };
}
