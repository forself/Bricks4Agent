# Bricks4Agent 目前版本技術手冊

Date: 2026-06-18

Status: current technical manual

Audience: platform engineers, maintainers, integration engineers

## 1. 系統定位

Bricks4Agent 是 broker-centered governed AI operations platform。它的核心不是讓模型直接操作工具，而是把自然語言意圖轉成可驗證、可審批、可稽核、可派發的結構化工作。

目前 canonical LINE 路徑：

```text
LINE webhook -> public tunnel -> line-worker -> broker /api/v1/high-level/line/process
```

目前 governed agent 路徑：

```text
agent container -> broker sessions/runtime/llm/execution APIs -> broker policy -> dispatcher/worker
```

設計不變式：

- Broker 是控制平面，不是 autonomous planner。

- 高階模型提出/整理意圖，broker 驗證與記錄。

- Execution layer 消費 structured intent，不消費 raw conversation。

- Agent container 不直接持有工具、repo、資料源或 provider key。

- 所有重要動作應有 session、capability、scope、policy、audit。

## 2. Repo 主要結構

| Path | 用途 |
|---|---|
| `packages/csharp/ControlPlane.slnx` | canonical .NET build solution |
| `packages/csharp/broker` | ASP.NET Core broker、admin UI、endpoint、tool specs |
| `packages/csharp/broker-core` | domain models、policy、session、token、audit、catalog、broker service |
| `packages/csharp/function-pool` | TCP worker registry、dispatch、health |
| `packages/csharp/worker-sdk` | capability worker host framework |
| `packages/csharp/workers/file-worker` | file capability worker |
| `packages/csharp/workers/browser-worker` | browser read/action worker |
| `packages/csharp/workers/transport-tdx-worker` | TDX transport worker |
| `packages/csharp/workers/site-crawler-worker` | site crawl/reconstruct/generate worker |
| `packages/csharp/workers/execution-adapter-worker` | `repo.patch.apply`、`build.test.run` trusted adapter |
| `packages/csharp/workers/line-worker` | LINE webhook ingress bridge |
| `packages/csharp/database/BaseOrm/net10` | canonical lightweight ORM |
| `packages/csharp/rag-service` | standalone RAG retrieval minimal API host |
| `packages/javascript/browser` | UI component library、user portal、page generator、browser package tests |
| `packages/javascript/browser/user-portal` | broker-served user frontend for login, commands, results and artifacts |
| `tools/agent` | local/generation/governed agent runtime |
| `tools/agent/container` | Podman governed agent stacks |
| `tools/scripts` | repository validation scripts |
| `tools/spa-generator` | SPA generator workbench |
| `templates/spa` | generated SPA template |
| `docs/designs` | architecture/design records |
| `docs/manuals` | runbooks and manuals |

## 3. Runtime 拓樸

### 3.1 Windows LINE Sidecar

Sidecar 是目前最重要的本機操作拓樸。

```text
PowerShell line-sidecar.ps1
  -> publish broker
  -> publish line-worker
  -> provision worker-auth.json
  -> start broker on 127.0.0.1:5361
  -> start line-worker webhook on 127.0.0.1:5357
  -> start/reuse public tunnel (ngrok 優先；不可用時改用 localhost.run，必要時重試 cloudflared)
  -> update LINE webhook
```

Runtime output：

```text
.run/line-sidecar/
  broker/
  line-worker/
  data/broker.db
  logs/
```

Sidecar 與直接 `dotnet run` 的差異：

- Sidecar 會注入 production-style override。

- Sidecar 會啟用 worker auth enforcement。

- Sidecar DB 位於 `.run/line-sidecar/data/broker.db`。

- Direct broker 預設 DB 是 `packages/csharp/broker/broker.db`。

### 3.2 Direct Broker Development

用於工程開發：

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ASPNETCORE_URLS='http://127.0.0.1:5361'
dotnet run --project packages/csharp/broker/Broker.csproj -- --FunctionPool:Enabled=true
```

若沒有 Ollama 且不測 RAG/local LLM：

```powershell
$env:Embedding__Enabled='false'
$env:LineChatGateway__RagEnabled='false'
```

### 3.3 FunctionPool Worker Topology

當 `FunctionPool:Enabled=true`：

```text
broker PoolListener :7000
  <- worker-sdk worker register
  -> PoolDispatcher dispatch route
  -> fallback in-process dispatcher if StrictMode=false
```

主要設定：

| Key | Default | 說明 |
|---|---|---|
| `FunctionPool:Enabled` | `false` | 是否啟用 worker TCP pool |
| `FunctionPool:StrictMode` | `false` | true 時不 fallback 到 in-process |
| `FunctionPool:ListenPort` | `7000` | worker registration/dispatch port |
| `FunctionPool:DispatchTimeoutSeconds` | `30` | dispatch timeout |
| `FunctionPool:MaxRetries` | `2` | dispatch retry |
| `FunctionPool:HeartbeatTimeoutSeconds` | `30` | worker heartbeat timeout |
| `FunctionPool:MaxWorkers` | `100` | worker 數上限 |

Worker helper：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker file
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker browser
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker transport-tdx
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker site-crawler
```

### 3.4 Governed Agent Container Stack

Podman stack 用於驗證 controlled agent core。

主要 stack：

| Compose | 用途 |
|---|---|
| `tools/agent/container/compose.yml` | mock Ollama protocol + broker + agent + workers |
| `compose.openai-compatible.yml` | OpenAI-compatible mock or real provider path |
| `compose.ollama-host.yml` | host Ollama path |

Agent container 被設計為：

- 只知道 broker URL、公鑰、principal、task、role。

- 不直接持有 provider key。

- 不直接接觸工具。

- 透過 broker `/api/v1/runtime/spec` 取得 runtime defaults。

- 透過 broker `/api/v1/llm/*` 取得 model health/list/chat。

- 透過 broker `/api/v1/execution-requests/submit` 送工具需求。

## 4. Broker 啟動與服務組成

Broker startup 的主要組成：

1. `BrokerDb.UseSqlite(connectionString)` 建立 SQLite-backed store。

2. `BrokerDbInitializer` 初始化 schema、development seed、dashboard seed。

