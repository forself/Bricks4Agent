# 自動移轉階段⑤:暖備 broker + 見證(HA 部署 scaffold)

> 把驗證過的 lease 選主機制(failover-sim demo + broker 內 `EtcdLeaderElection`)落地成「真 broker 暖備」。
> 本目錄 = 部署模板 + 驗證程序。**現況:模板就緒;真 broker 端到端驗證待實際 HA 部署。**

## 拓樸
```
3× etcd（見證/quorum）= broker-機A + broker-機B + 第3見證（筆電/小VPS)
broker-a（Cluster:NodeId=broker-a）  +  broker-b（NodeId=broker-b）
  └ 兩者 Cluster:EtcdEndpoints 指向 3 etcd → EtcdLeaderElection 選主
  └ 一個 PRIMARY（活躍）、一個 STANDBY（LeaderGuard 跳過非真錢工作）
狀態：Litestream 連續複製 PRIMARY 的 broker.db → STANDBY（見 docs/runbooks/Backup-Restore-Runbook.md §3.5）
```

## 啟用條件（broker 端)
- 設 `Cluster__EtcdEndpoints`（逗號分隔）→ 啟用 `EtcdLeaderElection`（否則 `SingleNodeLeaderElection`、永遠 PRIMARY、現狀不變）。
- `Cluster__NodeId`（每實例不同）、`Cluster__LeaseTtlSeconds`（預設 10）。

## 本地驗證的兩個前置（重要、踩過的坑）
1. **必須 rebuild b4a-broker image**：`EtcdLeaderElection`/`LeaderGuard` 是新碼,現有 `b4a-broker:latest` 是舊的、沒有。
   `docker compose -f tools/compose.trading.yml build broker`
2. **broker 需完整啟動 config**：minimal env 會在 DI 崩(`IWorkerRegistry` 等在 FunctionPool 設定 block 內、要對應 config 才註冊)。
   → 本 compose 的 broker env 要**合併 `tools/compose.trading.yml` 的完整 broker environment**(只是多加 `Cluster__*` + 各自 NodeId),不能只用這裡的最小集。

## 驗證程序（rebuild + 完整 config 後)
```bash
docker compose -p b4aha -f tools/ha/docker-compose.ha.yml up -d
# 1) 選主:應一個 PRIMARY 一個 STANDBY
docker logs b4aha-broker-a 2>&1 | grep "\[cluster\]"
docker logs b4aha-broker-b 2>&1 | grep "\[cluster\]"
# 2) 殺主接手:kill PRIMARY → 另一個 ~lease TTL 內升 PRIMARY
docker kill b4aha-broker-a
sleep 12; docker logs b4aha-broker-b 2>&1 | grep "\[cluster\]" | tail -2
# 3) 拆除
docker compose -p b4aha -f tools/ha/docker-compose.ha.yml down
```

## 現況與下一步
- ✅ 選主**機制**已實證:`demos/failover-sim`(brokersim、三測試全過)。
- ✅ broker 內 `EtcdLeaderElection`/`LeaderGuard`:**build 驗證 + 邏輯照搬 failover-sim**;但 C# 版**尚未實跑**(本地跑需上面兩前置)。
- ⬜ 真 broker 端到端(選主→殺主接手):待**實際 HA 部署**(rebuilt image + 完整 config + 真 etcd quorum)驗證,或本地補齊上面兩前置後跑。
- ⬜ 狀態複製(Litestream→STANDBY)+ failover 對賬(階段④、需 perp idempotency 階段③真錢驗)+ 演練(階段⑥)。

## 紀律
- ③ perp idempotency 真錢冪等沒驗前,**不要真的對真錢 broker 做自動 failover**(會雙下單)。本階段先當「暖備 + 半自動快速復原」+ 把選主在真 broker 跑通。
