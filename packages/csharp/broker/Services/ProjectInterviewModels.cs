namespace Broker.Services;

public enum ProjectInterviewPhase
{
    Idle = 0,
    CollectProjectName = 1,
    ClassifyProjectScale = 2,
    NarrowTemplateFamily = 3,
    ConfirmTemplateFamily = 4,
    CollectTemplateRequirements = 5,
    ResolveConflicts = 6,
    CompileProjectDefinition = 7,
    RenderReviewArtifacts = 8,
    AwaitUserReview = 9,
    ReviseRequested = 10,
    Confirmed = 11,
    Cancelled = 12,
    Failed = 13
}

public enum ProjectInterviewCommand
{
    StartProjectInterview = 0,
    Approve = 1,
    Revise = 2,
    Cancel = 3
}

public enum ProjectInterviewAdvanceReason
{
    ProjectNameAccepted = 0,
    ProjectScaleConfirmed = 1,
    TemplateFamilyNarrowed = 2,
    TemplateFamilyConfirmed = 3,
    RequirementsReady = 4,
    GraphCompiled = 5,
    ArtifactsRendered = 6,
    RevisionCaptured = 7
}

public sealed record ProjectInterviewSessionState(
    string Channel,
    string UserId,
    ProjectInterviewPhase CurrentPhase,
    string? ProjectName,
    string? ProjectFolderName,
    bool HasUniqueProjectFolder)
{
    public static ProjectInterviewSessionState CreateNew(string channel, string userId) =>
        new(channel, userId, ProjectInterviewPhase.Idle, null, null, false);
}

public sealed record HighLevelProjectInterviewCommand(bool IsProjectInterview, ProjectInterviewCommand? Command);
