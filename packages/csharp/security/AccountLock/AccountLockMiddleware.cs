using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Bricks4Agent.Security.AccountLock.Models;

namespace Bricks4Agent.Security.AccountLock
{
    /// <summary>
    /// Middleware to check account lock status on each request
    /// </summary>
    public class AccountLockMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly AccountLockMiddlewareOptions _options;

        public AccountLockMiddleware(RequestDelegate next, AccountLockMiddlewareOptions options = null)
        {
            _next = next;
            _options = options ?? new AccountLockMiddlewareOptions();
        }

        public async Task InvokeAsync(HttpContext context, IAccountLockService lockService)
        {
            // Skip check for excluded paths
            if (IsExcludedPath(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // Get client IP
            var clientIp = GetClientIp(context);

            // Check IP lock first
            var ipCheck = lockService.CheckIpLock(clientIp);
            if (ipCheck.IsLocked)
            {
                await WriteLockedResponse(context, ipCheck);
                return;
            }

            // Check account lock if user is authenticated
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var userId = GetUserId(context.User);
                if (userId.HasValue)
                {
                    var accountCheck = lockService.CheckLock(userId.Value);
                    if (accountCheck.IsLocked)
                    {
                        await WriteLockedResponse(context, accountCheck);
                        return;
                    }
                }
            }

            await _next(context);
        }

        private bool IsExcludedPath(PathString path)
        {
            foreach (var excluded in _options.ExcludedPaths)
            {
                if (path.StartsWithSegments(excluded, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string GetClientIp(HttpContext context)
        {
            return context.Request.Headers["CF-Connecting-IP"].ToString()
                ?? context.Request.Headers["X-Real-IP"].ToString()
                ?? context.Request.Headers["X-Forwarded-For"].ToString()?.Split(',')[0]?.Trim()
                ?? context.Connection.RemoteIpAddress?.ToString();
        }

        private static int? GetUserId(ClaimsPrincipal user)
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? user.FindFirst("sub")?.Value;

            if (int.TryParse(userIdClaim, out int userId))
                return userId;
            return null;
        }

        private static async Task WriteLockedResponse(HttpContext context, LockCheckResult lockResult)
        {
            context.Response.StatusCode = StatusCodes.Status423Locked;
            context.Response.ContentType = "application/json";

            if (lockResult.RetryAfterSeconds.HasValue)
            {
                context.Response.Headers["Retry-After"] = lockResult.RetryAfterSeconds.Value.ToString();
            }

            var response = new
            {
                error = "account_locked",
                message = lockResult.Message,
                lockType = lockResult.LockType?.ToString(),
                expiresAt = lockResult.ExpiresAt,
                retryAfterSeconds = lockResult.RetryAfterSeconds
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }

    /// <summary>
    /// Middleware options
    /// </summary>
    public class AccountLockMiddlewareOptions
    {
        /// <summary>
        /// Paths to exclude from lock checking
        /// </summary>
        public string[] ExcludedPaths { get; set; } = new[]
        {
            "/api/auth/login",
            "/api/auth/register",
            "/api/health",
            "/swagger"
        };
    }

    /// <summary>
    /// Extension methods for middleware registration
    /// </summary>
    public static class AccountLockMiddlewareExtensions
    {
        /// <summary>
        /// Add account lock checking middleware
        /// </summary>
        public static IApplicationBuilder UseAccountLockCheck(
            this IApplicationBuilder app,
            AccountLockMiddlewareOptions options = null)
        {
            return app.UseMiddleware<AccountLockMiddleware>(options ?? new AccountLockMiddlewareOptions());
        }
    }

    /// <summary>
    /// Helper class for integrating account lock with authentication
    /// </summary>
    public class AuthLockIntegration
    {
        private readonly IAccountLockService _lockService;

        public AuthLockIntegration(IAccountLockService lockService)
        {
            _lockService = lockService;
        }

        /// <summary>
        /// Check if login is allowed for user/IP combination
        /// Call this before validating credentials
        /// </summary>
        public LockCheckResult CheckLoginAllowed(int? userId, string ipAddress)
        {
            // Check IP lock
            var ipCheck = _lockService.CheckIpLock(ipAddress);
            if (ipCheck.IsLocked)
                return ipCheck;

            // Check account lock if user ID known
            if (userId.HasValue)
            {
                var accountCheck = _lockService.CheckLock(userId.Value, LockScope.Login);
                if (accountCheck.IsLocked)
                    return accountCheck;
            }

            return LockCheckResult.NotLocked();
        }

        /// <summary>
        /// Handle failed login attempt
        /// Call this after failed password verification
        /// </summary>
        /// <returns>Lock check result (may indicate new lock was created)</returns>
        public LockCheckResult HandleFailedLogin(int? userId, string username, string ipAddress)
        {
            return _lockService.RecordFailedLogin(userId, username, ipAddress);
        }

        /// <summary>
        /// Handle successful login
        /// Call this after successful authentication
        /// </summary>
        public void HandleSuccessfulLogin(int userId, string ipAddress)
        {
            _lockService.ResetFailedAttempts(userId, ipAddress);
        }

        /// <summary>
        /// Check if MFA verification is allowed
        /// </summary>
        public LockCheckResult CheckMfaAllowed(int userId, string ipAddress)
        {
            var ipCheck = _lockService.CheckIpLock(ipAddress);
            if (ipCheck.IsLocked)
                return ipCheck;

            return _lockService.CheckLock(userId, LockScope.Mfa);
        }

        /// <summary>
        /// Handle failed MFA attempt
        /// </summary>
        public LockCheckResult HandleFailedMfa(int userId, string username, string ipAddress)
        {
            return _lockService.RecordFailedMfa(userId, username, ipAddress);
        }

        /// <summary>
        /// Handle successful MFA verification
        /// </summary>
        public void HandleSuccessfulMfa(int userId, string ipAddress)
        {
            _lockService.ResetFailedAttempts(userId, ipAddress);
        }
    }
}
