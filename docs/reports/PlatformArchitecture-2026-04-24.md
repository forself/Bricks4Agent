# Bricks4Agent 平台架構報告（專題主體）

Date: 2026-04-24
範圍: Bricks4Agent 平台本身（`packages/csharp/` 原始控制平面）
定位: **此份為畢業專題主軸文件**。交易相關功能（Portfolio Dashboard、Strategy Lab、AI Research Loop、Vegas Tunnel 等）是**延伸容器**，獨立整理在 `AdditionsAndRelationships-2026-04-21.md`，屬於示範「平台擴充點真的被用起來會長什麼樣」的案例，而非本專題的評分核心。

---

## 一、專題定位

### 1.1 Bricks4Agent 是什麼

Bricks4Agent 是一個 **broker-centered governed AI operations platform** — 以「控制平面 + 受治理執行層」為骨幹的 AI 操作平台。它不是：

- ❌ 另一個 ChatGPT clone
- ❌ 把 LLM API wrap 成 Web 服務的 middleware
- ❌ 單純的 chatbot 框架

它試著解決的問題是：**當 AI 被允許做實際有副作用的事（改檔案、發訊息、查資料、跨系統執行）時，如何不讓它變成不可預測的黑盒子？**

### 1.2 核心設計哲學（一句話版）

> 「讓 AI 提議、讓 broker 驗證並記錄、讓 worker 只執行結構化的意圖（structured intent），而不執行原始的 AI 輸出。」

這句話貫穿整個專題的所有設計決策。

### 1.3 為何這件事值得做

目前市面上大多數 AI Agent 框架（LangChain / AutoGPT / CrewAI…）都有同一個問題：**把 LLM 的輸出直接接上工具執行**。這種設計有三個實務缺陷：

1. **不可檢視**：LLM 推理過程變成黑盒子，出錯時無法定位
2. **不可治理**：權限、稽核、重現性都很弱
3. **不可擴充**：加新能力常常要動既有流程

Bricks4Agent 的回答是把 **對話層 / 治理層 / 執行層**完全拆開，中間用結構化契約（structured intent）溝通。

---

## 二、三層架構

```
┌─────────────────────────────────────────────────────┐
│  High-Level Entry Model（對話與意圖層）             │
│  - LLM 負責澄清、改寫、形成候選意圖                  │
│  - 允許「解讀」，不允許自行造成副作用                │
│  - 目前實作：gpt-5.4-mini via openai-compatible      │
└──────────────────────┬──────────────────────────────┘
                       │ structured intent
                       ▼
┌─────────────────────────────────────────────────────┐
│  Broker（控制平面 / governance layer）              │
│  - ASP.NET Core / Minimal API                        │
│  - 驗證、路由、記憶、權限、審計、UI                   │
│  - 自己不執行副作用，只決定「要不要准」              │
│  - 載入點：packages/csharp/broker/                   │
└──────────────────────┬──────────────────────────────┘
                       │ Function Pool TCP
                       │ capability.id/route/payload
                       ▼
┌─────────────────────────────────────────────────────┐
│  Execution Layer（Workers）                          │
│  - line-worker / browser-worker / file-worker /      │
│    quote-worker / trading-worker / risk-worker /     │
│    transport-tdx-worker / strategy-worker（延伸）    │
│  - 每個 worker 宣告自己會的 capability               │
│  - 只吃結構化 request，不吃原始對話                  │
└─────────────────────────────────────────────────────┘
```

**關鍵紀律**：從下往上看，每一層**只能用下一層的 public contract**，不能跨層偷吃。從上往下看，每一層**只能提議**，是否執行由下一層決定。

---

## 三、Broker（控制平面）

### 3.1 Broker 不做什麼

Broker **刻意不做**以下事情：

- ❌ 不當自主規劃者（autonomous planner）
- ❌ 不改寫使用者意圖
- ❌ 不直接呼叫 LLM 來「代替使用者做決定」
- ❌ 不把 LLM 的生成內容直接送去執行

這是它最重要的紀律。Broker 的價值在於**縮小行為範圍並讓它可檢視、可重現、可治理**。

### 3.2 Broker 負責什麼

實際上 broker 處理以下工作（`packages/csharp/broker-core/Services/`）：

| 服務介面 | 職責 |
|---|---|
| `IBrokerService` | 入口路由、請求生命週期 |
| `ICapabilityCatalog` | worker 註冊的能力清單 |
| `IExecutionDispatcher` | 把 capability 請求分派給 worker |
| `IPolicyEngine` | 權限與範圍檢查 |
| `ISharedContextService` | 使用者持久化記憶（document-oriented） |
| `ISessionService` | 對話 session |
| `IAuditService` | 稽核與追蹤 |
| `ILlmProxyService` | 統一 LLM 呼叫入口（被 worker 使用） |
| `IPlanService` / `IPlanEngine` | workflow gating |
| `IScopedTokenService` | 臨時權限 token |
| `IRevocationService` | 權限撤銷 |
| `ISchemaValidator` | JSON Schema 驗證請求 |

每個介面都有一個對應的介面 `I*.cs` 和實作。**這種介面優先的設計讓 broker 所有的能力都是可替換的。**

### 3.3 Governance：Trust 與 Taint 邊界

Broker 區分三種輸入類型：

1. **Raw user input**（原始使用者輸入 — 不可信）
2. **Transformed / decoded input**（被轉換過的輸入，例如 LINE webhook 的事件 payload）
3. **Retrieved external content**（從網路抓回的內容 — 有 taint 標記）

**只有特定 command-shaped 的可信輸入才能直接影響 workflow**（例如 `?search`、`/name`、`confirm`）。LLM 自由文字不能直接觸發執行——必須先走過 command grammar 或 intent 結構化。

