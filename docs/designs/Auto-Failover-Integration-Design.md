# Broker 自動移轉整合設計(把驗證過的 lease+fence 接進真 broker)

> 2026-06-11。前置:failover-sim demo 已實證機制(lease 選主 + 自我 fence + 零腦裂零雙寫)。
> 本設計 = 把該機制落地到真 broker,達成「PRIMARY 死 → 秒級自動接手」。
> **鐵則:不改 Benson 核心**(control plane / capability / base approval);全用 BackgroundService + decorator 擴充(沿用作者既有模式,如 approval decorator chain)。

## 1. 架構總覽
```
見證層:3× etcd(broker-機A + broker-機B + 第3見證:筆電/小VPS)← 第3票破 2 節點腦裂
broker:PRIMARY(持 lease、活躍)  +  STANDBY(暖備、吃 Litestream、不動作)
狀態:Litestream 連續複製 broker.db(RPO ~10s)→ STANDBY 永遠有近即時副本
真相:真錢持倉以交易所為準,接手後 re-hydrate(runbook 已有)
```

## 2. `LeaderElectionService`(新 BackgroundService)
- 啟動 → 取 etcd lease(TTL,如 5-10s)+ keepalive;**持有 lease = PRIMARY、失去 = STANDBY**。
- 暴露 `IsPrimary`(+ 當前 epoch/lease 狀態)給其他元件查詢。
- 對應 demo 的選主迴圈;用真 etcd client(非 demo 的 HTTP gateway,避免那個 etcdctl key 怪坑)。

## 3. Lease-gating 真錢副作用(自我 fence)
- **每次真錢 dispatch 前查 `IsPrimary`**(且 lease 未過期)→ 否則**停手**(self-fence)。
- 實作:`LeaderGuardDecorator` 包在真錢 dispatch 路徑外層(沿用 approval decorator chain 模式)→ **不改核心**。
- 對應 demo 的 lease-guarded write:失去主權就寫不進/送不出。

## 4. 冪等(最難、最關鍵——做錯比停機慘)
- **風險**:failover 發生在「已送單、未記錄」之間 → 新 PRIMARY 重送 = **雙下真錢單**。
- **解**:真錢操作帶 **idempotency key**(client order id,確定性產生)。
  1. 送單**前**先把 key 持久化進 broker.db(隨 Litestream 複製到 STANDBY)。
  2. 送單。
  3. 成交回報後標記 key done。
  - 新 PRIMARY 接手時:掃 in-flight(persisted 但未 done)key → **查交易所該 key 成交沒** → 成交則標 done(不重送)、未成交才補送。
- **現況**(記憶盤點):**spot 已有 dedup;perp 端到端裸** → 本設計第一要務 = 給 perp dispatch 補 idempotency key。
- 對賬:接手後 re-hydrate 持倉 + 比對 in-flight key,決定補送/跳過。

## 5. Failover 流程(RTO 目標:分鐘級內含安全停頓)
```
PRIMARY 死 → lease 過期(~TTL)→ STANDBY 取得 lease → 升 PRIMARY
  → 還原最新 broker.db(Litestream,已有近即時副本,通常免動)
  → 從交易所 re-hydrate 持倉 + 比對 in-flight idempotency key
  → lease-gated 恢復 dispatch(AutoTrader 預設仍 disabled、真錢人工再武裝)
```

## 6. 結構洞 × 本設計如何解(對照 HA-Failover-Evaluation)
| 評估報告點出的洞 | 本設計如何解 |
|---|---|
| **副作用路徑無冪等** | idempotency key(§4)— perp 補上、spot 已有 |
| **跨機孤兒容器** | 舊 broker 失 lease → 自我 fence、停 dispatch → 其 worker 閒置無害;新機用自己的 worker |
| **第三方(交易所)無法 token-fence** | lease-gated 自我 fence:舊 broker 失主後**自己停送**、不需交易所配合;idempotency key 當 backstop |

## 7. 不改 Benson 核心(動的都是作者自己的擴充)
- **新增**:`LeaderElectionService`(BackgroundService)、`LeaderGuardDecorator`(包真錢 dispatch)、idempotency key 欄位(BaseOrm `AddColumnIfMissing`)。
- **不動**:Benson 的 control plane / capability model / base ApprovalService / audit。

## 8. 漸進交付(每階段先觀察、真錢 gate 最後且人工驗證)
```
① LeaderElection 唯讀:只選主 + 暴露 IsPrimary、不 gate 任何東西(觀察選主穩不穩)
② LeaderGuard gate「非真錢」路徑(如 scanner/排程)先試水溫
③ perp 補 idempotency key + 端到端冪等測試(脫離真錢、shadow 驗)
④ gate 真錢 dispatch(最後一步、人工驗證、可一鍵回退)
⑤ 架 STANDBY broker(吃 Litestream)+ 第3見證節點
⑥ 真環境 failover 演練(殺 PRIMARY → 接手 → 真錢不重複)
```

## 9. 誠實邊界 / 上線前必過
- 單機 demo 只證**機制**;真多機要實測**真實網路分區**下的行為(雲端網路抖動 ≠ docker disconnect)。
- 真錢 gate 上線前**必須**:① idempotency 端到端測試 ② 模擬 failover 落在「送單中」斷點的測試 ③ 可一鍵停用回退到現行單機。
- lease TTL 是 trade-off(短=接手快但網路抖誤判換主;長=穩但慢)——真環境要調。

## 10. 與論文 §8.1 的關係
本設計 = §8.1「未來工作:真 HA」的具體藍圖;demo(已驗證)+ 本設計(路徑)= 把「有狀態單例的根本難題」從「迴避」變成「有解且分階段可落地」。
