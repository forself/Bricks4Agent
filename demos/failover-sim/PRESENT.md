# Failover demo 演示腳本(報告/答辯用)

目標:3-4 分鐘內讓觀眾看懂「容錯自動移轉 + 防腦裂」是真的運作的。

## 0. 賽前準備(務必先做,別在台上等下載/build)

```bash
cd demos/failover-sim
docker compose -p fsim up -d --build   # 先 pull + build 好(第一次較久)
sleep 20                               # 等 etcd 成形 + 選主
bash status.sh                         # 確認看得到 LEADER + work 在跳,然後 Ctrl-C
docker compose -p fsim down            # 拆掉、上台前再重起(或直接留著開場)
```
**強烈建議:先完整錄一次螢幕**(`bash demo.sh`)當備案——live demo 一定會有莫非定律,有錄影就不怕。

## 1. 視窗配置(視覺最重要)

開**兩個終端視窗**並排:
- **左視窗(看反應)**:`docker compose -p fsim logs -f node-a node-b`
  → 觀眾看 `[A] PRIMARY`、`[B] work → N`、`⛔ 自我 fence` 即時跳出
- **右視窗(你打指令)**:你在這裡下 kill / 分區指令
- (可選)第三個小視窗跑 `bash status.sh` 當「儀表板」:`LEADER = node-X, work = N`

## 2. 講稿 + 動作(照這個節奏)

**① 開場問題(30 秒,先講痛點)**
> 「這是真錢交易控制平面。broker 死了誰管倉位?最天真的兩台互備會**腦裂**——兩個 broker 同時對交易所下單、重複下真錢單,比停機更慘。真正的難題是:**保證同一時刻只有一個主在動作**。」

**② 架構(1 分鐘,給張圖)**
> 「3 個 etcd 組成 quorum,對應**筆電 + 2 台 VPS**(第三票破解兩節點僵局)。兩個 broker 競爭一個 **lease**,搶到的當主。關鍵:**每次下單前先確認自己還握著 lease,否則停手**——這叫自我 fence。」
(指左視窗)「現在 node-? 是主、work 計數在跳 = 它在做有副作用的工作。」

**③ Live 測試 1 — 殺主(1 分鐘)**
右視窗打(把 X 換成現在的主):
```bash
docker kill fsim-node-X
```
> 「模擬主 VPS 當機。看——」(指左視窗)「lease 過期後,另一個自動升為主、work 計數**接續**沒斷。RTO ≈ lease TTL,約 5 秒。」
（補一句)「把它拉回來也不會搶主、避免雙主。」
```bash
docker start fsim-node-X
```

**④ Live 測試 2 — 分區自我 fence(1.5 分鐘,這是重點/money shot)**
右視窗(Y = 現在的主):
```bash
docker network disconnect fsim-net fsim-node-Y
```
> 「這是最危險的情況:網路分區、主機還活著但連不到 quorum。如果它繼續下單就腦裂了。看——」(指左視窗 node-Y)「它偵測到連不到 etcd,**自我 fence、停止寫入**,即使程序還活著。對側接手。work 計數由新主接續,**被分區那個一筆都沒偷寫**——零腦裂、零雙下單。」
```bash
docker network connect fsim-net fsim-node-Y
```
> 「重連後它看到主已經是別人,乖乖待命。」

**⑤ 誠實收尾(30 秒,加分而非扣分)**
> 「單機跑只證明**機制正確性**,不證明實體機死掉也活——那要真的多機。但對驗證設計可運作,這零成本、可重現就夠。真實部署只是把這 5 個容器散到 3 台機(筆電 + 2 VPS),機制一模一樣。」

## 3. 預期問答

- **Q:為什麼不直接用 Kubernetes?**
  A:k8s 解多節點調度,但 broker 是**有狀態單例**——k8s 不自動解 SQLite 狀態複製,也管不到「第三方下游(交易所)無法 fence」。我用 lease 自我 fence 直接解這條,比上整套 k8s 輕。
- **Q:lease TTL 設多短?**
  A:trade-off。短=接手快(RTO 小)但網路抖一下就誤判換手;長=穩但接手慢。demo 用 5s 示範。
- **Q:etcd 自己掛了呢?**
  A:3 節點 quorum,掛 1 個還有 2/3 多數、繼續運作;掛 2 個才停(此時寧可停、不腦裂)。
- **Q:這跟你真錢系統的關係?**
  A:真錢部分另有「交易所原生硬止損」當底線(broker 死也擋),所以停機不致命;這個 failover 是把停機從小時級壓到秒級的進階層,也是治理平台成熟度的展示。

## 4. 一鍵備案

如果 live 出狀況,直接跑全自動版(有完整輸出):
```bash
bash demo.sh        # 自動跑三段、印結論
bash demo.sh down   # 拆除
```
