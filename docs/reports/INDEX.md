# Reports Index

14 份報告分組導覽。**研究紀錄 .md 每天累積、不刪**（負面結果也是知識）。

## 🎯 部署/決策（最新優先）

| 報告 | 日期 | 摘要 |
|---|---|---|
| **[Portfolio Optimization Review](PortfolioOptimizationReview-2026-05-26.md)** | 2026-05-26 | portfolio.json 5 個部署逐 symbol 評估 + ETH/LTC LS 候選驗證。發現 LTC × fib_retrace_ls Sharpe 1.40、BNB 該換策略類別 |
| [Usable Combos](UsableCombos-2026-05-24.md) | 2026-05-24 | 12 策略單腿績效 + 去相關精選 4 支組合 + 部署細則。**目前 portfolio.json 的依據** |

## 📊 策略研究（按時間）

| 報告 | 日期 | 摘要 |
|---|---|---|
| [Long-Short Quant Batch](LongShortQuantBatch-2026-05-24.md) | 2026-05-24 | 5 支多空量化策略生成+驗證（含「crypto long-short 逆勢均值回歸會死」結論）|
| [Pattern Strategies (Fib + Harmonic)](PatternStrategies-FibHarmonic-2026-05-24.md) | 2026-05-24 | fib_retrace_ls 可用 ✅、harmonic_ls 無 OOS edge ❌ |
| [New Quant Strategies](NewQuantStrategies-2026-05-23.md) | 2026-05-23 | 第一批趨勢族策略 |

## 🔬 研究 Log（每日累積、跨多 commit）

| Log | 開張 | 結論 |
|---|---|---|
| **[Param Stability Research](ParamStabilityResearch-Log.md)** | 2026-05-26 | 5 個 robust (策略, 幣) pair 發現；filter 路徑不適合；LTC × fib_retrace 最強 |
| [Fib Retrace Research](FibRetraceResearch-Log.md) | 2026-05-26 | H1-Fib regime filter ❌；**H2-Fib SL ✅** 風險調整後 +72%（DD 96→67）|
| [Harmonic Research](HarmonicResearch-Log.md) | 2026-05-26 | H1 regime / H4 timeframe / H-Combo 全 ❌——**諧波研究線收線** |

## 🏗 架構文件

| 報告 | 日期 | 摘要 |
|---|---|---|
| [Platform Architecture](PlatformArchitecture-2026-04-24.md) | 2026-04-24 | 整體系統架構 |
| [Current Architecture & Progress](CurrentArchitectureAndProgress-2026-03-26.md) | 2026-03-26 | 早期進度快照 |
| [Additions and Relationships](AdditionsAndRelationships-2026-04-21.md) | 2026-04-21 | 模組關係 |

## 🖥 子系統

| 報告 | 日期 | 摘要 |
|---|---|---|
| [Dashboard / Quote Worker](Dashboard-QuoteWorker-Report-20260411.md) | 2026-04-11 | dashboard + quote-worker |

## 🧪 測試 Snapshot

| 報告 | 日期 | 摘要 |
|---|---|---|
| [Test Report](TestReport-20260411-143116.md) | 2026-04-11 | 早期測試 snapshot |
| [System Test Report](SystemTestReport-2026-03-22.md) | 2026-03-22 | 系統測試 |

---

## 研究 Log 寫作慣例

每條實驗一個 block：
- **假設**：驗 / 反驗什麼
- **改動**：具體做什麼（含檔案 link）
- **成功標準**：可量化、falsifiable
- **結果**：表格 + 數字
- **結論**：✅ / ❌ + 一句話
- **學到**：失敗也是知識、寫 meta-finding

不刪實驗紀錄——下次想重做前先看上次怎麼掛的（避免重踩同樣的坑）。

## 工具索引

- [tools/strat-validate](../../tools/strat-validate/) — 廣域策略驗證（20+ 策略 × 20 幣 × 5 時框）
- [tools/param-stability](../../tools/param-stability/) — walk-forward 參數穩定度 + LS 引擎驗證
- [tools/shared/KlineCache.cs](../../tools/shared/KlineCache.cs) — 共享 Binance klines 快取（24h TTL）
