# Bricks4Agent

中文版本：

- [README.zh-TW.md](/d:/Bricks4Agent/README.zh-TW.md)

## Position

`Bricks4Agent` is a broker-mediated AI operations prototype evolving toward a control plane.

It is no longer accurately described as only:

- an AI coding CLI
- a page generator
- a UI component library

Those subsystems still exist, but the current live system already includes:

- LINE ingress
- broker-governed high-level routing
- structured intent, memory, and promotion gates
- governed execution
- per-user managed workspaces
- artifact generation and delivery
- browser-governance groundwork
- Azure VM IIS deployment groundwork
- local admin console

## Current Canonical Live Path

`LINE webhook -> ngrok public URL -> line-worker -> broker /api/v1/high-level/line/process`

Current local canonical sidecar ports:

- broker: `127.0.0.1:5361`
- line-worker webhook: `127.0.0.1:5357`

Important clarification:

- `agent --line-listen` is legacy/development-only
- the canonical LINE path is `line-worker -> broker high-level coordinator`

## Main Areas

### Broker and control plane

- [broker](/d:/Bricks4Agent/packages/csharp/broker)
- [broker-core](/d:/Bricks4Agent/packages/csharp/broker-core)

### LINE ingress and operator path

- [line-worker](/d:/Bricks4Agent/packages/csharp/workers/line-worker)

### Agent runtime and governed execution

- [tools/agent](/d:/Bricks4Agent/tools/agent)
- [tools/agent/container](/d:/Bricks4Agent/tools/agent/container)

### UI library and generation

- [ui_components](/d:/Bricks4Agent/packages/javascript/browser/ui_components)
- [page-generator](/d:/Bricks4Agent/packages/javascript/browser/page-generator)
- [templates/spa](/d:/Bricks4Agent/templates/spa)
- [tools/spa-generator](/d:/Bricks4Agent/tools/spa-generator)

### Documents and design notes

- [docs/reports](/d:/Bricks4Agent/docs/reports)
- [docs/designs](/d:/Bricks4Agent/docs/designs)
- [docs/manuals](/d:/Bricks4Agent/docs/manuals)

## Module Entry Points

Primary module and subsystem entry documents:

- [packages/csharp/workers/line-worker/README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)
- [packages/csharp/workers/line-worker/README.zh-TW.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.zh-TW.md)
- [tools/agent/README.md](/d:/Bricks4Agent/tools/agent/README.md)
- [tools/agent/container/README.md](/d:/Bricks4Agent/tools/agent/container/README.md)
- [packages/javascript/browser/page-generator/README.md](/d:/Bricks4Agent/packages/javascript/browser/page-generator/README.md)
- [tools/spa-generator/README.md](/d:/Bricks4Agent/tools/spa-generator/README.md)
- [templates/spa/README.md](/d:/Bricks4Agent/templates/spa/README.md)
- [templates/spa/scripts/README.md](/d:/Bricks4Agent/templates/spa/scripts/README.md)
- [tools/static-server/README.md](/d:/Bricks4Agent/tools/static-server/README.md)
- [packages/csharp/reporting/README.md](/d:/Bricks4Agent/packages/csharp/reporting/README.md)
- [packages/csharp/reporting/ExampleHost/README.md](/d:/Bricks4Agent/packages/csharp/reporting/ExampleHost/README.md)
- [packages/csharp/database/BaseOrm/README.md](/d:/Bricks4Agent/packages/csharp/database/BaseOrm/README.md)

## Sample Projects

Representative sample/generated project entry points:

- [projects/ShopBricks-Gen/README.md](/d:/Bricks4Agent/projects/ShopBricks-Gen/README.md)
- [projects/ShopBricks-Gen/scripts/README.md](/d:/Bricks4Agent/projects/ShopBricks-Gen/scripts/README.md)
- [projects/ShopBricks/scripts/README.md](/d:/Bricks4Agent/projects/ShopBricks/scripts/README.md)

## Current High-Level Model

The LINE high-level responder is currently configured to use:

- provider: `openai-compatible`
- model: `gpt-5.4-mini`

This high-level model handles:

- conversation
- clarification
- mediated query synthesis
- execution-model suggestion

It is separate from downstream execution-model requests.

## Current High-Level Interaction Grammar

Representative commands include:

