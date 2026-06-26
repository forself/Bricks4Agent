# Bricks4Agent 環境需求與設置教學

Date: 2026-06-12  
Scope: repository-wide developer setup, local Windows sidecar operation, Node/SPA tooling, worker runtime, and validation commands.

## 1. 這份文件涵蓋什麼

本文件是依目前 repo 的程式碼、專案檔、設定檔、腳本與既有文件整理出的集中式環境指南。它補足的是「新機器或新開發者要準備哪些工具、設定哪些 local-only 檔案、用哪些命令啟動與驗證」。

Bricks4Agent 目前不是單一前端、單一 CLI 或單一 .NET API。主體是：

- broker-centered governed AI operations control plane
- .NET 8 broker / broker-core / worker-sdk / function-pool
- C# capability workers: `line-worker`, `file-worker`, `browser-worker`, `transport-tdx-worker`, `site-crawler-worker`
- Node.js governed agent runtime and generator tools
- vanilla JS UI component library, page generator, definition-json package
- SPA template and SPA generator workbench
- Podman compose smoke stacks for governed agent execution

Canonical live path remains:

```text
LINE webhook -> public tunnel -> line-worker -> broker /api/v1/high-level/line/process
```

`agent --line-listen` is legacy/development-only.

## 2. Repo 版圖摘要

主要目錄：

| Path | Purpose |
| --- | --- |
| `packages/csharp/ControlPlane.slnx` | Canonical control-plane build solution |
| `packages/csharp/broker` | ASP.NET Core 8 broker, local admin UI, high-level coordinator, tool specs |
| `packages/csharp/broker-core` | Broker domain services, policy/session/token/auth abstractions |
| `packages/csharp/function-pool` | TCP worker registration and dispatch |
| `packages/csharp/worker-sdk` | Worker host framework used by capability workers |
| `packages/csharp/workers/*` | LINE, file, browser, TDX transport, and site crawler workers |
| `packages/csharp/database/BaseOrm/net8` | Canonical lightweight ORM used by broker/template apps |
| `packages/javascript/browser` | UI library, page generator, definition-json, browser-side tests |
| `tools/agent` | Node governed/local agent runtime |
| `tools/agent/container` | Podman compose stacks and mock providers |
| `tools/scripts` | validation, crawl, conversion, and generation helper scripts |
| `tools/spa-generator` | SPA generator workbench |
| `tools/static-server` | small .NET static server for local frontend testing |
| `templates/spa` | generated SPA project template |
| `tests/e2e` | Playwright e2e tests |
| `docs` | architecture, manuals, reports, security, and setup docs |

Approximate tracked source/doc mix, excluding `bin`, `obj`, `node_modules`, and test output:

- C#: 423 files
- JavaScript: 374 files
- Markdown: 146 files
- JSON: 140 files
- HTML: 83 files
- C# project files: 34 files

## 3. Required tools

### 3.1 Minimum practical requirements

| Tool | Required for | Notes |
| --- | --- | --- |
| Git | all development | Standard clone/status/diff workflow |
| Windows 10/11 | canonical local LINE sidecar | Sidecar scripts are PowerShell and Windows-oriented |
| Windows PowerShell 5.1+ | sidecar scripts | Scripts use `#Requires -Version 5.1` |
| .NET SDK 8.0+ | C# build/test/runtime | All active control-plane projects target `net8.0` |
| Node.js 18+ | JS tools and agent | `tools/agent/README.md` states Node 18+ |
| npm | Node dependency install and scripts | Root package uses npm scripts |
| Playwright browsers | browser tests and browser worker | Install Chromium for JS e2e and .NET browser-worker |

Verified in this workspace on 2026-06-12:

- .NET SDK `10.0.300`
- .NET SDK `8.0.421`
- .NET runtime `8.0.27`
- Node.js `v24.16.0`
- npm `11.13.0`
- Playwright CLI `1.58.2`
- `dotnet build packages/csharp/ControlPlane.slnx` passed with `0` warnings and `0` errors

There is no `global.json`, so the installed SDK is selected by normal .NET SDK resolution. The repo targets .NET 8; installing .NET 8 SDK is the safest baseline even if a newer SDK is also present.

### 3.2 Optional but important tools

