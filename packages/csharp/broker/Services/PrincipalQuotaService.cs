using System.Collections.Concurrent;
using System.Text.Json;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// H1 — Per-principal LLM token + dispatch quota（in-memory、daily reset UTC midnight）
///
/// 為什麼需要：
/// - 既有 ACL 是 binary（allow / deny capability）、沒「上限」概念
/// - 攻擊者拿到 prn_dc_bot 即使被 ACL 擋掉 trading.order、還能一秒打 N 次 strategy.signal
///   把 LLM token 燒爆（Claude API key 花 USD）
/// - bot rate limit (P2) 是 per-Discord-user、不是 per-broker-principal — 互補但不重疊
///
/// 設計：
/// - 兩個 quota 維度：daily_llm_tokens（input+output 加總）、daily_dispatches（總呼叫數）
/// - 預設值來自 appsettings `Quota:Default:LlmTokensPerDay` / `Quota:Default:DispatchesPerDay`
///   per-principal override 可從 env `QUOTA_OVERRIDE_<principalId>=tokens,dispatches` 讀
/// - in-memory 累計、每天 UTC 00:00 重置（lazy reset：每次 check 時看當前日期）
/// - **soft-mode 預設**：超 quota 只 log warning + 寫 audit、不真擋下、不破壞 demo 流程
///   appsettings `Quota:Enforce=true` 才真擋（回 false→ caller 拒絕）
///
/// LLM hook：QuotaEnforcedLlmProxyService 包在 MeteredLlmProxyService 外、ChatAsync 後 record。
/// Dispatch hook：暫不接（避免改 InProcessDispatcher）— LLM 才是燒錢大宗、優先擋這個。
/// </summary>
public interface IPrincipalQuotaService
{
    bool EnforceMode { get; }
    long DefaultDailyLlmTokens { get; }
    long DefaultDailyDispatches { get; }

    /// <summary>記錄 LLM token 用量（input+output 合計）。回 (allowed_after, current, limit)。</summary>
    (bool Allowed, long Current, long Limit) RecordLlmUsage(string principalId, int tokens);

    /// <summary>記錄一次 dispatch（不分 capability）。回 (allowed_after, current, limit)。</summary>
    (bool Allowed, long Current, long Limit) RecordDispatch(string principalId);

    /// <summary>給 dashboard：snapshot 全部 principal 的當日用量。</summary>
    IReadOnlyDictionary<string, QuotaSnapshot> Snapshot();
}

public class QuotaSnapshot
{
    public long LlmTokensUsed { get; set; }
    public long LlmTokensLimit { get; set; }
    public long DispatchesUsed { get; set; }
    public long DispatchesLimit { get; set; }
    public DateTime DayUtc { get; set; }
    public bool LlmOverLimit => LlmTokensUsed > LlmTokensLimit;
    public bool DispatchOverLimit => DispatchesUsed > DispatchesLimit;
}

public sealed class PrincipalQuotaService : IPrincipalQuotaService
{
    private readonly long _defaultLlm;
    private readonly long _defaultDispatch;
    private readonly bool _enforce;
    private readonly Dictionary<string, (long Llm, long Dispatch)> _overrides = new();
    private readonly ConcurrentDictionary<string, PrincipalCounter> _counters = new();
    private readonly IAuditService _audit;
    private readonly ILogger<PrincipalQuotaService> _logger;

    public PrincipalQuotaService(IConfiguration cfg, IAuditService audit,
        ILogger<PrincipalQuotaService> logger)
    {
        _audit = audit; _logger = logger;
        _defaultLlm      = cfg.GetValue("Quota:Default:LlmTokensPerDay", 200_000L);
        _defaultDispatch = cfg.GetValue("Quota:Default:DispatchesPerDay", 5_000L);
        _enforce         = cfg.GetValue("Quota:Enforce", false);

        // env override：QUOTA_OVERRIDE_prn_dc_bot=50000,500
        foreach (var key in Environment.GetEnvironmentVariables().Keys)
        {
            var ks = key?.ToString() ?? "";
            if (!ks.StartsWith("QUOTA_OVERRIDE_", StringComparison.OrdinalIgnoreCase)) continue;
            var pid = ks.Substring("QUOTA_OVERRIDE_".Length);
            var v = Environment.GetEnvironmentVariable(ks) ?? "";
            var parts = v.Split(',', 2);
            if (parts.Length == 2
                && long.TryParse(parts[0].Trim(), out var llm)
                && long.TryParse(parts[1].Trim(), out var disp))
            {
                _overrides[pid] = (llm, disp);
            }
        }
    }

