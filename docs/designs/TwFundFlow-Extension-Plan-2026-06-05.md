# 台股資金流日報 — 擴充規劃(資料 + 排程)

2026-06-05。承接已上線的 `TwFundFlowService` / `TwFundFlowReport` / `TwFundFlowDaily`。
本文規劃「之後加資料」與「排程變動」兩條,先記設計與取捨,實作再逐項做。

---

## 0. 現況基線(擴充前先對齊)

| 面向 | 現況 |
|---|---|
| 資料源 | TWSE **上市**:T86(三大法人/股)+ MI_MARGN(融資融券/張)+ STOCK_DAY_ALL(收盤) |
| 涵蓋 | 4 位數普通股 + 主要 ETF(濾掉權證)。**無上櫃、無期貨、無借券** |
| 模型 | `tw_fund_flow_daily` 每檔每日一列(`entry_key = date:code`、DELETE-by-date 冪等) |
| 渲染 | Discord(自己、完整含 watchlist)/ LINE(家人、`includeWatchlist:false`)/ HTML(完整) |
| 排程 | 單一 `TW_FUNDFLOW_AT_UTC_HOUR`(預設 UTC9 = TST17:00),loop 算下一次 |
| 收件 | Discord = 自己;LINE = `TW_FUNDFLOW_LINE_TO`(家人,目前你爸) |

---

## 1. 資料擴充候選(依價值/工程量排序)

| 資料項 | 來源 | 價值 | 工程量 | 注意 |
|---|---|---|---|---|
| **成交量 / 漲跌幅** | 已有 STOCK_DAY_ALL(只取了 close)| 補強個股榜可讀性 | 小 | 同來源多取幾個 field |
| **上櫃(TPEx)三大法人 + 融資融券** | TPEx OpenAPI(另一組 endpoint)| 補齊櫃買中小型熱門股 | 中 | **另開表** `tw_otc_fund_flow_daily`、解析格式跟 TWSE 不同 |
| **期貨/選擇權 三大法人未平倉** | TAIFEX(另一站)| 大盤情緒(外資期貨多空)| 中 | 單位是「口/契約」非股、粒度是大盤非個股 → **另開表** `tw_futures_inst_daily` |
| **外資借券賣出 / 當沖比** | TWSE(另 endpoint)| 空方力道、投機度 | 小-中 | 個股級、可考慮併入主表或另表 |
| **月營收 / 法說** | TWSE/公開資訊觀測站 | 基本面 | 中 | **非每日**(月節奏)→ 獨立 job + 獨立排程 |
| 主力/券商分點 | 非 TWSE 公開(需爬) | 高但難 | 大 | 暫不做、合規/穩定性風險 |

**主題分組(未來報表可分頁/分段)**:籌碼面(法人/融資券/借券)、情緒面(期貨未平倉/當沖)、基本面(營收)。

---

## 2. 資料模型擴充策略 ⚠

**坑(CLAUDE.md case study「BaseOrm migration gap」)**:`EnsureTable` 對已存在的表**不會自動加新欄位**,且加的欄位**無 SQL DEFAULT**。直接在 `TwFundFlowDaily` 加 column → 既有 `tw_fund_flow_daily` 不會長出該欄、讀舊列會出問題。

**兩條路:**
- **A. 同表加欄**(如 volume/漲跌):必須處理 migration —— 要嘛手動 `ALTER TABLE ADD COLUMN ... DEFAULT 0`,要嘛 drop+rebuild(資料可由 backfill 重抓,成本可接受)。
- **B. 新資料另開表**(上櫃/期貨/營收):**推薦**。不同粒度(個股 vs 大盤)、不同節奏(日 vs 月)、不同單位本來就該分表,`by date` join。也完全避開既有表 migration。

**原則**:同來源同粒度的小欄位 → A(認帳處理 migration);新來源/新粒度 → B。

---

## 3. 排程彈性演進

**現況**:單一 hour、`ExecuteAsync` 算 `nextRun`。夠用於「一天一份收盤報」。

**未來情境**:盤中快訊 + 收盤報(多時段)、不同資料源不同發布時間(期貨午盤、營收月初)、不同收件人不同頻率。

**分層演進(按需求逐步上、別一次到位):**
- **L1(小改)**:`TW_FUNDFLOW_AT_UTC_HOUR` 支援逗號多值(`9,5` = 兩個時段),loop 取最近的下一個。滿足「多推幾次」。
- **L2(中)**:per-section 排程 —— 把「三大法人 / 期貨 / 營收」拆成各自的 build+push,各自 hour。`TwFundFlowService` 內部多個 timer,或一個 tick 迴圈查「哪些 section 到點」。
- **L3(大)**:抽象成 job registry —— 每個 job = `(source, schedule, recipients, render-profile)`,cron-like。等資料源/收件人組合多到 L2 撐不住再上。

