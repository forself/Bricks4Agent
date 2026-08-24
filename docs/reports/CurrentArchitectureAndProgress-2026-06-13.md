# Bricks4Agent Current Architecture And Progress

Date: 2026-06-13
Status: current working report
Supersedes: [CurrentArchitectureAndProgress-2026-03-26.md](CurrentArchitectureAndProgress-2026-03-26.md)

## 1. Executive Summary

Since the 2026-03-26 report the project went through a **three-way convergence**
and the **first real activation of the controlled agent container**.

`origin/main` had stalled while three developers worked on long-lived branches.
They were merged into one verified baseline, and then the controlled-agent
container path — code-complete but never powered on — was driven end-to-end for
the first time, against a mock backend, a host-side Ollama model, and a real
commercial API (ChatGPT). The macro position holds and is firmer:

**a broker-centered governed AI operations platform whose controlled-agent core now actually runs.**

## 2. Three-Way Convergence (PR #3, merged)

`origin/main` was 22 days stale and behind three developers. They converged:

| Source | Role | Contribution |
|---|---|---|
| **Benson Hsiao** (base) | platform + site-crawler | full site-crawler: worker, deterministic extractor, template framework, visual reconstruction, package verifier, LINE delivery |
| **Codex** (infra) | platform infra | worker auth credential provisioning, interview→construction handoff, browser action-level runtime, IIS deploy backup+rollback, agent container hardening, secrets out of repo, sidecar signed downloads |
| **AnthonyLee** (monitoring) | system monitoring | worker health-score (heartbeat/dispatch/resource) + snapshots, LLM proxy metrics, `/api/v1/health/*` endpoints |

The three were highly orthogonal (different files); the only shared core
(WorkerAuth) was Benson's original baseline inherited byte-identical. AnthonyLee's
quantitative-trading application layer (219 files) was intentionally left out of
the platform — it belongs in its own repo sharing the worker-sdk + broker contract.

## 3. Controlled Agent Container — Activated

Design §6.6 defines the agent container as a controlled execution shell: it may
only claim work, read authorized context, call a model, emit structured
execution requests, and report — never touching tools, data, repos, deployment,
or model providers directly.

The runtime code (~4639 lines under `tools/agent`) and broker governance
(capability catalog, scoped token, worker auth, audit, shared context) were
complete but **had never been powered on**. As of 2026-06-13 it runs end-to-end,
verified across three LLM backends:

| Backend | Model | Result |
|---|---|---|
| mock | mock-ollama/openai | `STACK_OK` + `[governed] read_file` |
| local ollama | host Ollama model via `STACK_MODEL` | broker-mediated round trip, agent completion, clean session close |
| OpenAI-compatible | bundled mock-openai by default; real OpenAI only with `OPENAI_BASE_URL` + key | protocol path via governed chain; real-provider run is env-gated |
| Anthropic Claude | `anthropic` provider / `claude-sonnet-4-6` via broker adapter and sidecar override | request/response adapter and sidecar config are unit/config-tested; not represented as a dedicated compose stack |

This satisfies the verifiable parts of §21: humans and AI go through the same
authorization path, and the container reaches tools only via broker
adjudication. The mock stack remains the deterministic text/tool sentinel; live
Ollama validation checks the broker-mediated round trip because local model
instruction following varies by model. Operations: [agent-container-runbook.md](../manuals/agent-container-runbook.md).
Activation detail: [AgentContainerActivation-2026-06-13.md](AgentContainerActivation-2026-06-13.md).

## 4. Commercial Model API

The broker `LlmProxy` speaks ollama, OpenAI chat (`v1/chat/completions`), and
OpenAI responses (`v1/responses`) with a Bearer key, plus Anthropic Claude
Messages (`/v1/messages`) with `x-api-key` and `anthropic-version`. The agent
container itself holds **no** provider key or base URL — it talks only to the
broker, and the **broker's** `LlmProxy` reaches the commercial API. Pointing that
proxy at a real model is a broker-side setting. The sidecar now prefers
`ANTHROPIC_API_KEY` and writes runtime overrides for `anthropic` /
`claude-sonnet-4-6`; without that key it keeps the existing OpenAI-compatible
`Api.txt` fallback path. The default OpenAI-compatible validation uses bundled
mock-openai so the protocol path is repeatable without secrets; real-provider
validation is available when provider environment variables are supplied. So the
broker is already the inference gateway for the agent path; what is missing is
*enforcing* that the container cannot bypass it.

