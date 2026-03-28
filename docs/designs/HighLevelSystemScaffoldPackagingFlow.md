# High-Level System Scaffold Packaging Flow

Date: 2026-03-29
Status: planning draft

## 1. Goal

Add a new high-level capability where the system can:

1. clarify requirements through high-level conversation,
2. converge on a structured system scaffold specification,
3. generate a complete project skeleton,
4. package the result,
5. deliver a downloadable artifact back to the user.

This is not the same as the current `doc_gen` or minimal `code_gen` path.

The current system can already:

- create production drafts,
- confirm them into `task / plan / handoff`,
- generate documents,
- generate a minimal website prototype,
- upload artifacts to Google Drive,
- notify LINE users with delivery links.

What it cannot yet do is produce a coherent multi-file application scaffold from a requirement interview and return that scaffold as a packaged deliverable.

## 2. Why This Is A Different Feature

This feature is materially more complex than the current `doc_gen` and current `code_gen`.

The difficulty is not only code generation. The real complexity is in:

- requirement elicitation,
- deciding when requirements are complete enough,
- turning conversation into a stable specification,
- generating multiple coordinated files,
- validating the scaffold,
- packaging the scaffold,
- preserving per-user isolation,
- delivering the result in a way users can actually consume.

If this is implemented carelessly, the system will regress into:

- vague chat pretending to be specification,
- brittle single-shot generation,
- unverifiable output,
- oversized prompts with no stable artifact model,
- delivery paths that produce files but do not produce usable downloads.

## 2.1 This Must Be Iterative, Not One-Shot

This feature must explicitly support iterative engineering behavior.

The target workflow is not:

- ask once,
- generate once,
- deliver once.

The real workflow must allow repeated cycles of:

- requirement analysis,
- design planning,
- implementation,
- testing,
- review,
- revision.

Without that, the system can only produce fragile first drafts. That is not enough for a useful system scaffold capability.

## 2.2 This Must Not Be A Silent Workflow

Every phase and every iteration must produce explicit user-visible progress feedback.

The system must not disappear into a long-running internal workflow and return only at the end.

Users need to know:

- which phase is currently running,
- what was completed,
- what is blocked,
- whether user input is required,
- whether the system is revising due to test or validation failures,
- whether packaging and delivery have started.

This is not optional UX polish. It is required for trust and controllability.

## 2.3 Memory Is A Core Constraint

This feature will fail if memory is treated as a generic chat transcript.

An iterative scaffold workflow accumulates:

- requirements,
- assumptions,
- rejected ideas,
- design revisions,
- test failures,
- packaging outcomes,
- delivery outcomes.

Those are not all the same kind of memory.

If they are mixed together carelessly, the system will:

- reintroduce discarded requirements,
- treat tentative ideas as confirmed design,
- forget which iteration actually passed testing,
- package stale outputs,
- or tell the user misleading progress.

So memory handling is not a secondary implementation detail. It is one of the main design constraints.

## 3. Architectural Fit

This feature should sit on top of the current architecture, not bypass it.

### 3.1 High-level model

The high-level model should handle:

- requirement interview,
- clarification questions,
- scope reduction,
- structured scaffold-spec proposal,
- user confirmation of the spec.

It should not directly generate arbitrary side effects outside broker governance.

### 3.2 Broker

The broker should remain the control point for:

- state transitions,
- spec persistence,
- execution intent promotion,
- managed workspace allocation,
- artifact packaging,
- delivery routing.

The broker should not “become the generator”. It should orchestrate the generation path and validate transitions.

### 3.3 Execution layer

The execution layer should consume:

- a structured scaffold spec,
- target paths,
- allowed generation scope,
- packaging instructions.

It should not consume raw conversation as its primary input.

## 4. Proposed Capability

Introduce a new production task type:

- `system_scaffold`

This is distinct from:

- `doc_gen`
- `code_gen`
- `code_modify`

It represents a bounded, scaffold-oriented generation workflow where the target output is a packaged project skeleton rather than a single HTML page or a single markdown file.

