using Broker.Services;

namespace Broker.Tests.Services;

/// <summary>
/// StrategyResearchAgent 的純函式合約測試（ParsePrompt / FormatReport）。
/// 不測 BackgroundService 本身、那需要完整 DI scope + StrategyResearchLoopService。
/// </summary>
public class StrategyResearchAgentServiceTests
{
    [Fact]
    public void ParsePrompt_AllFieldsPresent_ReturnsValues()
    {
        var p = "{\"symbol\":\"BTC-USDT\",\"family\":\"sma_cross\",\"generations\":5,\"data_limit\":500}";
        var (sym, fam, gen, dl) = StrategyResearchAgentService.ParsePrompt(p);
        sym.Should().Be("BTC-USDT");
        fam.Should().Be("sma_cross");
        gen.Should().Be(5);
        dl.Should().Be(500);
    }

    [Fact]
    public void ParsePrompt_NoFields_UsesDefaults()
    {
        var (sym, fam, gen, dl) = StrategyResearchAgentService.ParsePrompt("{}");
        sym.Should().Be("AAPL");
        fam.Should().Be("sma_cross");
        gen.Should().Be(3);
        dl.Should().Be(300);
    }

    [Fact]
    public void ParsePrompt_SymbolLowerCase_NormalizesToUpper()
    {
        var (sym, _, _, _) = StrategyResearchAgentService.ParsePrompt("{\"symbol\":\"btc-usdt\"}");
        sym.Should().Be("BTC-USDT");
    }

    [Theory]
    [InlineData(-5, 1)]   // clamp lower
    [InlineData(0, 1)]
    [InlineData(99, 10)]  // clamp upper
    public void ParsePrompt_GenerationsClampedTo1_10(int input, int expected)
    {
        var p = $"{{\"generations\":{input}}}";
        var (_, _, gen, _) = StrategyResearchAgentService.ParsePrompt(p);
        gen.Should().Be(expected);
    }

    [Theory]
    [InlineData(10, 100)]      // clamp lower
    [InlineData(99999, 5000)]  // clamp upper
    public void ParsePrompt_DataLimitClampedTo100_5000(int input, int expected)
    {
        var p = $"{{\"data_limit\":{input}}}";
        var (_, _, _, dl) = StrategyResearchAgentService.ParsePrompt(p);
        dl.Should().Be(expected);
    }

    [Fact]
    public void ParsePrompt_InvalidJson_ThrowsHelpfulError()
    {
        var act = () => StrategyResearchAgentService.ParsePrompt("not json");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*JSON*sma_cross*rsi_oversold*");   // 錯誤訊息應引導用戶
    }

    [Fact]
    public void FormatReport_Completed_IncludesBestCandidate()
    {
        var run = new ResearchRun
        {
            RunId = "run-test-001",
            Symbol = "BTC-USDT",
            Family = "sma_cross",
            Status = "completed",
            StartedAt = DateTime.UtcNow.AddSeconds(-30),
            CompletedAt = DateTime.UtcNow,
            TargetGenerations = 3,
            Candidates = new List<StrategyCandidate>
            {
                new() {
                    Index = 0, Family = "sma_cross",
                    Parameters = new Dictionary<string,int>{{"sma_fast",10},{"sma_slow",50}},
                    Rationale = "test rationale",
                    BacktestSuccess = true,
                    OutOfSampleSharpe = 2.5m, InSampleSharpe = 1.8m,
                    ReturnPct = 12.34m, MaxDrawdownPct = 3.5m, Trades = 8,
                    Windows = new List<WalkForwardWindow> { new(), new() }
                }
            }
        };
        var report = StrategyResearchAgentService.FormatReport(run);
        report.Should().Contain("BTC-USDT");
        report.Should().Contain("🏆 最佳候選");
        report.Should().Contain("sma_fast=10, sma_slow=50");
        report.Should().Contain("2.500");   // OOS Sharpe
        report.Should().Contain("12.34");   // ReturnPct
        report.Should().Contain("test rationale");
    }

    [Fact]
    public void FormatReport_AllFailedCandidates_ShowsNoUsableMessage()
    {
        var run = new ResearchRun
        {
            RunId = "run-test-fail",
            Symbol = "AAPL",
            Family = "sma_cross",
            Status = "completed",
            StartedAt = DateTime.UtcNow.AddSeconds(-5),
            CompletedAt = DateTime.UtcNow,
            TargetGenerations = 2,
            Candidates = new List<StrategyCandidate>
            {
                new() { Index = 0, BacktestSuccess = false, BacktestError = "data too few" },
            }
        };
        var report = StrategyResearchAgentService.FormatReport(run);
        report.Should().Contain("⚠️");
        report.Should().Contain("沒有可用候選");
    }
}
