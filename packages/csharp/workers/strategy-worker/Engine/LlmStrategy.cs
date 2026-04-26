using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// LLM 策略 — 將 K 線 + 技術指標摘要送給 LLM，請它判斷 buy/sell/hold。
///
/// 路由設計：所有 LLM 呼叫一律透過 broker 的 /api/v1/llm-proxy/chat 端點，
/// 而非直接連 Gemini / OpenAI。這樣 broker 端的 MeteredLlmProxyService 才能
/// 把每次呼叫記到儀表板的 LLM Proxy 分頁，符合「集中治理」設計原則。
/// </summary>
public class LlmStrategy : IStrategy
{
    private readonly HttpClient _http;
    private readonly ILogger<LlmStrategy> _logger;
    private readonly string _brokerUrl;
    private readonly string _model;

    // ── Circuit breaker（防止 backtest 數百次 LLM 呼叫造成 worker 超時斷線）
    // 連續失敗 >= _breakerThreshold 或單一 session 呼叫 > _breakerCallCap 後，
    // 接下來的 Evaluate 全部 fallback 到 composite rule-based 訊號。
    private int _consecutiveFailures = 0;
    private int _callCount = 0;
    private DateTime _breakerResetAt = DateTime.UtcNow;
    private const int BreakerFailureThreshold = 3;
    private const int BreakerCallCap = 50;               // 同一 minute 最多 50 次 LLM 呼叫
    private const int BreakerCooldownSeconds = 60;       // cooldown 一分鐘
    private static readonly TimeSpan PerCallTimeout = TimeSpan.FromSeconds(10);

    public string Name => "llm";

    public LlmStrategy(
        HttpClient http,
        ILogger<LlmStrategy> logger,
        string brokerUrl,
        string model = "gemini-2.0-flash")
    {
        _http      = http;
        _logger    = logger;
        _brokerUrl = brokerUrl.TrimEnd('/');
        _model     = model;
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        // Circuit breaker：若處於 cooldown，直接 fallback，不碰 LLM
        if (IsBreakerOpen(out var breakerReason))
        {
            var fallback = ComputeCompositeFallback(bars, config);
            fallback.Reason = $"[LLM breaker open: {breakerReason}] fallback → composite rule-based: {fallback.Reason}";
            return fallback;
        }

        _callCount++;

        // 同步包裝非同步呼叫（handler 合約為同步）。加 PerCallTimeout 限制單次等 LLM 的時間。
        try
        {
            using var cts = new CancellationTokenSource(PerCallTimeout);
            var result = EvaluateAsync(bars, config, cts.Token).GetAwaiter().GetResult();
            _consecutiveFailures = 0;  // 成功清零
            return result;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logger.LogWarning(ex, "LLM strategy failed ({Consec}/{Thresh}), fallback to composite", _consecutiveFailures, BreakerFailureThreshold);
            var fallback = ComputeCompositeFallback(bars, config);
            fallback.Reason = $"[LLM error #{_consecutiveFailures}: {Truncate(ex.Message, 60)}] fallback → composite: {fallback.Reason}";
            return fallback;
        }
    }

    private bool IsBreakerOpen(out string reason)
    {
        // 如果過了 cooldown 視窗，重置計數
        if ((DateTime.UtcNow - _breakerResetAt).TotalSeconds >= BreakerCooldownSeconds)
        {
            _breakerResetAt = DateTime.UtcNow;
            _callCount = 0;
            _consecutiveFailures = 0;
        }

        if (_consecutiveFailures >= BreakerFailureThreshold)
        {
            reason = $"{_consecutiveFailures} consecutive failures";
            return true;
        }
        if (_callCount >= BreakerCallCap)
        {
            reason = $"call cap {BreakerCallCap}/min reached — probably in backtest loop";
            return true;
        }
        reason = "";
        return false;
    }

