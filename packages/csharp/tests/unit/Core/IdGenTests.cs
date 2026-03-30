using BrokerCore;
using FluentAssertions;

namespace Unit.Tests.Core;

public class IdGenTests
{
    [Theory]
    [InlineData("msg")]
    [InlineData("conv")]
    [InlineData("fn")]
    [InlineData("task")]
    [InlineData("ses")]
    [InlineData("plan")]
    [InlineData("node")]
    [InlineData("ckpt")]
    [InlineData("grt")]
    [InlineData("wkr")]
    public void New_WithPrefix_StartsWithPrefix(string prefix)
    {
        var id = IdGen.New(prefix);
        id.Should().StartWith($"{prefix}_");
    }

    [Fact]
    public void New_GeneratesUniqueIds()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => IdGen.New("test")).ToList();
        ids.Distinct().Count().Should().Be(1000);
    }

    [Fact]
    public void New_HasThreeUnderscoreSeparatedParts()
    {
        var id = IdGen.New("x");
        // Format: {prefix}_{timestamp_hex}_{random_hex}
        var parts = id.Split('_');
        parts.Should().HaveCount(3);
        parts[0].Should().Be("x");
        parts[1].Should().HaveLength(12, "48-bit timestamp = 12 hex chars");
        parts[2].Should().HaveLength(16, "64-bit random = 16 hex chars");
    }
}
