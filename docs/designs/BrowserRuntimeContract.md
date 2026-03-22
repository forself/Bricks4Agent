# Browser Runtime Contract

## Goal

Define the canonical broker-to-browser-runtime request and result shapes before a real browser worker is introduced.

The browser tool model now has four governance layers:

- identity
- session/credential binding
- site binding
- action/approval

Those layers must survive into runtime payloads.
If they do not, runtime code will quietly reintroduce hidden policy.

## Core Rule

Browser runtime requests must carry policy context explicitly.

The runtime should not need to rediscover:

- whose identity is in use
- how the session is bound
- what site class or binding is allowed
- what maximum action level is permitted

That information should be attached by the broker.

## BrowserExecutionRequest

Shared request fields now include:

- routing identity
  - `request_id`
  - `tool_id`
  - `capability_id`
  - `route`
- policy identity
  - `identity_mode`
  - `credential_binding`
  - `session_binding_mode`
  - `session_reuse_scope`
  - `site_binding_mode`
  - `allowed_site_classes`
  - `max_action_level`
  - `requires_human_confirmation_on`
- broker context
  - `principal_id`
  - `task_id`
  - `session_id`
- optional runtime binding ids
  - `site_binding_id`
  - `user_grant_id`
  - `system_binding_id`
  - `session_lease_id`
- execution intent
  - `start_url`
  - `intended_action_level`
  - `arguments_json`
  - `scope_json`

## BrowserExecutionResult

Shared result fields now include:

- `request_id`
- `success`
- `tool_id`
- `action_level_reached`
- `final_url`
- `title`
- `content_text`
- `structured_data_json`
- `session_lease_id`
- `evidence_ref`
- `error_message`

This keeps browser execution results aligned with:

- audit
- future replay
- future session leasing
- promotion of browser results back into high-level memory or execution evidence

## Current Phase

Current implementation direction:

- shared contracts exist in `BrokerCore.Contracts`
- the contract is canonical before the first real browser worker is introduced

Not yet implemented:

- request builder from tool specs
- browser worker runtime
- result normalization adapters
- session lease manager
- DOM/action replay

## Rule

No browser runtime path should be introduced with ad hoc JSON once these contracts exist.
