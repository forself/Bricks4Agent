using System.Security.Cryptography;
using System.Text;
using BrokerCore.Crypto;
using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// Session 註冊憑證服務（見 <see cref="IRegistrationCredentialService"/>）。
///
/// - 密鑰是 32 bytes 亂數的 base64url；資料庫只存 UTF-8 密鑰的 SHA-256。
/// - 比對以 <see cref="CryptographicOperations.FixedTimeEquals"/> 逐筆進行，每次驗證固定比對相同的次數（不足的以固定雜湊補足）。
/// - 不在任何地方記錄密鑰本身。
/// </summary>
public class RegistrationCredentialService : IRegistrationCredentialService
{
    /// <summary>設定檔提供的密鑰最短長度（字元）。產生器輸出 43 個字元。</summary>
    public const int MinimumSecretLength = 32;

    /// <summary>單一憑證的最長有效時間（小時）。</summary>
    public const int MaxLifetimeHours = 720;

    // 驗證時每一次查詢最多比對的筆數。先只比對仍有效（未撤銷、未到期）的憑證，所以已撤銷或到期的紀錄
    //（例如 spawn 失敗留下的）再多，也不會把仍有效的舊憑證擠出比對範圍；同一組 principal＋task 正常只有少數幾把有效憑證。
    private const int MaxCandidates = 50;

    // 超過這個長度的輸入直接視為不符（仍做一次比對），避免以超長字串消耗雜湊時間。
    private const int MaxSecretLength = 512;

    private static readonly byte[] DummyHash = Encoding.ASCII.GetBytes(ComputeHash("registration-credential-placeholder"));

    private readonly BrokerDb _db;

