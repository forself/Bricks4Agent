# LINE 專案訪談代理實作計劃

> **For agentic workers:** 必須使用 `superpowers:subagent-driven-development`（建議）或 `superpowers:executing-plans` 逐 task 執行本計劃。步驟使用 checkbox (`- [ ]`) 語法追蹤。

**目標：** 新增一條專用的 `/proj` LINE 工作流程，執行任務範圍內的專案需求訪談，先經由 `project scale` 與 `template family` 逐步收斂，再將已確認的 assertions 編譯為版本化的 JSON/PDF 檢核產物，並等待使用者以 `/ok`、`/revise`、`/cancel` 明確表態。

## 目前狀態

這份計劃的主要任務已在第一階段訪談/審核範圍內完成，文件本身保留作為歷史實作計劃。

目前 `main` 的狀態：

- `/proj`、`#專案名稱`、規模收斂、網站結構方向收斂、review artifact generation 已可用
- `/ok`、`/revise`、`/cancel` 已可用
- LINE 對外 prompts 已是中英文且偏一般使用者文案
- 內部 scale/template 識別字仍只留在 broker 內部，不直接暴露給使用者
- 更深一層的需求訪談與自動實作交接仍是後續工作

**架構：** 在既有 broker high-level 路徑上擴充一個獨立的 `project_interview` 狀態機與每版本一張的 DAG。真相由 broker 擁有並持久化在 assertion documents 與 version-graph documents 中；LLM 只負責提出 interpretation 與 restatement options。已確認 assertions 會被編譯成 canonical `project-instance JSON`，再驅動 PDF review 文件與 artifact delivery。模板選擇則由既有 browser page-generator / component runtime 之上的 JSON catalog 驅動。

**技術棧：** C# /.NET 8 minimal API broker、既有 `HighLevelCoordinator` 與 artifact services、broker verify、`packages/csharp/tests` 下的 integration tests、`packages/javascript/browser` 下的 JSON template catalog、現有 SPA runtime/page-generator

---

## 學理設計理由

### 為什麼用狀態機

需求訪談不是自由聊天，而是一個協議化流程。狀態機可以明確限制每個 phase 可接受的指令、強制保留 approval checkpoint，並阻止自然語言歧義直接造成流程跳轉。從控制理論角度看，這是在機率性語義理解之外，再加上一層決定性的流程控制。

### 為什麼用每版本一張 DAG

JSON/PDF 不是單純對話摘要，而是由已確認 assertions 推導出的正式產物。每個版本各自使用一張 DAG，可以明確描述「哪些輸入事實」如何推導為「哪些輸出 artifact」。修訂時建立 `vN+1` 新圖，而不是偷偷修改舊圖，能保留 provenance 與 lineage。

### 為什麼記憶要用 confirmed restatements

長期對話記憶真正的問題不是 storage，而是壓縮後的語義漂移。這個設計把 canonical memory 定義成「已確認重述」而不是「滾動摘要」，符合 verification-first 的知識論模型：未確認 interpretation 只能暫存，不能升格為正式真相。

### 為什麼模板先行

既然後續實作受限於現有 component library 與 runtime，就不應把問題當成無邊界的 open-world synthesis。模板先行會提早縮小 hypothesis space，提升一致性，也讓後續規劃從「自由生成」變成「有界能力面上的受限配置」。

### 為什麼採 document-first 的 JSON-defined program

這個任務的 machine-readable 真相應該是「定義程式的文件」，不是把聊天直接拼接成手寫程式碼。這接近 model-driven engineering：訪談流程先產出結構化 IR，既有 page-generator/runtime 再負責解譯與渲染。這樣可以把意圖蒐集、結構定義、執行渲染清楚拆開。

### 為什麼要有中英文兩份計劃

這個功能同時涉及實作與架構審查。雙語文件讓 review 與討論的可及性更高，同時避免把精確的 code snippets 翻譯到失真。英文版保留 exact code blocks 與 shell commands；中文版保留相同 task 結構、理由與驗證邏輯，用於設計審查與團隊溝通。

---

## 語言版本

