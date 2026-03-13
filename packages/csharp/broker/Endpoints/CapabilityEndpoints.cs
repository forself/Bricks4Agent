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
            var body = RequestBodyHelper.GetBody(ctx);
            var filter = body.TryGetProperty("filter", out var f) ? f.GetString() : null;

            var capabilities = catalog.ListCapabilities(filter);
            return Results.Ok(ApiResponseHelper.Success(capabilities));
        });

        var grants = group.MapGroup("/grants");

        grants.MapPost("/list", (HttpContext ctx, ICapabilityCatalog catalog) =>
        {
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);
            var taskId = RequestBodyHelper.GetTaskId(ctx);
            var sessionId = RequestBodyHelper.GetSessionId(ctx);

            // 列出該 principal 在此 task/session 的所有有效 grant
            var capabilities = catalog.ListCapabilities();
            var activeGrants = capabilities
                .Select(c => catalog.GetActiveGrant(principalId, taskId, sessionId, c.CapabilityId))
                .Where(g => g != null)
                .ToList();

            return Results.Ok(ApiResponseHelper.Success(activeGrants));
        });
    }
}
