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
the first time, against a mock backend, a real open model (qwen3.6), and a real
commercial API (ChatGPT). The macro position holds and is firmer:

**a broker-centered governed AI operations platform whose controlled-agent core now actually runs.**

## 2. Three-Way Convergence (PR #3, merged)

`origin/main` was 22 days stale and behind three developers. They converged:

| Source | Role | Contribution |
|--------|------|--------------|
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
|---------|-------|--------|
| mock | mock-ollama/openai | `STACK_OK` + `[governed] read_file` |
| local ollama | qwen3.6 (23GB, native tool calling) | `OLLAMA_STACK_OK` |
| **commercial API** | **real ChatGPT gpt-5.4-mini** (api.openai.com) | `STACK_OK` via governed chain |

This satisfies the verifiable parts of §21: humans and AI go through the same
authorization path, and the container reaches tools only via broker
adjudication. Operations: [agent-container-runbook.md](../manuals/agent-container-runbook.md).
Activation detail: [AgentContainerActivation-2026-06-13.md](AgentContainerActivation-2026-06-13.md).

## 4. Commercial Model API (ChatGPT)

The broker `LlmProxy` speaks ollama, OpenAI chat (`v1/chat/completions`), and
OpenAI responses (`v1/responses`) with a Bearer key. The agent container itself
holds **no** provider key or base URL — it talks only to the broker, and the
**broker's** `LlmProxy` reaches the commercial API. Pointing that proxy at a real
model is a broker-side setting (`OPENAI_BASE_URL=https://api.openai.com` plus
key/format/model) — verified against real gpt-5.4-mini. (The LINE high-level path
already used real OpenAI; this extends the same capability to the broker's
agent-facing `LlmProxy`.) So the broker is already the inference gateway for the
agent path; what is missing is *enforcing* that the container cannot bypass it.

## 5. Integration Bugs Surfaced By First Activation

Powering on the container chain for the first time exposed a chain of
integration bugs that unit tests and mock stacks could not catch — each fixed:

1. broker container build `NETSDK1152` — site-crawler-worker appsettings leaking into broker publish
2. broker fails to boot with `FunctionPool=false` — monitoring HealthScoreService unconditionally depends on IWorkerRegistry
3. session register 500 `No data exists` — Microsoft.Data.Sqlite `IsDBNull` Linux edge case in BaseOrm
4. `GET /api/v1/health` `Body was inferred` — health endpoints registered without their FunctionPool services
5. real ChatGPT returns 200 but agent gets empty output — responses parser only read the top-level `output_text` SDK convenience field

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
- The controlled agent container is an MVP skeleton: **the approval service and execution adapters are not done** (the agent can read under governance but cannot yet do real work — repo edits, build/test). The container *itself* is now hardened: §13.1 network egress isolation and §13 OS sandboxing are both done (see below). The remaining gap is what the agent is *allowed to do* and *how risky actions are gated*, not how the container is confined.
- Agent container hardening (§13 + §13.1) — **done and verified 2026-06-13**:
  - **Egress sealed**: the agent sits on an `internal: true` compose network shared only with the broker — no route to host/internet (verified: a container on that network cannot reach `api.openai.com`; one on a bridge can). The commercial API still works because the broker, not the agent, makes the provider call.
  - **OS sandbox**: read-only rootfs (+ tmpfs `/tmp`), `cap_drop: ALL`, `no-new-privileges`, `pids_limit`, non-root uid 10001 (verified: rootfs write blocked, `CapEff=0`, `/tmp` writable, agent still completes the governed run). Seccomp is the runtime default profile (no custom profile yet).
- Browser runtime: action-level gating runs, but authenticated browser automation does not.
- Monitoring is health/metrics only; the control-plane console remains design-only (operator surface is still `line-admin.html`).
- README/runbook now cover the agent container path, but broader operator docs lag the code.

### Dishonest to claim
Not: a custom seccomp profile (the runtime default applies, not a tailored one),
approval-gated high-risk actions, execution adapters, or a complete §18 MVP. The
container *confinement* (egress isolation + OS sandbox) is done; what the agent
is *permitted to do*, and the gating of risky actions, is not.

### Dishonest to deny
The controlled autonomous agent — the hardest and most central piece — went from
"never powered on" to "runs end-to-end on real commercial and open models under
broker governance" in this cycle.

## 8. Recommended Near-Term Priorities

1. ~~Container security hardening (§13): read-only rootfs, cap-drop=ALL, no-new-privileges, tmpfs.~~ **Done 2026-06-13** — applied to the agent in all three stacks, enforcement verified (`CapEff=0`, rootfs write blocked). Custom seccomp profile still pending (runtime default in effect).
2. ~~Network isolation (§13.1): seal agent egress to an internal-only network.~~ **Done 2026-06-13** — agent on `internal: true` `agent-net`, egress-denial verified; broker remains the only path to model providers.
3. **Execution adapters (§18.1 MVP): repo-adapter, build-test-adapter.** ← next: makes the agent able to do real work, not just read.
4. Approval service + risk tiering (§18.2).
5. Control-plane console (design exists, not built).
6. Custom seccomp profile for the agent (tighten beyond the runtime default).

## 9. Bottom Line

The platform converged three developers back onto a live main, and its central
controlled-agent core now genuinely runs — including against ChatGPT. It is past
"serious POC" on the piece that matters most. It is not a hardened controlled
autonomous system yet: the security, isolation, and approval layers that make
"controlled" mean something under attack are still ahead.