| Tool | Needed when |
| --- | --- |
| ngrok | You run the LINE sidecar with an ngrok public webhook |
| cloudflared or localhost.run | Alternative public tunnel paths supported by the sidecar stack |
| Podman 5+ with compose support | You run governed agent container validations |
| Ollama | You use local agent provider mode, local embeddings, or RAG defaults |
| SQL Server / MySQL / PostgreSQL | You run optional live BaseOrm provider integration tests |
| Azure VM with IIS and WinRM | You exercise `deploy.azure-vm-iis` |
| Google OAuth/service account credentials | You exercise Google Drive delivery |
| TDX client credentials | You exercise live Taiwan transport data queries |

## 4. First-time local setup

From the repo root:

```powershell
git clone <repo-url> Bricks4Agent
cd Bricks4Agent

npm install
dotnet restore packages/csharp/ControlPlane.slnx
dotnet build packages/csharp/ControlPlane.slnx
```

If you will run the browser UI/e2e tests:

```powershell
npx playwright install chromium
```

If you will run `browser-worker`, build it once and install the .NET Playwright browser payload:

```powershell
dotnet build packages/csharp/workers/browser-worker/BrowserWorker.csproj
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\browser-worker\bin\Debug\net8.0\playwright.ps1 install chromium
```

If you will work inside `packages/javascript/browser` directly, install its package-local dev dependencies too:

```powershell
cd packages/javascript/browser
npm install
cd ..\..\..
```

Why two npm installs can matter:

- root `package.json` drives repo-level validation and e2e tools
- `packages/javascript/browser/package.json` has its own Vitest/jsdom dev dependencies for the browser package tests

## 5. Local-only secrets and config

Do not commit credentials. The repo already ignores local secret/config patterns such as `Api.txt`, `.env`, `client_secret_*.json`, `*.key`, `packages/csharp/broker/appsettings.Development.json`, and `packages/csharp/workers/line-worker/appsettings.json`.

### 5.1 Secrets directory

Default secure local directory:

```powershell
C:\secure\Bricks4Agent
```

Override:

```powershell
$env:BRICKS4AGENT_SECRETS_DIR = 'D:\secure\Bricks4Agent'
```

Common files:

| File | Used by |
| --- | --- |
| `Api.txt` | OpenAI-compatible fallback key when `ANTHROPIC_API_KEY` is not set |
| `client_secret_*.json` | Google Drive delegated OAuth setup |
| `worker-auth.json` | Generated/persisted worker identity credentials |

The checked-in local broker defaults use Ollama for both Portal/high-level conversation and agent-facing LLM proxy: `HighLevelLlm.Provider=ollama`, `HighLevelLlm.BaseUrl=http://localhost:11434`, `HighLevelLlm.ApiFormat=chat`, `HighLevelLlm.DefaultModel=qwen3.6:latest`, and `HighLevelLlm.MaxOutputTokens=256`. `HighLevelLlm.ApiKey` is only required when you intentionally override the high-level path to a commercial provider.

Sidecar prefers `ANTHROPIC_API_KEY` from the process or Windows User environment. When present, it writes runtime broker overrides for `anthropic` / `claude-sonnet-4-6`. `Api.txt` has a legacy repo-root fallback for OpenAI-compatible sidecar/container setups, but new OpenAI-compatible fallback setups should prefer the secure directory.

### 5.2 LINE worker local config

Copy the sidecar example to the local ignored config path:

```powershell
Copy-Item .\packages\csharp\workers\line-worker\appsettings.sidecar.example.json `
  .\packages\csharp\workers\line-worker\appsettings.json
