# 受控自主 AI 系統技術設計

來源文件：[受控自主AI系統設計.docx](/c:/Users/Lenovo/Downloads/受控自主AI系統設計.docx)

版本：v0.1  
日期：2026-03-13  
狀態：Draft

## 1. 目的

本文件將概念性架構落成為可實作的技術設計，作為後續 API、資料模型、部署拓樸、權限規則、容器基線與營運流程的共同依據。

本設計的核心目標不是最大化 AI 自由，而是建立一個可限制、可審核、可撤權、可重放、可替換的受控自主系統。

## 2. 設計範圍

本文件涵蓋：

- 控制平面與執行平面的系統分層
- 主體模型、角色模型、能力模型
- 任務、審批、能力發放、執行請求、稽核重放
- Agent 容器與中介核心之間的 API 契約
- 共享上下文儲存與證據保存
- 安全控制、容器基線、部署建議
- MVP 分期與演進路徑

本文件不涵蓋：

- 個別業務領域的任務模板細節
- LLM prompt 文案優化
- UI 視覺設計
- 單一雲供應商專屬 IaC 模板

## 3. 系統目標與非目標

### 3.1 目標

- 所有主體都必須先被定義角色與能力，再被允許行動
- 所有有意義的行為都必須經過單一不可迴避的控制平面
- 所有高風險行為都必須可審批、可撤銷、可重放
- 工作節點必須可被快速停用、替換、重建
- 狀態、上下文與證據必須外部化
- AI 與人類在授權模型上採統一主體原則

### 3.2 非目標

- 不追求通用型自由代理網路
- 不允許工作節點自行擴權
- 不允許工作節點直接接觸資料源、工具源、部署面或模型供應商
- 不把 MCP 視為治理模型，只把它視為工具調用契約的一種參考

## 4. 核心設計原則

### 4.1 統一主體模型

對控制平面而言，人類與 AI 都是 `Principal`。授權、審批、稽核、撤權與證據要求只看角色、能力、任務與信任錨，不看主體是否為人。

### 4.2 零信任主體

系統不預設任何主體會自我節制。所有主體都只在被顯式授權的能力面內工作。

### 4.3 不可迴避節點

所有任務提交、能力發放、執行請求、共享上下文寫入、升權申請與結果提交都必須經過控制平面。

### 4.4 狀態外部化

工作節點不持有長期記憶。任務上下文、批准結果、共享知識、工作記憶、稽核事件與證據都屬於系統。

### 4.5 角色先於自由

主體不能以自然語言自行詮釋權限。自由僅存在於角色允許的行為空間內。

### 4.6 能力目錄即憲法

Capability Catalog 是唯一合法能力面。任何未列入目錄的能力都視為不存在。

## 5. 邏輯架構

### 5.1 分層

1. 使用者與外部事件層
2. 控制平面層
3. 政策與審批層
4. 共享上下文與狀態層
5. 代理容器層
6. 執行配接層
7. 證據與觀測層

### 5.2 拓樸

```text
+---------------------------+
| Human UI / External Event |
+------------+--------------+
             |
             v
+---------------------------+
| Control Plane / PEP       |
| - Task API                |
| - Session API             |
| - Routing                 |
| - Token Issuer            |
| - Revocation              |
+------+----------+---------+
       |          |
       |          v
       |    +-----------------------+
       |    | Policy / Approval     |
       |    | - PDP                 |
       |    | - Risk Engine         |
       |    | - Trust Anchor        |
       |    +-----------------------+
       |
       +-----------------------------+
       |                             |
       v                             v
+--------------------+      +------------------------+
| Shared Context     |      | Audit / Evidence Store |
| - Task context     |      | - Append-only events   |
| - Working memory   |      | - Replay artifacts     |
| - Versioned docs   |      | - Trace correlation    |
+----------+---------+      +------------------------+
           |
           v
+----------------------------+
| Agent Container            |
| - Planner                  |
| - Role-specific runtime    |
| - Request-only interface   |
| - No direct tool/provider  |
+-------------+--------------+
              |
              v
+----------------------------+
| Execution Adapters         |
| - Doc service              |
| - Repo patch service       |
| - Build/test service       |
| - Deploy service           |
| - MCP-compatible tools     |
+----------------------------+
```

## 6. 主要元件設計

### 6.1 Control Plane

控制平面是唯一的 PEP，負責：

