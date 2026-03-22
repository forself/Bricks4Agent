# Browser Action And Approval Model

## Goal

Define the maximum action level and confirmation requirements for broker-governed browser tools.

Identity mode answers:

- whose identity is used

Session policy answers:

- how credentials and sessions are bound

Site policy answers:

- which sites and origins the tool may touch

Action policy answers:

- what the browser tool is actually allowed to do
- which levels require explicit human confirmation

All four are required before browser tools can safely evolve toward assistant-grade operation.

## Core Rule

Browser tools must not quietly grow from read-only retrieval into delegated action.

The maximum action level must be explicit in registry definition data.

## Action Policy Fields

The registry can now carry:

- `max_action_level`
  - `read`
  - `navigate`
  - `authenticate`
  - `draft_action`
  - `committed_action`
- `requires_human_confirmation_on`
- `allows_form_fill`
- `allows_submit`
- `allows_download`
- `allows_file_upload`

These fields belong to `browser_action_policy`.

## Meaning Of Action Levels

### `read`

- read already-loaded content
- extract text, metadata, or structured results

### `navigate`

- navigate between allowed pages
- follow links or broker-approved page transitions

### `authenticate`

- perform login or session-establishment steps
- may pass through interactive auth challenges depending on session policy

### `draft_action`

- populate forms or stage an action without committing external state
- should stop before final submission

### `committed_action`

- submit or confirm an action that changes external state
- highest-risk browser action class

## Human Confirmation

`requires_human_confirmation_on` lists action levels that must not proceed silently.

Typical examples:

- anonymous tools: none
- system-account read tools: maybe none, or `authenticate` depending on policy
- user-delegated tools: often `authenticate`, `draft_action`, and always `committed_action`

This is a registry-visible requirement, not an implementation detail hidden in runtime code.

## Capability Expectations

### Anonymous Public Read

- `max_action_level = navigate`
- `requires_human_confirmation_on = []`
- `allows_form_fill = false`
- `allows_submit = false`
- `allows_download = false`
- `allows_file_upload = false`

### System Account Read

- `max_action_level = authenticate`
- `requires_human_confirmation_on = []` or policy-driven
- `allows_form_fill = false`
- `allows_submit = false`

### User Delegated Read

- `max_action_level = authenticate`
- `requires_human_confirmation_on = ["authenticate"]`
- `allows_form_fill = false`
- `allows_submit = false`

## Why This Matters

Without an explicit action model, browser tools drift into unsafe ambiguity:

- is this tool only reading?
- may it log in?
- may it fill a form?
- may it submit?
- when must the user be asked again?

Those are governance questions and must be visible in broker-owned spec data.

## Current Phase

Current implementation direction:

- broker registry can read and surface `browser_action_policy`
- reference browser specs should declare action limits alongside identity/session/site policy

Not yet implemented:

- runtime action gate enforcement
- DOM-level action policy engine
- form-staging checkpoints
- final-commit approval workflow

## Rule

No browser tool definition is complete until it explicitly states:

1. who it acts as
2. how session/credentials are bound
3. which sites it may touch
4. what maximum action level it may reach
