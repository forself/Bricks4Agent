using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Broker.Services;

public sealed class HighLevelSystemScaffoldResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ProjectRoot { get; set; } = string.Empty;
    public string PackageFilePath { get; set; } = string.Empty;
    public string PackageFileName { get; set; } = string.Empty;
    public HighLevelSystemScaffoldSpec? Spec { get; set; }
    public List<string> ProgressMessages { get; set; } = new();
    public LineArtifactDeliveryResult? Delivery { get; set; }
}

public sealed class HighLevelSystemScaffoldService
{
    private readonly HighLevelLineWorkspaceService _workspaceService;
    private readonly LineArtifactDeliveryService _artifactDeliveryService;
    private readonly HighLevelSystemScaffoldSpecStore _specStore;
    private readonly HighLevelSystemScaffoldIterationStore _iterationStore;
    private readonly HighLevelSystemScaffoldProgressStore _progressStore;
    private readonly ILogger<HighLevelSystemScaffoldService> _logger;

    public HighLevelSystemScaffoldService(
        HighLevelLineWorkspaceService workspaceService,
        LineArtifactDeliveryService artifactDeliveryService,
        HighLevelSystemScaffoldSpecStore specStore,
        HighLevelSystemScaffoldIterationStore iterationStore,
        HighLevelSystemScaffoldProgressStore progressStore,
        ILogger<HighLevelSystemScaffoldService> logger)
    {
        _workspaceService = workspaceService;
        _artifactDeliveryService = artifactDeliveryService;
        _specStore = specStore;
        _iterationStore = iterationStore;
        _progressStore = progressStore;
        _logger = logger;
    }

    public void InitializeDraft(HighLevelTaskDraft draft)
    {
        if (!IsSystemScaffold(draft))
            return;

        draft.ScaffoldSpec ??= BuildSpec(draft, draft.OriginalMessage, isInitial: true);
        PersistScaffoldState(draft, "requirements_interview", "started", "已開始系統雛形需求分析。");
    }

    public void RefreshDraftState(HighLevelTaskDraft draft, string stage, string status, string message)
    {
        if (!IsSystemScaffold(draft))
            return;

        draft.ScaffoldSpec ??= BuildSpec(draft, draft.OriginalMessage, isInitial: true);
        PersistScaffoldState(draft, stage, status, message);
    }