3. `EnvelopeCrypto` 管理 ECDH envelope crypto。

4. `ScopedTokenService` 發行 scoped delegation token。

5. `SessionService` 管理 container/agent session。

6. `RevocationService` / `CacheRevocationService` 管理 token/session revocation。

7. `AuditService` 記錄 audit event。

8. `CapabilityCatalog` / `CacheCapabilityCatalog` 管理 capability。

9. `PolicyEngine` 做 PDP decision。

10. `LlmProxyService` / `MeteredLlmProxyService` 作 broker-side inference gateway。

11. `BrokerService` 執行 task、execution request、approval lifecycle。

12. `ToolSpecRegistry` 讀取 `broker/tool-specs` 並同步 capability。

13. `LineChatGateway` 與 `HighLevelCoordinator` 管理 LINE/portal 共用高階流程。

14. `PortalAuthService`、`PortalEndpoints` 與 broker-served `user-portal` 提供使用者登入、指令、結果與 artifact 前台。

15. Optional FunctionPool、container manager、worker health monitor。

16. Optional Drive、Azure IIS、browser runtime、RAG/embedding services。

## 5. Core Data Models

核心模型位於 `packages/csharp/broker-core/Models`。

| Model | 用途 |
|---|---|
| `Principal` | 使用者、agent、worker、admin 等主體 |
| `Role` | 角色定義 |
| `RoleBinding` | principal 和 role 的綁定 |
| `BrokerTask` | broker task |
| `Capability` | capability 定義、route、risk、policy |
| `CapabilityGrant` | task/principal/role 的能力授權與 scope |
| `ContainerSession` | agent/container session |
| `ExecutionRequest` | 受控執行請求與裁決結果 |
| `ApprovalRequest` | approval lifecycle |
| `AuditEvent` | audit trail |
| `SharedContextEntry` | document-oriented shared context |
| `VectorEntry` | embedding/vector search entries |
| `Plan` / `PlanNode` / `PlanEdge` | DAG plan |
| `Revocation` | token/session/capability revocation |
| `SystemEpoch` | epoch-based invalidation |
| `BrowserSiteBinding` | browser site identity binding |
| `BrowserUserGrant` | user delegated browser grant |
| `BrowserSystemBinding` | system account browser binding |
| `BrowserSessionLease` | browser lease |
| `GoogleDriveDelegatedCredential` | delegated OAuth token store |
| `GoogleDriveOAuthState` | OAuth state |
| `AzureIisDeploymentTarget` | deployment target |
| `LocalAdminCredential` / `LocalAdminSession` | localhost admin auth |
| `PortalUserCredential` / `PortalUserSession` | user portal password credential, LINE binding metadata, one-time verification hash, and HttpOnly cookie session |
| `ObservationEvent` / `HealthScoreSnapshot` | monitoring/health |

## 6. API Surface

Broker 使用 `/api/v1` 作主要 API group。

### 6.1 Public / bootstrap / exempt routes

| Route | 說明 |
|---|---|
| `GET/POST /api/v1/health` | basic health |
| `POST /api/v1/sessions/register` | session bootstrap |
| `/api/v1/tool-specs/*` | tool spec list/get |
| `/api/v1/local-admin/*` | localhost admin auth surface |
| `/api/v1/portal/*` | user portal auth, command, result and artifact surface |
| `/api/v1/high-level/line/*` | line-worker signed request path |
| `/api/v1/user/approvals/*` | signed-link user approval path |

### 6.2 Session, task, execution

| Route group | 主要用途 |
|---|---|
| `POST /api/v1/sessions/register` | register session |
| `POST /api/v1/sessions/heartbeat` | heartbeat |
| `POST /api/v1/sessions/close` | close and cleanup |
| `POST /api/v1/tasks/create` | create task |
| `POST /api/v1/tasks/query` | query tasks |
| `POST /api/v1/tasks/cancel` | cancel task |
| `POST /api/v1/execution-requests/submit` | submit governed execution request |
| `POST /api/v1/execution-requests/query` | query execution requests |

### 6.3 Capability, grants, context, audit

| Route group | 主要用途 |
|---|---|
| `POST /api/v1/capabilities/list` | list capabilities visible to caller |
| `POST /api/v1/grants/list` | list grants |
| `POST /api/v1/context/write` | write shared context |
| `POST /api/v1/context/read` | read shared context |
| `POST /api/v1/context/read-by-key` | read by key |
| `POST /api/v1/context/list` | list context |
| `POST /api/v1/context/history` | context history |
| `POST /api/v1/audit/query` | audit query |
| `POST /api/v1/audit/trace` | trace by correlation |
| `POST /api/v1/audit/verify` | audit verification |

### 6.4 Runtime and LLM proxy

| Route | 用途 |
|---|---|
| `POST /api/v1/runtime/spec` | runtime defaults for governed agent |
| `POST /api/v1/llm/health` | broker-mediated provider health |
| `POST /api/v1/llm/models` | model listing |
| `POST /api/v1/llm/chat` | chat/completion via broker |

`LlmProxy` supports:

- Ollama style API。

- OpenAI chat completions。

- OpenAI responses API。

- Anthropic Claude Messages API (`/v1/messages`)。

### 6.5 High-level LINE

| Route | 用途 |
|---|---|
| `POST /api/v1/high-level/line/process` | canonical line-worker -> broker process |
| `POST /api/v1/high-level/line/profile` | profile |
| `POST /api/v1/high-level/line/draft` | draft |
| `GET /api/v1/high-level/line/users` | users |
| `POST /api/v1/high-level/line/users/permissions` | user permissions |
| `GET/POST /api/v1/high-level/line/registration-policy` | registration policy |
| `POST /api/v1/high-level/line/users/registration/review` | registration review |
| `GET /api/v1/high-level/line/notifications/pending` | line-worker pulls notifications |
| `POST /api/v1/high-level/line/notifications/complete` | mark notification complete |

### 6.6 Local admin

`/api/v1/local-admin/*` powers `line-admin.html`。Local admin 是本機人類 operator 邊界，與 broker scoped-token `Principal` / `Role` 分離。登入後 session 會保存 operator id、username、role 與 permission snapshot；後端 endpoint filter 依 route permission gate 執行授權。