### 3.4 Memory Split（最重要的結構性改進）

Broker 把記憶拆成四層：

```
  raw interaction log       ← 絕對真實：每一個訊息的原始記錄
          │
          ▼
  interpretation record     ← LLM 對訊息的解讀（標記是誰的、何時、意圖）
          │
          ▼
  memory projection          ← 可重用的狀態（使用者偏好、近期上下文）
          │
          ▼
  execution intent          ← 被核准可以執行的結構化行動
```

設計上的取捨：

- **log 是真理源**：永遠不被改寫
- **memory 是重用狀態**：可以被更新或失效
- **execution intent 是動作**：一旦進入這層就表示已通過 gating

這個拆分讓「AI 記錯了什麼」不會污染「實際做了什麼」。

---

## 四、Function Pool / Capability 機制

### 4.1 為什麼需要 Function Pool

傳統 RPC 會把 client 鎖死到 server 端 API（gRPC、REST）。Bricks4Agent 選擇**反向**設計：

- **Worker 主動連 broker**（不是 broker 連 worker）
- **Worker 宣告它會什麼 capability**（不是 broker 查 service discovery）
- **Broker 只知道「capability x 有誰會」**（不知道 worker 在哪、跑什麼技術）

這個設計 = Function Pool（`packages/csharp/function-pool/`）。

### 4.2 Capability 命名空間

Capability 用三段式命名：

```
namespace.subject/route
```

範例：

- `trading.order/submit`
- `quote.ohlcv/get_bars`
- `browser.session/open`
- `line.message/send`
- `strategy.signal/evaluate`

**任何新功能都是新的 capability**。這比「在既有 API 加一條 endpoint」更乾淨 — worker 可以獨立開發、獨立測試、獨立部署。

### 4.3 Dispatcher 怎麼選 worker

```
1. broker 收到 request → 看 capabilityId
2. CapabilityCatalog 查 which worker(s) 宣告過自己會做
3. 若有多個 → 用 policy/load balancing 挑一個
4. Dispatcher 開一個 task 給那個 worker、等 result
5. 失敗 → 重試 or fallback 另一個 worker
```

這也讓**同一個 capability 可以有多個實作**。例如 `quote.ohlcv/get_bars` 同時被 Alpaca worker 和 Binance worker 實作 — broker 用 exchange 參數路由。

---

## 五、Worker SDK 與擴充性

### 5.1 新增一個 Worker 需要做什麼

```csharp
// 1. 實作一個 ICapabilityHandler
public class MyHandler : ICapabilityHandler {
    public string CapabilityId => "my.capability";
    public Task<(bool, string?, string?)> ExecuteAsync(...);
}

// 2. 在 WorkerHost 註冊
var worker = new WorkerHost(config, new[] { new MyHandler() });
await worker.RunAsync();
```

**就這樣。** 沒有 protobuf、沒有 codegen、沒有 service discovery 設定。Worker 會自己連上 broker 宣告 capability，broker 會自動把相關請求送過來。

### 5.2 為何這是重要的紀律

很多平台做到「可擴充」都是喊口號，實際上要加新能力要動 5 個地方。Bricks4Agent 的擴充性是**結構性**的：

- broker 有 `IExecutionDispatcher` — 不知道 worker 具體是什麼
- worker-sdk 有 `WorkerHost` — 不知道 broker 具體在哪
- 雙方只靠 capability name 對齊

這讓**新能力不會侵入既有流程**。延伸的交易層新增了 7 個 capability（`portfolio.*`、`research.*`、`strategy.signal/*`、`benchmark.*` 等）— 既有 broker 與既有 worker 的程式邏輯**一行都沒改**。

### 5.3 Worker SDK 的 Reconnect 退避機制

Worker 與 broker 斷線時用指數退避重連：

```csharp
var expSeconds = Math.Min(MaxBackoffSeconds, baseSeconds * Math.Pow(2, consecutiveFailures));
var jitter = rng.NextDouble();
Thread.Sleep(TimeSpan.FromSeconds(expSeconds + jitter));
```

（`packages/csharp/worker-sdk/WorkerHost.cs`）

這是為了解 thundering herd：broker 重啟時 N 個 worker 不會同時打回來。

---

## 六、User Model 與 Managed Workspace

### 6.1 使用者識別

使用者用 `(channel, userId)` 複合鍵識別：

- `channel` = 入口通道（例如 `line`、`discord`、`web`）
- `userId` = 該通道的外部 ID

好處：同一個人從 LINE 和 Discord 來會被看作不同帳號，權限與記憶完全隔離。

### 6.2 Managed Workspace 路徑

每個使用者有獨立工作目錄：

```
{AccessRoot}/
  {channel}/
    {userId}/
      conversations/    ← 對話紀錄
      documents/        ← 使用者檔案
      projects/         ← 生產性任務
        {projectName}/
```

**這個路徑是 capability 的 scope 邊界**：file-worker 只能在使用者的 workspace 內寫檔案，browser-worker 的 session 也綁在這個 scope 下。

### 6.3 Registration Policy

新使用者第一次出現時要經過註冊政策：

- `allow_all`：自動核准
- `manual_review`：擋住，等管理員核准
- `deny_all`：拒絕所有新註冊

這個 policy 是 broker 的 `ISharedContextService` 存的狀態，可以在 Admin Console 裡切換。

---

## 七、Admin Console（line-admin.html）

Admin Console 是 broker 自身的管理 UI（`packages/csharp/broker/wwwroot/line-admin.html`，1692 行、vanilla JS）。

### 7.1 能做什麼

- LINE 使用者清單與權限 toggle
- 對話檢視
- Registration policy 切換
- 瀏覽器綁定記錄
- 部署目標
- Tool Spec 檢視
- Artifact 產出與 Google Drive 交付
- 系統警示

