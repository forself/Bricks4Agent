using BrokerCore.Services;
using FluentAssertions;
using Xunit;

namespace Unit.Tests.Core;

public class BrowserActionGateTests
{
    [Theory]
    [InlineData("read", "navigate")]
    [InlineData("navigate", "navigate")]
    [InlineData("read", "committed_action")]
    [InlineData("authenticate", "committed_action")]
    public void Evaluate_AllowsWhenWithinMax(string intended, string max)
    {
        var decision = BrowserActionGate.Evaluate(intended, max, null);

        decision.IsAllowed.Should().BeTrue();
        decision.Kind.Should().Be(BrowserActionDecisionKind.Allow);
    }

    [Theory]
    [InlineData("navigate", "read")]
    [InlineData("authenticate", "navigate")]
    [InlineData("committed_action", "draft_action")]
    public void Evaluate_DeniesWhenExceedingMax(string intended, string max)
    {
        var decision = BrowserActionGate.Evaluate(intended, max, null);

        decision.IsAllowed.Should().BeFalse();
        decision.Kind.Should().Be(BrowserActionDecisionKind.ExceedsMaxLevel);
    }

    [Fact]
    public void Evaluate_MissingMaxDefaultsToReadOnly()
    {
        // 缺少政策時上限保守視為 read：navigate 被擋，read 放行。
        BrowserActionGate.Evaluate("navigate", null, null).Kind
            .Should().Be(BrowserActionDecisionKind.ExceedsMaxLevel);
        BrowserActionGate.Evaluate("read", "", null).IsAllowed
            .Should().BeTrue();
    }

    [Fact]
    public void Evaluate_MissingIntendedDefaultsToRead()
    {
        var decision = BrowserActionGate.Evaluate(null, "navigate", null);

        decision.IsAllowed.Should().BeTrue();
        decision.IntendedLevel.Should().Be(BrowserActionLevel.Read);
    }

    [Fact]
    public void Evaluate_RequiresHumanConfirmationOnListedLevel()
    {
        var decision = BrowserActionGate.Evaluate(
            "navigate", "committed_action", new[] { "navigate", "authenticate" });

        decision.IsAllowed.Should().BeFalse();
        decision.Kind.Should().Be(BrowserActionDecisionKind.RequiresHumanConfirmation);
    }

    [Fact]
    public void Evaluate_UnknownMaxIsTreatedAsExceeding()
    {
        var decision = BrowserActionGate.Evaluate("read", "superuser", null);

        decision.IsAllowed.Should().BeFalse();
        decision.Kind.Should().Be(BrowserActionDecisionKind.ExceedsMaxLevel);
        decision.MaxLevel.Should().Be(BrowserActionLevel.Read);
    }

    [Fact]
    public void Evaluate_UnknownIntendedIsRejected()
    {
        var decision = BrowserActionGate.Evaluate("teleport", "committed_action", null);

        decision.IsAllowed.Should().BeFalse();
        decision.Kind.Should().Be(BrowserActionDecisionKind.UnknownIntendedLevel);
    }
}