Major route categories:

- auth: status/login/change-password/logout。

- operators: `/operators/*`，建立、列出、調整角色、停用、重設密碼、撤銷 sessions。

- system: `/system/status`。

- LINE users/conversations/workflow。

- browser site-bindings/user-grants/system-bindings/leases/execute/executions。

- deployment targets/preview/execute/executions。

- delivery Google Drive status/OAuth/share。

- artifacts and notification retry。

- tool specs。

- approvals。

- alerts。

Local admin roles:

| Role | Permissions |
|---|---|
| `super_admin` | all local admin permissions |
| `system_admin` | `system.*`、`tool_spec.read`、`audit.read` |
| `permission_admin` | `permission.*`、`approval.admin.manage`、`tool_spec.read`、`audit.read` |
| `auditor` | `system.status.read`、`system.monitor.read`、`tool_spec.read`、`audit.read`、`audit.verify` |

Representative endpoint gates:

| Endpoint group | Permission |
|---|---|
| `/system/status` | `system.status.read` |
| `/operators/*` | `permission.operator.manage` |
| `/line/users/permissions`, registration review | `permission.user.manage` |
| `/line/registration-policy` POST | `permission.registration.manage` |
| `/browser/user-grants/*` | `permission.browser_grant.manage` |
| `/browser/site-bindings/*`, `/browser/system-bindings/*`, `/browser/leases/*`, `/browser/execute` | `system.browser.manage` |
| `/deployment/*` | `system.deployment.manage` |
| `/delivery/*`, artifact delivery retry | `system.delivery.manage` |
| `/tool-specs*` | `tool_spec.read` |
| `/approvals/*` approve/reject | `approval.admin.manage` |

### 6.7 User portal

`/api/v1/portal/*` powers `packages/javascript/browser/user-portal`.

| Route | 用途 |
|---|---|
| `GET /api/v1/portal/auth/status` | read current portal cookie session and self-registration status |
| `POST /api/v1/portal/auth/register` | create portal credential, issue session cookie, ensure high-level user profile, return one-time `line_verification` |
| `POST /api/v1/portal/auth/login` | verify password and issue HttpOnly session cookie |
| `POST /api/v1/portal/auth/logout` | revoke session and clear cookie |
| `POST /api/v1/portal/auth/line-verification` | issue a new short-lived LINE verification code for the authenticated Portal user |
| `GET /api/v1/portal/me` | read profile, LINE verification status, and current draft for the authenticated user |
| `POST /api/v1/portal/commands` | submit a user command to `HighLevelCoordinator.ProcessLineMessageAsync` |
| `GET /api/v1/portal/results` | read latest high-level interaction records for the authenticated user |
| `GET /api/v1/portal/artifacts` | list authenticated user's artifact metadata and download URLs |
| `GET /api/v1/portal/artifacts/{documentId}` | read one authenticated-user artifact by document id |

Security boundary:

- Portal uses its own `PortalUserCredential` and `PortalUserSession` records; it does not reuse local-admin sessions.

- The session cookie is HttpOnly, SameSite Strict, path `/`, and Secure when the request is HTTPS.

- LINE onboarding is Portal-first: registration/reissue returns a 6-digit `line_verification.code`, but only the SHA-256 hash is persisted on `PortalUserCredential`.

- Raw LINE user ids matching `U[a-fA-F0-9]{32}` are rejected until `/verify <user_id> <code>` or `/驗證 <user_id> <code>` binds that LINE id to a Portal account.

- After binding, `HighLevelCoordinator` resolves the raw LINE id to the Portal `user_id`; high-level API responses include `effective_user_id` for that mapped identity.

- Portal endpoints are plain JSON trusted paths in the encryption/auth middleware, but user resources still require the portal cookie.

- Artifact DTOs hide internal `FilePath` / workspace paths and return Google Drive or signed broker download links only.

### 6.8 Browser, deployment, Drive, artifacts

| Group | 用途 |
|---|---|
| `/api/v1/browser-admin/*` | browser binding and request preview |
| `/api/v1/deployment-admin/*` | Azure IIS target/request/preview/execute |
| `/api/v1/google-drive/oauth/callback` | OAuth callback |
| `/api/v1/artifacts/download/{artifactId}` | signed artifact download |
| `/api/v1/user/approvals/*` | user signed-link approval |

### 6.9 Worker health and container management

Available only when `FunctionPool:Enabled=true`：

| Route | 用途 |
|---|---|
| `GET /api/v1/workers/` | list registered workers |
| `POST /api/v1/workers/spawn` | spawn container worker |
| `POST /api/v1/workers/stop` | stop worker container |
| `GET /api/v1/workers/containers` | list containers |
| `POST /api/v1/workers/logs` | fetch container logs |
| `GET /api/v1/workers/health` | pool health |
| `GET /api/v1/health/workers` | worker health summary |
| `GET /api/v1/health/score` | worker health score |
| `GET /api/v1/health/score/history` | score history |

## 7. Tool Spec / Capability Catalog

Tool specs live under:

```text
packages/csharp/broker/tool-specs
```

Current spec directories:

| Tool spec |
|---|
| `browser.reference.anonymous.navigate` |
| `browser.reference.anonymous.read` |
| `browser.reference.system-account.read` |
| `browser.reference.user-delegated.read` |
| `commerce.price.search` |
| `delivery.google-drive.share` |
| `deploy.azure-vm-iis` |
| `knowledge.wikipedia.search` |
| `site.crawl.source` |
| `site.generate.package` |
| `site.reconstruct.package` |
| `transport.query` |
| `travel.bus.search` |
| `travel.flight.search` |
| `travel.hsr.search` |
| `travel.rail.search` |
| `web.search.duckduckgo` |
| `web.search.google` |

Execution adapter capabilities are seeded/tested in broker/agent paths:

| Capability | Agent tool route | Worker |
|---|---|---|
| `repo.patch.apply` | `apply_patch` | `execution-adapter-worker` |
| `build.test.run` | `run_build_test` | `execution-adapter-worker` |

