using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using GenerationWorker.Handlers;
using GenerationWorker.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Workers.Generation;

/// <summary>
/// generation-worker 的三個 handler，以測試用的假 CLI（<see cref="FakeGeneratorCli"/>）執行真正的 node 子程序：
/// catalog／validate 原樣轉交 CLI 的 JSON；generate 的輸出位置只取自 grant scope、冪等、zip 決定性，
/// 回傳內容不含主機路徑；逾時與輸出過大都會終止子程序並回報失敗。
/// </summary>
public sealed class GenerationHandlerTests : IDisposable
{
    private const string RequestId = "req_0000000000AA_00000000000000BB";
    private const string Slot = "task_0000000000CC_00000000000000DD";

    private readonly List<FakeGeneratorCli> _fixtures = new();

    public void Dispose()
    {
        foreach (var fixture in _fixtures)
            fixture.Dispose();
    }

    private FakeGeneratorCli NewFixture(Action<GenerationWorkerOptions>? configure = null)
    {
        var fixture = new FakeGeneratorCli(configure);
        _fixtures.Add(fixture);
        return fixture;
    }

    private static string Scope(
        string? slot = Slot,
        string? packageName = "contacts",
        string? maxPagesJson = "12",
        string? package = "definition-site-v1")
    {
        var scope = new JsonObject { ["routes"] = new JsonArray("generate_scaffold") };
        if (slot != null) scope["output_slot"] = slot;
        if (packageName != null) scope["package_name"] = packageName;
        if (maxPagesJson != null) scope["max_pages"] = JsonNode.Parse(maxPagesJson);
        if (package != null) scope["package"] = package;
        return scope.ToJsonString();
    }

    private static JsonObject Template(string? fake = null)
    {
        var template = new JsonObject
        {
            ["kind"] = "definition-template",
            ["pages"] = new JsonArray
            {
                new JsonObject { ["id"] = "ContactListPage", ["type"] = "list", ["fields"] = 3 },
                new JsonObject { ["id"] = "ContactDetailPage", ["type"] = "detail", ["fields"] = 3 },
                new JsonObject { ["id"] = "ContactFormPage", ["type"] = "form", ["fields"] = 3 },
            },
        };
        if (fake != null)
            template["fake"] = fake;
        return template;
    }

    private static string Payload(string route, JsonObject args)
        => new JsonObject { ["route"] = route, ["args"] = args, ["project_root"] = "/workspace" }.ToJsonString();

    private static string GeneratePayload(JsonObject? template = null, Action<JsonObject>? extra = null)
    {
        var args = new JsonObject { ["template"] = template ?? Template(), ["title"] = "Contacts" };
        extra?.Invoke(args);
        return Payload(ScaffoldGenerateHandler.Route, args);
    }

    private static ScaffoldGenerateHandler Generate(FakeGeneratorCli fixture)
        => new(fixture.Options, fixture.Cli, NullLogger.Instance);

    // ── catalog ──

    [Fact]
    public async Task Catalog_ReturnsTheCliJson()
    {
        var fixture = NewFixture();
        var handler = new CatalogQueryHandler(fixture.Options, fixture.Cli, NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, CatalogQueryHandler.Route,
            Payload(CatalogQueryHandler.Route, new JsonObject { ["section"] = "field_types" }), "{}", CancellationToken.None);

        success.Should().BeTrue(error);
        var json = JsonNode.Parse(payload!)!.AsObject();
        json["ok"]!.GetValue<bool>().Should().BeTrue();
        json["section"]!.GetValue<string>().Should().Be("field_types");
        json["catalog_sha256"]!.GetValue<string>().Should().HaveLength(64);
        JsonNode.Parse(fixture.LastInput("catalog"))!.AsObject().Select(p => p.Key).Should().BeEquivalentTo("section");
    }

    [Fact]
    public async Task Catalog_ComponentNotFound_IsASuccessfulCallWithOkFalse()
    {
        var fixture = NewFixture();
        var handler = new CatalogQueryHandler(fixture.Options, fixture.Cli, NullLogger.Instance);

        var (success, payload, _) = await handler.ExecuteAsync(RequestId, CatalogQueryHandler.Route,
            Payload(CatalogQueryHandler.Route, new JsonObject { ["section"] = "component", ["name"] = "Missing" }), "{}", CancellationToken.None);

        success.Should().BeTrue();
        var json = JsonNode.Parse(payload!)!.AsObject();
        json["ok"]!.GetValue<bool>().Should().BeFalse();
        json["errors"]![0]!["code"]!.GetValue<string>().Should().Be("COMPONENT_NOT_FOUND");
    }

