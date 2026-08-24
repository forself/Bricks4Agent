using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class PortalAuthOptions
{
    public bool AllowSelfRegistration { get; set; } = true;
    public int SessionHours { get; set; } = 12;
    public int MinimumPasswordLength { get; set; } = 8;
    public int LineVerificationCodeMinutes { get; set; } = 10;
}

public sealed class PortalAuthService
{
    private static readonly Regex UserIdPattern = new("^[A-Za-z0-9._:@-]{3,80}$", RegexOptions.CultureInvariant);
    public const string SessionCookieName = "b4a_portal";

    private readonly BrokerDb _db;
    private readonly HighLevelCoordinator _coordinator;
    private readonly PortalLineVerificationService _lineVerification;
    private readonly PortalAuthOptions _options;
    private readonly object _gate = new();

    public PortalAuthService(
        BrokerDb db,
        HighLevelCoordinator coordinator,
        PortalLineVerificationService lineVerification,
        PortalAuthOptions options)
    {
        _db = db;
        _coordinator = coordinator;
        _lineVerification = lineVerification;
        _options = options;
    }

    public PortalAuthStatus GetStatus(HttpContext context)
    {
        var session = GetAuthenticatedSession(context);
        HighLevelUserProfile? profile = null;
        PortalUserCredential? credential = null;
        if (session != null)
        {
            credential = _db.Get<PortalUserCredential>(session.UserId);
            profile = _coordinator.GetLineUserProfile(session.UserId);
        }

        return new PortalAuthStatus
        {
            Authenticated = session != null,
            UserId = session?.UserId ?? string.Empty,
            DisplayName = profile?.PreferredDisplayName ?? credential?.DisplayName ?? string.Empty,
            AccessTier = profile?.AccessTier ?? string.Empty,
            RegistrationStatus = profile?.RegistrationStatus ?? string.Empty,
            SessionExpiresAt = session?.ExpiresAt,
            SelfRegistrationEnabled = _options.AllowSelfRegistration,
            LineVerification = session == null
                ? null
                : _lineVerification.GetStatus(session.UserId)
        };
    }

    public PortalAuthLoginResult Register(HttpContext context, string userId, string password, string? displayName)
    {
        var normalizedUserId = NormalizeUserId(userId);
        ValidatePassword(password);

        if (!_options.AllowSelfRegistration)
            throw new InvalidOperationException("Portal self-registration is disabled.");

        lock (_gate)
        {
            var existing = _db.Get<PortalUserCredential>(normalizedUserId);
            if (existing != null)
                throw new InvalidOperationException("Portal user already exists.");

            var credential = CreateCredential(normalizedUserId, password, displayName);
            var profile = _coordinator.EnsureLineUserProfile(normalizedUserId, displayName);
            var lineVerification = _lineVerification.IssueCode(credential);
            var issued = IssueSession(credential.UserId);
            credential.LastLoginAt = DateTime.UtcNow;
            credential.UpdatedAt = DateTime.UtcNow;
            _db.Update(credential);
            WriteSessionCookie(context, issued.CookieValue, issued.ExpiresAt);

            return new PortalAuthLoginResult
            {
                Authenticated = true,
                UserId = credential.UserId,
                DisplayName = profile.PreferredDisplayName ?? credential.DisplayName,
                AccessTier = profile.AccessTier,
                RegistrationStatus = profile.RegistrationStatus,
                SessionExpiresAt = issued.ExpiresAt,
                Message = "registered",
                LineVerification = lineVerification
            };
        }
    }

    public PortalAuthLoginResult Login(HttpContext context, string userId, string password)
    {
        var normalizedUserId = NormalizeUserId(userId);
        lock (_gate)
        {
            var credential = _db.Get<PortalUserCredential>(normalizedUserId);
            if (credential == null || credential.Disabled || !VerifyPassword(password, credential))
            {
                return new PortalAuthLoginResult
                {
                    Authenticated = false,
                    UserId = normalizedUserId,
                    Message = "Invalid user id or password."
                };
            }

            var profile = _coordinator.EnsureLineUserProfile(credential.UserId, credential.DisplayName);
            var issued = IssueSession(credential.UserId);
            credential.LastLoginAt = DateTime.UtcNow;
            credential.UpdatedAt = DateTime.UtcNow;
            _db.Update(credential);
            WriteSessionCookie(context, issued.CookieValue, issued.ExpiresAt);

            return new PortalAuthLoginResult
            {
                Authenticated = true,
                UserId = credential.UserId,
                DisplayName = profile.PreferredDisplayName ?? credential.DisplayName,
                AccessTier = profile.AccessTier,
                RegistrationStatus = profile.RegistrationStatus,
                SessionExpiresAt = issued.ExpiresAt,
                Message = "ok",
                LineVerification = _lineVerification.GetStatus(credential.UserId)
            };
        }
    }

    public PortalLineVerificationIssue IssueLineVerificationCode(PortalUserSession session)
    {
        lock (_gate)
        {
            var credential = _db.Get<PortalUserCredential>(session.UserId)
                ?? throw new InvalidOperationException("Portal user not found.");
            return _lineVerification.IssueCode(credential);
        }
    }

