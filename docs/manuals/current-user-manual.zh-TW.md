# Bricks4Agent 目前版本使用手冊

Date: 2026-06-18  
Status: current operator and user manual  
Audience: LINE 使用者、系統管理者、在本機操作 sidecar 的工程/營運人員

## 1. 這套系統是什麼

Bricks4Agent 目前是一套以 broker 為中心的受治理 AI 操作平台。它不是單純的聊天機器人、CLI agent、頁面產生器或 UI 元件庫；這些都是平台內的子系統。核心設計是：

```text
使用者意圖 -> broker 解讀與驗證 -> 受控能力/工作流程 -> artifact 或外部動作
```

目前主要可用入口是 LINE sidecar：

```text
LINE webhook -> public tunnel -> line-worker -> broker /api/v1/high-level/line/process
```

重要界線：

- `line-worker -> broker high-level coordinator` 是目前 canonical LINE 路徑。
- `agent --line-listen` 只保留為 legacy/development-only，不是正式操作路徑。
- Podman governed agent stack 用於驗證受控 agent/container 路徑，不等於整個 LINE/Drive/部署系統。
- 高階對話模型與受控 agent 執行模型是分開設定的。

## 2. 角色與常見任務

| 角色 | 會做的事 | 主要入口 |
| --- | --- | --- |
| LINE 使用者 | 對話、查詢、建立需求草稿、確認/取消任務、查看審批連結 | LINE |
| 本機管理員 | 啟動 sidecar、查看狀態、登入後台、處理審批、重送通知/交付 | PowerShell、`line-admin.html` |
| 工程人員 | 建置、測試、執行 worker、驗證 governed stack、維護設定 | repo root、CLI、Podman |
| 受控 agent 測試者 | 驗證 agent 僅能透過 broker 取用模型與工具 | `tools/agent`、Podman stack |

## 3. 核心概念

### 3.1 Broker

Broker 是控制平面。它負責：

- 接收 LINE / agent / admin / worker request。
- 維護 principal、role、task、session、capability、grant、approval、audit。
- 判斷使用者或 agent 是否能執行某個 capability。
- 將允許的執行請求派發給 in-process handler 或 FunctionPool worker。
- 管理 artifact、Google Drive delivery、browser/deployment/admin surface。

### 3.2 High-Level Coordinator

High-Level Coordinator 是 LINE 高階互動層。它負責：

- 一般對話。
- 顯式查詢命令，例如 `?search`、`?rail`。
- 生產型需求的草稿、確認、取消、promotion gate。
- 專案訪談流程。
- 使用者 profile 與 managed workspace。

### 3.3 Managed Workspace

每個使用者有自己的工作區，邏輯形狀為：

```text
{AccessRoot}/{channel}/{userId}/{conversations|documents|projects}
```

在 sidecar 模式中，常見實際路徑位於：

```text
.run/line-sidecar/broker/managed-workspaces
```

### 3.4 Approval

高風險、scope 逸出、跨使用者或外部發佈/部署類動作不應由 agent 自行決定。Broker 會建立 approval request，並由：

- admin 在 `line-admin.html` 審批。
- 使用者透過 LINE 收到的短效簽章連結進入 `user-approvals.html` 審批。

目前 MVP 是單一 approver，不是雙人審批。

## 4. 最小安裝需求

建議最低環境：

| 工具 | 用途 |
| --- | --- |
| Windows 10/11 | canonical local LINE sidecar |
| Windows PowerShell 5.1+ | sidecar 腳本 |
| .NET SDK 8.0+ | broker / worker 建置與測試 |
| Node.js 18+ 與 npm | agent、JS tools、UI tests |
| Git | repo 操作 |
| ngrok | LINE webhook public tunnel |
| Podman | governed agent container stack |
| Playwright Chromium | browser worker / browser tests |
| Ollama | local model、embedding、host Ollama stack |

外部整合所需額外憑證：

- LINE Channel Access Token / Channel Secret。
- Anthropic Claude API key (`ANTHROPIC_API_KEY`)；若未設定，仍可使用 OpenAI-compatible API key。
- Google OAuth client JSON 或 service account。
- TDX Client ID / Client Secret。
- Azure VM IIS + WinRM 憑證。

## 5. 第一次本機設定

在 repo root：

