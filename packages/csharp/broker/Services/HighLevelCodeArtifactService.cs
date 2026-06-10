using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO.Compression;

namespace Broker.Services;

public sealed class HighLevelCodeArtifactResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string EntryFilePath { get; set; } = string.Empty;
    public string EntryFileName { get; set; } = "index.html";
    public string PackageFilePath { get; set; } = string.Empty;
    public string DeliveredFileName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool UploadedToGoogleDrive { get; set; }
    public LineArtifactDeliveryResult? Delivery { get; set; }
}

public sealed class HighLevelCodeArtifactService
{
    private readonly HighLevelLlmOptions _highLevelLlmOptions;
    private readonly GoogleDriveShareService _googleDriveShareService;
    private readonly LineArtifactDeliveryService _artifactDeliveryService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HighLevelCodeArtifactService> _logger;

    public HighLevelCodeArtifactService(
        HighLevelLlmOptions highLevelLlmOptions,
        GoogleDriveShareService googleDriveShareService,
        LineArtifactDeliveryService artifactDeliveryService,
        IHttpClientFactory httpClientFactory,
        ILogger<HighLevelCodeArtifactService> logger)
    {
        _highLevelLlmOptions = highLevelLlmOptions;
        _googleDriveShareService = googleDriveShareService;
        _artifactDeliveryService = artifactDeliveryService;
        _httpClient = httpClientFactory.CreateClient("high-level-llm");
        _logger = logger;
    }

    public async Task<HighLevelCodeArtifactResult> GenerateAndDeliverAsync(
        HighLevelTaskDraft draft,
        HighLevelUserProfile profile,
        string relatedTaskId,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(draft.TaskType, "code_gen", StringComparison.OrdinalIgnoreCase))
            return Fail("draft is not a code_gen task.");

        if (string.IsNullOrWhiteSpace(draft.ManagedPaths.ProjectRoot))
            return Fail("project root is not available.");

        Directory.CreateDirectory(draft.ManagedPaths.ProjectRoot);

        if (IsWebsiteReplicaRequest(draft))
        {
            var replica = await TryGenerateStaticWebsiteReplicaAsync(draft, cancellationToken);
            if (!replica.Success)
                return Fail(replica.Message);

            return await DeliverStaticWebsiteReplicaAsync(draft, profile, relatedTaskId, replica, cancellationToken);
        }

        CopyCustomComponentRuntimeIfAvailable(draft.ManagedPaths.ProjectRoot);

        var html = TryBuildDeterministicHtml(draft) ?? await GenerateProjectHtmlAsync(draft, cancellationToken);
        if (string.IsNullOrWhiteSpace(html))
            html = BuildFallbackHtml(draft);

        html = NormalizeHtml(html, draft);

        var entryFilePath = Path.Combine(draft.ManagedPaths.ProjectRoot, "index.html");
        await File.WriteAllTextAsync(entryFilePath, html, new UTF8Encoding(false), cancellationToken);

        var deliveredFileName = BuildDeliveredFileName(draft);
        var identityMode = _googleDriveShareService.ResolveIdentityMode(null);
        var uploadToGoogleDrive = _googleDriveShareService.CanUpload(identityMode, draft.Channel, draft.UserId);

        var delivery = await _artifactDeliveryService.GenerateAndDeliverAsync(new LineArtifactDeliveryRequest
        {
            UserId = profile.UserId,
            FileName = deliveredFileName,
            Format = "html",
            Content = html,
            UploadToGoogleDrive = uploadToGoogleDrive,
            IdentityMode = identityMode,
            FolderId = string.Empty,
            ShareMode = string.Empty,
            SendLineNotification = true,
            NotificationTitle = "網站原型已生成",
            Source = "high_level_code_gen",
            RelatedTaskType = draft.TaskType,
            RelatedDraftId = draft.DraftId,
            RelatedTaskId = relatedTaskId
        }, cancellationToken);

