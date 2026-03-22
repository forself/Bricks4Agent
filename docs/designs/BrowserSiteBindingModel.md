# Browser Site Binding Model

## Goal

Define how broker-governed browser tools bind to sites and origins.

Identity mode answers:

- whose identity is used

Session policy answers:

- how the session is materialized and reused

Site binding answers:

- which sites the tool is allowed to operate against
- whether a broker-registered site binding is required
- whether cross-origin navigation is allowed

All three are required for safe browser tooling.

## Core Rule

No browser tool should be treated as "generic web automation" by default.

Browser execution must always answer:

1. what identity is in use
2. how credentials/session are bound
3. which sites or origin classes are allowed

If site binding is unspecified, the tool definition is incomplete.

## Site Policy Fields

The registry can now carry:

- `site_binding_mode`
  - `public_open`
  - `registered_site`
  - `user_authorized_site`
- `allowed_site_classes`
- `requires_registered_site_binding`
- `requires_exact_origin_match`
- `allows_cross_origin_navigation`

These fields belong to `browser_site_policy`.

## Meaning Of Site Binding Modes

### `public_open`

- the tool may operate against public web targets
- no broker-owned site binding record is required
- still subject to source policy and runtime rules

Typical use:

- anonymous search
- anonymous page fetch

### `registered_site`

- the tool must use a broker-registered site binding
- site metadata is managed by the platform
- this is the expected mode for system-account browser tools

Typical use:

- broker-owned dashboards
- service-integrated backoffice sites

### `user_authorized_site`

- the tool must use a site binding that is explicitly authorized for the user context
- this is the expected mode for user-delegated browser tools

Typical use:

- personal assistant flows
- authenticated portals used on behalf of the user

## Allowed Site Classes

`allowed_site_classes` is a coarse classification for policy and audit.

Examples:

- `public_web`
- `broker_managed_site`
- `user_authorized_site`

These are not yet a full site catalog.
They are registry-visible coarse constraints that later runtime/site-binding systems can refine.

## Origin Matching

### `requires_exact_origin_match`

When `true`:

- execution must remain on the bound origin unless a separate policy explicitly permits otherwise

### `allows_cross_origin_navigation`

When `false`:

- runtime should not silently follow the browser into unrelated origins

This matters even for read-only tools.

## Expected Defaults

### Anonymous Public Read

- `site_binding_mode = public_open`
- `allowed_site_classes = ["public_web"]`
- `requires_registered_site_binding = false`
- `requires_exact_origin_match = false`
- `allows_cross_origin_navigation = true`

### System Account Read

- `site_binding_mode = registered_site`
- `allowed_site_classes = ["broker_managed_site"]`
- `requires_registered_site_binding = true`
- `requires_exact_origin_match = true`
- `allows_cross_origin_navigation = false`

### User Delegated Read

- `site_binding_mode = user_authorized_site`
- `allowed_site_classes = ["user_authorized_site"]`
- `requires_registered_site_binding = true`
- `requires_exact_origin_match = true`
- `allows_cross_origin_navigation = false`

## Current Phase

Current implementation direction:

- broker registry can read and surface `browser_site_policy`
- reference browser specs should declare identity, session policy, and site policy together

Not yet implemented:

- broker site-binding catalog
- user-authorized site registry
- runtime origin enforcement
- browser worker navigation guards

## Rule

No browser tool definition is complete until it explicitly states:

1. identity mode
2. session policy
3. site binding mode
4. origin/navigation constraints