- 任務收件
- 主體認證接入
- 角色與能力核發
- 執行請求收件
- 路由與節流
- 撤權與停用
- 審批串接
- 事件寫入與追蹤 ID 發放

不負責：

- 實際執行工具
- 保存長期業務資料
- 替代政策引擎做授權判定

建議拆分服務：

- `control-api`
- `session-service`
- `capability-issuer`
- `task-router`
- `revocation-service`

### 6.2 Policy Engine / PDP

政策引擎負責確定性裁決：

- 主體是否可承接某任務
- 角色是否可申請某 capability
- 請求參數是否符合 schema 與 scope
- 是否需升權
- 是否需人工批准
- 是否觸發風險阻斷

決策輸入：

- principal
- role binding
- task type
- capability definition
- request payload
- risk profile
- approval state
- environment policy

決策輸出：

- `allow`
- `deny`
- `require_approval`
- `require_elevation`
- `degraded_allow`

### 6.3 Approval Service / Trust Anchor

高風險行為不由 agent 自決。Approval Service 負責：

- 升權申請建立
- 指派審批者
- 記錄批准理由
- 發放短時效升權 token
- 逾時失效
- 回收已核發權限

信任錨可以是：

- 人工批准者
- 簽章服務
- 雙重批准規則
- 預先定義的變更控制程序

### 6.4 Capability Catalog

Capability Catalog 是版本化制度物件，不只是工具清單。

每個 capability 必須至少定義：

- `capability_id`
- `display_name`
- `route`
- `action_type`
- `resource_type`
- `param_schema`
- `result_schema`
- `scope_rules`
- `risk_level`
- `approval_policy`
- `ttl_seconds`
- `quota`
- `audit_level`
- `version`
- `status`

範例：

```json
{
  "capability_id": "repo.patch.apply",
  "display_name": "Apply Repository Patch",
  "route": "execution.repo.apply_patch",
  "action_type": "update",
  "resource_type": "repository",
  "param_schema": {
    "type": "object",
    "required": ["repo_id", "patch", "base_commit"],
    "properties": {
      "repo_id": { "type": "string" },
      "base_commit": { "type": "string" },
      "patch": { "type": "string" }
    }
  },
  "scope_rules": {
    "allowed_paths_from_task_scope": true,
    "max_patch_files": 20
  },
  "risk_level": "medium",
  "approval_policy": "auto_if_task_scope_match",
  "ttl_seconds": 900,
  "quota": { "per_task": 10 },
  "audit_level": "full",
  "version": 3,
  "status": "active"
}
```

### 6.5 Shared Context Store

共享上下文是版本化認知面，不是自由聊天通道。

責任：

- 保存任務上下文
- 保存角色間共享規劃
- 保存工作記憶與中介摘要
- 保存系統提供給工作節點的最小充分真相
- 保存變更版本與 ACL

設計要求：

- 文件與區塊級 ACL
- 版本不可覆寫，只能追加新版本
- 支援快照、diff、回滾視圖
- 支援引用鏈與來源追蹤

### 6.6 Agent Container

Agent 容器只是一個受控主體執行殼層。

可做：

- 請領工作項
- 讀取被授權的上下文
- 呼叫模型推理
- 產生結構化執行請求
- 回報進度與結果

不可做：

- 直接連 MCP 工具
- 直接碰外部資料源
- 直接碰代碼倉庫
- 直接部署
- 直接連公共模型供應商
- 自行發放權限

### 6.7 Execution Adapters

Execution Adapters 是唯一真正動手的層。它們接受控制平面已核可的請求後執行工作。

建議分成：

- `repo-adapter`
- `build-adapter`
- `test-adapter`
- `doc-adapter`
- `deploy-adapter`
- `db-read-adapter`
- `db-change-adapter`

可採 MCP 兼容包裝，但 MCP 只作工具調用接口，不承擔治理責任。

### 6.8 Audit and Forensic Replay

所有重要事件必須追加式保存，且支援事後重放。

最低紀錄事件：

- task submitted
- session issued
- capability granted
- command requested
- policy evaluated
- approval requested
- approval decided
- command executed
- result accepted
- capability revoked
- container terminated

## 7. 主體模型

### 7.1 Principal

```json
{
  "principal_id": "prn_01HXYZ...",
  "actor_type": "ai",
  "identity_provider": "control-plane",
  "display_name": "doc-writer-agent",
  "status": "active"
}
```

### 7.2 Role Binding

