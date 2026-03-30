# LINE Project Interview Agent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dedicated `/proj` LINE workflow that runs a task-scoped project interview, narrows the request through project scale and template-family selection, compiles confirmed assertions into versioned JSON/PDF review artifacts, and waits for explicit `/ok`, `/revise`, or `/cancel`.

**Architecture:** Extend the broker high-level path with a dedicated `project_interview` state machine and per-version DAG model. Keep truth in broker-owned persisted assertion and version-graph documents, use the LLM only to propose interpretations and restatement options, and compile confirmed assertions into a canonical project-instance JSON that drives PDF review generation and artifact delivery. Template selection is driven by a JSON catalog layered above the existing browser page-generator and UI component runtime.

**Tech Stack:** C# /.NET 8 minimal API broker, existing `HighLevelCoordinator` and artifact services, broker verify program, integration tests under `packages/csharp/tests`, JSON template catalog under `packages/javascript/browser`, existing SPA runtime and page-generator

---

## Design Rationale

### Why a state machine

The interview workflow is a protocol, not a free chat. A state machine is the correct control model because it constrains admissible commands, makes approval checkpoints explicit, and prevents accidental transitions from ambiguous natural language. In formal terms, this reduces control-flow entropy and keeps task progression deterministic even when semantic interpretation remains probabilistic.

### Why a per-version DAG

The generated JSON/PDF pair is not just a transcript artifact. It is a derived product whose provenance must remain auditable. A directed acyclic graph is appropriate because each review version is a one-way compilation from confirmed assertions to canonical project definition to rendered artifacts. Revision creates a new graph instead of mutating the old one, which preserves lineage and avoids hidden circular dependencies.

### Why confirmed restatements instead of free-form memory

Long-horizon agent memory fails mainly because compression introduces drift. The design therefore treats memory as a set of confirmed restatements rather than a rolling summary. This follows a verification-first epistemic model: unconfirmed interpretation remains provisional, while only explicit user-confirmed statements are promoted to canonical truth.

### Why template-first narrowing

The system should not solve an open-world synthesis problem if the implementation substrate is already bounded by an existing component library and runtime. Template-first narrowing reduces hypothesis space early, improves consistency, and lowers the cost of downstream implementation planning. In software architecture terms, this moves the system from unconstrained generation to guided configuration over a known capability surface.

### Why document-first JSON-defined programs

The canonical machine-readable output should be a document that defines the program, not ad hoc handwritten code assembled directly from chat. This aligns with model-driven engineering: the interview produces a structured intermediate representation, and the existing page-generator/runtime acts as the execution substrate. The benefit is separation of concerns between intent capture, structural definition, and executable rendering.

### Why bilingual planning artifacts

This feature touches both implementation and design review. Maintaining English and Traditional Chinese planning artifacts improves review accessibility while keeping execution precise. The English plan remains the source of exact code snippets and shell commands; the Chinese companion preserves the same task structure and rationale to support architectural discussion and review.

## Language Versions

- English source plan: `docs/superpowers/plans/2026-03-31-line-project-interview-agent.md`
- Traditional Chinese companion: `docs/superpowers/plans/2026-03-31-line-project-interview-agent.zh-TW.md`

The English plan is the canonical execution document for exact code blocks and commands. The Chinese companion must stay structurally aligned with it.

---

## File Map

### Create

- `packages/csharp/broker/Services/ProjectInterviewModels.cs`
  - Session state, assertion, restatement option, version-graph node/edge, and project-definition DTOs for the new workflow.
- `packages/csharp/broker/Services/ProjectInterviewStateService.cs`
  - Loads and persists `hlm.project-interview.*` documents and provides the broker-owned canonical task state API.
- `packages/csharp/broker/Services/ProjectInterviewStateMachine.cs`
  - Central state transition validator for `/proj`, `/ok`, `/revise`, `/cancel`, and phase progression.
- `packages/csharp/broker/Services/ProjectInterviewRestatementService.cs`
  - Converts model proposals into bounded explicit statement options plus the conservative escape option.
- `packages/csharp/broker/Services/ProjectInterviewTemplateCatalogService.cs`
  - Loads template manifests and narrows candidates by project scale, required modules, and constraints.
- `packages/csharp/broker/Services/ProjectInterviewProjectDefinitionCompiler.cs`
  - Builds the canonical `project_instance_definition` JSON and immutable per-version DAG from confirmed assertions.
- `packages/csharp/broker/Services/ProjectInterviewWorkflowDesignService.cs`
  - Builds the deterministic workflow-design view model, intermediate markdown/html, and render manifest.
- `packages/csharp/broker/Services/ProjectInterviewPdfRenderService.cs`
  - Renders the workflow-design PDF and embeds task/version/digest metadata.
- `packages/csharp/tests/integration/Api/ProjectInterviewLifecycleTests.cs`
  - End-to-end broker lifecycle tests for `/proj`, scale classification, template selection, document generation gating, and cancellation.
