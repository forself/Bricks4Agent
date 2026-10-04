using System.Net;
using System.Text.Json;
using Integration.Tests.Fixtures;

namespace Integration.Tests.Api;

/// <summary>
/// The LINE worker routes accept only a verified worker signature. With <c>WorkerAuth:Enforce=false</c>
/// no signature is verified, so these routes must answer 401 to every request: without credentials,
/// with an administrator scoped token, and even with a correctly signed request. The expected message
/// is the one written by the broker's scoped-token layer for worker routes, which shows the rejection
/// does not come from the worker-signature layer (that layer is off on this host) and that the request
/// never falls back to scoped-token authentication.
/// </summary>
public sealed class WorkerSignedRouteWithoutEnforcementTests : IClassFixture<WorkerAuthNotEnforcedFixture>
{
    private const string AdminRole = "role_admin";
    private const string WorkerAuthenticationRequired = "Worker authentication required.";

    private const string Unsigned = "unsigned";
    private const string AdminBearer = "admin-bearer";
    private const string WorkerSigned = "worker-signed";

    private readonly WorkerAuthNotEnforcedFixture _fixture;
    private readonly EncryptedBrokerClient _client;

    public WorkerSignedRouteWithoutEnforcementTests(WorkerAuthNotEnforcedFixture fixture)
    {
        _fixture = fixture;
        _client = new EncryptedBrokerClient(fixture.Client, fixture.Services);
    }

    [Theory]
    [InlineData("POST", "/api/v1/high-level/line/process", Unsigned)]
    [InlineData("POST", "/api/v1/high-level/line/process", AdminBearer)]
    [InlineData("POST", "/api/v1/high-level/line/process", WorkerSigned)]
    [InlineData("GET", "/api/v1/high-level/line/notifications/pending", Unsigned)]
    [InlineData("GET", "/api/v1/high-level/line/notifications/pending", AdminBearer)]
    [InlineData("GET", "/api/v1/high-level/line/notifications/pending", WorkerSigned)]
    [InlineData("POST", "/api/v1/high-level/line/notifications/complete", Unsigned)]
    [InlineData("POST", "/api/v1/high-level/line/notifications/complete", AdminBearer)]
    [InlineData("POST", "/api/v1/high-level/line/notifications/complete", WorkerSigned)]
    [InlineData("post", "/api/v1/high-level/line/process", AdminBearer)]
    [InlineData("GET", "/api/v1/high-level/line/notifications/pending/", AdminBearer)]
    [InlineData("POST", "/api/v1/high-level/line/notifications/complete/", WorkerSigned)]
    public async Task WorkerSignedRoute_WhenWorkerAuthIsNotEnforced_IsAlwaysUnauthorized(
        string method,
        string path,
        string credential)
    {
        _fixture.WorkerAuthEnforced.Should().BeFalse("this host must run with worker authentication switched off");
        var body = BodyFor(method, path);

        var reply = credential switch
        {
            Unsigned => await _client.SendPlainAsync(method, path, body),
            AdminBearer => await _client.SendPlainAsync(method, path, body, bearerToken: _client.OpenSession(AdminRole).ScopedToken),
            WorkerSigned => await _fixture.SendLineWorkerSignedAsync(new HttpMethod(method), path, body),
            _ => throw new ArgumentOutOfRangeException(nameof(credential), credential, "Unknown credential kind.")
        };

        reply.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the response was {0}", reply);
        reply.Message.Should().Be(WorkerAuthenticationRequired, "the response was {0}", reply);
    }

    private static string? BodyFor(string method, string path)
    {
        if (!string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return path.Contains("/notifications/complete", StringComparison.Ordinal)
            ? JsonSerializer.Serialize(new { notification_id = $"ntf_authz_{Guid.NewGuid():N}", status = "sent" })
            : JsonSerializer.Serialize(new { user_id = $"authz-line-user-{Guid.NewGuid():N}", message = "hello" });
    }
}
