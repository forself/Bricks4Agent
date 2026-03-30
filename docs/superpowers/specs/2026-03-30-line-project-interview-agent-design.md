# LINE Project Interview Agent Design

Date: 2026-03-30
Status: design draft for review

## Design Rationale

### Why this is a protocolized interview system

The target problem is not ordinary chat assistance. It is a requirement-elicitation protocol that must converge toward executable design artifacts under uncertainty. A protocolized design is necessary because open-ended conversation maximizes semantic flexibility but also maximizes ambiguity, drift, and accidental state mutation.

### Why the workflow uses a state machine

The interview has explicit gates, privileged commands, and terminal states. A state machine is the correct control abstraction because it constrains command admissibility and phase progression regardless of model variability. In theoretical terms, it separates stochastic interpretation from deterministic control flow.

### Why the design uses confirmed restatements and assertions

Free-form conversational memory is lossy because every summary compresses meaning and compounds drift across turns. The design therefore treats canonical memory as a set of confirmed restatements and assertions instead of a rolling summary. This is a verification-first model of truth: unconfirmed interpretation remains provisional, while explicit user confirmation is required before promotion into canonical state.

### Why the design uses a per-version DAG

The review artifacts are compiled outputs, not merely chat transcripts. A per-version DAG captures provenance: which confirmed assertions led to which canonical JSON, which in turn led to which PDF and delivery artifacts. Revision creates a new graph rather than mutating the prior one, preserving lineage and avoiding hidden cycles.

### Why the design is template-first

The downstream implementation surface is already bounded by an existing component library, page generator, and runtime. Template-first narrowing reduces the hypothesis space early and changes the problem from unconstrained generation into constrained configuration over a known capability surface. This improves consistency, reviewability, and downstream implementation reliability.

### Why the program is JSON-defined

The design favors a document-first `JSON-defined program` because the interview output should be structural truth, not ad hoc handwritten code assembled directly from chat. This follows model-driven engineering principles: the interview creates a structured intermediate representation, and the existing runtime executes it.

### Why the spec should exist in English and Traditional Chinese

This feature requires both precise implementation handoff and deep architectural discussion. Maintaining English and Traditional Chinese spec artifacts improves review accessibility while preserving exact technical meaning. The English spec remains the canonical source for exact naming when conflicts arise, but both versions must stay aligned.

## Language Versions

- English source spec: `docs/superpowers/specs/2026-03-30-line-project-interview-agent-design.md`
- Traditional Chinese companion: `docs/superpowers/specs/2026-03-30-line-project-interview-agent-design.zh-TW.md`

The English spec is the canonical source for exact type and file naming. The Traditional Chinese companion must preserve the same architecture, constraints, and review checkpoints.

## Goal

Add a dedicated LINE task-mode agent that starts from `/proj`, conducts a task-scoped requirements interview for a software project, detects missing or conflicting requirements, and produces a downloadable workflow design document for user review.

This is intentionally the first stage only.

The agent must not yet auto-implement the project.
Its job is to:

- interview the user,
- normalize requirements into structured state,
- produce architecture and implementation planning output,
- generate a versioned workflow design document,
- generate a matching structured JSON artifact,
- let the user review and approve the design checkpoint through LINE.

## Why This Is A Separate Task Type

This capability should not be folded into normal LINE chat or the existing `system_scaffold` task type.

It is materially different because it requires:

- explicit task entry,
- high-grade requirement elicitation,
- task-scoped memory only,
- deterministic requirement completeness checks,
- versioned design-document generation,
- a hard approval gate before any implementation workflow begins.

The new task type should therefore be independent:

- `project_interview`

This keeps it separate from:

- casual LINE conversation,
- `doc_gen`,
- minimal `code_gen`,
- current `system_scaffold` packaging flow.

## Scope

Version one covers:

- `/proj` explicit task entry
- project interview session creation
- project-name uniqueness validation
- task-scoped assertion memory
- project scale classification
- template-family selection
- JSON-defined program generation
- gap and conflict detection
- workflow design document generation
- versioned PDF output for the user
- versioned JSON output for the broker
- Google Drive upload and LINE delivery of the generated document
- explicit review commands:
  - `/ok`
  - `/revise`
  - `/cancel`

Version one does not cover:

