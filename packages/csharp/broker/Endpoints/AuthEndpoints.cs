using System.Text.Json;
using Broker.Helpers;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// 多用戶帳密登入 API。
///
///   POST /api/v1/auth/login            { principal_id, password } → 設 cookie b4a_session
///   POST /api/v1/auth/logout           清 cookie + 撤銷 session
///   GET  /api/v1/auth/me               目前登入身份（404 if 未登入）
///   POST /api/v1/auth/change-password  { current_password, new_password }
///
/// 不在 EncryptionMiddleware / BrokerAuthMiddleware 加密白名單裡（有自己的安全模型：
/// HTTPS 或 SSH tunnel + cookie + PBKDF2），但走 trusted-internal-plain-JSON 略過 ECDH。
/// </summary>
public static class AuthEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var auth = group.MapGroup("/auth");

        auth.MapPost("/login", async (PrincipalAuthService svc, HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            string pid = "", pwd = "";
            try
            {
                var doc = JsonDocument.Parse(body).RootElement;
                pid = doc.TryGetProperty("principal_id", out var p) ? p.GetString() ?? "" : "";
                pwd = doc.TryGetProperty("password", out var pp) ? pp.GetString() ?? "" : "";
            }
            catch { /* fallthrough → 空字串會被 Login 回 missing */ }

            string? totp = null;
            try
            {
                var doc = JsonDocument.Parse(body).RootElement;
                if (doc.TryGetProperty("totp_code", out var tc) && tc.ValueKind == JsonValueKind.String)
                    totp = tc.GetString();
            }
            catch { /* OK */ }

            var r = svc.Login(ctx, pid, pwd, totp);
            if (!r.Authenticated)
            {
                // requires_2fa 標 出來、frontend 顯示 TOTP 輸入欄位
                return Results.Json(new
                {
                    success = false,
                    message = r.Message,
                    data = (object?)new { requires_totp = r.RequiresTotp },
                    errorCode = 401,
                }, statusCode: 401);
            }

            return Results.Ok(ApiResponseHelper.Success(new
            {
                principal_id = r.PrincipalId,
                role = r.Role,
                must_change_password = r.MustChangePassword,
                expires_at = r.ExpiresAt,
                message = r.Message,
            }));
        });

        // ── A4 PASS 2: 2FA endpoints (require login) ──
        auth.MapPost("/2fa/setup", (PrincipalAuthService svc, HttpContext ctx) =>
        {
            var u = svc.GetCurrentUser(ctx);
            if (u == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            try
            {
                var setup = svc.Setup2fa(u.PrincipalId);
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    secret = setup.Secret,
                    otpauth_url = setup.OtpAuthUrl,
                    backup_codes = setup.BackupCodes,
                    note = "Backup codes shown ONCE. Store them. Use /2fa/verify-enroll with first TOTP to activate.",
                }));
            }
            catch (Exception ex) { return Results.BadRequest(ApiResponseHelper.Error(ex.Message)); }
        });

        auth.MapPost("/2fa/verify-enroll", async (PrincipalAuthService svc, HttpContext ctx) =>
        {
            var u = svc.GetCurrentUser(ctx);
            if (u == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            string code = "";
            try
            {
                var d = JsonDocument.Parse(body).RootElement;
                code = d.TryGetProperty("totp_code", out var c) ? c.GetString() ?? "" : "";
            }
            catch { return Results.BadRequest(ApiResponseHelper.Error("Invalid JSON")); }

            var ok = svc.VerifyEnroll2fa(u.PrincipalId, code);
            if (!ok) return Results.BadRequest(ApiResponseHelper.Error("TOTP code incorrect"));
            return Results.Ok(ApiResponseHelper.Success(new { enabled = true }));
        });

        auth.MapPost("/2fa/disable", async (PrincipalAuthService svc, HttpContext ctx) =>
        {
            var u = svc.GetCurrentUser(ctx);
            if (u == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            string code = "";
            try
            {
                var d = JsonDocument.Parse(body).RootElement;
                code = d.TryGetProperty("proof_code", out var c) ? c.GetString() ?? "" : "";
            }
            catch { return Results.BadRequest(ApiResponseHelper.Error("Invalid JSON")); }

            var ok = svc.Disable2fa(u.PrincipalId, code);
            if (!ok) return Results.BadRequest(ApiResponseHelper.Error("Proof code incorrect (need current TOTP or unused backup code)"));
            return Results.Ok(ApiResponseHelper.Success(new { disabled = true }));
        });

        auth.MapPost("/logout", (PrincipalAuthService svc, HttpContext ctx) =>
        {
            svc.Logout(ctx);
            return Results.Ok(ApiResponseHelper.Success(new { ok = true }));
        });

        auth.MapGet("/me", (PrincipalAuthService svc, HttpContext ctx) =>
        {
            var u = svc.GetCurrentUser(ctx);
            if (u == null)
                return Results.Json(ApiResponseHelper.Error("Not logged in", 401), statusCode: 401);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                principal_id = u.PrincipalId,
                role = u.Role,
                display_name = u.DisplayName,
                expires_at = u.ExpiresAt,
                two_fa_enabled = svc.Has2faEnabled(u.PrincipalId),
            }));
        });

        auth.MapPost("/change-password", async (PrincipalAuthService svc, HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            string cur = "", nw = "";
            try
            {
                var doc = JsonDocument.Parse(body).RootElement;
                cur = doc.TryGetProperty("current_password", out var c) ? c.GetString() ?? "" : "";
                nw = doc.TryGetProperty("new_password", out var n) ? n.GetString() ?? "" : "";
            }
            catch { }

            if (!svc.ChangePassword(ctx, cur, nw, out var error))
                return Results.Json(ApiResponseHelper.Error(error, 400), statusCode: 400);
            return Results.Ok(ApiResponseHelper.Success(new { ok = true }));
        });
    }
}
