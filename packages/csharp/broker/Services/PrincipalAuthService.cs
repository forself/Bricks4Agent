using System.Security.Cryptography;
using System.Text;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>
/// 多用戶 principal 帳密驗證——可從任何 IP 登入（vs LocalAdminAuthService 限 localhost），
/// 給 dashboard 客戶端登入用。Cookie session、httpOnly、PBKDF2-SHA256 120k iterations。
///
/// 跟 LocalAdminAuthService 並存：local admin 是「裝置維運」用、不要動；這個是「應用層用戶」用。
/// 兩個 cookie 名字不同（b4a_local_admin vs b4a_session）、互不衝突。
///
/// Phase A1 範圍：login / logout / me / change-password。沒 rate limit、沒鎖、沒 2FA。
/// 那些是 A4 工作。A1 只先把架構鋪好、確認 cookie / session / role 都能 round-trip。
/// </summary>
public sealed class PrincipalAuthService
{
    public const string SessionCookieName = "b4a_session";
    private const int SessionTtlHours = 12;

    private readonly BrokerDb _db;
    private readonly ILogger<PrincipalAuthService> _logger;

    public PrincipalAuthService(BrokerDb db, ILogger<PrincipalAuthService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public sealed class LoginResult
    {
        public bool Authenticated { get; set; }
        public string Message { get; set; } = "";
        public string? PrincipalId { get; set; }
        public string? Role { get; set; }
        public bool MustChangePassword { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }

    public sealed class CurrentUser
    {
        public string PrincipalId { get; set; } = "";
        public string Role { get; set; } = "user";
        public string? DisplayName { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public LoginResult Login(HttpContext context, string principalId, string password)
    {
        if (string.IsNullOrWhiteSpace(principalId) || string.IsNullOrWhiteSpace(password))
            return new LoginResult { Authenticated = false, Message = "Missing principalId or password" };

        var cred = _db.Get<PrincipalCredential>(principalId);
        if (cred == null || cred.Disabled)
            return new LoginResult { Authenticated = false, Message = "Invalid credentials" };  // 不洩漏「user 存在但密碼錯」

        if (!VerifyPassword(password, cred))
            return new LoginResult { Authenticated = false, Message = "Invalid credentials" };

        // OK：發 session
        var (cookieValue, expiresAt) = IssueSession(context, cred);
        WriteSessionCookie(context, cookieValue, expiresAt);

        cred.LastLoginAt = DateTime.UtcNow;
        cred.UpdatedAt = DateTime.UtcNow;
        _db.Update(cred);

        return new LoginResult
        {
            Authenticated = true,
            PrincipalId = cred.PrincipalId,
            Role = cred.Role,
            MustChangePassword = cred.MustChangePassword,
            ExpiresAt = expiresAt,
            Message = cred.MustChangePassword ? "Authenticated; password change required." : "ok",
        };
    }

    public bool ChangePassword(HttpContext context, string currentPassword, string newPassword, out string error)
    {
        error = "";
        var session = GetAuthenticatedSession(context);
        if (session == null) { error = "Not logged in"; return false; }

        var cred = _db.Get<PrincipalCredential>(session.PrincipalId);
        if (cred == null) { error = "Credential not found"; return false; }

        if (!VerifyPassword(currentPassword, cred)) { error = "Current password is incorrect"; return false; }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8) { error = "New password must be at least 8 characters"; return false; }

        var salt = RandomNumberGenerator.GetBytes(16);
        cred.PasswordSalt = Convert.ToBase64String(salt);
        cred.PasswordHash = Convert.ToBase64String(HashPassword(newPassword, salt, 120000));
        cred.HashIterations = 120000;
        cred.MustChangePassword = false;
        cred.UpdatedAt = DateTime.UtcNow;
        cred.LastPasswordChangeAt = DateTime.UtcNow;
        _db.Update(cred);
        return true;
    }

    public void Logout(HttpContext context)
    {
        if (TryReadSessionCookie(context, out var sessionId, out _))
        {
            var session = _db.Get<PrincipalSession>(sessionId);
            if (session != null && session.RevokedAt == null)
            {
                session.RevokedAt = DateTime.UtcNow;
                session.LastSeenAt = DateTime.UtcNow;
                _db.Update(session);
            }
        }
        ClearSessionCookie(context);
    }

    public CurrentUser? GetCurrentUser(HttpContext context)
    {
        var session = GetAuthenticatedSession(context);
        if (session == null) return null;
        var cred = _db.Get<PrincipalCredential>(session.PrincipalId);
        return new CurrentUser
        {
            PrincipalId = session.PrincipalId,
            Role = session.Role,
            DisplayName = cred?.DisplayName,
            ExpiresAt = session.ExpiresAt,
        };
    }

    public PrincipalSession? GetAuthenticatedSession(HttpContext context)
    {
        if (!TryReadSessionCookie(context, out var sessionId, out var token)) return null;
        var session = _db.Get<PrincipalSession>(sessionId);
        if (session == null || session.RevokedAt != null || session.ExpiresAt <= DateTime.UtcNow)
            return null;
        var expected = ComputeSha256(token);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(session.TokenHash)))
            return null;
        // 順手更新 LastSeenAt（不每次寫、避免 DB 太忙——只每 5 分鐘寫一次）
        if ((DateTime.UtcNow - session.LastSeenAt).TotalMinutes > 5)
        {
            session.LastSeenAt = DateTime.UtcNow;
            _db.Update(session);
        }
        return session;
    }

    /// <summary>
    /// 啟動時呼叫——確保 prn_dashboard 有一筆 admin credential。
    /// 沒密碼就用 env Auth__InitialAdminPassword（沒設則 "admin"）、強制下次登入改密碼。
    /// </summary>
    public void EnsureInitialAdmin(IConfiguration config)
    {
        const string adminPid = "prn_dashboard";
        var existing = _db.Get<PrincipalCredential>(adminPid);
        if (existing != null) return;

        var initialPwd = config.GetValue<string>("Auth:InitialAdminPassword") ?? "admin";
        var salt = RandomNumberGenerator.GetBytes(16);
        var cred = new PrincipalCredential
        {
            PrincipalId = adminPid,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(HashPassword(initialPwd, salt, 120000)),
            HashIterations = 120000,
            Role = "admin",
            DisplayName = "Dashboard Admin",
            MustChangePassword = true,   // 第一次登入強制改
        };
        _db.Insert(cred);
        _logger.LogWarning(
            "Auth: seeded initial admin '{Pid}' with default password (must change on first login). " +
            "Override default via Auth__InitialAdminPassword env.",
            adminPid);
    }

    // ── helpers ─────────────────────────────────────────────────────

    private (string CookieValue, DateTime ExpiresAt) IssueSession(HttpContext context, PrincipalCredential cred)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var tokenHash = ComputeSha256(token);
        var expiresAt = DateTime.UtcNow.AddHours(SessionTtlHours);
        _db.Insert(new PrincipalSession
        {
            SessionId = sessionId,
            PrincipalId = cred.PrincipalId,
            Role = cred.Role,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            IpAddress = context.Connection.RemoteIpAddress?.ToString(),
            UserAgent = context.Request.Headers.UserAgent.ToString().Length > 200
                ? context.Request.Headers.UserAgent.ToString()[..200]
                : context.Request.Headers.UserAgent.ToString(),
        });
        return ($"{sessionId}.{token}", expiresAt);
    }

