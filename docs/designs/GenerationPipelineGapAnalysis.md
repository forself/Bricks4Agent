# Generation Pipeline Gap Analysis

## Status

This document captures the current convergence gaps between:

- deterministic generators
- DefinitionTemplate-based generation
- LLM-driven CRUD pipeline generation

It is based on the repository state as of 2026-03-15.

## Goal

The goal is not to replace the current architecture.

The goal is to make the existing architecture converge into one coherent
generation pipeline so that the same feature request produces structurally
consistent outputs regardless of whether it comes from:

- CLI
- SPA Generator Web UI
- Agent pipeline

## What Already Works

The current system already has several strong foundations:

- zero-dependency frontend and low-dependency backend/runtime profile
- shared templates and shared component/runtime building blocks
- CompletionContract guardrails around LLM-authored states
- PageDefinition and DefinitionTemplate as structured generation inputs

The main problem is not that the repository lacks generators.

The main problem is that multiple generator paths exist in parallel and the
highest-visibility CRUD path does not consistently flow through the canonical
structured representations.

## Gap 1: Backend CRUD Generation Is Split

### Current state

There are two materially different backend generation paths:

- `templates/spa/scripts/generate-api.js`
  - deterministic
  - template-driven
  - emits model, service, and endpoint snippets
- `tools/agent/lib/pipelines/crud-pipeline.js`
  - State 2 generates `backend/Data/AppDbContext.cs`
  - State 3 generates `backend/Services/<Entity>Service.cs`
  - State 4 edits `backend/Program.cs`
  - all three depend on model-authored code generation

### Impact

The same CRUD request can produce different code shape, naming, and integration
patterns depending on whether the user goes through the CLI or the Agent.

This is an architecture-level inconsistency, not just a style issue.

It weakens:

- predictability
- testability
- reviewability
- long-term maintenance

### Direction

The Agent SHOULD not re-author the backend CRUD layer when a deterministic
generator already exists.

Recommended target flow:

1. LLM extracts entity name, plural form, fields, and route intent
2. Agent validates that extracted structure
3. Agent invokes `generate-api.js`
4. CompletionContract validates the emitted artifacts and integration result

In that model, the LLM remains responsible for interpretation, but not for
reimplementing the deterministic CRUD scaffolding.

## Gap 2: Frontend Generation Has Three Competing Paths

### Current state

There are currently at least three frontend generation paths:

1. Agent CRUD State 5 writes page files directly
2. `templates/spa/scripts/generate-page.js` writes `BasePage` subclasses directly
3. `tools/page-gen.js` normalizes structured definition input and invokes
   `PageGenerator`

This means the repository has multiple frontend authoring modes that do not
share one canonical structured flow.

### Impact

This creates several problems:

- generated pages can bypass `PageDefinition` validation
- generated pages can bypass `PageGenerator` consistency
- different prompts and models can produce different page architecture
- the component library and field-type system are not guaranteed to be used

### Direction

The canonical frontend generation path SHOULD be structure-first.

Recommended target flow:

1. LLM emits structured page intent
2. that intent is normalized into `PageDefinition` or `DefinitionTemplate`
3. `tools/page-gen.js` or the equivalent runtime path performs generation
4. validators check both the definition and the emitted code

Agent State 5 SHOULD become a structured-definition authoring stage, not a raw
page-authoring stage.

## Gap 3: DefinitionTemplate Exists But Is Not Yet the Main CRUD Input

### Current state

`DefinitionTemplate` is already real and already consumed in some paths:

- `tools/page-gen.js`
- `tools/app-gen.js`
- `tools/spa-generator/server.js`

So the problem is not that `DefinitionTemplate` is unused.

The real gap is that it is not yet the canonical input for the main CRUD
generation path.

### Impact

The repository currently has a specification-capable structured format, but the
highest-traffic CRUD path can still bypass it.

That means:

- canonical comparison is harder
- generator equivalence is weaker
- structured validation is underused where it matters most

### Direction

CRUD generation SHOULD normalize requests into a canonical structured form
before code generation.

