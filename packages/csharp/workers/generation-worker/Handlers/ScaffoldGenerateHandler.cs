using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GenerationWorker.Support;
using Microsoft.Extensions.Logging;
using WorkerSdk;

namespace GenerationWorker.Handlers;

/// <summary>
/// generation.scaffold.generate（route: generate_scaffold）：以 DefinitionTemplate 確定性生成多頁前端原型並打包成 zip。
///
/// - 輸出位置只取自 grant scope（broker 寫入）：<c>{OutputRoot}/{output_slot}/{requestId}/</c>。
///   請求參數中的任何路徑類欄位都不採用；scope 缺少 output_slot、package_name、max_pages 或 package 即拒絕。
/// - 冪等：同一 requestId 的 <c>result.json</c> 已存在且 zip 的 sha256 與大小相符時，直接回傳同一結果
///   （broker 逾時重派不會重複生成）；否則清空該 requestId 目錄（只會由本 worker 建立）後重做。
/// - 生成器先跑與 validate 相同的驗證；不通過時回 Success=false，錯誤訊息是結構化 errors 的 JSON。
/// - zip 以決定性方式產生（排序、固定時間戳與權限位元），頂層只有 <c>site/</c> 與 <c>report/</c>。
/// - 回傳的 payload 不含檔案內容，也不含主機的絕對路徑：zip 路徑相對於 OutputRoot。
/// </summary>
public sealed class ScaffoldGenerateHandler : ICapabilityHandler
{
    public const string Route = "generate_scaffold";
    public const string ResultFileName = "result.json";
    public const string WorkDirectoryName = "work";

    private const int MaxReportedErrors = 50;
    private const int MaxErrorMessageBytes = 16 * 1024;
    private static readonly string[] PackageTopLevel = { "site", "report" };

    private readonly GenerationWorkerOptions _options;
    private readonly IGeneratorCli _cli;
    private readonly ILogger _logger;

    // 同一時間只生成一個產物：broker 逾時重派同一請求時，第二次會等第一次結束後直接取用其結果。
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ScaffoldGenerateHandler(GenerationWorkerOptions options, IGeneratorCli cli, ILogger logger)
    {
        _options = options;
        _cli = cli;
        _logger = logger;
    }

    public string CapabilityId => "generation.scaffold.generate";

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            if (!string.Equals(route, Route, StringComparison.Ordinal))
                return Fail($"Route '{route}' is not handled by {CapabilityId}.");

            if (!GenerationRequest.IsSafeName(requestId))
                return Fail("Request id is not a safe name.");

            if (!GenerationScope.TryParse(scope, out var generationScope, out var scopeError))
                return Fail(scopeError);

            var args = GenerationRequest.ExtractArgs(payload);
            if (args == null)
                return Fail("Payload is not a JSON object.");

            if (args["template"] is not JsonObject template)
                return Fail("template is required and must be a JSON object.");

            if (!GenerationRequest.TryGetStringArray(args, "page_ids", 64, out var pageIds))
                return Fail("page_ids must be an array of strings of at most 64 characters.");

            if (!GenerationRequest.TryGetString(args, "title", 120, out var title))
                return Fail("title must be a string of at most 120 characters.");

