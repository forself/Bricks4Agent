using BrokerCore.Data;
using BrokerCore.Services;
using Microsoft.Extensions.Logging;

namespace Broker.Configuration;

/// <summary>
/// 啟動時檢查種子（DevelopmentSeed、DashboardSeed）的註冊密鑰。
///
/// 種子啟用時必須提供可用的 <c>RegistrationSecret</c>（不是佔位值、至少 32 個字元），
/// 存活時間必須介於 1 與 720 小時之間：
/// - DevelopmentSeed 在所有環境都會套用（compose 以 Production 執行），非 Development／Testing 環境缺少密鑰時拒絕啟動，
///   以免 agent 啟動後才默默註冊失敗；Development／Testing 只記警告，這組種子不建立憑證、無法註冊。
/// - DashboardSeed 只在 Development 套用，缺少密鑰時只記警告。
/// 錯誤訊息與 log 只寫設定鍵名，不寫密鑰內容。
/// </summary>
public static class RegistrationSeedValidator
{
    public const string DevelopmentSeedSection = "DevelopmentSeed";
    public const string DashboardSeedSection = "DashboardSeed";

    /// <summary>
    /// 檢查一個種子設定；回傳這個種子是否會建立註冊憑證。不合法且不允許降級時丟出 <see cref="InvalidOperationException"/>。
    /// </summary>
    public static bool Validate(
        DevelopmentSeedOptions? seed,
        string sectionName,
        string? environmentName,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (seed?.Enabled != true)
        {
            return false;
        }

        if (!DevelopmentSeedOptionsLifetimeIsValid(seed))
        {
            throw new InvalidOperationException(
                $"{sectionName}:RegistrationSecretLifetimeHours must be between 1 and {RegistrationCredentialService.MaxLifetimeHours}.");
        }

        if (RegistrationCredentialService.IsUsableSecret(seed.RegistrationSecret))
        {
            return true;
        }

        var requiredHere = string.Equals(sectionName, DevelopmentSeedSection, StringComparison.Ordinal)
            && !BrokerSecretsValidator.IsDevelopmentLike(environmentName);
        var message =
            $"{sectionName}:RegistrationSecret is missing, a placeholder, or shorter than {RegistrationCredentialService.MinimumSecretLength} characters. " +
            "Generate one (for example with tools/agent/container/gen-stack-secrets.mjs) and pass it through configuration.";

        if (requiredHere)
        {
            throw new InvalidOperationException(message);
        }

        logger.LogWarning(
            "{Message} The seeded principal and task cannot register a session until it is set.",
            message);
        return false;
    }

    private static bool DevelopmentSeedOptionsLifetimeIsValid(DevelopmentSeedOptions seed)
        => RegistrationCredentialOptions.IsValidLifetime(seed.RegistrationSecretLifetimeHours);
}
