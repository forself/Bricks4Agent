# Execution Adapters (§18.1) — Test-Driven Implementation Plan

Date: 2026-06-13
Status: **implemented + e2e-verified** (Steps A–F done; 38 real-git unit assertions, config test, and a full podman stack test where a model drives `apply_patch` through the governed chain and the file is actually patched — all green; no regression to the governed stack). Remaining: broker `--integration` HTTP coverage of the new routes.
Spec: [ControlledAutonomousAISystemTechnicalDesign.md](ControlledAutonomousAISystemTechnicalDesign.md) §14 (執行配接層), §18.1 (MVP), §9.2 (structured request), §6.4 (capability catalog), §13.2 (execution baseline), §21 (success criteria)
Builds on: agent container already activated + sealed (§13.1 egress, §13 OS sandbox) — see [CurrentArchitectureAndProgress-2026-06-13.md](../reports/CurrentArchitectureAndProgress-2026-06-13.md)

## 0. Goal

Make the controlled agent able to do **real work** — not just read under governance — by adding the two §18.1 MVP execution adapters:

- **repo-adapter** — apply a patch to a repo under scope, save a diff artifact, return a change summary.
- **build-test-adapter** — run a whitelisted build/test command in isolation, return structured stdout/stderr + exit code.

The agent never touches the repo or runs commands directly (§6.6). It emits a **structured execution request**; the broker adjudicates (capability + grant + scope + quota + policy); a **hardened adapter worker** executes and returns evidence. This is the piece that turns "powered-on, sealed, read-only agent" into "agent that can propose and—under governance—land changes."

## 1. Architecture decision

**Adapters are a dedicated worker (`execution-adapter-worker`) implementing `ICapabilityHandler`, NOT inline broker routes.** Rationale, anchored to spec:

- §14 names a distinct "執行配接層" (execution adapter layer) — keep the broker a pure control plane.
- §13.2 requires adapters run in an isolated, hardened environment — a worker container gives that.
- §21 #4 requires work be re-executable on a different node without changing governance — a stateless worker satisfies it.
- Reuses the **proven** path: `WorkerHost` → `WORKER_REGISTER` → `PoolDispatcher` round-robin → `ICapabilityHandler.ExecuteAsync`, with the broker's 16-step PEP (`BrokerService.SubmitExecutionRequestAsync`) already enforcing grant/quota/scope/policy before dispatch.

### Handler contract (exact, from `packages/csharp/worker-sdk/ICapabilityHandler.cs`)
```csharp
string CapabilityId { get; }
Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
    string requestId, string route, string payload, string scope, CancellationToken ct);
```
`scope` (JSON) carries the grant's `ScopeOverride` (allowed_paths, base_commit, max_patch_files). The adapter **re-validates** scope itself (§14.1 "配接器不得自行放寬 scope" — defense in depth, never widen).

### Capabilities (spec-exact naming, §6.4 example)
| capability_id | route | action | resource | risk | approval_policy (MVP) |
|---|---|---|---|---|---|
| `repo.patch.apply` | `execution.repo.apply_patch` | update | repository | medium | `auto_if_task_scope_match` |
| `build.test.run` | `execution.build_test.run` | execute | build | medium | `auto_if_task_scope_match` |

Seeded in `packages/csharp/broker-core/Data/BrokerDbInitializer.cs` `SeedCapabilities()` with `param_schema` from §9.2 / §14.

### Network/workspace posture (differs from the agent — adapters are trusted execution nodes)
- The **agent** stays sealed (internal-only `agent-net`, no egress) — unchanged.
- The **adapter worker** is on the broker's worker network and (for build/test) needs **egress** (npm/nuget/pip restore). It mounts the repo workspace **rw** (it is the control-plane-mediated write path, §13.4). It still gets §13.2 OS hardening (non-root, read-only rootfs + tmpfs, cap-drop, no docker socket) — only its network egress and workspace-write differ from the agent.

## 2. Test-first inventory (write these RED before any implementation)

Per repo conventions (custom broker test runner returning `(passed, failed)`; node stack tests asserting on `combinedOutput`).

### 2a. Broker unit tests — `packages/csharp/tests/broker-tests/ExecutionAdapterTests.cs`
Registered in `Program.cs` alongside `AgentContainerTests.Run()`.

**repo-adapter:**
1. valid patch within scope → files match patch, diff artifact written, change-summary returned, `Success=true`
2. `base_commit` ≠ repo HEAD → `Success=false`, reason "base commit mismatch", **no writes**
3. patch touches path outside `scope.allowed_paths` → rejected, **no writes** (§14.2)
4. payload is free-form shell / not a patch → rejected (§14.2 "不得直接接受自由 shell")
5. patch implicitly rewrites a file not in the patch → rejected (§14.2)
6. idempotency: replay same `idempotency_key` → returns prior result, does **not** double-apply (§14.1)

**build-test-adapter:**
7. whitelisted command (`dotnet --version` or a stub) → structured result: stdout, stderr, exit_code, `Success=true`
8. non-whitelisted command (`rm -rf`, arbitrary shell) → rejected (§14.3 whitelist)
9. large output truncated; exit code preserved; evidence_ref present