- actual project implementation
- autonomous code execution for the requested project
- long-term user memory
- reuse of prior project preferences
- cross-task personalization
- arbitrary natural-language approval detection

## Entry And Routing

### Explicit entry only

This agent must never be entered implicitly from normal conversation.

The only supported entry for version one is:

- `/proj`

This command starts a new task-scoped project interview session.

### General routing rule

- normal LINE chat remains on the current high-level conversation path
- `/proj` routes to the new `project_interview` workflow
- once inside an active project interview session, incoming user messages are interpreted against that session until the session is:
  - confirmed
  - cancelled
  - expired

### Review commands

After a workflow design document is generated, only these explicit commands may move the session:

- `/ok`
- `/revise`
- `/cancel`

Plain-language approval such as "可以" or "好" must not be treated as final confirmation in version one.

## Core Constraints

### Task-scoped memory only

This agent is not a general conversational persona.

It must retain no cross-task user memory except standard audit and artifact records already required by the broker.

The agent must not reuse:

- previous project preferences,
- previous architecture choices,
- historical user habits,
- earlier task-specific assumptions.

Only the current `project_interview` task state may influence its questioning and document output.

### High-grade model requirement

Requirement elicitation, contradiction detection, and design synthesis must use a high-grade model tier.

This is necessary because the agent must:

- ask focused clarification questions,
- interpret partial answers correctly,
- preserve consistency across many turns,
- produce architecture and planning output,
- respect skill-like process rules.

Version one should therefore explicitly separate:

- high-grade interview/synthesis model for requirement work,
- deterministic broker validators for state transitions and completeness checks.

The broker must not trust model output by itself.

### Superpowers-style process compliance

The interview agent is not literally running local Codex skills inside LINE.
However, it must emulate the same process constraints where applicable.

For version one this means:

- no implementation before reviewed design output exists,
- no transition to downstream implementation workflow before explicit user confirmation,
- structured plan output must include testing expectations,
- TDD must be stated as a downstream implementation rule in the workflow design output,
- requirement gaps and contradictions must be resolved before design finalization.

## High-Level Architecture

The new flow should sit on top of the current broker high-level system:

`LINE -> line-worker -> broker /api/v1/high-level/line/process -> HighLevelCoordinator -> project_interview services`

Recommended components:

- command/parser extension for `/proj`
- `project_interview` task draft and state handling inside `HighLevelCoordinator`
- assertion-state store
- interview progression service
- template-library selection service
- workflow design document generator
- PDF render service
- artifact delivery through existing `LineArtifactDeliveryService`

The broker remains responsible for:

- task/session state,
- storage,
- validation,
- artifact creation,
- delivery,
- approval gating.

The model remains responsible for:

- asking the next best question,
- extracting structured meaning from the user answer,
- summarizing requirements,
- proposing architecture and task flow.

## Session And State Model

The correct control model for this feature is not a single abstraction.
Version one should use:

- a session-level state machine for task progression and command handling
- a per-version directed acyclic graph (DAG) for requirement compilation and artifact derivation

The state machine controls allowed transitions.
The DAG controls dependency truth for a specific document version.

Each `/proj` invocation creates a dedicated session bound to:

- `task_id`
- `channel = line`
- `user_id`
- `task_type = project_interview`
- `created_at`
- `expires_at`

The task state should track:

- `current_phase`
- `current_question_id`
- `project_name`
- `project_folder_name`
- `project_scale`
- `candidate_template_families`
- `selected_template_family`
- `selected_modules`
- `selected_style_profile`
- `document_version`
- `review_state`
- `latest_pdf_artifact_id`
- `latest_json_artifact_id`
- `open_gaps`
- `open_conflicts`
- `pending_revision_request`

Recommended phases:

1. `idle`
2. `collect_project_name`
3. `classify_project_scale`
4. `narrow_template_family`
5. `confirm_template_family`
6. `collect_template_requirements`
7. `resolve_conflicts`
8. `compile_project_definition`
9. `render_review_artifacts`
10. `await_user_review`
11. `revise_requested`
12. `confirmed`
13. `cancelled`
14. `failed`

State transition rules:

- the session starts in `idle` and enters `collect_project_name` on `/proj`
- it cannot leave that phase until the name is valid and unique
- it cannot enter `compile_project_definition` while any required assertion bundle is unresolved or conflicted
- it cannot enter `render_review_artifacts` without a successful canonical project-instance JSON build
- it cannot enter `confirmed` without a generated document version and explicit `/ok`
- `/revise` from `await_user_review` must move the session to `revise_requested`, then back to the appropriate collection phase
- `/cancel` may transition any active non-terminal phase to `cancelled`

### State-machine responsibilities

The session state machine owns:

- command admissibility
- phase progression
- review gating
- retry and failure handling
- cancellation
- revision entry

It does not define truth derivation for the generated artifacts.
That belongs to the per-version DAG.

## Per-Version DAG Model

Each generated review version `vN` should have its own directed acyclic graph.
The graph represents how confirmed information for that version produces its JSON and PDF artifacts.

This graph must remain acyclic.
Revision does not mutate the old graph into a loop.
Revision creates a new graph for the next version.

### DAG purpose

The DAG exists to make these relationships explicit:

- which assertions are confirmed
- which derived choices depend on them
- which outputs were rendered from which canonical inputs

### Recommended DAG nodes

At minimum, a version DAG should contain nodes equivalent to:

- `raw_user_messages`
- `interpreted_candidates`
- `restatement_options`
- `user_selection_or_correction`
- `confirmed_assertions`
- `project_scale`
- `template_family_candidates`
- `selected_template_family`
- `selected_modules`
- `style_profile`
- `constraints`
- `project_instance_definition_json`
- `workflow_design_view_model`
- `workflow_design_pdf`
- `artifact_bundle`
- `delivery_links`

### Recommended DAG edges

The main dependency shape should be:

`raw_user_messages -> interpreted_candidates -> restatement_options -> user_selection_or_correction -> confirmed_assertions`

`confirmed_assertions -> project_scale`

`confirmed_assertions -> template_family_candidates -> selected_template_family`

`confirmed_assertions -> selected_modules`

`confirmed_assertions -> style_profile`

`confirmed_assertions -> constraints`

`project_scale + selected_template_family + selected_modules + style_profile + constraints -> project_instance_definition_json`

`project_instance_definition_json -> workflow_design_view_model -> workflow_design_pdf`

`workflow_design_pdf + project_instance_definition_json -> artifact_bundle -> delivery_links`

### Revision rule

If the user sends `/revise`:

- the previous version graph remains immutable
- revision requests produce new candidate assertions
- newly confirmed assertions produce a new canonical project-instance JSON
- the system builds a fresh DAG for `vN+1`

This makes version history auditable and prevents hidden mutation of past outputs.

## Requirement Memory Model

Task-scoped memory should be split into four layers.
The key rule is that memory is not a free-form summary.
The canonical state is built from confirmed restatements and assertions.

### 1. Conversation log

Append-only raw Q/A for audit and traceability.

This is not the canonical requirement state.

### 2. Working memory

Short-lived operational state:

- current phase
- current question
- current restatement bundle
- unresolved ambiguities
- recent user answer
- pending follow-up reason

### 3. Assertion registry

This is the canonical requirement state.

The smallest memory unit is an assertion.
Users do not interact with naked assertions directly.
The system groups assertions into explicit restatement options for confirmation.

Each assertion should carry:

- `assertion_id`
- `statement`
- `status`
- `evidence`
- `updated_at`

Allowed statuses:

- `missing`
- `candidate`
- `confirmed`
- `rejected`
- `superseded`
- `conflicted`

### 4. Structured project definition

This is the canonical machine-readable task output derived from confirmed assertions.

It should contain:

- project scale
- selected template family
- selected modules
- style profile
- constraint set
- generated JSON-defined program
- workflow-design render metadata

### 5. Design artifact state

Tracks generated outputs:

- version number
- render timestamp
- current PDF artifact id
- current JSON artifact id
- review history
- approval status
- current DAG id or digest
- prior version references

## Restatement And Confirmation Protocol

Version one should not rely on long free-form conversational carryover.
It should reduce semantic scope every turn.

### Core rule

Every meaningful requirement update must pass through:

1. user input
2. system interpretation
3. explicit restatement
4. user confirmation or correction
5. assertion-state update

### User-facing interaction unit

The internal truth unit is an assertion.
The user-facing interaction unit is an explicit statement composed from one or more assertions.