```

Fill in at least:

- `Line.ChannelAccessToken`
- `Line.ChannelSecret`
- `Line.DefaultRecipientId`
- `Line.AllowedUserIds`

The sidecar startup provisions worker auth in `worker-auth.json` and injects the matching line-worker credential into runtime config. Manual `Worker.Auth.*` values are still supported, but the sidecar-managed credential store is the canonical local path.

### 5.3 Broker local development config

For direct broker development, create or edit the ignored file:

```text
packages/csharp/broker/appsettings.Development.json
```

Start from:

```text
packages/csharp/broker/appsettings.Development.example.json
```

Common sections to override:

- `Broker:ScopedToken:Secret`
- `Broker:Encryption:MasterKeyBase64`
- `Broker:Encryption:EcdhPrivateKeyBase64`
- `FunctionPool:Enabled`
- `HighLevelLlm:ApiKey`
- `HighLevelLlm:Provider`
- `HighLevelLlm:BaseUrl`
- `HighLevelLlm:ApiFormat`
- `HighLevelLlm:DefaultModel`
- `HighLevelLlm:MaxOutputTokens`
- `Tdx:ClientId`
- `Tdx:ClientSecret`
- `GoogleDriveDelivery:*`
- `DeploymentSecrets:Mappings`
- `ArtifactDownload:SigningSecret`

## 6. Environment variables used by the repo

### 6.1 ASP.NET Core and broker

| Variable | Meaning |
| --- | --- |
| `ASPNETCORE_URLS` | Broker/template API listen URL, for example `http://127.0.0.1:5361` |
| `ASPNETCORE_ENVIRONMENT` | Use `Development` for `appsettings.Development.json` |
| `B4A_NODE_PATH` | Broker artifact generation can use this Node executable before falling back to bundled/user PATH Node |
| `Broker__MaxRequestBodyBytes` | Broker request body cap override |
| `Broker__IpRateLimit__Enabled` | Enable/disable broker POST API IP limiter |
| `Broker__IpRateLimit__PermitLimit` | Broker POST API per-IP requests per window |
| `Broker__IpRateLimit__WindowSeconds` | Broker POST API limiter window |
| `Broker__IpRateLimit__MaxTrackedClients` | Maximum in-memory broker limiter client windows |
| `BRICKS4AGENT_DEPLOY_SECRET__<NORMALIZED_SECRET_REF>__USERNAME` | Azure IIS deploy secret fallback |
| `BRICKS4AGENT_DEPLOY_SECRET__<NORMALIZED_SECRET_REF>__PASSWORD` | Azure IIS deploy secret fallback |

ASP.NET nested config can also be overridden with `__`, for example:

```powershell
$env:Embedding__Enabled = 'false'
$env:FunctionPool__Enabled = 'true'
$env:LlmProxy__BaseUrl = 'http://localhost:11434'
$env:Broker__IpRateLimit__PermitLimit = '120'
```

### 6.2 Worker config prefix

All C# workers call:

```text
AddEnvironmentVariables("WORKER_")
```

Examples:

```powershell
$env:WORKER_Worker__BrokerHost = 'localhost'
$env:WORKER_Worker__BrokerPort = '7000'
$env:WORKER_Worker__Auth__WorkerType = 'site-crawler-worker'
$env:WORKER_Worker__Auth__KeyId = '<key-id>'
$env:WORKER_Worker__Auth__SharedSecret = '<shared-secret>'
```

LINE worker also reads:

- `WORKER_Line__ChannelAccessToken`
- `WORKER_Line__ChannelSecret`
- `WORKER_Line__DefaultRecipientId`
- `WORKER_Line__AllowedUserIds`
- `WORKER_Line__OutboundRateLimit__PermitLimit`
- `WORKER_Line__OutboundRateLimit__WindowSeconds`
- `WORKER_Line__OutboundRateLimit__MaxTrackedKeys`
- `WORKER_Broker__ApiUrl`

`Line:OutboundRateLimit` is worker-local and keyed by recipient + capability. It currently covers `line.message.send` and `line.audio.send`; distributed quota coordination and `line.notification.send` coverage are separate hardening work.

### 6.3 Agent and provider variables

| Variable | Used by |
| --- | --- |
| `BROKER_URL` | governed agent broker URL |
| `BROKER_PUB_KEY` | governed agent broker public key |
| `BROKER_PRINCIPAL_ID` | governed agent principal |
| `BROKER_TASK_ID` | governed agent task |
| `BROKER_ROLE_ID` | governed agent role, default `role_reader` |
| `ANTHROPIC_API_KEY` | Anthropic Claude provider; sidecar prefers this and defaults to `claude-sonnet-4-6` |
| `OPENAI_API_KEY` | OpenAI-compatible provider |
| `GEMINI_API_KEY` | Gemini OpenAI-compatible provider |
| `DEEPSEEK_API_KEY` | DeepSeek provider |
| `GROQ_API_KEY` | Groq provider |
| `MISTRAL_API_KEY` | Mistral provider |
| `OLLAMA_BASE_URL` | host Ollama compose/test path |