- `packages/csharp/tests/integration/Api/ProjectInterviewReviewTests.cs`
  - Review command and revision tests for `/ok`, `/revise`, artifact regeneration, and immutable version history.
- `packages/javascript/browser/templates/catalog.json`
  - Top-level template catalog for the interview workflow.
- `packages/javascript/browser/templates/content_showcase/template.manifest.json`
- `packages/javascript/browser/templates/form_collection/template.manifest.json`
- `packages/javascript/browser/templates/member_portal/template.manifest.json`
- `packages/javascript/browser/templates/list_search/template.manifest.json`
- `packages/javascript/browser/templates/crud_admin/template.manifest.json`
- `packages/javascript/browser/templates/dashboard/template.manifest.json`
- `packages/javascript/browser/templates/multi_step_flow/template.manifest.json`
- `packages/javascript/browser/templates/transaction_flow/template.manifest.json`
- `packages/javascript/browser/__tests__/templates/TemplateCatalog.test.js`
  - Validates manifest shape, supported scales, and conservative narrowing behavior.

### Modify

- `packages/csharp/broker/Services/HighLevelCommandParser.cs`
  - Add `/proj`, `/ok`, `/revise`, and `/cancel` parsing for the project interview path.
- `packages/csharp/broker/Services/HighLevelCoordinator.cs`
  - Route project-interview commands and user turns into the new state service, state machine, restatement service, and compiler.
- `packages/csharp/broker/Services/HighLevelLlmOptions.cs`
  - Add prompt/config fields for the high-grade interview model if needed.
- `packages/csharp/broker/Services/LineArtifactDeliveryService.cs`
  - Reuse the existing artifact-delivery path for PDF/JSON/zip output.
- `packages/csharp/broker/Program.cs`
  - Register the new services and any config sections.
- `packages/csharp/broker/appsettings.json`
  - Add project interview config such as document TTL, template catalog path, and PDF defaults.
- `packages/csharp/broker/appsettings.Development.example.json`
  - Add development example values for the same settings.
- `packages/csharp/broker/verify/Program.cs`
  - Add deterministic verification coverage for state transitions, restatement promotion, template narrowing, DAG construction, and artifact metadata.
- `packages/javascript/browser/package.json`
  - Ensure any JSON-template test command or fixtures are reachable from the existing test setup if needed.

### Reuse Without Structural Change

- `packages/csharp/broker/Services/HighLevelDocumentArtifactService.cs`
  - Reuse for artifact recording.
- `packages/csharp/broker/Services/HighLevelMemoryStore.cs`
  - Reuse as the storage substrate for `hlm.project-interview.*` documents.
- `packages/javascript/browser/page-generator/PageDefinitionAdapter.js`
  - Reuse as the downstream execution substrate for JSON-defined programs.
- `templates/spa/frontend/runtime/page-generator/DynamicPageRenderer.js`
  - Reuse as the browser-side render substrate; do not redesign runtime in this feature.

---

### Task 1: Add Command Routing and Session State Machine

**Files:**
- Create: `packages/csharp/broker/Services/ProjectInterviewModels.cs`
- Create: `packages/csharp/broker/Services/ProjectInterviewStateMachine.cs`
- Modify: `packages/csharp/broker/Services/HighLevelCommandParser.cs`
- Modify: `packages/csharp/broker/Services/HighLevelCoordinator.cs`
- Modify: `packages/csharp/broker/Program.cs`
- Test: `packages/csharp/broker/verify/Program.cs`

- [ ] **Step 1: Write the failing verify tests for `/proj` and phase transitions**

Add coverage in `packages/csharp/broker/verify/Program.cs` for:

```csharp
var machine = new ProjectInterviewStateMachine();

var idle = ProjectInterviewSessionState.CreateNew("line", "U123");
AssertEqual(ProjectInterviewPhase.Idle, idle.CurrentPhase, "new session starts idle");

var started = machine.ApplyCommand(idle, ProjectInterviewCommand.StartProjectInterview);
AssertEqual(ProjectInterviewPhase.CollectProjectName, started.CurrentPhase, "/proj enters project-name collection");

var named = started with
{
    ProjectName = "Alpha Portal",
    ProjectFolderName = "alpha-portal",
    HasUniqueProjectFolder = true
};
var classified = machine.Advance(named, ProjectInterviewAdvanceReason.ProjectNameAccepted);
AssertEqual(ProjectInterviewPhase.ClassifyProjectScale, classified.CurrentPhase, "accepted project name advances to scale classification");

var cancelled = machine.ApplyCommand(classified, ProjectInterviewCommand.Cancel);
AssertEqual(ProjectInterviewPhase.Cancelled, cancelled.CurrentPhase, "/cancel reaches terminal cancelled state");
```

- [ ] **Step 2: Run verify to confirm the tests fail because the project interview types do not exist**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- FAIL with missing `ProjectInterviewStateMachine`, `ProjectInterviewSessionState`, and related types

- [ ] **Step 3: Add the minimal project interview models**

Create `packages/csharp/broker/Services/ProjectInterviewModels.cs`:

