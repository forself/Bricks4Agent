# Bricks4Agent 完整測試計畫設計

日期：2026-03-29
類型：測試架構設計規格
狀態：Draft

---

## 1. 現狀與問題

### 1.1 現有測試基礎

| 面向 | 現狀 |
|------|------|
| 測試框架 | 無（自建 Assert 函式，Console.WriteLine 輸出） |
| 單元/自驗證 | `broker-tests` + `broker/verify` 並存，已覆蓋 query/browser helper、artifact delivery、signed download、JSON redaction、middleware bypass |
| 整合測試 | 7 步線性流程，需手動啟動 Broker |
| E2E | e2e-bridge 互動式控制台（手動） |
| 前端測試 | 無 |
| UI 測試 | 無 |
| 使用者行為模擬 | 無 |
| CI/CD 整合 | 無（測試專案未加入 ControlPlane.slnx） |

### 1.2 關鍵覆蓋缺口

**後端仍缺乏成套自動化覆蓋的高風險元件：**
- `PolicyEngine`（7 條治理規則 — 系統安全核心）
- `BrokerService` PEP 16 步管線
- `SessionService`（ECDH 交握、Session 生命週期）
- `EnvelopeCrypto`（AES-256-GCM 加解密）
- `ScopedTokenService`（Token 發行與驗證）
- `PermissionService`（角色/能力/授權）
- `WorkerRegistry`（Round-Robin、併發安全）
- `ContainerManager`（Docker/Podman 容器管理）

**已由 `broker/verify` 覆蓋，但尚未遷移到正式測試框架的元件：**
- `LineArtifactDeliveryService`（Drive 遞送 + Broker 簽名下載 fallback）
- `SidecarPublicUrlResolver`（Artifact 公開 URL 解析）
- `BrokerArtifactDownloadOptions`（簽名下載端點設定）
- `BrokerArtifactDownloadService`（簽名產生、驗證、路徑硬化）
- `ArtifactDownloadEndpoints`（public signed download endpoint）

**Worker 層零覆蓋：**
- LINE Worker 5 個 Capability Handler
- File Worker 6 個 Handler（含沙箱驗證）
- Browser Worker（Playwright 操作）
- Worker SDK（TCP 連線、自動重連、心跳）

**前端零覆蓋：**
- 70+ UI 元件（mount/destroy 生命週期）
- DynamicPageRenderer + TriggerEngine + FieldResolver
- 主題切換（light/dark）
- i18n（zh-TW/en 切換）
- line-admin.html 管理介面（7 分頁、35+ API 整合）

---

## 2. 測試架構設計

### 2.1 測試金字塔

```
                    ┌─────────┐
                    │  E2E /  │  ← 使用者行為模擬（Playwright）
                    │ 行為模擬 │     LINE → Broker → Worker 全鏈路
                    ├─────────┤
                  ┌─┤ UI 測試  ├─┐  ← 元件渲染、互動、主題、i18n
                  │ └─────────┘ │    Admin 介面功能驗證
                ┌─┤  整合測試   ├─┐  ← API 端點、中介軟體管線
                │ └─────────────┘ │    Worker 註冊/調度、快取叢集
              ┌─┤    單元測試     ├─┐  ← 服務邏輯、ORM、加密、策略
              │ └─────────────────┘ │
              └─────────────────────┘
```

### 2.2 技術選型

