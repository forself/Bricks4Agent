# 後台與權限管理邊界設計

日期：2026-06-26

## 目標

將 Bricks4Agent 的本機後台從「單一 local admin 密碼即可操作全部功能」調整為具 named operator、角色與權限閘的治理式後台。系統管理與權限管理必須明確分離，讓日常維運、權限授予、審批、稽核可被分開授權、測試與追蹤。

## 現況

目前 `line-admin.html` 由 `/api/v1/local-admin/*` 支援，登入模型是單一 `LocalAdminCredential` 與 `LocalAdminSession`。只要 local request 且 session 有效，多數 local-admin API 即可操作，包含系統狀態、LINE 使用者、registration policy、browser binding、deployment、Drive delivery、approval、tool specs 與 alerts。

系統內另有 `/api/v1/admin/*` 與 `Principal` / `Role` / `RoleBinding`，但這套屬於 broker scoped-token / execution 授權，不適合作為後台 operator 權限的第一層來源。若直接混用，會把「人類後台操作員」與「AI / worker / task 執行角色」耦合，後續治理會更難驗證。

## 非目標

本階段不導入企業 SSO、MFA、外部 IAM、完整組織階層或多租戶 RBAC。這些可以在 operator 權限模型穩定後再接入。

本階段不移除既有 broker scoped-token `role_admin` 機制；它仍服務 `/api/v1/admin/*` 與 execution control plane。新的後台 operator 權限只治理 `/api/v1/local-admin/*` 與 `line-admin.html`。

## 角色

### `super_admin`

Bootstrap / break-glass 角色。具備系統管理、權限管理、審批、稽核全部權限。初始單一 `local_admin` credential 會遷移為此角色，避免現有安裝被鎖在門外。

### `system_admin`

負責平台維運與系統狀態，不得管理 operator 或授予權限。可操作：

- 系統狀態、LLM、Embedding、RAG、DB 摘要。

- worker / container health、logs 與控制。

- deployment target / preview / execute。

- Drive delivery / OAuth 狀態與交付重試。

- browser system binding 與 system-level lease。

- alerts 與 runtime monitoring。

### `permission_admin`

負責人員、使用者與權限治理，不得操作部署、worker 或系統組態。可操作：

- 後台 operator 建立、停用、改密碼、角色調整。

- LINE / Portal 使用者權限與 registration policy。

- browser user grant。

- approval queue 的 admin-tier 審批。

- tool spec 與 capability catalog 的唯讀檢視。

### `auditor`

唯讀稽核角色。可查看系統狀態、alerts、delivery history、approval history、audit trace 與 health score，不可修改任何狀態。

## 權限代碼

後端以明確 permission code 強制授權，不依賴前端隱藏按鈕。

| Permission | 用途 |
|---|---|
| `system.status.read` | 讀取 broker、LLM、Embedding、RAG、DB 摘要 |
| `system.monitor.read` | 讀取 health score、worker health、alerts、log 摘要 |
| `system.worker.manage` | spawn / stop worker、讀取 container logs |
| `system.deployment.manage` | deployment target、preview、execute |
| `system.delivery.manage` | Drive OAuth、delivery、retry |
| `system.browser.manage` | browser site/system binding、system lease、browser execution |
| `permission.operator.manage` | 後台 operator CRUD、重設密碼、停用 session |
| `permission.user.manage` | LINE / Portal 使用者權限、registration review |
| `permission.registration.manage` | registration policy |
| `permission.browser_grant.manage` | browser user grant |
| `approval.admin.manage` | admin-tier approval approve/reject |
| `tool_spec.read` | tool spec / capability catalog 讀取 |
| `audit.read` | audit / alert / delivery / approval history 唯讀 |
| `audit.verify` | audit chain verification |

角色預設權限：

| Role | Permissions |
|---|---|
| `super_admin` | 全部 |
| `system_admin` | `system.*`、`tool_spec.read`、`audit.read` |
| `permission_admin` | `permission.*`、`approval.admin.manage`、`tool_spec.read`、`audit.read` |
| `auditor` | `system.status.read`、`system.monitor.read`、`tool_spec.read`、`audit.read`、`audit.verify` |

## 資料模型

新增或擴充後台 operator 資料，不改變 broker execution 的 `Principal` / `Role`。

### `LocalAdminCredential`

保留既有 table `local_admin_credentials`，但語意從 singleton credential 擴充為 operator credential。

新增欄位：

- `operator_id`：穩定 operator id。既有 `credential_id` 可繼續作為 key；新資料兩者相同。

- `username`：登入名稱，唯一。

- `display_name`：顯示名稱。

- `role`：`super_admin` / `system_admin` / `permission_admin` / `auditor`。

- `permission_overrides`：JSON，預留給未來額外授權或拔權。本階段預設 `{}`。

- `status`：`active` / `disabled`。

- `last_login_at`：最後登入時間。

既有 `credential_id = local_admin` 會被視為 `operator_id = local_admin`、`username = admin`、`role = super_admin`。

### `LocalAdminSession`

新增欄位：

- `operator_id`

- `username`

- `role`

- `permissions_snapshot`