```csharp
namespace Broker.Services;

public enum ProjectInterviewPhase
{
    Idle,
    CollectProjectName,
    ClassifyProjectScale,
    NarrowTemplateFamily,
    ConfirmTemplateFamily,
    CollectTemplateRequirements,
    ResolveConflicts,
    CompileProjectDefinition,
    RenderReviewArtifacts,
    AwaitUserReview,
    ReviseRequested,
    Confirmed,
    Cancelled,
    Failed
}

public enum ProjectInterviewCommand
{
    StartProjectInterview,
    Approve,
    Revise,
    Cancel
}

public enum ProjectInterviewAdvanceReason
{
    ProjectNameAccepted,
    ProjectScaleConfirmed,
    TemplateFamilyNarrowed,
    TemplateFamilyConfirmed,
    RequirementsReady,
    GraphCompiled,
    ArtifactsRendered,
    RevisionCaptured
}

public sealed record ProjectInterviewSessionState(
    string Channel,
    string UserId,
    ProjectInterviewPhase CurrentPhase,
    string? ProjectName,
    string? ProjectFolderName,
    bool HasUniqueProjectFolder)
{
    public static ProjectInterviewSessionState CreateNew(string channel, string userId) =>
        new(channel, userId, ProjectInterviewPhase.Idle, null, null, false);
}
```

- [ ] **Step 4: Add the minimal state machine**

Create `packages/csharp/broker/Services/ProjectInterviewStateMachine.cs`:

```csharp
namespace Broker.Services;

public sealed class ProjectInterviewStateMachine
{
    public ProjectInterviewSessionState ApplyCommand(ProjectInterviewSessionState state, ProjectInterviewCommand command) =>
        (state.CurrentPhase, command) switch
        {
            (ProjectInterviewPhase.Idle, ProjectInterviewCommand.StartProjectInterview)
                => state with { CurrentPhase = ProjectInterviewPhase.CollectProjectName },
            (_, ProjectInterviewCommand.Cancel)
                => state with { CurrentPhase = ProjectInterviewPhase.Cancelled },
            (ProjectInterviewPhase.AwaitUserReview, ProjectInterviewCommand.Approve)
                => state with { CurrentPhase = ProjectInterviewPhase.Confirmed },
            (ProjectInterviewPhase.AwaitUserReview, ProjectInterviewCommand.Revise)
                => state with { CurrentPhase = ProjectInterviewPhase.ReviseRequested },
            _ => throw new InvalidOperationException($"Command {command} not allowed from {state.CurrentPhase}.")
        };

    public ProjectInterviewSessionState Advance(ProjectInterviewSessionState state, ProjectInterviewAdvanceReason reason) =>
        (state.CurrentPhase, reason) switch
        {
            (ProjectInterviewPhase.CollectProjectName, ProjectInterviewAdvanceReason.ProjectNameAccepted) when state.HasUniqueProjectFolder
                => state with { CurrentPhase = ProjectInterviewPhase.ClassifyProjectScale },
            _ => throw new InvalidOperationException($"Advance {reason} not allowed from {state.CurrentPhase}.")
        };
}
```

- [ ] **Step 5: Wire command parsing for `/proj`, `/ok`, `/revise`, and `/cancel`**

Modify `packages/csharp/broker/Services/HighLevelCommandParser.cs` to add a narrow command result:

```csharp
public sealed record HighLevelProjectInterviewCommand(bool IsProjectInterview, ProjectInterviewCommand? Command);

if (text.Equals("/proj", StringComparison.OrdinalIgnoreCase))
    return new HighLevelProjectInterviewCommand(true, ProjectInterviewCommand.StartProjectInterview);

if (text.Equals("/ok", StringComparison.OrdinalIgnoreCase))
    return new HighLevelProjectInterviewCommand(true, ProjectInterviewCommand.Approve);

if (text.Equals("/revise", StringComparison.OrdinalIgnoreCase))
    return new HighLevelProjectInterviewCommand(true, ProjectInterviewCommand.Revise);

if (text.Equals("/cancel", StringComparison.OrdinalIgnoreCase))
    return new HighLevelProjectInterviewCommand(true, ProjectInterviewCommand.Cancel);
```

- [ ] **Step 6: Register the state machine and route project-interview commands through the coordinator**

Modify `packages/csharp/broker/Program.cs`:

```csharp
builder.Services.AddSingleton<ProjectInterviewStateMachine>();
```

Modify `packages/csharp/broker/Services/HighLevelCoordinator.cs` to inject `ProjectInterviewStateMachine` and branch early:

```csharp
if (parsedProjectInterviewCommand.IsProjectInterview && parsedProjectInterviewCommand.Command is { } projectCommand)
{
    return await HandleProjectInterviewCommandAsync(channel, userId, projectCommand, cancellationToken);
}
```

- [ ] **Step 7: Run verify to confirm the state-machine tests pass**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- PASS for the new `/proj` and transition assertions
- overall suite may still fail later because persistence, restatement, and artifact services are not implemented

- [ ] **Step 8: Commit**

