using System.Diagnostics;
using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Services;
using ExecutionAdapterWorker.Handlers;
using ExecutionAdapterWorker.Support;

namespace Broker.Tests;

/// <summary>
/// §18.1 執行配接器單元測試 — repo.patch.apply 與 build.test.run。
/// 對照規格 §14.1（配接器原則）、§14.2（repo）、§14.3（build/test）、§6.4（catalog）。
/// </summary>
public static class ExecutionAdapterTests
{
    private static int _passed;
    private static int _failed;

    public static (int passed, int failed) Run()
    {
        _passed = 0;
        _failed = 0;

        Console.WriteLine("=== Execution Adapter Tests (§18.1) ===");
        Console.WriteLine();

        TestCatalogSeedsAdapterCapabilities();   // test 10
        TestRepoAppliesValidPatchInScope();      // test 1
        TestRepoRejectsBaseCommitMismatch();     // test 2
        TestRepoRejectsPathOutsideScope();       // test 3
        TestRepoRejectsFreeFormShell();          // test 4
        TestRepoOnlyTouchesPatchFiles();         // test 5
        TestRepoIdempotencyReplayDoesNotReapply();// test 6
        TestBuildRunsWhitelistedCommand();       // test 7
        TestBuildRejectsNonWhitelisted();        // test 8
        TestBuildCapturesExitAndTruncates();     // test 9

        Console.WriteLine();
        Console.WriteLine($"=== Execution Adapter Test Results: {_passed} passed, {_failed} failed ===");
        return (_passed, _failed);
    }

    // ── test 10: catalog seed ──
    private static void TestCatalogSeedsAdapterCapabilities()
    {
        Console.WriteLine("--- Catalog seeds adapter capabilities ---");
        var tempDir = NewTempDir("catalog");
        try
        {
            var dbPath = Path.Combine(tempDir, "broker.db");
            using var db = new BrokerDb($"Data Source={dbPath}");
            new BrokerDbInitializer(db).Initialize();
            var catalog = new CapabilityCatalog(db);

            var repo = catalog.GetCapability("repo.patch.apply");
            AssertTrue("repo-cap-exists", repo != null);
            AssertEqual("repo-cap-route", repo?.Route, "apply_patch");
            AssertEqual("repo-cap-risk", repo?.RiskLevel.ToString(), "Medium");
            AssertEqual("repo-cap-action", repo?.ActionType.ToString(), "Write");

            var build = catalog.GetCapability("build.test.run");
            AssertTrue("build-cap-exists", build != null);
            AssertEqual("build-cap-route", build?.Route, "run_build_test");
            AssertEqual("build-cap-action", build?.ActionType.ToString(), "Execute");
        }
        finally { TryDelete(tempDir); }
    }

    // ── test 1 ──
    private static void TestRepoAppliesValidPatchInScope()
    {
        Console.WriteLine("--- Repo applies valid patch in scope ---");
        var (tempDir, repoDir, evidenceDir) = NewRepoDirs("repo-apply");
        try
        {
            InitRepo(repoDir);
            var patch = MakePatch(repoDir, "fileA.txt", "line1\nline2\n");

            var handler = new RepoApplyPatchHandler(repoDir, evidenceDir);
            var (ok, result, err) = Exec(handler, Payload(new { patch }), Scope(new { allowed_paths = new[] { "fileA.txt" } }));

            AssertTrue("apply-success", ok);
            AssertTrue("apply-no-error", err == null);
            AssertEqual("apply-file-content", File.ReadAllText(Path.Combine(repoDir, "fileA.txt")), "line1\nline2\n");
            if (result != null)
            {
                using var doc = JsonDocument.Parse(result);
                var files = doc.RootElement.GetProperty("summary").GetProperty("files");
                AssertTrue("apply-summary-lists-file", files.EnumerateArray().Any(e => e.GetString() == "fileA.txt"));
                var evidence = doc.RootElement.GetProperty("evidence_ref").GetString();
                AssertTrue("apply-evidence-written", evidence != null && File.Exists(evidence));
            }
        }
        finally { TryDelete(tempDir); }
    }

    // ── test 2 ──
    private static void TestRepoRejectsBaseCommitMismatch()
    {
        Console.WriteLine("--- Repo rejects base_commit mismatch ---");
        var (tempDir, repoDir, evidenceDir) = NewRepoDirs("repo-base");
        try
        {
            InitRepo(repoDir);
            var patch = MakePatch(repoDir, "fileA.txt", "line1\nline2\n");

            var handler = new RepoApplyPatchHandler(repoDir, evidenceDir);
            var (ok, _, err) = Exec(handler,
                Payload(new { patch, base_commit = "0000000000000000000000000000000000000000" }),
                Scope(new { allowed_paths = new[] { "fileA.txt" } }));

            AssertTrue("base-mismatch-rejected", !ok);
            AssertTrue("base-mismatch-reason", err != null && err.Contains("base_commit"));
            AssertEqual("base-mismatch-no-write", File.ReadAllText(Path.Combine(repoDir, "fileA.txt")), "line1\n");
        }
        finally { TryDelete(tempDir); }
    }