    private bool VerifyPassword(string password, PrincipalCredential cred)
    {
        if (string.IsNullOrEmpty(cred.PasswordHash) || string.IsNullOrEmpty(cred.PasswordSalt)) return false;
        var salt = Convert.FromBase64String(cred.PasswordSalt);
        var expected = Convert.FromBase64String(cred.PasswordHash);
        var iter = cred.HashIterations <= 0 ? 120000 : cred.HashIterations;
        var actual = HashPassword(password, salt, iter);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] HashPassword(string password, byte[] salt, int iterations)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }

    private static string ComputeSha256(string s)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static bool TryReadSessionCookie(HttpContext ctx, out string sessionId, out string token)
    {
        sessionId = ""; token = "";
        if (!ctx.Request.Cookies.TryGetValue(SessionCookieName, out var v) || string.IsNullOrWhiteSpace(v)) return false;
        var dot = v.IndexOf('.');
        if (dot <= 0 || dot >= v.Length - 1) return false;
        sessionId = v[..dot]; token = v[(dot + 1)..];
        return !string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(token);
    }

    private static void WriteSessionCookie(HttpContext ctx, string value, DateTime expiresAt)
    {
        // SameSite=Lax 比 Strict 寬鬆一點（允許頂層 navigation 帶 cookie），對 dashboard 體驗更好。
        // Secure=false 是因為現階段走 SSH tunnel + http、之後上 HTTPS 直接改 true。
        ctx.Response.Cookies.Append(SessionCookieName, value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = false,
            IsEssential = true,
            Expires = new DateTimeOffset(expiresAt),
            Path = "/",
        });
    }

    private static void ClearSessionCookie(HttpContext ctx)
    {
        ctx.Response.Cookies.Delete(SessionCookieName, new CookieOptions
        {
            HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = false, IsEssential = true, Path = "/",
        });
    }
}