角色綁定需明示：

- principal 與 role 的關聯
- 生效時間
- 可承接的任務類型
- 預設 capabilities
- 最大升權範圍

### 7.3 Unified Principal Rule

`actor_type` 只能作稽核與觀測用途，不能用來繞過授權規則。

## 8. 任務生命週期

### 8.1 流程

1. 任務建立
2. 任務分類與風險評估
3. 角色配對
4. 容器建立或指派
5. session 與 scoped token 發放
6. 容器請領工作項
7. 容器提出宣告式執行請求
8. 控制平面送 PDP 裁決
9. 必要時進入審批
10. 核准後送 Execution Adapter
11. 結果與證據回寫
12. 完成、撤權、封存

### 8.2 典型序列

```text
Human/API -> Control Plane: create task
Control Plane -> PDP: evaluate intake
PDP -> Control Plane: allow + role template
Control Plane -> Container Orchestrator: start agent container
Container -> Control Plane: register session
Control Plane -> Container: scoped token + work item
Container -> Control Plane: request command
Control Plane -> PDP: evaluate request
PDP -> Control Plane: require approval / allow
Control Plane -> Approval Service: create approval if needed
Approval Service -> Control Plane: approved
Control Plane -> Execution Adapter: execute approved request
Execution Adapter -> Control Plane: result + evidence
Control Plane -> Context/Audit Store: append records
Control Plane -> Container: result summary
Control Plane -> Revocation Service: revoke token on completion
```

## 9. 宣告式指令模型

### 9.1 設計要求

- 指令必須結構化
- 指令必須可驗證 schema
- 指令必須顯示目標資源與作用範圍
- 指令必須可映射至 capability
- 指令必須攜帶追蹤資訊

### 9.2 Agent -> Control Plane Request Schema

```json
{
  "request_id": "req_01HXYZ...",
  "task_id": "task_01HXYZ...",
  "session_id": "ses_01HXYZ...",
  "principal_id": "prn_01HXYZ...",
  "capability_id": "repo.patch.apply",
  "intent": "apply_patch",
  "target": {
    "resource_type": "repository",
    "resource_id": "repo_shopbricks",
    "scope": {
      "paths": [
        "frontend/pages/",
        "frontend/components/"
      ],
      "base_commit": "abc123"
    }
  },
  "payload": {
    "patch": "*** Begin Patch\n..."
  },
  "reason": "Implement user-approved UI change",
  "expected_effect": "Modify 4 files under allowed scope",
  "idempotency_key": "0edb1d2f-0a8e-4c2b-9b39-f767cb305e1d"
}
```

### 9.3 Error Semantics

所有拒絕與失敗都應返回結構化錯誤：

```json
{
  "request_id": "req_01HXYZ...",
  "status": "denied",
  "error_code": "CAPABILITY_SCOPE_VIOLATION",
  "message": "Requested path is outside task scope",
  "retryable": false,
  "approval_eligible": false,
  "evidence_ref": "evt_01HXYZ..."
}
```

錯誤分類：

- `SCHEMA_INVALID`
- `CAPABILITY_NOT_GRANTED`
- `CAPABILITY_SCOPE_VIOLATION`
- `QUOTA_EXCEEDED`
- `TOKEN_EXPIRED`
- `APPROVAL_REQUIRED`
- `EXECUTION_BACKEND_UNAVAILABLE`
- `POLICY_DENIED`
- `TASK_STATE_INVALID`

## 10. API 設計

### 10.1 任務 API

#### `POST /v1/tasks`

建立任務。

#### `GET /v1/tasks/{task_id}`

查詢任務狀態、角色指派、進度、證據摘要。

#### `POST /v1/tasks/{task_id}/cancel`

取消任務並撤銷相關 session/token。

### 10.2 容器 Session API

#### `POST /v1/container-sessions/register`

容器啟動後註冊自身並換取短時效 session。

#### `POST /v1/container-sessions/{session_id}/heartbeat`

更新健康狀態與資源資訊。

#### `POST /v1/container-sessions/{session_id}/close`

主動結束工作。

### 10.3 工作項 API

#### `POST /v1/work-items/claim`

容器請領下一個工作項。

#### `POST /v1/work-items/{work_item_id}/result`

提交工作結果摘要與 artifact 引用。

### 10.4 執行請求 API

#### `POST /v1/execution-requests`

提交宣告式執行請求。