```powershell
npm install
dotnet restore packages/csharp/ControlPlane.slnx
dotnet build packages/csharp/ControlPlane.slnx
```

如果要跑 browser/e2e：

```powershell
npx playwright install chromium
```

如果要直接跑 `packages/javascript/browser` 的測試：

```powershell
cd packages/javascript/browser
npm install
cd ..\..\..
```

## 6. 本機 secrets 與設定

不要把 credentials commit 進 repo。

建議 secrets 目錄：

```powershell
C:\secure\Bricks4Agent
```

可用環境變數覆寫：

```powershell
$env:BRICKS4AGENT_SECRETS_DIR = 'D:\secure\Bricks4Agent'
```

常見檔案：

| 檔案 | 用途 |
| --- | --- |
| `Api.txt` | OpenAI-compatible fallback key；當 `ANTHROPIC_API_KEY` 未設定時 sidecar 注入 broker `HighLevelLlm.ApiKey` |
| `client_secret_*.json` | Google delegated OAuth |
| `worker-auth.json` | sidecar 產生/維護 worker identity credentials |

目前 sidecar 會優先讀取環境變數 `ANTHROPIC_API_KEY`，並將 broker high-level 與 agent-facing `LlmProxy` 設為 `anthropic` / `claude-sonnet-4-6`。若沒有 `ANTHROPIC_API_KEY`，才使用 `Api.txt` 的 OpenAI-compatible fallback。

LINE worker 本機設定：

```powershell
Copy-Item .\packages\csharp\workers\line-worker\appsettings.sidecar.example.json `
  .\packages\csharp\workers\line-worker\appsettings.json
```

至少填入：

- `Line.ChannelAccessToken`
- `Line.ChannelSecret`
- `Line.DefaultRecipientId`
- `Line.AllowedUserIds`

## 7. 啟動 LINE Sidecar

### 7.1 啟動

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
```

Sidecar 會：

1. 建立 `.run/line-sidecar` runtime workspace。
2. publish broker 與 line-worker。
3. 載入 high-level API key、Google OAuth、worker auth 等 runtime override。
4. 啟動 broker：`127.0.0.1:5361`。
5. 啟動 line-worker webhook：`127.0.0.1:5357`。
6. 啟動或沿用 ngrok tunnel。
7. 更新 LINE webhook URL，除非使用 `-SkipWebhookUpdate`。
8. 檢查 broker、line-worker、tunnel ready。

### 7.2 狀態

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
```

應確認：

- broker PID 存在。
- line-worker PID 存在。
- ngrok public URL 存在。
- LINE webhook endpoint active。
- admin console 可開啟。

### 7.3 驗證

直接驗證 broker high-level：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify-broker -UserId test-user -Message "hello"
```

驗證 live webhook：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -Message "hello"
```

遇到 shell 中文編碼問題時，改用 UTF-8 base64：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify-broker `
  -UserId test-user `
  -MessageBase64Utf8 <base64-utf8-message>
```

### 7.4 重啟與停止

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 restart
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 down
```

## 8. 使用 LINE 互動

### 8.1 基本對話

直接傳一般訊息即可。Broker 會走 high-level conversation path。當問題比較適合受控搜尋或查詢時，系統會引導你使用明確前綴命令。

### 8.2 查詢命令

| 指令 | 快捷 | 用途 | 範例 |
| --- | --- | --- | --- |
| `?help` | `?h` | 查看規則 | `?help` |
| `?search` | `?s` | 受控網路搜尋 | `?search 台灣 AI 法規趨勢` |
| `?rail` | `?r` | 台鐵/火車查詢 | `?rail 台北 台中 今天 18:00` |
| `?hsr` | | 高鐵查詢 | `?hsr 台北 左營 明天上午` |
| `?bus` | `?b` | 公車/客運查詢 | `?bus 台北 宜蘭 明天` |
| `?flight` | `?f` | 航班查詢 | `?flight TPE KIX tomorrow` |
| `?profile` | `?p` | 查看個人 profile | `?profile` |

目前建議使用顯式查詢命令。一般問題不保證自動選用即時查詢工具。

### 8.3 個人設定

| 指令 | 快捷 | 用途 | 範例 |
| --- | --- | --- | --- |
| `/name` | `/n` | 設定顯示稱呼 | `/name 小布` |
| `/id` | `/i` | 設定英數字使用者 ID | `/id bricks001` |

使用者 ID 只能包含英文字母與數字，長度需介於 3 到 32。

### 8.4 生產型需求與確認

當你提出「建立、產生、修改、修復、部署、交付」之類的需求，broker 會先建立 draft，而不是立刻執行。

典型流程：

1. 使用者提出需求，例如 `/create a website prototype`。
2. 系統要求專案名稱。
3. 使用者用 `#ProjectName` 回覆。
4. 系統整理 draft 和預計行動。
5. 使用者回覆 `confirm`、`yes`、`y`、`ok` 或 `確認`。
6. Broker 將合格 draft promotion 成 task / plan / handoff。
7. 若使用者回覆 `cancel`、`no`、`n` 或 `取消`，draft 取消。