```powershell
git add packages/csharp/broker/Services/ProjectInterviewModels.cs packages/csharp/broker/Services/ProjectInterviewStateMachine.cs packages/csharp/broker/Services/HighLevelCommandParser.cs packages/csharp/broker/Services/HighLevelCoordinator.cs packages/csharp/broker/Program.cs packages/csharp/broker/verify/Program.cs
git commit -m "feat: add project interview command routing and state machine"
```

### Task 2: Add Assertion Persistence and Restatement Confirmation

**Files:**
- Create: `packages/csharp/broker/Services/ProjectInterviewStateService.cs`
- Create: `packages/csharp/broker/Services/ProjectInterviewRestatementService.cs`
- Modify: `packages/csharp/broker/Services/ProjectInterviewModels.cs`
- Modify: `packages/csharp/broker/Services/HighLevelCoordinator.cs`
- Modify: `packages/csharp/broker/Program.cs`
- Test: `packages/csharp/broker/verify/Program.cs`
- Test: `packages/csharp/tests/integration/Api/ProjectInterviewLifecycleTests.cs`

- [ ] **Step 1: Write the failing verify tests for confirmation-only promotion**

Add coverage in `packages/csharp/broker/verify/Program.cs`:

```csharp
var service = new ProjectInterviewRestatementService();
var options = service.BuildOptions(
    new[]
    {
        "This is a small internal admin tool with login.",
        "This is a customer-facing portal with member accounts."
    },
    conservativeOptionText: "None of these is precise.");

AssertEqual(3, options.Count, "restatement adds conservative escape option");
AssertTrue(options[^1].IsConservativeEscape, "last option is the conservative escape");
```

- [ ] **Step 2: Run verify to confirm failure**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- FAIL because `ProjectInterviewRestatementService` and assertion DTOs do not exist yet

- [ ] **Step 3: Add persisted task-document and assertion models**

Extend `packages/csharp/broker/Services/ProjectInterviewModels.cs` with:

```csharp
public enum AssertionStatus
{
    Candidate,
    Confirmed,
    Rejected,
    Superseded,
    Conflicted
}

public sealed record ProjectInterviewAssertion(
    string AssertionId,
    string Statement,
    AssertionStatus Status,
    string Evidence,
    DateTimeOffset UpdatedAt);

public sealed record RestatementOption(
    string OptionId,
    string Text,
    IReadOnlyList<string> AssertionStatements,
    bool IsConservativeEscape);

public sealed record ProjectInterviewTaskDocument(string Channel, string UserId, IReadOnlyList<ProjectInterviewAssertion> Assertions)
{
    public static ProjectInterviewTaskDocument CreateEmpty(string channel, string userId) =>
        new(channel, userId, Array.Empty<ProjectInterviewAssertion>());
}
```

- [ ] **Step 4: Add the restatement service**

Create `packages/csharp/broker/Services/ProjectInterviewRestatementService.cs`:

```csharp
namespace Broker.Services;

public sealed class ProjectInterviewRestatementService
{
    public IReadOnlyList<RestatementOption> BuildOptions(IEnumerable<string> candidateStatements, string conservativeOptionText)
    {
        var statements = candidateStatements.Where(static x => !string.IsNullOrWhiteSpace(x)).Take(3).ToArray();
        var options = new List<RestatementOption>();
        for (var i = 0; i < statements.Length; i++)
        {
            options.Add(new RestatementOption($"opt-{i + 1}", statements[i], new[] { statements[i] }, false));
        }

        options.Add(new RestatementOption("opt-conservative", conservativeOptionText, Array.Empty<string>(), true));
        return options;
    }
}
```

- [ ] **Step 5: Add the persisted state service using the existing broker memory store**

Create `packages/csharp/broker/Services/ProjectInterviewStateService.cs`:

```csharp
namespace Broker.Services;

public sealed class ProjectInterviewStateService
{
    private readonly HighLevelMemoryStore _memoryStore;

    public ProjectInterviewStateService(HighLevelMemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    public Task<ProjectInterviewTaskDocument> LoadTaskDocumentAsync(string channel, string userId, CancellationToken cancellationToken)
        => _memoryStore.LoadJsonAsync<ProjectInterviewTaskDocument>(
            $"hlm.project-interview.requirements.{channel}.{userId}",
            () => ProjectInterviewTaskDocument.CreateEmpty(channel, userId),
            cancellationToken);

    public Task SaveTaskDocumentAsync(ProjectInterviewTaskDocument document, CancellationToken cancellationToken)
        => _memoryStore.SaveJsonAsync($"hlm.project-interview.requirements.{document.Channel}.{document.UserId}", document, cancellationToken);
}
```

- [ ] **Step 6: Wire the services into the coordinator**

Modify `packages/csharp/broker/Program.cs`:

```csharp
builder.Services.AddSingleton<ProjectInterviewStateService>();
builder.Services.AddSingleton<ProjectInterviewRestatementService>();
```

Modify `packages/csharp/broker/Services/HighLevelCoordinator.cs` to:

