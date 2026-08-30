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
|---|---|---|
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

`{AccessRoot}` 由 `HighLevelCoordinator:AccessRoot` 設定，必須是絕對路徑（相對路徑會被 broker 拒絕啟動）。broker 與 sidecar 的預設值相同：

```text
%LOCALAPPDATA%\Bricks4Agent\managed-workspaces
```

Sidecar 啟動時會建立這個目錄，並掛載給受控 worker container 使用。它不在 `.run/line-sidecar` 底下，所以清掉 sidecar runtime 不會清掉使用者工作區。

### 3.4 Approval

高風險、scope 逸出、跨使用者或外部發佈/部署類動作不應由 agent 自行決定。Broker 會建立 approval request，並由：

- admin 在 `line-admin.html` 審批。

- 使用者透過 LINE 收到的短效簽章連結進入 `user-approvals.html` 審批。

目前 High / `require_approval` 需要 1 次核准；Critical / `require_dual_approval` 需要 2 個不同 approver id 才能放行。Broker 會持久化 `ApprovalRequest.required_approval_count` 與每位 approver 一筆的 `approval_decisions`，避免同一 approver 重複核准。Local admin 現在使用 named operator session，approver id 會綁到 operator，並由後端 RBAC 判斷是否具備 admin approval 權限。

## 4. 最小安裝需求

建議最低環境：

| 工具 | 用途 |
|---|---|
| Windows 10/11 | canonical local LINE sidecar |
| Windows PowerShell 5.1+ | sidecar 腳本 |
| .NET SDK 10.0+ | broker / worker 建置與測試 |
| Node.js 18+ 與 npm | agent、JS tools、UI tests |
| Git | repo 操作 |
| ngrok | LINE webhook public tunnel；未安裝或未設定 `ngrok.yml` 時，sidecar 會警告並改用 localhost.run ssh tunnel |
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
dotnet restore packages/csharp/ControlPlane.slnx
dotnet build packages/csharp/ControlPlane.slnx
```

repo 的 npm 套件沒有宣告任何 dependencies / devDependencies，所以 `npm install` 不會安裝任何東西；JS 工具與測試都只用 Node 內建模組。

browser/e2e 類驗證（`npm run validate:ui-library:browser`、`npm run validate:user-portal` 等）需要另外準備 Playwright，因為 repo 不宣告這個相依：

```powershell
npx playwright install chromium
```

`validate:ui-library` 找不到 Playwright 時會跳過 browser smoke（除非加 `--require-browser`），並且在 Playwright 沒有自帶瀏覽器時會退回本機已安裝的 Chrome / Edge。

`packages/javascript/browser` 的測試同樣不需要安裝步驟：

```powershell
cd packages/javascript/browser
npm test
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
|---|---|
| `Api.txt` | OpenAI-compatible fallback key；只有在 sidecar/container 明確切到 OpenAI-compatible provider 時才需要 |
| `client_secret_*.json` | Google delegated OAuth |
| `worker-auth.json` | sidecar 產生/維護 worker identity credentials |

本機 broker / Portal 預設使用 host Ollama：`HighLevelLlm.Provider=ollama`、`HighLevelLlm.BaseUrl=http://localhost:11434`、`HighLevelLlm.ApiFormat=chat`、`HighLevelLlm.DefaultModel=qwen3.6:latest`、`HighLevelLlm.MaxOutputTokens=256`。此模式不需要 `HighLevelLlm.ApiKey`。

目前 sidecar 會優先讀取環境變數 `ANTHROPIC_API_KEY`，並將 broker high-level 與 agent-facing `LlmProxy` 設為 `anthropic` / `claude-sonnet-4-6`。若要改走 OpenAI-compatible provider，才使用 `Api.txt` fallback。

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

3. 若存在 `Bricks4Agent Dev Code Signing` 開發簽章憑證，補簽 sidecar runtime 內自家 `.dll` / `.exe`。

4. 載入 high-level API key、Google OAuth、worker auth 等 runtime override。

5. 啟動 broker：`127.0.0.1:5361`。

6. 啟動 line-worker webhook：`127.0.0.1:5357`。

7. 建立 public tunnel：優先用 ngrok；ngrok 不在 PATH 或沒有 `%LOCALAPPDATA%\ngrok\ngrok.yml` 時，改用 localhost.run ssh tunnel。

8. 更新 LINE webhook URL，除非使用 `-SkipWebhookUpdate`。

