# Broker 高可用 / 故障移轉:成熟方案評估 vs 自建

> 日期:2026-05-30
> 範圍:B4A broker(有狀態單例控制平面)的「治理層在故障時不只靠推播告警」這個需求。
> 立場:**中立評估,不護航**。本文把「自建半自動暖備」當成一份**待審提案**來挑毛病,
> 並依資深建議「先查成熟方案、再跟自建比對」執行。

---

## 0. TL;DR(先講結論)

1. **詞要分清楚**:目前構想是「**快速復原的暖備**(秒~分鐘級中斷)」,**不是「always-on(不中斷)」**。在報告/答辯時混用這兩個詞會被打。
2. **你現在用的 Litestream 官方說它不做 failover**——它是單機災難復原(DR)工具。拿它當「自動移轉」的基礎,定位上就不對。
3. 真要做 HA,要處理的層(反向代理導向、狀態複製一致性、心跳健康監控、選主防腦裂、容器重排程)——**成熟方案(k3s / LiteFS / Patroni / Consul / Traefik)已經各自做掉**,而且處理過自建還沒想到的 partition/fencing edge case。
4. **自建半自動暖備的核心弱點**:「半自動 = 用人取代仲裁」**沒有解決腦裂,只是把判斷搬給人**——而人在網路分割時無法可靠分辨「主真的死」vs「只是我連不到它」。
5. **建議的學術產出**:不是硬刻一個半成品 failover,而是「**成熟方案評估 + 取捨分析 + 一個 scoped 安全 demo**」。這更有價值、更安全,也正好回答老師「不只推播」。

---

## 1. 問題定義

- **broker 是什麼**:控制平面,有狀態單例 —— 狀態在本地 SQLite(`broker.db`:approval/audit/capability/grant)+ in-memory(RecentLogs、dedup 視窗、epoch 快取、AutoTrader cycle 狀態)。**帶真錢副作用**(下單)。
- **為什麼難**:無狀態服務多開幾台、前面擺 LB 就好;有狀態單例 + 副作用,兩台同時活會**重複下單**,所以必須有人或機制保證「同時只有一台在動」。
- **目標分級(關鍵)**:

  | 等級 | 中斷時間 | 難度 | 是否需要 |
  |---|---|---|---|
  | A. 自癒(同機重啟) | 秒級 | 低(**已做**) | ✅ 已有 |
  | B. 暖備復原(換機) | 秒~分鐘 + 人工 | 中 | ← 目前構想 |
  | C. always-on(自動不中斷) | ~0 | 高(接近碩論) | ✗ 暫不做 |

- **老師的需求**:平台治理出問題,只靠推播不夠,要有「其他手段」。→ **A 已部分達標**,B 是加分。
- **資深(Benson)的提醒**:實作很複雜(幾層反向代理 + 帶心跳的集群健康監控要自己處理);這種半自動同步切換他做過一次,當年沒有成熟雲端/集群方案,**現在有了**,建議先查再比對。**他沒有反對,只是提醒複雜度。**

---

## 2. 真要做 HA 必須處理的層

| 層 | 做什麼 | 自建要面對的難題 |
|---|---|---|
| **L1 反向代理 / 流量導向** | 把請求只導到「當前 primary」 | 切換誰觸發?兩台同時被導到 = 雙活 |
| **L2 狀態複製** | 讓備節點有主的資料 | 同步 vs 非同步(資料遺失窗);一致性 |
| **L3 心跳 / 集群健康監控** | 判定「主死了沒」 | 分辨「真死」vs「網路分割」← 最難 |
| **L4 選主 / 防腦裂(fencing)** | 保證同時只有一個 primary | 沒有 quorum 就無法安全自動決定 |
| **L5 容器自癒 / 重排程** | 掛了把它拉回來 | 單機 restart 已有;跨機要 orchestrator |
| **橫切:副作用冪等** | 切換瞬間別重複下單 | 真錢路徑要冪等鎖 + fencing token |

---

## 3. 成熟方案盤點(實際能力邊界 + 維護狀態)

