using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Services;
using FunctionPool.Container;
using Microsoft.Extensions.Configuration;

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
        TestSpawnCredentialAndDeactivateRevocation();
        TestNormalizeAgentIdHandlesUnsafeInput();
        TestContainerRunArgumentsAreAtomic();
        TestContainerRunArgumentsAreHardened();
        TestSecretEnvironmentStaysOutOfArguments();
        TestContainerRunArgumentsRejectUnsafeConfig();
        TestSpawnEndpointsValidateInput();

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

    private static void TestSpawnCredentialAndDeactivateRevocation()
    {
        Console.WriteLine("--- Spawn registration credential and deactivation ---");

        var tempDir = Path.Combine(Path.GetTempPath(), "b4a-agent-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "broker.db");
            using var db = new BrokerDb($"Data Source={dbPath}");
            new BrokerDbInitializer(db).Initialize();

            var credentials = new RegistrationCredentialService(db);
            var sessions = new SessionService(db);
            var service = new AgentSpawnService(db, credentials, sessions);
            var created = service.CreateAgent(new AgentSpawnRequest
            {
                AgentId = "credential",
                DisplayName = "Credential Agent",
                TaskType = "analysis",
                RequestedBy = "agent-container-test",
                LlmDefaultModel = "high-level-test-model"
            });
            AssertTrue("credential-agent-created", created.Success);
            var agent = service.ListAgents().Single(a => a.AgentId == created.AgentId);

            var first = service.IssueSpawnCredential(agent, "agent-container-test", TimeSpan.FromHours(1));
            AssertTrue("spawn-credential-verifies", credentials.Verify(agent.PrincipalId, agent.TaskId, first.Secret).Succeeded);
            AssertTrue("spawn-credential-to-string-hides-secret", !first.ToString().Contains(first.Secret, StringComparison.Ordinal));

            // A second spawn of the same agent supersedes the first credential.
            var second = service.IssueSpawnCredential(agent, "agent-container-test", TimeSpan.FromHours(1));
            AssertEqual("respawn-revokes-previous",
                credentials.Verify(agent.PrincipalId, agent.TaskId, first.Secret).Failure.ToString(),
                RegistrationCredentialFailure.Revoked.ToString());
            AssertTrue("respawn-credential-verifies", credentials.Verify(agent.PrincipalId, agent.TaskId, second.Secret).Succeeded);

            // A failed spawn revokes the credential it was given.
            var failed = service.IssueSpawnCredential(agent, "agent-container-test", TimeSpan.FromHours(1));
            service.RevokeSpawnCredential(failed.CredentialId, "agent-container-test");
            AssertEqual("failed-spawn-credential-revoked",
                credentials.Verify(agent.PrincipalId, agent.TaskId, failed.Secret).Failure.ToString(),
                RegistrationCredentialFailure.Revoked.ToString());

            var adminIssued = credentials.Issue(agent.PrincipalId, agent.TaskId, RegistrationCredentialSources.AdminIssue, "agent-container-test", DateTime.UtcNow.AddHours(1));
            var session = sessions.RegisterSession(agent.TaskId, agent.PrincipalId, agent.RoleId, "jti_credential", 1, string.Empty);

            AssertTrue("deactivate-succeeds", service.DeactivateAgent(created.AgentId));
            AssertEqual("deactivate-revokes-admin-issued",
                credentials.Verify(agent.PrincipalId, agent.TaskId, adminIssued.Secret).Failure.ToString(),
                RegistrationCredentialFailure.Revoked.ToString());
            AssertTrue("deactivate-leaves-no-active-credential", credentials.List(agent.PrincipalId, agent.TaskId).Count == 0);
            AssertEqual("deactivate-revokes-sessions",
                sessions.GetSession(session.SessionId)?.Status.ToString(),
                BrokerCore.Models.SessionStatus.Revoked.ToString());
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

    private static ContainerConfig NewConfig() => new()
    {
        NetworkName = "bricks4agent_worker-net",
        BrokerHostForWorkers = "broker",
        BrokerPortForWorkers = 7000
    };

    private static WorkerImageConfig NewAgentImage() => new()
    {
        Image = "bricks4agent-agent:latest",
        MemoryLimit = "512m",
        NetworkName = "bricks4agent_agent-net",
        User = "10001"
    };

    private static WorkerImageConfig NewWorkerImage() => new()
    {
        Image = "bricks4agent-file-worker:latest",
        MemoryLimit = "256m"
    };

    private static ContainerSpawnRequest AgentRequest(
        IReadOnlyDictionary<string, string>? trusted = null,
        IReadOnlyDictionary<string, string>? secrets = null) => new()
    {
        WorkerType = "agent",
        WorkerId = "agent_demo_raw",
        TrustedEnvironment = trusted ?? new Dictionary<string, string>(),
        SecretEnvironment = secrets ?? new Dictionary<string, string>()
    };

    private static ContainerSpawnRequest WorkerRequest() => new()
    {
        WorkerType = "file-worker",
        WorkerId = "file-worker-1"
    };

    private static List<string> Build(ContainerConfig config, WorkerImageConfig image, ContainerSpawnRequest request)
        => ContainerManager.BuildRunArguments(config, image, request, $"b4a-{request.WorkerType}-test").ToList();

    private static void TestContainerRunArgumentsAreAtomic()
    {
        Console.WriteLine("--- Container run argument construction ---");

        var config = NewConfig();
        var image = NewAgentImage();
        image.NetworkName = "bricks4agent_control-net";
        image.Environment["STATIC_VALUE"] = "two words";

        var envOverrides = new Dictionary<string, string>
        {
            ["AGENT_RUN"] = "Reply with spaces, quotes \"ok\", and equals=a=b.",
            ["BROKER_PUB_KEY"] = "abc+/=="
        };

        var args = Build(config, image, AgentRequest(envOverrides));

        AssertEqual("container-network-override", GetValueAfter(args, "--network"), "bricks4agent_control-net");
        AssertTrue("container-does-not-use-worker-network", !args.Contains("bricks4agent_worker-net"));
        AssertEqual("container-memory", GetValueAfter(args, "--memory"), "512m");
        AssertEqual("container-user", GetValueAfter(args, "--user"), "10001");
        AssertTrue("container-env-static-atomic", args.Contains("STATIC_VALUE=two words"));
        AssertTrue("container-env-run-atomic", args.Contains("AGENT_RUN=Reply with spaces, quotes \"ok\", and equals=a=b."));
        AssertTrue("container-env-key-atomic", args.Contains("BROKER_PUB_KEY=abc+/=="));
        AssertTrue("container-no-volume-for-agent", !args.Contains("-v"));
        AssertEqual("container-image-last", args.Last(), "bricks4agent-agent:latest");

        // Non-agent host path mount: Windows drive letter kept as one argument when allowed.
        var workerConfig = NewConfig();
        workerConfig.AllowedHostPathRoots.Add(@"D:\Agent Work");
        var worker = NewWorkerImage();
        worker.Volumes.Add(@"D:\Agent Work:/workspace:ro");
        var workerArgs = Build(workerConfig, worker, WorkerRequest());
        AssertTrue("container-volume-atomic", workerArgs.Contains(@"D:\Agent Work:/workspace:ro"));
        AssertEqual("container-worker-network-default", GetValueAfter(workerArgs, "--network"), "bricks4agent_worker-net");
    }

    private static void TestContainerRunArgumentsAreHardened()
    {
        Console.WriteLine("--- Container hardening flags ---");

        foreach (var (label, image, request) in new[]
        {
            ("agent", NewAgentImage(), AgentRequest()),
            ("worker", NewWorkerImage(), WorkerRequest())
        })
        {
            var args = Build(NewConfig(), image, request);
            var imageIndex = args.Count - 1;
            AssertEqual($"{label}-image-last", args[imageIndex], image.Image);

            AssertFlagBeforeImage($"{label}-read-only", args, "--read-only", null);
            AssertFlagBeforeImage($"{label}-tmpfs", args, "--tmpfs", "/tmp:rw,noexec,nosuid,nodev,size=64m");
            AssertFlagBeforeImage($"{label}-cap-drop", args, "--cap-drop", "ALL");
            AssertFlagBeforeImage($"{label}-no-new-privileges", args, "--security-opt", "no-new-privileges:true");
            AssertFlagBeforeImage($"{label}-pids-limit", args, "--pids-limit", "256");
            AssertFlagBeforeImage($"{label}-memory", args, "--memory", image.MemoryLimit);
            AssertFlagBeforeImage($"{label}-restart", args, "--restart", "on-failure:3");
            AssertTrue($"{label}-not-privileged", !args.Contains("--privileged"));
        }

        // Defaults when the image config leaves memory and pids unset; per-image pids override.
        var config = NewConfig();
        config.DefaultMemoryLimit = "384m";
        config.DefaultPidsLimit = 128;
        var bare = NewWorkerImage();
        bare.MemoryLimit = null;
        var defaults = Build(config, bare, WorkerRequest());
        AssertEqual("default-memory-applied", GetValueAfter(defaults, "--memory"), "384m");
        AssertEqual("default-pids-applied", GetValueAfter(defaults, "--pids-limit"), "128");

        var tuned = NewWorkerImage();
        tuned.PidsLimit = 1024;
        AssertEqual("per-image-pids", GetValueAfter(Build(config, tuned, WorkerRequest()), "--pids-limit"), "1024");

        AssertTrue("rm-removes-anonymous-volumes",
            ContainerManager.BuildRemoveArguments("abc123").SequenceEqual(new[] { "rm", "-f", "-v", "abc123" }));
    }

    private static void TestSecretEnvironmentStaysOutOfArguments()
    {
        Console.WriteLine("--- Secret environment ---");

        const string secretValue = "s3cr3t-value-that-must-not-reach-argv";
        var request = AgentRequest(
            new Dictionary<string, string> { ["BROKER_PRINCIPAL_ID"] = "prn_agent_demo" },
            new Dictionary<string, string> { ["BROKER_REGISTRATION_SECRET"] = secretValue });

        var args = Build(NewConfig(), NewAgentImage(), request);
        var nameIndex = args.IndexOf("BROKER_REGISTRATION_SECRET");
        AssertTrue("secret-passed-by-name", nameIndex > 0 && args[nameIndex - 1] == "-e");
        AssertTrue("secret-name-before-image", nameIndex >= 0 && nameIndex < args.Count - 1);
        AssertTrue("secret-value-not-in-args", args.All(a => !a.Contains(secretValue, StringComparison.Ordinal)));
        AssertTrue("secret-name-has-no-value", !args.Any(a => a.StartsWith("BROKER_REGISTRATION_SECRET=", StringComparison.Ordinal)));
        AssertTrue("trusted-env-still-plain", args.Contains("BROKER_PRINCIPAL_ID=prn_agent_demo"));

        var cliEnvironment = ContainerManager.BuildCliEnvironment(request);
        AssertEqual("secret-in-cli-environment", cliEnvironment.TryGetValue("BROKER_REGISTRATION_SECRET", out var v) ? v : null, secretValue);
        AssertTrue("cli-environment-only-secrets", cliEnvironment.Count == 1);

        AssertThrows("secret-name-collides-with-trusted", () => Build(NewConfig(), NewAgentImage(), AgentRequest(
            new Dictionary<string, string> { ["BROKER_REGISTRATION_SECRET"] = "plain" },
            new Dictionary<string, string> { ["BROKER_REGISTRATION_SECRET"] = secretValue })));
        AssertThrows("secret-name-invalid", () => Build(NewConfig(), NewAgentImage(), AgentRequest(
            secrets: new Dictionary<string, string> { ["BAD-NAME"] = secretValue })));
        AssertThrows("trusted-name-with-equals", () => Build(NewConfig(), NewAgentImage(), AgentRequest(
            new Dictionary<string, string> { ["A=B"] = "x" })));
    }

    private static void TestContainerRunArgumentsRejectUnsafeConfig()
    {
        Console.WriteLine("--- Container configuration that is refused ---");

        foreach (var user in new[] { "0", "root", "ROOT", "10001:0", "0:10001" })
        {
            var image = NewWorkerImage();
            image.User = user;
            AssertThrows($"user-{user}-refused", () => Build(NewConfig(), image, WorkerRequest()));
        }

        foreach (var network in new[] { "host", "HOST", "container:other", "ns:/proc/1/ns/net" })
        {
            var worker = NewWorkerImage();
            worker.NetworkName = network;
            AssertThrows($"worker-network-{network}-refused", () => Build(NewConfig(), worker, WorkerRequest()));
            var agent = NewAgentImage();
            agent.NetworkName = network;
            AssertThrows($"agent-network-{network}-refused", () => Build(NewConfig(), agent, AgentRequest()));
        }

        // Agent: no dedicated network → refused, and never falls back to the shared worker network.
        var noNetworkAgent = NewAgentImage();
        noNetworkAgent.NetworkName = null;
        AssertThrows("agent-without-network-refused", () => Build(NewConfig(), noNetworkAgent, AgentRequest()));
        var sidecarConfig = NewConfig();
        sidecarConfig.NetworkName = "";
        sidecarConfig.AllowAgentDefaultNetwork = true;
        var sidecarArgs = Build(sidecarConfig, noNetworkAgent, AgentRequest());
        AssertTrue("agent-default-network-opt-in", !sidecarArgs.Contains("--network"));
        var optInIgnoresWorkerNet = NewConfig();
        optInIgnoresWorkerNet.AllowAgentDefaultNetwork = true;
        AssertTrue("agent-never-uses-worker-network",
            !Build(optInIgnoresWorkerNet, noNetworkAgent, AgentRequest()).Contains("bricks4agent_worker-net"));

        // Agent: no mounts and no ports at all.
        var agentWithVolume = NewAgentImage();
        agentWithVolume.Volumes.Add("agent-data:/workspace:ro");
        AssertThrows("agent-volume-refused", () => Build(NewConfig(), agentWithVolume, AgentRequest()));
        var agentWithHostPath = NewAgentImage();
        agentWithHostPath.Volumes.Add(@"D:\Agent Work:/workspace");
        var permissive = NewConfig();
        permissive.AllowedHostPathRoots.Add(@"D:\Agent Work");
        AssertThrows("agent-host-path-refused-even-if-allowed", () => Build(permissive, agentWithHostPath, AgentRequest()));
        var agentWithPort = NewAgentImage();
        agentWithPort.Ports.Add("127.0.0.1:8080:8080");
        AssertThrows("agent-port-refused", () => Build(NewConfig(), agentWithPort, AgentRequest()));

        // Non-agent mounts: sockets, system paths, traversal, relative and non-allowlisted host paths.
        var allowed = NewConfig();
        allowed.AllowedHostPathRoots.Add("/srv/b4a");
        allowed.AllowedHostPathRoots.Add("/");               // a system path in the allowlist does not help
        allowed.AllowedHostPathRoots.Add(@"C:\");
        foreach (var volume in new[]
        {
            "/var/run/docker.sock:/var/run/docker.sock",
            "/run/podman/podman.sock:/run/podman/podman.sock",
            "/srv/b4a/runtime.sock:/x.sock",
            @"\\.\pipe\docker_engine:/pipe",
            "/etc:/host-etc:ro",
            "/proc:/host-proc",
            "/:/host",
            "/srv/b4a/../../etc:/x",
            "./data:/data",
            "/opt/elsewhere:/data",
            @"C:\:/c",
            @"C:\Windows\System32:/w",
            "/srv/b4a/data:relative-target"
        })
        {
            var worker = NewWorkerImage();
            worker.Volumes.Add(volume);
            AssertThrows($"volume-refused:{volume}", () => Build(allowed, worker, WorkerRequest()));
        }

        foreach (var volume in new[] { "/srv/b4a/data:/workspace:ro", "/srv/b4a:/workspace", "worker-cache:/cache" })
        {
            var worker = NewWorkerImage();
            worker.Volumes.Add(volume);
            AssertTrue($"volume-allowed:{volume}", Build(allowed, worker, WorkerRequest()).Contains(volume));
        }

        var noAllowlist = NewWorkerImage();
        noAllowlist.Volumes.Add("/srv/b4a/data:/workspace:ro");
        AssertThrows("host-path-refused-without-allowlist", () => Build(NewConfig(), noAllowlist, WorkerRequest()));

        var publicPort = NewWorkerImage();
        publicPort.Ports.Add("19090:19090");
        AssertThrows("worker-public-port-refused", () => Build(NewConfig(), publicPort, WorkerRequest()));
        var loopbackPort = NewWorkerImage();
        loopbackPort.Ports.Add("127.0.0.1:19090:19090");
        AssertTrue("worker-loopback-port-allowed", Build(NewConfig(), loopbackPort, WorkerRequest()).Contains("127.0.0.1:19090:19090"));

        var zeroPids = NewWorkerImage();
        zeroPids.PidsLimit = 0;
        AssertThrows("zero-pids-refused", () => Build(NewConfig(), zeroPids, WorkerRequest()));
    }

    private static void TestSpawnEndpointsValidateInput()
    {
        Console.WriteLine("--- Spawn endpoint input ---");

        (ContainerSpawnRequest? Request, string? Error) Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return Broker.Endpoints.WorkerEndpoints.ParseSpawnRequest(doc.RootElement.Clone());
        }

        AssertTrue("workers-spawn-missing-type-400", Parse("{}").Error?.Contains("worker_type is required") == true);
        AssertTrue("workers-spawn-empty-type-400", Parse("{\"worker_type\":\" \"}").Request == null);
        AssertTrue("workers-spawn-agent-refused", Parse("{\"worker_type\":\"agent\"}").Error?.Contains("/agents/spawn") == true);
        AssertTrue("workers-spawn-agent-case-refused", Parse("{\"worker_type\":\"Agent\"}").Request == null);
        AssertTrue("workers-spawn-environment-refused",
            Parse("{\"worker_type\":\"file-worker\",\"environment\":{\"LD_PRELOAD\":\"/x.so\"}}").Error?.Contains("environment") == true);
        AssertTrue("workers-spawn-bad-id-refused", Parse("{\"worker_type\":\"file-worker\",\"worker_id\":\"../x\"}").Request == null);
        AssertTrue("workers-spawn-long-id-refused",
            Parse($"{{\"worker_type\":\"file-worker\",\"worker_id\":\"{new string('a', 65)}\"}}").Request == null);

        var ok = Parse("{\"worker_type\":\"file-worker\",\"worker_id\":\"file-worker-2\"}");
        AssertEqual("workers-spawn-type", ok.Request?.WorkerType, "file-worker");
        AssertEqual("workers-spawn-id", ok.Request?.WorkerId, "file-worker-2");
        AssertTrue("workers-spawn-no-env", ok.Request?.TrustedEnvironment.Count == 0 && ok.Request?.SecretEnvironment.Count == 0);
        var generated = Parse("{\"worker_type\":\"file-worker\"}").Request;
        var generatedAgain = Parse("{\"worker_type\":\"file-worker\"}").Request;
        AssertTrue("workers-spawn-generated-id", generated != null && generated.WorkerId.EndsWith("-file-worker", StringComparison.Ordinal) && generated.WorkerId.Length <= 64);
        AssertTrue("workers-spawn-generated-id-unique-prefix",
            generated != null && generatedAgain != null && generated.WorkerId[..12] != generatedAgain.WorkerId[..12]);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FunctionPool:ContainerManager:AgentBrokerUrl"] = "http://host.containers.internal:5361"
            })
            .Build();
        (bool Ok, string BrokerUrl, string? Error) Resolve(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return Broker.Endpoints.AgentEndpoints.ResolveAgentBrokerUrl(doc.RootElement.Clone(), configuration);
        }

        AssertEqual("agent-broker-url-default", Resolve("{}").BrokerUrl, "http://host.containers.internal:5361");
        AssertTrue("agent-broker-url-same-value", Resolve("{\"broker_url\":\"http://host.containers.internal:5361/\"}").Ok);
        AssertTrue("agent-broker-url-other-host-refused", !Resolve("{\"broker_url\":\"http://attacker.example:5361\"}").Ok);
        AssertTrue("agent-broker-url-other-port-refused", !Resolve("{\"broker_url\":\"http://host.containers.internal:9999\"}").Ok);
        AssertTrue("agent-broker-url-non-string-refused", !Resolve("{\"broker_url\":42}").Ok);

        AssertTrue("agent-max-iterations-capped", Broker.Endpoints.AgentEndpoints.ClampMaxIterations(100000) == Broker.Endpoints.AgentEndpoints.MaxSpawnIterations);
        AssertTrue("agent-max-iterations-floor", Broker.Endpoints.AgentEndpoints.ClampMaxIterations(0) == 1);
        AssertTrue("agent-max-iterations-kept", Broker.Endpoints.AgentEndpoints.ClampMaxIterations(12) == 12);
    }

    private static void AssertFlagBeforeImage(string name, List<string> args, string flag, string? value)
    {
        var index = args.IndexOf(flag);
        var ok = index >= 0 && index < args.Count - 1 && (value == null || (index + 1 < args.Count - 1 && args[index + 1] == value));
        if (ok) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected {flag} {value} before the image"); _failed++; }
    }

    private static void AssertThrows(string name, Action action)
    {
        try
        {
            action();
            Console.Error.WriteLine($"  [FAIL] {name}: expected InvalidOperationException");
            _failed++;
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine($"  [PASS] {name}");
            _passed++;
        }
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
