# Broker 副作用冪等 / 意圖日誌設計(含對抗式驗證修正)

> 2026-05-29 workflow 設計 + 程式碼層對抗式驗證。通用容器平台視角,交易只是副作用的一例。
> **重要:原設計被驗證判為 flawed/overstated;本文是修正後的真實畫面,不是原設計。**

## 0. 一句話

intent-log(durable intent → execute → mark-done + 穩定 intent_id)能堵「**單機 broker 重啟時在途副作用被重複執行**」。但它**不解 split-brain**(見修正④),也**不是平台普遍缺的**(見副作用盤點)。

## 1. 副作用路徑盤點:哪些已保護、哪些裸(關鍵)

| 路徑 | 類別 | 現有保護 | 重放風險 |
|---|---|---|---|
| BrokerService dispatch(16-step PEP) | 通用 | ✅ 完整 `UNIQUE(task_id, idempotency_key)` 早退 | 低 |
| Plan DAG 節點 | 通用 | ✅ 完整(`plan_{id}_node_{n}_attempt_{r}` key) | 低 |
| 配額 ConsumeQuota | 通用 | ✅ 原子 SQL `remaining_quota-1 WHERE >0` | 低 |
| 容器 spawn | 通用 | ✅ 部分:`docker run --name b4a-{type}-{wid}` 撞名直接失敗(天生具名互斥) | 低-中 |
| 通知-錯誤訊息 | 通用 | ✅ `NotificationDedupRepo` 持久化 30 分鐘窗(跨重啟有效) | 低 |
| **Agent Inbox push** | **通用** | ❌ **無冪等鍵**(`inbox_{Guid}`)+ seq MAX→INSERT race | **高** |
| 通知-交易日誌(buy/sell) | 通用-ish | ⚠️ in-memory HashSet,broker 重啟清空→重推 | 中 |
| **AutoTrader perp place_order** | **交易** | ❌ **端到端裸**(見修正②) | **極高** |
| AutoTrader spot place_order | 交易 | ✅ 已有 `BuildAutoOrderKey` 5分鐘 bucket + worker dedup(見修正①) | 低 |

**結論:通用平台路徑大多已有冪等;真正裸的通用路徑只有 Agent Inbox(+交易日誌重啟重推)。最深的裸洞集中在交易/perp。**

## 2. 設計 sound 的骨架(驗證確認)
- **C1(寫意圖前掛)/ C2(寫意圖後執行前掛)邏輯正確**:不變式「state 到 in_flight 前副作用一定沒送出」由「先寫 DB 後 dispatch」保證 → C1/C2 可安全重放,只 C3 需 reconcile。對映現有管線 Step5 Insert→Step13 Dispatched→Step15 mark-done。
- **擴張 `ExecutionRequest` 不重造**:復用現成 `UNIQUE(task_id, idempotency_key)`,加 `dispatched_at`/`downstream_ref`(`AddColumnIfMissing`)。驗證確認此擴張**不破壞現有路徑**(AutoTrader 是新 caller、task_id 命名空間隔離)。
- **單機就受益**:不依賴多節點;broker 每次 deploy/recreate 重啟就兌現價值。

## 3. ⚠️ 對抗式驗證的五個修正(改動結論)

**① spot 已有確定性 key,別重造。** spot 路徑已有 `BuildAutoOrderKey`(5分鐘 bucket)+ `TradingOrderHandler` worker dedup。原設計另發明 bar_close_time 方案、沒跟既有 bucket 機制對齊 → 應**擴張既有的到 perp**,不是平行造一套。

**② perp 路徑端到端裸,§5 reconcile 目前是 no-op。** `perpDict` 不帶 client_order_id → `TradingPerpetualHandler.PlaceOrder` 自產 `OrderId=perp-{Guid}` 且**忽略**傳入 id、無 DB dedup → `BingxPerpetualClient` 不送 client id 給交易所。所以「用 intentId 當 client_order_id 去 reconcile」**今天無 key 可查**。要真閉環需三層改:(1) perpDict 帶 client_order_id (2) handler 認它+加 dedup (3) BingX client 送出+**確認 BingX perp API 支援 clientOrderId**。

**③ intent_id 公式不適用最高風險的平倉路徑。** `ExecutePerpProtectionOrderAsync`(AutoTraderService.cs:1579)發**真錢 reduce_only** 由**即時 mark-price 觸碰 SL/trailing**驅動,**非 bar-close 離散決定** → `logical_period=bar_close_time` 對它算不出穩定 id。原設計 §6「最小第一刀=place_order」根本沒覆蓋這條。需另一個 discriminator(如 SL 觸發事件身份 / position-state-version)。

**④ split-brain:本地 per-instance DB → UNIQUE 跨節點零保護。** 每台 broker 跑自己的本地 `broker.db`(baked 在旁)。failover/split-brain 時兩台**不共享** UNIQUE index → DB 唯一鍵提供 **0 跨節點保護**。**intent-log 只堵單機重啟,不堵 split-brain;後者仍須外部 fencing(=failover 決策)。** 原設計「DB UNIQUE 擋 split-brain」是錯的。

**⑤ 去重權威是「捕捉 UNIQUE 違反例外」,不是 SELECT 早退。** SELECT-then-INSERT 非 `BEGIN IMMEDIATE`、不阻塞並發寫;真正擋第二筆的是 UNIQUE index 在第 2 次 INSERT 拋例外。設計只靠非阻塞 SELECT 早退、沒指明捕捉該例外 → 真並發會變成未處理 insert 例外。(AutoTrader 自身是單執行緒循序,主要風險是 crash/restart 而非並發,但寫法仍要對。)

## 4. 修正後的建議(尊重「焦點容器、非交易」)

- **通用平台其實比想像中健康**:dispatch/plan/quota/spawn/錯誤通知都已有冪等。**「broker 重啟雙重執行副作用」對通用路徑大多不成立。** 原本的大型 intent-log 子系統對通用平台是 over-scoped。
- **唯一值得補的通用裸路徑 = Agent Inbox push**:加冪等鍵(`UNIQUE(agent_id, prompt_hash, requested_by)` 或 client 傳 idempotency_key)+ seq 原子化(`INSERT…SELECT MAX` 或 DB 自增)。小、通用、真有洞。次要:讓交易日誌通知 dedup 跨重啟(併進 `NotificationDedupRepo`)。
- **最深的洞在交易/perp** —— 你已**降低其優先序**;且它要三層改+BingX API 驗證,工程大。
- **split-brain 要 fencing**,不是 idempotency 能解 —— 屬 failover 決策(見 [[platform-failover-design]])。

## 結論
對「容器平台」而言,broker 的通用副作用冪等已大致到位;真正的小缺口是 **Agent Inbox push**(通用、可現在補)。大型 intent-log 與 perp 三層改屬交易範疇(已降優先序),split-brain 屬 fencing/failover 決策。**no-regret 小投資 = 修 Agent Inbox 冪等;其餘等你決定方向。**
相關:[[platform-failover-design]] [[resilience-failmode-asleep]] [[feedback_real_money_idempotency]] [[feedback_notify_dedup_strip_numbers]] [[feedback_baseorm_async_local]]
