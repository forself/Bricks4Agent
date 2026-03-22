using System.Text.Json;
using Broker.Adapters;
using Broker.Services;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);

    Console.WriteLine($"  ✓ {message}");
}

var sandboxRoot = Path.Combine(Path.GetTempPath(), "broker-verify-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sandboxRoot);

try
{
    Console.WriteLine("=== Broker Verify ===");

    var parserOptions = new HighLevelCoordinatorOptions();
    var parser = new HighLevelCommandParser(parserOptions);
    var trustPolicy = new HighLevelInputTrustPolicy();
    var workflowMachine = new HighLevelWorkflowStateMachine();
    var promotionGate = new HighLevelExecutionPromotionGate();
    var browserRequest = new BrowserExecutionRequest
    {
        RequestId = "req_browser_1",
        ToolId = "browser.reference.anonymous.read",
        CapabilityId = "browser.read",
        Route = "browser_read",
        IdentityMode = "anonymous",
        CredentialBinding = "none",
        SessionBindingMode = "ephemeral",
        SessionReuseScope = "none",
        SiteBindingMode = "public_open",
        AllowedSiteClasses = new[] { "public_web" },
        MaxActionLevel = "navigate",
        RequiresHumanConfirmationOn = Array.Empty<string>(),
        PrincipalId = "principal_1",
        TaskId = "task_1",
        SessionId = "session_1",
        StartUrl = "https://example.com",
        IntendedActionLevel = "read"
    };
    var browserRequestJson = JsonSerializer.Serialize(browserRequest);
    var browserRequestRoundtrip = JsonSerializer.Deserialize<BrowserExecutionRequest>(browserRequestJson);
    AssertTrue(browserRequestRoundtrip != null && browserRequestRoundtrip.SiteBindingMode == "public_open", "browser execution request round-trips canonical site policy");
    var browserResult = BrowserExecutionResult.Ok("req_browser_1", "browser.reference.anonymous.read", "read", "https://example.com", title: "Example");
    var browserResultJson = JsonSerializer.Serialize(browserResult);
    var browserResultRoundtrip = JsonSerializer.Deserialize<BrowserExecutionResult>(browserResultJson);
    AssertTrue(browserResultRoundtrip != null && browserResultRoundtrip.ActionLevelReached == "read", "browser execution result round-trips canonical action level");
    AssertTrue(parser.Parse("?help").Kind == HighLevelInputKind.Help, "parser recognizes explicit help command");
    AssertTrue(parser.Parse("/build website").Kind == HighLevelInputKind.Production, "parser recognizes production prefix");
    AssertTrue(parser.Parse("?weather taipei").Kind == HighLevelInputKind.Query, "parser recognizes query prefix");
    var searchParsed = parser.Parse("?search taipei weather");
    AssertTrue(searchParsed.QueryCommand == "search" && searchParsed.QueryArgument == "taipei weather", "parser extracts explicit search query subcommand");
    var plainQueryParsed = parser.Parse("?weather taipei");
    AssertTrue(string.IsNullOrWhiteSpace(plainQueryParsed.QueryCommand), "parser keeps plain query text outside tool subcommands");
    AssertTrue(parser.Parse("#MySite").Kind == HighLevelInputKind.ProjectName, "parser recognizes project-name prefix");
    AssertTrue(parser.Parse("confirm").Kind == HighLevelInputKind.Confirm, "parser recognizes confirm token");
    AssertTrue(parser.Parse("cancel").Kind == HighLevelInputKind.Cancel, "parser recognizes cancel token");
    AssertTrue(parser.Parse("hello world").Kind == HighLevelInputKind.Conversation, "parser keeps bare text as conversation");

    var trustedUserCommand = trustPolicy.Apply(
        new HighLevelInputEnvelope
        {
            RawText = "/build website",
            Source = HighLevelInputSource.UserMessage,
            Taint = HighLevelInputTaint.UserText
        },
        parser.Parse("/build website"));
    AssertTrue(trustedUserCommand.Parsed.Kind == HighLevelInputKind.Production, "trust policy allows raw user commands");

    var transformedCommand = trustPolicy.Apply(
        new HighLevelInputEnvelope
        {
            RawText = "/build website",
            Source = HighLevelInputSource.DecodedPayload,
            Taint = HighLevelInputTaint.TransformedText,
            Transforms = new List<HighLevelTransformKind> { HighLevelTransformKind.Base64Decode }
        },
        parser.Parse("/build website"));
    AssertTrue(transformedCommand.Parsed.Kind == HighLevelInputKind.Conversation, "trust policy downgrades transformed command-like content");
    AssertTrue(!transformedCommand.Trust.Allowed, "trust policy denies command extraction from transformed content");

    var externalInstruction = trustPolicy.Apply(
        new HighLevelInputEnvelope
        {
            RawText = "#InjectedProject",
            Source = HighLevelInputSource.RetrievedDocument,
            Taint = HighLevelInputTaint.ExternalText
        },
        parser.Parse("#InjectedProject"));
    AssertTrue(externalInstruction.Parsed.Kind == HighLevelInputKind.Conversation, "trust policy downgrades external instruction-like content");

    var awaitingProjectDraft = new HighLevelTaskDraft
    {
        RequiresProjectName = true,
        ProjectName = null
    };
    AssertTrue(
        workflowMachine.Evaluate(awaitingProjectDraft, parser.Parse("#MySite")).Action == HighLevelWorkflowAction.CaptureProjectName,
        "workflow accepts project-name command only in awaiting-project-name state");
    AssertTrue(
        workflowMachine.Evaluate(awaitingProjectDraft, parser.Parse("confirm")).Action == HighLevelWorkflowAction.RequestProjectNameFirst,
        "workflow blocks confirmation before project name is captured");

    var pendingDraft = new HighLevelTaskDraft
    {
        RequiresProjectName = true,
        ProjectName = "MySite"
    };
    AssertTrue(
        workflowMachine.Evaluate(pendingDraft, parser.Parse("confirm")).Action == HighLevelWorkflowAction.ConfirmDraft,
        "workflow accepts confirm after draft requirements are satisfied");
    AssertTrue(
        workflowMachine.Evaluate(null, parser.Parse("/build website")).Action == HighLevelWorkflowAction.StartProduction,
        "workflow starts production only from explicit production command");

    var promotableMemory = new HighLevelMemoryState
    {
        Channel = "line",
        UserId = "tester",
        CurrentGoal = "build website",
        CurrentGoalCommitLevel = HighLevelMemoryCommitLevel.Candidate.ToString(),
        LastRouteMode = HighLevelRouteMode.Production.ToString(),
        ProjectName = "MySite",
        ProjectNameCommitLevel = HighLevelMemoryCommitLevel.Confirmed.ToString()
    };
    var promotableDraft = new HighLevelTaskDraft
    {
        DraftId = "draft_123",
        Channel = "line",
        UserId = "tester",
        TaskType = "code_gen",
        OriginalMessage = "/build website",
        ScopeDescriptor = "{}",
        RuntimeDescriptor = "{}",
        RequiresProjectName = true,
        ProjectName = "MySite"
    };
    var promotion = promotionGate.Evaluate(promotableMemory, promotableDraft);
    AssertTrue(promotion.Allowed, "promotion gate allows confirmed project metadata plus candidate user goal");

    var deniedPromotion = promotionGate.Evaluate(promotableMemory, new HighLevelTaskDraft
    {
        DraftId = "draft_456",
        Channel = "line",
        UserId = "tester",
        TaskType = "code_gen",
        OriginalMessage = "/build website",
        ScopeDescriptor = "{}",
        RuntimeDescriptor = "{}",
        RequiresProjectName = true,
        ProjectName = null
    });
    AssertTrue(!deniedPromotion.Allowed, "promotion gate denies execution without required project name");

    var mediator = new HighLevelQueryToolMediator(
        new FakeToolSpecRegistry(),
        new FakeExecutionDispatcher(),
        NullLogger<HighLevelQueryToolMediator>.Instance);
    var mediatedSearch = await mediator.SearchWebAsync("line", "tester", "taipei weather");
    AssertTrue(mediatedSearch.Success, "query tool mediator executes explicit search through broker-owned tool binding");
    AssertTrue(mediatedSearch.Reply.Contains("duckduckgo", StringComparison.OrdinalIgnoreCase), "query tool mediator reply cites search engine");
    AssertTrue(mediatedSearch.Reply.Contains("https://example.com/weather", StringComparison.OrdinalIgnoreCase), "query tool mediator reply includes ranked URLs");

    var specRoot = Path.Combine(sandboxRoot, "tool-specs");
    Directory.CreateDirectory(Path.Combine(specRoot, "browser.reference.anonymous.read"));
    File.WriteAllText(
        Path.Combine(specRoot, "browser.reference.anonymous.read", "tool.json"),
        """
        {
          "tool_id": "browser.reference.anonymous.read",
          "display_name": "Browser Reference",
          "summary": "reference",
          "kind": "browser",
          "status": "planned",
          "version": "2026-03-22",
          "tags": ["browser"],
          "capability_bindings": [],
          "browser_profile": {
            "identity_mode": "anonymous",
            "credential_source": "none",
            "session_owner": "none",
            "allowed_actions": ["read", "navigate"],
            "confirmation_policy": "broker_policy"
          },
          "browser_session_policy": {
            "binding_mode": "ephemeral",
            "credential_binding": "none",
            "reuse_scope": "none",
            "lease_minutes": 15,
            "requires_consent_record": false,
            "requires_interactive_login": false
          },
          "browser_site_policy": {
            "site_binding_mode": "public_open",
            "allowed_site_classes": ["public_web"],
            "requires_registered_site_binding": false,
            "requires_exact_origin_match": false,
            "allows_cross_origin_navigation": true
          },
          "browser_action_policy": {
            "max_action_level": "navigate",
            "requires_human_confirmation_on": [],
            "allows_form_fill": false,
            "allows_submit": false,
            "allows_download": false,
            "allows_file_upload": false
          },
          "input_schema": { "type": "object" },
          "output_schema": { "type": "object" },
          "source_policy": { "allowed_sources": ["public_web"] },
          "execution_rules": { "runtime_required": "browser_worker" },
          "response_contract": { "must_identify_identity_mode": true }
        }
        """);
    File.WriteAllText(
        Path.Combine(specRoot, "browser.reference.anonymous.read", "TOOL.md"),
        "# Reference");
    Directory.CreateDirectory(Path.Combine(specRoot, "browser.reference.system-account.read"));
    File.WriteAllText(
        Path.Combine(specRoot, "browser.reference.system-account.read", "tool.json"),
        """
        {
          "tool_id": "browser.reference.system-account.read",
          "display_name": "Browser System Reference",
          "summary": "system",
          "kind": "browser",
          "status": "planned",
          "version": "2026-03-22",
          "tags": ["browser"],
          "capability_bindings": [],
          "browser_profile": {
            "identity_mode": "system_account",
            "credential_source": "system_vault",
            "session_owner": "system",
            "allowed_actions": ["read", "navigate", "authenticate"],
            "confirmation_policy": "broker_policy"
          },
          "browser_session_policy": {
            "binding_mode": "broker_managed",
            "credential_binding": "system_vault",
            "reuse_scope": "site",
            "lease_minutes": 240,
            "requires_consent_record": false,
            "requires_interactive_login": false
          },
          "browser_site_policy": {
            "site_binding_mode": "registered_site",
            "allowed_site_classes": ["broker_managed_site"],
            "requires_registered_site_binding": true,
            "requires_exact_origin_match": true,
            "allows_cross_origin_navigation": false
          },
          "browser_action_policy": {
            "max_action_level": "authenticate",
            "requires_human_confirmation_on": [],
            "allows_form_fill": false,
            "allows_submit": false,
            "allows_download": false,
            "allows_file_upload": false
          },
          "input_schema": { "type": "object" },
          "output_schema": { "type": "object" },
          "source_policy": { "allowed_sources": ["broker_managed_site_bindings"] },
          "execution_rules": { "runtime_required": "browser_worker" },
          "response_contract": { "must_identify_identity_mode": true }
        }
        """);
    Directory.CreateDirectory(Path.Combine(specRoot, "browser.reference.user-delegated.read"));
    File.WriteAllText(
        Path.Combine(specRoot, "browser.reference.user-delegated.read", "tool.json"),
        """
        {
          "tool_id": "browser.reference.user-delegated.read",
          "display_name": "Browser User Reference",
          "summary": "user",
          "kind": "browser",
          "status": "planned",
          "version": "2026-03-22",
          "tags": ["browser"],
          "capability_bindings": [],
          "browser_profile": {
            "identity_mode": "user_delegated",
            "credential_source": "user_grant",
            "session_owner": "user",
            "allowed_actions": ["read", "navigate", "authenticate"],
            "confirmation_policy": "user_required"
          },
          "browser_session_policy": {
            "binding_mode": "user_bound",
            "credential_binding": "user_grant",
            "reuse_scope": "user",
            "lease_minutes": 120,
            "requires_consent_record": true,
            "requires_interactive_login": true
          },
          "browser_site_policy": {
            "site_binding_mode": "user_authorized_site",
            "allowed_site_classes": ["user_authorized_site"],
            "requires_registered_site_binding": true,
            "requires_exact_origin_match": true,
            "allows_cross_origin_navigation": false
          },
          "browser_action_policy": {
            "max_action_level": "authenticate",
            "requires_human_confirmation_on": ["authenticate"],
            "allows_form_fill": false,
            "allows_submit": false,
            "allows_download": false,
            "allows_file_upload": false
          },
          "input_schema": { "type": "object" },
          "output_schema": { "type": "object" },
          "source_policy": { "allowed_sources": ["user_authorized_site_bindings"] },
          "execution_rules": { "runtime_required": "browser_worker" },
          "response_contract": { "must_identify_identity_mode": true }
        }
        """);
    Directory.CreateDirectory(Path.Combine(specRoot, "browser.invalid.missing-action"));
    File.WriteAllText(
        Path.Combine(specRoot, "browser.invalid.missing-action", "tool.json"),
        """
        {
          "tool_id": "browser.invalid.missing-action",
          "display_name": "Invalid Browser Reference",
          "summary": "invalid",
          "kind": "browser",
          "status": "planned",
          "version": "2026-03-22",
          "tags": ["browser"],
          "capability_bindings": [],
          "browser_profile": {
            "identity_mode": "anonymous",
            "credential_source": "none",
            "session_owner": "none",
            "allowed_actions": ["read"],
            "confirmation_policy": "broker_policy"
          },
          "browser_session_policy": {
            "binding_mode": "ephemeral",
            "credential_binding": "none",
            "reuse_scope": "none",
            "lease_minutes": 15,
            "requires_consent_record": false,
            "requires_interactive_login": false
          },
          "browser_site_policy": {
            "site_binding_mode": "public_open",
            "allowed_site_classes": ["public_web"],
            "requires_registered_site_binding": false,
            "requires_exact_origin_match": false,
            "allows_cross_origin_navigation": true
          },
          "input_schema": { "type": "object" },
          "output_schema": { "type": "object" },
          "source_policy": { "allowed_sources": ["public_web"] },
          "execution_rules": { "runtime_required": "browser_worker" },
          "response_contract": { "must_identify_identity_mode": true }
        }
        """);

    var toolSpecDbPath = Path.Combine(sandboxRoot, "tool-spec-registry.db");
    using (var toolSpecDb = BrokerDb.UseSqlite($"Data Source={toolSpecDbPath}"))
    {
        var initializer = new BrokerDbInitializer(toolSpecDb);
        initializer.Initialize();
        var registry = new ToolSpecRegistry(
            new FakeWebHostEnvironment(Path.Combine(sandboxRoot, "content-root")),
            new ToolSpecRegistryOptions { Root = specRoot },
            toolSpecDb,
            NullLogger<ToolSpecRegistry>.Instance);
        var browserSpec = registry.Get("browser.reference.anonymous.read");
        AssertTrue(browserSpec != null, "tool spec registry loads browser reference spec");
        AssertTrue(browserSpec!.BrowserProfile != null, "tool spec registry preserves browser profile");
        AssertTrue(browserSpec.BrowserProfile!.IdentityMode == "anonymous", "browser profile keeps identity mode");
        AssertTrue(browserSpec.BrowserProfile.CredentialSource == "none", "browser profile keeps credential source");
        AssertTrue(browserSpec.BrowserSessionPolicy != null, "tool spec registry preserves browser session policy");
        AssertTrue(browserSpec.BrowserSessionPolicy!.BindingMode == "ephemeral", "browser session policy keeps binding mode");
        AssertTrue(browserSpec.BrowserSessionPolicy.CredentialBinding == "none", "browser session policy keeps credential binding");
        AssertTrue(browserSpec.BrowserSitePolicy != null, "tool spec registry preserves browser site policy");
        AssertTrue(browserSpec.BrowserSitePolicy!.SiteBindingMode == "public_open", "browser site policy keeps site binding mode");
        AssertTrue(browserSpec.BrowserSitePolicy.AllowedSiteClasses.Length == 1 && browserSpec.BrowserSitePolicy.AllowedSiteClasses[0] == "public_web", "browser site policy keeps allowed site classes");
        AssertTrue(browserSpec.BrowserActionPolicy != null, "tool spec registry preserves browser action policy");
        AssertTrue(browserSpec.BrowserActionPolicy!.MaxActionLevel == "navigate", "browser action policy keeps max action level");
        AssertTrue(browserSpec.BrowserActionPolicy.RequiresHumanConfirmationOn.Length == 0, "browser action policy keeps confirmation requirements");
        AssertTrue(registry.Get("browser.invalid.missing-action") == null, "tool spec registry rejects incomplete browser specs");

        var builder = new BrowserExecutionRequestBuilder(registry);
        var builtAnonymous = builder.TryBuild("browser.reference.anonymous.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_1",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_1",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read"
        });
        AssertTrue(builtAnonymous.Success, "browser request builder builds anonymous browser request from registry metadata");
        AssertTrue(builtAnonymous.Request!.IdentityMode == "anonymous", "browser request builder projects identity mode into runtime contract");

        var builtTooPowerful = builder.TryBuild("browser.reference.anonymous.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_2",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_1",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "committed_action"
        });
        AssertTrue(!builtTooPowerful.Success && builtTooPowerful.Error == "browser_request_action_level_exceeds_policy", "browser request builder denies action level above browser policy");

        var builtSystemMissing = builder.TryBuild("browser.reference.system-account.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_3",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_1",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_1"
        });
        AssertTrue(!builtSystemMissing.Success && builtSystemMissing.Error == "browser_request_missing_system_binding", "browser request builder requires system binding for system-account tools");

        var builtUserMissingGrant = builder.TryBuild("browser.reference.user-delegated.read", new BrowserExecutionRequestBuildInput
        {
            RequestId = "req_browser_built_4",
            CapabilityId = "browser.read",
            Route = "browser_read",
            PrincipalId = "principal_1",
            TaskId = "task_1",
            SessionId = "session_1",
            StartUrl = "https://example.com",
            IntendedActionLevel = "read",
            SiteBindingId = "site_binding_2"
        });
        AssertTrue(!builtUserMissingGrant.Success && builtUserMissingGrant.Error == "browser_request_missing_user_grant", "browser request builder requires user grant for user-delegated tools");
    }

    var logDbPath = Path.Combine(sandboxRoot, "interaction-log.db");
    using (var logDb = BrokerDb.UseSqlite($"Data Source={logDbPath}"))
    {
        var initializer = new BrokerDbInitializer(logDb);
        initializer.Initialize();
        var recorder = new HighLevelInteractionRecorder(logDb);
        recorder.Record(new HighLevelInteractionRecord
        {
            Channel = "line",
            UserId = "tester",
            RawInput = "/build website",
            RawReply = "project name required",
            ParsedKind = HighLevelInputKind.Production.ToString(),
            WorkflowState = HighLevelWorkflowState.Idle.ToString(),
            WorkflowAction = HighLevelWorkflowAction.StartProduction.ToString(),
            RouteMode = HighLevelRouteMode.Production.ToString()
        });
        recorder.Record(new HighLevelInteractionRecord
        {
            Channel = "line",
            UserId = "tester",
            RawInput = "#MySite",
            RawReply = "draft updated",
            ParsedKind = HighLevelInputKind.ProjectName.ToString(),
            WorkflowState = HighLevelWorkflowState.AwaitingProjectName.ToString(),
            WorkflowAction = HighLevelWorkflowAction.CaptureProjectName.ToString(),
            RouteMode = HighLevelRouteMode.Production.ToString()
        });

        var logEntries = recorder.ReadLatest("line", "tester", 10);
        AssertTrue(logEntries.Count == 2, "interaction recorder persists append-only records by channel and user");
        AssertTrue(logEntries[0].RawInput == "/build website", "interaction recorder preserves raw input");
        AssertTrue(logEntries[1].WorkflowAction == HighLevelWorkflowAction.CaptureProjectName.ToString(), "interaction recorder preserves interpreted workflow action");

        var memoryStore = new HighLevelMemoryStore(logDb);
        memoryStore.Write(new HighLevelMemoryState
        {
            Channel = "line",
            UserId = "tester",
            CurrentGoal = "build website",
            LastRouteMode = HighLevelRouteMode.Production.ToString(),
            WorkflowState = HighLevelWorkflowState.AwaitingProjectName.ToString(),
            WorkflowAction = HighLevelWorkflowAction.CaptureProjectName.ToString(),
            PendingDraftId = "draft_123",
            PendingProjectName = true,
            LastTaskType = "code_gen"
        });

        var memoryState = memoryStore.ReadLatest("line", "tester");
        AssertTrue(memoryState != null, "memory store persists projected memory state");
        AssertTrue(memoryState!.CurrentGoal == "build website", "memory store keeps de-commanded current goal");
        AssertTrue(memoryState.PendingDraftId == "draft_123", "memory store preserves reusable workflow state");

        var interpretationStore = new HighLevelInterpretationStore(logDb);
        interpretationStore.Record(new HighLevelInterpretationRecord
        {
            Channel = "line",
            UserId = "tester",
            InteractionType = HighLevelRouteMode.Production.ToString(),
            ParsedKind = HighLevelInputKind.Production.ToString(),
            WorkflowState = HighLevelWorkflowState.Idle.ToString(),
            WorkflowAction = HighLevelWorkflowAction.StartProduction.ToString(),
            CommandExtractionAllowed = true,
            TrustReason = "raw user input may issue commands",
            CandidateGoal = "build website",
            TaskType = "code_gen",
            DraftId = "draft_123"
        });

        var interpretations = interpretationStore.ReadLatest("line", "tester", 10);
        AssertTrue(interpretations.Count == 1, "interpretation store persists append-only interpreted records");
        AssertTrue(interpretations[0].CandidateGoal == "build website", "interpretation store keeps candidate goal separate from raw log");

        var executionIntentStore = new HighLevelExecutionIntentStore(logDb);
        executionIntentStore.Write(new HighLevelExecutionIntent
        {
            Channel = "line",
            UserId = "tester",
            Stage = "executable",
            PromotionReason = "explicit confirm",
            Goal = "build website",
            TaskType = "code_gen",
            ProjectName = "MySite",
            DraftId = "draft_123"
        });

        var executionIntent = executionIntentStore.ReadLatest("line", "tester");
        AssertTrue(executionIntent != null, "execution intent store persists promoted executable intent");
        AssertTrue(executionIntent!.Stage == "executable", "execution intent store preserves promotion stage");
        AssertTrue(HighLevelExecutionIntentStore.BuildDocumentId("line", "tester") == "hlm.execution.line.tester", "execution intent document id is stable for downstream references");
    }

    var readmePath = Path.Combine(sandboxRoot, "README.txt");
    File.WriteAllText(readmePath, "hello broker");

    var policy = new PolicyEngine(new SchemaValidator(), new PolicyEngineOptions());
    var capability = new Capability
    {
        CapabilityId = "file.read",
        Route = "read_file",
        ActionType = ActionType.Read,
        ResourceType = "file",
        RiskLevel = RiskLevel.Low,
        ApprovalPolicy = "auto",
        ParamSchema = JsonSerializer.Serialize(new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string" }
            },
            required = new[] { "path" }
        })
    };

    var grant = new CapabilityGrant
    {
        CapabilityId = "file.read",
        ScopeOverride = JsonSerializer.Serialize(new
        {
            paths = new[] { sandboxRoot },
            routes = new[] { "read_file" }
        }),
        RemainingQuota = -1,
        ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        Status = GrantStatus.Active
    };

    var task = new BrokerTask
    {
        TaskId = "task_verify",
        TaskType = "analysis",
        SubmittedBy = "tester",
        ScopeDescriptor = "{}",
        State = TaskState.Active,
        RiskLevel = RiskLevel.Low
    };

    var allowed = new ExecutionRequest
    {
        RequestPayload = JsonSerializer.Serialize(new
        {
            route = "read_file",
            args = new { path = "README.txt" },
            project_root = sandboxRoot
        })
    };

    var allowedResult = policy.Evaluate(allowed, capability, grant, task, 1, 1);
    AssertTrue(allowedResult.Decision == PolicyDecision.Allow, "policy allows matching capability + route + path scope");

    var wrongRoute = new ExecutionRequest
    {
        RequestPayload = JsonSerializer.Serialize(new
        {
            route = "list_directory",
            args = new { path = "README.txt" },
            project_root = sandboxRoot
        })
    };

    var wrongRouteResult = policy.Evaluate(wrongRoute, capability, grant, task, 1, 1);
    AssertTrue(wrongRouteResult.Decision == PolicyDecision.Deny, "policy denies payload route outside capability route");

    var wrongPath = new ExecutionRequest
    {
        RequestPayload = JsonSerializer.Serialize(new
        {
            route = "read_file",
            args = new { path = "../secret.txt" },
            project_root = sandboxRoot
        })
    };

    var wrongPathResult = policy.Evaluate(wrongPath, capability, grant, task, 1, 1);
    AssertTrue(wrongPathResult.Decision == PolicyDecision.Deny, "policy denies path outside granted path scope");

    var missingPath = new ExecutionRequest
    {
        RequestPayload = JsonSerializer.Serialize(new
        {
            route = "read_file",
            args = new { },
            project_root = sandboxRoot
        })
    };

    var missingPathResult = policy.Evaluate(missingPath, capability, grant, task, 1, 1);
    AssertTrue(missingPathResult.Decision == PolicyDecision.Deny, "policy validates args schema instead of wrapper payload");

    var dispatcher = new InProcessDispatcher(NullLogger<InProcessDispatcher>.Instance, sandboxRoot);

    var readResult = await dispatcher.DispatchAsync(new BrokerCore.Contracts.ApprovedRequest
    {
        RequestId = "req_read",
        Route = "read_file",
        Payload = JsonSerializer.Serialize(new
        {
            route = "read_file",
            args = new { path = "README.txt" }
        })
    });
    AssertTrue(readResult.Success, "dispatcher reads file from canonical route+args payload");

    var searchResult = await dispatcher.DispatchAsync(new BrokerCore.Contracts.ApprovedRequest
    {
        RequestId = "req_search",
        Route = "search_content",
        Payload = JsonSerializer.Serialize(new
        {
            route = "search_content",
            args = new
            {
                directory = ".",
                pattern = "hello",
                file_pattern = "*.txt"
            }
        })
    });
    AssertTrue(searchResult.Success, "dispatcher searches content from canonical route+args payload");

    var routeMismatchResult = await dispatcher.DispatchAsync(new BrokerCore.Contracts.ApprovedRequest
    {
        RequestId = "req_bad_route",
        Route = "read_file",
        Payload = JsonSerializer.Serialize(new
        {
            route = "list_directory",
            args = new { path = "README.txt" }
        })
    });
    AssertTrue(!routeMismatchResult.Success, "dispatcher denies mismatched payload route");

    Console.WriteLine("Broker verify passed.");
}
finally
{
    var deleted = false;
    for (var attempt = 0; attempt < 5; attempt++)
    {
        try
        {
            Directory.Delete(sandboxRoot, recursive: true);
            deleted = true;
            break;
        }
        catch (IOException)
        {
            if (attempt < 4)
            {
                Thread.Sleep(250 * (attempt + 1));
            }
        }
        catch (UnauthorizedAccessException)
        {
            if (attempt < 4)
            {
                Thread.Sleep(250 * (attempt + 1));
            }
        }
    }

    if (!deleted)
    {
        Console.WriteLine($"Warning: verify temp directory not deleted: {sandboxRoot}");
    }
}