9. 檢查 broker、line-worker、tunnel ready。

若 Windows Smart App Control / WDAC 在啟動時封鎖 `Broker.dll`、`BrokerCore.dll`、`BaseOrm.dll` 或其他 runtime DLL，請用系統管理員 PowerShell 在 repo root 修復 sidecar runtime trust：

```powershell
npm run signing:wdac-repair -- -Deploy
```

此命令會掃描 repo 內的 `.run\line-sidecar`，產生 policy 到 `.run\wdac\line-sidecar-runtime\`，並確認 `{policy-id}.cip` 進入 `%WINDIR%\System32\CodeIntegrity\CiPolicies\Active`。只有 active policy 檢查通過才表示 WDAC trust 已生效。兩個路徑都可以用 `-RuntimeRoot` / `-OutputDir` 覆寫。

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

### 8.0 第一次綁定 LINE

新使用者不能只靠 LINE 訊息自動開通。正式流程是先在 Web Portal 註冊，再把 Portal 帳號綁定到 LINE：

1. 開啟 `http://127.0.0.1:5361/portal/index.html`。

2. 在 Portal 註冊 `user_id`、密碼與顯示名稱。

3. 註冊成功後，Portal 會顯示 6 位數 LINE 驗證碼與完整指令，例如 `/verify alice 123456`。

4. 在 LINE 傳送 `/verify <user_id> <code>`。也可使用 `/v` 或 `/驗證`。

5. 帳號或驗證碼不符合、過期、已使用，broker 會拒絕 LINE 操作。

6. 驗證成功後，同一個 LINE 使用者才會被映射到 Portal `user_id`，後續對話、結果紀錄與 artifact 都使用同一個使用者工作區。

若驗證碼過期，可登入 Portal，在個人資料區重新產生 LINE 驗證碼。

### 8.1 基本對話

直接傳一般訊息即可。Broker 會走 high-level conversation path。當問題比較適合受控搜尋或查詢時，系統會引導你使用明確前綴命令。

### 8.2 查詢命令

| 指令 | 快捷 | 用途 | 範例 |
|---|---|---|---|
| `?help` | `?h` | 查看規則 | `?help` |
| `?search` | `?s` | 受控網路搜尋 | `?search 台灣 AI 法規趨勢` |
| `?rail` | `?r` | 台鐵/火車查詢 | `?rail 台北 台中 今天 18:00` |
| `?hsr` |  | 高鐵查詢 | `?hsr 台北 左營 明天上午` |
| `?bus` | `?b` | 公車/客運查詢 | `?bus 台北 宜蘭 明天` |
| `?flight` | `?f` | 航班查詢 | `?flight TPE KIX tomorrow` |
| `?profile` | `?p` | 查看個人 profile | `?profile` |

目前建議使用顯式查詢命令。一般問題不保證自動選用即時查詢工具。

### 8.3 個人設定

| 指令 | 快捷 | 用途 | 範例 |
|---|---|---|---|
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
|---|---|
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

### 8.6 使用者 Portal 前台

Broker 也提供使用者 Web 前台，讓使用者可以登入、下指令、查看回應紀錄，以及下載 broker 產生的 artifact。

啟動 sidecar 或 direct broker 後開啟：

```text
http://127.0.0.1:5361/portal/index.html
```

目前 portal 使用 broker 內建輕量帳號密碼登入：

1. 第一次使用可在 portal 註冊使用者 ID 與密碼；broker 會同步建立對應的 high-level profile，並顯示一次性 LINE 驗證碼。

2. 若要使用 LINE，需把 Portal 顯示的 `/verify <user_id> <code>` 傳到 LINE 完成綁定。

3. 登入後可以在「指令與回應」輸入需求。Portal 會呼叫同一個 `HighLevelCoordinator`，所以 `?profile`、查詢命令、draft、專案訪談等行為與 LINE 高階入口一致。

4. 「結果檔案」會列出該使用者工作區中的 artifact。若 artifact 沒有 Google Drive 下載連結，portal 會使用 broker 的短效簽章下載連結。

5. Portal 只顯示自己的 profile、結果紀錄與 artifact metadata；不回傳 broker 內部檔案路徑。

Portal 是一般使用者操作入口；管理、審批、Drive OAuth、部署與系統監控仍使用 `line-admin.html`。

## 9. 使用本機管理後台

Sidecar 啟動後開啟：

```text
http://127.0.0.1:5361/line-admin.html
```