- 英文主計劃：`docs/superpowers/plans/2026-03-31-line-project-interview-agent.md`
- 繁體中文對應版：`docs/superpowers/plans/2026-03-31-line-project-interview-agent.zh-TW.md`

說明：
- 英文版是執行時的 canonical plan，包含精確 code blocks 與指令。
- 中文版必須與英文版保持相同的 task 編號、責任切分與驗證邏輯。
- 若兩份文件有內容衝突，以英文版的程式碼與命令為準，但必須同步回修中文版。

---

## File Map

### 新增

- `packages/csharp/broker/Services/ProjectInterviewModels.cs`
  - 定義 session state、assertion、restatement option、version-graph node/edge、project-definition DTO。
- `packages/csharp/broker/Services/ProjectInterviewStateService.cs`
  - 讀寫 `hlm.project-interview.*` 文件，提供 broker 擁有的 canonical task state API。
- `packages/csharp/broker/Services/ProjectInterviewStateMachine.cs`
  - 管 `/proj`、`/ok`、`/revise`、`/cancel` 與 phase progression。
- `packages/csharp/broker/Services/ProjectInterviewRestatementService.cs`
  - 將模型提案轉成 bounded explicit statement options，並附 conservative escape option。
- `packages/csharp/broker/Services/ProjectInterviewTemplateCatalogService.cs`
  - 載入 template manifests，依 project scale、modules、constraints 收斂候選模板。
- `packages/csharp/broker/Services/ProjectInterviewProjectDefinitionCompiler.cs`
  - 從 confirmed assertions 編譯 canonical `project_instance_definition` JSON 與 immutable per-version DAG。
- `packages/csharp/broker/Services/ProjectInterviewWorkflowDesignService.cs`
  - 建立 deterministic workflow-design view model 與 render manifest。
- `packages/csharp/broker/Services/ProjectInterviewPdfRenderService.cs`
  - 產出 versioned PDF，並寫入 task/version/digest metadata。
- `packages/csharp/tests/integration/Api/ProjectInterviewLifecycleTests.cs`
- `packages/csharp/tests/integration/Api/ProjectInterviewReviewTests.cs`
- `packages/javascript/browser/templates/catalog.json`
- `packages/javascript/browser/templates/*/template.manifest.json`
- `packages/javascript/browser/__tests__/templates/TemplateCatalog.test.js`

### 修改

- `packages/csharp/broker/Services/HighLevelCommandParser.cs`
- `packages/csharp/broker/Services/HighLevelCoordinator.cs`
- `packages/csharp/broker/Services/HighLevelLlmOptions.cs`
- `packages/csharp/broker/Services/LineArtifactDeliveryService.cs`
- `packages/csharp/broker/Program.cs`
- `packages/csharp/broker/appsettings.json`
- `packages/csharp/broker/appsettings.Development.example.json`
- `packages/csharp/broker/verify/Program.cs`
- `packages/javascript/browser/package.json`

### 重用，不做結構改造

- `packages/csharp/broker/Services/HighLevelDocumentArtifactService.cs`
- `packages/csharp/broker/Services/HighLevelMemoryStore.cs`
- `packages/javascript/browser/page-generator/PageDefinitionAdapter.js`
- `templates/spa/frontend/runtime/page-generator/DynamicPageRenderer.js`

---

## 任務總覽

### Task 1: 加入 command routing 與 session state machine

目的：
- 讓 `/proj`、`/ok`、`/revise`、`/cancel` 有正式語義。
- 將 project interview 從一般 high-level chat 明確分流。

檔案：
- Create: `ProjectInterviewModels.cs`
- Create: `ProjectInterviewStateMachine.cs`
- Modify: `HighLevelCommandParser.cs`
- Modify: `HighLevelCoordinator.cs`
- Modify: `Program.cs`
- Test: `broker/verify/Program.cs`

