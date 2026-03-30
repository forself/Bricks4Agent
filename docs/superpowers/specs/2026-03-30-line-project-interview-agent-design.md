# LINE Project Interview Agent Design

Date: 2026-03-30
Status: design draft for review

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
- task-scoped working memory
- structured requirement slots
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
- structured requirement state store
- interview progression service
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
- `document_version`
- `review_state`
- `latest_pdf_artifact_id`
- `latest_json_artifact_id`
- `open_gaps`
- `open_conflicts`
- `pending_revision_request`

Recommended phases:

1. `collect_project_name`
2. `collect_requirements`
3. `gap_review`
4. `draft_workflow_design`
5. `await_user_review`
6. `revise_workflow_design`
7. `confirmed`
8. `cancelled`

State transition rules:

- the session starts in `collect_project_name`
- it cannot leave that phase until the name is valid and unique
- it cannot enter `draft_workflow_design` while any required slot is `missing` or `conflicted`
- it cannot enter `confirmed` without a generated document version and explicit `/ok`

## Requirement Memory Model

Task-scoped memory should be split into four layers.

### 1. Conversation log

Append-only raw Q/A for audit and traceability.

This is not the canonical requirement state.

### 2. Working memory

Short-lived operational state:

- current phase
- current question
- unresolved ambiguities
- recent user answer
- pending follow-up reason

### 3. Structured requirements

This is the canonical requirement state.

Each field should carry:

- `value`
- `status`
- `evidence`
- `updated_at`

Allowed statuses:

- `missing`
- `partial`
- `filled`
- `conflicted`

### 4. Design artifact state

Tracks generated outputs:

- version number
- render timestamp
- current PDF artifact id
- current JSON artifact id
- review history
- approval status

## Required Requirement Schema

Version one should define a fixed required schema.

Minimum required fields:

- `project_name`
- `project_goal`
- `target_users`
- `project_type`
- `core_features`
- `user_flows`
- `data_entities`
- `auth_requirements`
- `external_integrations`
- `deployment_target`
- `non_functional_requirements`
- `acceptance_criteria`
- `constraints`
- `out_of_scope`

These fields must be complete enough to support:

- architecture summary,
- implementation plan outline,
- task-flow decomposition,
- downstream implementation gating.

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

Recommended base sequence:

1. project name
2. project goal
3. target users
4. system type
5. core features
6. user flows
7. data and persistence
8. auth and permissions
9. external integrations
10. deployment target
11. non-functional requirements
12. acceptance criteria
13. constraints and out-of-scope

### One-question rule

Version one should ask one primary question at a time.

This keeps the LINE interaction manageable and improves extraction quality.

### Dynamic follow-up rule

After every user answer, the system must do three things:

1. update structured requirement slots
2. detect `missing`, `partial`, or `conflicted` fields
3. choose the next most important question

Priority order:

1. resolve `conflicted`
2. fill `missing`
3. clarify `partial`
4. continue the default skeleton

### Handling user interruptions

If the user interrupts with additional requirement detail:

- map it into the appropriate slots
- mark affected dependent fields for re-check
- continue from the highest-priority unresolved field

If the user asks a side question:

- answer briefly
- then return to the unfinished interview path

If the user requests revision after document generation:

- enter `revise_workflow_design`
- capture the revision request
- update slots and affected sections
- regenerate the next version

## Gap And Conflict Detection

The model may propose slot updates, but the broker must also run deterministic validation.

### Gap examples

- `auth_requirements = required` but no login or role model specified
- deployment target declared but runtime stack unspecified
- core features given but no acceptance criteria

### Conflict examples

- `project_type = frontend_only` with backend-only deployment target
- `deployment_target = windows service` with SPA-only scope
- `auth_requirements = none` while role-based access control is requested

### Hard rule

The session must not generate a reviewable workflow design document until:

- no required field is `missing`
- no required field is `conflicted`

`partial` fields may only be allowed into draft generation if a deterministic policy explicitly permits them.
Version one should be conservative and require all required fields to be `filled`.

## Workflow Design Output

The workflow design output is the mandatory checkpoint artifact for this task.

It should include:

- project overview
- normalized requirements summary
- architecture proposal
- component/module breakdown
- data model summary
- external integration summary
- deployment target summary
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

### Render pipeline

Recommended pipeline:

1. JSON requirement state
2. deterministic document view-model
3. deterministic intermediate markdown or HTML
4. PDF render

### Verification strategy

Version one should enforce consistency using three checks:

1. **schema completeness check**
   - JSON must pass required-field validation before render

2. **render manifest check**
   - generate a digest from canonical JSON
   - embed the digest, task id, and version in the rendered document metadata or appendix
   - record the same digest in artifact state

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

Suggested artifact linkage:

- PDF and JSON outputs should be recorded through existing artifact recording and delivery services
- the current task state should point to the latest artifact ids and version number

This keeps the new feature aligned with the broker’s existing task/document model instead of introducing a parallel storage system.

## Model And Prompting Policy

The interview model prompt must explicitly enforce:

- task-scoped memory only
- no user-history reuse
- one primary question at a time
- requirement extraction into slots
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
- retain the structured requirement state
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
4. required-field validator correctly marks `missing`, `partial`, and `conflicted`
5. default interview flow advances question by question
6. user interruption updates the correct slots
7. document generation is blocked while required slots are incomplete
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
- requirement slots
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
- the session collects all required requirement fields
- duplicate project names are blocked up front
- the system detects requirement gaps and conflicts before document generation
- the broker generates a versioned workflow design PDF
- the broker generates a matching versioned JSON artifact
- both artifacts are recorded and delivered to the user
- the user can explicitly respond with `/ok`, `/revise`, or `/cancel`
- no long-term user memory is reused
- the flow does not silently transition into implementation