Tool spec capability sync runs as hosted service and keeps the broker catalog aligned with specs.

## 8. Policy, Risk, Approval

### 8.1 Risk levels

`RiskLevel` has four tiers：

| Tier | Meaning |
|---|---|
| Low | read-only, internal, no side effect |
| Medium | reversible write within task/user scope |
| High | irreversible, external, scope escape, agent control |
| Critical | privilege escalation, secrets, production deploy, destructive/bulk, free shell |

### 8.2 Approval policy

| Policy | Meaning |
|---|---|
| `auto` | allow directly |
| `auto_if_task_scope_match` | allow in scope, escalate if scope escapes |
| `require_approval` | create approval request; High remains a one-approval gate |
| `require_dual_approval` | create approval request requiring two distinct approver ids; Critical uses this gate |
| `deny` | reject |

### 8.3 Decision flow

Broker policy flow:

```text
ExecutionRequest
  -> validate session/token
  -> resolve capability
  -> resolve grants
  -> validate route and payload schema
  -> compare scope.paths/scope.routes
  -> evaluate risk/policy
  -> Allow | RequireApproval | Deny
```

High/Critical and scope escape should become `RequireApproval` when approval-eligible, not silent allow. Free shell remains denied.

### 8.4 Approval tiers

| Tier | Approver |
|---|---|
| User | owning user, signed link, own User-tier approvals only |
| Admin | local admin, global approvals |

High / `require_approval` remains single-approver. Critical / `require_dual_approval` is broker-enforced with `ApprovalRequest.required_approval_count` and persisted `approval_decisions`, one decision per approver id, so the same approver cannot satisfy the threshold twice. Local-admin approval identity is now based on the named operator id from the local admin session, and approve/reject routes require `approval.admin.manage`.

## 9. High-Level Coordinator

### 9.1 Input grammar

Parser rules are implemented in `HighLevelCommandParser`。

| Kind | Prefix/token | Example |
|---|---|---|
| Help | `?help`, `?h` | `?help` |
| Query | `?` | `?search keyword` |
| Production/config | `/` | `/name 小布` |
| Project name | `#`, `＃` | `#MySite` |
| Confirm | `確認`, `confirm`, `yes`, `y`, `ok`, `okay` | `confirm` |
| Cancel | `取消`, `cancel`, `no`, `n` | `cancel` |
| Conversation | no prefix | `請幫我想一個方案` |

Query subcommands:

- `search`, `s`, `搜尋`

- `rail`, `r`, `train`, `tra`, `火車`, `台鐵`

- `hsr`, `thsr`, `高鐵`

- `bus`, `b`, `公車`, `客運`

- `flight`, `f`, `flights`, `航班`, `機票`

- `profile`, `p`, `me`, `whoami`

Production/config subcommands:

- `name`, `n`, `display-name`, `displayname`, `稱呼`

- `id`, `i`, `user-id`, `userid`, `code`

Project interview commands:

- `/proj`

- `/ok`

- `/revise`

- `/cancel`

### 9.2 Workflow states

High-level workflow separates：

- conversation。

- explicit query。

- production draft。

- project name requirement。

- awaiting confirmation。

- confirmed promotion。

- cancellation。

Promotion gate prevents raw chat from becoming execution without confirmed project name and explicit confirmation.

### 9.3 Workspaces and artifacts

Workspace service uses:

```text
{AccessRoot}/{channel}/{userId}/conversations
{AccessRoot}/{channel}/{userId}/documents
{AccessRoot}/{channel}/{userId}/projects
```

Document/code/site artifacts are written to managed workspace and represented in shared context / DB records for admin listing and delivery.

## 10. Agent Runtime

`tools/agent` supports three modes:

| Mode | Description |
|---|---|
| local provider mode | direct Ollama/OpenAI-compatible provider |
| generation/pipeline mode | `project.json` and CRUD generation |
| governed mode | all model/tool traffic broker-mediated |

Provider aliases:

- `ollama`

- `openai`

- `gemini`

- `deepseek`

- `groq`

- `mistral`

Governed mode contract endpoints:

- `POST /api/v1/sessions/register`

- `POST /api/v1/runtime/spec`

- `POST /api/v1/llm/health`

- `POST /api/v1/llm/models`

- `POST /api/v1/llm/chat`

- `POST /api/v1/capabilities/list`

- `POST /api/v1/grants/list`

- `POST /api/v1/execution-requests/submit`

- `POST /api/v1/sessions/heartbeat`

- `POST /api/v1/sessions/close`

Governed mode ignores direct provider flags as formal execution path:

- `--provider`

- `--api-key`

- `--host`

## 11. Agent Container Hardening

Agent compose stacks apply:

| Control | Intent |
|---|---|
| internal-only agent network | agent egress sealed, broker is only path |
| read-only rootfs | prevent root filesystem mutation |
| tmpfs `/tmp` | temporary writable space |
| `cap_drop: ALL` | no Linux capabilities |
| `no-new-privileges:true` | prevent privilege escalation |
| non-root UID | least privilege |
| `pids_limit` | fork-bomb containment |

Current honest state:

- Network egress sealing and OS sandbox are applied and verified in stack tests.

- Runtime default seccomp profile applies.

- Custom seccomp profile is not yet implemented.

## 12. Execution Adapter Worker

The execution adapter is a trusted execution node, not an agent bypass. Agent requests still pass through broker grants, policy and dispatch.

### 12.1 `repo.patch.apply`

Responsibilities:

- Validate `base_commit == HEAD`。

- Enforce `scope.allowed_paths`。

- Reject out-of-scope patch。

- Run `git apply --check` before applying。

- Apply patch through `git apply`。

- Produce diff/evidence。

- Support idempotency。

- Avoid free-form shell。

### 12.2 `build.test.run`

Responsibilities:

- Accept only whitelist commands。

- Avoid shell expansion。

- Return structured stdout/stderr/exit code。

- Truncate/record evidence。

Typical validated commands include repo-approved forms such as:

- `npm test`

- `npm run build`

- `dotnet test`

- `dotnet build`

- `pytest`

Actual whitelist is controlled by worker configuration/code, not by natural-language prompt.

## 13. Worker Auth