session 建立時寫入 role 與 permission snapshot。之後若 operator 權限被調整，系統可選擇撤銷既有 session；本階段實作會在 operator 角色變更或停用時撤銷該 operator 的所有 active sessions。

## 後端服務

`LocalAdminAuthService` 擴充為後台 operator auth service：

- `Login(context, username, password, newPassword)` 支援 named operator。

- `GetStatus(context)` 回傳 operator、role、permissions。

- `TryRequireAuthenticated(...)` 保留，用於少數只要求登入的場景。

- 新增 `TryRequirePermission(context, permission, out session, out denied)`。

- 新增 `TryRequireAnyPermission(context, permissions, out session, out denied)`。

- 新增 operator 管理方法：list、create、update role、disable、reset password、revoke sessions。

所有 local-admin mutation endpoint 必須使用 permission gate；唯讀 endpoint 也要用對應 read permission。

## API 邊界

新增 `/api/v1/local-admin/operators/*`：

| Route | Permission |
|---|---|
| `GET /api/v1/local-admin/operators` | `permission.operator.manage` |
| `POST /api/v1/local-admin/operators` | `permission.operator.manage` |
| `POST /api/v1/local-admin/operators/{operatorId}/role` | `permission.operator.manage` |
| `POST /api/v1/local-admin/operators/{operatorId}/disable` | `permission.operator.manage` |
| `POST /api/v1/local-admin/operators/{operatorId}/reset-password` | `permission.operator.manage` |
| `POST /api/v1/local-admin/operators/{operatorId}/revoke-sessions` | `permission.operator.manage` |

既有 local-admin endpoint 權限映射：

| Endpoint group | Permission |
|---|---|
| `/system/status` | `system.status.read` |
| `/alerts` | `system.monitor.read` 或 `audit.read` |
| `/workflow/*` | `system.status.read` 或 `audit.read` |
| `/line/users`, `/line/conversations` | `permission.user.manage` 或 `audit.read` |
| `/line/registration-policy` | GET: `audit.read`；POST: `permission.registration.manage` |
| `/line/users/permissions`, `/line/users/registration/review` | `permission.user.manage` |
| `/browser/user-grants/*` | `permission.browser_grant.manage` |
| `/browser/site-bindings/*`, `/browser/system-bindings/*`, `/browser/leases/*`, `/browser/execute` | `system.browser.manage` |
| `/deployment/*` | `system.deployment.manage` |
| `/delivery/*`, artifact delivery retry | `system.delivery.manage` |
| `/tool-specs*` | `tool_spec.read` |
| `/approvals*` approve/reject | `approval.admin.manage` |

## 後台 UI

`line-admin.html` 仍是後台入口，但登入後依 `authStatus.permissions` 呈現導航。

新增分頁：

- 「系統監控」：整合 system status、LLM、Embedding、RAG、DB、worker health score、health history、alerts 與 delivery/worker log 摘要。

- 「權限管理」：operator 管理、使用者權限、registration policy、browser user grants。

既有分頁調整：

- 「LINE 與使用者」保留，但 mutation 操作只對 `permission.user.manage` 顯示。

- 「Deployment」、「Browser 綁定」、「交付記錄」歸入系統管理權限。

- 「審批」只對 `approval.admin.manage` 顯示。

- auditor 可見唯讀摘要與歷史，不顯示修改、重試、執行、刪除、核准、駁回按鈕。

前端隱藏只改善 UX；所有安全邊界以後端 permission gate 為準。

## 遷移策略

1. `BrokerDbInitializer` 確保新欄位存在，並建立 username 索引。

2. 若只有舊式 `local_admin` credential，補上 `username = admin`、`display_name = Local Super Admin`、`role = super_admin`、`status = active`。

3. 初始密碼流程保持相容：沒有 credential 時仍可從 localhost 用 `admin` bootstrap，但第一次必須設定新密碼。

4. 已存在的舊 session 沒有 role snapshot 時視為 `super_admin`，但第一次成功登入後會發新式 session。文件會要求升級後重新登入。

## 測試策略

採 TDD 實作。每個後端行為先寫失敗測試，再補最小實作。

必要測試：

- `LocalAdminAuthService` 可建立 named operator，登入後 status 回傳 role 與 permissions。

- `system_admin` 可讀 `/system/status`，但不能呼叫 operator 管理 endpoint。

- `permission_admin` 可管理 operator / registration policy，但不能執行 deployment。

- `auditor` 可讀 status / alerts，但不能 mutation。

- role 變更或 operator disable 後，既有 session 被撤銷。

- 舊式 `local_admin` credential 遷移為 `super_admin`。

- `line-admin.html` static smoke：登入狀態包含 permissions 時，分頁 gating 函式可正確判斷顯示狀態。

驗證命令：

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
npm run validate:user-portal
```

## 成功準則

- 後台 operator 不再是匿名單一 admin session。

- 系統管理與權限管理在後端 API 層可被獨立授權。

- `system_admin`、`permission_admin`、`auditor` 的拒絕行為有自動測試保護。

- UI 清楚呈現角色邊界，且不把無權限操作顯示成可點擊功能。

- 既有本機安裝可平順升級，不會因新模型失去後台入口。
