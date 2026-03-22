using System.Text.Json;
using Broker.Adapters;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
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
    AssertTrue(parser.Parse("?help").Kind == HighLevelInputKind.Help, "parser recognizes explicit help command");
    AssertTrue(parser.Parse("/build website").Kind == HighLevelInputKind.Production, "parser recognizes production prefix");
    AssertTrue(parser.Parse("?weather taipei").Kind == HighLevelInputKind.Query, "parser recognizes query prefix");
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