- `?help` / `?h`
- `?search` / `?s`
- `?rail` / `?r`
- `?hsr`
- `?bus` / `?b`
- `?flight` / `?f`
- `?profile` / `?p`
- `/name` / `/n`
- `/id` / `/i`
- `#ProjectName`
- `confirm`
- `cancel`

## Quick Start

### Canonical local sidecar path

Use:

- [packages/csharp/workers/line-worker/README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)
- [docs/manuals/line-sidecar-runbook.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.md)
- [docs/manuals/line-sidecar-runbook.zh-TW.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)

Canonical startup command:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
```

Canonical status command:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
```

Canonical verification command:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -Message "hello"
```

### Agent container path (controlled autonomous agent)

A separate, podman-based governed stack where an LLM-driven agent runs in an
isolated container and may only act through the broker (claim work, request
governed tool execution, report results) — it never touches tools, data, or
model providers directly. See [docs/manuals/agent-container-runbook.md](/d:/Bricks4Agent/docs/manuals/agent-container-runbook.md).

Three LLM backends, all verified end-to-end (2026-06-13):

```powershell
# mock (offline, validates the governed chain)
node tools/agent/tests/test-podman-governed-stack.js

# local ollama (real open model, e.g. qwen3.6)
node tools/agent/tests/test-podman-ollama-host-stack.js

# commercial API (real ChatGPT)
$env:OPENAI_BASE_URL="https://api.openai.com"; $env:OPENAI_API_KEY="<key>"
$env:OPENAI_API_FORMAT="responses"; $env:STACK_MODEL="gpt-5.4-mini"
node tools/agent/tests/test-podman-openai-compatible-stack.js
```

Requires podman (Windows: `podman machine start` on first use). The broker's
`LlmProxy` speaks ollama, OpenAI chat, and OpenAI responses formats.

### Local admin console

- `http://127.0.0.1:5361/line-admin.html`

If no admin credential exists in the local DB, the initial password is `admin` and the first login requires a password change.

## Current Strengths

- coherent control-plane direction
- real live LINE ingress path
- explicit command grammar and workflow gating
- growing separation between raw log, interpretation, memory, and execution intent
- practical integrations for delivery and deployment

## Current Limits

- maturity is uneven across subsystems
- broker remains a necessary central node and must be kept narrow and disciplined
- browser governance is still groundwork, not a finished browser automation platform
- deployment and delivery paths are real, but not yet fully generalized platform primitives

## Recommended Reading Order

1. [docs/reports/CurrentArchitectureAndProgress-2026-06-13.md](/d:/Bricks4Agent/docs/reports/CurrentArchitectureAndProgress-2026-06-13.md)
2. [packages/csharp/workers/line-worker/README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)
3. [docs/manuals/line-sidecar-runbook.zh-TW.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)
4. subsystem-specific documents in `docs/designs`

## Documentation

### Current system and architecture

- [CurrentArchitectureAndProgress-2026-06-13.md](/d:/Bricks4Agent/docs/reports/CurrentArchitectureAndProgress-2026-06-13.md) (current)
- [CurrentArchitectureAndProgress-2026-03-26.md](/d:/Bricks4Agent/docs/reports/CurrentArchitectureAndProgress-2026-03-26.md) (superseded)
- [Agent Container Runbook](/d:/Bricks4Agent/docs/manuals/agent-container-runbook.md)

### Manuals

- [User Guide](/d:/Bricks4Agent/docs/manuals/user-guide.md)
- [Engineer Guide](/d:/Bricks4Agent/docs/manuals/engineer-guide.md)
- [Engineer Guide (EN)](/d:/Bricks4Agent/docs/manuals/engineer-guide-en.md)
- [LINE Sidecar Runbook](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.md)
- [LINE Sidecar 操作手冊](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)

### Design notes

- [HighLevelModelRoutingAndMemory.md](/d:/Bricks4Agent/docs/designs/HighLevelModelRoutingAndMemory.md)
- [HighLevelMemoryAndLoggingModel.md](/d:/Bricks4Agent/docs/designs/HighLevelMemoryAndLoggingModel.md)
- [ToolSpecRegistry.md](/d:/Bricks4Agent/docs/designs/ToolSpecRegistry.md)
- [GoogleDriveDelivery.md](/d:/Bricks4Agent/docs/designs/GoogleDriveDelivery.md)
- [AzureVmIisDeployment.md](/d:/Bricks4Agent/docs/designs/AzureVmIisDeployment.md)
