using System.Text.Json;
using FileWorker.Handlers;

namespace Unit.Tests.Workers.FileWorker;

/// <summary>
/// ReadFileHandler sandbox path validation tests.
///
/// ResolveSandboxedPath is private, so we test via ExecuteAsync behaviour:
/// - Legal paths within sandbox should succeed (or report file-not-found, not sandbox violation)
/// - ../ traversal should fail with "Path outside sandbox"
/// - Absolute paths outside sandbox should fail with "Path outside sandbox"
/// </summary>
public class SandboxPathTests : IDisposable
{
    private readonly string _sandboxRoot;
    private readonly ReadFileHandler _handler;

    public SandboxPathTests()
    {
        _sandboxRoot = Path.Combine(Path.GetTempPath(), $"sandbox_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sandboxRoot);
        _handler = new ReadFileHandler(_sandboxRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandboxRoot, recursive: true); } catch { }
    }

    private static string MakePayload(string path) =>
        JsonSerializer.Serialize(new { path });

    // ---- Legal path tests ----

    [Fact]
    public async Task LegalPath_ExistingFile_ReturnsSuccess()
    {
        // Arrange: create a real file in the sandbox
        var filePath = Path.Combine(_sandboxRoot, "hello.txt");
        await File.WriteAllTextAsync(filePath, "Hello, World!");

        // Act
        var (success, result, error) = await _handler.ExecuteAsync(
            "req-1", "file.read", MakePayload("hello.txt"), "test", CancellationToken.None);

        // Assert
        success.Should().BeTrue();
        error.Should().BeNull();
        result.Should().NotBeNull();

        using var doc = JsonDocument.Parse(result!);
        doc.RootElement.GetProperty("content").GetString().Should().Be("Hello, World!");
    }

    [Fact]
    public async Task LegalPath_NonExistentFile_ReturnsFileNotFound()
    {
        // A path that stays within the sandbox but doesn't exist
        var (success, _, error) = await _handler.ExecuteAsync(
            "req-2", "file.read", MakePayload("does_not_exist.txt"), "test", CancellationToken.None);

        success.Should().BeFalse();
        error.Should().Contain("File not found");
        // Key: it should NOT say "Path outside sandbox"
        error.Should().NotContain("outside sandbox");
    }

    [Fact]
    public async Task LegalPath_Subdirectory_ReturnsSuccess()
    {
        // Arrange
        var subDir = Path.Combine(_sandboxRoot, "sub");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "nested.txt"), "nested content");

        // Act
        var (success, result, error) = await _handler.ExecuteAsync(
            "req-3", "file.read", MakePayload("sub/nested.txt"), "test", CancellationToken.None);

        // Assert
        success.Should().BeTrue();
        error.Should().BeNull();
    }

    // ---- Path traversal tests ----

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("../../etc/shadow")]
    [InlineData("sub/../../etc/passwd")]
    [InlineData("sub/../../../etc/hosts")]
    public async Task TraversalPath_ReturnsOutsideSandbox(string maliciousPath)
    {
        var (success, result, error) = await _handler.ExecuteAsync(
            "req-t", "file.read", MakePayload(maliciousPath), "test", CancellationToken.None);

        success.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Contain("outside sandbox");
    }

    // ---- Absolute path outside sandbox tests ----

    [Fact]
    public async Task AbsolutePath_OutsideSandbox_ReturnsOutsideSandbox()
    {
        // Use a path that is definitely outside the sandbox
        string outsidePath;
        if (OperatingSystem.IsWindows())
            outsidePath = @"C:\Windows\System32\drivers\etc\hosts";
        else
            outsidePath = "/etc/passwd";

        var (success, result, error) = await _handler.ExecuteAsync(
            "req-a", "file.read", MakePayload(outsidePath), "test", CancellationToken.None);

        success.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Contain("outside sandbox");
    }

    [Fact]
    public async Task AbsolutePath_InsideSandbox_Succeeds()
    {
        // An absolute path that is inside the sandbox should work
        var filePath = Path.Combine(_sandboxRoot, "abs_test.txt");
        await File.WriteAllTextAsync(filePath, "absolute but legal");

        var (success, result, error) = await _handler.ExecuteAsync(
            "req-b", "file.read", MakePayload(filePath), "test", CancellationToken.None);

        // Absolute path inside sandbox: ResolveSandboxedPath combines _sandboxRoot + absolutePath
        // Path.Combine(_sandboxRoot, absolutePath) when absolutePath is rooted returns absolutePath itself.
        // Then Path.GetFullPath(absolutePath).StartsWith(_sandboxRoot) should still be true.
        // Let's check the actual behaviour and assert accordingly.
        if (success)
        {
            error.Should().BeNull();
        }
        else
        {
            // If it fails, it could be because Path.Combine with an absolute path
            // returns the absolute path directly, which starts with _sandboxRoot, so it should succeed.
            // If this fails, the test reveals a bug.
            success.Should().BeTrue("Absolute path inside sandbox should be allowed");
        }
    }

    // ---- Payload format edge cases ----

    [Fact]
    public async Task Payload_WithArgsWrapper_WorksCorrectly()
    {
        // The handler also accepts { args: { path: "..." } } format
        var filePath = Path.Combine(_sandboxRoot, "args_test.txt");
        await File.WriteAllTextAsync(filePath, "via args");

        var payload = JsonSerializer.Serialize(new { args = new { path = "args_test.txt" } });

        var (success, result, error) = await _handler.ExecuteAsync(
            "req-c", "file.read", payload, "test", CancellationToken.None);

        success.Should().BeTrue();
        error.Should().BeNull();
    }
}
