# LINE Worker

English version:

- [README.md](/d:/Bricks4Agent/packages/csharp/workers/line-worker/README.md)

Canonical live path：

`LINE webhook -> ngrok public URL -> line-worker -> broker /api/v1/high-level/line/process`

## Worker 驗證

- broker 的 `/api/v1/high-level/line/process` 現在是 plain JSON，但不再是 trusted bypass。
- `line-worker` 必須使用 `Worker.Auth.WorkerType`、`Worker.Auth.KeyId`、`Worker.Auth.SharedSecret` 對 broker HTTP 請求簽章。
- `verify-high-level-process.ps1` 會優先讀取本機 `appsettings.json` 的 `Worker.Auth` 設定，自動附上簽章標頭。

完整操作手冊：

- [line-sidecar-runbook.zh-TW.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.zh-TW.md)
- [line-sidecar-runbook.md](/d:/Bricks4Agent/docs/manuals/line-sidecar-runbook.md)

`line-worker` 只是 ingress bridge。  
對話、需求澄清、查詢/production 分流、確認流程，屬於 broker 的 high-level layer，並透過 `HighLevelLlm` 與 execution/runtime LLM 設定分離。

已驗證現況：

- canonical local sidecar 埠號為 `5357/5361`
- signed live webhook verification 可回 `200`
- sidecar 啟動流程現在會自動補齊本機 ngrok agent
- `line-sidecar.ps1` 是 canonical operator entrypoint

目前查詢邊界：

- 明確 broker-mediated search 可用：`?search <keywords>`
- 一般 `?query` 仍走高階對話路徑
- 任意 query 尚未全面自動切成即時工具查詢，現行受控 live 路徑仍以顯式子命令為主

建議 sidecar 流程：

1. 複製 `appsettings.sidecar.example.json` 為本機 `appsettings.json`，填入真實 LINE 憑證。
2. 確認 `%LOCALAPPDATA%\\ngrok\\ngrok.yml` 存在，且內含有效 ngrok authtoken。
3. 執行 `line-sidecar.ps1 up`。
4. 執行 `line-sidecar.ps1 verify` 送出帶簽章的測試 webhook。
5. 用 `line-sidecar.ps1 status`、`line-sidecar.ps1 restart`、`line-sidecar.ps1 down` 管理執行狀態。

單一命令入口：

```powershell
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 up
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 status
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 restart
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify -MessageBase64Utf8 <base64-utf8>
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 verify-broker -UserId test-user -MessageBase64Utf8 <base64-utf8>
powershell -ExecutionPolicy Bypass -File .\packages\csharp\workers\line-worker\line-sidecar.ps1 down
```

這個統一腳本是 canonical 操作入口。它仍然會啟動獨立的 broker、line-worker、ngrok 程序，但已不再要求操作人員先手動把 ngrok admin API 撐起來。

預設 sidecar 埠號：

- broker：`5361`
- line-worker webhook：`5357`

選 `5357` 的原因：

- 這台機器已有可重用的 `http://*:5357/` URL ACL
- 可避開舊的長駐 `19090` worker
- 也避開較容易撞埠的 `8xxx` 區段
- sidecar 因此在這個埠上使用 `WebhookHost=\"*\"`

補充：

- `appsettings.json` 是本機檔案，不進 git。
- `line-sidecar.ps1` 是目前 Windows sidecar 的 canonical operator entrypoint。
- `start-sidecar-stack.ps1` 會建立 `.run/line-sidecar` 工作區，並將 LINE webhook 更新到目前 tunnel URL，除非使用 `-SkipWebhookUpdate`。
- sidecar 模式下，常見 managed workspace root 是 `.run/line-sidecar/broker/managed-workspaces`；正式 broker 設定可改成其他絕對 `HighLevelCoordinator:AccessRoot`。
- `verify-live-webhook.ps1` 會優先從 LINE API 讀目前 webhook endpoint，再 fallback 到 `.last-tunnel-url`。
- broker 與 webhook 驗證都應以 UTF-8 為準；shell 編碼不穩時，優先用 `-MessageFile` 或 `-MessageBase64Utf8`。
- `start-sidecar-stack.ps1`、`status-sidecar-stack.ps1`、`stop-sidecar-stack.ps1`、`verify-live-webhook.ps1`、`verify-high-level-process.ps1` 仍是 `line-sidecar.ps1` 背後的低階腳本。
- `line-sidecar.ps1 restart` 是 broker 或 worker 變更後的 canonical reload path。
- `start-with-tunnel.ps1` 是 legacy tunnel launcher，不是 canonical sidecar flow。
- 目前 canonical sidecar pair 是 webhook `5357` 與 broker `5361`。