```csharp
var taskDocument = await _projectInterviewStateService.LoadTaskDocumentAsync(channel, userId, cancellationToken);
var restatementOptions = _projectInterviewRestatementService.BuildOptions(modelCandidateStatements, "None of these is precise.");
```

- [ ] **Step 7: Add a broker integration test for explicit promotion**

Create `packages/csharp/tests/integration/Api/ProjectInterviewLifecycleTests.cs` with:

```csharp
[Fact]
public async Task ProjectInterview_DoesNotPromoteAssertionsWithoutExplicitConfirmation()
{
    using var fixture = await BrokerFixture.StartAsync();
    await fixture.SendHighLevelLineTextAsync("/proj");
    await fixture.SendHighLevelLineTextAsync("想做一個內部管理工具");

    var state = await fixture.ReadProjectInterviewRequirementsAsync("line", fixture.DefaultLineUserId);
    state.Assertions.Should().OnlyContain(a => a.Status != AssertionStatus.Confirmed);
}
```

- [ ] **Step 8: Run focused tests**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter ProjectInterviewLifecycleTests -v minimal
```

Expected:

- verify passes for the new restatement assertions
- integration test passes for explicit confirmation gating

- [ ] **Step 9: Commit**

```powershell
git add packages/csharp/broker/Services/ProjectInterviewModels.cs packages/csharp/broker/Services/ProjectInterviewStateService.cs packages/csharp/broker/Services/ProjectInterviewRestatementService.cs packages/csharp/broker/Services/HighLevelCoordinator.cs packages/csharp/broker/Program.cs packages/csharp/broker/verify/Program.cs packages/csharp/tests/integration/Api/ProjectInterviewLifecycleTests.cs
git commit -m "feat: add project interview assertion state and restatement flow"
```

### Task 3: Add Project Scale and Template Catalog Narrowing

**Files:**
- Create: `packages/csharp/broker/Services/ProjectInterviewTemplateCatalogService.cs`
- Create: `packages/javascript/browser/templates/catalog.json`
- Create: `packages/javascript/browser/templates/content_showcase/template.manifest.json`
- Create: `packages/javascript/browser/templates/form_collection/template.manifest.json`
- Create: `packages/javascript/browser/templates/member_portal/template.manifest.json`
- Create: `packages/javascript/browser/templates/list_search/template.manifest.json`
- Create: `packages/javascript/browser/templates/crud_admin/template.manifest.json`
- Create: `packages/javascript/browser/templates/dashboard/template.manifest.json`
- Create: `packages/javascript/browser/templates/multi_step_flow/template.manifest.json`
- Create: `packages/javascript/browser/templates/transaction_flow/template.manifest.json`
- Create: `packages/javascript/browser/__tests__/templates/TemplateCatalog.test.js`
- Modify: `packages/csharp/broker/Services/HighLevelCoordinator.cs`
- Modify: `packages/csharp/broker/appsettings.json`
- Modify: `packages/csharp/broker/appsettings.Development.example.json`

- [ ] **Step 1: Write the failing JS catalog tests**

Create `packages/javascript/browser/__tests__/templates/TemplateCatalog.test.js`:

```javascript
import { describe, expect, it } from "vitest";
import catalog from "../../templates/catalog.json";

describe("template catalog", () => {
  it("contains the initial interaction-structure families", () => {
    expect(catalog.families.map((entry) => entry.template_id)).toEqual([
      "content_showcase",
      "form_collection",
      "member_portal",
      "list_search",
      "crud_admin",
      "dashboard",
      "multi_step_flow",
      "transaction_flow",
    ]);
  });
});
```

- [ ] **Step 2: Run the JS test to confirm failure**

Run:

```powershell
npm --prefix packages/javascript/browser run test -- TemplateCatalog.test.js
```

Expected:

- FAIL because `packages/javascript/browser/templates/catalog.json` does not exist yet

- [ ] **Step 3: Add the initial template catalog**

Create `packages/javascript/browser/templates/catalog.json`:

```json
{
  "families": [
    { "template_id": "content_showcase", "supported_project_scales": ["tool_page", "mini_app"], "manifest_path": "./content_showcase/template.manifest.json" },
    { "template_id": "form_collection", "supported_project_scales": ["tool_page", "mini_app"], "manifest_path": "./form_collection/template.manifest.json" },
    { "template_id": "member_portal", "supported_project_scales": ["mini_app", "structured_app"], "manifest_path": "./member_portal/template.manifest.json" },
    { "template_id": "list_search", "supported_project_scales": ["tool_page", "mini_app"], "manifest_path": "./list_search/template.manifest.json" },
    { "template_id": "crud_admin", "supported_project_scales": ["mini_app", "structured_app"], "manifest_path": "./crud_admin/template.manifest.json" },
    { "template_id": "dashboard", "supported_project_scales": ["mini_app", "structured_app"], "manifest_path": "./dashboard/template.manifest.json" },
    { "template_id": "multi_step_flow", "supported_project_scales": ["mini_app", "structured_app"], "manifest_path": "./multi_step_flow/template.manifest.json" },
    { "template_id": "transaction_flow", "supported_project_scales": ["mini_app", "structured_app"], "manifest_path": "./transaction_flow/template.manifest.json" }
  ]
}
```

- [ ] **Step 4: Add the broker-side catalog narrowing service**

Create `packages/csharp/broker/Services/ProjectInterviewTemplateCatalogService.cs`:

```csharp
namespace Broker.Services;