### 7.2 Login 機制

- **Localhost 限定**：不開外網
- **本機管理密碼**：第一次使用 fallback 為 `admin`，強制立刻改密碼
- **Logout 支援**：清除 session

刻意選擇**沒有複雜的 SSO / OAuth**：這是後台工具、單人管理、localhost 使用，加 SSO 是 over-engineering。

### 7.3 視覺風格的延伸

這個 UI 本身的視覺設計（冷色 B4A 調色盤、sidebar shell + panel 系統）已經成為整個專案所有 Web 頁面的視覺範本 — 包括延伸容器的 `portfolio.html`、`trading.html`、`strategy-lab.html` 都採用同一套 CSS 變數與 layout 規則。

---

## 八、LLM Proxy 統一入口

### 8.1 為何需要 LLM Proxy

所有需要 LLM 的工作都**不直接呼叫 OpenAI / Gemini / Anthropic**，而是透過 `ILlmProxyService`：

```csharp
var result = await llmProxy.ChatAsync(body, brokerTask, ct);
```

這讓以下事項被統一管理：

| 治理面向 | 在 LlmProxyService 統一實作 |
|---|---|
| **API key 保護** | 只有 proxy 有 key，worker 永遠看不到 |
| **Rate limiting** | 所有 LLM 呼叫共用配額 |
| **審計** | 每一次 LLM 互動都被 AuditService 記錄 |
| **Model routing** | 某 capability 要 gpt-5.4-mini、某 capability 要 gemini-pro，由 proxy 判斷 |
| **Circuit breaker** | LLM 服務掛掉時 fallback（worker 端也有 per-strategy breaker，見延伸主題 L） |

### 8.2 實際效果

當 Gemini API 出現 overload 時，Proxy 可以：

1. 先試 gpt-5.4-mini
2. 被 rate-limit → 退避 1 秒重試
3. 仍失敗 → 回 fallback response（例如從 memory 拉上次的結果）
4. 稽核記錄「本次 fallback 發生於 xxx」

Worker 端看到的就只是「LLM 回了 / 沒回 / 回了 fallback」，完全不碰路由邏輯。

---

## 九、部署模型

### 9.1 本機開發

```bash
# Broker
dotnet run --project packages/csharp/broker/Broker.csproj
# http://127.0.0.1:5100 （canonical） or :5361 （legacy alt）

# Worker（各自一個 process）
dotnet run --project packages/csharp/workers/line-worker/LineWorker.csproj
dotnet run --project packages/csharp/workers/browser-worker/BrowserWorker.csproj
# ...以此類推
```

每個 worker 獨立 process、獨立 crash、獨立重啟。

### 9.2 Docker Compose（延伸）

延伸容器引入了 `compose.trading.yml`，把交易相關 worker 打包成容器。關鍵設計：

- **`b4a-trading-net` external network**：讓 Discord bot 容器能接進 broker 但被隔離在自己網段
- **`host.docker.internal:127.0.0.1` override**：防止容器內 LLM 呼叫繞過 broker 直接對外
- **`cap_drop: ALL`**：容器權限縮到最小

### 9.3 Azure VM IIS（生產目標）

生產部署目標是 Azure VM + IIS reverse proxy → broker in-proc。Design doc 見 `docs/designs/AzureVmIisDeployment.md`。

---

## 十、延伸容器：以交易子系統為例

### 10.1 這一層為何獨立看待

前九節所講的是**平台本身**。以下所述的交易子系統是**使用者基於平台擴充點延伸出來的個人應用**，不是專題評分核心，但是**它存在本身就是平台可擴充性的證明**。

### 10.2 擴充了什麼

延伸層在 Bricks4Agent 平台上加入：

- **3 個新 worker**：quote-worker / trading-worker / risk-worker（雖然這三個在 `packages/csharp/workers/` 資料夾下，但實際上是利用既有的 `WorkerHost` + `ICapabilityHandler` 模式延伸出來）
- **1 個純延伸 worker**：strategy-worker（新加的）
- **10+ 個新 broker service**：PortfolioAnalyticsService、BenchmarkService、StrategyComparisonService、StrategyResearchLoopService、KellyPositionSizingService、DiscordNotificationService…
- **6 個新 endpoint group**：`/api/v1/portfolio/*`、`/api/v1/research/*`、`/api/v1/backtest-history/*`、`/api/v1/notifications/*`、`/api/v1/risk/kelly`、`/api/v1/strategy-lab/*`
- **3 個新 UI 頁面**：`portfolio.html`、`strategy-lab.html`、`research-lab.html`
- **1 個 Discord bot 容器**：沙箱化執行交易指令

### 10.3 平台有多不被侵入

一個數字：延伸層改了 broker core (`packages/csharp/broker-core/`) **零行**。

改到的既有檔只有：

- `packages/csharp/broker/Program.cs`：`AddSingleton` 註冊新服務 + `MapPost` 掛新路由（**純加法**）
- `packages/csharp/broker/Middleware/BrokerAuthMiddleware.cs`：allowlist 加幾條路徑
- `packages/csharp/broker/Middleware/EncryptionMiddleware.cs`：allowlist 加幾條路徑
- `packages/csharp/workers/strategy-worker/Program.cs`：DI 字典加策略

**沒有一個既有方法的內容被改掉**。這就是平台擴充性的實證。

### 10.4 延伸容器的完整細節看這邊

各個延伸功能的詳細設計、實測結果、與既有檔案的關聯、報告說詞、trade-off，完整整理在：

> **`docs/reports/AdditionsAndRelationships-2026-04-21.md`**

該份文件含 16 個主題（A-P），橫跨 4 天開發，**完全聚焦於延伸層** — 是平台之上使用者層面的應用展示，不是此份平台架構文件的延伸。

