using System.Text.Json;
using Broker.Helpers;
using Broker.Services;
using BrokerCore.Models;

namespace Broker.Endpoints;

public static class BrowserBindingEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var browser = group.MapGroup("/browser-admin");

        browser.MapPost("/site-bindings/list", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;

            var body = RequestBodyHelper.GetBody(ctx);
            var identityMode = body.TryGetProperty("identity_mode", out var identityProp) ? identityProp.GetString() : null;
            var principalId = body.TryGetProperty("principal_id", out var principalProp) ? principalProp.GetString() : null;
            return Results.Ok(ApiResponseHelper.Success(service.ListSiteBindings(identityMode, principalId)));
        });

        browser.MapPost("/site-bindings/get", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "site_binding_id", out var siteBindingId, out var err))
                return err!;

            var binding = service.GetSiteBinding(siteBindingId);
            return binding == null
                ? Results.NotFound(ApiResponseHelper.Error("Site binding not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(binding));
        });

        browser.MapPost("/site-bindings/upsert", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequiredFields(body, new[] { "display_name", "identity_mode", "site_class", "origin" }, out var values, out var err))
                return err!;

            var binding = new BrowserSiteBinding
            {
                SiteBindingId = body.TryGetProperty("site_binding_id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                DisplayName = values["display_name"],
                IdentityMode = values["identity_mode"],
                SiteClass = values["site_class"],
                Origin = values["origin"],
                PrincipalId = body.TryGetProperty("principal_id", out var principalProp) ? principalProp.GetString() : null,
                Status = body.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "active" : "active",
                MetadataJson = body.TryGetProperty("metadata_json", out var metadataProp) ? metadataProp.GetRawText() : "{}"
            };

            return Results.Ok(ApiResponseHelper.Success(service.UpsertSiteBinding(binding)));
        });

        browser.MapPost("/user-grants/list", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            var principalId = body.TryGetProperty("principal_id", out var principalProp) ? principalProp.GetString() : null;
            return Results.Ok(ApiResponseHelper.Success(service.ListUserGrants(principalId)));
        });

        browser.MapPost("/user-grants/get", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "user_grant_id", out var userGrantId, out var err))
                return err!;

            var grant = service.GetUserGrant(userGrantId);
            return grant == null
                ? Results.NotFound(ApiResponseHelper.Error("User grant not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(grant));
        });

        browser.MapPost("/user-grants/upsert", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequiredFields(body, new[] { "principal_id", "consent_ref" }, out var values, out var err))
                return err!;

            var grant = new BrowserUserGrant
            {
                UserGrantId = body.TryGetProperty("user_grant_id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                PrincipalId = values["principal_id"],
                SiteBindingId = body.TryGetProperty("site_binding_id", out var siteProp) ? siteProp.GetString() : null,
                Status = body.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "active" : "active",
                ConsentRef = values["consent_ref"],
                ScopesJson = body.TryGetProperty("scopes_json", out var scopesProp) ? scopesProp.GetRawText() : "{}",
                ExpiresAt = TryGetDateTime(body, "expires_at")
            };

            return Results.Ok(ApiResponseHelper.Success(service.UpsertUserGrant(grant)));
        });

        browser.MapPost("/system-bindings/list", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            return Results.Ok(ApiResponseHelper.Success(service.ListSystemBindings()));
        });

        browser.MapPost("/system-bindings/get", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "system_binding_id", out var systemBindingId, out var err))
                return err!;

            var binding = service.GetSystemBinding(systemBindingId);
            return binding == null
                ? Results.NotFound(ApiResponseHelper.Error("System binding not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(binding));
        });

        browser.MapPost("/system-bindings/upsert", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequiredFields(body, new[] { "display_name", "secret_ref" }, out var values, out var err))
                return err!;

            var binding = new BrowserSystemBinding
            {
                SystemBindingId = body.TryGetProperty("system_binding_id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                DisplayName = values["display_name"],
                SiteBindingId = body.TryGetProperty("site_binding_id", out var siteProp) ? siteProp.GetString() : null,
                Status = body.TryGetProperty("status", out var statusProp) ? statusProp.GetString() ?? "active" : "active",
                SecretRef = values["secret_ref"]
            };

            return Results.Ok(ApiResponseHelper.Success(service.UpsertSystemBinding(binding)));
        });

        browser.MapPost("/leases/list", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            var principalId = body.TryGetProperty("principal_id", out var principalProp) ? principalProp.GetString() : null;
            return Results.Ok(ApiResponseHelper.Success(service.ListSessionLeases(principalId)));
        });

        browser.MapPost("/leases/get", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "session_lease_id", out var sessionLeaseId, out var err))
                return err!;

            var lease = service.GetSessionLease(sessionLeaseId);
            return lease == null
                ? Results.NotFound(ApiResponseHelper.Error("Session lease not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(lease));
        });

        browser.MapPost("/leases/issue", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequiredFields(body, new[] { "tool_id", "principal_id", "identity_mode" }, out var values, out var err))
                return err!;

            var expiresAt = TryGetDateTime(body, "expires_at") ?? DateTime.UtcNow.AddMinutes(30);
            var lease = service.IssueSessionLease(
                values["tool_id"],
                values["principal_id"],
                values["identity_mode"],
                expiresAt,
                body.TryGetProperty("site_binding_id", out var siteProp) ? siteProp.GetString() : null);
            return Results.Ok(ApiResponseHelper.Success(lease));
        });

        browser.MapPost("/leases/revoke", (HttpContext ctx, BrowserBindingService service) =>
        {
            if (!RequireAdmin(ctx, out var denied)) return denied;
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "session_lease_id", out var sessionLeaseId, out var err))
                return err!;

            var lease = service.RevokeSessionLease(sessionLeaseId);
            return lease == null
                ? Results.NotFound(ApiResponseHelper.Error("Session lease not found.", 404))
                : Results.Ok(ApiResponseHelper.Success(lease));
        });
    }

    private static DateTime? TryGetDateTime(JsonElement body, string propertyName)
    {
        if (!body.TryGetProperty(propertyName, out var prop))
            return null;
        if (prop.ValueKind == JsonValueKind.String && DateTime.TryParse(prop.GetString(), out var parsed))
            return parsed;
        return null;
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
