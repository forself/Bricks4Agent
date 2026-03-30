# LINE 專案訪談代理設計

Date: 2026-03-30  
Status: design draft for review

## 學理設計理由

### 為什麼這是一個協議化訪談系統

這不是一般聊天，而是必須在不確定條件下收斂到可執行設計產物的需求訪談流程。若讓系統保持完全開放式對話，語義彈性雖高，但 ambiguity、drift、與隱性狀態變異也會同步放大，因此必須協議化。

### 為什麼用狀態機

整個流程有明確 phase、特權指令與終止狀態。狀態機能把「模型對語意的機率性理解」和「流程控制的決定性約束」分開，讓 `/proj`、`/ok`、`/revise`、`/cancel` 不會因自然語言波動而失控。

### 為什麼 memory 要用 confirmed restatements 與 assertions

自由摘要式記憶的核心問題是壓縮後會漂移，而且會隨輪次累積。這個設計把 canonical memory 定義成「已確認的重述與斷言」，而不是持續滾動摘要。未確認 interpretation 只能停留在 provisional 狀態，不能直接升格為正式需求。

### 為什麼用每版本一張 DAG

review artifacts 是編譯後輸出，不只是聊天紀錄。每個版本各自對應一張 DAG，可以保留 provenance，說清楚哪些 confirmed assertions 推導出哪些 JSON、PDF 與 delivery artifacts。修訂時建立新圖，不回寫舊圖。

### 為什麼 template-first

既有實作能力已受限於 component library、page-generator 與 runtime，因此沒必要把問題當成 open-world generation。template-first narrowing 會提早收縮 hypothesis space，把問題轉成「在已知能力面上的受限配置」。

### 為什麼採 document-first 的 JSON-defined program

訪談的 machine-readable 真相應是結構化文件，而不是把聊天直接拼成手寫程式碼。這是 model-driven engineering 的做法：先產出中介結構表示，再交由既有 runtime 解譯與執行。

### 為什麼要有中英文兩份 spec

此功能同時需要精確實作交接與深度架構討論。雙語 spec 可以提升 review 可及性；英文版保留 exact naming 的 canonical 地位，中文版負責完整傳達架構、限制與決策脈絡。

## 語言版本

- 英文主 spec：`docs/superpowers/specs/2026-03-30-line-project-interview-agent-design.md`
- 繁體中文對應版：`docs/superpowers/specs/2026-03-30-line-project-interview-agent-design.zh-TW.md`

規則：
- 英文版是 exact type/file naming 的 canonical source
- 中文版必須維持相同架構、限制、review checkpoint
- 若兩份內容衝突，以英文 naming 為準，但需同步回修中文版

## Goal

新增一個專用的 LINE task-mode agent，從 `/proj` 進入，執行任務範圍內的軟體專案需求訪談，檢測缺漏與衝突需求，並產出可下載的 workflow design 文件供使用者審查。

這是第一階段而已。  
第一版不自動實作專案。

第一版的任務是：

- 訪談使用者
- 將需求正規化為結構化狀態
- 產出架構與實作計畫輸出
- 生成版本化 workflow design 文件
- 生成對應的結構化 JSON artifact
- 讓使用者在 LINE 內審查並明確批准

## 為什麼是獨立 task type

這個能力不應塞進一般 LINE 對話，也不應混入現有 `system_scaffold`。

因為它需要：

- 明確 task entry
- 高階模型做 requirement elicitation
- 任務範圍內記憶，不重用跨任務人格
- 決定性的 completeness/conflict checks
- 版本化設計文件生成
- 在任何 implementation workflow 前的硬性 approval gate

新的 task type：

- `project_interview`

## Scope

### Version one 包含

- `/proj` 明確入口
- 建立 project interview session
- project name 唯一性檢查
- task-scoped assertion memory
- project scale classification
- template-family selection
- JSON-defined program generation
- gap/conflict detection
- workflow design 文件生成
- 面向使用者的版本化 PDF
- 面向 broker 的版本化 JSON
- Google Drive upload 與 LINE delivery
- 明確 review commands：
  - `/ok`
  - `/revise`
  - `/cancel`

### Version one 不包含

