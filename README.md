# Bricks4Agent

中文版本：[README.zh-TW.md](README.zh-TW.md)

## What this is

The end state of `Bricks4Agent` is an **AI agent service**: an agent takes requests from human users or from other AI agents and generates systems, pages and features from this component library. The repo holds both the service and its building blocks:

- **AI agent service** — the agent runtime, a broker that issues scoped sessions, enforces capability and scope policy and proxies model traffic, and the workers it dispatches. The agent container reaches only the broker and holds no provider API key.

The building blocks are a **zero-runtime-dependency Vanilla JS UI component library** plus a
**page/SPA generator** that turns a JSON `PageDefinition` into working pages.

- **UI component library** — 134 components (form, layout, common, input, viz, social, editor, sections, data, analytics), pure vanilla JS, theme-token styling, built-in XSS protection and i18n.

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

### AI agent service

- [tools/agent](tools/agent/README.md) — agent runtime (local provider, generation pipeline and broker-governed modes)

- [tools/agent/container](tools/agent/container/README.md) — Podman stack for the governed path: broker, agent, workers and mock model upstreams

- [packages/csharp/broker](packages/csharp/broker) and [packages/csharp/broker-core](packages/csharp/broker-core) — scoped sessions, capability and scope policy, model proxy

- [packages/csharp/workers](packages/csharp/workers) — workers the broker dispatches (file, LINE, execution adapter)

- Validation: `npm run validate:agent-container-config`, `npm run validate:podman-governed-stack`

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
# generate one page / every page of a DefinitionTemplate / list supported field types
node tools/page-gen.js --def page.json --mode static --output ./out/
node tools/page-gen.js --def site-definition.json --all --mode static --output ./out/
node tools/page-gen.js --list-types
```

`--validate` checks a definition without writing files; `--page <id>` and `--pages <id,id>`
pick individual pages out of a DefinitionTemplate.

### Scaffold full-stack CRUD

```bash
# create a project — interactive; --name/--output only pre-fill the prompts
node templates/spa/scripts/spa-cli.js new --name my-app --output ./out

# non-interactive: answer everything up front (see scripts/project-config.example.json)
node templates/spa/scripts/create-project.js --config project.json

# generate a feature (C# Model/Service + frontend pages) into templates/spa itself
node templates/spa/scripts/spa-cli.js feature Article --fields "Title:string,Content:text,IsPublic:bool"
```

`feature`, `page` and `api` write relative to `templates/spa/`, not into the project
`new` just created — they edit the template tree that `new` copies from.

### Run the generator Web UI

```bash
node tools/spa-generator/server.js   # frontend + generation API on port 3080
npm run serve                        # static frontend only, also on port 3080
```

The Node server binds loopback and rejects non-loopback `Host`/`Origin` requests.

## Tests

```bash
npm --prefix packages/javascript/browser install  # once: vitest/jsdom devDependencies needed by npm test
npm test                        # generator examples + test:ui-components + test:custom-components + Vitest component suites
npm run validate:ui-library     # UI library checks
npm run audit:ui-styles         # style-token audit
npm run test:studio:self-host   # one authoritative JSON + component provenance
npm run test:studio:browser     # same-page tabs + Theme/Custom JSON round-trip
npm run test:form-designer:all  # form application unit, self-host and browser acceptance
npm run test:form-designer:dotnet # generate and compile all four provider backends
npm run test:dotnet10           # enforce net10.0; build all 35 projects with every warning as an error
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj
dotnet test templates/spa/backend.Tests/SpaApi.Template.Tests.csproj
```

Pull requests targeting `main` and pushes to `main` run the portable JavaScript, policy,
metadata, warning-free .NET 10 project matrix and generated-backend checks through
[GitHub Actions](.github/workflows/ci.yml). [`quality-gates.yml`](.github/workflows/quality-gates.yml)
also installs `playwright-core` with `--no-save` and runs six self-hosted smokes in system Edge
(`validate:ui-library:browser`, `test:spa-template:browser`, `test:theme-studio:browser`,
`test:custom-components:browser`, `test:studio:browser`, `test:form-designer:browser`).
Only the harnesses that need a `python -m http.server 8124` server (canvas chart, wave 2/3,
data explorer, cluster graph, icon) remain local-only.

The .NET 10 migration preserves the existing PBKDF2 password storage formats. Fixed
compatibility vectors cover Broker, MFA and the SPA template, so existing hashes remain
verifiable while new builds use the current static PBKDF2 API.

## Documentation

- [AGENT-UI-GUIDE.md](AGENT-UI-GUIDE.md) — component calling convention + React-rewrite playbook (for AI agents)

- [CUSTOM-COMPONENTS.md](CUSTOM-COMPONENTS.md) — JSON-generated self-host Studio, three-tier custom components and folder loading

- [tools/form-application-studio/README.md](tools/form-application-studio/README.md) — schema-to-form/API/database designer and connection policy

- [AGENT.md](AGENT.md) — SPA generator operation manual (for AI agents)

- [CLAUDE.md](CLAUDE.md) — Claude Code rules for this repo

- [page-generator/README.md](packages/javascript/browser/page-generator/README.md) — page generator details

- [templates/spa/README.md](templates/spa/README.md) — SPA template
