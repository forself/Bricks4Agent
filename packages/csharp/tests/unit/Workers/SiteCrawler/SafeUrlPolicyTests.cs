using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SafeUrlPolicyTests
{
    [Theory]
    [InlineData("https://example.com/docs", "https://example.com/docs")]
    [InlineData("http://example.com/", "http://example.com/")]
    public void Validate_AllowsHttpAndHttpsUrls(string rawUrl, string expectedUrl)
    {
        var result = SafeUrlPolicy.Validate(rawUrl);

        result.IsAllowed.Should().BeTrue();
        result.Uri.Should().NotBeNull();
        result.Uri!.ToString().Should().Be(expectedUrl);
        result.Reason.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_RequiresUrl(string? rawUrl)
    {
        var result = SafeUrlPolicy.Validate(rawUrl);

        result.IsAllowed.Should().BeFalse();
        result.Uri.Should().BeNull();
        result.Reason.Should().Be("url_required");
    }

    [Theory]
    [InlineData("example.com/docs")]
    [InlineData("/docs")]
    [InlineData("https://")]
    public void Validate_RejectsInvalidOrRelativeUrl(string rawUrl)
    {
        var result = SafeUrlPolicy.Validate(rawUrl);

        result.IsAllowed.Should().BeFalse();
        result.Uri.Should().BeNull();
        result.Reason.Should().Be("invalid_url");
    }

    [Theory]
    [InlineData("file:///c:/secret.txt")]
    [InlineData("data:text/plain,hello")]
    [InlineData("ftp://example.com/file")]
    public void Validate_RejectsUnsupportedSchemes(string rawUrl)
    {
        var result = SafeUrlPolicy.Validate(rawUrl);

        result.IsAllowed.Should().BeFalse();
        result.Uri.Should().BeNull();
        result.Reason.Should().Be("unsupported_scheme");
    }

    [Theory]
    [InlineData("https://localhost/admin")]
    [InlineData("http://localhost./")]
    [InlineData("http://localhost\u3002/")]
    [InlineData("http://\u24db\u24de\u24d2\u24d0\u24db\u24d7\u24de\u24e2\u24e3/")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.0.0.1./")]
    [InlineData("http://\uff11\uff12\uff17.\uff10.\uff10.\uff11/")]
    [InlineData("http://127\u30020\u30020\u30021/")]
    [InlineData("http://[::1]/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://10.0.0.1./")]
    [InlineData("http://172.16.0.1/")]
    [InlineData("http://192.168.0.1/")]
    [InlineData("http://224.0.0.1/")]
    [InlineData("http://239.255.255.255/")]
    [InlineData("http://[::ffff:127.0.0.1]/")]
    [InlineData("http://[::ffff:10.0.0.1]/")]
    [InlineData("http://[::ffff:224.0.0.1]/")]
    [InlineData("http://[::127.0.0.1]/")]
    [InlineData("http://[::10.0.0.1]/")]
    [InlineData("http://[::192.168.0.1]/")]
    [InlineData("http://[::224.0.0.1]/")]
    [InlineData("http://[fe80::1%25lo0]/")]
    [InlineData("http://[fec0::1%25lo0]/")]
    [InlineData("http://[ff00::1%25lo0]/")]
    [InlineData("http://[::1%25lo0]/")]
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://[fd00::1]/")]
    public void Validate_RejectsUnsafeHosts(string rawUrl)
    {
        var result = SafeUrlPolicy.Validate(rawUrl);

        result.IsAllowed.Should().BeFalse();
        result.Uri.Should().BeNull();
        result.Reason.Should().Be("blocked_host");
    }

    [Fact]
    public void Validate_NormalizesByTrimmingClearingFragmentAndEnsuringPath()
    {
        var result = SafeUrlPolicy.Validate("  https://example.com#section  ");

        result.IsAllowed.Should().BeTrue();
        result.Uri.Should().NotBeNull();
        result.Uri!.ToString().Should().Be("https://example.com/");
        result.Reason.Should().BeEmpty();
    }
}
