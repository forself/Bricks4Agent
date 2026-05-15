using Broker.Models;
using Broker.Services;

namespace Unit.Tests.Services;

/// <summary>
/// H2 — TimeAclService.Matches() 純函式測試（無 DB）。
///
/// 邊界跟坑：
/// - 同日視窗（09-17）跟跨日視窗（22-02）兩種 mode
/// - weekday bitmask（bit0=Sun ... bit6=Sat）
/// - timezone 找不到 fallback UTC、不丟例外
/// - 邊界小時：StartHour=9 → 09:00 算 in、EndHour=17 → 17:00 算 out（半開區間 [start, end)）
/// </summary>
public class TimeAclServiceTests
{
    private static TimeAclRule Rule(int start, int end, int mask = 0b1111111, string tz = "UTC")
        => new() { StartHour = start, EndHour = end, WeekdayMask = mask, Timezone = tz, Enabled = true };

    private static DateTime Utc(int year, int month, int day, int hour, int min = 0)
        => new(year, month, day, hour, min, 0, DateTimeKind.Utc);

    [Fact]
    public void SameDayWindow_InsideHours_True()
    {
        // 09-17 weekdays, test on Wednesday 12:00 UTC
        var rule = Rule(9, 17, 0b0111110);
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 12)).Should().BeTrue();
    }

    [Fact]
    public void SameDayWindow_BeforeStart_False()
    {
        var rule = Rule(9, 17);
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 8)).Should().BeFalse();
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 8, 59)).Should().BeFalse();
    }

    [Fact]
    public void SameDayWindow_AtStartHour_True_AtEndHour_False()
    {
        // [9, 17) 半開區間
        var rule = Rule(9, 17);
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 9, 0)).Should().BeTrue();
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 16, 59)).Should().BeTrue();
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 17, 0)).Should().BeFalse();
    }

    [Fact]
    public void CrossDayWindow_22_To_02_NightOk()
    {
        // 22-02 跨日：22:00-23:59 + 00:00-01:59
        var rule = Rule(22, 2);
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 22)).Should().BeTrue();   // 晚上
        TimeAclService.Matches(rule, Utc(2026, 5, 14, 1)).Should().BeTrue();    // 凌晨
        TimeAclService.Matches(rule, Utc(2026, 5, 14, 2)).Should().BeFalse();   // 上界排除
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 12)).Should().BeFalse();  // 中午不算
    }

    [Fact]
    public void Weekday_OnlyMonFri_RejectsWeekend()
    {
        // 0b0111110 = Mon-Fri
        var rule = Rule(0, 24, 0b0111110);
        // 2026-05-16 是 Saturday、5-17 是 Sunday、5-13 是 Wednesday
        TimeAclService.Matches(rule, Utc(2026, 5, 16, 12)).Should().BeFalse();  // Sat
        TimeAclService.Matches(rule, Utc(2026, 5, 17, 12)).Should().BeFalse();  // Sun
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 12)).Should().BeTrue();   // Wed
    }

    [Fact]
    public void Weekday_AllDays_AllowsAny()
    {
        var rule = Rule(0, 24, 0b1111111);
        TimeAclService.Matches(rule, Utc(2026, 5, 16, 12)).Should().BeTrue();
        TimeAclService.Matches(rule, Utc(2026, 5, 17, 12)).Should().BeTrue();
    }

    [Fact]
    public void InvalidTimezone_FallsBackToUtc_NoThrow()
    {
        var rule = Rule(9, 17, 0b1111111, tz: "Not/A/Zone");
        var act = () => TimeAclService.Matches(rule, Utc(2026, 5, 13, 12));
        act.Should().NotThrow();
        // 既然 fallback UTC、12:00 UTC in [9,17) → true
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 12)).Should().BeTrue();
    }

    [Fact]
    public void TaipeiTimezone_ShiftsHours()
    {
        // Asia/Taipei = UTC+8
        // UTC 01:00 → Taipei 09:00（in [9,17)）
        // UTC 09:00 → Taipei 17:00（out）
        var rule = Rule(9, 17, 0b1111111, tz: "Asia/Taipei");
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 1)).Should().BeTrue();
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 8, 59)).Should().BeTrue();   // Taipei 16:59
        TimeAclService.Matches(rule, Utc(2026, 5, 13, 9)).Should().BeFalse();      // Taipei 17:00
    }
}