Each question round should prefer:

- 1 to 3 explicit statement options
- 1 conservative escape option such as:
  - "none of these is precise"
  - "closest to A, but I need to revise it"

### Hard rule

Unconfirmed interpretation must not be promoted to canonical memory.
It may remain in working memory as a pending interpretation only.

## Required Requirement Schema

Version one should define a fixed required schema.
This schema is populated from confirmed assertions, not direct free-form slot filling.

Minimum required fields:

- `project_name`
- `project_scale`
- `template_family`
- `project_goal`
- `target_users`
- `enabled_modules`
- `disabled_modules`
- `core_user_flows`
- `data_entities`
- `auth_profile`
- `external_integrations`
- `deployment_target`
- `style_profile`
- `non_functional_requirements`
- `acceptance_criteria`
- `constraints`
- `out_of_scope`

These fields must be complete enough to support:

- template selection,
- architecture summary,
- implementation plan outline,
- task-flow decomposition,
- downstream implementation gating.

## Project Scale Classification

The interview must classify project scale before deep requirement expansion.

Version one should support:

- `tool_page`
  - single-purpose page or simple utility
  - uses lightweight UI composition DSL
- `mini_app`
  - small multi-page application with basic data flow
  - uses compact structure DSL
- `structured_app`
  - multi-module, multi-flow application
  - uses full workflow/structure DSL

This classification limits both interview scope and the allowed template space.

## Template Library Strategy

Templates should be treated as large reusable components above the existing component library.

They must not be raw hand-authored project code first.
Version one should define templates primarily as JSON-defined programs that compile onto existing runtime and component mechanisms.

Each template family should declare:

- interaction structure
- supported project scales
- required sections/pages
- optional modules
- unsupported patterns
- allowed style controls
- required component-library capabilities
- DSL path

Recommended initial template families:

- `content_showcase`
- `form_collection`
- `member_portal`
- `list_search`
- `crud_admin`
- `dashboard`
- `multi_step_flow`
- `transaction_flow`

## JSON-Defined Program Model

The canonical project definition should be document-first.

Version one should prefer three JSON layers:

1. `template_definition`
   - structure skeleton
   - route graph
   - page/section composition
   - module attachment points
2. `module_definition`
   - auth
   - search/filter
   - CRUD detail
   - workflow steps
   - dashboard widgets
3. `project_instance_definition`
   - selected template
   - enabled modules
   - style selections
   - project-specific configuration

Existing runtime mechanisms remain the execution substrate:

- `packages/javascript/browser/page-generator`
- `packages/javascript/browser/ui_components`
- `templates/spa/frontend/runtime/page-generator`
- `templates/spa/frontend/core`

## Project Name Rules

Project name is the first hard gate.

The system must:

- ask for project name first,
- normalize it into a folder-safe name,
- verify that the normalized folder name does not conflict with existing managed project folders,
- reject duplicates before continuing.

If the name is unavailable:

- remain in `collect_project_name`
- explain that the project name is already taken
- ask for another name

This check should reuse the existing managed-workspace isolation rules already present in the high-level coordinator.

## Interview Script

### Default flow when the user does not interrupt

The interview should use a fixed skeleton with dynamic follow-up.
The sequence should be template-first rather than fully open-ended.

Recommended base sequence:

1. project name
2. project scale classification
3. candidate template family narrowing
4. primary template family selection
5. project goal and target users
6. enabled and disabled modules
7. key user flows
8. data and persistence
9. auth and permissions
10. external integrations
11. deployment target
12. style profile
13. non-functional requirements
14. acceptance criteria
15. constraints and out-of-scope

### One-question rule

Version one should ask one primary question at a time.

This keeps the LINE interaction manageable and improves extraction quality.
Each question should prefer explicit statement options over broad free-form prompts.

### Dynamic follow-up rule

After every user answer, the system must do three things:

1. update pending interpretations
2. restate them as explicit statement options when needed
3. choose the next most important question

Priority order:

1. resolve `conflicted`
2. confirm candidate assertions
3. fill `missing`
4. continue the default skeleton

### Handling user interruptions

If the user interrupts with additional requirement detail:

- map it into pending assertions
- mark affected template/module choices for re-check
- continue from the highest-priority unresolved field

