# Query Component Catalog

Returns a bounded, deterministic summary of the component catalog and the DefinitionTemplate rules, so that a governed agent can write a generation definition without reading repository files.

## Capability

- Tool ID: `generation.catalog.query`

- Capability ID: `generation.catalog.query`

- Route: `query_component_catalog`

- Status: `beta`

- Risk: low, approval `auto`

- Runtime: `generation-worker`

## Input

| Field | Type | Meaning |
|---|---|---|
| `section` | `overview`, `field_types`, `example` or `component` | Which part of the summary to return (default `overview`) |
| `name` | string, at most 64 characters | The component to describe when `section` is `component` |

## Output

The generator's JSON: `ok`, `section`, `content`, `catalog_sha256`, `matrix_sha256` and `summary_version`. The summary is generated from `component-catalog.json` and `generator-support-matrix.json`; the two hashes identify the exact catalog it came from. A component that does not exist is a successful call with `ok: false` and the error code `COMPONENT_NOT_FOUND`.

## Rules

- The worker forwards only `section` and `name` to the generator.

- The call has no side effects and does not reach the network or an LLM.

- A response is at most 32 KB.
