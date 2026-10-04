namespace BrokerCore.Data;

public class DevelopmentSeedOptions
{
    public const int DefaultRegistrationSecretLifetimeHours = 24;

    public bool Enabled { get; set; }
    public string PrincipalId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Development Principal";
    public string ActorType { get; set; } = "AI";
    public string TaskId { get; set; } = string.Empty;
    public string TaskType { get; set; } = "analysis";
    public string ScopeDescriptor { get; set; } = "{}";
    public string RuntimeDescriptor { get; set; } = "{}";
    public string AssignedRoleId { get; set; } = "role_reader";

    /// <summary>
    /// 種入任務的註冊密鑰（至少 32 個字元，不可是佔位值）。broker 只保存其雜湊；
    /// 沒有設定時不建立憑證，並撤銷先前為這個種子建立的憑證，這組 principal＋task 也就無法註冊。
    /// </summary>
    public string RegistrationSecret { get; set; } = string.Empty;

    /// <summary>種子憑證的有效時間（小時），每次 broker 啟動時重新起算；必須介於 1 與 720 之間。</summary>
    public int RegistrationSecretLifetimeHours { get; set; } = DefaultRegistrationSecretLifetimeHours;

    /// <summary>密鑰不出現在 ToString（設定物件可能被記錄或在除錯器中顯示）。</summary>
    public override string ToString()
        => $"{nameof(DevelopmentSeedOptions)} {{ Enabled = {Enabled}, PrincipalId = {PrincipalId}, TaskId = {TaskId}, RegistrationSecret = {(string.IsNullOrEmpty(RegistrationSecret) ? "(none)" : "(set)")} }}";
}
