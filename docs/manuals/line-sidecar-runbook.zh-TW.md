# LINE Sidecar 執行手冊

日期：2026-09-26
範圍：目前本機 Windows sidecar 與 LINE live ingress 路徑
對象：操作人員 / 開發者

## 目的

這份手冊說明目前本機 live 路徑的啟動、驗證、操作與排障方式：

`LINE webhook -> public tunnel URL（ngrok；備援 localhost.run）-> line-worker -> broker /api/v1/high-level/line/process`

這條 broker 路徑仍然使用 plain JSON，但現在已改成 authenticated worker path，不再是單純 trusted bypass。

這是目前本機的 canonical operator path。

本文件不涵蓋：

- legacy `agent --line-listen` 路徑

- 純容器化的完整操作

- 多機正式部署

## 目前 canonical 埠號

- broker：`127.0.0.1:5361`

- line-worker webhook：`127.0.0.1:5357`

- ngrok tunnel 名稱（僅 ngrok 路徑）：`line5357`

## Sidecar 狀態持久化

- sidecar 的執行狀態現在會保存在：

- `.run\line-sidecar\data\broker.db`（位於 repo 根目錄下）

- 這個資料庫目前會保存 broker 擁有的本機狀態，例如：

- 本機後台管理員密碼

- shared context 與高階使用者 profile

- Google Drive delegated OAuth 憑證

- `line-sidecar.ps1 restart` 現在應該保留這些狀態，不再因為 publish 目錄重建而一起被清掉。

- 如果 Google Drive 授權是在這次持久化修正之前建立，且後來已消失，請在目前這個 sidecar 實例上重新授權一次。

## 前置條件

這台機器至少要有：

- Windows PowerShell 5.1 以上

- 可 publish / 執行 broker 與 line-worker 的 .NET SDK/runtime

- public tunnel：建議安裝並登入 `ngrok`，但非必要——若沒有 `ngrok` 或 `%LOCALAPPDATA%\ngrok\ngrok.yml` 不存在，`up` 會發出警告並改用 localhost.run tunnel，此時需要 `PATH` 上有 OpenSSH client（`ssh`）

- 已填好 LINE 憑證的本機 worker 設定

目前若要有最佳 live 行為，通常還需要：

- Windows User 或目前 shell 的 `ANTHROPIC_API_KEY`

- 給高階模型與 broker `LlmProxy` 使用；sidecar 會優先設為 `anthropic` / `claude-sonnet-4-6`

- `C:\secure\Bricks4Agent\Api.txt`（可用 `BRICKS4AGENT_SECRETS_DIR` 環境變數改路徑；repo 根目錄為舊版備援）

- 沒有 `ANTHROPIC_API_KEY` 時的 OpenAI-compatible fallback key

- 同一機密目錄下的 `client_secret_*.json`（優先搜尋機密目錄；repo 根目錄僅為舊版備援）

- 給 Google Drive OAuth 使用

- `%LOCALAPPDATA%\ngrok\ngrok.yml`

- 可用的 ngrok 設定（僅 ngrok 路徑需要）

## 本機檔案與輸入

### 1. LINE worker 設定

檔案：

- `packages/csharp/workers/line-worker/appsettings.json`

這是本機檔案，不應提交到 git。

至少要有可用值：

- `Line.ChannelAccessToken`

- `Line.ChannelSecret`

- `Line.DefaultRecipientId`

- `Worker.Auth.WorkerType`

- `Worker.Auth.KeyId`

- `Worker.Auth.SharedSecret`

### 2. 高階模型 API key

檔案：

- `C:\secure\Bricks4Agent\Api.txt`（或 `$env:BRICKS4AGENT_SECRETS_DIR\Api.txt`；repo 根目錄的 `Api.txt` 為舊版備援）

目前 sidecar 會：

- 優先讀取 `ANTHROPIC_API_KEY`，並設定 `HighLevelLlm` / `LlmProxy` 為 `anthropic`、`claude-sonnet-4-6`；LlmProxy 改用雲端供應者時（含 OpenAI-compatible fallback）一併關閉 `HighLevelExecutionModelPolicy`，代理改用 `LlmProxy:DefaultModel`