    // ── test 3 ──
    private static void TestRepoRejectsPathOutsideScope()
    {
        Console.WriteLine("--- Repo rejects path outside allowed scope ---");
        var (tempDir, repoDir, evidenceDir) = NewRepoDirs("repo-scope");
        try
        {
            InitRepo(repoDir);
            var patch = MakePatch(repoDir, "fileA.txt", "line1\nline2\n");

            var handler = new RepoApplyPatchHandler(repoDir, evidenceDir);
            var (ok, _, err) = Exec(handler, Payload(new { patch }),
                Scope(new { allowed_paths = new[] { "some/other/dir" } }));

            AssertTrue("scope-violation-rejected", !ok);
            AssertTrue("scope-violation-reason", err != null && err.Contains("outside allowed scope"));
            AssertEqual("scope-violation-no-write", File.ReadAllText(Path.Combine(repoDir, "fileA.txt")), "line1\n");
        }
        finally { TryDelete(tempDir); }
    }

    // ── test 4 ──
    private static void TestRepoRejectsFreeFormShell()
    {
        Console.WriteLine("--- Repo rejects free-form shell (non-patch) ---");
        var (tempDir, repoDir, evidenceDir) = NewRepoDirs("repo-shell");
        try
        {
            InitRepo(repoDir);
            var handler = new RepoApplyPatchHandler(repoDir, evidenceDir);
            // payload that is a command, not a patch
            var (ok, _, err) = Exec(handler, Payload(new { patch = "rm -rf / ; echo pwned" }), "{}");

            AssertTrue("shell-rejected", !ok);
            AssertTrue("shell-reason", err != null && err.Contains("not a valid unified diff"));
        }
        finally { TryDelete(tempDir); }
    }

    // ── test 5 ──
    private static void TestRepoOnlyTouchesPatchFiles()
    {
        Console.WriteLine("--- Repo only modifies files in the patch ---");
        var (tempDir, repoDir, evidenceDir) = NewRepoDirs("repo-isolated");
        try
        {
            InitRepo(repoDir, extraFiles: new() { ["fileB.txt"] = "keep\n" });
            var patch = MakePatch(repoDir, "fileA.txt", "line1\nline2\n");

            var handler = new RepoApplyPatchHandler(repoDir, evidenceDir);
            var (ok, _, _) = Exec(handler, Payload(new { patch }), "{}");

            AssertTrue("isolated-apply-success", ok);
            AssertEqual("isolated-fileA-changed", File.ReadAllText(Path.Combine(repoDir, "fileA.txt")), "line1\nline2\n");
            AssertEqual("isolated-fileB-untouched", File.ReadAllText(Path.Combine(repoDir, "fileB.txt")), "keep\n");
        }
        finally { TryDelete(tempDir); }
    }

    // ── test 6 ──
    private static void TestRepoIdempotencyReplayDoesNotReapply()
    {
        Console.WriteLine("--- Repo idempotency: replay returns cached, no double-apply ---");
        var (tempDir, repoDir, evidenceDir) = NewRepoDirs("repo-idem");
        try
        {
            InitRepo(repoDir);
            var patch = MakePatch(repoDir, "fileA.txt", "line1\nline2\n");

            var handler = new RepoApplyPatchHandler(repoDir, evidenceDir);
            var p = Payload(new { patch, idempotency_key = "idem-key-1" });

            var first = Exec(handler, p, "{}");
            var second = Exec(handler, p, "{}");

            AssertTrue("idem-first-success", first.Item1);
            AssertTrue("idem-second-success", second.Item1);
            AssertEqual("idem-result-cached-equal", second.Item2, first.Item2);
            AssertEqual("idem-not-double-applied", File.ReadAllText(Path.Combine(repoDir, "fileA.txt")), "line1\nline2\n");
        }
        finally { TryDelete(tempDir); }
    }

    // ── test 7 ──
    private static void TestBuildRunsWhitelistedCommand()
    {
        Console.WriteLine("--- Build runs whitelisted command ---");
        var (tempDir, workDir, evidenceDir) = NewRepoDirs("build-ok");
        try
        {
            var handler = new BuildTestRunHandler(workDir, new[] { "dotnet --version" }, evidenceDir);
            var (ok, result, err) = Exec(handler, Payload(new { command = "dotnet --version" }), "{}");

            AssertTrue("build-handler-success", ok);
            AssertTrue("build-no-error", err == null);
            if (result != null)
            {
                using var doc = JsonDocument.Parse(result);
                AssertTrue("build-exit-zero", doc.RootElement.GetProperty("exit_code").GetInt32() == 0);
                AssertTrue("build-success-flag", doc.RootElement.GetProperty("success").GetBoolean());
                var evidence = doc.RootElement.GetProperty("evidence_ref").GetString();
                AssertTrue("build-evidence-written", evidence != null && File.Exists(evidence));
            }
        }
        finally { TryDelete(tempDir); }
    }

