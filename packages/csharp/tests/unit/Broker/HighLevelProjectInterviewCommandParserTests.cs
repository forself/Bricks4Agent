using Broker.Services;
using FluentAssertions;
using Xunit;

namespace Unit.Tests.Broker;

public sealed class HighLevelProjectInterviewCommandParserTests
{
    private readonly HighLevelCommandParser _parser = new(new HighLevelCoordinatorOptions());

    [Theory]
    [InlineData("/revise project_scale=tool_page template_family=content_showcase", "project_scale=tool_page template_family=content_showcase")]
    [InlineData("/revise", "")]
    [InlineData("／revise template_family=crud_admin", "template_family=crud_admin")]
    public void ParseProjectInterviewCommand_RecognizesBareAndExplicitRevision(string input, string expectedInput)
    {
        var parsed = _parser.ParseProjectInterviewCommand(input);

        parsed.IsProjectInterview.Should().BeTrue();
        parsed.Command.Should().Be(ProjectInterviewCommand.Revise);
        parsed.RevisionInput.Should().Be(expectedInput);
    }

    [Fact]
    public void ParseProjectInterviewCommand_DoesNotTreatLongerCommandPrefixAsRevise()
    {
        var parsed = _parser.ParseProjectInterviewCommand("/reviser project_scale=tool_page");

        parsed.IsProjectInterview.Should().BeFalse();
        parsed.Command.Should().BeNull();
    }

    [Theory]
    [InlineData(ProjectInterviewPhase.Idle)]
    [InlineData(ProjectInterviewPhase.CollectProjectName)]
    [InlineData(ProjectInterviewPhase.ClassifyProjectScale)]
    [InlineData(ProjectInterviewPhase.NarrowTemplateFamily)]
    [InlineData(ProjectInterviewPhase.ReviseRequested)]
    [InlineData(ProjectInterviewPhase.Confirmed)]
    [InlineData(ProjectInterviewPhase.Cancelled)]
    [InlineData(ProjectInterviewPhase.Failed)]
    [InlineData(ProjectInterviewPhase.Expired)]
    public void ResolveFor_ReviseWithText_IsNotAnInterviewCommand_WithoutARevisableVersion(ProjectInterviewPhase phase)
    {
        // 即使文件裡留著先前編譯過的定義，階段不對就不是修訂。
        var document = DocumentIn(phase, withDefinition: true);

        var resolved = _parser.ParseProjectInterviewCommand("/revise the second paragraph").ResolveFor(document);

        resolved.IsProjectInterview.Should().BeFalse("the message keeps the path it had before /revise accepted text");
        resolved.Command.Should().BeNull();
        resolved.RevisionInput.Should().BeNull();
    }

    [Theory]
    [InlineData(ProjectInterviewPhase.AwaitUserReview)]
    [InlineData(ProjectInterviewPhase.AwaitBuildConfirmation)]
    public void ResolveFor_ReviseWithText_IsNotAnInterviewCommand_WhenNothingWasCompiledYet(ProjectInterviewPhase phase)
    {
        var resolved = _parser.ParseProjectInterviewCommand("/revise project_scale=tool_page")
            .ResolveFor(DocumentIn(phase, withDefinition: false));

        resolved.IsProjectInterview.Should().BeFalse();
    }

    [Theory]
    [InlineData(ProjectInterviewPhase.AwaitUserReview, "/revise project_scale=tool_page", "project_scale=tool_page")]
    [InlineData(ProjectInterviewPhase.AwaitBuildConfirmation, "/revise template_family=crud_admin", "template_family=crud_admin")]
    // 格式不對的內容仍然是訪談修訂：由修訂流程回報錯誤，不能悄悄改走其他路徑。
    [InlineData(ProjectInterviewPhase.AwaitUserReview, "/revise make it blue", "make it blue")]
    [InlineData(ProjectInterviewPhase.AwaitBuildConfirmation, "／revise project_scale", "project_scale")]
    public void ResolveFor_ReviseWithText_IsTheInterviewRevision_WhenAVersionAwaitsReviewOrBuild(
        ProjectInterviewPhase phase, string input, string expectedInput)
    {
        var resolved = _parser.ParseProjectInterviewCommand(input).ResolveFor(DocumentIn(phase, withDefinition: true));

        resolved.IsProjectInterview.Should().BeTrue();
        resolved.Command.Should().Be(ProjectInterviewCommand.Revise);
        resolved.RevisionInput.Should().Be(expectedInput);
    }

    [Theory]
    [InlineData("/revise", ProjectInterviewCommand.Revise)]
    [InlineData("/proj", ProjectInterviewCommand.StartProjectInterview)]
    [InlineData("/ok", ProjectInterviewCommand.Approve)]
    [InlineData("/cancel", ProjectInterviewCommand.Cancel)]
    public void ResolveFor_LeavesBareCommandsAlone_WhateverTheInterviewState(string input, ProjectInterviewCommand expected)
    {
        foreach (var phase in Enum.GetValues<ProjectInterviewPhase>())
        {
            var resolved = _parser.ParseProjectInterviewCommand(input).ResolveFor(DocumentIn(phase, withDefinition: false));

            resolved.IsProjectInterview.Should().BeTrue();
            resolved.Command.Should().Be(expected);
        }
    }

    [Fact]
    public void ResolveFor_LeavesNonInterviewMessagesAlone()
    {
        var resolved = _parser.ParseProjectInterviewCommand("/reviser project_scale=tool_page")
            .ResolveFor(DocumentIn(ProjectInterviewPhase.AwaitUserReview, withDefinition: true));

        resolved.IsProjectInterview.Should().BeFalse();
        resolved.Command.Should().BeNull();
    }

    private static ProjectInterviewTaskDocument DocumentIn(ProjectInterviewPhase phase, bool withDefinition)
    {
        var document = ProjectInterviewTaskDocument.CreateEmpty("line", "user-1");
        document = document.WithSessionState(document.SessionState with { CurrentPhase = phase });
        return withDefinition
            ? document.WithCompiledDefinition(1, new ProjectInstanceDefinition("mini_app", "member_portal", Array.Empty<string>(), "default"))
            : document;
    }
}
