using TransportTdxWorker.Services;

namespace Unit.Tests.Transport;

public class TransportQueryContextResolverTests
{
    private static Dictionary<string, string?> Resolve(string mode, string query)
        => new TransportQueryContextResolver().Resolve(mode, query, new Dictionary<string, string?>());

    // --- Station collision (longest-first + span masking) ---

    [Fact]
    public void Resolve_rail_distinguishes_overlapping_station_names()
    {
        var resolved = Resolve("rail", "新左營到台北的火車");

        resolved["origin"].Should().Be("新左營");
        resolved["destination"].Should().Be("台北");
    }

    [Fact]
    public void GetTraStationId_prefers_longest_contained_station()
    {
        var resolver = new TransportQueryContextResolver();

        resolver.GetTraStationId("新左營車站").Should().Be("4340");
        resolver.GetTraStationId("新左營").Should().Be("4340");
    }

    [Fact]
    public void GetThsrStationId_resolves_exact_and_contained_names()
    {
        var resolver = new TransportQueryContextResolver();

        resolver.GetThsrStationId("左營").Should().Be("1070");
        resolver.GetThsrStationId("台中高鐵站").Should().Be("1040");
    }

    // --- Absolute time ranges and single times ---

    [Fact]
    public void Resolve_extracts_absolute_time_range()
    {
        var resolved = Resolve("rail", "板橋到高雄 18:00-20:00");

        resolved["time_range"].Should().Be("18:00-20:00");
    }

    [Fact]
    public void ParseTimeRange_returns_inclusive_minute_window_for_range()
    {
        var window = new TransportQueryContextResolver().ParseTimeRange("18:00-20:00");

        window.Should().NotBeNull();
        window!.Value.startMinute.Should().Be(18 * 60);
        window.Value.endMinute.Should().Be(20 * 60);
    }

    [Fact]
    public void Resolve_extracts_single_time_with_meridiem()
    {
        var resolved = Resolve("hsr", "下午3點從台北出發");

        resolved["time_range"].Should().Be("15:00");
    }

    [Fact]
    public void ParseTimeRange_single_time_is_one_hour_window()
    {
        var window = new TransportQueryContextResolver().ParseTimeRange("15:00");

        window.Should().NotBeNull();
        window!.Value.startMinute.Should().Be(15 * 60);
        window.Value.endMinute.Should().Be((15 * 60) + 59);
    }

    [Fact]
    public void ParseTimeRange_keeps_day_segment_keywords()
    {
        var resolver = new TransportQueryContextResolver();

        resolver.ParseTimeRange("evening").Should().Be((18 * 60, (23 * 60) + 59));
        resolver.ParseTimeRange("morning").Should().Be((5 * 60, (11 * 60) + 59));
    }

    [Fact]
    public void Resolve_falls_back_to_day_segment_without_explicit_time()
    {
        Resolve("rail", "晚上的班次")["time_range"].Should().Be("evening");
    }

    // --- Relative dates ---

    [Fact]
    public void Resolve_handles_day_after_tomorrow()
    {
        var expected = DateOnly.FromDateTime(DateTime.Today).AddDays(2).ToString("yyyy-MM-dd");

        Resolve("rail", "後天板橋到高雄")["date"].Should().Be(expected);
    }

    [Fact]
    public void Resolve_handles_two_days_after_tomorrow()
    {
        var expected = DateOnly.FromDateTime(DateTime.Today).AddDays(3).ToString("yyyy-MM-dd");

        Resolve("rail", "大後天板橋到高雄")["date"].Should().Be(expected);
    }

    [Fact]
    public void Resolve_handles_upcoming_weekend()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var date = DateOnly.Parse(Resolve("rail", "這週末出遊")["date"]!);

        date.DayOfWeek.Should().Be(DayOfWeek.Saturday);
        date.Should().BeOnOrAfter(today);
        (date.DayNumber - today.DayNumber).Should().BeLessThanOrEqualTo(6);
    }

    [Fact]
    public void Resolve_handles_next_week_weekday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var date = DateOnly.Parse(Resolve("rail", "下週一的車")["date"]!);

        date.DayOfWeek.Should().Be(DayOfWeek.Monday);
        date.Should().BeAfter(today);
        // Next ISO week's Monday is at most 14 days out.
        (date.DayNumber - today.DayNumber).Should().BeLessThanOrEqualTo(14);
    }

    [Fact]
    public void Resolve_handles_plain_weekday_as_next_occurrence()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var date = DateOnly.Parse(Resolve("rail", "週五出發")["date"]!);

        date.DayOfWeek.Should().Be(DayOfWeek.Friday);
        date.Should().BeAfter(today);
        (date.DayNumber - today.DayNumber).Should().BeLessThanOrEqualTo(7);
    }
}
