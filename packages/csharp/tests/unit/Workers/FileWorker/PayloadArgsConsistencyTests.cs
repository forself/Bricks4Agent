using System.Text.Json;
using BrokerCore.Services;
using FileWorker.Handlers;

namespace Unit.Tests.Workers.FileWorker;

/// <summary>
/// The broker's PolicyEngine and the file-worker handlers read the same payload (the broker forwards it as
/// submitted). Every path a handler acts on must be one the PolicyEngine checked: handlers take their
/// arguments from args, else tool_args, else the payload root (the PolicyEngine's order), and the
/// PolicyEngine checks the path keys of all three locations.
/// </summary>
public class PayloadArgsConsistencyTests : IDisposable
{
    private readonly string _sandboxRoot;

    public PayloadArgsConsistencyTests()
    {
        _sandboxRoot = Path.Combine(Path.GetTempPath(), $"b4a-payload-args-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_sandboxRoot, "docs"));
        Directory.CreateDirectory(Path.Combine(_sandboxRoot, "src"));
        File.WriteAllText(Path.Combine(_sandboxRoot, "docs", "a.txt"), "DOCS_MARKER");
        File.WriteAllText(Path.Combine(_sandboxRoot, "src", "b.txt"), "SRC_MARKER");
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandboxRoot, recursive: true); } catch { }
    }

    public static TheoryData<string> PayloadShapes => new()
    {
        """{"route":"read_file","args":{"path":"docs/a.txt"}}""",
        """{"route":"read_file","tool_args":{"path":"docs/a.txt"}}""",
        """{"route":"read_file","path":"docs/a.txt"}""",
        """{"route":"read_file","tool_args":{"path":"docs/a.txt"},"path":"src/b.txt"}""",
        """{"route":"read_file","args":{"path":"docs/a.txt"},"path":"src/b.txt"}""",
        """{"route":"read_file","args":{"path":"docs/a.txt"},"tool_args":{"path":"src/b.txt"},"path":"src/c.txt"}""",
        """{"route":"read_file","args":"docs/a.txt","path":"src/b.txt"}""",
        """{"route":"read_file","args":["docs/a.txt"],"tool_args":{"path":"src/b.txt"}}""",
        """{"route":"read_file","args":null,"tool_args":"x","path":"src/b.txt"}""",
        """{"route":"read_file","args":{},"path":"src/b.txt"}""",
        """{"route":"search_files","tool_args":{"directory":"docs"},"directory":"src","path":"src"}""",
        """{"route":"search_files","args":{"path":"docs"},"directory":"src"}""",
    };

    [Theory]
    [MemberData(nameof(PayloadShapes))]
    public void EveryPathAHandlerUses_IsAPathThePolicyEngineChecks(string payload)
    {
        var checkedPaths = PolicyEngine.ExtractRawRequestedPaths(payload);

        using var doc = JsonDocument.Parse(payload);
        var args = PayloadArgs.GetArgsElement(doc.RootElement);
        foreach (var used in new[]
                 {
                     PayloadArgs.GetString(args, "path"),                 // read, list, write, delete
                     PayloadArgs.GetString(args, "directory", "path"),    // search by name or content
                 })
        {
            if (used is null)
                continue;
            checkedPaths.Should().Contain(used, "the handler would act on '{0}' for payload {1}", used, payload);
        }
    }

    [Fact]
    public void ArgumentLocation_FollowsArgsThenToolArgsThenRoot()
    {
        PathFrom("""{"args":{"path":"a"},"tool_args":{"path":"b"},"path":"c"}""").Should().Be("a");
        PathFrom("""{"tool_args":{"path":"b"},"path":"c"}""").Should().Be("b");
        PathFrom("""{"path":"c"}""").Should().Be("c");
        PathFrom("""{"args":"a","tool_args":{"path":"b"},"path":"c"}""").Should().Be("b", "args that is not an object is skipped");
        PathFrom("""{"args":"a","tool_args":"b","path":"c"}""").Should().Be("c");
        PathFrom("""{"args":{},"path":"c"}""").Should().BeNull("an args object is used even when it has no path");
    }

    /// <summary>The handlers themselves use that order: with no args, tool_args wins over a root-level path.</summary>
    [Fact]
    public async Task ReadFileHandler_UsesToolArgsBeforeTheRoot()
    {
        var handler = new ReadFileHandler(_sandboxRoot);

        var fromToolArgs = await handler.ExecuteAsync(
            "c1", "file.read", """{"route":"read_file","tool_args":{"path":"docs/a.txt"},"path":"src/b.txt"}""", "", default);
        fromToolArgs.Success.Should().BeTrue(fromToolArgs.Error);
        fromToolArgs.ResultPayload.Should().Contain("DOCS_MARKER").And.NotContain("SRC_MARKER");

        var fromArgs = await handler.ExecuteAsync(
            "c2", "file.read", """{"route":"read_file","args":{"path":"docs/a.txt"},"tool_args":{"path":"src/b.txt"}}""", "", default);
        fromArgs.ResultPayload.Should().Contain("DOCS_MARKER").And.NotContain("SRC_MARKER");

        var nonObjectArgs = await handler.ExecuteAsync(
            "c3", "file.read", """{"route":"read_file","args":"docs/a.txt","path":"src/b.txt"}""", "", default);
        nonObjectArgs.Success.Should().BeTrue(nonObjectArgs.Error);
        nonObjectArgs.ResultPayload.Should().Contain("SRC_MARKER", "args that is not an object is skipped, as the PolicyEngine does");

        var search = await new SearchFilesHandler(_sandboxRoot).ExecuteAsync(
            "c4", "file.search_name", """{"route":"search_files","tool_args":{"pattern":"*.txt","directory":"docs"},"directory":"src"}""", "", default);
        search.Success.Should().BeTrue(search.Error);
        search.ResultPayload.Should().Contain("a.txt").And.NotContain("b.txt");
    }

    private static string? PathFrom(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return PayloadArgs.GetString(PayloadArgs.GetArgsElement(doc.RootElement), "path");
    }
}