- 真正專案實作
- 自主 code execution
- 長期 user memory
- 重用舊專案偏好
- 跨任務 personalization
- 任意自然語言 approval detection

## Entry And Routing

### 明確入口

這個 agent 不能從一般對話隱式進入。  
Version one 唯一支援的入口是：

- `/proj`

### Routing 規則

- 一般 LINE chat 維持在既有 high-level conversation path
- `/proj` 轉進 `project_interview`
- 進入 active session 後，後續訊息都綁在該 session，直到：
  - confirmed
  - cancelled
  - expired

### Review commands

文件生成後，只允許下列明確指令改變狀態：

- `/ok`
- `/revise`
- `/cancel`

## Core Constraints

### 只保留 task-scoped memory

它不是一般聊天人格。  
除 broker 原本就必須保留的 audit/artifact records 外，不保留任何跨任務 user memory。

### 高級模型要求

requirement elicitation、contradiction detection、design synthesis 必須使用高級模型。  
但 broker 不能直接信任模型輸出。

因此 version one 明確分離：

- 高級 interview/synthesis model
- broker 端 deterministic validators

### Superpowers-style process compliance

雖然 LINE 裡不是真的在跑本地 Codex skill，但流程上必須模擬相同約束：

- reviewed design 出現前，不得進 implementation
- 使用者沒有明確確認前，不得轉進下游 implementation workflow
- 輸出必須明講 testing expectations
- workflow design 中必須明講 TDD 是下游規則
- gaps/conflicts 在設計定稿前必須解決

## High-Level Architecture

整體 flow 仍然掛在既有 broker high-level 系統上：

`LINE -> line-worker -> broker /api/v1/high-level/line/process -> HighLevelCoordinator -> project_interview services`

建議元件：

- `/proj` command/parser extension
- `HighLevelCoordinator` 內的 `project_interview` 路由
- assertion-state store
- interview progression service
- template-library selection service
- workflow design generator
- PDF render service
- 現有 `LineArtifactDeliveryService`

broker 仍負責：

- task/session state
- storage
- validation
- artifact creation
- delivery
- approval gating

模型負責：

- 提出下一個最值得問的問題
- 從使用者回答中抽取結構化語意
- 建議架構與 task flow

## Session And State Model

這個功能不能只靠單一抽象。  
Version one 應採：

- session-level state machine
- per-version directed acyclic graph (DAG)

前者控制 phase 與 command admissibility，後者控制某一版文件的依賴真相。

### Session state 應追蹤

- `current_phase`
- `current_question_id`
- `project_name`
- `project_folder_name`
- `project_scale`
- `candidate_template_families`
- `selected_template_family`
- `selected_modules`
- `selected_style_profile`
- `document_version`
- `review_state`
- `latest_pdf_artifact_id`
- `latest_json_artifact_id`
- `open_gaps`
- `open_conflicts`
- `pending_revision_request`

### 建議 phase

1. `idle`
2. `collect_project_name`
3. `classify_project_scale`
4. `narrow_template_family`
5. `confirm_template_family`
6. `collect_template_requirements`
7. `resolve_conflicts`
8. `compile_project_definition`
9. `render_review_artifacts`
10. `await_user_review`
11. `revise_requested`
12. `confirmed`
13. `cancelled`
14. `failed`

### 轉移規則

- `/proj`：`idle -> collect_project_name`
- project name 合法且唯一後，才能離開 `collect_project_name`
- required assertion bundles 若仍 unresolved/conflicted，不得進 `compile_project_definition`
- canonical project-instance JSON build 成功前，不得進 `render_review_artifacts`
- 沒有 reviewable document version 且沒有 `/ok`，不得進 `confirmed`
- `/revise` 會進 `revise_requested`，再回到適當的收集 phase
- `/cancel` 可把 active non-terminal phase 轉進 `cancelled`

## Per-Version DAG Model

每個 review 版本 `vN` 都應有一張自己的 DAG。  
revision 不改舊 DAG，而是建立新的 `vN+1 DAG`。

### DAG 的目的

- 表示哪些 assertions 已確認
- 表示哪些 derived choices 依賴哪些 assertions
- 表示哪些 JSON/PDF/links 由哪些 canonical inputs 產生

