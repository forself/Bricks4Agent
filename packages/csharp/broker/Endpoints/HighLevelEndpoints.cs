using Broker.Helpers;
using Broker.Services;
using System.Text.Json;

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
                follow_up_messages = result.FollowUpMessages,
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

        line.MapGet("/users", (HighLevelCoordinator coordinator) =>
        {
            var users = coordinator.ListLineUsers();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                total = users.Count,
                users
            }));
        });

        line.MapPost("/users/permissions", (HttpContext ctx, HighLevelCoordinator coordinator) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "user_id", out var userId, out var err))
            {
                return err!;
            }

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

            if (updated == null)
            {
                return Results.NotFound(ApiResponseHelper.Error("Profile not found.", 404));
            }

            return Results.Ok(ApiResponseHelper.Success(updated));
        });

        line.MapGet("/registration-policy", (HighLevelCoordinator coordinator) =>
            Results.Ok(ApiResponseHelper.Success(new
            {
                policy = coordinator.GetLineAnonymousRegistrationPolicy()
            })));

        line.MapPost("/registration-policy", (HttpContext ctx, HighLevelCoordinator coordinator) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "policy", out var policy, out var err))
            {
                return err!;
            }

            var updated = coordinator.SetLineAnonymousRegistrationPolicy(policy);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                policy = updated
            }));
        });

        line.MapPost("/users/registration/review", (HttpContext ctx, HighLevelCoordinator coordinator) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequiredFields(body, new[] { "user_id", "action" }, out var values, out var err))
            {
                return err!;
            }

            var note = body.TryGetProperty("note", out var noteProp) && noteProp.ValueKind == JsonValueKind.String
                ? noteProp.GetString()
                : null;
            var reviewed = coordinator.ReviewLineUserRegistration(values["user_id"], values["action"], note);
            if (reviewed == null)
            {
                return Results.NotFound(ApiResponseHelper.Error("Profile not found.", 404));
            }

            return Results.Ok(ApiResponseHelper.Success(reviewed));
        });

        line.MapGet("/notifications/pending", (HighLevelCoordinator coordinator, int limit = 20) =>
            Results.Ok(ApiResponseHelper.Success(new
            {
                total = coordinator.ListPendingLineNotifications(limit).Count,
                notifications = coordinator.ListPendingLineNotifications(limit)
            })));

        line.MapPost("/notifications/complete", (HttpContext ctx, HighLevelCoordinator coordinator) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequiredFields(body, new[] { "notification_id", "status" }, out var values, out var err))
            {
                return err!;
            }

            var error = body.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String
                ? errorProp.GetString()
                : null;
            var completed = coordinator.CompleteLineNotification(values["notification_id"], values["status"], error);
            if (completed == null)
            {
                return Results.NotFound(ApiResponseHelper.Error("Notification not found.", 404));
            }

            return Results.Ok(ApiResponseHelper.Success(completed));
        });
    }
}
