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

public enum AssertionStatus
{
    Candidate = 0,
    Confirmed = 1,
    Rejected = 2,
    Superseded = 3,
    Conflicted = 4
}

public sealed record ProjectInterviewAssertion(
    string AssertionId,
    string Statement,
    AssertionStatus Status,
    string Evidence,
    DateTimeOffset UpdatedAt);

public sealed record RestatementOption(
    string OptionId,
    string Text,
    IReadOnlyList<string> AssertionStatements,
    bool IsConservativeEscape);

public sealed record VersionDagNode(string NodeId, string NodeType, string PayloadDigest);

public sealed record VersionDagEdge(string FromNodeId, string ToNodeId);

public sealed record ProjectInterviewVersionDag(int Version, IReadOnlyList<VersionDagNode> Nodes, IReadOnlyList<VersionDagEdge> Edges);

public sealed record ProjectInstanceDefinition(
    string ProjectScale,
    string TemplateFamily,
    IReadOnlyList<string> EnabledModules,
    string StyleProfile);

public sealed record ProjectInterviewTaskDocument(
    string Channel,
    string UserId,
    ProjectInterviewSessionState SessionState,
    IReadOnlyList<ProjectInterviewAssertion> Assertions,
    IReadOnlyList<RestatementOption> PendingOptions,
    int CurrentVersion,
    ProjectInstanceDefinition? CurrentProjectDefinition)
{
    public static ProjectInterviewTaskDocument CreateEmpty(string channel, string userId) =>
        new(
            channel,
            userId,
            ProjectInterviewSessionState.CreateNew(channel, userId),
            Array.Empty<ProjectInterviewAssertion>(),
            Array.Empty<RestatementOption>(),
            0,
            null);

    public bool IsActiveSession =>
        SessionState.CurrentPhase is not ProjectInterviewPhase.Idle
        and not ProjectInterviewPhase.Confirmed
        and not ProjectInterviewPhase.Cancelled
        and not ProjectInterviewPhase.Failed;

    public ProjectInterviewTaskDocument WithSessionState(ProjectInterviewSessionState sessionState) =>
        this with { SessionState = sessionState };

    public ProjectInterviewTaskDocument WithPendingOptions(IReadOnlyList<RestatementOption> pendingOptions) =>
        this with { PendingOptions = pendingOptions };

    public ProjectInterviewTaskDocument ClearPendingOptions() =>
        this with { PendingOptions = Array.Empty<RestatementOption>() };

    public ProjectInterviewTaskDocument WithCompiledDefinition(int version, ProjectInstanceDefinition projectDefinition) =>
        this with
        {
            CurrentVersion = version,
            CurrentProjectDefinition = projectDefinition
        };

    public ProjectInterviewTaskDocument PromoteConfirmedOption(RestatementOption option, string evidence)
    {
        var timestamp = DateTimeOffset.UtcNow;
        var promotedAssertions = option.AssertionStatements
            .Select(statement => new ProjectInterviewAssertion(
                Guid.NewGuid().ToString("N"),
                statement,
                AssertionStatus.Confirmed,
                evidence,
                timestamp))
            .ToArray();

        return this with
        {
            Assertions = Assertions.Concat(promotedAssertions).ToArray(),
            PendingOptions = Array.Empty<RestatementOption>()
        };
    }
}
