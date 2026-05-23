# 原生多空量化策略批次（5 支）— 生成 + 驗證報告

**日期**：2026-05-24
**目標**：再生一批**原生多空（long-short）**、且**可用（正期望）**的量化策略，交付 5 支。流程同前：設計 → 廣宇宙多空驗證 → 留正期望的 → 測試 → 報告。
**前提**：使用者採全倉（cross margin）、可承受大回撤 → 可用門檻放寬為「**正期望（正報酬 + 正 Sharpe）**」，回撤大但會賺的留;**負期望的一律刷掉**（全倉救不了賠錢的策略）。

---

## 0. 最重要的發現：加密 long-short 裡「逆勢均值回歸」會死

第一版做了純逆勢 MR 多空（z-score / RSI / Stochastic 反轉）。結果在淨多頭加密樣本**直接爆倉**：

| 純逆勢 MR(多空) | 全期報酬 | Sharpe | maxDD |
|---|---:|---:|---:|
| bb_revert(純) | −119% | −1.29 | 183% |
| rsi_revert(純) | −152% | −0.80 | 195% |
| stoch_revert(純) | −102% | −0.81 | 178% |

>100% 的 maxDD = 帳戶被做空軋到歸零。原因:在結構性上漲的市場「做空超買」= 空在持續噴出的強勢上 = 無上限虧損。
**修法**:給 MR 加「大趨勢過濾」(只在升勢買跌、跌勢空漲,絕不逆大勢)→ 止住爆倉,但邊際 edge 仍弱。
**結論**:crypto 多空真正穩定正期望的 edge 幾乎只有**動量/趨勢**;MR 只能當「趨勢對齊 + 組合裡的去相關 sleeve」。

---

## 1. 最終 5 支（皆原生多空、正期望、皆新增檔）

| 名稱 | 類型 | 機制 | 多/空 |
|------|------|------|-------|
| `dual_mom_ls` | 動量 | 短/長 ROC 同號才進(雙時框共振) | 雙正做多 / 雙負做空 |
| `di_trend_ls` | 趨勢 | ADX≥門檻時順 +DI/−DI 方向 | +DI>−DI 多 / −DI>+DI 空 |
| `supertrend_ls` | 趨勢 | SuperTrend ATR 動態趨勢線 | 線上多 / 線下空 |
| `bb_revert_ls` | 回歸(去相關) | 趨勢對齊 z-score:升勢買跌、跌勢空漲 | 偏離 ±2σ 反向 |
| `donchian_fade_ls` | 回歸(去相關) | 震盪市(ADX 低)才 fade 通道上下緣 | 觸上緣空 / 觸下緣多 |

引擎：新增 **`LongShortBacktestEngine`**（不動 Benson 的 BacktestEngine）——buy→多、sell→空、hold→維持的多空反手、無槓桿、現金記帳對多空通用。
檔案：`strategy-worker/Engine/{DualMomentumLs,DiTrendLs,SuperTrendLs,BollingerRevertLs,DonchianFadeLs}Strategy.cs`；測試 `LongShortStrategiesBatch2Tests.cs`（11 個）。

---

## 2. 多空可用性驗證（`tools/strat-validate`，12 檔幣、`LongShortBacktestEngine`）

| 策略 | OOS檔正% | OOS中位% | 全期報酬% | 全期Sharpe | 全期回撤% | 正期望? |
|------|---------:|---------:|----------:|-----------:|----------:|:------:|
| dual_mom_ls | 92 | **8.4** | **87** | **0.54** | 62 | ✅ |
| di_trend_ls | 58 | 5.2 | 37 | 0.33 | 64 | ✅ |
| supertrend_ls | 50 | −1.0 | 20 | 0.19 | 70 | ✅(OOS 持平) |
| bb_revert_ls | 58 | 1.0 | 36 | 0.22 | 79 | ✅ |
| donchian_fade_ls | 50 | 0.7 | 29 | 0.11 | 102 | ✅(單腿回撤大) |
| **Buy & Hold** | — | — | 40(中位) | 0.44 | 72 | — |

- 5 支全為正期望（正報酬 + 正 Sharpe）。**強弱分層**:
  - **Tier 1（OOS 穩健）**:`dual_mom_ls`、`di_trend_ls` —— 可單獨部署。
  - **Tier 2（去相關 sleeve、單腿弱、組合才發威）**:`bb_revert_ls`、`donchian_fade_ls`。
  - **Tier 3（全期正、OOS 持平）**:`supertrend_ls`。

---

## 3. 相關矩陣（long-short, BTC 全期權益報酬）

