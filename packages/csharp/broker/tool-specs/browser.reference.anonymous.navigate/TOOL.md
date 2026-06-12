# Browser Reference: Anonymous Navigate

Reference spec for broker-governed anonymous, read-only multi-step navigation.

## Capability

- Capability ID: `browser.navigate`
- Route: `browser_navigate`
- Risk: low

## Action-Level Governance

Unlike `browser.read`, this capability is policy-aware. The broker attaches the
policy context (`max_action_level`, `intended_action_level`,
`requires_human_confirmation_on`) and the worker runtime enforces it through
`BrowserActionGate` before any browser action runs:

- `read` — extract a single page
- `navigate` — read-only multi-step navigation within the same origin or an
  allowed host suffix
- `authenticate` / `draft_action` / `committed_action` — never executed here;
  the worker returns a `gated` result requiring human confirmation or exceeding
  the authorized maximum

When policy context is missing, the maximum level defaults to `read`. The runtime
never silently escalates a read into a delegated action.

## Safety

No form fill, submit, download, or file upload. Navigation follows internal links
only; cross-host hops require an explicit allowed host suffix.