    public RegistrationCredentialService(BrokerDb db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>UTF-8 密鑰的 SHA-256，小寫十六進位。</summary>
    public static string ComputeHash(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    /// <summary>設定檔中的密鑰可以使用：不是佔位值，且至少 <see cref="MinimumSecretLength"/> 個字元。</summary>
    public static bool IsUsableSecret(string? secret)
        => !SecretPlaceholders.IsPlaceholder(secret) && secret.Trim().Length >= MinimumSecretLength;

    /// <summary>產生新的密鑰（32 bytes 亂數，base64url、無填充）。</summary>
    public static string GenerateSecret()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <inheritdoc />
    public IssuedRegistrationCredential Issue(string principalId, string taskId, string source, string issuedBy, DateTime expiresAt)
    {
        RequireSubject(principalId, taskId);
        var secret = GenerateSecret();
        var credential = Insert(principalId, taskId, ComputeHash(secret), source, issuedBy, expiresAt);
        return new IssuedRegistrationCredential(credential.CredentialId, secret, credential.ExpiresAt);
    }

    /// <inheritdoc />
    public RegistrationCredentialCheck Verify(string? principalId, string? taskId, string? secret)
    {
        var provided = Encoding.ASCII.GetBytes(
            string.IsNullOrEmpty(secret) || secret.Length > MaxSecretLength
                ? ComputeHash("registration-credential-missing")
                : ComputeHash(secret));

        var hasSubject = !string.IsNullOrWhiteSpace(principalId) && !string.IsNullOrWhiteSpace(taskId);
        var now = DateTime.UtcNow;

        // 1. 比對仍有效的憑證（同一把密鑰若有多筆紀錄，例如撤銷後又以同一把重新種入，以有效的那一筆為準）。
        var active = hasSubject
            ? _db.Query<RegistrationCredential>(
                $@"SELECT * FROM registration_credentials
                   WHERE principal_id = @principalId AND task_id = @taskId
                     AND revoked_at IS NULL AND expires_at > @now
                   ORDER BY created_at DESC LIMIT {MaxCandidates}",
                new { principalId, taskId, now })
            : new List<RegistrationCredential>();

        // 2. 比對最近的紀錄（不分狀態），只用來判斷失敗原因（撤銷、到期或不符），寫進 log 與稽核。
        //    兩次查詢一律執行，每次都固定比對 MaxCandidates 次，讓各種情況的工作量相近。
        var recent = hasSubject
            ? _db.Query<RegistrationCredential>(
                $@"SELECT * FROM registration_credentials
                   WHERE principal_id = @principalId AND task_id = @taskId
                   ORDER BY created_at DESC LIMIT {MaxCandidates}",
                new { principalId, taskId })
            : new List<RegistrationCredential>();

        var matchedActive = FindMatch(provided, active);
        var matchedRecent = FindMatch(provided, recent);

        if (string.IsNullOrEmpty(secret))
        {
            return new RegistrationCredentialCheck(RegistrationCredentialFailure.Missing, null);
        }

        if (secret.Length > MaxSecretLength)
        {
            return new RegistrationCredentialCheck(
                recent.Count == 0 ? RegistrationCredentialFailure.Unknown : RegistrationCredentialFailure.Mismatch,
                null);
        }

        if (matchedActive != null && IsActive(matchedActive, now))
        {
            return new RegistrationCredentialCheck(RegistrationCredentialFailure.None, matchedActive);
        }

        if (matchedRecent == null)
        {
            return new RegistrationCredentialCheck(
                recent.Count == 0 ? RegistrationCredentialFailure.Unknown : RegistrationCredentialFailure.Mismatch,
                null);
        }

        if (matchedRecent.RevokedAt != null)
        {
            return new RegistrationCredentialCheck(RegistrationCredentialFailure.Revoked, matchedRecent);
        }

        if (matchedRecent.ExpiresAt <= now)
        {
            return new RegistrationCredentialCheck(RegistrationCredentialFailure.Expired, matchedRecent);
        }

        // 未撤銷也未到期（第一次查詢與這裡的時間判斷在邊界上不一致時）：仍是有效的憑證。
        return new RegistrationCredentialCheck(RegistrationCredentialFailure.None, matchedRecent);
    }

    /// <summary>
    /// 以 <see cref="CryptographicOperations.FixedTimeEquals"/> 固定比對 <see cref="MaxCandidates"/> 次
    /// （不足的以固定雜湊補足，不提早結束），回傳第一筆（最新的）相符的紀錄。
    /// </summary>
    private static RegistrationCredential? FindMatch(byte[] provided, IReadOnlyList<RegistrationCredential> candidates)
    {
        RegistrationCredential? matched = null;
        for (var index = 0; index < MaxCandidates; index++)
        {
            var isCandidate = index < candidates.Count;
            var stored = isCandidate
                ? Encoding.ASCII.GetBytes(candidates[index].SecretHash ?? string.Empty)
                : DummyHash;
            if (CryptographicOperations.FixedTimeEquals(provided, stored) && isCandidate && matched == null)
            {
                matched = candidates[index];
            }
        }

        return matched;
    }

    private static bool IsActive(RegistrationCredential credential, DateTime now)
        => credential.RevokedAt == null && credential.ExpiresAt > now;

    /// <inheritdoc />
    public void RecordUse(string credentialId)
    {
        _db.Execute(
            "UPDATE registration_credentials SET last_used_at = @now, use_count = use_count + 1 WHERE credential_id = @credentialId",
            new { now = DateTime.UtcNow, credentialId });
    }

    /// <inheritdoc />
    public bool IsRevoked(string credentialId)
    {
        if (string.IsNullOrWhiteSpace(credentialId))
            return true;

        var credential = _db.Get<RegistrationCredential>(credentialId);
        return credential == null || credential.RevokedAt != null;
    }

    /// <inheritdoc />
    public bool Revoke(string credentialId, string reason, string revokedBy)
    {
        var affected = _db.Execute(
            @"UPDATE registration_credentials
              SET revoked_at = @now, revoked_by = @revokedBy, revoke_reason = @reason
              WHERE credential_id = @credentialId AND revoked_at IS NULL",
            new { now = DateTime.UtcNow, revokedBy = revokedBy ?? string.Empty, reason = reason ?? string.Empty, credentialId });
        return affected > 0;
    }

    /// <inheritdoc />
    public int RevokeFor(string principalId, string taskId, string reason, string revokedBy, string? source = null, string? exceptCredentialId = null)
    {
        var parameters = new
        {
            now = DateTime.UtcNow,
            revokedBy = revokedBy ?? string.Empty,
            reason = reason ?? string.Empty,
            principalId,
            taskId,
            source = source ?? string.Empty,
            exceptCredentialId = exceptCredentialId ?? string.Empty
        };

        // 條件只由固定片段組成，值一律以參數傳入。
        var sql = @"UPDATE registration_credentials
                  SET revoked_at = @now, revoked_by = @revokedBy, revoke_reason = @reason
                  WHERE principal_id = @principalId AND task_id = @taskId AND revoked_at IS NULL";
        if (source != null)
            sql += " AND source = @source";
        if (!string.IsNullOrEmpty(exceptCredentialId))
            sql += " AND credential_id <> @exceptCredentialId";

        return _db.Execute(sql, parameters);
    }

    /// <summary>種子啟動流程在憑證紀錄中的建立者與撤銷者；其他撤銷者（管理員等）的撤銷不會被重新種入。</summary>
    public const string SeedStartupActor = "broker-startup";

    /// <inheritdoc />
    public RegistrationCredential? UpsertSeed(string principalId, string taskId, string secret, string source, DateTime expiresAt)
    {
        RequireSubject(principalId, taskId);
        if (!IsUsableSecret(secret))
        {
            throw new ArgumentException(
                $"A seed registration secret must not be a placeholder and must have at least {MinimumSecretLength} characters.",
                nameof(secret));
        }

        ValidateExpiry(expiresAt);
        var hash = Encoding.ASCII.GetBytes(ComputeHash(secret));
        var active = _db.Query<RegistrationCredential>(
            @"SELECT * FROM registration_credentials
              WHERE principal_id = @principalId AND task_id = @taskId AND source = @source AND revoked_at IS NULL
              ORDER BY created_at DESC",
            new { principalId, taskId, source });

        RegistrationCredential? kept = null;
        foreach (var credential in active)
        {
            if (kept == null && CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(credential.SecretHash ?? string.Empty)))
            {
                kept = credential;
            }
        }

        foreach (var credential in active.Where(credential => !ReferenceEquals(credential, kept)))
        {
            Revoke(credential.CredentialId, "Replaced by a new seed secret.", SeedStartupActor);
        }

        if (kept == null)
        {
            // 同一把密鑰曾被啟動流程以外的人（例如管理員）撤銷：撤銷跨重啟保留，不再種入；
            // 要恢復就換一把密鑰。
            if (IsRevokedByOperator(principalId, taskId, source, hash))
            {
                return null;
            }

            return Insert(principalId, taskId, ComputeHash(secret), source, SeedStartupActor, expiresAt);
        }

        // 同一把密鑰：每次啟動重新設定到期時間。
        _db.Execute(
            "UPDATE registration_credentials SET expires_at = @expiresAt WHERE credential_id = @credentialId",
            new { expiresAt, credentialId = kept.CredentialId });
        kept.ExpiresAt = expiresAt;
        return kept;
    }

    private bool IsRevokedByOperator(string principalId, string taskId, string source, byte[] hash)
    {
        var revoked = _db.Query<RegistrationCredential>(
            @"SELECT * FROM registration_credentials
              WHERE principal_id = @principalId AND task_id = @taskId AND source = @source
                AND revoked_at IS NOT NULL AND revoked_by <> @startup
              ORDER BY created_at DESC LIMIT 200",
            new { principalId, taskId, source, startup = SeedStartupActor });

        var found = false;
        foreach (var credential in revoked)
        {
            if (CryptographicOperations.FixedTimeEquals(hash, Encoding.ASCII.GetBytes(credential.SecretHash ?? string.Empty)))
            {
                found = true;
            }
        }

        return found;
    }

    /// <inheritdoc />
    public IReadOnlyList<RegistrationCredential> List(string? principalId = null, string? taskId = null, bool includeInactive = false)
    {
        var filters = new List<string>();
        if (!string.IsNullOrWhiteSpace(principalId)) filters.Add("principal_id = @principalId");
        if (!string.IsNullOrWhiteSpace(taskId)) filters.Add("task_id = @taskId");
        if (!includeInactive) filters.Add("revoked_at IS NULL AND expires_at > @now");

        var where = filters.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", filters);
        return _db.Query<RegistrationCredential>(
            $"SELECT * FROM registration_credentials {where} ORDER BY created_at DESC LIMIT 200",
            new { principalId = principalId ?? string.Empty, taskId = taskId ?? string.Empty, now = DateTime.UtcNow });
    }

    private RegistrationCredential Insert(string principalId, string taskId, string secretHash, string source, string issuedBy, DateTime expiresAt)
    {
        ValidateExpiry(expiresAt);
        var credential = new RegistrationCredential
        {
            CredentialId = IdGen.New("rgc"),
            PrincipalId = principalId,
            TaskId = taskId,
            SecretHash = secretHash,
            Source = source ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = issuedBy ?? string.Empty,
            ExpiresAt = expiresAt,
            UseCount = 0
        };
        _db.Insert(credential);
        return credential;
    }

    /// <summary>每一把憑證都必須在未來、且不超過 <see cref="MaxLifetimeHours"/> 小時後到期。</summary>
    private static void ValidateExpiry(DateTime expiresAt)
    {
        var now = DateTime.UtcNow;
        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "A registration credential must expire in the future.");
        }

        if (expiresAt > now.AddHours(MaxLifetimeHours).AddMinutes(1))
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), $"A registration credential may live at most {MaxLifetimeHours} hours.");
        }
    }

    private static void RequireSubject(string principalId, string taskId)
    {
        if (string.IsNullOrWhiteSpace(principalId))
            throw new ArgumentException("principal_id is required.", nameof(principalId));
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("task_id is required.", nameof(taskId));
    }
}
