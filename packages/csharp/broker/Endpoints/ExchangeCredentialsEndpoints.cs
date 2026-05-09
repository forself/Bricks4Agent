using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// 用戶 exchange 憑證管理 API。要登入；admin 看全部、user 看自己。
///
///   GET    /api/v1/exchange-credentials                列我的（admin: 全部）
///   POST   /api/v1/exchange-credentials                新增 { exchange, api_key, api_secret, label?, is_demo? }
///   POST   /api/v1/exchange-credentials/{id}/disable    暫停（保留）
///   POST   /api/v1/exchange-credentials/{id}/enable     恢復
///   DELETE /api/v1/exchange-credentials/{id}            徹底刪
///   POST   /api/v1/exchange-credentials/{id}/test       測試連線（A2.5b 接 trading-worker、暫先回 placeholder）
///
/// 沒接 trading-worker 之前、create + delete 走得通、但 BingX 那邊還是用 env 的 key 跑。
/// A2.5b 接好後、AutoTraderService 會優先看用戶自己的 credential。
/// </summary>
public static class ExchangeCredentialsEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var ec = group.MapGroup("/exchange-credentials");

        ec.MapGet("/", (ExchangeCredentialService svc, HttpContext ctx) =>
        {
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            var isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
            var rows = svc.ListForViewer(pid, isAdmin);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                count = rows.Count,
                viewer = new { principal_id = pid, role, scope = isAdmin ? "all" : "self" },
                credentials = rows.Select(r => new
                {
                    entry_id = r.EntryId,
                    owner_principal_id = r.OwnerPrincipalId,
                    exchange = r.Exchange,
                    label = r.Label,
                    is_demo = r.IsDemo,
                    disabled = r.Disabled,
                    api_key_masked = r.ApiKeyMasked,
                    created_at = r.CreatedAt,
                    updated_at = r.UpdatedAt,
                    last_used_at = r.LastUsedAt,
                }),
            }));
        });

        ec.MapPost("/", async (ExchangeCredentialService svc, HttpContext ctx) =>
        {
            var (pid, _) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);

            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            string exchange = "", apiKey = "", apiSecret = "", label = "";
            bool isDemo = false;
            try
            {
                var doc = JsonDocument.Parse(body).RootElement;
                exchange = doc.TryGetProperty("exchange", out var e) ? e.GetString() ?? "" : "";
                apiKey = doc.TryGetProperty("api_key", out var k) ? k.GetString() ?? "" : "";
                apiSecret = doc.TryGetProperty("api_secret", out var s) ? s.GetString() ?? "" : "";
                label = doc.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
                isDemo = doc.TryGetProperty("is_demo", out var d) && d.GetBoolean();
            }
            catch { return Results.BadRequest(ApiResponseHelper.Error("Invalid JSON")); }

            try
            {
                var view = svc.Create(pid, exchange, apiKey, apiSecret, label, isDemo);
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    entry_id = view!.EntryId,
                    exchange = view.Exchange,
                    is_demo = view.IsDemo,
                    label = view.Label,
                }));
            }
            catch (Exception ex)
            {
                return Results.BadRequest(ApiResponseHelper.Error(ex.Message));
            }
        });

        ec.MapDelete("/{entryId}", (ExchangeCredentialService svc, HttpContext ctx, string entryId) =>
        {
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            try
            {
                var ok = svc.Delete(entryId, pid, string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase));
                return Results.Ok(ApiResponseHelper.Success(new { removed = ok }));
            }
            catch (UnauthorizedAccessException) { return Results.Json(ApiResponseHelper.Error("Not your credential", 403), statusCode: 403); }
        });

        ec.MapPost("/{entryId}/disable", (ExchangeCredentialService svc, HttpContext ctx, string entryId) =>
        {
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            try
            {
                var ok = svc.SetDisabled(entryId, true, pid, string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase));
                return Results.Ok(ApiResponseHelper.Success(new { disabled = true, ok }));
            }
            catch (UnauthorizedAccessException) { return Results.Json(ApiResponseHelper.Error("Not your credential", 403), statusCode: 403); }
        });

        ec.MapPost("/{entryId}/enable", (ExchangeCredentialService svc, HttpContext ctx, string entryId) =>
        {
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            try
            {
                var ok = svc.SetDisabled(entryId, false, pid, string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase));
                return Results.Ok(ApiResponseHelper.Success(new { disabled = false, ok }));
            }
            catch (UnauthorizedAccessException) { return Results.Json(ApiResponseHelper.Error("Not your credential", 403), statusCode: 403); }
        });

        ec.MapPost("/{entryId}/test", (ExchangeCredentialService svc, HttpContext ctx, string entryId) =>
        {
            // A2.5b 會接 trading-worker 真做 get_account 驗證；現在僅做「能解密」檢查當 placeholder
            var (pid, role) = ctx.GetCurrentUser();
            if (pid == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            try
            {
                var existing = svc.ListForViewer(pid, string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault(r => r.EntryId == entryId);
                if (existing == null)
                    return Results.NotFound(ApiResponseHelper.Error("Credential not found"));
                var dec = svc.Resolve(existing.OwnerPrincipalId, existing.Exchange, existing.IsDemo);
                if (dec == null)
                    return Results.Ok(ApiResponseHelper.Error("Decryption failed — master key may have changed"));
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    decrypt_ok = true,
                    api_key_preview = dec.ApiKey.Length > 10
                        ? dec.ApiKey[..6] + "..." + dec.ApiKey[^4..]
                        : "(short)",
                    note = "Real exchange API check (get_account) wires up in A2.5b.",
                }));
            }
            catch (Exception ex) { return Results.Ok(ApiResponseHelper.Error(ex.Message)); }
        });
    }
}
