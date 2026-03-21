using Broker.Helpers;
using Broker.Services;

namespace Broker.Endpoints;

public static class HighLevelEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var highLevel = group.MapGroup("/high-level");
        var line = highLevel.MapGroup("/line");

        line.MapPost("/process", async (HttpContext ctx, HighLevelCoordinator coordinator, CancellationToken cancellationToken) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequiredFields(body, new[] { "user_id", "message" }, out var values, out var err))
            {
                return err!;
            }

            var result = await coordinator.ProcessLineMessageAsync(
                values["user_id"],
                values["message"],
                cancellationToken);

            return Results.Ok(ApiResponseHelper.Success(new
            {
                mode = result.Mode.ToString().ToLowerInvariant(),
                reply = result.Reply,
                error = result.Error,
                decision_reason = result.DecisionReason,
                history_count = result.HistoryCount,
                draft_cleared = result.DraftCleared,
                draft = result.Draft,
                created_task = result.CreatedTask,
                created_plan = result.CreatedPlan,
                handoff = result.Handoff,
                rag_snippets = result.RagSnippets
            }));
        });

        line.MapPost("/profile", (HttpContext ctx, HighLevelCoordinator coordinator) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "user_id", out var userId, out var err))
            {
                return err!;
            }

            var profile = coordinator.GetLineUserProfile(userId);
            if (profile == null)
            {
                return Results.NotFound(ApiResponseHelper.Error("Profile not found.", 404));
            }

            return Results.Ok(ApiResponseHelper.Success(profile));
        });

        line.MapPost("/draft", (HttpContext ctx, HighLevelCoordinator coordinator) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "user_id", out var userId, out var err))
            {
                return err!;
            }

            var draft = coordinator.GetLineDraft(userId);
            if (draft == null)
            {
                return Results.NotFound(ApiResponseHelper.Error("Draft not found.", 404));
            }

            return Results.Ok(ApiResponseHelper.Success(draft));
        });
    }
}