#### `GET /v1/execution-requests/{request_id}`

查詢裁決與執行狀態。

### 10.5 審批 API

#### `POST /v1/approval-requests`

建立審批請求。

#### `POST /v1/approval-requests/{approval_id}/decision`

批准或拒絕。

### 10.6 Context API

#### `GET /v1/context/{task_id}/views/{view_id}`

讀取被授權上下文視圖。

#### `POST /v1/context/{task_id}/writes`

提交共享上下文版本化寫入。

### 10.7 審計 API

#### `GET /v1/audit/events/{event_id}`

查詢單一事件。

#### `POST /v1/audit/replay`

依任務、時間區間或 session 進行鑑識重放。

## 11. 資料模型

### 11.1 核心資料表

| Table | 用途 |
| --- | --- |
| `principals` | 系統主體 |
| `principal_credentials` | 認證資訊與信任來源 |
| `roles` | 角色定義 |
| `role_bindings` | 主體與角色綁定 |
| `capabilities` | 能力目錄 |
| `capability_grants` | 任務期能力核發 |
| `tasks` | 任務主檔 |
| `task_assignments` | 任務與主體指派 |
| `container_sessions` | 容器 session |
| `execution_requests` | 執行請求 |
| `approval_requests` | 審批請求 |
| `approval_decisions` | 審批決定 |
| `context_documents` | 共享上下文文件 |
| `context_versions` | 共享上下文版本 |
| `audit_events` | 追加式稽核事件 |
| `artifacts` | 證據、輸出、封存物 |
| `revocations` | token/session/capability 撤銷紀錄 |

### 11.2 `tasks`

建議欄位：

- `task_id`
- `task_type`
- `submitted_by_principal_id`
- `risk_level`
- `state`
- `scope_descriptor`
- `context_root_id`
- `created_at`
- `updated_at`
- `completed_at`

### 11.3 `execution_requests`

建議欄位：

- `request_id`
- `task_id`
- `session_id`
- `principal_id`
- `capability_id`
- `request_payload`
- `policy_decision`
- `execution_state`
- `approval_id`
- `adapter_job_id`
- `trace_id`
- `created_at`

### 11.4 `audit_events`

建議欄位：

- `event_id`
- `trace_id`
- `event_type`
- `principal_id`
- `task_id`
- `session_id`
- `resource_ref`
- `payload_digest`
- `occurred_at`
- `previous_event_hash`
- `event_hash`

## 12. Scoped Delegation Token

### 12.1 原則

- 短時效
- 任務綁定
- scope 綁定
- quota 綁定
- capability 綁定
- 可撤銷

### 12.2 Token Claims

```json
{
  "iss": "control-plane",
  "sub": "prn_01HXYZ...",
  "aud": "control-plane",
  "jti": "tok_01HXYZ...",
  "task_id": "task_01HXYZ...",
  "session_id": "ses_01HXYZ...",
  "role_id": "role_doc_writer",
  "capability_ids": ["repo.patch.apply", "doc.generate"],
  "scope": {
    "repo_id": "repo_shopbricks",
    "allowed_paths": ["docs/", "frontend/"]
  },
  "quota": {
    "repo.patch.apply": 5
  },
  "exp": 1773350400,
  "nbf": 1773349800
}
```

### 12.3 格式

優先使用 PASETO 或具可驗簽與可撤銷能力的短時效 JWT。撤銷狀態需落地於控制平面快取與資料庫。

## 13. 容器安全設計

### 13.1 網路

- 容器只允許連控制平面入口
- 容器不得直接對外連網
- 容器不得直接連模型供應商
- 容器不得直接連資料庫、儲存體、代碼倉庫、部署端點

### 13.2 執行基線

- non-root user
- read-only root filesystem
- `cap-drop=ALL`
- `no-new-privileges`
- seccomp / AppArmor / SELinux
- `/tmp` 採 tmpfs
- 禁用 Docker socket 掛載
- 禁用 hostPath 與特權模式

### 13.3 密鑰

- 容器不持有長期 API key
- 容器只持有短時效 session credential
- 所有 provider credential 由控制平面或專用 inference gateway 持有

### 13.4 工作區

若需提供檔案視圖，採以下兩種模式之一：

- 最嚴格：所有讀取都經控制平面代理
- 較實用：提供唯讀快照，任何寫入仍需經控制平面請求

## 14. 執行配接層設計

### 14.1 原則