    private static Signal ComputeCompositeFallback(List<BarData> bars, StrategyConfig config)
    {
        // 用既有 rule-based 策略的等權投票當作 fallback（跟 CompositeStrategy 同邏輯）
        var sig = CompositeStrategy.Default().Evaluate(bars, config);
        sig.Strategy = "llm";  // 對外仍掛「llm」名，但 Reason 會說明 fallback
        return sig;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    private async Task<Signal> EvaluateAsync(List<BarData> bars, StrategyConfig config, CancellationToken ct)
    {
        // 先跑規則策略取得指標數據
        var smaResult  = new SmaCrossStrategy().Evaluate(bars, config);
        var rsiResult  = new RsiStrategy().Evaluate(bars, config);
        var macdResult = new MacdStrategy().Evaluate(bars, config);

        // 準備最近 10 根 K 線摘要
        var recentBars = bars.TakeLast(10).Select(b =>
            $"{b.OpenTime:yyyy-MM-dd}: O={b.Open:F2} H={b.High:F2} L={b.Low:F2} C={b.Close:F2} V={b.Volume:F0}"
        );

        var jsonFormat = @"{""action"": ""buy|sell|hold"", ""confidence"": 0.0-1.0, ""reason"": ""brief explanation""}";
        var prompt = $"""
            You are a quantitative trading analyst. Analyze the following market data and provide a trading signal.

            Symbol: {config.Symbol}
            Exchange: {config.Exchange}
            Interval: {config.Interval}

            Recent price bars (last 10):
            {string.Join("\n", recentBars)}

            Technical indicators:
            - SMA Cross: {smaResult.Action} (confidence: {smaResult.Confidence:P0}) — {smaResult.Reason}
            - RSI: {rsiResult.Action} (confidence: {rsiResult.Confidence:P0}) — {rsiResult.Reason}
            - MACD: {macdResult.Action} (confidence: {macdResult.Confidence:P0}) — {macdResult.Reason}

            Respond ONLY with this exact JSON, no markdown, no code blocks, no extra text:
            {jsonFormat}
            """;

        // 統一走 broker 的 /api/v1/llm-proxy/chat（OpenAI-compatible body shape）
        var requestBody = JsonSerializer.Serialize(new
        {
            model    = _model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.3,
            max_tokens  = 200,
        });

        var resp = await _http.PostAsync(
            $"{_brokerUrl}/api/v1/llm-proxy/chat",
            new StringContent(requestBody, Encoding.UTF8, "application/json"),
            ct);
        resp.EnsureSuccessStatusCode();
        var respJson = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(respJson);

        // broker wrapper: { success, data: { content, model, eval_count, ... } }
        if (!doc.RootElement.TryGetProperty("data", out var dataEl))
            throw new InvalidOperationException("broker response missing data: " + respJson);
        var content = dataEl.TryGetProperty("content", out var contentProp)
            ? (contentProp.GetString() ?? "")
            : "";

        // 嘗試從回應中解析 JSON
        var action     = "hold";
        var confidence = 0.5m;
        var reason     = content;

        try
        {
            // 找到 JSON 部分
            var jsonStart = content.IndexOf('{');
            var jsonEnd   = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var llmJson = content[jsonStart..(jsonEnd + 1)];
                var llmDoc  = JsonDocument.Parse(llmJson);
                var root    = llmDoc.RootElement;

                action     = root.TryGetProperty("action",     out var a) ? a.GetString() ?? "hold" : "hold";
                confidence = root.TryGetProperty("confidence",  out var c) ? c.GetDecimal()          : 0.5m;
                reason     = root.TryGetProperty("reason",      out var r) ? r.GetString() ?? ""     : content;
            }
        }
        catch
        {
            _logger.LogDebug("Failed to parse LLM JSON response, using raw content");
        }

        // 合併所有指標
        var indicators = new Dictionary<string, decimal>
        {
            ["price"] = bars[^1].Close,
        };
        foreach (var kv in smaResult.Indicators)  indicators[$"sma.{kv.Key}"]  = kv.Value;
        foreach (var kv in rsiResult.Indicators)   indicators[$"rsi.{kv.Key}"]  = kv.Value;
        foreach (var kv in macdResult.Indicators)  indicators[$"macd.{kv.Key}"] = kv.Value;

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = $"[LLM] {reason}",
            Interval   = config.Interval,
            Indicators = indicators,
        };
    }

    private static Signal HoldSignal(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "llm", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