---

## 十一、架構紀律的複利效果

### 11.1 一張圖看整個專題

```
┌─── Bricks4Agent Platform ────────────────────────────┐
│                                                       │
│  ┌─ high-level model ─┐                              │
│  │   gpt-5.4-mini      │  ← 提議層                    │
│  └──────────┬───────────┘                            │
│             ▼                                         │
│  ┌─ broker ──────────────────────────────────┐       │
│  │  IBrokerService / IExecutionDispatcher /   │       │
│  │  ICapabilityCatalog / IPolicyEngine /      │       │
│  │  ISharedContextService / IAuditService /   │       │
│  │  ILlmProxyService / ISchemaValidator       │       │
│  │  ─────                                     │       │
│  │  + extension endpoints（delta-only）:      │       │
│  │    /api/v1/portfolio/*                     │       │
│  │    /api/v1/research/*                      │       │
│  │    /api/v1/backtest-history/*              │       │
│  │    /api/v1/notifications/*                 │       │
│  └──────────────┬──────────────────────────────┘      │
│                 │ Function Pool                       │
│                 ▼                                     │
│  ┌─ workers ───────────────────────────────┐         │
│  │  line-worker / browser-worker /           │         │
│  │  file-worker / transport-tdx-worker       │◄── 原生 │
│  │  ─────                                     │         │
│  │  quote-worker / trading-worker /           │◄── 延伸 │
│  │  risk-worker / strategy-worker             │         │
│  └──────────────────────────────────────────┘         │
│                                                       │
└───────────────────────────────────────────────────────┘
```

### 11.2 報告可講的三個核心點

**【Point 1】三層拆分是紀律，不是技術炫技**

> 「把 AI 對話、治理、執行拆成三層是個很老的設計模式（就是 MVC 的精神）。Bricks4Agent 的特殊之處不在於『拆』這個動作，而在於**拆完之後三層之間只用結構化契約溝通** — 對話層不能直接觸發副作用、broker 不會自己改寫意圖、worker 不吃原始對話。這種克制才是整個系統可以被信任的原因。」

**【Point 2】Capability-based 的擴充性是結構性的，不是口號**

> 「我做的交易延伸層有 10 個新服務、3 個新頁面、7 個新 capability。broker core 被改動的行數是**零**。既有 worker 被改動的行數是**零**。這不是我有意克制，而是平台本身的 IExecutionDispatcher + CapabilityCatalog 架構讓它必須如此 — 新能力只能掛進擴充點，不能侵入既有流程。架構紀律在這裡產生了複利效果。」

**【Point 3】LLM 是工具，不是主角**

> 「這個平台裡 LLM 只出現在兩個地方：高階對話入口模型（gpt-5.4-mini）和 LlmProxyService 的 worker 端使用。LLM 的角色是**提議** — 提出候選意圖、產生策略參數假設、分析新聞情緒。它從不直接執行、從不直接改狀態、從不被信任。這是我對『AI-first』這個流行說法的反動：AI-*governed* 比 AI-*first* 更符合實際生產需求。」

---

## 十二、Trade-offs 備答

| 可能被問的問題 | 我的選擇 | 為什麼 |
|---|---|---|
| 為何 broker 不用 gRPC 做 worker 通訊？ | 自研 Function Pool TCP | 自研能控制連線方向（worker-initiated），避免 broker 需要知道 worker 在哪；gRPC 需要 service discovery |
| 為何 broker 不用 Kubernetes？ | 單 process + worker list | 專題規模不需要 k8s 複雜度；擴容可以後加，先讓開發速度最大化 |
| 為何不用 LangChain / AutoGen？ | 自建 | 這些框架預設 AI 直接接工具，不符合「AI 提議、broker 驗證」的紀律 |
| 為何 broker 用 C# 不是 Python/Node？ | C# / .NET 8 | 靜態型別在 governance 層非常重要 — `ICapabilityCatalog` 的介面 invariants 比動態型別環境更難違反 |
| 為何用 SQLite + BaseOrm 不是 PostgreSQL + EF Core？ | SQLite / BaseOrm | 專題定位是 single-node；EF Core 的 migration 複雜度在這個規模是負債；BaseOrm 輕量、document-oriented 適合 SharedContextEntry |
| 為何 Admin Console 只是 vanilla JS 不用 React？ | 1 個 HTML 檔、vanilla JS | 管理後台複雜度低、部署簡單、載入快；加 build step 是 over-engineering |
| 為何延伸層的交易 worker 也放 `packages/csharp/workers/`？ | 同一資料夾 | 從結構上它們**確實**是 worker；刻意不開新 top-level 資料夾避免視覺混亂，讓平台延伸性的證明更直觀 |

---

## 十三、已知限制與未來方向

### 13.1 平台層的誠實限制

- **High-level model 選擇尚未細化**：目前是全域 gpt-5.4-mini，未來應允許 per-capability model routing
- **Session 尚無 TTL 過期**：使用者 session 無限期存活，對多人環境會累積
- **Registration policy 沒有 webhook**：新使用者進來時不會主動通知管理員（manual_review 只能靠管理員主動巡）
- **Audit 存在 SQLite**：長期累積會壓資料庫，未來應分表或歸檔

### 13.2 可以接著做的方向

- **Capability versioning**：目前 capability 是 `namespace/route` 沒版本，未來需要 `v1/`、`v2/` 的 versioning 規則
- **Distributed broker**：目前單一 broker process；要支撐多 region 會需要 broker cluster
- **Policy 語言**：目前 IPolicyEngine 是硬編碼 C# 規則，未來可考慮 OPA / Rego 之類的 policy-as-code
- **Worker 版本管理**：worker 升版的 rollout / rollback 沒有正式流程

---

## 十四、平台治理層的觀測性補完（2026-04-25 ~ 04-26）

