# Worker Identity Auth Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 為 broker 補上統一的 worker identity 驗證，先覆蓋 `line-worker -> broker` 的 HTTP 呼叫與 function-pool `WORKER_REGISTER` 註冊，移除 `line-worker` 的免驗證特例。

**Architecture:** 新增共用的 HMAC 驗證服務與 nonce store，讓 broker 能驗證 `worker_type + key_id + timestamp + nonce + signature`。HTTP 路徑透過 middleware 驗證 worker 身分，function-pool registration 則在 `WorkerSession.HandleRegisterAsync()` 驗證後才允許進入 `WorkerRegistry`。`line-worker` 與 `worker-sdk` 負責補上 outbound signing。

**Tech Stack:** C# / .NET 8, ASP.NET Core middleware, BaseOrm/SQLite, xUnit integration/unit tests, existing worker-sdk / function-pool / broker services.

---

## File Map

### Create

- `packages/csharp/broker-core/Services/WorkerIdentityAuthService.cs`
- `packages/csharp/broker-core/Services/WorkerIdentityAuthOptions.cs`
- `packages/csharp/broker-core/Services/WorkerAuthNonceStore.cs`
- `packages/csharp/broker/Middleware/WorkerIdentityAuthMiddleware.cs`
- `packages/csharp/tests/unit/Core/WorkerIdentityAuthServiceTests.cs`
- `packages/csharp/tests/integration/Api/HighLevelWorkerAuthTests.cs`
- `packages/csharp/tests/unit/FunctionPool/WorkerSessionAuthTests.cs`
 

### Modify

- `packages/csharp/broker/Program.cs`
- `packages/csharp/broker/Middleware/BrokerAuthMiddleware.cs`
- `packages/csharp/broker/Middleware/EncryptionMiddleware.cs`
- `packages/csharp/broker/Endpoints/HighLevelEndpoints.cs`
- `packages/csharp/function-pool/Network/WorkerSession.cs`
- `packages/csharp/worker-sdk/WorkerHost.cs`
- `packages/csharp/worker-sdk/WorkerHostOptions.cs`
- `packages/csharp/workers/line-worker/Program.cs`
- `packages/csharp/workers/line-worker/InboundDispatcher.cs`
- `packages/csharp/tests/integration/Fixtures/BrokerFixture.cs`
- `packages/csharp/tests/integration/Middleware/EncryptionBypassTests.cs`
- `docs/manuals/line-sidecar-runbook.md`
- `docs/manuals/line-sidecar-runbook.zh-TW.md`

---

### Task 1: 共用 Worker Identity 驗證服務

**Files:**
- Create: `packages/csharp/broker-core/Services/WorkerIdentityAuthOptions.cs`
- Create: `packages/csharp/broker-core/Services/WorkerIdentityAuthService.cs`
- Create: `packages/csharp/broker-core/Services/WorkerAuthNonceStore.cs`
- Test: `packages/csharp/tests/unit/Core/WorkerIdentityAuthServiceTests.cs`

- [ ] **Step 1: 寫 failing unit tests**

測試覆蓋：
- 正確 HMAC 可驗過
- `key_id` 不存在失敗
- timestamp 超過容忍窗失敗
- nonce replay 失敗
- `worker_type` 無權呼叫指定 route 失敗

- [ ] **Step 2: 跑 unit test，確認 RED**

Run:
```powershell
dotnet test packages\csharp\tests\unit\Unit.Tests.csproj -v minimal --filter WorkerIdentityAuthServiceTests --disable-build-servers -p:UseSharedCompilation=false -m:1
```

Expected:
- 編譯失敗或測試失敗，因為 service/options/store 尚未存在

- [ ] **Step 3: 以最小程式碼建立 options / nonce store / auth service**

實作內容：
- `WorkerCredentialRecord`
- `WorkerRouteRule`
- `WorkerIdentityAuthOptions`
- `WorkerAuthNonceStore`
- `WorkerIdentityAuthService`

責任：
- 解析 credential
- 驗證 timestamp 容忍窗
- 驗證 nonce 未重複
- 驗證 route allowlist
- 驗證 HMAC-SHA256 signature

- [ ] **Step 4: 重跑 unit test，確認 GREEN**

Run:
```powershell
dotnet test packages\csharp\tests\unit\Unit.Tests.csproj -v minimal --filter WorkerIdentityAuthServiceTests --disable-build-servers -p:UseSharedCompilation=false -m:1
```

Expected:
- `WorkerIdentityAuthServiceTests` 全通過

- [ ] **Step 5: commit**

