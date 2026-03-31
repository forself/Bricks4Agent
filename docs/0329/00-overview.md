# Bricks4Agent 03/29 程式碼分析總覽

日期：2026-03-29
類型：全模組程式碼分析快照
權威等級：低（快照，非即時架構文件）

## 目的

本資料夾為 2026-03-29 全專案程式碼分析快照。涵蓋所有 C# 後端模組、JavaScript 前端模組、工具鏈、範本與範例專案。

如需即時架構資訊，請參閱：
- [CurrentArchitectureAndProgress-2026-03-26.md](../reports/CurrentArchitectureAndProgress-2026-03-26.md)

## 專案定位

`Bricks4Agent` 是一個以 Broker 為中心的受治理 AI 操作平台，主要層級包括：

1. **LINE 入口與操作者互動** — LINE Worker 接收 webhook、審核工作流、斜線指令
2. **Broker 治理協調** — BrokerService 16 步 PEP 管線、PolicyEngine 7 規則、ECDH 加密通道
3. **任務、意圖、記憶與策略控制** — 9 種意圖類型、角色權限、SharedContextEntry 文件導向狀態
4. **受治理執行與 Worker/函式路由** — 3 Workers（LINE/File/Browser）、TCP 二進位協定、三層調度策略
5. **產出物產生與遞送** — DefinitionTemplate 系統、DynamicPageRenderer、Google Drive 遞送、Broker 簽名下載 fallback
6. **部署與瀏覽器治理基礎工程** — Azure IIS 部署、Playwright 瀏覽器 Worker、容器生命週期管理

## 模組清單與分析文件

### 核心控制面 (Broker)

| 文件 | 模組 | 路徑 | 說明 |
|------|------|------|------|
| [01-broker.md](01-broker.md) | Broker 應用程式 | `packages/csharp/broker/` | ASP.NET Core Web API，70+ 端點、18 端點群組、PEP 管線 |
| [02-broker-core.md](02-broker-core.md) | Broker Core 核心邏輯 | `packages/csharp/broker-core/` | 26+ 領域模型、18+ 服務、16 步 PEP 流程、PolicyEngine |

### 基礎設施

| 文件 | 模組 | 路徑 | 說明 |
|------|------|------|------|
| [03-api-database-security.md](03-api-database-security.md) | API / 資料庫 / 安全 | `packages/csharp/api,database,security/` | 帳戶鎖定、MFA、稽核日誌、速率限制、BaseOrm (多資料庫)、BaseCache |
| [04-cache-subsystem.md](04-cache-subsystem.md) | 快取子系統 | `packages/csharp/cache-*/` | TCP 二進位協定、30+ OpCode、叢集堆疊（選主/複製/快照） |

### 執行層

| 文件 | 模組 | 路徑 | 說明 |
|------|------|------|------|
| [05-workers-and-execution.md](05-workers-and-execution.md) | Workers / SDK / Function Pool | `packages/csharp/workers,worker-sdk,function-pool/` | 3 Workers（LINE/File/Browser）、TCP Worker SDK、三層調度策略 |

### 支援模組

| 文件 | 模組 | 路徑 | 說明 |
|------|------|------|------|
| [06-logging-reporting-utils.md](06-logging-reporting-utils.md) | 日誌 / 報表 / 工具類別 | `packages/csharp/logging,reporting,utils/` | BaseLogger (5 輸出目標)、ClosedXML Excel 報表、DateTimeHelper/PaginationHelper |

### 品質保證

| 文件 | 模組 | 路徑 | 說明 |
|------|------|------|------|
| [07-tests.md](07-tests.md) | 測試套件 | `packages/csharp/tests/`, `packages/csharp/broker/verify/` | xUnit/Vitest/Playwright + broker verify，已覆蓋交付 fallback 與 `/proj` 生命周期 |

### 前端與工具鏈

| 文件 | 模組 | 路徑 | 說明 |
|------|------|------|------|
| [08-tools-templates-projects.md](08-tools-templates-projects.md) | 工具 / 範本 / 專案 | `tools/`, `templates/`, `projects/` | DefinitionTemplate 系統、Agent v2.0 多模型、品質驗證腳本套件 |
| [09-frontend-and-ui.md](09-frontend-and-ui.md) | 前端與 UI | `packages/javascript/browser/` | 70+ UI 元件、DynamicPageRenderer、TriggerEngine、管理介面 |

