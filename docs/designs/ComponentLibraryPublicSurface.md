# Component Library Public Surface

## Status

Draft repository design note.

> **2026-06-15 更新**:依 [ComponentLibraryConsolidation-2026-06-15.md](ComponentLibraryConsolidation-2026-06-15.md),`ui_components`(本文件描述的這套)被定為 **canonical 組件庫**;產生器側的 C# schema 詞表(`HeroSection` 等罐頭)已被拆解吸收 —— 每個產生器型別綁定到本庫的閉集(`b_component`),`HeroSection` 已刪。
>
> **2026-06-16 落地(Stage 3 部分)**:① 決定性 —— 新增 `utils/uid.js`(`nextUid`/`resetUid`),`Notification`/`Tooltip`/`WebTextEditor`/`BatchUploader` 的實例 ID 去 `Math.random()`+`Date.now()` 化、可注入,並已併入 `utils` 公開面;② viz/editor/map 補測試(Vitest 168 全綠)。**但**本文件以下記錄的「公開面不齊」(viz/utils 直檔 vs 目錄 `index.js` 不一致)**尚未**全面正規化 —— 該完整盤點刻意延後為後續維護,故以下描述仍成立。

This document only describes what the component library can reasonably claim
about its own import surface.

It does not try to regulate how applications, generators, or agents must use
the library.

## Scope

This document is limited to:

- `packages/javascript/browser/ui_components`
- the import entrypoints that this directory itself exposes

This document is not a policy for:

- application architecture
- generator output shape
- agent behavior

Those concerns belong to other layers.

## Current Surface Model

From the current repository structure, the UI component library already exposes
three visible entrypoint layers plus a small set of exceptions.

### 1. Library root entrypoint

Present file:

- `packages/javascript/browser/ui_components/index.js`

Current role:

- category-level aggregation
- top-level import surface for consumers who want one library entrypoint

Current exports:

- `binding`
- `common`
- `data`
- `editor`
- `form`
- `layout`
- `input`
- `social`
- `utils`
- `viz`
- `Locale` from `i18n/index.js`

Important limitation:

- this root entrypoint still does not aggregate non-JS asset directories such as
  `themes` or `refresource`

## 2. Category entrypoints

Present files include:

- `packages/javascript/browser/ui_components/binding/index.js`
- `packages/javascript/browser/ui_components/common/index.js`
- `packages/javascript/browser/ui_components/data/index.js`
- `packages/javascript/browser/ui_components/editor/index.js`
- `packages/javascript/browser/ui_components/form/index.js`
- `packages/javascript/browser/ui_components/input/index.js`
- `packages/javascript/browser/ui_components/layout/index.js`
- `packages/javascript/browser/ui_components/social/index.js`
- `packages/javascript/browser/ui_components/utils/index.js`
- `packages/javascript/browser/ui_components/viz/index.js`

Current role:

- category-level public surface
- stable aggregation point for components in the same domain

Important limitation:

- category structure is not fully uniform
- `viz/index.js` mixes direct file exports such as `./BarChart.js` with nested
  folder exports such as `./DrawingBoard/index.js`
- `utils/index.js` is now present, but `utils` still exposes direct file modules
  alongside the category entrypoint

## 3. Leaf entrypoints

Many component folders expose their own `index.js`, for example:

- `packages/javascript/browser/ui_components/form/TextInput/index.js`
- `packages/javascript/browser/ui_components/common/ColorPicker/index.js`
- `packages/javascript/browser/ui_components/layout/Panel/index.js`
- `packages/javascript/browser/ui_components/input/AddressInput/index.js`
- `packages/javascript/browser/ui_components/viz/DrawingBoard/index.js`

Current role:

- component-local public surface
- preferred place for a component folder to re-export its implementation

Observed pattern:

- when a component has its own directory, an `index.js` usually re-exports the
  implementation file and may also expose a default export

## 4. File-level exceptions

Not all library-owned public modules currently use the same pattern.

The current repository also exposes some direct file modules as public modules
in parallel with category entrypoints:

- `packages/javascript/browser/ui_components/utils/GeolocationService.js`
- `packages/javascript/browser/ui_components/utils/WeatherService.js`
- `packages/javascript/browser/ui_components/utils/security.js`
- `packages/javascript/browser/ui_components/utils/SimpleZip.js`

Also, parts of `viz` are exported directly from files at category root:

- `packages/javascript/browser/ui_components/viz/BarChart.js`
- `packages/javascript/browser/ui_components/viz/LineChart.js`
- `packages/javascript/browser/ui_components/viz/PieChart.js`
- `packages/javascript/browser/ui_components/viz/RoseChart.js`
- and related root-level visualization files exported by
  `packages/javascript/browser/ui_components/viz/index.js`

## What The Library Can Actually Promise

Based on the current structure, the library can make these claims about itself:

- it owns the entrypoint files under `ui_components/**/index.js`
- it owns direct public modules that are intentionally exposed without an
  `index.js`, such as the current `utils` files and some `viz` files
- it can document which of those surfaces are preferred for consumers
- it can change internal implementation files as long as the chosen public
  entrypoints remain stable

For library-owned code, that also means:

- composite components should prefer public entrypoints when they compose other
  components from the library
- when a child component has a folder-level `index.js`, library-owned composite
  code should prefer that leaf entrypoint over the child implementation file
- this is a rule about library maintenance inside `ui_components`, not a rule
  imposed on external applications

## What The Library Should Not Claim

The library should not claim:

- how all applications must import it
- how generators must compose pages
- how agents must choose between generators and handwritten code

Those are consumer-layer or orchestration-layer decisions.

## Current Practical Reading

The current repository does not have one perfectly normalized public import
surface.

Instead, it has:

- a real root entrypoint
- real category entrypoints
- many real leaf entrypoints
- a few direct file exceptions

That is enough to define a truthful contract:

- the library already has public surfaces
- those surfaces are currently uneven
- normalizing them is a library maintenance task
- library-owned composite components should move toward those same public
  surfaces instead of cross-component deep imports
- consumer behavior should be described separately from the library contract
