using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>註冊憑證的來源（寫入 <see cref="RegistrationCredential.Source"/>）</summary>
public static class RegistrationCredentialSources
{
    /// <summary>DevelopmentSeed 設定的密鑰（compose 與本機測試）</summary>
    public const string DevelopmentSeed = "development_seed";

    /// <summary>DashboardSeed 設定的密鑰（只在 Development）</summary>
    public const string DashboardSeed = "dashboard_seed";

    /// <summary>POST /agents/spawn 為容器簽發</summary>
    public const string AgentSpawn = "agent_spawn";

    /// <summary>管理員以 /admin/registration-credentials/issue 簽發</summary>
    public const string AdminIssue = "admin_issue";
}

/// <summary>驗證失敗的實際原因：只寫進伺服器端 log 與稽核，回應一律是同一個 401。</summary>
public enum RegistrationCredentialFailure
{
    None = 0,
    /// <summary>請求沒有帶密鑰</summary>
    Missing,
    /// <summary>這組 principal＋task 沒有任何憑證</summary>
    Unknown,
    /// <summary>有憑證，但密鑰不符</summary>
    Mismatch,
    /// <summary>密鑰相符，但憑證已到期</summary>
    Expired,
    /// <summary>密鑰相符，但憑證已撤銷</summary>
    Revoked
}

/// <summary>驗證結果。<see cref="Credential"/> 是密鑰相符的那一筆（到期或撤銷時也會帶出，供稽核）。</summary>
public sealed class RegistrationCredentialCheck
{
    public RegistrationCredentialCheck(RegistrationCredentialFailure failure, RegistrationCredential? credential)
    {
        Failure = failure;
        Credential = credential;
    }

    public RegistrationCredentialFailure Failure { get; }

    public RegistrationCredential? Credential { get; }

    public bool Succeeded => Failure == RegistrationCredentialFailure.None && Credential != null;
}

/// <summary>剛簽發的憑證。<see cref="Secret"/> 只在這裡出現一次；ToString 不含密鑰。</summary>
public sealed class IssuedRegistrationCredential
{
    public IssuedRegistrationCredential(string credentialId, string secret, DateTime expiresAt)
    {
        CredentialId = credentialId;
        Secret = secret;
        ExpiresAt = expiresAt;
    }

    public string CredentialId { get; }

    public string Secret { get; }

    public DateTime ExpiresAt { get; }

    public override string ToString() => $"{nameof(IssuedRegistrationCredential)} {{ {CredentialId}, expires {ExpiresAt:O} }}";
}

/// <summary>
/// Session 註冊憑證：每組 principal＋task 一把高熵密鑰，只保存 SHA-256 雜湊，以常數時間比對。
/// 可重複使用直到到期或撤銷；不做一次性，也不做「最新註冊為準」。
/// </summary>
public interface IRegistrationCredentialService
{
    /// <summary>簽發新憑證；明文只回傳這一次。</summary>
    IssuedRegistrationCredential Issue(string principalId, string taskId, string source, string issuedBy, DateTime expiresAt);

    /// <summary>
    /// 驗證密鑰是否屬於這組 principal＋task 的有效憑證。缺少、不符、到期、撤銷都視為失敗；
    /// 呼叫端必須以同一個回應回覆所有失敗。不更新使用紀錄（註冊成功後再呼叫 <see cref="RecordUse"/>）。
    /// </summary>
    RegistrationCredentialCheck Verify(string? principalId, string? taskId, string? secret);

    /// <summary>記錄一次成功的註冊（最後使用時間與次數）。</summary>
    void RecordUse(string credentialId);

    /// <summary>撤銷單一憑證；已撤銷或不存在時回傳 false。</summary>
    bool Revoke(string credentialId, string reason, string revokedBy);

    /// <summary>撤銷這組 principal＋task 尚未撤銷的憑證（可限定來源），回傳撤銷筆數。</summary>
    int RevokeFor(string principalId, string taskId, string reason, string revokedBy, string? source = null);

    /// <summary>
    /// 依設定的種子密鑰建立或保留憑證：同一把密鑰保留原紀錄並把到期時間重設為 <paramref name="expiresAt"/>；
    /// 密鑰換了就撤銷同來源的舊憑證並建立新的。同一把密鑰曾被種子啟動流程以外的人（例如管理員）撤銷時，
    /// 撤銷保留、不再種入，回傳 null；要恢復必須換一把密鑰。
    /// </summary>
    RegistrationCredential? UpsertSeed(string principalId, string taskId, string secret, string source, DateTime expiresAt);

    /// <summary>列出憑證（不含雜湊以外的密鑰資料）；預設只列仍有效者。</summary>
    IReadOnlyList<RegistrationCredential> List(string? principalId = null, string? taskId = null, bool includeInactive = false);
}
