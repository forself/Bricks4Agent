# LINE Sidecar 操作手冊

日期：2026-03-26  
範圍：目前本機 Windows sidecar 的 LINE live ingress 路徑  
對象：操作人員 / 開發者  

## 用途

這份手冊說明目前 live 本機路徑的啟動、驗證、日常操作與基本排障方式：

`LINE webhook -> ngrok public URL -> line-worker -> broker /api/v1/high-level/line/process`

這是目前本機操作上的 canonical 路徑。

這份手冊不涵蓋：

- 舊的 `agent --line-listen` 路徑
- 純容器化的完整操作
- 正式多主機部署流程

## 目前 canonical 埠號

- broker：`127.0.0.1:5361`
- line-worker webhook：`127.0.0.1:5357`
- ngrok tunnel 名稱：`line5357`

## 前置條件

這台機器至少要有：

- Windows PowerShell 5.1 或更新版本
- 可發佈並執行 broker / line-worker 的 .NET SDK 或 runtime
- 已安裝並完成登入驗證的 `ngrok`
- LINE worker 本機設定內的有效 LINE 憑證

目前要讓 live 行為較完整，通常還需要：

- repo 根目錄的 `Api.txt`，供高階模型使用 OpenAI-compatible API key
- repo 根目錄的 `client_secret_*.json`，供 Google Drive OAuth 使用
- `%LOCALAPPDATA%\ngrok\ngrok.yml` 的有效 ngrok 設定

## 本機專用檔案與輸入

### 1. LINE worker 設定

檔案：

- [appsettings.json](/d:/Bricks4Agent/packages/csharp/workers/line-worker/appsettings.json)

這是本機專用檔案，不進 git。

至少要有正確值：

- `Line.ChannelAccessToken`
- `Line.ChannelSecret`
- `Line.DefaultRecipientId`

### 2. 高階模型 API key

檔案：

- `D:\Bricks4Agent\Api.txt`

目前 sidecar 行為：

- `start-sidecar-stack.ps1` 會讀這個檔案
- 並把 key 注入 broker 的 `HighLevelLlm.ApiKey`

### 3. Google Drive OAuth client

檔名模式：

- `D:\Bricks4Agent\client_secret_*.json`

目前 sidecar 行為：

- 會選第一個符合的檔案
- delegated redirect URI 會設為 `http://127.0.0.1:5361/api/v1/google-drive/oauth/callback`

## Canonical 操作命令

本機正常操作應統一透過：

- [line-sidecar.ps1](/d:/Bricks4Agent/packages/csharp/workers/line-worker/line-sidecar.ps1)

### 啟動

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
```

這是唯一正常的啟動命令。

不應再要求操作人員先手動啟動 broker、line-worker 或 ngrok。

### 查看狀態

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
```

### 重新啟動

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

### 驗證帶簽章的 LINE webhook

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -MessageBase64Utf8 <base64-utf8>
```

## `up` 實際會做什麼

目前啟動流程會依序做這些事：

1. 建立 `.run/line-sidecar`
2. 將 broker publish 到 `.run/line-sidecar/broker`
3. 將 line-worker publish 到 `.run/line-sidecar/line-worker`
4. 注入本機 production override：
   - high-level API key
   - Google Drive OAuth 設定
5. 啟動 broker 於 `127.0.0.1:5361`
6. 啟動 line-worker 於 `*:5357`
7. 重建 ngrok tunnel `line5357`
8. 更新 LINE webhook endpoint，除非使用 `-SkipWebhookUpdate`
9. 等待 broker 與本機 webhook 實際可連線後，才算啟動成功
10. 確認具名 ngrok tunnel 真的存在後，才算啟動成功

重要說明：

- 如果本機 `127.0.0.1:4040` 的 ngrok admin API 尚未存在
- 腳本現在會自動用以下方式拉起 ngrok agent：
  - `ngrok start --none --config %LOCALAPPDATA%\ngrok\ngrok.yml`

也就是說，啟動文件現在的標準是：

- `up` 不能成功，就要能在文件裡找到對應原因
- 不能再假設使用者自己知道要怎麼先把 ngrok 撐起來

第一次啟動可能會比較久，因為 broker 可能需要先做本機 RAG seed 才會變成 ready。

## 啟動成功後應看到的訊號

執行 `up` 後，至少應看到：

- `status` 顯示 broker PID 與 line-worker PID 都在執行
- `status` 顯示 ngrok PID 在執行
- `status` 顯示 ngrok public URL
- `status` 顯示 LINE webhook endpoint 且 `active = True`
- 後台頁面可開啟：
  - `http://127.0.0.1:5361/line-admin.html`
