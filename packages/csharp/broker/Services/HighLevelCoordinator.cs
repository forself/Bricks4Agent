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
    private static readonly Regex PreferredUserCodePattern = new("^[A-Za-z0-9]{3,32}$", RegexOptions.CultureInvariant);
    private static readonly Regex LineUserIdPattern = new("^U[a-fA-F0-9]{32}$", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, (string mode, string synthesisLabel)> TransportModes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rail"] = ("rail", "rail_search"),
        ["hsr"] = ("hsr", "hsr_search"),
        ["bus"] = ("bus", "bus_search"),
        ["flight"] = ("flight", "flight_search")
    };

    private readonly BrokerDb _db;
    private readonly IBrokerService _brokerService;
    private readonly IPlanService _planService;
    private readonly ITaskRouter _taskRouter;
    private readonly LineChatGateway _lineChatGateway;
    private readonly HighLevelQueryToolMediator _queryToolMediator;
    private readonly HighLevelRelationQueryService _relationQueryService;
    private readonly HighLevelCoordinatorOptions _options;
    private readonly HighLevelCommandParser _commandParser;
    private readonly HighLevelInputTrustPolicy _inputTrustPolicy;
    private readonly HighLevelWorkflowStateMachine _workflowStateMachine;
    private readonly HighLevelInteractionRecorder _interactionRecorder;
    private readonly HighLevelInterpretationStore _interpretationStore;
    private readonly HighLevelMemoryStore _memoryStore;
    private readonly HighLevelExecutionIntentStore _executionIntentStore;
    private readonly HighLevelExecutionPromotionGate _executionPromotionGate;
    private readonly IHighLevelExecutionModelPlanner _executionModelPlanner;
    private readonly HighLevelDocumentArtifactService _documentArtifactService;
    private readonly HighLevelCodeArtifactService _codeArtifactService;
    private readonly HighLevelSystemScaffoldService _systemScaffoldService;
    private readonly BrowserBindingService _browserBindingService;
    private readonly ILogger<HighLevelCoordinator> _logger;
    private readonly string _accessRoot;

    public HighLevelCoordinator(
        BrokerDb db,
        IBrokerService brokerService,
        IPlanService planService,
        ITaskRouter taskRouter,
        LineChatGateway lineChatGateway,
        HighLevelQueryToolMediator queryToolMediator,
        HighLevelRelationQueryService relationQueryService,
        HighLevelCoordinatorOptions options,
        IHighLevelExecutionModelPlanner executionModelPlanner,
        HighLevelDocumentArtifactService documentArtifactService,
        HighLevelCodeArtifactService codeArtifactService,
        HighLevelSystemScaffoldService systemScaffoldService,
        BrowserBindingService browserBindingService,
        ILogger<HighLevelCoordinator> logger)
    {
        _db = db;
        _brokerService = brokerService;
        _planService = planService;
        _taskRouter = taskRouter;
        _lineChatGateway = lineChatGateway;
        _queryToolMediator = queryToolMediator;
        _relationQueryService = relationQueryService;
        _options = options;
        _commandParser = new HighLevelCommandParser(_options);
        _inputTrustPolicy = new HighLevelInputTrustPolicy();
        _workflowStateMachine = new HighLevelWorkflowStateMachine();
        _interactionRecorder = new HighLevelInteractionRecorder(_db);
        _interpretationStore = new HighLevelInterpretationStore(_db);
        _memoryStore = new HighLevelMemoryStore(_db);
        _executionIntentStore = new HighLevelExecutionIntentStore(_db);
        _executionPromotionGate = new HighLevelExecutionPromotionGate();
        _executionModelPlanner = executionModelPlanner;
        _documentArtifactService = documentArtifactService;
        _codeArtifactService = codeArtifactService;
        _systemScaffoldService = systemScaffoldService;
        _browserBindingService = browserBindingService;
        _logger = logger;
        _accessRoot = ResolveAccessRoot(_options.AccessRoot);
    }

    public async Task<HighLevelProcessResult> ProcessLineMessageAsync(
        string userId,
        string message,
        CancellationToken cancellationToken = default)
    {
        const string channel = "line";
        var envelope = BuildLineEnvelope(message);
        var trustedParse = _inputTrustPolicy.Apply(envelope, _commandParser.Parse(message));
        var parsed = trustedParse.Parsed;
        var workflow = _workflowStateMachine.Evaluate(null, parsed);

        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(message))
        {
            return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Conversation,
                Reply = "user_id and message are required.",
                Error = "empty_input"
            });
        }

        var trimmed = message.Trim();
        envelope = BuildLineEnvelope(trimmed);
        trustedParse = _inputTrustPolicy.Apply(envelope, _commandParser.Parse(trimmed));
        parsed = trustedParse.Parsed;
        var existingProfile = LoadUserProfile(channel, userId);
        var profile = existingProfile ?? new HighLevelUserProfile
        {
            Channel = channel,
            UserId = userId
        };
        var draft = ResolvePendingDraft(channel, userId, profile);

        if (draft != null && IsExpired(draft))
        {
            DeleteDocument(BuildDraftDocumentId(channel, userId));
            draft = null;
            ClearPendingDraftSnapshot(profile);
        }

        workflow = _workflowStateMachine.Evaluate(draft, parsed);

        if (TryHandleRegistrationGate(channel, userId, trimmed, existingProfile == null, profile, draft, trustedParse, workflow, out var registrationResult))
        {
            return registrationResult;
        }

        if (TryHandleProfileCommand(channel, userId, trimmed, profile, draft, parsed, trustedParse, workflow, out var profileResult))
        {
            return profileResult;
        }

        if (workflow.Action == HighLevelWorkflowAction.ShowHelp)
        {
            profile.LastInteractionAt = DateTimeOffset.UtcNow;
            profile.LastUpdatedAt = DateTime.UtcNow;
            profile.LastDecision = HighLevelRouteMode.Query.ToString();
            IncrementDecisionCount(profile, HighLevelRouteMode.Query);
            var helpReply = BuildHelpReplySafe(profile, draft);
            profile.LastCommandGuideAt = DateTimeOffset.UtcNow;
            SaveUserProfile(channel, userId, profile);

            return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Query,
                Reply = helpReply,
                DecisionReason = "matched help query",
                Draft = draft
            });
        }

        if (draft != null)
        {
            if (draft.RequiresProjectName && string.IsNullOrWhiteSpace(draft.ProjectName))
            {
                if (workflow.Action == HighLevelWorkflowAction.CancelDraft)
                {
                    DeleteDocument(BuildDraftDocumentId(channel, userId));
                    ClearPendingDraftSnapshot(profile);
                    profile.LastDecision = HighLevelRouteMode.Production.ToString();
                    profile.LastUpdatedAt = DateTime.UtcNow;
                    IncrementDecisionCount(profile, HighLevelRouteMode.Production);
                    var reply = PrepareReplySafe(profile, trimmed, "\u5df2\u53d6\u6d88\u672c\u6b21 production \u898f\u5283\uff0c\u4e0d\u6703\u5efa\u7acb task \u6216 plan\u3002");
                    SaveUserProfile(channel, userId, profile);

                    return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
                    {
                        Mode = HighLevelRouteMode.Production,
                        Reply = reply,
                        DraftCleared = true
                    });
                }

                if (workflow.Action == HighLevelWorkflowAction.RequestProjectNameFirst)
                {
                    var reply = PrepareReplyWithoutGuide(profile, BuildCompactProjectNameRequestReply(draft));
                    SaveUserProfile(channel, userId, profile);
                    return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
                    {
                        Mode = HighLevelRouteMode.Production,
                        Reply = reply,
                        FollowUpMessages = BuildProjectNameFollowUpMessages(draft),
                        Draft = draft,
                        DecisionReason = "project name required before confirmation"
                    });
                }

                var projectNameError = "\u8acb\u4ee5 # \u958b\u982d\u56de\u8986\u5c08\u6848\u540d\u7a31\uff0c\u4f8b\u5982 #MySite\u3002\u4e0d\u8981\u91cd\u65b0\u8f38\u5165\u6574\u6bb5\u9700\u6c42\u3002";
                if (workflow.Action == HighLevelWorkflowAction.CaptureProjectName &&
                    TryAssignProjectName(draft, parsed.Body, out projectNameError))
                {
                    SaveTaskDraft(channel, userId, draft);
                    UpdatePendingDraftSnapshot(profile, draft);
                    var reply = PrepareReplyWithoutGuide(
                        profile,
                        string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                            ? _systemScaffoldService.BuildDraftReply(draft)
                            : BuildCompactDraftConfirmationReply(draft));
                    SaveUserProfile(channel, userId, profile);

                    return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
                    {
                        Mode = HighLevelRouteMode.Production,
                        Reply = reply,
                        FollowUpMessages = string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                            ? _systemScaffoldService.BuildDraftFollowUpMessages()
                            : BuildDraftFollowUpMessages(draft),
                        Draft = draft,
                        DecisionReason = "project name captured"
                    });
                }

                if (workflow.Action == HighLevelWorkflowAction.RemindProjectName)
                {
                    draft.ProjectNameValidationError = projectNameError;
                    SaveTaskDraft(channel, userId, draft);
                    var projectNameReply = PrepareReplyWithoutGuide(profile, BuildCompactProjectNameRequestReply(draft));
                    SaveUserProfile(channel, userId, profile);
                    return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
                    {
                        Mode = HighLevelRouteMode.Production,
                        Reply = projectNameReply,
                        FollowUpMessages = BuildProjectNameFollowUpMessages(draft),
                        Draft = draft,
                        DecisionReason = workflow.Reason
                    });
                }
            }

            if (string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase) &&
                workflow.Action != HighLevelWorkflowAction.ConfirmDraft &&
                workflow.Action != HighLevelWorkflowAction.CancelDraft &&
                parsed.Kind != HighLevelInputKind.Query &&
                parsed.Kind != HighLevelInputKind.Help &&
                parsed.Kind != HighLevelInputKind.ProjectName &&
                !string.IsNullOrWhiteSpace(trimmed))
            {
                _systemScaffoldService.ApplyRequirementRefinement(draft, trimmed);
                SaveTaskDraft(channel, userId, draft);
                UpdatePendingDraftSnapshot(profile, draft);
                var reply = PrepareReplyWithoutGuide(profile, _systemScaffoldService.BuildDraftReply(draft));
                SaveUserProfile(channel, userId, profile);

                return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Production,
                    Reply = reply,
                    FollowUpMessages = _systemScaffoldService.BuildDraftFollowUpMessages(),
                    Draft = draft,
                    DecisionReason = "system scaffold requirements refined"
                });
            }

            if (workflow.Action == HighLevelWorkflowAction.ConfirmDraft)
            {
                var confirmed = await ConfirmDraft(channel, userId, profile, draft, cancellationToken);
                confirmed.Result.Reply = PrepareReplyWithoutGuide(confirmed.Profile, confirmed.Result.Reply);
                SaveUserProfile(channel, userId, confirmed.Profile);
                return FinalizeResult(channel, userId, envelope, trustedParse, workflow, confirmed.Result);
            }

            if (workflow.Action == HighLevelWorkflowAction.CancelDraft)
            {
                DeleteDocument(BuildDraftDocumentId(channel, userId));
                ClearPendingDraftSnapshot(profile);
                profile.LastDecision = HighLevelRouteMode.Production.ToString();
                profile.LastUpdatedAt = DateTime.UtcNow;
                IncrementDecisionCount(profile, HighLevelRouteMode.Production);
                var reply = PrepareReplySafe(profile, trimmed, "\u5df2\u53d6\u6d88\u672c\u6b21 production \u898f\u5283\uff0c\u4e0d\u6703\u5efa\u7acb task \u6216 plan\u3002");
                SaveUserProfile(channel, userId, profile);

                return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Production,
                    Reply = reply,
                    DraftCleared = true
                });
            }

            if (workflow.Action == HighLevelWorkflowAction.RemindPendingDraft)
            {
                var pendingReply = PrepareReplyWithoutGuide(profile, BuildCompactPendingDraftReminder(draft));
                if (string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase))
                    pendingReply = PrepareReplyWithoutGuide(profile, _systemScaffoldService.BuildDraftReply(draft));
                SaveUserProfile(channel, userId, profile);
                return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Production,
                    Reply = pendingReply,
                    FollowUpMessages = string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                        ? _systemScaffoldService.BuildDraftFollowUpMessages()
                        : BuildDraftFollowUpMessages(draft),
                    Draft = draft,
                    DecisionReason = workflow.Reason
                });
            }
        }

        var decision = Classify(parsed);
        profile.LastDecision = decision.Mode.ToString();
        profile.LastUpdatedAt = DateTime.UtcNow;
        IncrementDecisionCount(profile, decision.Mode);

        if (TryHandlePermissionGate(channel, userId, trimmed, profile, draft, parsed, trustedParse, workflow, decision, out var permissionResult))
        {
            return permissionResult;
        }

        if (decision.Mode == HighLevelRouteMode.Production)
        {
            var nextDraft = CreateDraft(channel, userId, profile, trimmed, decision);
            SaveTaskDraft(channel, userId, nextDraft);
            UpdatePendingDraftSnapshot(profile, nextDraft);
            var reply = PrepareReplyWithoutGuide(
                profile,
                nextDraft.RequiresProjectName && string.IsNullOrWhiteSpace(nextDraft.ProjectName)
                    ? BuildCompactProjectNameRequestReply(nextDraft)
                    : string.Equals(nextDraft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                        ? _systemScaffoldService.BuildDraftReply(nextDraft)
                        : BuildCompactDraftConfirmationReply(nextDraft));
            SaveUserProfile(channel, userId, profile);

            return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Production,
                Reply = reply,
                FollowUpMessages = nextDraft.RequiresProjectName && string.IsNullOrWhiteSpace(nextDraft.ProjectName)
                    ? BuildProjectNameFollowUpMessages(nextDraft)
                    : string.Equals(nextDraft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                        ? _systemScaffoldService.BuildDraftFollowUpMessages()
                        : BuildDraftFollowUpMessages(nextDraft),
                Draft = nextDraft,
                DecisionReason = decision.Reason
            });
        }

        if (decision.Mode == HighLevelRouteMode.Query &&
            string.Equals(parsed.QueryCommand, "search", StringComparison.OrdinalIgnoreCase))
        {
            var searchResult = await _queryToolMediator.SearchWebAsync(channel, userId, parsed.QueryArgument, cancellationToken);
            var searchReplyBody = searchResult.Reply;
            if (searchResult.Success && searchResult.Results.Count > 0)
            {
                _logger.LogInformation(
                    "Query synthesis: mode=web_search query={Query} resultCount={Count}",
                    parsed.QueryArgument, searchResult.Results.Count);

                var synthesized = await _lineChatGateway.SummarizeQueryResultsAsync(
                    userId,
                    "web_search",
                    parsed.QueryArgument,
                    searchResult.Results,
                    cancellationToken);
                if (!string.IsNullOrWhiteSpace(synthesized))
                {
                    _logger.LogInformation(
                        "Query synthesis completed: mode=web_search synthesizedLength={Length}",
                        synthesized.Length);
                    searchReplyBody = synthesized;
                }
            }

            var searchReply = PrepareReplySafe(profile, trimmed, searchReplyBody);
            SaveUserProfile(channel, userId, profile);
            return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Query,
                Reply = searchReply,
                Error = searchResult.Error,
                DecisionReason = searchResult.Success
                    ? "explicit query subcommand mediated by broker tool"
                    : "explicit query subcommand failed during broker tool mediation"
            });
        }

        if (decision.Mode == HighLevelRouteMode.Query &&
            TransportModes.TryGetValue(parsed.QueryCommand ?? "", out var transportMode))
        {
            return await HandleTransportQueryAsync(
                channel, userId, parsed, profile, trimmed, envelope, trustedParse, workflow,
                transportMode, cancellationToken);
        }

        if (decision.Mode == HighLevelRouteMode.Query &&
            string.IsNullOrWhiteSpace(parsed.QueryCommand) &&
            !string.IsNullOrWhiteSpace(parsed.Body))
        {
            var relationResult = await _relationQueryService.TryAnswerAsync(channel, userId, parsed.Body, cancellationToken);
            if (relationResult.Handled)
            {
                var relationReply = PrepareReplySafe(profile, trimmed, relationResult.Reply);
                SaveUserProfile(channel, userId, profile);
                return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Query,
                    Reply = relationReply,
                    DecisionReason = relationResult.DecisionReason
                });
            }
        }

        if (ShouldSuggestControlledSearch(decision, parsed))
        {
            var permissions = EnsurePermissions(profile);
            var suggestionIsTransport = IsTransportLookupSuggestion(parsed);
            if ((suggestionIsTransport && !permissions.AllowTransport) ||
                (!suggestionIsTransport && !permissions.AllowQuery))
            {
                var deniedReply = suggestionIsTransport
                    ? "目前你的帳戶不能使用交通查詢。若需要此權限，請聯絡管理員。"
                    : "目前你的帳戶不能使用受控查詢。若需要此權限，請聯絡管理員。";
                var deniedResult = BuildPermissionDeniedResult(
                    channel, userId, trimmed, profile, trustedParse, workflow,
                    HighLevelRouteMode.Query,
                    deniedReply,
                    suggestionIsTransport ? "transport_disabled" : "query_disabled");
                return deniedResult;
            }

            var suggestedReply = PrepareReplyWithoutGuide(profile, BuildControlledSearchSuggestionReply(parsed));
            SaveUserProfile(channel, userId, profile);
            return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
            {
                Mode = decision.Mode,
                Reply = suggestedReply,
                FollowUpMessages = BuildControlledSearchFollowUpMessages(parsed),
                DecisionReason = decision.Mode == HighLevelRouteMode.Query
                    ? "lookup-style query was redirected to explicit controlled search guidance"
                    : "lookup-style conversation was redirected to explicit controlled search guidance"
            });
        }

        var chatInput = decision.Mode == HighLevelRouteMode.Query && !string.IsNullOrWhiteSpace(parsed.Body)
            ? parsed.Body
            : trimmed;
        var chat = await _lineChatGateway.ChatAsync(userId, chatInput, cancellationToken);
        var replyBody = chat.Reply;
        if (decision.Mode == HighLevelRouteMode.Query && string.IsNullOrWhiteSpace(replyBody))
            replyBody = "目前沒有取得穩定答案。";

        var followUpMessages = new List<string>();
        if (decision.Mode == HighLevelRouteMode.Query && string.IsNullOrWhiteSpace(chat.Reply))
        {
            replyBody = "目前沒有取得穩定答案。";
            if (!string.IsNullOrWhiteSpace(parsed.Body))
            {
                followUpMessages.Add($"?search {parsed.Body}");
            }
            else
            {
                followUpMessages.Add("?search 關鍵字");
            }
        }

        var chatReply = PrepareReplySafe(profile, trimmed, replyBody);
        SaveUserProfile(channel, userId, profile);
        return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
        {
            Mode = decision.Mode,
            Reply = chatReply,
            FollowUpMessages = followUpMessages.Count > 0 ? followUpMessages : null,
            Error = string.IsNullOrWhiteSpace(chat.Reply) && decision.Mode == HighLevelRouteMode.Query
                ? "query_reply_empty"
                : chat.Error,
            DecisionReason = string.IsNullOrWhiteSpace(chat.Reply) && decision.Mode == HighLevelRouteMode.Query
                ? "query route returned empty high-level answer and was downgraded to explicit search guidance"
                : decision.Reason,
            RagSnippets = chat.RagSnippets,
            HistoryCount = chat.HistoryCount
        });
    }

    public HighLevelUserProfile? GetLineUserProfile(string userId)
        => LoadUserProfile("line", userId);

    public HighLevelTaskDraft? GetLineDraft(string userId)
        => LoadTaskDraft("line", userId);

    public HighLevelManagedPaths? GetLineManagedPaths(string userId, bool ensureExists = true)
    {
        var profile = LoadUserProfile("line", userId);
        if (profile == null)
            return null;

        var managedPaths = BuildManagedPaths("line", userId, profile, null);
        if (ensureExists)
            EnsureManagedWorkspaceLayout(managedPaths);

        return managedPaths;
    }

    public HighLevelLineNotification QueueLineNotification(string userId, string title, string body)
        => EnqueueLineNotification(userId, title, body);

    public IReadOnlyList<HighLevelLineUserSummary> ListLineUsers()
    {
        var entries = _db.Query<SharedContextEntry>(
            @"SELECT e.*
              FROM shared_context_entries e
              INNER JOIN (
                  SELECT document_id, MAX(version) AS max_version
                  FROM shared_context_entries
                  WHERE document_id LIKE @prefix
                  GROUP BY document_id
              ) latest
                ON latest.document_id = e.document_id AND latest.max_version = e.version
              ORDER BY e.created_at DESC",
            new { prefix = "hlm.profile.line.%" });

        var summaries = new List<HighLevelLineUserSummary>();
        foreach (var entry in entries)
        {
            HighLevelUserProfile? profile;
            try
            {
                profile = JsonSerializer.Deserialize<HighLevelUserProfile>(entry.ContentRef);
            }
            catch
            {
                continue;
            }

            if (profile == null || string.IsNullOrWhiteSpace(profile.UserId))
                continue;

            var draft = LoadTaskDraft("line", profile.UserId);
            var managedPaths = BuildManagedPaths("line", profile.UserId, profile, draft?.ProjectFolderName);
            var principalCandidates = ResolvePrincipalCandidates(profile).ToArray();
            var activeUserGrants = principalCandidates
                .SelectMany(principalId => _browserBindingService.ListUserGrants(principalId))
                .Where(grant => grant.Status == "active" && (grant.ExpiresAt == null || grant.ExpiresAt > DateTime.UtcNow))
                .GroupBy(grant => grant.UserGrantId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Count();
            var activeUserSites = principalCandidates
                .SelectMany(principalId => _browserBindingService.ListSiteBindings("user_delegated", principalId))
                .Where(binding => binding.Status == "active")
                .GroupBy(binding => binding.SiteBindingId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Count();

            summaries.Add(new HighLevelLineUserSummary
            {
                UserId = profile.UserId,
                DisplayName = profile.PreferredDisplayName,
                UserCode = profile.PreferredUserCode,
                IsTestAccount = IsTestLineAccount(profile.UserId),
                AccountType = IsTestLineAccount(profile.UserId) ? "test" : "line_user",
                Permissions = EnsurePermissions(profile),
                RegistrationStatus = ResolveRegistrationStatus(profile),
                RegistrationRequestedAt = profile.RegistrationRequestedAt,
                RegistrationReviewedAt = profile.RegistrationReviewedAt,
                RegistrationReviewNote = profile.RegistrationReviewNote,
                LastInteractionAt = profile.LastInteractionAt,
                LastDecision = profile.LastDecision,
                PendingDraftId = profile.PendingDraftId,
                LastTaskId = profile.LastTaskId,
                LastPlanId = profile.LastPlanId,
                UserRoot = managedPaths.UserRoot,
                ProjectsRoot = managedPaths.ProjectsRoot,
                ActiveUserGrantCount = activeUserGrants,
                ActiveUserSiteBindingCount = activeUserSites
            });
        }

        return summaries
            .OrderByDescending(summary => summary.LastInteractionAt ?? DateTimeOffset.MinValue)
            .ThenBy(summary => summary.UserId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public HighLevelUserProfile? SetLineUserPermissions(string userId, HighLevelUserPermissionsPatch patch)
    {
        var profile = LoadUserProfile("line", userId);
        if (profile == null)
            return null;

        var permissions = EnsurePermissions(profile);
        if (patch.AllowQuery.HasValue) permissions.AllowQuery = patch.AllowQuery.Value;
        if (patch.AllowTransport.HasValue) permissions.AllowTransport = patch.AllowTransport.Value;
        if (patch.AllowProduction.HasValue) permissions.AllowProduction = patch.AllowProduction.Value;
        if (patch.AllowBrowserDelegated.HasValue) permissions.AllowBrowserDelegated = patch.AllowBrowserDelegated.Value;
        if (patch.AllowDeployment.HasValue) permissions.AllowDeployment = patch.AllowDeployment.Value;

        profile.LastUpdatedAt = DateTime.UtcNow;
        SaveUserProfile("line", userId, profile);
        return profile;
    }

    public string GetLineAnonymousRegistrationPolicy()
        => GetAnonymousRegistrationPolicy("line");

    public string SetLineAnonymousRegistrationPolicy(string policy, string updatedBy = "admin")
    {
        var normalized = NormalizeAnonymousRegistrationPolicy(policy);
        var state = new HighLevelRegistrationPolicyState
        {
            Channel = "line",
            Policy = normalized,
            UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "admin" : updatedBy,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        UpsertDocument(
            BuildRegistrationPolicyDocumentId("line"),
            BuildRegistrationPolicyDocumentId("line"),
            JsonSerializer.Serialize(state),
            "application/json",
            "global");

        return normalized;
    }

    public HighLevelRegistrationReviewResult? ReviewLineUserRegistration(string userId, string action, string? note = null)
    {
        var profile = LoadUserProfile("line", userId);
        if (profile == null)
            return null;

        var normalizedAction = Normalize(action);
        var approved = normalizedAction is "approve" or "approved" or "allow";
        var rejected = normalizedAction is "reject" or "rejected" or "deny";
        if (!approved && !rejected)
            throw new InvalidOperationException("Unsupported registration review action.");

        profile.RegistrationStatus = approved
            ? HighLevelRegistrationStatus.Approved
            : HighLevelRegistrationStatus.Rejected;
        profile.RegistrationReviewedAt = DateTimeOffset.UtcNow;
        profile.RegistrationReviewNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        SaveUserProfile("line", userId, profile);

        if (rejected)
        {
            DeleteDocument(BuildDraftDocumentId("line", userId));
            ClearPendingDraftSnapshot(profile);
            SaveUserProfile("line", userId, profile);
        }

        var notification = EnqueueLineNotification(
            userId,
            approved ? "註冊審核通過" : "註冊審核結果",
            approved
                ? (string.IsNullOrWhiteSpace(note)
                    ? "你的使用申請已通過，現在可以開始使用。"
                    : $"你的使用申請已通過。\n備註：{note}")
                : (string.IsNullOrWhiteSpace(note)
                    ? "你的使用申請未通過，若需要可再聯絡管理者。"
                    : $"你的使用申請未通過。\n原因：{note}"));

        return new HighLevelRegistrationReviewResult
        {
            UserId = userId,
            RegistrationStatus = profile.RegistrationStatus,
            ReviewNote = profile.RegistrationReviewNote,
            Notification = notification
        };
    }

    public IReadOnlyList<HighLevelLineNotification> ListPendingLineNotifications(int limit = 20)
    {
        var entries = _db.Query<SharedContextEntry>(
            @"SELECT e.*
              FROM shared_context_entries e
              INNER JOIN (
                  SELECT document_id, MAX(version) AS max_version
                  FROM shared_context_entries
                  WHERE document_id LIKE @prefix
                  GROUP BY document_id
              ) latest
                ON latest.document_id = e.document_id AND latest.max_version = e.version
              ORDER BY e.created_at ASC
              LIMIT @lim",
            new { prefix = "hlm.notify.line.%", lim = Math.Max(1, limit) });

        return entries
            .Select(entry =>
            {
                try { return JsonSerializer.Deserialize<HighLevelLineNotification>(entry.ContentRef); }
                catch { return null; }
            })
            .Where(notification => notification != null &&
                                   string.Equals(notification.DeliveryStatus, "pending", StringComparison.OrdinalIgnoreCase))
            .Cast<HighLevelLineNotification>()
            .ToArray();
    }

    public HighLevelLineNotification? CompleteLineNotification(string notificationId, string deliveryStatus, string? error = null)
    {
        var notification = LoadLatestJson<HighLevelLineNotification>(BuildLineNotificationDocumentId(notificationId));
        if (notification == null)
            return null;

        notification.DeliveryStatus = NormalizeNotificationStatus(deliveryStatus);
        notification.Error = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
        notification.CompletedAt = DateTimeOffset.UtcNow;
        UpsertDocument(
            BuildLineNotificationDocumentId(notificationId),
            BuildLineNotificationDocumentId(notificationId),
            JsonSerializer.Serialize(notification),
            "application/json",
            "global");

        UpdateArtifactNotificationStatus(notificationId, notification.DeliveryStatus);

        return notification;
    }

    private void UpdateArtifactNotificationStatus(string notificationId, string notificationStatus)
    {
        try
        {
            var entries = _db.Query<SharedContextEntry>(
                """
                SELECT * FROM shared_context_entries
                WHERE document_id LIKE 'hlm.artifact.line.%'
                  AND content_ref LIKE @pattern
                ORDER BY version DESC
                """,
                new { pattern = $"%\"{notificationId}\"%"  });

            foreach (var entry in entries)
            {
                var artifact = System.Text.Json.JsonSerializer.Deserialize<HighLevelLineArtifactRecord>(entry.ContentRef);
                if (artifact == null || artifact.NotificationId != notificationId)
                    continue;

                artifact.OverallStatus = notificationStatus == "sent" ? "completed" : artifact.OverallStatus;
                entry.ContentRef = System.Text.Json.JsonSerializer.Serialize(artifact);
                entry.Version += 1;
                _db.Update(entry);
                break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update artifact notification status for {NotificationId}", notificationId);
        }
    }

    private bool TryHandleRegistrationGate(
        string channel,
        string userId,
        string message,
        bool isNewUser,
        HighLevelUserProfile profile,
        HighLevelTaskDraft? draft,
        HighLevelTrustedParseResult trustedParse,
        HighLevelWorkflowDecision workflow,
        out HighLevelProcessResult result)
    {
        result = default!;
        var registrationStatus = ResolveRegistrationStatus(profile);
        if (!isNewUser)
        {
            if (string.Equals(registrationStatus, HighLevelRegistrationStatus.PendingReview, StringComparison.OrdinalIgnoreCase))
            {
                SaveUserProfile(channel, userId, profile);
                result = FinalizeResult(channel, userId, BuildLineEnvelope(message), trustedParse, workflow, new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Conversation,
                    Reply = BuildPendingRegistrationReply(profile),
                    Error = "registration_pending_review",
                    DecisionReason = "registration gate: pending review"
                });
                return true;
            }

            if (string.Equals(registrationStatus, HighLevelRegistrationStatus.Rejected, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(registrationStatus, HighLevelRegistrationStatus.DeniedByPolicy, StringComparison.OrdinalIgnoreCase))
            {
                SaveUserProfile(channel, userId, profile);
                result = FinalizeResult(channel, userId, BuildLineEnvelope(message), trustedParse, workflow, new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Conversation,
                    Reply = BuildRejectedRegistrationReply(profile),
                    Error = "registration_not_allowed",
                    DecisionReason = "registration gate: denied"
                });
                return true;
            }

            return false;
        }

        var policy = GetAnonymousRegistrationPolicy(channel);
        switch (policy)
        {
            case HighLevelAnonymousRegistrationPolicy.AllowAll:
                profile.RegistrationStatus = HighLevelRegistrationStatus.Approved;
                profile.RegistrationReviewedAt ??= DateTimeOffset.UtcNow;
                SaveUserProfile(channel, userId, profile);
                return false;

            case HighLevelAnonymousRegistrationPolicy.ManualReview:
                profile.RegistrationStatus = HighLevelRegistrationStatus.PendingReview;
                profile.RegistrationRequestedAt = DateTimeOffset.UtcNow;
                profile.RegistrationReviewNote = null;
                SaveUserProfile(channel, userId, profile);
                result = FinalizeResult(channel, userId, BuildLineEnvelope(message), trustedParse, workflow, new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Conversation,
                    Reply = BuildPendingRegistrationReply(profile),
                    Error = "registration_pending_review",
                    DecisionReason = "registration gate: manual review"
                });
                return true;

            case HighLevelAnonymousRegistrationPolicy.DenyAll:
            default:
                profile.RegistrationStatus = HighLevelRegistrationStatus.DeniedByPolicy;
                profile.RegistrationReviewedAt = DateTimeOffset.UtcNow;
                profile.RegistrationReviewNote = "目前未開放匿名註冊。";
                SaveUserProfile(channel, userId, profile);
                result = FinalizeResult(channel, userId, BuildLineEnvelope(message), trustedParse, workflow, new HighLevelProcessResult
                {
                    Mode = HighLevelRouteMode.Conversation,
                    Reply = BuildRejectedRegistrationReply(profile),
                    Error = "registration_denied_by_policy",
                    DecisionReason = "registration gate: deny all"
                });
                return true;
        }
    }

    private bool TryHandleProfileCommand(
        string channel,
        string userId,
        string message,
        HighLevelUserProfile profile,
        HighLevelTaskDraft? draft,
        HighLevelParsedInput parsed,
        HighLevelTrustedParseResult trustedParse,
        HighLevelWorkflowDecision workflow,
        out HighLevelProcessResult result)
    {
        result = default!;

        if (parsed.Kind == HighLevelInputKind.Query &&
            string.Equals(parsed.QueryCommand, "profile", StringComparison.OrdinalIgnoreCase))
        {
            profile.LastDecision = HighLevelRouteMode.Query.ToString();
            profile.LastUpdatedAt = DateTime.UtcNow;
            IncrementDecisionCount(profile, HighLevelRouteMode.Query);
            SaveUserProfile(channel, userId, profile);

            result = FinalizeResult(channel, userId, BuildLineEnvelope(message), trustedParse, workflow, new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Query,
                Reply = PrepareReplySafe(profile, message, BuildProfileReply(profile, draft)),
                DecisionReason = "explicit profile query"
            });
            return true;
        }

        if (parsed.Kind != HighLevelInputKind.Production || string.IsNullOrWhiteSpace(parsed.ProductionCommand))
        {
            return false;
        }

        string reply;
        string? error = null;
        switch (parsed.ProductionCommand)
        {
            case "name":
                if (!TryUpdatePreferredDisplayName(channel, userId, profile, parsed.ProductionArgument, out reply))
                {
                    error = "invalid_display_name";
                }
                break;

            case "id":
                if (!TryUpdatePreferredUserCode(channel, userId, profile, draft, parsed.ProductionArgument, out reply))
                {
                    error = "invalid_user_code";
                }
                else if (draft != null)
                {
                    SaveTaskDraft(channel, userId, draft);
                }
                break;

            default:
                return false;
        }

        profile.LastDecision = HighLevelRouteMode.Production.ToString();
        profile.LastUpdatedAt = DateTime.UtcNow;
        IncrementDecisionCount(profile, HighLevelRouteMode.Production);
        SaveUserProfile(channel, userId, profile);

        result = FinalizeResult(channel, userId, BuildLineEnvelope(message), trustedParse, workflow, new HighLevelProcessResult
        {
            Mode = HighLevelRouteMode.Production,
            Reply = PrepareReplySafe(profile, message, reply),
            Error = error,
            DecisionReason = $"explicit profile command: {parsed.ProductionCommand}",
            Draft = draft
        });
        return true;
    }

    private async Task<(HighLevelProcessResult Result, HighLevelUserProfile Profile)> ConfirmDraft(
        string channel,
        string userId,
        HighLevelUserProfile profile,
        HighLevelTaskDraft draft,
        CancellationToken cancellationToken)
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

        var memory = ResolvePromotableMemory(channel, userId, profile, draft);
        var promotion = _executionPromotionGate.Evaluate(memory, draft);
        if (!promotion.Allowed)
        {
            return (new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Production,
                Reply = $"無法將目前 draft 升格為 executable intent：{promotion.Reason}",
                Draft = draft,
                Error = "execution_promotion_denied",
                DecisionReason = promotion.Reason
            }, profile);
        }

        var requestedExecutionModel = await _executionModelPlanner.RecommendAsync(draft, memory!, cancellationToken);
        var executionIntent = BuildExecutionIntent(channel, userId, draft, memory!, promotion, requestedExecutionModel);
        _executionIntentStore.Write(executionIntent);

        var submittedBy = $"{channel}:{userId}";
        var assignedRole = _taskRouter.RecommendRole(draft.TaskType);
        var promotedRuntimeDescriptor = BuildPromotedRuntimeDescriptor(draft, executionIntent);
        var promotedScopeDescriptor = BuildPromotedScopeDescriptor(draft, executionIntent);

        var task = _brokerService.CreateTask(
            submittedBy,
            draft.TaskType,
            promotedScopeDescriptor,
            assignedRoleId: assignedRole,
            runtimeDescriptor: promotedRuntimeDescriptor);

        var plan = _planService.CreatePlan(task.TaskId, submittedBy, draft.Title, draft.Description);
        var handoff = BuildHandoff(task, plan, draft, executionIntent, channel, userId);
        SaveHandoff(task.TaskId, handoff);

        DeleteDocument(BuildDraftDocumentId(channel, userId));

        ClearPendingDraftSnapshot(profile);
        profile.LastTaskId = task.TaskId;
        profile.LastPlanId = plan.PlanId;
        profile.LastDecision = HighLevelRouteMode.Production.ToString();
        profile.LastUpdatedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "High-level coordinator created task {TaskId} and plan {PlanId} for {Channel}:{UserId}",
            task.TaskId, plan.PlanId, channel, userId);

        HighLevelDocumentArtifactResult? artifactResult = null;
        HighLevelCodeArtifactResult? codeArtifactResult = null;
        HighLevelSystemScaffoldResult? scaffoldArtifactResult = null;
        if (string.Equals(draft.TaskType, "doc_gen", StringComparison.OrdinalIgnoreCase))
        {
            artifactResult = await _documentArtifactService.GenerateAndDeliverAsync(draft, profile, cancellationToken);
        }
        else if (string.Equals(draft.TaskType, "code_gen", StringComparison.OrdinalIgnoreCase))
        {
            codeArtifactResult = await _codeArtifactService.GenerateAndDeliverAsync(draft, profile, task.TaskId, cancellationToken);
        }
        else if (string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase))
        {
            scaffoldArtifactResult = await _systemScaffoldService.GenerateAndDeliverAsync(draft, profile, task.TaskId, cancellationToken);
        }

        _logger.LogInformation(
            "High-level confirmed: intent={IntentId} task={TaskId} plan={PlanId} type={TaskType}",
            executionIntent.IntentId, task.TaskId, plan.PlanId, task.TaskType);

        var replyLines = new List<string>
        {
            $"已確認任務：{draft.Title}"
        };
        if (artifactResult != null)
        {
            replyLines.Add(BuildArtifactReply(artifactResult));
        }
        else if (codeArtifactResult != null)
        {
            replyLines.Add(BuildCodeArtifactReply(codeArtifactResult));
        }
        else if (scaffoldArtifactResult != null)
        {
            replyLines.Add(BuildSystemScaffoldReply(scaffoldArtifactResult));
        }
        else
        {
            replyLines.Add("目前已建立 task / plan / handoff。");
            replyLines.Add("這條 production 路徑尚未自動啟動下游執行代理，因此還不會直接產出網站。");
        }
        var reply = string.Join('\n', replyLines.Where(line => !string.IsNullOrWhiteSpace(line)));

        return (new HighLevelProcessResult
        {
            Mode = HighLevelRouteMode.Production,
            Reply = reply,
            FollowUpMessages = scaffoldArtifactResult?.ProgressMessages,
            CreatedTask = task,
            CreatedPlan = plan,
            Handoff = handoff
        }, profile);
    }

    private async Task<HighLevelProcessResult> HandleTransportQueryAsync(
        string channel, string userId,
        HighLevelParsedInput parsed,
        HighLevelUserProfile profile,
        string trimmed,
        HighLevelInputEnvelope envelope,
        HighLevelTrustedParseResult trustedParse,
        HighLevelWorkflowDecision workflow,
        (string mode, string synthesisLabel) transport,
        CancellationToken cancellationToken)
    {
        var query = parsed.QueryArgument;
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 4)
        {
            var hint = $"請提供更完整的查詢條件。\n範例：?{transport.mode} 台北 台中 今天 18:00";
            var hintReply = PrepareReplySafe(profile, trimmed, hint);
            SaveUserProfile(channel, userId, profile);
            return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
            {
                Mode = HighLevelRouteMode.Query,
                Reply = hintReply,
                DecisionReason = $"transport query too short for {transport.mode}, returned usage hint"
            });
        }

        var transportResult = transport.mode switch
        {
            "rail" => await _queryToolMediator.SearchRailAsync(channel, userId, query, cancellationToken),
            "hsr" => await _queryToolMediator.SearchHsrAsync(channel, userId, query, cancellationToken),
            "bus" => await _queryToolMediator.SearchBusAsync(channel, userId, query, cancellationToken),
            "flight" => await _queryToolMediator.SearchFlightAsync(channel, userId, query, cancellationToken),
            _ => await _queryToolMediator.SearchRailAsync(channel, userId, query, cancellationToken)
        };

        var transportReplyBody = transportResult.Reply;
        if (transportResult.Success && transportResult.Results.Count > 0)
        {
            _logger.LogInformation(
                "Query synthesis: mode={Mode} query={Query} resultCount={Count}",
                transport.synthesisLabel, query, transportResult.Results.Count);

            var synthesized = await _lineChatGateway.SummarizeQueryResultsAsync(
                userId,
                transport.synthesisLabel,
                query,
                transportResult.Results,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(synthesized))
            {
                _logger.LogInformation(
                    "Query synthesis completed: mode={Mode} synthesizedLength={Length}",
                    transport.synthesisLabel, synthesized.Length);
                transportReplyBody = synthesized;
            }
        }

        var transportReply = PrepareReplySafe(profile, trimmed, transportReplyBody);
        SaveUserProfile(channel, userId, profile);
        return FinalizeResult(channel, userId, envelope, trustedParse, workflow, new HighLevelProcessResult
        {
            Mode = HighLevelRouteMode.Query,
            Reply = transportReply,
            Error = transportResult.Error,
            DecisionReason = transportResult.Success
                ? $"explicit {transport.mode} query subcommand mediated by broker tool"
                : $"explicit {transport.mode} query subcommand failed during broker tool mediation"
        });
    }

    internal static string BuildArtifactReply(HighLevelDocumentArtifactResult artifactResult)
    {
        if (artifactResult.Delivery == null)
            return artifactResult.Success
                ? "文件已生成，稍後將透過此對話發送下載連結。"
                : $"文件生成失敗：{artifactResult.Message}";

        if (artifactResult.Success)
        {
            if (artifactResult.Delivery.GoogleDrive?.Success == true)
                return $"文件「{artifactResult.FileName}」已生成並上傳至雲端，稍後將透過此對話發送下載連結。";

            return $"文件「{artifactResult.FileName}」已生成，但雲端上傳未完成。管理員將協助提供下載連結。";
        }

        return $"文件生成失敗：{artifactResult.Message}";
    }

    internal static string BuildCodeArtifactReply(HighLevelCodeArtifactResult artifactResult)
    {
        if (!artifactResult.Success)
            return $"網站原型生成失敗：{artifactResult.Message}";

        var lines = new List<string>
        {
            "已生成網站原型。",
            $"project_root: {artifactResult.ProjectRoot}",
            $"entry_file: {artifactResult.EntryFilePath}"
        };

        if (artifactResult.Delivery?.GoogleDrive?.Success == true)
        {
            lines.Add("雲端交付已完成。");
            if (!string.IsNullOrWhiteSpace(artifactResult.Delivery.GoogleDrive.DownloadLink))
                lines.Add(artifactResult.Delivery.GoogleDrive.DownloadLink);
        }
        else if (artifactResult.Delivery != null)
        {
            lines.Add("雲端上傳未完成，但本機專案檔已建立。");
        }

        return string.Join('\n', lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    internal static string BuildSystemScaffoldReply(HighLevelSystemScaffoldResult artifactResult)
    {
        if (!artifactResult.Success)
            return $"系統雛形生成失敗：{artifactResult.Message}";

        var lines = new List<string>
        {
            "已生成並封裝系統雛形。",
            $"project_root: {artifactResult.ProjectRoot}",
            $"package_file: {artifactResult.PackageFilePath}"
        };

        if (artifactResult.Delivery?.GoogleDrive?.Success == true)
        {
            lines.Add("雲端交付已完成。");
            if (!string.IsNullOrWhiteSpace(artifactResult.Delivery.GoogleDrive.DownloadLink))
                lines.Add(artifactResult.Delivery.GoogleDrive.DownloadLink);
        }
        else if (artifactResult.Delivery != null)
        {
            lines.Add("雲端上傳未完成，但封裝檔已建立。");
        }

        return string.Join('\n', lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static HighLevelMemoryState BuildFallbackMemoryState(
        string channel,
        string userId,
        HighLevelUserProfile profile,
        HighLevelTaskDraft draft)
    {
        return new HighLevelMemoryState
        {
            Channel = channel,
            UserId = userId,
            PreferredDisplayName = profile.PreferredDisplayName,
            PreferredUserCode = profile.PreferredUserCode,
            CurrentGoal = draft.OriginalMessage.Trim(),
            CurrentGoalCommitLevel = HighLevelMemoryCommitLevel.Candidate.ToString(),
            CurrentGoalSource = HighLevelMemorySource.User.ToString(),
            CurrentGoalCommitReason = "fallback projection from production draft",
            LastRouteMode = HighLevelRouteMode.Production.ToString(),
            WorkflowState = draft.RequiresProjectName && string.IsNullOrWhiteSpace(draft.ProjectName)
                ? HighLevelWorkflowState.AwaitingProjectName.ToString()
                : HighLevelWorkflowState.AwaitingConfirmation.ToString(),
            WorkflowAction = HighLevelWorkflowAction.ConfirmDraft.ToString(),
            PendingDraftId = draft.DraftId,
            PendingProjectName = draft.RequiresProjectName && string.IsNullOrWhiteSpace(draft.ProjectName),
            ProjectName = draft.ProjectName,
            ProjectNameCommitLevel = string.IsNullOrWhiteSpace(draft.ProjectName)
                ? string.Empty
                : HighLevelMemoryCommitLevel.Confirmed.ToString(),
            ProjectNameSource = string.IsNullOrWhiteSpace(draft.ProjectName)
                ? string.Empty
                : HighLevelMemorySource.ConfirmedUser.ToString(),
            ProjectNameCommitReason = string.IsNullOrWhiteSpace(draft.ProjectName)
                ? string.Empty
                : "fallback projection from persisted draft",
            LastTaskType = draft.TaskType,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private HighLevelMemoryState ResolvePromotableMemory(
        string channel,
        string userId,
        HighLevelUserProfile profile,
        HighLevelTaskDraft draft)
    {
        var latestMemory = _memoryStore.ReadLatest(channel, userId);
        if (latestMemory == null)
        {
            return BuildFallbackMemoryState(channel, userId, profile, draft);
        }

        var matchesDraft =
            string.Equals(latestMemory.PendingDraftId, draft.DraftId, StringComparison.Ordinal) &&
            string.Equals(latestMemory.LastTaskType, draft.TaskType, StringComparison.OrdinalIgnoreCase);

        var eligibleForPromotion =
            string.Equals(latestMemory.LastRouteMode, HighLevelRouteMode.Production.ToString(), StringComparison.OrdinalIgnoreCase) &&
            (!draft.RequiresProjectName || string.Equals(latestMemory.ProjectName, draft.ProjectName, StringComparison.Ordinal));

        if (matchesDraft && eligibleForPromotion)
        {
            return latestMemory;
        }

        _logger.LogInformation(
            "Promotion memory fallback: channel={Channel} user={UserId} draft={DraftId} route={Route} pendingDraft={PendingDraft}",
            channel,
            userId,
            draft.DraftId,
            latestMemory.LastRouteMode,
            latestMemory.PendingDraftId);

        return BuildFallbackMemoryState(channel, userId, profile, draft);
    }

    private HighLevelRouteDecision Classify(HighLevelParsedInput parsed)
    {
        if (parsed.Kind == HighLevelInputKind.Production)
        {
            var taskType = InferTaskType(parsed.Normalized);
            return new HighLevelRouteDecision
            {
                Mode = HighLevelRouteMode.Production,
                TaskType = taskType,
                Reason = "matched production prefix"
            };
        }

        if (parsed.Kind == HighLevelInputKind.Query || parsed.Kind == HighLevelInputKind.Help)
        {
            return new HighLevelRouteDecision
            {
                Mode = HighLevelRouteMode.Query,
                TaskType = "query",
                Reason = "matched query prefix"
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
        if (ContainsAny(normalized, _options.SystemScaffoldKeywords)) return "system_scaffold";
        if (ContainsAny(normalized, _options.CodeModifyKeywords)) return "code_modify";
        if (ContainsAny(normalized, _options.DocKeywords)) return "doc_gen";
        if (ContainsAny(normalized, _options.CodeGenKeywords)) return "code_gen";
        return "task_management";
    }

    private HighLevelTaskDraft CreateDraft(
        string channel,
        string userId,
        HighLevelUserProfile profile,
        string message,
        HighLevelRouteDecision decision)
    {
        var summary = message.Length <= _options.MaxDraftSummaryLength
            ? message
            : message[.._options.MaxDraftSummaryLength] + "...";

        var title = decision.TaskType switch
        {
            "system_scaffold" => $"Generate system scaffold from {channel} request",
            "code_modify" => $"Modify artifact from {channel} request",
            "doc_gen" => $"Generate document from {channel} request",
            "code_gen" => $"Generate deliverable from {channel} request",
            _ => $"Production task from {channel} request"
        };
        var requiresProjectName = decision.TaskType is "code_gen" or "system_scaffold";
        var inlineProjectName = requiresProjectName ? TryExtractInlineProjectName(message) : null;
        var inlineProjectFolderName = string.IsNullOrWhiteSpace(inlineProjectName)
            ? null
            : SanitizePathSegment(inlineProjectName, "project");
        var managedPaths = BuildManagedPaths(channel, userId, profile, inlineProjectFolderName);

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
            ProjectName = inlineProjectName,
            ProjectFolderName = inlineProjectFolderName,
            ProposedPhases = BuildProposedPhases(decision.TaskType),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(Math.Max(1, _options.DraftTtlMinutes))
        };

        if (decision.TaskType == "system_scaffold")
            _systemScaffoldService.InitializeDraft(draft);

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

        if (taskType == "system_scaffold")
        {
            return new List<HighLevelTaskPhase>
            {
                new() { PhaseId = "requirements", Title = "Analyze requirements", Kind = "requirements_analysis" },
                new() { PhaseId = "design", Title = "Plan design", Kind = "design_planning" },
                new() { PhaseId = "implement", Title = "Generate scaffold", Kind = "artifact_creation" },
                new() { PhaseId = "test", Title = "Run basic verification", Kind = "verification" },
                new() { PhaseId = "package", Title = "Package scaffold", Kind = "packaging" },
                new() { PhaseId = "deliver", Title = "Deliver artifact", Kind = "delivery" }
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
        var permissions = HighLevelUserPermissions.CreateDefault();
        return string.Join('\n', new[]
        {
            $"- 受控網路搜尋：{(permissions.AllowQuery ? "允許" : "停用")}",
            $"- 交通查詢：{(permissions.AllowTransport ? "允許" : "停用")}",
            $"- Production 任務：{(permissions.AllowProduction ? "允許" : "停用")}",
            $"- 使用者授權網站：{(permissions.AllowBrowserDelegated ? "允許" : "停用")}",
            $"- 佈署能力：{(permissions.AllowDeployment ? "允許" : "停用")}",
            $"- 受控網路搜尋：{(permissions.AllowQuery ? "允許" : "停用")}",
            $"- 交通查詢：{(permissions.AllowTransport ? "允許" : "停用")}",
            $"- Production 任務：{(permissions.AllowProduction ? "允許" : "停用")}",
            $"- 使用者授權網站：{(permissions.AllowBrowserDelegated ? "允許" : "停用")}",
            $"- 佈署能力：{(permissions.AllowDeployment ? "允許" : "停用")}",
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
            "\u82e5\u78ba\u8a8d\u8981\u5efa\u7acb task / plan\uff0c\u8acb\u56de\u8986\u300c\u78ba\u8a8d\u300d\u3001confirm \u6216 y\u3002",
            "\u82e5\u8981\u53d6\u6d88\uff0c\u8acb\u56de\u8986\u300c\u53d6\u6d88\u300d\u3001cancel \u6216 n\u3002"
        }.Skip(10).Where(line => !string.IsNullOrWhiteSpace(line)));
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
            "\u8acb\u5148\u56de\u8986\u300c\u78ba\u8a8d\u300d / confirm / y \u6216\u300c\u53d6\u6d88\u300d / cancel / n\uff0c\u518d\u7e7c\u7e8c\u4e0b\u4e00\u500b\u751f\u7522\u4efb\u52d9\u3002"
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
                ? "\u8acb\u4ee5 # \u958b\u982d\u63d0\u4f9b\u5c08\u6848\u540d\u7a31\uff0c\u4f8b\u5982\uff1a#MySite\u3002"
                : draft.ProjectNameValidationError,
            "\u5c08\u6848\u540d\u7a31\u6703\u5728\u4f60\u7684 user_root/projects \u4e0b\u5efa\u7acb\u5c08\u5c6c\u76ee\u9304\uff0c\u540c\u540d\u5c08\u6848\u4e0d\u6703\u91cd\u8907\u5efa\u7acb\u3002",
            "\u82e5\u8981\u53d6\u6d88\uff0c\u8acb\u56de\u8986\u300c\u53d6\u6d88\u300d\u3001cancel \u6216 n\u3002"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildCompactDraftConfirmationReply(HighLevelTaskDraft draft)
    {
        return string.Join('\n', new[]
        {
            "已建立 production draft。",
            $"task_type: {draft.TaskType}",
            $"summary: {draft.Summary}",
            string.IsNullOrWhiteSpace(draft.ProjectName) ? null : $"project_name: {draft.ProjectName}",
            "下一步請直接回覆下方指令。"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildCompactPendingDraftReminder(HighLevelTaskDraft draft)
    {
        return string.Join('\n', new[]
        {
            "目前仍有一個待確認的 production draft。",
            $"task_type: {draft.TaskType}",
            $"summary: {draft.Summary}",
            string.IsNullOrWhiteSpace(draft.ProjectName) ? null : $"project_name: {draft.ProjectName}",
            "下一步請直接回覆下方指令。"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static string BuildCompactProjectNameRequestReply(HighLevelTaskDraft draft)
    {
        return string.Join('\n', new[]
        {
            "這是一個需要專案名稱的 production 請求。",
            $"task_type: {draft.TaskType}",
            string.IsNullOrWhiteSpace(draft.ProjectNameValidationError)
                ? "請以 # 開頭提供專案名稱。"
                : draft.ProjectNameValidationError,
            "下一步請直接回覆下方指令。"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static List<string> BuildDraftFollowUpMessages(HighLevelTaskDraft draft)
        => new()
        {
            "y",
            "n"
        };

    private static List<string> BuildProjectNameFollowUpMessages(HighLevelTaskDraft draft)
        => new()
        {
            "#MySite",
            "n"
        };

    private static string BuildControlledSearchSuggestionReply(HighLevelParsedInput parsed)
    {
        var target = parsed.Kind == HighLevelInputKind.Query && !string.IsNullOrWhiteSpace(parsed.Body)
            ? parsed.Body
            : parsed.Raw;
        var normalized = Normalize(target);

        if (ContainsAny(normalized, new[] { "擃", "hsr", "thsr" }))
            return "這題較適合做高鐵查詢。";

        if (ContainsAny(normalized, new[] { "?怨?", "?圈", "rail", "train" }))
            return "這題較適合做火車查詢。";

        if (ContainsAny(normalized, new[] { "?祈?", "摰ａ?", "bus" }))
            return "這題較適合做公車查詢。";

        if (ContainsAny(normalized, new[] { "?芰", "璈巨", "flight", "flights" }))
            return "這題較適合做航班查詢。";

        return "這題較適合做受控搜尋。";
    }

    private List<string> BuildControlledSearchFollowUpMessages(HighLevelParsedInput parsed)
        => new()
        {
            BuildControlledSearchSuggestion(parsed)
        };

    private string BuildProfileReply(HighLevelUserProfile profile, HighLevelTaskDraft? draft)
    {
        var managedPaths = BuildManagedPaths(profile.Channel, profile.UserId, profile, null);
        return string.Join('\n', new[]
        {
            "目前使用者設定如下：",
            $"line_user_id: {profile.UserId}",
            $"display_name: {profile.PreferredDisplayName ?? "(not set)"}",
            $"user_code: {profile.PreferredUserCode ?? "(not set)"}",
            $"user_root: {managedPaths.UserRoot}",
            $"documents_root: {managedPaths.DocumentsRoot}",
            $"conversations_root: {managedPaths.ConversationsRoot}",
            $"projects_root: {managedPaths.ProjectsRoot}",
            draft?.DraftId is null ? null : $"pending_draft_id: {draft.DraftId}",
            "",
            BuildPermissionSummary(profile),
            "",
            "可用設定指令：",
            "- /name <稱呼>",
            "- /id <英數字ID>",
            "- ?profile",
            "- ?help"
        }.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private bool TryUpdatePreferredDisplayName(
        string channel,
        string userId,
        HighLevelUserProfile profile,
        string rawValue,
        out string reply)
    {
        var displayName = NormalizeDisplayName(rawValue);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            reply = "稱呼不能是空白。請使用 /name <稱呼>，例如 /name 小布。";
            return false;
        }

        profile.PreferredDisplayName = displayName;
        var managedPaths = BuildManagedPaths(channel, userId, profile, null);
        reply = string.Join('\n', new[]
        {
            $"已更新稱呼為 {displayName}。",
            $"之後回覆會優先使用這個稱呼。",
            $"目前 user_root: {managedPaths.UserRoot}"
        });
        return true;
    }

    private string BuildPermissionSummary(HighLevelUserProfile profile)
    {
        var permissions = EnsurePermissions(profile);
        var principalCandidates = ResolvePrincipalCandidates(profile).ToArray();
        var activeUserGrants = principalCandidates
            .SelectMany(principalId => _browserBindingService.ListUserGrants(principalId))
            .Where(grant => grant.Status == "active" && (grant.ExpiresAt == null || grant.ExpiresAt > DateTime.UtcNow))
            .GroupBy(grant => grant.UserGrantId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var activeUserSites = principalCandidates
            .SelectMany(principalId => _browserBindingService.ListSiteBindings("user_delegated", principalId))
            .Where(binding => binding.Status == "active")
            .GroupBy(binding => binding.SiteBindingId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return string.Join('\n', new[]
        {
            "目前擁有的權限：",
            "- 高階對話與需求澄清",
            "- 受控網路搜尋：?search（快捷：?s）",
            "- 交通查詢：?rail、?hsr、?bus、?flight（快捷：?r、?hsr、?b、?f）",
            "- 建立 production draft：/ 指令",
            "- 個人設定：/name、/id、?profile、?help（快捷：/n、/i、?p、?h）",
            activeUserGrants.Length == 0
                ? "- 使用者授權網站能力：目前沒有"
                : $"- 使用者授權網站能力：{activeUserGrants.Length} 個 grant、{activeUserSites.Length} 個 site binding"
        });
    }

    private static IEnumerable<string> ResolvePrincipalCandidates(HighLevelUserProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.Channel) && !string.IsNullOrWhiteSpace(profile.UserId))
            yield return $"{profile.Channel}:{profile.UserId}";

        if (!string.IsNullOrWhiteSpace(profile.UserId))
            yield return profile.UserId;

        if (!string.IsNullOrWhiteSpace(profile.PreferredUserCode))
            yield return profile.PreferredUserCode;
    }

    private bool IsTransportQueryCommand(HighLevelParsedInput parsed)
        => parsed.Kind == HighLevelInputKind.Query &&
           parsed.QueryCommand != null &&
           (string.Equals(parsed.QueryCommand, "rail", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parsed.QueryCommand, "hsr", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parsed.QueryCommand, "bus", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parsed.QueryCommand, "flight", StringComparison.OrdinalIgnoreCase));

    private bool TryHandlePermissionGate(
        string channel,
        string userId,
        string message,
        HighLevelUserProfile profile,
        HighLevelTaskDraft? draft,
        HighLevelParsedInput parsed,
        HighLevelTrustedParseResult trustedParse,
        HighLevelWorkflowDecision workflow,
        HighLevelRouteDecision decision,
        out HighLevelProcessResult result)
    {
        result = default!;
        var permissions = EnsurePermissions(profile);

        if (decision.Mode == HighLevelRouteMode.Production &&
            draft == null &&
            parsed.Kind == HighLevelInputKind.Production &&
            !string.Equals(parsed.ProductionCommand, "name", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(parsed.ProductionCommand, "id", StringComparison.OrdinalIgnoreCase) &&
            !permissions.AllowProduction)
        {
            result = BuildPermissionDeniedResult(
                channel, userId, message, profile, trustedParse, workflow,
                HighLevelRouteMode.Production,
                "目前你的帳戶不能建立 production 任務。若需要此權限，請聯絡管理員。",
                "production_disabled");
            return true;
        }

        if (IsTransportQueryCommand(parsed) && !permissions.AllowTransport)
        {
            result = BuildPermissionDeniedResult(
                channel, userId, message, profile, trustedParse, workflow,
                HighLevelRouteMode.Query,
                "目前你的帳戶不能使用交通查詢。若需要此權限，請聯絡管理員。",
                "transport_disabled");
            return true;
        }

        if (decision.Mode == HighLevelRouteMode.Query &&
            !IsTransportQueryCommand(parsed) &&
            !permissions.AllowQuery)
        {
            result = BuildPermissionDeniedResult(
                channel, userId, message, profile, trustedParse, workflow,
                HighLevelRouteMode.Query,
                "目前你的帳戶不能使用受控查詢。若需要此權限，請聯絡管理員。",
                "query_disabled");
            return true;
        }

        return false;
    }

    private HighLevelProcessResult BuildPermissionDeniedResult(
        string channel,
        string userId,
        string message,
        HighLevelUserProfile profile,
        HighLevelTrustedParseResult trustedParse,
        HighLevelWorkflowDecision workflow,
        HighLevelRouteMode mode,
        string reply,
        string error)
    {
        SaveUserProfile(channel, userId, profile);

        return FinalizeResult(channel, userId, BuildLineEnvelope(message), trustedParse, workflow, new HighLevelProcessResult
        {
            Mode = mode,
            Reply = PrepareReplySafe(profile, message, reply),
            Error = error,
            DecisionReason = "user permission gate denied request"
        });
    }

    private bool TryUpdatePreferredUserCode(
        string channel,
        string userId,
        HighLevelUserProfile profile,
        HighLevelTaskDraft? draft,
        string rawValue,
        out string reply)
    {
        var userCode = rawValue.Trim();
        if (!PreferredUserCodePattern.IsMatch(userCode))
        {
            reply = "使用者 ID 只能包含英文字母與數字，長度需介於 3 到 32。請使用 /id <AlphanumericId>，例如 /id bricks001。";
            return false;
        }

        var currentCode = profile.PreferredUserCode;
        if (string.Equals(currentCode, userCode, StringComparison.OrdinalIgnoreCase))
        {
            reply = $"使用者 ID 已經是 {currentCode}。";
            return true;
        }

        var existingReservation = LoadUserCodeReservation(channel, userCode);
        if (existingReservation != null && !string.Equals(existingReservation.UserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            reply = $"使用者 ID {userCode} 已被其他使用者占用，請換一個新的英數字 ID。";
            return false;
        }

        var currentPaths = BuildManagedPaths(channel, userId, profile, draft?.ProjectFolderName);
        var candidateProfile = new HighLevelUserProfile
        {
            Channel = profile.Channel,
            UserId = profile.UserId,
            PreferredDisplayName = profile.PreferredDisplayName,
            PreferredUserCode = userCode
        };
        var nextPaths = BuildManagedPaths(channel, userId, candidateProfile, draft?.ProjectFolderName);

        if (!TryMoveManagedWorkspace(currentPaths, nextPaths, out var moveError))
        {
            reply = moveError;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(currentCode))
        {
            DeleteDocument(BuildUserCodeDocumentId(channel, currentCode));
        }

        profile.PreferredUserCode = userCode;
        SaveUserCodeReservation(channel, userId, userCode);

        if (draft != null)
        {
            draft.ManagedPaths = nextPaths;
            UpdateDraftDescriptors(draft);
        }

        reply = string.Join('\n', new[]
        {
            $"已更新使用者 ID 為 {userCode}。",
            $"之後個人工作區與延伸服務識別會使用這個 ID。",
            $"目前 user_root: {nextPaths.UserRoot}"
        });
        return true;
    }

    private bool TryMoveManagedWorkspace(HighLevelManagedPaths currentPaths, HighLevelManagedPaths nextPaths, out string error)
    {
        error = string.Empty;
        if (string.Equals(currentPaths.UserRoot, nextPaths.UserRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Directory.Exists(nextPaths.UserRoot))
        {
            error = $"目標 user_root 已存在：{nextPaths.UserRoot}。請換一個不重複的使用者 ID。";
            return false;
        }

        Directory.CreateDirectory(nextPaths.ChannelRoot);
        if (Directory.Exists(currentPaths.UserRoot))
        {
            Directory.Move(currentPaths.UserRoot, nextPaths.UserRoot);
        }

        return true;
    }

    private HighLevelTaskHandoff BuildHandoff(
        BrokerTask task,
        Plan plan,
        HighLevelTaskDraft draft,
        HighLevelExecutionIntent executionIntent,
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
            RuntimeDescriptor = JsonSerializer.Deserialize<JsonElement>(task.RuntimeDescriptor),
            ScopeDescriptor = JsonSerializer.Deserialize<JsonElement>(task.ScopeDescriptor),
            ConversationDocument = BuildConversationDocumentId(userId),
            UserProfileDocument = BuildProfileDocumentId(channel, userId),
            ExecutionIntentId = executionIntent.IntentId,
            ExecutionIntentDocument = HighLevelExecutionIntentStore.BuildDocumentId(channel, userId),
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
            preferred_user_code = LoadUserProfile(draft.Channel, draft.UserId)?.PreferredUserCode,
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
            preferred_display_name = LoadUserProfile(draft.Channel, draft.UserId)?.PreferredDisplayName,
            preferred_user_code = LoadUserProfile(draft.Channel, draft.UserId)?.PreferredUserCode,
            high_level = true,
            conversation_document = BuildConversationDocumentId(draft.UserId),
            user_profile_document = BuildProfileDocumentId(draft.Channel, draft.UserId),
            draft_document = BuildDraftDocumentId(draft.Channel, draft.UserId),
            scaffold_spec_document = string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                ? HighLevelSystemScaffoldSpecStore.BuildDocumentId(draft.Channel, draft.UserId)
                : null,
            scaffold_iteration_document = string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                ? HighLevelSystemScaffoldIterationStore.BuildDocumentId(draft.Channel, draft.UserId)
                : null,
            managed_paths = draft.ManagedPaths,
            project = new
            {
                required = draft.RequiresProjectName,
                name = draft.ProjectName,
                folder_name = draft.ProjectFolderName
            },
            scaffold = string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                ? draft.ScaffoldSpec
                : null
        });
    }

    private HighLevelExecutionIntent BuildExecutionIntent(
        string channel,
        string userId,
        HighLevelTaskDraft draft,
        HighLevelMemoryState memory,
        HighLevelExecutionPromotionDecision promotion,
        HighLevelExecutionModelRequest? requestedExecutionModel)
    {
        return new HighLevelExecutionIntent
        {
            Channel = channel,
            UserId = userId,
            Stage = promotion.Stage,
            PromotionReason = promotion.Reason,
            Goal = memory.CurrentGoal ?? ToMemoryGoal(draft.OriginalMessage),
            TaskType = draft.TaskType,
            ProjectName = draft.ProjectName,
            DraftId = draft.DraftId,
            RequestedExecutionModel = requestedExecutionModel,
            ScopeDescriptor = draft.ScopeDescriptor,
            RuntimeDescriptor = draft.RuntimeDescriptor,
            DocumentId = HighLevelExecutionIntentStore.BuildDocumentId(channel, userId)
        };
    }

    private string BuildPromotedRuntimeDescriptor(HighLevelTaskDraft draft, HighLevelExecutionIntent executionIntent)
    {
        return JsonSerializer.Serialize(new
        {
            source = draft.Channel,
            source_user_id = draft.UserId,
            preferred_display_name = LoadUserProfile(draft.Channel, draft.UserId)?.PreferredDisplayName,
            preferred_user_code = LoadUserProfile(draft.Channel, draft.UserId)?.PreferredUserCode,
            high_level = true,
            conversation_document = BuildConversationDocumentId(draft.UserId),
            user_profile_document = BuildProfileDocumentId(draft.Channel, draft.UserId),
            draft_document = BuildDraftDocumentId(draft.Channel, draft.UserId),
            execution_intent_document = HighLevelExecutionIntentStore.BuildDocumentId(draft.Channel, draft.UserId),
            execution_intent_id = executionIntent.IntentId,
            execution_stage = executionIntent.Stage,
            promotion_reason = executionIntent.PromotionReason,
            scaffold_spec_document = string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                ? HighLevelSystemScaffoldSpecStore.BuildDocumentId(draft.Channel, draft.UserId)
                : null,
            scaffold_iteration_document = string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                ? HighLevelSystemScaffoldIterationStore.BuildDocumentId(draft.Channel, draft.UserId)
                : null,
            requested_execution_model = executionIntent.RequestedExecutionModel,
            llm = executionIntent.RequestedExecutionModel == null
                ? null
                : new
                {
                    default_model = executionIntent.RequestedExecutionModel.Model,
                    allow_model_override = false
                },
            managed_paths = draft.ManagedPaths,
            project = new
            {
                required = draft.RequiresProjectName,
                name = draft.ProjectName,
                folder_name = draft.ProjectFolderName
            },
            scaffold = string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase)
                ? draft.ScaffoldSpec
                : null
        });
    }

    private string BuildPromotedScopeDescriptor(HighLevelTaskDraft draft, HighLevelExecutionIntent executionIntent)
    {
        using var baseScope = JsonDocument.Parse(draft.ScopeDescriptor);
        return JsonSerializer.Serialize(new
        {
            channel = draft.Channel,
            origin_user_id = draft.UserId,
            mode = "production",
            source = "high-level-coordinator",
            execution_intent_id = executionIntent.IntentId,
            execution_intent_document = HighLevelExecutionIntentStore.BuildDocumentId(draft.Channel, draft.UserId),
            path_scope = baseScope.RootElement.TryGetProperty("path_scope", out var pathScope)
                ? JsonSerializer.Deserialize<object>(pathScope.GetRawText())
                : null
        });
    }

    private HighLevelManagedPaths BuildManagedPaths(
        string channel,
        string userId,
        HighLevelUserProfile? profile,
        string? projectFolderName)
    {
        var safeChannel = SanitizePathSegment(channel, "channel");
        var userFolderName = ResolveUserFolderName(profile, userId);
        var channelRoot = Path.Combine(_accessRoot, safeChannel);
        var userRoot = Path.Combine(channelRoot, userFolderName);
        var conversationsRoot = Path.Combine(userRoot, "conversations");
        var documentsRoot = Path.Combine(userRoot, "documents");
        var projectsRoot = Path.Combine(userRoot, "projects");

        return new HighLevelManagedPaths
        {
            AccessRoot = _accessRoot,
            ChannelRoot = channelRoot,
            UserFolderName = userFolderName,
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

        var profile = LoadUserProfile(draft.Channel, draft.UserId);
        var managedPaths = BuildManagedPaths(draft.Channel, draft.UserId, profile, projectFolderName);
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
        else if (draft.TaskType == "system_scaffold")
        {
            draft.Title = $"Generate system scaffold {projectName} from {draft.Channel} request";
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
        if (string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase))
            _systemScaffoldService.RefreshDraftState(draft, "requirements_analysis", "updated", "已記錄系統雛形專案名稱。");
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
        var profile = LoadUserProfile(draft.Channel, draft.UserId);
        draft.ManagedPaths = BuildManagedPaths(draft.Channel, draft.UserId, profile, null);
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

    private static string? TryExtractInlineProjectName(string message)
    {
        var trimmed = message.Trim();
        var directHashMatch = Regex.Match(trimmed, @"(?:^|\s)[#＃](?<name>[^\s#＃]+)\s*$", RegexOptions.CultureInvariant);
        if (directHashMatch.Success)
        {
            var candidate = NormalizeProjectNameInput(directHashMatch.Groups["name"].Value);
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return TryExtractProjectNameHint(message);
    }

    private static string NormalizeProjectNameInput(string rawInput)
    {
        var candidate = rawInput.Trim();
        candidate = Regex.Replace(candidate, @"^(?:project\s*name|project|專案名稱|name)\s*[:：]\s*", string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        candidate = candidate.Trim().Trim('"', '\'', '“', '”', '「', '」', '『', '』');
        return candidate;
    }

    private static string NormalizeDisplayName(string rawInput)
    {
        var candidate = rawInput.Trim();
        candidate = Regex.Replace(candidate, @"\s+", " ");
        candidate = new string(candidate.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (candidate.Length > 40)
        {
            candidate = candidate[..40].Trim();
        }

        return candidate;
    }

    private static string ResolveUserFolderName(HighLevelUserProfile? profile, string userId)
    {
        if (!string.IsNullOrWhiteSpace(profile?.PreferredUserCode))
        {
            return SanitizePathSegment(profile.PreferredUserCode, "user");
        }

        return SanitizePathSegment(userId, "user");
    }

    private static bool IsTestLineAccount(string userId)
        => !string.IsNullOrWhiteSpace(userId) && !LineUserIdPattern.IsMatch(userId);

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
            return HighLevelCoordinatorDefaults.DefaultAccessRoot;
        }

        var expanded = Environment.ExpandEnvironmentVariables(configuredRoot.Trim());
        if (!Path.IsPathRooted(expanded))
        {
            throw new InvalidOperationException(
                "HighLevelCoordinator:AccessRoot must be an absolute path. Relative paths are not allowed.");
        }

        return Path.GetFullPath(expanded);
    }

    private HighLevelUserProfile? LoadUserProfile(string channel, string userId)
    {
        var profile = LoadLatestJson<HighLevelUserProfile>(BuildProfileDocumentId(channel, userId));
        if (profile != null)
        {
            EnsurePermissions(profile);
        }

        return profile;
    }

    private HighLevelUserCodeReservation? LoadUserCodeReservation(string channel, string userCode)
        => LoadLatestJson<HighLevelUserCodeReservation>(BuildUserCodeDocumentId(channel, userCode));

    private HighLevelTaskDraft? ResolvePendingDraft(string channel, string userId, HighLevelUserProfile profile)
    {
        var persisted = LoadTaskDraft(channel, userId);
        if (persisted != null)
        {
            return persisted;
        }

        if (string.IsNullOrWhiteSpace(profile.PendingDraftId) ||
            string.IsNullOrWhiteSpace(profile.PendingDraftTaskType) ||
            string.IsNullOrWhiteSpace(profile.PendingDraftOriginalMessage))
        {
            return null;
        }

        var draft = new HighLevelTaskDraft
        {
            DraftId = profile.PendingDraftId,
            Channel = channel,
            UserId = userId,
            OriginalMessage = profile.PendingDraftOriginalMessage,
            Summary = profile.PendingDraftSummary ?? profile.PendingDraftOriginalMessage,
            TaskType = profile.PendingDraftTaskType,
            Title = profile.PendingDraftTitle ?? "Production task from line request",
            Description = profile.PendingDraftDescription ?? $"Origin: {channel}:{userId}\n\nUser request:\n{profile.PendingDraftOriginalMessage}",
            RequiresProjectName = profile.PendingDraftRequiresProjectName,
            ProjectName = profile.PendingDraftProjectName,
            ProjectFolderName = profile.PendingDraftProjectFolderName,
            ManagedPaths = BuildManagedPaths(channel, userId, profile, profile.PendingDraftProjectFolderName),
            ProposedPhases = BuildProposedPhases(profile.PendingDraftTaskType),
            CreatedAt = profile.PendingDraftCreatedAt ?? DateTime.UtcNow,
            ExpiresAt = profile.PendingDraftExpiresAt ?? DateTime.UtcNow.AddMinutes(Math.Max(1, _options.DraftTtlMinutes))
        };
        UpdateDraftDescriptors(draft);

        _logger.LogInformation(
            "Rehydrated pending draft from profile snapshot: channel={Channel} user={UserId} draft={DraftId}",
            channel,
            userId,
            draft.DraftId);

        return draft;
    }

    private void SaveUserProfile(string channel, string userId, HighLevelUserProfile profile)
    {
        profile.Channel = channel;
        profile.UserId = userId;
        EnsurePermissions(profile);
        UpsertDocument(
            BuildProfileDocumentId(channel, userId),
            BuildProfileDocumentId(channel, userId),
            JsonSerializer.Serialize(profile),
            "application/json",
            "global");
    }

    private static HighLevelUserPermissions EnsurePermissions(HighLevelUserProfile profile)
    {
        profile.Permissions ??= HighLevelUserPermissions.CreateDefault();
        return profile.Permissions;
    }

    private static void UpdatePendingDraftSnapshot(HighLevelUserProfile profile, HighLevelTaskDraft draft)
    {
        profile.PendingDraftId = draft.DraftId;
        profile.PendingDraftOriginalMessage = draft.OriginalMessage;
        profile.PendingDraftSummary = draft.Summary;
        profile.PendingDraftTaskType = draft.TaskType;
        profile.PendingDraftTitle = draft.Title;
        profile.PendingDraftDescription = draft.Description;
        profile.PendingDraftRequiresProjectName = draft.RequiresProjectName;
        profile.PendingDraftProjectName = draft.ProjectName;
        profile.PendingDraftProjectFolderName = draft.ProjectFolderName;
        profile.PendingDraftCreatedAt = draft.CreatedAt;
        profile.PendingDraftExpiresAt = draft.ExpiresAt;
    }

    private static void ClearPendingDraftSnapshot(HighLevelUserProfile profile)
    {
        profile.PendingDraftId = null;
        profile.PendingDraftOriginalMessage = null;
        profile.PendingDraftSummary = null;
        profile.PendingDraftTaskType = null;
        profile.PendingDraftTitle = null;
        profile.PendingDraftDescription = null;
        profile.PendingDraftProjectName = null;
        profile.PendingDraftProjectFolderName = null;
        profile.PendingDraftRequiresProjectName = false;
        profile.PendingDraftCreatedAt = null;
        profile.PendingDraftExpiresAt = null;
    }

    private void SaveUserCodeReservation(string channel, string userId, string userCode)
    {
        var reservation = new HighLevelUserCodeReservation
        {
            Channel = channel,
            UserCode = userCode,
            UserId = userId,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        UpsertDocument(
            BuildUserCodeDocumentId(channel, userCode),
            BuildUserCodeDocumentId(channel, userCode),
            JsonSerializer.Serialize(reservation),
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
        var json = _db.Scalar<string>(
            "SELECT content_ref FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId });

        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to deserialize high-level document {DocumentId} as {TypeName}",
                documentId,
                typeof(T).Name);
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

    private string GetAnonymousRegistrationPolicy(string channel)
    {
        var state = LoadLatestJson<HighLevelRegistrationPolicyState>(BuildRegistrationPolicyDocumentId(channel));
        return NormalizeAnonymousRegistrationPolicy(state?.Policy);
    }

    private static string NormalizeAnonymousRegistrationPolicy(string? policy)
        => Normalize(policy ?? string.Empty) switch
        {
            HighLevelAnonymousRegistrationPolicy.DenyAll => HighLevelAnonymousRegistrationPolicy.DenyAll,
            HighLevelAnonymousRegistrationPolicy.ManualReview => HighLevelAnonymousRegistrationPolicy.ManualReview,
            _ => HighLevelAnonymousRegistrationPolicy.AllowAll
        };

    private static string ResolveRegistrationStatus(HighLevelUserProfile profile)
        => string.IsNullOrWhiteSpace(profile.RegistrationStatus)
            ? HighLevelRegistrationStatus.Approved
            : Normalize(profile.RegistrationStatus) switch
            {
                HighLevelRegistrationStatus.PendingReview => HighLevelRegistrationStatus.PendingReview,
                HighLevelRegistrationStatus.Rejected => HighLevelRegistrationStatus.Rejected,
                HighLevelRegistrationStatus.DeniedByPolicy => HighLevelRegistrationStatus.DeniedByPolicy,
                _ => HighLevelRegistrationStatus.Approved
            };

    private HighLevelLineNotification EnqueueLineNotification(string userId, string title, string body)
    {
        var notification = new HighLevelLineNotification
        {
            NotificationId = IdGen.New("hlmnoti"),
            Channel = "line",
            UserId = userId,
            Title = title,
            Body = body,
            DeliveryStatus = "pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        UpsertDocument(
            BuildLineNotificationDocumentId(notification.NotificationId),
            BuildLineNotificationDocumentId(notification.NotificationId),
            JsonSerializer.Serialize(notification),
            "application/json",
            "global");

        return notification;
    }

    private static string NormalizeNotificationStatus(string? status)
        => Normalize(status ?? string.Empty) switch
        {
            "sent" => "sent",
            "failed" => "failed",
            _ => "pending"
        };

    private static string BuildConversationDocumentId(string userId)
        => $"{ConversationDocumentPrefix}{userId}";

    private static string BuildProfileDocumentId(string channel, string userId)
        => $"hlm.profile.{channel}.{userId}";

    private static string BuildRegistrationPolicyDocumentId(string channel)
        => $"hlm.registration-policy.{channel}";

    private static string BuildDraftDocumentId(string channel, string userId)
        => $"hlm.draft.{channel}.{userId}";

    private static string BuildHandoffDocumentId(string taskId)
        => $"hlm.handoff.{taskId}";

    private static string BuildUserCodeDocumentId(string channel, string userCode)
        => $"hlm.usercode.{channel}.{Normalize(userCode)}";

    private static string BuildLineNotificationDocumentId(string notificationId)
        => $"hlm.notify.line.{notificationId}";

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

    private static string BuildPendingRegistrationReply(HighLevelUserProfile profile)
    {
        var note = string.IsNullOrWhiteSpace(profile.RegistrationReviewNote)
            ? string.Empty
            : $"\n備註：{profile.RegistrationReviewNote}";
        return $"目前採人工審核註冊。\n已收到你的申請，審核完成後會再通知。{note}";
    }

    private static string BuildRejectedRegistrationReply(HighLevelUserProfile profile)
    {
        var note = string.IsNullOrWhiteSpace(profile.RegistrationReviewNote)
            ? string.Empty
            : $"\n原因：{profile.RegistrationReviewNote}";
        return $"目前無法使用此服務。{note}";
    }

    private string PrepareReplySafe(HighLevelUserProfile profile, string message, string reply)
    {
        var now = DateTimeOffset.UtcNow;
        profile.LastInteractionAt = now;

        var personalizedReply = string.IsNullOrWhiteSpace(profile.PreferredDisplayName)
            ? reply
            : $"{profile.PreferredDisplayName}，\n{reply}";

        var guideMode = GetCommandGuideMode(profile, message);
        if (guideMode == CommandGuideMode.None)
        {
            return personalizedReply;
        }

        profile.LastCommandGuideAt = now;
        if (guideMode == CommandGuideMode.Full)
        {
            return string.Join('\n', new[]
            {
                personalizedReply,
                "",
                BuildCommandGuideBlockSafe(profile)
            });
        }

        return string.Join('\n', new[]
        {
            personalizedReply,
            "",
            "若要再次查看規則，請輸入 ?help。"
        });
    }

    private static string PrepareReplyWithoutGuide(HighLevelUserProfile profile, string reply)
    {
        profile.LastInteractionAt = DateTimeOffset.UtcNow;

        return string.IsNullOrWhiteSpace(profile.PreferredDisplayName)
            ? reply
            : $"{profile.PreferredDisplayName}：\n{reply}";
    }

    private string BuildHelpReplySafe(HighLevelUserProfile profile, HighLevelTaskDraft? draft)
    {
        if (draft?.RequiresProjectName == true && string.IsNullOrWhiteSpace(draft.ProjectName))
        {
            return string.Join('\n', new[]
            {
                "目前這個 production draft 正在等待專案名稱。",
                "請用 # 開頭提供專案名稱，例如 #MySite。",
                "",
                BuildCommandGuideBlockSafe(profile)
            });
        }

        return BuildCommandGuideBlockSafe(profile);
    }

    private string BuildCommandGuideBlockSafe(HighLevelUserProfile profile)
    {
        return string.Join('\n', new[]
        {
            BuildPermissionSummary(profile),
            "",
            "使用規則：",
            "- 一般對話：直接輸入",
            "- 顯式搜尋：?search 關鍵字（快捷：?s 關鍵字）",
            "- 火車查詢：?rail 條件（快捷：?r 條件）",
            "- 高鐵查詢：?hsr 條件（快捷：?hsr 條件）",
            "- 公車查詢：?bus 條件（快捷：?b 條件）",
            "- 航班查詢：?flight 條件（快捷：?f 條件）",
            "- 任務或指令：/內容",
            "- 專案名稱：#名稱",
            "- 個人設定：/name <稱呼>（快捷：/n）、/id <英數字ID>（快捷：/i）",
            "- 查看個人設定：?profile（快捷：?p）",
            "- 查看說明：?help（快捷：?h）",
            "- 確認 / 取消：確認、confirm、取消、cancel"
        });
    }

    private string PrepareReplyClean(HighLevelUserProfile profile, string message, string reply)
    {
        var now = DateTimeOffset.UtcNow;
        profile.LastInteractionAt = now;

        var personalizedReply = string.IsNullOrWhiteSpace(profile.PreferredDisplayName)
            ? reply
            : $"{profile.PreferredDisplayName}，\n{reply}";

        var guideMode = GetCommandGuideMode(profile, message);
        if (guideMode == CommandGuideMode.None)
        {
            return personalizedReply;
        }

        profile.LastCommandGuideAt = now;
        if (guideMode == CommandGuideMode.Full)
        {
            return string.Join('\n', new[]
            {
                personalizedReply,
                "",
                BuildCommandGuideBlockClean()
            });
        }

        return string.Join('\n', new[]
        {
            personalizedReply,
            "",
            "若要查看前綴規則與個人設定指令，請輸入 ?help。"
        });
    }

    private string BuildHelpReplyClean(HighLevelTaskDraft? draft)
    {
        if (draft?.RequiresProjectName == true && string.IsNullOrWhiteSpace(draft.ProjectName))
        {
            return string.Join('\n', new[]
            {
                "目前這個 production draft 正在等待專案名稱。",
                "請用 # 開頭提供專案名稱，例如 #MySite。",
                "",
                BuildCommandGuideBlockClean()
            });
        }

        return BuildCommandGuideBlockClean();
    }

    private static string BuildCommandGuideBlockClean()
    {
        return string.Join('\n', new[]
        {
            "使用規則：",
            "- 一般對話：直接輸入",
            "- 查詢：?內容",
            "- 顯式搜尋：?search 關鍵字",
            "- 任務/指令：/內容",
            "- 專案名稱：#名稱",
            "- 個人設定：/name <稱呼>、/id <英數字ID>",
            "- 查看個人設定：?profile",
            "- 查看說明：?help",
            "- 確認 / 取消：確認、confirm、取消、cancel"
        });
    }

    private string PrepareReply(HighLevelUserProfile profile, string message, string reply)
    {
        var now = DateTimeOffset.UtcNow;
        profile.LastInteractionAt = now;
        var personalizedReply = string.IsNullOrWhiteSpace(profile.PreferredDisplayName)
            ? reply
            : $"{profile.PreferredDisplayName}，\n{reply}";

        var guideMode = GetCommandGuideMode(profile, message);
        if (guideMode == CommandGuideMode.None)
        {
            return personalizedReply;
        }

        profile.LastCommandGuideAt = now;
        if (guideMode == CommandGuideMode.Full)
        {
            return string.Join('\n', new[]
            {
                personalizedReply,
                "",
                BuildCommandGuideBlock()
            });
        }

        return string.Join('\n', new[]
        {
            personalizedReply,
            "",
            "若要查看前綴規則，請輸入 ?help。"
        });
    }

    private CommandGuideMode GetCommandGuideMode(HighLevelUserProfile profile, string message)
    {
        if (profile.LastCommandGuideAt == null)
        {
            return CommandGuideMode.Full;
        }

        if (LooksLikePotentialCommandWithoutPrefix(message) && IsCommandGuideExpired(profile.LastCommandGuideAt))
        {
            return CommandGuideMode.Short;
        }

        return CommandGuideMode.None;
    }

    private bool LooksLikePotentialCommandWithoutPrefix(string message)
    {
        var trimmed = message.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        if (_commandParser.Parse(trimmed).Kind != HighLevelInputKind.Conversation)
        {
            return false;
        }

        var normalized = Normalize(trimmed);
        return ContainsAny(normalized, _options.ProductionKeywords) ||
               ContainsAny(normalized, _options.QueryKeywords);
    }

    private bool IsCommandGuideExpired(DateTimeOffset? lastCommandGuideAt)
    {
        if (lastCommandGuideAt == null)
        {
            return true;
        }

        return lastCommandGuideAt.Value.AddMinutes(Math.Max(1, _options.CommandGuideReminderMinutes)) <= DateTimeOffset.UtcNow;
    }

    private string BuildHelpReply(HighLevelTaskDraft? draft)
    {
        if (draft?.RequiresProjectName == true && string.IsNullOrWhiteSpace(draft.ProjectName))
        {
            return string.Join('\n', new[]
            {
                "你目前正在提供專案名稱。",
                "請用 # 開頭回覆專案名稱，例如 #MySite。",
                "",
                BuildCommandGuideBlock()
            });
        }

        return BuildCommandGuideBlock();
    }

    private static string BuildCommandGuideBlock()
    {
        return string.Join('\n', new[]
        {
            "前綴規則：",
            "- 一般對話：直接輸入",
            "- 查詢：?內容",
            "- 受控搜尋：?search 關鍵字",
            "- 任務/指令：/內容",
            "- 專案名稱：#名稱",
            "- 使用說明：?help",
            "- 確認 / 取消：確認 或 取消"
        });
    }

    private static void IncrementDecisionCount(HighLevelUserProfile profile, HighLevelRouteMode mode)
    {
        var key = mode.ToString().ToLowerInvariant();
        profile.DecisionCounts.TryGetValue(key, out var current);
        profile.DecisionCounts[key] = current + 1;
    }

    private bool ShouldSuggestControlledSearch(HighLevelRouteDecision decision, HighLevelParsedInput parsed)
    {
        if (!string.IsNullOrWhiteSpace(parsed.QueryCommand))
            return false;

        var text = parsed.Kind == HighLevelInputKind.Query && !string.IsNullOrWhiteSpace(parsed.Body)
            ? parsed.Body
            : parsed.Raw;
        var normalized = Normalize(text);

        if (decision.Mode == HighLevelRouteMode.Query)
            return LooksLikeLookupNeed(normalized);

        if (decision.Mode == HighLevelRouteMode.Conversation)
            return LooksLikeLookupNeed(normalized);

        return false;
    }

    private static bool LooksLikeLookupNeed(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var keywords = new[]
        {
            "附近", "鄰近", "相鄰", "接壤", "行政區", "行政區劃",
            "官網", "網址", "哪裡", "哪個", "哪些", "多少",
            "最新", "價格", "規格", "班次", "時刻", "時刻表", "最早", "最晚",
            "火車", "台鐵", "高鐵", "公車", "客運", "航班", "機票",
            "schedule", "official", "nearby",
            "district", "borough", "county", "province", "state"
        };

        return keywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private string BuildControlledSearchSuggestion(HighLevelParsedInput parsed)
    {
        var target = parsed.Kind == HighLevelInputKind.Query && !string.IsNullOrWhiteSpace(parsed.Body)
            ? parsed.Body
            : parsed.Raw;
        var normalized = Normalize(target);

        if (ContainsAny(normalized, new[] { "高鐵", "hsr", "thsr" }))
            return $"這題較適合做受控查詢。可直接輸入 ?hsr {target}";

        if (ContainsAny(normalized, new[] { "火車", "台鐵", "rail", "train" }))
            return $"這題較適合做受控查詢。可直接輸入 ?rail {target}";

        if (ContainsAny(normalized, new[] { "公車", "客運", "bus" }))
            return $"這題較適合做受控查詢。可直接輸入 ?bus {target}";

        if (ContainsAny(normalized, new[] { "航班", "機票", "flight", "flights" }))
            return $"這題較適合做受控查詢。可直接輸入 ?flight {target}";

        return $"這題較適合做受控搜尋。可直接輸入 ?search {target}";
    }

    private bool IsTransportLookupSuggestion(HighLevelParsedInput parsed)
    {
        var target = parsed.Kind == HighLevelInputKind.Query && !string.IsNullOrWhiteSpace(parsed.Body)
            ? parsed.Body
            : parsed.Raw;
        var normalized = Normalize(target);

        return ContainsAny(normalized, new[] { "高鐵", "hsr", "thsr", "火車", "台鐵", "rail", "train", "公車", "客運", "bus", "航班", "機票", "flight", "flights" });
    }

    private HighLevelProcessResult FinalizeResult(
        string channel,
        string userId,
        HighLevelInputEnvelope envelope,
        HighLevelTrustedParseResult trustedParse,
        HighLevelWorkflowDecision workflow,
        HighLevelProcessResult result)
    {
        var parsed = trustedParse.Parsed;
        try
        {
            _interactionRecorder.Record(new HighLevelInteractionRecord
            {
                Channel = channel,
                UserId = userId,
                RawInput = parsed.Raw,
                RawReply = result.Reply,
                ParsedKind = parsed.Kind.ToString(),
                ParsedPrefix = parsed.Prefix,
                ParsedBody = parsed.Body,
                InputSource = envelope.Source.ToString(),
                InputTaint = envelope.Taint.ToString(),
                AppliedTransforms = envelope.Transforms.Select(t => t.ToString()).ToArray(),
                CommandExtractionAllowed = trustedParse.Trust.Allowed,
                CommandTrustReason = trustedParse.Trust.Reason,
                WorkflowState = workflow.State.ToString(),
                WorkflowAction = workflow.Action.ToString(),
                WorkflowReason = workflow.Reason,
                RouteMode = result.Mode.ToString(),
                DecisionReason = result.DecisionReason,
                Error = result.Error,
                DraftId = result.Draft?.DraftId,
                TaskId = result.CreatedTask?.TaskId,
                PlanId = result.CreatedPlan?.PlanId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append high-level interaction log for {Channel}:{UserId}", channel, userId);
        }

        try
        {
            _interpretationStore.Record(BuildInterpretationRecord(channel, userId, parsed, trustedParse, workflow, result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append high-level interpretation record for {Channel}:{UserId}", channel, userId);
        }

        try
        {
            var latestMemory = _memoryStore.ReadLatest(channel, userId);
            var currentProfile = LoadUserProfile(channel, userId);
            var projectedMemory = BuildMemoryState(channel, userId, currentProfile, parsed, workflow, result, latestMemory);
            _memoryStore.Write(projectedMemory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to project high-level memory state for {Channel}:{UserId}", channel, userId);
        }

        return result;
    }

    private HighLevelMemoryState BuildMemoryState(
        string channel,
        string userId,
        HighLevelUserProfile? profile,
        HighLevelParsedInput parsed,
        HighLevelWorkflowDecision workflow,
        HighLevelProcessResult result,
        HighLevelMemoryState? previous)
    {
        var draft = result.Draft;
        var currentGoal = previous?.CurrentGoal;
        if (!string.IsNullOrWhiteSpace(draft?.OriginalMessage))
        {
            currentGoal = ToMemoryGoal(draft.OriginalMessage);
        }
        else if (result.Mode == HighLevelRouteMode.Query && !string.IsNullOrWhiteSpace(parsed.Body))
        {
            currentGoal = string.Equals(parsed.QueryCommand, "search", StringComparison.OrdinalIgnoreCase) &&
                          !string.IsNullOrWhiteSpace(parsed.QueryArgument)
                ? parsed.QueryArgument
                : parsed.Body;
        }
        else if (result.Mode == HighLevelRouteMode.Conversation && !string.IsNullOrWhiteSpace(parsed.Raw))
        {
            currentGoal ??= parsed.Raw.Trim();
        }

        var currentGoalCommitLevel = previous?.CurrentGoalCommitLevel ?? HighLevelMemoryCommitLevel.Candidate.ToString();
        var currentGoalSource = previous?.CurrentGoalSource ?? HighLevelMemorySource.System.ToString();
        var currentGoalCommitReason = previous?.CurrentGoalCommitReason ?? "carry-forward";
        if (!string.IsNullOrWhiteSpace(draft?.OriginalMessage))
        {
            currentGoalCommitLevel = result.CreatedTask != null
                ? HighLevelMemoryCommitLevel.Confirmed.ToString()
                : HighLevelMemoryCommitLevel.Candidate.ToString();
            currentGoalSource = HighLevelMemorySource.User.ToString();
            currentGoalCommitReason = result.CreatedTask != null
                ? "draft confirmed into executable task"
                : "candidate extracted from explicit user command";
        }
        else if (result.Mode == HighLevelRouteMode.Query && !string.IsNullOrWhiteSpace(parsed.Body))
        {
            currentGoalCommitLevel = HighLevelMemoryCommitLevel.Candidate.ToString();
            currentGoalSource = HighLevelMemorySource.User.ToString();
            currentGoalCommitReason = string.Equals(parsed.QueryCommand, "search", StringComparison.OrdinalIgnoreCase)
                ? "candidate extracted from explicit search query"
                : "candidate extracted from query";
        }

        var projectName = draft?.ProjectName ?? previous?.ProjectName;
        var projectNameCommitLevel = previous?.ProjectNameCommitLevel ?? string.Empty;
        var projectNameSource = previous?.ProjectNameSource ?? string.Empty;
        var projectNameCommitReason = previous?.ProjectNameCommitReason ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(draft?.ProjectName))
        {
            projectNameCommitLevel = HighLevelMemoryCommitLevel.Confirmed.ToString();
            projectNameSource = HighLevelMemorySource.ConfirmedUser.ToString();
            projectNameCommitReason = "explicit #project_name command accepted by workflow";
        }

        return new HighLevelMemoryState
        {
            Channel = channel,
            UserId = userId,
            PreferredDisplayName = profile?.PreferredDisplayName,
            PreferredUserCode = profile?.PreferredUserCode,
            CurrentGoal = currentGoal,
            CurrentGoalCommitLevel = currentGoalCommitLevel,
            CurrentGoalSource = currentGoalSource,
            CurrentGoalCommitReason = currentGoalCommitReason,
            LastRouteMode = result.Mode.ToString(),
            WorkflowState = workflow.State.ToString(),
            WorkflowAction = workflow.Action.ToString(),
            PendingDraftId = draft?.DraftId,
            PendingProjectName = draft?.RequiresProjectName == true && string.IsNullOrWhiteSpace(draft.ProjectName),
            ProjectName = projectName,
            ProjectNameCommitLevel = projectNameCommitLevel,
            ProjectNameSource = projectNameSource,
            ProjectNameCommitReason = projectNameCommitReason,
            LastTaskType = result.CreatedTask?.TaskType ?? draft?.TaskType ?? previous?.LastTaskType,
            LastTaskId = result.CreatedTask?.TaskId ?? previous?.LastTaskId,
            LastPlanId = result.CreatedPlan?.PlanId ?? previous?.LastPlanId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private string ToMemoryGoal(string rawInput)
    {
        var parsed = _inputTrustPolicy.Apply(BuildLineEnvelope(rawInput), _commandParser.Parse(rawInput)).Parsed;
        if (!string.IsNullOrWhiteSpace(parsed.Body))
        {
            return parsed.Body;
        }

        return rawInput.Trim();
    }

    private static HighLevelInputEnvelope BuildLineEnvelope(string rawText)
        => new()
        {
            RawText = rawText,
            Source = HighLevelInputSource.UserMessage,
            Taint = HighLevelInputTaint.UserText
        };

    private HighLevelInterpretationRecord BuildInterpretationRecord(
        string channel,
        string userId,
        HighLevelParsedInput parsed,
        HighLevelTrustedParseResult trustedParse,
        HighLevelWorkflowDecision workflow,
        HighLevelProcessResult result)
    {
        return new HighLevelInterpretationRecord
        {
            Channel = channel,
            UserId = userId,
            InteractionType = result.Mode.ToString(),
            ParsedKind = parsed.Kind.ToString(),
            WorkflowState = workflow.State.ToString(),
            WorkflowAction = workflow.Action.ToString(),
            CommandExtractionAllowed = trustedParse.Trust.Allowed,
            TrustReason = trustedParse.Trust.Reason,
            CandidateGoal = string.Equals(parsed.QueryCommand, "search", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(parsed.QueryArgument)
                ? parsed.QueryArgument
                : string.IsNullOrWhiteSpace(parsed.Body) ? null : parsed.Body,
            TaskType = result.CreatedTask?.TaskType ?? result.Draft?.TaskType,
            ProjectName = result.Draft?.ProjectName,
            DraftId = result.Draft?.DraftId,
            DecisionReason = result.DecisionReason
        };
    }
}

public static class HighLevelCoordinatorDefaults
{
    public static string DefaultAccessRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Bricks4Agent",
            "managed-workspaces");
}

public class HighLevelCoordinatorOptions
{
    public int DraftTtlMinutes { get; set; } = 30;
    public int MaxDraftSummaryLength { get; set; } = 160;
    public int CommandGuideReminderMinutes { get; set; } = 60;
    public string AccessRoot { get; set; } = HighLevelCoordinatorDefaults.DefaultAccessRoot;
    public string AnonymousRegistrationPolicy { get; set; } = HighLevelAnonymousRegistrationPolicy.AllowAll;
    public string[] QueryPrefixes { get; set; } = new[] { "?", "\uFF1F" };
    public string[] ProductionPrefixes { get; set; } = new[] { "/", "\uFF0F" };
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

    public string[] SystemScaffoldKeywords { get; set; } = new[]
    {
        "\u7cfb\u7d71\u96db\u5f62",
        "\u5b8c\u6574\u7cfb\u7d71",
        "\u5b8c\u6574\u7db2\u7ad9",
        "system scaffold",
        "full system",
        "project skeleton",
        "scaffold"
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
    public List<string>? FollowUpMessages { get; set; }
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
    public string? PreferredDisplayName { get; set; }
    public string? PreferredUserCode { get; set; }
    public HighLevelUserPermissions Permissions { get; set; } = HighLevelUserPermissions.CreateDefault();
    public string RegistrationStatus { get; set; } = HighLevelRegistrationStatus.Approved;
    public DateTimeOffset? RegistrationRequestedAt { get; set; }
    public DateTimeOffset? RegistrationReviewedAt { get; set; }
    public string? RegistrationReviewNote { get; set; }
    public string? LastDecision { get; set; }
    public DateTimeOffset? LastInteractionAt { get; set; }
    public DateTimeOffset? LastCommandGuideAt { get; set; }
    public string? LastTaskId { get; set; }
    public string? LastPlanId { get; set; }
    public string? PendingDraftId { get; set; }
    public string? PendingDraftOriginalMessage { get; set; }
    public string? PendingDraftSummary { get; set; }
    public string? PendingDraftTaskType { get; set; }
    public string? PendingDraftTitle { get; set; }
    public string? PendingDraftDescription { get; set; }
    public bool PendingDraftRequiresProjectName { get; set; }
    public string? PendingDraftProjectName { get; set; }
    public string? PendingDraftProjectFolderName { get; set; }
    public DateTime? PendingDraftCreatedAt { get; set; }
    public DateTime? PendingDraftExpiresAt { get; set; }
    public Dictionary<string, int> DecisionCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class HighLevelLineUserSummary
{
    public string UserId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? UserCode { get; set; }
    public bool IsTestAccount { get; set; }
    public string AccountType { get; set; } = "line_user";
    public HighLevelUserPermissions Permissions { get; set; } = HighLevelUserPermissions.CreateDefault();
    public string RegistrationStatus { get; set; } = HighLevelRegistrationStatus.Approved;
    public DateTimeOffset? RegistrationRequestedAt { get; set; }
    public DateTimeOffset? RegistrationReviewedAt { get; set; }
    public string? RegistrationReviewNote { get; set; }
    public DateTimeOffset? LastInteractionAt { get; set; }
    public string? LastDecision { get; set; }
    public string? PendingDraftId { get; set; }
    public string? LastTaskId { get; set; }
    public string? LastPlanId { get; set; }
    public string UserRoot { get; set; } = string.Empty;
    public string ProjectsRoot { get; set; } = string.Empty;
    public int ActiveUserGrantCount { get; set; }
    public int ActiveUserSiteBindingCount { get; set; }
}

public sealed class HighLevelUserPermissions
{
    public bool AllowQuery { get; set; } = true;
    public bool AllowTransport { get; set; } = true;
    public bool AllowProduction { get; set; } = true;
    public bool AllowBrowserDelegated { get; set; }
    public bool AllowDeployment { get; set; }

    public static HighLevelUserPermissions CreateDefault()
        => new();
}

public sealed class HighLevelUserPermissionsPatch
{
    public bool? AllowQuery { get; set; }
    public bool? AllowTransport { get; set; }
    public bool? AllowProduction { get; set; }
    public bool? AllowBrowserDelegated { get; set; }
    public bool? AllowDeployment { get; set; }
}

internal enum CommandGuideMode
{
    None = 0,
    Short = 1,
    Full = 2
}

public static class HighLevelAnonymousRegistrationPolicy
{
    public const string DenyAll = "deny_all";
    public const string ManualReview = "manual_review";
    public const string AllowAll = "allow_all";
}

public static class HighLevelRegistrationStatus
{
    public const string Approved = "approved";
    public const string PendingReview = "pending_review";
    public const string Rejected = "rejected";
    public const string DeniedByPolicy = "denied_by_policy";
}

public sealed class HighLevelRegistrationPolicyState
{
    public string Channel { get; set; } = "line";
    public string Policy { get; set; } = HighLevelAnonymousRegistrationPolicy.AllowAll;
    public string UpdatedBy { get; set; } = "system:high-level-coordinator";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HighLevelLineNotification
{
    public string NotificationId { get; set; } = string.Empty;
    public string Channel { get; set; } = "line";
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = "pending";
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class HighLevelRegistrationReviewResult
{
    public string UserId { get; set; } = string.Empty;
    public string RegistrationStatus { get; set; } = string.Empty;
    public string? ReviewNote { get; set; }
    public HighLevelLineNotification? Notification { get; set; }
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
    public HighLevelSystemScaffoldSpec? ScaffoldSpec { get; set; }
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
    public string ExecutionIntentId { get; set; } = string.Empty;
    public string ExecutionIntentDocument { get; set; } = string.Empty;
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
    public string UserFolderName { get; set; } = string.Empty;
    public string UserRoot { get; set; } = string.Empty;
    public string ConversationsRoot { get; set; } = string.Empty;
    public string DocumentsRoot { get; set; } = string.Empty;
    public string ProjectsRoot { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
}

public class HighLevelUserCodeReservation
{
    public string Channel { get; set; } = string.Empty;
    public string UserCode { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