- 配接器只接受核可請求
- 配接器不得自行放寬 scope
- 配接器輸出必須附證據
- 配接器必須支援 idempotency key

### 14.2 Repository Adapter

責任：

- 套用 patch
- 驗證 base commit
- 產出變更摘要
- 保存 diff artifact

禁止：

- 直接接受自由 shell
- 在未授權路徑寫入
- 隱式改寫未列入 patch 的檔案

### 14.3 Build/Test Adapter

責任：

- 在隔離環境執行白名單命令
- 收集 stdout/stderr
- 產出結構化結果

命令範圍必須白名單化，例如：

- `npm test`
- `npm run build`
- `dotnet test`
- `pytest`

## 15. 觀測性與重放

### 15.1 Trace

每個請求至少需要：

- `trace_id`
- `task_id`
- `session_id`
- `principal_id`
- `request_id`
- `approval_id`
- `adapter_job_id`

### 15.2 指標

- 任務完成率
- 申請被拒率
- 升權率
- 審批延遲
- 配接器失敗率
- 撤權時間
- 容器平均存活時間

### 15.3 重放

重放輸入至少包含：

- 任務時間範圍
- 上下文快照版本
- 容器 session
- 原始請求 payload
- 政策版本
- capability catalog 版本
- approval decision
- adapter result

## 16. 部署拓樸

### 16.1 建議部署

- `control-plane` 採多副本部署
- `policy-engine` 可獨立水平擴展
- `approval-service` 可獨立部署
- `shared-context-store` 使用 PostgreSQL + object storage
- `audit-store` 使用 append-only table + object storage + SIEM sink
- `container-orchestrator` 使用 Kubernetes Jobs 或 Nomad batch
- `execution-adapters` 獨立部署於受控網段

### 16.2 邏輯元件與技術建議

| 元件 | 建議技術 |
| --- | --- |
| Control Plane API | .NET / Node.js |
| Policy Engine | OPA 或自研規則引擎 |
| Queue | RabbitMQ / NATS / SQS 類型服務 |
| Shared Context Store | PostgreSQL |
| Artifact Store | S3/MinIO |
| Audit Sink | PostgreSQL + SIEM |
| Container Runtime | Kubernetes |

## 17. 高可用與災難恢復

### 17.1 高可用

- 控制平面需多副本與健康檢查
- 撤權服務不可單點
- context store 與 audit store 需主從或多區容災

### 17.2 DR 要求

- capability catalog 可版本化恢復
- audit log 不可遺失
- 未完成任務可重新指派
- 容器失效後可由共享上下文恢復工作

## 18. MVP 分期

### 18.1 第一階段

- 統一任務 API
- 統一 principal / role / capability 模型
- 控制平面可核發 scoped token
- Agent 容器只能對控制平面提請求
- 最小共享上下文服務
- 最小 audit trail
- 兩個 execution adapters：`repo-adapter`、`build-test-adapter`

### 18.2 第二階段

- 審批服務
- 風險分級策略
- capability catalog 版本控制
- 可重放鑑識工具
- context ACL 細粒度控制

### 18.3 第三階段

- 多租戶隔離
- 雙人批准與變更控制
- 高可用撤權平面
- 自動化策略測試
- 灰度發布與 canary

## 19. 實作約束

為維持設計一致性，後續實作必須遵守：

- Agent runtime 不得直接持有外部工具權限
- 所有能力都需落入 Capability Catalog
- 所有 token 都需 task-bound 且短時效
- 所有重大狀態變更都需有 audit event
- 所有高風險動作都需經 PDP 與信任錨
- 所有工作節點都必須可無狀態替換

## 20. 待確認議題

- 共享上下文是否允許唯讀快照直掛至容器
- 模型推理是否由控制平面直管，或由專用 inference gateway 管理
- 是否允許低風險自動批准在無人工參與下執行
- capability catalog 的版本升級是否允許任務中途切換
- audit event 是否需要鏈式雜湊與外部時間戳服務

## 21. 成功判準

當以下條件成立時，可視為本架構最小成功：

- 任一 agent 容器失效時，不會遺失任務核心狀態
- 任一 agent 容器被入侵時，無法直接存取外部工具與資料源
- 任一高風險操作都能追溯是誰、何時、基於何種權限與批准執行
- 任一工作可在不更換治理模型的前提下替換執行節點
- 人類與 AI 皆走同一套授權與稽核路徑