- 若沒有 `ANTHROPIC_API_KEY`，才讀取這個檔案並注入 broker 的 `HighLevelLlm.ApiKey`

### 3. Google Drive OAuth client

檔案樣式：

- `C:\secure\Bricks4Agent\client_secret_*.json`（或 `$env:BRICKS4AGENT_SECRETS_DIR`；`start-sidecar-stack.ps1` 先搜尋機密目錄，repo 根目錄僅為舊版備援）

### 3.1 Worker 身分憑證庫

檔案：

- `C:\secure\Bricks4Agent\worker-auth.json`（或 `$env:BRICKS4AGENT_SECRETS_DIR\worker-auth.json`）

目前 sidecar 會：

- 啟動時為缺少憑證的 worker 類型自動產生並持久化（line-worker、file-worker、browser-worker、transport-tdx、site-crawler-worker、generation-worker）

- 將全部憑證注入 broker runtime 設定，並開啟 `WorkerAuth.Enforce = true`

- line-worker runtime 設定取得對應憑證

- `B4A_LINE_WORKER_KEY_ID` / `B4A_LINE_WORKER_SHARED_SECRET` 仍可覆寫該次啟動的 line-worker 條目

要讓其他 worker 對啟用驗證的 broker 註冊，使用：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\run-worker.ps1 -Worker site-crawler
```

（`-Worker` 可用 `file`、`browser`、`transport-tdx`、`site-crawler`、`generation`。）此腳本讀取同一憑證庫，註冊即可通過 worker 身分驗證。

### 3.1.1 受治理生成（選用）

`line-sidecar.ps1 up -GenerationMode Governed`（或 `restart`）會另外發布並以主機程序啟動 `generation-worker`（與其他 sidecar worker 相同），並把 broker 切到 `HighLevelCoordinator:Generation:SystemScaffoldMode = Governed`。之後確認過的系統雛形需求，改由 broker 啟動的受控代理經三個生成能力產出，不再在 broker 程序內生成。broker 與 worker 共用 `Generation:OutputRoot`，位置是 `.run\line-sidecar\data\generation-out`，在各使用者工作區之外。worker 執行的 node 取自 `B4A_NODE_PATH`（有設定時），否則用 PATH 上的 `node`。

不加 `-GenerationMode`（或設為 `Legacy`）時行為不變。sidecar 上啟動的代理沿用已記載的 `AllowAgentDefaultNetwork` 例外（不在 internal 網路上），代理看到的需求文字有外流的可能；專用的 internal 代理網路列為後續。

啟用前的準備（升級後也要重做第 1 項）：

1. 以 `tools/agent/Containerfile` 重建 `bricks4agent-agent:latest`（在 repo 根目錄執行 `podman build -t bricks4agent-agent:latest -f tools/agent/Containerfile .`，使用 docker 時把 `podman` 換成 `docker`）。三個生成工具與生成用的 system prompt 都烤在這個映像裡；sidecar 直接使用本機既有的映像、不會自己建置，就緒檢查也不看映像版本。舊映像啟動的代理沒有這三個工具，每次生成都要等到代理結束或 watchdog 期限才失敗。
2. 容器執行環境（podman 或 docker，可用 `B4A_CONTAINER_RUNTIME` 指定）在 PATH 上，ContainerManager 因此啟用（或以 `B4A_CONTAINER_MANAGER_ENABLED` 明確開啟）。
3. LlmProxy 有模型可用：有 Anthropic 或 OpenAI 的 API 金鑰時，sidecar 把 LlmProxy 切到該供應者。沒有金鑰時 LlmProxy 仍是啟用的，沿用 broker `appsettings.json` 的本機 Ollama（`LlmProxy:BaseUrl` 與 `LlmProxy:DefaultModel`），這時必須有可用的 Ollama 與該模型；`-GenerationMode Governed` 啟動前會檢查，不成立就停止並說明原因。
4. generation-worker 找得到 node：`B4A_NODE_PATH` 或 PATH 上的 `node`。

第 2 項不成立時，確認系統雛形會回「系統雛形生成暫不可用」且不建立任務；第 3 項由啟動腳本檢查（就緒檢查只看 LlmProxy 是否啟用，看不出供應者是否可達）。第 1、4 項不成立時就緒檢查看不出來（它不看映像版本，也不檢查 worker 能否執行 node），每次生成都要等到代理結束或 watchdog 期限才失敗，所以啟用前請先確認。

要對執行中的 broker 手動啟動 worker：`run-worker.ps1 -Worker generation`（加 `-GenerationOutputRoot` 可改用其他輸出根目錄；broker 必須使用同一個）。輸出根目錄必須是實際的目錄，不可是符號連結或 junction：保留期限清理不跟隨連結，所以 worker 遇到連結的根目錄就拒絕啟動。要移到其他磁碟時，直接指定那顆磁碟上的實際路徑。

產物的保留期限：generation-worker 在啟動時、每次生成之前，以及執行期間每小時，刪除 `generation-out` 中超過 `Generation:RetentionHours`（預設 24 小時）的請求目錄。zip 含使用者的需求內容；broker 收下時已複製到使用者的文件區。worker 持續執行時，使用者刪除自己文件區中的產物後，`generation-out` 中的副本最遲在保留期限再加一小時後消失。worker 沒有執行時不會清理：切回 Legacy（啟動時不加 `-GenerationMode Governed`）之後，`generation-out` 中留下的目錄要手動刪除，或再以 Governed 模式啟動一次（worker 啟動時會清理）。

### 3.2 LINE outbound rate limit

line-worker 會對 `line.message.send` 與 `line.audio.send` 做 worker-local outbound rate limiting，key 為 recipient + capability。預設值在 `packages/csharp/workers/line-worker/appsettings*.json`：

- `Line.OutboundRateLimit.PermitLimit`: 預設 `20`

- `Line.OutboundRateLimit.WindowSeconds`: 預設 `60`

- `Line.OutboundRateLimit.MaxTrackedKeys`: 預設 `1024`

sidecar 或手動啟動時可用環境變數覆寫：

```powershell
$env:WORKER_Line__OutboundRateLimit__PermitLimit = '20'
$env:WORKER_Line__OutboundRateLimit__WindowSeconds = '60'
$env:WORKER_Line__OutboundRateLimit__MaxTrackedKeys = '1024'
```

這不是分散式 quota；多個 line-worker instance 之間不共享計數，`line.notification.send` 目前也尚未套用這個 limiter。

目前 sidecar 會：

- 使用第一個符合的 client JSON

- 將 delegated redirect URI 設為：

- `http://127.0.0.1:5361/api/v1/google-drive/oauth/callback`

