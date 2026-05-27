# 量化成熟化 Roadmap — 從學生作品到專業 quant 12 個月計畫

**寫於**:2026-05-27
**啟動目標**:把 B4A 從「個人量化專題」推進到「業界初/中級 quant 平台」、累積 hireable / fundable 級別實證
**核心心法**:**不要找永遠 work 的策略、要建能持續找到新策略的 pipeline**

---

## 🎯 12 個月願景(2027-05-27)

到那時:
- ✅ 真錢跑滿 1 年、有 live track record(Sharpe / DD / 月度報酬曲線)
- ✅ Portfolio 含 10+ 結構性 alpha(非只 pattern alpha)
- ✅ 月度自動化 strategy decay 監測 + 替補 pipeline
- ✅ 讀完 5 本核心 quant 書、寫過 10 篇研究摘要
- ✅ 能在 prop firm / crypto quant fund 面試過初級職位
- ✅ 學期報告 / master 申請 / GitHub portfolio 有完整故事

---

## 📅 季度 milestones

### Q1(2026-05 → 2026-08):portfolio 配重成熟

**主題**:從固定 notional sizing 進化到專業組合管理

- [ ] **Kelly fraction sizing**(2 週)— 每 scanner 按 edge × win-rate 動態配
- [ ] **Vol-targeting**(2 週)— 每月按 realized vol 調整 notional
- [ ] **Mean-variance portfolio optimization**(3 週)— 8+ scanner 之間配重
- [ ] **Risk parity / equal risk contribution**(3 週)— ERC across scanners
- [ ] **Maximum drawdown 控制**(2 週)— DD-aware position scaling
- [ ] 讀完:Robert Carver《Systematic Trading》、Ernie Chan《Algorithmic Trading》
- [ ] Q1 結束指標:組合層 Sharpe ≥ 1.0(目前單 scanner 平均 ~0.7)

### Q2(2026-08 → 2026-11):結構性 alpha 擴充

**主題**:把抗 decay alpha 從 2 個擴到 8+ 個、跨資料源

- [ ] **OI momentum**(2 週)— 加 OI history 抓取 + 同 funding 模式驗證
- [ ] **Long/Short ratio momentum**(2 週)— Binance fapi/v1/topLongShortPositionRatio
- [ ] **Cross-exchange basis arb**(4 週)— BingX vs Binance vs Bybit
- [ ] **On-chain whale flow**(4 週)— Nansen / Glassnode API、ETH 大轉帳訊號
- [ ] **Liquidation cascade prediction**(3 週)— Coinglass / Hyblock API
- [ ] **Cross-asset funding spread**(2 週)— 每幣 vs BTC funding spread momentum
- [ ] 讀完:Andreas Clenow《Following the Trend》、Larry Harris《Trading and Exchanges》前 1/3
- [ ] Q2 結束指標:5+ 個結構性 alpha 上 shadow、跟 pattern alpha 0.4+ 去相關

### Q3(2026-11 → 2027-02):微觀結構 + 真錢規模化

**主題**:理解市場微觀結構、把規模從 $347 推到 $5k-$10k

- [ ] **Order book L2 接入**(3 週)— Binance / BingX websocket
- [ ] **Bid-ask spread cost modeling**(2 週)— 取代固定 slippage 假設
- [ ] **TWAP / VWAP execution**(2 週)— 大單拆單算法
- [ ] **Queue position aware limit orders**(2 週)— 取代純 market order
- [ ] **真錢 scale up**(持續)— 每月 +50% 規模、保紀律
- [ ] 讀完:Larry Harris《Trading and Exchanges》全本、Cartea/Jaimungal《Algorithmic and High-Frequency Trading》前半
- [ ] Q3 結束指標:真錢 equity ≥ $5k、月 PnL 跟 backtest 偏差 < 30%

### Q4(2027-02 → 2027-05):進階 + 證明書

**主題**:進階方法論 + 對外輸出

