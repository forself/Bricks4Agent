# 每日 TODO 模板 — 量化成熟化日常推進

**寫於**:2026-05-27
**用法**:每天開始時複製到 daily TODO 工具(或就在 session 開頭跑一次)、確保不停下 momentum

關聯:[QuantMatureRoadmap.md](QuantMatureRoadmap.md) 12 個月 vision

---

## 🌅 每日(15-60 分鐘、最低要求)

### 必做(15 分鐘)

- [ ] **check shadow scanner 狀態**:`ssh b4a "docker logs --since 24h b4a-broker 2>&1 | grep '[SHADOW]' | tail -20"`
- [ ] **check 真錢 6 腿 PnL**:打開 trading-manage / 看 Discord daily digest
- [ ] **check 有沒有 emergency alert**(Discord critical / CB triggered)

### 推進(30-45 分鐘、選 1-2 個)

從這個清單任選一個推進:

**A. 閱讀(30 分鐘)**
- [ ] 讀當期目標書一章(QuantMatureRoadmap §核心閱讀清單)
- [ ] 讀一篇 SSRN / arXiv q-fin 新 paper
- [ ] 看 1 篇 AQR / Two Sigma 工程部落格

**B. 程式 / 研究(30-60 分鐘)**
- [ ] 從 Q1/Q2/Q3/Q4 milestones 挑一個任務、做 1 step
- [ ] 寫一個新 strategy variant + 跑 strat-validate(15 分鐘)
- [ ] Refactor 1 個既有 strategy / engine code
- [ ] 加 1 個 monitoring metric / dashboard widget

**C. 文檔 / 紀律(30 分鐘)**
- [ ] 寫今天的研究摘要(就算只是「沒推進、看了 X 本書 Y 頁」)
- [ ] 更新 StrategyCatalog 若有新 t-stat 跑
- [ ] 寫一個 anti-pattern 進 memory(發現自己想跳紀律就記)

---

## 📅 每週(1-2 小時、週日)

- [ ] **週末跑 strat-validate --apply-funding** — 重新驗 t-stat、看排名變化
- [ ] **review 8-10 scanner shadow PnL** — 有沒有偏離 backtest expectations
- [ ] **review 真錢 6 腿** — t-stat 邊界腿(BNB / BTC)是否該換
- [ ] **寫週報摘要**(放 docs/weekly/YYYY-WW.md)
  - 本週推進:具體 commits / 學了什麼 / 解了什麼
  - 本週警訊:任何反正常訊號
  - 下週聚焦:1-2 個具體任務
- [ ] **檢視 QuantMatureRoadmap 進度**(打勾)
- [ ] **新增/刪除 memory entry**(新 anti-pattern / 過時的)

---

## 📊 每月(2-4 小時、月底)

- [ ] **完整 strat-validate + funding-impact A/B**(全宇宙重跑)
- [ ] **月度自評** 按 QuantMatureRoadmap §月度 self-evaluation 框架
- [ ] **真錢 month-end review**:equity / Sharpe / DD / 每腿歸因
- [ ] **strategy decay 監測**:對比每支策略上 shadow 時的預期 Sharpe vs 實際 Sharpe、跌 ≥50% → 退役流程
- [ ] **更新 QuantMatureRoadmap.md**:milestone 標記 + 新發現 gap 加入
- [ ] **學期 / 求職 / 申請物料更新**(若 Q4)

---

## 📈 每季(4-8 小時、季末)

- [ ] **完整 Roadmap milestone 評估**:Q1 結束時看哪些做了、哪些 carry 到 Q2
- [ ] **大幅重寫 StrategyCatalog**:加新發現 / 退役失效
- [ ] **portfolio 配重大調**:依季度 t-stat 跟 live 表現重排
- [ ] **書本 / paper 進度 review**:讀完幾本、計劃下季讀什麼
- [ ] **跨季抗 decay 評估**:結構性 alpha 是否還結構性、找新類別

---

## 🚨 反正常檢核(每週執行)

如果以下任一條成立、停 + 反思:

- [ ] 連續 3 週都 skip daily(代表系統壞了、要 fix)
- [ ] 連續 1 個月真錢虧損 → 紀律加嚴(縮 budget / 加 shadow 條件)
- [ ] 連續 1 個月沒讀書(學習引擎熄火)
- [ ] 連續 1 個月沒新策略嘗試(研究 pipeline 停)
- [ ] 自己想跳「shadow 4 週」紀律(危險、要 stop 自問為什麼)
- [ ] 想加大槓桿 / 加大單腿配重(危險、要回頭)

---

## 💎 今日的 minimum viable progress

**最少 15 分鐘、每天都要做**:就是上面「必做」三項。

連這 15 分鐘都做不到 → 表示要降載(可能課業壓力大、可能身心要休息)、就直接休一天、但**不破 streak 連續超過 3 天**。

連續 7 天沒推進 → 強制檢討:「我是不是放棄了?還是只是疲乏?」、決定是 take a break(明確的)還是 push through。

---

## 🎯 跟 Claude session 結合

每次 Claude session 開頭如果是「繼續推進」、可以說:
> 「今天從 daily template 開始、推進 [X] 任務」

Claude 會自動:
1. 看 daily template + roadmap 的當前 milestone
2. 提案具體推進步驟
3. 執行 + 寫 commit
4. session 結尾更新 weekly summary

長期 sustained focus 比短期 sprint 重要。
