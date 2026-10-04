namespace BrokerCore.Services;

/// <summary>
/// broker 簽發的註冊憑證存活時間（設定區段 <c>Broker:RegistrationCredential</c>）。
///
/// - <see cref="SpawnedAgentLifetimeHours"/>：<c>POST /agents/spawn</c> 交給容器的憑證有效時間。
///   容器在這段時間內可以重新註冊（例如重啟或 session 到達最長存活時間後）；過期後要重新 spawn。
/// - <see cref="AdminIssuedLifetimeHours"/>：管理員簽發時沒有指定 <c>lifetime_hours</c> 的預設值。
///
/// 種子憑證的存活時間在 <c>DevelopmentSeed:RegistrationSecretLifetimeHours</c>（每次啟動重新起算）。
/// 所有值都必須介於 1 與 <see cref="RegistrationCredentialService.MaxLifetimeHours"/> 之間。
/// </summary>
public sealed class RegistrationCredentialOptions
{
    public const string SectionName = "Broker:RegistrationCredential";
    public const int DefaultLifetimeHours = 24;

    public int SpawnedAgentLifetimeHours { get; set; } = DefaultLifetimeHours;

    public int AdminIssuedLifetimeHours { get; set; } = DefaultLifetimeHours;

    public TimeSpan SpawnedAgentLifetime => TimeSpan.FromHours(SpawnedAgentLifetimeHours);

    public TimeSpan AdminIssuedLifetime => TimeSpan.FromHours(AdminIssuedLifetimeHours);

    /// <summary>設定不合理時丟出 <see cref="ArgumentOutOfRangeException"/>，讓 broker 在啟動時就失敗。</summary>
    public void Validate()
    {
        RequireLifetime(nameof(SpawnedAgentLifetimeHours), SpawnedAgentLifetimeHours);
        RequireLifetime(nameof(AdminIssuedLifetimeHours), AdminIssuedLifetimeHours);
    }

    /// <summary>存活時間（小時）是否在允許範圍內。</summary>
    public static bool IsValidLifetime(int hours)
        => hours >= 1 && hours <= RegistrationCredentialService.MaxLifetimeHours;

    private static void RequireLifetime(string name, int hours)
    {
        if (!IsValidLifetime(hours))
        {
            throw new ArgumentOutOfRangeException(
                name,
                hours,
                $"{SectionName}:{name} must be between 1 and {RegistrationCredentialService.MaxLifetimeHours} hours.");
        }
    }
}
