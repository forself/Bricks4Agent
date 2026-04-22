using System.Text.Json;
using System.Text.RegularExpressions;
using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// Strategy Generator Agent — 用 LLM 生成量化策略參數。
///
/// 流程：
///   1. 依「策略家族」和「歷史候選」建 prompt
///   2. 透過 ILlmProxyService.ChatAsync 呼 LLM
///   3. 解析回傳的 JSON（含容錯：markdown fence、前後多餘文字）
///   4. 驗證參數合理性（範圍 + 語意約束如 sma_slow > sma_fast）
///   5. 回傳 StrategyCandidate
///
/// 這是「AI 當研究員」的核心——把「決定參數」這件人類原本做的事交給 LLM。
/// 之後 StrategyResearchLoopService 會把回測結果餵回 LLM 做自我迭代。
/// </summary>
public class StrategyGeneratorService
{
    private readonly ILlmProxyService _llm;
    private readonly ILogger<StrategyGeneratorService> _logger;

    public StrategyGeneratorService(ILlmProxyService llm, ILogger<StrategyGeneratorService> logger)
    {
        _llm = llm;
        _logger = logger;
    }

    public bool IsEnabled => _llm.IsEnabled;

    public async Task<GenerationResult> GenerateAsync(
        string family,
        string symbol,
        List<PriorAttempt>? history,
        CancellationToken ct = default)
    {
        if (!_llm.IsEnabled)
            return GenerationResult.Failure("LLM proxy is not enabled");

        family = family.ToLowerInvariant();
        if (family != "sma_cross" && family != "rsi_oversold")
            return GenerationResult.Failure($"Unsupported family: {family}. Supported: sma_cross, rsi_oversold");

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(family, symbol, history);

        // OpenAI-compatible chat completions payload
        var body = JsonSerializer.SerializeToElement(new
        {
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt },
            },
            temperature = 0.7,  // 一點 variety，但不要太天馬行空
        });

        LlmChatResult chat;
        try
        {
            chat = await _llm.ChatAsync(body, task: null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM chat failed");
            return GenerationResult.Failure($"LLM call failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(chat.Content))
            return GenerationResult.Failure("LLM returned empty content");

        var (parsed, rationale, parseErr) = ExtractJson(chat.Content, family);
        if (parsed == null)
            return GenerationResult.Failure(parseErr ?? "parse failed", chat.Content);

        var (valid, validationErr) = Validate(family, parsed);
        if (!valid)
            return GenerationResult.Failure(validationErr ?? "validation failed", chat.Content);

        return new GenerationResult
        {
            Success = true,
            Family = family,
            Symbol = symbol,
            Parameters = parsed,
            Rationale = rationale,
            RawLlmResponse = chat.Content,
            Model = chat.Model,
            TokensUsed = chat.EvalCount,
        };
    }

    // ── Prompt 構造 ──────────────────────────────────────────────────

    private string BuildSystemPrompt() => """
你是一位量化交易研究員。你的任務是根據給定的條件，為特定股票提出**一個**策略參數配置進行回測。

嚴格規則：
1. 只回傳一個 JSON 物件，不要加任何文字說明、markdown 或程式碼框
2. 參數必須是整數，且在規定範圍內
3. 遵守所有語意約束（例如 sma_slow > sma_fast）
4. 一併提供簡短的 rationale 欄位說明為何選這些值
""";

    private string BuildUserPrompt(string family, string symbol, List<PriorAttempt>? history)
    {
        var schema = family == "sma_cross"
            ? """{"sma_fast": <int 5-30>, "sma_slow": <int 20-100, MUST > sma_fast>, "rationale": "<30 字內說明>"}"""
            : """{"rsi_period": <int 5-30>, "rsi_oversold": <int 15-40>, "rsi_overbought": <int 60-85, MUST > rsi_oversold>, "rationale": "<30 字內說明>"}""";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Symbol: {symbol}");
        sb.AppendLine($"Strategy family: {family}");
        sb.AppendLine($"Required JSON schema:");
        sb.AppendLine(schema);
        sb.AppendLine();

        if (history != null && history.Count > 0)
        {
            sb.AppendLine("此前嘗試過的參數組合與回測結果：");
            int i = 1;
            foreach (var h in history.OrderByDescending(h => h.OutOfSampleSharpe).Take(8))
            {
                var p = string.Join(", ", h.Parameters.Select(kv => $"{kv.Key}={kv.Value}"));
                sb.AppendLine($"  {i}. [{p}] → OOS Sharpe={h.OutOfSampleSharpe:F2}, Return={h.ReturnPct:F1}%, MaxDD={h.MaxDrawdownPct:F1}%, Trades={h.Trades}");
                i++;
            }
            sb.AppendLine();
            sb.AppendLine("根據以上歷史，提出下一個值得嘗試的參數組合。目標是找到 OOS Sharpe 最高、MaxDD 合理（<15%）、交易數 >= 3 筆的組合。");
            sb.AppendLine("請特別避免已嘗試過的參數。如果觀察到某個方向（例如「sma_fast 越大 OOS Sharpe 越高」），往那個方向探索。");
        }
        else
        {
            sb.AppendLine("這是這個 symbol 的第一次嘗試。請從常見且合理的參數開始（不要太極端）。");
        }
        return sb.ToString();
    }

    // ── LLM 回傳解析 ─────────────────────────────────────────────────

    private (Dictionary<string, int>? parsed, string rationale, string? error) ExtractJson(string text, string family)
    {
        // 去掉常見的 markdown 包裝
        var stripped = text.Trim();
        // ```json ... ``` 或 ``` ... ```
        var fenceMatch = Regex.Match(stripped, @"```(?:json)?\s*(?<body>[\s\S]*?)\s*```", RegexOptions.IgnoreCase);
        if (fenceMatch.Success) stripped = fenceMatch.Groups["body"].Value.Trim();

        // 找第一個 '{' 到最後一個 '}' 間的子字串
        var firstBrace = stripped.IndexOf('{');
        var lastBrace = stripped.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
            return (null, "", "LLM 回傳找不到 JSON 物件");

        var jsonStr = stripped.Substring(firstBrace, lastBrace - firstBrace + 1);

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(jsonStr).RootElement;
        }
        catch (Exception ex)
        {
            return (null, "", $"JSON parse failed: {ex.Message}");
        }

        var dict = new Dictionary<string, int>();
        var rationale = "";
        string[] requiredKeys = family == "sma_cross"
            ? new[] { "sma_fast", "sma_slow" }
            : new[] { "rsi_period", "rsi_oversold", "rsi_overbought" };

        foreach (var key in requiredKeys)
        {
            if (!root.TryGetProperty(key, out var v))
                return (null, "", $"LLM 缺少必要欄位: {key}");
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var iv))
                return (null, "", $"欄位 {key} 不是整數");
            dict[key] = iv;
        }

        if (root.TryGetProperty("rationale", out var r) && r.ValueKind == JsonValueKind.String)
            rationale = r.GetString() ?? "";

        return (dict, rationale, null);
    }

    // ── 驗證 ─────────────────────────────────────────────────────────

    private (bool ok, string? error) Validate(string family, Dictionary<string, int> p)
    {
        if (family == "sma_cross")
        {
            var f = p["sma_fast"]; var s = p["sma_slow"];
            if (f < 5 || f > 30) return (false, $"sma_fast {f} 超出 5-30");
            if (s < 20 || s > 100) return (false, $"sma_slow {s} 超出 20-100");
            if (s <= f) return (false, $"sma_slow ({s}) 必須 > sma_fast ({f})");
        }
        else if (family == "rsi_oversold")
        {
            var pd = p["rsi_period"]; var os = p["rsi_oversold"]; var ob = p["rsi_overbought"];
            if (pd < 5 || pd > 30) return (false, $"rsi_period {pd} 超出 5-30");
            if (os < 15 || os > 40) return (false, $"rsi_oversold {os} 超出 15-40");
            if (ob < 60 || ob > 85) return (false, $"rsi_overbought {ob} 超出 60-85");
            if (ob <= os) return (false, $"rsi_overbought ({ob}) 必須 > rsi_oversold ({os})");
        }
        return (true, null);
    }
}

// ── DTO ──────────────────────────────────────────────────────────────

public class GenerationResult
{
    public bool Success { get; set; }
    public string Family { get; set; } = "";
    public string Symbol { get; set; } = "";
    public Dictionary<string, int> Parameters { get; set; } = new();
    public string Rationale { get; set; } = "";
    public string RawLlmResponse { get; set; } = "";
    public string Model { get; set; } = "";
    public int TokensUsed { get; set; }
    public string? Error { get; set; }

    public static GenerationResult Failure(string error, string raw = "")
        => new() { Success = false, Error = error, RawLlmResponse = raw };
}

public class PriorAttempt
{
    public Dictionary<string, int> Parameters { get; set; } = new();
    public decimal OutOfSampleSharpe { get; set; }
    public decimal ReturnPct { get; set; }
    public decimal MaxDrawdownPct { get; set; }
    public int Trades { get; set; }
}