步驟：
- [ ] 先寫 failing verify tests，驗證 `/proj` 進入 `CollectProjectName`、project name accepted 後進 `ClassifyProjectScale`、`/cancel` 進 `Cancelled`
- [ ] 跑 `dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj`，確認因缺 type 而失敗
- [ ] 新增最小 `ProjectInterviewPhase` / `ProjectInterviewCommand` / `ProjectInterviewAdvanceReason` / `ProjectInterviewSessionState`
- [ ] 實作 `ProjectInterviewStateMachine`
- [ ] 在 `HighLevelCommandParser.cs` 加入 `/proj`、`/ok`、`/revise`、`/cancel`
- [ ] 在 `HighLevelCoordinator.cs` 早期分流 project interview path
- [ ] 再跑 verify，確認這批 state-machine 測試通過
- [ ] commit：`feat: add project interview command routing and state machine`

### Task 2: 加入 assertion persistence 與 restatement confirmation

目的：
- 將 canonical memory 改成 assertion documents
- 強制 explicit confirmation 才能 promotion

檔案：
- Create: `ProjectInterviewStateService.cs`
- Create: `ProjectInterviewRestatementService.cs`
- Modify: `ProjectInterviewModels.cs`
- Modify: `HighLevelCoordinator.cs`
- Modify: `Program.cs`
- Test: `broker/verify/Program.cs`
- Test: `ProjectInterviewLifecycleTests.cs`

步驟：
- [ ] 先寫 failing verify tests，驗證 conservative option 一定存在，且未確認內容不能 promotion
- [ ] 跑 verify，確認因缺 restatement/assertion types 而失敗
- [ ] 新增 `AssertionStatus`、`ProjectInterviewAssertion`、`RestatementOption`、`ProjectInterviewTaskDocument`
- [ ] 實作 `ProjectInterviewRestatementService`
- [ ] 用既有 `HighLevelMemoryStore` 做 `ProjectInterviewStateService`
- [ ] 在 coordinator 載入 task document 並建立 restatement options
- [ ] 新增 integration test，確認沒有 explicit confirmation 就不會出現 confirmed assertions
- [ ] 跑 verify + integration
- [ ] commit：`feat: add project interview assertion state and restatement flow`

### Task 3: 加入 project scale 與 template catalog narrowing

目的：
- 先判定 `tool_page / mini_app / structured_app`
- 再依 template catalog 收斂候選模板

檔案：
- Create: `ProjectInterviewTemplateCatalogService.cs`
- Create: `packages/javascript/browser/templates/catalog.json`
- Create: `packages/javascript/browser/templates/*/template.manifest.json`
- Create: `packages/javascript/browser/__tests__/templates/TemplateCatalog.test.js`
- Modify: `HighLevelCoordinator.cs`
- Modify: `appsettings.json`
- Modify: `appsettings.Development.example.json`

步驟：
- [ ] 先寫 failing JS test，驗證 catalog 含 8 個初始 template families
- [ ] 跑 `npm --prefix packages/javascript/browser run test -- TemplateCatalog.test.js`，確認因缺 catalog 失敗
- [ ] 新增 `catalog.json`
- [ ] 新增 8 個 template manifests
- [ ] 實作 `ProjectInterviewTemplateCatalogService`
- [ ] 在 coordinator 加入 `NarrowByScale(projectScale)`
- [ ] 補 config 的 `ProjectInterview:TemplateCatalogPath`
- [ ] 跑 JS tests + broker verify
- [ ] commit：`feat: add project interview template catalog narrowing`

### Task 4: 編譯 canonical project definition 與 version DAG

目的：
- 將 confirmed assertions 編譯成 canonical `project_instance_definition`
- 為每一版 review artifact 建立一張 immutable DAG

檔案：
- Create: `ProjectInterviewProjectDefinitionCompiler.cs`
- Modify: `ProjectInterviewModels.cs`
- Modify: `ProjectInterviewStateService.cs`
- Modify: `HighLevelCoordinator.cs`
- Test: `broker/verify/Program.cs`