Worker HTTP routes and TCP registration use identity credentials.

Sidecar provisions:

```text
C:\secure\Bricks4Agent\worker-auth.json
```

Worker env prefix:

```text
WORKER_
```

Examples:

```powershell
$env:WORKER_Worker__BrokerHost='localhost'
$env:WORKER_Worker__BrokerPort='7000'
$env:WORKER_Worker__Auth__WorkerType='site-crawler-worker'
$env:WORKER_Worker__Auth__KeyId='<key-id>'
$env:WORKER_Worker__Auth__SharedSecret='<shared-secret>'
```

For canonical sidecar, prefer `run-worker.ps1` so credentials are loaded from the secure store.

## 14. Storage and BaseOrm

BaseOrm is the project’s lightweight ORM:

- explicit SQL。

- row mapping。

- attribute-driven CRUD helpers。

- simple table bootstrap。

- no LINQ provider。

- no change tracking。

- no migrations。

Supported providers:

- SQLite。

- SQL Server。

- MySQL。

- PostgreSQL。

Important current behavior:

- SQLite file-backed connections enable WAL, foreign keys, busy timeout。

- Transaction state is execution-local, preventing concurrent request leakage on a shared `BaseDb`/`BrokerDb` instance。

- `BaseOrm.cs` is mirrored into SPA generator and template locations; run sync validation after changing it。

Validation：

```powershell
npm run validate:baseorm
npm run validate:baseorm-sync
```

Live SQL Server/MySQL/PostgreSQL checks require:

- `BASEORM_SQLSERVER_CONNECTION_STRING`

- `BASEORM_MYSQL_CONNECTION_STRING`

- `BASEORM_POSTGRESQL_CONNECTION_STRING`

## 15. UI Library and Generators

### 15.1 UI Components

Canonical browser component library：

```text
packages/javascript/browser/ui_components
```

Metadata catalog：

```text
packages/javascript/browser/ui_components/metadata/component-catalog.json
```

Current catalog summary:

- 116 components。

- Categories: `common`, `form`, `input`, `layout`, `sections`, `social`, `viz`, `editor`, `data`, `analytics`。

- Kinds: `atomic`, `composite`, `container`, `visualizer`。

- Usage modes: `manual_only`, `definition_explicit`, `field_direct`, `runtime_only`。

- `CommandComposer` is a reusable `form` composite used by the user portal command surface.

Validation:

```powershell
cd packages/javascript/browser
npm run metadata:check
npm test
cd ..\..\..
```

Repo-level validation:

```powershell
npm run validate:ui-state
npm run audit:ui-styles
npm run validate:ui-library
npm run validate:ui-library:browser
npm run validate:user-portal
```

Determinism boundary:

- site-gen static output and selected instance IDs are deterministic-clean。

- static package is anchored to B components with `b_component` and `b-binding.json`。

- Do not claim all runtime viz/map/social/download components are byte-deterministic; runtime timestamp/file naming behavior can be intentional。

### 15.2 User Portal Frontend

User-facing portal frontend:

```text
packages/javascript/browser/user-portal
```

Runtime path:

```text
http://127.0.0.1:5361/portal/index.html
```

Architecture:

- static ES modules served by broker; no separate frontend build step。

- imports the custom component library from `ui_components/index.js`。

- `PortalApiClient` calls `/api/v1/portal/*` with `credentials: include`。

- `CommandComposer` is the shared command-input component; future reusable frontend controls should be added to `ui_components` before product-specific use。

- Broker project output includes `user-portal` and `ui_components` under `wwwroot` for publish scenarios; direct development uses `PhysicalFileProvider` against `packages/javascript/browser`。

Smoke validation:

```powershell
npm run validate:user-portal
```

### 15.3 Page Generator

Page generator entry:

```text
packages/javascript/browser/page-generator
tools/page-gen.js
```

Commands:

```powershell
node tools/page-gen.js --validate --def employee.json
node tools/page-gen.js --def employee.json --mode static --output .\output
node tools/page-gen.js --list-types
```

`--mode` 接受 `static | dynamic | both`（預設 `static`）。定義也可從 stdin 讀入。

從 DefinitionTemplate 取頁面時，用 `--page` 選單一頁、`--pages` 逗號分隔批次、`--all` 批次生成全部（批次模式輸出彙總 JSON）：

```powershell
node tools/page-gen.js --def site-definition.json --page products-list --mode static --output .\output
node tools/page-gen.js --def site-definition.json --pages products-list,orders-form --mode static --output .\output
node tools/page-gen.js --def site-definition.json --all --mode static --output .\output
```

### 15.4 SPA Generator

Workbench：

```text
tools/spa-generator
```

It uses:

- ASP.NET Core minimal API on .NET 10 (`tools/spa-generator/backend/spa-generator.csproj`)。

- SQLite。

- BaseOrm。

- JWT auth。

- Vanilla JS frontend。

It is not the canonical broker/LINE runtime.

### 15.5 SPA Template

Template：

```text
templates/spa
```

Current policy:

- BaseOrm, not EF Core。

- Validation includes EF Core removal guard。

```powershell
node tools/agent/tests/validate-efcore-removal.js
```

## 16. Site Crawler / Static Site Generation

`site-crawler-worker` supports:

- crawling source site。

- deterministic extraction。

- template matching。

- static package generation。

- reconstruct package path。

- B component binding manifest。

Important current architecture:

- Generator vocabulary is anchored to canonical `ui_components` closed set。

- Manifest loading fail-closes if `b_component` is absent or not in `BComponentRegistry`。

- Arbitrary `.First()` template fallback was removed; neutral fallback records a gap rather than fabricating a component。

- Static renderer remains as a byte-stable static export projection of B vocabulary.

Tool specs:

- `site.crawl.source`

- `site.generate.package`

- `site.reconstruct.package`

## 17. RAG / Legal Knowledge Core

RAG is treated as a state-externalization and confidence core, not only as an optional LLM helper. The current implementation has a reusable retrieval core in `packages/csharp/broker-core/Services/RagRetrievalService.cs`.

Verified deterministic scope:

- `rag_import` writes legal snippets into SQLite `SharedContextEntry`.

