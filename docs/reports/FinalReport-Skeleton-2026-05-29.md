# 期末報告骨架 — Bricks4Agent 治理平台

**定位(最重要、決定全文成敗)**:
> **「面向 AI 交易代理的 broker-centric 治理平台(broker-centric governance platform for AI trading agents)」**

⚠ **不要**寫成「AI 量化交易系統」——那會被拿去跟同學的策略數量/前端精緻度/商業化比,失焦且不是你的貢獻。你的核心貢獻是**治理**:讓 AI agent 的交易提議在碰到真錢/真系統前,先過 approval / audit / risk gate / 冪等鎖。交易只是用來**展示治理**的 workload。

骨架是大綱 + 每段要放的關鍵主張/證據;散文由你擴寫。

---

## 1. 摘要 / Abstract
- 一句話:Bricks4Agent 是把「高階模型提案 → 治理層驗證/記錄 → 執行層消費結構化意圖」分離的控制平面。
- 三個賣點:① 治理閉環(approval→手機按鈕→派發→audit)② 資產無關(同一套治理跑 crypto 真錢 + 美股/ETF/台股 paper)③ 生產實證(真錢跑著、踩過的 bug 變 case study)。

## 2. 問題與動機 (Motivation)
- AI agent 會「提議」交易,但**提議 ≠ 可直接執行**:需要授權、風控、稽核、不可重複下單。
- 業界/同類多是**單體、未治理**(模型直接下單);缺「控制平面」這層。
- 真錢環境放大一切:一個 bug = 真金白銀。→ 治理是剛需、不是 nice-to-have。

## 3. 系統架構 (Architecture) ⭐ 核心貢獻
- **Broker = 控制平面**(非自主規劃者):高階模型提案、broker 驗證並記錄、執行層消費結構化 intent(非原始對話)。
- **多 worker + capability dispatch**:quote / strategy / risk / trading / line worker,各自 capability、broker 路由。
- **治理三層**(放架構圖):
  - ACL / Approval / Override / Multi-sig(四層 IApprovalService decorator chain)
  - 冪等鎖(scanner_id+symbol+bar_ts / dispatched_at)
  - Audit chain(每筆 governed action 可追溯)
- 對比 design 取捨:單體(同學)vs broker-worker(你)——後者治理/隔離/可測性勝。

## 4. 治理機制深入 (Governance Deep-dive)
- **閉環安全 demo(報告 demo 段主秀、不是秀 30 個策略)**:
  bot 講話 → approval gate 命中 → **手機按鈕審核** → 派發 → 真實成交 → audit 記錄。
- 冪等:approve-and-dispatch 上鎖,admin 多按一次不會下兩單真錢。
- 風控引擎:r-rules(cooldown / max position / DD)+ circuit breaker + 有效槓桿錨定(名目 vs 權益)。
- 雙重認證:admin / role check 吃 cookie session + scoped token;真錢武裝需 multi-sig + 人手按。

## 5. 資產無關治理化:多市場 demo ⭐(本研究的具體展示)
- **同一套 scanner / 保護鏈 / governance,跑遍資產類別**,只靠 `exchange` 欄抽象:
  | 市場 | 交易所 | 狀態 | 策略(per-market fit) |
  |---|---|---|---|
  | crypto perp | BingX | 真錢 | trend 組合(去集中後) |
  | 美股單股 | Alpaca | paper-shadow | harmonic(t=7.29) |
  | 美股 ETF | Alpaca | paper-shadow | harmonic(t=9.46、最乾淨) |
  | 台股 | (twse 抽象) | paper-shadow | trend ts_momentum(t=5.68) |
- **論點**:治理層完全不在乎資產類別——換市場只換 exchange + 重驗策略,governance/approval/audit/risk 一字不改。這證明平台的核心 thesis(治理可泛化)。
- 副發現(展示「資產有別、治理一致」):美股=反轉(harmonic)、台股=趨勢、FX 無 edge——**策略要 per-market 驗,但治理不用**。

## 6. 方法論嚴謹性 (Rigor) — 框成「平台只治理驗證過的策略」
- Walk-forward OOS pool t-stat + bootstrap CI(非全期報酬)。
- **多重檢定校正**:Deflated Sharpe / Bonferroni / BH-FDR(López de Prado)——揪出「試 N 個變體的假陽性」。
- 成本建模(commission + slippage + funding)、去相關組合、含 2022 熊市深歷史審計。
- 紀律:shadow 4 週 → t-stat 閘 → 先降風險再加風險。**負結果也是貢獻**(conf-sizing 無益、funding spread 冗餘、buffering 不需要——證明不過擬合/不趕鴨子)。

## 7. 生產環境案例研究 (Production Case Studies) ⭐ 教授很愛「learned from production」
每個 bug = 一個治理/正確性教訓:
- **dispatched_at 雙倒單**:真錢冪等鎖缺失 → 多按一次下兩單。修:approve-and-dispatch 上鎖。
- **AsyncLocal transaction race**:BaseOrm instance field 跨 thread 互看、AutoTrader 跟 admin endpoint 撞 transaction。
- **principal=system bypass**:hardcode principal 繞過 approval gate 的權限漏洞。
- **LLM exception 污染 worker connection**:upstream 502 後 exception propagation 破壞 connection state(症狀像 framing bug 但不是)。
- **BaseOrm migration gap**:加 model 欄不自動 migrate 既有表 + 不產 SQL DEFAULT。

## 8. 與同類專案對比 (Comparison)
- 對標同學 `ai-quant-starter2`(單體 FastAPI、~25k LOC、4 test、商業化導向)。
- **你贏的軸**:治理架構、工程嚴謹(750 tests)、真錢冪等鎖、回測可信度(他假 WF + 無顯著性檢定 / 你真 OOS + BH-FDR)、多市場治理。
- **取捨敘事**:他 broad+產品化+未驗證;你 narrow→已反超廣度(97 變體)+ 驗證紮實。monolith vs broker-worker 抽象的 trade-off。

## 9. 結論與未來 (Conclusion / Future Work)
- 12 個月量化成熟化 roadmap(portfolio 配重 → 結構性 alpha → microstructure → 進階方法)。
- 核心心法:**建「能持續找到新 alpha 的 pipeline」,而非找永遠 work 的策略**。
- 治理平台 = 可承載這個 pipeline 的基礎設施。

---

## 寫作提醒(來自對標教訓)
- 開頭就放**架構圖**(多 worker + capability dispatch),讓讀者先看見「這是平台不是腳本」。
- demo 段秀**閉環安全**,不秀策略數量。
- 把真錢 bug 寫成 case study(production rigor 的證據)。
- 策略/前端/商業化**不要主打**(已追平或刻意不做、不是貢獻點)。
