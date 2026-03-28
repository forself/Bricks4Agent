using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Broker.Services;

public sealed class HighLevelCodeArtifactResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string EntryFilePath { get; set; } = string.Empty;
    public string EntryFileName { get; set; } = "index.html";
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

        var html = await GenerateProjectHtmlAsync(draft, cancellationToken);
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

    private static string BuildDeliveredFileName(HighLevelTaskDraft draft)
    {
        var baseName = string.IsNullOrWhiteSpace(draft.ProjectFolderName)
            ? "prototype"
            : draft.ProjectFolderName.Trim();
        return $"{baseName}.html";
    }

    private static string BuildFallbackHtml(HighLevelTaskDraft draft)
    {
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

    private static HighLevelCodeArtifactResult Fail(string message)
        => new()
        {
            Success = false,
            Message = message
        };
}
