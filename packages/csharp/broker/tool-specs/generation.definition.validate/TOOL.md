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

The generator's JSON: `ok`, `errors`, `warnings`, `pages` (`id`, `type`, `field_count`), `validation_digest` and `validator_version`. Every error and warning carries `code`, `path`, `message` and `hint`. An error found at several places is reported once: its message gives the count and `paths` lists the first five paths (`path` is the first of them).

When errors or warnings were merged or cut, the result also carries:

| Field | Meaning |
|---|---|
| `total_errors` | Number of errors before merging and cutting |
| `truncated` | `true` when not every error is listed (at most 50 are; every error code keeps at least one entry) |
| `total_warnings`, `warnings_truncated` | The same for warnings |

The worker cuts a result that is larger than its `MaxResultBytes` the same way (first errors and warnings, `total_errors`, `truncated: true`); it is still a successful call.

When the grant scope carries `max_pages` and the selected pages exceed it, the result is `ok: false` with the error code `MAX_PAGES_EXCEEDED` first. The page limit is at most 12.

Warnings do not block generation. The cross-page warnings (`RESOURCE_WITHOUT_FORM`, `FORM_WITHOUT_LIST`, `FIELD_NOT_IN_FORM`, `OPTIONS_MISMATCH`) point at pages of one resource that do not share the api base path, field names or options, which breaks the prototype's list, detail and form flow.

A definition that fails validation is a successful call with `ok: false`, so the agent can correct the definition and validate again. Only internal failures (time-out, oversized output, generator crash) and malformed input (for example an object key longer than 128 characters) fail the execution.

## Rules

- The worker forwards only `template` and `page_ids` to the generator, and rejects a template with an object key longer than 128 characters before it runs the generator.

- Validation has no side effects. `generate_scaffold` runs the same checks before it writes anything.
