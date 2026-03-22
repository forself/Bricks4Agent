using System.Text.Json;
using Broker.Helpers;
using Broker.Services;
using BrokerCore.Models;

namespace Broker.Endpoints;

public static class DeploymentAdminEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var deployment = group.MapGroup("/deployment-admin");

        deployment.MapPost("/targets/list", (HttpContext ctx, AzureIisDeploymentTargetService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            var status = body.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            return Results.Ok(ApiResponseHelper.Success(service.ListTargets(status)));
        });

        deployment.MapPost("/targets/get", (HttpContext ctx, AzureIisDeploymentTargetService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "target_id", out var targetId, out var err))
                return err!;

            var target = service.GetTarget(targetId);
            return target == null
                ? Results.NotFound(ApiResponseHelper.Error("Deployment target not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(target));
        });

        deployment.MapPost("/targets/upsert", (HttpContext ctx, AzureIisDeploymentTargetService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequiredFields(
                    body,
                    new[] { "display_name", "vm_host", "site_name", "app_pool_name", "physical_path", "secret_ref" },
                    out var values,
                    out var err))
            {
                return err!;
            }

            var target = new AzureIisDeploymentTarget
            {
                TargetId = body.TryGetProperty("target_id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                DisplayName = values["display_name"],
                Provider = body.TryGetProperty("provider", out var providerProp) ? providerProp.GetString() ?? "azure_vm_iis" : "azure_vm_iis",
                VmHost = values["vm_host"],
                Port = body.TryGetProperty("port", out var portProp) && portProp.TryGetInt32(out var portValue) ? portValue : 5985,
                UseSsl = body.TryGetProperty("use_ssl", out var sslProp) && sslProp.ValueKind == JsonValueKind.True,
                Transport = body.TryGetProperty("transport", out var transportProp) ? transportProp.GetString() ?? "winrm_powershell" : "winrm_powershell",
                SiteName = values["site_name"],
                AppPoolName = values["app_pool_name"],
                PhysicalPath = values["physical_path"],
                SecretRef = values["secret_ref"],
                Status = body.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "active" : "active",
                MetadataJson = body.TryGetProperty("metadata_json", out var metadataProp) ? metadataProp.GetRawText() : "{}"
            };

            return Results.Ok(ApiResponseHelper.Success(service.UpsertTarget(target)));
        });

        deployment.MapPost("/requests/build", (HttpContext ctx, IAzureIisDeploymentRequestBuilder builder) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!TryParseBuildInput(body, out var input, out var err))
                return err!;

            var toolId = body.TryGetProperty("tool_id", out var toolProp) ? toolProp.GetString() ?? "deploy.azure-vm-iis" : "deploy.azure-vm-iis";
            var result = builder.TryBuild(toolId, input);
            if (!result.Success)
                return Results.BadRequest(ApiResponseHelper.Error(result.Error ?? "deployment_request_build_failed"));

            return Results.Ok(ApiResponseHelper.Success(result.Request));
        });

        deployment.MapPost("/requests/preview", (HttpContext ctx, AzureIisDeploymentPreviewService previewService) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!TryParseBuildInput(body, out var input, out var err))
                return err!;

            var toolId = body.TryGetProperty("tool_id", out var toolProp) ? toolProp.GetString() ?? "deploy.azure-vm-iis" : "deploy.azure-vm-iis";
            var result = previewService.Preview(toolId, input);
            if (!result.Success)
                return Results.BadRequest(ApiResponseHelper.Error(result.Error ?? "deployment_preview_failed"));

            return Results.Ok(ApiResponseHelper.Success(new
            {
                request = result.Request,
                result = result.Result
            }));
        });

        deployment.MapPost("/requests/execute", async (HttpContext ctx, AzureIisDeploymentExecutionService executionService, CancellationToken cancellationToken) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!TryParseBuildInput(body, out var input, out var err))
                return err!;

            var dryRun = body.TryGetProperty("dry_run", out var dryRunProp) && dryRunProp.ValueKind == JsonValueKind.True;
            var toolId = body.TryGetProperty("tool_id", out var toolProp) ? toolProp.GetString() ?? "deploy.azure-vm-iis" : "deploy.azure-vm-iis";
            var result = await executionService.ExecuteAsync(toolId, input, dryRun, cancellationToken);
            if (!result.Success)
                return Results.BadRequest(ApiResponseHelper.Error(result.Result?.Message ?? result.Error ?? "deployment_execute_failed"));

            return Results.Ok(ApiResponseHelper.Success(new
            {
                request = result.Request,
                result = result.Result
            }));
        });
    }

    private static bool TryParseBuildInput(JsonElement body, out AzureIisDeploymentBuildInput input, out IResult? error)
    {
        error = null;
        input = new AzureIisDeploymentBuildInput();
        if (!RequestBodyHelper.TryGetRequiredFields(
                body,
                new[] { "capability_id", "route", "principal_id", "task_id", "session_id", "target_id", "project_path" },
                out var values,
                out error))
        {
            return false;
        }

        input = new AzureIisDeploymentBuildInput
        {
            RequestId = body.TryGetProperty("request_id", out var requestProp) ? requestProp.GetString() ?? BrokerCore.IdGen.New("dreq") : BrokerCore.IdGen.New("dreq"),
            CapabilityId = values["capability_id"],
            Route = values["route"],
            PrincipalId = values["principal_id"],
            TaskId = values["task_id"],
            SessionId = values["session_id"],
            TargetId = values["target_id"],
            ProjectPath = values["project_path"],
            Configuration = body.TryGetProperty("configuration", out var configProp) ? configProp.GetString() ?? "Release" : "Release",
            RuntimeIdentifier = body.TryGetProperty("runtime_identifier", out var ridProp) ? ridProp.GetString() : null,
            SelfContained = body.TryGetProperty("self_contained", out var scProp) && scProp.ValueKind == JsonValueKind.True,
            CleanupTarget = !body.TryGetProperty("cleanup_target", out var cleanupProp) || cleanupProp.ValueKind == JsonValueKind.True,
            RestartSite = !body.TryGetProperty("restart_site", out var restartProp) || restartProp.ValueKind == JsonValueKind.True,
            ScopeJson = body.TryGetProperty("scope_json", out var scopeProp) ? scopeProp.GetRawText() : "{}",
            MetadataJson = body.TryGetProperty("metadata_json", out var metadataProp) ? metadataProp.GetRawText() : "{}"
        };
        return true;
    }

    private static bool RequireAdmin(HttpContext ctx, out IResult denied)
    {
        if (RequestBodyHelper.IsAdmin(ctx))
        {
            denied = null!;
            return true;
        }

        denied = Results.Json(ApiResponseHelper.Error("Forbidden: admin role required.", 403), statusCode: 403);
        return false;
    }
}
