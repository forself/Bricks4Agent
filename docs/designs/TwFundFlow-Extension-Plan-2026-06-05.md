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

## 5. 建議近期順序

1. **(小)** 成交量/漲跌幅補進個股榜(同來源、處理 migration 或併進另表)
2. **(中)** 排程 L1 多時段(若想要盤中 + 收盤兩推)
3. **(中)** 上櫃 TPEx — 另開表、補齊櫃買
4. **(中)** 期貨三大法人未平倉 — 另開表 + 「大盤情緒」段
5. **(later)** 月營收(獨立 job/排程)、借券賣出、L2/L3 排程抽象

每項上線前:單元測試 + family 版 default 去個人段 + 注意 BaseOrm migration。
