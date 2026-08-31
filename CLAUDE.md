# Bricks4Agent — Claude Code Rules

## Project Identity

A zero-runtime-dependency Vanilla JS **UI component library** plus a **page/SPA generator**
that turns a JSON `PageDefinition` into working pages (static code generation or dynamic runtime rendering).

- Authoritative component list: [component-catalog.json](packages/javascript/browser/ui_components/metadata/component-catalog.json) (116 components)

- Component calling convention (read this before building): [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)

## Build & Test

- Page-generator tests: `npm test`

- UI library checks: `npm run validate:ui-library` (add `:browser` for a real browser)

- Style-token audit: `npm run audit:ui-styles`

- All SDK-style .NET 10 projects, with every warning treated as an error: `npm run test:dotnet10`

- .NET tests: `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj`, `dotnet test packages/csharp/tests/integration/Integration.Tests.csproj`, `dotnet test templates/spa/backend.Tests/SpaApi.Template.Tests.csproj`

- CSP + SVG hard-zero gate (must pass before any commit touching ui_components): `node tools/scripts/audit-csp.mjs`

- Browser smoke harnesses (need `python -m http.server 8124` at repo root; Edge via tim-web/poc playwright-core):
 `node tools/theme-studio/run.mjs`, `node tools/scripts/canvas-chart-smoke.mjs`,
 `node tools/scripts/wave2-stage-sweep.mjs`, `node tools/scripts/data-explorer-smoke.mjs`,
 `node tools/scripts/cluster-graph-perf.mjs`

- Generated .NET 10 backend (SPA template): `dotnet build templates/spa/backend/SpaApi.csproj`

- Rebuild component metadata after adding/changing a component:
 `node packages/javascript/browser/ui_components/metadata/build-metadata.mjs` (`--check` to validate only)

- Generate pages from a PageDefinition: `node tools/page-gen.js --def page.json --mode static --output ./out/`.
 For a DefinitionTemplate: `--page <id>` for one page, or `--pages <id,id,...>` / `--all` to generate many in a single process (aggregated JSON result; all selected pages are validated before any is written).

## Test Artifacts and Cleanup

**Rule: all test-generated files must be tracked and cleaned up.**

| Artifact | Source | Cleanup |
|---|---|---|
| `.test-output/` | test output directory | delete after testing |
| generated pages/projects under `out/` (or your `--output`) | `spa-cli.js` / `page-gen.js` | delete after testing |

When adding tests that produce files: add the pattern to this table, ensure it is in `.gitignore`, and clean it up in the test.

## Code Conventions

- Frontend: JavaScript, ES modules, vanilla — **no third-party runtime UI dependency** (the only exceptions are vendored copies under `ui_components/vendor/`: Leaflet 1.9.4 and html2canvas, loaded locally first with CDN fallback only when the local file is missing).

- Styling: theme tokens only (`var(--cl-*)`); no hard-coded colors. Dark mode via `[data-theme="dark"]`, not per-component media queries. See [STYLE_CONVENTION.md](packages/javascript/browser/ui_components/STYLE_CONVENTION.md).

- Strict CSP (machine-enforced by `tools/scripts/audit-csp.mjs`): no `<style>` injection, no `style=`/`on*=` inside innerHTML templates, no eval / `javascript:` URLs. Style via CSSOM (`cssText`/`setProperty`) or a co-located `.css` + same-origin `<link>`.

- **SVG is banned — Canvas only**. `audit-csp.mjs` enforces hard zero; `tools/scripts/svg-baseline.json` is an empty inventory snapshot and cannot waive findings. Chart base = `viz/CanvasChart.js` (DPR, hit-regions, tooltip, exportPNG); theme reactivity via `utils/theme-bus.js`; `Path2D` accepts SVG path strings directly. Canvas color fallbacks must use `FALLBACK_PAINT` from theme-bus — no loose hex in components.

- Security: escape all dynamic HTML with `escapeHtml()`; opt into raw HTML explicitly with `raw()` (see [utils/security.js](packages/javascript/browser/ui_components/utils/security.js)). `isRawHtml()` only accepts the `Symbol.for('bricks4agent.rawHtml')` own property that `raw()` brands (a plain `{ __html }` logs a one-time console warning naming `raw()` rather than failing silently) — a hand-written or `JSON.parse`d `{ __html }` is no longer an opt-in and falls back to escaping/`sanitizeHTML()`, so API data cannot forge it. `__html` survives on the marker for readers, not as authorisation.

- i18n: user-facing strings go through `Locale.t()` (see [i18n/index.js](packages/javascript/browser/ui_components/i18n/index.js)).

- Component contract: `new X(options)` → `.mount(container)` → `.destroy()`; value components expose `getValue/setValue/setDisabled/clear` (form ones also `setError/clearError`). `destroy()` is mandatory, not optional. `ModalPanel.confirm/alert/prompt` pass `destroyOnClose: true` so the dialog self-destructs after `close()`; a direct `new ModalPanel` keeps `destroyOnClose: false` and stays reusable across `close()`/`open()`.

- Generator definitions: `definition.name`, `field.name` and `behaviors.*` (`onInit`/`onSave`/`onDelete`/`fieldTriggers` values, all handler-method names) are emitted as bare JavaScript identifiers, so `PageGenerator` validates them as real `IdentifierName` — CJK names such as `姓名` pass; reserved words are rejected only in binding positions (`definition.name`). Failures come back through the normal `{ code: null, errors: [...] }` contract.

- Tool pages (`type: 'tool'` / `DynamicToolRenderer`) default to [LazyComponentFactory](packages/javascript/browser/ui_components/binding/LazyComponentFactory.js), which loads only the components a definition names: `create()` stays synchronous, `preload(names)` is awaited inside `init()`. The eager `ComponentFactory` is unchanged for direct callers.

- Detail pages default to `lazyTabs: true` (`DynamicPageRenderer` / `DynamicDetailRenderer`): a tab's content is built on first activation, so components in never-activated tabs do not exist yet. Pass `lazyTabs: false` to build everything up front. An unpopulated panel carries `data-tab-pending="true"` until first activation (removed once populated), so DOM inspection can tell "not built yet" from "definition is wrong".

- Generated backend: .NET 10 Minimal API, BaseOrm (lightweight, no EF Core).

- Password hashing: use the static `Rfc2898DeriveBytes.Pbkdf2` API without changing the existing iteration/salt/hash sizes or stored formats; compatibility vectors live in the unit and SPA template test projects.

## Adding a Component

1. Create `ui_components/<category>/<Name>/` with `<Name>.js`, `index.js`, and `<Name>.manifest.json` (schema: [manifest-schema.js](packages/javascript/browser/ui_components/metadata/manifest-schema.js)).

2. Export it from the category `index.js`.

3. Register it in [ComponentFactory.js](packages/javascript/browser/ui_components/binding/ComponentFactory.js) if it should be usable by name/generator.

4. Rebuild metadata: `node packages/javascript/browser/ui_components/metadata/build-metadata.mjs --check`.

Full details: [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) §8.

## Documentation

- Component calling convention + React-rewrite playbook: [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)

- SPA generator manual: [AGENT.md](AGENT.md)

- Page generator: [page-generator/README.md](packages/javascript/browser/page-generator/README.md)