## 5. Integration Bugs Surfaced By First Activation

Powering on the container chain for the first time exposed a chain of
integration bugs that unit tests and mock stacks could not catch — each fixed:

1. broker container build `NETSDK1152` — site-crawler-worker appsettings leaking into broker publish

2. broker fails to boot with `FunctionPool=false` — monitoring HealthScoreService unconditionally depends on IWorkerRegistry

3. session register 500 `No data exists` — Microsoft.Data.Sqlite `IsDBNull` Linux edge case in BaseOrm

4. `GET /api/v1/health` `Body was inferred` — health endpoints registered without their FunctionPool services

5. real-provider responses API returns 200 but agent gets empty output — responses parser only read the top-level `output_text` SDK convenience field

Two of these (2, 4) were regressions from integrating the monitoring layer —
caught because the integration agent only built, never ran.

## 6. Capability & Worker Surface

Workers: line, file, browser, transport-tdx, site-crawler (+ the trading-worker
family kept in AnthonyLee's domain). Tool specs cover web search, transport,
commerce, Google Drive delivery, Azure IIS deploy, Wikipedia, browser reference,
site crawl source, and the agent-container governed tools (read_file etc.).

## 7. Hard Critique

### Stronger than March

- main is now a live convergence of three developers' work, not a stalled baseline.

- The controlled-agent core — the project's central thesis — actually runs, on a real commercial model.

- The "language → gated structure → executed-under-governance" loop has a working demonstration end to end.

### Still weak / honest limits

- The controlled agent container is past the read-only stage: the two §18.1 execution adapters (repo-adapter, build-test-adapter) are **implemented and verified end-to-end** (see below), so the agent now does real work — a model drove the agent to apply a patch all the way through the governed chain and the file was actually changed. **Still not done: broker `--integration` coverage of the new adapter routes.** The container itself is hardened (§13.1 egress + §13 OS sandbox).

- Execution adapters (§18.1) — **implemented + e2e-verified 2026-06-13**:

- A dedicated hardened worker (`execution-adapter-worker`) implements `repo.patch.apply` (validate base_commit, enforce scope.allowed_paths, `git apply --check` then apply, diff-artifact evidence, idempotency) and `build.test.run` (whitelist-only, no-shell, structured stdout/stderr + exit, evidence).

- Verified by 38 broker unit-test assertions against **real git** (apply in-scope; reject base-commit mismatch / out-of-scope path / free-form shell with no write; only-patch-files-touched; idempotent replay; whitelist enforcement; truncation) — broker suite 154/154. Agent tool surface (`apply_patch`, `run_build_test`) wired; config-validation test green; the change does not regress the existing governed stack (still `STACK_OK`).

- The adapter is a trusted execution node: OS-hardened (§13.2, no docker socket) but with workspace-write and egress (build/test restore). The agent still cannot reach it — only the broker dispatches to it. It is profile-gated in compose (`--profile adapters`) and defaults to a throwaway workspace, never the real repo.

- **End-to-end verified**: a podman stack test (`test-podman-execution-adapter-stack.js`) brings up broker + sealed agent + the adapter on the `adapters` profile, has the model drive `apply_patch` on a throwaway fixture, and asserts the file was actually patched **through** the governed chain (grant → policy → pool dispatch → `git apply`). This surfaced four integration bugs unit tests structurally could not (mock env wiring, git bind-mount `safe.directory`/`core.fileMode`, capability route must equal the tool name, and workspace-root scope normalization) — all fixed.

- Not yet: broker `--integration` HTTP coverage of the new routes (the stack test covers the real dispatch path instead).

- Agent container hardening (§13 + §13.1) — **done and verified 2026-06-13**:

- **Egress sealed**: the agent sits on an `internal: true` compose network shared only with the broker — no route to host/internet (verified: a container on that network cannot reach `api.openai.com`; one on a bridge can). The commercial API still works because the broker, not the agent, makes the provider call.

- **OS sandbox**: read-only rootfs (+ tmpfs `/tmp`), `cap_drop: ALL`, `no-new-privileges`, `pids_limit`, non-root uid 10001 (verified: rootfs write blocked, `CapEff=0`, `/tmp` writable, agent still completes the governed run). Seccomp is the runtime default profile (no custom profile yet).

- Browser runtime: action-level gating runs, but authenticated browser automation does not.

- Monitoring is health/metrics only; the control-plane console remains design-only (operator surface is still `line-admin.html`).

- README/runbook now cover the agent container path, but broader operator docs lag the code.

### Dishonest to claim

Not: a custom seccomp profile (the runtime default applies), full named-operator
account management for dual approvals (the current local-admin approver id is
session-derived), a fully production-hardened operator console, or
distributed/all-capability LINE send quotas. Broker-level dual approval for
Critical actions now persists `ApprovalRequest.required_approval_count` plus
per-approver `approval_decisions` and requires two distinct approver ids. Worker-local rate
limiting exists for `line.message.send` and `line.audio.send`; `line.notification.send`
and distributed quota coordination are still open. Container confinement (egress + OS sandbox),
the execution adapters (e2e-verified — a model drove `apply_patch` through the
governed chain), and the §18.2 approval layer (decision + lifecycle + two tiers +
both web surfaces, see §10) are all done and verified this cycle.

### Dishonest to deny

The controlled autonomous agent — the hardest and most central piece — went from
"never powered on" to "runs end-to-end on real commercial and open models under
broker governance" in this cycle.

## 8. Recommended Near-Term Priorities

1. ~~Container security hardening (§13): read-only rootfs, cap-drop=ALL, no-new-privileges, tmpfs.~~ **Done 2026-06-13** — applied to the agent in all three stacks, enforcement verified (`CapEff=0`, rootfs write blocked). Custom seccomp profile still pending (runtime default in effect).

2. ~~Network isolation (§13.1): seal agent egress to an internal-only network.~~ **Done 2026-06-13** — agent on `internal: true` `agent-net`, egress-denial verified; broker remains the only path to model providers.

3. ~~Execution adapters (§18.1 MVP): repo-adapter, build-test-adapter.~~ **Implemented + e2e-verified 2026-06-13** — `execution-adapter-worker` (`repo.patch.apply` + `build.test.run`), 38 real-git unit assertions + a full podman stack test where a model drives `apply_patch` through the governed chain and the file is actually patched. Remaining: broker `--integration` HTTP coverage of the new routes.

4. ~~Approval service + risk tiering (§18.2).~~ **Implemented + verified 2026-06-13** — see §10.

5. ~~Control-plane console (approval surface).~~ **Partially built 2026-06-13** — `line-admin.html` gained an approval queue tab; the broader operator console is still design-only.

6. Custom seccomp profile for the agent; broker `--integration` coverage of adapter + approval routes; distributed/all-capability LINE send quotas.

## 10. Governance approval system (§18.2) — implemented + verified 2026-06-13

The risk-tiering + approval layer, built test-first this cycle on a written
definition ([RiskClassificationAndApproval-2026-06-13.md](../designs/RiskClassificationAndApproval-2026-06-13.md),
[ApprovalWebInterface-2026-06-13.md](../designs/ApprovalWebInterface-2026-06-13.md)):

- **Decision (PolicyEngine)**: High/Critical and scope-escape no longer hard-denied — they return `RequireApproval`; `approval_policy` drives Low/Medium (`auto`, `auto_if_task_scope_match`, `require_approval`, `deny`). The primary risk discriminator is **scope ownership**: a user acting within their own private folder is auto; escaping it escalates.

- **Lifecycle (BrokerService)**: `RequireApproval` holds the request as `PendingApproval` + creates an `ApprovalRequest`; an admin/user approve records a decision and dispatches the held request once the required threshold is met, reject denies it — all audited. Quota + dispatch shared with the normal Allow path.

- **Two tiers + thresholds**: `User` (the owning user approves, in their own interface, limited to their scope) vs `Admin` (global, back-office). Broker enforces the authorization (a non-admin can only decide a User-tier approval they own). High / `require_approval` remains one approval; Critical / `require_dual_approval` requires two distinct approver ids through `ApprovalRequest.required_approval_count` and persisted per-approver `approval_decisions`.

- **Web surfaces**: admin approval tab in `line-admin.html` (localhost-only) + a user page (`user-approvals.html`) authenticated by a short-lived signed link sent over LINE. Both render the request **content** — a `repo.patch.apply` shows its unified diff — so the approver decides on substance, not a one-line string. The link is auto-sent on User-tier creation via `IApprovalNotifier` → `QueueLineNotification`.

- **Verified**: PolicyEngine 13 tests, approval lifecycle 25, link/notifier 13 — broker suite **192/192**, xUnit **343/343**, solution builds clean; a live broker smoke confirmed the endpoints serve and enforce auth (bad token → 401, admin without login → 401).

- **Not done**: named operator account management for local-admin dual approvals (current approver ids are admin-session based), distributed/all-capability LINE send quotas, a full browser+LINE manual e2e, and the broader control-plane console.

## 9. Bottom Line

The platform converged three developers back onto a live main, and its central
controlled-agent core now genuinely runs — including against ChatGPT. It is past
"serious POC" on the piece that matters most. It is not a fully production-hardened
controlled autonomous system yet: custom seccomp, broader operator-console
coverage, distributed/all-capability LINE send quotas, and deeper HTTP integration coverage are still
ahead.

## 10. Addendum 2026-06-16 — Component-library consolidation + site-replica e2e

The site-crawler generator's component vocabulary was a corpus-sampled "canned" schema
(`HeroSection`, `NewsGrid`, …) masquerading as a designed library. It is now anchored to the
canonical JS `ui_components` library (B):

- **Stage 1** — every generator type declares a `b_component` binding into B's closed set
 (`BComponentRegistry`); `ComponentLibraryLoader` fail-closes any manifest with a missing or
 out-of-set binding; dead `HeroSection` dropped; `TemplateMatcher`'s arbitrary `.First()` fallback
 removed (neutral container + recorded gap, never fabricated).

- **Stage 2** — the static package is verifiably B-anchored: `components/manifest.json` carries
 `b_component`, `components/b-binding.json` is the flat `type→b_component` index, README declares B
 canonical. Embedded deterministic renderers are kept as B's static-export projection (a **named
 divergence**, rationale in `docs/designs/ComponentLibraryConsolidation-2026-06-15.md` §9) — forcing
 B's live-FSM ESM components through byte-stable static export would regress Demo #3.

- **Stage 3** — determinism-clean instance IDs (`utils/uid.js` replaces `Math.random()`/`Date.now()`
 in Notification/Tooltip/WebTextEditor/BatchUploader); viz/editor/map smoke tests added.

A new broker-free `reconstruct` CLI on `site-crawler-worker` invokes the production
`SiteReconstructPackageHandler` locally (see the worker README). An end-to-end run against National
Taipei University of Technology (`www.ntut.edu.tw`) crawled 6 pages, produced **0 component gaps**,
**byte-identical archives** across runs, and surfaced a real bug unit tests had masked:
`TemplateCompiler.CloneManifest` dropped `b_component` on the live path, leaving `b-binding.json`
empty (fixed + regression test through the real Extract→Match→Compile path).

**Verified**: solution 0/0; xUnit **382**, broker **192**, Vitest **168** all green. Commits
`00f6615`, `f627769`, `81a75b6`, `e328042`, `151c307` (Stage 0 in `0b6d096`; ChainedInput `fcb3c26`).