- `memory_fts` indexes CJK legal content through `Fts5TextNormalizer`.

- `rag_retrieve` returns Consumer Protection Act fixture content through fulltext search.

- vector retrieval is verified with a deterministic fake embedding provider; no live Ollama call is required for this proof.

- tag filtering excludes unrelated non-law entries.

- `LineChatGateway`, `/dev/rag-test`, `/agents/rag/test`, `RagRetrieveHandler`, and `InProcessDispatcher` now use the shared retrieval core instead of separate retrieval implementations.

- `LineChatGateway` uses a compact deterministic FTS-first retrieval query for high-level LINE/Portal answers, with rewrite/rerank disabled on that path so answers do not depend on extra model calls before evidence is retrieved.

- `packages/csharp/rag-service` is a standalone minimal API host over the same `RagRetrievalService`; it has no broker, LINE, approval, or agent-dispatch dependency.

Operational/legal POC scope:

- The earlier legal POC still exists in `SeedConsumerProtectionLaw.cs`.

- It targets the Taiwan Consumer Protection Act family from `law.moj.gov.tw`.

- Live seed/backfill is controlled by `RagSeed:Enabled`; live vector embedding requires `Embedding:Enabled=true` and an embedding provider such as Ollama.

- Broker default embedding is `bge-m3` for multilingual / Chinese semantic retrieval; `nomic-embed-text` remains a lighter fallback.

- `vector_entries` now treats `embedding_model` as part of vector identity. The same `content_hash + task_id` may have multiple vectors for different embedding models.

- Retrieval filters vectors by the active embedding model and vector dimension, so old `nomic-embed-text` vectors are not mixed into `bge-m3` similarity scoring.

- `/dev/rag-test` reports both `vectors_current_model` and `vectors_all_models` to make model migrations visible.

- This is not a complete legal database and is not legal advice. It is an evidence-retrieval substrate for broker-governed answers.

Important files:

| File | Purpose |
|---|---|
| `broker-core/Services/RagRetrievalService.cs` | reusable retrieval core: FTS5, vector, RRF, tag filter, optional rewrite/rerank |
| `broker-core/Services/Fts5TextNormalizer.cs` | CJK FTS5 query/content normalization |
| `broker/Scripts/RagIngestService.cs` | JSON/CSV/web ingestion into `SharedContextEntry`, `memory_fts`, `vector_entries` |
| `broker/Scripts/SeedConsumerProtectionLaw.cs` | live legal seed POC for Taiwan consumer-protection law family |
| `rag-service/` | standalone retrieval service host: `/healthz`, `/rag/retrieve` |
| `broker/verify/Program.cs` | offline deterministic legal RAG verification |

Standalone retrieval host:

```powershell
$env:RAG_DB_PATH='D:\path\to\rag.db'
dotnet run --project packages/csharp/rag-service/RagService.csproj -- --urls http://127.0.0.1:5599
```

The default host initializes schema and can run without Ollama because embedding, query rewrite, and rerank are disabled by default in `rag-service/appsettings.json`. To enable semantic retrieval, configure `Embedding:Enabled=true` and point `Embedding:BaseUrl` at the embedding provider.

Use signed validation on SAC/WDAC hosts:

```powershell
npm run validate:broker-scope:signed
npm run validate:db:signed
npm run test:unit:signed
npm run test:integration:signed
npm run test:broker:signed
npm run test:dotnet:signed
npm run test:broker:trusted
```

## 18. Optional Integrations

### 18.1 Ollama

Used by:

- local agent provider mode。

- broker embedding。

- RAG pipeline。

- broker `LlmProxy` default config。

- host Ollama Podman stack。

Common setup:

```powershell
ollama serve
ollama pull bge-m3
ollama pull nomic-embed-text
ollama pull qwen3.6:latest
```

If not using RAG/local LLM:

```powershell
$env:Embedding__Enabled='false'
$env:LineChatGateway__RagEnabled='false'
```

### 18.2 Broker high-level model paths

High-level LINE path uses `HighLevelLlm`。

Agent-facing broker proxy uses `LlmProxy`。

The checked-in local broker defaults use host Ollama for both paths:

- `HighLevelLlm.Provider`: `ollama`

- `HighLevelLlm.BaseUrl`: `http://localhost:11434`

- `HighLevelLlm.ApiFormat`: `chat`

- `HighLevelLlm.DefaultModel`: `qwen3.6:latest`

- `HighLevelLlm.MaxOutputTokens`: `256`

- `LlmProxy.DefaultModel`: `qwen3.6:latest`

`HighLevelLlm.ApiKey` should stay empty for this local default. Set a key only when deliberately overriding the high-level path to Anthropic or an OpenAI-compatible provider.

When `ANTHROPIC_API_KEY` is available to `start-sidecar-stack.ps1`, sidecar writes runtime overrides for:

- `Provider`: `anthropic`

- `BaseUrl`: `https://api.anthropic.com`

- `ApiFormat`: `messages`

- `DefaultModel`: `claude-sonnet-4-6`

- `MaxOutputTokens`: `4096`

This is the preferred current sidecar path for Claude. The key stays in the user environment and is not committed to the repo.

Typical OpenAI responses config:

```powershell
$env:OPENAI_BASE_URL='https://api.openai.com'
$env:OPENAI_API_KEY='<key>'
$env:OPENAI_API_FORMAT='responses'
$env:STACK_MODEL='gpt-5.4-mini'
```

Do not put provider keys inside the agent container as the formal path.

### 18.3 TDX

Used by transport query / travel handlers.

Required:

- `Tdx:ClientId`

- `Tdx:ClientSecret`

Validation env:

- `TDX_CLIENT_ID`

- `TDX_CLIENT_SECRET`

Without credentials, live TDX validation is skipped.

### 18.4 Google Drive

Modes:

- `shared_delegated`

- `user_delegated`

- `system_account`

Important routes:

- `GET /api/v1/google-drive/oauth/callback`

- local-admin Drive OAuth/status/share endpoints。

Required for delegated OAuth:

- `client_secret_*.json`

- redirect URI: `http://127.0.0.1:5361/api/v1/google-drive/oauth/callback`