### 6.4 Optional validation variables

| Variable | Enables |
| --- | --- |
| `TDX_CLIENT_ID` | live TDX verification in broker verify |
| `TDX_CLIENT_SECRET` | live TDX verification in broker verify |
| `BASEORM_SQLSERVER_CONNECTION_STRING` | live SQL Server BaseOrm integration |
| `BASEORM_MYSQL_CONNECTION_STRING` | live MySQL BaseOrm integration |
| `BASEORM_POSTGRESQL_CONNECTION_STRING` | live PostgreSQL BaseOrm integration |

## 7. Default ports and URLs

| Port / URL | Owner | Notes |
| --- | --- | --- |
| `http://127.0.0.1:5361` | canonical sidecar broker | User portal at `/portal/index.html`; admin UI at `/line-admin.html` |
| `http://127.0.0.1:5357` | canonical sidecar line-worker webhook | Public tunnel forwards here |
| `7000` | function-pool TCP worker port | Workers register here |
| `http://localhost:5000` | generic broker / SPA template backend / some e2e defaults | Can collide across subsystems |
| `https://localhost:5002` | SPA generator backend default | Workbench backend |
| `http://localhost:3080` | SPA generator frontend | `npm run serve` or `tools/spa-generator/server.js` |
| `http://localhost:3000` | generic static server / SPA template frontend | `tools/static-server` default examples |
| `6380` | cache-server default | Optional cache subsystem |
| `19090` | LINE worker container dev port | Not the canonical Windows sidecar port |
| `11434` | Ollama default | Optional local model/embedding path |

When the sidecar is running on `5361`, avoid starting the OpenAI-compatible compose stack on its default `BROKER_PORT=5361`; override it first.

## 8. Canonical Windows LINE sidecar

This is the main local operator path.

Start:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
```

Status:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
```

Verify live webhook path:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -Message "hello"
```

Verify broker high-level process directly:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify-broker -UserId test-user -Message "hello"
```

Restart:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 restart
```

Stop:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 down
```

Sidecar behavior:

- publishes broker and line-worker into `.run/line-sidecar`
- signs Bricks4Agent-owned sidecar runtime `.dll` / `.exe` files when the dev code-signing certificate exists
- stores runtime DB at `.run/line-sidecar/data/broker.db`
- writes logs under `.run/line-sidecar/logs`
- provisions `worker-auth.json` under the secure secrets directory
- injects `WorkerAuth.Enforce = true` into the sidecar broker runtime config
- updates LINE webhook URL unless `-SkipWebhookUpdate` is used
- user portal is available at `http://127.0.0.1:5361/portal/index.html`
- admin console is available at `http://127.0.0.1:5361/line-admin.html`

On Windows machines with Smart App Control / WDAC enforcement, sidecar startup can fail with `0x800711C7` or Code Integrity messages such as `did not meet the Enterprise signing level requirements`. If this happens, do not keep re-running `up`; repair the runtime trust policy from an elevated PowerShell:

```powershell
cd D:\Bricks4Agent
npm run signing:wdac-repair -- -Deploy
```

This scans `D:\Bricks4Agent\.run\line-sidecar`, generates the supplemental policy under `D:\Bricks4Agent\.run\wdac\line-sidecar-runtime\`, installs it with `CiTool`, and verifies that the generated `{policy-id}.cip` appears under `C:\Windows\System32\CodeIntegrity\CiPolicies\Active`. The policy is not effective until the repair output shows it is active. See `docs/manuals/dev-code-signing-wdac.zh-TW.md` for the full flow.

If shell encoding is unreliable for Chinese text, prefer:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify-broker `
  -UserId test-user `
  -MessageBase64Utf8 <base64-utf8-message>
```

## 9. Running workers against a broker

For a broker with function-pool enabled on port `7000`, start workers through the helper so they use the sidecar-managed credential store:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker file
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker browser
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker transport-tdx
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker site-crawler
```

Override broker port when needed:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker site-crawler -BrokerPort 7000
```

