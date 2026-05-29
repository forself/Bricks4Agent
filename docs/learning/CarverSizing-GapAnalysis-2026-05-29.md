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
- [x] **confidence 校準檢查** ✅ 做了(2026-05-29、strat-validate `--conf-diag`、10 支已驗策略 × 20 幣 full-sample):
  - 工具:`BacktestTrade.EntryConfidence`(進場當下 signal.Confidence)+ LS 引擎記錄;每筆 (conf, PnlPct) 算 Pearson/Spearman + conf 三分桶 avgPnl/勝率。
  - **① 範圍壓縮**:conf 全擠在 0.6-1.0(進場閘 ≥0.6 截斷);**mfi=0.70、rsi_stoch=0.80 吐常數 conf(min=max、零資訊)**。
  - **② 跨策略不可比**:conf 中位 spread = **0.17**(rsi_stoch 0.80 / smc 0.70 / ts_mom·di_trend 0.64)→ 各策略的「0.X」不等義、不能放同一尺度 scale 倉位。
  - **③ 預測力**:Pearson·Spearman 同號且皆>0.1 的只 **1/10**(bb_revert_ls、且 n=81 最小樣本);dual_mom +0.13/−0.13、di_trend +0.11/−0.02 = **兩者反號 = 離群驅動假關係、非單調預測**;桶趨勢多數非單調。
  - **結論**:confidence【不是校準 forecast】—— 經驗證實了上面「從推理」的歸因。**決定維持不開 confidence sizing**;唯一弱訊號 bb_revert(MR 策略:高 conf=偏離越大=回歸越多、合理但樣本薄)列為未來 per-strategy 深究、非現在。
- [ ] ~~confidence 校準檢查~~(已完成、見上)。原規劃的 per-strategy normalization:只有在「先把策略改成 emit 連續校準 forecast」(策略層重構、見下)之後才有意義,否則 normalize 一個零資訊/常數的數字無益。
- [x] **position buffering** ✅ 評估了(2026-05-29)= **不需要**:Carver buffering 解「連續重算目標→小幅漂移→頻繁交易」的 churn,但 B4A 是**固定倉位 + 訊號驅動**、沒有連續漂移的目標可 buffer。且既有防護已足:`r9 Symbol Cooldown`(active、60s、雙向)+ AutoTrader 30min re-open 冷卻 + scanner 冪等鎖 + 訊號驅動持有。**現在加 = 解不存在的問題 + 真錢引擎冗餘複雜度**。→ 只有未來開了 conf/Kelly 連續 sizing 才回來做。
- [ ] 讀 Carver Ch5-7 原文核對本摘要(本 doc 是從框架記憶寫的、非逐頁)。

## 不要做
- ❌ IDM 放大 gross(跟有效槓桿紀律衝突)
- ❌ max-Sharpe 配重(μ 敏感、strat-validate 已標註、用 risk-parity)
- ❌ 急著開 confidence/Kelly sizing 進真錢(先 backtest + shadow、循紀律)

---

## 深入筆記:Carver 的 forecast 怎麼建(2026-05-29、conf-sizing 負結果的根因)

今天 conf-sizing 實驗失敗的根因是「B4A confidence 不是校準 forecast」。這節挖深:Carver 的 forecast 到底是什麼、B4A 差在哪、要補什麼。

### Carver forecast recipe(Ch7)
1. **Raw forecast**:rule 的連續、帶號輸出(e.g. EWMAC = 快EMA − 慢EMA;breakout = 距通道 %)。**強度有意義、不是二元 buy/sell**。
2. **波動標準化**:raw ÷ 標的價格波動(σ_price)。→ forecast 變成「風險調整後的訊號強度」、跨標的可比。
3. **Forecast scalar**:× 一個常數,讓 forecast 的長期**平均絕對值 = 10**(Carver 慣例)。scalar = 10 / avg(|vol-std raw|)。
4. **Cap ±20**:極端訊號削頂(robustness、別在一個爆衝訊號 all-in)。
5. **組合多 rule**:capped forecasts 加權平均 × **FDM**(分散後 forecast 波動變低 → 乘回去補到平均 10、capped ~2.5)。
6. **部位**:= (combined forecast / 10) × vol-target 部位。→ forecast 10 = 滿倉、20 = 2×、5 = 半倉。**連續、校準、可比**。

### B4A 的 confidence 差在哪(為何 conf-sizing 沒用)
| Carver forecast | B4A confidence |
|---|---|
| 連續帶號、強度=部位 | [0,1] 量值 + 另外的 action 方向 |
| 波動標準化(跨標的可比) | 各策略各自啟發式、**未標準化**(ts_mom 0.7 ≠ harmonic 0.7) |
| scale 到平均絕對值 10(校準) | **未校準**(0.9 不等於「比 0.45 好兩倍」) |
| 範圍完整 ±20 | **被 ≥0.6 開倉閘截斷成 0.6-1.0** |

→ 拿一個「不可比、未校準、範圍壓縮」的數字 scale 倉位 = scale by noise。**今天 t-stat 不動就是這個**。

### 要把 confidence 變成真 forecast 要補什麼(未來工程、非現在)
1. 每策略 emit **連續帶號 forecast**(不只 action+confidence)。
2. **波動標準化**(÷ 價格 vol)。
3. **per-strategy forecast scalar**(校準各策略平均 |forecast| 到共同尺度)。
4. 才談 forecast-strength sizing —— 那時輸入校準了、才可能加值。
→ 這是**策略層重構**(動每支策略的輸出)、比 sizing flag 大得多。**列為未來項、不是現在**。

### 哲學分歧(刻意的):Carver lever-up vs B4A 存活優先
Carver 的精神:**分散是唯一免費午餐 → 用槓桿把分散組合放大到固定 vol 目標**(IDM)、同 vol 拿更多報酬。
B4A 刻意放棄這個:**名目壓 ~1× 權益、存活 > vol 目標**([[feedback_effective_leverage_is_real_risk]])。
→ 不是 B4A 漏做、是**有意識的 trade-off**:Carver 假設「不會在你需要時爆掉」、B4A 在 crypto 5x 強平環境選擇先活著。report 可寫成「對標 Carver、在槓桿哲學上 deliberate divergence」。

相關:[[QuantMatureRoadmap]] mental model #9「position sizing is alpha」、[[feedback_effective_leverage_is_real_risk]]、[[project_q2_portfolio_survival]]
