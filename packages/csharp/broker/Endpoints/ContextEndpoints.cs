using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>POST /api/v1/context/* — SharedContext CRUD（plan-related only）</summary>
public static class ContextEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var ctx = group.MapGroup("/context");

        // ── 寫入 context（新建或新版本） ──
        ctx.MapPost("/write", (HttpContext httpCtx, ISharedContextService contextService) =>
        {
            var body = GetBody(httpCtx);
            var principalId = httpCtx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "";

            var documentId = body.GetProperty("document_id").GetString() ?? "";
            var key = body.GetProperty("key").GetString() ?? "";
            var contentRef = body.GetProperty("content_ref").GetString() ?? "";
            var contentType = body.TryGetProperty("content_type", out var ct)
                ? ct.GetString() ?? "application/json" : "application/json";
            var acl = body.TryGetProperty("acl", out var a)
                ? a.GetRawText() : "{}";
            var taskId = body.TryGetProperty("task_id", out var t)
                ? t.GetString() : null;

            try
            {
                var entry = contextService.Write(principalId, documentId, key, contentRef, contentType, acl, taskId);
                return Results.Ok(ApiResponseHelper.Success(entry));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        // ── 讀取最新版本 ──
        ctx.MapPost("/read", (HttpContext httpCtx, ISharedContextService contextService) =>
        {
            var body = GetBody(httpCtx);
            var principalId = httpCtx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "";
            var documentId = body.GetProperty("document_id").GetString() ?? "";

            var entry = contextService.ReadLatest(documentId, principalId);
            if (entry == null)
                return Results.NotFound(ApiResponseHelper.Error("Context entry not found or access denied.", 404));

            return Results.Ok(ApiResponseHelper.Success(entry));
        });

        // ── 按 key + taskId 讀取（node output 查詢） ──
        ctx.MapPost("/read-by-key", (HttpContext httpCtx, ISharedContextService contextService) =>
        {
            var body = GetBody(httpCtx);
            var principalId = httpCtx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "";
            var key = body.GetProperty("key").GetString() ?? "";
            var taskId = body.TryGetProperty("task_id", out var t)
                ? t.GetString() : null;

            var entry = contextService.ReadByKey(key, taskId, principalId);
            if (entry == null)
                return Results.NotFound(ApiResponseHelper.Error("Context entry not found or access denied.", 404));

            return Results.Ok(ApiResponseHelper.Success(entry));
        });

        // ── 列出 task 下所有 context entries ──
        ctx.MapPost("/list", (HttpContext httpCtx, ISharedContextService contextService) =>
        {
            var body = GetBody(httpCtx);
            var principalId = httpCtx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "";
            var taskId = body.GetProperty("task_id").GetString() ?? "";

            var entries = contextService.ListByTask(taskId, principalId);
            return Results.Ok(ApiResponseHelper.Success(entries));
        });

        // ── 列出版本歷史 ──
        ctx.MapPost("/history", (HttpContext httpCtx, ISharedContextService contextService) =>
        {
            var body = GetBody(httpCtx);
            var principalId = httpCtx.Items[BrokerAuthMiddleware.PrincipalIdKey] as string ?? "";
            var documentId = body.GetProperty("document_id").GetString() ?? "";

            var entries = contextService.ListVersions(documentId, principalId);
            return Results.Ok(ApiResponseHelper.Success(entries));
        });
    }

    private static JsonElement GetBody(HttpContext ctx)
    {
        var json = ctx.Items[EncryptionMiddleware.DecryptedBodyKey] as string ?? "{}";
        return JsonDocument.Parse(json).RootElement;
    }
}