    public bool EnforceMode => _enforce;
    public long DefaultDailyLlmTokens => _defaultLlm;
    public long DefaultDailyDispatches => _defaultDispatch;

    public (bool Allowed, long Current, long Limit) RecordLlmUsage(string principalId, int tokens)
    {
        var pid = string.IsNullOrWhiteSpace(principalId) ? "(unknown)" : principalId;
        var counter = GetCounter(pid);
        long current;
        lock (counter)
        {
            counter.LlmTokensUsed += Math.Max(0, tokens);
            current = counter.LlmTokensUsed;
        }
        var limit = GetLlmLimit(pid);
        var wasOver = current > limit;
        if (wasOver) Alert(pid, "LLM_TOKEN_QUOTA", current, limit);
        return (!_enforce || !wasOver, current, limit);
    }

    public (bool Allowed, long Current, long Limit) RecordDispatch(string principalId)
    {
        var pid = string.IsNullOrWhiteSpace(principalId) ? "(unknown)" : principalId;
        var counter = GetCounter(pid);
        long current;
        lock (counter)
        {
            counter.DispatchesUsed++;
            current = counter.DispatchesUsed;
        }
        var limit = GetDispatchLimit(pid);
        var wasOver = current > limit;
        if (wasOver) Alert(pid, "DISPATCH_QUOTA", current, limit);
        return (!_enforce || !wasOver, current, limit);
    }

    public IReadOnlyDictionary<string, QuotaSnapshot> Snapshot()
    {
        var today = DateTime.UtcNow.Date;
        var result = new Dictionary<string, QuotaSnapshot>();
        foreach (var (pid, c) in _counters)
        {
            lock (c)
            {
                if (c.DayUtc != today) continue;   // 過期 counter 略過
                result[pid] = new QuotaSnapshot {
                    LlmTokensUsed = c.LlmTokensUsed,
                    LlmTokensLimit = GetLlmLimit(pid),
                    DispatchesUsed = c.DispatchesUsed,
                    DispatchesLimit = GetDispatchLimit(pid),
                    DayUtc = c.DayUtc,
                };
            }
        }
        return result;
    }

    private PrincipalCounter GetCounter(string principalId)
    {
        var counter = _counters.GetOrAdd(principalId, _ => new PrincipalCounter { DayUtc = DateTime.UtcNow.Date });
        var today = DateTime.UtcNow.Date;
        lock (counter)
        {
            if (counter.DayUtc != today)
            {
                counter.DayUtc = today;
                counter.LlmTokensUsed = 0;
                counter.DispatchesUsed = 0;
            }
        }
        return counter;
    }

    private long GetLlmLimit(string pid)
        => _overrides.TryGetValue(pid, out var v) ? v.Llm : _defaultLlm;

    private long GetDispatchLimit(string pid)
        => _overrides.TryGetValue(pid, out var v) ? v.Dispatch : _defaultDispatch;

    private void Alert(string pid, string type, long current, long limit)
    {
        _logger.LogWarning("Quota OVER: principal={Pid} type={T} current={Cur} limit={Lim} enforce={E}",
            pid, type, current, limit, _enforce);
        try
        {
            var traceId = BrokerCore.IdGen.New("trc");
            _audit.RecordEvent(traceId, "QUOTA_EXCEEDED",
                principalId: pid,
                resourceRef: type,
                details: JsonSerializer.Serialize(new {
                    type, current, limit, enforce_mode = _enforce, at = DateTime.UtcNow,
                }));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Quota audit write failed");
        }
    }

    private sealed class PrincipalCounter
    {
        public long LlmTokensUsed;
        public long DispatchesUsed;
        public DateTime DayUtc;
    }
}
