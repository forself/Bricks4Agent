# Browser Session And Credential Binding Model

## Goal

Define how browser-capable tools bind:

- credentials
- sessions
- reuse scope
- consent expectations

Identity mode answers **whose identity** is used.
Session binding answers **how that identity is materialized and governed at runtime**.

Both are required.

## Core Rule

Browser tools must never rely on ad hoc runtime session handling.

The following must be explicit in broker-owned definition data:

- where credentials come from
- who owns the session
- whether the session may be reused
- how long a session lease may live
- whether a consent record is required

## Session Binding Fields

The current registry model can now carry:

- `binding_mode`
  - `ephemeral`
  - `broker_managed`
  - `user_bound`
- `credential_binding`
  - `none`
  - `system_vault`
  - `user_grant`
- `reuse_scope`
  - `none`
  - `site`
  - `user`
  - `task`
- `lease_minutes`
- `requires_consent_record`
- `requires_interactive_login`

These fields belong to `browser_session_policy`.

## Meaning Of Binding Modes

### `ephemeral`

- a browser session is created for a single bounded action
- the session is discarded after completion
- typically used for anonymous public retrieval

### `broker_managed`

- the broker owns the session lifecycle
- the session may be reused according to policy
- typically used for system-account browser tools

### `user_bound`

- the session is bound to a specific user delegation context
- reuse must remain scoped to that user
- typically used for user-authorized assistant behavior

## Meaning Of Credential Bindings

### `none`

- no login material is required

### `system_vault`

- credentials come from a broker-owned vault or equivalent secret binding
- the runtime receives only the material needed for execution
- the model never sees the secret

### `user_grant`

- credentials or session grants come from an explicit user authorization record
- use must remain attributable to that user

## Reuse Scope

`reuse_scope` defines how broadly a session may be reused:

- `none`
  - no reuse
- `site`
  - reusable for the same site binding
- `user`
  - reusable for the same user-delegated context
- `task`
  - reusable only within a single broker task

This is separate from identity mode.

## Consent Expectations

### `requires_consent_record`

When `true`:

- execution must be tied to an explicit broker-recorded consent or grant
- audit must be able to point to that record

### `requires_interactive_login`

When `true`:

- the system must assume the user may need to complete an interactive login or challenge step
- the tool should not pretend it can silently complete authentication headlessly

## Expected Defaults

### Anonymous Public Read

- `binding_mode = ephemeral`
- `credential_binding = none`
- `reuse_scope = none`
- `requires_consent_record = false`
- `requires_interactive_login = false`

### System Account Read

- `binding_mode = broker_managed`
- `credential_binding = system_vault`
- `reuse_scope = site`
- `requires_consent_record = false`
- `requires_interactive_login = false`

### User Delegated Read

- `binding_mode = user_bound`
- `credential_binding = user_grant`
- `reuse_scope = user`
- `requires_consent_record = true`
- `requires_interactive_login = true` or policy-driven

## Why This Matters

Without explicit session policy, different browser tools will quietly diverge on:

- whether they reuse sessions
- how long sessions live
- whether credentials come from a vault or from user grants
- whether consent is enforced

That creates hidden security policy in implementation code.

The session and credential model must therefore be registry-visible before browser workers are introduced.

## Current Phase

Current implementation direction:

- broker registry can read and surface `browser_session_policy`
- reference browser specs should declare both identity and session policy

Not yet implemented:

- credential vault
- user grant store
- session lease issuance and revocation
- browser worker session pooling
- consent record enforcement

## Rule

No browser tool definition is complete until it explicitly states:

1. identity mode
2. credential binding
3. session binding
4. reuse scope
