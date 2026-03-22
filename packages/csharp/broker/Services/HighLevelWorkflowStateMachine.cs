namespace Broker.Services;

public sealed class HighLevelWorkflowStateMachine
{
    public HighLevelWorkflowDecision Evaluate(HighLevelTaskDraft? draft, HighLevelParsedInput parsed)
    {
        if (parsed.Kind == HighLevelInputKind.Help)
        {
            return new HighLevelWorkflowDecision
            {
                State = GetState(draft),
                Action = HighLevelWorkflowAction.ShowHelp,
                Reason = "explicit help command"
            };
        }

        if (draft == null)
        {
            return parsed.Kind switch
            {
                HighLevelInputKind.Production => Create(HighLevelWorkflowState.Idle, HighLevelWorkflowAction.StartProduction, "production command"),
                HighLevelInputKind.Query => Create(HighLevelWorkflowState.Idle, HighLevelWorkflowAction.StartQuery, "query command"),
                _ => Create(HighLevelWorkflowState.Idle, HighLevelWorkflowAction.StartConversation, "default conversation route")
            };
        }

        if (draft.RequiresProjectName && string.IsNullOrWhiteSpace(draft.ProjectName))
        {
            return parsed.Kind switch
            {
                HighLevelInputKind.Cancel => Create(HighLevelWorkflowState.AwaitingProjectName, HighLevelWorkflowAction.CancelDraft, "cancel while awaiting project name"),
                HighLevelInputKind.Confirm => Create(HighLevelWorkflowState.AwaitingProjectName, HighLevelWorkflowAction.RequestProjectNameFirst, "confirm before project name"),
                HighLevelInputKind.ProjectName => Create(HighLevelWorkflowState.AwaitingProjectName, HighLevelWorkflowAction.CaptureProjectName, "project name supplied"),
                _ => Create(HighLevelWorkflowState.AwaitingProjectName, HighLevelWorkflowAction.RemindProjectName, "project name required")
            };
        }

        return parsed.Kind switch
        {
            HighLevelInputKind.Confirm => Create(HighLevelWorkflowState.AwaitingConfirmation, HighLevelWorkflowAction.ConfirmDraft, "confirm pending draft"),
            HighLevelInputKind.Cancel => Create(HighLevelWorkflowState.AwaitingConfirmation, HighLevelWorkflowAction.CancelDraft, "cancel pending draft"),
            _ => Create(HighLevelWorkflowState.AwaitingConfirmation, HighLevelWorkflowAction.RemindPendingDraft, "pending draft requires confirmation")
        };
    }

    private static HighLevelWorkflowDecision Create(
        HighLevelWorkflowState state,
        HighLevelWorkflowAction action,
        string reason)
        => new()
        {
            State = state,
            Action = action,
            Reason = reason
        };

    private static HighLevelWorkflowState GetState(HighLevelTaskDraft? draft)
    {
        if (draft == null)
        {
            return HighLevelWorkflowState.Idle;
        }

        return draft.RequiresProjectName && string.IsNullOrWhiteSpace(draft.ProjectName)
            ? HighLevelWorkflowState.AwaitingProjectName
            : HighLevelWorkflowState.AwaitingConfirmation;
    }
}

public enum HighLevelWorkflowState
{
    Idle = 0,
    AwaitingProjectName = 1,
    AwaitingConfirmation = 2
}

public enum HighLevelWorkflowAction
{
    ShowHelp = 0,
    StartConversation = 1,
    StartQuery = 2,
    StartProduction = 3,
    CaptureProjectName = 4,
    RequestProjectNameFirst = 5,
    RemindProjectName = 6,
    ConfirmDraft = 7,
    CancelDraft = 8,
    RemindPendingDraft = 9
}

public sealed class HighLevelWorkflowDecision
{
    public HighLevelWorkflowState State { get; set; }
    public HighLevelWorkflowAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
}
