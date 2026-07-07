# Generator Component Library Usage

## Status

Draft repository design note.

> **2026-06-15 規劃**:見 [ComponentLibraryConsolidation-2026-06-15.md](ComponentLibraryConsolidation-2026-06-15.md)。
>
> **2026-06-16 落地(實況,含具名偏離)**:整併 Stage 0–3 已進 main。對 **C# site-crawler 產生器**:
> - `SiteGeneratorConverter.EnsureGeneratedComponent` 的「現捏組件」死碼整串移除(Stage 0)。
> - 詞彙錨定 B:每個產生器型別宣告 `b_component` 綁定到 `ui_components` 閉集(`BComponentRegistry`),manifest 載入時 fail-closed 驗證;刪除死行話 `HeroSection`;`TemplateMatcher` 移除任意 `.First()` 退路、改退指定中性容器並一律記錄缺口(Stage 1)。
> - 靜態包顯式錨定 B:`components/manifest.json` 帶 `b_component`、新增 `components/b-binding.json`(`type→b_component` 機讀索引)、README 宣告 B canonical(Stage 2)。
> - **具名偏離**:`StaticSitePackageGenerator` 的內嵌 JS renderer **未退役**,刻意保留為「B 詞彙的位元組決定性靜態匯出投影」(把 B 的即時 FSM 組件硬塞進位元組穩定的靜態匯出是退步;理由見整併文件 §9)。
> - e2e 驗證:本地 `reconstruct` CLI(`site-crawler-worker` 免 broker 子命令)對台北科大實跑,並抓出 `TemplateCompiler.CloneManifest` 漏帶 `b_component` 的 bug(已修 + 回歸測試,見整併文件 §10)。
>
> 注意:**本文件描述的是 JS `page-generator` 對實作檔路徑的耦合**(繞過 `index.js`),與上述 C# site-crawler 是不同的產生器。該 JS 耦合的 `index.js` 公開面正規化(整併規劃 Stage 3 的一部分)**尚未**進行,本文件以下描述仍成立。

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
