# 容器監管與韌性:從心智模型到 B4A 落地 + VPS 磁碟結論

> 寫於 2026-05-29(workflow 深查:B4A 實況 + 業界通用做法 + VPS 磁碟即時診斷)。
> 給單台 VPS + docker compose 跑 broker 中心治理平台的擁有者。讀完應能:用一個框架解釋任何監管機制、看懂 B4A 走到哪一層就斷了、知道哪些業界做法值得抄哪些是殺雞用牛刀、並對「磁碟夠不夠」給出有依據的決策。

---

## 第一部分:心智模型 —— 一個框架收編所有監管機制

### 核心一句話

> **監管的本質不是「讓東西不死」,而是「假設一切都會死,設計死了之後如何快速、安全、可觀測地恢復;並且在無法恢復時主動停損、叫人,而不是無限掙扎。」**

### 五根支柱(任何監管機制都是它的變體)

1. **健康語意** —— 「活著」是什麼意思?process 在 ≠ 服務可用。要分:進程死了(硬崩潰)/ 進程活著但卡死(deadlock、連線池耗盡)/ 進程沒事但下游壞了。**沒有主動探測,自癒天花板只到「硬崩潰」**;軟卡死要靠主動戳一下看回不回應。
2. **監督階梯** —— 誰來救?救不動往上交給誰?就地重啟(爆炸半徑最小)→ 退避 → **有上限** → 超限**隔離(quarantine)+ 升級(escalate)叫人**。無限重啟是反模式:故障分暫時性(重試有效)與確定性(重試一萬次還是失敗),無限重啟會燒資源、掩蓋根因、真錢可能重複送單、製造「有在自癒」的假象。
3. **失效域 / 爆炸半徑** —— 一個壞了連坐誰?目標是縮小單一故障波及範圍(worker 職責分離、子帳戶隔離、bulkhead 艙壁隔離=獨立資源池)。
4. **放棄時的安全狀態** —— 救不回時停在哪才安全?= graceful degradation,安全 > 功能。真錢安全態 = **只守不攻**(管既有倉、禁新開倉、保留風控)。行情/LLM/broker 任一掛都該自動落到這。
5. **監管者的位置** —— **看門狗必須活在被監管者的失效域之外**;跟被監管者一起死的看門狗等於沒有。判準:「沉默即故障」(dead-man's switch / 心跳)——不靠壞掉的東西報告自己壞了,靠「該聽到心跳卻沒聽到」偵測。

階梯遞推:process 內 try/catch 救不了 process 崩潰 → docker/systemd 看;autoheal 救不了 docker daemon / 整機掛 → host watchdog 看;host watchdog 救不了整台 VPS 斷網斷電 → 要「機器外」的東西看。

---

## 第二部分:B4A 現況對照 —— 走到哪一層就斷了

### 四層監管全景

| 層 | 機制 | 框架對應 | 偵測 | 動作 |
|---|---|---|---|---|
| Docker | `restart: unless-stopped` / `on-failure:3` | 支柱1硬崩潰 + 第0階重啟 | exit code≠0 | 自動拉起 |
| Docker | healthcheck(**只有 broker**) | 支柱1軟卡死(liveness) | `/metrics` 連3次失敗 | 標 unhealthy |
| autoheal 容器 | 監看 unhealthy label | 補 docker 的洞 | status=unhealthy | `docker restart` |
| Broker | `WorkerAutoRestartService` | 支柱2第0階+退避+放棄 | State=Stopped/Failed | 5級退避(0s→30s→2m→10m→30m),超限標 `Unrecoverable` |
| Host | `docker-watchdog.py`(systemd) | 支柱1軟卡死(worker)+ 支柱5部分 | docker ps + broker health API | docker restart + Discord 告警 |

### 計分卡

- **支柱1(健康語意)✅ 扎實**:broker healthcheck 打 `/metrics`(最輕量路由、deadlock 也戳得到);`/api/v1/health/workers` 主動派測試 request 抓 worker wedge,三態 disconnected/error/healthy。斷點:worker image 無 healthcheck,軟卡死偵測綁死在 watchdog+broker health API 活著。
- **支柱2(監督階梯)⚠️ 有雛形、斷在升級**:`WorkerAutoRestartService` 有退避+上限+`Unrecoverable`(很多系統根本沒有「放棄」)。**但標記 Unrecoverable 後沒接:① 主動推人告警 ② 觸發安全態 ③ 過期清理**。host watchdog 的 crash-loop Discord 告警是另一條路徑(看 docker restart count),跟 broker 的 Unrecoverable 沒串起來。
- **支柱3(失效域)✅ 好**:worker 職責分離、子帳戶隔離、broker 無狀態。注意 `MaxContainersPerType=3` 但無主動 failover(是容忍非無縫,對此規模 OK)。
- **支柱4(安全狀態)⚠️ 沒明確定義**:缺「broker/行情/風控任一不可用 → 自動進只守不攻」閘門。「broker 掛=真錢裸奔 CRITICAL」正是此缺口症狀(靠告警叫人、非自動降級)。
- **支柱5(監管者位置)❌ 最大結構弱點**:監管鏈最頂 = host `docker-watchdog.py`,但活在跟所有被監管者同一台 VPS 的失效域內。整機斷網/斷電/daemon 崩 → 全部一起掛、無任何域外通知。

通用 vs 寫死:容器類型(✅ WorkerImages 字典)、退避邏輯(⚠️ 數值寫死 code);健康能力映射 / watchdog WORKER_MAP / restart policy / healthcheck(只 broker)/ Unrecoverable 後處理(❌ 寫死或缺)。

**一句總結**:支柱 1、3 扎實;支柱 2 有雛形但「升級」沒接上;支柱 4 安全態沒明確定義;支柱 5 域外監管斷掉。答辯可主動 framing:「自癒做到 tier-2,我清楚天花板在『整機級域外告警』和『放棄後安全停損自動化』——這是下一步,而非假裝沒有。」

---

## 第三部分:業界做法精華 + 可抄清單

原則:**抄「機制的智慧」,不抄「機制的重量」。**

### Erlang/OTP 監督樹 —— 最該抄「升級哲學」
- 樹狀:葉=worker(做事),節點=supervisor(只看顧不做事)。鐵律:監管者不做業務、業務者不自我監管。
- 三策略=失效域邊界:`one_for_one`(子掛只重啟它)、`rest_for_one`(它+依賴它的)、`one_for_all`(全部)。
- **精華 `max_restarts/max_seconds`**:X 秒內超過 N 次重啟 → 連 supervisor 自己也死、往上一層升級。翻成真錢:「同 worker X 分內重啟超 N 次 → 別悶頭重啟,停掉+進安全態+推 Discord」。B4A 已有一半(退避+上限+Unrecoverable),**缺後半段升級**——最高 CP 值補強,不需引入 OTP,只要把 Unrecoverable 接上告警+安全態。

### Kubernetes —— 抄觀念別抄系統
liveness/readiness/startup probe(可抄 liveness=healthcheck、startup=start_period;readiness 你沒 LB 不用);CrashLoopBackOff 退避(可抄,但 k8s 哲學不徹底:無限退避、不放棄、不升級,OTP 更強);reconciliation loop(自癒本質=寫下期望態+不斷比對修正,你 watchdog 就是手刻版);PDB(借「維護即故障源」觀念→ rebuild checklist)。

### systemd —— 你 VPS 現成的單機 supervisor
`Restart=on-failure`(主動 stop 不被救回,真錢要)；**`StartLimitIntervalSec`+`StartLimitBurst`** = 單機版 max_restarts(超限放棄標 failed)；**`OnFailure=`** = 單機版「升級」(進 failed 自動觸發另一 unit:推 Discord / 安全停機腳本)。

### SRE 心法
bulkhead(獨立資源池/API 額度)、circuit breaker(下游連續失敗跳閘、真錢=停下單避免重複送單或被封)、graceful degradation(=支柱4)、dead-man's switch(=支柱5)。

### 可抄清單
**A 級 直接移植**:① 每個關鍵真錢服務都要 healthcheck+start_period 且 autoheal 涵蓋到 ② **【最該補①】重啟上限→超限隔離+升級告警+進安全態**(Unrecoverable 接 Discord+只守不攻;或 systemd StartLimitBurst+OnFailure)③ 退避統一治理 ④ circuit breaker 包外部依賴(交易所/LLM/行情,真錢跳閘停下單)⑤ 明確實作降級安全態(自動落只守不攻,非靠告警叫人)⑥ **【最該補②】域外 dead-man's switch**(別台機/免費 uptime 服務/手機 ping broker health,沉默超時告警)⑦ 延續失效域分隔 ⑧ 監管者職責分離。

**B 級 懂心法不抄實作**:OTP 連坐(用 compose depends_on 手刻)、readiness(無 LB)、PDB(→checklist)。

**C 級 明確 over-engineering(別碰)**:
- **不要上 k8s**:核心價值是多節點調度,你只有一台沒「別台」可搬→最大賣點失效;control plane 維運複雜度與故障面可能比真錢應用還大=自加新失效域;單節點付全部複雜度只換到 compose 也能給的。
- Swarm/Nomad 同理(單台不需叢集編排)。
- 多副本/真 HA:單台談 HA 是自欺(機器本身就是單點)。要可用性優先序:**域外監控告警 > 第二台熱備 >> 編排器**。

> 收尾:你需要的不是 k8s,是「**重試有上限、超限會隔離並叫人、外部依賴有斷路器、故障時優雅降級到只守不攻、一個活在 VPS 之外的死人開關**」——把 OTP 與 SRE 精華裝進現有 compose+systemd,每件對真錢都是直接停損價值。

---

## 第四部分:VPS 磁碟結論(2026-05-29 即時診斷)

### 判斷:水位假象,不是真實成長

```
df -h /        : 96G 總 / 77G 用 / 20G 剩 / 80%   ← 表面嚇人
df -i /        : inode 只用 9%                     ← 非 inode 耗盡
docker system df:
   Build Cache  73.62GB (73.3GB RECLAIMABLE)       ← 垃圾
   Images       70.79GB (69.06GB=97% 可回收)        ← 垃圾(與上者在 containerd 重疊)
   Volumes       1.057GB (0B 可回收)                ← 真實資料,別動
du /var/lib/containerd = 71G(壓倒性最大)
```

真實需要的東西合計才 ~5GB(volumes 1GB:broker-data 936M/quote-data 118M/trading-data 2M;/opt/b4a 440M 含 169M pre-demo 備份;OS ~3GB)。其餘 ~70GB 是 452 層 build cache(每次 rebuild 累積、從未 GC)+ 5 個 dangling image。

**性質**:對抗驗證精確定性為「**recurring leak 但可免費修**」——每次 rebuild 都漲(根因=沒設保留上限),但隨時可一鍵清。不是該擴容的真實成長。

### 兩個問題的答案
- **(a) 現在正在發生?** 不是。80% 是垃圾假水位;uptime 20 天、load 0.29、8 容器全 Up、broker healthy、無因磁碟崩潰跡象。
- **(b) 該買大方案?** 不該。清完水位掉到 ~7%,96GB 長期夠用。連 70GB 垃圾都沒清就談擴容=為裝垃圾付錢。

### 安全回收(只動 Docker 垃圾、不碰真錢容器/volume)
1. `docker builder prune -af` — 清 build cache(452 層、~65GB);running 容器不受影響,代價=下次 build 慢。
2. `docker image prune -af` — 清 5 個 dangling image(~4GB);目前 8 image 都有 container 用,安全。
**別動**:三個 volume、`/opt/b4a/backups/broker_pre-demo_20260516.db`(回滾點)。

### 根治(讓它不再漲回)
根因=`/etc/docker/daemon.json` 不存在=build cache 無上限。建立:
```json
{
  "builder": { "gc": { "enabled": true, "defaultKeepStorage": "10GB" } },
  "log-opts": { "max-size": "10m", "max-file": "3" }
}
```
`builder.gc` 讓 cache 封頂 10GB(根治);`log-opts` 順手加 log rotation(目前最大 log 才 332K、非當務之急)。改完需 `systemctl restart docker`(重啟容器→挑非持倉/非交易時段、當成故障源做 checklist)。

---

## 全文總綱

1. 監管 = 假設都會死,設計死後快速安全恢復、救不回時主動停損叫人。五支柱:健康語意、監督階梯、失效域、安全狀態、監管者位置。
2. B4A 支柱 1/3 扎實;支柱 2 有退避與放棄但「升級」沒接上;支柱 4 安全態沒明確定義;支柱 5 域外監管斷掉。
3. 最該補兩件:① Unrecoverable→自動升級告警+進只守不攻安全態;② 域外 dead-man's switch。不用上 k8s。
4. 磁碟是水位非漏水:現在不緊急、不該買大方案;清 ~70GB 垃圾 + 設 daemon.json build cache 10GB 上限根治。