專案名稱必須使用 `#` 或全形 `＃` 開頭：

```text
#MySite
```

### 8.5 專案訪談流程

專案訪談用於更結構化地收集需求並產生 artifact。

| 指令 | 用途 |
| --- | --- |
| `/proj` | 開始專案訪談 |
| `#ProjectName` | 指定專案名稱 |
| `/ok` | 確認目前訪談/設計 |
| `/revise` | 要求修訂 |
| `/cancel` | 取消訪談 |

典型流程：

1. 傳 `/proj`。
2. 用 `#ProjectName` 指定專案名。
3. 回答系統提出的需求問題。
4. 查看系統產出的摘要、設計、PDF/JSON review artifact。
5. 用 `/ok`、`/revise` 或 `/cancel` 決定下一步。

## 9. 使用本機管理後台

Sidecar 啟動後開啟：

```text
http://127.0.0.1:5361/line-admin.html
```

安全特性：

- local admin 僅供 localhost 操作。
- 若 local DB 沒有 admin credential，初始密碼為 `admin`。
- 第一次登入後會要求修改密碼。
- session 以 cookie 維持。

常用區域：

| 區域 | 功能 |
| --- | --- |
| 系統狀態 | broker、LLM proxy、embedding、RAG、DB 狀態 |
| LINE 使用者 | 使用者列表、註冊政策、權限 |
| 對話/工作流 | 對話紀錄、draft、handoff、execution intent |
| 審批 | 查看 pending approval，批准或拒絕 |
| Browser governance | site binding、user grant、lease、request build、execute |
| Deployment | Azure IIS target、preview、execute、execution history |
| Delivery | Google Drive OAuth、share、artifact delivery |
| Artifacts | 使用者文件與交付物、重試 Drive 或 LINE notification |
| Tool specs | 查看 broker tool/capability 規格 |
| Alerts | 觀測事件與系統警示 |

## 10. 審批操作

### 10.1 管理員審批

在 `line-admin.html` 的審批分頁：

1. 查看 request 細節。
2. 檢查 rendered content，例如 unified diff、payload、目標路徑、risk reason。
3. 輸入 reason。
4. 按 approve 或 reject。

Admin 層適用於：

- scope 逸出。
- High / Critical 風險。
- agent create/stop。
- 部署。
- 對外發佈。
- 自由 shell 類，現階段通常拒絕。

### 10.2 使用者審批

User 層 approval 會透過 LINE 傳短效簽章連結：

```text
http(s)://.../user-approvals.html#token=<signed>
```

使用者只能看見與決定自己 scope 內的 User 層 approval。連結會過期。

## 11. Artifact 與交付

系統可以產出：

- Markdown / JSON / PDF review artifact。
- 靜態網站套件。
- scaffold / project definition。
- web report。
- 其他由 high-level artifact service 產生的文件。

交付方式：

| 方式 | 說明 |
| --- | --- |
| Broker signed download | 短效簽章下載連結，不依賴 Google Drive |
| Google Drive shared delegated | 使用共用 delegated owner 上傳到 Drive |
| Google Drive user delegated | 使用 LINE 使用者自己的 delegated OAuth |
| Google Drive system account | 使用 service account，通常適合 Shared Drive |
| LINE notification queue | broker queue notification，line-worker 取出後送 LINE |

若 Drive 或通知失敗，可在 admin console 重試。

## 12. Governed Agent Container 使用時機

Podman governed stack 用於驗證「agent 不能直接拿工具或模型，只能透過 broker」。