### 18.5 Azure VM IIS Deployment

Tool spec:

```text
deploy.azure-vm-iis
```

Requires:

- target VM with IIS。

- WinRM/PowerShell remoting。

- `WebAdministration` module。

- deployment target record。

- credentials via `DeploymentSecrets:Mappings` or env vars。

The shipped tool spec declares `risk_level: high` with `approval_policy: require_approval`, so this capability always goes through a single Admin-tier approval（High 不會自動放行）。

### 18.6 Browser Governance

Current browser subsystem has:

- site binding。

- user grant。

- system binding。

- lease。

- request build。

- preview/fetch。

- execution records。

Honest boundary:

- action-level governance groundwork exists。

- authenticated browser automation is not a full production browser automation platform yet。

## 19. Configuration Reference

### 19.1 Broker config sections

| Section | Purpose |
|---|---|
| `Database` | DB path |
| `Broker:MaxRequestBodyBytes` | broker request body cap, default 1 MiB |
| `Broker:IpRateLimit` | single-node POST API IP fixed-window limiter |
| `Broker:ScopedToken` | token secret/issuer/audience/ttl |
| `Broker:Encryption` | session/envelope crypto |
| `FunctionPool` | worker pool |
| `Embedding` | embedding provider |
| `RagPipeline` | query rewrite/rerank/cache |
| `LineChatGateway` | conversation/RAG behavior |
| `HighLevelLlm` | LINE high-level model |
| `HighLevelExecutionModelPolicy` | execution model aliases |
| `HighLevelCoordinator` | workspace/root/keywords/draft TTL |
| `ProjectInterview` | template catalog/session timeout |
| `ArtifactDownload` | signed download secret/TTL |
| `PortalAuth` | user portal self-registration, password length, session TTL, and LINE verification code TTL |
| `WorkerAuth` | worker identity enforcement/routes |
| `Tdx` | TDX credentials and URLs |
| `ToolSpecRegistry` | tool spec root |
| `LlmProxy` | agent-facing broker LLM proxy |
| `GoogleDriveDelivery` | Drive delivery config |
| `DeploymentSecrets` | deployment credential mapping |

### 19.2 Common environment variables

| Variable | Purpose |
|---|---|
| `ASPNETCORE_URLS` | broker listen URL |
| `ASPNETCORE_ENVIRONMENT` | development/runtime config |
| `BRICKS4AGENT_SECRETS_DIR` | secure local secrets directory |
| `B4A_NODE_PATH` | broker artifact generation Node path |
| `BROKER_URL` | governed agent broker URL |
| `BROKER_PUB_KEY` | governed agent broker public key |
| `BROKER_PRINCIPAL_ID` | governed agent principal |
| `BROKER_TASK_ID` | governed agent task |
| `BROKER_ROLE_ID` | governed agent role |
| `OPENAI_API_KEY` | OpenAI-compatible provider key |
| `ANTHROPIC_API_KEY` | Anthropic Claude provider key; sidecar prefers this over `Api.txt` when present |
| `OPENAI_BASE_URL` | provider base URL in stack scripts |
| `OPENAI_API_FORMAT` | `chat` or `responses` |
| `STACK_MODEL` | Podman stack model override |
| `OLLAMA_BASE_URL` | host Ollama URL |
| `Broker__IpRateLimit__PermitLimit` | broker POST API per-IP requests per window |
| `Broker__IpRateLimit__WindowSeconds` | broker POST API limiter window |
| `WORKER_Line__OutboundRateLimit__PermitLimit` | LINE worker outbound sends per recipient/capability/window |
| `WORKER_Line__OutboundRateLimit__WindowSeconds` | LINE worker outbound limiter window |
| `TDX_CLIENT_ID` | TDX validation/runtime |
| `TDX_CLIENT_SECRET` | TDX validation/runtime |

ASP.NET nested config uses `__`:

```powershell
$env:FunctionPool__Enabled='true'
$env:LlmProxy__BaseUrl='http://localhost:11434'
$env:Embedding__Enabled='false'
$env:Broker__IpRateLimit__PermitLimit='120'
$env:WORKER_Line__OutboundRateLimit__PermitLimit='20'
```

### 19.3 Rate-limit guardrails

Broker POST APIs use `Broker:IpRateLimit` as an in-process, fixed-window, per-IP guardrail. It returns JSON `429` responses with `Retry-After`. Health, local-admin, artifact download, OAuth callback, and `/dev` paths are excluded from this generic limiter and should keep their own route-specific controls.

LINE worker outbound sending uses `Line:OutboundRateLimit` / `WORKER_Line__OutboundRateLimit__*`. The current limiter is in-memory and keyed by recipient + capability. It is implemented for `line.message.send` and `line.audio.send`; distributed quota coordination and `line.notification.send` coverage remain future hardening items.

## 20. Build, Test, Validation

### 20.1 Canonical build

```powershell
dotnet build packages/csharp/ControlPlane.slnx
```

### 20.2 Broker tests

```powershell
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
```

Integration with running broker:

```powershell
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj -- --integration http://localhost:5361
```

