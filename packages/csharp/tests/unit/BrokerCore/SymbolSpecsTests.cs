using BrokerCore.Trading;

namespace Unit.Tests.BrokerCore;

/// <summary>
/// SymbolSpecs.PreflightOrder 契約測試 — 鎖住「下單前先擋掉違規」的行為。
/// 重點是「不過 → return false + 明確 error」、別讓壞單流到 approval / dispatch。
/// </summary>
public class SymbolSpecsTests
{
    [Fact]
    public void Bingx_BTC_Qty_BelowMin_Rejected()
    {
        var (ok, err, _) = SymbolSpecs.PreflightOrder("bingx", "BTC-USDT", qty: 0.00001m, leverage: 5);
        ok.Should().BeFalse();
        err.Should().Contain("0.0001");
    }

    [Fact]
    public void Bingx_BTC_Qty_AtMin_Accepted()
    {
        var (ok, _, _) = SymbolSpecs.PreflightOrder("bingx", "BTC-USDT", qty: 0.0001m, leverage: 5);
        ok.Should().BeTrue();
    }

    [Fact]
    public void Bingx_ETH_Qty_005_Rejected()
    {
        // 跟今天踩過的 case 一致：0.005 ETH 低於 BingX min 0.01
        var (ok, err, _) = SymbolSpecs.PreflightOrder("bingx", "ETH-USDT", qty: 0.005m, leverage: 10);
        ok.Should().BeFalse();
        err.Should().Contain("0.01");
    }

    [Fact]
    public void Bingx_ETH_Qty_001_Accepted()
    {
        var (ok, _, _) = SymbolSpecs.PreflightOrder("bingx", "ETH-USDT", qty: 0.01m, leverage: 10);
        ok.Should().BeTrue();
    }

    [Fact]
    public void Leverage_OutOfRange_Rejected()
    {
        var (okHi, errHi, _) = SymbolSpecs.PreflightOrder("bingx", "BTC-USDT", qty: 0.0001m, leverage: 200);
        okHi.Should().BeFalse();
        errHi.Should().Contain("125");

        var (okLo, errLo, _) = SymbolSpecs.PreflightOrder("bingx", "BTC-USDT", qty: 0.0001m, leverage: 0);
        okLo.Should().BeFalse();
        errLo.Should().Contain("range");
    }

    [Fact]
    public void Leverage_AboveSymbolMax_Rejected()
    {
        // SOL max 75x、超過要擋下
        var (ok, err, _) = SymbolSpecs.PreflightOrder("bingx", "SOL-USDT", qty: 0.1m, leverage: 80);
        ok.Should().BeFalse();
        err.Should().Contain("75");
    }

    [Fact]
    public void UnknownSymbol_Passes_With_Warning()
    {
        // 不在 spec 表的 symbol（例如新上市的）→ ok=true 但有 warning、由交易所端做最終 sanity
        var (ok, err, warn) = SymbolSpecs.PreflightOrder("bingx", "PEPE-USDT", qty: 1000m, leverage: 5);
        ok.Should().BeTrue();
        err.Should().BeNull();
        warn.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void UnknownExchange_Passes_With_Warning()
    {
        var (ok, _, warn) = SymbolSpecs.PreflightOrder("kraken", "BTC-USDT", qty: 0.001m, leverage: 5);
        ok.Should().BeTrue();
        warn.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ZeroQty_Rejected()
    {
        var (ok, err, _) = SymbolSpecs.PreflightOrder("bingx", "BTC-USDT", qty: 0m, leverage: 5);
        ok.Should().BeFalse();
        err.Should().Contain("> 0");
    }

    [Fact]
    public void NotionalBelowMin_Rejected()
    {
        // BTC min notional 2 USDT；0.0001 BTC × $10000 (假 mark) = $1 < $2
        var (ok, err, _) = SymbolSpecs.PreflightOrder("bingx", "BTC-USDT", qty: 0.0001m, leverage: 5, markPrice: 10000m);
        ok.Should().BeFalse();
        err.Should().Contain("notional");
    }

    [Fact]
    public void NotionalAboveMin_Accepted()
    {
        // 0.0001 BTC × $80000 = $8 > $2 min notional
        var (ok, _, _) = SymbolSpecs.PreflightOrder("bingx", "BTC-USDT", qty: 0.0001m, leverage: 5, markPrice: 80000m);
        ok.Should().BeTrue();
    }
}
