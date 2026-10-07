using System.Text;
using System.Text.Json.Nodes;
using GenerationWorker.Handlers;
using GenerationWorker.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Workers.Generation;

/// <summary>
/// validate 的結果超過 MaxResultBytes 時，worker 回結構化的截斷結果（Success=true、ok 照 CLI、前幾筆錯誤、
/// total_errors 與 truncated:true），代理仍拿得到可修正的資訊；catalog 超過上限仍是內部失敗。
/// grant scope 帶 max_pages 時，頁數超過也在 validate 回報（MAX_PAGES_EXCEEDED）。
/// </summary>
public sealed class DefinitionValidateTruncationTests : IDisposable
{
    private const string RequestId = "req_0000000000AA_00000000000000BB";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"b4a-gen-truncate-{Guid.NewGuid():N}");

    public DefinitionValidateTruncationTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>不啟動 node：直接回傳預先準備的 CLI 輸出。</summary>
    private sealed class CannedCli : IGeneratorCli
    {
        private readonly JsonObject _output;

        public CannedCli(JsonObject output) => _output = output;

        public Task<GeneratorCliResult> RunAsync(string command, JsonObject input, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult(GeneratorCliResult.FromOutput((JsonObject)_output.DeepClone(), _output.ToJsonString()));
    }

    private GenerationWorkerOptions Options(int maxResultBytes) => new()
    {
        ToolsRoot = _root,
        OutputRoot = Path.Combine(_root, "out"),
        MaxResultBytes = maxResultBytes
    };

    private static JsonObject ValidationOutput(int errorCount, int warningCount = 0)
    {
        var errors = new JsonArray();
        for (var i = 0; i < errorCount; i++)
        {
            errors.Add(new JsonObject
            {
                ["code"] = "UNKNOWN_KEY",
                ["path"] = $"definitions.pages[0].definition.fields[{i}].note{i}",
                ["message"] = $"Unknown key \"note{i}\".",
                ["hint"] = "Allowed keys: name, type, label, required, default, options, validation, component."
            });
        }

        var warnings = new JsonArray();
        for (var i = 0; i < warningCount; i++)
            warnings.Add(new JsonObject { ["code"] = "API_MISMATCH", ["path"] = $"definitions.pages[{i}].definition.api", ["message"] = "check", ["hint"] = "" });

        return new JsonObject
        {
            ["ok"] = false,
            ["errors"] = errors,
            ["warnings"] = warnings,
            ["pages"] = new JsonArray(new JsonObject { ["id"] = "items-form", ["type"] = "form", ["field_count"] = errorCount }),
            ["validation_digest"] = new string('d', 64),
            ["validator_version"] = "definition-validator/1.1.0"
        };
    }

    private static string ValidatePayload()
        => new JsonObject
        {
            ["route"] = DefinitionValidateHandler.Route,
            ["args"] = new JsonObject { ["template"] = new JsonObject { ["kind"] = "definition-template" } },
            ["project_root"] = "/workspace"
        }.ToJsonString();

    [Fact]
    public async Task OversizedValidation_ReturnsTheFirstErrors_AsASuccessfulCall()
    {
        var output = ValidationOutput(errorCount: 2000, warningCount: 100);
        var limit = 16 * 1024;
        Encoding.UTF8.GetByteCount(output.ToJsonString()).Should().BeGreaterThan(limit);
        var handler = new DefinitionValidateHandler(Options(limit), new CannedCli(output), NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route, ValidatePayload(), "{}", CancellationToken.None);

        success.Should().BeTrue(error);
        Encoding.UTF8.GetByteCount(payload!).Should().BeLessThanOrEqualTo(limit);
        var result = JsonNode.Parse(payload!)!.AsObject();
        result["ok"]!.GetValue<bool>().Should().BeFalse();
        result["truncated"]!.GetValue<bool>().Should().BeTrue();
        result["total_errors"]!.GetValue<int>().Should().Be(2000);
        result["total_warnings"]!.GetValue<int>().Should().Be(100);
        var errors = result["errors"]!.AsArray();
        errors.Count.Should().BeInRange(1, 50);
        errors[0]!["path"]!.GetValue<string>().Should().Be("definitions.pages[0].definition.fields[0].note0", "the first errors are kept in order");
        result["validation_digest"]!.GetValue<string>().Should().Be(new string('d', 64));
        result["pages"]!.AsArray().Should().ContainSingle();
    }

    [Fact]
    public async Task ValidationWithinTheLimit_IsReturnedUnchanged()
    {
        var output = ValidationOutput(errorCount: 3);
        var handler = new DefinitionValidateHandler(Options(256 * 1024), new CannedCli(output), NullLogger.Instance);

        var (success, payload, _) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route, ValidatePayload(), "{}", CancellationToken.None);

        success.Should().BeTrue();
        payload.Should().Be(output.ToJsonString());
    }

    private static JsonObject PassingOutput(int pageCount)
    {
        var pages = new JsonArray();
        for (var i = 0; i < pageCount; i++)
            pages.Add(new JsonObject { ["id"] = $"page-{i}", ["type"] = "form", ["field_count"] = 1 });
        return new JsonObject
        {
            ["ok"] = true,
            ["errors"] = new JsonArray(),
            ["warnings"] = new JsonArray(),
            ["pages"] = pages,
            ["validation_digest"] = new string('d', 64),
            ["validator_version"] = "definition-validator/1.1.0"
        };
    }

    [Fact]
    public async Task PagesOverTheScopeLimit_AreReportedByValidate()
    {
        var handler = new DefinitionValidateHandler(Options(256 * 1024), new CannedCli(PassingOutput(3)), NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route, ValidatePayload(),
            "{\"routes\":[\"validate_definition\"],\"max_pages\":2}", CancellationToken.None);

        success.Should().BeTrue(error);
        var result = JsonNode.Parse(payload!)!.AsObject();
        result["ok"]!.GetValue<bool>().Should().BeFalse();
        result["errors"]![0]!["code"]!.GetValue<string>().Should().Be("MAX_PAGES_EXCEEDED");
        result["errors"]![0]!["path"]!.GetValue<string>().Should().Be("definitions.pages");

        var (withinSuccess, withinPayload, _) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route, ValidatePayload(),
            "{\"routes\":[\"validate_definition\"],\"max_pages\":3}", CancellationToken.None);
        withinSuccess.Should().BeTrue();
        JsonNode.Parse(withinPayload!)!["ok"]!.GetValue<bool>().Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"routes\":[\"validate_definition\"],\"max_pages\":\"12\"}")]
    [InlineData("{\"routes\":[\"validate_definition\"],\"max_pages\":0}")]
    [InlineData("{\"routes\":[\"validate_definition\"],\"max_pages\":101}")]
    [InlineData("[]")]
    public async Task InvalidScopeMaxPages_IsRejected(string scope)
    {
        var handler = new DefinitionValidateHandler(Options(256 * 1024), new CannedCli(PassingOutput(1)), NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route, ValidatePayload(), scope, CancellationToken.None);

        success.Should().BeFalse();
        payload.Should().BeNull();
        error.Should().Contain("max_pages");
    }

    [Fact]
    public async Task OversizedCatalog_IsStillAnInternalFailure()
    {
        var output = new JsonObject { ["ok"] = true, ["section"] = "overview", ["content"] = new string('x', 8 * 1024) };
        var handler = new CatalogQueryHandler(Options(4 * 1024), new CannedCli(output), NullLogger.Instance);
        var payload = new JsonObject
        {
            ["route"] = CatalogQueryHandler.Route,
            ["args"] = new JsonObject { ["section"] = "overview" },
            ["project_root"] = "/workspace"
        }.ToJsonString();

        var (success, result, error) = await handler.ExecuteAsync(RequestId, CatalogQueryHandler.Route, payload, "{}", CancellationToken.None);

        success.Should().BeFalse();
        result.Should().BeNull();
        error.Should().Contain("exceeded");
    }
}
