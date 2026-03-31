namespace Broker.Services;

public sealed class ProjectInterviewStateMachine
{
    public ProjectInterviewSessionState ApplyCommand(ProjectInterviewSessionState state, ProjectInterviewCommand command) =>
        (state.CurrentPhase, command) switch
        {
            (ProjectInterviewPhase.Idle, ProjectInterviewCommand.StartProjectInterview)
                => state with { CurrentPhase = ProjectInterviewPhase.CollectProjectName },
            (ProjectInterviewPhase.AwaitUserReview, ProjectInterviewCommand.Approve)
                => state with { CurrentPhase = ProjectInterviewPhase.Confirmed },
            (ProjectInterviewPhase.AwaitUserReview, ProjectInterviewCommand.Revise)
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
            _ => throw new InvalidOperationException($"Advance {reason} not allowed from {state.CurrentPhase}.")
        };
}
