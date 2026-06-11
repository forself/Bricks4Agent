# 給 Benson 的 PR 準備(平台貢獻交回、交易排除)— 備料

> 2026-06-11 備。Benson 要平台累積 + 研究方向;交易不放。他自己決定要不要 merge。
> 機制:PR(你 push 你的 fork → 對 forself/Bricks4Agent 開 PR、他 review/merge、不用給你寫入權)。
> **現實**:領先 862 commit、分歧 3 個月、近期工作高度是交易且與平台交錯 → 不可能「乾淨拆 30 個獨立主題 PR」。改用下列務實計畫。

## 載體
- `benson-mergeable` 分支(本地、myorigin):已 strip Discord + 交易 stack、build green @ 5/16。**= 平台貢獻的主體**(韌性層 / 治理裝飾鏈 / 安全管線 / 儀表板 / 7 workers,即論文 §4-7+5c 那些)。
- 分歧點:2026-03-15。origin = forself/Bricks4Agent(Benson、近月幾乎未動)。

## 務實 PR 計畫(分批、各自可 review)
### PR #1 —— 平台主體(benson-mergeable 現狀)
- 內容:自 3/15 以來的平台演進(運維韌性層、治理 decorator chain、安全中介管線、儀表板、worker 擴充)。**交易/Discord 已排除。**
- 動作:① 先 `git fetch origin` 看 benson-mergeable 能否乾淨 merge 到當前 origin/main(Benson 近月沒動、應該還行)② 對 forself/Bricks4Agent 開 PR。
- ⚠️ 須先 build-verify(benson-mergeable 停 5/16、要確認對當前 origin/main 仍 green)。

### PR #2 —— 台股資金流報表系統(tw-fundflow,自包覆)
- 23 個 commit(老→新,cherry-pick 順序):
```
b97a7f5 每日彙整(三大法人+融資融券、存DB+DM)
cc40059 完整每日報表(金額榜/連續買賣超/摘要+HTML)
afc8f70 HTML 手機版面
77af979 LINE 推播給家人(gated)
cc961a1 家人版省略 watchlist + operator 收件人硬化
d6b45bc docs 擴充規劃
d525584 docs 單位跳動已知問題
c4ccc17 根治「張↔億元」單位跳動
5e64e49 docs 單位跳動已修
c067179 各產業資金流彙總(家人版以產業為主)
66880f5 ?family=true 預覽(推 operator 自己 Discord)
d4b708a family 公開頁去個人段(修 watchlist 外洩)
27b0d1c 個股榜加漲跌%
dd1f081 併入上櫃 TPEx
639a6dd 加大盤情緒 TAIFEX 期貨未平倉
ac368fc 產業報表加龍頭股/融資融券/連續
0f22256 產業連續天數對齊報表日
f808a92 產業連續用現價加權
bc90d79 連續標籤明示「外資連N日」
7b66e20 細產業樹狀資金流
30a0ed5 樹狀分支 + LINE 連結 bulletproof
4aed75d 報表下載 endpoint
b8e0f01 報表對話分析(broker endpoint + bot 工具)  ← ⚠️ 依賴 bot-node(benson-mergeable 已 strip),此 commit 要嘛先帶 bot-node、要嘛跳過
```
- ⚠️ tw-fundflow 核心自包覆,但 **b8e0f01 的 bot 工具依賴 bot-node**(已被 strip)→ 跳過或補 bot-node。其餘 22 個應可較乾淨 cherry-pick。
- 動作:`git checkout benson-mergeable` → cherry-pick 上列(除 b8e0f01)→ build-verify → 併入 PR#1 或單獨 PR。

### PR #3(可選)—— worker-sdk 核心修復
- `f0ac007` fix(worker-sdk): persistent receive buffer — 真正的 parallel-dispatch bug
- `b23fe7d` fix(worker-sdk): serialize frame writes — restore parallel strategy batch
- = **Benson 核心 worker-SDK 的真 bug 修復**,他應該會想要;小、獨立、好 review。

## 排除(不進 Benson)
- 全部交易研究(strat-validate / harmonic / carry / regime / backtest / scanner …)
- 交易執行(autotrader / perpetual / bracket / bingx / trading-worker / risk perp-spot …)
- 論文 docs(docs/thesis/ —— 你的學術交付,非平台碼)
- 量化 docs 已移出私有 repo(2c14078)

## PR 描述草稿(PR #1 用)
> **標題**:平台演進:運維韌性層 + 治理擴充 + 安全管線 + 儀表板 + worker 擴充
> **內文**:
> 此 PR 帶入自 2026-03 分歧以來、在 fork 上累積的**平台層**貢獻(交易應用已排除、保持平台 canonical 乾淨):
> - **運維韌性層**:worker 自動重啟(指數退避)/ 自動擴容 / 策略健康監測 / 緊急 KillSwitch+lockdown / 健康評分 + 拓樸 + 診斷 endpoint。讓「可治理核心」變「可運維」。
> - **治理擴充**:IApprovalService 四層裝飾鏈(template-aware + time-windowed + multi-sig),decorator 疊在你的 base 上、不改核心。
> - **安全中介管線**:AES-256-GCM 信封加密(per-session ECDH + replay 防護)+ scoped-token 認證(epoch 閘道 = 大規模即時撤權)+ A1 fail-closed。
> - **觀測儀表板**:單頁、輪詢控制平面、治理可視化(待審/override/緊急)+ 運維牆。
> - **worker 擴充**:quote / risk / telemetry / code-exec(沙箱+require_approval+audit)等。
> 全部遵守你的執行契約(ApprovedRequest、能力模型),擴充點順勢加、未改受治理核心設計。歡迎挑選要 merge 的部分。

## 執行順序(下次帶新鮮腦袋)
1. `git fetch origin`(取 Benson 最新)
2. build-verify benson-mergeable 對當前 origin/main
3. 開 PR #1
4. (選)cherry-pick tw-fundflow → PR #2 ;worker-sdk fix → PR #3
5. 全程**不改 Benson 的檔**、只加你的;Benson 決定 merge 什麼