Google Drive 交付模式現在可設定為：

- `shared_delegated`

- `user_delegated`

- `system_account`

若沒有額外 override，且 `Line.DefaultRecipientId` 有值，sidecar 目前會預設：

- `DefaultIdentityMode = shared_delegated`

- `SharedDelegatedUserId = Line.DefaultRecipientId`

也就是預設用單一 Google 帳號作為共用雲端交付者。

## Canonical 操作指令

正常本機操作應一律走：

- [line-sidecar.ps1](../../packages/csharp/workers/line-worker/line-sidecar.ps1)

### 啟動

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
```

這是唯一正常的啟動方式。
不要另外先手動開 broker、line-worker 或 ngrok。

### 查看狀態

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
```

### 重啟

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 restart
```

### 停止

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 down
```

### 直接驗證 broker 路徑

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify-broker -UserId test-user -MessageBase64Utf8 <base64-utf8>
```

### 驗證簽章後的 LINE webhook

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -MessageBase64Utf8 <base64-utf8>
```

## `up` 實際會做什麼

目前啟動流程會：

1. 建立 `.run/line-sidecar`

2. publish broker 到 `.run/line-sidecar/broker`

3. publish line-worker 到 `.run/line-sidecar/line-worker`

4. 若存在 `Bricks4Agent Dev Code Signing` 開發簽章憑證，補簽 `.run/line-sidecar` 內自家 `.dll` / `.exe`