Recommended rule:

- `DefinitionTemplate` SHOULD be the canonical root input for generator-native
  flows
- direct handwritten source generation SHOULD be treated as a legacy or escape
  hatch path

This does not require the repository to delete current tools immediately.

It does require new feature work to converge on one canonical input model.

## Gap 4: Last-Mile Integration Is Still Manual

### Current state

`generate-api.js` still prints manual next steps for:

- table creation SQL in `AppDbContext.cs`
- service registration in `Program.cs`
- endpoint mapping insertion before `app.Run()`

This is currently the most fragile step in the deterministic flow.

### Impact

This manual gap creates:

- easy-to-miss integration errors
- inconsistent host composition
- lower confidence in generated output
- more copy-paste drift over time

### Direction

The deterministic generator SHOULD own the last-mile integration.

Recommended requirements:

- patch `AppDbContext.cs` idempotently
- patch `Program.cs` idempotently
- fail fast if expected anchors are missing
- emit a clear machine-readable report of what was inserted or updated

If anchor-based patching is not yet stable enough, the next best step is to add
explicit guarded region markers and patch those markers only.

## Gap 5: Lifetime And Connection Policy Are Inconsistent

### Current state

The current runtime policy is not fully aligned:

- template app registers `AppDb` as `Singleton`
- template app registers built-in services as `Scoped`
- SPA Generator backend registers `AppDb` as `Singleton`
- SPA Generator backend registers built-in services as `Singleton`
- `generate-api.js` prints `AddSingleton<IEntityService, EntityService>()`

At the same time, `BaseDb` keeps a cached `DbConnection` instance internally
instead of opening a fresh connection per operation.

### Impact

This creates two classes of risk:

1. architectural inconsistency
2. operational concurrency risk

The inconsistency makes it harder to reason about the intended hosting model.

The connection reuse model can become a real problem with SQLite under
concurrent access, especially once generated endpoints receive overlapping
requests.

### Direction

The repository SHOULD define one explicit runtime policy for:

- service lifetime
- database object lifetime
- connection lifetime
- transaction boundaries

Recommended target:

- align service lifetime guidance across template, generator, and docs
- document the current SQLite profile as demo-oriented if it remains unchanged
- refactor `BaseDb` toward per-operation connection scope or another
  concurrency-safe model before positioning the stack for production traffic

## Priority Roadmap

| Priority | Item | Reason |
| --- | --- | --- |
| P0 | Make Agent backend CRUD path call deterministic generator | removes LLM variability from the most repetitive code path |
| P0 | Make Agent frontend path emit structured definitions, then generate pages | converges frontend output and uses the existing field system |
| P1 | Automate `Program.cs` and `AppDbContext.cs` patching | removes the highest-friction manual step |
| P1 | Make `DefinitionTemplate` the canonical CRUD input | turns the spec layer into the real source of truth |
| P2 | Fix `BaseDb` connection lifecycle and align DI policy | required before stronger concurrency or production claims |

## Recommended Execution Order

1. Change CRUD pipeline States 2-4 to invoke deterministic backend generation
   instead of directly authoring CRUD code.
2. Replace CRUD pipeline State 5 with structured page-definition emission plus
   `tools/page-gen.js` invocation.
3. Extend deterministic generators to patch host files idempotently.
4. Add a normalization layer from CRUD intent to `DefinitionTemplate`.
5. Unify DI lifetime policy and refactor `BaseDb` connection handling.

## Non-Goals For This Phase

The following items are explicitly out of scope for the first convergence
phase:

- full reverse engineering of arbitrary handwritten code back into definitions
- removal of CompletionContract guardrails
- immediate package split across the entire repository
- elimination of all legacy entry points in one step

## Conclusion

The repository already contains the right building blocks:

- shared templates
- structured definitions
- deterministic generators
- runtime renderers
- validation guardrails

The key missing work is not invention.

It is convergence.

Once the CRUD path, frontend path, and definition path are connected into one
structured flow, the repository moves from "a set of strong tools" to "a
coherent generator platform".
