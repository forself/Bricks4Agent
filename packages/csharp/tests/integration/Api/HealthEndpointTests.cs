using System.Text.Json;
using FluentAssertions;
using Integration.Tests.Fixtures;
using Xunit;

namespace Integration.Tests.Api;

/// <summary>
/// Verifies the /api/v1/health endpoint, which is the simplest smoke test
/// for the Broker. It is excluded from encryption and auth middleware, so
/// it should always return 200 with a JSON body containing status, timestamp,
/// and the broker public key.
/// </summary>
public class HealthEndpointTests : IClassFixture<BrokerFixture>
{
    private readonly HttpClient _client;

    public HealthEndpointTests(BrokerFixture fixture)
    {
        _client = fixture.Client;
    }

    [Fact]
    public async Task Get_Health_Returns_200_With_Status_Ok()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/health");

        // Assert
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetString().Should().Be("ok");
        root.TryGetProperty("timestamp", out _).Should().BeTrue();
        root.TryGetProperty("broker_public_key", out var pubKey).Should().BeTrue();
        pubKey.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Post_Health_Returns_200_With_Status_Ok()
    {
        // The health endpoint also accepts POST for backward compatibility
        var response = await _client.PostAsync("/api/v1/health", null);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("status").GetString().Should().Be("ok");
    }
}