安全特性：

- local admin 僅供 localhost 操作。

- 若 local DB 沒有 admin credential，初始帳號為 `admin`、初始密碼為 `admin`。

- 第一次登入後會要求修改密碼，並建立 `super_admin` operator。

- session 以 cookie 維持。

- 後端會依 operator 角色檢查 API 權限；UI 隱藏頁籤只是操作提示，不是安全邊界。

本機 operator 角色：

| 角色 | 用途 |
|---|---|
| `super_admin` | break-glass / 全權限，可建立與調整其他 operator |
| `system_admin` | 系統狀態、監控、browser/deployment/delivery 等系統操作 |
| `permission_admin` | operator、使用者權限、註冊政策、browser user grant、admin approval 管理 |
| `auditor` | 只讀監控、audit、tool specs 與狀態檢視 |

側邊欄頁籤（依實際 UI 標籤）：

| 頁籤 | 功能 |
|---|---|
| LINE 與使用者 | 系統摘要、交付檔案清單、註冊政策、使用者與高階權限、對話紀錄、Google Drive 授權與 artifact 交付 |
| 系統監控 | LLM、Embedding、RAG Pipeline、Shared Context / Vectors 狀態摘要 |
| Workflow | Recent Tasks / Plans、execution intent、handoff |
| Browser 綁定 | site binding、user grant、lease、request build、execute、recent executions |
| Deployment | Azure IIS target、preview、execute、recent deployments |
| 交付記錄 | 依狀態（completed / partial / failed）檢視交付結果，partial 可重試 Drive 上傳 |
| 權限管理 | named local admin operator 建立、角色調整、停用與 session 撤銷 |
| 審批 | 查看 pending approval，批准或拒絕 |
| 系統警示 | 依時間範圍（1 小時到 7 天）檢視觀測事件 |
| Tool Specs | 查看 broker tool/capability 規格 |

沒有獨立的「系統狀態」或「Artifacts」頁籤：broker 狀態與目前 operator / LLM 顯示在側邊欄頂端的標籤，artifact 清單在「LINE 與使用者」頁籤內。

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
|---|---|
| Broker signed download | 短效簽章下載連結，不依賴 Google Drive |
| Google Drive shared delegated | 使用共用 delegated owner 上傳到 Drive |
| Google Drive user delegated | 使用 LINE 使用者自己的 delegated OAuth |
| Google Drive system account | 使用 service account，通常適合 Shared Drive |
| LINE notification queue | broker queue notification，line-worker 取出後送 LINE |

Drive 上傳失敗（交付狀態為 `partial`）時，可在 admin console 的「交付記錄」頁籤按「重試 Drive 上傳」，重試成功會一併重送通知。

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
|---|---|
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

`--mode` 可用 `static`、`dynamic`、`both`（預設 `static`）。若 `--def` 給的是 DefinitionTemplate（含 `definitions.pages`），可以指定要處理哪些 page：

```powershell
node tools/page-gen.js --def site-definition.json --page products-list --mode static --output .\output
node tools/page-gen.js --def site-definition.json --pages products-list,orders-form --mode static --output .\output
node tools/page-gen.js --def site-definition.json --all --mode static --output .\output
```

`--pages` 與 `--all` 是批次模式，輸出一份彙總 JSON，`results` 內每個 page 各有自己的 `pageId` 與 `files`。

頁面名稱必須是 PascalCase 且以 `Page` 結尾，欄位名稱與 behaviors 也必須是合法 JS 識別字；不合格時 CLI 會回 `success: false` 與 `errors` 陣列，不產生檔案。

### 14.3 UI Component Library

目前 component metadata catalog 包含 116 個 components，分類包含：

- `common`

- `form`

- `input`

- `layout`

- `sections`

- `social`

- `viz`

- `editor`

- `data`

- `analytics`

注意：

- site generator 的 component vocabulary 已錨定到 canonical `ui_components` 閉集。

- static site package 會輸出 `b_component` / `b-binding.json` 作為 B component anchor。

- 不是整個 `ui_components` 都宣稱 byte-deterministic；site-gen 靜態輸出與特定 instance ID 已清 deterministic，viz/map/social/download 類仍可能有 runtime timestamp 或互動時 ID/檔名需求。