5. 注入本機 production override：
 - high-level API key
 - Google Drive OAuth 設定
 - Google Drive 預設身分模式與 shared delegated owner

6. 啟動 broker 到 `127.0.0.1:5361`

7. 啟動 line-worker 到 `*:5357`

8. 重建 ngrok tunnel `line5357`；若 ngrok 不可用，改啟動 localhost.run tunnel（並啟動 webhook-sync watchdog，在 localhost.run URL 變動時重新指向 LINE webhook）

9. 更新 LINE webhook endpoint，除非使用 `-SkipWebhookUpdate`

10. 等到 broker 與本機 webhook 真正 ready

11. 走 ngrok 路徑時，確認命名的 ngrok tunnel 確實存在

重要補充：

- 若 `127.0.0.1:4040` 的 ngrok admin API 尚未存在

- 腳本現在會自動啟動本機 ngrok agent：

- `ngrok start --none --config %LOCALAPPDATA%\ngrok\ngrok.yml`

- 若 `PATH` 上沒有 `ngrok` 或設定檔不存在，腳本會警告並改用 localhost.run（警告文字仍寫「cloudflared quick tunnel」，但實際啟動的是 localhost.run；腳本內的 cloudflared 分支目前不可達）

也就是說，現在文件的標準是：

- 如果 `up` 失敗，文件必須能解釋原因

- 不能再假設操作者自己猜到要怎麼先手動開 ngrok

第一次啟動可能比較慢，因為 broker 可能需要 seed 一部分本機資料後才會 ready。

### Smart App Control / WDAC 封鎖 runtime DLL

若 `up` 失敗，且 `.run/line-sidecar/logs/broker.err.log` 或 Windows Code Integrity event 顯示 `0x800711C7`、`Smart App Control`、`did not meet the Enterprise signing level requirements`，代表 Windows 仍未信任目前 runtime 載入的程式碼。

這時不要只反覆重跑 `line-sidecar.ps1 up`。請用系統管理員 PowerShell 執行：

```powershell
# 在 repo 根目錄執行
npm run signing:wdac-repair -- -Deploy
```

這個 repair flow 會掃描（相對於 repo 根目錄）：

```text
.run\line-sidecar
```

並產生 policy 到：

```text
.run\wdac\line-sidecar-runtime\
```

部署成功後，輸出的 `{policy-id}.cip` 必須出現在：

```text
C:\Windows\System32\CodeIntegrity\CiPolicies\Active
```

只有 active policy 檢查通過才代表 WDAC policy 實際生效。完整說明見 [dev-code-signing-wdac.zh-TW.md](dev-code-signing-wdac.zh-TW.md)。

## 啟動成功的判準

`up` 成功後，應該同時看到：

- `status` 顯示 broker PID 與 line-worker PID 都在跑

- `status` 顯示 ngrok PID 在跑（走 localhost.run 備援時則是 localhost.run PID）

- `status` 顯示 ngrok public URL（或 `latest localhost.run` URL）

- `status` 顯示 LINE webhook endpoint 且 `active = True`

- 後台可開：

- `http://127.0.0.1:5361/line-admin.html`

- broker API 可回：

- `http://127.0.0.1:5361/api/v1/local-admin/status`

- 本機 webhook 驗證成功：

- `verify` 回 `Webhook status: 200`

- public webhook 也能通

如果 `up` 回傳成功但上述條件不成立，應視為啟動失敗。

## 後台

目前本機後台：

- `http://127.0.0.1:5361/line-admin.html`

目前行為：

- 僅限 localhost

- 需要 local admin login

- 若 DB 尚未有管理密碼，初始密碼是 `admin`

- 第一次登入必須改密碼

目前後台的分頁如下（每個分頁只在登入的 operator 具備對應權限時顯示）：

- LINE 與使用者：LINE 使用者與標籤、註冊政策、每位使用者高階權限、Google Drive OAuth 與 delivery 操作

- 系統監控

- Workflow

