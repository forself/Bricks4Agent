using Broker.Models;
using BrokerCore.Data;

namespace Broker.Services;

/// <summary>
/// H2 — 評估「現在這個 capability 是否在 auto window 內」。
///
/// IsInsideAutoWindow(capabilityId) → true（走原 ACL） / false（強制 require_approval）/ null（沒規則、不影響）
/// </summary>
public class TimeAclService
{
    private readonly BrokerDb _db;
    private readonly ILogger<TimeAclService> _logger;

    public TimeAclService(BrokerDb db, ILogger<TimeAclService> logger)
    {
        _db = db; _logger = logger;
    }

    /// <summary>true = 在窗內走原 ACL；false = 在窗外強制 approve；null = 沒規則</summary>
    public bool? IsInsideAutoWindow(string capabilityId, DateTime utcNow)
    {
        var rules = _db.Query<TimeAclRule>(
            "SELECT * FROM time_acl_rules WHERE enabled = 1 AND capability_id = @cid",
            new { cid = capabilityId });
        if (rules.Count == 0) return null;

        // 多條規則：任一條覆蓋（in window）就 ok
        foreach (var r in rules)
        {
            if (Matches(r, utcNow)) return true;
        }
        return false;
    }

    public List<TimeAclRule> ListRules()
        => _db.Query<TimeAclRule>("SELECT * FROM time_acl_rules ORDER BY created_at DESC LIMIT 200");

    internal static bool Matches(TimeAclRule rule, DateTime utcNow)
    {
        DateTime local;
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(rule.Timezone);
            local = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
        }
        catch
        {
            // 找不到時區 fallback UTC、規則仍適用
            local = utcNow;
        }

        // weekday：DayOfWeek 0=Sun ... 6=Sat
        var bit = 1 << (int)local.DayOfWeek;
        if ((rule.WeekdayMask & bit) == 0) return false;

        var hour = local.Hour;
        if (rule.StartHour <= rule.EndHour)
        {
            // 同日視窗（例 09-17）
            return hour >= rule.StartHour && hour < rule.EndHour;
        }
        // 跨日（例 22-02 表示 22:00-23:59 + 00:00-02:00）
        return hour >= rule.StartHour || hour < rule.EndHour;
    }
}
