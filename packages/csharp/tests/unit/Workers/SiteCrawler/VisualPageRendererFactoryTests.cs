using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class VisualPageRendererFactoryTests
{
    [Fact]
    public async Task Create_WhenVisualCaptureIsEnabledByDefault_ReturnsPlaywrightRenderer()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        await using var renderer = VisualPageRendererFactory.Create(config, NullLoggerFactory.Instance);

        renderer.Should().BeOfType<PlaywrightVisualPageRenderer>();
    }

    [Fact]
    public async Task Create_WhenVisualCaptureIsDisabled_ReturnsNull()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Crawler:Visual:Enabled"] = "false",
        });

        await using var renderer = VisualPageRendererFactory.Create(config, NullLoggerFactory.Instance);

        renderer.Should().BeNull();
    }

    private static IConfiguration BuildConfig(IReadOnlyDictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