**建議**:先停在 **L1**;資料源破 3 種、發布時間錯開時再上 L2。

---

## 4. 擴充時必須守住的原則(隱私/分版)

1. **`includeWatchlist` 模式延伸**:任何「個人化」段(我的 watchlist、我的持股、我的關注)在**家人版一律預設排除**。新增 render section 時,family render profile **default 不含個人內容**。
2. **Public URL 仍含 watchlist**:`tw.b4a-trading.app/tw-fundflow.html` 是公開且含 watchlist 的 —— **別**設成 `TW_FUNDFLOW_PUBLIC_URL` 給家人。要給家人 HTML 連結就得做一份 **family-HTML(去個人段)**,另開 wwwroot 檔 + 另一條 cloudflared path。
3. **收件人隔離**:交易/operator 告警收件人(`NOTIFICATIONS_LINE_RECIPIENT`)跟家人(`TW_FUNDFLOW_LINE_TO`)物理分開,別共用。
4. **新收件人 onboard**:對方先加 bot → 傳一句 → 看 bot-node log 的完整 `[line] rejected user=U...` → 加進對應收件清單(**不**加進 `access.json` 白名單,除非要讓他能操作 bot)。

---

## 4.5 已知問題(2026-06-05 發現)

### ✅ 單位「張 ↔ 億元」會跳動 — 已根治(commit c4ccc17、2026-06-05)

**修法**:改用 TWSE `MI_INDEX?date=YYYYMMDD&type=ALLBUT0999` 抓「報表日自己的」全市場收盤(`TwseFundFlowClient.ParseMiIndex` / `FetchClosesForDateAsync`,用欄位名「證券代號」+「收盤價」定位個股表)。`TwFundFlowService` 改抓報表日收盤、MI_INDEX 失敗退回 DB 既存 `close_price`(`LoadStoredCloses`)、backfill 也補抓各日收盤。→ 任何報表日(含舊日/錯位/歷史日)都拿得到當日收盤、榜單穩定顯示億元。已部署驗證:date=2026-06-05 useAmount=True、聯電+105.1億等。(原始記錄保留如下)

### 🔴 單位「張 ↔ 億元」會跳動(優先修)

**現象**:報表有時顯示「買賣超金額(億元)」、有時顯示「張數」。家人(你爸)看到「張」會很陌生、以為資料怪。

**根因**:`useAmount = closes.Count > 0`,而收盤價來自 `STOCK_DAY_ALL`(**只回最新一個交易日**)。當報表日 ≠ STOCK_DAY_ALL 的最新日(如手動推舊日期、或推送時點與收盤發布錯位)→ `closeMatches=false` → 不掛收盤 → 全表 fallback 成「張」。

**驗證(2026-06-04 資料,已對 TWSE 原始 T86)**:數字本身**完全正確、非 bug**。聯電(外資+149,147張/投信-156,280張)、凱基金(外資-150,842張/投信+215,910張)是當日真實大額法人對作。問題**只在顯示單位**。

**正常情況**:每日 17:00 TST 排程推「當日」資料時,STOCK_DAY_ALL 最新日 = 當日 → 對得上 → 顯示億元。只有手動推舊日期/錯位才跳張。

**根治方向**:報表日要用「**該日自己的收盤價**」,別靠「只回最新日」的 STOCK_DAY_ALL:
- 每個交易日把收盤價可靠存進 DB(`tw_fund_flow_daily.close_price` 目前只在 `closeMatches` 時才寫 → backfill 的歷史日 close=0)。
- backfill 時也補抓對應日收盤(需逐檔 STOCK_DAY,或找回傳指定日全市場收盤的來源)。
- render 時優先用 DB 存的當日 close;真的沒有才退張,並在抬頭明示單位。

---

## 5. 建議近期順序

0. ✅ **(已完成 2026-06-05、c4ccc17)** 修「張↔億元」跳動 — 改 MI_INDEX 抓報表日自己的收盤、穩定億元(見 4.5)
1. **(小)** 成交量/漲跌幅補進個股榜(同來源、處理 migration 或併進另表)
2. **(中)** 排程 L1 多時段(若想要盤中 + 收盤兩推)
3. **(中)** 上櫃 TPEx — 另開表、補齊櫃買
4. **(中)** 期貨三大法人未平倉 — 另開表 + 「大盤情緒」段
5. **(later)** 月營收(獨立 job/排程)、借券賣出、L2/L3 排程抽象

每項上線前:單元測試 + family 版 default 去個人段 + 注意 BaseOrm migration。
