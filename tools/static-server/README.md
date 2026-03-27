# Static Server (C#)

Minimal static file server implemented with plain .NET `HttpListener`.

## What it is for

Use this helper when you need a lightweight local server for:

- SPA frontend files
- static demo pages
- generated frontend smoke testing

It is a small local utility, not a production web host and not a replacement for the broker or the LINE sidecar.

## Current behavior

Based on [StaticServer.cs](/d:/Bricks4Agent/tools/static-server/StaticServer.cs), the server currently:

- listens on `http://localhost:<port>/`
- also listens on `http://127.0.0.1:<port>/`
- serves static files from a local root directory
- falls back to `index.html` for SPA-style routes
- adds permissive development CORS headers
- adds `X-Content-Type-Options` and `X-Frame-Options`
- rejects non-`GET` / non-`HEAD` methods except `OPTIONS`

## Quick Start

### Run directly

```bash
cd tools/static-server
dotnet run ../spa-generator/frontend 3000
```

### Publish

```bash
dotnet publish -c Release -o ./dist
```

Run the published executable:

```bash
./dist/serve ./frontend 3000
```

### Self-contained publish

```bash
# Windows
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o ./dist

# Linux
dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o ./dist

# macOS
dotnet publish -c Release -r osx-x64 --self-contained -p:PublishSingleFile=true -o ./dist
```

## Arguments

```text
serve [directory] [port]
```

| Argument | Default | Meaning |
| --- | --- | --- |
| directory | `.` | Static root |
| port | `3000` | HTTP port |

## Examples

```bash
# current directory on port 3000
serve

# custom directory
serve ./public

# custom directory and port
serve ./frontend 8080
```

## Limits

- no hot reload
- no TLS termination
- no auth
- no compression pipeline
- no production-grade caching policy

If you need a local static helper, this is enough. If you need a real edge or production serving layer, use something else.
