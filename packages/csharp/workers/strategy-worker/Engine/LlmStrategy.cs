using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StrategyWorker.Models;

namespace StrategyWorker.Engine;

/// <summary>
/// LLM 策略 — 將 K 線 + 技術指標摘要送給 LLM，請它判斷 buy/sell/hold。
/// 透過 OpenAI-compatible API（可接 Gemini、Claude、本地 Ollama）。
/// </summary>
public class LlmStrategy : IStrategy
{
    private readonly HttpClient _http;
    private readonly ILogger<LlmStrategy> _logger;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _model;

    public string Name => "llm";

    public LlmStrategy(
        HttpClient http,
        ILogger<LlmStrategy> logger,
        string baseUrl,
        string apiKey,
        string model = "gemini-2.0-flash")
    {
        _http    = http;
        _logger  = logger;
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey  = apiKey;
        _model   = model;
    }

    public Signal Evaluate(List<BarData> bars, StrategyConfig config)
    {
        // 同步包裝非同步呼叫（handler 合約為同步）
        try
        {
            return EvaluateAsync(bars, config, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM strategy failed, returning hold");
            return HoldSignal(config, $"LLM error: {ex.Message}");
        }
    }

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

        var requestBody = JsonSerializer.Serialize(new
        {
            model    = _model,
            messages = new[] { new { role = "user", content = prompt } },
            temperature = 0.3,
            max_tokens  = 200,
        });

        string json;
        if (_baseUrl.Contains("generativelanguage.googleapis.com"))
        {
            // Gemini 原生 API
            var geminiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";
            var geminiBody = JsonSerializer.Serialize(new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new { temperature = 0.1, maxOutputTokens = 1024, responseMimeType = "application/json", thinkingConfig = new { thinkingBudget = 0 } }
            });
            var geminiResp = await _http.PostAsync(geminiUrl,
                new StringContent(geminiBody, Encoding.UTF8, "application/json"), ct);
            geminiResp.EnsureSuccessStatusCode();
            json = await geminiResp.Content.ReadAsStringAsync(ct);
            var geminiDoc = JsonDocument.Parse(json);
            var content_raw = geminiDoc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";
            _logger.LogInformation("Gemini raw response: {Raw}", content_raw);
            json = content_raw;
        }
        else
        {
            // OpenAI-compatible API
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
            {
                Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrEmpty(_apiKey))
                request.Headers.Add("Authorization", $"Bearer {_apiKey}");
            var resp = await _http.SendAsync(request, ct);
            resp.EnsureSuccessStatusCode();
            var respJson = await resp.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(respJson);
            json = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";
        }

        var content = json;

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