它適合驗證：

- agent bootstrap。
- broker-issued runtime descriptor。
- capability / scope-gated execution。
- broker LLM proxy。
- worker container attachment。
- execution adapter patch/build/test path。

它不直接驗證：

- LINE webhook。
- admin console。
- Google Drive delivery。
- Azure IIS deployment。
- browser-governed production readiness。

### 12.1 Mock governed stack

```powershell
npm run validate:podman-governed-stack
```

預期能看到 deterministic `STACK_OK`，並覆蓋 broker-governed `read_file` 路徑。

### 12.2 OpenAI-compatible stack

Mock upstream：

```powershell
npm run validate:podman-openai-compatible-stack
```

真實 OpenAI-compatible provider：

```powershell
$env:OPENAI_BASE_URL="https://api.openai.com"
$env:OPENAI_API_KEY="<key>"
$env:OPENAI_API_FORMAT="responses"
$env:STACK_MODEL="gpt-5.4-mini"
npm run validate:podman-openai-compatible-stack
```

### 12.3 Host Ollama stack

先確認 Ollama 有模型：

```powershell
ollama list
```

執行：

```powershell
npm run validate:podman-ollama-host-stack
```

Live Ollama 驗證的是 broker-mediated round trip、agent completion、session close、無 broker/API error。逐字 sentinel 只由 mock stack 保證，因為不同本機模型會改寫回答。

### 12.4 Execution adapter stack

```powershell
node tools/agent/tests/test-podman-execution-adapter-stack.js
```

這會建立 throwaway fixture，讓模型透過 broker-governed chain 呼叫 `apply_patch`，最後確認 patch 真的由 execution-adapter-worker 套用。

## 13. Local Agent CLI

CLI 位於：

```text
tools/agent/agent.js
```

主要模式：

| 模式 | 說明 |
| --- | --- |
| local provider mode | agent 直接呼叫 Ollama/OpenAI-compatible provider |
| generation/pipeline mode | 產生 `project.json` 或 CRUD pipeline |
| governed mode | agent 只跟 broker 溝通，模型與工具都 broker-mediated |

One-shot 範例：

```powershell
node tools/agent/agent.js --run "Read AGENT.md and summarize the constraints"
```

Governed 範例：

```powershell
node tools/agent/agent.js `
  --governed `
  --broker-url http://127.0.0.1:5361 `
  --broker-pub-key <base64> `
  --principal-id prn_xxx `
  --task-id task_xxx `
  --role-id role_reader `
  --run "Inspect the repo"
