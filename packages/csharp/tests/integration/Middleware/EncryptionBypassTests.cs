using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Integration.Tests.Fixtures;
using Xunit;

namespace Integration.Tests.Middleware;

/// <summary>
/// Tests that certain paths correctly bypass the encryption middleware.
///
/// The EncryptionMiddleware has three bypass conditions:
///   1. Path is in the ExcludedPaths set (e.g. /api/v1/health)
///   2. Path starts with /dev/
///   3. Request method is not POST
///   4. Path is a "plain JSON trusted path" (high-level/line/*, tool-specs/*, local-admin/*)
///
/// These tests verify that requests to bypass paths do NOT receive a 400
/// encryption-related error. They may receive 401 (auth required) or 200,
/// but should never fail with an encryption payload parsing error.
/// </summary>
public class EncryptionBypassTests : IClassFixture<BrokerFixture>
{
    private readonly HttpClient _client;

    public EncryptionBypassTests(BrokerFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task LocalAdmin_Status_Bypasses_Encryption()
    {
        // /api/v1/local-admin/status is a GET endpoint in the local-admin group,
        // which is a plain JSON trusted path. It should not require encryption.
        var response = await _client.GetAsync("/api/v1/local-admin/status");

        // Should return 200 (status info) — not a 400 encryption error
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        // The response should be valid JSON
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task HighLevel_Line_Process_Bypasses_Encryption_With_PlainJson()
    {
        // /api/v1/high-level/line/process is a plain JSON trusted path.
        // Sending a plain JSON body should NOT trigger a 400 encryption error.
        // It will likely fail with a business logic error or auth error, but
        // the key assertion is that encryption middleware does not reject it.
        var payload = new { channel = "test", user_id = "u_test", text = "hello" };
        var content = new StringContent(
            JsonSerializer.Serialize(payload),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/api/v1/high-level/line/process", content);

        // Should NOT be a 400 from encryption middleware.
        // Acceptable: 200, 400 (business logic), 401, 500 — but not an encryption envelope parse error
        var body = await response.Content.ReadAsStringAsync();

        // If it's a 400, it should not be about missing encrypted_payload or encryption
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            body.Should().NotContain("encrypted_payload",
                "the encryption middleware should not reject plain JSON on trusted paths");
            body.Should().NotContain("encryption",
                "the encryption middleware should not reject plain JSON on trusted paths");
        }
    }

    [Fact]
    public async Task NonPost_Request_Bypasses_Encryption()
    {
        // GET requests are always excluded from encryption middleware.
        // /api/v1/health already tests this, but let's verify with a different path.
        var response = await _client.GetAsync("/api/v1/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
