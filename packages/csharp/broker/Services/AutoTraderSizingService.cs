using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Broker.Services;

/// <summary>
/// D1 Phase 1 — 從 AutoTraderService 抽出來的「sizing 決策」單一職責服務。
///
/// 集中今天 commit 加進去的所有 sizing 邏輯：
///   - C2 confidence-aware sizing (commit 2ed655f)
///   - C2 follow-up Kelly fraction integration + 6h cache (commit 1138173)
///   - C1 slippage backoff state（commit 668d185；slippage threshold 觀察留 AutoTrader、
///     gate 跟 cache 搬到這裡）
///
/// 為什麼抽：AutoTraderService 2569 行、太多 concern 混在一起。Sizing 是最乾淨的切口——
/// 對外面沒副作用（不下單、不寫 DB）、只「給條件、回 qty」。Pure function 為主、
/// 唯一 state 是兩個 cache（Kelly + slippage backoff）。
///
/// 不動的：
///   - dynamic risk sizing（balance × _dynamicRiskPct / SL%）還在 AutoTraderService 內、
///     因為它要拿 PortfolioRisk / OpenPositions context、抽出來反而拉一堆依賴
///   - slippage observation（讀 order response、寫 AddLog）還在 AutoTrader、因為 AddLog
///     是 per-watch log buffer、AutoTrader 有的 state。只把 ArmBackoff 暴露成 service method
///
/// Test：AutoTraderService.ApplyAdaptiveSizing / NormalizeKellyToFactor / ShouldSkipForSlippageBackoff
/// 全是 internal static、本 commit 搬過來時保留簽名、test 改用新類別。
/// </summary>
public class AutoTraderSizingService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoTraderSizingService> _logger;

    // ── Config（建構時讀 env、跟 AutoTraderService 同步） ────────────────
    private readonly bool _confidenceSizingEnabled;
    private readonly decimal _confidenceSizingFloor;
    private readonly bool _kellySizingEnabled;
    private readonly int _kellyCacheHours;
    private readonly int _slippageBackoffMin;

    // ── State ─────────────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, (decimal EffectiveFraction, DateTime ComputedAt)> _kellyCache = new();
    private readonly ConcurrentDictionary<string, byte> _kellyRefreshing = new();
    private readonly ConcurrentDictionary<string, DateTime> _slippageBackoffUntil = new();

    public AutoTraderSizingService(IServiceProvider sp, ILogger<AutoTraderSizingService> logger)
    {
        _serviceProvider = sp;
        _logger = logger;

        _confidenceSizingEnabled = string.Equals(
            Environment.GetEnvironmentVariable("AUTOTRADER_CONFIDENCE_SIZING_ENABLED") ?? "false",
            "true", StringComparison.OrdinalIgnoreCase);
        _confidenceSizingFloor = ParsePctEnv("AUTOTRADER_CONFIDENCE_SIZING_FLOOR", 0.3m, 0.05m, 1m);

        _kellySizingEnabled = string.Equals(
            Environment.GetEnvironmentVariable("AUTOTRADER_KELLY_SIZING_ENABLED") ?? "false",
            "true", StringComparison.OrdinalIgnoreCase);
        _kellyCacheHours = ParseIntEnv("AUTOTRADER_KELLY_CACHE_HOURS", 6, 1, 168);

        _slippageBackoffMin = ParseIntEnv("AUTOTRADER_SLIPPAGE_BACKOFF_MIN", 30, 0, 720);
    }

    // ── 對外 API（給 AutoTraderService 用） ──────────────────────────────

    public bool ConfidenceEnabled => _confidenceSizingEnabled;
    public bool KellyEnabled => _kellySizingEnabled;
    public int SlippageBackoffMin => _slippageBackoffMin;

    /// <summary>主入口：給 open/scale-in signal 算最終 qty。pure 不下單。</summary>
    public (decimal SizedQty, string Tags) ApplyForSignal(
        decimal baseQty, decimal confidence,
        string exchange, string symbol, string strategy)
    {
        if (!_confidenceSizingEnabled && !_kellySizingEnabled) return (baseQty, "");
        var kellyFactor = GetKellyFactorOrTrigger(exchange, symbol, strategy);
        var sized = ApplyAdaptiveSizing(
            baseQty, confidence, _confidenceSizingFloor, kellyFactor,
            _confidenceSizingEnabled, _kellySizingEnabled);
        var tags = (_confidenceSizingEnabled ? $"conf={confidence:P0} " : "")
                 + (_kellySizingEnabled ? $"kelly={kellyFactor:P0}" : "");
        return (sized, tags.Trim());
    }

    /// <summary>slippage backoff gate — 給 AutoTrader 在下單前查。</summary>
    public bool IsInSlippageBackoff(string exchange, string symbol, out int remainMinutes)
        => ShouldSkipForSlippageBackoff(_slippageBackoffUntil, $"{exchange}:{symbol}", DateTime.UtcNow, out remainMinutes);

    /// <summary>觀察到 slippage 超 threshold 後、AutoTrader call 這裡 arm cooldown。</summary>
    public void ArmSlippageBackoff(string exchange, string symbol)
    {
        if (_slippageBackoffMin <= 0) return;
        _slippageBackoffUntil[$"{exchange}:{symbol}"] = DateTime.UtcNow.AddMinutes(_slippageBackoffMin);
    }

    // ── Internal helpers（保留 internal static 簽名、test 可直接驗） ─────

    /// <summary>
    /// Adaptive sizing pure function（原 AutoTraderService.ApplyAdaptiveSizing）。
    /// factor = (confEnabled ? max(floor, clamp(conf, 0, 1)) : 1) × (kellyEnabled ? clamp(kelly, 0, 1) : 1)
    /// 兩個 multiplier 互乘、不平均（兩條都偏弱應該更積極縮）。
    /// </summary>
    internal static decimal ApplyAdaptiveSizing(
        decimal baseQty, decimal confidence, decimal confidenceFloor,
        decimal kellyFraction, bool confidenceEnabled, bool kellyEnabled)
    {
        var factor = 1m;
        if (confidenceEnabled)
        {
            var conf = Math.Max(0m, Math.Min(1m, confidence));
            factor *= Math.Max(confidenceFloor, conf);
        }
        if (kellyEnabled)
        {
            factor *= Math.Clamp(kellyFraction, 0m, 1m);
        }
        return baseQty * factor;
    }

    /// <summary>把 KellyPositionSizingService.EffectiveFraction [0, 0.25] 攤平到 [0, 1]。</summary>
    internal static decimal NormalizeKellyToFactor(decimal effectiveFraction)
        => Math.Min(1m, Math.Max(0m, effectiveFraction) / 0.25m);

    /// <summary>Slippage backoff gate pure check（test 可直接灌 map / now）。</summary>
    internal static bool ShouldSkipForSlippageBackoff(
        ConcurrentDictionary<string, DateTime> backoffUntil, string key, DateTime nowUtc,
        out int remainMinutes)
    {
        remainMinutes = 0;
        if (!backoffUntil.TryGetValue(key, out var until)) return false;
        if (nowUtc >= until) return false;
        remainMinutes = (int)Math.Ceiling((until - nowUtc).TotalMinutes);
        return true;
    }

    // ── Kelly cache & lazy refresh ────────────────────────────────────

    /// <summary>
    /// 拿 cached Kelly fraction、normalize 到 [0, 1] 給 ApplyAdaptiveSizing。
    /// cache miss / stale → fire-and-forget refresh、回 1.0（保守不縮）。
    /// </summary>
    internal decimal GetKellyFactorOrTrigger(string exchange, string symbol, string strategy)
    {
        if (!_kellySizingEnabled) return 1m;
        var key = $"{exchange}:{symbol}:{strategy}";
        if (_kellyCache.TryGetValue(key, out var entry) &&
            (DateTime.UtcNow - entry.ComputedAt).TotalHours < _kellyCacheHours)
        {
            return NormalizeKellyToFactor(entry.EffectiveFraction);
        }
        TriggerKellyRefresh(exchange, symbol, strategy);
        return 1m;
    }

    private void TriggerKellyRefresh(string exchange, string symbol, string strategy)
    {
        var key = $"{exchange}:{symbol}:{strategy}";
        if (!_kellyRefreshing.TryAdd(key, 0)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var kelly = _serviceProvider.GetService<KellyPositionSizingService>();
                if (kelly == null) return;
                var sugg = await kelly.SuggestAsync(strategy, symbol, capital: 1000m, fraction: 0.5m);
                if (sugg.Success)
                {
                    _kellyCache[key] = (sugg.EffectiveFraction, DateTime.UtcNow);
                    _logger.LogInformation(
                        "Kelly cache updated {Key}: effective={Frac:F4} (normalized factor={Factor:F2})",
                        key, sugg.EffectiveFraction, NormalizeKellyToFactor(sugg.EffectiveFraction));
                }
                else
                {
                    _logger.LogDebug("Kelly compute no suggestion for {Key}: {Err}", key, sugg.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Kelly refresh failed for {Key}", key);
            }
            finally
            {
                _kellyRefreshing.TryRemove(key, out _);
            }
        });
    }

    // ── Env parsing helpers（複製自 AutoTraderService 以避免反向依賴） ────

    private static decimal ParsePctEnv(string envName, decimal defaultValue, decimal min, decimal max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrEmpty(raw)) return defaultValue;
        if (decimal.TryParse(raw, out var v)) return Math.Clamp(v, min, max);
        return defaultValue;
    }

    private static int ParseIntEnv(string envName, int defaultValue, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrEmpty(raw)) return defaultValue;
        if (int.TryParse(raw, out var v)) return Math.Clamp(v, min, max);
        return defaultValue;
    }
}