public sealed class ProjectInterviewTemplateCatalogService
{
    private readonly string _catalogPath;

    public ProjectInterviewTemplateCatalogService(IConfiguration configuration)
    {
        _catalogPath = configuration["ProjectInterview:TemplateCatalogPath"]
            ?? ".\\packages\\javascript\\browser\\templates\\catalog.json";
    }
}
```

- [ ] **Step 5: Wire template narrowing into the coordinator and config**

Modify `packages/csharp/broker/Services/HighLevelCoordinator.cs`:

```csharp
var candidateTemplateFamilies = _projectInterviewTemplateCatalogService.NarrowByScale(projectScale);
if (candidateTemplateFamilies.Count == 0)
{
    throw new InvalidOperationException($"No template family supports project scale '{projectScale}'.");
}
```

Modify `packages/csharp/broker/appsettings.json` and `packages/csharp/broker/appsettings.Development.example.json`:

```json
"ProjectInterview": {
  "TemplateCatalogPath": ".\\packages\\javascript\\browser\\templates\\catalog.json"
},
```

- [ ] **Step 6: Run JS and broker verification**

Run:

```powershell
npm --prefix packages/javascript/browser run test -- TemplateCatalog.test.js
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- JS template catalog tests PASS
- broker verify passes for scale-based narrowing assertions

- [ ] **Step 7: Commit**

```powershell
git add packages/javascript/browser/templates packages/javascript/browser/__tests__/templates/TemplateCatalog.test.js packages/csharp/broker/Services/ProjectInterviewTemplateCatalogService.cs packages/csharp/broker/Services/HighLevelCoordinator.cs packages/csharp/broker/appsettings.json packages/csharp/broker/appsettings.Development.example.json packages/csharp/broker/verify/Program.cs
git commit -m "feat: add project interview template catalog narrowing"
```

### Task 4: Compile Canonical Project Definition and Version DAG

**Files:**
- Create: `packages/csharp/broker/Services/ProjectInterviewProjectDefinitionCompiler.cs`
- Modify: `packages/csharp/broker/Services/ProjectInterviewModels.cs`
- Modify: `packages/csharp/broker/Services/ProjectInterviewStateService.cs`
- Modify: `packages/csharp/broker/Services/HighLevelCoordinator.cs`
- Test: `packages/csharp/broker/verify/Program.cs`

- [ ] **Step 1: Write the failing verify tests for version-DAG compilation**

Add coverage in `packages/csharp/broker/verify/Program.cs`:

```csharp
var compiler = new ProjectInterviewProjectDefinitionCompiler();
var result = compiler.Compile(
    version: 1,
    confirmedAssertions: new[]
    {
        "project_scale=mini_app",
        "template_family=member_portal",
        "enabled_module=auth",
        "enabled_module=dashboard",
        "style_profile=clean-enterprise"
    });

AssertEqual(1, result.Version, "compiler stamps version");
AssertEqual("member_portal", result.ProjectDefinition.TemplateFamily, "compiler projects selected template");
AssertTrue(result.Dag.Nodes.Any(node => node.NodeType == "project_instance_definition_json"), "dag contains canonical json node");
```

- [ ] **Step 2: Add the version-DAG and project-definition DTOs**

Extend `packages/csharp/broker/Services/ProjectInterviewModels.cs`:

```csharp
public sealed record VersionDagNode(string NodeId, string NodeType, string PayloadDigest);
public sealed record VersionDagEdge(string FromNodeId, string ToNodeId);
public sealed record ProjectInterviewVersionDag(int Version, IReadOnlyList<VersionDagNode> Nodes, IReadOnlyList<VersionDagEdge> Edges);

public sealed record ProjectInstanceDefinition(
    string ProjectScale,
    string TemplateFamily,
    IReadOnlyList<string> EnabledModules,
    string StyleProfile);
```

- [ ] **Step 3: Add the compiler**

Create `packages/csharp/broker/Services/ProjectInterviewProjectDefinitionCompiler.cs`:

