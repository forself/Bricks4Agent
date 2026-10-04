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
    [InlineData(".env", true)]
    [InlineData(".env.local", true)]
    [InlineData(".env.production", true)]
    [InlineData("appsettings.Development.json", true)]
    [InlineData("APPSETTINGS.DEVELOPMENT.JSON", true)]
    [InlineData("agent-stack.env", true)]
    [InlineData("server.pem", true)]
    [InlineData("app.key", true)]
    [InlineData("cert.pfx", true)]
    [InlineData("bundle.p12", true)]
    [InlineData("id_rsa", true)]
    [InlineData("id_rsa.pub", true)]
    [InlineData("id_ed25519", true)]
    [InlineData("client_secret_123.json", true)]
    [InlineData(".gitignore", false)]
    [InlineData(".gitattributes", false)]
    [InlineData(".github", false)]
    [InlineData("README.md", false)]
    [InlineData("appsettings.json", false)]
    [InlineData("environment.ts", false)]
    [InlineData("monkey", false)]
    [InlineData("keys.json", false)]
    public void IsSensitiveName_MatchesDenyList(string name, bool expected)
    {
        SandboxPolicy.IsSensitiveName(name).Should().Be(expected);
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
