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

    // ── A4 PASS 1：rate limit / lockout 參數 ──
    private const int FailedAttemptThreshold = 5;          // N 次失敗就鎖
    private const int FailedWindowMinutes = 10;            // sliding window
    private const int LockDurationMinutes = 15;            // 鎖多久

    // 每個 IP 多久最多嘗試幾次（更廣的防爆破層、跨 principal 累計）
    // 結構：ip → (firstAttemptAt, count)。in-memory、broker 重啟清。
    private const int IpAttemptThreshold = 20;
    private const int IpWindowMinutes = 5;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime FirstAt, int Count)> _ipAttempts = new();

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

        // A4 PASS 1：IP 層 rate limit（防同一個爆破來源跨 principal 試）
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (IsIpRateLimited(ip, out var ipRetryMin))
        {
            _logger.LogWarning("Auth: IP {Ip} rate-limited (>={Threshold}/{WindowMin}min). Try again in ~{Min}min.",
                ip, IpAttemptThreshold, IpWindowMinutes, ipRetryMin);
            return new LoginResult { Authenticated = false, Message = $"Too many attempts from this IP. Try again in ~{ipRetryMin}min." };
        }

        var cred = _db.Get<PrincipalCredential>(principalId);
        if (cred == null || cred.Disabled)
        {
            // 即使 user 不存在也記 IP 嘗試、避免 enum
            RecordIpAttempt(ip);
            return new LoginResult { Authenticated = false, Message = "Invalid credentials" };
        }

        // 帳號層 lockout 檢查（避免進 PBKDF2 浪費 CPU）
        if (cred.LockedUntil.HasValue && cred.LockedUntil.Value > DateTime.UtcNow)
        {
            var remaining = (int)Math.Ceiling((cred.LockedUntil.Value - DateTime.UtcNow).TotalMinutes);
            _logger.LogWarning("Auth: account {Pid} is locked, remaining {Min}min", principalId, remaining);
            return new LoginResult { Authenticated = false, Message = $"Account locked. Try again in {remaining} min." };
        }

        if (!VerifyPassword(password, cred))
        {
            RecordIpAttempt(ip);
            RecordFailedAttempt(cred);
            return new LoginResult { Authenticated = false, Message = "Invalid credentials" };
        }

        // 成功：清失敗計數、發 session
        if (cred.FailedLoginAttempts > 0 || cred.LockedUntil.HasValue)
        {
            cred.FailedLoginAttempts = 0;
            cred.FirstFailedAt = null;
            cred.LockedUntil = null;
        }

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

    // ── A4 PASS 1 helpers ────────────────────────────────────────────

    private bool IsIpRateLimited(string ip, out int retryMinutes)
    {
        retryMinutes = 0;
        if (!_ipAttempts.TryGetValue(ip, out var rec)) return false;
        var windowStart = DateTime.UtcNow.AddMinutes(-IpWindowMinutes);
        if (rec.FirstAt < windowStart)
        {
            // 視窗已過、清掉
            _ipAttempts.TryRemove(ip, out _);
            return false;
        }
        if (rec.Count >= IpAttemptThreshold)
        {
            retryMinutes = Math.Max(1, (int)Math.Ceiling((rec.FirstAt.AddMinutes(IpWindowMinutes) - DateTime.UtcNow).TotalMinutes));
            return true;
        }
        return false;
    }

    private void RecordIpAttempt(string ip)
    {
        var now = DateTime.UtcNow;
        _ipAttempts.AddOrUpdate(ip,
            _ => (now, 1),
            (_, prev) =>
            {
                // 視窗外 → reset 計數
                if (prev.FirstAt < now.AddMinutes(-IpWindowMinutes)) return (now, 1);
                return (prev.FirstAt, prev.Count + 1);
            });
    }

    private void RecordFailedAttempt(PrincipalCredential cred)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-FailedWindowMinutes);

        // 視窗外 → reset
        if (!cred.FirstFailedAt.HasValue || cred.FirstFailedAt.Value < windowStart)
        {
            cred.FirstFailedAt = now;
            cred.FailedLoginAttempts = 1;
        }
        else
        {
            cred.FailedLoginAttempts++;
        }

        if (cred.FailedLoginAttempts >= FailedAttemptThreshold)
        {
            cred.LockedUntil = now.AddMinutes(LockDurationMinutes);
            _logger.LogWarning("Auth: account {Pid} locked for {Min}min after {N} failed attempts",
                cred.PrincipalId, LockDurationMinutes, cred.FailedLoginAttempts);
        }
        cred.UpdatedAt = now;
        _db.Update(cred);
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

    // ── A3：admin operations ─────────────────────────────────────────

    public sealed class UserSummary
    {
        public string PrincipalId { get; set; } = "";
        public string Role { get; set; } = "user";
        public string? DisplayName { get; set; }
        public bool MustChangePassword { get; set; }
        public bool Disabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime? LastPasswordChangeAt { get; set; }
    }

    public List<UserSummary> ListUsers()
    {
        return _db.GetAll<PrincipalCredential>()
            .OrderBy(c => c.PrincipalId)
            .Select(c => new UserSummary
            {
                PrincipalId = c.PrincipalId, Role = c.Role,
                DisplayName = c.DisplayName, MustChangePassword = c.MustChangePassword, Disabled = c.Disabled,
                CreatedAt = c.CreatedAt, LastLoginAt = c.LastLoginAt, LastPasswordChangeAt = c.LastPasswordChangeAt,
            })
            .ToList();
    }

    public UserSummary CreateUser(string principalId, string password, string role, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(principalId)) throw new InvalidOperationException("principal_id required");
        if (principalId.Length < 3 || principalId.Length > 80) throw new InvalidOperationException("principal_id length must be 3-80");
        // 簡單格式限制：英數+底線+冒號（codebase 既有 'prn_dashboard' / 'role_admin' 等用底線、要兼容）
        foreach (var ch in principalId)
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == ':' || ch == '-'))
                throw new InvalidOperationException("principal_id must be alphanumeric / _ / : / -");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8) throw new InvalidOperationException("password must be ≥ 8 chars");
        if (role != "admin" && role != "user") throw new InvalidOperationException("role must be 'admin' or 'user'");

        if (_db.Get<PrincipalCredential>(principalId) != null)
            throw new InvalidOperationException($"User '{principalId}' already exists");

        var salt = RandomNumberGenerator.GetBytes(16);
        var cred = new PrincipalCredential
        {
            PrincipalId = principalId,
            PasswordSalt = Convert.ToBase64String(salt),
            PasswordHash = Convert.ToBase64String(HashPassword(password, salt, 120000)),
            HashIterations = 120000,
            Role = role,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? principalId : displayName.Trim(),
            MustChangePassword = false,
        };
        _db.Insert(cred);
        _logger.LogInformation("Auth admin: created user {Pid} role={Role}", principalId, role);
        return new UserSummary
        {
            PrincipalId = cred.PrincipalId, Role = cred.Role,
            DisplayName = cred.DisplayName, CreatedAt = cred.CreatedAt,
        };
    }

    public bool AdminResetPassword(string principalId, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
            throw new InvalidOperationException("New password must be ≥ 8 chars");
        var cred = _db.Get<PrincipalCredential>(principalId);
        if (cred == null) return false;
        var salt = RandomNumberGenerator.GetBytes(16);
        cred.PasswordSalt = Convert.ToBase64String(salt);
        cred.PasswordHash = Convert.ToBase64String(HashPassword(newPassword, salt, 120000));
        cred.HashIterations = 120000;
        cred.MustChangePassword = true;     // admin 重設後、user 下次登入強制再改
        cred.UpdatedAt = DateTime.UtcNow;
        cred.LastPasswordChangeAt = DateTime.UtcNow;
        _db.Update(cred);
        // 撤銷所有現存 session、強迫重登
        RevokeAllSessions(principalId);
        _logger.LogInformation("Auth admin: reset password for {Pid} (force-change on next login)", principalId);
        return true;
    }

    public bool SetUserDisabled(string principalId, bool disabled)
    {
        var cred = _db.Get<PrincipalCredential>(principalId);
        if (cred == null) return false;
        cred.Disabled = disabled;
        cred.UpdatedAt = DateTime.UtcNow;
        _db.Update(cred);
        if (disabled) RevokeAllSessions(principalId);    // disable 立刻踢線
        _logger.LogInformation("Auth admin: {State} user {Pid}", disabled ? "DISABLED" : "ENABLED", principalId);
        return true;
    }

    public bool SetUserRole(string principalId, string role)
    {
        if (role != "admin" && role != "user") throw new InvalidOperationException("role must be 'admin' or 'user'");
        var cred = _db.Get<PrincipalCredential>(principalId);
        if (cred == null) return false;
        cred.Role = role;
        cred.UpdatedAt = DateTime.UtcNow;
        _db.Update(cred);
        // 改 role 後既有 session 仍記載舊 role、可選擇撤銷重登
        RevokeAllSessions(principalId);
        _logger.LogInformation("Auth admin: changed role of {Pid} to {Role}", principalId, role);
        return true;
    }

    public bool DeleteUser(string principalId)
    {
        if (principalId == "prn_dashboard") throw new InvalidOperationException("Cannot delete the primary admin");
        var cred = _db.Get<PrincipalCredential>(principalId);
        if (cred == null) return false;
        RevokeAllSessions(principalId);
        _db.Delete<PrincipalCredential>(principalId);
        _logger.LogInformation("Auth admin: deleted user {Pid}", principalId);
        return true;
    }

    private void RevokeAllSessions(string principalId)
    {
        var sessions = _db.Query<PrincipalSession>(
            "SELECT * FROM principal_sessions WHERE principal_id = @pid AND revoked_at IS NULL",
            new { pid = principalId });
        foreach (var s in sessions)
        {
            s.RevokedAt = DateTime.UtcNow;
            _db.Update(s);
        }
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
