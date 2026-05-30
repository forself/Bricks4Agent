# 容器平台「整機死亡自動轉移」設計與評估(含對抗式驗證修正)

> 2026-05-29,workflow 設計 + 對抗式驗證(程式碼層)。焦點=**容器/平台架構**;真錢交易只當「會產生不可逆副作用的工作」的一個例子,非主軸。任何「下指令給容器 / 推通知 / spawn 子容器 / dispatch 任務」都是同一類問題。

## 1. 一句話定性

> 自動 failover 的難題,幾乎 100% 集中在**那一個有狀態的單例 broker**;worker 是無狀態的(驗證確認:無本地快取/檔案/會話,連線一律從零 `RegisterAsync` 重註冊),死了在任一台再起即可、零資料丟失,難度趨近零。broker 同時要求「唯一 + 高可用 + 持有狀態 + 發不可逆副作用」——分散式系統裡最難 failover 的東西。

## 2. 前提鐵律:2 台機器不能安全自動 failover

「對方沒回應」永遠分不清是 (a) 對方真死 還是 (b) 我到對方的網路斷了(分區)。
- 設「超時就接手」→ 遇分區=**腦裂**(兩個 broker 都當主、各自發指令寫分歧狀態;對控制平面比停擺更糟:停擺是安全失敗,腦裂是錯誤的成功)。
- 設「不確定不接手」→ 對方真死時也不敢接 = 沒 failover。
- 2 節點怎麼調都只能二選一。**解法 = 第三個見證者 → quorum(3 取 2 多數決才有資格當主)。** SQL Server Always On 也需要 WSFC 仲裁第三票,只是包在裡面。「再買一台」本質是「再加一台 + 一個見證」。

## 3. 四大支柱 × B4A 現況(程式碼驗證)

| 支柱 | 作用 | B4A 現況 |
|---|---|---|
| ① 共識/選主 | 誰是主(配 fencing token) | ❌ 無 |
| ② 狀態複製 | 權威狀態同步到備機 | ❌ broker.db SQLite 無複製;且大量狀態只在記憶體(WorkerRegistry、`_containers` dict、AutoTrader `_peakByExchange`/`_positionState`/`_lastEntryAt`、restart backoff `_state`)→ broker 死即丟 |
| ③ fencing | 罷黜的舊主必須保證停止動作 | ❌ 無,且對 B4A 最難(見洞 3) |
| ④ 重導向 | client/worker 連到新主 | ❌ worker 寫死 `BrokerHostForWorkers="broker"` 連 docker 網路名 → broker 換機即連不上 |

SQLite 複製工具:LiteFS(lease 保證單一 primary)/ Litestream(→S3 restore)/ rqlite·dqlite(Raft)/ 或改 Postgres streaming replication。

## 4. ⚠️ 對抗式驗證戳破的三個結構性洞(核心)

**洞 1 — 副作用路徑沒有冪等保護,failover 重放會重複執行。**
設計原以為「沿用 `ExecutionRequest` 的 `(task_id, idempotency_key)` UNIQUE 去重範本即可」。但程式碼追查:會產生副作用的 dispatch(`AutoTraderService.BuildRequest`→`_dispatcher.DispatchAsync`)**每次都用全新隨機 `RequestId=Guid.NewGuid()`、`TaskId` 寫死 "auto-trader"、繞過 `BrokerService` 的 UNIQUE 早退**。→ 新主接手重放時下游無去重鍵可比對 → **重複 spawn / 重複推送 / 重複 dispatch / 重複下單**。這是 failover 真正會雙重執行副作用的洞,且不限交易。
→ 修法:所有副作用操作前先寫 **durable intent(含穩定 intent_id)** → execute → mark done;下游用 intent_id 去重。最痛的是這條裸路徑要先補穩定 id。

**洞 2 — 跨機時「狀態落地進 SQLite」必要但不充分,甚至造成錯誤恢復。**
`ContainerManager._containers` 的 `SyncFromDockerAsync` 靠**本機** `docker ps -a` 重建。即使把映射落地進 DB,記的 `ContainerId` 指向**舊機的 docker daemon**;新主在另一台 VPS、它的 daemon 沒這些容器 → 新主「以為管得到一批它碰不到的孤兒容器」。**「管理跨機容器」比「複製 DB」更深一層**,LiteFS/Litestream 解不了。落地解的是「單機重啟」,不是「跨機接管」。

**洞 3 — token-fencing 對第三方下游結構上裝不上。**
防殭屍舊主最通用手段=下游檢查 leader epoch token、拒收舊主指令。但 B4A 下游是**交易所 REST、Discord/LINE webhook、本機 Docker daemon——全第三方,無法叫它們認你的 epoch**。→「內網分區但兩邊都能連外」時 token-fencing 用不上,**只剩雲 API power-fencing(直接關掉舊主 VM)**,且要求見證者與雲控制面同側可達。

## 5. 三條路徑(誠實工作量;驗證判定此節 sound)

- **Path A 採用編排器(k3s/Nomad)**:得到共識+排程+service discovery+跨節點 self-healing(解①④和 worker 半)。**但不解**:有狀態 broker+SQLite 不會因跑在 k8s 上就自動 HA(②要自己 LiteFS/Postgres)、第三方副作用 fencing(③)它管不到;control plane 自己要 3 節點、運維複雜度高、單人維護成本真實。**「上 k3s 就 HA」是迷思——它只解一半。**
- **Path B 自建四支柱(etcd/Consul + LiteFS + 自寫選主/fencing)**:= 手工打造迷你 orchestrator。**fencing 最易做錯且最致命**(常見翻車:選主+複製都做了、fencing 漏了 → 舊主假死復活就腦裂)。對單人起步最不划算。
- **Path C 託管雲(managed Postgres failover + 雲 LB)**:少寫,但綁供應商 + 貴。

## 6. RTO/RPO 框架 + 建議

先量化:**能接受多久恢復(RTO)、能丟多少狀態(RPO)**。
- 需求是「分鐘級 RTO、可半自動」→ **暖備 + 第三見證 + 半自動一鍵升級**(人確認主機真死後切,**人當 fence、零腦裂風險**),~90% 恢復速度、避開最危險的 fencing。**甜蜜點。**
- 需求是「秒級 RTO、無人值守」→ 必須吞完整四支柱 + 接受「第三方下游只能 power-fence」+ 把所有副作用補真正的意圖日誌/冪等鍵(洞 1)。量級更大。

**誠實的但書(驗證 gap)**:半自動「那一下」**仍要有人醒著按** → 不滿足「無人值守」。要真無人,就是上面那條大工程,沒有捷徑繞過 quorum/fencing/idempotency。

## 結論
worker failover 趨近零難度;真難題是「有狀態單例 broker + 不可逆副作用」。先補 **idempotency/意圖日誌(洞 1,單機也該補)** 和 **把記憶體易失狀態落地(洞 2 的單機那半)** —— 這兩件無論走哪條路徑都用得到、且現在單機重啟就有價值。全自動 failover 是更大的決策,卡在 quorum(第三票)+ 對第三方下游無法 token-fence 兩個結構限制上。
