# Browser Capability Identity Model

> **Implementation status (checked 2026-09-26)**
>
> - The implemented action-level ladder is `read` / `navigate` / `authenticate` / `draft_action` / `committed_action` (`packages/csharp/broker-core/Services/BrowserActionGate.cs:7-14`); there is no `submit` level as listed under Secondary Axes. See [BrowserActionAndApprovalModel.md](BrowserActionAndApprovalModel.md).
> - `browser.reference.anonymous.read` and `browser.reference.anonymous.navigate` are `active`; the system-account and user-delegated reference specs remain `planned` (`packages/csharp/broker/tool-specs/*/tool.json`).

## Goal

Define the first-level identity split for broker-governed browser tools.

Browser tools must not be modeled only by function (`search`, `fetch`, `submit`).
The first classification must be:

- whose identity is used

- whose credentials are used

- whose session is being created or reused

This is the primary trust boundary for future assistant behavior.

## Primary Identity Modes

### 1. `anonymous`

Meaning:

- the system does not log in

- no user account session is established

- no delegated personal identity is represented

Typical use:

- public search

- public page fetch

- public comparison

Properties:

- credential source: `none`

- session owner: `none`

- default risk: lowest of the browser classes

### 2. `system_account`

Meaning:

- the platform logs in with a broker-owned or service-owned account

- the identity is not the end user

- credentials are never exposed to the user or to the model

Typical use:

- platform integration accounts

- service-side dashboards

- broker-owned synchronization and collection jobs

Properties:

- credential source: `system_vault`

- session owner: `system`

- audit must record which system principal or vault binding was used

### 3. `user_delegated`

Meaning:

- the broker uses credentials, tokens, or session material explicitly granted by the user

- actions are performed as that user

- this is the basis for personal-assistant behavior

Typical use:

- reading a user’s own tickets, bookings, or account history

- filling forms on behalf of the user

- delegated browsing of authenticated portals

Properties:

- credential source: `user_grant`

- session owner: `user`

- audit and consent requirements are highest

## Why Identity Comes Before Function

These actions may look similar at the DOM level:

- read a page

- click a result

- submit a form

But they have completely different governance implications depending on identity mode:

- anonymous public fetch

- system-account authenticated read

- user-delegated authenticated write

Therefore identity mode must be the first classification, before action level.

## Secondary Axes

After identity mode, a browser tool may further classify:

- action level

- `read`

- `navigate`

- `authenticate`

- `submit`

- session lifetime

- `ephemeral`

- `broker_managed`

- `user_bound`

- confirmation policy

- `not_required`

- `broker_policy`

- `user_required`

These are secondary to identity.

## Tool Spec Representation

Browser-capable tool specs may include:

```json
{
  "browser_profile": {
    "identity_mode": "anonymous",
    "credential_source": "none",
    "session_owner": "none",
    "allowed_actions": ["read", "navigate"],
    "confirmation_policy": "broker_policy"
  }
}
```

This does not mean the tool is active.
It only means the registry can now carry the required governance metadata.

## Governance Expectations By Identity Mode

### Anonymous

- may read public content

- must not silently escalate into authenticated access

- should default to ephemeral browser state

### System Account

- credentials must come from broker-owned secret storage

- the model never sees the raw credentials

- audit must record which system account binding was used

### User Delegated

- explicit user authorization is required

- session binding must be user-specific

- actions that change external state should normally require stronger confirmation

## Current Implementation Direction

Current phase:

- define the identity model

- make the tool registry able to read and surface `browser_profile`

- keep browser tools in `planned` state until execution/runtime policy is ready (anonymous `read` / `navigate` reference tools are now `active`; system-account and user-delegated remain `planned`)

- browser worker runtime (implemented: `packages/csharp/workers/browser-worker`, handlers for `browser.read` and `browser.navigate`)

- broker-owned session leasing records with issue/revoke (implemented: `packages/csharp/broker/Endpoints/BrowserBindingEndpoints.cs:160-188`)

Not yet implemented:

- credential vault integration

- session replay (browser worker reuse of leased sessions)

- delegated write flows

- DOM action policy engine

## Rule

From this point onward, any browser tool proposal should answer this question first:

> Is this tool anonymous, system-account, or user-delegated?

If that is not explicit, the browser tool definition is incomplete.