    public void ApplyRequirementRefinement(HighLevelTaskDraft draft, string userInput)
    {
        if (!IsSystemScaffold(draft))
            return;

        var normalized = (userInput ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var spec = draft.ScaffoldSpec ?? BuildSpec(draft, draft.OriginalMessage, isInitial: true);
        spec.IterationNumber = Math.Max(1, spec.IterationNumber) + 1;
        spec.LatestUserInput = normalized;
        spec.RequirementNotes.Add(normalized);
        MergeInference(spec, normalized);
        NormalizeSpec(draft, spec);
        draft.ScaffoldSpec = spec;

        PersistScaffoldState(
            draft,
            "requirements_analysis",
            "updated",
            $"已更新需求分析，第 {spec.IterationNumber} 次迭代摘要已整理。");
    }

    public string BuildDraftReply(HighLevelTaskDraft draft)
    {
        var spec = draft.ScaffoldSpec ?? BuildSpec(draft, draft.OriginalMessage, isInitial: true);
        draft.ScaffoldSpec = spec;

        var lines = new List<string>
        {
            "已建立系統雛形 draft。",
            $"task_type: {draft.TaskType}",
            $"project_name: {draft.ProjectName ?? "(not set)"}",
            $"scaffold_family: {spec.ScaffoldFamily}",
            $"frontend: {spec.FrontendStack}",
            $"backend: {spec.BackendStack}",
            $"database: {spec.DatabaseStack}",
            $"auth: {spec.AuthMode}",
            $"package_format: {spec.PackageFormat}",
            $"iteration: {spec.IterationNumber}",
            "目前已完成需求初步分析。你可以直接補充需求，或回覆下方指令。"
        };

        if (spec.ConfirmedRequirements.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("需求摘要：");
            lines.AddRange(spec.ConfirmedRequirements.Select(item => $"- {item}"));
        }

        if (spec.Assumptions.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("目前假設：");
            lines.AddRange(spec.Assumptions.Select(item => $"- {item}"));
        }

        if (spec.OpenQuestions.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("待補充項目：");
            lines.AddRange(spec.OpenQuestions.Select(item => $"- {item}"));
        }

        lines.Add(string.Empty);
        lines.Add("下一步請直接回覆下方指令，或直接補充需求。");
        return string.Join('\n', lines.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    public List<string> BuildDraftFollowUpMessages()
        => new()
        {
            "confirm",
            "cancel"
        };

    public async Task<HighLevelSystemScaffoldResult> GenerateAndDeliverAsync(
        HighLevelTaskDraft draft,
        HighLevelUserProfile profile,
        string relatedTaskId,
        CancellationToken cancellationToken = default)
    {
        if (!IsSystemScaffold(draft))
            return Fail("draft is not a system_scaffold task.");

        if (string.IsNullOrWhiteSpace(draft.ManagedPaths.ProjectRoot))
            return Fail("project root is not available.");

        var spec = draft.ScaffoldSpec ?? BuildSpec(draft, draft.OriginalMessage, isInitial: true);
        NormalizeSpec(draft, spec);
        draft.ScaffoldSpec = spec;

        var progressMessages = new List<string>();
        void Record(string phase, string status, string message)
        {
            progressMessages.Add(message);
            PersistScaffoldState(draft, phase, status, message);
        }

        Directory.CreateDirectory(draft.ManagedPaths.ProjectRoot);
        Directory.CreateDirectory(draft.ManagedPaths.DocumentsRoot);

        Record("requirements_analysis", "completed", "進度：已完成需求分析。");
        WriteRequirementsAnalysis(draft, spec);

        Record("design_planning", "completed", "進度：已完成設計規劃。");
        WriteDesignPlan(draft, spec);

        Record("implementation", "started", "進度：開始生成系統雛形。");
        WriteProjectScaffold(draft, spec);
        WriteIterationState(draft, spec, "implementation_completed");
        Record("implementation", "completed", "進度：已生成系統雛形檔案。");

        Record("testing", "started", "進度：開始執行基本驗證。");
        var verification = VerifyProjectScaffold(draft, spec);
        WriteVerificationReport(draft, verification);
        WriteIterationState(draft, spec, verification.Passed ? "testing_passed" : "testing_failed");
        Record("testing", verification.Passed ? "completed" : "failed",
            verification.Passed ? "進度：基本驗證已通過。" : "進度：基本驗證有警告，但仍保留目前封裝結果。");

        Record("packaging", "started", "進度：開始封裝下載檔。");
        var packageFileName = BuildPackageFileName(draft);
        var packageFilePath = Path.Combine(draft.ManagedPaths.DocumentsRoot, packageFileName);
        if (File.Exists(packageFilePath))
            File.Delete(packageFilePath);
        ZipFile.CreateFromDirectory(draft.ManagedPaths.ProjectRoot, packageFilePath, CompressionLevel.SmallestSize, false, Encoding.UTF8);
        Record("packaging", "completed", "進度：已完成封裝。");

        var uploadToGoogleDrive = _artifactDeliveryService.CanUploadToGoogleDrive(profile.UserId, "shared_delegated");
        var delivery = await _artifactDeliveryService.DeliverExistingFileAsync(new LineExistingArtifactDeliveryRequest
        {
            UserId = profile.UserId,
            FilePath = packageFilePath,
            FileName = packageFileName,
            UploadToGoogleDrive = uploadToGoogleDrive,
            IdentityMode = "shared_delegated",
            ShareMode = string.Empty,
            SendLineNotification = true,
            NotificationTitle = "系統雛形封裝已完成",
            Source = "high_level_system_scaffold",
            RelatedTaskType = draft.TaskType,
            RelatedDraftId = draft.DraftId,
            RelatedTaskId = relatedTaskId
        }, cancellationToken);

        Record("delivery", delivery.UploadedToGoogleDrive ? "completed" : "partial",
            delivery.UploadedToGoogleDrive
                ? "進度：雲端交付已完成。"
                : "進度：檔案已封裝，但雲端上傳未完成。");

        return new HighLevelSystemScaffoldResult
        {
            Success = delivery.Success,
            Message = delivery.Success
                ? (delivery.UploadedToGoogleDrive ? "system_scaffold_packaged_and_uploaded" : "system_scaffold_packaged_locally_only")
                : delivery.Message,
            ProjectRoot = draft.ManagedPaths.ProjectRoot,
            PackageFilePath = packageFilePath,
            PackageFileName = packageFileName,
            Spec = spec,
            ProgressMessages = progressMessages,
            Delivery = delivery
        };
    }

    private static bool IsSystemScaffold(HighLevelTaskDraft draft)
        => string.Equals(draft.TaskType, "system_scaffold", StringComparison.OrdinalIgnoreCase);

    private HighLevelSystemScaffoldSpec BuildSpec(HighLevelTaskDraft draft, string input, bool isInitial)
    {
        var spec = draft.ScaffoldSpec ?? new HighLevelSystemScaffoldSpec
        {
            Channel = draft.Channel,
            UserId = draft.UserId,
            DraftId = draft.DraftId,
            ProjectName = draft.ProjectName ?? string.Empty,
            PackageFormat = "zip",
            IterationNumber = 1
        };

        if (!isInitial)
            spec.IterationNumber = Math.Max(1, spec.IterationNumber);

        if (!string.IsNullOrWhiteSpace(draft.ProjectName))
            spec.ProjectName = draft.ProjectName!;

        spec.RequestSummary = draft.Summary;
        MergeInference(spec, input);
        NormalizeSpec(draft, spec);
        return spec;
    }

    private static void MergeInference(HighLevelSystemScaffoldSpec spec, string input)
    {
        var normalized = Normalize(input);
        if (string.IsNullOrWhiteSpace(spec.ScaffoldFamily))
        {
            spec.ScaffoldFamily = ContainsAny(normalized, "system", "系統", "平台", "dashboard", "後台")
                ? "full_stack_web"
                : "web_app";
        }

        if (ContainsAny(normalized, "react"))
            spec.FrontendStack = "react";
        else if (ContainsAny(normalized, "vue"))
            spec.FrontendStack = "vue";
        else if (ContainsAny(normalized, "blazor"))
            spec.FrontendStack = "blazor";
        else if (string.IsNullOrWhiteSpace(spec.FrontendStack))
            spec.FrontendStack = "vanilla_html";

        if (ContainsAny(normalized, "api", "後端", "backend", "前後端", "server"))
            spec.BackendStack = "aspnet_core_api";
        else if (string.IsNullOrWhiteSpace(spec.BackendStack))
            spec.BackendStack = ContainsAny(normalized, "system", "系統", "平台")
                ? "aspnet_core_api"
                : "none";

        if (ContainsAny(normalized, "sqlite"))
            spec.DatabaseStack = "sqlite";
        else if (ContainsAny(normalized, "sql server", "sqlserver"))
            spec.DatabaseStack = "sqlserver";
        else if (ContainsAny(normalized, "postgres", "postgresql"))
            spec.DatabaseStack = "postgresql";
        else if (string.IsNullOrWhiteSpace(spec.DatabaseStack))
            spec.DatabaseStack = spec.BackendStack == "none" ? "none" : "sqlite";

        if (ContainsAny(normalized, "登入", "登錄", "auth", "login", "sign in", "帳號"))
            spec.AuthMode = "required";
        else if (string.IsNullOrWhiteSpace(spec.AuthMode))
            spec.AuthMode = "none";

        if (ContainsAny(normalized, "azure", "iis"))
            spec.DeploymentTarget = "azure_vm_iis";
        else if (string.IsNullOrWhiteSpace(spec.DeploymentTarget))
            spec.DeploymentTarget = "generic_web";

        if (ContainsAny(normalized, "單頁", "single page", "spa"))
            spec.UiShape = "single_page";
        else if (ContainsAny(normalized, "dashboard", "後台", "多頁", "multi page"))
            spec.UiShape = "multi_page";
        else if (string.IsNullOrWhiteSpace(spec.UiShape))
            spec.UiShape = "single_page";

        if (ContainsAny(normalized, "calculator", "計算機"))
            AddDistinct(spec.ConfirmedRequirements, "核心功能包含基礎計算機操作。");
        if (ContainsAny(normalized, "管理", "dashboard", "後台"))
            AddDistinct(spec.ConfirmedRequirements, "需要管理式介面或操作後台。");
        if (ContainsAny(normalized, "api"))
            AddDistinct(spec.ConfirmedRequirements, "需要 API 邊界或資料交換介面。");
    }

    private static void NormalizeSpec(HighLevelTaskDraft draft, HighLevelSystemScaffoldSpec spec)
    {
        spec.ProjectName = string.IsNullOrWhiteSpace(draft.ProjectName) ? spec.ProjectName : draft.ProjectName!;
        spec.UpdatedAt = DateTimeOffset.UtcNow;
        spec.Assumptions.Clear();
        spec.OpenQuestions.Clear();

        if (string.IsNullOrWhiteSpace(spec.ProjectName))
            AddDistinct(spec.OpenQuestions, "尚未提供專案名稱。");

        if (string.Equals(spec.FrontendStack, "vanilla_html", StringComparison.OrdinalIgnoreCase))
            AddDistinct(spec.Assumptions, "前端預設採用單檔或簡潔的 HTML/CSS/JavaScript 雛形。");

        if (string.Equals(spec.BackendStack, "aspnet_core_api", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(spec.DatabaseStack, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(spec.Assumptions, "後端預設採用 ASP.NET Core API，資料層預設為 SQLite 雛形。");
        }
        else if (string.Equals(spec.BackendStack, "none", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(spec.Assumptions, "目前假設以純前端為主，不建立後端執行面。");
        }

        if (string.Equals(spec.AuthMode, "none", StringComparison.OrdinalIgnoreCase))
            AddDistinct(spec.Assumptions, "目前不加入登入或權限驗證。");

        if (string.IsNullOrWhiteSpace(spec.DeploymentTarget) || string.Equals(spec.DeploymentTarget, "generic_web", StringComparison.OrdinalIgnoreCase))
            AddDistinct(spec.OpenQuestions, "若有特定部署目標，可直接補充，例如 Azure VM IIS。");

        spec.ReadyForConfirmation = !string.IsNullOrWhiteSpace(spec.ProjectName);
    }

    private static void AddDistinct(List<string> items, string value)
    {
        if (!items.Any(existing => string.Equals(existing, value, StringComparison.Ordinal)))
            items.Add(value);
    }

    private void PersistScaffoldState(HighLevelTaskDraft draft, string phase, string status, string message)
    {
        var spec = draft.ScaffoldSpec ?? BuildSpec(draft, draft.OriginalMessage, isInitial: true);
        draft.ScaffoldSpec = spec;
        _specStore.Write(spec);

        var iteration = new HighLevelSystemScaffoldIterationState
        {
            Channel = draft.Channel,
            UserId = draft.UserId,
            DraftId = draft.DraftId,
            TaskType = draft.TaskType,
            ProjectName = draft.ProjectName ?? spec.ProjectName,
            CurrentPhase = phase,
            CurrentStatus = status,
            IterationNumber = Math.Max(1, spec.IterationNumber),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _iterationStore.Write(iteration);

        _progressStore.Record(new HighLevelSystemScaffoldProgressEvent
        {
            Channel = draft.Channel,
            UserId = draft.UserId,
            DraftId = draft.DraftId,
            TaskType = draft.TaskType,
            Phase = phase,
            Status = status,
            Message = message,
            IterationNumber = Math.Max(1, spec.IterationNumber),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static void WriteRequirementsAnalysis(HighLevelTaskDraft draft, HighLevelSystemScaffoldSpec spec)
    {
        var docsRoot = Path.Combine(draft.ManagedPaths.ProjectRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        var content = string.Join('\n', new[]
        {
            "# Requirements Analysis",
            "",
            $"Project: {spec.ProjectName}",
            $"Scaffold Family: {spec.ScaffoldFamily}",
            "",
            "## Confirmed Requirements",
            spec.ConfirmedRequirements.Count == 0 ? "- None captured yet." : string.Join('\n', spec.ConfirmedRequirements.Select(item => $"- {item}")),
            "",
            "## Assumptions",
            spec.Assumptions.Count == 0 ? "- None." : string.Join('\n', spec.Assumptions.Select(item => $"- {item}")),
            "",
            "## Open Questions",
            spec.OpenQuestions.Count == 0 ? "- None." : string.Join('\n', spec.OpenQuestions.Select(item => $"- {item}")),
            "",
            "## Source Request",
            draft.OriginalMessage
        });
        File.WriteAllText(Path.Combine(docsRoot, "requirements-analysis.md"), content, new UTF8Encoding(false));
    }

    private static void WriteDesignPlan(HighLevelTaskDraft draft, HighLevelSystemScaffoldSpec spec)
    {
        var docsRoot = Path.Combine(draft.ManagedPaths.ProjectRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        var content = string.Join('\n', new[]
        {
            "# Design Plan",
            "",
            $"Project: {spec.ProjectName}",
            $"Frontend: {spec.FrontendStack}",
            $"Backend: {spec.BackendStack}",
            $"Database: {spec.DatabaseStack}",
            $"Auth: {spec.AuthMode}",
            $"Deployment: {spec.DeploymentTarget}",
            $"Package: {spec.PackageFormat}",
            "",
            "## Planned Structure",
            "- docs/: requirement, design, verification, iteration artifacts",
            "- frontend/: client-facing scaffold",
            "- backend/: API or service placeholder when backend is enabled",
            "- tests/: smoke and iteration guidance",
            "",
            "## Iteration Rule",
            "- requirement analysis",
            "- design planning",
            "- implementation",
            "- testing",
            "- revision if needed",
            "- packaging and delivery"
        });
        File.WriteAllText(Path.Combine(docsRoot, "design-plan.md"), content, new UTF8Encoding(false));
    }

    private static void WriteProjectScaffold(HighLevelTaskDraft draft, HighLevelSystemScaffoldSpec spec)
    {
        var frontendRoot = Path.Combine(draft.ManagedPaths.ProjectRoot, "frontend");
        Directory.CreateDirectory(frontendRoot);
        var docsRoot = Path.Combine(draft.ManagedPaths.ProjectRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        var testsRoot = Path.Combine(draft.ManagedPaths.ProjectRoot, "tests");
        Directory.CreateDirectory(testsRoot);

        File.WriteAllText(Path.Combine(draft.ManagedPaths.ProjectRoot, "README.md"), BuildProjectReadme(spec), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(frontendRoot, "index.html"), BuildIndexHtml(spec), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(frontendRoot, "app.js"), BuildAppJs(spec), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(frontendRoot, "styles.css"), BuildStylesCss(spec), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(testsRoot, "smoke-checklist.md"), BuildSmokeChecklist(spec), new UTF8Encoding(false));

        if (!string.Equals(spec.BackendStack, "none", StringComparison.OrdinalIgnoreCase))
        {
            var backendRoot = Path.Combine(draft.ManagedPaths.ProjectRoot, "backend");
            Directory.CreateDirectory(backendRoot);
            File.WriteAllText(Path.Combine(backendRoot, "README.md"), BuildBackendReadme(spec), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(backendRoot, "openapi.json"), BuildOpenApiSkeleton(spec), new UTF8Encoding(false));
        }
    }

    private static void WriteIterationState(HighLevelTaskDraft draft, HighLevelSystemScaffoldSpec spec, string status)
    {
        var docsRoot = Path.Combine(draft.ManagedPaths.ProjectRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        var iterationState = new
        {
            project_name = spec.ProjectName,
            task_type = draft.TaskType,
            iteration_number = spec.IterationNumber,
            status,
            updated_at = DateTimeOffset.UtcNow,
            confirmed_requirements = spec.ConfirmedRequirements,
            assumptions = spec.Assumptions,
            open_questions = spec.OpenQuestions
        };
        File.WriteAllText(
            Path.Combine(docsRoot, "iteration-state.json"),
            JsonSerializer.Serialize(iterationState, new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
    }

    private static HighLevelScaffoldVerificationResult VerifyProjectScaffold(HighLevelTaskDraft draft, HighLevelSystemScaffoldSpec spec)
    {
        var requiredFiles = new List<string>
        {
            Path.Combine(draft.ManagedPaths.ProjectRoot, "README.md"),
            Path.Combine(draft.ManagedPaths.ProjectRoot, "docs", "requirements-analysis.md"),
            Path.Combine(draft.ManagedPaths.ProjectRoot, "docs", "design-plan.md"),
            Path.Combine(draft.ManagedPaths.ProjectRoot, "docs", "iteration-state.json"),
            Path.Combine(draft.ManagedPaths.ProjectRoot, "frontend", "index.html"),
            Path.Combine(draft.ManagedPaths.ProjectRoot, "frontend", "app.js"),
            Path.Combine(draft.ManagedPaths.ProjectRoot, "frontend", "styles.css"),
            Path.Combine(draft.ManagedPaths.ProjectRoot, "tests", "smoke-checklist.md")
        };

        if (!string.Equals(spec.BackendStack, "none", StringComparison.OrdinalIgnoreCase))
        {
            requiredFiles.Add(Path.Combine(draft.ManagedPaths.ProjectRoot, "backend", "README.md"));
            requiredFiles.Add(Path.Combine(draft.ManagedPaths.ProjectRoot, "backend", "openapi.json"));
        }

        var missing = requiredFiles.Where(path => !File.Exists(path)).ToList();
        return new HighLevelScaffoldVerificationResult
        {
            Passed = missing.Count == 0,
            MissingFiles = missing
        };
    }

    private static void WriteVerificationReport(HighLevelTaskDraft draft, HighLevelScaffoldVerificationResult verification)
    {
        var docsRoot = Path.Combine(draft.ManagedPaths.ProjectRoot, "docs");
        Directory.CreateDirectory(docsRoot);
        var lines = new List<string>
        {
            "# Verification Report",
            "",
            $"Passed: {verification.Passed}",
            ""
        };
        if (verification.MissingFiles.Count == 0)
        {
            lines.Add("All expected scaffold files were generated.");
        }
        else
        {
            lines.Add("Missing files:");
            lines.AddRange(verification.MissingFiles.Select(item => $"- {item}"));
        }

        File.WriteAllText(Path.Combine(docsRoot, "verification-report.md"), string.Join('\n', lines), new UTF8Encoding(false));
    }

    private static string BuildPackageFileName(HighLevelTaskDraft draft)
    {
        var name = string.IsNullOrWhiteSpace(draft.ProjectFolderName) ? "system-scaffold" : draft.ProjectFolderName;
        return $"{name}-scaffold.zip";
    }

    private static string BuildProjectReadme(HighLevelSystemScaffoldSpec spec)
        => string.Join('\n', new[]
        {
            $"# {spec.ProjectName}",
            "",
            "This package is a broker-generated system scaffold.",
            "",
            "## Scaffold Summary",
            $"- Family: {spec.ScaffoldFamily}",
            $"- Frontend: {spec.FrontendStack}",
            $"- Backend: {spec.BackendStack}",
            $"- Database: {spec.DatabaseStack}",
            $"- Auth: {spec.AuthMode}",
            $"- Deployment: {spec.DeploymentTarget}",
            "",
            "## Included",
            "- requirement analysis",
            "- design plan",
            "- iteration state",
            "- frontend scaffold",
            "- backend placeholder when enabled",
            "- smoke checklist"
        });

    private static string BuildIndexHtml(HighLevelSystemScaffoldSpec spec)
        => string.Join('\n', new[]
        {
            "<!DOCTYPE html>",
            "<html lang=\"zh-Hant\">",
            "<head>",
            "  <meta charset=\"utf-8\">",
            "  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">",
            $"  <title>{EscapeHtml(spec.ProjectName)}</title>",
            "  <link rel=\"stylesheet\" href=\"styles.css\">",
            "</head>",
            "<body>",
            "  <main class=\"shell\">",
            $"    <h1>{EscapeHtml(spec.ProjectName)}</h1>",
            $"    <p>Scaffold family: {EscapeHtml(spec.ScaffoldFamily)}</p>",
            "    <section id=\"app\"></section>",
            "  </main>",
            "  <script src=\"app.js\"></script>",
            "</body>",
            "</html>"
        });

    private static string BuildAppJs(HighLevelSystemScaffoldSpec spec)
        => string.Join('\n', new[]
        {
            "const app = document.getElementById('app');",
            "if (app) {",
            "  app.innerHTML = `",
            "    <div class=\"card\">",
            $"      <h2>{EscapeJs(spec.ProjectName)} scaffold</h2>",
            $"      <p>Frontend: {EscapeJs(spec.FrontendStack)}</p>",
            $"      <p>Backend: {EscapeJs(spec.BackendStack)}</p>",
            $"      <p>Database: {EscapeJs(spec.DatabaseStack)}</p>",
            "    </div>`;",
            "}"
        });

    private static string BuildStylesCss(HighLevelSystemScaffoldSpec spec)
        => string.Join('\n', new[]
        {
            ":root {",
            "  color-scheme: light;",
            "  --bg: #f4f1ea;",
            "  --panel: #fffdf8;",
            "  --ink: #1f1b16;",
            "  --accent: #005f73;",
            "}",
            "body {",
            "  margin: 0;",
            "  font-family: \"Noto Sans TC\", \"Segoe UI\", sans-serif;",
            "  background: linear-gradient(180deg, #f4f1ea, #e9e2d0);",
            "  color: var(--ink);",
            "}",
            ".shell {",
            "  max-width: 960px;",
            "  margin: 0 auto;",
            "  padding: 48px 20px;",
            "}",
            ".card {",
            "  background: var(--panel);",
            "  border: 1px solid rgba(0, 0, 0, 0.08);",
            "  border-radius: 18px;",
            "  padding: 24px;",
            "  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.08);",
            "}",
            $"/* {spec.ProjectName} scaffold */"
        });

    private static string BuildSmokeChecklist(HighLevelSystemScaffoldSpec spec)
        => string.Join('\n', new[]
        {
            "# Smoke Checklist",
            "",
            "- Open frontend/index.html in a browser",
            "- Confirm basic scaffold metadata is visible",
            "- Review docs/requirements-analysis.md",
            "- Review docs/design-plan.md",
            "- If backend is enabled, inspect backend/openapi.json",
            "",
            $"Current backend mode: {spec.BackendStack}"
        });

    private static string BuildBackendReadme(HighLevelSystemScaffoldSpec spec)
        => string.Join('\n', new[]
        {
            "# Backend Placeholder",
            "",
            $"Selected backend stack: {spec.BackendStack}",
            $"Selected database: {spec.DatabaseStack}",
            $"Auth mode: {spec.AuthMode}",
            "",
            "This folder is the execution placeholder for future backend implementation iterations."
        });

    private static string BuildOpenApiSkeleton(HighLevelSystemScaffoldSpec spec)
        => JsonSerializer.Serialize(new
        {
            openapi = "3.1.0",
            info = new
            {
                title = spec.ProjectName + " API",
                version = "0.1.0"
            },
            paths = new Dictionary<string, object>
            {
                ["/health"] = new
                {
                    get = new
                    {
                        summary = "Health check placeholder",
                        responses = new Dictionary<string, object>
                        {
                            ["200"] = new
                            {
                                description = "OK"
                            }
                        }
                    }
                }
            }
        }, new JsonSerializerOptions { WriteIndented = true });

    private static string EscapeHtml(string value)
        => value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string EscapeJs(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("$", "\\$", StringComparison.Ordinal);

    private static bool ContainsAny(string normalized, params string[] keywords)
        => keywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string value)
        => (value ?? string.Empty).Trim().ToLowerInvariant();

    private HighLevelSystemScaffoldResult Fail(string message)
    {
        _logger.LogWarning("System scaffold generation failed: {Message}", message);
        return new HighLevelSystemScaffoldResult
        {
            Success = false,
            Message = message
        };
    }
}

public sealed class HighLevelSystemScaffoldSpecStore
{
    private const string SystemPrincipalId = "system:high-level-scaffold";
    private readonly BrokerCore.Data.BrokerDb _db;

    public HighLevelSystemScaffoldSpecStore(BrokerCore.Data.BrokerDb db)
    {
        _db = db;
    }

    public void Write(HighLevelSystemScaffoldSpec spec)
        => WriteDocument(BuildDocumentId(spec.Channel, spec.UserId), spec, "application/json", "[\"high-level\",\"scaffold-spec\"]", spec.UpdatedAt.UtcDateTime);

    public static string BuildDocumentId(string channel, string userId)
        => $"hlm.scaffold-spec.{channel}.{userId}";

    private void WriteDocument(string documentId, object payload, string contentType, string tags, DateTime createdAt)
    {
        var latest = _db.Query<BrokerCore.Models.SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        var entry = new BrokerCore.Models.SharedContextEntry
        {
            EntryId = BrokerCore.IdGen.New("ctx"),
            DocumentId = documentId,
            Version = (latest?.Version ?? 0) + 1,
            ParentVersion = latest?.Version,
            Key = documentId,
            ContentRef = JsonSerializer.Serialize(payload),
            ContentType = contentType,
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:high-level-scaffold\"]}",
            AuthorPrincipalId = SystemPrincipalId,
            TaskId = "global",
            Tags = tags,
            CreatedAt = createdAt
        };

        _db.Insert(entry);
    }
}

public sealed class HighLevelSystemScaffoldIterationStore
{
    private const string SystemPrincipalId = "system:high-level-scaffold";
    private readonly BrokerCore.Data.BrokerDb _db;

    public HighLevelSystemScaffoldIterationStore(BrokerCore.Data.BrokerDb db)
    {
        _db = db;
    }

    public void Write(HighLevelSystemScaffoldIterationState state)
    {
        var documentId = BuildDocumentId(state.Channel, state.UserId);
        var latest = _db.Query<BrokerCore.Models.SharedContextEntry>(
            "SELECT * FROM shared_context_entries WHERE document_id = @docId ORDER BY version DESC LIMIT 1",
            new { docId = documentId }).FirstOrDefault();

        _db.Insert(new BrokerCore.Models.SharedContextEntry
        {
            EntryId = BrokerCore.IdGen.New("ctx"),
            DocumentId = documentId,
            Version = (latest?.Version ?? 0) + 1,
            ParentVersion = latest?.Version,
            Key = documentId,
            ContentRef = JsonSerializer.Serialize(state),
            ContentType = "application/json",
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:high-level-scaffold\"]}",
            AuthorPrincipalId = SystemPrincipalId,
            TaskId = "global",
            Tags = "[\"high-level\",\"scaffold-iteration\"]",
            CreatedAt = state.UpdatedAt.UtcDateTime
        });
    }

    public static string BuildDocumentId(string channel, string userId)
        => $"hlm.scaffold-iteration.{channel}.{userId}";
}

public sealed class HighLevelSystemScaffoldProgressStore
{
    private const string SystemPrincipalId = "system:high-level-scaffold";
    private readonly BrokerCore.Data.BrokerDb _db;

    public HighLevelSystemScaffoldProgressStore(BrokerCore.Data.BrokerDb db)
    {
        _db = db;
    }

    public void Record(HighLevelSystemScaffoldProgressEvent progressEvent)
    {
        var documentId = BuildDocumentId(progressEvent.Channel, progressEvent.UserId, progressEvent.EventId);
        _db.Insert(new BrokerCore.Models.SharedContextEntry
        {
            EntryId = BrokerCore.IdGen.New("ctx"),
            DocumentId = documentId,
            Version = 1,
            Key = documentId,
            ContentRef = JsonSerializer.Serialize(progressEvent),
            ContentType = "application/json",
            Acl = "{\"read\":[\"*\"],\"write\":[\"system:high-level-scaffold\"]}",
            AuthorPrincipalId = SystemPrincipalId,
            TaskId = progressEvent.TaskId ?? "global",
            Tags = "[\"high-level\",\"scaffold-progress\"]",
            CreatedAt = progressEvent.CreatedAt.UtcDateTime
        });
    }

    public static string BuildDocumentId(string channel, string userId, string eventId)
        => $"hlm.scaffold-progress.{channel}.{userId}.{eventId}";
}

public sealed class HighLevelSystemScaffoldSpec
{
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string RequestSummary { get; set; } = string.Empty;
    public string LatestUserInput { get; set; } = string.Empty;
    public string ScaffoldFamily { get; set; } = string.Empty;
    public string FrontendStack { get; set; } = string.Empty;
    public string BackendStack { get; set; } = string.Empty;
    public string DatabaseStack { get; set; } = string.Empty;
    public string AuthMode { get; set; } = string.Empty;
    public string DeploymentTarget { get; set; } = string.Empty;
    public string PackageFormat { get; set; } = "zip";
    public string UiShape { get; set; } = string.Empty;
    public int IterationNumber { get; set; } = 1;
    public bool ReadyForConfirmation { get; set; }
    public List<string> ConfirmedRequirements { get; set; } = new();
    public List<string> RequirementNotes { get; set; } = new();
    public List<string> Assumptions { get; set; } = new();
    public List<string> OpenQuestions { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HighLevelSystemScaffoldIterationState
{
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string? ProjectName { get; set; }
    public string CurrentPhase { get; set; } = string.Empty;
    public string CurrentStatus { get; set; } = string.Empty;
    public int IterationNumber { get; set; } = 1;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HighLevelSystemScaffoldProgressEvent
{
    public string EventId { get; set; } = BrokerCore.IdGen.New("hspg");
    public string Channel { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DraftId { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int IterationNumber { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class HighLevelScaffoldVerificationResult
{
    public bool Passed { get; set; }
    public List<string> MissingFiles { get; set; } = new();
}
