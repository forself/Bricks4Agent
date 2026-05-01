using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class PathDepthScopeTests
{
    [Fact]
    public void Create_NormalizesStartPathPrefixAndOrigin()
    {
        var scope = PathDepthScope.Create(
            new Uri("https://example.com/docs/"),
            new SiteCrawlScope { MaxDepth = 1 });

        scope.PathPrefix.Should().Be("/docs/");
        scope.Origin.Should().Be("https://example.com");
    }

    [Theory]
    [InlineData("https://example.com/docs/", true, 0, "")]
    [InlineData("https://example.com/docs/a", true, 1, "")]
    [InlineData("https://example.com/docs/b/", true, 1, "")]
    [InlineData("https://example.com/docs/a/detail", false, 2, "outside_path_depth")]
    [InlineData("https://example.com/other", false, -1, "outside_path_prefix")]
    [InlineData("https://other.example.com/docs/a", false, -1, "outside_origin")]
    public void Evaluate_AppliesSameOriginPathPrefixAndMaxDepth(
        string targetUrl,
        bool isAllowed,
        int depth,
        string reason)
    {
        var scope = PathDepthScope.Create(
            new Uri("https://example.com/docs/"),
            new SiteCrawlScope
            {
                MaxDepth = 1,
                SameOriginOnly = true,
                PathPrefixLock = true,
            });

        var result = scope.Evaluate(new Uri(targetUrl));

        result.IsAllowed.Should().Be(isAllowed);
        result.Depth.Should().Be(depth);
        result.Reason.Should().Be(reason);
    }

    [Fact]
    public void Evaluate_WithMaxDepthZero_AllowsOnlyPathPrefixRoot()
    {
        var scope = PathDepthScope.Create(
            new Uri("https://example.com/docs/"),
            new SiteCrawlScope
            {
                MaxDepth = 0,
                SameOriginOnly = true,
                PathPrefixLock = true,
            });

        var root = scope.Evaluate(new Uri("https://example.com/docs/"));
        var child = scope.Evaluate(new Uri("https://example.com/docs/a"));

        root.IsAllowed.Should().BeTrue();
        root.Depth.Should().Be(0);
        root.Reason.Should().BeEmpty();
        child.IsAllowed.Should().BeFalse();
        child.Depth.Should().Be(1);
        child.Reason.Should().Be("outside_path_depth");
    }

    [Fact]
    public void Evaluate_WhenSameOriginOnlyFalse_AllowsDifferentOriginWithinPathDepth()
    {
        var scope = PathDepthScope.Create(
            new Uri("https://example.com/docs/"),
            new SiteCrawlScope
            {
                MaxDepth = 1,
                SameOriginOnly = false,
                PathPrefixLock = true,
            });

        var result = scope.Evaluate(new Uri("https://other.example.com/docs/a"));

        result.IsAllowed.Should().BeTrue();
        result.Depth.Should().Be(1);
        result.Reason.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_WhenPathPrefixLockFalse_EvaluatesOutsidePrefixByDepth()
    {
        var scope = PathDepthScope.Create(
            new Uri("https://example.com/docs/"),
            new SiteCrawlScope
            {
                MaxDepth = 1,
                SameOriginOnly = true,
                PathPrefixLock = false,
            });

        var result = scope.Evaluate(new Uri("https://example.com/other/detail"));

        result.IsAllowed.Should().BeFalse();
        result.Depth.Should().Be(2);
        result.Reason.Should().Be("outside_path_depth");
    }
}
