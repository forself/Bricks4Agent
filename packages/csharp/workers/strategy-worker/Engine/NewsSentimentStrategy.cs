using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// 新聞情緒分析策略 — 用 LLM 分析財經新聞標題，判斷市場情緒。
///
/// 流程：
/// 1. 從免費新聞 API 抓取最近的標題
/// 2. 結合 K 線技術指標摘要
/// 3. 送給 LLM 做情緒判斷
/// </summary>
public class NewsSentimentStrategy : IStrategy
{
    private readonly HttpClient _http;
    private readonly ILogger<NewsSentimentStrategy> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public string Name => "news_sentiment";

    public NewsSentimentStrategy(
        HttpClient http,
        ILogger<NewsSentimentStrategy> logger,
        string geminiApiKey,
        string model = "gemini-2.5-flash")
    {
        _http   = http;
        _logger = logger;
        _apiKey = geminiApiKey;
        _model  = model;
    }

    // Circuit breaker（同 LlmStrategy 的邏輯；防 backtest 迴圈拖垮 worker）
    private int _consecutiveFailures = 0;
    private int _callCount = 0;
    private DateTime _breakerResetAt = DateTime.UtcNow;
    private const int BreakerFailureThreshold = 3;
    private const int BreakerCallCap = 30;  // News 更慢（RSS + LLM），更嚴格
    private const int BreakerCooldownSeconds = 60;
    private static readonly TimeSpan PerCallTimeout = TimeSpan.FromSeconds(15);

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        // Circuit breaker
        if ((DateTime.UtcNow - _breakerResetAt).TotalSeconds >= BreakerCooldownSeconds)
        {
            _breakerResetAt = DateTime.UtcNow;
            _callCount = 0;
            _consecutiveFailures = 0;
        }
        if (_consecutiveFailures >= BreakerFailureThreshold || _callCount >= BreakerCallCap)
        {
            var fb = CompositeStrategy.Default().Evaluate(bars, config);
            fb.Strategy = "news_sentiment";
            fb.Reason = $"[news breaker open] fallback → composite: {fb.Reason}";
            return fb;
        }
        _callCount++;

        try
        {
            using var cts = new CancellationTokenSource(PerCallTimeout);
            var result = EvaluateAsync(bars, config, cts.Token).GetAwaiter().GetResult();
            _consecutiveFailures = 0;
            return result;
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logger.LogWarning(ex, "News sentiment failed ({Consec}/{Thresh})", _consecutiveFailures, BreakerFailureThreshold);
            var fb = CompositeStrategy.Default().Evaluate(bars, config);
            fb.Strategy = "news_sentiment";
            fb.Reason = $"[news error: {(ex.Message.Length > 60 ? ex.Message[..60] + "…" : ex.Message)}] fallback → composite";
            return fb;
        }
    }

    private async Task<Signal> EvaluateAsync(List<BarData> bars, StrategyConfig config, CancellationToken ct)
    {
        // Step 1: 抓新聞標題（用免費的 Google News RSS 或 DuckDuckGo）
        var headlines = await FetchHeadlinesAsync(config.Symbol, ct);

        // Step 2: 技術指標摘要
        var sma = new SmaCrossStrategy().Evaluate(bars, config);
        var rsi = new RsiStrategy().Evaluate(bars, config);
        var price = bars.Count > 0 ? bars[^1].Close : 0;

        // Step 3: 送給 LLM
        var jsonFormat = @"{""action"": ""buy|sell|hold"", ""confidence"": 0.0-1.0, ""sentiment"": ""bullish|bearish|neutral"", ""reason"": ""brief explanation""}";
        var prompt = $"""
            You are a financial analyst. Analyze the market sentiment for {config.Symbol} based on recent news and technical data.

            Recent news headlines:
            {string.Join("\n", headlines.Take(10).Select(h => $"- {h}"))}

            Technical summary:
            - Current price: ${price:F2}
            - SMA trend: {sma.Action} ({sma.Reason})
            - RSI: {rsi.Action} ({rsi.Reason})

            Respond ONLY with this exact JSON, no other text:
            {jsonFormat}
            """;

        var geminiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
        var body = JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { temperature = 0.1, maxOutputTokens = 256, responseMimeType = "application/json", thinkingConfig = new { thinkingBudget = 0 } }
        });

        var resp = await _http.PostAsync(geminiUrl, new StringContent(body, Encoding.UTF8, "application/json"), ct);
        resp.EnsureSuccessStatusCode();

        var respJson = await resp.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(respJson);
        var content = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";

        // 解析 LLM JSON
        var action = "hold";
        var confidence = 0.5m;
        var sentiment = "neutral";
        var reason = content;

        try
        {
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var llmDoc = JsonDocument.Parse(content[jsonStart..(jsonEnd + 1)]);
                var root = llmDoc.RootElement;
                action     = root.TryGetProperty("action",     out var a) ? a.GetString() ?? "hold" : "hold";
                confidence = root.TryGetProperty("confidence",  out var c) ? c.GetDecimal() : 0.5m;
                sentiment  = root.TryGetProperty("sentiment",   out var s) ? s.GetString() ?? "neutral" : "neutral";
                reason     = root.TryGetProperty("reason",      out var r) ? r.GetString() ?? "" : content;
            }
        }
        catch { }

        var indicators = new Dictionary<string, decimal>
        {
            ["price"] = price,
            ["headlines_count"] = headlines.Count,
        };
        foreach (var kv in sma.Indicators) indicators[$"sma.{kv.Key}"] = kv.Value;
        foreach (var kv in rsi.Indicators) indicators[$"rsi.{kv.Key}"] = kv.Value;

        return new Signal
        {
            SignalId   = $"sig-{Guid.NewGuid():N}"[..16],
            Strategy   = Name,
            Symbol     = config.Symbol,
            Exchange   = config.Exchange,
            Action     = action,
            Confidence = Math.Round(confidence, 2),
            Reason     = $"[Sentiment: {sentiment}] {reason}",
            Interval   = config.Interval,
            Indicators = indicators,
        };
    }

    /// <summary>從 Google News RSS 抓取新聞標題。</summary>
    private async Task<List<string>> FetchHeadlinesAsync(string symbol, CancellationToken ct)
    {
        var headlines = new List<string>();
        try
        {
            // Google News RSS (免費、不需 API key)
            var url = $"https://news.google.com/rss/search?q={Uri.EscapeDataString(symbol + " stock market")}&hl=en-US&gl=US&ceid=US:en";
            var resp = await _http.GetStringAsync(url, ct);

            // 簡易 XML 解析抓 <title>
            var idx = 0;
            while (idx < resp.Length && headlines.Count < 15)
            {
                var start = resp.IndexOf("<title>", idx);
                if (start < 0) break;
                start += 7;
                var end = resp.IndexOf("</title>", start);
                if (end < 0) break;
                var title = resp[start..end]
                    .Replace("<![CDATA[", "").Replace("]]>", "")
                    .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&#39;", "'")
                    .Trim();
                if (title.Length > 5 && !title.Contains("Google News"))
                    headlines.Add(title);
                idx = end + 8;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch news headlines for {Symbol}", symbol);
            headlines.Add($"(Unable to fetch news for {symbol})");
        }
        return headlines;
    }

    private static Signal HoldSignal(StrategyConfig c, string reason) => new()
    {
        SignalId = $"sig-{Guid.NewGuid():N}"[..16],
        Strategy = "news_sentiment", Symbol = c.Symbol, Exchange = c.Exchange,
        Action = "hold", Confidence = 0, Reason = reason, Interval = c.Interval,
    };
}