- 使用者 Portal 的指令輸入使用元件庫中的 `CommandComposer`；後續前台需要的新前後端元件也應先補回共用元件庫或 broker reusable service，再由產品畫面引用。

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
npm run validate:user-portal
npm run validate:agent-governed
npm run validate:broker-llm-proxy
```

若 Windows Smart App Control / WDAC 擋下 `BaseOrm.dll`、broker verify executable、`Unit.Tests.dll`、`Broker.Tests.exe` 或其他本機 build output，先依 [dev-code-signing-wdac.zh-TW.md](dev-code-signing-wdac.zh-TW.md) 建立 dev code-signing 與 WDAC supplemental policy 流程，再用 signed 入口重跑驗證：

```powershell
npm run validate:db:signed
npm run test:unit:signed
npm run test:integration:signed
npm run test:broker:signed
npm run test:dotnet:signed
```

若測試 runtime 仍被封鎖，請用系統管理員 PowerShell 執行 `npm run signing:wdac-repair-tests -- -Deploy`，讓 broker、broker-tests、unit-tests、integration-tests runtime 都納入 Publisher-level supplemental policy。SAC / WDAC 下不要在簽章後直接跑一般 `dotnet run` 或 `dotnet test`，因為它們可能重新 build 並覆蓋簽章。

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

安裝 .NET SDK 10.0+，再確認：

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

- 本機 Portal / broker 預設路徑：確認 Ollama 正在執行，且 `ollama list` 看得到 `qwen3.6:latest`。

- 外部 provider 路徑：確認 `ANTHROPIC_API_KEY`，或 `C:\secure\Bricks4Agent\Api.txt` 的 OpenAI-compatible fallback key。

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
|---|---|
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

- 企業級 operator console，例如 SSO、MFA、集中式 IAM、跨機器 operator 同步。

- 自訂 seccomp profile。

- Critical dual approval 已具備 broker 層持久化與兩個不同 approver id 門檻；local admin 具備 named operator RBAC，但尚未接企業 IAM。

- `line.message.send` / `line.audio.send` 已有 worker-local outbound rate limiting；分散式配額與 `line.notification.send` 覆蓋仍未完成。

- browser authenticated automation production readiness。

- 所有外部 provider 在每台機器都已實測。

- 整個 `ui_components` 全庫 byte-deterministic。

目前已可依文件操作與驗證的重點是：

- LINE sidecar canonical path。

- broker high-level conversation / query / draft / confirmation。

- local admin console。

- approval lifecycle、User/Admin 兩層介面，以及 Critical dual approval 的 broker 層門檻。

- governed agent container path。

- execution adapter stack。

- UI/generator validation。

- broker/core build and tests。

## 19. Legal RAG / 法律檢索輔助

目前系統有一個小型法律 RAG 可行性模組，重點是把可查證資料外部化到資料庫，再讓回答可以引用檢索到的片段。這不是完整法律資料庫，也不是法律意見。

目前範圍：

- 法律 POC 來源是台灣消費者保護法相關資料。

- broker 會把資料存成 `SharedContextEntry`，並建立 SQLite FTS5 全文索引。

- 若啟用 embedding provider，會同步建立 `vector_entries` 做語意檢索；目前 broker 預設使用 `bge-m3`，`nomic-embed-text` 是較輕量的備用模型。

- 同一份資料可同時保留不同 embedding model 的向量；檢索時只會使用目前設定模型的向量，不會把不同維度或不同模型混在同一輪語意比對。

- LINE 對話、`rag_retrieve` tool、`/agents/rag/test` 與 `/dev/rag-test` 都走同一個 RAG retrieval core。

- 需要獨立查詢服務時，可啟動 `packages/csharp/rag-service`；它只提供 `/healthz` 與 `/rag/retrieve`，可共用同一份 SQLite RAG DB，不依賴 LINE 或 broker 對話流程。

使用限制：

- 法律 RAG 只能作為檢索與佐證輔助，不應視為律師意見。

- 若沒有啟用 Ollama / embedding，仍可用 FTS5 全文檢索；語意向量檢索需要 embedding provider。

- live 法規 seed 需要網路；正式驗證使用離線 fixture，不依賴法務部網站或 live model。

- `rag-service` 預設關閉 embedding、query rewrite、rerank，所以可以先作為純 FTS5 retrieval host；若要語意檢索，需另外設定 embedding provider。

驗證：

```powershell
npm run validate:broker-scope:signed
npm run validate:db:signed
```

`validate:broker-scope` 會驗證法律 RAG import、狀態外部化、FTS5、deterministic vector retrieval 與 tag filter。
