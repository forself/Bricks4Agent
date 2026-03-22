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