```

Governed mode 中，`--provider`、`--api-key`、`--host` 不是 canonical 執行路徑；LLM health、model list、chat 都會透過 broker `/api/v1/llm/*`。

## 14. SPA / Page Generator / UI Library

這些是平台內的生成與前端工具，不是 LINE sidecar 本身。

### 14.1 SPA Generator Workbench

啟動 frontend：

```powershell
npm run serve
```

或：

```powershell
cd tools/spa-generator
node server.js
```

開啟：

```text
http://localhost:3080
```

Backend 預設：

```text
https://localhost:5002
```

### 14.2 Page Generator CLI

```powershell
node tools/page-gen.js --validate --def employee.json
node tools/page-gen.js --def employee.json --mode static --output .\output
node tools/page-gen.js --list-types
```

### 14.3 UI Component Library

目前 component metadata catalog 包含 108 個 components，分類包含：

- `common`
- `form`
- `input`
- `layout`
- `sections`
- `social`
- `viz`
- `editor`
- `data`

注意：

- site generator 的 component vocabulary 已錨定到 canonical `ui_components` 閉集。
- static site package 會輸出 `b_component` / `b-binding.json` 作為 B component anchor。
- 不是整個 `ui_components` 都宣稱 byte-deterministic；site-gen 靜態輸出與特定 instance ID 已清 deterministic，viz/map/social/download 類仍可能有 runtime timestamp 或互動時 ID/檔名需求。

## 15. 常用驗證命令

### 15.1 C# build/test

```powershell
dotnet build packages/csharp/ControlPlane.slnx
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj
```

Broker HTTP integration 需要先有 broker：

```powershell
dotnet run --project packages/csharp/tests/broker-tests/Broker.Tests.csproj -- --integration http://localhost:5361
```

### 15.2 Repo validation

```powershell
npm test
npm run validate:baseorm
npm run validate:broker-scope
npm run validate:backend-governance
npm run validate:ui-state
npm run validate:ui-library
npm run validate:ui-library:browser
npm run validate:agent-governed
npm run validate:broker-llm-proxy
```

### 15.3 Podman stacks

```powershell
npm run validate:podman-governed-stack
npm run validate:podman-openai-compatible-stack
npm run validate:podman-ollama-host-stack
node tools/agent/tests/test-podman-execution-adapter-stack.js
```

## 16. 測試產物清理

直接測試後清理：

```powershell
Remove-Item -Force `
  .\packages\csharp\broker\broker.db, `
  .\packages\csharp\broker\broker.db-shm, `
  .\packages\csharp\broker\broker.db-wal `
  -ErrorAction SilentlyContinue

Remove-Item -Recurse -Force .\.test-output -ErrorAction SilentlyContinue
```

若是 sidecar runtime，不要直接刪 `.run/line-sidecar`，除非你要重置 sidecar 狀態。先停止：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 down
```

Podman stack 停止與移除 volume：

```powershell
podman compose -f tools/agent/container/compose.yml down -v
```

## 17. 疑難排解

### 17.1 `dotnet` 找不到

安裝 .NET SDK 8.0+，再確認：

```powershell
dotnet --info
dotnet --list-sdks
```

### 17.2 中文顯示亂碼

檔案以 UTF-8 為主。PowerShell 5.1 可能顯示異常。建議：

- 使用 PowerShell 7。
- 驗證 LINE 訊息時用 `-MessageBase64Utf8`。
- 讀檔時指定 UTF-8。

### 17.3 LINE webhook 無法收到

檢查：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
```

確認：

- ngrok tunnel 有 public URL。
- LINE webhook endpoint active。
- `line-worker` PID 存在。
- LINE credentials 正確。
- `.run/line-sidecar/logs/line-worker.err.log` 沒有簽章或 token 錯誤。

### 17.4 Broker 回覆但 AI 不動

檢查：

- `ANTHROPIC_API_KEY` 是否存在；若沒有，確認 `C:\secure\Bricks4Agent\Api.txt` 有 OpenAI-compatible fallback key。
- `HighLevelLlm` provider / model / API format 是否匹配。
- `.run/line-sidecar/logs/broker.err.log` 是否有 upstream `401`、`400`、timeout。

### 17.5 Worker registration 失敗

先用 sidecar 產生 `worker-auth.json`，再用 helper 啟 worker：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker site-crawler
```

若手動啟 worker，需要提供符合 broker 設定的：

- `Worker:Auth:WorkerType`
- `Worker:Auth:KeyId`
- `Worker:Auth:SharedSecret`

### 17.6 Google Drive OAuth 失敗

檢查：

- `client_secret_*.json` 是否在 secrets 目錄。
- redirect URI 是否包含：

```text
http://127.0.0.1:5361/api/v1/google-drive/oauth/callback
```

- sidecar public URL 是否最新。
- callback log 是否出現 `invalid_state` 或 `state_expired`。

### 17.7 Podman stack port 衝突

常見 broker port：

| Stack | 預設 port |
| --- | --- |
| `compose.yml` | `5000` |
| `compose.openai-compatible.yml` | `5361` |
| `compose.ollama-host.yml` | `5002` |

如果 sidecar 正在使用 `5361`，先覆寫：

```powershell
$env:BROKER_PORT='5601'
npm run validate:podman-openai-compatible-stack
```

## 18. 已知限制

目前不要宣稱以下已完成：

- 全生產級 operator console。
- 自訂 seccomp profile。
- Critical dual approval。
- `line.message.send` / `line.audio.send` 已有 worker-local outbound rate limiting；分散式配額與 `line.notification.send` 覆蓋仍未完成。
- browser authenticated automation production readiness。
- 所有外部 provider 在每台機器都已實測。
- 整個 `ui_components` 全庫 byte-deterministic。

目前已可依文件操作與驗證的重點是：

- LINE sidecar canonical path。
- broker high-level conversation / query / draft / confirmation。
- local admin console。
- approval MVP。
- governed agent container path。
- execution adapter stack。
- UI/generator validation。
- broker/core build and tests。