- Browser 綁定

- Deployment：deployment targets

- 交付記錄

- 權限管理：operator 與權限管理

- 審批：approval queue

- 系統警示

- Tool Specs

## 目前 Google Drive 交付模式

broker 現在支援三種 Google Drive 身分：

- `shared_delegated`

- 單一 Google 帳號授權一次

- 所有 LINE 使用者的檔案都上傳到同一個 Drive

- broker 仍會記錄檔案屬於哪位 LINE 使用者

- `user_delegated`

- 每位 LINE 使用者各自授權自己的 Google Drive

- `system_account`

- service account 路徑，較適合 Shared Drive

目前本機 sidecar 預期的預設模式是：

- `shared_delegated`

這對「全部都上傳到同一個 Google Drive」的場景才是正確設計。

## 下載功能需要的設定

目前 live 交付已經有兩條下載路徑：

- Google Drive 仍是主要的使用者下載路徑

- 若 Google Drive 上傳失敗，且 sidecar 具有 public URL，broker 會改送短效簽名下載連結

也就是說，若要讓 LINE 使用者在生成文件或網站原型後真的拿到可下載連結，至少要有：

1. 可用的高階模型 API
- `C:\secure\Bricks4Agent\Api.txt`
- 這決定文件或網站原型能否先被生成

2. 可用的 Google OAuth client JSON
- `C:\secure\Bricks4Agent\client_secret_*.json`（先找機密目錄；repo 根目錄為舊版備援）
- callback URI 必須對應：
 - `http://127.0.0.1:5361/api/v1/google-drive/oauth/callback`

3. 正確的 Google Drive 交付模式
- 若你的需求是「所有 LINE 使用者都上傳到同一個 Google Drive」：
 - 應使用 `shared_delegated`
- 若改成 `user_delegated`，就會變成每位使用者各自授權自己的 Drive

4. 有效的 Drive 授權憑證已保存在目前 sidecar DB
- sidecar 現在使用的持久化 DB 是：
 - `.run\line-sidecar\data\broker.db`（位於 repo 根目錄下）
- 若 `google_drive_delegated_credentials` 沒資料，交付結果只會落到本機，不會有雲端下載連結

5. LINE sidecar 已重啟到最新版本
- 目前的檔案交付、shared delegated owner、持久化 DB 都依賴最新 sidecar publish

目前行為：

- 若 Google Drive 上傳成功，LINE 回覆會優先使用 Drive 連結

- 若 Google Drive 上傳失敗，且目前 sidecar public URL 可用，broker 會改送短效簽名下載連結

- 若兩條路徑都不可用，才會退化成無連結通知

終端使用者也可以登入使用者入口網站（`http://127.0.0.1:5361/portal/index.html`），查看自己的 artifact 清單與其 Drive 連結或 broker 簽名下載路徑。

## 目前高階模型

現在 live LINE 路徑的高階回應模型是：

- 有 `ANTHROPIC_API_KEY` 時優先使用 provider `anthropic`、model `claude-sonnet-4-6`

- 否則採用 broker 內建預設（`ollama` / `qwen3.6:latest`）；`openai-compatible` / `gpt-5.4-mini` 為最後備援形狀

這和 downstream execution-model request 是分開的。

## 基本 live 用法

### 一般對話

直接在 LINE 傳：

- `hello`

- `請幫我釐清需求`

### 專案訪談

目前專案訪談的明確入口是：

- `/proj`

目前的基本 happy path：

1. 傳 `/proj`

2. 用 `#專案名稱` 回覆

3. 以編號選最接近的專案規模

4. 以編號選最接近的網站結構方向

5. 檢視系統產出的 PDF/JSON review artifacts

6. 用 `/ok`、`/revise`、`/cancel` 表態

7. `/ok` 之後回 `y` 開始建置，回 `n` 取消

補充：

- `/proj`、`/ok`、`/revise`、訪談中的回答與 draft 確認（`y`）需要 production 權限（會員層級，且管理員已開啟 production 任務）；基本註冊者會收到與 `/建立` 相同的權限不足回覆，`/cancel` 不受限

