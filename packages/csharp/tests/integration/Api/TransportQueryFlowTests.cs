using FluentAssertions;
using Integration.Tests.Fixtures;
using Xunit;

namespace Integration.Tests.Api;

public class TransportQueryFlowTests : IClassFixture<BrokerFixture>
{
    private readonly BrokerFixture _fixture;

    public TransportQueryFlowTests(BrokerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Rail_Query_WithoutDate_ReturnsRangeGuidance()
    {
        using var reply = await _fixture.SendHighLevelLineTextAsync("?rail 板橋 高雄", "line-transport-rail-range");
        var message = reply.RootElement.GetProperty("data").GetProperty("reply").GetString();

        message.Should().Contain("目前先依較寬條件整理");
        message.Should().Contain("如果你要指定日期");
    }

    [Fact]
    public async Task Bus_Query_WithoutCity_ReturnsFollowUpOptions()
    {
        using var reply = await _fixture.SendHighLevelLineTextAsync("?bus 307", "line-transport-bus-followup");
        var message = reply.RootElement.GetProperty("data").GetProperty("reply").GetString();

        message.Should().Contain("我還需要確認所在城市");
        message.Should().Contain("台北市");
        message.Should().Contain("新北市");
    }
}
