using System.Diagnostics.CodeAnalysis;

namespace BrokerCore.Crypto;

/// <summary>
/// 設定檔中「範本佔位值」的共用判斷規則。
///
/// 下列值都視為「尚未設定真正的密鑰」：
/// - null、空字串或只有空白
/// - 以 <c>CHANGE_ME</c> 開頭（broker/appsettings.json 的範本值）
/// - 以 <c>REPLACE_WITH_</c> 開頭（appsettings.Development.example.json 與 worker 範本值）
///
/// broker 啟動時由 Broker.Configuration.BrokerSecretsValidator 依環境決定
/// 佔位值要改用隨機金鑰或拒絕啟動；ScopedTokenService 與 DbSessionKeyStore
/// 也使用同一條規則，避免三把金鑰的判斷不一致。
/// </summary>
public static class SecretPlaceholders
{
    public const string ChangeMePrefix = "CHANGE_ME";
    public const string ReplaceWithPrefix = "REPLACE_WITH_";

    public static bool IsPlaceholder([NotNullWhen(false)] string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        var trimmed = value.Trim();
        return trimmed.StartsWith(ChangeMePrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(ReplaceWithPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
