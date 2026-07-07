# SPA Template

This directory contains the scaffold template used by the SPA generation flow.

It is a template artifact: it describes the shape of a generated SPA, which the
generator copies and fills in.

## What this template currently represents

- frontend: Vanilla JS SPA
- backend: ASP.NET Core 8 minimal API
- persistence: SQLite + `BaseOrm`
- auth: JWT bearer auth
- local static serving options for the frontend

## Current Layout

```text
templates/spa/
├── frontend/
│   ├── index.html
│   ├── core/
│   │   ├── App.js
│   │   ├── Router.js
│   │   ├── Store.js
│   │   ├── ApiService.js
│   │   ├── Layout.js
│   │   ├── BasePage.js
│   │   └── NestedPage.js
│   ├── pages/
│   ├── components/
│   └── styles/
└── backend/
    ├── Program.cs
    ├── SpaApi.csproj
    ├── appsettings.json
    ├── Data/
    │   └── AppDbContext.cs   # BaseOrm-backed AppDb
    ├── Models/
    └── Services/
```

## Local Start

### Frontend

Any static server works, but the built-in helper is the simplest repo-local option:

```bash
cd templates/spa/frontend
dotnet run --project ../../tools/static-server -- . 3000
```

Then open:

- `http://localhost:3000`

### Backend

```bash
cd templates/spa/backend
dotnet restore
dotnet run
```

Default backend URL:

- `http://localhost:5000`

## Current Backend Notes

The backend is currently **not** an EF Core sample.

From the current code:

- `Data/AppDbContext.cs` defines `AppDb : BaseDb`
- table bootstrap happens through `EnsureCreated()`
- `DbInitializer` seeds the initial admin account

## Seed Admin Behavior

Default seed admin email in the template:

- `admin@example.com`

If `SeedData:AdminPassword` is not configured:

- the backend generates a development password
- the password is printed to the backend console at startup

So the template does **not** currently guarantee a fixed checked-in password.

## Frontend Architecture

The scaffold includes:

- hash-based routing
- page classes via `BasePage` and `NestedPage`
- a small client store
- an `ApiService` wrapper
- generated page modules under `pages/`

## Backend Architecture

The scaffold currently uses:

- ASP.NET Core 8 minimal API
- SQLite
- `BaseOrm`
- JWT bearer authentication
- rate limiting
- CORS
- security headers

Representative endpoints include:

- `/health`
- `/api/auth/login`
- `/api/auth/register`
- `/api/users`
- `/api/dashboard`

## Extension Notes

When you extend a generated project, you still need to wire both the frontend and backend intentionally.

Typical follow-up work still includes:

1. update backend schema / `EnsureCreated()`
2. add backend service and endpoint wiring
3. update frontend routes
4. connect generated pages to real backend behavior

This template is useful, but it is still a scaffold. It is not the same thing as a finished product path.
