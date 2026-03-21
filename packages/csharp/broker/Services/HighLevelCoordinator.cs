using System.Text.Json;
using System.Text.RegularExpressions;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Services;

public class HighLevelCoordinator
{
    private const string SystemPrincipalId = "system:high-level-coordinator";
    private const string ConversationDocumentPrefix = "convlog:";

    private readonly BrokerDb _db;
    private readonly IBrokerService _brokerService;
    private readonly IPlanService _planService;
    private readonly ITaskRouter _taskRouter;
    private readonly LineChatGateway _lineChatGateway;
    private readonly HighLevelCoordinatorOptions _options;
    private readonly ILogger<HighLevelCoordinator> _logger;
    private readonly string _accessRoot;

    public HighLevelCoordinator(
        BrokerDb db,
        IBrokerService brokerService,
        IPlanService planService,
        ITaskRouter taskRouter,
        LineChatGateway lineChatGateway,
        HighLevelCoordinatorOptions options,
        ILogger<HighLevelCoordinator> logger)
    {
        _db = db;
        _brokerService = brokerService;
        _planService = planService;
        _taskRouter = taskRouter;
        _lineChatGateway = lineChatGateway;
        _options = options;
        _logger = logger;
        _accessRoot = ResolveAccessRoot(_options.AccessRoot);
    }