### 20.3 xUnit

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --filter PortalEndpointTests
```

### 20.4 BaseOrm and broker verify

```powershell
npm run validate:baseorm
npm run validate:baseorm-sync
npm run validate:broker-scope
```

`validate:broker-scope` skips live TDX when credentials are missing.

`validate:broker-scope` also includes the offline legal RAG gate: import a small Consumer Protection Act fixture, verify `SharedContextEntry` state externalization, verify CJK FTS5 retrieval, verify deterministic vector retrieval, and verify tag filtering. It does not require live `law.moj.gov.tw` or live Ollama.

On Windows hosts with Smart App Control / WDAC enforcement, `validate:baseorm`, `validate:broker-scope`, xUnit tests, and broker console tests can fail before assertions if unsigned or untrusted local build outputs are blocked. Use [dev-code-signing-wdac.zh-TW.md](dev-code-signing-wdac.zh-TW.md) to create the Bricks4Agent dev signer, sign self assemblies, and generate a WDAC supplemental policy. For DB verification on those hosts, prefer `npm run validate:db:signed`; it builds, signs, then runs verify projects with `dotnet run --no-build` so `dotnet run` does not overwrite signatures. For xUnit and broker tests, prefer `npm run test:unit:signed`, `npm run test:integration:signed`, `npm run test:broker:signed`, or `npm run test:dotnet:signed`. If WDAC still blocks the test runtime, run `npm run signing:wdac-repair-tests -- -Deploy` from an elevated PowerShell; it covers broker, broker-tests, unit-tests, and integration-tests with Publisher-level supplemental policies plus fallback trust where needed.

### 20.5 Backend governance

```powershell
npm run validate:dotnet-deps
npm run validate:dotnet-api-usage
npm run validate:backend-governance
npm run validate:line-admin
npm run validate:dotnet10
npm run test:dotnet10
```

`validate:dotnet10` 檢查 target-framework 政策：所有 SDK-style 專案必須是 `net10.0`，唯一豁免是 allowlist 內的 `packages/csharp/database/BaseOrm/netfx48/BaseOrm.csproj`。`test:dotnet10` 在政策通過後再以 Release + `--warnaserror` 逐一 build 這些專案。

### 20.6 JS/UI/generator

```powershell
npm test
npm run test:generator
npm run validate:ui-state
npm run audit:ui-styles
npm run validate:ui-library
npm run validate:ui-library:browser
npm run validate:user-portal
npm run audit:csp
```

`audit:csp`（`tools/scripts/audit-csp.mjs`）是動 `ui_components` 前必過的硬門檻：它同時掃 CSP 違規（`<style>` 注入、`setAttribute('style')`、HTML 字串內 `style=` / `on*=`、`eval` / `new Function`、`javascript:` URL）與 Canvas-only 政策的 SVG 硬零（`<svg`、`createElementNS`、`data:image/svg`）。任一命中即 exit 1；`tools/scripts/svg-baseline.json` 只是盤點快照，不能豁免 SVG 命中。

Browser package:

```powershell
cd packages/javascript/browser
npm run metadata:check
npm test
cd ..\..\..
```

### 20.7 Agent and stacks

```powershell
npm run validate:agent-governed
npm run validate:agent-container-config
npm run validate:broker-llm-proxy
npm run validate:anthropic-provider-smoke
npm run validate:podman-governed-stack
npm run validate:podman-openai-compatible-stack
npm run validate:podman-ollama-host-stack
node tools/agent/tests/test-execution-adapter-config.js
node tools/agent/tests/test-podman-execution-adapter-stack.js
node tools/agent/tests/test-convergence.js
node tools/agent/tests/validate-efcore-removal.js
```

## 21. Security Invariants

Maintain these invariants:

- No secrets committed to repo。

- Worker auth credentials are local/secure and not hard-coded。

- Agent container does not receive provider API key as formal path。

- Broker mediates model calls for governed agent。

- Broker mediates tool calls for governed agent。

- Scope escape escalates to approval or deny。

- Free-form shell is denied。

- Execution adapter validates patch base commit and path scope。

- Deployment (`deploy.azure-vm-iis`) is High risk and requires Admin-tier approval。

- Artifact links are signed and time-limited。

- Portal user sessions use HttpOnly cookies and never expose internal artifact file paths to the browser。

- Local admin is localhost-only。

- Audit records are generated for security-relevant state changes。

## 22. Known Limits

Do not overstate the current system:

- Custom seccomp profile is pending。

- Enterprise operator console is not complete: SSO, MFA, centralized IAM, and cross-machine operator synchronization are not implemented。

- Critical dual approval is active at the broker persistence/threshold layer, and local-admin approval uses named operator identity; enterprise IAM-backed operator identity is still future work。

- `line.message.send` and `line.audio.send` have worker-local outbound rate limiting; distributed quota coordination and `line.notification.send` coverage are not complete。

- Browser authenticated automation is not production-complete。

- User portal currently uses lightweight local username/password sessions; SSO, MFA, password reset and named enterprise identity lifecycle are not implemented。

- Broader HTTP integration coverage for adapter/approval routes is still an improvement area。

- Live external SQL Server/MySQL/PostgreSQL BaseOrm tests require configured DBs。

- TDX live validation requires configured credentials。

- Full `ui_components` runtime byte determinism is not claimed。

## 23. Test Artifacts and Cleanup

Known artifacts:

| Artifact | Source | Cleanup |
|---|---|---|
| `packages/csharp/broker/broker.db` | direct broker startup | delete after testing |
| `packages/csharp/broker/broker.db-shm` | SQLite WAL/shared memory | delete with DB |
| `packages/csharp/broker/broker.db-wal` | SQLite WAL | delete with DB |
| `.test-output/` | stack/tests | delete after testing |
| `test-results/` | Playwright | delete after testing |
| `.run/line-sidecar/` | sidecar runtime | keep unless intentionally resetting sidecar |
| `bin/`, `obj/` | .NET build | clean when no process uses them |
| `node_modules/` | npm install | reinstallable |

Cleanup direct broker/test output:

```powershell
Remove-Item -Force `
  .\packages\csharp\broker\broker.db, `
  .\packages\csharp\broker\broker.db-shm, `
  .\packages\csharp\broker\broker.db-wal `
  -ErrorAction SilentlyContinue

Remove-Item -Recurse -Force .\.test-output -ErrorAction SilentlyContinue
```

Stop sidecar cleanly:

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 down
```

Stop Podman stack:

```powershell
podman compose -f tools/agent/container/compose.yml down -v
```

## 24. Recommended Reading Order

1. `docs/manuals/current-user-manual.zh-TW.md`

2. `docs/reports/CurrentArchitectureAndProgress-2026-06-13.md`

3. `docs/designs/ControlledAutonomousAISystemTechnicalDesign.md`

4. `docs/designs/RiskClassificationAndApproval-2026-06-13.md`

5. `docs/designs/ApprovalWebInterface-2026-06-13.md`

6. `docs/designs/ComponentLibraryConsolidation-2026-06-15.md`

7. `tools/agent/README.md`

8. `tools/agent/container/README.md`

9. `packages/csharp/database/BaseOrm/README.md`

10. `docs/manuals/dev-code-signing-wdac.zh-TW.md`