**catalog:**
10. `SeedCapabilities()` produces `repo.patch.apply` + `build.test.run` with correct route/risk/param_schema

### 2b. Broker integration tests — `ExecutionAdapterIntegrationTests.RunAsync(brokerUrl)` (`-- --integration`)
11. submit `repo.patch.apply` with a valid grant → success; with no grant → `Denied` (PEP seam)
12. an audit row is written with `trace_id` + `request_id` (§21 #3 traceability)

### 2c. Node stack test — `tools/agent/tests/test-podman-execution-adapter-stack.js`
13. bring up broker + `execution-adapter-worker` + agent; instruct the agent (mock LLM scripted tool call) to apply a tiny patch; assert `combinedOutput` includes the governed route + an `EXEC_ADAPTER_OK` marker; assert the target file actually changed in a mounted snapshot; `down -v` cleanup.

### 2d. Config validation test — `tools/agent/tests/test-execution-adapter-config.js` (no containers)
14. compose declares `execution-adapter-worker` with §13.2 hardening keys (read_only, cap_drop ALL, no-new-privileges, no docker socket) and the repo mount.

## 3. Implementation steps (each turns specific tests green)

- **Step A — capability seed + result conventions** (broker-core). Seed the two capabilities; define a shared result envelope `{ success, summary, evidence_ref, stdout?, stderr?, exit_code? }`. → tests 10, (enables 1–9).
- **Step B — repo-adapter handler.** New `packages/csharp/workers/execution-adapter-worker/` with `RepoApplyPatchHandler : ICapabilityHandler` (`CapabilityId="repo.patch.apply"`): validate base_commit, enforce allowed_paths + patch-only writes, apply patch, write diff artifact, return summary + evidence; idempotency store. → tests 1–6.
- **Step C — build-test-adapter handler.** `BuildTestRunHandler : ICapabilityHandler` (`CapabilityId="build.test.run"`): config-driven command whitelist, run in workspace, capture stdout/stderr+exit, truncate, structured result + evidence. → tests 7–9.
- **Step D — agent tool surface.** `tools/agent/lib/tool-registry.js`: add `apply_patch → repo.patch.apply` and `run_build_test → build.test.run` to `TOOL_DEFINITIONS` + `TOOL_TO_CAPABILITY`; `governed-executor.js`: intent strings. → supports 13.
- **Step E — compose wiring + hardening.** Add `execution-adapter-worker` to the three compose files (worker-net + egress for build, repo mount rw, §13.2 hardening, no docker socket); grant the capabilities via `DevelopmentSeed__RuntimeDescriptor`. → tests 14, 13.
- **Step F — integration + stack run.** Run broker unit + `--integration`; run the new stack test; verify scope enforcement, evidence, and audit end-to-end. → tests 11–13.

## 4. Invariants that must hold (each is tested)
- Adapter executes **only** an `ApprovedRequest` routed by the broker — agent can't call it directly (§6.6, §21 #2).
- Adapter **re-validates** scope; refuses out-of-scope writes, free-form shell, non-whitelisted commands (§14.1–14.3).
- Every result carries `evidence_ref`; every execution writes an audit row (§14.1, §21 #3).
- Idempotency key honored (§14.1).
- Adapter is **stateless** — all durable state in the control plane / shared context (§21 #4).
- Adapter container hardened per §13.2; **no docker socket**, non-root, read-only rootfs.

## 5. Approval seam (Phase Two §18.2 — NOT built now)
Capabilities carry `risk_level` + `approval_policy`. In the MVP both are `auto_if_task_scope_match`. The adapter **never** sees approval logic — it only ever receives pre-approved requests. When §18.2 lands, the broker will intercept `require_approval` decisions before dispatch; the adapter contract does not change. Keep it that way.

## 6. Open decisions (need Benson's call before/while building)
1. **Patch tooling**: `git apply` in the adapter worker vs a managed diff applier. (Recommend `git apply --check` then apply, for base_commit + path enforcement.)
2. **Build/test egress**: confirm the adapter worker may reach npm/nuget/pip registries (it must, for restore) — adapter on an egress network, distinct from the sealed agent. OK?
3. **Evidence/artifact storage**: reuse the existing artifact store / shared-context entry, or a new `evidence/` path? (Recommend reuse the artifact pattern already used by deploy/ delivery.)
4. **Idempotency replay**: return cached result vs re-run-and-compare. (Recommend return cached.)
5. **Quota timing**: reserve at submit vs charge on result. (Recommend charge on success.)

## 7. Out of scope (MVP)
Approval service / risk-tier UI (§18.2), custom seccomp profile, control-plane console, multi-repo orchestration.

## 8. Test artifacts & cleanup (CLAUDE.md)
Temp git repos under `.test-output/` or `GetTempPath()` with GUID suffix; `DeleteDirectoryWithRetry`; broker.db + `-shm`/`-wal` removed; `podman compose down -v`. New artifact patterns added to the CLAUDE.md table.

## 9. Estimated shape
~2 handlers (~300–500 LoC C#), 1 worker project + Containerfile, 3 compose edits, ~14 tests across 4 files, 2 agent tool entries, 2 capability seeds, doc updates. Largest risk: patch application + scope enforcement correctness (mitigated by tests 1–6 first).
