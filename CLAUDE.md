# Bricks4Agent — Claude Code Rules

## Project Identity

Broker-centered governed AI operations platform.
Canonical LINE path: `line-worker -> broker high-level coordinator`.
`--line-listen` on agent side is legacy/development-only.

## Build & Test

- Solution: `dotnet build packages/csharp/ControlPlane.slnx`
- Unit tests: `dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj`
- Integration tests: `dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj -- --integration http://localhost:{port}`
- Integration tests require a running broker instance.

## Test Artifacts and Cleanup

**Rule: All test-generated files must be tracked and cleaned up.**

### Known test artifacts

| Artifact | Source | Gitignored | Cleanup |
|----------|--------|------------|---------|
| `packages/csharp/broker/broker.db` | Broker startup (auto-created) | Yes (`*.db`) | Delete after testing |
| `packages/csharp/broker/broker.db-shm` | SQLite shared memory | Yes | Delete with broker.db |
| `packages/csharp/broker/broker.db-wal` | SQLite write-ahead log | Yes | Delete with broker.db |
| `.test-output/` | Test output directory | Yes | Delete after testing |

### Cleanup procedure

After integration tests:

```bash
# Stop broker first
taskkill //F //IM dotnet.exe  # Windows
# or: pkill -f "dotnet.*Broker.dll"  # Unix

# Remove test database
rm -f packages/csharp/broker/broker.db packages/csharp/broker/broker.db-shm packages/csharp/broker/broker.db-wal
```

### Rule for new test artifacts

When adding tests that produce files:
1. Add the file pattern to this table
2. Ensure the pattern is in `.gitignore`
3. Include cleanup in the test or document it here

## Code Conventions

- Language: C# (.NET 8), JavaScript (ES modules, vanilla)
- ORM: BaseOrm (lightweight, no EF Core)
- Admin UI: Single HTML file (`line-admin.html`), vanilla JS
- State storage: SharedContextEntry documents in SQLite (document-oriented pattern)
- ID generation: `BrokerCore.IdGen.New("prefix")`

## Key Architecture Notes

- Broker is control plane, not autonomous planner
- High-level model proposes; broker validates and records
- Execution layer consumes structured intent, not raw conversation
- User workspace: `{AccessRoot}/{channel}/{userId}/{conversations|documents|projects}`

## Documentation

- Canonical architecture: `docs/reports/CurrentArchitectureAndProgress-2026-03-26.md`
- Snapshot inventory: `docs/0326/` (cleaned snapshot, not single source of truth)
- Design docs: `docs/designs/`
