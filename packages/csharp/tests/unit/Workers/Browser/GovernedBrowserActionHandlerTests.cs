using System.Text.Json;
using BrowserWorker;
using BrowserWorker.Handlers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Unit.Tests.Workers.Browser;

public class GovernedBrowserActionHandlerTests
{
    private static GovernedBrowserActionHandler Handler(FakeFetcher fetcher)
        => new(fetcher, NullLogger<GovernedBrowserActionHandler>.Instance);

    private static string Payload(object body)
        => JsonSerializer.Serialize(body);

    [Fact]
    public async Task ReadWithinPolicy_ExecutesFetch()
    {
        var fetcher = new FakeFetcher();
        var result = await Handler(fetcher).ExecuteAsync("req1", "browser_navigate", Payload(new
        {
            start_url = "https://example.com/",
            intended_action_level = "read",
            max_action_level = "navigate"
        }), "{}", CancellationToken.None);

        result.Success.Should().BeTrue();
        fetcher.FetchCalls.Should().Be(1);
        fetcher.NavigateCalls.Should().Be(0);
        using var doc = JsonDocument.Parse(result.ResultPayload!);
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("executed");
        doc.RootElement.GetProperty("action_level_reached").GetString().Should().Be("read");
    }

    [Fact]
    public async Task NavigateWithinPolicy_ExecutesNavigation()
    {
        var fetcher = new FakeFetcher();
        var result = await Handler(fetcher).ExecuteAsync("req2", "browser_navigate", Payload(new
        {
            start_url = "https://example.com/",
            intended_action_level = "navigate",
            max_action_level = "navigate",
            args = new { max_steps = 2 }
        }), "{}", CancellationToken.None);

        result.Success.Should().BeTrue();
        fetcher.NavigateCalls.Should().Be(1);
        fetcher.FetchCalls.Should().Be(0);
        fetcher.LastMaxSteps.Should().Be(2);
        using var doc = JsonDocument.Parse(result.ResultPayload!);
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("executed");
        doc.RootElement.GetProperty("action_level_reached").GetString().Should().Be("navigate");
        // max_steps=2 means the start page plus up to 2 followed links.
        doc.RootElement.GetProperty("steps").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task NavigateExceedingMax_GatedWithoutExecuting()
    {
        var fetcher = new FakeFetcher();
        var result = await Handler(fetcher).ExecuteAsync("req3", "browser_navigate", Payload(new
        {
            start_url = "https://example.com/",
            intended_action_level = "navigate",
            max_action_level = "read"
        }), "{}", CancellationToken.None);

        result.Success.Should().BeTrue();
        fetcher.FetchCalls.Should().Be(0);
        fetcher.NavigateCalls.Should().Be(0); // nothing executed
        using var doc = JsonDocument.Parse(result.ResultPayload!);
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("gated");
        doc.RootElement.GetProperty("gate_decision").GetString().Should().Be("ExceedsMaxLevel");
    }

    [Fact]
    public async Task AuthenticateLevel_AlwaysGated()
    {
        var fetcher = new FakeFetcher();
        var result = await Handler(fetcher).ExecuteAsync("req4", "browser_navigate", Payload(new
        {
            start_url = "https://example.com/login",
            intended_action_level = "authenticate",
            max_action_level = "committed_action",
            requires_human_confirmation_on = new[] { "authenticate", "draft_action", "committed_action" }
        }), "{}", CancellationToken.None);

        result.Success.Should().BeTrue();
        fetcher.FetchCalls.Should().Be(0);
        fetcher.NavigateCalls.Should().Be(0);
        using var doc = JsonDocument.Parse(result.ResultPayload!);
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("gated");
        doc.RootElement.GetProperty("requires_human_confirmation").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task MissingPolicy_DefaultsToReadOnly()
    {
        var fetcher = new FakeFetcher();
        // intended navigate, but no max policy -> gate defaults max to read -> gated
        var result = await Handler(fetcher).ExecuteAsync("req5", "browser_navigate", Payload(new
        {
            start_url = "https://example.com/",
            intended_action_level = "navigate"
        }), "{}", CancellationToken.None);

        fetcher.NavigateCalls.Should().Be(0);
        using var doc = JsonDocument.Parse(result.ResultPayload!);
        doc.RootElement.GetProperty("outcome").GetString().Should().Be("gated");
    }

    [Fact]
    public async Task MissingStartUrl_Rejected()
    {
        var result = await Handler(new FakeFetcher()).ExecuteAsync("req6", "browser_navigate",
            "{\"intended_action_level\":\"read\"}", "{}", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("start_url");
    }

    [Fact]
    public async Task NonHttpUrl_Rejected()
    {
        var result = await Handler(new FakeFetcher()).ExecuteAsync("req7", "browser_navigate",
            Payload(new { start_url = "file:///etc/passwd", intended_action_level = "read", max_action_level = "read" }),
            "{}", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("http");
    }

    private sealed class FakeFetcher : IBrowserPageFetcher
    {
        public int FetchCalls { get; private set; }
        public int NavigateCalls { get; private set; }
        public int LastMaxSteps { get; private set; }

        public Task<BrowserPageResult> FetchPageAsync(string url, string? userAgent = null, CancellationToken cancellationToken = default)
        {
            FetchCalls++;
            return Task.FromResult(new BrowserPageResult
            {
                Success = true,
                StatusCode = 200,
                FinalUrl = url,
                Title = "Example",
                TextContent = "hello"
            });
        }

        public Task<BrowserNavigationResult> NavigateAsync(string startUrl, int maxSteps,
            IReadOnlyCollection<string>? allowedHostSuffixes = null, string? userAgent = null, CancellationToken cancellationToken = default)
        {
            NavigateCalls++;
            LastMaxSteps = maxSteps;
            var steps = new List<BrowserPageResult>();
            for (var i = 0; i <= maxSteps; i++)
            {
                steps.Add(new BrowserPageResult { Success = true, StatusCode = 200, FinalUrl = $"{startUrl}#{i}", Title = $"Step {i}" });
            }
            return Task.FromResult(BrowserNavigationResult.Ok(steps));
        }
    }
}
