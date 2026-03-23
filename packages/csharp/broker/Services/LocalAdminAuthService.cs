using System.Net;
using System.Security.Cryptography;
using System.Text;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class LocalAdminAuthService
{
    private const string CredentialId = "local_admin";
    private const string InitialPassword = "admin";
    public const string SessionCookieName = "b4a_local_admin";

    private readonly BrokerDb _db;
    private readonly ILogger<LocalAdminAuthService> _logger;
    private readonly object _gate = new();

    public LocalAdminAuthService(BrokerDb db, ILogger<LocalAdminAuthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public LocalAdminStatus GetStatus(HttpContext context)
    {
        var localRequest = IsLocalRequest(context);
        var credential = GetCredential();
        var session = localRequest ? GetAuthenticatedSession(context) : null;

        return new LocalAdminStatus
        {
            LocalRequest = localRequest,
            Authenticated = session != null,
            HasPassword = credential != null,
            RequiresPasswordChange = credential == null || credential.MustChangePassword,
            InitialPasswordActive = credential == null,
            SessionExpiresAt = session?.ExpiresAt
        };
    }

    public LocalAdminLoginResult Login(HttpContext context, string password, string? newPassword)
    {
        if (!IsLocalRequest(context))
            throw new InvalidOperationException("Local admin login is only available from localhost.");

        lock (_gate)
        {
            var credential = GetCredential();
            if (credential == null)
            {
                if (!string.Equals(password, InitialPassword, StringComparison.Ordinal))
                    return new LocalAdminLoginResult { Authenticated = false, RequiresPasswordChange = true, Message = "Initial password is required." };

                if (string.IsNullOrWhiteSpace(newPassword))
                    return new LocalAdminLoginResult { Authenticated = false, RequiresPasswordChange = true, Message = "First login must set a new password." };

                ValidateNewPassword(newPassword);
                credential = CreateCredential(newPassword);
            }
            else
            {
                if (!VerifyPassword(password, credential))
                    return new LocalAdminLoginResult { Authenticated = false, RequiresPasswordChange = credential.MustChangePassword, Message = "Invalid password." };

                if (credential.MustChangePassword)
                {
                    if (string.IsNullOrWhiteSpace(newPassword))
                        return new LocalAdminLoginResult { Authenticated = false, RequiresPasswordChange = true, Message = "Password change required." };

                    ValidateNewPassword(newPassword);
                    credential = UpdatePassword(credential, newPassword);
                }
            }

            var issued = IssueSession();
            WriteSessionCookie(context, issued.CookieValue, issued.ExpiresAt);
            return new LocalAdminLoginResult
            {
                Authenticated = true,
                RequiresPasswordChange = false,
                Message = "ok",
                SessionExpiresAt = issued.ExpiresAt
            };
        }
    }

    public LocalAdminLoginResult ChangePassword(HttpContext context, string currentPassword, string newPassword)
    {
        if (!IsLocalRequest(context))
            throw new InvalidOperationException("Local admin password change is only available from localhost.");

        var session = GetAuthenticatedSession(context);
        if (session == null)
            throw new InvalidOperationException("Admin login required.");

        lock (_gate)
        {
            var credential = GetCredential() ?? throw new InvalidOperationException("Local admin credential not initialized.");
            if (!VerifyPassword(currentPassword, credential))
                return new LocalAdminLoginResult { Authenticated = true, RequiresPasswordChange = credential.MustChangePassword, Message = "Current password is incorrect." };

            ValidateNewPassword(newPassword);
            UpdatePassword(credential, newPassword);
            return new LocalAdminLoginResult
            {
                Authenticated = true,
                RequiresPasswordChange = false,
                Message = "Password updated.",
                SessionExpiresAt = session.ExpiresAt
            };
        }
    }

    public void Logout(HttpContext context)
    {
        if (!TryReadSessionCookie(context, out var sessionId, out _))
        {
            ClearSessionCookie(context);
            return;
        }

        lock (_gate)
        {
            var existing = _db.Get<LocalAdminSession>(sessionId);
            if (existing != null && existing.RevokedAt == null)
            {
                existing.RevokedAt = DateTime.UtcNow;
                existing.LastSeenAt = DateTime.UtcNow;
                _db.Update(existing);
            }
        }

        ClearSessionCookie(context);
    }

    public bool TryRequireAuthenticated(HttpContext context, out LocalAdminSession session, out IResult denied)
    {
        session = null!;
        denied = null!;

        if (!IsLocalRequest(context))
        {
            denied = Results.StatusCode(StatusCodes.Status403Forbidden);
            return false;
        }

        var authenticated = GetAuthenticatedSession(context);
        if (authenticated == null)
        {
            denied = Results.Json(Broker.Helpers.ApiResponseHelper.Error("Admin login required.", 401), statusCode: 401);
            return false;
        }

        session = authenticated;
        return true;
    }

    private LocalAdminCredential? GetCredential()
        => _db.Get<LocalAdminCredential>(CredentialId);

    private LocalAdminCredential CreateCredential(string newPassword)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(newPassword, salt, 120000);
        var credential = new LocalAdminCredential
        {
            CredentialId = CredentialId,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(hash),
            HashIterations = 120000,
            MustChangePassword = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            LastPasswordChangeAt = DateTime.UtcNow
        };
        _db.Insert(credential);
        return credential;
    }

    private LocalAdminCredential UpdatePassword(LocalAdminCredential credential, string newPassword)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        credential.PasswordSalt = Convert.ToBase64String(salt);
        credential.PasswordHash = Convert.ToBase64String(HashPassword(newPassword, salt, credential.HashIterations <= 0 ? 120000 : credential.HashIterations));
        credential.MustChangePassword = false;
        credential.UpdatedAt = DateTime.UtcNow;
        credential.LastPasswordChangeAt = DateTime.UtcNow;
        _db.Update(credential);
        return credential;
    }

    private bool VerifyPassword(string password, LocalAdminCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.PasswordHash) || string.IsNullOrWhiteSpace(credential.PasswordSalt))
            return false;

        var salt = Convert.FromBase64String(credential.PasswordSalt);
        var expected = Convert.FromBase64String(credential.PasswordHash);
        var actual = HashPassword(password, salt, credential.HashIterations <= 0 ? 120000 : credential.HashIterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }

    private (string CookieValue, DateTime ExpiresAt) IssueSession()
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes);
        var tokenHash = ComputeSha256(token);
        var expiresAt = DateTime.UtcNow.AddHours(12);

        var session = new LocalAdminSession
        {
            SessionId = sessionId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        _db.Insert(session);
        return ($"{sessionId}.{token}", expiresAt);
    }

    public LocalAdminSession? GetAuthenticatedSession(HttpContext context)
    {
        if (!TryReadSessionCookie(context, out var sessionId, out var token))
            return null;

        var session = _db.Get<LocalAdminSession>(sessionId);
        if (session == null || session.RevokedAt != null || session.ExpiresAt <= DateTime.UtcNow)
            return null;

        var expected = ComputeSha256(token);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(session.TokenHash)))
            return null;

        return session;
    }

    private static void ValidateNewPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new InvalidOperationException("New password must be at least 8 characters.");
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsLocalRequest(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip == null)
            return false;

        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(ip.MapToIPv4()))
            return true;

        return false;
    }

    private static bool TryReadSessionCookie(HttpContext context, out string sessionId, out string token)
    {
        sessionId = string.Empty;
        token = string.Empty;
        if (!context.Request.Cookies.TryGetValue(SessionCookieName, out var cookieValue) || string.IsNullOrWhiteSpace(cookieValue))
            return false;

        var splitIndex = cookieValue.IndexOf('.', StringComparison.Ordinal);
        if (splitIndex <= 0 || splitIndex >= cookieValue.Length - 1)
            return false;

        sessionId = cookieValue[..splitIndex];
        token = cookieValue[(splitIndex + 1)..];
        return !string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(token);
    }

    private static void WriteSessionCookie(HttpContext context, string cookieValue, DateTime expiresAt)
    {
        context.Response.Cookies.Append(SessionCookieName, cookieValue, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = false,
            IsEssential = true,
            Expires = new DateTimeOffset(expiresAt)
        });
    }

    private static void ClearSessionCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(SessionCookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = false,
            IsEssential = true
        });
    }
}

public sealed class LocalAdminStatus
{
    public bool LocalRequest { get; set; }
    public bool Authenticated { get; set; }
    public bool HasPassword { get; set; }
    public bool RequiresPasswordChange { get; set; }
    public bool InitialPasswordActive { get; set; }
    public DateTime? SessionExpiresAt { get; set; }
}

public sealed class LocalAdminLoginResult
{
    public bool Authenticated { get; set; }
    public bool RequiresPasswordChange { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? SessionExpiresAt { get; set; }
}
