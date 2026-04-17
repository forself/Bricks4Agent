# Bricks4Agent 儀表板 & 金融報價 Worker 開發報告

**日期：** 2026-04-11
**專案：** Bricks4Agent — Broker-centered governed AI operations platform

---

## 一、本次開發成果總覽

| 項目 | 說明 |
|------|------|
| 容器監控儀表板 | 即時監控 Docker 容器 CPU/記憶體/網路，支援啟動/停止/日誌查看 |
| 系統診斷工具 | 自動掃描容器執行環境、Worker 心跳、日誌異常偵測 |
| 金融報價 Worker | 自動抓取加密貨幣與股票報價，使用 Job Queue 批次處理 |
| 金融報價儀表板 | 即時顯示報價、漲跌幅、作業歷程與錯誤追蹤 |

---

## 二、系統架構

```
                    HTTP :5000
  瀏覽器儀表板 ◄──────────────► Broker (容器)
                                    │
                              Docker Socket
                              (管理容器生命週期)
                                    │
                    TCP :7000       │
  quote-worker ◄────────────► Function Pool
  (容器)                      (TCP Listener)
    • CoinGecko API                 │
    • Yahoo Finance API       TCP :7000
    • 內部 Job Queue                │
                              file-worker ◄──►
                              (容器)
```

### 通訊協定
- **儀表板 ↔ Broker：** ECDH P-256 + AES-256-GCM 加密 HTTP
- **Worker ↔ Broker：** TCP 二進位 Frame 協定（WORKER_REGISTER → WORKER_EXECUTE → WORKER_RESULT）
- **心跳：** Worker 每 5 秒 PING，Broker 回 PONG，30 秒無心跳標記斷線

### 安全機制
- 所有 API 呼叫透過 ECDH 金鑰交換建立加密 session
- Worker 註冊支援 HMAC 簽章認證（可選）
- Scoped Token 控制存取權限

---

## 三、金融報價 Worker（quote-worker）

### 3.1 功能特性

| 特性 | 說明 |
|------|------|
| 自動抓取 | 啟動後 5 秒首次抓取，之後每 5 分鐘自動執行 |
| Job Queue | 內部佇列機制，避免重複抓取，最多 5 筆待執行 |
| 批次處理 | 每次 job 一次抓完所有商品（約 1.5～2.5 秒） |
| 錯誤追蹤 | 每筆 job 記錄成功/失敗/部分成功，保留最近 50 筆歷程 |
| 手動觸發 | 儀表板「立即抓取」按鈕可隨時觸發 |

### 3.2 報價來源

| 類別 | 來源 | 標的 | API Key |
|------|------|------|---------|
| 加密貨幣 | CoinGecko 免費 API | BTC, ETH, SOL, DOGE | 不需要 |
| 股票 | Yahoo Finance chart API | AAPL, MSFT, TSLA, NVDA | 不需要 |

### 3.3 報價欄位

| 欄位 | 說明 |
|------|------|
| Symbol | 商品代號（如 BTC, AAPL） |
| Name | 商品全名 |
| Price | 目前價格（USD） |
| Change | 24 小時漲跌金額 |
| Change% | 24 小時漲跌百分比 |
| Market Cap | 市值（加密貨幣） |
| Volume | 24 小時成交量 |
| Fetched At | 抓取時間 |

### 3.4 實測數據（2026-04-11）

| 商品 | 價格 (USD) | 漲跌% |
|------|-----------|-------|
| BTC | $72,886 | +1.42% |
| ETH | $2,238 | +2.40% |
| SOL | $84.47 | +1.88% |
| DOGE | $0.0937 | +1.73% |
| AAPL | $260.48 | 0.00% |
| MSFT | $370.87 | -0.59% |
| TSLA | $348.95 | +0.98% |
| NVDA | $188.63 | +2.55% |

---

## 四、Worker 能力註冊

quote-worker 向 Broker 註冊 3 個能力：

| 能力 ID | 說明 | 觸發方式 |
|---------|------|----------|
| `quote.prices` | 回傳所有商品最新報價 | GET /api/v1/workers/quote/prices |
| `quote.history` | 回傳最近 N 筆 job 歷程 | GET /api/v1/workers/quote/history |
| `quote.fetch_now` | 立即觸發一次抓取 | GET /api/v1/workers/quote/fetch |

---

## 五、儀表板功能一覽

