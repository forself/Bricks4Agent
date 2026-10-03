# Browser Binding And Lease Model

## Goal

Move browser execution away from free-form ids and toward broker-owned records.

The browser request builder now validates against real broker data for:

- `BrowserSiteBinding`

- `BrowserSessionLease`

- `BrowserUserGrant`

- `BrowserSystemBinding`

Current implementation validates site bindings, user grants, system bindings and session leases (`packages/csharp/broker/Services/BrowserExecutionRequestBuilder.cs:65-157`).
Session leases give future runtime/session pooling work a canonical record shape to target.

## BrowserSiteBinding

`BrowserSiteBinding` represents a broker-known site binding.

Current fields:

- `site_binding_id`

- `display_name`

- `identity_mode`

- `site_class`

- `origin`

- `principal_id`

- `status`

- `metadata_json`

- `created_at`

### Intended meaning

- `identity_mode`

- must align with the browser tool identity mode

- `site_class`

- must be allowed by the browser site policy

- `origin`

- canonical origin for future origin enforcement

- `principal_id`

- required for user-delegated bindings

## BrowserSessionLease

`BrowserSessionLease` represents a broker-owned runtime session lease.

Current fields:

- `session_lease_id`

- `tool_id`

- `site_binding_id`

- `principal_id`

- `identity_mode`

- `lease_state`

- `expires_at`

- `created_at`

- `last_used_at`

### Intended meaning

The request builder validates a referenced lease (active, same principal, same identity mode, not expired, same site binding), and the broker runtime echoes the lease id in the result and touches `last_used_at` after execution (`packages/csharp/broker/Services/BrowserExecutionRuntimeService.cs:83,93,134-140`); no runtime reuses a leased browser session yet.
It exists so future browser workers do not invent their own lease identifiers and lifecycle rules outside the broker.

## BrowserUserGrant

`BrowserUserGrant` represents a broker-owned delegated user authorization record.

Current fields:

- `user_grant_id`

- `principal_id`

- `site_binding_id`

- `status`

- `consent_ref`

- `scopes_json`

- `expires_at`

- `created_at`

## BrowserSystemBinding

`BrowserSystemBinding` represents a broker-owned system credential binding.

Current fields:

- `system_binding_id`

- `display_name`

- `site_binding_id`

- `status`

- `secret_ref`

- `created_at`

## Current Builder Enforcement

`BrowserExecutionRequestBuilder` now validates:

- required site binding exists when site policy requires it

- site binding is active

- site binding identity matches tool identity mode

- site binding class is allowed by tool site policy

- for user-delegated tools, the site binding principal must match the requesting principal

- referenced user grant exists, is active, belongs to the requesting principal, and matches the site binding when pinned

- referenced system binding exists, is active, and matches the site binding when pinned

- referenced session lease exists, is active, belongs to the requesting principal, matches the tool identity mode, is not expired, and matches the site binding when pinned

It still also validates:

- action level ceiling

- user grant requirement

- system binding requirement

## Current Phase

Implemented:

- broker tables for site bindings and session leases

- broker tables for user grants and system bindings

- indexes for lookup by identity/principal/state

- builder validation against `BrowserSiteBinding`

- builder validation against `BrowserUserGrant`

- builder validation against `BrowserSystemBinding`

- builder validation against `BrowserSessionLease`

- site-binding / user-grant / system-binding catalog management API (`/api/v1/browser-admin/*/list|get|upsert`, `packages/csharp/broker/Endpoints/BrowserBindingEndpoints.cs:14-137`; local-admin mirror under `/api/v1/local-admin/browser/*`)

- session lease issuance and revocation (`/api/v1/browser-admin/leases/issue|revoke`, `BrowserBindingEndpoints.cs:160-188`)

Not yet implemented:

- browser worker reuse of lease records

## Rule

From this point onward, browser execution should move toward broker-owned records and away from ad hoc binding identifiers.