## 5. User-Level Flow

The intended user flow is:

1. User asks for a system or project scaffold.
2. High-level model enters requirement interview mode.
3. High-level model asks targeted questions until the scaffold specification is sufficiently complete.
4. Broker stores a structured scaffold draft state.
5. High-level model presents a concise spec summary.
6. User confirms.
7. Broker promotes the scaffold spec into executable intent.
8. Generator produces files into the user’s project workspace.
9. Broker runs packaging.
10. Broker delivers the package through configured delivery mode.
11. User receives a download link or package notice.

The user should not need to manually reconstruct the spec from memory.

## 6. Requirement Interview Model

This feature should not rely on “chat until it feels done”.

It needs a finite scaffold-spec schema and an interview gate.

### 6.1 Minimal scaffold spec

At minimum, the scaffold spec should capture:

- project name
- project type
- target platform
- language / stack
- frontend presence
- backend presence
- database presence
- auth requirement
- deployment target intent
- output artifact format

### 6.2 Optional scaffold spec

As the system matures, the spec can expand to include:

- routing style
- UI style or design system preference
- API shape
- persistence technology
- testing scaffold requirement
- CI/deployment files
- containerization preference
- Azure/IIS target preference

Default principle:

- prefer the project's custom component library,
- do not assume a generic third-party UI kit unless the user explicitly asks for one.

### 6.3 Completion rule

The interview should end only when:

- required scaffold fields are filled,
- unresolved ambiguity is below threshold,
- the user has reviewed the generated summary.

This must be explicit. Otherwise the system will oscillate between over-questioning and under-specified generation.

## 6.4 Requirement Analysis Output

The interview stage should not only fill fields. It should also produce a requirement-analysis artifact.

Suggested outputs:

- scope summary
- assumptions
- constraints
- missing decisions
- risks
- accepted tradeoffs

This becomes the basis for later design and implementation iterations.

## 6.5 Requirement Memory Rules

Requirement-related memory should be split at least into:

- `candidate_requirements`
- `confirmed_requirements`
- `rejected_requirements`
- `open_questions`
- `assumptions`

The system must not silently promote a candidate requirement into a confirmed one just because it appeared repeatedly in conversation.

Confirmation should happen through:

- explicit user confirmation,
- or an explicit summarized spec review step.

## 7. New State Model

This feature needs additional workflow states beyond current draft confirmation.

Suggested states:

- `ScaffoldInterview`
- `RequirementsAnalyzed`
- `DesignPlanned`
- `ScaffoldDraftReady`
- `ScaffoldConfirmed`
- `ImplementationInProgress`
- `ImplementationCompleted`
- `TestingInProgress`
- `TestingCompleted`
- `RevisionRequested`
- `ScaffoldGenerating`
- `ScaffoldGenerated`
- `ScaffoldPackaged`
- `ScaffoldDelivered`
- `ScaffoldFailed`

This should not be collapsed into the existing generic `production draft` shape without extra state, because the interview loop is different from a simple one-shot draft.

The important point is that generation is only one phase inside a larger iterative workflow.

Each state transition should also have a user-facing progress event.

State transitions should also define memory-commit behavior. Each transition should specify:

- which memory fields may be updated,
- which fields may only be appended,
- which fields become immutable once confirmed,
- which prior iteration memories are superseded rather than overwritten.

## 8. New Data Models

### 8.1 Scaffold spec document

New persisted document:

- `hlm.scaffold-spec.{channel}.{userId}`

Contents should include:

- current scaffold field values,
- missing required fields,
- completion status,
- last spec summary,
- associated project root,
- packaging preference.

This document should contain only the current normalized scaffold spec, not the full conversational history.

### 8.2 Design plan document

New persisted document:

- `hlm.scaffold-plan.{channel}.{userId}`

Contents should include:

- architecture summary
- module breakdown
- file/package layout
- generation strategy
- test strategy
- packaging target

This document should be versioned by iteration, because design plans can change across revisions.

### 8.3 Iteration state document

