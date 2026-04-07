using System.Net;
using FluentAssertions;
using Integration.Tests.Fixtures;
using Xunit;

namespace Integration.Tests.Api;

public class HighLevelWorkerAuthTests : IClassFixture<BrokerFixture>
{
    private readonly BrokerFixture _fixture;

    public HighLevelWorkerAuthTests(BrokerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HighLevel_Line_Process_WithoutWorkerHeaders_ReturnsUnauthorized()
    {
        var response = await _fixture.SendHighLevelLineTextUnsignedAsync("hello unauthenticated");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task HighLevel_Line_Process_WithSignedLineWorkerHeaders_Succeeds()
    {
        var response = await _fixture.SendHighLevelLineTextAsync("hello authenticated");

        response.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        response.RootElement.GetProperty("data").GetProperty("reply").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task HighLevel_Line_Process_WithWrongWorkerType_ReturnsForbidden()
    {
        var response = await _fixture.SendHighLevelLineTextAsWorkerRawAsync(
            "file-worker",
            "file-v1",
            "file-secret",
            "hello wrong worker");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task HighLevel_Line_Notifications_Pending_WithSignedLineWorkerHeaders_Succeeds()
    {
        var response = await _fixture.GetPendingNotificationsAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
