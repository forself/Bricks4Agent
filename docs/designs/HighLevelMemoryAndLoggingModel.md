# High-Level Memory And Logging Model

## Goal

Define how the high-level conversation layer should separate:

- raw audit logs
- interpreted interaction state
- reusable memory state
- executable downstream payloads

The core rule is:

- logs preserve truth
- memory preserves reusable state
- execution payloads preserve actionable intent

These are not the same thing and must not be collapsed into one store.

## Layer 1: Raw Log

Purpose:

- audit
- replay
- traceability
- debugging
- incident review

Properties:

- must preserve the original inbound message
- must preserve the original outbound reply
- must preserve channel metadata, timestamps, identifiers, and request metadata
- may include normalized routing results, but must not overwrite the raw content

Examples:

- original LINE message text
- original assistant reply
- webhook metadata
- routing result
- session / task ids

Important rule:

- raw log is not conversation memory

## Layer 2: Interaction Interpretation

Purpose:

- classify what the message means in system terms
- extract structured intent from raw human language

Typical outputs:

- `interaction_type`
  - `conversation`
  - `query`
  - `production`
  - `confirm`
  - `cancel`
  - `project_name`
  - `help`
- `prefixed_command`
  - `none`
  - `query`
  - `production`
  - `project_name`
- `needs_confirmation`
- `requires_project_name`
- `possible_goal`

Important rule:

- this layer is transient interpretation state, not long-term memory by itself

## Layer 3: Memory Form

Purpose:

- store information in the format most useful for future retrieval and reuse
- avoid forcing future callers to re-parse old natural language every time

Memory should be stored in the expected future access form, not as raw conversation fragments.

Examples:

- `current_goal`
- `confirmed_scope`
- `pending_project_name`
- `routing_state`
- `last_confirmed_task_type`
- `user_preferences`
- `allowed_workflow_step`

Good memory examples:

- `current_goal = "build a website prototype"`
- `pending_project_name = true`
- `preferred_language = "zh-TW"`
- `last_routing_mode = "production"`

Bad memory examples:

- full repeated raw transcripts when only a small confirmed fact is needed
- raw prompt text stored as if it were structured state
- previous instruction syntax preserved when only intent matters

Important rules:

- memory should be de-commanded where possible
- memory should not depend on the original prefix syntax to remain meaningful
- memory should be optimized for future system use, not for human verbatim replay

## Layer 4: Execution Form

Purpose:

- carry validated, structured intent into broker, tools, workers, or downstream task planners

Examples:

- `route_mode = production`
- `task_type = code_gen`
- `project_name = MySite`
- `tool_request = web.search.duckduckgo`
- `scope_descriptor = {...}`
- `runtime_descriptor = {...}`

Important rules:

- execution form should be deterministic and explicit
- execution form should not require downstream workers to re-interpret raw human wording
- execution form should be capability- and scope-ready

## Why Logs And Memory Must Be Split

If raw conversation text is used directly as memory, the system accumulates noise:

- prefix syntax leaks into future state
- temporary wording is mistaken for confirmed intent
- reminder text pollutes later prompts
- system-control metadata is mixed with user conversation state

If memory is over-compressed without raw logs, the system loses:

- auditability
- replayability
- forensic review
- proof of what the user actually said

Therefore:

- raw logs must exist
- memory must be derived
- execution state must be derived again from memory and current interaction

## De-Commanding Rule

User messages may use prefixes like:

- `?query`
- `/task`
- `#project`

Those prefixes are useful at interaction time, but they are not the ideal long-term memory representation.

Examples:

- raw input:
  - `/請幫我建立一個網站程式`
- interpreted form:
  - `interaction_type = production`
- memory form:
  - `current_goal = build website program`
- execution form:
  - `task_type = code_gen`

Likewise:

- raw input:
  - `#MySite`
- memory form:
  - `project_name = MySite`

The prefix is retained in logs, but stripped from memory and execution state.

Current implementation note:

- explicit `?search <keywords>` remains verbatim in raw interaction logs
- the memory projection stores only the de-commanded goal text (`<keywords>`)
- the execution layer references the broker-mediated tool capability separately, rather than reusing raw prefixed text

## Control-Plane State Must Not Pollute Conversation Memory

The following should remain control-plane state, not normal conversation memory:

- task draft ids
- handoff ids
- guide reminder timestamps
- routing counters
- capability / scope metadata

They may live in user profile or workflow documents, but they should not be injected into model prompts as if they were user conversation.

## Recommended Persistence Split

### Raw Log Store

- append-only or versioned
- suitable for replay and audit

### Memory Store

- mutable, compact, structured
- optimized for retrieval by future handlers

### Workflow Store

- pending draft
- current workflow step
- confirmation state
- project-name collection state

### Execution Store

- task
- plan
- handoff
- tool request

## Current Direction

The existing broker high-level layer already contains pieces of:

- conversation memory
- user profile memory
- draft / handoff workflow memory

The next refinement should make the split explicit:

1. raw interaction log as its own store or document family
2. memory documents storing de-commanded reusable state
3. workflow documents storing focused state machines
4. downstream execution payloads derived from current state, not from raw transcript scanning

## Decision Rule

From now on, new memory fields should be evaluated with this question:

> Is this field being stored because it preserves the original event, or because it will be useful to future callers?

If the answer is:

- "preserve original event" -> it belongs in log
- "useful to future callers" -> it belongs in memory or workflow state

Do not store one form and pretend it satisfies both.