Direct worker run shape:

```powershell
dotnet run --project packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj -- `
  --Worker:BrokerHost=localhost `
  --Worker:BrokerPort=7000
```

The direct form is useful for development, but if broker worker auth is enforced you must also supply matching `Worker:Auth:*` values.

## 10. Direct broker development

The sidecar is preferred for the canonical LINE path. For direct broker development:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5361'
dotnet run --project packages/csharp/broker/Broker.csproj -- --FunctionPool:Enabled=true
```

Health:

```powershell
Invoke-RestMethod http://127.0.0.1:5361/api/v1/health
```

Admin UI:

```text
http://127.0.0.1:5361/line-admin.html
```

User portal:

```text
http://127.0.0.1:5361/portal/index.html
```

Direct broker defaults differ from sidecar defaults:

- repo `appsettings.json` has `WorkerAuth.Enforce = false`
- sidecar runtime enables worker auth enforcement and injects generated credentials
- repo `appsettings.json` points embeddings/RAG/LLM proxy at local Ollama by default

If you do not have Ollama running and are not testing RAG/local LLM paths, disable those sections for focused broker work:

```powershell
$env:Embedding__Enabled = 'false'
$env:LineChatGateway__RagEnabled = 'false'
```

## 11. Node, UI, and generator tooling

Root package scripts:

```powershell
npm test
npm run test:generator
npm run audit:ui-styles
npm run validate:ui-state
npm run validate:ui-library
npm run validate:ui-library:browser
npm run validate:user-portal
npm run validate:backend-governance
npm run validate:baseorm
npm run validate:broker-scope
npm run validate:broker-llm-proxy
npm run validate:anthropic-provider-smoke
npm run serve
```

SPA generator frontend:

```powershell
npm run serve
# open http://localhost:3080
```

Alternative:

```powershell
cd tools/spa-generator
node server.js
```

Page generator CLI:

```powershell
node tools/page-gen.js --validate --def employee.json
node tools/page-gen.js --def employee.json --mode static --output .\output
node tools/page-gen.js --list-types
```

Browser package tests:

```powershell
cd packages/javascript/browser
npm test
npm run metadata:check
cd ..\..\..
```

SPA template CLI:

```powershell
cd templates/spa/scripts
node spa-cli.js new --name my-shop --output ..\..\..\projects
node spa-cli.js feature Product --fields "Name:string,Price:decimal,Stock:int"
```

## 12. Playwright and e2e tests

Install Chromium:

```powershell
npx playwright install chromium
```

Run the SPA commerce proof e2e test:

```powershell
npx playwright test tests/e2e/ui/spa-commerce-proof.spec.ts --config tests/e2e/playwright.config.ts
```

Run the SPA generator Playwright suite:

```powershell
npx playwright test --config tools/spa-generator/playwright.config.mjs
```

The e2e config can start the template backend automatically at:

```text
http://127.0.0.1:5000
```

If port `5000` is already in use, stop the conflicting service or override the relevant config before running the test.

## 13. C# build and test commands

Canonical build:

```powershell
dotnet build packages/csharp/ControlPlane.slnx
```

Broker console/unit-style test suite:

```powershell
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
```

On Windows hosts with Smart App Control / WDAC enforcement, run the trusted test entry from an elevated PowerShell instead. It builds, signs, deploys the broker test WDAC policies, then runs the test host with `--no-build` so hash trust is not invalidated by a later build:

```powershell
npm run test:broker:trusted
```

Broker integration mode, requiring a running broker:

```powershell
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj -- --integration http://localhost:5361
```

xUnit unit tests:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
```

xUnit integration tests:

```powershell
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj
```

BaseOrm verification:

```powershell
dotnet run --project packages/csharp/database/BaseOrm/net8/verify/BaseOrm.Verify.csproj
npm run validate:baseorm
```