            await _gate.WaitAsync(ct);
            try
            {
                return await GenerateAsync(requestId, generationScope!, template, pageIds, title, ct);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Capability} failed for {RequestId}", CapabilityId, requestId);
            return Fail($"{CapabilityId} failed.");
        }
    }

    private async Task<(bool, string?, string?)> GenerateAsync(
        string requestId,
        GenerationScope scope,
        JsonObject template,
        JsonArray? pageIds,
        string? title,
        CancellationToken ct)
    {
        var outputRoot = Path.GetFullPath(_options.OutputRoot);
        Directory.CreateDirectory(outputRoot);

        var slotDirectory = Path.Combine(outputRoot, scope.OutputSlot);
        var requestDirectory = Path.Combine(slotDirectory, requestId);
        if (!IsStrictlyInside(outputRoot, requestDirectory))
            return Fail("Output location is outside the output root.");

        if (IsLink(slotDirectory))
            return Fail("Output slot directory is a link.");
        Directory.CreateDirectory(slotDirectory);
        if (IsLink(slotDirectory))
            return Fail("Output slot directory is a link.");

        var zipName = $"{scope.PackageName}-scaffold.zip";
        var zipRelativePath = $"{scope.OutputSlot}/{requestId}/{zipName}";
        var zipPath = Path.Combine(requestDirectory, zipName);
        var resultPath = Path.Combine(requestDirectory, ResultFileName);

        if (Directory.Exists(requestDirectory) || File.Exists(requestDirectory))
        {
            if (IsLink(requestDirectory) || File.Exists(requestDirectory))
                return Fail("Request output location is not a plain directory.");

            if (TryReuseResult(resultPath, zipPath, scope, requestId, zipRelativePath, out var cached))
            {
                _logger.LogInformation("Reusing the stored result for {RequestId}.", requestId);
                return (true, cached, null);
            }

            // 只會是本 worker 先前（中斷或失敗）留下的目錄：清空後重做。
            Directory.Delete(requestDirectory, recursive: true);
        }

        Directory.CreateDirectory(requestDirectory);
        var workDirectory = Path.Combine(requestDirectory, WorkDirectoryName);

        var input = new JsonObject { ["template"] = template.DeepClone() };
        if (pageIds != null)
            input["page_ids"] = pageIds;
        if (title != null)
            input["title"] = title;
        input["out_dir"] = workDirectory;

        var cli = await _cli.RunAsync("build", input, _options.BuildTimeout, ct);
        if (!cli.Succeeded)
            return FailAndClean(requestDirectory, cli.Failure ?? "Generator failed.");

        var output = cli.Output!;
        if (!cli.Ok)
            return FailAndClean(requestDirectory, StructuredErrors(output["errors"] as JsonArray, outputRoot));

        if (!TryReadPages(output, out var pages))
            return FailAndClean(requestDirectory, "Generator result has no valid pages list.");

        if (pages.Count > scope.MaxPages)
        {
            var errors = new JsonArray
            {
                new JsonObject
                {
                    ["code"] = "MAX_PAGES_EXCEEDED",
                    ["path"] = "pages",
                    ["message"] = $"The definition produced {pages.Count} pages; this task allows at most {scope.MaxPages}.",
                    ["hint"] = "Remove pages or select a subset with page_ids.",
                },
            };
            return FailAndClean(requestDirectory, StructuredErrors(errors, outputRoot));
        }

        if (!TryReadToken(output, "generator_version", out var generatorVersion) ||
            !TryReadToken(output, "catalog_sha256", out var catalogSha256) ||
            !TryReadToken(output, "validation_digest", out var validationDigest))
        {
            return FailAndClean(requestDirectory, "Generator result is missing generator_version, catalog_sha256 or validation_digest.");
        }

        if (!TryCollectPackageFiles(workDirectory, out var files, out var collectError))
            return FailAndClean(requestDirectory, collectError);

        if (!VerifyListedFiles(output["files"] as JsonArray, workDirectory, files, out var verifyError))
            return FailAndClean(requestDirectory, verifyError);

        if (ReportsContainLocalPath(workDirectory, files, outputRoot))
            return FailAndClean(requestDirectory, "Generator report contained a local path.");

        var temporaryZip = zipPath + ".tmp";
        DeterministicZip.Create(workDirectory, files, temporaryZip);
        File.Move(temporaryZip, zipPath);

        var zipSha256 = DeterministicZip.Sha256Hex(zipPath);
        var zipSize = new FileInfo(zipPath).Length;

        var result = new JsonObject
        {
            ["output_slot"] = scope.OutputSlot,
            ["request_id"] = requestId,
            ["zip"] = new JsonObject
            {
                ["path"] = zipRelativePath,
                ["sha256"] = zipSha256,
                ["size"] = zipSize,
            },
            ["pages"] = pages,
            ["file_count"] = files.Count,
            ["validation_digest"] = validationDigest,
            ["generator_version"] = generatorVersion,
            ["catalog_sha256"] = catalogSha256,
        };

        var resultJson = result.ToJsonString();
        if (LocalPathGuard.ContainsLocalPath(resultJson, outputRoot, _options.ToolsRoot))
            return FailAndClean(requestDirectory, "Generation result contained a local path and was withheld.");

        var temporaryResult = resultPath + ".tmp";
        await File.WriteAllTextAsync(temporaryResult, resultJson, new UTF8Encoding(false), ct);
        File.Move(temporaryResult, resultPath);

        TryDeleteDirectory(workDirectory);

        _logger.LogInformation(
            "Generated {ZipPath} for {RequestId}: {FileCount} files, {Size} bytes.",
            zipRelativePath, requestId, files.Count, zipSize);
        return (true, resultJson, null);
    }

    /// <summary>先前的結果仍完整時取用：result.json 的 slot、requestId 與 zip 路徑相符，zip 的 sha256 與大小也相符。</summary>
    private bool TryReuseResult(
        string resultPath,
        string zipPath,
        GenerationScope scope,
        string requestId,
        string zipRelativePath,
        out string cached)
    {
        cached = string.Empty;
        try
        {
            if (!File.Exists(resultPath) || !File.Exists(zipPath) || IsLink(resultPath) || IsLink(zipPath))
                return false;

            var text = File.ReadAllText(resultPath, Encoding.UTF8);
            if (JsonNode.Parse(text) is not JsonObject stored ||
                stored["zip"] is not JsonObject zip ||
                !StringEquals(stored["output_slot"], scope.OutputSlot) ||
                !StringEquals(stored["request_id"], requestId) ||
                !StringEquals(zip["path"], zipRelativePath) ||
                zip["sha256"] is not JsonValue shaValue ||
                !shaValue.TryGetValue<string>(out var storedSha) ||
                zip["size"] is not JsonValue sizeValue ||
                !sizeValue.TryGetValue<long>(out var storedSize))
            {
                return false;
            }

            if (new FileInfo(zipPath).Length != storedSize ||
                !string.Equals(DeterministicZip.Sha256Hex(zipPath), storedSha, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            cached = text;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Stored result for {RequestId} could not be reused.", requestId);
            return false;
        }
    }

    /// <summary>
    /// 只保留每頁的 id、type 與 field_count（生成器的其他欄位不轉交）。
    /// </summary>
    private static bool TryReadPages(JsonObject output, out JsonArray pages)
    {
        pages = new JsonArray();
        if (output["pages"] is not JsonArray source)
            return false;

        foreach (var item in source)
        {
            if (item is not JsonObject page ||
                page["id"] is not JsonValue idValue ||
                !idValue.TryGetValue<string>(out var id) ||
                !IsPlainToken(id, 64))
            {
                return false;
            }

            var copy = new JsonObject { ["id"] = id };
            if (page["type"] is JsonValue typeValue && typeValue.TryGetValue<string>(out var type) && IsPlainToken(type, 32))
                copy["type"] = type;
            if (page["field_count"] is JsonValue countValue && countValue.TryGetValue<int>(out var fieldCount) && fieldCount >= 0)
                copy["field_count"] = fieldCount;
            pages.Add(copy);
        }

        return true;
    }

    private static bool TryReadToken(JsonObject output, string name, out string value)
    {
        value = string.Empty;
        if (output[name] is not JsonValue node || !node.TryGetValue<string>(out var text) || !IsPlainToken(text, 200))
            return false;
        value = text;
        return true;
    }

    private static bool IsPlainToken(string value, int maxLength)
        => value.Length > 0 &&
           value.Length <= maxLength &&
           !value.Any(char.IsControl) &&
           !value.Contains('\\') &&
           !LocalPathGuard.LooksAbsolute(value);

    /// <summary>
    /// 列出 work 目錄中的所有檔案（以 / 分隔的相對路徑）。頂層只能是 site 與 report 兩個目錄；
    /// 任何符號連結或 junction 都拒絕；檔案數與總大小有上限。
    /// </summary>
    private bool TryCollectPackageFiles(string workDirectory, out List<string> files, out string error)
    {
        files = new List<string>();
        error = string.Empty;

        var root = new DirectoryInfo(workDirectory);
        if (!root.Exists || IsLink(root))
        {
            error = "Generator did not produce an output directory.";
            return false;
        }

        var topLevel = root.EnumerateFileSystemInfos().ToList();
        var topNames = topLevel.Select(info => info.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!topNames.SequenceEqual(PackageTopLevel.OrderBy(name => name, StringComparer.Ordinal), StringComparer.Ordinal) ||
            topLevel.Any(info => info is not DirectoryInfo))
        {
            error = "Generator output must contain exactly the site/ and report/ directories.";
            return false;
        }

        long totalBytes = 0;
        var pending = new Stack<(DirectoryInfo Directory, string Relative)>();
        foreach (var directory in topLevel.Cast<DirectoryInfo>())
            pending.Push((directory, directory.Name));

        while (pending.Count > 0)
        {
            var (directory, relative) = pending.Pop();
            if (IsLink(directory))
            {
                error = "Generator output contains a link.";
                return false;
            }

            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                if (IsLink(entry))
                {
                    error = "Generator output contains a link.";
                    return false;
                }

                var entryRelative = $"{relative}/{entry.Name}";
                if (!LocalPathGuard.IsSafeRelativePath(entryRelative))
                {
                    error = "Generator output contains an unsupported file name.";
                    return false;
                }

                if (entry is DirectoryInfo childDirectory)
                {
                    pending.Push((childDirectory, entryRelative));
                    continue;
                }

                if (entry is not FileInfo file)
                    continue;

                totalBytes += file.Length;
                files.Add(entryRelative);
                if (files.Count > _options.MaxPackageFiles || totalBytes > _options.MaxPackageBytes)
                {
                    error = "Generator output exceeds the package size limits.";
                    return false;
                }
            }
        }

        if (!files.Any(path => path.StartsWith("site/", StringComparison.Ordinal)))
        {
            error = "Generator output has no site files.";
            return false;
        }

        files.Sort(StringComparer.Ordinal);
        return true;
    }

    /// <summary>
    /// 生成器回報的每個檔案都要在 work 目錄中，且大小與 sha256 相符；
    /// 目錄中沒被列出的檔案只能在 report/ 之下（例如記錄清單本身的 manifest）。
    /// </summary>
    private static bool VerifyListedFiles(JsonArray? listed, string workDirectory, List<string> files, out string error)
    {
        error = string.Empty;
        if (listed == null)
        {
            error = "Generator result has no files list.";
            return false;
        }

        var onDisk = new HashSet<string>(files, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in listed)
        {
            if (item is not JsonObject entry ||
                entry["path"] is not JsonValue pathValue ||
                !pathValue.TryGetValue<string>(out var path) ||
                !LocalPathGuard.IsSafeRelativePath(path) ||
                !(path.StartsWith("site/", StringComparison.Ordinal) || path.StartsWith("report/", StringComparison.Ordinal)) ||
                entry["sha256"] is not JsonValue shaValue ||
                !shaValue.TryGetValue<string>(out var sha) ||
                entry["size"] is not JsonValue sizeValue ||
                !sizeValue.TryGetValue<long>(out var size))
            {
                error = "Generator files list has an invalid entry.";
                return false;
            }

            if (!onDisk.Contains(path) || !seen.Add(path))
            {
                error = "Generator files list does not match the output directory.";
                return false;
            }

            var fullPath = Path.Combine(workDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            if (new FileInfo(fullPath).Length != size ||
                !string.Equals(DeterministicZip.Sha256Hex(fullPath), sha, StringComparison.OrdinalIgnoreCase))
            {
                error = "Generator files list does not match the written files.";
                return false;
            }
        }

        if (files.Any(path => !seen.Contains(path) && !path.StartsWith("report/", StringComparison.Ordinal)))
        {
            error = "Output directory contains files the generator did not report.";
            return false;
        }

        return true;
    }

    /// <summary>report/ 之下的檔案會交到使用者手上，不可含這台主機的路徑（例如生成時的輸出目錄）。</summary>
    private bool ReportsContainLocalPath(string workDirectory, List<string> files, string outputRoot)
    {
        foreach (var path in files.Where(path => path.StartsWith("report/", StringComparison.Ordinal)))
        {
            var fullPath = Path.Combine(workDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            var info = new FileInfo(fullPath);
            if (info.Length > 4 * 1024 * 1024)
                return true;

            var text = File.ReadAllText(fullPath, Encoding.UTF8);
            if (LocalPathGuard.ContainsLocalPath(text, outputRoot, _options.ToolsRoot))
                return true;
        }

        return false;
    }

    /// <summary>驗證失敗時的錯誤訊息：<c>{"ok":false,"errors":[...]}</c>，最多 50 筆、16KB，不含本機路徑。</summary>
    private string StructuredErrors(JsonArray? errors, string outputRoot)
    {
        var copy = new JsonArray();
        if (errors != null)
        {
            foreach (var error in errors.Take(MaxReportedErrors))
                copy.Add(error?.DeepClone());
        }

        var message = new JsonObject { ["ok"] = false, ["errors"] = copy };
        var json = message.ToJsonString();
        while (Encoding.UTF8.GetByteCount(json) > MaxErrorMessageBytes && copy.Count > 1)
        {
            copy.RemoveAt(copy.Count - 1);
            message["truncated"] = true;
            json = message.ToJsonString();
        }

        if (LocalPathGuard.ContainsLocalPath(json, outputRoot, _options.ToolsRoot))
        {
            _logger.LogWarning("Generator validation errors contained a local path and were withheld.");
            return """{"ok":false,"errors":[{"code":"GENERATION_FAILED","path":"","message":"The definition was rejected; details were withheld because they referenced a local path.","hint":"Run validate_definition and fix the reported errors."}]}""";
        }

        return json;
    }

    private (bool, string?, string?) FailAndClean(string requestDirectory, string message)
    {
        TryDeleteDirectory(requestDirectory);
        return Fail(message);
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !IsLink(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove a generation work directory.");
        }
    }

    private static bool StringEquals(JsonNode? node, string expected)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && string.Equals(text, expected, StringComparison.Ordinal);

    private static bool IsStrictlyInside(string root, string candidate)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, comparison) && normalizedCandidate.Length > normalizedRoot.Length;
    }

    private static bool IsLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            return info.Exists && IsLink(info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsLink(FileSystemInfo info)
        => info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget != null;

    private static (bool, string?, string?) Fail(string message) => (false, null, message);
}