| Tab | 功能 | 資料來源 |
|-----|------|----------|
| 總覽 | 代理數量、活躍數、Session 序號 | agents/capabilities API |
| 代理 | AI 代理清單，建立/停止 | agents API (加密) |
| 能力 | 系統已註冊能力清單 | capabilities API (加密) |
| 系統診斷 | 容器環境掃描、Worker 心跳、日誌異常 | diagnostics/scan API |
| 容器監控 | 容器列表、CPU/記憶體、啟動/停止/日誌 | workers/containers + stats API |
| 金融報價 | 即時報價表 + 作業歷程表 | workers/quote/* API |

---

## 六、新增檔案清單

### quote-worker（全新建立）
```
packages/csharp/workers/quote-worker/
├── QuoteWorker.csproj
├── Program.cs                    # Worker 主程式進入點
├── Containerfile                 # Docker 容器定義
├── appsettings.json              # 設定檔
├── Models/
│   ├── QuoteResult.cs            # 報價資料模型
│   └── QuoteFetchJob.cs          # Job 歷程模型
├── Fetcher/
│   └── QuoteFetcher.cs           # CoinGecko + Yahoo Finance 抓取
├── Queue/
│   └── QuoteJobQueue.cs          # Job Queue + 定時排程
└── Handlers/
    ├── QuoteHistoryHandler.cs    # quote.history 能力
    ├── QuotePricesHandler.cs     # quote.prices 能力
    └── QuoteFetchNowHandler.cs   # quote.fetch_now 能力
```

### Broker 端（新增）
```
packages/csharp/broker/
├── Endpoints/QuoteWorkerEndpoints.cs   # quote API 端點
├── Endpoints/DiagnosticsEndpoints.cs   # 診斷 API 端點
└── Containerfile.monitor               # 含 Docker CLI 的 Broker 映像
```

### Function Pool（新增）
```
packages/csharp/function-pool/
├── Container/ContainerStats.cs         # docker stats 資料模型
├── Diagnostics/
│   ├── DiagnosticIssue.cs              # 診斷問題模型
│   ├── DiagnosticReport.cs             # 診斷報告模型
│   ├── IDiagnosticsService.cs          # 診斷服務介面
│   └── DiagnosticsService.cs           # 診斷掃描實作
```

### 基礎建設
```
tools/compose.dashboard-test.yml        # Docker Compose 測試堆疊
scripts/build-images.ps1                # 容器映像建置腳本
scripts/build-images.sh                 # 同上（Linux/WSL 版）
```

---

## 七、既有檔案修改（僅 additive）

| 檔案 | 修改類型 |
|------|----------|
| `broker/Program.cs` | 加一行 `QuoteWorkerEndpoints.Map(api)` + DiagnosticsService DI 註冊 |
| `function-pool/Container/IContainerManager.cs` | 介面新增 `GetStatsAsync()` 方法 |
| `function-pool/Container/ContainerManager.cs` | 實作 `GetStatsAsync()` |
| `function-pool/Container/NoOpContainerManager.cs` | 實作空的 `GetStatsAsync()` |
| `function-pool/Network/WorkerSession.cs` | 修正認證欄位檢查邏輯（respect Enforce flag） |
| `broker/Endpoints/WorkerEndpoints.cs` | 新增 `/stats` endpoint |
| `broker/wwwroot/index.html` | 新增容器監控/診斷/金融報價三個 tab |

---

## 八、操作手冊

### 環境啟動
```powershell
# 1. 建置映像（首次或更新後）
.\scripts\build-images.ps1

# 2. 啟動測試堆疊
docker compose -f tools/compose.dashboard-test.yml up -d

# 3. 開啟儀表板
# http://localhost:5000/index.html
```

### 登入
- 三個欄位保持預設值直接按登入
- Principal: `prn_dashboard` / Task: `task_dashboard` / Role: `role_admin`

### 啟動 quote-worker
1. 容器監控 tab → 「+ 啟動容器」
2. Worker 類型填 `quote-worker`
3. 確認 → 等 10 秒
4. 切到「金融報價」tab → 按「重新整理」

### 關閉環境
```powershell
docker compose -f tools/compose.dashboard-test.yml down -v
```

---

## 九、技術亮點

1. **無 SDK 依賴的容器管理** — 透過 Docker CLI 而非 Docker SDK，同時支援 Docker 和 Podman
2. **端到端加密** — 儀表板與 Broker 之間所有通訊經 ECDH P-256 + AES-256-GCM 加密
3. **Worker 自治** — quote-worker 內建 Job Queue 和定時器，獨立運作無需 Broker 排程
4. **免費資料來源** — CoinGecko 和 Yahoo Finance 皆不需 API Key
5. **即時診斷** — 自動掃描 12 種日誌異常模式，偵測 Worker 心跳逾時和容器異常狀態
6. **Vue 3 Runtime Compiler** — 單一 HTML 檔實現完整 SPA，CDN 載入無需建置工具

---

## 十、已知限制與後續方向

| 項目 | 說明 |
|------|------|
| Broker 重啟後容器記錄遺失 | 容器追蹤在記憶體中，重啟後需重新啟動 Worker |
| Yahoo Finance 限流 | 每個 symbol 間隔 300ms，大量標的會拉長抓取時間 |
| 報價歷史不持久化 | Job 歷程只保留在 Worker 記憶體中（最近 50 筆） |
| 可擴展方向 | 加入更多標的、WebSocket 推送、報價歷史資料庫 |
