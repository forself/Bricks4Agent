# Bricks4Agent — Agent Rules

## Project Identity

A zero-runtime-dependency Vanilla JS **UI component library** plus a **page/SPA generator**
that turns a JSON `PageDefinition` into working pages (static code generation or dynamic runtime rendering).

- Authoritative component list: [component-catalog.json](packages/javascript/browser/ui_components/metadata/component-catalog.json) (86 components)
- Read before building: [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) (calling convention) and [AGENT.md](AGENT.md) (SPA generator manual)

## Build & Test

- Page-generator tests: `npm test`
- UI library checks: `npm run validate:ui-library`
- Style-token audit: `npm run audit:ui-styles`
- Generated .NET 8 backend (SPA template): `dotnet build templates/spa/backend/SpaApi.csproj`
- Rebuild component metadata after changing a component:
  `node packages/javascript/browser/ui_components/metadata/build-metadata.mjs` (`--check` to validate only)

## Test Artifacts and Cleanup

**Rule: all test-generated files must be tracked and cleaned up.**

| Artifact | Source | Cleanup |
|----------|--------|---------|
| `.test-output/` | test output directory | delete after testing |
| generated pages/projects under `out/` (or your `--output`) | `spa-cli.js` / `page-gen.js` | delete after testing |

## Code Conventions

- Frontend: JavaScript, ES modules, vanilla — **no third-party runtime UI dependency** (only exception: `LeafletMap`, CDN-loaded with fallback).
- Styling: theme tokens only (`var(--cl-*)`); dark mode via `[data-theme="dark"]`. See [STYLE_CONVENTION.md](packages/javascript/browser/ui_components/STYLE_CONVENTION.md).
- Security: escape dynamic HTML with `escapeHtml()`; raw HTML only via explicit `raw()`.
- i18n: user-facing strings via `Locale.t()`.
- Component contract: `new X(options)` → `.mount(container)` → `.destroy()`.
- Generated backend: .NET 8 Minimal API, BaseOrm (no EF Core).

## Key Notes

- The generator has two paths: **static** (`PageGenerator.generate()` / `tools/page-gen.js` emit `.js` files) and **dynamic** (`DynamicPageRenderer` renders at runtime from JSON).
- Field types map to components via `PageDefinition.ComponentMapping` and `FieldResolver`; field interactions via `TriggerEngine`.
- To add a missing component, follow [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) §8, then rebuild metadata.

## Documentation

- Calling convention + React-rewrite playbook: [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md)
- SPA generator manual: [AGENT.md](AGENT.md)
- Page generator: [page-generator/README.md](packages/javascript/browser/page-generator/README.md)
</content>