Broker verify project:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
npm run validate:broker-scope
```

Backend dependency governance:

```powershell
npm run validate:dotnet-deps
npm run validate:dotnet-api-usage
npm run validate:backend-governance
```

## 14. Podman governed agent stacks

The Podman stacks prove the governed agent path, not the full LINE sidecar system.

Preferred smoke test:

```powershell
npm run validate:podman-governed-stack
```

Manual default stack:

```powershell
podman compose -f tools/agent/container/compose.yml up --build --abort-on-container-exit --exit-code-from agent
```

OpenAI-compatible mock stack:

```powershell
podman compose -f tools/agent/container/compose.openai-compatible.yml up --build --abort-on-container-exit --exit-code-from agent
```

Host Ollama stack:

```powershell
$env:STACK_MODEL = 'qwen3.6:latest'
podman compose -f tools/agent/container/compose.ollama-host.yml up --build --abort-on-container-exit --exit-code-from agent
```

Stop and remove volumes:

```powershell
podman compose -f tools/agent/container/compose.yml down -v
```

Important port note:

- `compose.yml` broker default host port: `5000`
- `compose.openai-compatible.yml` broker default host port: `5361`
- `compose.ollama-host.yml` broker default host port: `5002`

Override when needed:

```powershell
$env:BROKER_PORT = '5601'
podman compose -f tools/agent/container/compose.openai-compatible.yml up --build --abort-on-container-exit --exit-code-from agent
```

## 15. Optional integrations

### 15.1 Ollama

Used by:

- `tools/agent` default local provider mode
- broker `Embedding`
- broker `RagPipeline`
- broker `LlmProxy` default config

Typical local setup:

```powershell
ollama serve
ollama pull bge-m3
ollama pull nomic-embed-text
ollama pull qwen3.6:latest
```

`qwen3.6:latest` is the default local high-level and LLM proxy model used by the broker and Portal path. `bge-m3` is the default embedding model for multilingual / Chinese RAG vectors; `nomic-embed-text` is kept as a lighter fallback. Model names in config are defaults, not hard requirements for every workflow. If you do not run RAG or local model paths, disable those settings instead of installing large models.

### 15.2 Anthropic Claude

Used by the LINE sidecar high-level responder and broker `LlmProxy` when `ANTHROPIC_API_KEY` exists:

```powershell
[Environment]::SetEnvironmentVariable('ANTHROPIC_API_KEY', '<key>', 'User')
```

The sidecar runtime override uses:

- `Provider=anthropic`
- `BaseUrl=https://api.anthropic.com`
- `ApiFormat=messages`
- `DefaultModel=claude-sonnet-4-6`
- `MaxOutputTokens=4096`

Restart the sidecar after changing the key.

Smoke check without logging the key:

```powershell
npm run validate:anthropic-provider-smoke
```

If `ANTHROPIC_API_KEY` is absent, this check exits successfully as skipped. If present, it confirms `claude-sonnet-4-6` is visible through Anthropic `/v1/models`.

### 15.3 TDX transport

Required only for live transport data:

- `Tdx:ClientId`
- `Tdx:ClientSecret`
- optional `TDX_CLIENT_ID` / `TDX_CLIENT_SECRET` for verify tools

TDX defaults:

- auth URL: `https://tdx.transportdata.tw/auth/realms/TDXConnect/protocol/openid-connect/token`
- API base URL: `https://tdx.transportdata.tw/api/basic`

### 15.4 Google Drive delivery

Required only for Drive artifact delivery:

- OAuth client JSON: `client_secret_*.json`
- optional service account JSON
- `GoogleDriveDelivery:DefaultFolderId`
- delegated redirect URI: `http://127.0.0.1:5361/api/v1/google-drive/oauth/callback`

Signed broker artifact download is a fallback delivery leg and does not require Drive.

### 15.5 Azure VM IIS deployment

Required only for `deploy.azure-vm-iis`:

- target VM has IIS installed
- PowerShell remoting / WinRM enabled
- `WebAdministration` module available on the VM
- deployment target registered through broker admin endpoints or local admin UI
- credentials configured through `DeploymentSecrets:Mappings` or `BRICKS4AGENT_DEPLOY_SECRET__...` environment variables

## 16. Test artifacts and cleanup

Known generated artifacts:

