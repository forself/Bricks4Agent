using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using StrategyWorker.Engine;
using StrategyWorker.Handlers;
using Xunit;

namespace Unit.Tests.Workers.Strategy;

/// <summary>
/// strategy.signal 的 "scan" route 端到端契約測試:
/// JSON payload(symbols→bars)→ 掃描 → JSON 結果(candidates 依 magnitude 排序、尊重 top_n)。
/// </summary>
public class StrategyScanRouteTests
{
    // 產一段 K 線、序列化成 scan payload 預期的 bar 物件
    private static List<object> SeriesJson(int count, decimal start, decimal drift, decimal wiggle)
    {
        var bars = new List<object>();
        decimal price = start;
        var t0 = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < count; i++)
        {
            decimal open = price;
            decimal osc = (i % 2 == 0) ? wiggle : -wiggle;
            decimal close = open + drift + osc;
            decimal high = Math.Max(open, close) + wiggle;
            decimal low = Math.Min(open, close) - wiggle;
            bars.Add(new
            {
                open_time = t0.AddHours(i * 4).ToString("o"),
                open, high, low, close, volume = 1_000_000m,
            });
            price = close;
        }
        return bars;
    }

    [Fact]
    public async Task ScanRoute_ReturnsSortedCandidatesJson()
    {
        var handler = new StrategySignalHandler(new DefaultStrategyRegistry());
        var payload = JsonSerializer.Serialize(new
        {
            symbols = new Dictionary<string, object>
            {
                ["UP"]   = SeriesJson(80, 100m,  0.8m, 1.0m),
                ["DOWN"] = SeriesJson(80, 200m, -0.8m, 1.0m),
                ["CHOP"] = SeriesJson(80, 150m,  0.0m, 2.5m),
            },
            min_magnitude = 0m,
            top_n = 2,
        });

        var (ok, result, err) = await handler
            .ExecuteAsync("req1", "scan", payload, "", CancellationToken.None);

        ok.Should().BeTrue(err);
        result.Should().NotBeNull();

        var root = JsonDocument.Parse(result!).RootElement;
        root.GetProperty("scanned").GetInt32().Should().Be(3);

        var candidates = root.GetProperty("candidates");
        candidates.GetArrayLength().Should().BeLessThanOrEqualTo(2);

        decimal prev = decimal.MaxValue;
        foreach (var c in candidates.EnumerateArray())
        {
            var mag = c.GetProperty("magnitude").GetDecimal();
            mag.Should().BeLessThanOrEqualTo(prev);
            prev = mag;
        }
    }

    [Fact]
    public async Task ScanRoute_MissingSymbols_ReturnsError()
    {
        var handler = new StrategySignalHandler(new DefaultStrategyRegistry());
        var (ok, _, err) = await handler
            .ExecuteAsync("req2", "scan", "{}", "", CancellationToken.None);

        ok.Should().BeFalse();
        err.Should().NotBeNull();
    }
}
