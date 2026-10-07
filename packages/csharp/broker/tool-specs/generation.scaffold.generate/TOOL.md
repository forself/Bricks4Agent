# Generate Scaffold

Generates a multi-page front-end prototype (form, list and detail pages) from a validated DefinitionTemplate with the component library, and packages it as a deterministic zip.

## Capability

- Tool ID: `generation.scaffold.generate`

- Capability ID: `generation.scaffold.generate`

- Route: `generate_scaffold`

- Status: `beta`

- Risk: medium, approval `auto_if_task_scope_match` (inside the granted scope it runs without further approval; outside it needs administrator approval)

- Runtime: `generation-worker`

## Input

| Field | Type | Meaning |
|---|---|---|
| `template` | object (required) | The validated DefinitionTemplate |
| `page_ids` | array of strings, each at most 64 characters | Generate only these pages |
| `title` | string, at most 120 characters | Title of the prototype |

The request carries no output location. Path-like arguments are ignored.

## Grant scope

The broker writes the output location into the grant scope; the worker validates it and refuses the request when any key is missing or malformed.

| Key | Meaning |
|---|---|
| `routes` | `["generate_scaffold"]` |
| `output_slot` | Output directory under the generation output root (letters, digits, `_` and `-`, 1 to 80 characters) |
| `package_name` | Base name of the zip (same character rule) |
| `max_pages` | Maximum number of pages (integer) |
| `package` | `definition-site-v1` |

## Output

```json
{
  "output_slot": "<output_slot>",
  "request_id": "<request id>",
  "zip": { "path": "<output_slot>/<request id>/<package_name>-scaffold.zip", "sha256": "<hex>", "size": 0 },
  "pages": [{ "id": "<page id>", "type": "list", "field_count": 0 }],
  "file_count": 0,
  "validation_digest": "<hex>",
  "generator_version": "<version>",
  "catalog_sha256": "<hex>"
}
```

The zip path is relative to the generation output root. The payload contains no file contents and no host paths. The zip holds `site/` (the prototype) and `report/` (validation report and file manifest).

A definition that fails validation fails the execution; its error message is `{"ok":false,"errors":[...]}` with the same structured errors as `validate_definition`.

## Rules

- Idempotent per request: a retried request returns the stored result when the zip still matches its recorded sha256.

- The zip is deterministic: sorted entries, fixed timestamps and permissions.

- The broker verifies the zip path and sha256 before it delivers the package to the user.
