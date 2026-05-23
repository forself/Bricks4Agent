# 現在能用的正收益組合（部署參考）

**更新**：2026-05-24　**驗證**：12 檔幣日線、`LongShortBacktestEngine`、walk-forward OOS(train250/test90/stride60)+ 全期連續回測
**基準**：Buy & Hold 中位報酬 40%、Sharpe 0.44、**maxDD 72%**

---

## TL;DR — 直接照這個部署

> **最佳風險調整組合 = 去相關精選 4 支、反波動率配重：**
>
> | 策略 | 角色 | 建議權重 |
> |------|------|---------:|
> | `dual_mom_ls` | 動量(主力) | **38%** |
> | `dual_thrust` | 突破 | **32%** |
> | `bb_revert_ls` | 均值回歸(去相關) | **19%** |
> | `fib_retrace_ls` | Fib 回撤(負相關對沖) | **10%** |
>
> **組合表現**：全期 Sharpe **0.62**、maxDD **46%**、OOS 平均 +4.8%/fold、+fold 59%。
> 部署方式：**4 支各自獨立持倉**(不是合成一個訊號)、依上表配資金。多空都開。

---

## 1. 三種可用組合（依需求選）

| 組合 | 全期Sharpe | maxDD | OOS avg | 適用 |
|------|-----------:|------:|--------:|------|
| A. 去相關精選 4 支(獨立·風險加權) | **0.62** | **46%** | 4.8% | 理論最佳(同 symbol 多部位、回測抽象) |
| B. 全部 12 支(風險加權) | 0.64 | **39%** | 4.1% | 要最大分散、不挑策略 |
| **C. `decorr4_ls` 一鍵(淨加權 + confidence-sizing)** | **0.59** | **51%** | 8.3% | **★實盤主推:單一 watch 可一鍵部署** |

**重要(交易所現實)**:真實交易所單一 symbol 只有「一個淨部位」,無法同時持 4 支的多/空當獨立部位。
但數學上「4 支加權組合在某 symbol 的損益」≡「持有 4 支加權後的淨曝險」損益。所以可部署形式 = **C:淨加權曝險 ensemble**
(action=淨曝險方向、部位大小∝|淨曝險|、分歧自動縮量)。配 `AUTOTRADER_CONFIDENCE_SIZING_ENABLED=true`,
C 的 Sharpe 0.59 / DD 51% 已逼近理論值 A(0.62/46%、差 ~5pp)→ **實盤直接用 C、一個 watch 搞定。**
(舊投票版 ensemble 是 0.50/61%、分歧退 hold 抹掉紅利,已改為淨加權版。)

---

## 2. 可用單腿清單（正期望、多空;依全期 Sharpe 排序）

| 策略 | 類型 | OOS中位% | 全期Sharpe | 全期maxDD% | 備註 |
|------|------|---------:|-----------:|-----------:|------|
| `dual_mom_ls` | 動量(雙時框) | 8.4 | 0.54 | 62 | 最強單腿 |
| `ma_regime_trend` | 趨勢(均線斜率) | 5.7 | 0.53 | 64 | |
| `accel_momentum` | 動量加速度 | 0.3 | 0.38 | 68 | |
| `chandelier_trend` | 突破(Donchian) | 0.6 | 0.36 | 66 | |
| `di_trend_ls` | 趨勢(ADX/DI) | 5.2 | 0.33 | 64 | |
| `fib_retrace_ls` | Fib 回撤 | 8.4 | 0.31 | 110 | **對趨勢負相關=最佳對沖腿**;單押回撤大、務必降權 |
| `ts_momentum` | 動量(vol-managed) | 7.5 | 0.24 | 67 | |
| `bb_revert_ls` | 均值回歸(z) | 1.0 | 0.22 | 79 | 對全部≈0 相關=分散腿 |
| `dual_thrust` | 突破(區間) | 8.6 | 0.20 | 73 | |
| `supertrend_ls` | 趨勢(SuperTrend) | −1.0 | 0.19 | 70 | OOS 持平、偏弱 |
| `donchian_fade_ls` | 震盪 fade | 0.7 | 0.11 | 102 | 單押回撤大、只放組合 |

