using System.Collections.Concurrent;
using System.Text.Json;
using Broker.Models;
using BrokerCore.Data;
using BrokerCore.Models;

namespace Broker.Services;

/// <summary>
/// H3 — 把 payload 跟 approval_templates 一條條比對、回第一條命中的 template（或 null）。
///
/// payload_match 條件支援運算子：
///   {"args.symbol": "BTC-USDT"}             ← 字串相等
///   {"args.quantity": {"$lte": 0.01}}       ← 數值小於等於
///   {"args.side": {"$in":["buy","BUY"]}}    ← 任一字串
///   {"args.leverage": {"$lte": 5, "$gt": 0}} ← AND 條件
///
/// dot-path 解析：`args.symbol` 從 root JsonElement 沿著 `args.symbol` 取值。
/// 條件解析失敗（schema 不對 / 缺欄位） → 該 template 視為不命中、不丟例外。
///
/// 命中計數：in-memory dictionary（template_id → today's count）、UTC 換日 lazy 重置。
/// 不持久化：broker restart 後計數歸零、不算嚴重（max_uses 預設 0 = 不限、設 1 才會在意這個）。
/// </summary>
public class ApprovalTemplateMatcher
{
    private readonly BrokerDb _db;
    private readonly ILogger<ApprovalTemplateMatcher> _logger;
    private readonly ConcurrentDictionary<string, (DateTime Day, int Count)> _hits = new();

    public ApprovalTemplateMatcher(BrokerDb db, ILogger<ApprovalTemplateMatcher> logger)
    {
        _db = db; _logger = logger;
    }

    /// <summary>找第一條命中的 enabled template；null = 沒命中、走人工 pending 流程。</summary>
    public ApprovalTemplate? FindMatch(string capabilityId, string route, string payloadJson)
    {
        var templates = _db.Query<ApprovalTemplate>(
            "SELECT * FROM approval_templates WHERE enabled = 1 AND capability_id = @cid ORDER BY created_at ASC",
            new { cid = capabilityId });

        if (templates.Count == 0) return null;

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson);
            root = doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Template match: payload not JSON, skipping");
            return null;
        }

        foreach (var t in templates)
        {
            // route 條件
            if (!string.IsNullOrEmpty(t.Route) && !t.Route.Equals(route, StringComparison.OrdinalIgnoreCase))
                continue;

            // payload_match
            if (!PayloadMatches(root, t.PayloadMatch))
                continue;

            // 命中次數上限
            if (t.MaxUsesPerDay > 0 && !TryConsumeQuota(t.TemplateId, t.MaxUsesPerDay))
                continue;

            return t;
        }
        return null;
    }

    public IReadOnlyDictionary<string, int> Snapshot()
    {
        var today = DateTime.UtcNow.Date;
        return _hits.Where(kv => kv.Value.Day == today)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Count);
    }

    private bool TryConsumeQuota(string templateId, int maxPerDay)
    {
        var today = DateTime.UtcNow.Date;
        var updated = _hits.AddOrUpdate(templateId,
            _ => (today, 1),
            (_, prev) => prev.Day != today ? (today, 1) : (today, prev.Count + 1));
        return updated.Count <= maxPerDay;
    }

    internal static bool PayloadMatches(JsonElement payload, string matchJson)
    {
        if (string.IsNullOrWhiteSpace(matchJson) || matchJson == "{}") return true;
        try
        {
            using var doc = JsonDocument.Parse(matchJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!TryResolvePath(payload, prop.Name, out var actual)) return false;
                if (!ValueMatches(actual, prop.Value)) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static bool TryResolvePath(JsonElement root, string dotPath, out JsonElement found)
    {
        found = root;
        foreach (var seg in dotPath.Split('.'))
        {
            if (found.ValueKind != JsonValueKind.Object) return false;
            if (!found.TryGetProperty(seg, out var next)) return false;
            found = next;
        }
        return true;
    }

    private static bool ValueMatches(JsonElement actual, JsonElement expected)
    {
        // primitive 字串/數字直接 equality
        if (expected.ValueKind == JsonValueKind.String)
            return actual.ValueKind == JsonValueKind.String
                && string.Equals(actual.GetString(), expected.GetString(), StringComparison.OrdinalIgnoreCase);
        if (expected.ValueKind == JsonValueKind.Number)
        {
            if (actual.ValueKind != JsonValueKind.Number) return false;
            return actual.GetDecimal() == expected.GetDecimal();
        }
        if (expected.ValueKind == JsonValueKind.True || expected.ValueKind == JsonValueKind.False)
            return actual.ValueKind == expected.ValueKind;

        // operator object
        if (expected.ValueKind != JsonValueKind.Object) return false;
        foreach (var op in expected.EnumerateObject())
        {
            var ok = op.Name switch
            {
                "$eq"  => ValueMatches(actual, op.Value),
                "$lte" => CompareNum(actual, op.Value, (a,b)=> a <= b),
                "$lt"  => CompareNum(actual, op.Value, (a,b)=> a <  b),
                "$gte" => CompareNum(actual, op.Value, (a,b)=> a >= b),
                "$gt"  => CompareNum(actual, op.Value, (a,b)=> a >  b),
                "$in"  => CheckIn(actual, op.Value),
                _      => false,
            };
            if (!ok) return false;
        }
        return true;
    }

    private static bool CompareNum(JsonElement actual, JsonElement bound, Func<decimal,decimal,bool> cmp)
    {
        if (actual.ValueKind != JsonValueKind.Number || bound.ValueKind != JsonValueKind.Number) return false;
        return cmp(actual.GetDecimal(), bound.GetDecimal());
    }

    private static bool CheckIn(JsonElement actual, JsonElement arr)
    {
        if (arr.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in arr.EnumerateArray())
            if (ValueMatches(actual, item)) return true;
        return false;
    }
}