file sealed class FakeToolSpecRegistry : IToolSpecRegistry
{
    private readonly ToolSpecView _view = new()
    {
        ToolId = "web.search.duckduckgo",
        DisplayName = "DuckDuckGo Web Search",
        Summary = "broker mediated search",
        Kind = "search",
        Status = "active",
        CapabilityBindings =
        [
            new ToolCapabilityBindingView
            {
                CapabilityId = "web.search.duckduckgo",
                Route = "web_search_duckduckgo",
                Purpose = "test",
                Registered = true,
                RegisteredRoute = "web_search_duckduckgo"
            }
        ]
    };

    public ToolSpecView? Get(string toolId)
        => string.Equals(toolId, _view.ToolId, StringComparison.OrdinalIgnoreCase) ? _view : null;

    public IReadOnlyList<ToolSpecDocument> GetDefinitions()
        => Array.Empty<ToolSpecDocument>();

    public IReadOnlyList<ToolSpecView> List(string? filter = null)
        => [_view];
}

file sealed class FakeExecutionDispatcher : IExecutionDispatcher
{
    public Task<ExecutionResult> DispatchAsync(ApprovedRequest approvedRequest)
    {
        var payload = JsonSerializer.Serialize(new
        {
            engine = "duckduckgo",
            query = "taipei weather",
            results = new[]
            {
                new
                {
                    rank = 1,
                    title = "Taipei Weather",
                    url = "https://example.com/weather",
                    snippet = "Rain later this afternoon."
                }
            }
        });

        return Task.FromResult(ExecutionResult.Ok(approvedRequest.RequestId, payload));
    }
}

file sealed class FakeWebHostEnvironment : IWebHostEnvironment
{
    public FakeWebHostEnvironment(string contentRootPath)
    {
        ContentRootPath = contentRootPath;
        WebRootPath = contentRootPath;
        ApplicationName = "Broker.Verify";
        EnvironmentName = "Development";
    }

    public string ApplicationName { get; set; }
    public IFileProvider WebRootFileProvider { get; set; } = null!;
    public string WebRootPath { get; set; }
    public string EnvironmentName { get; set; }
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}