> 去相關核心:`fib_retrace_ls`(對趨勢 −0.07~−0.32)與 `bb_revert_ls`(對全部 ≈0)是僅有的兩條非趨勢腿,組合降回撤靠它們。

---

## 3. 部署細則（真錢）

0. **一鍵部署(主推)**:用 `tools/deploy-decorr4.ps1` 把 `decorr4_ls`(淨加權 ensemble)佈到 12 個 symbol。
   預設 **shadow 模式**(只評估、不下單),先對帳數週確認 live≈回測,再加 `-Live` 真上線。
   ```powershell
   ./tools/deploy-decorr4.ps1 -Broker http://localhost:5100 -Token $env:B4A_TOKEN          # shadow
   ./tools/deploy-decorr4.ps1 -Broker https://your-broker -Token $env:B4A_TOKEN -Live      # 真上線(會二次確認)
   ```
   必開環境變數:`AUTOTRADER_CONFIDENCE_SIZING_ENABLED=true`(讓部位隨淨曝險強度縮放、分歧縮量 →
   復刻風險加權組合)、`AUTOTRADER_MIN_CONFIDENCE=0.55`。`decorr4_ls` 的反波動率權重已寫死在策略內
   (dual_mom 38/dual_thrust 32/bb_revert 19/fib 10),無需手動配重。
1. **(替代)手動獨立部署**:把 A 的 4 支當 4 個 watch 各配資金 —— 但同一 symbol 只能一個 watch,
   故此法需把 4 支分散到不同 symbol、無法在同一 symbol 復刻組合。**同一 symbol 的組合請用上面的 `decorr4_ls`。**
2. **全倉/cross-margin 提醒**:全倉不會把賠錢策略變賺錢、且賠起來整個帳戶一起。本清單已剔除所有負期望策略,但 `fib_retrace_ls`/`donchian_fade_ls` 單腿回撤 >100% → **絕不單押、只放組合且降權**。
3. **每筆風險上限**:務必開 `RISK_MAX_LOSS_PER_TRADE_PCT`,即使全倉也限制單筆最大虧損。
4. **多空**:這些都是原生多空(`LongShortBacktestEngine` 語意:buy→多、sell→空、hold→維持)。牛市可只開多側(long-only 趨勢腿也都正、見 batch-1 報告);要對沖空頭/做市場中性才全開多空。
5. **一鍵替代**:不想配 4 支就用 `decorr4_ls`(已註冊 worker)、接受回撤高 ~15pp。

---

## 4. 明確「不要用」的（負期望、已驗證）

| 策略/做法 | 為何不用 |
|-----------|----------|
| `harmonic_ls`(諧波) | OOS −6.4%、全期 −34%/Sharpe −0.09;正規諧波法+多空仍無 edge。已標 ⚠勿實盤。 |
| 諧波當趨勢過濾(`dualmom_harmonic_ls`) | 反向警示否決掉好單、全面變差(已刪) |
| 純逆勢均值回歸多空(無趨勢過濾) | 牛市做空被軋爆倉(報酬 −100~−150%、DD >180%) |
| 投票 ensemble 取代獨立組合 | 抹掉去相關紅利、回撤多 ~15pp(見 §1) |

---

## 5. 限制（誠實）

- 樣本期(2022–2025)偏多頭;多空的空頭腿在牛市是負擔,真正價值在空頭/盤整(可做市場中性)。
- OOS 已驗證(walk-forward),非單純全期回測好看;但加密 regime 變化大,上線後仍須持續對帳(shadow mode 先跑)。
- 更強的去相關需另類因子(資金費率 carry / 跨資產),屬後續。

---

## 6. 重現

```bash
dotnet run --project tools/strat-validate/StratValidate.csproj   # 多空可用性 + 相關 + 組合(等權/風險加權)+ 建議權重
```
相關報告:`NewQuantStrategies-2026-05-23.md`(第一批趨勢)、`LongShortQuantBatch-2026-05-24.md`(第二批多空)、`PatternStrategies-FibHarmonic-2026-05-24.md`(諧波/Fib)。