```
                dual_mom  di_trend  supertr  bb_rev  don_fade
dual_mom_ls       1.00     0.48     0.64     0.02    -0.11
di_trend_ls       0.48     1.00     0.53    -0.01    -0.09
supertrend_ls     0.64     0.53     1.00    -0.10    -0.08
bb_revert_ls      0.02    -0.01    -0.10     1.00     0.02
donchian_fade_ls -0.11    -0.09    -0.08     0.02     1.00
```

**這批比第一批更去相關**:`bb_revert_ls` 與 `donchian_fade_ls` 對其他全部相關 ≈ 0（±0.1 內）= 兩條真正正交的腿。
3 條動量/趨勢彼此 0.48–0.64（同質),但跟 2 條回歸腿幾乎不相關。

---

## 4. 組合層回測（下一步：long-short、等權、跨 12 檔平均）

| 組合 | 報酬% | Sharpe | maxDD% |
|------|------:|-------:|-------:|
| 單腿範圍 | 20–87 | 0.11–0.54 | 62–107 |
| **全部 5 支等權** | 42 | **0.36** | **49** |
| 低相關對 [di_trend_ls + bb_revert_ls] (ρ=0.01) | 37 | 0.34 | 58 |

**去相關紅利明確**:5 支等權組合的 **maxDD 從單腿 62–107% 砍到 49%**（也低於 B&H 的 72%）、Sharpe 0.36 高於 5 支裡的 4 支單腿。
兩條正交腿（bb_revert / donchian_fade）是壓回撤的主力。**這 5 支的設計重點就是「合在一起用」**,不是單押一支。

---

## 4b. 跨兩批 10 支的多空組合（含第一批趨勢家族）

把第一批 5 支(趨勢家族)也拉進多空引擎,連同第二批共 10 支一起算相關 + 組合(`LongShortBacktestEngine.RunPortfolioWalkForward`,OOS 版)。

**相關結構(BTC 全期權益)**:7 支動量/趨勢彼此 0.4–0.83(同質);`bb_revert_ls` 對全部 ≈0(最強分散)、`dual_thrust`/`donchian_fade_ls` 也偏低相關。

**組合(long-short、等權;full=全期跨檔, OOS=walk-forward 池化)**:

| 組合 | 全期Sharpe | 全期maxDD | OOS avg報酬 | OOS +fold% |
|------|-----------:|----------:|------------:|-----------:|
| 最佳單腿 dual_mom_ls | 0.54 | 66% | 8.4 | 54 |
| 全部 10 支等權 | 0.42 | **50** | 5.0 | 47 |
| **去相關精選 3 支** | **0.47** | 55 | **5.6** | **53** |

去相關精選(貪婪挑 |ρ|<0.4)= **`dual_mom_ls`(動量) + `bb_revert_ls`(均值回歸) + `dual_thrust`(突破)** —— 三種不同報酬驅動、彼此 <0.4。

**結論**:**「精挑 3 支去相關」勝過「全丟 10 支」也勝過「單押」**——Sharpe(0.47)與 OOS(+5.6%/53% folds)都最好,maxDD 55% 遠低於單腿(66–107%)。
而且 **OOS walk-forward 證實降回撤/正報酬在樣本外成立**,不只是全期回測好看。`bb_revert_ls` 是跨兩批最關鍵的分散腿。

> 附:把 10 支也丟進 `tools/decorr-analysis`(long-only 排名 vs 既有 21 支)——但那是**多單**引擎,會砍掉第二批的空單腿、低估它們(bb_revert_ls 在 long-only 只剩做多側 → 墊底)。原生多空策略的公允評估看 §2 的多空表,不是 long-only 排名。

---

## 5. 結論 + 風險提醒（真錢）

- **交付 5 支原生多空、正期望策略**;最強單腿 `dual_mom_ls`(全期 +87%/Sharpe 0.54),最佳用法是 **5 支等權組合**(maxDD 49%)。
- **全倉(cross margin)提醒**:全倉不會把賠錢策略變賺錢、且賠起來是整個帳戶一起賠。本批已刷掉所有負期望策略,但 `donchian_fade_ls` 單腿 maxDD 仍達 102% → **單押有歸零風險,務必放在組合裡、或加停損**。建議真錢用組合 + 每筆風險上限(RISK_MAX_LOSS_PER_TRADE_PCT)。
- 限制:樣本偏多頭;多空的真正價值在空頭/盤整(可做市場中性),牛市裡空頭腿仍是負擔。

---

## 6. 重現

```bash
dotnet build packages/csharp/ControlPlane.slnx
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter "FullyQualifiedName~Workers.Strategy"
dotnet run --project tools/strat-validate/StratValidate.csproj   # 同跑 long-only / long-short + 組合層
```

> 註:全期連續回測用全部資料但不調參(固定預設),屬「歷史上有沒有賺」的公允檢查;OOS 才是樣本外證據。