- 本機 broker 可回應：
  - `http://127.0.0.1:5361/api/v1/local-admin/status`
- 本機 webhook 驗證可成功：
  - `verify` 回 `Webhook status: 200`
- 公開 webhook 也應該已可路由，前提是具名 ngrok tunnel 已存在

如果 `up` 執行完後，這些條件沒有成立，就應視為啟動失敗。

## 後台

目前本機後台：

- `http://127.0.0.1:5361/line-admin.html`

目前行為：

- 只限 localhost
- 需要 local admin login
- 若資料庫中還沒有 admin 密碼，初始密碼為 `admin`
- 第一次登入必須修改密碼

目前後台已整合：

- LINE 使用者列表與標籤
- 註冊政策
- 每位使用者權限
- browser records
- deployment targets
- tool specs
- Google Drive OAuth 與 delivery 操作

## 目前高階模型

目前 live LINE 路徑使用的高階回應模型：

- provider：`openai-compatible`
- model：`gpt-5.4-mini`

這與下游 task 的 execution-model request 分開。

## 基本 live 使用方式

### 一般對話

直接在 LINE 輸入：

- `hello`
- `please help me clarify my requirements`

### 說明與個人資訊

- `?help`
- `?profile`

### 受控查詢

- `?search Central Weather Administration official site`
- `?s Central Weather Administration official site`

### 交通查詢

- `?rail Taipei Taichung today 18:00`
- `?hsr Taipei Taichung today 18:00`
- `?bus Taipei Taichung today 18:00`
- `?flight TPE KIX tomorrow`

### Production 流程

- `/create a website prototype`
- `#MyProject`
- `confirm`

## 使用者與工作目錄行為

每位高階 LINE 使用者都會在 broker 管理的 access root 下面取得自己的路徑：

- `conversations`
- `documents`
- `projects`

目前 live sidecar 常見位置：

- `.run/line-sidecar/broker/managed-workspaces`

正式 broker 設定可以改用其他絕對路徑。

## UTF-8 驗證原則

UTF-8 是硬性需求。

要做可靠的多語系驗證時，請優先用：

- `verify-high-level-process.ps1` 搭配 `-MessageFile`
- `verify-live-webhook.ps1` 搭配 `-MessageFile`
- 或 `-MessageBase64Utf8`

如果終端編碼不穩，不要信任直接在 shell 內輸入的文字。

中文與其他多語系測試，建議優先使用 UTF-8 檔案，而不是直接打字。

## 基本異常排除

### 1. LINE 完全沒有回應

先檢查：

- `line-sidecar.ps1 status`
- ngrok tunnel 是否存在
- LINE webhook endpoint 是否 active
- line-worker PID 是否還在

常見原因：

- ngrok tunnel 掉了
- webhook endpoint 沒更新
- line-worker 停掉
- ngrok agent 根本沒起來