### 建議節點

- `raw_user_messages`
- `interpreted_candidates`
- `restatement_options`
- `user_selection_or_correction`
- `confirmed_assertions`
- `project_scale`
- `template_family_candidates`
- `selected_template_family`
- `selected_modules`
- `style_profile`
- `constraints`
- `project_instance_definition_json`
- `workflow_design_view_model`
- `workflow_design_pdf`
- `artifact_bundle`
- `delivery_links`

## Requirement Memory Model

task-scoped memory 應拆成多層：

1. `conversation log`
2. `working memory`
3. `assertion registry`
4. `structured project definition`
5. `design artifact state`

關鍵規則：
- canonical state 不是自由摘要
- canonical state 來自 confirmed restatements 與 assertions

### Assertion registry

最小真值單位是 assertion。  
使用者不直接面對裸 assertion；系統會把多個 assertions 組成 explicit statement options 給使用者確認。

每個 assertion 至少帶：

- `assertion_id`
- `statement`
- `status`
- `evidence`
- `updated_at`

狀態：

- `missing`
- `candidate`
- `confirmed`
- `rejected`
- `superseded`
- `conflicted`

## Restatement And Confirmation Protocol

核心規則：

1. user input
2. system interpretation
3. explicit restatement
4. user confirmation or correction
5. assertion-state update

### 對使用者的互動單位

內部真值單位是 assertion。  
對外互動單位是由 1 到多個 assertions 組成的 explicit statement。

每輪應偏好：

- 1 到 3 個 explicit statement options
- 1 個 conservative escape option，例如：
  - none of these is precise
  - closest to A, but I need to revise it

### 硬規則

未確認 interpretation 不得 promotion 到 canonical memory。

## Required Requirement Schema

固定 schema 仍然需要，但這個 schema 不是直接靠 slot filling，而是由 confirmed assertions 填充。

至少包含：

- `project_name`
- `project_scale`
- `template_family`
- `project_goal`
- `target_users`
- `enabled_modules`
- `disabled_modules`
- `core_user_flows`
- `data_entities`
- `auth_profile`
- `external_integrations`
- `deployment_target`
- `style_profile`
- `non_functional_requirements`
- `acceptance_criteria`
- `constraints`
- `out_of_scope`

## Project Scale Classification

在深挖需求前，必須先判定專案尺度：

- `tool_page`
  - 單功能頁或小工具
  - 走 lightweight UI composition DSL
- `mini_app`
  - 少量頁面與基本資料流
  - 走 compact structure DSL
- `structured_app`
  - 多模組、多流程
  - 走 full workflow/structure DSL

## Template Library Strategy

模板應位於既有 component library 之上，作為 large reusable components。  
模板不應先是手寫 project code；第一版應優先定義成 JSON-defined programs。

建議初始 template families：

- `content_showcase`
- `form_collection`
- `member_portal`
- `list_search`
- `crud_admin`
- `dashboard`
- `multi_step_flow`
- `transaction_flow`

## JSON-Defined Program Model

canonical project definition 應該是 document-first。  
建議三層 JSON：

1. `template_definition`
2. `module_definition`
3. `project_instance_definition`

既有執行基底：

- `packages/javascript/browser/page-generator`
- `packages/javascript/browser/ui_components`
- `templates/spa/frontend/runtime/page-generator`
- `templates/spa/frontend/core`

## Interview Script

預設流程應改成 template-first：

1. project name
2. project scale classification
3. candidate template family narrowing
4. primary template family selection
5. project goal and target users
6. enabled/disabled modules
7. key user flows
8. data and persistence
9. auth and permissions
10. external integrations
11. deployment target
12. style profile
13. non-functional requirements
14. acceptance criteria
15. constraints and out-of-scope

規則：
- 一次一個主問題
- 偏好 explicit statement options
- 優先解 `conflicted`
- 再 confirm `candidate assertions`
- 再補 `missing`

## Gap And Conflict Detection

broker 不只接受模型提案，還要跑 deterministic validation。