    // ── test 8 ──
    private static void TestBuildRejectsNonWhitelisted()
    {
        Console.WriteLine("--- Build rejects non-whitelisted command ---");
        var (tempDir, workDir, evidenceDir) = NewRepoDirs("build-deny");
        try
        {
            var handler = new BuildTestRunHandler(workDir, new[] { "dotnet --version" }, evidenceDir);
            var (ok, _, err) = Exec(handler, Payload(new { command = "rm -rf /" }), "{}");

            AssertTrue("build-nonwhitelisted-rejected", !ok);
            AssertTrue("build-nonwhitelisted-reason", err != null && err.Contains("not whitelisted"));
        }
        finally { TryDelete(tempDir); }
    }

    // ── test 9 ──
    private static void TestBuildCapturesExitAndTruncates()
    {
        Console.WriteLine("--- Build captures exit code; runner truncates output ---");
        var (tempDir, workDir, evidenceDir) = NewRepoDirs("build-exit");
        try
        {
            // non-zero exit: unknown dotnet command → handler success (reported), command success=false
            var handler = new BuildTestRunHandler(workDir, new[] { "dotnet b4a-nonexistent-cmd" }, evidenceDir);
            var (ok, result, _) = Exec(handler, Payload(new { command = "dotnet b4a-nonexistent-cmd" }), "{}");

            AssertTrue("exit-handler-reports", ok);
            if (result != null)
            {
                using var doc = JsonDocument.Parse(result);
                AssertTrue("exit-nonzero", doc.RootElement.GetProperty("exit_code").GetInt32() != 0);
                AssertTrue("exit-success-false", !doc.RootElement.GetProperty("success").GetBoolean());
            }

            // direct truncation check on the runner
            var run = ProcessRunner.RunAsync("dotnet", new[] { "--version" }, workDir,
                TimeSpan.FromSeconds(30), maxOutputChars: 5).GetAwaiter().GetResult();
            AssertTrue("runner-truncates", run.Stdout.Contains("[truncated"));
        }
        finally { TryDelete(tempDir); }
    }

    // ── helpers ──

    private static (bool, string?, string?) Exec(ExecutionAdapterWorker.Handlers.RepoApplyPatchHandler h, string payload, string scope)
        => h.ExecuteAsync(Guid.NewGuid().ToString("N"), "execution.repo.apply_patch", payload, scope, CancellationToken.None).GetAwaiter().GetResult();

    private static (bool, string?, string?) Exec(BuildTestRunHandler h, string payload, string scope)
        => h.ExecuteAsync(Guid.NewGuid().ToString("N"), "execution.build_test.run", payload, scope, CancellationToken.None).GetAwaiter().GetResult();

    private static string Payload(object o) => JsonSerializer.Serialize(new { args = o });
    private static string Scope(object o) => JsonSerializer.Serialize(o);

    private static string NewTempDir(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"b4a-exec-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static (string temp, string repo, string evidence) NewRepoDirs(string label)
    {
        var temp = NewTempDir(label);
        var repo = Path.Combine(temp, "repo");
        var evidence = Path.Combine(temp, "evidence");
        Directory.CreateDirectory(repo);
        return (temp, repo, evidence);
    }

    private static void InitRepo(string dir, Dictionary<string, string>? extraFiles = null)
    {
        Git(dir, "init", "-q");
        Git(dir, "config", "user.email", "test@bricks4agent.local");
        Git(dir, "config", "user.name", "test");
        // pin LF so the working tree matches exactly on Windows (and the Linux adapter container)
        Git(dir, "config", "core.autocrlf", "false");
        Git(dir, "config", "core.eol", "lf");
        File.WriteAllText(Path.Combine(dir, "fileA.txt"), "line1\n");
        if (extraFiles != null)
            foreach (var (name, content) in extraFiles)
                File.WriteAllText(Path.Combine(dir, name), content);
        Git(dir, "add", "-A");
        Git(dir, "commit", "-q", "-m", "init");
    }

    /// <summary>edit a file to newContent, capture the diff, revert — returns an applicable patch.</summary>
    private static string MakePatch(string dir, string file, string newContent)
    {
        File.WriteAllText(Path.Combine(dir, file), newContent);
        var (_, patch, _) = Git(dir, "diff");
        Git(dir, "checkout", "--", ".");
        return patch;
    }

    private static (int ExitCode, string Stdout, string Stderr) Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, o, e);
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

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
