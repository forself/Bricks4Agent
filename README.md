# Bricks4Agent

中文版本：[README.zh-TW.md](README.zh-TW.md)

## What this is

`Bricks4Agent` is a **zero-runtime-dependency Vanilla JS UI component library** plus a
**page/SPA generator** that turns a JSON `PageDefinition` into working pages.

- **UI component library** — 116 components (form, layout, common, input, viz, social, editor, sections, data, analytics), pure vanilla JS, theme-token styling, built-in XSS protection and i18n.
- **Page generator** — a `PageDefinition` (JSON) becomes a page in one of two ways: **static code generation** (emits `.js` page files) or **dynamic rendering** (renders at runtime from the JSON).
- **SPA tooling** — a CLI and a Web UI that scaffold full-stack CRUD (frontend pages + optional .NET 10 backend).
- **Form application studio** — imports a table schema, visually arranges fields, and generates a form `PageDefinition`, .NET 10 Minimal API/BaseOrm code, and database SQL. A blank connection string targets local SQLite.

> Building on top of this library? Read [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) first — it is the calling-convention entry point for both humans and AI agents.

## Main areas

### UI component library
- [ui_components](packages/javascript/browser/ui_components) — the components
- [ui_components/index.js](packages/javascript/browser/ui_components/index.js) — single import barrel
- [metadata/component-catalog.json](packages/javascript/browser/ui_components/metadata/component-catalog.json) — the authoritative component list
- [STYLE_CONVENTION.md](packages/javascript/browser/ui_components/STYLE_CONVENTION.md) — theming / token rules

### Page generator
- [page-generator](packages/javascript/browser/page-generator) — engine (static + dynamic)
- [page-generator/README.md](packages/javascript/browser/page-generator/README.md)

### SPA scaffolding
- [templates/spa](templates/spa) — SPA project template (frontend core + .NET 10 backend)
- [templates/spa/scripts](templates/spa/scripts) — `spa-cli.js`, `generate-page.js`, `generate-api.js`
- [tools/spa-generator](tools/spa-generator) — generator Web UI (port 3080)
- [tools/page-gen.js](tools/page-gen.js) — standalone PageDefinition CLI ([docs](tools/page-gen.README.md))
- [tools/static-server](tools/static-server) — static file server for previewing
- [tools/form-application-studio](tools/form-application-studio) — JSON-self-hosted form/API/database designer

## Quick start

Use Node.js 22 and the .NET 10 SDK. The checked-in `global.json` accepts the latest installed .NET 10 feature band.

### Use the component library

```js
import { TextInput, DataTable, BarChart } from './packages/javascript/browser/ui_components/index.js';

new TextInput({ label: 'Name', required: true }).mount('#app');
```

Every component follows the same contract: `new X(options)` → `.mount(container)` → `.destroy()`.
See [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) for the full convention and the component inventory.

### Generate a page from a definition

```bash
# validate / generate / list supported field types
node tools/page-gen.js --def page.json --mode static --output ./out/
node tools/page-gen.js --list-types
```

### Scaffold full-stack CRUD

```bash
# create a project, then generate a feature (frontend pages + C# Model/Service/API)
node templates/spa/scripts/spa-cli.js new --name my-app --output ./out
node templates/spa/scripts/spa-cli.js feature Article --fields "Title:string,Content:text,IsPublic:bool"
```

### Run the generator Web UI

```bash
npm run serve   # serves tools/spa-generator/frontend on port 3080
```

## Tests

```bash
npm test                        # page-generator test suite
npm run validate:ui-library     # UI library checks
npm run audit:ui-styles         # style-token audit
npm run test:studio:self-host   # one authoritative JSON + component provenance
npm run test:studio:browser     # same-page tabs + Theme/Custom JSON round-trip
npm run test:form-designer:all  # form application unit, self-host and browser acceptance
npm run test:form-designer:dotnet # generate and compile all four provider backends
npm run test:dotnet10           # enforce net10.0 and build all 35 SDK-style projects
```

Pull requests targeting `main` and pushes to `main` run the portable JavaScript, policy,
metadata, all .NET 10 projects and generated-backend checks through
[GitHub Actions](.github/workflows/ci.yml). The real Edge interaction harness remains
a local acceptance gate because it intentionally uses the repository's pre-existing
external Playwright/Edge runtime instead of adding npm dependencies.

## Documentation

- [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) — component calling convention + React-rewrite playbook (for AI agents)
- [CUSTOM-COMPONENTS.md](CUSTOM-COMPONENTS.md) — JSON-generated self-host Studio, three-tier custom components and folder loading
- [tools/form-application-studio/README.md](tools/form-application-studio/README.md) — schema-to-form/API/database designer and connection policy
- [AGENT.md](AGENT.md) — SPA generator operation manual (for AI agents)
- [CLAUDE.md](CLAUDE.md) — Claude Code rules for this repo
- [page-generator/README.md](packages/javascript/browser/page-generator/README.md) — page generator details
- [templates/spa/README.md](templates/spa/README.md) — SPA template
</content>
