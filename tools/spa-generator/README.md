# SPA Generator (Web UI)

The Web UI for the page/SPA generator — a visual workbench for building `PageDefinition`s
and scaffolding CRUD projects.

It is a self-contained generator area with:

- a Vanilla JS frontend at `frontend/`
- a .NET 8 backend at `backend/`
- helper launchers such as `server.js`, `start.bat`, and `start.sh`

## Scope

Use this directory when you want to:

- explore the SPA generator UI
- test the scaffolded frontend/backend pattern
- generate or inspect template-style CRUD project structure

Related: [../../AGENT.md](../../AGENT.md) (generator manual) · [../../AGENT-UI-GUIDE.md](../../AGENT-UI-GUIDE.md) (component calling convention) · [../page-gen.README.md](../page-gen.README.md) (standalone CLI).

## Quick Start

### Backend API

```bash
cd tools/spa-generator/backend
dotnet restore
dotnet run
```

Default API URL:

- `https://localhost:5002`

### Frontend UI

Recommended:

```bash
cd tools/spa-generator
node server.js
```

Alternative launchers:

```bash
# Windows
start.bat

# macOS / Linux
./start.sh
```

Then open:

- `http://localhost:3080`

## Auth and Seed Data

Default admin email in the current generator backend config:

- `admin@generator.local`

If `SeedData:AdminPassword` is not set, the backend generates a development password and prints it to the backend console during startup.

That means:

- the password is **not** a fixed checked-in secret
- the first usable password depends on current configuration or generated startup output

## Current Backend Stack

The backend in this directory currently uses:

- ASP.NET Core 8 minimal API
- SQLite
- `BaseOrm`
- JWT bearer auth

It is not an EF Core sample.

## Layout

```text
tools/spa-generator/
├── backend/                 # .NET 8 minimal API + BaseOrm
├── frontend/                # Vanilla JS SPA
├── server.js                # Node dev server
├── start.bat
├── start.sh
└── project.json
```

## Notes

- `server.js` is the preferred frontend launcher because it handles API routing and `/packages/` path behavior more completely than a bare static server.
- The generator frontend and backend use ports `3080` / `5002`.