## 當前即時互動路徑

```
LINE webhook → line-worker → broker /api/v1/high-level/line/process → HighLevelCoordinator
```

重要：`--line-listen` 在 agent CLI 上為 legacy/開發用途，不是正式路徑。

## 相對於 0326 快照的變化

與 03/26 快照相比，03/29 的主要進展：

- **系統 Scaffold 迭代流程**：新增 iterative system scaffold packaging flow（`75cf1be`）
- **程式碼產出物執行**：支援 execute line code-gen artifacts 並持久化 sidecar state（`45a30ba`）
- **交通查詢整合**：新增 TDX 運輸查詢的日期時間擷取（`e9b04fe`）
- **系統 Scaffold 預設**：預設使用自定元件庫（`4d7447a`）
- **短指令確認**：支援 y/n 簡短確認（`38e1edb`）
- **Broker 下載 fallback**：新增 artifact download config 與 sidecar public URL resolver（`d779bad`）
- **Broker 簽名下載服務**：新增 signed artifact download service（`72c9d52`）
- **Public download endpoint**：新增 `/api/v1/artifacts/download/{artifactId}`（`9fcb829`）
- **Drive 失敗 fallback**：Google Drive 上傳失敗時改送 broker 簽名下載連結（`231e667`）
- **Project interview workflow**：新增 `/proj` 專案訪談、版本化 workflow-design PDF/JSON、`/ok` `/revise` `/cancel`
- **User-facing copy cleanup**：LINE 使用者可見 prompts 改為中英雙語且不暴露 `tool_page` / `mini_app` / `structured_app` / `template family`（`13f430e`, `d1f668a`）

## 成熟度總覽

| 模組 | 成熟度 | 說明 |
|------|--------|------|
| Broker / API 層 | ⬛⬛⬛⬛⬜ 高 | 70+ 端點、18 群組、PEP 管線、Handler 架構 |
| BrokerService / PEP | ⬛⬛⬛⬛⬜ 高 | 16 步治理管線、PolicyEngine 7 規則、ECDH+AES-GCM |
| LINE Worker | ⬛⬛⬛⬛⬜ 高 | 完整 webhook、審核工作流、通知輪詢、斜線指令 |
| File/Browser Worker | ⬛⬛⬛⬜⬜ 中 | 沙箱檔案操作、Playwright 瀏覽、功能完整但較新 |
| 資料庫 / ORM | ⬛⬛⬛⬜⬜ 中高 | BaseOrm 多資料庫、26+ 資料表、FTS5、但無遷移 |
| 安全模組 | ⬛⬛⬛⬜⬜ 中 | MFA/帳戶鎖定/稽核日誌/速率限制就位，儲存庫為記憶體實作 |
| 快取子系統 | ⬛⬛⬛⬜⬜ 中 | 叢集堆疊（選主/複製/快照）完整，缺 LRU、非同步複製有風險 |
| Worker SDK | ⬛⬛⬛⬜⬜ 中 | TCP 二進位協定、自動重連、精簡 3 檔案框架 |
| Function Pool | ⬛⬛⬛⬜⬜ 中 | 三層調度（Pool/Strict/Fallback）、容器生命週期管理 |
| 日誌模組 | ⬛⬛⬛⬜⬜ 中 | BaseLogger 1,100 行、5 輸出目標、非同步緩衝，已連結但未使用 |
| 報表 / Utils | ⬛⬛⬜⬜⬜ 基礎 | ClosedXML Excel、工具類別缺 .csproj 無法引用 |
| 測試套件 | ⬛⬛⬛⬜⬜ 中 | `broker-tests`、`broker/verify`、整合測試與 E2E Bridge 並存，已覆蓋交付、下載、redaction 與 middleware bypass |
| UI 元件庫 | ⬛⬛⬛⬛⬜ 高 | 70+ 元件、DynamicPageRenderer、TriggerEngine 8 行為 |
| 頁面/SPA 產生器 | ⬛⬛⬛⬜⬜ 中高 | DefinitionTemplate 系統、30 欄位類型 |
| Agent 執行環境 | ⬛⬛⬛⬜⬜ 中 | v2.0 多模型（Ollama/OpenAI/Gemini 等）、狀態機管線 |
| 管理介面 | ⬛⬛⬛⬛⬜ 高 | 7 分頁、35+ API 端點整合、零建置部署 |
