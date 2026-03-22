namespace Broker.Services;

public sealed class HighLevelExecutionPromotionGate
{
    public HighLevelExecutionPromotionDecision Evaluate(
        HighLevelMemoryState? memory,
        HighLevelTaskDraft? draft)
    {
        if (draft == null)
        {
            return Deny("no active draft to promote");
        }

        if (memory == null)
        {
            return Deny("memory state missing");
        }

        if (!string.Equals(memory.LastRouteMode, HighLevelRouteMode.Production.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return Deny("latest memory route is not production");
        }

        if (string.IsNullOrWhiteSpace(memory.CurrentGoal))
        {
            return Deny("current goal is missing");
        }

        if (memory.CurrentGoalCommitLevel != HighLevelMemoryCommitLevel.Candidate.ToString() &&
            memory.CurrentGoalCommitLevel != HighLevelMemoryCommitLevel.Confirmed.ToString())
        {
            return Deny("current goal is not eligible for promotion");
        }

        if (draft.RequiresProjectName)
        {
            if (string.IsNullOrWhiteSpace(draft.ProjectName))
            {
                return Deny("project name is required");
            }

            if (!string.Equals(memory.ProjectName, draft.ProjectName, StringComparison.Ordinal))
            {
                return Deny("memory project name does not match draft project name");
            }

            if (memory.ProjectNameCommitLevel != HighLevelMemoryCommitLevel.Confirmed.ToString())
            {
                return Deny("project name has not reached confirmed commit level");
            }
        }

        return new HighLevelExecutionPromotionDecision
        {
            Allowed = true,
            Stage = "executable",
            Reason = "explicit confirmation promoted eligible memory into executable intent"
        };
    }

    private static HighLevelExecutionPromotionDecision Deny(string reason)
        => new() { Allowed = false, Stage = "candidate", Reason = reason };
}

public sealed class HighLevelExecutionPromotionDecision
{
    public bool Allowed { get; set; }
    public string Stage { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
