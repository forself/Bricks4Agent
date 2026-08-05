# Bricks4Agent — Agent Rules

## Project Identity

A zero-runtime-dependency Vanilla JS **UI component library** plus a **page/SPA generator**
that turns a JSON `PageDefinition` into working pages (static code generation or dynamic runtime rendering).

- Authoritative component list: [component-catalog.json](packages/javascript/browser/ui_components/metadata/component-catalog.json) (116 components)
- Read before building: [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) (calling convention) and [AGENT.md](AGENT.md) (SPA generator manual)

## Build & Test

- Page-generator tests: `npm test`
- UI library checks: `npm run validate:ui-library`
- Style-token audit: `npm run audit:ui-styles`
- All SDK-style .NET 10 projects, with every warning treated as an error: `npm run test:dotnet10`
- .NET tests: `dotnet test packages/csharp/tests/unit/Unit.Tests.csproj`, `dotnet test packages/csharp/tests/integration/Integration.Tests.csproj`, `dotnet test templates/spa/backend.Tests/SpaApi.Template.Tests.csproj`
- CSP + SVG hard-zero gate (must pass before any commit touching ui_components): `node tools/scripts/audit-csp.mjs`
- Browser smoke harnesses (serve repo root on :8124 first): `node tools/theme-studio/run.mjs`, `node tools/scripts/canvas-chart-smoke.mjs`, `node tools/scripts/wave2-stage-sweep.mjs`, `node tools/scripts/data-explorer-smoke.mjs`, `node tools/scripts/cluster-graph-perf.mjs`
- Generated .NET 10 backend (SPA template): `dotnet build templates/spa/backend/SpaApi.csproj`
- Rebuild component metadata after changing a component:
  `node packages/javascript/browser/ui_components/metadata/build-metadata.mjs` (`--check` to validate only)

## Test Artifacts and Cleanup

**Rule: all test-generated files must be tracked and cleaned up.**

| Artifact | Source | Cleanup |
|----------|--------|---------|
| `.test-output/` | test output directory | delete after testing |
| generated pages/projects under `out/` (or your `--output`) | `spa-cli.js` / `page-gen.js` | delete after testing |

## Code Conventions

- Frontend: JavaScript, ES modules, vanilla — **no third-party runtime UI dependency** (only exceptions: vendored Leaflet 1.9.4 + html2canvas under `ui_components/vendor/`, local-first with CDN fallback).
- Styling: theme tokens only (`var(--cl-*)`); dark mode via `[data-theme="dark"]`. See [STYLE_CONVENTION.md](packages/javascript/browser/ui_components/STYLE_CONVENTION.md).
- Strict CSP (machine-enforced): no `<style>` injection, no `style=`/`on*=` in innerHTML templates, no eval / `javascript:`. Style via CSSOM or co-located `.css` + same-origin `<link>`.
- **SVG banned — Canvas only**. `audit-csp.mjs` enforces hard zero; `tools/scripts/svg-baseline.json` is an empty inventory snapshot and cannot waive findings. Chart base `viz/CanvasChart.js`; theme reactivity `utils/theme-bus.js`; color fallbacks only via theme-bus `FALLBACK_PAINT` (no loose hex).
- Security: escape dynamic HTML with `escapeHtml()`; raw HTML only via explicit `raw()`.
- i18n: user-facing strings via `Locale.t()`.
- Component contract: `new X(options)` → `.mount(container)` → `.destroy()`.
- Generated backend: .NET 10 Minimal API, BaseOrm (no EF Core).
- Password hashing: use the static `Rfc2898DeriveBytes.Pbkdf2` API without changing the existing iteration/salt/hash sizes or stored formats; compatibility vectors live in the unit and SPA template test projects.

## Key Notes

- The generator has two paths: **static** (`PageGenerator.generate()` / `tools/page-gen.js` emit `.js` files) and **dynamic** (`DynamicPageRenderer` renders at runtime from JSON).
- Field types map to components via `PageDefinition.ComponentMapping` and `FieldResolver`; field interactions via `TriggerEngine`.
- To add a missing component, follow [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) §8, then rebuild metadata.

## Documentation

- Calling convention + React-rewrite playbook: [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)
- JSON custom components + Studio + folder/runtime contract: [CUSTOM-COMPONENTS.md](CUSTOM-COMPONENTS.md)
- SPA generator manual: [AGENT.md](AGENT.md)
- Page generator: [page-generator/README.md](packages/javascript/browser/page-generator/README.md)
