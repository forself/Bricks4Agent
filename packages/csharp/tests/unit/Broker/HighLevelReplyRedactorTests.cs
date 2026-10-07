using Broker.Services;
using FluentAssertions;
using Xunit;

namespace Unit.Tests.Broker;

public class HighLevelReplyRedactorTests
{
    private const string WindowsRoot = @"C:\srv\b4a\managed";
    private const string PosixRoot = "/srv/b4a/managed";

    [Fact]
    public void RedactRoot_RewritesPathsUnderRootAsRelativeNames()
    {
        var text = $"workspace: {WindowsRoot}\\line\\demo-user\\documents\\a.md";

        var redacted = HighLevelReplyRedactor.RedactRoot(text, WindowsRoot);

        redacted.Should().Be("workspace: line/demo-user/documents/a.md");
    }

    [Fact]
    public void RedactRoot_MatchesForwardSlashAndCaseVariants()
    {
        var text = "file C:/SRV/b4a/managed/line/u1/projects/demo ready";

        var redacted = HighLevelReplyRedactor.RedactRoot(text, WindowsRoot);

        redacted.Should().Be("file line/u1/projects/demo ready");
    }

    [Fact]
    public void RedactRoot_RootItselfBecomesDot_AndSiblingPrefixIsUntouched()
    {
        HighLevelReplyRedactor.RedactRoot($"root={PosixRoot}.", PosixRoot).Should().Be("root=..");
        HighLevelReplyRedactor.RedactRoot($"other={PosixRoot}2/x", PosixRoot).Should().Be($"other={PosixRoot}2/x");
    }

    [Fact]
    public void RedactRoot_LeavesTextWithoutRootUnchanged()
    {
        HighLevelReplyRedactor.RedactRoot("已確認任務：demo", WindowsRoot).Should().Be("已確認任務：demo");
        HighLevelReplyRedactor.RedactRoot("text", null).Should().Be("text");
        HighLevelReplyRedactor.RedactRoot(null, WindowsRoot).Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"failed at D:\work\repo\tools\gen.mjs:12:5", "failed at gen.mjs:12:5")]
    [InlineData(@"cannot open \\fileserver\share\out\site.zip", "cannot open site.zip")]
    [InlineData("missing /var/lib/b4a/projects/demo/index.html", "missing index.html")]
    [InlineData("llm_timeout", "llm_timeout")]
    [InlineData("see https://example.com/a/b/c for details", "see https://example.com/a/b/c for details")]
    [InlineData("/proj command", "/proj command")]
    public void SanitizeDetail_KeepsOnlyLastSegmentOfAbsolutePaths(string input, string expected)
    {
        HighLevelReplyRedactor.SanitizeDetail(input).Should().Be(expected);
    }

    [Fact]
    public void LeafNameAndRelativeName_ReturnNamesWithoutHostLocation()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "b4a-redactor", "projects", "demo");
        var entry = Path.Combine(projectRoot, "frontend", "index.html");

        HighLevelReplyRedactor.LeafName(projectRoot + Path.DirectorySeparatorChar).Should().Be("demo");
        HighLevelReplyRedactor.RelativeName(projectRoot, entry).Should().Be("frontend/index.html");
        HighLevelReplyRedactor.RelativeName(Path.Combine(Path.GetTempPath(), "elsewhere"), entry).Should().Be("index.html");
        HighLevelReplyRedactor.LeafName(null).Should().BeEmpty();
    }
}
