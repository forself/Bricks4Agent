using Broker.Helpers;
using Broker.Services;

namespace Broker.Endpoints;

public static class ToolSpecEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var tools = group.MapGroup("/tool-specs");

        tools.MapPost("/list", (HttpContext ctx, IToolSpecRegistry registry) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            var filter = body.TryGetProperty("filter", out var f) ? f.GetString() : null;
            var specs = registry.List(filter);
            return Results.Ok(ApiResponseHelper.Success(specs));
        });

        tools.MapPost("/get", (HttpContext ctx, IToolSpecRegistry registry) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "tool_id", out var toolId, out var err))
            {
                return err!;
            }

            var spec = registry.Get(toolId);
            if (spec == null)
            {
                return Results.NotFound(ApiResponseHelper.Error("Tool spec not found.", 404));
            }

            return Results.Ok(ApiResponseHelper.Success(spec));
        });
    }
}