    public PortalLineVerificationStatus GetLineVerificationStatus(string userId)
        => _lineVerification.GetStatus(userId);

    public void Logout(HttpContext context)
    {
        if (!TryReadSessionCookie(context, out var sessionId, out _))
        {
            ClearSessionCookie(context);
            return;
        }

        lock (_gate)
        {
            var existing = _db.Get<PortalUserSession>(sessionId);
            if (existing != null && existing.RevokedAt == null)
            {
                existing.RevokedAt = DateTime.UtcNow;
                existing.LastSeenAt = DateTime.UtcNow;
                _db.Update(existing);
            }
        }

        ClearSessionCookie(context);
    }

    public bool TryRequireAuthenticated(HttpContext context, out PortalUserSession session, out IResult denied)
    {
        session = null!;
        denied = null!;

        var authenticated = GetAuthenticatedSession(context);
        if (authenticated == null)
        {
            denied = Results.Json(Broker.Helpers.ApiResponseHelper.Error("Portal login required.", 401), statusCode: 401);
            return false;
        }

        session = authenticated;
        return true;
    }

    public PortalUserSession? GetAuthenticatedSession(HttpContext context)
    {
        if (!TryReadSessionCookie(context, out var sessionId, out var token))
            return null;

        var session = _db.Get<PortalUserSession>(sessionId);
        if (session == null || session.RevokedAt != null || session.ExpiresAt <= DateTime.UtcNow)
            return null;

        var expected = ComputeSha256(token);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(session.TokenHash)))
            return null;

        session.LastSeenAt = DateTime.UtcNow;
        _db.Update(session);
        return session;
    }

    private PortalUserCredential CreateCredential(string userId, string password, string? displayName)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var credential = new PortalUserCredential
        {
            UserId = userId,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(HashPassword(password, salt, 120000)),
            HashIterations = 120000,
            DisplayName = NormalizeDisplayName(displayName),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Insert(credential);
        return credential;
    }

    private bool VerifyPassword(string password, PortalUserCredential credential)
    {
        if (string.IsNullOrWhiteSpace(credential.PasswordHash) || string.IsNullOrWhiteSpace(credential.PasswordSalt))
            return false;

        var salt = Convert.FromBase64String(credential.PasswordSalt);
        var expected = Convert.FromBase64String(credential.PasswordHash);
        var actual = HashPassword(password, salt, credential.HashIterations <= 0 ? 120000 : credential.HashIterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private (string CookieValue, DateTime ExpiresAt) IssueSession(string userId)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes);
        var expiresAt = DateTime.UtcNow.AddHours(Math.Max(1, _options.SessionHours));

        _db.Insert(new PortalUserSession
        {
            SessionId = sessionId,
            UserId = userId,
            TokenHash = ComputeSha256(token),
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        });

        return ($"{sessionId}.{token}", expiresAt);
    }

    private static string NormalizeUserId(string value)
    {
        var userId = value.Trim();
        if (!UserIdPattern.IsMatch(userId))
            throw new InvalidOperationException("user_id must be 3-80 characters and may contain letters, numbers, dot, underscore, dash, colon, or at sign.");
        return userId;
    }

    private void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < Math.Max(8, _options.MinimumPasswordLength))
            throw new InvalidOperationException($"Password must be at least {Math.Max(8, _options.MinimumPasswordLength)} characters.");
    }

    private static string NormalizeDisplayName(string? value)
    {
        var displayName = value?.Trim() ?? string.Empty;
        return displayName.Length > 80 ? displayName[..80] : displayName;
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
            Secure = context.Request.IsHttps,
            IsEssential = true,
            Expires = new DateTimeOffset(expiresAt),
            Path = "/"
        });
    }

    private static void ClearSessionCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(SessionCookieName, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            IsEssential = true,
            Path = "/"
        });
    }
}

public sealed class PortalAuthStatus
{
    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; set; }
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("access_tier")]
    public string AccessTier { get; set; } = string.Empty;
    [JsonPropertyName("registration_status")]
    public string RegistrationStatus { get; set; } = string.Empty;
    [JsonPropertyName("session_expires_at")]
    public DateTime? SessionExpiresAt { get; set; }
    [JsonPropertyName("self_registration_enabled")]
    public bool SelfRegistrationEnabled { get; set; }
    [JsonPropertyName("line_verification")]
    public PortalLineVerificationStatus? LineVerification { get; set; }
}

public sealed class PortalAuthLoginResult
{
    [JsonPropertyName("authenticated")]
    public bool Authenticated { get; set; }
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("access_tier")]
    public string AccessTier { get; set; } = string.Empty;
    [JsonPropertyName("registration_status")]
    public string RegistrationStatus { get; set; } = string.Empty;
    [JsonPropertyName("session_expires_at")]
    public DateTime? SessionExpiresAt { get; set; }
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
    [JsonPropertyName("line_verification")]
    public object? LineVerification { get; set; }
}
