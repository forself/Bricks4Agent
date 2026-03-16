# Generator Component Library Usage

## Status

Draft repository design note.

This document describes how the current generator stack actually consumes the
component library.

It is descriptive first. It is not a rule for all applications.

## Scope

This document is limited to the current generator-related code paths:

- `packages/javascript/browser/page-generator/PageGenerator.js`
- `packages/javascript/browser/page-generator/FieldResolver.js`
- `packages/javascript/browser/page-generator/DynamicFormRenderer.js`
- `packages/javascript/browser/page-generator/DynamicListRenderer.js`
- related generated examples under
  `packages/javascript/browser/page-generator/examples/generated`

## Key Observation

The component library already exposes `index.js` entrypoints at root, category,
and many leaf folders.

However, the current generator stack does not primarily consume those
entrypoints.

Instead, the generator stack is currently coupled to leaf implementation file
paths.

## Current Generator Consumption Patterns

### 1. Code generation path

`PageGenerator.js` emits imports through `ComponentPaths`.

Current emitted style:

- `@component-library/ui_components/form/DatePicker/DatePicker.js`
- `@component-library/ui_components/common/ColorPicker/ColorPicker.js`
- `@component-library/ui_components/layout/Panel/ToastPanel.js`
- `@component-library/ui_components/input/AddressInput/AddressInput.js`

This means:

- generated source code imports implementation files directly
- generated source code does not currently target component `index.js`
- generated source code does not currently target category `index.js`

### 2. Runtime dynamic resolution path

`FieldResolver.js` dynamically imports relative implementation file paths such
as:

- `../ui_components/form/TextInput/TextInput.js`
- `../ui_components/form/Dropdown/Dropdown.js`
- `../ui_components/common/ColorPicker/ColorPicker.js`
- `../ui_components/input/DateTimeInput/DateTimeInput.js`
- `../ui_components/viz/DrawingBoard/DrawingBoard.js`
- `../ui_components/utils/GeolocationService.js`

This path has the same shape as the code generation path:

- it also targets implementation files
- it also bypasses existing `index.js` entrypoints

### 3. Renderer support modules

Other generator runtime modules also bind directly to implementation files:

- `DynamicFormRenderer.js` imports
  `../ui_components/layout/FormRow/FormRow.js`
- `DynamicListRenderer.js` imports
  `../ui_components/form/SearchForm/SearchForm.js`
- `DynamicListRenderer.js` imports
  `../ui_components/layout/DataTable/DataTable.js`
- `DynamicListRenderer.js` imports
  `../ui_components/common/Pagination/Pagination.js`

## Current Dependency Reality

From the generator side, the current effective dependency contract is:

- host page base from app/runtime code such as `BasePage`
- component library implementation files
- utility/service files under `ui_components/utils`
- app/runtime helpers outside the component library

This is narrower than "all possible consumer imports", but it is also more
coupled than the component library's existing `index.js` surfaces.

## Mismatch With Library-Owned Surface

The generator and the library are not currently aligned at the same abstraction
level.

Current state:

- the library already exposes public entrypoints through `index.js`
- the generator mostly bypasses those entrypoints
- the generator therefore depends on file layout details more than necessary

This is not automatically wrong, but it is an explicit coupling choice.

## What This Document Is For

This document exists to keep three ideas separate:

- what the library itself exposes
- what the generator currently consumes
- what an application may or may not choose to do

Those three are related, but they are not the same thing.

## Current Practical Reading

The current generator stack can truthfully say:

- it uses a specific, real subset of the component library
- it currently imports that subset mostly by implementation file path
- this is a statement about the generator's own coupling, not a rule for all
  consumers

If the repository later chooses to reduce generator coupling, the proper target
would be to move generator imports toward library-owned entrypoints rather than
to impose import restrictions on applications in general.
