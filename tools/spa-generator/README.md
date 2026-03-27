# SPA Generator

This directory contains the SPA generator workbench, not the canonical Bricks4Agent live system.

It is a self-contained generator/demo area with:

- a generated-style frontend at `frontend/`
- a generated-style backend at `backend/`
- helper launchers such as `server.js`, `start.bat`, and `start.sh`

It does **not** represent the current LINE sidecar or broker control-plane runtime.

## Scope

Use this directory when you want to:

- explore the SPA generator UI
- test the scaffolded frontend/backend pattern
- generate or inspect template-style CRUD project structure

Do not use it as the authority for:

- current LINE ingress
- current broker admin console
- current production control-plane ports

For those, use:

- [README.md](/d:/Bricks4Agent/README.md)
- [packages/csharp/workers/line-worker/README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)

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
- The generator frontend and backend use their own ports (`3080` / `5002`) and should not be confused with the LINE sidecar pair (`5357` / `5361`).
- This directory is still useful, but it should be read as generator/workbench infrastructure rather than the current system control plane.
