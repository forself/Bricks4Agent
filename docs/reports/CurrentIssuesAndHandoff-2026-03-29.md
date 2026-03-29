# Bricks4Agent Current Issues And Handoff

Date: 2026-03-29
Status: active handoff report for current local branch state

## 1. Purpose

This file records:

- current working progress,
- open issues that still block reliable user-facing behavior,
- local-only changes that have not yet been pushed,
- immediate handoff steps for the next engineer or next iteration.

It is not a broad architecture overview. For that, see:

- [CurrentArchitectureAndProgress-2026-03-26.md](/d:/Bricks4Agent/docs/reports/CurrentArchitectureAndProgress-2026-03-26.md)

## 2. Current Local Git State

As of this handoff:

- branch: `main`
- tracking: `origin/main`
- local branch is ahead by `3` commits

Local-only commits not yet pushed:

1. `75cf1be` `feat: add iterative system scaffold packaging flow`
2. `4d7447a` `feat: default system scaffolds to custom component libraries`
3. `38e1edb` `feat: support short line confirmations with y and n`

Current working tree:

- no tracked file modifications
- one unrelated untracked file remains outside this handoff scope:
  - `tw-11134207-7r98z-lrvkacg3kj4i83.webp`

## 3. What Is Already Working

### 3.1 LINE sidecar baseline

The current canonical local live path is:

`LINE webhook -> ngrok -> line-worker -> broker /api/v1/high-level/line/process`

Canonical local ports:

- broker: `127.0.0.1:5361`
- line-worker webhook: `127.0.0.1:5357`

Sidecar broker state is now persisted to:

- `D:\Bricks4Agent\.run\line-sidecar\data\broker.db`

That persistence change matters because restart no longer wipes:

- local admin password state
- shared context documents
- broker profile memory
- future OAuth credentials stored in the sidecar DB

### 3.2 High-level production flow

The following are already operational:

- production draft creation
- project-name capture
- confirm / cancel flow
- short confirmations:
  - `confirm` or `y`
  - `cancel` or `n`

### 3.3 `code_gen` path

`code_gen` no longer stops at `task / plan / handoff` only.

After confirmation, the current implementation now generates a minimal website prototype in the managed project directory and reports:

- project root
- entry file

This is not a full autonomous website factory, but it is no longer a fake execution path.

### 3.4 `system_scaffold` path

A first tranche of iterative scaffold generation has been added.

Current behavior:

- high-level conversation can create a `system_scaffold` draft
- follow-up requirement messages can update the draft
- confirm triggers:
  - requirement summary
  - design-plan document
  - scaffold files
  - basic packaging into zip
  - delivery attempt through the existing artifact delivery layer

This tranche is intentionally limited. It is scaffold-oriented, not a full autonomous multi-iteration engineering factory yet.

### 3.5 UI component policy default

By default, `system_scaffold` now records:

- `ui_components: custom_component_library`

This default is carried through:

- draft output
- design-plan
- iteration-state
- generated project README

That matches the current project principle: prefer the in-repo custom component library unless the user explicitly asks for a different UI kit.

## 4. What Is Not Working Reliably Yet

### 4.1 Google Drive upload in the current live sidecar

The Google Drive upload code is not globally broken.

The current live problem is narrower:

- the running sidecar DB currently has no delegated Google Drive credential row
- therefore `shared_delegated` upload falls back to local-only artifact generation
- user-facing result becomes:
  - file generated locally
  - cloud upload incomplete
  - no cloud link returned

Confirmed current live state:

- local admin status works
- local admin login works
- Google Drive delivery configuration is present
- OAuth client JSON path is present
- default folder id is present
- but `google_drive_delegated_credentials` is currently empty in the live sidecar DB

So the active blocker is:

- missing shared delegated OAuth credential in the current persistent broker DB

It is not:

- a missing code path for upload
- a compile failure
- a missing default folder configuration

### 4.2 Frontend download path is still missing

There is still no broker-owned public download route for artifacts.

The system currently depends on cloud delivery for user-friendly downloads.

That means:

- if Google Drive upload is unavailable, user delivery becomes degraded
- local file generation may still succeed, but user download remains incomplete

This is already documented as a missing frontend capability and should remain tracked as such.

### 4.3 LINE follow-up UX still needs more polish

Recent work added:

- short commands `y` and `n`
- separate follow-up command messages

