# Validate Definition

Validates a DefinitionTemplate with the same layered, fail-closed checks that generation runs, and returns structured errors that the agent can fix.

## Capability

- Tool ID: `generation.definition.validate`

- Capability ID: `generation.definition.validate`

- Route: `validate_definition`

- Status: `beta`

- Risk: low, approval `auto`

- Runtime: `generation-worker`

## Input

| Field | Type | Meaning |
|---|---|---|
| `template` | object (required) | The DefinitionTemplate to validate |
| `page_ids` | array of strings, each at most 64 characters | Validate only these pages |

## Output

The generator's JSON: `ok`, `errors`, `warnings`, `pages` (`id`, `type`, `field_count`), `validation_digest` and `validator_version`. Every error carries `code`, `path`, `message` and `hint`.

A definition that fails validation is a successful call with `ok: false`, so the agent can correct the definition and validate again. Only internal failures (time-out, oversized output, generator crash) fail the execution.

## Rules

- The worker forwards only `template` and `page_ids` to the generator.

- Validation has no side effects. `generate_scaffold` runs the same checks before it writes anything.