| 層級 | 框架 | 理由 |
|------|------|------|
| C# 單元測試 | **xUnit** + FluentAssertions + NSubstitute | .NET 生態最成熟，取代自建 Assert |
| C# 整合測試 | **WebApplicationFactory** (Microsoft.AspNetCore.Mvc.Testing) | 記憶體內 Broker，無需手動啟動 |
| 前端單元測試 | **Vitest** + jsdom | 零配置、ES Module 原生支援、適合 vanilla JS |
| UI 元件測試 | **Playwright Component Testing** 或 Vitest + jsdom DOM 斷言 | 真實瀏覽器渲染 |
| E2E / 行為模擬 | **Playwright** (.NET + JS 雙軌) | 跨瀏覽器、支援 API + UI 混合測試 |
| 覆蓋率 | **Coverlet** (C#) + **c8** (JS) | 標準覆蓋率工具 |

### 2.3 專案結構

```
packages/csharp/tests/
├── unit/                          # C# 單元測試
│   ├── Unit.Tests.csproj          # xUnit 專案
│   ├── Core/
│   │   ├── PolicyEngineTests.cs
│   │   ├── BrokerServiceTests.cs
│   │   ├── SessionServiceTests.cs
│   │   ├── PermissionServiceTests.cs
│   │   ├── SharedContextServiceTests.cs
│   │   ├── PlanServiceTests.cs
│   │   └── IdGenTests.cs
│   ├── Crypto/
│   │   ├── EnvelopeCryptoTests.cs
│   │   ├── SessionKeyStoreTests.cs
│   │   └── ScopedTokenServiceTests.cs
│   ├── Data/
│   │   ├── BrokerDbTests.cs
│   │   └── BrokerDbInitializerTests.cs
│   ├── Workers/
│   │   ├── WorkerHostTests.cs
│   │   ├── LineWorker/
│   │   │   ├── TextCapabilityTests.cs
│   │   │   ├── ApprovalCapabilityTests.cs
│   │   │   ├── SlashCommandCapabilityTests.cs
│   │   │   └── InboundDispatcherTests.cs
│   │   ├── FileWorker/
│   │   │   ├── ReadFileHandlerTests.cs
│   │   │   ├── WriteFileHandlerTests.cs
│   │   │   └── SandboxPathTests.cs
│   │   └── BrowserWorker/
│   │       └── BrowserReadHandlerTests.cs
│   ├── FunctionPool/
│   │   ├── WorkerRegistryTests.cs
│   │   ├── PoolDispatcherTests.cs
│   │   ├── StrictPoolDispatcherTests.cs
│   │   ├── FallbackDispatcherTests.cs
│   │   ├── HealthMonitorTests.cs
│   │   └── ContainerManagerTests.cs
│   ├── Delivery/
│   │   ├── LineArtifactDeliveryServiceTests.cs  # Drive + Broker fallback
│   │   ├── SidecarPublicUrlResolverTests.cs
│   │   └── ArtifactDownloadSignatureTests.cs    # 簽名產生與驗證
│   └── Cache/
│       ├── CacheEngineTests.cs
│       ├── FrameCodecTests.cs
│       ├── LeaderElectionTests.cs
│       └── ReplicationLogTests.cs
│
├── integration/                   # C# 整合測試
│   ├── Integration.Tests.csproj
│   ├── Fixtures/
│   │   ├── BrokerFixture.cs       # WebApplicationFactory<Program>
│   │   ├── CacheServerFixture.cs
│   │   └── TestDatabaseFixture.cs
│   ├── Api/
│   │   ├── SessionEndpointsTests.cs
│   │   ├── TaskEndpointsTests.cs
│   │   ├── HighLevelEndpointsTests.cs
│   │   ├── AdminEndpointsTests.cs
│   │   ├── PlanEndpointsTests.cs
│   │   ├── WorkerEndpointsTests.cs
│   │   ├── ArtifactDownloadTests.cs  # Broker 簽名下載端點
│   │   └── DeliveryEndpointsTests.cs  # Drive + fallback 遞送
│   ├── Middleware/
│   │   ├── EncryptionMiddlewareTests.cs
│   │   ├── BrokerAuthMiddlewareTests.cs
│   │   └── AuditMiddlewareTests.cs
│   ├── Pipeline/
│   │   └── PepPipelineTests.cs    # 16 步 PEP 完整管線
│   └── WorkerIntegration/
│       ├── WorkerRegistrationTests.cs
│       ├── FunctionDispatchTests.cs
│       └── WorkerHealthTests.cs
│
├── broker-tests/                  # 保留現有測試（向後相容）
│   └── ...
├── e2e-bridge/                    # 保留現有 E2E
│   └── ...
│
packages/javascript/browser/
├── __tests__/                     # 前端單元測試
│   ├── vitest.config.js
│   ├── setup.js                   # jsdom 初始化
│   ├── components/
│   │   ├── common/
│   │   │   ├── Button.test.js
│   │   │   ├── Modal.test.js
│   │   │   ├── Table.test.js
│   │   │   ├── Toast.test.js
│   │   │   └── ...
│   │   ├── form/
│   │   │   ├── Input.test.js
│   │   │   ├── Select.test.js
│   │   │   └── ...
│   │   └── layout/
│   │       ├── Navbar.test.js
│   │       ├── Sidebar.test.js
│   │       └── ...
│   ├── page-generator/
│   │   ├── FieldResolver.test.js
│   │   ├── TriggerEngine.test.js
│   │   ├── DynamicPageRenderer.test.js
│   │   └── PageDefinitionAdapter.test.js
│   ├── theme/
│   │   └── ThemeSwitching.test.js
│   └── i18n/
│       └── Locale.test.js
│
tests/                             # E2E / 行為模擬
├── e2e/
│   ├── playwright.config.ts
│   ├── fixtures/
│   │   ├── broker-setup.ts        # 啟動 Broker + Worker
│   │   └── test-user.ts           # 模擬 LINE 使用者
│   ├── scenarios/
│   │   ├── line-conversation.spec.ts
│   │   ├── admin-console.spec.ts
│   │   ├── scaffold-flow.spec.ts
│   │   ├── approval-workflow.spec.ts
│   │   └── artifact-delivery.spec.ts
│   └── ui/
│       ├── admin-dashboard.spec.ts
│       ├── admin-conversations.spec.ts
│       ├── admin-functions.spec.ts
│       └── component-catalog.spec.ts
```

---

## 3. 單元測試設計

### 3.1 優先級 P0 — 安全與治理核心

#### PolicyEngine Tests

```
測試目標：7 條治理規則的正確性
測試策略：每條規則獨立測試 + 組合場景

測試案例：
├── Rule 1: Epoch 驗證
│   ├── 有效 epoch → 通過
│   ├── 過期 epoch → 拒絕 (EPOCH_EXPIRED)
│   └── 無 epoch → 拒絕
├── Rule 2: 風險等級
│   ├── LOW 操作 → 通過
│   ├── HIGH 操作 + 無審核 → 拒絕
│   └── HIGH 操作 + 已審核 → 通過
├── Rule 3: 路由匹配
│   ├── 已註冊路由 → 通過
│   └── 未知路由 → 拒絕 (UNKNOWN_ROUTE)
├── Rule 4: Scope 驗證
│   ├── Scope 包含目標 → 通過
│   └── Scope 不含目標 → 拒絕
├── Rule 5: 路徑沙箱
│   ├── 路徑在沙箱內 → 通過
│   ├── 路徑穿越 (../) → 拒絕
│   └── 絕對路徑超出範圍 → 拒絕
├── Rule 6: 指令黑名單
│   ├── 安全指令 → 通過
│   └── 黑名單指令 (rm -rf, DROP TABLE) → 拒絕
├── Rule 7: Schema 驗證
│   ├── 符合 Schema → 通過
│   └── 欄位缺失/型別錯誤 → 拒絕
└── 組合場景
    ├── 所有規則通過 → ApprovedRequest
    ├── 第一條失敗即短路 → 正確錯誤碼
    └── 多條同時失敗 → 回報第一個
```

#### EnvelopeCrypto Tests

```
測試案例：
├── ECDH 金鑰交換
│   ├── 產生有效金鑰對
│   ├── 雙方 DeriveKey 結果一致
│   └── 不同金鑰對產生不同 SharedSecret
├── AES-256-GCM 加解密
│   ├── 加密後解密 → 原文一致
│   ├── 竄改密文 → 解密失敗 (AuthenticationTag 不符)
│   ├── 錯誤金鑰 → 解密失敗
│   ├── 空訊息 → 正常處理
│   └── 大訊息 (1MB) → 正常處理
├── AAD (Additional Authenticated Data)
│   ├── 正確 AAD → 解密成功
│   ├── 竄改 AAD → 解密失敗
│   └── req: vs resp: 方向標記正確性
└── 序列號
    ├── 遞增序列號 → 接受
    ├── 重放（相同序列號）→ 拒絕
    └── 亂序（跳號）→ 依設定接受或拒絕
```

#### SessionService Tests

```
測試案例：
├── Session 生命週期
│   ├── Register → 產生 SessionId + Token
│   ├── Heartbeat → 更新 LastSeen
│   ├── Close → 標記為 Closed
│   └── 過期 Session → 自動清除
├── Token 驗證
│   ├── 有效 Token → 回傳 Session 資訊
│   ├── 過期 Token → 拒絕
│   ├── 竄改 Token → 拒絕
│   └── 已撤銷 Token → 拒絕
└── 併發安全
    ├── 同時 Register 多個 Session → 各自獨立
    └── 同時 Heartbeat + Close → 無競態
```

### 3.2 優先級 P1 — 業務邏輯

#### Worker Handler Tests

```
LINE Worker:
├── TextCapability
│   ├── 正常文字訊息 → 回傳處理結果
│   ├── 空訊息 → 適當錯誤
│   └── 超長訊息 (>5000 字) → 截斷處理
├── ApprovalCapability
│   ├── RegisterApproval → 產生 approvalKey
│   ├── "approve" / "y" → Approved
│   ├── "deny" / "n" → Denied
│   ├── 逾時 (300s) → Timeout
│   └── 重複審核 → 忽略
├── SlashCommandCapability
│   ├── /help → 回傳說明
│   ├── /status → 回傳狀態
│   ├── /clear → 清除對話
│   └── 未知指令 → 適當提示

File Worker:
├── SandboxPath 驗證（高優先）
│   ├── 合法路徑 → 通過
│   ├── ../ 穿越 → 拒絕
│   ├── 符號連結逃逸 → 拒絕
│   └── 絕對路徑超出 SandboxRoot → 拒絕
├── ReadFileHandler
│   ├── 正常讀取 → 回傳內容
│   ├── 超過 100KB → 截斷
│   └── 檔案不存在 → 錯誤
└── WriteFileHandler
    ├── 正常寫入 → 成功
    ├── 沙箱外寫入 → 拒絕
    └── 目錄不存在 → 自動建立

Browser Worker:
├── BrowserReadHandler
│   ├── 正常 URL → 回傳 title + content_text
│   ├── 無效 URL → 錯誤
│   └── 逾時 → 適當錯誤
```

#### FunctionPool Tests

```
WorkerRegistry:
├── 註冊/反註冊
│   ├── Register → Worker 可被查詢
│   ├── Deregister → Worker 不可查詢
│   └── 重複註冊 → 更新而非新增
├── Round-Robin 負載平衡
│   ├── 3 Workers → 依序分配
│   ├── 1 Worker 斷線 → 跳過
│   └── 全部斷線 → 回傳 null
├── 併發安全 (H-4, H-9, H-11)
│   ├── 100 併發 GetAvailableWorker → 無例外
│   ├── 同時 Register + Deregister → 無競態
│   └── ActiveTask Increment/Decrement → 計數正確

Dispatchers:
├── PoolDispatcher
│   ├── Worker 可用 → 分派成功
│   ├── Worker 不可用 → 重試 (MaxRetries=2)
│   └── 全部失敗 → 回傳 DispatchFailed
├── StrictPoolDispatcher
│   ├── Worker 可用 → 分派
│   └── 不可用 → 立即失敗（無 fallback）
└── FallbackDispatcher
    ├── Pool 成功 → 使用 Pool
    ├── Pool 失敗 + InProcess 可處理 → fallback
    └── Pool 失敗 + InProcess 不可處理 → 失敗
```

### 3.3 優先級 P2 — 快取與資料層

```
CacheEngine:
├── KV 基本操作 (Get/Set/Delete/Exists)
├── TTL 過期
├── CAS (Compare-And-Swap)
├── 分散式鎖 (Lock/Unlock/Fencing Token)
├── Pub/Sub (Subscribe/Publish/PubMessage)

FrameCodec:
├── 編碼 → 解碼 → 原訊息一致
├── 魔術位元組 0xCA 0xCE 驗證
├── 超過 MaxPayloadSize → 拒絕
├── 不完整 Frame → 等待更多資料

LeaderElection:
├── 單節點 → 自動成為 Leader
├── 多節點 → 選出唯一 Leader
├── Leader 斷線 → 觸發新選舉
├── 投票規則 (每 term 最多一票)

ReplicationLog:
├── Append → LSN 遞增
├── GetEntriesAfter → 正確範圍
├── 超過上限 (100,000) → 截斷
└── 併發 Append → LSN 唯一
```

### 3.4 優先級 P1 — Artifact 遞送與 Broker 下載

```
LineArtifactDeliveryService:
├── Drive 上傳成功 → 通知含 Drive 連結（無內部路徑）
├── Drive 上傳失敗 → fallback 至 Broker 簽名下載連結
├── Drive 成功但無 DownloadLink → 僅含預覽連結
├── Drive Success=false (quota_exceeded) → 降級通知
├── local-only 交付 → 保持 neutral message，不誤報 Drive failure
└── JSON redaction 驗證（不暴露 `DocumentsRoot`、`FilePath`、`Artifact`）

ArtifactDownloadSignature:
├── 產生簽名 → sig 與 exp 正確格式
├── 驗證有效簽名 → 通過
├── 過期簽名 (exp < now) → `410 Gone`
├── 竄改 sig → 拒絕
├── 竄改 artifactId → 拒絕（sig 不符）
├── exp 格式不合法 → 拒絕
└── out-of-root / hard link / junction / 缺檔 → 拒絕

SidecarPublicUrlResolver:
├── 有效 `.last-tunnel-url` → 解析為 sidecar public base URL
├── 無設定或內容無效 → 回傳 `null`
└── 搭配 `BrokerArtifactDownloadService` 產生正確的 signed URL 格式
```

---

## 4. 整合測試設計

### 4.1 測試基礎設施

#### BrokerFixture（記憶體內 Broker）

```csharp
// 使用 WebApplicationFactory 取代手動啟動
public class BrokerFixture : IAsyncLifetime
{
    public WebApplicationFactory<Program> Factory { get; private set; }
    public HttpClient Client { get; private set; }

    public async Task InitializeAsync()
    {
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // 替換為 in-memory SQLite
                    // 替換為 NoOpContainerManager
                    // 注入測試用 ECDH 金鑰
                });
            });
        Client = Factory.CreateClient();
    }
}
```

### 4.2 API 端點測試

```
Session 端點：
├── POST /sessions/register → ECDH 交握成功
├── POST /sessions/heartbeat → Session 活躍
├── POST /sessions/close → Session 關閉
└── 過期 Session 請求 → 401

PEP 管線完整流程：
├── 合法請求 → 通過 16 步 → 執行成功
├── 無效 Epoch → Step 3 拒絕
├── 無權限 → Step 7 拒絕
├── 超過配額 → Step 9 拒絕
├── 黑名單指令 → PolicyEngine 拒絕
└── 加密請求 → 解密 → 處理 → 加密回應

中介軟體管線：
├── EncryptionMiddleware
│   ├── 加密請求 → 正確解密
│   ├── 未加密 + 非排除路徑 → 400
│   ├── /api/v1/local-admin/* → 不走加密
│   └── /api/v1/high-level/line/* → plain JSON
├── BrokerAuthMiddleware
│   ├── 有效 Token → 通過
│   ├── 無 Token → 401
│   └── 排除路徑 → 跳過認證
└── AuditMiddleware
    ├── 請求/回應 → 產生 AuditEvent
    └── 敏感路徑 → 記錄但遮蔽 payload

Artifact 下載端點：
├── GET /api/v1/artifacts/download/{id}?exp=...&sig=... → 200 + 檔案
├── 過期簽名 (exp < now) → 410
├── 竄改簽名 → 403
├── 不存在的 artifactId → 404
└── 此端點不走加密/認證中間件（public endpoint）

Worker 整合：
├── Worker 註冊 (TCP) → WorkerRegistry 記錄
├── 心跳 → 更新 LastHeartbeat
├── 函式分派 → Worker 執行 → 結果回傳
├── Worker 斷線 → HealthMonitor 偵測 → 標記 Disconnected
└── 重連 → 重新註冊
```

### 4.3 快取叢集整合

```
單節點：
├── 啟動 CacheServer → 自動成為 Leader
├── Client Get/Set/Delete → 成功
└── Client CAS/Lock → 成功

多節點（3 節點）：
├── 啟動 3 節點 → 選出 1 Leader + 2 Follower
├── Write → Leader → 複製到 Follower
├── Read → 任意節點
├── StrongGet → 導向 Leader
├── Leader 停機 → 新 Leader 選出
├── Follower Write → REDIRECT 到 Leader
└── 網路分區 → 分區側無法選出 Leader（少數派）
```

---

## 5. 前端 UI 測試設計

### 5.1 元件單元測試（Vitest + jsdom）

```
每個元件測試涵蓋：
├── 渲染
│   ├── mount(container) → DOM 正確產生
│   ├── 帶 options 渲染 → 正確套用
│   └── destroy() → DOM 清除、事件解綁
├── 互動
│   ├── 點擊/輸入 → 事件觸發
│   ├── getValue() → 回傳正確值
│   └── setValue(v) → UI 更新
├── 主題
│   ├── data-theme="light" → 正確樣式
│   └── data-theme="dark" → 對應 CSS 變數
└── i18n
    ├── Locale.setLang('zh-TW') → 中文文字
    └── Locale.setLang('en') → 英文文字

優先測試元件（依使用頻率）：
├── P0: Button, Input, Select, Modal, Toast, Table, Form
├── P1: Navbar, Sidebar, Tabs, Card, Alert, Pagination
├── P2: DatePicker, Upload, RichTextEditor, Chart
└── P3: 其餘特化元件
```

### 5.2 頁面產生器測試

```
FieldResolver:
├── 30 種 FieldType → 各自產生正確元件
├── 未知 FieldType → fallback 或錯誤
└── options 正確傳遞

TriggerEngine:
├── visibility 觸發 → 欄位顯示/隱藏
├── value 觸發 → 目標值更新
├── options 觸發 → 選項清單更新
├── disabled 觸發 → 欄位啟用/停用
├── required 觸發 → 必填切換
├── validation 觸發 → 驗證規則生效
├── style 觸發 → CSS 變更
└── custom 觸發 → 自訂回呼執行

DynamicPageRenderer:
├── JSON 定義 → 完整頁面渲染
├── 含觸發器的定義 → 欄位互動正確
├── 空定義 → 空頁面（不崩潰）
└── 格式轉換 (PageDefinitionAdapter) → 新舊格式雙向正確
```

### 5.3 管理介面測試（Playwright）

```
line-admin.html 功能測試：
├── 載入
│   ├── 頁面載入 → 7 分頁全部可見
│   └── 預設分頁 → 正確顯示
├── 分頁切換
│   ├── 點擊各分頁 → 對應內容顯示
│   └── 分頁間切換 → 狀態獨立
├── tab-line: LINE 與使用者
│   ├── 系統摘要（對話數、Context 數、Vector 數）
│   ├── 使用者列表載入 + 搜尋
│   ├── 使用者核准/拒絕
│   ├── 高階權限控制（查詢、交通查詢、建立任務、瀏覽器授權、部署）
│   ├── 註冊政策切換（allow_all / manual_review / deny_all）
│   ├── 交付檔案管理（列表、詳細 JSON 檢視）
│   ├── Google Drive 交付（OAuth 授權、檔案上傳、分享模式、身分模式）
│   ├── Broker 下載 fallback（Drive 失敗 → broker 簽名連結顯示在通知內容中）
│   └── 對話紀錄（訊息瀏覽、測試訊息發送、對話清除）
├── tab-workflow: Workflow
│   ├── Tasks / Plans 摘要
│   ├── Execution Intents 列表與詳細
│   └── Handoffs 列表與詳細
├── tab-browser: Browser 綁定
│   ├── Site Bindings CRUD
│   ├── User Grants CRUD
│   ├── System Bindings CRUD
│   ├── Session Leases 簽發/撤銷
│   └── Browser Execute + Recent Executions
├── tab-deployment: Deployment
│   ├── Targets 管理（VM Host、Port、SSL、IIS）
│   ├── 部署預覽 / 執行
│   └── Recent Deployments
├── tab-delivery: 交付記錄
│   ├── 交付歷史（按狀態篩選：completed/partial/failed）
│   ├── Drive 上傳重試
│   ├── Drive 預覽連結
│   └── Drive 失敗後觀察 broker fallback 通知內容
├── tab-alerts: 系統警示
│   ├── 時間範圍篩選（1h/6h/24h/7d）
│   └── 嚴重等級（Error/Warning/Info）
├── tab-tools: Tool Specs
│   ├── Tool 列表（含篩選）
│   └── Tool 詳細資訊（JSON 檢視）
├── API 整合
│   ├── API 成功 → 資料正確顯示
│   ├── API 失敗 → 錯誤提示
│   ├── API 逾時 → loading 狀態 → 錯誤
│   └── requestChain 序列化 → 無並行競態
├── 登入系統
│   ├── 密碼認證 → 登入成功
│   ├── 首次登入 → 強制變更密碼
│   └── Session 過期 → 重導登入
└── 響應式
    ├── 桌面 (1920x1080) → CSS Grid 側邊欄 + 主內容區
    ├── 平板 (≤1180px) → 單欄佈局
    └── 自動刷新（5 秒間隔）→ 資料即時更新
```

---

## 6. 使用者行為模擬（E2E）

### 6.1 測試架構

```typescript
// Playwright + 自訂 Fixture
// 同時模擬 LINE webhook + Admin UI + Broker API

test.describe('LINE 對話完整流程', () => {
    // Fixture: 啟動 Broker + LINE Worker
    // 模擬: LINE Platform webhook POST

    test('使用者傳送文字訊息 → 收到回應', async () => {
        // 1. POST /webhook/line (模擬 LINE 平台)
        // 2. 驗證 Broker 處理
        // 3. 驗證回應訊息格式
    });
});
```

### 6.2 情境測試案例

#### Scenario 1: LINE 對話全鏈路

```
前置：Broker 運行 + LINE Worker 已註冊

步驟：
1. [LINE 使用者] 傳送「你好」
2. [LINE Worker] 接收 webhook → 轉發 Broker
3. [Broker] HighLevelCoordinator 處理 → 辨識意圖
4. [Broker] 回應結果 → LINE Worker
5. [LINE Worker] 透過 LINE API 回覆使用者

驗證：
├── webhook 簽名驗證通過
├── 對話記錄正確建立
├── 回應訊息 ≤5000 字
├── 回應不含技術路徑或內部 ID
└── AuditEvent 正確記錄
```

#### Scenario 2: 審核工作流

```
前置：Broker 運行 + LINE Worker + 管理員帳號

步驟：
1. [使用者] 傳送需要審核的操作請求
2. [系統] 建立 Approval 並通知管理員
3. [管理員] 在 Admin 介面看到待審核項目
4. [管理員] 點擊「核准」
5. [系統] 執行原始操作
6. [使用者] 收到操作完成通知

驗證：
├── Approval 正確建立（approvalKey、description）
├── 管理員通知送達
├── 核准後操作確實執行
├── 使用者收到結果通知
└── 審核記錄可追溯
```

#### Scenario 3: System Scaffold 包裝流程

```
前置：Broker 運行 + LINE Worker

步驟：
1. [使用者] 要求建立系統 scaffold
2. [系統] 進入迭代流程
3. [使用者] 回覆「y」確認
4. [系統] 打包 scaffold
5. [使用者] 收到完成通知

驗證：
├── scaffold 正確產生
├── 迭代確認流程正確（y/n 簡短確認）
├── 預設使用自定元件庫
└── 產出物可存取
```

#### Scenario 4: Admin 管理介面完整操作

```
前置：Broker 運行 + 管理員密碼已設定

步驟（Playwright 瀏覽器自動化）：
1. 開啟 http://localhost:{port}/line-admin.html
2. 輸入管理員密碼登入
3. tab-line: 檢視系統摘要 → 瀏覽使用者列表 → 核准/拒絕使用者
4. tab-line: 切換註冊政策 → 變更使用者權限
5. tab-line: 檢視交付檔案 → JSON 詳細檢視
6. tab-line: 瀏覽對話紀錄 → 發送測試訊息
7. tab-workflow: 檢視 Tasks/Plans 摘要 → Execution Intents 詳細
8. tab-browser: 管理 Site Bindings → 簽發 Session Lease
9. tab-deployment: 設定 Deployment Target → 預覽 → 執行部署
10. tab-delivery: 檢視交付歷史 → 按狀態篩選 → 觀察 Drive/fallback 狀態
11. tab-alerts: 切換時間範圍 → 檢視不同嚴重等級告警
12. tab-tools: 篩選 Tool 列表 → 檢視 Tool JSON 詳細

驗證：
├── 登入/登出流程正確
├── 首次登入 → 強制變更密碼
├── 7 個分頁全部可正常載入 + 切換
├── 每個分頁的 API 呼叫（/api/v1/local-admin/*）正確發送
├── requestChain 序列化 → 無並行競態
├── 資料正確渲染（escapeHtml 處理使用者輸入）
├── 自動刷新（5 秒）→ 資料即時更新
├── 響應式佈局：≤1180px → 單欄
└── 交付分頁可觀察 Drive 失敗後的 broker fallback 通知內容
```

#### Scenario 5: Artifact 產出與遞送（含 Broker 下載 fallback）

```
前置：Broker + LINE Worker + Google Drive 設定

步驟 A（Drive 成功路徑）：
1. [使用者] 透過 LINE 要求產出文件
2. [系統] 呼叫 LLM 產生內容
3. [系統] 儲存產出物到工作區
4. [系統] 上傳至 Google Drive → 成功
5. [使用者] 收到包含 Drive 下載連結 + 預覽連結的通知

步驟 B（Drive 失敗 → Broker fallback 路徑）：
1. [使用者] 透過 LINE 要求產出文件
2. [系統] 產生內容 + 儲存
3. [系統] 上傳至 Google Drive → 失敗（quota_exceeded / 網路錯誤）
4. [系統] fallback → 產生 Broker 簽名下載連結
   (GET /api/v1/artifacts/download/{artifactId}?exp=...&sig=...)
5. [使用者] 收到包含 Broker 簽名下載連結的通知（非 Drive 連結）

驗證：
├── Drive 成功：通知含檔名 + Drive 下載/預覽連結
├── Drive 失敗：通知含檔名 + Broker 簽名下載連結
├── Broker 簽名連結有效（exp 未過期 + sig 正確 → 200 + 檔案下載）
├── Broker 簽名連結過期 → 410
├── Broker 簽名連結竄改 sig → 403
├── 通知不含內部路徑 (/path/...) 或技術 ID
├── local-only 路徑不應誤送 broker fallback link
└── Admin 介面 tab-delivery 可觀察 fallback 狀態
```

#### Scenario 6: Worker 容錯

```
前置：Broker + 多個 Worker

步驟：
1. [Worker A] 正常註冊並服務
2. [模擬] Worker A 斷線
3. [系統] HealthMonitor 偵測到心跳遺失
4. [系統] 標記 Worker A 為 Disconnected
5. [使用者] 傳送新請求
6. [系統] 自動路由到 Worker B
7. [Worker A] 重新連線
8. [系統] Worker A 重新註冊，恢復服務

驗證：
├── 斷線偵測 ≤ HeartbeatTimeout (30s)
├── 請求不丟失，正確轉發
├── 重連後 Worker 恢復為 Ready 狀態
└── ObservationEvent 記錄 WORKER_HEARTBEAT_LOST
```

---

## 7. 測試執行與 CI 整合

### 7.1 執行命令

```bash
# C# 單元測試
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --verbosity normal

# C# 整合測試
dotnet test packages/csharp/tests/integration/Integration.Tests.csproj --verbosity normal

# 前端單元測試
cd packages/javascript/browser && npx vitest run

# E2E 測試（需要 Broker 運行）
npx playwright test tests/e2e/

# 全部（單元 + 整合 + 前端）
dotnet test packages/csharp/tests/ --verbosity normal && cd packages/javascript/browser && npx vitest run

# 覆蓋率
dotnet test packages/csharp/tests/unit/ /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
cd packages/javascript/browser && npx vitest run --coverage
```

### 7.2 覆蓋率目標

| 層級 | 目標 | 優先級 |
|------|------|--------|
| PolicyEngine | 100% 分支覆蓋 | P0 |
| EnvelopeCrypto | 100% 分支覆蓋 | P0 |
| SessionService | ≥90% | P0 |
| BrokerService PEP | ≥85% | P0 |
| Artifact 遞送 + 簽名下載 | ≥90% | P0 |
| Worker Handlers | ≥80% | P1 |
| FunctionPool | ≥80% | P1 |
| 快取引擎 | ≥75% | P2 |
| 前端核心元件 | ≥70% | P1 |
| FieldResolver/TriggerEngine | ≥90% | P1 |
| 整體 C# | ≥60% | — |
| 整體 JS | ≥50% | — |

### 7.3 測試分類與執行策略

```
PR 提交時（快速，<2 分鐘）：
├── C# 單元測試（全部）
├── 前端單元測試（全部）
└── Lint / 格式檢查

合併前（中等，<10 分鐘）：
├── 上述全部
├── C# 整合測試
└── 快取整合測試

每日 / Release 前（完整，<30 分鐘）：
├── 上述全部
├── E2E 行為模擬
├── UI 測試（Playwright）
├── 覆蓋率報告產生
└── 效能基準測試（可選）
```

---

## 8. 實施順序

### Phase 1: 基礎建設（第 1 週）

1. 建立 xUnit 專案結構，設定 NSubstitute + FluentAssertions
2. 建立 WebApplicationFactory Fixture（記憶體內 Broker）
3. 建立 Vitest 環境（jsdom 設定）
4. 將測試專案加入 ControlPlane.slnx
5. 遷移現有 62 個斷言到 xUnit 格式

### Phase 2: P0 安全核心測試（第 2 週）

6. PolicyEngine 7 規則完整測試
7. EnvelopeCrypto 加解密測試
8. SessionService 生命週期測試
9. ScopedTokenService 測試
10. BrokerService PEP 管線整合測試

### Phase 3: P1 業務邏輯測試（第 3 週）

11. Worker Handler 單元測試（LINE/File/Browser）
12. FunctionPool（Registry + Dispatchers + Health）
13. 前端核心元件測試（P0 元件 ~10 個）
14. FieldResolver + TriggerEngine 測試

### Phase 4: 整合 + UI 測試（第 4 週）

15. API 端點整合測試
16. 中介軟體管線測試
17. 快取叢集整合測試
18. line-admin.html UI 測試（Playwright）

### Phase 5: E2E 行為模擬（第 5 週）

19. Playwright 環境建立 + Fixture
20. LINE 對話全鏈路模擬
21. 審核工作流模擬
22. Admin 介面操作模擬
23. Worker 容錯模擬
24. Artifact 遞送模擬

---

## 9. 設計決策與取捨

### 9.1 為何選 xUnit 而非 NUnit/MSTest

- xUnit 是 .NET 社群最活躍的框架，`IAsyncLifetime` 更適合 WebApplicationFactory
- `[Theory]` + `[InlineData]` 適合策略規則的參數化測試
- 與 `dotnet test` 無縫整合

### 9.2 為何選 Vitest 而非 Jest

- 原生 ESM 支援（專案使用 ES modules）
- 零配置、啟動快
- 與 Vite 生態一致（如未來需要打包）

### 9.3 為何 WebApplicationFactory 而非獨立啟動

- 記憶體內運行，無需佔用 port，測試間隔離
- 可替換 DI 服務（in-memory DB、mock Worker）
- 測試結束自動清理

### 9.4 為何不用 Docker 做整合測試

- 增加 CI 複雜度和時間
- WebApplicationFactory + in-memory SQLite 已足夠
- 快取叢集測試可用多進程模擬（無需 Docker）

### 9.5 保留現有測試

- 不刪除 `broker-tests/` 和 `e2e-bridge/`，保持向後相容
- 新測試建立在新結構中，逐步遷移
