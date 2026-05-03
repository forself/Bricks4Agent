using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
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
    public async Task Create_WhenUsingDefaults_DoesNotBlockImageResources()
    {
        var config = BuildConfig(new Dictionary<string, string?>());

        await using var renderer = VisualPageRendererFactory.Create(config, NullLoggerFactory.Instance);

        GetOptions(renderer!).BlockHeavyResources.Should().BeFalse();
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

    private static VisualPageRendererOptions GetOptions(IVisualPageRenderer renderer)
    {
        var field = typeof(PlaywrightVisualPageRenderer).GetField("options", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        return (VisualPageRendererOptions)field!.GetValue(renderer)!;
    }
}
