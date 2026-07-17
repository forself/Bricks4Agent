# Bricks4Agent — Claude Code Rules

## Project Identity

A zero-runtime-dependency Vanilla JS **UI component library** plus a **page/SPA generator**
that turns a JSON `PageDefinition` into working pages (static code generation or dynamic runtime rendering).

- Authoritative component list: [component-catalog.json](packages/javascript/browser/ui_components/metadata/component-catalog.json) (115 components)
- Component calling convention (read this before building): [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)

## Build & Test

- Page-generator tests: `npm test`
- UI library checks: `npm run validate:ui-library` (add `:browser` for a real browser)
- Style-token audit: `npm run audit:ui-styles`
- CSP + SVG-ratchet gate (must pass before any commit touching ui_components): `node tools/scripts/audit-csp.mjs`
- Browser smoke harnesses (need `python -m http.server 8124` at repo root; Edge via tim-web/poc playwright-core):
  `node tools/theme-studio/run.mjs`, `node tools/scripts/canvas-chart-smoke.mjs`,
  `node tools/scripts/wave2-stage-sweep.mjs`, `node tools/scripts/data-explorer-smoke.mjs`,
  `node tools/scripts/cluster-graph-perf.mjs`
- Generated .NET 8 backend (SPA template): `dotnet build templates/spa/backend/SpaApi.csproj`
- Rebuild component metadata after adding/changing a component:
  `node packages/javascript/browser/ui_components/metadata/build-metadata.mjs` (`--check` to validate only)

## Test Artifacts and Cleanup

**Rule: all test-generated files must be tracked and cleaned up.**

| Artifact | Source | Cleanup |
|----------|--------|---------|
| `.test-output/` | test output directory | delete after testing |
| generated pages/projects under `out/` (or your `--output`) | `spa-cli.js` / `page-gen.js` | delete after testing |

When adding tests that produce files: add the pattern to this table, ensure it is in `.gitignore`, and clean it up in the test.

## Code Conventions

- Frontend: JavaScript, ES modules, vanilla — **no third-party runtime UI dependency** (the only exceptions are vendored copies under `ui_components/vendor/`: Leaflet 1.9.4 and html2canvas, loaded locally first with CDN fallback only when the local file is missing).
- Styling: theme tokens only (`var(--cl-*)`); no hard-coded colors. Dark mode via `[data-theme="dark"]`, not per-component media queries. See [STYLE_CONVENTION.md](packages/javascript/browser/ui_components/STYLE_CONVENTION.md).
- Strict CSP (machine-enforced by `tools/scripts/audit-csp.mjs`): no `<style>` injection, no `style=`/`on*=` inside innerHTML templates, no eval / `javascript:` URLs. Style via CSSOM (`cssText`/`setProperty`) or a co-located `.css` + same-origin `<link>`.
- **SVG is banned — Canvas only** (ratchet on legacy files via `tools/scripts/svg-baseline.json`: new/increased SVG fails the audit). Chart base = `viz/CanvasChart.js` (DPR, hit-regions, tooltip, exportPNG); theme reactivity via `utils/theme-bus.js`; `Path2D` accepts SVG path strings directly. Canvas color fallbacks must use `FALLBACK_PAINT` from theme-bus — no loose hex in components.
- Security: escape all dynamic HTML with `escapeHtml()`; opt into raw HTML explicitly with `raw()` (see [utils/security.js](packages/javascript/browser/ui_components/utils/security.js)).
- i18n: user-facing strings go through `Locale.t()` (see [i18n/index.js](packages/javascript/browser/ui_components/i18n/index.js)).
- Component contract: `new X(options)` → `.mount(container)` → `.destroy()`; value components expose `getValue/setValue/setDisabled/clear` (form ones also `setError/clearError`).
- Generated backend: .NET 8 Minimal API, BaseOrm (lightweight, no EF Core).

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
</content>
