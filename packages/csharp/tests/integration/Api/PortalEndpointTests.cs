using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;
using Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task PortalRegistrationCode_BindsLineUserBeforeLineAccess()
    {
        var userId = $"line-bind-{Guid.NewGuid():N}"[..30];
        var rawLineUserId = "U" + Guid.NewGuid().ToString("N");
        using var register = await _fixture.Client.PostAsJsonAsync("/api/v1/portal/auth/register", new
        {
            user_id = userId,
            password = "correct-horse-battery",
            display_name = "Line Bind User"
        });

        register.StatusCode.Should().Be(HttpStatusCode.OK);
        using var registerJson = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var lineVerification = registerJson.RootElement
            .GetProperty("data")
            .GetProperty("line_verification");
        var code = lineVerification.GetProperty("code").GetString();
        code.Should().MatchRegex("^\\d{6}$");
        lineVerification.GetProperty("command").GetString().Should().Contain(userId).And.Contain(code);
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
            var credential = db.Get<PortalUserCredential>(userId);
            credential.Should().NotBeNull();
            credential!.LineVerificationCodeHash.Should().NotBeNullOrWhiteSpace();
            credential.LineVerificationCodeHash.Should().NotBe(code);
            credential.LineVerificationCodeExpiresAt.Should().BeAfter(DateTime.UtcNow);
        }

        using var denied = await _fixture.SendHighLevelLineTextAsync("?profile", rawLineUserId);
        denied.RootElement.GetProperty("data").GetProperty("error").GetString().Should().Be("line_verification_required");

        using var wrongCode = await _fixture.SendHighLevelLineTextAsync($"/verify {userId} 000000", rawLineUserId);
        wrongCode.RootElement.GetProperty("data").GetProperty("error").GetString().Should().Be("line_verification_failed");

        using var verified = await _fixture.SendHighLevelLineTextAsync($"/verify {userId} {code}", rawLineUserId);
        var verifiedData = verified.RootElement.GetProperty("data");
        verifiedData.GetProperty("error").GetString().Should().BeNullOrEmpty();
        verifiedData.GetProperty("effective_user_id").GetString().Should().Be(userId);

        using var profile = await _fixture.SendHighLevelLineTextAsync("?profile", rawLineUserId);
        var profileData = profile.RootElement.GetProperty("data");
        profileData.GetProperty("error").GetString().Should().BeNullOrEmpty();
        profileData.GetProperty("effective_user_id").GetString().Should().Be(userId);
        profileData.GetProperty("reply").GetString().Should().Contain(userId);
    }

    [Fact]
    public async Task PortalLineVerificationCode_CanBeReissuedFromPortalSession()
    {
        var userId = $"line-reissue-{Guid.NewGuid():N}"[..30];
        using var register = await _fixture.Client.PostAsJsonAsync("/api/v1/portal/auth/register", new
        {
            user_id = userId,
            password = "correct-horse-battery",
            display_name = "Line Reissue User"
        });

        register.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = ReadPortalCookie(register);

        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/portal/me");
        me.Headers.Add("Cookie", cookie);
        using var meResponse = await _fixture.Client.SendAsync(me);
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var meJson = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        var status = meJson.RootElement.GetProperty("data").GetProperty("line_verification");
        status.GetProperty("verified").GetBoolean().Should().BeFalse();
        status.GetProperty("command_template").GetString().Should().Be("/verify <user_id> <code>");

        using var reissue = new HttpRequestMessage(HttpMethod.Post, "/api/v1/portal/auth/line-verification");
        reissue.Headers.Add("Cookie", cookie);
        using var reissueResponse = await _fixture.Client.SendAsync(reissue);
        reissueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var reissueJson = JsonDocument.Parse(await reissueResponse.Content.ReadAsStringAsync());
        var issue = reissueJson.RootElement.GetProperty("data");
        issue.GetProperty("code").GetString().Should().MatchRegex("^\\d{6}$");
        issue.GetProperty("command").GetString().Should().Contain(userId);
        issue.GetProperty("expires_at").GetString().Should().NotBeNullOrWhiteSpace();
    }

    private static string ReadPortalCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var values).Should().BeTrue();
        var cookie = values!.FirstOrDefault(value => value.StartsWith($"{PortalAuthService.SessionCookieName}=", StringComparison.Ordinal));
        cookie.Should().NotBeNullOrWhiteSpace();
        return cookie!.Split(';', 2)[0];
    }
}
