# LINE Sidecar 執行手冊

日期：2026-03-26
範圍：目前本機 Windows sidecar 與 LINE live ingress 路徑
對象：操作人員 / 開發者

## 目的

這份手冊說明目前本機 live 路徑的啟動、驗證、操作與排障方式：

`LINE webhook -> ngrok public URL -> line-worker -> broker /api/v1/high-level/line/process`

這是目前本機的 canonical operator path。

本文件不涵蓋：

- legacy `agent --line-listen` 路徑
- 純容器化的完整操作
- 多機正式部署

## 目前 canonical 埠號

- broker：`127.0.0.1:5361`
- line-worker webhook：`127.0.0.1:5357`
- ngrok tunnel 名稱：`line5357`

## Sidecar 狀態持久化

- sidecar 的執行狀態現在會保存在：
  - `D:\Bricks4Agent\.run\line-sidecar\data\broker.db`
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
- 已安裝且已登入的 `ngrok`
- 已填好 LINE 憑證的本機 worker 設定

目前若要有最佳 live 行為，通常還需要：

- repo 根目錄的 `Api.txt`
  - 給高階模型用的 OpenAI-compatible API key
- repo 根目錄的 `client_secret_*.json`
  - 給 Google Drive OAuth 使用
- `%LOCALAPPDATA%\ngrok\ngrok.yml`
  - 可用的 ngrok 設定

## 本機檔案與輸入

### 1. LINE worker 設定

檔案：

- [appsettings.json](/d:/Bricks4Agent/packages/csharp/workers/line-worker/appsettings.json)

這是本機檔案，不應提交到 git。

至少要有可用值：

- `Line.ChannelAccessToken`
- `Line.ChannelSecret`
- `Line.DefaultRecipientId`

### 2. 高階模型 API key

檔案：

- `D:\Bricks4Agent\Api.txt`

目前 sidecar 會：

- 讀取這個檔案
- 將內容注入 broker 的 `HighLevelLlm.ApiKey`

### 3. Google Drive OAuth client

檔案樣式：

- `D:\Bricks4Agent\client_secret_*.json`

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

- [line-sidecar.ps1](/d:/Bricks4Agent/packages/csharp/workers/line-worker/line-sidecar.ps1)

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
4. 注入本機 production override：
   - high-level API key
   - Google Drive OAuth 設定
   - Google Drive 預設身分模式與 shared delegated owner
5. 啟動 broker 到 `127.0.0.1:5361`
6. 啟動 line-worker 到 `*:5357`
7. 重建 ngrok tunnel `line5357`
8. 更新 LINE webhook endpoint，除非使用 `-SkipWebhookUpdate`
9. 等到 broker 與本機 webhook 真正 ready
10. 確認命名的 ngrok tunnel 確實存在

重要補充：

- 若 `127.0.0.1:4040` 的 ngrok admin API 尚未存在
- 腳本現在會自動啟動本機 ngrok agent：
  - `ngrok start --none --config %LOCALAPPDATA%\ngrok\ngrok.yml`

也就是說，現在文件的標準是：

- 如果 `up` 失敗，文件必須能解釋原因
- 不能再假設操作者自己猜到要怎麼先手動開 ngrok

第一次啟動可能比較慢，因為 broker 可能需要 seed 一部分本機資料後才會 ready。

## 啟動成功的判準

`up` 成功後，應該同時看到：

- `status` 顯示 broker PID 與 line-worker PID 都在跑
- `status` 顯示 ngrok PID 在跑
- `status` 顯示 ngrok public URL
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

目前後台已包含：

- LINE 使用者與對話
- 註冊政策
- 每位使用者高階權限
- browser records
- deployment targets
- tool specs
- Google Drive OAuth 與 delivery 操作

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
- `D:\Bricks4Agent\Api.txt`
- 這決定文件或網站原型能否先被生成

2. 可用的 Google OAuth client JSON
- `D:\Bricks4Agent\client_secret_*.json`
- callback URI 必須對應：
  - `http://127.0.0.1:5361/api/v1/google-drive/oauth/callback`

3. 正確的 Google Drive 交付模式
- 若你的需求是「所有 LINE 使用者都上傳到同一個 Google Drive」：
  - 應使用 `shared_delegated`
- 若改成 `user_delegated`，就會變成每位使用者各自授權自己的 Drive

4. 有效的 Drive 授權憑證已保存在目前 sidecar DB
- sidecar 現在使用的持久化 DB 是：
  - `D:\Bricks4Agent\.run\line-sidecar\data\broker.db`
- 若 `google_drive_delegated_credentials` 沒資料，交付結果只會落到本機，不會有雲端下載連結

5. LINE sidecar 已重啟到最新版本
- 目前的檔案交付、shared delegated owner、持久化 DB 都依賴最新 sidecar publish

目前行為：

- 若 Google Drive 上傳成功，LINE 回覆會優先使用 Drive 連結
- 若 Google Drive 上傳失敗，且目前 sidecar public URL 可用，broker 會改送短效簽名下載連結
- 若兩條路徑都不可用，才會退化成無連結通知

目前仍沒有專門的終端使用者下載頁；fallback 仍是 broker 直接提供的簽名下載端點。

## 目前高階模型

現在 live LINE 路徑的高階回應模型是：

- provider：`openai-compatible`
- model：`gpt-5.4-mini`

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

補充：

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

目前 live sidecar 常見路徑：

- `.run/line-sidecar/broker/managed-workspaces`

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
- ngrok tunnel 是否存在
- LINE webhook endpoint 是否 active
- line-worker PID 是否存在

常見原因：

- ngrok tunnel 掉了
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

### 3. broker 有起來，但 LINE 說 AI 服務暫時無法回應

常見原因：

- `Api.txt` 不存在或無法讀
- 上游 API key 無效
- high-level 模型 upstream 回 `401` 或 `400`
- sidecar publish output 吃到舊檔

檢查：

- `.run/line-sidecar/logs/broker.out.log`
- `.run/line-sidecar/logs/broker.err.log`

修正：

- 確認 `Api.txt` 內是有效 key
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

- 確認 repo 根目錄有可用的 `client_secret_*.json`
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

- `D:\Bricks4Agent\.run\line-sidecar`

主要 log：

- `.run/line-sidecar/logs/broker.out.log`
- `.run/line-sidecar/logs/broker.err.log`
- `.run/line-sidecar/logs/line-worker.out.log`
- `.run/line-sidecar/logs/line-worker.err.log`
- `.run/line-sidecar/logs/ngrok.out.log`
- `.run/line-sidecar/logs/ngrok.err.log`

## 目前尚未完成的前台能力

目前還沒有使用者前台可讓終端使用者：

- 查看自己的交付檔案歷史
- 直接登入後下載 artifact
- 透過 broker 授權規則取得安全下載

現在已有的是：

- 本機 admin 後台
- LINE 對話中的交付連結
- broker 內部 artifact records

未來如果系統直接對外服務，應補上：

- 經驗證的前台下載 API
- 使用者自己的 artifact 清單頁
- broker 控制的下載授權檢查

這是「前台應有功能」，目前尚未實作。

## 相關文件

- [CurrentArchitectureAndProgress-2026-03-26.md](../reports/CurrentArchitectureAndProgress-2026-03-26.md)
- [README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)
- [GoogleDriveDelivery.md](../designs/GoogleDriveDelivery.md)
- [AzureVmIisDeployment.md](../designs/AzureVmIisDeployment.md)
