using BrokerCore.Data;
using BrokerCore.Services;
using FunctionPool.Container;

namespace Broker.Tests;

public static class AgentContainerTests
{
    private static int _passed;
    private static int _failed;

    public static (int passed, int failed) Run()
    {
        _passed = 0;
        _failed = 0;

        Console.WriteLine("=== Agent Container Tests ===");
        Console.WriteLine();

        TestCreateListAndStopUseCanonicalAgentId();
        TestNormalizeAgentIdHandlesUnsafeInput();
        TestContainerRunArgumentsAreAtomic();

        Console.WriteLine();
        Console.WriteLine($"=== Agent Container Test Results: {_passed} passed, {_failed} failed ===");
        return (_passed, _failed);
    }

    private static void TestCreateListAndStopUseCanonicalAgentId()
    {
        Console.WriteLine("--- Agent create/list/stop identity ---");

        var tempDir = Path.Combine(Path.GetTempPath(), "b4a-agent-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "broker.db");
            using var db = new BrokerDb($"Data Source={dbPath}");
            new BrokerDbInitializer(db).Initialize();

            var service = new AgentSpawnService(db);
            var result = service.CreateAgent(new AgentSpawnRequest
            {
                AgentId = "demo raw!",
                DisplayName = "Demo Agent",
                TaskType = "rag",
                RequestedBy = "agent-container-test",
                LlmDefaultModel = "high-level-test-model",
                LlmAllowModelOverride = false,
                LlmSupportsToolCalling = true,
                LlmStreamingEnabled = false
            });

            AssertTrue("agent-create-success", result.Success);
            AssertEqual("agent-id-normalized", result.AgentId, "agent_demo_raw");
            AssertEqual("agent-principal-id", result.PrincipalId, "prn_agent_demo_raw");
            AssertEqual("agent-task-id", result.TaskId, "task_agent_demo_raw");
            AssertTrue("agent-rag-capability", result.GrantedCapabilities.Contains("rag.retrieve"));
            AssertAgentRuntimeModel("agent-runtime-high-level-model", result.RuntimeDescriptor, "high-level-test-model");

            var listed = service.ListAgents().SingleOrDefault(a => a.AgentId == result.AgentId);
            AssertTrue("agent-list-finds-created", listed != null);
            AssertEqual("agent-list-principal", listed?.PrincipalId, result.PrincipalId);
            AssertEqual("agent-list-state-active", listed?.State, "Active");

            var deactivated = service.DeactivateAgent("demo raw!");
            AssertTrue("agent-stop-accepts-raw-id", deactivated);
            var stopped = service.ListAgents().Single(a => a.AgentId == result.AgentId);
            AssertEqual("agent-list-state-completed", stopped.State, "Completed");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }
    }

    private static void TestNormalizeAgentIdHandlesUnsafeInput()
    {
        Console.WriteLine("--- Agent id normalization ---");

        var unsafeId = AgentSpawnService.NormalizeAgentId("!!!");
        AssertTrue("agent-id-symbol-fallback-prefix", unsafeId.StartsWith("agent_", StringComparison.Ordinal));
        AssertTrue("agent-id-symbol-fallback-not-bare", unsafeId.Length > "agent_".Length);

        var longId = AgentSpawnService.NormalizeAgentId(new string('x', 100));
        AssertTrue("agent-id-long-truncated", longId.Length <= 64);
        AssertTrue("agent-id-long-has-prefix", longId.StartsWith("agent_", StringComparison.Ordinal));
    }

    private static void TestContainerRunArgumentsAreAtomic()
    {
        Console.WriteLine("--- Container run argument construction ---");

        var config = new ContainerConfig
        {
            NetworkName = "bricks4agent_worker-net",
            BrokerHostForWorkers = "broker",
            BrokerPortForWorkers = 7000
        };
        var image = new WorkerImageConfig
        {
            Image = "bricks4agent-agent:latest",
            MemoryLimit = "512m",
            NetworkName = "bricks4agent_control-net",
            User = "10001"
        };
        image.Environment["STATIC_VALUE"] = "two words";
        image.Volumes.Add(@"D:\Agent Work:/workspace");

        var envOverrides = new Dictionary<string, string>
        {
            ["AGENT_RUN"] = "Reply with spaces, quotes \"ok\", and equals=a=b.",
            ["BROKER_PUB_KEY"] = "abc+/=="
        };

        var args = ContainerManager.BuildRunArguments(
            config, image, "agent_demo_raw", "b4a-agent-agent", envOverrides).ToList();

        AssertEqual("container-network-override", GetValueAfter(args, "--network"), "bricks4agent_control-net");
        AssertTrue("container-does-not-use-worker-network", !args.Contains("bricks4agent_worker-net"));
        AssertEqual("container-memory", GetValueAfter(args, "--memory"), "512m");
        AssertEqual("container-user", GetValueAfter(args, "--user"), "10001");
        AssertTrue("container-env-run-atomic", args.Contains("AGENT_RUN=Reply with spaces, quotes \"ok\", and equals=a=b."));
        AssertTrue("container-env-key-atomic", args.Contains("BROKER_PUB_KEY=abc+/=="));
        AssertTrue("container-volume-atomic", args.Contains(@"D:\Agent Work:/workspace"));
        AssertEqual("container-image-last", args.Last(), "bricks4agent-agent:latest");
    }

    private static string? GetValueAfter(List<string> args, string option)
    {
        var index = args.IndexOf(option);
        return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
    }

    private static void AssertAgentRuntimeModel(string name, string runtimeDescriptor, string expectedModel)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(runtimeDescriptor);
        var llm = doc.RootElement.GetProperty("llm");
        AssertEqual(name, llm.GetProperty("default_model").GetString(), expectedModel);
        AssertTrue("agent-runtime-tool-calling", llm.GetProperty("supports_tool_calling").GetBoolean());
    }

    private static void AssertTrue(string name, bool condition)
    {
        if (condition) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected true"); _failed++; }
    }

    private static void AssertEqual(string name, string? actual, string? expected)
    {
        if (actual == expected) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected \"{expected}\", got \"{actual}\""); _failed++; }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Test cleanup is best-effort; AGENTS.md cleanup sweep handles leftovers.
        }
    }
}
