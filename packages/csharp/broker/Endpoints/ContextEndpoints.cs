using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Endpoints;

/// <summary>
/// POST /api/v1/context/* — SharedContext CRUD（plan-related only）
///
/// 範圍規則（在 service 層的讀取 ACL 之外）：
/// - 文件屬於某任務時，只有該任務的擁有者（token 綁定的任務，或任務的提交者）或管理員能讀寫；
///   系統範圍 <c>global</c> 沒有對應的任務，只限管理員。
/// - 文件不屬於任何任務時（舊資料），只限原作者或管理員。
/// - 新文件一律落在某個任務範圍內；系統保留的 document_id 前綴只限管理員寫入。
/// - 讀取被拒與文件不存在回應相同（404），不透露文件是否存在。
/// </summary>
public static class ContextEndpoints
{
    private const string NotFoundMessage = "Context entry not found or access denied.";

    public static void Map(RouteGroupBuilder group)
    {
        var ctx = group.MapGroup("/context");

        // ── 寫入 context（新建或新版本） ──
        ctx.MapPost("/write", (HttpContext httpCtx, ISharedContextService contextService, IBrokerService broker, BrokerDb db) =>
        {
            var body = RequestBodyHelper.GetBody(httpCtx);
            var principalId = RequestBodyHelper.GetPrincipalId(httpCtx);

            // M-1 修復：驗證必填欄位
            if (!RequestBodyHelper.TryGetRequiredFields(body,
                new[] { "document_id", "key", "content_ref" }, out var fields, out var err))
                return err!;
            var documentId = fields["document_id"];
            var key = fields["key"];
            var contentRef = fields["content_ref"];
            var contentType = body.TryGetProperty("content_type", out var ct)
                ? ct.GetString() ?? "application/json" : "application/json";
            var acl = body.TryGetProperty("acl", out var a)
                ? a.GetRawText() : "{}";
            if (!TryGetOptionalTaskId(body, out var requestedTaskId, out var taskIdError))
                return taskIdError;

            // acl 必須是物件；有 read 時必須是字串陣列（服務層寫入時也會再檢查一次）
            if (!SharedContextService.IsWellFormedAcl(acl))
                return Results.BadRequest(ApiResponseHelper.Error(
                    "acl must be a JSON object; when it has a 'read' entry, that entry must be an array of strings."));

            // 系統元件維護的文件只限管理員經由這個端點寫入
            if (SystemContextDocuments.IsReservedDocumentId(documentId) && !BrokerAuthorization.IsAdmin(httpCtx))
                return Forbidden("Forbidden: document write access denied.");

            var latest = FindLatest(db, documentId);

            // 既有文件只能由能存取其範圍者追加新版本
            if (latest != null && !HasEntryScope(httpCtx, broker, latest, principalId))
                return Forbidden("Forbidden: document write access denied.");

            // 寫入範圍：未指定 task_id 時，既有文件沿用其任務，新文件（或沒有任務的舊文件）用 token 綁定的任務；
            // 指定或推得的任務都必須是呼叫者可存取的任務。
            var taskId = requestedTaskId;
            if (string.IsNullOrWhiteSpace(taskId))
                taskId = !string.IsNullOrWhiteSpace(latest?.TaskId) ? latest.TaskId : RequestBodyHelper.GetTaskId(httpCtx);
            if (string.IsNullOrWhiteSpace(taskId))
                return Forbidden("Forbidden: a context write needs a task scope.");

            if (!TryRequireTaskScope(httpCtx, broker, taskId, out var taskDenied))
                return taskDenied;

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
        ctx.MapPost("/read", (HttpContext httpCtx, ISharedContextService contextService, IBrokerService broker, BrokerDb db) =>
        {
            var body = RequestBodyHelper.GetBody(httpCtx);
            var principalId = RequestBodyHelper.GetPrincipalId(httpCtx);
            if (!RequestBodyHelper.TryGetRequired(body, "document_id", out var documentId, out var err))
                return err!;

            // 先確認範圍，再交給 service 做 ACL 檢查與稽核；回傳前再確認一次實際讀到的版本
            var latest = FindLatest(db, documentId);
            if (latest == null || !HasEntryScope(httpCtx, broker, latest, principalId))
                return NotFound();

            var entry = contextService.ReadLatest(documentId, principalId);
            if (entry == null || !HasEntryScope(httpCtx, broker, entry, principalId))
                return NotFound();

            return Results.Ok(ApiResponseHelper.Success(entry));
        });

        // ── 按 key + taskId 讀取（node output 查詢） ──
        ctx.MapPost("/read-by-key", (HttpContext httpCtx, ISharedContextService contextService, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(httpCtx);
            var principalId = RequestBodyHelper.GetPrincipalId(httpCtx);
            if (!RequestBodyHelper.TryGetRequired(body, "key", out var key, out var err))
                return err!;
            if (!TryGetOptionalTaskId(body, out var taskId, out var taskIdError))
                return taskIdError;

            if (!string.IsNullOrWhiteSpace(taskId))
            {
                // 明確指定的任務必須是呼叫者可存取的任務
                if (!TryRequireTaskScope(httpCtx, broker, taskId, out var denied))
                    return denied;
            }
            else
            {
                // 未指定 task_id 時以呼叫者 token 的任務為範圍，不做跨任務的全域查詢
                taskId = RequestBodyHelper.GetTaskId(httpCtx);
                if (string.IsNullOrWhiteSpace(taskId))
                    return NotFound();
            }

            var entry = contextService.ReadByKey(key, taskId, principalId);
            if (entry == null)
                return NotFound();

            return Results.Ok(ApiResponseHelper.Success(entry));
        });

        // ── 列出 task 下所有 context entries ──
        ctx.MapPost("/list", (HttpContext httpCtx, ISharedContextService contextService, IBrokerService broker) =>
        {
            var body = RequestBodyHelper.GetBody(httpCtx);
            var principalId = RequestBodyHelper.GetPrincipalId(httpCtx);
            if (!RequestBodyHelper.TryGetRequired(body, "task_id", out var taskId, out var err))
                return err!;

            // 列出某任務的 context 屬任務範圍的讀取，只限該任務的擁有者（或管理員）
            if (!TryRequireTaskScope(httpCtx, broker, taskId, out var denied))
                return denied;

            var entries = contextService.ListByTask(taskId, principalId);
            return Results.Ok(ApiResponseHelper.Success(entries));
        });

        // ── 列出版本歷史 ──
        ctx.MapPost("/history", (HttpContext httpCtx, ISharedContextService contextService, IBrokerService broker, BrokerDb db) =>
        {
            var body = RequestBodyHelper.GetBody(httpCtx);
            var principalId = RequestBodyHelper.GetPrincipalId(httpCtx);
            if (!RequestBodyHelper.TryGetRequired(body, "document_id", out var documentId, out var err))
                return err!;

            var latest = FindLatest(db, documentId);
            if (latest == null || !HasEntryScope(httpCtx, broker, latest, principalId))
                return NotFound();

            // service 層逐版套用 ACL；這裡再逐版確認範圍（同一文件的版本可能屬於不同任務）
            var entries = contextService.ListVersions(documentId, principalId)
                .Where(entry => HasEntryScope(httpCtx, broker, entry, principalId))
                .ToList();
            if (entries.Count == 0)
                return NotFound();

            return Results.Ok(ApiResponseHelper.Success(entries));
        });
    }

    private static SharedContextEntry? FindLatest(BrokerDb db, string documentId)
        => db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

    /// <summary>
    /// 呼叫者能否存取這個版本所屬的範圍：屬於某任務時需有該任務的存取權（<c>global</c> 只限管理員）；
    /// 不屬於任何任務時限原作者或管理員。
    /// </summary>
    private static bool HasEntryScope(HttpContext ctx, IBrokerService broker, SharedContextEntry entry, string principalId)
    {
        if (BrokerAuthorization.IsAdmin(ctx))
            return true;

        if (!string.IsNullOrWhiteSpace(entry.TaskId))
            return HasTaskScope(ctx, broker, entry.TaskId);

        return !string.IsNullOrEmpty(principalId) &&
               string.Equals(entry.AuthorPrincipalId, principalId, StringComparison.Ordinal);
    }

    private static bool HasTaskScope(HttpContext ctx, IBrokerService broker, string taskId)
    {
        if (BrokerAuthorization.IsAdmin(ctx))
            return true;

        if (string.Equals(taskId, SystemContextDocuments.GlobalTaskId, StringComparison.OrdinalIgnoreCase))
            return false;

        return BrokerAuthorization.HasTaskAccess(ctx, broker.GetTask(taskId), taskId);
    }

    /// <summary>任務範圍檢查（<c>global</c> 只限管理員），不通過回 403。</summary>
    private static bool TryRequireTaskScope(HttpContext ctx, IBrokerService broker, string taskId, out IResult denied)
    {
        if (HasTaskScope(ctx, broker, taskId))
        {
            denied = null!;
            return true;
        }

        denied = Forbidden("Forbidden: task access denied.");
        return false;
    }

    /// <summary>讀取選填的 task_id：未提供或為 null 時回 null；不是字串時回 400。</summary>
    private static bool TryGetOptionalTaskId(JsonElement body, out string? taskId, out IResult error)
    {
        taskId = null;
        error = null!;
        if (!body.TryGetProperty("task_id", out var value) || value.ValueKind == JsonValueKind.Null)
            return true;

        if (value.ValueKind != JsonValueKind.String)
        {
            error = Results.BadRequest(ApiResponseHelper.Error("task_id must be a string."));
            return false;
        }

        taskId = value.GetString();
        return true;
    }

    private static IResult NotFound()
        => Results.NotFound(ApiResponseHelper.Error(NotFoundMessage, StatusCodes.Status404NotFound));

    private static IResult Forbidden(string message)
        => Results.Json(
            ApiResponseHelper.Error(message, StatusCodes.Status403Forbidden),
            statusCode: StatusCodes.Status403Forbidden);
}