New persisted document:

- `hlm.scaffold-iteration.{channel}.{userId}`

Contents should include:

- current phase
- iteration count
- latest review result
- open issues
- last requested revision
- last successful checkpoint

This document should make it possible to distinguish:

- current iteration,
- last completed iteration,
- last test-passing iteration,
- last deliverable iteration.

### 8.4 Progress event document

New persisted document:

- `hlm.scaffold-progress.{channel}.{userId}`

Contents should include:

- current phase
- iteration index
- status summary
- short user-facing message
- whether response was already delivered to the user
- whether user action is required
- timestamp

This document is for progress communication, not long-term design truth.

### 8.5 Scaffold artifact document

New artifact record type or extended artifact metadata should include:

- artifact type = `system_scaffold_package`
- package path
- package filename
- package format
- project root
- generation summary
- delivery mode

Artifacts should be tied to the iteration that produced them. Otherwise later revisions may accidentally deliver obsolete outputs.

### 8.6 Scaffold packaging evidence

Packaging should generate explicit evidence, for example:

- package manifest
- zipped file list
- packaging timestamp
- source project root

This matters because packaging is a real step, not a cosmetic add-on.

## 9. Generation Strategy

There are two realistic implementation phases.

### 9.1 Phase A: Deterministic scaffold templates

Use known scaffold recipes and template emitters.

Examples:

- static SPA scaffold
- frontend + API scaffold
- documentation-heavy prototype scaffold

Advantages:

- predictable output,
- easier validation,
- lower hallucination risk,
- easier packaging.

Disadvantages:

- narrower range,
- less expressive.

### 9.2 Phase B: Template + model-assisted synthesis

Use:

- fixed scaffold template,
- plus model-generated files where safe,
- plus validation and rewrite steps.

Examples:

- README generation,
- route/page placeholders,
- seed components,
- config files with user-specific naming.

This should come after deterministic scaffold packaging works.

## 9.3 Iterative Generation Strategy

Generation should be organized into explicit passes rather than one giant output.

Suggested pass structure:

1. requirement-analysis pass
2. design-plan pass
3. implementation pass
4. validation/test pass
5. revision pass
6. packaging pass

Each pass should consume structured outputs from the previous pass, not the full raw conversation by default.

This matters because iterative systems degrade quickly if every revision restarts from unbounded raw chat context.

## 9.4 Iteration Memory Projection

Each major pass should write a reduced, phase-specific memory projection.

Suggested projections:

- requirement-analysis projection
- design-plan projection
- implementation projection
- test-result projection
- delivery projection

These projections should be the canonical inputs for later phases.

Raw chat history may still be retained for audit, but should not be the primary working memory for downstream steps.

## 10. Packaging Strategy

Packaging should be a first-class step.

Suggested initial package format:

- `.zip`

Package content should include:

- generated project files,
- top-level README,
- package manifest,
- generated summary.

Initial package placement:

- under the user’s managed workspace,
- probably in `documents` or a dedicated `packages` folder under the project root.

Suggested canonical path:

- `{projectRoot}/dist/{packageFile}`
or
- `{userRoot}/documents/{packageFile}`

This must be defined clearly before implementation. Avoid hidden temporary outputs with no formal artifact path.

## 11. Delivery Strategy

Current viable delivery path:

- Google Drive upload and share link

Current missing path:

- broker-owned front-end download API

That missing path should be explicitly tracked as a front-end feature, but not block this feature’s first phase.

### 11.1 Delivery modes

The feature should support selectable delivery modes:

- `google_drive_shared`
- `google_drive_user_delegated`
- `local_only`
- `broker_download_api` (planned, not implemented yet)

### 11.2 Important front-end gap

The project does not currently have a formal end-user front-end download surface for artifacts.

That means:

- delivery is currently operational through Drive,
- but broker-native download is still a product gap.

This is not a blocker for scaffold generation itself, but it is a blocker for calling the broker a complete direct-delivery system.

## 12. Validation

This feature must not stop at “files were written”.

