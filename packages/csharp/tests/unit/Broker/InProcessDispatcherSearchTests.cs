using System.Text.Json;
using Broker.Adapters;
using BrokerCore.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Broker;

/// <summary>
/// The in-process file routes (used when no file worker answers) keep to the same rules as the file worker:
/// a search pattern or file_pattern may only match file names (the folder comes from directory and goes
/// through the sandbox check), every result stays inside the sandbox root, and the root is compared
/// together with the separator, so a sibling folder whose name starts with the root's name is outside.
/// </summary>
public sealed class InProcessDispatcherSearchTests : IDisposable
{
    private readonly string _parent;
    private readonly string _sandboxRoot;
    private readonly InProcessDispatcher _dispatcher;

    public InProcessDispatcherSearchTests()
    {
        _parent = Path.Combine(Path.GetTempPath(), $"b4a-inprocess-search-{Guid.NewGuid():N}");
        _sandboxRoot = Path.Combine(_parent, "sandbox");
        var sibling = Path.Combine(_parent, "sandbox-evil");
        Directory.CreateDirectory(Path.Combine(_sandboxRoot, "sub"));
        Directory.CreateDirectory(sibling);

        File.WriteAllText(Path.Combine(_sandboxRoot, "a.txt"), "MARKER inside a");
        File.WriteAllText(Path.Combine(_sandboxRoot, "sub", "b.txt"), "MARKER inside b");
        File.WriteAllText(Path.Combine(sibling, "secret.txt"), "MARKER outside");

        _dispatcher = new InProcessDispatcher(NullLogger<InProcessDispatcher>.Instance, _sandboxRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_parent, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("../sandbox-evil/*")]
    [InlineData(@"..\sandbox-evil\*")]
    [InlineData("sub/*.txt")]
    [InlineData(@"sub\*.txt")]
    [InlineData("C:*")]
    [InlineData("..")]
    [InlineData(".")]
    public async Task SearchFiles_PatternWithDirectoryPart_IsRefused(string pattern)
    {
        var result = await DispatchAsync("search_files", new { pattern });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(InProcessDispatcher.InvalidSearchPatternError);
        result.ResultPayload.Should().BeNull();
    }

    [Theory]
    [InlineData("../sandbox-evil/*")]
    [InlineData(@"..\sandbox-evil\*")]
    [InlineData("sub/*.txt")]
    [InlineData("C:*")]
    public async Task SearchContent_FilePatternWithDirectoryPart_IsRefused(string filePattern)
    {
        var result = await DispatchAsync("search_content", new { pattern = "MARKER", file_pattern = filePattern });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be(InProcessDispatcher.InvalidSearchPatternError);
        result.ResultPayload.Should().BeNull();
    }

    [Fact]
    public async Task Search_WithFileNamePatterns_ReturnsOnlyFilesInsideTheSandbox()
    {
        var byName = await DispatchAsync("search_files", new { pattern = "*.txt" });
        byName.Success.Should().BeTrue(byName.ErrorMessage);
        Strings(byName.ResultPayload!, "matches").OrderBy(m => m, StringComparer.Ordinal).Should().Equal("a.txt", "sub/b.txt");

        var inFolder = await DispatchAsync("search_files", new { pattern = "*.txt", directory = "sub" });
        Strings(inFolder.ResultPayload!, "matches").Should().Equal("b.txt");

        var byContent = await DispatchAsync("search_content", new { pattern = "MARKER", file_pattern = "*.txt" });
        byContent.Success.Should().BeTrue(byContent.ErrorMessage);
        byContent.ResultPayload.Should().NotContain("outside");
        using var doc = JsonDocument.Parse(byContent.ResultPayload!);
        doc.RootElement.GetProperty("matches").EnumerateArray()
            .Select(m => m.GetProperty("file").GetString()!).OrderBy(f => f, StringComparer.Ordinal)
            .Should().Equal("a.txt", "sub/b.txt");
    }

    [Theory]
    [InlineData("search_files")]
    [InlineData("search_content")]
    [InlineData("list_directory")]
    public async Task SiblingFolderWithTheSamePrefix_IsOutsideTheSandbox(string route)
    {
        var result = await DispatchAsync(route, new { pattern = "MARKER", directory = "../sandbox-evil" });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("Path outside sandbox.");
    }

    [Fact]
    public async Task ReadFile_InSiblingFolderWithTheSamePrefix_IsOutsideTheSandbox()
    {
        var outside = await DispatchAsync("read_file", new { path = "../sandbox-evil/secret.txt" });
        outside.Success.Should().BeFalse();
        outside.ErrorMessage.Should().Be("Path outside sandbox.");

        var inside = await DispatchAsync("read_file", new { path = "sub/b.txt" });
        inside.Success.Should().BeTrue(inside.ErrorMessage);
        inside.ResultPayload.Should().Contain("MARKER inside b");
    }

    private Task<ExecutionResult> DispatchAsync(string route, object args)
        => _dispatcher.DispatchAsync(new ApprovedRequest
        {
            RequestId = $"req_{Guid.NewGuid():N}",
            CapabilityId = "file.test",
            Route = route,
            Payload = JsonSerializer.Serialize(new { route, args }),
            Scope = "{}",
            TraceId = "trace_inprocess_search"
        });

    private static List<string> Strings(string payload, string property)
    {
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToList();
    }
}
