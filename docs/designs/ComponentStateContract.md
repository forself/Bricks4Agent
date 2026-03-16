# Component State Contract

## Status

Draft repository design note.

This document describes how `ui_components` may evolve toward explicit
state-machine-based components without overreaching into generator, app, or
agent policy.

## Scope

This document applies only to:

- `packages/javascript/browser/ui_components`
- the internal implementation model of components owned by the library

This document does not define:

- how applications must use components
- how generators must emit component calls
- how agents must invoke components

Those concerns belong to other layers.

## Core Position

Every component is treated as a state machine.

That does not mean every component must use the same heavyweight FSM pattern.
It means each component must have:

- a current state
- explicit transitions
- render output that is a projection of that state

The main difference between components is complexity:

- some components are mostly externally driven
- some components are internally interactive
- some components orchestrate child component state

## Non-Negotiable Constraints

### 1. State-machine work must not reduce component self-containment

Component state machines must remain an internal implementation detail of the
component or of library-owned local helpers.

The library must not require consumers to understand external transition schema
or external machine registries just to use a component.

Accepted:

- component-local transitions
- library-owned shared state helpers
- same-folder helpers when they reduce duplication without hiding behavior

Not accepted:

- consumer-supplied machine schema as a requirement for normal use
- remote registries or app-owned machine orchestration as a prerequisite
- thin wrapper components whose real behavior lives somewhere else

### 2. Early phases must preserve generator compatibility

For the migration waves that touch generator-facing components, existing public
methods remain the compatibility layer.

At minimum, migrated field components must keep working through the current
generator/runtime call surface:

- `mount`
- `destroy`
- `getValue`
- `setValue`
- `setDisabled`
- `clear`

New state-machine methods such as `snapshot()` and `send()` may be added, but
the generator must not need to know them in the early phases.

### 3. State-machine work must not increase required external knowledge

If a component becomes harder to use because consumers must learn machine
internals that were previously irrelevant, the migration has failed.

State-machine structure is allowed to increase internal rigor. It is not
allowed to push that complexity outward without a separate decision by the
generator or application layer.

## Shared State Vocabulary

All migrated components should use the smallest honest subset of these state
regions:

- `lifecycle`: `created | mounted | destroyed`
- `visibility`: `visible | hidden`
- `availability`: `enabled | disabled`
- `interaction`: such as `idle | focused | open | active`
- `value`
- `validation`
- `async`

Not every component needs every region.

## Required Public Contract For Migrated Components

Each migrated component should expose:

- `snapshot()`
- `send(event, payload?)`

But legacy public methods remain valid and should map to those transitions.

Examples:

- `setValue(value)` -> `send('SET_VALUE', { value })`
- `setDisabled(true)` -> `send('SET_DISABLED', { disabled: true })`
- `clear()` -> `send('CLEAR')`
- `mount(container)` -> append DOM + `send('MOUNT')`

## Migration Strategy

### Phase 0

- define the contract
- add a minimal library-owned state helper
- add baseline tests for the helper and pilot components

### Phase 1

Pilot components:

- one simple display component
- one generator-facing field component

Current pilot choice:

- `Badge`
- `TextInput`

### Phase 2

Simple field components:

- `NumberInput`
- `Checkbox`

### Phase 3

Interactive field components:

- `Dropdown`
- `DatePicker`
- `TimePicker`
- `MultiSelectDropdown`

### Phase 4

Composite orchestrators:

- `ChainedInput`
- `DateTimeInput`
- `ListInput`
- related composite inputs

### Phase 5

High-complexity async/editor components:

- `BatchUploader`
- `WebTextEditor`
- `WebPainter`
- `OSMMapEditor`

## Checkpoints

Every phase must keep these gates green:

- existing UI library validation
- explicit state tests for newly migrated components
- legacy API compatibility checks for generator-facing components

If a phase needs generator changes just to keep current behavior working, it is
too early or too broad.