If the user asks a side question:

- answer briefly
- then return to the unfinished interview path

If the user requests revision after document generation:

- enter `revise_requested`
- capture the revision request
- update affected assertions, template selections, and document sections
- regenerate the next version

## Gap And Conflict Detection

The model may propose interpretations, but the broker must also run deterministic validation.

### Gap examples

- a template family is selected but no enabled modules are chosen
- auth is required but no login or role model is confirmed
- deployment target is declared but no compatible DSL path is selected
- core features are given but no acceptance criteria are confirmed

### Conflict examples

- `project_scale = tool_page` with a structured multi-role workflow
- `template_family = content_showcase` with required CRUD admin behavior
- `deployment_target = windows service` with a SPA-only template path
- `auth_profile = none` while role-based access control is required

### Hard rule

The session must not generate a reviewable workflow design document until:

- no required assertion bundle is unresolved
- no required field is `missing`
- no required field is `conflicted`

Version one should be conservative and require all required fields to be backed by confirmed assertions.

## Workflow Design Output

The workflow design output is the mandatory checkpoint artifact for this task.

It should include:

- project overview
- project scale classification
- selected template family and why it fits
- selected modules and excluded modules
- normalized requirements summary
- architecture proposal
- component/module breakdown
- data model summary
- external integration summary
- deployment target summary
- DSL path summary
- implementation phases
- detailed task flow table
- testing strategy with TDD expectation
- open assumptions, if any remain

This artifact is not just a conversation summary.
It is the formal handoff document for the next implementation-stage agent.

## PDF And JSON Output Model

### User-facing artifact

The primary user-facing artifact must be:

- `workflow-design.vN.pdf`

This is the file the LINE user downloads and reviews.

### Broker-facing artifact

The broker must also generate:

- `workflow-design.vN.json`

This JSON is the canonical machine-readable task output for downstream automation.
It should be the derived project-instance definition, not just a slot dump.

### Versioning

Before approval, the document may be regenerated many times:

- `v1`
- `v2`
- `v3`

Each revision produces both:

- a new PDF
- a new JSON

The latest version becomes the current review target.

## PDF/JSON Mutual Verification

Full semantic bidirectional verification between arbitrary PDF text and structured JSON is too fragile to treat as deterministic in version one.

Therefore the design should use a controlled verification model instead of pretending PDF is canonical.

### Canonical source

The canonical source of truth is the structured JSON.
More precisely, it is the canonical project-instance JSON derived from confirmed assertions inside a specific version DAG.

### Render pipeline

Recommended pipeline:

1. confirmed assertion state
2. version DAG build
3. canonical project-instance JSON
4. deterministic document view-model
5. deterministic intermediate markdown or HTML
6. PDF render

### Verification strategy

Version one should enforce consistency using three checks:

1. **schema completeness check**
   - project-instance JSON must pass required-field validation before render

2. **render manifest check**
   - generate a digest from canonical project-instance JSON
   - embed the digest, task id, and version in the rendered document metadata or appendix
   - record the same digest in artifact state
   - record the source DAG id or digest alongside the artifact pair

3. **section coverage check**
   - before rendering PDF, validate that the intermediate document contains all required sections:
     - overview
     - requirements
     - architecture
     - task flow
     - testing

This gives a defensible mutual-verification model:

- JSON validates the required structured meaning
- rendered document proves it corresponds to a specific JSON version
- broker can verify the artifact pair before delivery

Version one should not claim that parsing the PDF back into full JSON is reliable enough to be a hard gate.

## Delivery Behavior

The generated review artifact pair should be delivered through the existing artifact-delivery path.

Recommended delivery set:

- user-facing PDF artifact
- machine-readable JSON artifact
- optional zip containing both files

For LINE delivery, the simplest usable version is:

- upload the PDF
- upload a zip containing `pdf + json`
- send the user the download link for the zip and mention the PDF as the primary review file

If Google Drive delivery succeeds:

- keep the current Drive-first path

If Google Drive delivery fails:

- rely on the existing broker-owned signed download fallback path

## Commands During Review

After document generation:

- `/ok` marks the current version as approved
- `/revise` marks the current version as needing revision
- `/cancel` cancels the task

Suggested optional convenience commands for version one:

