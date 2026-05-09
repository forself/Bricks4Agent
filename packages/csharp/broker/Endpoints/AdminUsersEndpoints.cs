using System.Text.Json;
using Broker.Helpers;
using Broker.Middleware;
using Broker.Services;

namespace Broker.Endpoints;

/// <summary>
/// Admin 用戶管理 API（A3）。所有 endpoint 都檢查 caller 是 admin role、否則 403。
///
///   GET    /api/v1/admin/users                     列所有用戶
///   POST   /api/v1/admin/users                     新建 { principal_id, password, role, display_name? }
///   POST   /api/v1/admin/users/{id}/reset-password { new_password }
///   POST   /api/v1/admin/users/{id}/disable
///   POST   /api/v1/admin/users/{id}/enable
///   POST   /api/v1/admin/users/{id}/role           { role }
///   DELETE /api/v1/admin/users/{id}                徹底刪（不能刪 prn_dashboard / 自己）
///
/// 跟 AdminEndpoints（kill-switch / revoke）有意分開——那邊是緊急殺權、這邊是日常 user CRUD。
/// </summary>
public static class AdminUsersEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/users");

        static IResult? RequireAdmin(HttpContext ctx, out string callerPid)
        {
            var (p, role) = ctx.GetCurrentUser();
            callerPid = p ?? "";
            if (p == null) return Results.Json(ApiResponseHelper.Error("Login required", 401), statusCode: 401);
            if (!string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase))
                return Results.Json(ApiResponseHelper.Error("Admin only", 403), statusCode: 403);
            return null;
        }

        admin.MapGet("/", (PrincipalAuthService svc, HttpContext ctx) =>
        {
            var deny = RequireAdmin(ctx, out _); if (deny != null) return deny;
            var users = svc.ListUsers();
            return Results.Ok(ApiResponseHelper.Success(new
            {
                count = users.Count,
                users = users.Select(u => new
                {
                    principal_id = u.PrincipalId,
                    role = u.Role,
                    display_name = u.DisplayName,
                    must_change_password = u.MustChangePassword,
                    disabled = u.Disabled,
                    created_at = u.CreatedAt,
                    last_login_at = u.LastLoginAt,
                    last_password_change_at = u.LastPasswordChangeAt,
                }),
            }));
        });

        admin.MapPost("/", async (PrincipalAuthService svc, HttpContext ctx) =>
        {
            var deny = RequireAdmin(ctx, out _); if (deny != null) return deny;
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            string pid = "", pwd = "", role = "user", displayName = "";
            try
            {
                var d = JsonDocument.Parse(body).RootElement;
                pid = d.TryGetProperty("principal_id", out var p) ? p.GetString() ?? "" : "";
                pwd = d.TryGetProperty("password", out var pw) ? pw.GetString() ?? "" : "";
                role = d.TryGetProperty("role", out var r) ? r.GetString() ?? "user" : "user";
                displayName = d.TryGetProperty("display_name", out var n) ? n.GetString() ?? "" : "";
            }
            catch { return Results.BadRequest(ApiResponseHelper.Error("Invalid JSON")); }

            try
            {
                var u = svc.CreateUser(pid, pwd, role, displayName);
                return Results.Ok(ApiResponseHelper.Success(new
                {
                    principal_id = u.PrincipalId, role = u.Role, display_name = u.DisplayName,
                }));
            }
            catch (Exception ex) { return Results.BadRequest(ApiResponseHelper.Error(ex.Message)); }
        });

        admin.MapPost("/{id}/reset-password", async (PrincipalAuthService svc, HttpContext ctx, string id) =>
        {
            var deny = RequireAdmin(ctx, out _); if (deny != null) return deny;
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            string nw = "";
            try
            {
                var d = JsonDocument.Parse(body).RootElement;
                nw = d.TryGetProperty("new_password", out var n) ? n.GetString() ?? "" : "";
            }
            catch { return Results.BadRequest(ApiResponseHelper.Error("Invalid JSON")); }

            try
            {
                var ok = svc.AdminResetPassword(id, nw);
                if (!ok) return Results.NotFound(ApiResponseHelper.Error("User not found"));
                return Results.Ok(ApiResponseHelper.Success(new { ok, must_change_password = true, sessions_revoked = true }));
            }
            catch (Exception ex) { return Results.BadRequest(ApiResponseHelper.Error(ex.Message)); }
        });

        admin.MapPost("/{id}/disable", (PrincipalAuthService svc, HttpContext ctx, string id) =>
        {
            var deny = RequireAdmin(ctx, out var caller); if (deny != null) return deny;
            if (id == caller) return Results.BadRequest(ApiResponseHelper.Error("Cannot disable yourself"));
            var ok = svc.SetUserDisabled(id, true);
            if (!ok) return Results.NotFound(ApiResponseHelper.Error("User not found"));
            return Results.Ok(ApiResponseHelper.Success(new { ok, disabled = true }));
        });

        admin.MapPost("/{id}/enable", (PrincipalAuthService svc, HttpContext ctx, string id) =>
        {
            var deny = RequireAdmin(ctx, out _); if (deny != null) return deny;
            var ok = svc.SetUserDisabled(id, false);
            if (!ok) return Results.NotFound(ApiResponseHelper.Error("User not found"));
            return Results.Ok(ApiResponseHelper.Success(new { ok, disabled = false }));
        });

        admin.MapPost("/{id}/role", async (PrincipalAuthService svc, HttpContext ctx, string id) =>
        {
            var deny = RequireAdmin(ctx, out var caller); if (deny != null) return deny;
            if (id == caller) return Results.BadRequest(ApiResponseHelper.Error("Cannot change your own role"));
            using var reader = new StreamReader(ctx.Request.Body);
            var body = await reader.ReadToEndAsync();
            string role = "";
            try
            {
                var d = JsonDocument.Parse(body).RootElement;
                role = d.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
            }
            catch { return Results.BadRequest(ApiResponseHelper.Error("Invalid JSON")); }

            try
            {
                var ok = svc.SetUserRole(id, role);
                if (!ok) return Results.NotFound(ApiResponseHelper.Error("User not found"));
                return Results.Ok(ApiResponseHelper.Success(new { ok, role, sessions_revoked = true }));
            }
            catch (Exception ex) { return Results.BadRequest(ApiResponseHelper.Error(ex.Message)); }
        });

        admin.MapDelete("/{id}", (PrincipalAuthService svc, HttpContext ctx, string id) =>
        {
            var deny = RequireAdmin(ctx, out var caller); if (deny != null) return deny;
            if (id == caller) return Results.BadRequest(ApiResponseHelper.Error("Cannot delete yourself"));
            try
            {
                var ok = svc.DeleteUser(id);
                if (!ok) return Results.NotFound(ApiResponseHelper.Error("User not found"));
                return Results.Ok(ApiResponseHelper.Success(new { removed = true }));
            }
            catch (Exception ex) { return Results.BadRequest(ApiResponseHelper.Error(ex.Message)); }
        });
    }
}
