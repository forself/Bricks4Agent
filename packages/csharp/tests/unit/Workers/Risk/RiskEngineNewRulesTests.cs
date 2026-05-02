using RiskWorker.Engine;
using RiskWorker.Models;

namespace Unit.Tests.Workers.Risk;

/// <summary>
/// 鎖住 cut 5 加的兩條新規則的契約：
///   - min_cash_reserve：買入後不能讓 cash 低於 threshold
///   - max_position_count：不能讓持有的不同標的數超過 threshold
/// 既有 6 條規則的測試覆蓋是更早的 PR 該補的，這裡只覆蓋本 cut 新加的。
/// </summary>
public class RiskEngineNewRulesTests
{
    // ── min_cash_reserve ───────────────────────────────────────────

    [Fact]
    public void MinCashReserve_BuyKeepingCashAboveFloor_Passes()
    {
        var engine = new RiskEngine(new() { Rule("r1", "min_cash_reserve", 500m) });
        var portfolio = new PortfolioSnapshot { Cash = 2000m };

        var r = engine.Check("AAPL", "alpaca", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void MinCashReserve_BuyDroppingCashBelowFloor_Fails()
    {
        var engine = new RiskEngine(new() { Rule("r1", "min_cash_reserve", 500m) });
        var portfolio = new PortfolioSnapshot { Cash = 600m };

        // 600 - 200 = 400 < 500 floor
        var r = engine.Check("AAPL", "alpaca", "buy", quantity: 1m, estimatedPrice: 200m, portfolio);

        r.Passed.Should().BeFalse();
        r.Violations.Should().ContainSingle(v => v.RuleId == "r1");
    }

    [Fact]
    public void MinCashReserve_Sell_AlwaysPasses()
    {
        // 賣出會增加現金，不該被 reserve 規則卡到（即使現在低於 floor）
        var engine = new RiskEngine(new() { Rule("r1", "min_cash_reserve", 500m) });
        var portfolio = new PortfolioSnapshot { Cash = 100m };

        var r = engine.Check("AAPL", "alpaca", "sell", quantity: 1m, estimatedPrice: 200m, portfolio);

        r.Passed.Should().BeTrue();
    }

    // ── max_position_count ─────────────────────────────────────────

    [Fact]
    public void MaxPositionCount_BuyExistingHolding_Passes()
    {
        // 已經持有 AAPL，加碼不算「新增」一個位
        var engine = new RiskEngine(new() { Rule("r1", "max_position_count", 3m) });
        var portfolio = new PortfolioSnapshot
        {
            Positions = new()
            {
                Pos("AAPL", qty: 5),
                Pos("TSLA", qty: 2),
                Pos("MSFT", qty: 1),
            },
        };

        var r = engine.Check("AAPL", "alpaca", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void MaxPositionCount_BuyNewSymbolWhenAtLimit_Fails()
    {
        var engine = new RiskEngine(new() { Rule("r1", "max_position_count", 3m) });
        var portfolio = new PortfolioSnapshot
        {
            Positions = new()
            {
                Pos("AAPL", qty: 5),
                Pos("TSLA", qty: 2),
                Pos("MSFT", qty: 1),
            },
        };

        // 已持有 3 個（達上限），買 NVDA 會新增第 4 個
        var r = engine.Check("NVDA", "alpaca", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeFalse();
        r.Violations.Should().ContainSingle(v => v.RuleId == "r1");
    }

    [Fact]
    public void MaxPositionCount_BuyNewSymbolWhenBelowLimit_Passes()
    {
        var engine = new RiskEngine(new() { Rule("r1", "max_position_count", 5m) });
        var portfolio = new PortfolioSnapshot
        {
            Positions = new() { Pos("AAPL", qty: 5), Pos("TSLA", qty: 2) },
        };

        var r = engine.Check("NVDA", "alpaca", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void MaxPositionCount_ZeroQtyPositionsDoNotCount()
    {
        // 殘留 0 quantity 的歷史 position 紀錄不該佔名額
        var engine = new RiskEngine(new() { Rule("r1", "max_position_count", 2m) });
        var portfolio = new PortfolioSnapshot
        {
            Positions = new()
            {
                Pos("AAPL", qty: 5),
                Pos("TSLA", qty: 0),    // 已平倉、紀錄還在
                Pos("MSFT", qty: 0),    // 同上
            },
        };

        var r = engine.Check("NVDA", "alpaca", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void MaxPositionCount_Sell_AlwaysPasses()
    {
        var engine = new RiskEngine(new() { Rule("r1", "max_position_count", 2m) });
        var portfolio = new PortfolioSnapshot
        {
            Positions = new() { Pos("A", 1), Pos("B", 1), Pos("C", 1), Pos("D", 1) },
        };

        var r = engine.Check("X", "alpaca", "sell", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeTrue();
    }

    // ── cooldown_seconds ───────────────────────────────────────────

    [Fact]
    public void Cooldown_NoLastTrade_AlwaysPasses()
    {
        // 第一次交易這個 symbol、dict 裡沒紀錄 → 不違反
        var engine = new RiskEngine(new() { Rule("r1", "cooldown_seconds", 60m) });
        var portfolio = new PortfolioSnapshot();   // 空 LastTradeBySymbol

        var r = engine.Check("AAPL", "alpaca", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void Cooldown_LastTradeRecent_BlocksOrder()
    {
        var engine = new RiskEngine(new() { Rule("r1", "cooldown_seconds", 60m) });
        var portfolio = new PortfolioSnapshot
        {
            LastTradeBySymbol = { ["alpaca:AAPL"] = DateTime.UtcNow.AddSeconds(-10) },   // 10 秒前剛交易
        };

        var r = engine.Check("AAPL", "alpaca", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeFalse();
        r.Violations.Should().ContainSingle(v => v.RuleId == "r1");
    }

    [Fact]
    public void Cooldown_LastTradeBeyondWindow_Passes()
    {
        var engine = new RiskEngine(new() { Rule("r1", "cooldown_seconds", 60m) });
        var portfolio = new PortfolioSnapshot
        {
            LastTradeBySymbol = { ["alpaca:AAPL"] = DateTime.UtcNow.AddSeconds(-120) },   // 2 分鐘前 OK
        };

        var r = engine.Check("AAPL", "alpaca", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void Cooldown_DifferentSymbol_DoesNotShareCooldown()
    {
        // AAPL 剛交易過、TSLA 沒交易過 —— cooldown 不該擋 TSLA
        var engine = new RiskEngine(new() { Rule("r1", "cooldown_seconds", 60m) });
        var portfolio = new PortfolioSnapshot
        {
            LastTradeBySymbol = { ["alpaca:AAPL"] = DateTime.UtcNow.AddSeconds(-5) },
        };

        var r = engine.Check("TSLA", "alpaca", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void Cooldown_SameSymbolDifferentExchange_DoesNotShareCooldown()
    {
        // 同一 symbol 但不同交易所 —— 各自獨立的 cooldown
        var engine = new RiskEngine(new() { Rule("r1", "cooldown_seconds", 60m) });
        var portfolio = new PortfolioSnapshot
        {
            LastTradeBySymbol = { ["alpaca:BTC"] = DateTime.UtcNow.AddSeconds(-5) },
        };

        var r = engine.Check("BTC", "binance", "buy", quantity: 1m, estimatedPrice: 100m, portfolio);

        r.Passed.Should().BeTrue();
    }

    [Fact]
    public void Cooldown_BlocksBothBuyAndSell()
    {
        // signal 抖動可能 buy→sell→buy 連環觸發、cooldown 必須兩個方向都擋
        var engine = new RiskEngine(new() { Rule("r1", "cooldown_seconds", 60m) });
        var portfolio = new PortfolioSnapshot
        {
            LastTradeBySymbol = { ["alpaca:AAPL"] = DateTime.UtcNow.AddSeconds(-10) },
        };

        var buyResult  = engine.Check("AAPL", "alpaca", "buy",  quantity: 1m, estimatedPrice: 100m, portfolio);
        var sellResult = engine.Check("AAPL", "alpaca", "sell", quantity: 1m, estimatedPrice: 100m, portfolio);

        buyResult.Passed.Should().BeFalse();
        sellResult.Passed.Should().BeFalse();
    }

    // ── DefaultRules 整合 ──────────────────────────────────────────

    [Fact]
    public void DefaultRules_IncludesAllNewRules()
    {
        // 確保新規則被加進預設集，避免之後不小心被移掉
        var defaults = RiskEngine.DefaultRules();
        defaults.Select(r => r.Type).Should()
            .Contain("min_cash_reserve")
            .And.Contain("max_position_count")
            .And.Contain("cooldown_seconds");
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static RiskRule Rule(string id, string type, decimal threshold)
        => new() { RuleId = id, Name = type, Type = type, Threshold = threshold, Enabled = true };

    private static PositionEntry Pos(string symbol, decimal qty)
        => new() { Symbol = symbol, Exchange = "alpaca", Quantity = qty, MarketValue = qty * 100m };
}