But this area still needs more tightening:

- some older guide/help text may still mention only `confirm / cancel`
- long or mixed guidance can still appear in some flows if a prompt path was not fully normalized
- this should be treated as UX debt, not as a completed area

### 4.4 `system_scaffold` is still first-tranche only

The current scaffold flow does not yet do all of the following:

- true multi-round implementation revision cycles
- full automated test-fix-test loops
- broker-owned download API
- full live validation through LINE ingress for every scaffold branch

It is currently a real scaffold generator, but not yet the final end-state described in the design document.

## 5. Most Important Immediate Handoff Items

### 5.1 Restore shared Google Drive owner credential

This is the top operational blocker for user-visible cloud delivery.

Required action:

1. Use local admin to generate a new Google Drive OAuth authorization URL for the shared owner binding.
2. Complete the OAuth flow in browser.
3. Confirm that a delegated credential row now exists in the persistent sidecar DB.
4. Generate a new artifact and verify that:
   - upload succeeds,
   - a share link is produced,
   - LINE receives the link.

Why this matters:

- without this, document and scaffold packaging can succeed locally but still fail the actual user delivery expectation

### 5.2 Push the three local commits

The local branch is ahead by three commits. Those changes should not remain only on one machine indefinitely.

Push candidates:

1. `75cf1be`
2. `4d7447a`
3. `38e1edb`

Do not forget:

- Windows PowerShell should not use `&&` for chained git commands
- use `;` or `if ($LASTEXITCODE -eq 0) { ... }`

### 5.3 Add a broker-owned artifact download API

This is not optional long-term polish. It is a structural missing piece.

Current delivery reality:

- local artifact generation exists
- Google Drive delivery exists when credential state is healthy
- direct broker-hosted download still does not exist

Without that, the system has no first-party download path.

### 5.4 Continue `system_scaffold` from scaffold to iteration engine

The design already states that the feature must support:

- requirement analysis
- design planning
- implementation
- testing
- revision
- packaging and delivery

The first tranche only covers the early scaffold version of that lifecycle.

The next engineering step is to add:

- iteration records
- revision cycles
- explicit progress documents per phase
- clearer test-result gating before final package delivery

## 6. Verification Baseline For The Next Engineer

Before making more changes, re-run at least:

```powershell
dotnet build packages/csharp/broker/Broker.csproj -c Release --disable-build-servers -nodeReuse:false
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

For sidecar runtime checks:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify
```

For local admin status:

- `http://127.0.0.1:5361/api/v1/local-admin/status`

For local admin console:

- `http://127.0.0.1:5361/line-admin.html`

## 7. Operational Notes

### 7.1 Local admin password

Current working live password has previously been set to:

- `AdminPass#2026`

This should not be treated as permanent. It is only noted here because some operational steps still depend on local admin access.

### 7.2 Google Drive mode

Current intended mode is:

- `shared_delegated`

Meaning:

- all LINE users can deliver to the same Google Drive owner account
- Google does not need to know which LINE user originated the artifact
- user-level separation remains broker-side, not Drive-side

### 7.3 Documentation references

Relevant current documents:

- [CurrentArchitectureAndProgress-2026-03-26.md](/d:/Bricks4Agent/docs/reports/CurrentArchitectureAndProgress-2026-03-26.md)
- [HighLevelSystemScaffoldPackagingFlow.md](/d:/Bricks4Agent/docs/designs/HighLevelSystemScaffoldPackagingFlow.md)
- [line-sidecar-runbook.zh-TW.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)
- [line-sidecar-runbook.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.md)

## 8. Recommended Next Order

Recommended next order of work:

1. restore shared Google Drive delegated credential in the live persistent sidecar DB
2. validate end-to-end artifact upload and LINE delivery
3. push the three local commits
4. add a broker-owned download API
5. extend `system_scaffold` into a real iteration engine with revision and test cycles

## 9. Honest Summary

The system is in a meaningful but uneven state.

What is true:

- the control-plane direction is real
- the sidecar path is real
- `code_gen` now performs actual artifact generation
- `system_scaffold` is no longer just a plan

What is also true:

- delivery still depends too much on cloud state being healthy
- first-party download is still missing
- scaffold generation is ahead of artifact delivery reliability
- some UX and progress/reporting paths are still not as clean as they should be

This is not stalled work. It is active work with a few clear operational bottlenecks.