前面 §三 ~ §八 描述的是**設計時的治理機制**（Trust/Taint、Memory Split、Capability 白名單、LLM Proxy 集中入口）。這節記錄一輪「**讓這些機制可被儀表板看見**」的工程，把抽象設計變成可驗證的實證。

### 14.1 LLM Proxy 觀測性 + 量化策略路由對齊

**問題**：§八 講「所有 LLM 呼叫透過 LlmProxyService 統一管理」，但實際上 `strategy-worker` 容器的 `LlmStrategy` 跟 `NewsSentimentStrategy` 是**自己拿 HttpClient 直連 Gemini**，繞過 broker。等於專題故事裡最常引用的「集中治理」這條，**worker 端是說一套做一套**。

**修法**（commit `e300fd2`）：

1. broker 加 `POST /api/v1/llm-proxy/chat` endpoint（trusted-internal allowlist），收 OpenAI-compatible body，內部走 `MeteredLlmProxyService → ILlmProxyService.ChatAsync`。
2. `LlmStrategy` / `NewsSentimentStrategy` 建構子改成只吃 `brokerUrl + model`，**砍掉 `apiKey` 跟對 Gemini / OpenAI 的雙分支**。所有 LLM 呼叫單一路徑：POST broker `/llm-proxy/chat`、解 broker wrapper `data.content`。
3. `compose.trading.yml` 對應改：strategy-worker 不再注入 `LLM_API_KEY`、只注入 `BrokerUrl=http://broker:5000`。**API key 只留在 broker 一份**。

**觀測層裝飾器**（commit `f1458ab`）：

- 新增 `LlmProxyMetrics`（`broker-core/Services/`）— 純記憶體 ring buffer 200 筆 + thread-safe counters + per-model 聚合
- 新增 `MeteredLlmProxyService`（**decorator 模式**包住 `LlmProxyService`，`ChatAsync` 前後 `Stopwatch` + try/catch 紀錄）
- DI rewire：原本 `AddHttpClient<ILlmProxyService, LlmProxyService>` 拆成兩步 — 內層 LlmProxyService 註冊不變，外層 ILlmProxyService 註冊為 MeteredLlmProxyService
- **既有所有 ILlmProxyService 消費者（HighLevelCoordinator、StrategyGeneratorService、RuntimeEndpoints、…）一行不用改、自動拿到觀測能力**

**為什麼用 decorator 不直接改 LlmProxyService**：單一職責 + 觀測層可以選擇性註冊（DI 開關）+ 不污染既有測試。這是 §五 Worker SDK 章節提的「介面驅動讓擴充免侵入」紀律的另一個應用點。

**儀表板呈現**（新「LLM Proxy」分頁）：

- 配置卡：provider / api_format / default_model / api_key_set + 長度（mask 不洩漏值）/ upstream host（只顯示 host 部分、不洩漏完整 URL）
- KPI 卡：total_calls / success_rate (% with color thresholds 95/80) / avg_latency / total_eval_tokens
- 依模型統計表：每個 model 的 calls / success ratio / avg_latency / tokens / last_call_ts
- 最近呼叫表：時間 / OK-FAIL tag / model / task_id / latency / tokens / 截短錯誤訊息
- 🩺 健康檢查按鈕：主動 ping 上游確認可達
- **趨勢長條圖**：最近 24 個 bucket（預設 10 分鐘/格 = 4 小時視窗）的成功/失敗筆數堆疊柱狀圖，hover 看單格細節
  - 後端：`LlmProxyMetrics.Trend(bucketMinutes, bucketCount)` 對 ring buffer 做時間 bucket 分組 + `GET /llm-proxy/trend`
  - 前端：純 CSS flex 長條（不引第三方圖表庫）

**驗證**：直接 POST `/api/v1/llm-proxy/chat` 帶 `gemini-2.5-flash` + 一句測試 prompt，5 秒內儀表板看到：

```text
total_calls=1 success=1 avg_latency=1369ms
per_model: gemini-2.5-flash 1 call 1 token
```

### 14.2 容器錯誤日誌持久化（Container Logs SQLite）

**問題**：原本看容器日誌只能即時 `docker logs --tail N`，**容器一停就消失**、無法 postmortem。

**修法**（commit `9ce8152` + `225fedf`）：

- `function-pool/ContainerLogs/ContainerLogTailService.cs` — `Timer` 每 10 秒對所有 running 容器跑 `docker logs --tail N --timestamps`
- Regex 過濾 `ERROR / WARN / FATAL / Exception / failed / denied / refused / timeout / crashed` 的行；**stderr 行不論內容直接視為 ERROR**
- ANSI escape 碼自動剝除（discord-bot 跑 Claude Code TUI 會噴一堆色碼/游標控制）
- 寫入獨立 `/data/container-logs.db`（不污染 `broker.db` 跟 `diagnostics.db` 的 WAL）
- `(container_id, ts, msg_hash)` 複合索引去重 — 容許重疊時間視窗讀、不會重複寫
- **保留 7 天**自動清理（每 ~10 分鐘一次 retention sweep + VACUUM）
- API：`GET /api/v1/workers/log-history?container_id=&level=&error_code=&limit=`

**為什麼用 `--tail N` 不用 `--since {n}s`**：實測 Docker 29.x + WSL 對 `--since 60s` / `--since 5m` 等小視窗會回 0 行（`--since 1h` 才會），疑似 timezone / 解析 bug。改用 `--tail N` + msg_hash 去重雖然每次重讀部分行、但跨 docker CLI 版本穩。

**Regex 守門**：`0 errors` / `no errors` / `successfully` 這類**包含錯誤關鍵字但本意是成功**的行被加入 `FalsePositivePattern` 排除（quote-worker 的 `[Job xxx] done: 8 ok, 0 errors, 2.2s` 是典型誤判源）。