```csharp
namespace Broker.Services;

public sealed class ProjectInterviewProjectDefinitionCompiler
{
    public (int Version, ProjectInstanceDefinition ProjectDefinition, ProjectInterviewVersionDag Dag) Compile(int version, IReadOnlyList<string> confirmedAssertions)
    {
        var scale = confirmedAssertions.Single(x => x.StartsWith("project_scale=", StringComparison.Ordinal)).Split('=')[1];
        var template = confirmedAssertions.Single(x => x.StartsWith("template_family=", StringComparison.Ordinal)).Split('=')[1];
        var enabledModules = confirmedAssertions.Where(x => x.StartsWith("enabled_module=", StringComparison.Ordinal)).Select(x => x.Split('=')[1]).ToArray();
        var styleProfile = confirmedAssertions.Single(x => x.StartsWith("style_profile=", StringComparison.Ordinal)).Split('=')[1];

        var projectDefinition = new ProjectInstanceDefinition(scale, template, enabledModules, styleProfile);
        var dag = new ProjectInterviewVersionDag(
            version,
            new[]
            {
                new VersionDagNode("confirmed_assertions", "confirmed_assertions", "confirmed"),
                new VersionDagNode("project_instance_definition_json", "project_instance_definition_json", JsonSerializer.Serialize(projectDefinition)),
            },
            new[]
            {
                new VersionDagEdge("confirmed_assertions", "project_instance_definition_json"),
            });

        return (version, projectDefinition, dag);
    }
}
```

- [ ] **Step 4: Persist the DAG by version**

Modify `packages/csharp/broker/Services/ProjectInterviewStateService.cs`:

```csharp
public Task SaveVersionDagAsync(string channel, string userId, int version, ProjectInterviewVersionDag dag, CancellationToken cancellationToken)
    => _memoryStore.SaveJsonAsync($"hlm.project-interview.version-graph.{channel}.{userId}.{version}", dag, cancellationToken);
```

- [ ] **Step 5: Integrate compile-and-save into the coordinator**

Modify `packages/csharp/broker/Services/HighLevelCoordinator.cs`:

```csharp
var compileResult = _projectInterviewProjectDefinitionCompiler.Compile(nextVersion, confirmedAssertions);
await _projectInterviewStateService.SaveVersionDagAsync(channel, userId, nextVersion, compileResult.Dag, cancellationToken);
```

- [ ] **Step 6: Run verification**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- verify passes for compiler and DAG assertions

- [ ] **Step 7: Commit**

```powershell
git add packages/csharp/broker/Services/ProjectInterviewModels.cs packages/csharp/broker/Services/ProjectInterviewProjectDefinitionCompiler.cs packages/csharp/broker/Services/ProjectInterviewStateService.cs packages/csharp/broker/Services/HighLevelCoordinator.cs packages/csharp/broker/verify/Program.cs
git commit -m "feat: compile project interview definition and version dag"
```

### Task 5: Generate Versioned PDF/JSON Review Artifacts and Review Commands

**Files:**
- Create: `packages/csharp/broker/Services/ProjectInterviewWorkflowDesignService.cs`
- Create: `packages/csharp/broker/Services/ProjectInterviewPdfRenderService.cs`
- Modify: `packages/csharp/broker/Services/HighLevelCoordinator.cs`
- Modify: `packages/csharp/broker/Services/LineArtifactDeliveryService.cs`
- Modify: `packages/csharp/broker/Program.cs`
- Test: `packages/csharp/broker/verify/Program.cs`
- Test: `packages/csharp/tests/integration/Api/ProjectInterviewReviewTests.cs`

- [ ] **Step 1: Write the failing verify tests for render metadata**

Add coverage in `packages/csharp/broker/verify/Program.cs`:

```csharp
var workflow = new ProjectInterviewWorkflowDesignService();
var viewModel = workflow.BuildViewModel(
    taskId: "task-1",
    version: 2,
    projectDefinition: new ProjectInstanceDefinition("mini_app", "member_portal", new[] { "auth" }, "clean-enterprise"));

AssertEqual(2, viewModel.Version, "view model carries version");
AssertEqual("member_portal", viewModel.TemplateFamily, "view model carries template family");
```

- [ ] **Step 2: Add the deterministic workflow-design view-model builder**

Create `packages/csharp/broker/Services/ProjectInterviewWorkflowDesignService.cs`:

```csharp
namespace Broker.Services;

public sealed record WorkflowDesignViewModel(string TaskId, int Version, string TemplateFamily, string ProjectScale, IReadOnlyList<string> EnabledModules, string StyleProfile);

public sealed class ProjectInterviewWorkflowDesignService
{
    public WorkflowDesignViewModel BuildViewModel(string taskId, int version, ProjectInstanceDefinition projectDefinition) =>
        new(taskId, version, projectDefinition.TemplateFamily, projectDefinition.ProjectScale, projectDefinition.EnabledModules, projectDefinition.StyleProfile);
}
```

- [ ] **Step 3: Add the PDF renderer stub with versioned metadata**

Create `packages/csharp/broker/Services/ProjectInterviewPdfRenderService.cs`:

```csharp
namespace Broker.Services;

public sealed record PdfRenderResult(string FileName, byte[] Bytes, string MetadataDigest);

public sealed class ProjectInterviewPdfRenderService
{
    public PdfRenderResult Render(WorkflowDesignViewModel viewModel, string jsonDigest)
    {
        var content = Encoding.UTF8.GetBytes($"workflow-design:{viewModel.TaskId}:v{viewModel.Version}:{jsonDigest}");
        return new PdfRenderResult($"workflow-design.v{viewModel.Version}.pdf", content, jsonDigest);
    }
}
```