- `/status` to show current phase and version
- `/latest` to resend the latest review artifact links

These are useful but not mandatory for the first rollout.

## Data Persistence

The current broker persistence model should be reused.

Recommended new document families:

- `hlm.project-interview.state.{channel}.{userId}`
- `hlm.project-interview.requirements.{channel}.{userId}`
- `hlm.project-interview.review.{channel}.{userId}`
- `hlm.project-interview.version-graph.{channel}.{userId}.{version}`

Suggested artifact linkage:

- PDF and JSON outputs should be recorded through existing artifact recording and delivery services
- the current task state should point to the latest artifact ids and version number
- the task state should also point to the selected scale, template family, and DSL path
- each version should preserve its own DAG metadata and upstream assertion references

This keeps the new feature aligned with the broker’s existing task/document model instead of introducing a parallel storage system.

## Model And Prompting Policy

The interview model prompt must explicitly enforce:

- task-scoped memory only
- no user-history reuse
- one primary question at a time
- explicit restatement before canonical write
- requirement extraction into assertions and confirmed bundles
- template-first narrowing
- no implementation promises
- no approval inference without `/ok`
- no skipped gaps or unresolved contradictions

The model should also be instructed that downstream implementation is expected to follow:

- planning discipline
- TDD
- verification before completion

That requirement belongs in the generated workflow design output even though version one stops before implementation.

## Error Handling

### Invalid or duplicate project name

- do not progress
- remain in project-name collection

### Incomplete requirements

- do not generate the workflow design
- keep asking focused follow-up questions

### Render failure

- mark the artifact generation step as failed
- retain the assertion state and canonical project-instance JSON inputs
- allow regeneration after retry

### Delivery failure

- rely on existing artifact-delivery fallback behavior
- never lose the current approved or pending review version state because delivery failed

## Testing Strategy

Version one should be implemented with TDD.

Minimum test coverage should include:

1. `/proj` routes into `project_interview`
2. normal chat does not enter `project_interview`
3. project name uniqueness gate blocks duplicates
4. project scale classification selects the correct DSL path
5. template-family narrowing returns bounded candidate sets
6. restatement confirmation promotes assertions only after explicit user confirmation
7. document generation is blocked while required assertions or fields are incomplete
8. document generation creates versioned JSON state
9. document generation creates versioned PDF artifact metadata
10. `/ok` only confirms when a reviewable document version exists
11. `/revise` creates a new version instead of overwriting approval state
12. `/cancel` closes the task cleanly
13. generated review artifacts can be delivered through existing artifact delivery
14. user-facing output does not expose internal managed-workspace paths

## Recommended File-Level Change Direction

Likely areas of change:

- `packages/csharp/broker/Services/HighLevelCoordinator.cs`
  - new task type routing
  - `/proj` command handling
  - project interview state transitions

- new focused services under `packages/csharp/broker/Services/`
  - project interview state service
  - project interview validator
  - workflow design document generator
  - PDF render helper/service

- existing artifact delivery path
  - reuse `LineArtifactDeliveryService`

- broker verification and formal test layers
  - `packages/csharp/broker/verify/Program.cs`
  - `packages/csharp/tests/integration/**`

## Rollout Strategy

This feature should be shipped in two stages.

### Stage 1

Build only the dedicated interview-and-review workflow:

- `/proj`
- assertion-based interview state
- scale/template/DSL selection
- document generation
- PDF/JSON delivery
- explicit review gate

### Stage 2

Add the downstream implementation agent that consumes the approved `project_interview` output and actually builds the project in a unique project folder with TDD and final packaging.

Stage 2 must not start until Stage 1 is stable, because the workflow design document is the key checkpoint between interview and execution.

## Acceptance Criteria

The feature is complete for version one when:

- a LINE user can enter `/proj`
- the broker starts a task-scoped interview session
- the session classifies project scale and selects a template family
- the session collects all required requirement fields through confirmed restatements
- duplicate project names are blocked up front
- the system detects requirement gaps and conflicts before document generation
- the broker generates a versioned workflow design PDF
- the broker generates a matching versioned project-definition JSON artifact
- both artifacts are recorded and delivered to the user
- the user can explicitly respond with `/ok`, `/revise`, or `/cancel`
- no long-term user memory is reused
- the flow does not silently transition into implementation