### 3.1 Litestream(**你正在用的**)
- **能力**:持續把 SQLite WAL 非同步複製到物件儲存(R2/S3)。v0.5(2025/10)新增 VFS read replica。
- **邊界(官方原話)**:**「無法複製到其他 live 伺服器,且不支援自動 failover;是單機災難復原工具。」**
- **結論**:**這是 DR/備份,不是 failover。** 拿它當「自動移轉」的核心,定位錯誤。它能做的是「備節點 restore 出一份近期狀態」,RPO 秒級——可作 B 級暖備的**資料來源**,但切換邏輯它一概不管。

### 3.2 LiteFS(SQLite 專用 HA,Litestream 同作者群)
- **能力**:FUSE-based,primary/replica + **Consul lease 自動 failover**(預設 lease TTL 10s → 主死後最多 10s 才有新主接手寫入);replica 上的寫入轉發給 primary。
- **邊界**:**非同步複製仍有資料窗**;**LiteFS Cloud 已停運**,只剩 OSS 工具持續維護文件(社群活躍度下降)。FUSE 掛載增加部署複雜度。
- **結論**:**這才是 SQLite 對應的自動 failover 方案**,概念上正是你想要的「自動版」。但它把難題(選主/fencing)外包給 Consul——印證了「沒有 quorum 就做不到安全自動切」。維護狀態是隱憂。

### 3.3 PostgreSQL + Patroni(業界 DB HA 標準)
- **能力**:Patroni 用 etcd/Consul 做選主 + 自動 failover,搭 HAProxy/PgBouncer 做導向。久經實戰。
- **邊界**:**要先把 broker 從 SQLite 遷到 Postgres** —— 這會**動到 Benson 的核心 ORM/儲存層**,違反「不動原檔」,工程量大。
- **結論**:最成熟,但遷移成本最高、最不符合本專案約束。

### 3.4 k3s / Docker Swarm / Nomad(容器編排)
- **能力**:liveness/readiness probe(= L3 心跳健康監控,**內建**)+ 自動 reschedule(L5)+ Service/Ingress(= L1 反向代理,**內建**)。
- **邊界**:**單副本 stateful 服務在 reschedule 時仍有中斷**(不是 always-on);而且**不解副作用腦裂**——編排器會在新節點拉起 broker,但無法保證舊的真的死透(尤其節點失聯非崩潰時)。
- **結論**:**一次把 L1/L3/L5 好幾層包掉**,這正是 Benson 說「幾層反向代理 + 心跳健康監控」自己幹很煩、而現在有現成的。k3s 在單台 VPS 可單節點跑,但要真 HA 需要多節點 + 仍要解 L2/L4。

### 3.5 etcd / Consul / ZooKeeper(協調原語)
- **能力**:分散式 KV + **quorum 選主 + 心跳**。是 LiteFS/Patroni 底下用的東西。
- **邊界**:是「原語」不是「成品」,自己用要寫整合;**需要奇數節點(通常 3)**才能容忍 1 台故障——這就是「第三見證」的由來。
- **結論**:你之前做的 failover sim 就是用 etcd。直接用它=回到「自己刻」,Benson 正是要你避開。

### 3.6 導向層:Keepalived(VRRP)/ HAProxy / Traefik / Cloudflare Load Balancing
- **能力**:主動健康檢查 + 自動把流量切到活的後端;Keepalived 用浮動虛擬 IP 在兩台間漂移;Cloudflare LB 在 DNS/代理層做 health-check failover(你已用 Cloudflare Tunnel)。
- **結論**:這是 L1 的成熟解。**你構想裡「tunnel 憑證在備份、DNS 免改」其實沒設計這層**——兩台同跑同 tunnel 會被 Cloudflare 同時導流。

### 3.7 Managed DB / 雲託管(RDS Multi-AZ、Cloud SQL HA 等)
- **能力**:provider 全包自動 failover。
- **邊界**:要上公有雲 + 遷 DB + 花錢 + 資料離開自管環境。
- **結論**:最省事但最不符合「自管 VPS + 學生專案」情境。

