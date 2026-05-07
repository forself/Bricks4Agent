using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BrokerCore.Services;

/// <summary>
/// 機密讀取統一入口——讓所有 service 不直接 `config.GetValue&lt;string&gt;("Foo:ApiKey")`，
/// 而是 `config.GetSecret("Foo:ApiKey")`，以便支援 Docker secrets / 檔案掛載模式。
///
/// 解析優先序（從高到低）：
///   1. `{key}File` 設定值——檔案路徑、讀檔內容（trim 換行）
///      （配合 Docker secrets：`environment: Foo__ApiKeyFile=/run/secrets/foo_key`）
///   2. `{key}` 設定值——直接當值用（既有行為，向後相容）
///   3. null
///
/// 為什麼這樣設計：
///   - 不破壞現有的 .env / appsettings 直接賦值——不設 `*File` 就跟以前一樣
///   - Docker secrets 純 file mount，不污染 env，cron / docker inspect 不會誤洩
///   - 可以混搭：dev 用 env、prod 用 secrets，同一份 code 不用改
/// </summary>
public static class SecretConfig
{
    /// <summary>
    /// 解析機密。先看 `{key}File` 環境/config（path）→ 讀檔；fallback 到 `{key}` 直接讀。
    /// 讀到的字串會 Trim() 掉前後空白與換行（Docker secrets 檔案常帶 trailing newline）。
    /// </summary>
    public static string? GetSecret(this IConfiguration config, string key)
    {
        // 1. Try {key}File (path indirection) first — typical for Docker secrets
        var filePath = config.GetValue<string>(key + "File");
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var content = File.ReadAllText(filePath).Trim();
                    if (!string.IsNullOrEmpty(content)) return content;
                }
            }
            catch
            {
                // 讀檔失敗就 fallback 到 env 直接讀，不打死整個 service。
                // 呼叫端可選擇 log warning（透過 GetSecretWithSource）
            }
        }

        // 2. Fallback to direct value (backward compatible with existing code)
        return config.GetValue<string>(key);
    }

    /// <summary>
    /// 跟 GetSecret 相同邏輯、額外回傳來源（"file" / "env" / "missing"）給呼叫端 log 用，
    /// 方便看出哪些 secret 走 file mount、哪些還是 plain env、哪些根本沒設定。
    /// 不會 leak 實際值——僅來源類型。
    /// </summary>
    public static (string? Value, string Source) GetSecretWithSource(this IConfiguration config, string key)
    {
        var filePath = config.GetValue<string>(key + "File");
        if (!string.IsNullOrWhiteSpace(filePath))
        {
            try
            {
                if (File.Exists(filePath))
                {
                    var content = File.ReadAllText(filePath).Trim();
                    if (!string.IsNullOrEmpty(content)) return (content, "file");
                }
            }
            catch { /* fall through */ }
        }

        var direct = config.GetValue<string>(key);
        if (!string.IsNullOrEmpty(direct)) return (direct, "env");
        return (null, "missing");
    }

    /// <summary>
    /// 啟動時呼叫——log 一次「哪些 secret 設了、來源是什麼」總覽。
    /// 永遠不 print 實際值；只 print key 名稱跟 source / 長度。
    /// </summary>
    public static void LogSecretSummary(this IConfiguration config, ILogger logger, params string[] keys)
    {
        foreach (var key in keys)
        {
            var (value, source) = GetSecretWithSource(config, key);
            if (value == null)
            {
                logger.LogWarning("Secret missing: {Key} (set via {Key} env or {Key}File path)", key, key, key);
            }
            else
            {
                logger.LogInformation("Secret loaded: {Key} (source={Source}, length={Length})",
                    key, source, value.Length);
            }
        }
    }
}
