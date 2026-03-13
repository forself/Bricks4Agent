using System.Text.Json;
using BrokerCore.Contracts;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// 政策裁決引擎 —— 7 條確定性規則
///
/// 規則 1: Epoch 閘道    — token.epoch &lt; current_epoch → Deny
/// 規則 2: 風險閘道      — risk_level &gt; Medium → Deny（Phase 3: 允許 Low + Medium）
/// 規則 3: 範圍匹配      — 請求路徑 ⊆ scope
/// 規則 4: 角色匹配      — 角色 allowed_task_types 包含任務類型
/// 規則 5: 路徑沙箱      — 檔案路徑 ⊆ 允許路徑
/// 規則 6: 指令黑名單    — 命令黑名單
/// 規則 7: Schema 驗證   — payload 符合 param_schema
///
/// ML 僅用於建議性風險評分，絕不用於授權決策。
/// </summary>
public class PolicyEngine : IPolicyEngine
{
    private readonly ISchemaValidator _schemaValidator;
    private readonly PolicyEngineOptions _options;

    // M-2 修復：黑名單從 PolicyEngineOptions 讀取，不再硬編碼

    public PolicyEngine(ISchemaValidator schemaValidator, PolicyEngineOptions? options = null)
    {
        _schemaValidator = schemaValidator;
        _options = options ?? new PolicyEngineOptions();
    }

    /// <inheritdoc />
    public PolicyResult Evaluate(
        ExecutionRequest request,
        Capability capability,
        CapabilityGrant grant,
        BrokerTask task,
        int currentEpoch,
        int tokenEpoch)
    {
        // ── 規則 1: Epoch 閘道 ──
        if (tokenEpoch < currentEpoch)
        {
            return PolicyResult.Deny(
                $"Token epoch ({tokenEpoch}) is behind system epoch ({currentEpoch}). Token invalidated by kill switch.");
        }

        // ── 規則 2: 風險閘道（Phase 3: 允許 Low + Medium） ──
        if (capability.RiskLevel > RiskLevel.Medium)
        {
            return PolicyResult.Deny(
                $"Capability '{capability.CapabilityId}' has risk level {capability.RiskLevel}. Only Low and Medium risk are allowed.");
        }

        // ── 規則 3: 範圍匹配 ──
        // 檢查請求的資源是否在 grant 的 scope 範圍內
        if (!IsScopeValid(request.RequestPayload, grant.ScopeOverride, task.ScopeDescriptor))
        {
            return PolicyResult.Deny("Request resource is outside the granted scope.");
        }

        // ── 規則 4: 角色匹配（驗證任務類型是否在允許列表） ──
        // 此規則在 BrokerService 層處理（需要 Role 資訊），此處信任上游已驗證

        // ── 規則 5: 路徑沙箱 ──
        if (!IsPathSandboxed(request.RequestPayload))
        {
            return PolicyResult.Deny("File path violates sandbox restrictions.");
        }

        // ── 規則 6: 指令黑名單 ──
        if (ContainsBlacklistedCommand(request.RequestPayload))
        {
            return PolicyResult.Deny("Request contains blacklisted command.");
        }

        // ── 規則 7: Schema 驗證 ──
        var (isValid, errorMsg) = _schemaValidator.Validate(request.RequestPayload, capability.ParamSchema);
        if (!isValid)
        {
            return PolicyResult.Deny($"Payload schema validation failed: {errorMsg}");
        }

        // ── 通過所有規則 ──
        return PolicyResult.Allow();
    }

    // ── 內部驗證方法 ──

    /// <summary>驗證請求資源在 scope 範圍內</summary>
    private static bool IsScopeValid(string payload, string grantScope, string taskScope)
    {
        // Phase 1 簡易實作：檢查 payload 中的 path 是否在 scope 的 paths 陣列內
        try
        {
            string? requestPath = ExtractPath(payload);
            if (requestPath == null) return true; // 無路徑的請求不需 scope 檢查

            var allowedPaths = ExtractPaths(grantScope) ?? ExtractPaths(taskScope);
            if (allowedPaths == null || allowedPaths.Count == 0) return true; // 無 scope 限制

            requestPath = NormalizePath(requestPath);
            return allowedPaths.Any(allowed => requestPath.StartsWith(NormalizePath(allowed), StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return true; // 解析失敗 → 不阻斷（Phase 1 寬鬆）
        }
    }

    /// <summary>
    /// 路徑沙箱檢查：禁止路徑遍歷（修復 H-8：處理 URL 編碼遍歷）
    /// M-2 修復：禁止路徑前綴從 PolicyEngineOptions 讀取
    /// </summary>
    private bool IsPathSandboxed(string payload)
    {
        try
        {
            var path = ExtractPath(payload);
            if (path == null) return true;

            // 先解碼 URL 編碼（防止 %2e%2e 繞過）
            var decoded = Uri.UnescapeDataString(path);

            // 禁止路徑遍歷（原始路徑和解碼後都檢查）
            if (decoded.Contains("..") || path.Contains(".."))
                return false;

            // 禁止 ~ 展開（但允許 Windows 路徑中的合法字元）
            if (decoded.Contains('~') || path.Contains('~'))
                return false;

            // 禁止 null byte 注入
            if (decoded.Contains('\0') || path.Contains('\0'))
                return false;

            // 禁止存取系統目錄（M-2 修復：從配置讀取）
            var normalized = NormalizePath(decoded);

            foreach (var prefix in _options.ForbiddenPathPrefixes)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    // 必須完全匹配或後接 /（避免 /etcetera 被攔）
                    if (normalized.Length == prefix.Length ||
                        normalized[prefix.Length] == '/')
                        return false;
                }
            }

            return true;
        }
        catch
        {
            return false; // 解析失敗 → 拒絕（fail-closed）
        }
    }

    /// <summary>
    /// 指令黑名單檢查（修復 H-5：使用前綴匹配避免 "rm" 誤攔 "firmware" 等）
    /// M-2 修復：黑名單從 PolicyEngineOptions 讀取
    /// </summary>
    private bool ContainsBlacklistedCommand(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            // 檢查 command 欄位
            if (root.TryGetProperty("command", out var cmdProp))
            {
                var command = (cmdProp.GetString() ?? "").Trim();
                if (string.IsNullOrEmpty(command)) return false;

                // 精確匹配（command 完全等於黑名單項目）
                foreach (var exact in _options.CommandExactBlacklist)
                {
                    if (command.Equals(exact, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // 前綴匹配（command 以黑名單項目開頭，後接空白/tab）
                foreach (var prefix in _options.CommandPrefixBlacklist)
                {
                    if (command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // SQL 注入檢查（不區分大小寫的子字串匹配，保留原有行為）
                foreach (var sql in _options.SqlBlacklist)
                {
                    if (command.Contains(sql, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    // ── 工具方法 ──

    private static string? ExtractPath(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("path", out var p))
                return p.GetString();
            if (doc.RootElement.TryGetProperty("file_path", out var fp))
                return fp.GetString();
            return null;
        }
        catch { return null; }
    }

    private static List<string>? ExtractPaths(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array)
            {
                return paths.EnumerateArray()
                    .Select(p => p.GetString() ?? "")
                    .Where(p => !string.IsNullOrEmpty(p))
                    .ToList();
            }
            return null;
        }
        catch { return null; }
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimEnd('/');
    }
}