---

## 4. 逐層對照:自建 vs 成熟

| 需要的層 | 自建構想怎麼做 | 成熟現成方案 | 差距 / 風險 |
|---|---|---|---|
| L1 導向 | Cloudflare tunnel 憑證在備份 | Traefik / HAProxy / Keepalived / Cloudflare LB | **構想未設計此層**;雙 tunnel 會雙活 |
| L2 複製 | Litestream → R2(只複製) | LiteFS / Patroni / managed DB | Litestream **官方說不做 failover**;async 有資料窗 |
| L3 心跳/判定主死 | **人工確認** | etcd / Consul quorum | 人分不出「死」vs「分割」 |
| L4 選主/fencing | 半自動(人按) | Patroni / LiteFS(Consul lease) | **自建無 fencing**,partition 時可能雙活 |
| L5 容器自癒/重排程 | docker restart + watchdog(已有) | k3s / Swarm / Nomad | 跨機重排程未做 |
| 副作用冪等 | approve-and-dispatch 已有冪等鎖 | (同) | 跨節點 UNIQUE 無效,仍需 fencing token |

**一句話**:自建想做的,大致等於 `k3s + LiteFS + Consul + Traefik` 這套堆疊已經做掉的事。

---

## 5. 在「我們這台」的可行性(Contabo VPS ×1 + 筆電 + Cloudflare Tunnel + SQLite + 真錢)

| 方案 | 落地難度 | 成本 | 風險 | 在本專案可行? |
|---|---|---|---|---|
| 自建半自動暖備 | 中 | 0 | 腦裂搬給人;Litestream 定位錯 | demo 可、production 不宜 |
| LiteFS + Consul | 中高 | 0(自架 Consul)| FUSE 複雜;Cloud 已停 | 概念正確、可作「正確做法」展示 |
| k3s(多節點) | 高 | 需 2+ 節點 | 單副本仍中斷;不解副作用 | 太重,單機意義不大 |
| Patroni + Postgres | 高 | 0 | **要遷 DB、動核心** | 違反不動原檔,不建議 |
| Managed DB | 低 | **要錢 + 上雲** | 資料離開自管 | 不符情境 |

---

## 6. 嚴格質疑:自建半自動暖備的四個洞

1. **「半自動」沒解決腦裂,只是把仲裁搬給人。** 網路分割時,「主沒回應」可能只是你連不到、它其實還活著服務別人。人按 promote → 雙活 → 接真錢=雙下單。quorum 存在的唯一理由就是機器也分不出來,要靠多數決——這恰恰被「半自動」跳過、卻沒消失。
2. **Litestream 是複製/備份,不是 failover。** async、RPO 秒級;主 commit 了還沒上傳就死 → 那幾秒的 audit/approval/dispatch 紀錄在備節點不存在 → 一筆「已派發」沒同步過去 → 備節點可能**重派**。
3. **導向層(L1)本身會腦裂。**「憑證在備份、DNS 免改」把 L1 講太簡單;兩台同跑同 tunnel,Cloudflare 兩邊都導。需要「同時只一台註冊 tunnel」或主動健康檢查反代——構想裡沒有。
4. **暖備不只 DB 要同步。** secrets / master key / tunnel 憑證 / env / 映像版本都要預先在備機備好且**與主節點保持一致**,否則切過去行為不同。

---

## 7. 中立結論 + 建議路徑

- **詞分清楚**:現有構想是 **B 級「快速復原暖備」**,不是 **C 級「always-on」**。報告就用 B 的語言,別宣稱不中斷。
- **Litestream 重新定位**:它是你的 **DR 層(備份/復原)**,不是 failover 引擎。這個定位**本身就是 A/B 級的有效手段**,對老師「不只推播」有交代。
- **不要硬刻 C 級。** 自動 failover 需要 quorum + fencing + 導向切換,等於重做 `k3s+LiteFS+Consul`,且在真錢上線前無法安全驗證 → 這是 Benson 說的「接近碩論」的部分。
- **建議的學術產出**(風險最低、價值最高):
  1. **本評估報告**(成熟方案 vs 自建的取捨分析)—— 證明你查過、懂邊界、懂為什麼難。
  2. **一個 scoped 安全 demo**:拿一個**無真錢、24/7、有狀態**的容器當靶(如 telemetry-worker / quote-worker),示範「Litestream 複製 → 備機 restore → 半自動 promote → 狀態完整轉移」。**證明機制,不碰真錢。**
  3. **真 C 級 always-on 列為 future work**,並畫出正確架構(k3s + LiteFS/Consul 方向)——展示你知道正確做法,即使不全做。
