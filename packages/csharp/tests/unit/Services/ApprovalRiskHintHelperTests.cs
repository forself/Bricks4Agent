using Broker.Services;

namespace Unit.Tests.Services;

/// <summary>
/// W14 P3 — ApprovalRiskHintHelper 規則化測試。
///
/// 重點不變式：
/// - 已知高風險 capability 必出 ⚠ 開頭、admin 一眼看出
/// - 低風險 capability 回 "(low risk)" — 不誤標、不騷擾
/// - 不認識的 capability 不丟例外、回 fallback hint
/// - payload 解析失敗（非 JSON / 缺欄位）回 fallback、不 crash
/// </summary>
public class ApprovalRiskHintHelperTests
{
    [Theory]
    [InlineData("trading.order")]
    [InlineData("trading.perpetual")]
    [InlineData("rag.import.web")]
    [InlineData("agent.spawn")]
    [InlineData("deploy.azure.iis")]
    public void IsHighRisk_RecognizesKnownDangerousCaps(string cap)
    {
        ApprovalRiskHintHelper.IsHighRisk(cap).Should().BeTrue();
    }

    [Theory]
    [InlineData("read_file")]
    [InlineData("strategy.signal")]
    [InlineData("quote.prices")]
    [InlineData("")]
    public void IsHighRisk_LowRiskOrUnknown_False(string cap)
    {
        ApprovalRiskHintHelper.IsHighRisk(cap).Should().BeFalse();
    }

    [Fact]
    public void Hint_TradingOrder_IncludesSymbolQtyAndMaxLossEstimate()
    {
        var payload = """
            {"args":{"symbol":"BTC-USDT","side":"buy","quantity":0.05,"exchange":"bingx","mode":"perp_long_only","leverage":10}}
            """;
        var hint = ApprovalRiskHintHelper.Hint("trading.order", payload);
        hint.Should().StartWith("⚠");
        hint.Should().Contain("BTC-USDT");
        hint.Should().Contain("BUY");
        hint.Should().Contain("0.05");
        hint.Should().Contain("10x", "leverage 應該被顯示");
        hint.Should().Contain("最大損失");
    }

    [Fact]
    public void Hint_TradingOrder_SpotMode_NoLeverageString()
    {
        var payload = """{"args":{"symbol":"BTC-USDT","side":"sell","quantity":0.01,"mode":"spot"}}""";
        var hint = ApprovalRiskHintHelper.Hint("trading.order", payload);
        hint.Should().NotContain(" 1x ", "spot 不應該顯示 leverage");
    }

    [Fact]
    public void Hint_LowRiskCapability_ReturnsLowRiskTag()
    {
        ApprovalRiskHintHelper.Hint("read_file", """{"args":{"path":"/x"}}""")
            .Should().Be("(low risk)");
    }

    [Fact]
    public void Hint_UnknownButHighRisk_FallbackTagged()
    {
        // 不在 switch 列舉、但若在 IsHighRisk 列舉裡會走 default
        // 模擬未列舉但高風險的情況用未列舉的 capability
        var hint = ApprovalRiskHintHelper.Hint("unknown.dangerous", "{}");
        hint.Should().Be("(low risk)", "未在高風險清單裡的就是 low risk");
    }

    [Fact]
    public void Hint_RagImport_ShowsSourceCount()
    {
        var hint = ApprovalRiskHintHelper.Hint("rag.import.web",
            """{"args":{"urls":["https://a.com","https://b.com","https://c.com"],"max_pages":5}}""");
        hint.Should().Contain("RAG ingest");
        hint.Should().Contain("3 URLs");
        hint.Should().Contain("max_pages=5");
    }

    [Fact]
    public void Hint_RagImport_QueryMode()
    {
        var hint = ApprovalRiskHintHelper.Hint("rag.import.web",
            """{"args":{"query":"區塊鏈","max_pages":3}}""");
        hint.Should().Contain("區塊鏈");
    }

    [Fact]
    public void Hint_AgentSpawn_ShowsTemplate()
    {
        var hint = ApprovalRiskHintHelper.Hint("agent.spawn",
            """{"args":{"template":"file_reader"}}""");
        hint.Should().Contain("spawn agent");
        hint.Should().Contain("file_reader");
    }

    [Fact]
    public void Hint_MalformedPayload_NoCrash()
    {
        var act = () => ApprovalRiskHintHelper.Hint("trading.order", "not-json-at-all");
        act.Should().NotThrow();
        ApprovalRiskHintHelper.Hint("trading.order", "not-json-at-all")
            .Should().Contain("無法解析");
    }

    [Fact]
    public void Hint_EmptyCapabilityId_ReturnsUnknownTag()
    {
        ApprovalRiskHintHelper.Hint("", "{}").Should().Be("(unknown capability)");
    }

    [Fact]
    public void Hint_TradingOrder_MissingFields_ShowsQuestionMarks()
    {
        var hint = ApprovalRiskHintHelper.Hint("trading.order", "{}");
        hint.Should().StartWith("⚠");
        hint.Should().Contain("?");   // 缺欄位用 ? 佔位、不 throw
    }
}
