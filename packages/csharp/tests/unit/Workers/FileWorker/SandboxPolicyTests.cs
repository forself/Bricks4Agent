using System.Diagnostics;
using System.Text.Json;
using FileWorker;
using FileWorker.Handlers;

namespace Unit.Tests.Workers.FileWorker;

/// <summary>
/// SandboxPolicy：完整路徑邊界（含分隔字元）、symlink 解析，以及 read／list／search／write／delete
/// 一致套用的敏感路徑拒絕清單。
/// </summary>
public class SandboxPolicyTests : IDisposable
{
    private readonly string _parent;
    private readonly string _sandboxRoot;
    private readonly string _outside;

    public SandboxPolicyTests()
    {
        _parent = Path.Combine(Path.GetTempPath(), $"b4a-sandbox-policy-{Guid.NewGuid():N}");
        _sandboxRoot = Path.Combine(_parent, "sandbox");
        // 與 sandbox 同前綴的兄弟目錄：舊的 StartsWith（沒有分隔字元）會誤判為 sandbox 內。
        _outside = Path.Combine(_parent, "sandbox-evil");
        Directory.CreateDirectory(_sandboxRoot);
        Directory.CreateDirectory(_outside);

        File.WriteAllText(Path.Combine(_outside, "secret.txt"), "OUTSIDE_MARKER");
        File.WriteAllText(Path.Combine(_sandboxRoot, "README.md"), "readme MARKER_TEXT");
        File.WriteAllText(Path.Combine(_sandboxRoot, ".gitignore"), "bin/");
        File.WriteAllText(Path.Combine(_sandboxRoot, ".env"), "MARKER_TEXT=env");
        File.WriteAllText(Path.Combine(_sandboxRoot, "server.pem"), "MARKER_TEXT pem");
        Directory.CreateDirectory(Path.Combine(_sandboxRoot, ".git", "hooks"));
        File.WriteAllText(Path.Combine(_sandboxRoot, ".git", "config"), "MARKER_TEXT git");
        Directory.CreateDirectory(Path.Combine(_sandboxRoot, ".claude"));
        File.WriteAllText(Path.Combine(_sandboxRoot, ".claude", "settings.local.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_sandboxRoot, "src", "config"));
        File.WriteAllText(Path.Combine(_sandboxRoot, "src", "app.js"), "// MARKER_TEXT app");
        File.WriteAllText(Path.Combine(_sandboxRoot, "src", "config", "appsettings.Development.json"), "{\"MARKER_TEXT\":1}");
        File.WriteAllText(Path.Combine(_sandboxRoot, "src", "config", ".env.production"), "MARKER_TEXT=prod");
    }

    public void Dispose()
    {
        try { Directory.Delete(_parent, recursive: true); } catch { }
    }

    private static string Payload(object value) => JsonSerializer.Serialize(value);

    // ── 名稱判定 ──

    [Theory]
    [InlineData(".git", true)]
    [InlineData(".GIT", true)]
    [InlineData(".claude", true)]
    [InlineData(".codegraph-cache", true)]
    [InlineData(".ssh", true)]
    [InlineData(".run", true)]
    [InlineData(".RUN", true)]
    [InlineData(".env", true)]
    [InlineData(".env.local", true)]
    [InlineData(".env.production", true)]
    [InlineData("appsettings.Development.json", true)]
    [InlineData("APPSETTINGS.DEVELOPMENT.JSON", true)]
    [InlineData("appsettings.Production.json", true)]
    [InlineData("agent-stack.env", true)]
    [InlineData("server.pem", true)]
    [InlineData("app.key", true)]
    [InlineData("cert.pfx", true)]
    [InlineData("bundle.p12", true)]
    [InlineData("id_rsa", true)]
    [InlineData("id_rsa.pub", true)]
    [InlineData("id_ed25519", true)]
    [InlineData("client_secret_123.json", true)]
    [InlineData("Api.txt", true)]
    [InlineData("API.TXT", true)]
    [InlineData("ngrok_recovery_codes.txt", true)]
    [InlineData("broker.db", true)]
    [InlineData("broker.db-wal", true)]
    [InlineData("broker.db-shm", true)]
    [InlineData("broker.db-journal", true)]
    [InlineData("sample-project-0123456789ab.json", true)]
    [InlineData("notes:part", true)]
    [InlineData(".gitignore", false)]
    [InlineData(".gitattributes", false)]
    [InlineData(".github", false)]
    [InlineData("README.md", false)]
    [InlineData("appsettings.json", false)]
    [InlineData("appsettings.Production.example.json", false)]
    [InlineData("run", false)]
    [InlineData(".runner", false)]
    [InlineData("environment.ts", false)]
    [InlineData("monkey", false)]
    [InlineData("keys.json", false)]
    [InlineData("package-lock.json", false)]
    [InlineData("api-notes.txt", false)]
    [InlineData("dbcontext.cs", false)]
    [InlineData("schema.dbml", false)]
    [InlineData("sample-project-settings.json", false)]
    public void IsSensitiveName_MatchesDenyList(string name, bool expected)
    {
        SandboxPolicy.IsSensitiveName(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("packages/csharp/workers/line-worker/appsettings.json", true)]
    [InlineData("workers/line-worker/appsettings.json", true)]
    [InlineData(@"packages\csharp\workers\line-worker\APPSETTINGS.JSON", true)]
    [InlineData("packages/csharp/broker/appsettings.json", false)]
    [InlineData("packages/csharp/workers/line-worker/appsettings.example.json", false)]
    [InlineData("appsettings.json", false)]
    [InlineData("data/broker.db", true)]
    [InlineData(".run/line-sidecar/broker/appsettings.Production.json", true)]
    [InlineData(@".run\line-sidecar\broker\appsettings.Production.json", true)]
    [InlineData(".run/line-sidecar/logs/broker.log", true)]
    [InlineData("deploy/appsettings.Production.json", true)]
    [InlineData("src/app.js", false)]
    [InlineData(".", false)]
    public void IsSensitiveRelativePath_CoversLocationSpecificFiles(string relativePath, bool expected)
    {
        SandboxPolicy.IsSensitiveRelativePath(relativePath).Should().Be(expected);
    }

    /// <summary>
    /// 每個列在 .gitignore「Secrets」區段的模式（以及 SQLite 資料庫與本機密鑰設定檔），拒絕清單都要涵蓋。
    /// 在 .gitignore 新增密鑰類項目而沒有同步拒絕清單時，這個測試會失敗。
    /// </summary>
    [Fact]
    public void GitignoreSecretEntries_AreAllCoveredByTheDenyList()
    {
        var lines = File.ReadAllLines(FindRepositoryFile(".gitignore"));

        var secrets = SectionEntries(lines, "# Secrets");
        secrets.Should().NotBeEmpty("the .gitignore Secrets section must exist");
        var databases = SectionEntries(lines, "# SQLite databases").Where(entry => entry.StartsWith("*.db", StringComparison.Ordinal)).ToList();
        databases.Should().NotBeEmpty("the .gitignore SQLite section must exist");
        var localSecrets = new[]
        {
            "ngrok_recovery_codes.txt",
            "packages/csharp/workers/line-worker/appsettings.json",
            "packages/csharp/broker/appsettings.Development.json",
            // 本機執行期狀態：本機啟動腳本在這裡寫入執行期的設定覆寫檔。
            ".run/",
        };
        lines.Select(line => line.Trim()).Should().Contain(localSecrets);

        foreach (var entry in secrets.Concat(databases).Concat(localSecrets))
        {
            var sample = entry.StartsWith("**/", StringComparison.Ordinal) ? entry[3..] : entry;
            sample = sample.Replace("*", "sample", StringComparison.Ordinal).TrimEnd('/');
            SandboxPolicy.IsSensitiveRelativePath(sample).Should().BeTrue($".gitignore entry '{entry}' (checked as '{sample}') must be refused by the file-worker deny list");
            new SandboxPolicy(_sandboxRoot).Resolve(sample).Error.Should().Be(SandboxPolicy.BlockedPathError, $"'{sample}' must be blocked");
        }
    }

    [Fact]
    public async Task LocationSpecificSecretFile_IsBlockedAndHiddenOnlyAtItsLocation()
    {
        var lineWorker = Path.Combine(_sandboxRoot, "packages", "csharp", "workers", "line-worker");
        Directory.CreateDirectory(lineWorker);
        File.WriteAllText(Path.Combine(lineWorker, "appsettings.json"), "{\"MARKER_TEXT\":\"line\"}");
        File.WriteAllText(Path.Combine(lineWorker, "README.md"), "line worker");
        File.WriteAllText(Path.Combine(_sandboxRoot, "src", "appsettings.json"), "{\"name\":\"app\"}");

        var policy = new SandboxPolicy(_sandboxRoot);
        policy.Resolve("packages/csharp/workers/line-worker/appsettings.json").Error.Should().Be(SandboxPolicy.BlockedPathError);
        policy.Resolve("src/appsettings.json").Error.Should().BeNull("only that one location is refused, not every appsettings.json");

        var list = await new ListDirHandler(_sandboxRoot)
            .ExecuteAsync("x1", "file.list", Payload(new { path = "packages/csharp/workers/line-worker" }), "", default);
        ListedNames(list.ResultPayload!).Should().Equal("README.md");

        var names = await new SearchFilesHandler(_sandboxRoot)
            .ExecuteAsync("x2", "file.search_name", Payload(new { pattern = "appsettings.json" }), "", default);
        Matches(names.ResultPayload!).Should().Equal("src/appsettings.json");

        var content = await new SearchContentHandler(_sandboxRoot)
            .ExecuteAsync("x3", "file.search_content", Payload(new { pattern = "MARKER_TEXT", file_pattern = "*.json" }), "", default);
        content.ResultPayload.Should().NotContain("line-worker");
    }

    /// <summary>
    /// 本機執行期狀態目錄（.run，gitignore）：本機啟動腳本在裡面寫入執行期的設定覆寫檔。
    /// 整個目錄對 read、list、search、write 一律拒絕或略過。
    /// </summary>
    [Fact]
    public async Task RuntimeStateDirectory_IsBlockedAndHiddenEverywhere()
    {
        var brokerState = Path.Combine(_sandboxRoot, ".run", "line-sidecar", "broker");
        Directory.CreateDirectory(brokerState);
        const string overridePath = ".run/line-sidecar/broker/appsettings.Production.json";
        File.WriteAllText(Path.Combine(brokerState, "appsettings.Production.json"), "{\"MARKER_TEXT\":\"override\"}");
        File.WriteAllText(Path.Combine(brokerState, "notes.txt"), "MARKER_TEXT notes");

        var policy = new SandboxPolicy(_sandboxRoot);
        SandboxPolicy.IsSensitiveRelativePath(overridePath).Should().BeTrue();
        policy.Resolve(overridePath).Error.Should().Be(SandboxPolicy.BlockedPathError);
        policy.Resolve(".run/line-sidecar/broker/notes.txt").Error.Should().Be(SandboxPolicy.BlockedPathError);
        policy.Resolve("src/../.run/line-sidecar/broker/appsettings.Production.json").Error.Should().Be(SandboxPolicy.BlockedPathError);

        var read = await new ReadFileHandler(_sandboxRoot)
            .ExecuteAsync("q1", "file.read", Payload(new { path = overridePath }), "", default);
        read.Success.Should().BeFalse();
        read.Error.Should().Be(SandboxPolicy.BlockedPathError);
        read.ResultPayload.Should().BeNull();

        var root = await new ListDirHandler(_sandboxRoot)
            .ExecuteAsync("q2", "file.list", Payload(new { path = "." }), "", default);
        root.Success.Should().BeTrue();
        ListedNames(root.ResultPayload!).Should().NotContain(".run");
        var inside = await new ListDirHandler(_sandboxRoot)
            .ExecuteAsync("q3", "file.list", Payload(new { path = ".run/line-sidecar" }), "", default);
        inside.Error.Should().Be(SandboxPolicy.BlockedPathError);

        var names = await new SearchFilesHandler(_sandboxRoot)
            .ExecuteAsync("q4", "file.search_name", Payload(new { pattern = "*" }), "", default);
        names.Success.Should().BeTrue();
        Matches(names.ResultPayload!).Should().NotContain(match => match.StartsWith(".run/", StringComparison.Ordinal));
        var byName = await new SearchFilesHandler(_sandboxRoot)
            .ExecuteAsync("q5", "file.search_name", Payload(new { pattern = "appsettings.Production.json" }), "", default);
        Matches(byName.ResultPayload!).Should().BeEmpty();

        var content = await new SearchContentHandler(_sandboxRoot)
            .ExecuteAsync("q6", "file.search_content", Payload(new { pattern = "MARKER_TEXT" }), "", default);
        content.Success.Should().BeTrue();
        content.ResultPayload.Should().NotContain(".run");
        var contentInside = await new SearchContentHandler(_sandboxRoot)
            .ExecuteAsync("q7", "file.search_content", Payload(new { pattern = "MARKER_TEXT", directory = ".run" }), "", default);
        contentInside.Error.Should().Be(SandboxPolicy.BlockedPathError);

        var write = await new WriteFileHandler(_sandboxRoot)
            .ExecuteAsync("q8", "file.write", Payload(new { path = overridePath, content = "{}" }), "", default);
        write.Error.Should().Be(SandboxPolicy.BlockedPathError);
        File.ReadAllText(Path.Combine(brokerState, "appsettings.Production.json")).Should().Contain("override");
    }

    // ── 路徑段含冒號 ──

    [Theory]
    [InlineData("README.md:part")]
    [InlineData("src:alt/app.js")]
    [InlineData("src/app.js:part")]
    [InlineData(@"src\app.js:part")]
    [InlineData("notes/part:1.txt")]
    public void Resolve_SegmentWithColon_IsRejected(string path)
    {
        var (fullPath, error) = new SandboxPolicy(_sandboxRoot).Resolve(path);

        fullPath.Should().BeNull();
        error.Should().Be(SandboxPolicy.BlockedPathError);
    }

    [Fact]
    public void Resolve_FullyQualifiedPathInsideTheSandbox_IsStillAllowed()
    {
        var policy = new SandboxPolicy(_sandboxRoot);

        policy.Resolve(Path.Combine(_sandboxRoot, "README.md")).FullPath.Should().Be(Path.Combine(_sandboxRoot, "README.md"));
    }

    /// <summary>Windows（NTFS）上，路徑段含冒號的讀與寫都被拒絕，不論目標是一般檔案或拒絕清單中的檔案與目錄。</summary>
    [Fact]
    public async Task ReadAndWrite_WithColonInASegment_AreRejectedOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var reader = new ReadFileHandler(_sandboxRoot);
        var writer = new WriteFileHandler(_sandboxRoot);
        var envBefore = File.ReadAllText(Path.Combine(_sandboxRoot, ".env"));

        foreach (var path in new[] { "README.md:part", ".env:part", "server.pem:part", ".git:part/config", "src/config/appsettings.Development.json:part" })
        {
            var read = await reader.ExecuteAsync("n1", "file.read", Payload(new { path }), "", default);
            read.Success.Should().BeFalse($"reading '{path}' must be refused");
            read.Error.Should().Be(SandboxPolicy.BlockedPathError);
            read.ResultPayload.Should().BeNull();

            var write = await writer.ExecuteAsync("n2", "file.write", Payload(new { path, content = "x" }), "", default);
            write.Success.Should().BeFalse($"writing '{path}' must be refused");
            write.Error.Should().Be(SandboxPolicy.BlockedPathError);
        }

        File.ReadAllText(Path.Combine(_sandboxRoot, ".env")).Should().Be(envBefore);
        File.ReadAllText(Path.Combine(_sandboxRoot, "README.md")).Should().Be("readme MARKER_TEXT");
    }

    // ── 代理工具與能力 schema 的參數（pattern、directory、file_pattern）──

    [Fact]
    public async Task Search_AcceptsTheAgentToolArguments()
    {
        File.WriteAllText(Path.Combine(_sandboxRoot, "src", "config", "settings.js"), "// MARKER_TEXT settings");

        var byName = await new SearchFilesHandler(_sandboxRoot).ExecuteAsync(
            "a1", "file.search_name",
            Payload(new { route = "search_files", args = new { pattern = "*.js", directory = "src/config" } }), "", default);
        byName.Success.Should().BeTrue(byName.Error);
        Matches(byName.ResultPayload!).Should().Equal("settings.js");

        var byContent = await new SearchContentHandler(_sandboxRoot).ExecuteAsync(
            "a2", "file.search_content",
            Payload(new { route = "search_content", args = new { pattern = "MARKER_TEXT", directory = "src", file_pattern = "*.js" } }), "", default);
        byContent.Success.Should().BeTrue(byContent.Error);
        using (var doc = JsonDocument.Parse(byContent.ResultPayload!))
        {
            doc.RootElement.GetProperty("query").GetString().Should().Be("MARKER_TEXT");
            doc.RootElement.GetProperty("basePath").GetString().Should().Be("src");
            doc.RootElement.GetProperty("matches").EnumerateArray()
                .Select(m => m.GetProperty("file").GetString()!).OrderBy(f => f, StringComparer.Ordinal)
                .Should().Equal("app.js", "config/settings.js");
        }

        // Without a pattern, a name search lists every (non-sensitive) file.
        var noPattern = await new SearchFilesHandler(_sandboxRoot).ExecuteAsync(
            "a3", "file.search_name", Payload(new { args = new { directory = "src" } }), "", default);
        noPattern.Success.Should().BeTrue(noPattern.Error);
        Matches(noPattern.ResultPayload!).Should().Contain(new[] { "app.js", "config/settings.js" });

        // directory goes through the same sandbox checks as path.
        var outside = await new SearchFilesHandler(_sandboxRoot).ExecuteAsync(
            "a4", "file.search_name", Payload(new { args = new { pattern = "*", directory = "../sandbox-evil" } }), "", default);
        outside.Error.Should().Be(SandboxPolicy.OutsideSandboxError);
        var blocked = await new SearchContentHandler(_sandboxRoot).ExecuteAsync(
            "a5", "file.search_content", Payload(new { args = new { pattern = "MARKER_TEXT", directory = ".git" } }), "", default);
        blocked.Error.Should().Be(SandboxPolicy.BlockedPathError);
    }

    // ── 邊界 ──

    [Theory]
    [InlineData("../sandbox-evil/secret.txt")]
    [InlineData("sub/../../sandbox-evil/secret.txt")]
    [InlineData("..")]
    public void Resolve_SiblingWithSamePrefix_IsOutside(string path)
    {
        var (fullPath, error) = new SandboxPolicy(_sandboxRoot).Resolve(path);

        fullPath.Should().BeNull();
        error.Should().Be(SandboxPolicy.OutsideSandboxError);
    }

    [Fact]
    public void Resolve_RootTrailingSeparator_StillComparesWithSeparator()
    {
        var policy = new SandboxPolicy(_sandboxRoot + Path.DirectorySeparatorChar);

        policy.Resolve("../sandbox-evil/secret.txt").Error.Should().Be(SandboxPolicy.OutsideSandboxError);
        policy.Resolve("README.md").FullPath.Should().Be(Path.Combine(_sandboxRoot, "README.md"));
        policy.Resolve(".").FullPath.Should().Be(_sandboxRoot);
    }

    [Fact]
    public void Resolve_NulCharacter_IsRejected()
    {
        new SandboxPolicy(_sandboxRoot).Resolve("README.md\0.txt").FullPath.Should().BeNull();
    }

    [Theory]
    [InlineData(".git/config")]
    [InlineData(".GIT/config")]
    [InlineData(".git")]
    [InlineData(".claude/settings.local.json")]
    [InlineData(".env")]
    [InlineData("src/config/.env.production")]
    [InlineData("src/config/appsettings.Development.json")]
    [InlineData("server.pem")]
    [InlineData(".codegraph-cache/index.bin")]
    [InlineData("src/../.git/hooks/pre-commit")]
    public void Resolve_SensitivePath_IsBlocked(string path)
    {
        var (fullPath, error) = new SandboxPolicy(_sandboxRoot).Resolve(path);

        fullPath.Should().BeNull();
        error.Should().Be(SandboxPolicy.BlockedPathError);
    }

    // ── 每個 handler 一致套用 ──

    [Fact]
    public async Task ReadFile_SensitiveAndSibling_AreRejected_NormalFileWorks()
    {
        var handler = new ReadFileHandler(_sandboxRoot);

        var blocked = await handler.ExecuteAsync("r1", "file.read", Payload(new { path = ".git/config" }), "", default);
        blocked.Success.Should().BeFalse();
        blocked.Error.Should().Be(SandboxPolicy.BlockedPathError);

        var sibling = await handler.ExecuteAsync("r2", "file.read", Payload(new { path = "../sandbox-evil/secret.txt" }), "", default);
        sibling.Success.Should().BeFalse();
        sibling.Error.Should().Contain("outside sandbox");

        var ok = await handler.ExecuteAsync("r3", "file.read", Payload(new { args = new { path = "README.md" } }), "", default);
        ok.Success.Should().BeTrue();
        ok.ResultPayload.Should().Contain("readme MARKER_TEXT");
    }

    [Fact]
    public async Task ListDirectory_HidesSensitiveEntries_AndBlocksSensitiveDirectories()
    {
        var handler = new ListDirHandler(_sandboxRoot);

        var root = await handler.ExecuteAsync("l1", "file.list", Payload(new { path = "." }), "", default);
        root.Success.Should().BeTrue();
        var names = ListedNames(root.ResultPayload!);
        names.Should().Contain(new[] { "README.md", ".gitignore", "src" });
        names.Should().NotContain(new[] { ".git", ".claude", ".env", "server.pem" });

        var config = await handler.ExecuteAsync("l2", "file.list", Payload(new { path = "src/config" }), "", default);
        ListedNames(config.ResultPayload!).Should().BeEmpty();

        var git = await handler.ExecuteAsync("l3", "file.list", Payload(new { path = ".git" }), "", default);
        git.Success.Should().BeFalse();
        git.Error.Should().Be(SandboxPolicy.BlockedPathError);

        var sibling = await handler.ExecuteAsync("l4", "file.list", Payload(new { path = "../sandbox-evil" }), "", default);
        sibling.Error.Should().Be(SandboxPolicy.OutsideSandboxError);
    }

    [Fact]
    public async Task SearchName_SkipsSensitiveFilesAndDirectories()
    {
        var handler = new SearchFilesHandler(_sandboxRoot);

        var all = await handler.ExecuteAsync("s1", "file.search_name", Payload(new { pattern = "*" }), "", default);
        all.Success.Should().BeTrue();
        var matches = Matches(all.ResultPayload!);
        matches.Should().Contain(new[] { "README.md", "src/app.js", ".gitignore" });
        matches.Should().NotContain(m => m.StartsWith(".git/") || m.StartsWith(".claude/") || m == ".env"
            || m.EndsWith(".pem") || m.EndsWith("appsettings.Development.json") || m.EndsWith(".env.production"));

        var envOnly = await handler.ExecuteAsync("s2", "file.search_name", Payload(new { pattern = ".env*" }), "", default);
        Matches(envOnly.ResultPayload!).Should().BeEmpty();

        var insideGit = await handler.ExecuteAsync("s3", "file.search_name", Payload(new { pattern = "*", path = ".git" }), "", default);
        insideGit.Error.Should().Be(SandboxPolicy.BlockedPathError);
    }

    [Fact]
    public async Task SearchContent_OnlyReturnsNonSensitiveFiles()
    {
        var handler = new SearchContentHandler(_sandboxRoot);

        var result = await handler.ExecuteAsync("c1", "file.search_content", Payload(new { query = "MARKER_TEXT" }), "", default);
        result.Success.Should().BeTrue();
        using var doc = JsonDocument.Parse(result.ResultPayload!);
        var files = doc.RootElement.GetProperty("matches").EnumerateArray()
            .Select(m => m.GetProperty("file").GetString()!).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToList();
        files.Should().Equal("README.md", "src/app.js");

        var sibling = await handler.ExecuteAsync("c2", "file.search_content", Payload(new { query = "OUTSIDE", path = "../sandbox-evil" }), "", default);
        sibling.Error.Should().Be(SandboxPolicy.OutsideSandboxError);
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("*.cs", true)]
    [InlineData("test-*.ts", true)]
    [InlineData("README.md", true)]
    [InlineData("", true)]
    [InlineData(null, false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("src/*.js", false)]
    [InlineData(@"src\*.js", false)]
    [InlineData("../sandbox-evil/*", false)]
    [InlineData(@"..\sandbox-evil\*", false)]
    [InlineData(".git/*", false)]
    [InlineData("C:*", false)]
    public void IsFileNamePattern_RejectsDirectoryParts(string? pattern, bool expected)
    {
        SandboxPolicy.IsFileNamePattern(pattern).Should().Be(expected);
    }

    [Fact]
    public void EnumerateFilesRecursive_RejectsPatternWithDirectoryPart()
    {
        var policy = new SandboxPolicy(_sandboxRoot);
        var act = () => policy.EnumerateFilesRecursive(policy.Root, ".git/*").ToList();
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("../sandbox-evil/*")]
    [InlineData(@"..\sandbox-evil\*")]
    [InlineData(".git/*")]
    [InlineData(@".git\config")]
    [InlineData("src/config/*")]
    public async Task Search_PatternWithDirectoryPart_IsRefused(string pattern)
    {
        var byName = await new SearchFilesHandler(_sandboxRoot)
            .ExecuteAsync("p1", "file.search_name", Payload(new { pattern }), "", default);
        byName.Success.Should().BeFalse();
        byName.Error.Should().Be(SandboxPolicy.InvalidPatternError);
        byName.ResultPayload.Should().BeNull();

        var byContent = await new SearchContentHandler(_sandboxRoot)
            .ExecuteAsync("p2", "file.search_content", Payload(new { query = "MARKER", file_pattern = pattern }), "", default);
        byContent.Success.Should().BeFalse();
        byContent.Error.Should().Be(SandboxPolicy.InvalidPatternError);
        byContent.ResultPayload.Should().BeNull();
    }

    [Fact]
    public async Task WriteAndDelete_ApplyTheSamePolicy()
    {
        var writer = new WriteFileHandler(_sandboxRoot);
        var deleter = new DeleteFileHandler(_sandboxRoot);

        var hook = await writer.ExecuteAsync("w1", "file.write", Payload(new { path = ".git/hooks/pre-commit", content = "x" }), "", default);
        hook.Success.Should().BeFalse();
        hook.Error.Should().Be(SandboxPolicy.BlockedPathError);
        File.Exists(Path.Combine(_sandboxRoot, ".git", "hooks", "pre-commit")).Should().BeFalse();

        var env = await writer.ExecuteAsync("w2", "file.write", Payload(new { path = "src/.env.local", content = "x" }), "", default);
        env.Error.Should().Be(SandboxPolicy.BlockedPathError);

        var sibling = await writer.ExecuteAsync("w3", "file.write", Payload(new { path = "../sandbox-evil/new.txt", content = "x" }), "", default);
        sibling.Error.Should().Be(SandboxPolicy.OutsideSandboxError);
        File.Exists(Path.Combine(_outside, "new.txt")).Should().BeFalse();

        var ok = await writer.ExecuteAsync("w4", "file.write", Payload(new { path = "notes/new.txt", content = "hello" }), "", default);
        ok.Success.Should().BeTrue();
        File.ReadAllText(Path.Combine(_sandboxRoot, "notes", "new.txt")).Should().Be("hello");

        var deleteEnv = await deleter.ExecuteAsync("d1", "file.delete", Payload(new { path = ".env" }), "", default);
        deleteEnv.Error.Should().Be(SandboxPolicy.BlockedPathError);
        File.Exists(Path.Combine(_sandboxRoot, ".env")).Should().BeTrue();

        var deleteOk = await deleter.ExecuteAsync("d2", "file.delete", Payload(new { path = "notes/new.txt" }), "", default);
        deleteOk.Success.Should().BeTrue();
    }

    // ── symlink／junction ──

    [Fact]
    public async Task DirectoryLink_PointingOutside_IsRejectedEverywhere()
    {
        var link = Path.Combine(_sandboxRoot, "escape");
        if (!TryCreateDirectoryLink(link, _outside))
            return; // 平台不允許建立連結時略過（CI 的 Linux 一定可以建立 symlink）。

        var policy = new SandboxPolicy(_sandboxRoot);
        policy.Resolve("escape/secret.txt").Error.Should().Be(SandboxPolicy.OutsideSandboxError);
        policy.Resolve("escape").Error.Should().Be(SandboxPolicy.OutsideSandboxError);
        policy.Resolve("escape/new.txt").Error.Should().Be(SandboxPolicy.OutsideSandboxError);

        var read = await new ReadFileHandler(_sandboxRoot)
            .ExecuteAsync("k1", "file.read", Payload(new { path = "escape/secret.txt" }), "", default);
        read.Success.Should().BeFalse();
        read.Error.Should().Contain("outside sandbox");

        var write = await new WriteFileHandler(_sandboxRoot)
            .ExecuteAsync("k2", "file.write", Payload(new { path = "escape/new.txt", content = "x" }), "", default);
        write.Success.Should().BeFalse();
        File.Exists(Path.Combine(_outside, "new.txt")).Should().BeFalse();

        var list = await new ListDirHandler(_sandboxRoot)
            .ExecuteAsync("k3", "file.list", Payload(new { path = "." }), "", default);
        ListedNames(list.ResultPayload!).Should().NotContain("escape");

        var search = await new SearchContentHandler(_sandboxRoot)
            .ExecuteAsync("k4", "file.search_content", Payload(new { query = "OUTSIDE_MARKER" }), "", default);
        search.Success.Should().BeTrue();
        using (var doc = JsonDocument.Parse(search.ResultPayload!))
            doc.RootElement.GetProperty("matches").GetArrayLength().Should().Be(0);

        var names = await new SearchFilesHandler(_sandboxRoot)
            .ExecuteAsync("k5", "file.search_name", Payload(new { pattern = "secret.txt" }), "", default);
        Matches(names.ResultPayload!).Should().BeEmpty();
    }

    [Fact]
    public void FileLink_ToSensitiveFileInsideSandbox_IsBlocked()
    {
        var link = Path.Combine(_sandboxRoot, "notes.txt");
        if (!TryCreateFileLink(link, Path.Combine(_sandboxRoot, ".env")))
            return;

        new SandboxPolicy(_sandboxRoot).Resolve("notes.txt").Error.Should().Be(SandboxPolicy.BlockedPathError);
    }

    [Fact]
    public void FileLink_PointingOutside_IsRejected()
    {
        var link = Path.Combine(_sandboxRoot, "outside-link.txt");
        if (!TryCreateFileLink(link, Path.Combine(_outside, "secret.txt")))
            return;

        new SandboxPolicy(_sandboxRoot).Resolve("outside-link.txt").Error.Should().Be(SandboxPolicy.OutsideSandboxError);
    }

    [Fact]
    public void Link_InsideSandbox_ToNormalFile_IsAllowed()
    {
        var link = Path.Combine(_sandboxRoot, "readme-link.md");
        if (!TryCreateFileLink(link, Path.Combine(_sandboxRoot, "README.md")))
            return;

        new SandboxPolicy(_sandboxRoot).Resolve("readme-link.md").FullPath.Should().Be(link);
    }

    /// <summary>從測試輸出目錄往上找 repo 根目錄中的檔案。</summary>
    private static string FindRepositoryFile(string name)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, name);
            if (File.Exists(candidate) && Directory.Exists(Path.Combine(directory.FullName, "packages", "csharp")))
                return candidate;
        }

        throw new FileNotFoundException($"Could not find {name} at the repository root above {AppContext.BaseDirectory}.");
    }

    /// <summary>某個註解標題之後、下一個空行之前的項目（略過註解）。</summary>
    private static List<string> SectionEntries(string[] lines, string header)
    {
        var start = Array.FindIndex(lines, line => line.Trim().StartsWith(header, StringComparison.Ordinal));
        if (start < 0)
            return new List<string>();

        var entries = new List<string>();
        for (var index = start + 1; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0)
                break;
            if (!line.StartsWith('#'))
                entries.Add(line);
        }

        return entries;
    }

    private static List<string> ListedNames(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("name").GetString()!).ToList();
    }

    private static List<string> Matches(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty("matches").EnumerateArray().Select(e => e.GetString()!).ToList();
    }

    private static bool TryCreateFileLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows())
                return false;
        }

        // Windows 沒有開發人員模式時不能建立 symlink；改用不需權限的 junction。
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                ArgumentList = { "/c", "mklink", "/J", link, target },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            process!.WaitForExit(10_000);
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch
        {
            return false;
        }
    }
}
