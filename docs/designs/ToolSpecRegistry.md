# Tool Spec Registry

## Goal

Make broker-registered tools document-driven.

The source of truth for a tool must not be adapter code alone.
Instead, each tool should have:

- machine-readable definition data
- human/model-readable explanation
- broker registration metadata
- capability bindings

## Current Scope

The current registry is a broker-owned file registry rooted at:

- `packages/csharp/broker/tool-specs`

Each tool lives in its own directory:

- `tool.json`
- `TOOL.md`

## Current Broker Behavior

The broker now:

- loads tool specs from the configured root
- registers them into an in-memory `IToolSpecRegistry`
- exposes list/get endpoints
- validates whether bound capability ids already exist in the capability catalog
- synchronizes active tool specs into broker capabilities at startup

This is intentionally only a registry layer.
Execution adapters still remain separate from the specs.

## Status Handling

Tool specs may exist before they are executable.

- `planned`: documented only, not synchronized into runtime capabilities
- `active`
- `ready`
- `beta`

Only `active`, `ready`, and `beta` specs are synchronized into broker capability records.

## Tool Directory Contract

Each tool directory is expected to contain:

### `tool.json`

Machine-readable definition, including:

- `tool_id`
- `display_name`
- `summary`
- `kind`
- `status`
- `version`
- `tags`
- `capability_bindings`
- `input_schema`
- `output_schema`
- `source_policy`
- `execution_rules`
- `response_contract`

### `TOOL.md`

Human/model-readable explanation, including:

- intended use
- status
- source constraints
- response rules
- cautions

## Design Principle

The registry defines:

- what a tool is
- how it should be requested
- what sources it may use
- how it should respond

The adapter defines only:

- how the tool is executed

So the broker should evolve from:

- code/seed-driven capabilities

toward:

- spec-driven tool registration
- capability generation or reconciliation from specs
- prompt exposure derived from specs
- execution adapters selected under broker control

## First Registered Specs

The initial spec set is:

- `web.search.google`
- `web.search.duckduckgo`
- `travel.flight.search`
- `travel.rail.search`
- `commerce.price.search`

At this stage, they are registered as documented tools, not yet executable adapters.

Right now:

- `web.search.google` is planned
- `web.search.duckduckgo` is active
- `travel.flight.search` is planned
- `travel.rail.search` is planned
- `commerce.price.search` is planned