### 14.3 錯誤目錄分類（ErrorCatalog 13 類）

**問題**：14.2 抓到的錯誤行只標 ERROR/WARN 等級不夠用。要回答「**哪類錯誤最常發生**」需要分類維度。

**修法**（commit `25de5f9`）：

- `function-pool/ContainerLogs/ErrorCatalog.cs` — 13 條規則的目錄
- 每條 entry 是 `{Code, Category, Description, Severity, Pattern}`
- **順序敏感**：先具體（LLM rate limit、SQLite locked）後通用（fail/error 通配）；命中第一條就停
- 寫入時 classify、SQLite 多兩個欄位 `error_code` + `category`（`ALTER TABLE` 等冪檢查 `PRAGMA table_info` 後加）
- 啟動時 `ReclassifyUnclassified()` 重跑舊資料的分類器

13 類目錄概覽：

| Code | 類別 | 觸發 pattern 摘要 |
|---|---|---|
| ERR-001 | Worker 連線中斷 | connection error/lost/closed, Reconnecting |
| ERR-002 | 資料庫異常 | SqliteException, database is locked |
| ERR-003 | LLM / 外部 API 限流 | rate limit, 429, quota |
| ERR-004 | 認證 / 授權失敗 | 401, 403, unauthorized |
| ERR-005 | Timeout / 逾時 | timeout, deadline exceeded |
| ERR-006 | 資源找不到 | 404, FileNotFound |
| ERR-007 | 資料解析錯誤 | JsonException, parse error |
| ERR-008 | 設定 / 環境變數缺失 | missing env, required.*not provided |
| ERR-009 | Null / 參照錯誤 | NullReferenceException, TypeError |
| ERR-010 | 例外 / 未處理 | Traceback, panic, unhandled |
| ERR-011 | 套件 / 更新失敗 | auto-update failed, npm install |
| WRN-001 | 已棄用 API | deprecated, obsolete |
| WRN-002 | 重試 / Backoff | retrying, backoff |

加 `ERR-999 / WRN-999` 兩條 fallback。**新增類別只要 push 到 `Entries` list 前面**，broker 重啟時 reclassify 自動更新舊資料。

**儀表板呈現**：每個容器卡的「日誌 → 歷史錯誤」分頁頂部 chip 列（紅 ERROR / 橘 WARN，點 chip 過濾），entry rows 預設收合只顯示 `[CODE] 分類` + 時間，點開看 raw message — 模擬 HTTP 狀態碼設計（`404 Not Found` 給人看、stack trace 點進去看）。

**實證效果**：discord-bot 容器 18 筆 `Auto-update failed · Try claude doctor` 從 `ERR-999 未分類` 移到 `ERR-011 套件 / 更新失敗`；strategy/trading-worker 的 Reconnect 訊息歸到 `ERR-001 Worker 連線中斷`。儀表板從「45 行 ERROR」變成「ERR-001 × 45 / ERR-011 × 18 / WRN-999 × 45」三個分類聚合，**讓「最近主要在出什麼包」一眼看穿**。

### 14.4 Agent Capability Scoping 活 demo

**問題**：§三、§六、§十一講的 Capability 白名單 + Role-based scoping 是設計概念，過去沒有具體驗證。

**驗證**（2026-04-26）：透過儀表板「+ 建立代理」建立一個名為 `Research Assistant` 的 agent：

```text
顯示名稱: Research Assistant
任務類別: analysis
能力: 點「預設（低權限）」→ 自動勾全部 Low risk capability
```

**broker 端 AgentSpawnService.CreateAgent 流程**：

1. 建 `Principal { id=prn_agent_xxx, type=AI, status=Active }`
2. 建 `BrokerTask { id=task_agent_xxx, type=analysis, scope=task_agent_xxx }`
3. 為勾的每個 capability 建一筆 `CapabilityGrant { principal_id, task_id, capability_id, scope, quota }`
4. 看選的能力**最高風險等級**自動配 role（Low → `role_reader`、Medium+ → `role_executor`）
5. 寫 `RuntimeDescriptor`（之後 spawn 容器時用）

**儀表板顯示的 agent**：

```text
Agent ID: agent_3273e7d4bba045ada3
Principal: prn_agent_3273e7d4bba045ada3
Task: task_agent_3273e7d4bba045ada3
狀態: Active
任務類別: 通用 (analysis)
能力: 25
角色: role_reader（自動推導）
```

**這證明了什麼**（給專題報告的關鍵句）：
> 「我建了一個 Research Assistant agent、broker 自動配給它 `role_reader` + 25 條 read-only `capability_grants`。如果這個 agent 之後嘗試呼叫 `trading.order/submit`（白名單裡沒有），broker 的 `PolicyEngine` 會在 dispatch 階段直接拒絕、根本到不了 trading-worker。這就是平台 §三 講的 **AI 提議 → broker 驗證 → 執行層只執行結構化意圖** 治理模型的具體實現，不是設計圖上的箭頭、是真的擋得了的牆。」

### 14.5 Agent Runtime MVP-0：runnable 容器（2026-05-01）

**問題**：14.4 demo 把 Research Assistant 的「身份 + 政策」紀錄寫進 DB 了，但點儀表板上的「啟動容器」按鈕會 500 — 因為 `ContainerManager.WorkerImages` 沒設 `agent` 條目、`b4a-agent:latest` image 也根本不存在。**有政策層、沒有執行層**。

**做法**：建立 `packages/csharp/workers/agent-worker/`（4 個新檔，純 HttpClient，不相依 WorkerSdk），建出 `b4a-agent:latest` image 並補進 compose 的 `WorkerImages` 設定。

**容器啟動行為**：

