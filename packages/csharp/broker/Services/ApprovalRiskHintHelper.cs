using System.Text.Json;

namespace Broker.Services;

/// <summary>
/// W14 P3 — 把 approval payload 翻譯成人話 risk hint，給 admin 一眼看出風險量級。
///
/// 攻擊情境：攻擊者 prompt-inject bot 偷下單、broker 寫一筆 approval、
/// admin 在 Discord 看到「approve trading.order?」→ 隨手 approve → 真錢損失。
///
/// 防禦：approval 通知不只給 capability_id，還要給「這筆做下去最壞會怎樣」。
/// 例如：「BTC 100x long 0.05、估算最大損失 ≈ 50 USDT」、
/// admin 看到金額 > 預期就會留意、被誘騙的機會大幅下降。
///
/// 純函式、無外部依賴；payload 解析失敗 fallback 簡短描述、不丟 exception。
/// </summary>
public static class ApprovalRiskHintHelper
{
    /// <summary>給 capability_id + payload JSON 字串、回 1-2 句中文 risk hint。</summary>
    public static string Hint(string capabilityId, string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(capabilityId)) return "(unknown capability)";

        // 不是高風險 capability、不需要 risk hint
        if (!IsHighRisk(capabilityId)) return "(low risk)";

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            var root = doc.RootElement;
            var args = root.TryGetProperty("args", out var a) ? a : root;

            return capabilityId switch
            {
                "trading.order"     => HintTradingOrder(args),
                "trading.perpetual" => HintTradingPerp(args),
                "rag.import.web"    => HintRagImport(args),
                "agent.spawn"       => HintAgentSpawn(args),
                _                   => $"⚠ {capabilityId}（未列舉、人工確認 payload）",
            };
        }
        catch
        {
            return $"⚠ {capabilityId}（payload 無法解析、確認原始 JSON）";
        }
    }

    public static bool IsHighRisk(string capabilityId) => capabilityId switch
    {
        "trading.order"     => true,
        "trading.perpetual" => true,
        "rag.import.web"    => true,    // 會打外網、燒 LLM
        "agent.spawn"       => true,    // 會起 container 用資源
        "deploy.azure.iis"  => true,
        _ => false,
    };

    private static string HintTradingOrder(JsonElement args)
    {
        var symbol = StrOrNull(args, "symbol") ?? "?";
        var side = StrOrNull(args, "side") ?? "?";
        var qty = NumOrNull(args, "quantity") ?? NumOrNull(args, "qty") ?? 0;
        var exchange = StrOrNull(args, "exchange") ?? "?";
        var mode = StrOrNull(args, "mode") ?? "spot";
        var leverage = (int)(NumOrNull(args, "leverage") ?? 1);

        var leverageStr = mode == "spot" ? "" : $" {leverage}x";
        // SL 預設約 1.5%（AutoTrader InitialSlPct 預設）— 估算最大損失 = qty * price * sl_pct * leverage
        // 這裡沒 price、用「估算最大損失 = qty × 1.5% × leverage」相對表達
        var maxLossRel = qty * 0.015m * Math.Max(1, leverage);
        return $"⚠ {exchange} {mode}{leverageStr} {side.ToUpperInvariant()} {symbol} qty={qty}" +
               $"｜估算最大損失 ≈ {maxLossRel:F4} (qty × ~1.5%SL × leverage)" +
               $"｜真錢操作、approve 前再確認 symbol/qty";
    }

    private static string HintTradingPerp(JsonElement args)
    {
        var route = StrOrNull(args, "route") ?? "?";
        var symbol = StrOrNull(args, "symbol") ?? "?";
        return $"⚠ trading.perpetual route={route} symbol={symbol}（讀類 OK，寫類請看實際 args）";
    }

    private static string HintRagImport(JsonElement args)
    {
        var query = StrOrNull(args, "query");
        var urlsCount = args.TryGetProperty("urls", out var u) && u.ValueKind == JsonValueKind.Array
            ? u.GetArrayLength() : 0;
        var maxPages = (int)(NumOrNull(args, "max_pages") ?? 5);
        var sources = urlsCount > 0 ? $"{urlsCount} URLs" : (query != null ? $"web search '{query}'" : "?");
        return $"📥 RAG ingest｜sources={sources}｜max_pages={maxPages}｜會打外網 + 跑 embedding（消耗 LLM quota）";
    }

    private static string HintAgentSpawn(JsonElement args)
    {
        var template = StrOrNull(args, "template") ?? StrOrNull(args, "agent_type") ?? "?";
        return $"🤖 spawn agent template={template}（會起新 container、佔用 CPU/Memory + LLM quota）";
    }

    private static string? StrOrNull(JsonElement el, string prop)
        => el.ValueKind == JsonValueKind.Object && el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static decimal? NumOrNull(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out var dd)) return dd;
        return null;
    }
}
