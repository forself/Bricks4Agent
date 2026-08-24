using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

public sealed class LocalAdminAuthService
{
    private const string BootstrapCredentialId = "local_admin";
    private const string BootstrapUsername = "admin";
    private const string InitialPassword = "admin";
    private const string ActiveStatus = "active";
    private const string DisabledStatus = "disabled";
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
        var hasCredential = _db.GetAll<LocalAdminCredential>().Count > 0;
        var session = localRequest ? GetAuthenticatedSession(context) : null;
        var permissions = session == null
            ? Array.Empty<string>()
            : ResolveSessionPermissions(session).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();

        return new LocalAdminStatus
        {
            LocalRequest = localRequest,
            Authenticated = session != null,
            HasPassword = hasCredential,
            RequiresPasswordChange = !hasCredential || HasBootstrapCredentialRequiringChange(),
            InitialPasswordActive = !hasCredential,
            SessionExpiresAt = session?.ExpiresAt,
            OperatorId = session?.OperatorId ?? string.Empty,
            Username = session?.Username ?? string.Empty,
            Role = session?.Role ?? string.Empty,
            Permissions = permissions
        };
    }

    public LocalAdminLoginResult Login(HttpContext context, string password, string? newPassword)
        => Login(context, BootstrapUsername, password, newPassword);

    public LocalAdminLoginResult Login(HttpContext context, string username, string password, string? newPassword)
    {
        if (!IsLocalRequest(context))
            throw new InvalidOperationException("Local admin login is only available from localhost.");

        lock (_gate)
        {
            var normalizedUsername = NormalizeUsername(username);
            var credential = GetCredentialByUsername(normalizedUsername);
            if (credential == null && _db.GetAll<LocalAdminCredential>().Count == 0)
            {
                if (!string.Equals(normalizedUsername, BootstrapUsername, StringComparison.Ordinal))
                    return FailedLogin("Initial admin username is required.", requiresPasswordChange: true);

                if (!string.Equals(password, InitialPassword, StringComparison.Ordinal))
                    return FailedLogin("Initial password is required.", requiresPasswordChange: true);

                if (string.IsNullOrWhiteSpace(newPassword))
                    return FailedLogin("First login must set a new password.", requiresPasswordChange: true);

                ValidateNewPassword(newPassword);
                credential = CreateCredential(
                    BootstrapCredentialId,
                    BootstrapUsername,
                    "Local Super Admin",
                    LocalAdminRoles.SuperAdmin,
                    newPassword);
            }
            else
            {
                if (credential == null ||
                    !string.Equals(credential.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
                    return FailedLogin("Invalid username or password.");

                if (!VerifyPassword(password, credential))
                    return FailedLogin("Invalid username or password.", credential.MustChangePassword);

                if (credential.MustChangePassword)
                {
                    if (string.IsNullOrWhiteSpace(newPassword))
                        return FailedLogin("Password change required.", requiresPasswordChange: true);

                    ValidateNewPassword(newPassword);
                    credential = UpdatePassword(credential, newPassword);
                }
            }

            credential.LastLoginAt = DateTime.UtcNow;
            credential.UpdatedAt = DateTime.UtcNow;
            _db.Update(credential);

            var issued = IssueSession(credential);
            WriteSessionCookie(context, issued.CookieValue, issued.ExpiresAt);
            return BuildLoginResult(credential, issued.ExpiresAt);
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
            var credential = GetCredentialByOperator(session.OperatorId)
                ?? throw new InvalidOperationException("Local admin credential not initialized.");
            if (!VerifyPassword(currentPassword, credential))
            {
                return new LocalAdminLoginResult
                {
                    Authenticated = true,
                    RequiresPasswordChange = credential.MustChangePassword,
                    Message = "Current password is incorrect.",
                    SessionExpiresAt = session.ExpiresAt,
                    OperatorId = session.OperatorId,
                    Username = session.Username,
                    Role = session.Role,
                    Permissions = ResolveSessionPermissions(session).ToArray()
                };
            }

            UpdatePassword(credential, newPassword);
            return new LocalAdminLoginResult
            {
                Authenticated = true,
                RequiresPasswordChange = false,
                Message = "Password updated.",
                SessionExpiresAt = session.ExpiresAt,
                OperatorId = session.OperatorId,
                Username = session.Username,
                Role = session.Role,
                Permissions = ResolveSessionPermissions(session).ToArray()
            };
        }
    }

    public IReadOnlyList<LocalAdminOperatorView> ListOperators()
        => _db.GetAll<LocalAdminCredential>()
            .OrderBy(c => c.Username, StringComparer.OrdinalIgnoreCase)
            .Select(ToOperatorView)
            .ToArray();

    public LocalAdminOperatorView CreateOperator(
        string username,
        string displayName,
        string role,
        string password,
        string createdBy)
    {
        var normalizedUsername = NormalizeUsername(username);
        if (string.IsNullOrWhiteSpace(normalizedUsername))
            throw new InvalidOperationException("Username is required.");
        if (!LocalAdminRoles.IsKnown(role))
            throw new InvalidOperationException("Unknown local admin role.");
        ValidateNewPassword(password);

        lock (_gate)
        {
            if (GetCredentialByUsername(normalizedUsername) != null)
                throw new InvalidOperationException("Operator username already exists.");

            var credential = CreateCredential(
                normalizedUsername,
                normalizedUsername,
                string.IsNullOrWhiteSpace(displayName) ? normalizedUsername : displayName.Trim(),
                LocalAdminRoles.Normalize(role),
                password);
            _logger.LogInformation(
                "Local admin operator {Username} created by {CreatedBy}",
                normalizedUsername,
                createdBy);
            return ToOperatorView(credential);
        }
    }

    public LocalAdminOperatorView UpdateOperatorRole(string operatorIdOrUsername, string role, string updatedBy)
    {
        if (!LocalAdminRoles.IsKnown(role))
            throw new InvalidOperationException("Unknown local admin role.");

        lock (_gate)
        {
            var credential = FindOperator(operatorIdOrUsername)
                ?? throw new InvalidOperationException("Operator not found.");
            credential.Role = LocalAdminRoles.Normalize(role);
            credential.UpdatedAt = DateTime.UtcNow;
            _db.Update(credential);
            RevokeOperatorSessions(credential.OperatorId, updatedBy);
            return ToOperatorView(credential);
        }
    }

    public LocalAdminOperatorView DisableOperator(string operatorIdOrUsername, string disabledBy)
    {
        lock (_gate)
        {
            var credential = FindOperator(operatorIdOrUsername)
                ?? throw new InvalidOperationException("Operator not found.");
            credential.Status = DisabledStatus;
            credential.UpdatedAt = DateTime.UtcNow;
            _db.Update(credential);
            RevokeOperatorSessions(credential.OperatorId, disabledBy);
            return ToOperatorView(credential);
        }
    }

    public LocalAdminOperatorView ResetOperatorPassword(string operatorIdOrUsername, string newPassword, string updatedBy)
    {
        ValidateNewPassword(newPassword);
        lock (_gate)
        {
            var credential = FindOperator(operatorIdOrUsername)
                ?? throw new InvalidOperationException("Operator not found.");
            UpdatePassword(credential, newPassword, mustChangePassword: true);
            RevokeOperatorSessions(credential.OperatorId, updatedBy);
            return ToOperatorView(credential);
        }
    }

    public int RevokeOperatorSessions(string operatorIdOrUsername, string revokedBy)
    {
        var credential = FindOperator(operatorIdOrUsername);
        var operatorId = credential?.OperatorId ?? NormalizeUsername(operatorIdOrUsername);
        var now = DateTime.UtcNow;
        var sessions = _db.GetAll<LocalAdminSession>()
            .Where(s =>
                string.Equals(s.OperatorId, operatorId, StringComparison.OrdinalIgnoreCase) &&
                s.RevokedAt == null)
            .ToList();

        foreach (var session in sessions)
        {
            session.RevokedAt = now;
            session.LastSeenAt = now;
            _db.Update(session);
        }

        if (sessions.Count > 0)
        {
            _logger.LogInformation(
                "Revoked {Count} local admin sessions for {OperatorId} by {RevokedBy}",
                sessions.Count,
                operatorId,
                revokedBy);
        }

        return sessions.Count;
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

    public bool TryRequirePermission(
        HttpContext context,
        string permission,
        out LocalAdminSession session,
        out IResult denied)
    {
        if (!TryRequireAuthenticated(context, out session, out denied))
            return false;

        if (ResolveSessionPermissions(session).Contains(permission))
            return true;

        denied = Results.Json(
            Broker.Helpers.ApiResponseHelper.Error($"Forbidden: permission '{permission}' required.", 403),
            statusCode: 403);
        return false;
    }

    public bool TryRequireAnyPermission(
        HttpContext context,
        IReadOnlyCollection<string> permissions,
        out LocalAdminSession session,
        out IResult denied)
    {
        if (!TryRequireAuthenticated(context, out session, out denied))
            return false;

        var resolved = ResolveSessionPermissions(session);
        if (permissions.Any(resolved.Contains))
            return true;

        denied = Results.Json(
            Broker.Helpers.ApiResponseHelper.Error($"Forbidden: one of [{string.Join(", ", permissions)}] required.", 403),
            statusCode: 403);
        return false;
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

        var credential = GetCredentialByOperator(session.OperatorId);
        if (credential != null && !string.Equals(credential.Status, ActiveStatus, StringComparison.OrdinalIgnoreCase))
            return null;

        return session;
    }

    private bool HasBootstrapCredentialRequiringChange()
    {
        var bootstrap = GetCredentialByUsername(BootstrapUsername) ?? GetCredentialByOperator(BootstrapCredentialId);
        return bootstrap?.MustChangePassword ?? true;
    }

    private LocalAdminCredential? GetCredentialByUsername(string username)
        => _db.Query<LocalAdminCredential>(
                "SELECT * FROM local_admin_credentials WHERE lower(username) = lower(@username) LIMIT 1",
                new { username = NormalizeUsername(username) })
            .FirstOrDefault();

    private LocalAdminCredential? GetCredentialByOperator(string operatorId)
        => _db.Get<LocalAdminCredential>(operatorId) ?? _db.Query<LocalAdminCredential>(
                "SELECT * FROM local_admin_credentials WHERE lower(operator_id) = lower(@operatorId) LIMIT 1",
                new { operatorId = NormalizeUsername(operatorId) })
            .FirstOrDefault();

    private LocalAdminCredential? FindOperator(string operatorIdOrUsername)
        => GetCredentialByOperator(operatorIdOrUsername)
            ?? GetCredentialByUsername(operatorIdOrUsername);

    private LocalAdminCredential CreateCredential(
        string credentialId,
        string username,
        string displayName,
        string role,
        string newPassword)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = HashPassword(newPassword, salt, 120000);
        var credential = new LocalAdminCredential
        {
            CredentialId = credentialId,
            OperatorId = credentialId,
            Username = NormalizeUsername(username),
            DisplayName = displayName,
            Role = LocalAdminRoles.Normalize(role),
            PermissionOverrides = "{}",
            Status = ActiveStatus,
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

    private LocalAdminCredential UpdatePassword(
        LocalAdminCredential credential,
        string newPassword,
        bool mustChangePassword = false)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        credential.PasswordSalt = Convert.ToBase64String(salt);
        credential.PasswordHash = Convert.ToBase64String(HashPassword(
            newPassword,
            salt,
            credential.HashIterations <= 0 ? 120000 : credential.HashIterations));
        credential.MustChangePassword = mustChangePassword;
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
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);
    }

    private (string CookieValue, DateTime ExpiresAt) IssueSession(LocalAdminCredential credential)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes);
        var tokenHash = ComputeSha256(token);
        var expiresAt = DateTime.UtcNow.AddHours(12);
        var permissions = LocalAdminPermissions.ForRole(credential.Role)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var session = new LocalAdminSession
        {
            SessionId = sessionId,
            TokenHash = tokenHash,
            OperatorId = string.IsNullOrWhiteSpace(credential.OperatorId) ? credential.CredentialId : credential.OperatorId,
            Username = credential.Username,
            Role = credential.Role,
            PermissionsSnapshot = JsonSerializer.Serialize(permissions),
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };
        _db.Insert(session);
        return ($"{sessionId}.{token}", expiresAt);
    }

    private static LocalAdminLoginResult BuildLoginResult(LocalAdminCredential credential, DateTime expiresAt)
    {
        var permissions = LocalAdminPermissions.ForRole(credential.Role)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LocalAdminLoginResult
        {
            Authenticated = true,
            RequiresPasswordChange = false,
            Message = "ok",
            SessionExpiresAt = expiresAt,
            OperatorId = string.IsNullOrWhiteSpace(credential.OperatorId) ? credential.CredentialId : credential.OperatorId,
            Username = credential.Username,
            Role = credential.Role,
            Permissions = permissions
        };
    }

    private static LocalAdminLoginResult FailedLogin(
        string message,
        bool requiresPasswordChange = false)
        => new()
        {
            Authenticated = false,
            RequiresPasswordChange = requiresPasswordChange,
            Message = message,
            Permissions = Array.Empty<string>()
        };

    private static IReadOnlySet<string> ResolveSessionPermissions(LocalAdminSession session)
    {
        if (!string.IsNullOrWhiteSpace(session.PermissionsSnapshot))
        {
            try
            {
                var permissions = JsonSerializer.Deserialize<string[]>(session.PermissionsSnapshot);
                if (permissions is { Length: > 0 })
                    return new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                // Fall back to role-derived permissions.
            }
        }

        return new HashSet<string>(LocalAdminPermissions.ForRole(session.Role), StringComparer.OrdinalIgnoreCase);
    }

    private static LocalAdminOperatorView ToOperatorView(LocalAdminCredential credential)
        => new()
        {
            OperatorId = string.IsNullOrWhiteSpace(credential.OperatorId) ? credential.CredentialId : credential.OperatorId,
            Username = credential.Username,
            DisplayName = credential.DisplayName,
            Role = credential.Role,
            Status = credential.Status,
            CreatedAt = credential.CreatedAt,
            UpdatedAt = credential.UpdatedAt,
            LastLoginAt = credential.LastLoginAt
        };

    private static void ValidateNewPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            throw new InvalidOperationException("New password must be at least 8 characters.");
    }

    private static string NormalizeUsername(string? username)
        => (username ?? string.Empty).Trim().ToLowerInvariant();

    private static string ComputeSha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsLocalRequest(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip == null)
            return true;

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
    public string OperatorId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = Array.Empty<string>();
}

public sealed class LocalAdminLoginResult
{
    public bool Authenticated { get; set; }
    public bool RequiresPasswordChange { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? SessionExpiresAt { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string[] Permissions { get; set; } = Array.Empty<string>();
}

public sealed class LocalAdminOperatorView
{
    public string OperatorId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