- `/ok` 只建立系統雛形 draft，使用者回 `y` 才建置，走與 `/建立` 相同的 draft 確認（專案名稱重查、升格閘、task、plan、handoff）；回 `n` 或 draft 逾時後可再 `/ok` 或 `/revise`

- 回覆只寫專案資料夾名、封裝檔名與相對於受管根目錄的工作區位置，不寫主機絕對路徑

- prompts 目前是中英文雙語

- 文案刻意偏一般 LINE 使用者，不是工程術語

- `tool_page`、`mini_app`、`structured_app`、`template family` 這類內部識別字不直接顯示給使用者

### 說明與個人資訊

- `?help`

- `?profile`

### 受控搜尋

- `?search 中央氣象署官網`

- `?s 中央氣象署官網`

### 交通查詢

- `?rail 台北 台中 今天 18:00`

- `?hsr 台北 台中 今天 18:00`

- `?bus 台北 台中 今天 18:00`

- `?flight TPE KIX tomorrow`

### Production 流程

- `/create a website prototype`

- `#MyProject`

- `confirm`

## 使用者與工作區

每位高階 LINE 使用者都會在 broker 的 absolute access root 下擁有：

- `conversations`

- `documents`

- `projects`

目前 live sidecar 使用 `packages/csharp/broker/appsettings.json` 中 `HighLevelCoordinator.AccessRoot` 的預設值：

- `%LOCALAPPDATA%\Bricks4Agent\managed-workspaces`

正式 broker 設定可改成別的 absolute access root。

## UTF-8 驗證原則

UTF-8 是基本要求，不可退回 ASCII。

若要做可靠的多語系驗證，請優先使用：

- `verify-high-level-process.ps1 -MessageFile`

- `verify-live-webhook.ps1 -MessageFile`

- 或 `-MessageBase64Utf8`

不要完全相信 shell 直接打中文時的終端顯示。
若 shell 顯示亂碼，不代表檔案不是 UTF-8。

## 基本排障

### 1. LINE 完全沒回應

先檢查：

- `line-sidecar.ps1 status`

- public tunnel 是否存在（ngrok tunnel 或 localhost.run 程序）

- LINE webhook endpoint 是否 active

- line-worker PID 是否存在

常見原因：

- ngrok / localhost.run tunnel 掉了

- webhook endpoint 沒更新

- line-worker 停了

- ngrok agent 根本沒起來