        return new HighLevelCodeArtifactResult
        {
            Success = delivery.Success,
            Message = delivery.Success
                ? (uploadToGoogleDrive ? "project_created_and_uploaded" : "project_created_locally_only")
                : delivery.Message,
            ProjectRoot = draft.ManagedPaths.ProjectRoot,
            EntryFilePath = entryFilePath,
            EntryFileName = "index.html",
            DeliveredFileName = deliveredFileName,
            Content = html,
            UploadedToGoogleDrive = uploadToGoogleDrive && delivery.Success,
            Delivery = delivery
        };
    }

    private async Task<string?> GenerateProjectHtmlAsync(
        HighLevelTaskDraft draft,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = BuildPrompt(draft);
            var raw = await CallLlmAsync(prompt, cancellationToken);
            return ExtractHtml(raw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "High-level code artifact generation failed for {UserId}", draft.UserId);
            return null;
        }
    }

    private async Task<StaticWebsiteReplicaResult> TryGenerateStaticWebsiteReplicaAsync(
        HighLevelTaskDraft draft,
        CancellationToken cancellationToken)
    {
        var startUrl = ExtractFirstHttpUrl(draft.OriginalMessage);
        if (string.IsNullOrWhiteSpace(startUrl))
            return StaticWebsiteReplicaResult.Fail("site_replica_missing_url");

        var repoRoot = FindRepositoryRoot();
        if (string.IsNullOrWhiteSpace(repoRoot))
            return StaticWebsiteReplicaResult.Fail("site_replica_generator_not_found");

        var crawlScript = Path.Combine(repoRoot, "tools", "scripts", "crawl-site-to-generator-json.mjs");
        var generatorScript = Path.Combine(repoRoot, "tools", "scripts", "generate-static-site-from-crawl-bundle.mjs");
        if (!File.Exists(crawlScript) || !File.Exists(generatorScript))
            return StaticWebsiteReplicaResult.Fail("site_replica_generator_scripts_missing");

        var outputRoot = draft.ManagedPaths.ProjectRoot;
        var crawlRoot = Path.Combine(Path.GetTempPath(), "bricks4agent-site-crawl", Guid.NewGuid().ToString("N"));
        var options = ResolveStaticReplicaOptions(draft, startUrl);

        try
        {
            Directory.CreateDirectory(crawlRoot);
            var crawlResult = await RunNodeScriptAsync(
                repoRoot,
                crawlScript,
                new[]
                {
                    "--url", startUrl,
                    "--output", crawlRoot,
                    "--max-depth", options.MaxDepth.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--max-pages", options.MaxPages.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "--domain-suffix", options.DomainSuffix,
                },
                TimeSpan.FromMinutes(12),
                cancellationToken);

            if (!crawlResult.Success)
                return StaticWebsiteReplicaResult.Fail($"site_replica_crawl_failed: {crawlResult.Message}");

            var bundlePath = Path.Combine(crawlRoot, "site-generator-bundle.json");
            if (!File.Exists(bundlePath))
                return StaticWebsiteReplicaResult.Fail("site_replica_bundle_missing");

            var generateResult = await RunNodeScriptAsync(
                repoRoot,
                generatorScript,
                new[]
                {
                    "--bundle", bundlePath,
                    "--output", outputRoot,
                },
                TimeSpan.FromMinutes(8),
                cancellationToken);

            if (!generateResult.Success)
                return StaticWebsiteReplicaResult.Fail($"site_replica_generation_failed: {generateResult.Message}");

            var entryFilePath = Path.Combine(outputRoot, "index.html");
            var appFilePath = Path.Combine(outputRoot, "app.js");
            var dataFilePath = Path.Combine(outputRoot, "data", "site-data.js");
            var styleFilePath = Path.Combine(outputRoot, "styles", "site.css");
            var componentIndexPath = Path.Combine(outputRoot, "runtime", "ui_components", "index.js");
            if (!File.Exists(entryFilePath) ||
                !File.Exists(appFilePath) ||
                !File.Exists(dataFilePath) ||
                !File.Exists(styleFilePath) ||
                !File.Exists(componentIndexPath))
            {
                return StaticWebsiteReplicaResult.Fail("site_replica_output_incomplete");
            }

            var pageCount = ReadGeneratedPageCount(outputRoot);
            if (pageCount <= 1)
                return StaticWebsiteReplicaResult.Fail($"site_replica_page_count_too_low: {pageCount}");

            var packagePath = Path.Combine(
                Path.GetDirectoryName(outputRoot) ?? outputRoot,
                $"{SanitizeFileStem(draft.ProjectFolderName, "site-replica")}.zip");
            if (File.Exists(packagePath))
                File.Delete(packagePath);
            ZipFile.CreateFromDirectory(outputRoot, packagePath, CompressionLevel.Optimal, includeBaseDirectory: false);

            return StaticWebsiteReplicaResult.Ok(
                entryFilePath,
                packagePath,
                await File.ReadAllTextAsync(entryFilePath, Encoding.UTF8, cancellationToken),
                pageCount);
        }
        catch (OperationCanceledException)
        {
            return StaticWebsiteReplicaResult.Fail("site_replica_generation_timeout");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Static website replica generation failed for {UserId}", draft.UserId);
            return StaticWebsiteReplicaResult.Fail($"site_replica_exception: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(crawlRoot))
                    Directory.Delete(crawlRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup only; the generator output is already in the managed project root.
            }
        }
    }

    private async Task<HighLevelCodeArtifactResult> DeliverStaticWebsiteReplicaAsync(
        HighLevelTaskDraft draft,
        HighLevelUserProfile profile,
        string relatedTaskId,
        StaticWebsiteReplicaResult replica,
        CancellationToken cancellationToken)
    {
        var deliveredFileName = $"{SanitizeFileStem(draft.ProjectFolderName, "site-replica")}.zip";
        var identityMode = _googleDriveShareService.ResolveIdentityMode(null);
        var uploadToGoogleDrive = _googleDriveShareService.CanUpload(identityMode, draft.Channel, draft.UserId);

        var delivery = await _artifactDeliveryService.DeliverExistingFileAsync(new LineExistingArtifactDeliveryRequest
        {
            UserId = profile.UserId,
            FilePath = replica.PackageFilePath,
            FileName = deliveredFileName,
            UploadToGoogleDrive = uploadToGoogleDrive,
            IdentityMode = identityMode,
            FolderId = string.Empty,
            ShareMode = string.Empty,
            SendLineNotification = true,
            NotificationTitle = "離線網站已生成",
            Source = "high_level_static_site_replica",
            RelatedTaskType = draft.TaskType,
            RelatedDraftId = draft.DraftId,
            RelatedTaskId = relatedTaskId
        }, cancellationToken);

        return new HighLevelCodeArtifactResult
        {
            Success = delivery.Success,
            Message = delivery.Success
                ? (uploadToGoogleDrive ? "static_site_created_and_uploaded" : "static_site_created_locally_only")
                : delivery.Message,
            ProjectRoot = draft.ManagedPaths.ProjectRoot,
            EntryFilePath = replica.EntryFilePath,
            EntryFileName = "index.html",
            PackageFilePath = replica.PackageFilePath,
            DeliveredFileName = deliveredFileName,
            Content = replica.Content,
            UploadedToGoogleDrive = uploadToGoogleDrive && delivery.Success,
            Delivery = delivery
        };
    }

    private static bool IsWebsiteReplicaRequest(HighLevelTaskDraft draft)
    {
        var text = $"{draft.OriginalMessage}\n{draft.Summary}".ToLowerInvariant();
        if (!Regex.IsMatch(text, @"https?://", RegexOptions.IgnoreCase))
            return false;

        var hasReplicaIntent =
            text.Contains("參照", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("重製", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("仿製", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("離線", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("offline", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("crawl", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("爬蟲", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("bfs", StringComparison.OrdinalIgnoreCase);

        var hasWebsiteIntent =
            text.Contains("網站", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("網頁", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("website", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("site", StringComparison.OrdinalIgnoreCase);

        return hasReplicaIntent && hasWebsiteIntent;
    }

    private static string? ExtractFirstHttpUrl(string text)
    {
        var match = Regex.Match(text ?? string.Empty, @"https?://[^\s，,；;。)）\]】>""']+", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.TrimEnd('/', '。', '.', '，', ',', '；', ';') + "/" : null;
    }

    private static StaticReplicaOptions ResolveStaticReplicaOptions(HighLevelTaskDraft draft, string startUrl)
    {
        var text = $"{draft.OriginalMessage}\n{draft.Summary}";
        var maxDepth = ReadBoundedNumber(text, @"(?:深度|depth)\s*[:：]?\s*(?<value>\d+)", 3, 0, 5);
        var maxPages = ReadBoundedNumber(text, @"(?:最多|上限|max-pages|max pages|pages|頁)\D{0,8}(?<value>\d+)", 200, 1, 200);
        var host = new Uri(startUrl).Host.ToLowerInvariant();
        var domainSuffix = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        return new StaticReplicaOptions(maxDepth, maxPages, domainSuffix);
    }

    private static int ReadBoundedNumber(string text, string pattern, int defaultValue, int min, int max)
    {
        var match = Regex.Match(text ?? string.Empty, pattern, RegexOptions.IgnoreCase);
        if (!match.Success || !int.TryParse(match.Groups["value"].Value, out var value))
            return defaultValue;
        return Math.Clamp(value, min, max);
    }

    private static async Task<ProcessRunResult> RunNodeScriptAsync(
        string workingDirectory,
        string scriptPath,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveNodeExecutable(),
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process == null)
            return ProcessRunResult.Fail("node_process_not_started");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
                return ProcessRunResult.Fail(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
            return ProcessRunResult.Ok(stdout);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process cleanup is best effort after timeout/cancellation.
            }
            throw;
        }
    }

    private static string ResolveNodeExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("B4A_NODE_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var bundled = Path.Combine(
                userProfile,
                ".cache",
                "codex-runtimes",
                "codex-primary-runtime",
                "dependencies",
                "node",
                "bin",
                OperatingSystem.IsWindows() ? "node.exe" : "node");
            if (File.Exists(bundled))
                return bundled;
        }

        return OperatingSystem.IsWindows() ? "node.exe" : "node";
    }

    private static int ReadGeneratedPageCount(string outputRoot)
    {
        var snapshotPath = Path.Combine(outputRoot, "site-generator-bundle.snapshot.json");
        if (!File.Exists(snapshotPath))
            return 0;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(snapshotPath, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("extracted_site_model", out var model) &&
                model.TryGetProperty("pages", out var pages) &&
                pages.ValueKind == JsonValueKind.Array)
            {
                return pages.GetArrayLength();
            }
        }
        catch
        {
            return 0;
        }

        return 0;
    }

    private string BuildPrompt(HighLevelTaskDraft draft)
    {
        return string.Join('\n', new[]
        {
            "你要產生一個可以直接打開的單檔 HTML 網頁原型。",
            "請輸出完整、可執行、UTF-8 友善的 HTML。",
            "限制：",
            "- 只能輸出單一 HTML 檔",
            "- CSS 與 JavaScript 必須內嵌在同一檔案",
            "- 不要輸出 Markdown 說明",
            "- 不要輸出 code fence",
            "- 頁面要可直接在本機瀏覽器開啟",
            "- 任何網頁程式都必須優先使用專案自訂元件庫；元件可從 ./runtime/ui_components/index.js 匯入，樣式可載入 ./runtime/ui_components/theme.css",
            "- 若自訂元件庫已有相符元件，必須使用它，例如 BasicButton、ButtonGroup、FeatureCard、PhotoCard、ImageViewer、SideMenu、TabContainer、DataTable、InfoPanel、PhotoWall",
            "- 只有在元件庫沒有相符元件時，才可以手寫原生 HTML/CSS/JS 補足特定互動或遊戲邏輯",
            "- 如果需求是計算機，必須真的可計算",
            string.Empty,
            $"title: {draft.Title}",
            $"summary: {draft.Summary}",
            $"project_name: {draft.ProjectName}",
            "user_request:",
            draft.OriginalMessage
        });
    }

    private async Task<string?> CallLlmAsync(string prompt, CancellationToken ct)
    {
        var provider = (_highLevelLlmOptions.Provider ?? "ollama").Trim().ToLowerInvariant();
        return provider switch
        {
            "ollama" => await SendOllamaChatAsync(prompt, ct),
            _ => string.Equals(_highLevelLlmOptions.ApiFormat, "responses", StringComparison.OrdinalIgnoreCase)
                ? await SendResponsesApiAsync(prompt, ct)
                : await SendChatCompletionsAsync(prompt, ct)
        };
    }

    private async Task<string?> SendOllamaChatAsync(string prompt, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            },
            ["stream"] = false
        };

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("api/chat", content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString()?.Trim();
    }

    private async Task<string?> SendChatCompletionsAsync(string prompt, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            },
            ["stream"] = false
        };

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("v1/chat/completions", content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return null;

        return choices[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
    }

    private async Task<string?> SendResponsesApiAsync(string prompt, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["input"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "input_text",
                            ["text"] = prompt
                        }
                    }
                }
            }
        };

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("v1/responses", content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        if (doc.RootElement.TryGetProperty("output_text", out var outputTextNode))
            return outputTextNode.GetString()?.Trim();

        return null;
    }

    private static string? ExtractHtml(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var trimmed = raw.Trim();
        var fenced = Regex.Match(trimmed, "```(?:html)?\\s*(?<content>[\\s\\S]*?)```", RegexOptions.IgnoreCase);
        if (fenced.Success)
            trimmed = fenced.Groups["content"].Value.Trim();

        return trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
               trimmed.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : null;
    }

    private static string NormalizeHtml(string html, HighLevelTaskDraft draft)
    {
        var trimmed = html.Trim();
        if (!trimmed.Contains("<meta charset=", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Replace("<head>", "<head>\n<meta charset=\"utf-8\">", StringComparison.OrdinalIgnoreCase);
        }

        if (!trimmed.Contains("<title>", StringComparison.OrdinalIgnoreCase))
        {
            var title = string.IsNullOrWhiteSpace(draft.ProjectName) ? "Generated Prototype" : draft.ProjectName.Trim();
            trimmed = trimmed.Replace("<head>", $"<head>\n<title>{System.Net.WebUtility.HtmlEncode(title)}</title>", StringComparison.OrdinalIgnoreCase);
        }

        return trimmed;
    }

    private static void CopyCustomComponentRuntimeIfAvailable(string projectRoot)
    {
        var source = FindCustomComponentLibraryRoot();
        if (string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
            return;

        var destination = Path.Combine(projectRoot, "runtime", "ui_components");
        CopyDirectory(source, destination);
    }

    private static string? FindCustomComponentLibraryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var depth = 0; dir != null && depth < 10; depth++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "packages", "javascript", "browser", "ui_components");
                if (File.Exists(Path.Combine(candidate, "index.js")))
                    return candidate;
            }
        }

        return null;
    }

    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var depth = 0; dir != null && depth < 10; depth++, dir = dir.Parent)
            {
                var crawlScript = Path.Combine(dir.FullName, "tools", "scripts", "crawl-site-to-generator-json.mjs");
                var generatorScript = Path.Combine(dir.FullName, "tools", "scripts", "generate-static-site-from-crawl-bundle.mjs");
                if (File.Exists(crawlScript) && File.Exists(generatorScript))
                    return dir.FullName;
            }
        }

        return null;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, sourceFile);
            var destinationFile = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }

    private static string BuildDeliveredFileName(HighLevelTaskDraft draft)
    {
        var baseName = string.IsNullOrWhiteSpace(draft.ProjectFolderName)
            ? "prototype"
            : draft.ProjectFolderName.Trim();
        return $"{baseName}.html";
    }

    private static string SanitizeFileStem(string? value, string fallback)
    {
        var stem = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            stem = stem.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(stem) ? fallback : stem;
    }

    private static string BuildFallbackHtml(HighLevelTaskDraft draft)
    {
        var deterministicHtml = TryBuildDeterministicHtml(draft);
        if (!string.IsNullOrWhiteSpace(deterministicHtml))
            return deterministicHtml;

        if (ContainsCalculatorIntent(draft.OriginalMessage))
            return BuildCalculatorHtml(draft);

        var title = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(draft.ProjectName) ? "Generated Prototype" : draft.ProjectName.Trim());
        var summary = System.Net.WebUtility.HtmlEncode(draft.Summary);

        return $$"""
<!DOCTYPE html>
<html lang="zh-Hant">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <link rel="stylesheet" href="./runtime/ui_components/theme.css">
  <title>{{title}}</title>
  <style>
    :root { color-scheme: light; --bg:#f5f1e8; --panel:#fffdf8; --ink:#1f2430; --accent:#bc5f04; --muted:#6e6256; }
    * { box-sizing:border-box; }
    body { margin:0; font-family:"Segoe UI","Noto Sans TC",sans-serif; background:radial-gradient(circle at top,#fff6dd,#f5f1e8 55%); color:var(--ink); }
    .wrap { min-height:100vh; display:grid; place-items:center; padding:32px; }
    .card { max-width:860px; width:100%; background:var(--panel); border:1px solid #eadbc2; border-radius:24px; padding:36px; box-shadow:0 24px 80px rgba(94,65,20,.14); }
    h1 { margin:0 0 12px; font-size:clamp(2rem,4vw,3.6rem); line-height:1.05; }
    p { color:var(--muted); font-size:1.05rem; line-height:1.75; }
    .pill { display:inline-block; padding:6px 12px; border-radius:999px; background:#fff2d8; color:var(--accent); font-weight:700; margin-bottom:18px; }
    .grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(220px,1fr)); gap:16px; margin-top:28px; }
    .panel { border-radius:18px; padding:18px; background:#fff8ed; border:1px solid #f0ddba; }
  </style>
</head>
<body>
  <main class="wrap">
    <section class="card">
      <div class="pill">Generated Web Prototype</div>
      <h1>{{title}}</h1>
      <p>{{summary}}</p>
      <div class="grid">
        <article class="panel">
          <strong>狀態</strong>
          <p>這是目前的最小可用網站原型。後續可以再接完整生成流程與更多頁面。</p>
        </article>
        <article class="panel">
          <strong>交付</strong>
          <p>目前提供可直接開啟的單檔 HTML，已寫入專案目錄並可進一步交付。</p>
        </article>
      </div>
    </section>
  </main>
</body>
</html>
""";
    }

    private static string? TryBuildDeterministicHtml(HighLevelTaskDraft draft)
    {
        if (ContainsTicTacToeIntent(draft.OriginalMessage) || ContainsTicTacToeIntent(draft.Summary))
            return BuildTicTacToeHtml(draft);

        if (ContainsCalculatorIntent(draft.OriginalMessage) || ContainsCalculatorIntent(draft.Summary))
            return BuildCalculatorHtml(draft);

        return null;
    }

    private static string BuildTicTacToeHtml(HighLevelTaskDraft draft)
    {
        var title = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(draft.ProjectName) ? "井字遊戲" : draft.ProjectName.Trim());
        return $$"""
<!DOCTYPE html>
<html lang="zh-Hant">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <link rel="stylesheet" href="./runtime/ui_components/theme.css">
  <title>{{title}}</title>
  <style>
    :root {
      color-scheme: light;
      --bg: #101820;
      --surface: #f8fafc;
      --surface-2: #eef6f6;
      --ink: #17202a;
      --muted: #61717d;
      --line: #c7d4dc;
      --x: #007d8a;
      --o: #c43d51;
      --win: #f0c94b;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      padding: 24px;
      background:
        linear-gradient(135deg, rgba(0, 125, 138, .24), transparent 36%),
        linear-gradient(315deg, rgba(196, 61, 81, .22), transparent 42%),
        var(--bg);
      color: var(--ink);
      font-family: "Segoe UI", "Noto Sans TC", sans-serif;
    }
    main {
      width: min(94vw, 760px);
      display: grid;
      gap: 18px;
      background: var(--surface);
      border: 1px solid rgba(255, 255, 255, .24);
      border-radius: 8px;
      padding: clamp(18px, 4vw, 32px);
      box-shadow: 0 24px 72px rgba(0, 0, 0, .35);
    }
    header {
      display: flex;
      justify-content: space-between;
      align-items: end;
      gap: 16px;
      flex-wrap: wrap;
      border-bottom: 1px solid var(--line);
      padding-bottom: 14px;
    }
    h1 {
      margin: 0;
      font-size: clamp(2rem, 5vw, 3.1rem);
      line-height: 1.05;
      letter-spacing: 0;
    }
    .status {
      min-height: 44px;
      display: flex;
      align-items: center;
      padding: 10px 12px;
      border: 1px solid var(--line);
      border-radius: 8px;
      background: var(--surface-2);
      color: var(--ink);
      font-weight: 700;
    }
    .game {
      display: grid;
      grid-template-columns: minmax(250px, 1fr) minmax(180px, 240px);
      gap: 20px;
      align-items: start;
    }
    .board {
      aspect-ratio: 1;
      display: grid;
      grid-template-columns: repeat(3, 1fr);
      gap: 10px;
      width: 100%;
    }
    .cell {
      aspect-ratio: 1;
      border: 2px solid var(--line);
      border-radius: 8px;
      background: #ffffff;
      color: var(--ink);
      font-size: clamp(2.5rem, 12vw, 5.7rem);
      font-weight: 900;
      line-height: 1;
      cursor: pointer;
      transition: transform .12s ease, border-color .12s ease, background .12s ease;
    }
    .cell:hover:not(:disabled), .cell:focus-visible {
      border-color: var(--x);
      transform: translateY(-2px);
      outline: none;
    }
    .cell:disabled { cursor: default; }
    .cell.x { color: var(--x); }
    .cell.o { color: var(--o); }
    .cell.win {
      background: color-mix(in srgb, var(--win) 34%, white);
      border-color: var(--win);
    }
    .side {
      display: grid;
      gap: 12px;
    }
    .score {
      display: grid;
      gap: 8px;
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 14px;
      background: var(--surface-2);
    }
    .score-row {
      display: flex;
      justify-content: space-between;
      gap: 12px;
      font-weight: 700;
    }
    .score-row span:first-child { color: var(--muted); }
    button.reset {
      min-height: 44px;
      border: 0;
      border-radius: 8px;
      background: var(--ink);
      color: #ffffff;
      font-weight: 800;
      cursor: pointer;
    }
    button.reset:hover { background: #263342; }
    @media (max-width: 680px) {
      body { padding: 14px; }
      .game { grid-template-columns: 1fr; }
      .side { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <main>
    <header>
      <h1>{{title}}</h1>
      <div id="status" class="status" role="status" aria-live="polite">輪到 X</div>
    </header>

    <section class="game" aria-label="井字遊戲棋盤">
      <div id="board" class="board">
        <button class="cell" data-cell="0" aria-label="第 1 格"></button>
        <button class="cell" data-cell="1" aria-label="第 2 格"></button>
        <button class="cell" data-cell="2" aria-label="第 3 格"></button>
        <button class="cell" data-cell="3" aria-label="第 4 格"></button>
        <button class="cell" data-cell="4" aria-label="第 5 格"></button>
        <button class="cell" data-cell="5" aria-label="第 6 格"></button>
        <button class="cell" data-cell="6" aria-label="第 7 格"></button>
        <button class="cell" data-cell="7" aria-label="第 8 格"></button>
        <button class="cell" data-cell="8" aria-label="第 9 格"></button>
      </div>

      <aside class="side">
        <div class="score" aria-label="計分板">
          <div class="score-row"><span>X 勝場</span><strong id="scoreX">0</strong></div>
          <div class="score-row"><span>O 勝場</span><strong id="scoreO">0</strong></div>
          <div class="score-row"><span>平手</span><strong id="scoreDraw">0</strong></div>
        </div>
        <button id="reset" class="reset" type="button">重新開始</button>
      </aside>
    </section>
  </main>

  <script type="module">
    const cells = [...document.querySelectorAll('[data-cell]')];
    const status = document.getElementById('status');
    const reset = document.getElementById('reset');
    const scoreX = document.getElementById('scoreX');
    const scoreO = document.getElementById('scoreO');
    const scoreDraw = document.getElementById('scoreDraw');
    const winningLines = [
      [0, 1, 2], [3, 4, 5], [6, 7, 8],
      [0, 3, 6], [1, 4, 7], [2, 5, 8],
      [0, 4, 8], [2, 4, 6]
    ];

    let board = Array(9).fill('');
    let current = 'X';
    let locked = false;
    const score = { X: 0, O: 0, draw: 0 };

    function render() {
      cells.forEach((cell, index) => {
        const value = board[index];
        cell.textContent = value;
        cell.disabled = locked || Boolean(value);
        cell.className = 'cell' + (value ? ' ' + value.toLowerCase() : '');
      });
      scoreX.textContent = score.X;
      scoreO.textContent = score.O;
      scoreDraw.textContent = score.draw;
    }

    function findWinner() {
      return winningLines.find(line => {
        const [a, b, c] = line;
        return board[a] && board[a] === board[b] && board[a] === board[c];
      });
    }

    function play(index) {
      if (locked || board[index]) return;
      board[index] = current;
      const winnerLine = findWinner();

      if (winnerLine) {
        locked = true;
        winnerLine.forEach(i => cells[i].classList.add('win'));
        score[current] += 1;
        status.textContent = current + ' 獲勝';
      } else if (board.every(Boolean)) {
        locked = true;
        score.draw += 1;
        status.textContent = '平手';
      } else {
        current = current === 'X' ? 'O' : 'X';
        status.textContent = '輪到 ' + current;
      }
      render();
      if (winnerLine) winnerLine.forEach(i => cells[i].classList.add('win'));
    }

    function newRound() {
      board = Array(9).fill('');
      current = 'X';
      locked = false;
      status.textContent = '輪到 X';
      render();
    }

    cells.forEach(cell => cell.addEventListener('click', () => play(Number(cell.dataset.cell))));
    reset.addEventListener('click', newRound);

    try {
      const { BasicButton } = await import('./runtime/ui_components/index.js');
      const replacementHost = document.createElement('div');
      reset.replaceWith(replacementHost);
      new BasicButton({
        type: 'reset',
        customLabel: '重新開始',
        fullWidth: true,
        onClick: newRound
      }).mount(replacementHost);
    } catch {
      reset.hidden = false;
    }

    render();
  </script>
</body>
</html>
""";
    }

    private static string BuildCalculatorHtml(HighLevelTaskDraft draft)
    {
        var title = System.Net.WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(draft.ProjectName) ? "Calculator" : draft.ProjectName.Trim());
        return $$"""
<!DOCTYPE html>
<html lang="zh-Hant">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>{{title}}</title>
  <style>
    :root { --bg:#171717; --panel:#232323; --key:#343434; --key-accent:#ff9f0a; --ink:#fff8ef; --muted:#b9b1a6; }
    * { box-sizing:border-box; }
    body { margin:0; min-height:100vh; display:grid; place-items:center; background:
      radial-gradient(circle at top,#3f2a14,#171717 55%); color:var(--ink); font-family:"Segoe UI","Noto Sans TC",sans-serif; }
    .calculator { width:min(92vw,360px); background:linear-gradient(180deg,#2b2b2b,#1c1c1c); border-radius:28px; padding:22px; box-shadow:0 32px 80px rgba(0,0,0,.45); border:1px solid rgba(255,255,255,.06); }
    .badge { color:var(--muted); text-transform:uppercase; letter-spacing:.14em; font-size:.76rem; margin-bottom:12px; }
    .display { width:100%; min-height:88px; padding:18px 16px; border-radius:18px; background:#111; display:flex; align-items:end; justify-content:end; font-size:2.6rem; overflow:auto; }
    .keys { margin-top:18px; display:grid; grid-template-columns:repeat(4,1fr); gap:12px; }
    button { border:none; border-radius:18px; padding:18px 0; font-size:1.18rem; font-weight:700; background:var(--key); color:var(--ink); cursor:pointer; }
    button:hover { filter:brightness(1.08); }
    button.op { background:#4b4b4b; }
    button.eq { background:var(--key-accent); color:#1b1308; }
    button.wide { grid-column:span 2; }
  </style>
</head>
<body>
  <main class="calculator">
    <div class="badge">{{title}}</div>
    <div id="display" class="display">0</div>
    <div class="keys">
      <button data-action="clear" class="op">AC</button>
      <button data-action="delete" class="op">⌫</button>
      <button data-value="%" class="op">%</button>
      <button data-value="/" class="eq">÷</button>
      <button data-value="7">7</button>
      <button data-value="8">8</button>
      <button data-value="9">9</button>
      <button data-value="*" class="eq">×</button>
      <button data-value="4">4</button>
      <button data-value="5">5</button>
      <button data-value="6">6</button>
      <button data-value="-" class="eq">−</button>
      <button data-value="1">1</button>
      <button data-value="2">2</button>
      <button data-value="3">3</button>
      <button data-value="+" class="eq">＋</button>
      <button data-value="0" class="wide">0</button>
      <button data-value=".">.</button>
      <button data-action="equals" class="eq">＝</button>
    </div>
  </main>
  <script>
    const display = document.getElementById('display');
    let expression = '0';

    function render() {
      display.textContent = expression || '0';
    }

    function append(value) {
      expression = expression === '0' && value !== '.' ? value : expression + value;
      render();
    }

    function clearAll() {
      expression = '0';
      render();
    }

    function backspace() {
      expression = expression.length <= 1 ? '0' : expression.slice(0, -1);
      render();
    }

    function evaluateExpression() {
      try {
        const sanitized = expression.replace(/%/g, '/100');
        const result = Function('"use strict"; return (' + sanitized + ')')();
        expression = Number.isFinite(result) ? String(result) : 'Error';
      } catch {
        expression = 'Error';
      }
      render();
    }

    document.querySelector('.keys').addEventListener('click', event => {
      const button = event.target.closest('button');
      if (!button) return;
      const action = button.dataset.action;
      const value = button.dataset.value;
      if (action === 'clear') return clearAll();
      if (action === 'delete') return backspace();
      if (action === 'equals') return evaluateExpression();
      if (expression === 'Error') expression = '0';
      if (value) append(value);
    });

    render();
  </script>
</body>
</html>
""";
    }

    private static bool ContainsCalculatorIntent(string message)
        => message.Contains("計算機", StringComparison.OrdinalIgnoreCase)
           || message.Contains("calculator", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsTicTacToeIntent(string message)
        => message.Contains("井字", StringComparison.OrdinalIgnoreCase)
           || message.Contains("圈叉", StringComparison.OrdinalIgnoreCase)
           || message.Contains("圈圈叉叉", StringComparison.OrdinalIgnoreCase)
           || message.Contains("tic tac toe", StringComparison.OrdinalIgnoreCase)
           || message.Contains("tic-tac-toe", StringComparison.OrdinalIgnoreCase)
           || message.Contains("noughts", StringComparison.OrdinalIgnoreCase);

    private static HighLevelCodeArtifactResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message
        };

    private sealed record StaticReplicaOptions(int MaxDepth, int MaxPages, string DomainSuffix);

    private sealed class ProcessRunResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;

        public static ProcessRunResult Ok(string message) => new() { Success = true, Message = message };
        public static ProcessRunResult Fail(string message) => new() { Success = false, Message = TrimProcessMessage(message) };
    }

    private sealed class StaticWebsiteReplicaResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string EntryFilePath { get; init; } = string.Empty;
        public string PackageFilePath { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public int PageCount { get; init; }

        public static StaticWebsiteReplicaResult Ok(string entryFilePath, string packageFilePath, string content, int pageCount)
            => new()
            {
                Success = true,
                EntryFilePath = entryFilePath,
                PackageFilePath = packageFilePath,
                Content = content,
                PageCount = pageCount
            };

        public static StaticWebsiteReplicaResult Fail(string message)
            => new() { Success = false, Message = TrimProcessMessage(message) };
    }

    private static string TrimProcessMessage(string message)
    {
        var compact = Regex.Replace(message ?? string.Empty, "\\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(compact))
            return "unknown_error";
        return compact.Length <= 480 ? compact : compact[..480];
    }
}
