using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// LLM 仲裁者：當 ensemble 成員意見分歧時，把所有成員的 action/confidence/reason
/// 餵給 Gemini，請它判斷誰的 reasoning 比較有道理、給出最終 buy/sell/hold。
///
/// 走 broker 的 /api/v1/llm-proxy/chat 端點（跟 LlmStrategy 同樣集中代理），
/// 所以呼叫會被 MeteredLlmProxyService 計費 + 記錄到儀表板。
///
/// 失敗時回 null，讓 ensemble fallback 到加權投票——絕不拋例外干擾主流程。
///
/// Cost 控制：跟 LlmStrategy 共享 circuit breaker 邏輯精神（連續失敗 / 高頻呼叫
/// 自動 disable）但這裡更輕——因為仲裁本來就只在「衝突」時才呼叫，頻率天然受限。
/// </summary>
public class LlmEnsembleArbitrator : IEnsembleArbitrator
{
    private readonly HttpClient _http;
    private readonly ILogger<LlmEnsembleArbitrator> _logger;
    private readonly string _brokerUrl;
    private readonly string _model;
    private readonly decimal _threshold;
    private static readonly TimeSpan PerCallTimeout = TimeSpan.FromSeconds(8);

    public decimal Threshold => _threshold;

    public LlmEnsembleArbitrator(
        HttpClient http,
        ILogger<LlmEnsembleArbitrator> logger,
        string brokerUrl,
        string model = "gemini-2.0-flash",
        decimal threshold = 0.6m)
    {
        _http = http;
        _logger = logger;
        _brokerUrl = brokerUrl.TrimEnd('/');
        _model = model;
        _threshold = threshold;
    }

    public Signal? Arbitrate(
        IReadOnlyList<Signal> constituentSignals,
        decimal agreementRatio,
        List<BarData> bars,
        StrategyConfig config)
    {
        if (constituentSignals.Count == 0) return null;
        try
        {
            using var cts = new CancellationTokenSource(PerCallTimeout);
            return ArbitrateAsync(constituentSignals, agreementRatio, bars, config, cts.Token)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM arbitrator failed for {Symbol}, fallback to weighted vote", config.Symbol);
            return null;
        }
    }

    private async Task<Signal?> ArbitrateAsync(
        IReadOnlyList<Signal> sigs,
        decimal agreementRatio,
        List<BarData> bars,
        StrategyConfig config,
        CancellationToken ct)
    {
        var lastBar = bars[^1];
        var summaries = string.Join("\n", sigs.Select(s =>
            $"- {s.Strategy} → {s.Action.ToUpperInvariant()} (confidence: {s.Confidence:P0}): {Truncate(s.Reason, 200)}"));

        var jsonFormat = @"{""action"": ""buy|sell|hold"", ""confidence"": 0.0-1.0, ""rationale"": ""brief""}";
        var prompt = $"""
            You are arbitrating between conflicting trading signals from multiple algorithmic strategies.
            Pick the most defensible final action by weighing the reasoning quality, not just vote count.

            Symbol: {config.Symbol} ({config.Exchange}) · {config.Interval}
            Last close: {lastBar.Close:F4}
            Member agreement ratio: {agreementRatio:P0} (low = high disagreement)

            Member signals:
            {summaries}

            Decision rules:
            - If one member's reasoning explicitly mentions a regime (trending/range/squeeze) that fits current
              conditions, prefer that one even if outvoted.
            - If reasoning sounds generic / boilerplate across all members, prefer hold.
            - confidence ≤ 0.5 means low conviction — output hold and reduce position risk.

            Respond ONLY with this exact JSON, no markdown, no code fences, no commentary:
            {jsonFormat}
            """;

        var requestBody = JsonSerializer.Serialize(new
        {
            model = _model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.2,
            max_tokens = 220,
        });

        var resp = await _http.PostAsync(
            $"{_brokerUrl}/api/v1/llm-proxy/chat",
            new StringContent(requestBody, Encoding.UTF8, "application/json"),
            ct);
        resp.EnsureSuccessStatusCode();
        var respJson = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(respJson);

        if (!doc.RootElement.TryGetProperty("data", out var dataEl)) return null;
        var content = dataEl.TryGetProperty("content", out var cp) ? (cp.GetString() ?? "") : "";

        var jsonStart = content.IndexOf('{');
        var jsonEnd = content.LastIndexOf('}');
        if (jsonStart < 0 || jsonEnd <= jsonStart) return null;

        try
        {
            var llmDoc = JsonDocument.Parse(content[jsonStart..(jsonEnd + 1)]);
            var root = llmDoc.RootElement;
            var action = root.TryGetProperty("action", out var a) ? a.GetString()?.ToLowerInvariant() : "hold";
            if (action != "buy" && action != "sell" && action != "hold") action = "hold";
            var confidence = root.TryGetProperty("confidence", out var cf) ? cf.GetDecimal() : 0.5m;
            var rationale = root.TryGetProperty("rationale", out var r) ? r.GetString() ?? "" : "";

            return new Signal
            {
                SignalId = $"arb-{Guid.NewGuid():N}"[..16],
                Strategy = "ensemble",
                Symbol = config.Symbol,
                Exchange = config.Exchange,
                Action = action!,
                Confidence = Math.Round(Math.Clamp(confidence, 0m, 1m), 2),
                Reason = $"LLM 仲裁（agreement {agreementRatio:P0}）：{rationale}",
                Interval = config.Interval,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