```text
1. Broker spawn 時注入 env：BROKER_URL / BROKER_PRINCIPAL_ID / BROKER_TASK_ID / BROKER_ROLE_ID
2. Agent 容器讀 env，向 broker 的 /api/v1/llm-proxy/chat 跑一輪自介 LLM call
3. LLM 回覆印到 stdout（dashboard 容器分頁的「日誌」按鈕看得到）
4. 進入心跳 idle 迴圈，dashboard 看得到容器 RUNNING
```

**順手修到的 bug**：`compose.trading.yml` 的 `NetworkName` 寫成 `b4a-trading_trading-net`（compose project 預設前綴格式），但檔尾 `networks.trading-net.name` 把實際網路名 override 成 `b4a-trading-net`。在沒人 spawn 過的時候（trading 堆疊都是 compose 自己起的）這個錯不會被觸發；MVP-0 第一次按 spawn 才暴露。

**這層補完了什麼**：
> 平台從「DB 裡有 agent 紀錄」進到「真的能 spawn 出帶有對應身份的容器」。Agent 容器的所有出站呼叫都透過 broker 走（LLM 走 LlmProxy、capability 之後會走 exec endpoint），這意味著**容器拿不到 LLM API key、也拿不到任何工具的直接通道**——它的能耐 = broker 願意幫它做的事，不多不少。這是 §三「政策授權邊界」的物理實現。

### 14.6 Agent Runtime MVP-1：Inbox 任務派發（2026-05-01）

**問題**：MVP-0 的 agent spawn 起來只能跑啟動時 hard-coded 的 prompt，**派一個新任務 = 重起容器**。實際上沒法用。

**做法**：

1. 新表 `agent_inbox_tasks`（schema 與 Phase 1 表並列；`pending → processing → done|failed` 狀態流）
2. 新端點四個：
   - `POST /api/v1/agents/inbox/push`（dashboard / 外部呼叫者下單）
   - `GET  /api/v1/agents/inbox/pull?agent_id=X`（agent 容器拉一筆，原子地標 processing 防搶單）
   - `POST /api/v1/agents/inbox/complete`（agent 回填結果 + token / latency）
   - `GET  /api/v1/agents/inbox/list?agent_id=X`（dashboard 看歷史）
3. Agent runtime 改成 5 秒 poll 迴圈
4. Dashboard「代理」分頁加「派任務」按鈕、modal 含 prompt textarea + 任務歷史表格（5 秒輪詢看狀態變化）

**搶單原子性**：`UPDATE ... SET status='processing' WHERE task_id=? AND status='pending'`，靠 SQLite 的 row-level update 保證若兩個 agent 同時 pull 同一筆、第二個會收到 `rowsAffected=0` 退回再試。

**設計取捨**：`/agents/inbox/*` 與既有 `/llm-proxy/*` 一樣走 `IsTrustedInternalPlainJsonPath` allowlist，**不過 ECDH 加密**。理由是這條路徑只有 broker 內部網路（agent 容器、dashboard）能打到；token 走網路邊界控制就夠，不需要會話加密層。註解明確標 `[whitelist add]` 給未來看的人辨識（與「Benson 原作不動」紀律一致）。

**這層補完了什麼**：
> 平台有了**面向 LLM 的工作隊列**。任務的全生命週期（提交 / 取出 / 完成）都被記到 SQLite，每筆紀錄含 prompt、reply、模型、token、延遲、誰送的——一份「政策邊界內 LLM 在做什麼」的可審計流水帳，不靠日誌、不靠抓包、是 DB 第一公民。

### 14.7 Agent Runtime MVP-2：LLM Tool Calling（2026-05-01）

**問題**：14.4 給 Research Assistant 配了 25 條 `capability_grants`、MVP-1 讓它能收任務，但**它根本沒辦法用那 25 條能力**——LLM 拿到 prompt 只能憑空回答，不能查資料、不能寫記憶。

**做法**：

1. 新端點 `POST /api/v1/agents/exec`（capability gate + dispatch）
   - 驗證 agent 的 `RuntimeDescriptor.capability_grants` 包含 requested `capability_id`，否則 403
   - 構造 `ApprovedRequest` 直接送 `IExecutionDispatcher.DispatchAsync`（既有的 InProcessDispatcher，已實作 16 個 route：file / memory / web / rag / convlog 等）
   - 故意 bypass `BrokerService` 的 16-step PEP pipeline——理由：grant 在 spawn 時已被 PEP 審核，每個工具呼叫再跑一次審批會卡死 agent 互動式行為。會觸發 `ApprovedRequest.WarnIfBypass` 的 DEBUG 警告，是預期內的權衡
2. 新端點 `GET /api/v1/agents/exec/tools?agent_id=X`：把 grants 翻成 OpenAI function-calling spec 給 agent 餵 LLM
3. Agent runtime 啟動時 `LoadToolsAsync()`，poll 任務時走 `ToolCallingLoopAsync`：

```text
LLM round 1: 看 prompt + tools list
   ├── 若回 tool_calls → 走 /agents/exec → 結果加進 messages
   └── 若回 final text → 結束
LLM round 2: 看更新後 messages
   ...（最多 4 輪 fail-safe）
最後一輪不允許 tools → 強制 LLM 給文字答覆
```

**實證**（task #4 → #6 的兩分鐘間距、跨 task 記憶持久化）：