```powershell
git add packages/csharp/broker/Services/WorkerIdentityAuthOptions.cs packages/csharp/broker/Services/WorkerIdentityAuthService.cs packages/csharp/broker/Services/WorkerAuthNonceStore.cs packages/csharp/tests/unit/Services/WorkerIdentityAuthServiceTests.cs
git commit -m "feat: add worker identity auth service"
```

---

### Task 2: HTTP worker auth middleware 與 line-worker outbound signing

**Files:**
- Create: `packages/csharp/broker/Middleware/WorkerIdentityAuthMiddleware.cs`
- Modify: `packages/csharp/broker/Program.cs`
- Modify: `packages/csharp/broker/Middleware/BrokerAuthMiddleware.cs`
- Modify: `packages/csharp/broker/Middleware/EncryptionMiddleware.cs`
- Modify: `packages/csharp/workers/line-worker/Program.cs`
- Modify: `packages/csharp/workers/line-worker/InboundDispatcher.cs`
- Modify: `packages/csharp/tests/integration/Fixtures/BrokerFixture.cs`
- Create: `packages/csharp/tests/integration/Api/HighLevelWorkerAuthTests.cs`
- Modify: `packages/csharp/tests/integration/Middleware/EncryptionBypassTests.cs`

- [ ] **Step 1: 寫 failing integration tests**

測試案例：
- 無簽章呼叫 `/api/v1/high-level/line/process` 回 `401`
- 正確 `line-worker` 簽章呼叫成功
- 使用 `file-worker` 簽章呼叫 line path 回 `403`
- `/api/v1/local-admin/*` 仍維持原本 plain JSON bypass

- [ ] **Step 2: 跑 integration test，確認 RED**

Run:
```powershell
dotnet test packages\csharp\tests\integration\Integration.Tests.csproj -v minimal --filter HighLevelWorkerAuthTests --disable-build-servers -p:UseSharedCompilation=false -m:1
```

Expected:
- 測試失敗，因為 middleware 與 worker signing 尚未實作

- [ ] **Step 3: 加入 broker 端 HTTP worker auth middleware**

實作內容：
- 在 `/api/v1/high-level/line/*` 前驗 `X-B4A-*` headers
- 驗過才放行至 endpoint
- 未驗過回 `401/403`
- `BrokerAuthMiddleware` / `EncryptionMiddleware` 保持 plain JSON，但不再把 high-level line path 視為免驗證 caller path

- [ ] **Step 4: 在 line-worker 加入 outbound signing**

實作內容：
- `Program.cs` 讀取 `WorkerAuth:*`
- `InboundDispatcher` 對 broker HTTP POST 加入 `X-B4A-Worker-Type`
  `X-B4A-Key-Id`
  `X-B4A-Timestamp`
  `X-B4A-Nonce`
  `X-B4A-Signature`

- [ ] **Step 5: 重跑 targeted integration tests，確認 GREEN**

Run:
```powershell
dotnet test packages\csharp\tests\integration\Integration.Tests.csproj -v minimal --filter HighLevelWorkerAuthTests --disable-build-servers -p:UseSharedCompilation=false -m:1
```

Expected:
- `HighLevelWorkerAuthTests` 全通過

- [ ] **Step 6: 重跑 bypass regression tests**

Run:
```powershell
dotnet test packages\csharp\tests\integration\Integration.Tests.csproj -v minimal --filter EncryptionBypassTests --disable-build-servers -p:UseSharedCompilation=false -m:1
```

Expected:
- `local-admin` bypass 仍通過
- high-level line path 行為改為 worker-auth protected，測試同步更新後通過

- [ ] **Step 7: commit**

```powershell
git add packages/csharp/broker/Middleware/WorkerIdentityAuthMiddleware.cs packages/csharp/broker/Program.cs packages/csharp/broker/Middleware/BrokerAuthMiddleware.cs packages/csharp/broker/Middleware/EncryptionMiddleware.cs packages/csharp/workers/line-worker/Program.cs packages/csharp/workers/line-worker/InboundDispatcher.cs packages/csharp/tests/integration/Fixtures/BrokerFixture.cs packages/csharp/tests/integration/Api/HighLevelWorkerAuthTests.cs packages/csharp/tests/integration/Middleware/EncryptionBypassTests.cs
git commit -m "feat: require worker auth on high-level line routes"
```

---

### Task 3: function-pool `WORKER_REGISTER` signing 與驗證

**Files:**
- Modify: `packages/csharp/worker-sdk/WorkerHostOptions.cs`
- Modify: `packages/csharp/worker-sdk/WorkerHost.cs`
- Modify: `packages/csharp/function-pool/Network/WorkerSession.cs`
- Test: `packages/csharp/tests/unit/WorkerSdk/WorkerRegisterSigningTests.cs`

- [ ] **Step 1: 寫 failing tests**

