# Carver《Systematic Trading》Ch5-7 sizing 框架 → B4A 實作 gap 分析

**寫於**:2026-05-29(Q1 portfolio 配重 milestone、roadmap §核心閱讀 #2)
**結論先講**:B4A 比預期更 Carver-aligned —— **sizing chain 的核心 primitives 大多已建好,但關鍵幾個預設關著或只是 recommendation**。Q1 的工作是「驗證 + 啟用」不是「從零建」。

---

## Carver 的 sizing chain(Ch5-7 精要)

Carver 的部位 = 一條「波動標準化」的管線,每一段都把風險拉回可比/可控:

1. **連續 forecast**(非二元):訊號波動標準化、scale 到期望絕對值 ~10、capped ±20。**訊號強度 → 部位大小**(強訊號大倉)。
2. **Forecast Diversification Multiplier (FDM)**:組合多條 rule 時、合成 forecast 波動較低 → 乘 FDM 放大(capped)。
3. **Volatility targeting**:部位 = forecast/10 × vol scalar,vol scalar = 目標風險 / 標的風險。整個系統盯一個年化 vol 目標。
4. **Instrument Diversification Multiplier (IDM)**:持多標的、組合波動 < 各部位和 → 乘 IDM 放大 gross(capped)。
5. **Instrument weights**:Carver 偏好 **handcrafting**(分組 risk-parity 的經驗法則)> mean-variance/max-Sharpe(μ 不可知、不穩、過擬合)。
6. **Position buffering**:目標部位變動在 ~10% 緩衝帶內**不交易** → 砍 churn / 成本。
7. **保守風險目標**:half-Kelly 或更低(參數不確定性下、full-Kelly 太激進)。

---

## 對照表:Carver → B4A

| Carver 概念 | B4A 現狀 | 缺口性質 |
|---|---|---|
| 連續 forecast → 部位 | ✅ **已建**:`AutoTraderSizingService` confidence multiplier(commit 2ed655f)`factor=max(floor 0.3, conf)`、qty×factor。**⚠ 預設關**(`AUTOTRADER_CONFIDENCE_SIZING_ENABLED=false`) | **啟用+驗證**,非建。且 confidence 跨策略未校準(見下) |
| FDM(rule 組合放大) | ⚠ 有 WeightedEnsemble 組合 rule、但無顯式 FDM 放大 | 小缺口、低優先 |
| Volatility targeting | ✅ Kelly recommendation 含 vol-scalar(target 60%、cap 2.0、低 vol regime ×2.0);live 路徑 `KellyPositionSizingService` | 有;但 live Kelly 也**預設關**(`AUTOTRADER_KELLY_SIZING_ENABLED=false`),且無「月度 rebalance」自動化 |
| IDM(組合放大 gross) | ❌ 無 | **刻意不做**(見下、跟有效槓桿紀律衝突) |
| Instrument weights | ✅ strat-validate 算 equal/inv-vol/min-var/**risk-parity**/max-Sharpe;risk-parity 為主、max-Sharpe 標註 μ 敏感 | 跟 Carver 一致(risk-parity ≈ handcrafting 精神、且已棄 max-Sharpe) |
| Position buffering | ⚠ 有「per-symbol 30min 開倉冷卻」(時間式 churn 防護)、無「目標部位緩衝帶」 | **真缺口**:rebalance 時無 buffer 帶、可能小變動就重交易 |
| 保守 Kelly | ✅ quarter-Kelly(比 Carver 的 half 更保守)+ max 20% clamp | 達標、甚至更保守 |

---

## 三個關鍵發現

1. **「forecast-strength sizing」不是沒做、是沒開。** confidence-aware sizing + Kelly 都建好了(pure function `ApplyAdaptiveSizing`、有 unit test),但 `AUTOTRADER_CONFIDENCE_SIZING_ENABLED` / `KELLY_SIZING_ENABLED` 都 `false`。現在真錢是 **固定 budget_pct**(訊號強弱不影響倉位大小)。→ Q1 任務 = **先 backtest 驗證 confidence-scaling 有沒有改善風險調整後報酬,再(shadow 後)開**。

2. **confidence 跨策略未校準 = forecast 不可比。** Carver 的 forecast 是波動標準化、跨標的可比的;B4A 各策略各自吐 confidence,ts_momentum 的 0.74 跟 harmonic 的 0.74 不保證等義。**就算開 confidence sizing、也要先確認 confidence 校準**(否則是拿不可比的數字 scale 倉位)。

3. **IDM 是刻意不做、不是漏做。** Carver 的 IDM 假設你「盯 vol 目標、用槓桿把分散組合放大到目標 vol」;B4A 的紀律是 **名目壓 ~1× 權益、不靠槓桿**(見 [[feedback_effective_leverage_is_real_risk]])。盲套 IDM = 鼓勵加 gross、跟存活優先衝突。**知道但不套**。

---

## Q1 可動的下一步(沒被 6/25 / Phase2 時間閘擋)

- [x] **backtest confidence-scaling** ✅ 做了(2026-05-29、strat-validate `--conf-sizing`、8 部署策略 A/B):
  - 結果:**mean 全面↓~35%、但 t-stat 幾乎不動**(harm 5.47→6.05、ts_mom 4.16→4.14、dual_mom 3.58→3.49…)。
  - 解讀:confidence-scaling 只是按比例縮曝險(vol 同比縮)、**risk-adj 報酬無改善**;沒選出更好的交易、只少賺。
  - 根因:B4A confidence(0.6-1.0、門檻 gate 副產品)**不是校準 forecast**。Carver forecast-strength sizing 要 work、forecast 必須 predictive。
  - **決定:不開 `CONFIDENCE_SIZING_ENABLED`**(會降報酬不升 Sharpe);固定倉位現狀是對的。要用先做 confidence 校準。
- [ ] **confidence 校準檢查**:抽幾支策略看 confidence 分布,確認跨策略可比(或加一層 per-strategy normalization)。
- [x] **position buffering** ✅ 評估了(2026-05-29)= **不需要**:Carver buffering 解「連續重算目標→小幅漂移→頻繁交易」的 churn,但 B4A 是**固定倉位 + 訊號驅動**、沒有連續漂移的目標可 buffer。且既有防護已足:`r9 Symbol Cooldown`(active、60s、雙向)+ AutoTrader 30min re-open 冷卻 + scanner 冪等鎖 + 訊號驅動持有。**現在加 = 解不存在的問題 + 真錢引擎冗餘複雜度**。→ 只有未來開了 conf/Kelly 連續 sizing 才回來做。
- [ ] 讀 Carver Ch5-7 原文核對本摘要(本 doc 是從框架記憶寫的、非逐頁)。

## 不要做
- ❌ IDM 放大 gross(跟有效槓桿紀律衝突)
- ❌ max-Sharpe 配重(μ 敏感、strat-validate 已標註、用 risk-parity)
- ❌ 急著開 confidence/Kelly sizing 進真錢(先 backtest + shadow、循紀律)

相關:[[QuantMatureRoadmap]] mental model #9「position sizing is alpha」、[[feedback_effective_leverage_is_real_risk]]、[[project_q2_portfolio_survival]]
