# PlanEngine ApprovalGate Consumption Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `PlanEngine` actually consume `EdgeType.ApprovalGate` so plan traversal pauses at gated edges, files a standard `ApprovalRequest`, and resumes or cancels on the human decision — turning the model layer's「需人工審批才放行」from an enum comment into runtime behavior.

**Why now:** the data layer is already complete — `PlanEdge` persists `EdgeType.ApprovalGate = 2` (`packages/csharp/broker-core/Models/PlanEdge.cs`, `Enums.cs`), `ApprovalRequest`/`ApprovalDecision` models live in broker-core, and §18.2-C2 shipped user-facing approval surfaces (LINE signed link + `user-approvals.html` + admin tab). But `PlanEngine.cs` consumes only `EdgeType.DataFlow` (single `FindAll`); an ApprovalGate edge today is silently treated as a plain dependency. This is the plan-layer keystone of the GATE pillar: it gates what the *high-level model proposed*, exactly the case the Critical dual-approval decision record identified as the real need.

**Architecture (proposed):**

1. **Gate semantics.** A node is *runnable* when (a) all upstream nodes completed (existing readiness) AND (b) every incoming `ApprovalGate` edge has a granted approval. Readiness check stays pull-based inside the engine's scheduling loop — no new scheduler.
2. **Pause, don't poll-block.** When a node is upstream-ready but gate-blocked: mark the node `WaitingApproval` (new node-state enum member; int-stored so migration-free), keep the plan `Running`, emit `PLAN_GATE_PENDING` (audit + observation, on the plan's `broker_trace_id`), and return from the scheduling pass. The plan simply stops making progress on that branch; other branches continue.
3. **One ApprovalRequest per gate edge, idempotent.** Correlation key = `edge_id`. Payload carries `plan_id`, from/to node titles, and the requesting principal. Re-entering the scheduler never duplicates a pending request.
4. **Decision routing reuses §18.2-C2 wholesale.** No new UI. The existing approval surfaces decide the `ApprovalRequest`; a small decision hook (or the next scheduling pass) re-evaluates the plan: APPROVED → node becomes runnable, `PLAN_GATE_APPROVED` audited; DENIED → node → failure path via existing `CancelDownstreamNodes(...)`, `PLAN_GATE_DENIED` audited. **Fail-closed defaults:** decision timeout (config, e.g. 24h) counts as DENY; unknown/malformed gate state never falls through to execution.
5. **Edge `Condition` JSON (already persisted, currently unused) carries per-gate options** — `{"approvers": 1, "timeout_minutes": 1440}`. Only these two keys in this pass; general conditional-edge expressions stay Phase 5 / out of scope.
6. **Dependency direction stays clean.** broker-core `PlanEngine` talks to a new small `IPlanApprovalService` (broker-core interface, CRUD over `ApprovalRequest` rows + decision lookup); the broker host implements/registers it and bridges to the existing §18.2-C2 surfaces (`ApprovalLinkService`, `LineApprovalNotifier`). broker-core gains no dependency on broker.

**Out of scope:** general `Condition` expression evaluation, sibling-parallel execution, checkpoint rollback (all Phase 5 topics tracked elsewhere); multi-approver quorum beyond reusing the existing dual-approval machinery.

**Tech Stack:** C# .NET 8, BaseOrm, SQLite, xUnit/FluentAssertions.

---

## File Structure

- Modify `packages/csharp/broker-core/Models/Enums.cs`: add `WaitingApproval` node-state member (int-stored; no migration).
- Create `packages/csharp/broker-core/Services/IPlanApprovalService.cs`: `EnsurePendingForEdge(edge, plan, principal)` / `GetDecision(edgeId)` (granted / denied / pending / expired).
- Create `packages/csharp/broker-core/Services/PlanApprovalService.cs`: BaseOrm implementation over `ApprovalRequest` + `ApprovalDecision` (idempotent by `edge_id`, timeout-as-deny).
- Modify `packages/csharp/broker-core/Services/PlanEngine.cs`: gate check in the readiness pass; `PLAN_GATE_*` audit/observation emission; deny → existing `CancelDownstreamNodes`.
- Modify broker host DI + approval decision endpoint(s): registering the service; nudging plan re-evaluation on decision (or rely on the engine's next pass); surface pending gates in plan status payload.
- Create `packages/csharp/tests/unit/Core/PlanEngineApprovalGateTests.cs`: deterministic tests via `TestDb`.
- Update `docs/manuals/current-technical-manual.zh-TW.md`: ApprovalGate edge behavior + fail-closed defaults.

## Task 1: Gate Semantics in PlanEngine (TDD)

- [ ] Add failing unit tests: gated node does not execute while approval pending; sibling ungated branch still completes; `PLAN_GATE_PENDING` emitted exactly once (idempotent re-entry).
- [ ] Run the filtered unit tests and confirm they fail.
- [ ] Add `WaitingApproval` node state; implement gate check in the readiness pass behind `IPlanApprovalService`.
- [ ] Run the filtered unit tests and confirm they pass.

## Task 2: Approve / Deny / Timeout Paths (TDD)

- [ ] Add failing unit tests: APPROVED resumes and completes the gated branch; DENIED cancels the node + downstream via `CancelDownstreamNodes` and fails the plan honestly; expired approval behaves as DENY (fail-closed).
- [ ] Run the filtered unit tests and confirm they fail.
- [ ] Implement `PlanApprovalService` decision mapping (granted / denied / pending / expired) + `Condition` JSON options (`approvers`, `timeout_minutes`).
- [ ] Run the filtered unit tests and confirm they pass.

## Task 3: Host Wiring and Surfaces

- [ ] Register `IPlanApprovalService` in broker DI; bridge approval decision handling to plan re-evaluation.
- [ ] Expose pending gate info in plan status responses (id, edge, waiting-since) for the admin/user approval surfaces.
- [ ] Reuse §18.2-C2 notification path so a pending plan gate produces the same signed-link approval UX as other approvals.
- [ ] Run `dotnet build packages/csharp/ControlPlane.slnx` and the full unit suite.

## Task 4: Docs and Verification

- [ ] Update technical manual section on plan edges (gate semantics, fail-closed defaults, `Condition` options).
- [ ] Add an end-to-end verify block (offline deterministic): submit a 3-node plan with one gated edge → observe pause → approve → observe completion; deny variant asserts downstream cancellation.
- [ ] Clean test artifacts.
- [ ] Commit the completed change.

---

## Notes for the reviewer

- This proposal deliberately **adds no new approval UI and no new trust surface** — it routes the plan layer into the approval machinery the team already shipped and enforced (critical dual-approval, signed links). The delta is engine consumption + a thin service.
- Verified anchors as of `main_0707`: `PlanEdge.cs` (ApprovalGate comment + `Condition` column), `Enums.cs` `EdgeType.ApprovalGate = 2`, `PlanEngine.cs` consuming only `DataFlow`, `CancelDownstreamNodes` available for the deny path, `ApprovalRequest`/`ApprovalDecision` in broker-core, §18.2-C2 surfaces in broker.
- Happy to implement task-by-task as follow-up PRs, or leave this plan for whoever picks it up.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