測試覆蓋：
- `WorkerHost` 產出的 register payload 包含 `worker_type/key_id/timestamp/nonce/signature`
- broker 端驗證 register 簽章成功
- replay / key 不存在 / signature 錯誤被拒

- [ ] **Step 2: 跑 tests，確認 RED**

Run:
```powershell
dotnet test packages\csharp\tests\unit\Unit.Tests.csproj -v minimal --filter WorkerRegisterSigningTests --disable-build-servers -p:UseSharedCompilation=false -m:1
```

Expected:
- 測試失敗，因為 register signing 尚未存在

- [ ] **Step 3: 擴充 worker-sdk register payload**

實作內容：
- `WorkerHostOptions` 新增 `WorkerType / KeyId / SharedSecret`
- `WorkerHost.RegisterAsync()` 產生簽章並放入 register payload

- [ ] **Step 4: 擴充 broker `WorkerSession.HandleRegisterAsync()`**

實作內容：
- 解析新欄位
- 呼叫 `WorkerIdentityAuthService` 進行 register 驗證
- 驗過才 `WorkerRegistry.Register()`

- [ ] **Step 5: 重跑 tests，確認 GREEN**

Run:
```powershell
dotnet test packages\csharp\tests\unit\Unit.Tests.csproj -v minimal --filter WorkerRegisterSigningTests --disable-build-servers -p:UseSharedCompilation=false -m:1
```

Expected:
- `WorkerRegisterSigningTests` 全通過

- [ ] **Step 6: commit**

```powershell
git add packages/csharp/worker-sdk/WorkerHostOptions.cs packages/csharp/worker-sdk/WorkerHost.cs packages/csharp/function-pool/Network/WorkerSession.cs packages/csharp/tests/unit/WorkerSdk/WorkerRegisterSigningTests.cs
git commit -m "feat: sign and verify worker registration"
```

---

### Task 4: 設定、runbook 與全面驗證

**Files:**
- Modify: `packages/csharp/broker/appsettings.Development.example.json`
- Modify: `packages/csharp/broker/appsettings.json`
- Modify: `packages/csharp/workers/line-worker/appsettings.example.json`
- Modify: `packages/csharp/workers/line-worker/appsettings.sidecar.example.json`
- Modify: `docs/manuals/line-sidecar-runbook.md`
- Modify: `docs/manuals/line-sidecar-runbook.zh-TW.md`

- [ ] **Step 1: 補設定檔樣板**

新增：
- broker `WorkerAuth:Credentials`
- line-worker `WorkerAuth:*`
- 需要時補 `WorkerAuth:Enforce`

- [ ] **Step 2: 更新 runbook**

寫清楚：
- worker secret 怎麼設定
- `line-sidecar` 啟動前需要哪些 config
- rotate secret 時的順序
- broker line route 不再是免驗證 trusted path

- [ ] **Step 3: 跑完整驗證**

Run:
```powershell
dotnet test packages\csharp\tests\unit\Unit.Tests.csproj -v minimal --disable-build-servers -p:UseSharedCompilation=false -m:1
dotnet test packages\csharp\tests\integration\Integration.Tests.csproj -v minimal --disable-build-servers -p:UseSharedCompilation=false -m:1
dotnet run --project packages\csharp\broker\verify\Broker.Verify.csproj --disable-build-servers
npm.cmd --prefix packages\javascript\browser run test
```

Expected:
- unit 全綠
- integration 全綠
- broker verify 通過
- browser tests 維持全綠

- [ ] **Step 4: commit**

```powershell
git add packages/csharp/broker/appsettings.Development.example.json packages/csharp/broker/appsettings.json packages/csharp/workers/line-worker/appsettings.example.json packages/csharp/workers/line-worker/appsettings.sidecar.example.json docs/manuals/line-sidecar-runbook.md docs/manuals/line-sidecar-runbook.zh-TW.md
git commit -m "docs: document worker identity auth configuration"
```

---

## Spec Coverage Check

- HTTP `line-worker -> broker` 驗證：Task 2
- function-pool `WORKER_REGISTER` 驗證：Task 3
- 每種 worker 一組 credential：Task 1 / Task 4
- trusted path 改成 authenticated worker path：Task 2
- docs / runbook 同步：Task 4

## Placeholder Check

- 無 `TBD` / `TODO`
- 每個 task 都有檔案、驗證命令、預期結果

## Type Consistency Check

- 服務名稱統一為 `WorkerIdentityAuthService`
- options 統一為 `WorkerIdentityAuthOptions`
- nonce store 統一為 `WorkerAuthNonceStore`
- `WorkerType / KeyId / SharedSecret` 命名在 broker 與 worker-sdk 一致