步驟：
- [ ] 先寫 failing verify tests，驗證 compiler 能產出 version、template family、`project_instance_definition_json` node
- [ ] 新增 `VersionDagNode`、`VersionDagEdge`、`ProjectInterviewVersionDag`、`ProjectInstanceDefinition`
- [ ] 實作 `ProjectInterviewProjectDefinitionCompiler`
- [ ] 在 `ProjectInterviewStateService` 加入 `SaveVersionDagAsync(...)`
- [ ] 在 coordinator compile 完後持久化 DAG
- [ ] 跑 verify
- [ ] commit：`feat: compile project interview definition and version dag`

### Task 5: 生成 versioned PDF/JSON review artifacts 與 review commands

目的：
- 產出 `workflow-design.vN.pdf` 與 `workflow-design.vN.json`
- 讓 `/ok`、`/revise`、`/cancel` 真正控制 review lifecycle

檔案：
- Create: `ProjectInterviewWorkflowDesignService.cs`
- Create: `ProjectInterviewPdfRenderService.cs`
- Modify: `HighLevelCoordinator.cs`
- Modify: `LineArtifactDeliveryService.cs`
- Modify: `Program.cs`
- Test: `broker/verify/Program.cs`
- Test: `ProjectInterviewReviewTests.cs`

步驟：
- [ ] 先寫 failing verify tests，驗證 view model 帶 version/template metadata
- [ ] 新增 `WorkflowDesignViewModel`
- [ ] 實作 `ProjectInterviewWorkflowDesignService`
- [ ] 實作 `ProjectInterviewPdfRenderService`
- [ ] 在 coordinator 計算 JSON digest、render PDF、錄 artifact、走現有 delivery path
- [ ] 新增 review integration tests，驗證 `/revise` 會建立新版本且不覆蓋舊 graph
- [ ] 跑 verify + integration review tests
- [ ] commit：`feat: add project interview review artifact generation`

### Task 6: 完整驗證、文件同步、與安全檢查

目的：
- 對齊 spec、0329 文件、與實際 verify/test 狀態
- 確保對使用者輸出不暴露內部路徑

檔案：
- Modify: `packages/csharp/broker/verify/Program.cs`
- Modify: `docs/superpowers/specs/2026-03-30-line-project-interview-agent-design.md`
- Modify: `docs/0329/00-overview.md`
- Modify: `docs/0329/01-broker.md`
- Modify: `docs/0329/07-tests.md`

步驟：
- [ ] 在 verify 增加 path redaction assertions
- [ ] 跑完整驗證：
  - `dotnet build packages/csharp/ControlPlane.slnx -c Release --disable-build-servers -nodeReuse:false`
  - `dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj`
  - `dotnet test packages/csharp/tests/integration/Integration.Tests.csproj -v minimal`
  - `npm --prefix packages/javascript/browser run test`
- [ ] 更新 spec 與 `docs/0329`，反映 state machine + per-version DAG + template catalog
- [ ] commit：`docs: document project interview workflow and verification`

---

## 執行與同步規則

- 程式碼區塊與 shell commands：以英文版為 canonical source
- 中文版任務編號、檔案責任、驗證邏輯、commit 邏輯：必須與英文版一致
- 若英文版有新增/刪除 task，中文版需同步
- 若中文版審查提出設計變更，必須先回寫英文版，再保持兩份同步

## 自檢

### 規格覆蓋

- `/proj` 入口、review commands、task-scoped state machine：Task 1
- confirmed-restatement assertion model：Task 2
- scale classification、template-family narrowing、JSON template catalog：Task 3
- canonical project-instance JSON 與 per-version DAG：Task 4
- versioned PDF/JSON generation 與 review workflow：Task 5
- delivery safety、verification、docs sync：Task 6

### 無 placeholder

- 不保留 `TODO`、`TBD`、`implement later`
- 中文版不重新發明新 task，只翻譯既有結構

### 型別一致性

- `ProjectInterviewSessionState`
- `ProjectInterviewStateMachine`
- `ProjectInterviewTaskDocument`
- `ProjectInterviewRestatementService`
- `ProjectInterviewTemplateCatalogService`
- `ProjectInterviewProjectDefinitionCompiler`
- `ProjectInstanceDefinition`
- `ProjectInterviewVersionDag`

這些名稱必須在中英文文件中保持一致，避免後續規劃與實作對不上。