修正：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 restart
```

### 2. public webhook 回 `404` 或 ngrok 錯誤

症狀：

- public webhook URL 回 `404`

- 回應出現 `ERR_NGROK_3200`

原因：

- tunnel 離線或 stale

修正：

- 先跑 `status`

- 再跑 `restart`

若還是不行：

- 看 `.run/line-sidecar/logs/ngrok.out.log`

- 看 `.run/line-sidecar/logs/ngrok.err.log`

- 確認 `%LOCALAPPDATA%\ngrok\ngrok.yml` 存在且含有效 authtoken

- 走 localhost.run 備援時，看 `.run/line-sidecar/logs/localhostrun.out.log` / `localhostrun.err.log` 與 `webhook-sync.out.log` / `webhook-sync.err.log`

### 3. broker 有起來，但 LINE 說 AI 服務暫時無法回應

常見原因：

- `ANTHROPIC_API_KEY` 不存在，且 `Api.txt` 不存在或無法讀

- 上游 API key 無效

- high-level 模型 upstream 回 `401` 或 `400`

- sidecar publish output 吃到舊檔

檢查：

- `.run/line-sidecar/logs/broker.out.log`

- `.run/line-sidecar/logs/broker.err.log`

修正：

- 確認 `ANTHROPIC_API_KEY` 是有效 Anthropic key，或 `Api.txt` 內是有效 OpenAI-compatible fallback key

- 重啟 sidecar

### 4. Google Drive OAuth 出現 `invalid_state` 或 `state_expired`

原因：

- 舊授權網址重用

- state 已被使用或逾時

修正：

- 回到後台重新發起 OAuth

- 立即使用新的授權網址

### 5. Google Drive OAuth callback 回 `500`

常見原因：

- sidecar broker 未在 callback 邏輯調整後重啟

- publish output stale

- `client_secret_*.json` 不存在或內容錯誤

修正：

- 確認機密目錄（`C:\secure\Bricks4Agent` 或 `BRICKS4AGENT_SECRETS_DIR`）有可用的 `client_secret_*.json`；repo 根目錄僅為舊版備援

- 執行 `line-sidecar.ps1 restart`

### 6. Google Drive upload 失敗並出現 `storageQuotaExceeded`

如果你走的是 `system_account`：

- 個人 My Drive 不夠

- service account 上傳通常要 Shared Drive 或改走 delegated-user flow

目前個人 Google 帳號最適合的路徑是：

- delegated OAuth user Drive

### 7. Drive 成功但 LINE 沒收到通知

常見原因：

- 你用的是測試帳戶，不是真實 LINE `U...` 使用者

- notification queue 有建，但 LINE 對假 recipient 無法送達

檢查：

- 後台使用者標籤

- 目前選中的 user 是否標成「真實 LINE」

### 8. 後台可開但登入失敗

規則：

- 若 DB 尚未建立管理密碼，初始密碼是 `admin`

- 第一次登入必須改密碼

若是已運作過的 live sidecar，密碼可能早就被改過。
這時應在本機 broker DB / admin 層處理，不要猜。

### 9. sidecar restart 因 publish output 被鎖而失敗

常見原因：

- 舊 broker 或 line-worker 程序仍持有檔案

目前 restart 已有：

- 等程序停止

- republish 前清空輸出目錄

若仍失敗：

- 先 `down`

- 確認 broker / worker 程序真的都不在了

- 再 `up`

## Log 與工作目錄

目前 sidecar runtime 目錄：

- `.run\line-sidecar`（位於 repo 根目錄下）

主要 log：

- `.run/line-sidecar/logs/broker.out.log`

- `.run/line-sidecar/logs/broker.err.log`

- `.run/line-sidecar/logs/line-worker.out.log`

- `.run/line-sidecar/logs/line-worker.err.log`

- `.run/line-sidecar/logs/ngrok.out.log`

- `.run/line-sidecar/logs/ngrok.err.log`

- `.run/line-sidecar/logs/localhostrun.out.log` / `localhostrun.err.log`（localhost.run 備援）

- `.run/line-sidecar/logs/webhook-sync.out.log` / `webhook-sync.err.log`（localhost.run webhook-sync watchdog）

## 本手冊尚未涵蓋

- broker 與 line-worker 的多機正式部署

- 強化過的遠端後台認證

- browser worker runtime 操作

- 完整的 Azure IIS 部署操作

- 完整的災難復原程序

## 終端使用者前台（使用者入口網站）

broker 現在提供終端使用者入口網站：

- 頁面：`http://127.0.0.1:5361/portal/index.html`（靜態檔來自 `packages/javascript/browser/user-portal/`，由 `packages/csharp/broker/Program.cs` 掛載）

- API：`packages/csharp/broker/Endpoints/PortalEndpoints.cs` 的 `/api/v1/portal/*`——`auth/status`、`auth/register`、`auth/login`、`auth/logout`、`auth/line-verification`、`me`、`commands`、`results`、`artifacts`、`artifacts/{documentId}`

- 登入的使用者只看得到自己的 artifact；每筆都附 Drive 連結或 broker 簽名下載路徑

同時仍有：本機 admin 後台、LINE 對話中的交付連結、broker 內部 artifact records。

## 相關文件

- [CurrentArchitectureAndProgress-2026-06-13.md](../reports/CurrentArchitectureAndProgress-2026-06-13.md)

- [current-technical-manual.zh-TW.md](current-technical-manual.zh-TW.md)

- [README.md](../../packages/csharp/workers/line-worker/README.md)

- [GoogleDriveDelivery.md](../designs/GoogleDriveDelivery.md)

- [AzureVmIisDeployment.md](../designs/AzureVmIisDeployment.md)
