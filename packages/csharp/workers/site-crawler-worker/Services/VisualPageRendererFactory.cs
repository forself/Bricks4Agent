using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace SiteCrawlerWorker.Services;

public static class VisualPageRendererFactory
{
    public static IVisualPageRenderer? Create(IConfiguration config, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        if (!config.GetValue("Crawler:Visual:Enabled", true))
        {
            return null;
        }

        var defaults = new VisualPageRendererOptions();
        var options = new VisualPageRendererOptions
        {
            Headless = config.GetValue("Crawler:Visual:Headless", defaults.Headless),
            ViewportWidth = config.GetValue("Crawler:Visual:ViewportWidth", defaults.ViewportWidth),
            ViewportHeight = config.GetValue("Crawler:Visual:ViewportHeight", defaults.ViewportHeight),
            DeviceScaleFactor = config.GetValue("Crawler:Visual:DeviceScaleFactor", defaults.DeviceScaleFactor),
            DefaultTimeoutMs = config.GetValue("Crawler:Visual:DefaultTimeoutMs", defaults.DefaultTimeoutMs),
            NavigationTimeoutMs = config.GetValue("Crawler:Visual:NavigationTimeoutMs", defaults.NavigationTimeoutMs),
            PostNavigationSettleMs = config.GetValue("Crawler:Visual:PostNavigationSettleMs", defaults.PostNavigationSettleMs),
            NetworkIdleTimeoutMs = config.GetValue("Crawler:Visual:NetworkIdleTimeoutMs", defaults.NetworkIdleTimeoutMs),
            UserAgent = config.GetValue("Crawler:Visual:UserAgent", defaults.UserAgent) ?? defaults.UserAgent,
            BlockHeavyResources = config.GetValue("Crawler:Visual:BlockHeavyResources", defaults.BlockHeavyResources),
            MaxRegions = config.GetValue("Crawler:Visual:MaxRegions", defaults.MaxRegions),
            MaxItemsPerRegion = config.GetValue("Crawler:Visual:MaxItemsPerRegion", defaults.MaxItemsPerRegion),
            MaxRegionTextLength = config.GetValue("Crawler:Visual:MaxRegionTextLength", defaults.MaxRegionTextLength),
        };

        return new PlaywrightVisualPageRenderer(
            options,
            loggerFactory.CreateLogger<PlaywrightVisualPageRenderer>());
    }
}
