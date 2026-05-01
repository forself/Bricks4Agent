using FluentAssertions;
using Integration.Tests.Fixtures;
using Xunit;

namespace Integration.Tests.Api;

public class HighLevelProductionRoutingTests : IClassFixture<BrokerFixture>
{
    private readonly BrokerFixture _fixture;

    public HighLevelProductionRoutingTests(BrokerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Production_CreativeWritingRequest_UsesDocGenWithoutProjectName()
    {
        var userId = $"line-production-creative-doc-{Guid.NewGuid():N}";

        using var response = await _fixture.SendHighLevelLineTextAsync("/建立 童話故事", userId);
        var data = response.RootElement.GetProperty("data");
        var draft = data.GetProperty("draft");

        data.GetProperty("mode").GetString().Should().Be("production");
        draft.GetProperty("taskType").GetString().Should().Be("doc_gen");
        draft.GetProperty("requiresProjectName").GetBoolean().Should().BeFalse();
        data.GetProperty("reply").GetString().Should().NotContain("請以 # 開頭提供專案名稱");
    }

    [Fact]
    public async Task Production_CreativeWritingWebsiteRequest_StillUsesCodeGen()
    {
        var userId = $"line-production-creative-site-{Guid.NewGuid():N}";

        using var response = await _fixture.SendHighLevelLineTextAsync("/建立 童話故事網站", userId);
        var draft = response.RootElement.GetProperty("data").GetProperty("draft");

        draft.GetProperty("taskType").GetString().Should().Be("code_gen");
        draft.GetProperty("requiresProjectName").GetBoolean().Should().BeTrue();
    }
}