處理：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 restart
```

### 2. 公開 webhook 回 `404` 或 ngrok 錯誤

症狀：

- 公開 webhook URL 回 `404`
- 回應含 `ERR_NGROK_3200` 等錯誤

原因：

- tunnel 已離線或變成 stale

處理：

- 先跑 `status`
- 再跑 `restart`

如果 tunnel 還是起不來：

- 檢查 `.run/line-sidecar/logs/ngrok.out.log`
- 檢查 `.run/line-sidecar/logs/ngrok.err.log`
- 確認 `%LOCALAPPDATA%\ngrok\ngrok.yml` 存在且 authtoken 有效

### 3. broker 有起來，但 LINE 說 AI 服務暫時無法回應

常見原因：

- `Api.txt` 不存在或不可讀
- 上游 API key 無效
- 高階模型 upstream 回 `401` 或 `400`
- sidecar publish output 是舊的

檢查：

- `.run/line-sidecar/logs/broker.out.log`
- `.run/line-sidecar/logs/broker.err.log`

處理：

- 確認 `Api.txt` 存在且內容有效
- 重新啟動 sidecar

### 4. Google Drive OAuth 出現 `invalid_state` 或 `state_expired`

原因：

- 重用了舊授權網址
- state 已被消耗或逾時

處理：

- 回到後台重新發起 OAuth
- 立刻使用新網址

### 5. Google Drive OAuth callback 回 `500`

常見原因：

- OAuth callback 相關程式變更後，sidecar broker 沒重啟
- publish output 是舊的
- `client_secret_*.json` 缺失或無效

處理：

- 確認 repo 根目錄有正確的 `client_secret_*.json`
- 執行 `line-sidecar.ps1 restart`

### 6. Google Drive upload 失敗並出現 `storageQuotaExceeded`

如果你走的是 service account 路徑：

- 個人 My Drive 資料夾不夠
- service account 上傳需要 Shared Drive 或 delegated-user flow

目前個人 Google 帳號的建議路徑：

- delegated OAuth user Drive

### 7. Drive 上傳成功了，但 LINE 沒收到交付通知

原因：

- 目前操作的是 synthetic/test 帳戶，不是真實 LINE `U...` 使用者
- notification queue 有建立，但 LINE 無法投遞到假 recipient

檢查：

- 後台的 user label
- 該使用者是否標成 `真實 LINE`

### 8. 後台打得開，但無法登入

目前規則：

- 第一次啟動且資料庫沒有 credential 時，密碼是 `admin`
- 第一次成功登入後，必須修改密碼

如果這是已使用過的 live sidecar，密碼可能早已被改過。

若不知道密碼，應從本機 broker DB / admin 層做重設，不要硬猜。

### 9. sidecar restart 因 publish output 被鎖住而失敗

常見原因：

- 舊的 broker 或 worker 程序仍握住檔案

目前已做的緩解：

- restart 會等待程序退出
- republish 前會先清空輸出目錄

若還是失敗：

- 先 `down`
- 確認 broker 與 worker 程序真的已經消失
- 再重新 `up`

## Log 與工作目錄

目前 sidecar runtime 目錄：

- `D:\Bricks4Agent\.run\line-sidecar`

重要 log：

- `.run/line-sidecar/logs/broker.out.log`
- `.run/line-sidecar/logs/broker.err.log`
- `.run/line-sidecar/logs/line-worker.out.log`
- `.run/line-sidecar/logs/line-worker.err.log`
- `.run/line-sidecar/logs/ngrok.out.log`
- `.run/line-sidecar/logs/ngrok.err.log`

## 這份手冊目前還沒涵蓋的內容

- 正式多主機部署下的 broker / line-worker 操作
- 強化過的遠端 admin auth
- browser worker runtime 的日常操作
- 完整 Azure IIS deployment 操作
- 完整 disaster recovery 流程

## 相關文件

- [CurrentArchitectureAndProgress-2026-03-26.md](../reports/CurrentArchitectureAndProgress-2026-03-26.md)
- [README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)
- [GoogleDriveDelivery.md](../designs/GoogleDriveDelivery.md)
- [AzureVmIisDeployment.md](../designs/AzureVmIisDeployment.md)