```text
[Task #4] prompt: 請幫我把「我的專題期末展示日是 2026-06-15」記下來，key 用 capstone_demo_date
🔧 tool_call: memory_store({"value":"我的專題期末展示日是 2026-06-15","key":"capstone_demo_date"})
   ← {"success":true,"capability_id":"memory.write","route":"memory_store",
      "result":{"key":"capstone_demo_date","version":1,"stored":true,...}}
✓ task #4 done (3260ms, 53 tokens, 1 tool calls)

[Task #6] prompt: 請查一下你之前用 capstone_demo_date 這個 key 記下了什麼
🔧 tool_call: memory_retrieve({"key":"capstone_demo_date"})
   ← {"success":true,"capability_id":"memory.read","route":"memory_retrieve",
      "result":{"key":"capstone_demo_date","value":"我的專題期末展示日是 2026-06-15","version":1,...}}
✓ task #6 done (2591ms, 57 tokens, 1 tool calls)
```

LLM 沒有「假裝」記得（它做不到，會話無狀態），而是**實際呼叫 `memory_retrieve` 工具**才能答出值。這條路徑：LLM ↔ broker `/exec` ↔ InProcessDispatcher ↔ SharedContextEntry — 中間任何一步擋掉（grant 不對、route 沒實作、SQLite 寫失敗）任務就 fail。是真的接通的鏈，不是模擬。

**16 個目前可用的工具**（dispatcher 已實作的）：file 4 個、memory 5 個、rag 1 個、web 4 個、conv-log 2 個。`agent.list` / `line.message.send` 等 9 個 dispatcher 還沒接，agent 端 `/agents/exec/tools` 自動過濾掉、不會把它們餵給 LLM（避免 LLM 呼叫不存在的工具）。

**這層補完了什麼**：
> 25 條 `capability_grants` 從「DB 裡的紀錄」變成「LLM 真的會去 invoke 並收到結果」。**14.4 的政策層 + 14.6 的隊列層 + 14.7 的執行層**合起來：使用者下任務 → broker 收單 → agent 透過 LLM tool calling 動用工具 → broker 驗證每次工具呼叫是否在 grants 內 → 派發給既有 dispatcher → 結果回流 LLM → 最終答覆寫回 inbox。整條鏈每一步都有 DB 紀錄、都被 §14.1 的 LlmProxy metrics 計入。**這就是「AI 提議 → broker 驗證 → 執行層只執行結構化意圖」這句話被完整實作後長什麼樣**。

### 14.8 整輪工程的小結（給說詞用）

七個小節合起來把專題主軸文件的抽象主張**全部變成有 demo 證據的實證**：

| §X 抽象主張 | §十四 哪一節提供實證 |
|---|---|
| §三 LLM 集中治理（LlmProxyService 統一入口） | 14.1（worker 路由對齊 + Metered decorator + 儀表板觀測） |
| §三 Memory Split / 可檢視 / 可重現 | 14.2（容器錯誤持久化 7 天，事後可查） |
| §三 治理 vs 觀測：可檢視 | 14.3（錯誤分類目錄讓「治理層的事故」可分類聚合） |
| §六 Capability 白名單 + Role 自動推導 | 14.4（Research Assistant agent DB 紀錄） |
| §三 政策授權邊界（容器拿不到 API key） | 14.5（agent runtime 容器只能透過 broker 出站） |
| §三 可審計（每筆 LLM 呼叫都被記） | 14.6（Inbox 任務全生命週期持久化到 SQLite） |
| §三 「AI 提議 → broker 驗證 → 結構化執行」三段式 | 14.7（LLM tool calling end-to-end 實測，含 capability gate 拒絕路徑） |

**設計哲學的複利**：14.1 的 decorator 模式跟 §五 Worker SDK 的 `ICapabilityHandler` 是同一招——介面驅動讓擴充不侵入既有實作。14.2 的 SQLite + Timer + retention 跟 §九 提到的 ScheduledDiagnosticsService 是同一招——獨立 db + 背景服務 + 滾動清理。14.3 的目錄分類器跟 §四的 capability 命名空間是同一招——具體先、通用後的命中順序。14.6 的 inbox FIFO + atomic `UPDATE WHERE status='pending'` 跟 §八 ExecutionRequest 的 `(task_id, idempotency_key)` 唯一索引是同一招——靠 DB 約束做正確性、不寫應用層鎖。14.7 的 grants 驗證 → bypass PEP → 直接 dispatch 跟 §三 「政策資訊在預先審核完後就應該物化、不要每次重算」是同一招——授權狀態的快取化降低 hot-path 延遲。**讓系統好擴充的紀律會在不同層級重複出現**——這就是架構紀律的複利，也是讓「平台」這個詞不只是行銷口號的本質差異。

---

## 附錄 A：packages/csharp/ 結構速查

| 路徑 | 角色 |
|---|---|
| `broker/` | 控制平面主 process（ASP.NET Core） |
| `broker-core/` | broker 的 service 介面與預設實作 |
| `function-pool/` | Worker 註冊與任務分派的 TCP 協定 |
| `worker-sdk/` | Worker 開發套件（`WorkerHost`、`ICapabilityHandler`） |
| `workers/` | 所有 worker 實作（原生 + 延伸） |
| `cache-server/`、`cache-client/`、`cache-protocol/` | 分散式快取（獨立 subsystem） |
| `database/` | BaseOrm（輕量 ORM，非 EF Core） |
| `security/` | Crypto、auth 公用工具 |
| `api/` | 跨套件共用契約 |
| `logging/` | 統一記錄 |
| `reporting/` | 稽核報告輸出 |
| `tests/` | broker-tests + integration tests |

---

## 附錄 B：延伸文件索引

- **本專題主軸（此份）**：`docs/reports/PlatformArchitecture-2026-04-24.md`
- **延伸容器功能詳解**：`docs/reports/AdditionsAndRelationships-2026-04-21.md`（16 主題，A-P）
- **更早的平台現況快照**：`docs/reports/CurrentArchitectureAndProgress-2026-03-26.md`
- **Governance / Design 深度文件**：`docs/designs/`（Browser、LLM、Tool Spec、Deployment 等各獨立設計文件）