- [ ] **HMM regime detection**(3 週)— 取代 EMA cross 粗略 regime
- [ ] **Bayesian portfolio optimization**(3 週)— 含不確定性的 mean-variance
- [ ] **ML for feature engineering**(4 週)— 小心 overfit、用 walk-forward sanity
- [ ] **Cointegration / pairs trading**(2 週)— 加上 mean-reverting LS 類別
- [ ] **Options Greeks for hedging**(3 週)— Deribit BTC options 對沖 perp
- [ ] 讀完:Marcos López de Prado《Advances in Financial Machine Learning》
- [ ] **學期報告 / 求職 / 申請投出**
- [ ] Q4 結束指標:有 face-to-face 面 prop firm / fund / master、track record 1 年 Sharpe ≥ 0.8

---

## 📚 核心閱讀清單(優先序)

### Year 1 必讀(實作向、跟現有專案直接對應)

1. **Ernie Chan《Algorithmic Trading: Winning Strategies and Their Rationale》** ⭐⭐⭐
   - 為何:跟現有 ts_momentum / mean revert / pairs trade 直接對應
   - 行動:每章配做一個既有策略對照、寫成 doc

2. **Robert Carver《Systematic Trading》** ⭐⭐⭐
   - 為何:Q1 portfolio 配重的聖經、Kelly / risk targeting / forecast diversification
   - 行動:讀完直接 implement 章節 5-7 的配重公式

3. **Andreas Clenow《Following the Trend》** ⭐⭐
   - 為何:tsmom 已是我們真錢策略、Clenow 是這領域的 voice
   - 行動:對照 ts_momentum 實作、看落差

4. **Larry Harris《Trading and Exchanges》** ⭐⭐⭐
   - 為何:微觀結構聖經、Q3 訂單簿 / 流動性必讀
   - 行動:讀前 200 頁(基礎)、後半 reference 用

5. **Marcos López de Prado《Advances in Financial Machine Learning》** ⭐
   - 為何:ML in finance 最嚴肅的書、避免 overfit / 看穿假 alpha
   - 行動:Q4 讀、先讀第 1-7 章(資料 + 標籤 + 抗 overfit)

### Year 1 必讀 papers

1. **Moskowitz, Ooi, Pedersen "Time Series Momentum" (2012)**
   - 為何:我們 ts_momentum 的學術依據、60+ 年實證
2. **McLean, Pontiff "Does Academic Research Destroy Stock Return Predictability?" (2016)**
   - 為何:alpha decay 量化研究、為什麼公開的 anomaly 半衰期短
3. **AQR "Buffett's Alpha" (Frazzini, Kabiller, Pedersen)**
   - 為何:長期 quant 風格論證(低 vol + 槓桿)
4. **AQR "Volatility-Managed Portfolios" (Moreira, Muir)**
   - 為何:Q1 vol-targeting 學術版

### 持續看的 source

- **SSRN**(quantitative finance):每週瀏覽
- **arXiv q-fin**:每週瀏覽
- **AQR research papers**:每月一篇
- **Hudson River Trading / Renaissance / Two Sigma 工程部落格**(罕有但金)

---

## 🛠 Skill gaps 待補(優先順)

| 優先 | 技能 | 對應書 / 章節 | 為何 |
|---|---|---|---|
| 1 | Kelly fraction position sizing | Carver Ch 5 | 真錢 sizing 從固定 → 動態、最高 ROI |
| 2 | Vol-targeting | Carver Ch 7, AQR paper | 高 vol 期降倉、降 DD |
| 3 | Mean-variance portfolio | Carver Ch 11 | 8 scanner 配重 |
| 4 | Risk parity (ERC) | Carver Ch 11 | mean-variance 替代、不需報酬預期 |
| 5 | Drawdown control | Chan Ch 8 | DD-aware scaling |
| 6 | Cross-sectional momentum | Asness papers | 加新策略類別 |
| 7 | HMM regime detection | López de Prado Ch 8 | 取代 EMA cross |
| 8 | Bayesian methods | López de Prado | overfit control |
| 9 | Microstructure | Harris 全本 | execution alpha |
| 10 | Options Greeks | Hull 入門 | 對沖工具 |

