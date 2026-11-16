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
    Failed = 13,
    Expired = 14,

    /// <summary>
    /// 設計已批准（/ok），system_scaffold draft 等待使用者回 y。建置任務建立成功後才進入 <see cref="Confirmed"/>；
    /// 回 n 或 draft 過期時仍可再 /ok 或 /revise。這個階段不算進行中的訪談，回覆 y／n 由 draft 流程處理。
    /// </summary>
    AwaitBuildConfirmation = 15
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
    RevisionCaptured = 7,
    BuildConfirmed = 8
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

public sealed record HighLevelProjectInterviewCommand(
    bool IsProjectInterview,
    ProjectInterviewCommand? Command,
    string? RevisionInput = null)
{
    /// <summary>
    /// 依訪談現況決定這則訊息是不是訪談指令。parser 不知道訪談狀態，會把任何 <c>/revise 文字</c> 都讀成修訂；
    /// 只有訪談已有可修訂的版本（<see cref="ProjectInterviewTaskDocument.HasRevisableVersion"/>）時它才是訪談修訂，
    /// 否則不算訪談指令，訊息照加入修訂語法之前的路徑處理（進行中的訪談當作回答，沒有訪談時走 / 前綴的 production 流程）。
    /// 不帶文字的 /revise 與其他指令不受影響。
    /// </summary>
    public HighLevelProjectInterviewCommand ResolveFor(ProjectInterviewTaskDocument document) =>
        Command == ProjectInterviewCommand.Revise &&
        !string.IsNullOrWhiteSpace(RevisionInput) &&
        !document.HasRevisableVersion
            ? new HighLevelProjectInterviewCommand(false, null)
            : this;
}

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
        and not ProjectInterviewPhase.AwaitBuildConfirmation
        and not ProjectInterviewPhase.Cancelled
        and not ProjectInterviewPhase.Failed
        and not ProjectInterviewPhase.Expired;

    /// <summary>已有編譯完成、可用 /revise 修訂的版本：等待審查，或已批准但建置尚未確認。</summary>
    public bool HasRevisableVersion =>
        SessionState.CurrentPhase is ProjectInterviewPhase.AwaitUserReview or ProjectInterviewPhase.AwaitBuildConfirmation &&
        CurrentProjectDefinition != null;

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
