using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/tasks/*</summary>
public static class TaskEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var tasks = group.MapGroup("/tasks");

        tasks.MapPost("/create", (HttpContext ctx, IBrokerService broker) =>
        {
            var body = GetBody(ctx);
            var principalId = ctx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "";
            var taskType = body.GetProperty("task_type").GetString() ?? "";
            var scope = body.TryGetProperty("scope_descriptor", out var s)
                ? s.GetRawText() : "{}";

            var task = broker.CreateTask(principalId, taskType, scope);
            return Results.Ok(ApiResponseHelper.Success(task));
        });

        tasks.MapPost("/query", (HttpContext ctx, IBrokerService broker) =>
        {
            var body = GetBody(ctx);
            var taskId = body.GetProperty("task_id").GetString() ?? "";

            var task = broker.GetTask(taskId);
            if (task == null)
                return Results.NotFound(ApiResponseHelper.Error("Task not found.", 404));

            return Results.Ok(ApiResponseHelper.Success(task));
        });

        tasks.MapPost("/cancel", (HttpContext ctx, IBrokerService broker) =>
        {
            var body = GetBody(ctx);
            var taskId = body.GetProperty("task_id").GetString() ?? "";
            var reason = body.TryGetProperty("reason", out var r)
                ? r.GetString() ?? "" : "Cancelled by user";
            var principalId = ctx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "";

            var success = broker.CancelTask(taskId, principalId, reason);
            if (!success)
                return Results.BadRequest(ApiResponseHelper.Error("Cannot cancel task."));

            return Results.Ok(ApiResponseHelper.Success<object>(null, "Task cancelled."));
        });
    }

    private static JsonElement GetBody(HttpContext ctx)
    {
        var json = ctx.Items[EncryptionMiddleware.DecryptedBodyKey] as string ?? "{}";
        return JsonDocument.Parse(json).RootElement;
    }
}
