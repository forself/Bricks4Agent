using System.Text.Json;
using Broker.Helpers;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Services;

namespace Broker.Endpoints;

public static class LocalAdminEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var localAdmin = group.MapGroup("/local-admin");

        localAdmin.MapGet("/status", (HttpContext ctx, LocalAdminAuthService auth) =>
            Results.Ok(ApiResponseHelper.Success(auth.GetStatus(ctx))));

        localAdmin.MapPost("/login", (HttpContext ctx, LocalAdminAuthService auth) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "password", out var password, out var error))
                return error!;

            var newPassword = body.TryGetProperty("new_password", out var np) && np.ValueKind == JsonValueKind.String
                ? np.GetString()
                : null;

            try
            {
                var result = auth.Login(ctx, password, newPassword);
                return Results.Ok(ApiResponseHelper.Success(result));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        localAdmin.MapPost("/change-password", (HttpContext ctx, LocalAdminAuthService auth) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "current_password", out var currentPassword, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "new_password", out var newPassword, out error))
                return error!;

            try
            {
                var result = auth.ChangePassword(ctx, currentPassword, newPassword);
                return Results.Ok(ApiResponseHelper.Success(result));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        localAdmin.MapPost("/logout", (HttpContext ctx, LocalAdminAuthService auth) =>
        {
            auth.Logout(ctx);
            return Results.Ok(ApiResponseHelper.Success(new { ok = true }));
        });

        localAdmin.MapGet("/system/status", (HttpContext ctx, LocalAdminAuthService auth, BrokerDb db, ILlmProxyService llm, EmbeddingService embedding, LlmProxyOptions llmOptions, RagPipelineService rag) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;

            bool llmOk = false;
            string llmError = string.Empty;
            try { llmOk = llm.HealthCheckAsync().GetAwaiter().GetResult(); }
            catch (Exception ex) { llmError = ex.Message; }

            var articleCount = db.GetAll<BrokerCore.Models.SharedContextEntry>().Count;
            var vectorCount = db.GetAll<BrokerCore.Models.VectorEntry>().Count;
            var convCount = db.Query<int>("SELECT COUNT(DISTINCT key) FROM shared_context_entries WHERE key LIKE 'convlog:%'").FirstOrDefault();

            return Results.Ok(ApiResponseHelper.Success(new
            {
                status = llmOk ? "ok" : "degraded",
                timestamp = DateTime.UtcNow,
                services = new
                {
                    llm = new { ok = llmOk, error = llmError, model = llmOptions.DefaultModel, provider = llmOptions.Provider },
                    embedding = new { ok = embedding.IsEnabled, model = embedding.ModelName },
                    rag_pipeline = new { query_rewrite = rag.QueryRewriteEnabled, rerank = rag.RerankEnabled, cache = rag.CacheEnabled }
                },
                database = new
                {
                    shared_context_entries = articleCount,
                    vector_entries = vectorCount,
                    active_conversations = convCount
                }
            }));
        });

        localAdmin.MapGet("/line/users", (HttpContext ctx, LocalAdminAuthService auth, HighLevelCoordinator coordinator) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var users = coordinator.ListLineUsers();
            return Results.Ok(ApiResponseHelper.Success(new { total = users.Count, users }));
        });

        localAdmin.MapGet("/line/conversations", (HttpContext ctx, LocalAdminAuthService auth, LineChatGateway gateway) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var conversations = gateway.ListConversations();
            return Results.Ok(ApiResponseHelper.Success(new { total = conversations.Count, conversations }));
        });

        localAdmin.MapGet("/line/conversations/{userId}", (HttpContext ctx, LocalAdminAuthService auth, LineChatGateway gateway, string userId) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var messages = gateway.GetConversation(userId);
            return Results.Ok(ApiResponseHelper.Success(new { user_id = userId, total = messages.Count, messages }));
        });

        localAdmin.MapDelete("/line/conversations/{userId}", (HttpContext ctx, LocalAdminAuthService auth, LineChatGateway gateway, string userId) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            gateway.ClearConversation(userId);
            return Results.Ok(ApiResponseHelper.Success(new { ok = true }));
        });

        localAdmin.MapPost("/line/chat", async (HttpContext ctx, LocalAdminAuthService auth, LineChatGateway gateway) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "user_id", out var userId, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "message", out var message, out error))
                return error!;
            var result = await gateway.ChatAsync(userId, message, ctx.RequestAborted);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                reply = result.Reply,
                error = result.Error,
                rag_snippets = result.RagSnippets,
                history_count = result.HistoryCount
            }));
        });

        localAdmin.MapGet("/line/registration-policy", (HttpContext ctx, LocalAdminAuthService auth, HighLevelCoordinator coordinator) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            return Results.Ok(ApiResponseHelper.Success(new { policy = coordinator.GetLineAnonymousRegistrationPolicy() }));
        });

        localAdmin.MapPost("/line/registration-policy", (HttpContext ctx, LocalAdminAuthService auth, HighLevelCoordinator coordinator) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "policy", out var policy, out var error))
                return error!;
            return Results.Ok(ApiResponseHelper.Success(new { policy = coordinator.SetLineAnonymousRegistrationPolicy(policy) }));
        });

        localAdmin.MapPost("/line/users/permissions", (HttpContext ctx, LocalAdminAuthService auth, HighLevelCoordinator coordinator) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "user_id", out var userId, out var error))
                return error!;

            static bool? ReadBoolean(JsonElement body, string name)
            {
                if (!body.TryGetProperty(name, out var prop))
                    return null;
                return prop.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                };
            }

            var updated = coordinator.SetLineUserPermissions(userId, new HighLevelUserPermissionsPatch
            {
                AllowQuery = ReadBoolean(body, "allow_query"),
                AllowTransport = ReadBoolean(body, "allow_transport"),
                AllowProduction = ReadBoolean(body, "allow_production"),
                AllowBrowserDelegated = ReadBoolean(body, "allow_browser_delegated"),
                AllowDeployment = ReadBoolean(body, "allow_deployment")
            });

            return updated == null
                ? Results.NotFound(ApiResponseHelper.Error("Profile not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(updated));
        });

        localAdmin.MapPost("/line/users/registration/review", (HttpContext ctx, LocalAdminAuthService auth, HighLevelCoordinator coordinator) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequiredFields(body, new[] { "user_id", "action" }, out var values, out var error))
                return error!;
            var note = body.TryGetProperty("note", out var noteProp) && noteProp.ValueKind == JsonValueKind.String ? noteProp.GetString() : null;
            var reviewed = coordinator.ReviewLineUserRegistration(values["user_id"], values["action"], note);
            return reviewed == null
                ? Results.NotFound(ApiResponseHelper.Error("Profile not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(reviewed));
        });

        localAdmin.MapGet("/browser/site-bindings", (HttpContext ctx, LocalAdminAuthService auth, BrowserBindingService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var identityMode = ctx.Request.Query["identity_mode"].ToString();
            var principalId = ctx.Request.Query["principal_id"].ToString();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                items = service.ListSiteBindings(string.IsNullOrWhiteSpace(identityMode) ? null : identityMode, string.IsNullOrWhiteSpace(principalId) ? null : principalId)
            }));
        });

        localAdmin.MapPost("/browser/site-bindings", async (HttpContext ctx, LocalAdminAuthService auth, BrowserBindingService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "display_name", out var displayName, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "identity_mode", out var identityMode, out error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "site_class", out var siteClass, out error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "origin", out var origin, out error))
                return error!;
            var binding = new BrokerCore.Models.BrowserSiteBinding
            {
                SiteBindingId = GetString(body, "site_binding_id"),
                DisplayName = displayName,
                IdentityMode = identityMode,
                SiteClass = siteClass,
                Origin = origin,
                PrincipalId = GetOptionalString(body, "principal_id"),
                Status = GetString(body, "status", "active"),
                MetadataJson = GetRawJson(body, "metadata_json", "{}")
            };
            return Results.Ok(ApiResponseHelper.Success(new { item = service.UpsertSiteBinding(binding) }));
        });

        localAdmin.MapGet("/browser/user-grants", (HttpContext ctx, LocalAdminAuthService auth, BrowserBindingService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var principalId = ctx.Request.Query["principal_id"].ToString();
            return Results.Ok(ApiResponseHelper.Success(new { items = service.ListUserGrants(string.IsNullOrWhiteSpace(principalId) ? null : principalId) }));
        });

        localAdmin.MapPost("/browser/user-grants", (HttpContext ctx, LocalAdminAuthService auth, BrowserBindingService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "principal_id", out var principalId, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "consent_ref", out var consentRef, out error))
                return error!;
            var grant = new BrokerCore.Models.BrowserUserGrant
            {
                UserGrantId = GetString(body, "user_grant_id"),
                PrincipalId = principalId,
                SiteBindingId = GetOptionalString(body, "site_binding_id"),
                Status = GetString(body, "status", "active"),
                ConsentRef = consentRef,
                ScopesJson = GetRawJson(body, "scopes_json", "{}"),
                ExpiresAt = TryGetDateTime(body, "expires_at")
            };
            return Results.Ok(ApiResponseHelper.Success(new { item = service.UpsertUserGrant(grant) }));
        });

        localAdmin.MapGet("/browser/system-bindings", (HttpContext ctx, LocalAdminAuthService auth, BrowserBindingService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            return Results.Ok(ApiResponseHelper.Success(new { items = service.ListSystemBindings() }));
        });

        localAdmin.MapPost("/browser/system-bindings", (HttpContext ctx, LocalAdminAuthService auth, BrowserBindingService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "display_name", out var displayName, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "secret_ref", out var secretRef, out error))
                return error!;
            var binding = new BrokerCore.Models.BrowserSystemBinding
            {
                SystemBindingId = GetString(body, "system_binding_id"),
                DisplayName = displayName,
                SiteBindingId = GetOptionalString(body, "site_binding_id"),
                Status = GetString(body, "status", "active"),
                SecretRef = secretRef
            };
            return Results.Ok(ApiResponseHelper.Success(new { item = service.UpsertSystemBinding(binding) }));
        });

        localAdmin.MapGet("/browser/leases", (HttpContext ctx, LocalAdminAuthService auth, BrowserBindingService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var principalId = ctx.Request.Query["principal_id"].ToString();
            return Results.Ok(ApiResponseHelper.Success(new { items = service.ListSessionLeases(string.IsNullOrWhiteSpace(principalId) ? null : principalId) }));
        });

        localAdmin.MapPost("/browser/leases/issue", (HttpContext ctx, LocalAdminAuthService auth, BrowserBindingService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "tool_id", out var toolId, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "principal_id", out var principalId, out error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "identity_mode", out var identityMode, out error))
                return error!;
            var expiresAt = TryGetDateTime(body, "expires_at") ?? DateTime.UtcNow.AddMinutes(30);
            var lease = service.IssueSessionLease(toolId, principalId, identityMode, expiresAt, GetOptionalString(body, "site_binding_id"));
            return Results.Ok(ApiResponseHelper.Success(new { item = lease }));
        });

        localAdmin.MapPost("/browser/leases/revoke", (HttpContext ctx, LocalAdminAuthService auth, BrowserBindingService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "session_lease_id", out var leaseId, out var error))
                return error!;
            var lease = service.RevokeSessionLease(leaseId);
            return lease == null
                ? Results.NotFound(ApiResponseHelper.Error("Session lease not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(new { item = lease }));
        });

        localAdmin.MapGet("/deployment/targets", (HttpContext ctx, LocalAdminAuthService auth, AzureIisDeploymentTargetService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var status = ctx.Request.Query["status"].ToString();
            return Results.Ok(ApiResponseHelper.Success(new { items = service.ListTargets(string.IsNullOrWhiteSpace(status) ? null : status) }));
        });

        localAdmin.MapPost("/deployment/targets", (HttpContext ctx, LocalAdminAuthService auth, AzureIisDeploymentTargetService service) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "display_name", out var displayName, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "vm_host", out var vmHost, out error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "site_name", out var siteName, out error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "app_pool_name", out var appPoolName, out error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "physical_path", out var physicalPath, out error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "secret_ref", out var secretRef, out error))
                return error!;
            var target = new BrokerCore.Models.AzureIisDeploymentTarget
            {
                TargetId = GetString(body, "target_id"),
                DisplayName = displayName,
                Provider = GetString(body, "provider", "azure_vm_iis"),
                VmHost = vmHost,
                Port = GetInt(body, "port", 5985),
                UseSsl = GetBool(body, "use_ssl", false),
                Transport = GetString(body, "transport", "winrm_powershell"),
                SiteName = siteName,
                DeploymentMode = GetString(body, "deployment_mode", "site_root"),
                ApplicationPath = GetString(body, "application_path", string.Empty),
                AppPoolName = appPoolName,
                PhysicalPath = physicalPath,
                HealthCheckPath = GetString(body, "health_check_path", string.Empty),
                SecretRef = secretRef,
                Status = GetString(body, "status", "active"),
                MetadataJson = GetRawJson(body, "metadata_json", "{}")
            };
            try
            {
                return Results.Ok(ApiResponseHelper.Success(new { item = service.UpsertTarget(target) }));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        localAdmin.MapPost("/deployment/preview", (HttpContext ctx, LocalAdminAuthService auth, AzureIisDeploymentPreviewService previewService) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "target_id", out var targetId, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "project_path", out var projectPath, out error))
                return error!;
            var input = BuildDeploymentInput(body, targetId, projectPath);
            var result = previewService.Preview("deploy.azure-vm-iis", input);
            return !result.Success
                ? Results.BadRequest(ApiResponseHelper.Error(result.Error ?? "deployment_preview_failed"))
                : Results.Ok(ApiResponseHelper.Success(new { request = result.Request, result = result.Result }));
        });

        localAdmin.MapPost("/deployment/execute", async (HttpContext ctx, LocalAdminAuthService auth, AzureIisDeploymentExecutionService executionService, CancellationToken cancellationToken) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "target_id", out var targetId, out var error))
                return error!;
            if (!RequestBodyHelper.TryGetRequired(body, "project_path", out var projectPath, out error))
                return error!;
            var input = BuildDeploymentInput(body, targetId, projectPath);
            var dryRun = GetBool(body, "dry_run", false);
            var result = await executionService.ExecuteAsync("deploy.azure-vm-iis", input, dryRun, cancellationToken);
            return !result.Success
                ? Results.BadRequest(ApiResponseHelper.Error(result.Result?.Message ?? result.Error ?? "deployment_execute_failed"))
                : Results.Ok(ApiResponseHelper.Success(new { request = result.Request, result = result.Result }));
        });

        localAdmin.MapGet("/tool-specs", (HttpContext ctx, LocalAdminAuthService auth, IToolSpecRegistry registry) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var filter = ctx.Request.Query["filter"].ToString();
            return Results.Ok(ApiResponseHelper.Success(new { items = registry.List(string.IsNullOrWhiteSpace(filter) ? null : filter) }));
        });

        localAdmin.MapPost("/tool-specs/get", (HttpContext ctx, LocalAdminAuthService auth, IToolSpecRegistry registry) =>
        {
            if (!auth.TryRequireAuthenticated(ctx, out _, out var denied))
                return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "tool_id", out var toolId, out var error))
                return error!;
            var spec = registry.Get(toolId);
            return spec == null
                ? Results.NotFound(ApiResponseHelper.Error("Tool spec not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(new { item = spec }));
        });
    }

    private static string GetString(JsonElement body, string name, string defaultValue = "")
        => body.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? defaultValue : defaultValue;

    private static string? GetOptionalString(JsonElement body, string name)
        => body.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

    private static int GetInt(JsonElement body, string name, int defaultValue)
        => body.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value) ? value : defaultValue;

    private static bool GetBool(JsonElement body, string name, bool defaultValue)
        => body.TryGetProperty(name, out var prop) ? prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        } : defaultValue;

    private static DateTime? TryGetDateTime(JsonElement body, string name)
    {
        if (!body.TryGetProperty(name, out var prop) || prop.ValueKind != JsonValueKind.String)
            return null;
        return DateTime.TryParse(prop.GetString(), out var parsed) ? parsed : null;
    }

    private static string GetRawJson(JsonElement body, string name, string defaultJson)
    {
        if (!body.TryGetProperty(name, out var prop))
            return defaultJson;
        return prop.ValueKind == JsonValueKind.String ? (prop.GetString() ?? defaultJson) : prop.GetRawText();
    }

    private static AzureIisDeploymentBuildInput BuildDeploymentInput(JsonElement body, string targetId, string projectPath)
        => new()
        {
            CapabilityId = "deploy.azure-vm-iis",
            Route = "deploy_azure_vm_iis",
            PrincipalId = "prn_dashboard",
            TaskId = "task_dashboard",
            SessionId = "local_admin",
            TargetId = targetId,
            ProjectPath = projectPath
        };
}