例子：
- template family 已選，但沒有 enabled modules
- auth required 但無 login/role model
- `project_scale = tool_page` 卻要求 structured multi-role workflow
- `template_family = content_showcase` 卻要求 CRUD admin

## Workflow Design Output

workflow design 是必經檢核點。  
內容應含：

- project overview
- project scale classification
- selected template family and rationale
- selected/excluded modules
- normalized requirements summary
- architecture proposal
- component/module breakdown
- data model summary
- external integration summary
- deployment target summary
- DSL path summary
- implementation phases
- detailed task flow table
- testing strategy with TDD expectation

## PDF And JSON Output Model

### 對使用者

- `workflow-design.vN.pdf`

### 對 broker

- `workflow-design.vN.json`

這個 JSON 應是 derived `project-instance definition`，不是 slot dump。

### Render pipeline

1. confirmed assertion state
2. version DAG build
3. canonical project-instance JSON
4. deterministic document view-model
5. deterministic intermediate markdown/html
6. PDF render

### 驗證策略

1. schema completeness check
2. render manifest check
3. section coverage check

另外：
- artifact state 需記錄 JSON digest
- artifact pair 需記錄 source DAG id/digest

## Delivery Behavior

使用既有 artifact-delivery path。

建議交付：

- user-facing PDF
- machine-readable JSON
- 可選 zip（pdf + json）

若 Google Drive 成功：
- 維持 Drive-first

若失敗：
- 用既有 broker signed download fallback

## Commands During Review

- `/ok`
- `/revise`
- `/cancel`

可選：
- `/status`
- `/latest`

## Data Persistence

建議 document families：

- `hlm.project-interview.state.{channel}.{userId}`
- `hlm.project-interview.requirements.{channel}.{userId}`
- `hlm.project-interview.review.{channel}.{userId}`
- `hlm.project-interview.version-graph.{channel}.{userId}.{version}`

## Model And Prompting Policy

interview model prompt 必須強制：

- task-scoped memory only
- no user-history reuse
- one primary question at a time
- explicit restatement before canonical write
- extraction into assertions and confirmed bundles
- template-first narrowing
- no implementation promises
- no approval inference without `/ok`
- no skipped gaps/conflicts

## Error Handling

### duplicate project name

- 不前進
- 留在 project-name phase

### incomplete requirements

- 不生成 workflow design
- 持續追問

### render failure

- 標記 artifact generation failure
- 保留 assertion state 與 canonical JSON inputs

### delivery failure

- 使用現有 fallback
- 不得遺失 version state

## Testing Strategy

Version one 應採 TDD。  
至少驗證：

1. `/proj` 正確路由
2. 一般 chat 不會誤入
3. duplicate project name 會阻擋
4. project scale classification 正確選 DSL path
5. template-family narrowing 回 bounded candidate set
6. restatement confirmation 只有 explicit confirm 才 promotion
7. required assertions/fields 未完成前不得生成文件
8. versioned JSON 會生成
9. versioned PDF metadata 會生成
10. `/ok` 只有在存在 reviewable version 時有效
11. `/revise` 建新版本，不覆蓋 approval state
12. `/cancel` 正確關閉 task
13. review artifacts 能被交付
14. 對使用者輸出不暴露內部 managed-workspace paths

## Rollout Strategy

### Stage 1

只做 interview-and-review workflow：

- `/proj`
- assertion-based interview state
- scale/template/DSL selection
- document generation
- PDF/JSON delivery
- explicit review gate

### Stage 2

再接下游 implementation agent，消費已批准的 `project_interview` 輸出，建立 unique project folder、依 TDD 實作並最終打包。

## Acceptance Criteria

version one 完成條件：

- LINE user 可輸入 `/proj`
- broker 啟動 task-scoped interview session
- session 能完成 scale classification 與 template family selection
- 所有 required fields 透過 confirmed restatements 收齊
- duplicate project names 會被阻擋
- gap/conflict 在生成文件前能被偵測
- broker 生成 versioned workflow design PDF
- broker 生成 matching versioned project-definition JSON
- artifacts 被記錄與交付
- user 可用 `/ok`、`/revise`、`/cancel`
- 不重用 long-term user memory
- 不會 silently transition into implementation
