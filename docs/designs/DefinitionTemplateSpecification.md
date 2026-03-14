# DefinitionTemplate Specification v0.1

## Status

This document is the normative specification for generator-native definition documents in this repository.

It replaces the previous draft-level framing that treated page-level and app-level definition work as loosely parallel concepts.

## Scope

This specification defines:

1. the canonical root format for generator-native definition documents
2. the structural boundary between page semantics and app composition semantics
3. identity, reference, compatibility, and prohibition rules
4. the minimum common semantic intersection that must be representable before broader equivalence is attempted

This specification does not define:

1. full broker-equivalent expressiveness
2. arbitrary handwritten source-code round-trip
3. automatic reverse extraction
4. capability registry resolution rules beyond the structural placeholders already present in the schema

Version `0.1` is intentionally scoped to the shared semantic intersection across:

- website-style applications
- richer generated website examples
- control-plane-like applications only where their semantics intersect with the generator's common model

## Normative Language

The keywords below are normative:

- `MUST`: mandatory
- `MUST NOT`: prohibited
- `SHOULD`: recommended unless there is a justified reason not to do so
- `MAY`: optional

## Canonical Rule

`DefinitionTemplate` is the only canonical root definition format.

Generated `.js`, `.cs`, configuration files, project files, and other emitted artifacts are implementation outputs, not canonical sources.

A generator-native feature is not complete unless it can be:

1. represented in the canonical definition format
2. generated from that definition
3. described back into that definition at semantic level

## Root Object

A conforming root document MUST have this shape:

```json
{
  "kind": "definition-template",
  "version": "0.1.0",
  "definitions": {
    "pages": [],
    "apps": []
  }
}
```

### Root Rules

The root object:

- MUST contain `kind`
- MUST contain `version`
- MUST contain `definitions`
- MAY contain `meta`
- MUST NOT contain feature-specific payloads outside `definitions`

`kind` MUST equal `definition-template`.

`version` identifies the definition format version, not the generated application's product version.

## First-Layer Child Collections

The `definitions` object is the only place where canonical definition content may live at root level.

In version `0.1`, the only first-layer child collections are:

- `definitions.pages`
- `definitions.apps`

No additional peer collection is permitted in `0.1`.

## Page Nodes

Each entry in `definitions.pages` MUST have:

- `id`
- `definition`

The canonical page node shape is:

```json
{
  "id": "products-list",
  "definition": {
    "...": "PageDefinition payload"
  }
}
```

### Page Node Rules

- `id` MUST be stable within the document.
- `id` MUST be unique within `definitions.pages`.
- `definition` MUST follow the current page-definition semantics used by the page generator.
- `definition.type` in `0.1` MUST remain within the currently supported page types:
  - `form`
  - `list`
  - `detail`
  - `dashboard`

For version `0.1`, page payload semantics are defined by the existing page-definition contract implemented in:

- `D:\Bricks4Agent\packages\javascript\browser\page-generator\PageDefinition.js`

This means `DefinitionTemplate` MUST reuse existing page semantics instead of inventing a second page DSL.

## App Nodes

Each entry in `definitions.apps` MUST have:

- `id`
- `app`

The app node is the canonical location for application composition semantics.

### Minimum App Model

In version `0.1`, an app node MUST use this logical shape:

- `app.identity`
- `app.profiles`
- `app.configuration`
- `app.frontend.pageRefs`
- `app.backend.features`
- `app.backend.services`
- `app.backend.policies`
- `app.backend.middleware`
- `app.backend.endpointModules`
- `app.backend.routeGroups`
- `app.backend.startupHooks`
- `app.backend.hosting`

This is the minimum common semantic intersection between the current website generator baseline and the broader application composition patterns already present elsewhere in the repository.

### App Node Rules

- `definitions.apps[*].id` MUST be unique within `definitions.apps`.
- `app.frontend.pageRefs` MUST reference `definitions.pages[*].id` values in the same canonical document.
- `app.frontend` MUST NOT redefine page structure.
- `app.backend` MUST describe composition semantics, not freeform implementation code.
- A conforming `0.1` app node MAY omit `frontend` only for API-only applications.

## Node Identity Rules

The following node identifiers MUST be unique within their containing collection:

- `definitions.pages[*].id`
- `definitions.apps[*].id`
- `app.backend.features[*].id`
- `app.backend.services[*].id`
- `app.backend.policies[*].id`
- `app.backend.middleware[*].id`
- `app.backend.endpointModules[*].id`
- `app.backend.routeGroups[*].id`
- `app.backend.startupHooks[*].id`

Identifiers are semantic identities, not display labels.

Changing an identifier SHOULD be treated as a breaking semantic rename unless the normalizer or migration layer explicitly handles it.

## Reference Integrity Rules

The following references MUST resolve within the same canonical document in version `0.1`:

- `app.frontend.pageRefs[*] -> definitions.pages[*].id`
- `app.backend.routeGroups[*].moduleRefs[*] -> app.backend.endpointModules[*].id`
- `app.backend.routeGroups[*].policies[*] -> app.backend.policies[*].id`

Capability references and conditional expressions are not fully standardized in `0.1`.

Therefore:

- a document MAY contain structurally valid capability or condition fields
- a conforming generator MAY reject them unless the implementation has a documented resolver

## Prohibited Content

A canonical definition document MUST NOT embed arbitrary source code.

This prohibition includes:

- raw C# code fragments
- raw JavaScript code fragments
- raw lambda bodies
- raw SQL statements
- generator-internal temporary fields

Canonical definitions MUST describe semantics through:

- structured fields
- identifiers
- references
- ordering
- relationships
- declared options

## Conformance Model

### 1. Structural Conformance

A structurally conforming document MUST:

- satisfy `DefinitionTemplate.schema.json`
- satisfy all root and child-collection shape rules in this specification

### 2. Semantic Conformance

A semantically conforming document MUST:

- satisfy page-definition semantics for every page payload
- satisfy identifier uniqueness rules
- satisfy reference integrity rules
- avoid prohibited content

### 3. Generator Conformance

A conforming generator MUST:

- treat `DefinitionTemplate` as canonical input once normalized
- preserve page/app boundary rules
- reject invalid references rather than silently generating drifted artifacts

### 4. Reverse Description Conformance

A conforming reverse-description workflow MUST output semantic equivalents in the same root format.

Byte-for-byte source reconstruction is not required.

## Compatibility and Normalization

Backward compatibility is a transition mechanism, not a second canonical model.

In version `0.1`:

- legacy standalone page-definition inputs MAY still be accepted by tools
- legacy project payloads MAY still be accepted by tools
- but internal processing SHOULD normalize them into `DefinitionTemplate`

Canonical storage and canonical comparison SHOULD converge on `DefinitionTemplate`.

## Minimum Viable Proof for v0.1

The success condition for version `0.1` is not full equivalence with every current handwritten application.

The success condition is:

1. one canonical root format
2. no schema split between page and app roots
3. CLI and server normalization into the same root format
4. generation of the minimum common app semantic intersection
5. at least one generator-native sample manually described back into the same format

## Reserved for Later Versions

The topics below are reserved for later specifications:

- formal capability registry contract
- condition mini-DSL
- broker-level non-CRUD endpoint grammar beyond the common intersection
- automatic reverse extraction
- full transaction grammar
- complete data-layer strategy switching

## Normative Schema

The machine-readable companion for this specification is:

- `D:\Bricks4Agent\tools\spa-generator\schemas\DefinitionTemplate.schema.json`

If the schema and this specification conflict, this specification is authoritative.
