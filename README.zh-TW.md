# Bricks4Agent

English version：

- [README.md](/d:/Bricks4Agent/README.md)

## 專案定位

`Bricks4Agent` 是一個正在往控制面發展的 broker-mediated AI operations prototype。

它現在已經不能準確地只被描述成：

- AI coding CLI
- page generator
- UI component library

這些子系統仍然存在，但目前 live 系統已經包含：

- LINE ingress
- broker-governed high-level routing
- 結構化 intent、memory、promotion gate
- governed execution
- 每位使用者的 managed workspace
- artifact generation 與 delivery
- browser governance groundwork
- Azure VM IIS deployment groundwork
- 本機 admin console

## 目前 canonical live 路徑

`LINE webhook -> ngrok public URL -> line-worker -> broker /api/v1/high-level/line/process`

目前本機 canonical sidecar 埠號：

- broker：`127.0.0.1:5361`
- line-worker webhook：`127.0.0.1:5357`

重要說明：

- `agent --line-listen` 已是 legacy/development-only
- 目前正式 LINE 路徑是 `line-worker -> broker high-level coordinator`

## 主要區域

### Broker 與控制面

- [broker](/d:/Bricks4Agent/packages/csharp/broker)
- [broker-core](/d:/Bricks4Agent/packages/csharp/broker-core)

### LINE ingress 與操作路徑

- [line-worker](/d:/Bricks4Agent/packages/csharp/workers/line-worker)

### Agent runtime 與 governed execution

- [tools/agent](/d:/Bricks4Agent/tools/agent)
- [tools/agent/container](/d:/Bricks4Agent/tools/agent/container)

### UI library 與 generation

- [ui_components](/d:/Bricks4Agent/packages/javascript/browser/ui_components)
- [page-generator](/d:/Bricks4Agent/packages/javascript/browser/page-generator)
- [templates/spa](/d:/Bricks4Agent/templates/spa)
- [tools/spa-generator](/d:/Bricks4Agent/tools/spa-generator)

### 文件與設計說明

- [docs/reports](/d:/Bricks4Agent/docs/reports)
- [docs/designs](/d:/Bricks4Agent/docs/designs)
- [docs/manuals](/d:/Bricks4Agent/docs/manuals)

## 模組入口文件

主要模組與子系統的入口文件：

- [packages/csharp/workers/line-worker/README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)
- [packages/csharp/workers/line-worker/README.zh-TW.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.zh-TW.md)
- [tools/agent/README.md](/d:/Bricks4Agent/tools/agent/README.md)
- [tools/agent/container/README.md](/d:/Bricks4Agent/tools/agent/container/README.md)
- [packages/javascript/browser/page-generator/README.md](/d:/Bricks4Agent/packages/javascript/browser/page-generator/README.md)
- [tools/spa-generator/README.md](/d:/Bricks4Agent/tools/spa-generator/README.md)
- [templates/spa/README.md](/d:/Bricks4Agent/templates/spa/README.md)
- [templates/spa/scripts/README.md](/d:/Bricks4Agent/templates/spa/scripts/README.md)
- [tools/static-server/README.md](/d:/Bricks4Agent/tools/static-server/README.md)
- [packages/csharp/reporting/README.md](/d:/Bricks4Agent/packages/csharp/reporting/README.md)
- [packages/csharp/reporting/ExampleHost/README.md](/d:/Bricks4Agent/packages/csharp/reporting/ExampleHost/README.md)
- [packages/csharp/database/BaseOrm/README.md](/d:/Bricks4Agent/packages/csharp/database/BaseOrm/README.md)

## 子專案與範例專案

代表性的 sample / generated project 入口：

- [projects/ShopBricks-Gen/README.md](/d:/Bricks4Agent/projects/ShopBricks-Gen/README.md)
- [projects/ShopBricks-Gen/scripts/README.md](/d:/Bricks4Agent/projects/ShopBricks-Gen/scripts/README.md)
- [projects/ShopBricks/scripts/README.md](/d:/Bricks4Agent/projects/ShopBricks/scripts/README.md)

## 目前高階模型

LINE 高階回應層目前設定為：

- provider：`openai-compatible`
- model：`gpt-5.4-mini`

這一層負責：

- 對話
- 需求澄清
- broker-mediated query synthesis
- execution-model suggestion

它和下游 execution-model request 是分開的。

## 目前高階互動語法

代表性指令包括：

- `?help` / `?h`
- `?search` / `?s`
- `?rail` / `?r`
- `?hsr`
- `?bus` / `?b`
- `?flight` / `?f`
- `?profile` / `?p`
- `/name` / `/n`
- `/id` / `/i`
- `#ProjectName`
- `confirm`
- `cancel`

## 快速開始

### Canonical 本機 sidecar 路徑

請看：

- [packages/csharp/workers/line-worker/README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)
- [packages/csharp/workers/line-worker/README.zh-TW.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.zh-TW.md)
- [docs/manuals/line-sidecar-runbook.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.md)
- [docs/manuals/line-sidecar-runbook.zh-TW.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)

canonical 啟動命令：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
```

canonical 狀態命令：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
```

canonical 驗證命令：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -Message "hello"
```

### 本機 admin console

- `http://127.0.0.1:5361/line-admin.html`

若本機 DB 裡還沒有 admin credential，初始密碼是 `admin`，第一次登入必須修改。

## 目前的優點

- 控制面方向已經明確
- 有真實可用的 LINE live ingress path
- 有明確 command grammar 與 workflow gating
- raw log / interpretation / memory / execution intent 的分層已建立
- delivery 與 deployment 不再只是概念稿

## 目前的限制

- 各子系統成熟度仍不平均
- broker 是不可迴避節點，必須持續維持窄核心與清楚邊界
- browser governance 還在 groundwork 階段，不是完整 browser automation platform
- deployment 與 delivery 雖已能用，但還未完全抽象成通用平台原語

## 建議閱讀順序

1. [docs/reports/CurrentArchitectureAndProgress-2026-03-26.md](/d:/Bricks4Agent/docs/reports/CurrentArchitectureAndProgress-2026-03-26.md)
2. [packages/csharp/workers/line-worker/README.zh-TW.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.zh-TW.md)
3. [docs/manuals/line-sidecar-runbook.zh-TW.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)
4. `docs/designs` 內的子系統文件

## 文件入口

### 現況與架構

- [CurrentArchitectureAndProgress-2026-03-26.md](/d:/Bricks4Agent/docs/reports/CurrentArchitectureAndProgress-2026-03-26.md)

### 手冊

- [User Guide](/d:/Bricks4Agent/docs/manuals/user-guide.md)
- [Engineer Guide](/d:/Bricks4Agent/docs/manuals/engineer-guide.md)
- [Engineer Guide (EN)](/d:/Bricks4Agent/docs/manuals/engineer-guide-en.md)
- [LINE Sidecar Runbook](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.md)
- [LINE Sidecar 操作手冊](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)

### 設計說明

- [HighLevelModelRoutingAndMemory.md](/d:/Bricks4Agent/docs/designs/HighLevelModelRoutingAndMemory.md)
- [HighLevelMemoryAndLoggingModel.md](/d:/Bricks4Agent/docs/designs/HighLevelMemoryAndLoggingModel.md)
- [ToolSpecRegistry.md](/d:/Bricks4Agent/docs/designs/ToolSpecRegistry.md)
- [GoogleDriveDelivery.md](/d:/Bricks4Agent/docs/designs/GoogleDriveDelivery.md)
- [AzureVmIisDeployment.md](/d:/Bricks4Agent/docs/designs/AzureVmIisDeployment.md)
