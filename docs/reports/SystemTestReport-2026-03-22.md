# System Test Report 2026-03-22

## Scope

This report records the repository-wide validation run performed on 2026-03-22 in the local Windows development environment.

## Environment

- OS: Windows
- Node.js: `v22.17.1`
- npm: `11.5.1`
- .NET SDK: `10.0.104`
- Podman: `5.5.2`

## Commands Executed

### Build

- `dotnet build Bricks4Agent.sln -c Release --disable-build-servers -nodeReuse:false`
- `dotnet build packages/csharp/workers/line-worker/LineWorker.csproj -c Release --disable-build-servers -nodeReuse:false`

### JavaScript / UI

- `npm test`
- `npm run validate:ui-library`
- `npm run validate:ui-library:browser`
- `npm run validate:ui-state`
- `npm run validate:backend-governance`

### Broker / Agent / BaseOrm

- `npm run validate:agent-governed`
- `npm run validate:broker-scope`
- `npm run validate:broker-llm-proxy`
- `npm run validate:baseorm-sync`
- `npm run validate:baseorm`
- `dotnet run --project packages/csharp/database/BaseOrm/net8/verify/BaseOrm.Verify.csproj`
  with `BASEORM_SQLSERVER_CONNECTION_STRING=Server=localhost\SQL2022;Database=master;User ID=TIMUser;Password=TIMUser;TrustServerCertificate=True`

### Podman Integration

- `npm run validate:podman-governed-stack`
- `npm run validate:podman-openai-compatible-stack`
- `npm run validate:podman-ollama-host-stack`

### Live LINE / High-Level Flow

- `status-sidecar-stack.ps1`
- `verify-high-level-process.ps1`
- `verify-live-webhook.ps1`

## Results

### Passed

- Solution build completed with `0 errors`.
- `LineWorker` release build completed with `0 errors / 0 warnings`.
- Page generator examples passed.
- UI library validation passed.
- UI browser smoke passed for `8` demos.
- UI state contract validation passed with `13/13`.
- .NET dependency and API governance validation passed.
- Governed agent validation passed.
- Broker route/path scope validation passed.
- Broker LLM proxy integration passed.
- BaseOrm sync validation passed.
- BaseOrm verification passed.
- BaseOrm live SQL Server verification passed.
- Podman governed stack passed.
- Podman OpenAI-compatible stack passed.
- Podman Ollama-host stack passed.
- LINE sidecar status check passed.
- Live signed webhook check returned `200`.
- High-level broker processing accepted UTF-8 Chinese production input and created a pending production draft.

### Passed With Existing Warnings / Gaps

- `dotnet build Bricks4Agent.sln` reported `207` warnings and `0` errors.
- Warnings remain concentrated in:
  - `packages/csharp/security/Mfa`
  - `packages/csharp/security/AuditLog`
  - `packages/csharp/security/AccountLock`
- BaseOrm live MySQL and PostgreSQL direct integration were skipped because connection strings were not configured in the shell during this run.
  Podman-based integration coverage for governed stacks still passed.

### Verified Runtime Behavior

- Canonical LINE ingress path is working:
  `LINE webhook -> ngrok -> line-worker -> broker /api/v1/high-level/line/process`
- Current canonical sidecar ports:
  - broker: `5361`
  - LINE webhook ingress: `5357`
- High-level production requests in Chinese correctly route to `production`, require project naming, and stay UTF-8 safe end-to-end.
- Managed workspace roots in sidecar mode were observed under:
  `D:\Bricks4Agent\.run\line-sidecar\broker\managed-workspaces`

## Current Limitations Confirmed By Test

1. Query routing works, but only explicit broker-mediated query tooling is currently available.
   The live controlled search path is `?search <keywords>`, which now executes `web.search.duckduckgo` under broker mediation.
   Plain `?query` traffic still follows the high-level dialogue path rather than auto-selecting a real-time search tool.

2. High-level production flow is operational up to draft creation and confirmation handoff, but this report does not claim full downstream artifact generation from LINE.

3. The solution is buildable on .NET 10 SDK, but the nullable-warning backlog is still large enough that manuals should not describe the backend as warning-clean.

## Overall Assessment

The repository is currently in a testable and operational state for:

- UI library validation
- page generation
- governed broker/agent execution
- BaseOrm verification
- governed Podman stacks
- high-level LINE ingress and production draft routing

The main remaining functional gap exposed by the test run is that broker-mediated real-time query tooling is explicit rather than generally auto-selected for arbitrary query text.
