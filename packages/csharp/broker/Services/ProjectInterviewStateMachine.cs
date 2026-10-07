namespace Broker.Services;

public sealed class ProjectInterviewStateMachine
{
    public ProjectInterviewSessionState ApplyCommand(ProjectInterviewSessionState state, ProjectInterviewCommand command) =>
        (state.CurrentPhase, command) switch
        {
            (ProjectInterviewPhase.Idle, ProjectInterviewCommand.StartProjectInterview)
                => state with { CurrentPhase = ProjectInterviewPhase.CollectProjectName },
            // 批准只把設計交給建置確認（draft 等待 y）；建置任務建立後才以 BuildConfirmed 進入 Confirmed。
            (ProjectInterviewPhase.AwaitUserReview or ProjectInterviewPhase.AwaitBuildConfirmation, ProjectInterviewCommand.Approve)
                => state with { CurrentPhase = ProjectInterviewPhase.AwaitBuildConfirmation },
            (ProjectInterviewPhase.AwaitUserReview or ProjectInterviewPhase.AwaitBuildConfirmation, ProjectInterviewCommand.Revise)
                => state with { CurrentPhase = ProjectInterviewPhase.ReviseRequested },
            (_, ProjectInterviewCommand.Cancel)
                => state with { CurrentPhase = ProjectInterviewPhase.Cancelled },
            _ => throw new InvalidOperationException($"Command {command} not allowed from {state.CurrentPhase}.")
        };

    public ProjectInterviewSessionState Advance(ProjectInterviewSessionState state, ProjectInterviewAdvanceReason reason) =>
        (state.CurrentPhase, reason) switch
        {
            (ProjectInterviewPhase.CollectProjectName, ProjectInterviewAdvanceReason.ProjectNameAccepted) when state.HasUniqueProjectFolder
                => state with { CurrentPhase = ProjectInterviewPhase.ClassifyProjectScale },
            (ProjectInterviewPhase.ClassifyProjectScale, ProjectInterviewAdvanceReason.ProjectScaleConfirmed)
                => state with { CurrentPhase = ProjectInterviewPhase.NarrowTemplateFamily },
            (ProjectInterviewPhase.NarrowTemplateFamily, ProjectInterviewAdvanceReason.TemplateFamilyNarrowed)
                => state with { CurrentPhase = ProjectInterviewPhase.ConfirmTemplateFamily },
            (ProjectInterviewPhase.ConfirmTemplateFamily, ProjectInterviewAdvanceReason.TemplateFamilyConfirmed)
                => state with { CurrentPhase = ProjectInterviewPhase.CollectTemplateRequirements },
            (ProjectInterviewPhase.ReviseRequested, ProjectInterviewAdvanceReason.RevisionCaptured)
                => state with { CurrentPhase = ProjectInterviewPhase.CollectTemplateRequirements },
            (ProjectInterviewPhase.AwaitBuildConfirmation, ProjectInterviewAdvanceReason.BuildConfirmed)
                => state with { CurrentPhase = ProjectInterviewPhase.Confirmed },
            _ => throw new InvalidOperationException($"Advance {reason} not allowed from {state.CurrentPhase}.")
        };
}