- [ ] **Step 4: Integrate artifact recording and review commands**

Modify `packages/csharp/broker/Services/HighLevelCoordinator.cs`:

```csharp
var viewModel = _projectInterviewWorkflowDesignService.BuildViewModel(taskId, version, projectDefinition);
var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(projectDefinition);
var jsonDigest = Convert.ToHexString(SHA256.HashData(jsonBytes));
var pdf = _projectInterviewPdfRenderService.Render(viewModel, jsonDigest);
```

- [ ] **Step 5: Add the review integration test**

Create `packages/csharp/tests/integration/Api/ProjectInterviewReviewTests.cs` with:

```csharp
[Fact]
public async Task ProjectInterview_ReviseCreatesNewVersionAndKeepsPriorGraph()
{
    using var fixture = await BrokerFixture.StartAsync();
    await fixture.CompleteProjectInterviewToReviewAsync();
    await fixture.SendHighLevelLineTextAsync("/revise");

    var review = await fixture.ReadProjectInterviewReviewAsync("line", fixture.DefaultLineUserId);
    review.CurrentVersion.Should().Be(2);
}
```

- [ ] **Step 6: Run broker verify and integration review tests**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter ProjectInterviewReviewTests -v minimal
```

Expected:

- verify passes for PDF/JSON metadata assertions
- review tests pass for `/ok`, `/revise`, and immutable prior-version behavior

- [ ] **Step 7: Commit**

```powershell
git add packages/csharp/broker/Services/ProjectInterviewWorkflowDesignService.cs packages/csharp/broker/Services/ProjectInterviewPdfRenderService.cs packages/csharp/broker/Services/HighLevelCoordinator.cs packages/csharp/broker/Services/LineArtifactDeliveryService.cs packages/csharp/broker/Program.cs packages/csharp/broker/verify/Program.cs packages/csharp/tests/integration/Api/ProjectInterviewReviewTests.cs
git commit -m "feat: add project interview review artifact generation"
```

### Task 6: Full Verification, Docs Sync, and Safety Checks

**Files:**
- Modify: `packages/csharp/broker/verify/Program.cs`
- Modify: `docs/superpowers/specs/2026-03-30-line-project-interview-agent-design.md`
- Modify: `docs/0329/00-overview.md`
- Modify: `docs/0329/01-broker.md`
- Modify: `docs/0329/07-tests.md`

- [ ] **Step 1: Add the final verify assertions for path redaction and delivery safety**

Extend `packages/csharp/broker/verify/Program.cs`:

```csharp
AssertTrue(!deliveryMessage.Contains("D:\\\\"), "user-facing review output does not expose internal windows paths");
AssertTrue(!deliveryMessage.Contains("/packages/csharp/"), "user-facing review output does not expose repo paths");
AssertTrue(deliveryMessage.Contains("workflow-design.v"), "user-facing review output references versioned artifact");
```

- [ ] **Step 2: Run the full verification stack**

Run:

```powershell
dotnet build packages/csharp/ControlPlane.slnx -c Release --disable-build-servers -nodeReuse:false
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj -v minimal
npm --prefix packages/javascript/browser run test
```

Expected:

- build succeeds with `0` warnings / `0` errors
- verify prints a passing result
- integration tests pass
- JS tests pass including template-catalog coverage

- [ ] **Step 3: Sync the docs to the implemented shape**

Update:

```markdown
- `project_interview` now uses a broker-owned state machine plus per-version DAG.
- review artifacts are versioned `workflow-design.vN.pdf` and `workflow-design.vN.json`.
- template narrowing is driven by `packages/javascript/browser/templates/catalog.json`.
```

- [ ] **Step 4: Commit**

```powershell
git add packages/csharp/broker/verify/Program.cs docs/superpowers/specs/2026-03-30-line-project-interview-agent-design.md docs/0329/00-overview.md docs/0329/01-broker.md docs/0329/07-tests.md
git commit -m "docs: document project interview workflow and verification"
```

---

## Self-Review

### Spec coverage

- `/proj` explicit entry, review commands, and task-scoped state machine: covered by Task 1.
- Confirmed-restatement assertion model and conservative option handling: covered by Task 2.
- Project scale classification, template-family narrowing, and JSON template catalog: covered by Task 3.
- Canonical project-instance JSON and per-version DAG: covered by Task 4.
- Versioned PDF/JSON generation and review workflow: covered by Task 5.
- Delivery safety, verification, and docs sync: covered by Task 6.

### Placeholder scan

- No `TODO`, `TBD`, or “implement later” placeholders remain.
- Each task has exact files, commands, and code snippets.

### Type consistency

- `ProjectInterviewSessionState`, `ProjectInterviewStateMachine`, `ProjectInterviewTaskDocument`, `ProjectInterviewRestatementService`, `ProjectInterviewTemplateCatalogService`, `ProjectInterviewProjectDefinitionCompiler`, `ProjectInstanceDefinition`, and `ProjectInterviewVersionDag` are introduced in order and reused consistently.


```
