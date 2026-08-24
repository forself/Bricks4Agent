using System.Net;
using System.Net.Http.Json;
using Broker.Services;
using Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Tests.Api;

public class LocalAdminPermissionEndpointTests : IClassFixture<BrokerFixture>
{
    private readonly BrokerFixture _fixture;

    public LocalAdminPermissionEndpointTests(BrokerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SystemAdmin_CanReadSystemStatus_ButCannotListOperators()
    {
        CreateOperator("sysadmin", LocalAdminRoles.SystemAdmin);
        var cookie = await LoginAsync("sysadmin", "system-password");

        using var status = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/local-admin/system/status", cookie);
        status.StatusCode.Should().Be(HttpStatusCode.OK);

        using var operators = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/local-admin/operators", cookie);
        operators.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PermissionAdmin_CanListOperators_ButCannotPreviewDeployment()
    {
        CreateOperator("permadmin", LocalAdminRoles.PermissionAdmin);
        var cookie = await LoginAsync("permadmin", "system-password");

        using var operators = await SendWithCookieAsync(HttpMethod.Get, "/api/v1/local-admin/operators", cookie);
        operators.StatusCode.Should().Be(HttpStatusCode.OK);

        using var deployment = await SendWithCookieAsync(
            HttpMethod.Post,
            "/api/v1/local-admin/deployment/preview",
            cookie,
            new { target_id = "missing", project_path = "site.zip" });
        deployment.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Auditor_CannotApproveRequests()
    {
        CreateOperator("auditor", LocalAdminRoles.Auditor);
        var cookie = await LoginAsync("auditor", "system-password");

        using var response = await SendWithCookieAsync(
            HttpMethod.Post,
            "/api/v1/local-admin/approvals/apr_missing/approve",
            cookie,
            new { reason = "nope" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private void CreateOperator(string username, string role)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var auth = scope.ServiceProvider.GetRequiredService<LocalAdminAuthService>();
        if (auth.ListOperators().Any(o => string.Equals(o.Username, username, StringComparison.OrdinalIgnoreCase)))
            return;

        auth.CreateOperator(username, username, role, "system-password", "test");
    }

    private async Task<string> LoginAsync(string username, string password)
    {
        using var response = await _fixture.Client.PostAsJsonAsync("/api/v1/local-admin/login", new
        {
            username,
            password
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.TryGetValues("Set-Cookie", out var values).Should().BeTrue();
        var cookie = values!.First(value => value.StartsWith($"{LocalAdminAuthService.SessionCookieName}=", StringComparison.Ordinal));
        return cookie.Split(';', 2)[0];
    }

    private Task<HttpResponseMessage> SendWithCookieAsync(HttpMethod method, string path, string cookie, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("Cookie", cookie);
        if (body != null)
            request.Content = JsonContent.Create(body);
        return _fixture.Client.SendAsync(request);
    }
}