Minimum validation after generation:

- required files exist,
- package can be created,
- package path is recorded,
- delivery path returns a usable link or explicit local-only result.

For selected scaffold types, stronger validation should exist:

- static site entry file exists,
- API project file exists,
- README exists,
- package opens as valid zip.

Later phases may add:

- `dotnet build`
- `npm install`
- smoke tests

But the first phase should not pretend build validation exists if it does not.

## 12.1 Testing As A First-Class Iteration Phase

Testing is not a postscript. It must be represented as an explicit workflow phase.

At minimum, the system should track:

- whether generation completed,
- whether packaging completed,
- whether structural checks passed,
- whether build/test was attempted,
- whether revision is required before delivery.

The system should be allowed to stop before delivery if testing indicates the scaffold is inconsistent.

## 12.3 Test Memory Rules

Testing must produce structured memory, not just free-form logs.

At minimum, store:

- iteration number
- test suite attempted
- pass/fail outcome
- blocking failures
- unresolved warnings
- tested artifact reference

Only a test-passing iteration should be eligible for packaging and delivery unless the user explicitly accepts a weaker outcome.

## 12.2 User-Facing Progress Contract

Every major phase should emit a concise progress response.

Minimum phase-level responses:

1. requirement interview started
2. requirement analysis completed
3. design plan completed
4. implementation iteration started
5. implementation iteration completed
6. testing started
7. testing completed
8. revision requested
9. packaging started
10. packaging completed
11. delivery started
12. delivery completed

Each response should be short and operational, for example:

- current phase
- iteration number
- current outcome
- next step

If user input is needed, that prompt should be its own explicit follow-up, not buried inside a long paragraph.

This matches the existing LINE direction where actionable follow-up commands should be separated from the main message.

## 13. Risks

### 13.1 Requirement drift

If the interview state is weak, the spec will mutate unpredictably and generation will not be reproducible.

### 13.2 Prompt bloat

If the entire conversation is passed into generation, the scaffold will become unstable and difficult to validate.

### 13.3 Overclaiming

A scaffold generator is not a full system generator.

The product language must avoid pretending the system creates production-ready applications unless actual validation exists.

### 13.4 Packaging without artifact discipline

If package paths and records are informal, users will get “a zip somewhere” rather than a governed artifact.

## 14. Recommended Implementation Order

### Step 1

Add `system_scaffold` task type plus:

- scaffold-spec document
- design-plan document
- iteration-state document
- progress-event document
- memory-commit rules for each phase

### Step 2

Add high-level interview mode and requirement-analysis output.

### Step 3

Add design-planning pass for one narrow target.

### Step 4

Implement deterministic scaffold generation for one narrow target:

- `single-page app scaffold`
or
- `frontend + minimal API scaffold`

### Step 5

Add structural validation and basic testing stage.

### Step 6

Add packaging service and package artifact record.

### Step 7

Connect packaging result to existing Drive delivery flow.

### Step 8

Add admin visibility:

- scaffold spec
- design plan
- iteration state
- progress events
- iteration memory projections
- generation result
- package artifact

### Step 9

Only then expand into richer multi-project or multi-service generation.

## 15. Honest Assessment

This feature is feasible, but it is not “just a bigger code_gen”.

The difficult part is not asking one more model call to write files.
The difficult part is making the result:

- analysis-driven,
- plan-driven,
- spec-driven,
- inspectable,
- revisable,
- testable,
- packageable,
- deliverable,
- and stable enough that users can rely on it.

If implemented properly, this becomes one of the most valuable features in the whole system.

If implemented lazily, it becomes a demo trap:

- impressive in screenshots,
- unreliable in real use,
- and expensive to maintain.

## 16. Current Recommendation

Do not jump straight to “generate any full system”.

The correct next target is:

- requirement interview
- requirement analysis artifact
- structured scaffold spec
- design plan
- one deterministic scaffold family
- basic validation/test phase
- zip packaging
- Google Drive delivery

That is the smallest version that is still architecturally honest.
