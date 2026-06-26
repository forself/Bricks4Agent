using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Broker.Services;
using Integration.Tests.Fixtures;

namespace Integration.Tests.Api;

public class PortalEndpointTests : IClassFixture<BrokerFixture>
{
    private readonly BrokerFixture _fixture;

    public PortalEndpointTests(BrokerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Portal_RequiresLoginForUserResources()
    {
        using var response = await _fixture.Client.GetAsync("/api/v1/portal/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Portal_StaticEntryPointIsServedByBroker()
    {
        using var response = await _fixture.Client.GetAsync("/portal/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Bricks4Agent Portal");
        html.Should().Contain("./src/PortalApp.js");
    }

    [Fact]
    public async Task Portal_RegisterCommandAndResultsRoundTrip()
    {
        var userId = $"portal-user-{Guid.NewGuid():N}"[..30];
        using var register = await _fixture.Client.PostAsJsonAsync("/api/v1/portal/auth/register", new
        {
            user_id = userId,
            password = "correct-horse-battery",
            display_name = "Portal User"
        });

        register.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = ReadPortalCookie(register);

        using var command = new HttpRequestMessage(HttpMethod.Post, "/api/v1/portal/commands")
        {
            Content = JsonContent.Create(new { message = "?profile" })
        };
        command.Headers.Add("Cookie", cookie);
        using var commandResponse = await _fixture.Client.SendAsync(command);

        commandResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var commandJson = JsonDocument.Parse(await commandResponse.Content.ReadAsStringAsync());
        var commandData = commandJson.RootElement.GetProperty("data");
        commandData.GetProperty("user_id").GetString().Should().Be(userId);
        commandData.GetProperty("result").GetProperty("reply").GetString().Should().NotBeNullOrWhiteSpace();

        using var results = new HttpRequestMessage(HttpMethod.Get, "/api/v1/portal/results?limit=5");
        results.Headers.Add("Cookie", cookie);
        using var resultsResponse = await _fixture.Client.SendAsync(results);

        resultsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var resultsJson = JsonDocument.Parse(await resultsResponse.Content.ReadAsStringAsync());
        var items = resultsJson.RootElement.GetProperty("data").GetProperty("items");
        items.GetArrayLength().Should().BeGreaterThan(0);
        items.EnumerateArray()
            .Should()
            .Contain(item => item.GetProperty("user_message").GetString() == "?profile");
    }

    private static string ReadPortalCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var values).Should().BeTrue();
        var cookie = values!.FirstOrDefault(value => value.StartsWith($"{PortalAuthService.SessionCookieName}=", StringComparison.Ordinal));
        cookie.Should().NotBeNullOrWhiteSpace();
        return cookie!.Split(';', 2)[0];
    }
}
