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
            var body = RequestBodyHelper.GetBody(ctx);
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "task_type", out var taskType, out var err))
            {
                return err!;
            }

            var scope = body.TryGetProperty("scope_descriptor", out var scopeProp)
                ? scopeProp.GetRawText()
                : "{}";
            var assignedPrincipalId = body.TryGetProperty("assigned_principal_id", out var assignedPrincipalProp)
                ? assignedPrincipalProp.GetString()
                : null;
            var assignedRoleId = body.TryGetProperty("assigned_role_id", out var assignedRoleProp)
                ? assignedRoleProp.GetString()
                : null;
            var runtimeDescriptor = body.TryGetProperty("runtime_descriptor", out var runtimeDescriptorProp)
                ? runtimeDescriptorProp.GetRawText()
                : "{}";

            var task = broker.CreateTask(
                principalId,
                taskType,
                scope,
                assignedPrincipalId,
                assignedRoleId,
                runtimeDescriptor);

            return Results.Ok(ApiResponseHelper.Success(task));
        });

        tasks.MapPost("/query", (HttpContext ctx, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "task_id", out var taskId, out var err))
            {
                return err!;
            }

            var task = broker.GetTask(taskId);
            if (task == null)
            {
                return Results.NotFound(ApiResponseHelper.Error("Task not found.", 404));
            }

            return Results.Ok(ApiResponseHelper.Success(task));
        });

        tasks.MapPost("/cancel", (HttpContext ctx, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(ctx);
            if (!RequestBodyHelper.TryGetRequired(body, "task_id", out var taskId, out var err))
            {
                return err!;
            }

            var reason = body.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString() ?? string.Empty
                : "Cancelled by user";
            var principalId = RequestBodyHelper.GetPrincipalId(ctx);

            var success = broker.CancelTask(taskId, principalId, reason);
            if (!success)
            {
                return Results.BadRequest(ApiResponseHelper.Error("Cannot cancel task."));
            }

            return Results.Ok(ApiResponseHelper.Success<object>(null, "Task cancelled."));
        });
    }
}