- **紀律**:任何 failover 測試**只在隔離 broker 實例(AutoTrader OFF、不放真錢 key)上做,絕不在實盤 armed broker 上跑**。

---

## 8. 架構圖

### 8.1 自建半自動暖備(漏洞已標)

```
                    ┌──────────────────────────────────────┐
     使用者/bot ────▶│  L1 反向代理/導向                       │
                    │  Cloudflare Tunnel                    │
                    │  ⚠ 兩台同跑同 tunnel → 同時被導流(雙活) │
                    └───────────────┬──────────────────────┘
                          只能指向 primary ← 切換誰觸發?切錯=雙活
              ┌──────────────────────┴──────────────────────┐
              ▼ active                                        ▼ passive 暖備
     ┌──────────────────┐                          ┌──────────────────┐
     │ Broker 主 (VPS)   │                          │ Broker 暖備(筆電) │
     │ broker.db        │                          │ broker.db(還原)   │
     │ + in-mem 狀態     │                          │ AutoTrader OFF    │
     └────────┬─────────┘                          └─────────▲────────┘
              │ Litestream WAL (async, RPO~10s)              │ restore(定期=暖)
              ▼                                              │
     ┌────── L2 狀態複製：R2 物件儲存 ──────────────────────────┘
     │  ⚠ async = 主死前最後幾秒沒上傳 → 暖備缺資料 → 可能重派
     └─────────────────────────────────────────────────────
              ▲
     ┌────────┴────── L3 心跳/判定主死 ───────────────────────┐
     │  自建 = 人工判斷                                        │
     │  ⚠ 人分不出「真死」vs「網路分割」→ L4 fencing 缺 → 腦裂   │
     └────────────────────────────────────────────────────┘
```

### 8.2 成熟堆疊對應(同樣的層、現成元件)

```
     使用者/bot ──▶ [Traefik/HAProxy 主動健檢] ──▶ 只導活的後端   (L1)
                         │
            ┌────────────┴────────────┐
            ▼                          ▼
     ┌─────────────┐            ┌─────────────┐
     │ broker (k3s │  liveness  │ broker (k3s │              (L5 reschedule)
     │  pod, 主)   │  probe ───▶│  pod, 備)   │
     │  LiteFS     │            │  LiteFS     │
     └──────┬──────┘            └──────┬──────┘
            │  同步/半同步複製 + 寫轉發         (L2)
            ▼                          ▼
     ┌─────────────────────────────────────┐
     │  Consul:lease 選主 + 心跳 + fencing   │   (L3 + L4,quorum 需 3 節點)
     └─────────────────────────────────────┘
```

> 兩張圖的差別**不是元件多寡,而是 L3/L4**:成熟堆疊用 Consul quorum **自動且安全**地判定主死 + fence;自建把這一步交給人,在網路分割下不可靠。

---

## Sources(查證來源)

- Litestream 官方定位(不做 live 複製、不做自動 failover、單機 DR):
  - https://litestream.io/how-it-works/
  - https://fly.io/blog/litestream-v050-is-here/(v0.5 read replica)
  - https://simonwillison.net/2025/Oct/3/litestream/
- LiteFS 自動 failover(Consul lease、TTL 10s)與 Cloud 停運:
  - https://fly.io/docs/litefs/faq/
  - https://fly.io/docs/flyctl/litefs-cloud-status/
  - https://community.fly.io/t/what-is-the-status-of-litefs/23883
