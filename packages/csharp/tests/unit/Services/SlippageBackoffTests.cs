using System.Collections.Concurrent;
using Broker.Services;
using FluentAssertions;

namespace Unit.Tests.Services;

/// <summary>
/// 鎖住 C1 slippage backoff gate 契約：
///   - 沒紀錄 → 不擋
///   - 紀錄已過期（now >= until）→ 不擋
///   - 還在 cooldown 內 → 擋、remainMinutes 是向上取整的剩餘分鐘
///
/// 為什麼這條 test 重要：「self-healing」claim 不能只是 PR 標題、要程式化驗證
/// 「執行品質 degraded 平台會自動退場」這條閉環。Demo 給 Benson 看也是 unit test
/// 證據比較硬。
/// </summary>
public class SlippageBackoffTests
{
    [Fact]
    public void NoEntry_NotInBackoff_AllowsThrough()
    {
        var map = new ConcurrentDictionary<string, DateTime>();
        var skip = AutoTraderSizingService.ShouldSkipForSlippageBackoff(
            map, "bingx:BTC-USDT", DateTime.UtcNow, out var remain);
        skip.Should().BeFalse("無紀錄 = 沒觸發過 slippage backoff");
        remain.Should().Be(0);
    }

    [Fact]
    public void ExpiredEntry_AllowsThrough()
    {
        var map = new ConcurrentDictionary<string, DateTime>();
        map["bingx:BTC-USDT"] = DateTime.UtcNow.AddMinutes(-5); // 已過期 5min
        var skip = AutoTraderSizingService.ShouldSkipForSlippageBackoff(
            map, "bingx:BTC-USDT", DateTime.UtcNow, out var remain);
        skip.Should().BeFalse("過期 cooldown 不該再擋");
        remain.Should().Be(0);
    }

    [Fact]
    public void ActiveBackoff_BlocksAndReturnsRemainMinutes()
    {
        var map = new ConcurrentDictionary<string, DateTime>();
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        map["bingx:BTC-USDT"] = now.AddMinutes(20);  // 還剩 20 min
        var skip = AutoTraderSizingService.ShouldSkipForSlippageBackoff(
            map, "bingx:BTC-USDT", now, out var remain);
        skip.Should().BeTrue();
        remain.Should().Be(20);
    }

    [Fact]
    public void PartialMinute_CeilingToFullMinute()
    {
        // 還剩 9 min 15s → ceil 到 10 min（避免顯示「0 min 還在 backoff」造成混淆）
        var map = new ConcurrentDictionary<string, DateTime>();
        var now = new DateTime(2026, 5, 18, 12, 0, 0, DateTimeKind.Utc);
        map["bingx:BTC-USDT"] = now.AddSeconds(9 * 60 + 15);
        var skip = AutoTraderSizingService.ShouldSkipForSlippageBackoff(
            map, "bingx:BTC-USDT", now, out var remain);
        skip.Should().BeTrue();
        remain.Should().Be(10);
    }

    [Fact]
    public void DifferentSymbol_IsolatedNotAffected()
    {
        // BTC backoff 不影響 ETH 下單
        var map = new ConcurrentDictionary<string, DateTime>();
        map["bingx:BTC-USDT"] = DateTime.UtcNow.AddMinutes(30);
        var skip = AutoTraderSizingService.ShouldSkipForSlippageBackoff(
            map, "bingx:ETH-USDT", DateTime.UtcNow, out var _);
        skip.Should().BeFalse("backoff per (exchange:symbol) 隔離、不該影響其它 symbol");
    }

    [Fact]
    public void DifferentExchange_IsolatedNotAffected()
    {
        // 同 symbol 不同交易所也隔離
        var map = new ConcurrentDictionary<string, DateTime>();
        map["bingx:BTC-USDT"] = DateTime.UtcNow.AddMinutes(30);
        var skip = AutoTraderSizingService.ShouldSkipForSlippageBackoff(
            map, "binance:BTC-USDT", DateTime.UtcNow, out var _);
        skip.Should().BeFalse("不同交易所、流動性不同、backoff 不該跨交易所");
    }
}