---

## 🧠 Mental models 待建立

業界 quant 跟散戶差別、心智模型層面:

1. **Pipeline thinking** — 「永遠在養下一支策略」、不愛上現役 ✅ (已建立)
2. **Decay awareness** — 策略半衰期、退場規則 ✅ (已建立)
3. **Cost reality** — funding / slippage / commission 從預設假設變真實 modeling ✅ (今天建立)
4. **Selection bias** — 描述性 stats ≠ 處方性 alpha ✅ (今天建立)
5. **Regime humility** — 牛市策略不一定 bear 也 work ⚠ (部分、需 bear 樣本)
6. **Path dependence** — 同 Sharpe 不同路徑、爆倉風險不同 ⚠ (部分)
7. **Compounding > novelty** — 活著比花俏重要 ✅ (已建立)
8. **No edge in obvious places** — RSI / MACD 等顯學死了 ✅ (已建立)
9. **Position sizing is alpha** — Kelly / vol-target 跟訊號同重要 ❌ (Q1 主題)
10. **Microstructure exists** — 不是只有 close-to-close、bid-ask 是真實的 ❌ (Q3 主題)

---

## 📊 月度 self-evaluation 框架

每月 1 日跑一次:

### 量化指標
- 真錢 equity / 上月 % 變化 / 累計 PnL
- shadow scanner: active 數 / closed 數 / 累計 PnL / 勝率
- 策略 t-stat 重跑(顯著數 / 王者策略 t 是否下降)
- 真實 vs backtest Sharpe 偏差

### 質性指標
- 本月讀了幾本書 / 幾篇 paper
- 本月新增幾個 strategy / 退役幾個
- 本月寫了幾篇研究摘要
- 本月解了幾個 bug / 學了幾個 anti-pattern

### 行動產出
- 學期 / 研究 doc 寫了幾篇
- 投出哪些 application(若 Q4)

---

## 🌊 反正常 / 反趨勢警訊

下面任何一個出現、立刻停 + 反思:

- 連續 3 個月真錢虧損
- shadow Sharpe ≤ backtest Sharpe 50%(策略 decay 警報)
- 自己覺得「我搞懂了、不會錯」(危險、業界叫 hubris)
- 想改紀律(縮 shadow 期、跳 t-stat、用單路徑做決策)
- 焦慮消失(健康的 quant 永遠焦慮)
- 從專業書本沒新學習超過 2 週

---

## 🎯 不在範圍(避免分心)

明確 NOT do 的事:

- ❌ 學 day trading / scalp(跟長期 build 衝突)
- ❌ 追 meme coin alpha(crypto 內最危險)
- ❌ Twitter influencer 跟單(訊號 -EV)
- ❌ 大幅加槓桿(規模慢慢加、不靠槓桿)
- ❌ 大規模重構(只在必要時、避免技術債放大)
- ❌ 學 deep learning for prediction(López de Prado 才碰、現在易 overfit)

---

## 🔄 維護指引

這份 doc 每月底更新:
- 完成的 milestone 標 ✅
- 推進中的標 🟡
- 新發現的 gap 加進清單
- 修正過時的優先序

下次大改:2026-08-27(Q1 結束、回顧 + 訂 Q2 細節)

---

## 📌 相關文件

- 每日 TODO 模板:[DailyTodoTemplate.md](DailyTodoTemplate.md)
- 真錢上線紀律:`docs/reports/PortfolioOptimizationReview-2026-05-26.md`
- 策略目錄(動態):`docs/reports/StrategyCatalog-2026-05-27.md`
- 研究紀律 memory(8+ 條): `~/.claude/projects/.../memory/feedback_*.md`
