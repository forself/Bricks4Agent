using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/capabilities/* + /api/v1/grants/*</summary>
public static class CapabilityEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var caps = group.MapGroup("/capabilities");

        caps.MapPost("/list", (HttpContext ctx, ICapabilityCatalog catalog) =>
        {
            var body = GetBody(ctx);
            var filter = body.TryGetProperty("filter", out var f) ? f.GetString() : null;

            var capabilities = catalog.ListCapabilities(filter);
            return Results.Ok(ApiResponseHelper.Success(capabilities));
        });

        var grants = group.MapGroup("/grants");

        grants.MapPost("/list", (HttpContext ctx, ICapabilityCatalog catalog) =>
        {
            var principalId = ctx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "";
            var taskId = ctx.Items[BrokerAuthMiddleware.TaskIdKey] as string ?? "";
            var sessionId = ctx.Items[BrokerAuthMiddleware.SessionIdKey] as string ?? "";

            // 列出該 principal 在此 task/session 的所有有效 grant
            // Phase 1 簡化：列出所有已知能力的 grant
            var capabilities = catalog.ListCapabilities();
            var activeGrants = capabilities
                .Select(c => catalog.GetActiveGrant(principalId, taskId, sessionId, c.CapabilityId))
                .Where(g => g != null)
                .ToList();

            return Results.Ok(ApiResponseHelper.Success(activeGrants));
        });
    }

    private static JsonElement GetBody(HttpContext ctx)
    {
        var json = ctx.Items[EncryptionMiddleware.DecryptedBodyKey] as string ?? "{}";
        return JsonDocument.Parse(json).RootElement;
    }
}