    public async Task<HighLevelProcessResult> ProcessLineMessageAsync(
        string userId,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(message))
        {
            return new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Conversation,
                Reply = "user_id and message are required.",
                Error = "empty_input"
            };
        }

        const string channel = "line";
        var trimmed = message.Trim();
        var normalized = Normalize(trimmed);
        var profile = LoadUserProfile(channel, userId) ?? new HighLevelUserProfile
        {
            Channel = channel,
            UserId = userId
        };
        var draft = LoadTaskDraft(channel, userId);

        if (draft != null && IsExpired(draft))
        {
            DeleteDocument(BuildDraftDocumentId(channel, userId));
            draft = null;
            profile.PendingDraftId = null;
        }

        if (draft != null)
        {
            if (draft.RequiresProjectName && string.IsNullOrWhiteSpace(draft.ProjectName))
            {
                if (IsCancel(normalized))
                {
                    DeleteDocument(BuildDraftDocumentId(channel, userId));
                    profile.PendingDraftId = null;
                    profile.LastDecision = HighLevelRouteMode.Production.ToString();
                    profile.LastUpdatedAt = DateTime.UtcNow;
                    IncrementDecisionCount(profile, HighLevelRouteMode.Production);
                    SaveUserProfile(channel, userId, profile);

                    return new HighLevelProcessResult
                    {
                        Mode = HighLevelRouteMode.Production,
                        Reply = "\u5df2\u53d6\u6d88\u672c\u6b21 production \u898f\u5283\uff0c\u4e0d\u6703\u5efa\u7acb task \u6216 plan\u3002",
                        DraftCleared = true
                    };
                }

                if (IsConfirm(normalized))
                {
                    SaveUserProfile(channel, userId, profile);
                    return new HighLevelProcessResult
                    {
                        Mode = HighLevelRouteMode.Production,
                        Reply = BuildProjectNameRequestReply(draft),
                        Draft = draft,
                        DecisionReason = "project name required before confirmation"
                    };
                }

                if (TryAssignProjectName(draft, trimmed, out var projectNameError))
                {
                    SaveTaskDraft(channel, userId, draft);
                    profile.PendingDraftId = draft.DraftId;
                    SaveUserProfile(channel, userId, profile);

                    return new HighLevelProcessResult
                    {
                        Mode = HighLevelRouteMode.Production,
                        Reply = BuildDraftConfirmationReply(draft),
                        Draft = draft,
                        DecisionReason = "project name captured"
                    };
                }

                draft.ProjectNameValidationError = projectNameError;
                SaveTaskDraft(channel, userId, draft);
                SaveUserProfile(channel, userId, profile);
                return new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Production,
                    Reply = BuildProjectNameRequestReply(draft),
                    Draft = draft,
                    DecisionReason = "project name required"
                };
            }

            if (IsConfirm(normalized))
            {
                var confirmed = ConfirmDraft(channel, userId, profile, draft);
                SaveUserProfile(channel, userId, confirmed.Profile);
                return confirmed.Result;
            }

            if (IsCancel(normalized))
            {
                DeleteDocument(BuildDraftDocumentId(channel, userId));
                profile.PendingDraftId = null;
                profile.LastDecision = HighLevelRouteMode.Production.ToString();
                profile.LastUpdatedAt = DateTime.UtcNow;
                IncrementDecisionCount(profile, HighLevelRouteMode.Production);
                SaveUserProfile(channel, userId, profile);

                return new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Production,
                    Reply = "\u5df2\u53d6\u6d88\u672c\u6b21 production \u898f\u5283\uff0c\u4e0d\u6703\u5efa\u7acb task \u6216 plan\u3002",
                    DraftCleared = true
                };
            }

            SaveUserProfile(channel, userId, profile);
            return new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Production,
                Reply = BuildPendingDraftReminder(draft),
                Draft = draft,
                DecisionReason = "pending draft requires confirmation"
            };
        }

        var decision = Classify(trimmed);
        profile.LastDecision = decision.Mode.ToString();
        profile.LastUpdatedAt = DateTime.UtcNow;
        IncrementDecisionCount(profile, decision.Mode);

        if (decision.Mode == HighLevelRouteMode.Production)
        {
            var nextDraft = CreateDraft(channel, userId, trimmed, decision);
            SaveTaskDraft(channel, userId, nextDraft);

            profile.PendingDraftId = nextDraft.DraftId;
            SaveUserProfile(channel, userId, profile);

            return new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Production,
                Reply = nextDraft.RequiresProjectName && string.IsNullOrWhiteSpace(nextDraft.ProjectName)
                    ? BuildProjectNameRequestReply(nextDraft)
                    : BuildDraftConfirmationReply(nextDraft),
                Draft = nextDraft,
                DecisionReason = decision.Reason
            };
        }

        SaveUserProfile(channel, userId, profile);

        var chat = await _lineChatGateway.ChatAsync(userId, trimmed, cancellationToken);
        return new HighLevelProcessResult
        {
            Mode = decision.Mode,
            Reply = chat.Reply,
            Error = chat.Error,
            DecisionReason = decision.Reason,
            RagSnippets = chat.RagSnippets,
            HistoryCount = chat.HistoryCount
        };
    }

    public HighLevelUserProfile? GetLineUserProfile(string userId)
        => LoadUserProfile("line", userId);

    public HighLevelTaskDraft? GetLineDraft(string userId)
        => LoadTaskDraft("line", userId);

    private (HighLevelProcessResult Result, HighLevelUserProfile Profile) ConfirmDraft(
        string channel,
        string userId,
        HighLevelUserProfile profile,
        HighLevelTaskDraft draft)
    {
        if (draft.RequiresProjectName && string.IsNullOrWhiteSpace(draft.ProjectName))
        {
            return (new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Production,
                Reply = BuildProjectNameRequestReply(draft),
                Draft = draft,
                Error = "project_name_required"
            }, profile);
        }

        if (!EnsureProjectNameStillAvailable(draft, out var projectConflictReply))
        {
            SaveTaskDraft(channel, userId, draft);
            return (new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Production,
                Reply = projectConflictReply,
                Draft = draft,
                Error = "project_name_conflict"
            }, profile);
        }

        EnsureManagedWorkspaceLayout(draft.ManagedPaths);

        var submittedBy = $"{channel}:{userId}";
        var assignedRole = _taskRouter.RecommendRole(draft.TaskType);
        var task = _brokerService.CreateTask(
            submittedBy,
            draft.TaskType,
            draft.ScopeDescriptor,
            assignedRoleId: assignedRole,
            runtimeDescriptor: draft.RuntimeDescriptor);

        var plan = _planService.CreatePlan(task.TaskId, submittedBy, draft.Title, draft.Description);
        var handoff = BuildHandoff(task, plan, draft, channel, userId);
        SaveHandoff(task.TaskId, handoff);

        DeleteDocument(BuildDraftDocumentId(channel, userId));

        profile.PendingDraftId = null;
        profile.LastTaskId = task.TaskId;
        profile.LastPlanId = plan.PlanId;
        profile.LastDecision = HighLevelRouteMode.Production.ToString();
        profile.LastUpdatedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "High-level coordinator created task {TaskId} and plan {PlanId} for {Channel}:{UserId}",
            task.TaskId, plan.PlanId, channel, userId);

        var reply = string.Join('\n', new[]
        {
            "\u5df2\u78ba\u8a8d\u70ba production \u4efb\u52d9\u3002",
            $"task_id: {task.TaskId}",
            $"plan_id: {plan.PlanId}",
            $"task_type: {task.TaskType}",
            $"title: {draft.Title}",
            string.IsNullOrWhiteSpace(draft.ProjectName) ? null : $"project_name: {draft.ProjectName}",
            string.IsNullOrWhiteSpace(draft.ManagedPaths?.ProjectRoot) ? null : $"project_root: {draft.ManagedPaths.ProjectRoot}",
            "",
            "\u5df2\u5efa\u7acb task / plan\uff0c\u4e26\u7522\u751f handoff \u8207 task tree skeleton \u4f9b\u4e0b\u6e38\u6a5f\u5236\u7e7c\u7e8c\u7cbe\u5316\u3002"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));

        return (new HighLevelProcessResult
        {
            Mode = HighLevelRouteMode.Production,
            Reply = reply,
            CreatedTask = task,
            CreatedPlan = plan,
            Handoff = handoff
        }, profile);
    }

    private HighLevelRouteDecision Classify(string message)
    {
        var normalized = Normalize(message);

        if (ContainsAny(normalized, _options.ProductionKeywords))
        {
            var taskType = InferTaskType(normalized);
            return new HighLevelRouteDecision
            {
                Mode = HighLevelRouteMode.Production,
                TaskType = taskType,
                Reason = "matched production keywords"
            };
        }

        if (ContainsAny(normalized, _options.QueryKeywords) || normalized.Contains('?'))
        {
            return new HighLevelRouteDecision
            {
                Mode = HighLevelRouteMode.Query,
                TaskType = "query",
                Reason = "matched query keywords"
            };
        }

        return new HighLevelRouteDecision
        {
            Mode = HighLevelRouteMode.Conversation,
            TaskType = "analysis",
            Reason = "default conversation route"
        };
    }

    private string InferTaskType(string normalized)
    {
        if (ContainsAny(normalized, _options.CodeModifyKeywords)) return "code_modify";
        if (ContainsAny(normalized, _options.DocKeywords)) return "doc_gen";
        if (ContainsAny(normalized, _options.CodeGenKeywords)) return "code_gen";
        return "task_management";
    }

    private HighLevelTaskDraft CreateDraft(
        string channel,
        string userId,
        string message,
        HighLevelRouteDecision decision)
    {
        var summary = message.Length <= _options.MaxDraftSummaryLength
            ? message
            : message[.._options.MaxDraftSummaryLength] + "...";

        var title = decision.TaskType switch
        {
            "code_modify" => $"Modify artifact from {channel} request",
            "doc_gen" => $"Generate document from {channel} request",
            "code_gen" => $"Generate deliverable from {channel} request",
            _ => $"Production task from {channel} request"
        };
        var requiresProjectName = decision.TaskType == "code_gen";
        var managedPaths = BuildManagedPaths(channel, userId, null);

        var draft = new HighLevelTaskDraft
        {
            DraftId = $"draft_{Guid.NewGuid():N}"[..18],
            Channel = channel,
            UserId = userId,
            OriginalMessage = message,
            Summary = summary,
            TaskType = decision.TaskType,
            Title = title,
            Description = $"Origin: {channel}:{userId}\n\nUser request:\n{message}",
            ManagedPaths = managedPaths,
            RequiresProjectName = requiresProjectName,
            ProposedPhases = BuildProposedPhases(decision.TaskType),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, _options.DraftTtlMinutes))
        };

        if (requiresProjectName)
        {
            var hintedProjectName = TryExtractProjectNameHint(message);
            if (!string.IsNullOrWhiteSpace(hintedProjectName) &&
                !TryAssignProjectName(draft, hintedProjectName, out var projectNameError))
            {
                draft.ProjectNameValidationError = projectNameError;
            }
        }

        UpdateDraftDescriptors(draft);
        return draft;
    }

    private static List<HighLevelTaskPhase> BuildProposedPhases(string taskType)
    {
        if (taskType == "doc_gen")
        {
            return new List<HighLevelTaskPhase>
            {
                new() { PhaseId = "clarify", Title = "Clarify scope", Kind = "conversation_control" },
                new() { PhaseId = "collect", Title = "Collect sources", Kind = "context_collection" },
                new() { PhaseId = "draft", Title = "Draft deliverable", Kind = "artifact_creation" },
                new() { PhaseId = "review", Title = "Review and handoff", Kind = "verification" }
            };
        }

        return new List<HighLevelTaskPhase>
        {
            new() { PhaseId = "clarify", Title = "Clarify scope", Kind = "conversation_control" },
            new() { PhaseId = "inspect", Title = "Inspect context", Kind = "context_collection" },
            new() { PhaseId = "build", Title = "Produce artifact", Kind = "artifact_creation" },
            new() { PhaseId = "verify", Title = "Verify result", Kind = "verification" },
            new() { PhaseId = "handoff", Title = "Handoff", Kind = "handoff" }
        };
    }

    private static string BuildDraftConfirmationReply(HighLevelTaskDraft draft)
    {
        return string.Join('\n', new[]
        {
            "\u6211\u5224\u65b7\u9019\u662f\u4e00\u500b production \u8acb\u6c42\uff0c\u6e96\u5099\u9032\u5165\u53d7\u63a7\u4efb\u52d9\u6d41\u7a0b\u3002",
            $"task_type: {draft.TaskType}",
            $"title: {draft.Title}",
            $"summary: {draft.Summary}",
            string.IsNullOrWhiteSpace(draft.ProjectName) ? null : $"project_name: {draft.ProjectName}",
            $"agent_access_root: {draft.ManagedPaths.AccessRoot}",
            $"user_root: {draft.ManagedPaths.UserRoot}",
            $"documents_root: {draft.ManagedPaths.DocumentsRoot}",
            $"conversations_root: {draft.ManagedPaths.ConversationsRoot}",
            string.IsNullOrWhiteSpace(draft.ManagedPaths.ProjectRoot) ? null : $"project_root: {draft.ManagedPaths.ProjectRoot}",
            "",
            "\u9810\u8a08 phases:",
            string.Join('\n', draft.ProposedPhases.Select((phase, index) => $"{index + 1}. {phase.Title} ({phase.Kind})")),
            "",
            "\u82e5\u78ba\u8a8d\u8981\u5efa\u7acb task / plan\uff0c\u8acb\u56de\u8986\u300c\u78ba\u8a8d\u300d\u6216 confirm\u3002",
            "\u82e5\u8981\u53d6\u6d88\uff0c\u8acb\u56de\u8986\u300c\u53d6\u6d88\u300d\u6216 cancel\u3002"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildPendingDraftReminder(HighLevelTaskDraft draft)
    {
        return string.Join('\n', new[]
        {
            "\u76ee\u524d\u4ecd\u6709\u4e00\u500b\u5f85\u78ba\u8a8d\u7684 production draft\u3002",
            $"task_type: {draft.TaskType}",
            $"title: {draft.Title}",
            $"summary: {draft.Summary}",
            string.IsNullOrWhiteSpace(draft.ProjectName) ? null : $"project_name: {draft.ProjectName}",
            "",
            "\u8acb\u5148\u56de\u8986\u300c\u78ba\u8a8d\u300d / confirm \u6216\u300c\u53d6\u6d88\u300d / cancel\uff0c\u518d\u7e7c\u7e8c\u4e0b\u4e00\u500b\u751f\u7522\u4efb\u52d9\u3002"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildProjectNameRequestReply(HighLevelTaskDraft draft)
    {
        return string.Join('\n', new[]
        {
            "\u9019\u662f\u4e00\u500b\u9700\u8981\u5efa\u7acb\u5c08\u6848\u76ee\u9304\u7684 production \u8acb\u6c42\u3002",
            $"task_type: {draft.TaskType}",
            $"agent_access_root: {draft.ManagedPaths.AccessRoot}",
            $"user_root: {draft.ManagedPaths.UserRoot}",
            $"projects_root: {draft.ManagedPaths.ProjectsRoot}",
            "",
            string.IsNullOrWhiteSpace(draft.ProjectNameValidationError)
                ? "\u8acb\u5148\u63d0\u4f9b\u5c08\u6848\u540d\u7a31\uff0c\u4f8b\u5982\uff1a\u300c\u5c08\u6848\u540d\u7a31\uff1aMySite\u300d\u6216\u76f4\u63a5\u56de\u8986 MySite\u3002"
                : draft.ProjectNameValidationError,
            "\u5c08\u6848\u540d\u7a31\u6703\u5728\u4f60\u7684 user_root/projects \u4e0b\u5efa\u7acb\u5c08\u5c6c\u76ee\u9304\uff0c\u540c\u540d\u5c08\u6848\u4e0d\u6703\u91cd\u8907\u5efa\u7acb\u3002",
            "\u82e5\u8981\u53d6\u6d88\uff0c\u8acb\u56de\u8986\u300c\u53d6\u6d88\u300d\u6216 cancel\u3002"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private HighLevelTaskHandoff BuildHandoff(
        BrokerTask task,
        Plan plan,
        HighLevelTaskDraft draft,
        string channel,
        string userId)
    {
        return new HighLevelTaskHandoff
        {
            TaskId = task.TaskId,
            PlanId = plan.PlanId,
            Channel = channel,
            UserId = userId,
            OriginalMessage = draft.OriginalMessage,
            Summary = draft.Summary,
            TaskType = draft.TaskType,
            Title = draft.Title,
            Description = draft.Description,
            ProposedPhases = draft.ProposedPhases,
            RuntimeDescriptor = JsonSerializer.Deserialize<JsonElement>(draft.RuntimeDescriptor),
            ScopeDescriptor = JsonSerializer.Deserialize<JsonElement>(draft.ScopeDescriptor),
            ConversationDocument = BuildConversationDocumentId(userId),
            UserProfileDocument = BuildProfileDocumentId(channel, userId),
            ProjectName = draft.ProjectName,
            ProjectFolderName = draft.ProjectFolderName,
            ManagedPaths = draft.ManagedPaths,
            CreatedAt = DateTime.UtcNow
        };
    }

    private void UpdateDraftDescriptors(HighLevelTaskDraft draft)
    {
        draft.ScopeDescriptor = BuildScopeDescriptor(draft);
        draft.RuntimeDescriptor = BuildRuntimeDescriptor(draft);
    }

    private string BuildScopeDescriptor(HighLevelTaskDraft draft)
    {
        var paths = new List<string>
        {
            draft.ManagedPaths.UserRoot,
            draft.ManagedPaths.DocumentsRoot,
            draft.ManagedPaths.ConversationsRoot,
            draft.ManagedPaths.ProjectsRoot
        };

        if (!string.IsNullOrWhiteSpace(draft.ManagedPaths.ProjectRoot))
        {
            paths.Add(draft.ManagedPaths.ProjectRoot);
        }

        return JsonSerializer.Serialize(new
        {
            channel = draft.Channel,
            origin_user_id = draft.UserId,
            mode = "production",
            source = "high-level-coordinator",
            path_scope = new
            {
                access_root = draft.ManagedPaths.AccessRoot,
                user_root = draft.ManagedPaths.UserRoot,
                documents_root = draft.ManagedPaths.DocumentsRoot,
                conversations_root = draft.ManagedPaths.ConversationsRoot,
                projects_root = draft.ManagedPaths.ProjectsRoot,
                project_root = draft.ManagedPaths.ProjectRoot,
                paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            }
        });
    }

    private string BuildRuntimeDescriptor(HighLevelTaskDraft draft)
    {
        return JsonSerializer.Serialize(new
        {
            source = draft.Channel,
            source_user_id = draft.UserId,
            high_level = true,
            conversation_document = BuildConversationDocumentId(draft.UserId),
            user_profile_document = BuildProfileDocumentId(draft.Channel, draft.UserId),
            draft_document = BuildDraftDocumentId(draft.Channel, draft.UserId),
            managed_paths = draft.ManagedPaths,
            project = new
            {
                required = draft.RequiresProjectName,
                name = draft.ProjectName,
                folder_name = draft.ProjectFolderName
            }
        });
    }

    private HighLevelManagedPaths BuildManagedPaths(string channel, string userId, string? projectFolderName)
    {
        var safeChannel = SanitizePathSegment(channel, "channel");
        var safeUserId = SanitizePathSegment(userId, "user");
        var channelRoot = Path.Combine(_accessRoot, safeChannel);
        var userRoot = Path.Combine(channelRoot, safeUserId);
        var conversationsRoot = Path.Combine(userRoot, "conversations");
        var documentsRoot = Path.Combine(userRoot, "documents");
        var projectsRoot = Path.Combine(userRoot, "projects");

        return new HighLevelManagedPaths
        {
            AccessRoot = _accessRoot,
            ChannelRoot = channelRoot,
            UserRoot = userRoot,
            ConversationsRoot = conversationsRoot,
            DocumentsRoot = documentsRoot,
            ProjectsRoot = projectsRoot,
            ProjectRoot = string.IsNullOrWhiteSpace(projectFolderName)
                ? string.Empty
                : Path.Combine(projectsRoot, projectFolderName)
        };
    }

    private void EnsureManagedWorkspaceLayout(HighLevelManagedPaths paths)
    {
        Directory.CreateDirectory(paths.AccessRoot);
        Directory.CreateDirectory(paths.ChannelRoot);
        Directory.CreateDirectory(paths.UserRoot);
        Directory.CreateDirectory(paths.ConversationsRoot);
        Directory.CreateDirectory(paths.DocumentsRoot);
        Directory.CreateDirectory(paths.ProjectsRoot);

        if (!string.IsNullOrWhiteSpace(paths.ProjectRoot))
        {
            Directory.CreateDirectory(paths.ProjectRoot);
        }
    }

    private bool TryAssignProjectName(HighLevelTaskDraft draft, string rawInput, out string error)
    {
        error = string.Empty;

        var projectName = NormalizeProjectNameInput(rawInput);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            error = "\u5c08\u6848\u540d\u7a31\u4e0d\u80fd\u662f\u7a7a\u767d\u3002\u8acb\u63d0\u4f9b\u4e00\u500b\u660e\u78ba\u7684\u5c08\u6848\u540d\u7a31\u3002";
            return false;
        }

        var projectFolderName = SanitizePathSegment(projectName, string.Empty);
        if (string.IsNullOrWhiteSpace(projectFolderName))
        {
            error = "\u9019\u500b\u5c08\u6848\u540d\u7a31\u7121\u6cd5\u8f49\u6210\u53ef\u7528\u7684\u76ee\u9304\u540d\u7a31\u3002\u8acb\u63db\u4e00\u500b\u540d\u7a31\u3002";
            return false;
        }

        var managedPaths = BuildManagedPaths(draft.Channel, draft.UserId, projectFolderName);
        if (Directory.Exists(managedPaths.ProjectRoot))
        {
            error = $"\u5c08\u6848\u540d\u7a31\u300c{projectName}\u300d\u5df2\u5b58\u5728\u3002\u8acb\u63db\u4e00\u500b\u4e0d\u91cd\u8907\u7684\u540d\u7a31\u3002";
            return false;
        }

        draft.ProjectName = projectName;
        draft.ProjectFolderName = projectFolderName;
        draft.ProjectNameValidationError = null;
        draft.ManagedPaths = managedPaths;
        if (draft.TaskType == "code_gen")
        {
            draft.Title = $"Generate project {projectName} from {draft.Channel} request";
        }

        draft.Description = string.Join('\n', new[]
        {
            $"Origin: {draft.Channel}:{draft.UserId}",
            $"Project name: {projectName}",
            $"Project root: {managedPaths.ProjectRoot}",
            "",
            "User request:",
            draft.OriginalMessage
        });

        UpdateDraftDescriptors(draft);
        return true;
    }

    private bool EnsureProjectNameStillAvailable(HighLevelTaskDraft draft, out string reply)
    {
        reply = string.Empty;
        if (!draft.RequiresProjectName || string.IsNullOrWhiteSpace(draft.ProjectName))
        {
            return true;
        }

        if (!Directory.Exists(draft.ManagedPaths.ProjectRoot))
        {
            return true;
        }

        draft.ProjectName = null;
        draft.ProjectFolderName = null;
        draft.ProjectNameValidationError = "\u5728\u78ba\u8a8d\u524d\uff0c\u540c\u540d\u5c08\u6848\u5df2\u88ab\u5efa\u7acb\u3002\u8acb\u63d0\u4f9b\u65b0\u7684\u5c08\u6848\u540d\u7a31\u3002";
        draft.ManagedPaths = BuildManagedPaths(draft.Channel, draft.UserId, null);
        UpdateDraftDescriptors(draft);
        reply = BuildProjectNameRequestReply(draft);
        return false;
    }

    private static string? TryExtractProjectNameHint(string message)
    {
        foreach (var pattern in new[]
                 {
                     @"(?:^|\s)(?:project\s*name|project|專案名稱)\s*[:：]\s*(.+)$",
                     @"(?:^|\s)(?:name)\s*[:：]\s*(.+)$"
                 })
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                var candidate = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static string NormalizeProjectNameInput(string rawInput)
    {
        var candidate = rawInput.Trim();
        candidate = Regex.Replace(candidate, @"^(?:project\s*name|project|專案名稱|name)\s*[:：]\s*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        candidate = candidate.Trim().Trim('"', '\'', '“', '”', '「', '」', '『', '』');
        return candidate;
    }

    private static string SanitizePathSegment(string value, string fallback)
    {
        var sanitized = value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalid, '-');
        }

        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        sanitized = sanitized.TrimEnd('.', ' ');
        if (sanitized.Length > 80)
        {
            sanitized = sanitized[..80].TrimEnd('.', ' ');
        }

        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string ResolveAccessRoot(string configuredRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            configuredRoot = "managed-workspaces";
        }

        return Path.GetFullPath(configuredRoot);
    }

    private HighLevelUserProfile? LoadUserProfile(string channel, string userId)
        => LoadLatestJson<HighLevelUserProfile>(BuildProfileDocumentId(channel, userId));

    private void SaveUserProfile(string channel, string userId, HighLevelUserProfile profile)
    {
        profile.Channel = channel;
        profile.UserId = userId;
        UpsertDocument(
            BuildProfileDocumentId(channel, userId),
            BuildProfileDocumentId(channel, userId),
            JsonSerializer.Serialize(profile),
            "application/json",
            "global");
    }

    private HighLevelTaskDraft? LoadTaskDraft(string channel, string userId)
        => LoadLatestJson<HighLevelTaskDraft>(BuildDraftDocumentId(channel, userId));

    private void SaveTaskDraft(string channel, string userId, HighLevelTaskDraft draft)
    {
        UpsertDocument(
            BuildDraftDocumentId(channel, userId),
            BuildDraftDocumentId(channel, userId),
            JsonSerializer.Serialize(draft),
            "application/json",
            "global");
    }

    private void SaveHandoff(string taskId, HighLevelTaskHandoff handoff)
    {
        UpsertDocument(
            BuildHandoffDocumentId(taskId),
            BuildHandoffDocumentId(taskId),
            JsonSerializer.Serialize(handoff),
            "application/handoff+json",
            taskId);
    }

    private T? LoadLatestJson<T>(string documentId)
    {
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        if (latest == null || string.IsNullOrWhiteSpace(latest.ContentRef))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(latest.ContentRef);
        }
        catch
        {
            return default;
        }
    }

    private void UpsertDocument(string documentId, string key, string json, string contentType, string? taskId)
    {
        var latest = _db.Query<SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        var entry = new SharedContextEntry
        {
            EntryId = IdGen.New("ctx"),
            DocumentId = documentId,
            Version = (latest?.Version ?? 0) + 1,
            ParentVersion = latest?.Version,
            Key = key,
            ContentRef = json,
            ContentType = contentType,
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:high-level-coordinator\"]}",
            AuthorPrincipalId = SystemPrincipalId,
            TaskId = taskId,
            Tags = "[\"high-level\"]",
            CreatedAt = DateTime.UtcNow
        };

        _db.Insert(entry);
    }

    private void DeleteDocument(string documentId)
    {
        _db.Execute(
            "DELETE FROM shared_context_entries WHERE document_id = @docId",
            new { docId = documentId });
    }

    private static string BuildConversationDocumentId(string userId)
        => $"{ConversationDocumentPrefix}{userId}";

    private static string BuildProfileDocumentId(string channel, string userId)
        => $"hlm.profile.{channel}.{userId}";

    private static string BuildDraftDocumentId(string channel, string userId)
        => $"hlm.draft.{channel}.{userId}";

    private static string BuildHandoffDocumentId(string taskId)
        => $"hlm.handoff.{taskId}";

    private static string Normalize(string value)
        => value.Trim().ToLowerInvariant();

    private static bool IsExpired(HighLevelTaskDraft draft)
        => draft.ExpiresAt != default && draft.ExpiresAt < DateTime.UtcNow;

    private static bool IsConfirm(string normalized)
        => normalized is "\u78ba\u8a8d" or "confirm" or "yes" or "y" or "ok" or "okay";

    private static bool IsCancel(string normalized)
        => normalized is "\u53d6\u6d88" or "cancel" or "no" or "n";

    private static bool ContainsAny(string normalized, IEnumerable<string> keywords)
        => keywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static void IncrementDecisionCount(HighLevelUserProfile profile, HighLevelRouteMode mode)
    {
        var key = mode.ToString().ToLowerInvariant();
        profile.DecisionCounts.TryGetValue(key, out var current);
        profile.DecisionCounts[key] = current + 1;
    }
}

public class HighLevelCoordinatorOptions
{
    public int DraftTtlMinutes { get; set; } = 30;
    public int MaxDraftSummaryLength { get; set; } = 160;
    public string AccessRoot { get; set; } = "managed-workspaces";
    public string[] QueryKeywords { get; set; } = new[]
    {
        "\u67e5\u8a62",
        "\u641c\u5c0b",
        "\u627e",
        "\u4ec0\u9ebc",
        "\u70ba\u4ec0\u9ebc",
        "\u600e\u9ebc",
        "search",
        "query",
        "what",
        "how",
        "why"
    };

    public string[] ProductionKeywords { get; set; } = new[]
    {
        "\u5efa\u7acb",
        "\u5efa\u7f6e",
        "\u65b0\u589e",
        "\u4fee\u6539",
        "\u66f4\u65b0",
        "\u522a\u9664",
        "\u7522\u751f",
        "\u505a\u4e00\u500b",
        "\u751f\u6210",
        "\u96db\u5f62",
        "build",
        "create",
        "generate",
        "modify",
        "update",
        "delete",
        "prototype",
        "draft"
    };

    public string[] CodeModifyKeywords { get; set; } = new[]
    {
        "\u4fee\u6539",
        "\u8abf\u6574",
        "\u91cd\u69cb",
        "\u4fee\u5fa9",
        "modify",
        "edit",
        "refactor",
        "fix",
        "patch"
    };

    public string[] CodeGenKeywords { get; set; } = new[]
    {
        "\u5efa\u7acb",
        "\u65b0\u589e",
        "\u751f\u6210",
        "\u505a\u4e00\u500b",
        "\u96db\u5f62",
        "build",
        "create",
        "generate",
        "prototype"
    };

    public string[] DocKeywords { get; set; } = new[]
    {
        "\u6587\u4ef6",
        "\u8aaa\u660e",
        "\u898f\u683c",
        "\u624b\u518a",
        "readme",
        "doc",
        "documentation"
    };
}

public class HighLevelProcessResult
{
    public HighLevelRouteMode Mode { get; set; }
    public string Reply { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string? DecisionReason { get; set; }
    public int HistoryCount { get; set; }
    public bool DraftCleared { get; set; }
    public HighLevelTaskDraft? Draft { get; set; }
    public BrokerTask? CreatedTask { get; set; }
    public Plan? CreatedPlan { get; set; }
    public HighLevelTaskHandoff? Handoff { get; set; }
    public List<RagSnippet>? RagSnippets { get; set; }
}

public enum HighLevelRouteMode
{
    Conversation = 0,
    Query = 1,
    Production = 2
}

public class HighLevelRouteDecision
{
    public HighLevelRouteMode Mode { get; set; }
    public string TaskType { get; set; } = "analysis";
    public string Reason { get; set; } = string.Empty;
}

public class HighLevelUserProfile
{
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? LastDecision { get; set; }
    public string? LastTaskId { get; set; }
    public string? LastPlanId { get; set; }
    public string? PendingDraftId { get; set; }
    public Dictionary<string, int> DecisionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}

public class HighLevelTaskDraft
{
    public string DraftId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string OriginalMessage { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool RequiresProjectName { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectFolderName { get; set; }
    public string? ProjectNameValidationError { get; set; }
    public HighLevelManagedPaths ManagedPaths { get; set; } = new();
    public string ScopeDescriptor { get; set; } = "{}";
    public string RuntimeDescriptor { get; set; } = "{}";
    public List<HighLevelTaskPhase> ProposedPhases { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
}

public class HighLevelTaskPhase
{
    public string PhaseId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}

public class HighLevelTaskHandoff
{
    public string TaskId { get; set; } = string.Empty;
    public string PlanId { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string OriginalMessage { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string? ProjectFolderName { get; set; }
    public string ConversationDocument { get; set; } = string.Empty;
    public string UserProfileDocument { get; set; } = string.Empty;
    public HighLevelManagedPaths ManagedPaths { get; set; } = new();
    public JsonElement ScopeDescriptor { get; set; }
    public JsonElement RuntimeDescriptor { get; set; }
    public List<HighLevelTaskPhase> ProposedPhases { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class HighLevelManagedPaths
{
    public string AccessRoot { get; set; } = string.Empty;
    public string ChannelRoot { get; set; } = string.Empty;
    public string UserRoot { get; set; } = string.Empty;
    public string ConversationsRoot { get; set; } = string.Empty;
    public string DocumentsRoot { get; set; } = string.Empty;
    public string ProjectsRoot { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
}