    [Theory]
    [InlineData("""{"section":"everything"}""")]
    [InlineData("""{"section":5}""")]
    [InlineData("""{"name":"a-name-that-is-much-longer-than-sixty-four-characters-and-keeps-on-going"}""")]
    public async Task Catalog_InvalidArguments_AreRejectedWithoutRunningTheCli(string args)
    {
        var fixture = NewFixture();
        var handler = new CatalogQueryHandler(fixture.Options, fixture.Cli, NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, CatalogQueryHandler.Route,
            Payload(CatalogQueryHandler.Route, JsonNode.Parse(args)!.AsObject()), "{}", CancellationToken.None);

        success.Should().BeFalse();
        payload.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
        fixture.Invocations("catalog").Should().Be(0);
    }

    [Fact]
    public async Task Catalog_ResultContainingALocalPath_IsWithheld()
    {
        var fixture = NewFixture();
        var handler = new CatalogQueryHandler(fixture.Options, fixture.Cli, NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, CatalogQueryHandler.Route,
            Payload(CatalogQueryHandler.Route, new JsonObject { ["section"] = "component", ["name"] = "leak" }), "{}", CancellationToken.None);

        success.Should().BeFalse();
        payload.Should().BeNull();
        error.Should().Contain("local path");
        error.Should().NotContain(fixture.Root);
    }

    [Fact]
    public async Task Cli_DoesNotInheritWorkerCredentialsOrSecrets()
    {
        var fixture = NewFixture();
        var handler = new CatalogQueryHandler(fixture.Options, fixture.Cli, NullLogger.Instance);
        var names = new[] { "WORKER_Worker__Auth__SharedSecret", "B4A_TEST_SHARED_SECRET", "NODE_OPTIONS" };
        foreach (var name in names)
            Environment.SetEnvironmentVariable(name, name == "NODE_OPTIONS" ? "--max-old-space-size=512" : "test-value");
        try
        {
            var (success, payload, error) = await handler.ExecuteAsync(RequestId, CatalogQueryHandler.Route,
                Payload(CatalogQueryHandler.Route, new JsonObject { ["name"] = "env-check" }), "{}", CancellationToken.None);

            success.Should().BeTrue(error);
            JsonNode.Parse(payload!)!["content"]!.GetValue<string>().Should().BeEmpty();
        }
        finally
        {
            foreach (var name in names)
                Environment.SetEnvironmentVariable(name, null);
        }
    }

    // ── validate ──

    [Fact]
    public async Task Validate_InvalidDefinition_IsASuccessfulCallWithStructuredErrors()
    {
        var fixture = NewFixture();
        var handler = new DefinitionValidateHandler(fixture.Options, fixture.Cli, NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route,
            Payload(DefinitionValidateHandler.Route, new JsonObject { ["template"] = Template("invalid") }), "{}", CancellationToken.None);

        success.Should().BeTrue(error);
        var json = JsonNode.Parse(payload!)!.AsObject();
        json["ok"]!.GetValue<bool>().Should().BeFalse();
        var first = json["errors"]![0]!.AsObject();
        first["code"]!.GetValue<string>().Should().Be("FIELD_TYPE_UNSUPPORTED");
        first.Select(p => p.Key).Should().Contain(new[] { "code", "path", "message", "hint" });
    }

    [Fact]
    public async Task Validate_ForwardsOnlyTemplateAndPageIds()
    {
        var fixture = NewFixture();
        var handler = new DefinitionValidateHandler(fixture.Options, fixture.Cli, NullLogger.Instance);
        var args = new JsonObject
        {
            ["template"] = Template(),
            ["page_ids"] = new JsonArray("ContactListPage"),
            ["out_dir"] = Path.Combine(fixture.Root, "elsewhere"),
            ["path"] = "../../etc",
        };

        var (success, _, error) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route,
            Payload(DefinitionValidateHandler.Route, args), "{}", CancellationToken.None);

