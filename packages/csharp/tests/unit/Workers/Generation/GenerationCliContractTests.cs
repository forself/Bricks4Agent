using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using GenerationWorker.Handlers;
using GenerationWorker.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Unit.Tests.Workers.Generation;

/// <summary>
/// generation-worker 與 repo 中真正的生成器（tools/generation/cli.mjs）之間的契約：
/// 以 golden 範例跑 catalog、validate 與 generate，zip 解開後與直接執行 build 的輸出逐位元組相同，
/// 兩次生成的 zip sha256 相同，回傳內容不含主機路徑。
/// （<see cref="GenerationHandlerTests"/> 以假的 CLI 測 handler 的邊界行為；這裡只驗證兩邊真的接得起來。）
/// </summary>
public sealed class GenerationCliContractTests : IDisposable
{
    private const string Slot = "task_0000000000AA_00000000000000BB";
    private const string PackageName = "contacts";
    private const string Title = "Contacts prototype";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"b4a-gencontract-{Guid.NewGuid():N}");
    private readonly string _repoRoot = FindRepositoryRoot();

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "tools", "generation", "cli.mjs")))
                return directory.FullName;
        }

        throw new InvalidOperationException("tools/generation/cli.mjs not found above the test output.");
    }

    private GenerationWorkerOptions Options(string outputRoot) => new()
    {
        NodePath = GenerationWorkerOptions.ResolveNodePath(null),
        ToolsRoot = _repoRoot,
        OutputRoot = outputRoot,
        QueryTimeout = TimeSpan.FromSeconds(60),
        BuildTimeout = TimeSpan.FromSeconds(120),
    };

    private JsonObject GoldenTemplate()
        => JsonNode.Parse(File.ReadAllText(Path.Combine(_repoRoot, "tools", "generation", "examples", "golden.definition-template.json")))!.AsObject();

    private static string Payload(string route, JsonObject args)
        => new JsonObject { ["route"] = route, ["args"] = args, ["project_root"] = "/workspace" }.ToJsonString();

    private static string GenerateScope() => new JsonObject
    {
        ["routes"] = new JsonArray("generate_scaffold"),
        ["output_slot"] = Slot,
        ["package_name"] = PackageName,
        ["max_pages"] = 12,
        ["package"] = "definition-site-v1",
    }.ToJsonString();

    private void AssertNoLocalPath(string? text)
    {
        text.Should().NotBeNull();
        foreach (var path in new[] { _repoRoot, _root })
        {
            text.Should().NotContain(path);
            text.Should().NotContain(path.Replace('\\', '/'));
            text.Should().NotContain(path.Replace("\\", "\\\\"));
        }
    }

    [Fact]
    public async Task CatalogAndValidate_WithTheRealGenerator_ReturnTheContractShape()
    {
        var options = Options(Path.Combine(_root, "out"));
        var cli = new NodeGeneratorCli(options, NullLogger.Instance);

        var catalog = await new CatalogQueryHandler(options, cli, NullLogger.Instance).ExecuteAsync(
            "req_0000000000AA_0000000000000001", CatalogQueryHandler.Route,
            Payload(CatalogQueryHandler.Route, new JsonObject { ["section"] = "field_types" }), "{}", CancellationToken.None);
        catalog.Success.Should().BeTrue(catalog.Error);
        AssertNoLocalPath(catalog.ResultPayload);
        var catalogJson = JsonNode.Parse(catalog.ResultPayload!)!.AsObject();
        catalogJson["ok"]!.GetValue<bool>().Should().BeTrue();
        catalogJson["section"]!.GetValue<string>().Should().Be("field_types");
        catalogJson["catalog_sha256"]!.GetValue<string>().Should().MatchRegex("^[0-9a-f]{64}$");

        var validate = new DefinitionValidateHandler(options, cli, NullLogger.Instance);
        var valid = await validate.ExecuteAsync(
            "req_0000000000AA_0000000000000002", DefinitionValidateHandler.Route,
            Payload(DefinitionValidateHandler.Route, new JsonObject { ["template"] = GoldenTemplate() }), "{}", CancellationToken.None);
        valid.Success.Should().BeTrue(valid.Error);
        var validJson = JsonNode.Parse(valid.ResultPayload!)!.AsObject();
        validJson["ok"]!.GetValue<bool>().Should().BeTrue();
        validJson["pages"]!.AsArray().Select(page => page!["type"]!.GetValue<string>()).Should().Equal("list", "detail", "form");

        // 生成器不認得的欄位型別：仍是成功的呼叫，payload ok:false 並帶結構化錯誤（代理依此修正）。
        var broken = GoldenTemplate();
        broken["definitions"]!["pages"]![2]!["definition"]!["fields"]![0]!["type"] = "slider";
        var invalid = await validate.ExecuteAsync(
            "req_0000000000AA_0000000000000003", DefinitionValidateHandler.Route,
            Payload(DefinitionValidateHandler.Route, new JsonObject { ["template"] = broken }), "{}", CancellationToken.None);
        invalid.Success.Should().BeTrue(invalid.Error);
        var invalidJson = JsonNode.Parse(invalid.ResultPayload!)!.AsObject();
        invalidJson["ok"]!.GetValue<bool>().Should().BeFalse();
        var error = invalidJson["errors"]![0]!.AsObject();
        error["code"]!.GetValue<string>().Should().Be("FIELD_TYPE_UNSUPPORTED");
        error["path"]!.GetValue<string>().Should().Be("definitions.pages[2].definition.fields[0].type");
        AssertNoLocalPath(invalid.ResultPayload);
    }

    [Fact]
    public async Task Generate_WithTheRealGenerator_PackagesExactlyWhatBuildWrites_AndIsDeterministic()
    {
        var firstRoot = Path.Combine(_root, "out-1");
        var secondRoot = Path.Combine(_root, "out-2");
        const string requestId = "req_0000000000AA_00000000000000C1";

        async Task<JsonObject> GenerateAsync(string outputRoot)
        {
            var options = Options(outputRoot);
            var handler = new ScaffoldGenerateHandler(options, new NodeGeneratorCli(options, NullLogger.Instance), NullLogger.Instance);
            var args = new JsonObject { ["template"] = GoldenTemplate(), ["title"] = Title };
            var (success, payload, error) = await handler.ExecuteAsync(
                requestId, ScaffoldGenerateHandler.Route, Payload(ScaffoldGenerateHandler.Route, args), GenerateScope(), CancellationToken.None);
            success.Should().BeTrue(error);
            AssertNoLocalPath(payload);
            return JsonNode.Parse(payload!)!.AsObject();
        }

        var first = await GenerateAsync(firstRoot);
        var second = await GenerateAsync(secondRoot);

        // 兩次生成（不同的輸出根目錄、不同的時間）得到相同的 zip。
        first["zip"]!["sha256"]!.GetValue<string>().Should().Be(second["zip"]!["sha256"]!.GetValue<string>());
        first["zip"]!["size"]!.GetValue<long>().Should().Be(second["zip"]!["size"]!.GetValue<long>());
        first.ToJsonString().Should().Be(second.ToJsonString());

        var zipPath = first["zip"]!["path"]!.GetValue<string>();
        zipPath.Should().Be($"{Slot}/{requestId}/{PackageName}-scaffold.zip");
        first["output_slot"]!.GetValue<string>().Should().Be(Slot);
        first["request_id"]!.GetValue<string>().Should().Be(requestId);
        first["generator_version"]!.GetValue<string>().Should().Be("definition-site/1.2.0");
        first["pages"]!.AsArray().Select(page => page!["type"]!.GetValue<string>()).Should().Equal("list", "detail", "form");

        var zipFile = Path.Combine(firstRoot, zipPath.Replace('/', Path.DirectorySeparatorChar));
        DeterministicZip.Sha256Hex(zipFile).Should().Be(first["zip"]!["sha256"]!.GetValue<string>());
        Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(zipFile)!).Select(Path.GetFileName)
            .Should().BeEquivalentTo($"{PackageName}-scaffold.zip", ScaffoldGenerateHandler.ResultFileName);

        // 同一份輸入直接執行 build：zip 的內容與它逐檔、逐位元組相同。
        var directOutput = Path.Combine(_root, "direct");
        var directOptions = Options(Path.Combine(_root, "unused"));
        var build = await new NodeGeneratorCli(directOptions, NullLogger.Instance).RunAsync("build", new JsonObject
        {
            ["template"] = GoldenTemplate(),
            ["title"] = Title,
            ["out_dir"] = directOutput,
        }, TimeSpan.FromSeconds(120), CancellationToken.None);
        build.Succeeded.Should().BeTrue(build.Failure);
        build.Ok.Should().BeTrue();
        build.Output!["validation_digest"]!.GetValue<string>().Should().Be(first["validation_digest"]!.GetValue<string>());
        build.Output!["catalog_sha256"]!.GetValue<string>().Should().Be(first["catalog_sha256"]!.GetValue<string>());

        var directFiles = Directory.EnumerateFiles(directOutput, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directOutput, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
        first["file_count"]!.GetValue<int>().Should().Be(directFiles.Count);

        using var archive = ZipFile.OpenRead(zipFile);
        var entries = archive.Entries.Select(entry => entry.FullName).ToList();
        entries.Should().Equal(directFiles);
        entries.Should().OnlyContain(name => name.StartsWith("site/", StringComparison.Ordinal) || name.StartsWith("report/", StringComparison.Ordinal));
        entries.Should().Contain(new[] { "site/index.html", "site/README.txt", "report/manifest.json", "report/validation.json" });

        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            var zipped = SHA256.HashData(stream);
            var direct = SHA256.HashData(File.ReadAllBytes(Path.Combine(directOutput, entry.FullName.Replace('/', Path.DirectorySeparatorChar))));
            zipped.Should().Equal(direct, $"{entry.FullName} matches the build output");
        }

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(directOutput, "report", "manifest.json")))!.AsObject();
        manifest["format"]!.GetValue<string>().Should().Be("definition-site-v1");
        AssertNoLocalPath(manifest.ToJsonString());
    }
}