| Artifact | Source | Cleanup |
| --- | --- | --- |
| `packages/csharp/broker/broker.db` | direct broker startup | delete after tests if not needed |
| `packages/csharp/broker/broker.db-shm` | SQLite shared memory | delete with DB |
| `packages/csharp/broker/broker.db-wal` | SQLite WAL | delete with DB |
| `.test-output/` | test output | delete after tests |
| `test-results/` | Playwright output | delete after tests |
| `.run/line-sidecar/` | sidecar runtime state | do not delete unless intentionally resetting sidecar state |
| `**/bin/`, `**/obj/` | .NET build output | safe to clean when not running processes |
| `node_modules/` | npm install | reinstall with `npm install` |

Stop broker/sidecar before deleting SQLite files.

Windows cleanup after direct integration tests:

```powershell
taskkill /F /IM dotnet.exe
Remove-Item -Force `
  .\packages\csharp\broker\broker.db, `
  .\packages\csharp\broker\broker.db-shm, `
  .\packages\csharp\broker\broker.db-wal `
  -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force .\.test-output -ErrorAction SilentlyContinue
```

Use `line-sidecar.ps1 down` instead of killing all `dotnet.exe` processes when you only want to stop the sidecar:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 down
```

## 17. Troubleshooting

### 17.1 `dotnet` not found

Install .NET SDK 8.0+. The sidecar script also checks:

```text
%USERPROFILE%\.dotnet\dotnet.exe
```

Verify:

```powershell
dotnet --info
dotnet --list-sdks
```

### 17.2 Chinese text appears corrupted in PowerShell output

The source files are UTF-8. Prefer PowerShell 7 or explicitly read files with UTF-8:

```powershell
Get-Content .\packages\csharp\broker\Broker.csproj -Encoding UTF8
```

For LINE verification messages, prefer `-MessageFile` or `-MessageBase64Utf8`.

### 17.3 Worker registration fails with auth errors

Use the sidecar once to provision `worker-auth.json`, then start workers with:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker <file|browser|transport-tdx|site-crawler>
```

If manually starting workers, ensure `Worker:Auth:WorkerType`, `Worker:Auth:KeyId`, and `Worker:Auth:SharedSecret` match broker `WorkerAuth:Credentials`.

### 17.4 Browser worker fails with Playwright browser missing

Run:

```powershell
dotnet build packages/csharp/workers/browser-worker/BrowserWorker.csproj
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\browser-worker\bin\Debug\net8.0\playwright.ps1 install chromium
```

### 17.5 E2E tests fail because port `5000` or `5361` is occupied

Check what is running:

```powershell
netstat -ano | findstr ":5000"
netstat -ano | findstr ":5361"
```

Stop the conflicting process or override the relevant port before starting compose/e2e/sidecar.

### 17.6 Ollama is not installed or models are missing

If the workflow does not need local RAG or local LLM proxying, disable:

```powershell
$env:Embedding__Enabled = 'false'
$env:LineChatGateway__RagEnabled = 'false'
```

If the workflow does need local models, start Ollama and pull the configured model names.

## 18. Recommended setup paths

### 18.1 Minimal code contributor

```powershell
npm install
dotnet build packages/csharp/ControlPlane.slnx
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
```

### 18.2 Frontend / generator contributor

```powershell
npm install
npx playwright install chromium
npm run validate:ui-library
npm run validate:ui-state
npm run serve
```

For package-local browser tests:

```powershell
cd packages/javascript/browser
npm install
npm test
```

### 18.3 LINE sidecar operator

```powershell
New-Item -ItemType Directory -Force C:\secure\Bricks4Agent | Out-Null
Copy-Item .\packages\csharp\workers\line-worker\appsettings.sidecar.example.json `
  .\packages\csharp\workers\line-worker\appsettings.json
# Fill LINE credentials in the ignored appsettings.json.
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -Message "hello"
```

### 18.4 Governed agent / container contributor

```powershell
npm install
npm run validate:agent-governed
npm run validate:broker-llm-proxy
npm run validate:podman-governed-stack
```

## 19. Source documents worth reading next

- `README.md`
- `AGENTS.md`
- `docs/reports/CurrentArchitectureAndProgress-2026-06-11.md`
- `docs/manuals/line-sidecar-runbook.md`
- `packages/csharp/workers/line-worker/README.md`
- `tools/agent/README.md`
- `tools/agent/container/README.md`
- `tools/spa-generator/README.md`
- `templates/spa/README.md`
- `packages/csharp/database/BaseOrm/README.md`