        success.Should().BeTrue(error);
        JsonNode.Parse(fixture.LastInput("validate"))!.AsObject().Select(p => p.Key)
            .Should().BeEquivalentTo("template", "page_ids");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"template":"not an object"}""")]
    [InlineData("""{"template":{},"page_ids":"ContactListPage"}""")]
    [InlineData("""{"template":{},"page_ids":[1]}""")]
    public async Task Validate_InvalidArguments_AreRejectedWithoutRunningTheCli(string args)
    {
        var fixture = NewFixture();
        var handler = new DefinitionValidateHandler(fixture.Options, fixture.Cli, NullLogger.Instance);

        var (success, _, _) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route,
            Payload(DefinitionValidateHandler.Route, JsonNode.Parse(args)!.AsObject()), "{}", CancellationToken.None);

        success.Should().BeFalse();
        fixture.Invocations("validate").Should().Be(0);
    }

    [Fact]
    public async Task OverlongTemplateKeys_AreRejectedWithoutRunningTheCli()
    {
        var fixture = NewFixture();
        var template = Template();
        template["pages"]![0]!.AsObject()[new string('k', GenerationRequest.MaxTemplateKeyLength + 1)] = 1;

        var validate = new DefinitionValidateHandler(fixture.Options, fixture.Cli, NullLogger.Instance);
        var (validateSuccess, _, validateError) = await validate.ExecuteAsync(RequestId, DefinitionValidateHandler.Route,
            Payload(DefinitionValidateHandler.Route, new JsonObject { ["template"] = template.DeepClone() }), "{}", CancellationToken.None);
        var (generateSuccess, _, generateError) = await Generate(fixture).ExecuteAsync(RequestId, ScaffoldGenerateHandler.Route,
            GeneratePayload(template), Scope(), CancellationToken.None);

        validateSuccess.Should().BeFalse();
        validateError.Should().Contain("longer than 128");
        generateSuccess.Should().BeFalse();
        generateError.Should().Contain("longer than 128");
        fixture.Invocations("validate").Should().Be(0);
        fixture.Invocations("build").Should().Be(0);
    }

    /// <summary>名稱的格式檢查涵蓋整個字串：.NET 的 <c>$</c> 也接受結尾的換行，這裡不可以。</summary>
    [Theory]
    [InlineData("task_0000000000CC", true)]
    [InlineData("CONSOLE", true)]
    [InlineData("task_1\n", false)]
    [InlineData("task_1\r\n", false)]
    [InlineData("\ntask_1", false)]
    [InlineData("CON", false)]
    [InlineData("lpt1", false)]
    public void IsSafeName_MatchesTheWholeString(string value, bool expected)
        => GenerationRequest.IsSafeName(value).Should().Be(expected);

    [Fact]
    public void HasOverlongKey_ChecksEveryNestedObject()
    {
        var atLimit = new JsonObject { ["a"] = new JsonArray(new JsonObject { [new string('k', GenerationRequest.MaxTemplateKeyLength)] = 1 }) };
        var overLimit = new JsonObject { ["a"] = new JsonArray(new JsonObject { ["b"] = new JsonObject { [new string('k', GenerationRequest.MaxTemplateKeyLength + 1)] = null } }) };

        GenerationRequest.HasOverlongKey(atLimit).Should().BeFalse();
        GenerationRequest.HasOverlongKey(overLimit).Should().BeTrue();
        GenerationRequest.HasOverlongKey(null).Should().BeFalse();
    }

    [Fact]
    public async Task Cli_RunsNodeWithAHeapLimit()
    {
        var fixture = NewFixture(options => options.MaxOldSpaceMegabytes = 192);
        var handler = new CatalogQueryHandler(fixture.Options, fixture.Cli, NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, CatalogQueryHandler.Route,
            Payload(CatalogQueryHandler.Route, new JsonObject { ["name"] = "heap-check" }), "{}", CancellationToken.None);

        success.Should().BeTrue(error);
        JsonNode.Parse(payload!)!["content"]!.GetValue<string>().Should().Contain("--max-old-space-size=192");
    }

    [Fact]
    public void Options_RejectATooSmallHeapLimit()
    {
        var fixture = NewFixture(options => options.MaxOldSpaceMegabytes = 16);
        fixture.Options.Validate().Should().Contain("MaxOldSpaceMegabytes");
    }

    [Fact]
    public async Task Validate_Timeout_StopsTheProcessAndFails()
    {
        var fixture = NewFixture(options => options.QueryTimeout = TimeSpan.FromSeconds(2));
        var handler = new DefinitionValidateHandler(fixture.Options, fixture.Cli, NullLogger.Instance);
        var stopwatch = Stopwatch.StartNew();

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route,
            Payload(DefinitionValidateHandler.Route, new JsonObject { ["template"] = Template("sleep") }), "{}", CancellationToken.None);

        stopwatch.Stop();
        success.Should().BeFalse();
        payload.Should().BeNull();
        error.Should().Contain("timed out");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Validate_OutputLargerThanTheLimit_StopsTheProcessAndFails()
    {
        var fixture = NewFixture(options => options.MaxStdoutBytes = 64 * 1024);
        var handler = new DefinitionValidateHandler(fixture.Options, fixture.Cli, NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route,
            Payload(DefinitionValidateHandler.Route, new JsonObject { ["template"] = Template("flood") }), "{}", CancellationToken.None);

        success.Should().BeFalse();
        payload.Should().BeNull();
        error.Should().Contain("exceeded");
    }

    [Theory]
    [InlineData("crash")]
    [InlineData("badjson")]
    public async Task Validate_CliFailure_IsAnInternalErrorWithoutLocalPaths(string mode)
    {
        var fixture = NewFixture();
        var handler = new DefinitionValidateHandler(fixture.Options, fixture.Cli, NullLogger.Instance);

        var (success, payload, error) = await handler.ExecuteAsync(RequestId, DefinitionValidateHandler.Route,
            Payload(DefinitionValidateHandler.Route, new JsonObject { ["template"] = Template(mode) }), "{}", CancellationToken.None);

        success.Should().BeFalse();
        payload.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
        error.Should().NotContain(fixture.Root);
        error.Should().NotContain(fixture.Root.Replace('\\', '/'));
    }

    // ── generate ──

    [Fact]
    public async Task Generate_WritesOnlyUnderTheScopeSlot_AndIgnoresPathArguments()
    {
        var fixture = NewFixture();
        var elsewhere = Path.Combine(fixture.Root, "elsewhere");
        var payload = GeneratePayload(extra: args =>
        {
            args["out_dir"] = elsewhere;
            args["output_directory"] = elsewhere;
            args["archive_path"] = Path.Combine(elsewhere, "x.zip");
            args["path"] = "../../outside";
            args["output_slot"] = "other_slot";
            args["package_name"] = "other_name";
        });

        var (success, result, error) = await Generate(fixture).ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, payload, Scope(), CancellationToken.None);

        success.Should().BeTrue(error);
        Directory.Exists(elsewhere).Should().BeFalse();
        Directory.Exists(Path.Combine(fixture.OutputRoot, "other_slot")).Should().BeFalse();
        Directory.EnumerateDirectories(fixture.OutputRoot).Select(Path.GetFileName).Should().Equal(Slot);

        var requestDirectory = Path.Combine(fixture.OutputRoot, Slot, RequestId);
        Directory.EnumerateFileSystemEntries(requestDirectory).Select(Path.GetFileName)
            .Should().BeEquivalentTo("contacts-scaffold.zip", "result.json");

        var forwarded = JsonNode.Parse(fixture.LastInput("build"))!.AsObject();
        forwarded.Select(p => p.Key).Should().BeEquivalentTo("template", "title", "out_dir");
        Path.GetFullPath(forwarded["out_dir"]!.GetValue<string>())
            .Should().Be(Path.Combine(requestDirectory, ScaffoldGenerateHandler.WorkDirectoryName));
    }

    [Fact]
    public async Task Generate_PayloadHasRelativeZipPathAndNoLocalPaths()
    {
        var fixture = NewFixture();

        var (success, result, error) = await Generate(fixture).ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(), Scope(), CancellationToken.None);

        success.Should().BeTrue(error);
        result.Should().NotContain(fixture.Root);
        result.Should().NotContain(fixture.Root.Replace('\\', '/'));
        result.Should().NotContain(fixture.Root.Replace("\\", "\\\\"));
        result.Should().NotContain("site/index.html", "file contents and file lists stay out of the payload");

        var json = JsonNode.Parse(result!)!.AsObject();
        json.Select(p => p.Key).Should().BeEquivalentTo(
            "output_slot", "request_id", "zip", "pages", "file_count", "validation_digest", "generator_version", "catalog_sha256");
        json["output_slot"]!.GetValue<string>().Should().Be(Slot);
        json["request_id"]!.GetValue<string>().Should().Be(RequestId);
        var zipPath = json["zip"]!["path"]!.GetValue<string>();
        zipPath.Should().Be($"{Slot}/{RequestId}/contacts-scaffold.zip");
        LocalPathGuard.LooksAbsolute(zipPath).Should().BeFalse();

        var zipFile = Path.Combine(fixture.OutputRoot, zipPath.Replace('/', Path.DirectorySeparatorChar));
        json["zip"]!["sha256"]!.GetValue<string>().Should().Be(DeterministicZip.Sha256Hex(zipFile));
        json["zip"]!["size"]!.GetValue<long>().Should().Be(new FileInfo(zipFile).Length);
        json["pages"]!.AsArray().Select(p => p!["id"]!.GetValue<string>())
            .Should().Equal("ContactListPage", "ContactDetailPage", "ContactFormPage");
        json["file_count"]!.GetValue<int>().Should().Be(8);
        File.ReadAllText(Path.Combine(fixture.OutputRoot, Slot, RequestId, "result.json")).Should().Be(result);
    }

    [Fact]
    public async Task Generate_ZipIsDeterministic_WithOnlySiteAndReportAtTheTop()
    {
        var fixture = NewFixture();
        var handler = Generate(fixture);

        var first = await handler.ExecuteAsync(RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(), Scope(), CancellationToken.None);
        await Task.Delay(1100); // 檔案時間不同也不影響 zip
        var second = await handler.ExecuteAsync("req_0000000000AA_00000000000000EE", ScaffoldGenerateHandler.Route, GeneratePayload(), Scope(), CancellationToken.None);

        first.Success.Should().BeTrue(first.Error);
        second.Success.Should().BeTrue(second.Error);
        var firstSha = JsonNode.Parse(first.ResultPayload!)!["zip"]!["sha256"]!.GetValue<string>();
        var secondSha = JsonNode.Parse(second.ResultPayload!)!["zip"]!["sha256"]!.GetValue<string>();
        secondSha.Should().Be(firstSha);

        var zipFile = Path.Combine(fixture.OutputRoot, Slot, RequestId, "contacts-scaffold.zip");
        using var archive = ZipFile.OpenRead(zipFile);
        var names = archive.Entries.Select(entry => entry.FullName).ToList();
        names.Should().BeInAscendingOrder(StringComparer.Ordinal);
        names.Should().OnlyContain(name => name.StartsWith("site/", StringComparison.Ordinal) || name.StartsWith("report/", StringComparison.Ordinal));
        names.Should().Contain(new[] { "site/index.html", "report/manifest.json", "report/validation.json" });
        archive.Entries.Should().OnlyContain(entry => entry.LastWriteTime.DateTime == DeterministicZip.FixedTimestamp.DateTime);
        archive.Entries.Should().OnlyContain(entry => entry.ExternalAttributes == DeterministicZip.RegularFileAttributes);
    }

    [Fact]
    public async Task Generate_SameRequestAgain_ReturnsTheStoredResultWithoutRebuilding()
    {
        var fixture = NewFixture();
        var handler = Generate(fixture);

        var first = await handler.ExecuteAsync(RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(), Scope(), CancellationToken.None);
        var second = await handler.ExecuteAsync(RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(), Scope(), CancellationToken.None);

        first.Success.Should().BeTrue(first.Error);
        second.Success.Should().BeTrue(second.Error);
        second.ResultPayload.Should().Be(first.ResultPayload);
        fixture.Invocations("build").Should().Be(1);

        // zip 與紀錄不符（被改動或寫到一半）：清空後重做。
        var zipFile = Path.Combine(fixture.OutputRoot, Slot, RequestId, "contacts-scaffold.zip");
        await File.AppendAllTextAsync(zipFile, "tampered");
        var third = await handler.ExecuteAsync(RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(), Scope(), CancellationToken.None);

        third.Success.Should().BeTrue(third.Error);
        fixture.Invocations("build").Should().Be(2);
        third.ResultPayload.Should().Be(first.ResultPayload, "the rebuilt package is byte-identical");
    }

    [Fact]
    public async Task Generate_ConcurrentRetriesOfTheSameRequest_BuildOnce()
    {
        var fixture = NewFixture();
        var handler = Generate(fixture);

        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ =>
            handler.ExecuteAsync(RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(), Scope(), CancellationToken.None)));

        results.Should().OnlyContain(result => result.Success);
        results.Select(result => result.ResultPayload).Distinct().Should().HaveCount(1);
        fixture.Invocations("build").Should().Be(1);
    }

    public static TheoryData<string, string> InvalidScopes => new()
    {
        { "", "empty" },
        { "not json", "not JSON" },
        { "[]", "array" },
        { Scope(slot: null), "output_slot missing" },
        { Scope(slot: "../escape"), "output_slot traversal" },
        { Scope(slot: "a/b"), "output_slot separator" },
        { Scope(slot: "a\\b"), "output_slot backslash" },
        { Scope(slot: "CON"), "output_slot device name" },
        { Scope(slot: new string('a', 81)), "output_slot too long" },
        { Scope(slot: "task.1"), "output_slot dot" },
        { Scope(slot: "task_1\n"), "output_slot trailing newline" },
        { Scope(packageName: "contacts\n"), "package_name trailing newline" },
        { Scope(packageName: null), "package_name missing" },
        { Scope(packageName: "..\\x"), "package_name traversal" },
        { Scope(packageName: "名稱"), "package_name non-ascii" },
        { Scope(maxPagesJson: null), "max_pages missing" },
        { Scope(maxPagesJson: "0"), "max_pages zero" },
        { Scope(maxPagesJson: "\"12\""), "max_pages string" },
        { Scope(maxPagesJson: "2.5"), "max_pages fraction" },
        { Scope(maxPagesJson: "13"), "max_pages above the generator page limit" },
        { Scope(maxPagesJson: "101"), "max_pages too large" },
        { Scope(package: null), "package missing" },
        { Scope(package: "definition-site-v2"), "package unknown" },
        { """{"output_slot":5,"package_name":"contacts","max_pages":12,"package":"definition-site-v1"}""", "output_slot number" },
    };

    [Theory]
    [MemberData(nameof(InvalidScopes))]
    public async Task Generate_ScopeMissingOrMalformed_IsRejectedBeforeAnythingIsWritten(string scope, string because)
    {
        var fixture = NewFixture();

        var (success, payload, error) = await Generate(fixture).ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(), scope, CancellationToken.None);

        success.Should().BeFalse(because);
        payload.Should().BeNull();
        error.Should().Contain("scope", because);
        Directory.Exists(fixture.OutputRoot).Should().BeFalse(because);
        fixture.Invocations("build").Should().Be(0, because);
    }

    [Theory]
    [InlineData("../req")]
    [InlineData("req/1")]
    [InlineData("req.1")]
    [InlineData("")]
    [InlineData("NUL")]
    [InlineData("req_1\n")]
    public async Task Generate_UnsafeRequestId_IsRejected(string requestId)
    {
        var fixture = NewFixture();

        var (success, _, _) = await Generate(fixture).ExecuteAsync(
            requestId, ScaffoldGenerateHandler.Route, GeneratePayload(), Scope(), CancellationToken.None);

        success.Should().BeFalse();
        Directory.Exists(fixture.OutputRoot).Should().BeFalse();
        fixture.Invocations("build").Should().Be(0);
    }

    [Fact]
    public async Task Generate_OtherRoute_IsRejected()
    {
        var fixture = NewFixture();

        var (success, _, _) = await Generate(fixture).ExecuteAsync(
            RequestId, "write_file", GeneratePayload(), Scope(), CancellationToken.None);

        success.Should().BeFalse();
        fixture.Invocations("build").Should().Be(0);
    }

    [Fact]
    public async Task Generate_InvalidDefinition_ReturnsStructuredErrorsAndLeavesNothingBehind()
    {
        var fixture = NewFixture();

        var (success, payload, error) = await Generate(fixture).ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(Template("invalid")), Scope(), CancellationToken.None);

        success.Should().BeFalse();
        payload.Should().BeNull();
        var json = JsonNode.Parse(error!)!.AsObject();
        json["ok"]!.GetValue<bool>().Should().BeFalse();
        json["errors"]![0]!["code"]!.GetValue<string>().Should().Be("FIELD_TYPE_UNSUPPORTED");
        Directory.Exists(Path.Combine(fixture.OutputRoot, Slot, RequestId)).Should().BeFalse();
    }

    [Fact]
    public async Task Generate_MorePagesThanTheScopeAllows_IsRejected()
    {
        var fixture = NewFixture();

        var (success, _, error) = await Generate(fixture).ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(Template("pages:3")), Scope(maxPagesJson: "2"), CancellationToken.None);

        success.Should().BeFalse();
        JsonNode.Parse(error!)!["errors"]![0]!["code"]!.GetValue<string>().Should().Be("MAX_PAGES_EXCEEDED");
        Directory.Exists(Path.Combine(fixture.OutputRoot, Slot, RequestId)).Should().BeFalse();
    }

    [Theory]
    [InlineData("extra", "site/ and report/")]
    [InlineData("unlisted", "did not report")]
    [InlineData("report-leak", "local path")]
    public async Task Generate_UnexpectedGeneratorOutput_IsRejected(string mode, string expectedError)
    {
        var fixture = NewFixture();

        var (success, payload, error) = await Generate(fixture).ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(Template(mode)), Scope(), CancellationToken.None);

        success.Should().BeFalse();
        payload.Should().BeNull();
        error.Should().Contain(expectedError);
        error.Should().NotContain(fixture.Root);
        Directory.Exists(Path.Combine(fixture.OutputRoot, Slot, RequestId)).Should().BeFalse();
    }

    [Fact]
    public async Task Generate_Timeout_FailsAndCleansUp()
    {
        var fixture = NewFixture(options => options.BuildTimeout = TimeSpan.FromSeconds(2));

        var (success, _, error) = await Generate(fixture).ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(Template("sleep")), Scope(), CancellationToken.None);

        success.Should().BeFalse();
        error.Should().Contain("timed out");
        Directory.Exists(Path.Combine(fixture.OutputRoot, Slot, RequestId)).Should().BeFalse();
    }

    [Fact]
    public async Task Generate_OutputLargerThanTheLimit_Fails()
    {
        var fixture = NewFixture(options => options.MaxStdoutBytes = 64 * 1024);

        var (success, _, error) = await Generate(fixture).ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(Template("flood")), Scope(), CancellationToken.None);

        success.Should().BeFalse();
        error.Should().Contain("exceeded");
    }

    // ── 保留期限 ──

    private static string MakeRequestDirectory(string outputRoot, string slot, string requestId, DateTime? lastWriteUtc = null)
    {
        var directory = Path.Combine(outputRoot, slot, requestId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "contacts-scaffold.zip"), "PK-earlier-package");
        File.WriteAllText(Path.Combine(directory, ScaffoldGenerateHandler.ResultFileName), "{}");
        if (lastWriteUtc is { } time)
        {
            Directory.SetLastWriteTimeUtc(directory, time);
            Directory.SetLastWriteTimeUtc(Path.Combine(outputRoot, slot), time);
        }

        return directory;
    }

    [Fact]
    public async Task Generate_FirstRemovesPackagesOlderThanTheRetention()
    {
        var fixture = NewFixture();
        var expired = DateTime.UtcNow.AddHours(-25);

        var expiredRequest = MakeRequestDirectory(fixture.OutputRoot, "task_expired", "req_expired", expired);
        var recentRequest = MakeRequestDirectory(fixture.OutputRoot, "task_recent", "req_recent");
        var mixedExpired = MakeRequestDirectory(fixture.OutputRoot, "task_mixed", "req_mixed_old", expired);
        var mixedRecent = MakeRequestDirectory(fixture.OutputRoot, "task_mixed", "req_mixed_new");
        // 本 worker 不會建立的名稱（slot 含 '.'）：不動。
        var foreign = MakeRequestDirectory(fixture.OutputRoot, "not.a.slot", "req_foreign", expired);

        var (success, _, error) = await Generate(fixture).ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(), Scope(), CancellationToken.None);

        success.Should().BeTrue(error);
        Directory.Exists(expiredRequest).Should().BeFalse("a package older than the retention is removed");
        Directory.Exists(Path.Combine(fixture.OutputRoot, "task_expired")).Should().BeFalse("a slot left empty by expired requests is removed");
        Directory.Exists(recentRequest).Should().BeTrue();
        Directory.Exists(mixedExpired).Should().BeFalse();
        Directory.Exists(mixedRecent).Should().BeTrue("the slot keeps its recent request");
        Directory.Exists(foreign).Should().BeTrue("directories the worker never creates are left alone");
        File.Exists(Path.Combine(fixture.OutputRoot, Slot, RequestId, "contacts-scaffold.zip")).Should().BeTrue("the new package is kept");
    }

    [Fact]
    public async Task PeriodicSweep_RemovesExpiredPackages_WithoutAGenerate()
    {
        // worker 閒置（沒有新的 generate、也沒有重啟）時，定期清理仍刪除過期的產物。
        var fixture = NewFixture();
        var expired = DateTime.UtcNow.AddHours(-25);
        var expiredRequest = MakeRequestDirectory(fixture.OutputRoot, "task_idle", "req_idle_old", expired);
        var recentRequest = MakeRequestDirectory(fixture.OutputRoot, "task_idle_recent", "req_idle_new");
        var handler = Generate(fixture);

        using var stop = new CancellationTokenSource();
        var loop = OutputRetention.RunPeriodicAsync(handler.SweepExpiredOutputsAsync, TimeSpan.FromMilliseconds(50), NullLogger.Instance, stop.Token);
        var waited = Stopwatch.StartNew();
        while (Directory.Exists(expiredRequest) && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(25);
        stop.Cancel();
        await loop;

        Directory.Exists(expiredRequest).Should().BeFalse("the periodic sweep removes an expired package without a generate");
        Directory.Exists(recentRequest).Should().BeTrue();
        fixture.Invocations("build").Should().Be(0, "the sweep never runs the generator");
    }

    [Fact]
    public async Task PeriodicSweep_KeepsRunningAfterAFailedSweep()
    {
        var calls = 0;
        using var stop = new CancellationTokenSource();
        var loop = OutputRetention.RunPeriodicAsync(_ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new IOException("simulated sweep failure");
            return Task.FromResult(0);
        }, TimeSpan.FromMilliseconds(20), NullLogger.Instance, stop.Token);

        var waited = Stopwatch.StartNew();
        while (Volatile.Read(ref calls) < 3 && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(10);
        stop.Cancel();
        await loop;

        Volatile.Read(ref calls).Should().BeGreaterThanOrEqualTo(3, "one failed sweep does not stop the later ones");
    }

    [Fact]
    public async Task SweepExpiredOutputs_WaitsForARunningGenerate()
    {
        var fixture = NewFixture(options => options.BuildTimeout = TimeSpan.FromSeconds(3));
        var handler = Generate(fixture);
        var generate = handler.ExecuteAsync(
            RequestId, ScaffoldGenerateHandler.Route, GeneratePayload(Template("sleep")), Scope(), CancellationToken.None);

        var waited = Stopwatch.StartNew();
        while (fixture.Invocations("build") == 0 && waited.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(25);
        fixture.Invocations("build").Should().Be(1, "the generate is running");

        // 生成進行中才出現的過期目錄：清理要等生成結束（同一把鎖）才動手。
        var expiredRequest = MakeRequestDirectory(fixture.OutputRoot, "task_waiting", "req_waiting_old", DateTime.UtcNow.AddHours(-25));
        var sweep = handler.SweepExpiredOutputsAsync(CancellationToken.None);
        await Task.Delay(500);
        sweep.IsCompleted.Should().BeFalse("the sweep waits while a generate holds the output directory");
        Directory.Exists(expiredRequest).Should().BeTrue();

        var (success, _, _) = await generate;
        success.Should().BeFalse("the sleeping generator times out");
        (await sweep).Should().Be(1);
        Directory.Exists(expiredRequest).Should().BeFalse();
    }

    [Fact]
    public void Options_SweepTheOutputHourly()
        => NewFixture().Options.RetentionSweepInterval.Should().Be(TimeSpan.FromHours(1));

    [Fact]
    public void RetentionSweep_DoesNotFollowOrRemoveLinks()
    {
        var fixture = NewFixture();
        var expired = DateTime.UtcNow.AddHours(-25);
        var outside = Path.Combine(fixture.Root, "outside");
        var outsideRequest = MakeRequestDirectory(outside, "task_target", "req_target", expired);

        Directory.CreateDirectory(fixture.OutputRoot);
        var linkedSlot = Path.Combine(fixture.OutputRoot, "task_linked");
        if (!Unit.Tests.Broker.GovernedGenerationTestSupport.TryCreateDirectoryLink(linkedSlot, Path.Combine(outside, "task_target")))
            return; // 這台機器不能建立連結（沒有 symlink 權限也沒有 junction），無法重現。

        // 請求目錄裡有連結：整個略過，連結指向的內容也不動。
        var requestWithLink = MakeRequestDirectory(fixture.OutputRoot, "task_plain", "req_with_link");
        var linkInside = Path.Combine(requestWithLink, "linked");
        if (!Unit.Tests.Broker.GovernedGenerationTestSupport.TryCreateDirectoryLink(linkInside, Path.Combine(outside, "task_target")))
            return;
        Directory.SetLastWriteTimeUtc(requestWithLink, expired);

        var removed = OutputRetention.Sweep(fixture.OutputRoot, TimeSpan.FromHours(24), DateTimeOffset.UtcNow, NullLogger.Instance);

        removed.Should().Be(0);
        Directory.Exists(linkedSlot).Should().BeTrue("a linked slot is neither followed nor removed");
        Directory.Exists(requestWithLink).Should().BeTrue("a request directory with a link inside is left in place");
        File.Exists(Path.Combine(outsideRequest, "contacts-scaffold.zip")).Should().BeTrue("content behind a link is never deleted");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(GenerationWorkerOptions.MaxRetentionHours + 1)]
    public void Options_RejectARetentionOutsideTheRange(int hours)
    {
        var fixture = NewFixture(options => options.RetentionHours = hours);
        fixture.Options.Validate().Should().Contain("RetentionHours");
    }

    [Fact]
    public void Options_DefaultRetentionIsOneDay()
    {
        var fixture = NewFixture();
        fixture.Options.RetentionHours.Should().Be(24);
        fixture.Options.Validate().Should().BeNull();
    }
}
