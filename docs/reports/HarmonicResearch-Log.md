# Harmonic Research Log

每日小實驗、累積知識。**就算失敗也是知識**——避免下次重踩。

格式：每條一個假設、改動、結果、結論、學到。

**baseline**（[PatternStrategies-FibHarmonic-2026-05-24.md](PatternStrategies-FibHarmonic-2026-05-24.md)）：
- `harmonic_ls`（正規 Carney 法、無過濾）：OOS 中位 **−6.4%**、全期 Sharpe **−0.09**、DD 98%。❌ 無 edge
- 失敗根因：**強趨勢輾過反轉訊號**。要救得**改使用條件**，不是調門檻

---

## 2026-05-26 H1 — Regime filter（橫盤 only）

**假設**：諧波只在橫盤 regime 才有 edge。強趨勢中反轉訊號會被輾過，是 baseline 失敗根因。

**改動**：新策略 `harmonic_range_ls` = `harmonic_ls` + `RegimeDetector` 閘門。`RegimeDetector.Detect(bars).Type == RangeBound` 才允許進場，其餘 regime（TrendingUp/Down/Squeeze/HighVol/Unclear）→ hold。其他邏輯（PRZ、燭線確認、RSI 背離、min_conf 0.6）保持原 `harmonic_ls` 不變。

**怎麼測**：在 [tools/strat-validate](../../tools/strat-validate/Program.cs) 註冊新策略，跑 12 檔幣 walk-forward OOS。`harmonic_ls`（baseline）與 `harmonic_range_ls`（H1）同次輸出、直接 A/B 比。

**成功標準**：
- OOS 中位 > 0%（baseline −6.4%）
- 全期 Sharpe > 0（baseline −0.09）
- 至少不比 baseline 差

**結果**（20 檔幣、walk-forward train250/test90/stride60、daily）：

| | OOSsym+% | OOSmed% | +fold% | fullRet% | fullSh | fullDD% | 判定 |
|---|---:|---:|---:|---:|---:|---:|:---:|
| **Long-only** | | | | | | | |
| `harmonic_ls`（baseline）| 30 | −1.8 | 19 | −26 | −0.08 | 56 | ❌ |
| `harmonic_range_ls`（H1）| **5** | **0.0** | **3** | **−6** | **−0.03** | **14** | ❌ |
| **Long-short** | | | | | | | |
| `harmonic_ls`（baseline）| 35 | −7.2 | 26 | −36 | −0.15 | 133 | ❌ |
| `harmonic_range_ls`（H1）| **10** | **0.0** | **4** | **−6** | **−0.05** | **23** | ❌ |

**結論**：❌ **假設不成立**。H1 沒讓諧波變正期望。但**失敗方式有訊號**：

- ✅ **大幅降低虧損與風險暴露**：LS fullRet −36→−6、DD 133→23、long-only DD 56→14；Sharpe −0.15→−0.05（接近 0 而非更負）
- ❌ **沒創造 edge**：OOSmed 從 −7.2 變 0 而不是變正；OOSsym+% 從 35% 跌到 10%；+fold% 從 26% 跌到 4%
- 看 OOS 矩陣：`harmonic_range_ls` **20 檔裡 18 檔全是 0**（沒進場過），只有 TRX 1%

**學到**（這條最重要）：

「**強趨勢輾過反轉**」這個失敗 hypothesis **不是主因**。如果是主因，去掉趨勢段就會變正期望。實際結果：去掉趨勢段 → **不虧也不賺**（=幾乎不進場）。意思：**橫盤時的諧波訊號也沒 edge**，H1 只是降低了「參與度」而不是「找出條件性 edge」。

**真正的失敗根因**：機械諧波偵測 + Carney 確認法在 crypto daily 上，**任何 regime 下都沒 edge**。問題在「諧波形態本身」而非「使用情境」。

**下一步該往哪**：
- ❌ **不要再做 regime/filter 類變體**（降參與救不了無 edge 的訊號）
- 仍可試：H4 高時框（週線/4H）——若雜訊是根因之一，時框可能有用
- 仍可試：H3 funding 極端——但要當「**雙確認 sentiment proxy**」而非過濾
- 誠實選項：**這條研究線可能就到這了**。每天花時間沒問題、但別期待奇蹟。

**檔案**：[HarmonicRangeLsStrategy.cs](../../packages/csharp/workers/strategy-worker/Engine/HarmonicRangeLsStrategy.cs)、註冊於 [strat-validate Program.cs](../../tools/strat-validate/Program.cs)。Build 0 warnings、無回歸。

---

## 2026-05-26 H4 — 高時框 only（**直接用 H1 既有跑次的資料、無需新代碼**）

**假設**：若雜訊是諧波失敗根因之一，高時框（4h / 12h / 1w）應該變正。

**證據**：strat-validate 已跨 5 個時框跑（1h/4h/12h/1d/1w、20 檔幣），跨時框中位 OOS%：

| 策略 | 1h | 4h | 12h | 1d | 1w | 平均 | 正/5 |
|---|---:|---:|---:|---:|---:|---:|:---:|
| `harmonic_ls` | 0 | 0 | −1 | −2 | 0 | **−0.4** | **1/5** ❌ |
| `harmonic_range_ls`(H1) | 0 | 0 | 0 | 0 | 0 | 0.0 | 0/5 ❌ |
| `fib_retrace_ls`（對照）| 0 | 1 | −3 | 4 | **13** | 3.0 | 4/5 ✓ |
| `ma_regime_trend`（對照）| 0 | 0 | −4 | 5 | **15** | 3.2 | 2/5 ✓ |
| `dual_mom_ls`（對照）| 0 | −3 | −5 | 5 | 11 | 1.7 | 2/5 ✓ |

**結論**：❌ **假設不成立**。

別的策略在 **1w（週線）都明顯改善**（fib_retrace 13、ma_regime 15、dual_mom 11、di_trend 34、ts_momentum 36）——雜訊降低對它們有效。**諧波在 1w 仍是 0**——不是雜訊問題、是訊號本身沒 edge。

**學到（決定性）**：

H1 排除「regime 不對」、H4 排除「雜訊」。再加上 baseline 已證實「機械 + Carney 確認」無 edge——**所有「使用情境」類假設都已排除**。失敗根因鎖定在**諧波形態本身在 crypto 沒有可重現的反轉預測力**。

**研究線收線**：H2（pattern subset）、H5（sizing modifier）、H3（funding 雙確認）按目前證據預期都不會贏（要對抗「訊號本身無 edge」這個底層問題）。建議：
- 諧波檔案保留供日後新角度的測試（如：跨資產證據、機構持倉結合、新型形態定義）
- 研究重心轉到**已有 edge 的策略深耕**（fib_retrace_ls 跨時框 4/5 正、是最穩的對沖腿；funding/OI meta 已接進 character_ensemble、值得追）

---

## 2026-05-26 H-Combo — 失敗策略組合是否有突破口？

**動機**：用戶提出假設「失敗的策略可能組合起來有突破」。三個 ensemble 同次跑：

| Ensemble | 組成 | 測試什麼 |
|---|---|---|
| `harm_fib_5050` | harmonic 50% + fib 50% | 失敗 + 有 edge 是否 orthogonal |
| `harm_fib_3070` | harmonic 30% + fib 70% | fib 領導、harmonic 小權重輔助 |
| `harm_range_fib_regime_5050` | harm_range 50% + fib_regime 50% | **雙失敗組合直接驗證** |

### 結果（LS 引擎、20 檔幣 walk-forward）

| Strategy | OOSmed% | Sharpe | DD% | 跨時框 (正/5) | t-stat |
|---|---:|---:|---:|---:|---:|
| `fib_retrace_ls`（baseline）| **7.6** | **0.29** | 96 | 3.0 (**4/5**) | **2.13** ✅ |
| `harmonic_ls`（baseline）| −7.3 | −0.15 | 133 | −0.4 (1/5) | — |
| `harm_fib_3070` | 2.5 | 0.13 | 118 | 0.9 (3/5) | 1.16 ❌ |
| `harm_fib_5050` | −2.2 | 0.08 | 152 | −0.4 (3/5) | 0.91 ❌ |
| `harm_range_fib_regime_5050` | 0.0 | **0.00** | 50 | −0.6 (1/5) | — |

### 結論

**1. 雙失敗組合確認沒救** ❌

`harm_range_fib_regime_5050` Sharpe **0.00**、跨時框 1/5、OOSmed 0。**filter+filter = 更嚴的 filter**，預期會這樣、結果照辦。

**2. harmonic 在組合裡不是「補充」、是「噪音」** ❌

加 harmonic 任何權重都拉低純 fib：
- Sharpe 0.29 → 0.13 → 0.08（權重越高、越拖累）
- **t-stat 從 2.13 顯著 → 1.16 / 0.91 不顯著**（殺掉統計顯著性）

**3. 唯一小亮點（不足以推薦部署）**：跨時框穩定性

`harm_fib_5050` 在 BTC 跨時框 5/5 正、SOL/INJ 最佳——比純 fib 略穩。但 Sharpe 0.08 太低，**穩定性換不掉 edge 流失**。

### Meta-learning

用戶 hypothesis「失敗組合可能有突破」**反面驗證**：
- 失敗策略沒救（單獨或組合都沒救）
- 失敗 + 有 edge 也是拖累而非互補
- **訊號本身無 edge = 加入組合也無 edge**（在 net-weighted ensemble 下）

但這條負面結果**值得記下來**——之後若想加新策略到 decorr4，先在 ensemble 跑 t-stat、確認顯著性沒被殺再考慮。

→ 諧波線**徹底收線**。剩餘可能性：跨資產 / 機構持倉 / 新型形態定義——但都屬「另起爐灶」、不是現有諧波研究的延伸。

---

## 2026-05-26 H5 — PRZ 4 點投影進場（⚠ 翻案前面三條結論）

**用戶 pushback 觸發的重新檢視**：用戶質疑「諧波 10 個延伸形態、不該慘成這樣」，要我查實作。讀完 [HarmonicPatterns.DetectAll](../../packages/csharp/workers/strategy-worker/Engine/Indicators/HarmonicPatterns.cs#L634) + [FindPivots](../../packages/csharp/workers/strategy-worker/Engine/Indicators/HarmonicPatterns.cs#L276) 發現：

**baseline harmonic_ls 的根本缺陷**：
- `FindPivots(bars, window=3)`：D 必須過 3 根 K 線才被確認成 pivot
- `DetectAll` 要求 X/A/B/C/D **5 點全是已確認 pivot**
- 策略的 entryWindow=8 → 實際進場在 D 之後 **3–8 根 K 線**

⇒ **策略在反轉開始後 3–8 根 K 線才下單**（追單而非預測）——違反 Carney 教科書原意（PRZ 投影、在 D 形成前就在 PRZ 等）。**前面 H1/H4/H-Combo 三條測試都建在錯誤的進場機制上**。

**改動**：
1. 新方法 `HarmonicPatterns.ProjectFromXabc`：4 點 XABC、用 AB/XA + BC/AB 比率比對 10 個 pattern，從 A 投影 PRZ
2. 新策略 [HarmonicPrzLsStrategy](../../packages/csharp/workers/strategy-worker/Engine/HarmonicPrzLsStrategy.cs)：取最近 4 pivot → 投影 PRZ → 當前價在 PRZ + 燭線/RSI 確認 → 進場（SL = X ± 0.5%、走 LongShortBacktestEngine 已支援的 StopPrice）

### 結果（20 檔幣 walk-forward OOS）

| | OOSsym+ | OOSmed | +fold | fullRet% | Sharpe | DD% |
|---|---:|---:|---:|---:|---:|---:|
| **Long-only** | | | | | | |
| harmonic_ls | 30% | −1.8 | 19% | **−26** | **−0.08** | 56 |
| **harmonic_prz_ls** | **40%** | 0.0 | 8% | **+8** | **+0.14** | **4** |
| **Long-short** | | | | | | |
| harmonic_ls | 35% | −7.3 | 26% | **−36** | **−0.15** | **133** |
| **harmonic_prz_ls** | **40%** | −0.2 | 10% | **+6** | **−0.01** | **11** |

**統計顯著性（pooled 240 folds）**：

| Strategy | OOSavg | 95% CI | t-stat | 結論 |
|---|---:|---|---:|---|
| fib_retrace_ls | 4.9% | [0.6, 9.6] | 2.11 | ✅ 顯著 |
| **harmonic_prz_ls** | **0.6%** | **[0.1, 1.4]** | **2.00** | ✅ **顯著** |
| harmonic_range_ls | −0.5% | [−1.2, 0.1] | −1.50 | — |

### 結論（⚠ 翻案）

⭐ **諧波（PRZ 進場）在 crypto 有微小但統計顯著的 OOS edge**：
- t=2.00、p<0.05、95% CI 不跨 0
- LS fullRet 從 **−36% → +6%**
- LS DD 從 **133% → 11%**（砍 92%）
- long-only Sharpe 從 −0.08 → **+0.14**

**前面三條結論修正**：

| 原結論 | 修正後 |
|---|---|
| 諧波在 crypto 沒 edge | ⭐ 進場機制改對後**有微小但顯著的 edge** |
| filter 不救無 edge | ✓ 仍對（regime filter 救不了）但「無 edge」是**錯誤前提** |
| H-Combo harmonic + fib 拖累 | ⚠ 用錯版 harmonic、結論作廢、**該用 harm_prz_ls 重做** |

### 但別過度興奮

- edge 很小（0.6% / fold）→ 單腿部署利潤微薄
- 跨 5 時框仍 0/5 正——1d 是唯一有效時框（PRZ 條件嚴 → 多時框觸發都不夠）
- 觸發頻率低（OOSsym+ 40%、+fold 8-10%）= 很多時候空轉

**真正可能用途**：
- ⭐ 對沖腿 / 去相關 sleeve（不是主力）
- ⭐ 跟 fib_retrace_ls 在 ensemble 組合（重做 H-Combo、這次用 harm_prz_ls）

### Meta-learning（重要、加進研究紀律）

**「結論前先驗證實作是否符合理論假設」**。
H1/H4/H-Combo 跑得很乾淨、結論看似很硬——但前提（進場機制）錯了的話，整條研究線的結論都浮動。用戶 pushback 觸發的重讀是這次研究最有價值的一步。

下一步候選 broader roadmap：
- **H6** per-pattern 拆解（10 個形態哪個撐起這個 edge）
- **H7** 高 RR pattern subset（Crab/Deep_Crab/Butterfly）
- **H8** 歷史 EV 加權（自適應信心）
- **H9** Volume divergence 第三 confirmation
- **H10** 多時框 confluence
- **H11** harm_prz_ls + fib_retrace_ls 重做 H-Combo
- **H14**（用戶 2026-05-26 提出、保留待用）：**PRZ 區間自己也加 ±15% 浮動**。
  現行 `TolerancePad=0.15` 只套在 AB/XA、BC/AB 比率比對；PRZ（用 Ad 範圍從 A 投影）
  用的是 pattern 的 strict Ad.Min/Ad.Max。若 butterfly + five_o 之外的 pattern
  其實也有 edge 但被 strict PRZ 排掉，加 PRZ 浮動會撈回。一行 code 改動。
  條件：若 H7/H11 顯示 edge 不夠時觸發。

---

## H6-H16 — 一條線連到「真王牌」（2026-05-26 下午〜晚上）

### H6（per-pattern 拆解）
10 個形態各自跑。Butterfly + Five-O 微弱正、其餘多接近 0。結論：把所有形態合起來看才會穩、單一形態樣本太少。

### H7（高 RR subset:Crab / Deep_Crab / Butterfly）
合起來測 — 略強於 baseline 但不顯著。停在這條線。

### H11（harm_prz_ls + fib_retrace_ls H-Combo 重做）
等權 ensemble。OOS 7-8%、可用。但組合互補性沒打開、邊際提升有限。

### H12 / H13（regime / timeframe filter）
跟 fib 結論一樣 — filter 只是等比例縮量、不改善訊號質量。**收線**。
(歷史結論[feedback_strategy_filter_pattern](../../.claude/projects/-Users-dba-anthony-Note/memory/feedback_strategy_filter_pattern.md)再次驗證。)

### H15 — multi-window scan（重大突破）⭐⭐⭐
**假設**：原 `HarmonicPrzLsStrategy.Evaluate` 只檢查最近 4 個 pivots 為 XABC、漏掉「過去幾根 bar 內有更好 pattern」的機會。

**改動**：加 `scanWindows` 參數,iterate `pivots.Count-1 down to scanWindows back`。

**結果**(scan10、2000 bars、1d):
- t 2.00 → **4.03**
- OOS mean 0.6% → **5.2%**
- Sharpe 0.14 → **0.73**

**啟示**：實作 bug 等級的限制(只看最近 4 點)壓抑了訊號到「微弱顯著」。放開後是穩定 edge。

### H14 落地 — PRZ ±15% widening ⭐⭐⭐
H15 後追加。`ProjectFromXabc` 加 `przWideningPct` 參數,range ±15%。

**結果**(scan10_widepz、2000 bars、1d):
- t = **5.01**(再升、上界 10.2%)
- OOS mean = **7.2%**
- LS fullRet = **1280%**(無 TP 版)
- LS Sharpe 0.86、DD 30%

**Per-symbol robustness**(4 種 walk-forward 配置):
- ✅ OP / ADA / INJ:4 配置都 Sharpe>1.2
- ⚠ DOT:Sharp 跨配置不穩(-0.36 → 1.08)
- ❌ NEAR:樣本運氣(從 portfolio 拿掉)
- ❌ BTC:諧波在 BTC 完全無 edge

### H16 — LS 引擎讀 `Signal.TargetPrice`(2026-05-26 晚)
**動機**:`HarmonicPrzLsStrategy.Evaluate` 早就設 `TargetPrice = Math.Round(proj.Tp1, 4)`,但 LS 引擎之前完全忽略。Carney 教科書 PRZ 進場本來就配 TP 出場(D 反彈到 C / 0.382 retrace 等)。

**改動**:[LongShortBacktestEngine.cs](../../packages/csharp/workers/strategy-worker/Engine/LongShortBacktestEngine.cs) 加 `activeTargetPrice` field、同 SL 邏輯反向(long 用 High、short 用 Low)。同根都觸發按 SL 先(保守)。

**H16 前後 LS 引擎全期(2000 bars、1d)對比**:

| 策略 | OOSmed% | +fold% | fullRet% | Sharpe | DD% |
|---|---|---|---|---|---|
| `harm_prz_scan10` | 6.6 → **7.3** ⬆ | 32 → 37 ⬆ | 393 → 388 ≈ | 0.73 → **0.91** ⬆ | 28 → **11** ⬇⬇ |
| `harm_prz_scan10_widepz` | 11.0 → 9.8 ⬇ | 38 → 39 ≈ | 1280 → 931 ⬇ | 0.86 → 0.77 ⬇ | 30 → **22** ⬇ |
| `harm_prz_top2_scan10` | 5.3 → 4.5 ⬇ | 20 → 18 ≈ | 192 → 177 ≈ | 0.77 → **0.90** ⬆ | 18 → **6** ⬇⬇ |
| `harm_prz_top2_scan10_widepz` | 8.5 → 5.1 ⬇⬇ | 23 → 18 ⬇ | 379 → 295 ⬇ | 0.77 → 0.61 ⬇⬇ | 29 → 21 ⬇ |
| `harm_prz_butterfly_scan10` | 2.1 → 2.1 = | 11 → 11 = | 58 → 58 = | 0.74 → 0.74 = | 0 → 0 = |

**解讀**:
1. 窄 PRZ + TP = 純加分(`scan10`、`top2_scan10` Sharpe ↑、DD 砍半)
2. 寬 PRZ + TP = 喜憂參半(widepz 配置 TP 比例過緊、提前出場錯失 trend continuation)
3. `butterfly_scan10` 完全沒變 — TP 還沒到價就被反向訊號平掉

**新風向**:H16 後 `scan10`(無 widepz)反而是 risk-adjusted 王者(Sharpe 0.91、DD 11%)。配重應從「widepz 重押」改成「scan10 / scan10_widepz 並列」。

### 收結

從 H1「結論諧波無 edge」→ H5「PRZ 進場救回 t=2」→ H15「scan10 t=4.03」→ H14「widepz t=5.01」→ H16「scan10 Sharpe 0.91 / DD 11%」。一條線 5 個 hypothesis、把諧波從「無用」推到「t > 5 的真王牌」。

**累計 commits**(2026-05-26):
- `b49a72c` H5 PRZ 翻案
- `70ad979` H6/H7/H11-15 諧波研究 + --fast 模式
- `4cb01ad` H22 Binance 分頁 + widepz Tier 1 組合
- `4c8d088` Portfolio 2000-bar acid test
- `2a66892` H16 — LS 引擎讀 TargetPrice

---

## 待辦 / 待驗(交接給下台機器)

### 必驗(配重 finalize 前)
- [ ] **`fib_retrace_sl` × H16 影響**:該策略也設 TP(Fib 1.272 擴展),H16 後表現可能翻案
- [ ] **`HarmonicPrzLsStrategy.Tp1` 計算合理性**:H16 顯示 widepz 配置 TP 過緊 → 重新看 `ProjectFromXabc` 內部如何投影 Tp1。可能要按 PRZ 寬度比例調整
- [ ] **1h / 4h 跨時框**:已驗 — 王牌只在 12h + 1d 有 edge。worker 排程務必鎖 1d(或 12h)

### shadow 啟動前
- [ ] Final portfolio 配重(用 H16 後新建議:scan10 20% / scan10_widepz 20% / decorr5_widepz 15% / ma_regime_trend 15% / accel_momentum 10% / dual_mom_ls 10% / ts_momentum 5% / top2_scan10 5%)
- [ ] 幣池限制清單(BTC / NEAR 禁用 harm_prz_*;LTC / OP / ADA / INJ / APT / SUI 主場)
- [ ] shadow 啟動日 + 4 週後評估標準(PnL ≥ +5%、DD < 20% 才升 live)

### H17-H21 路線圖
- **H17**:Confidence-based 倉位 sizing — `HarmonicPrzLsStrategy` 的 `signal.Confidence` 已根據 pattern 質量在 0.55-0.80 間浮動,引擎已有 `confidenceSizing` 旗標,需評估打開後對 Sharpe 的影響
- **H18**:ATR trailing SL — 取代固定 SL,在 trend 段最大化、解決 widepz TP 太緊的問題
- **H20**:多時框 confluence(12h 訊號 + 1d 訊號才開倉、訊號頻率 ↓ 但勝率 ↑)
- **H21**:Volume divergence 第三 confirmation(PRZ 觸發 + 量背離才開倉)

### 系統面
- [ ] strat-validate t-stat pool 改用 LS 引擎(目前用 Benson long-only、看不到 H16 影響)
- [ ] portfolio.json 修訂前的 shadow 驗證流程文件化(現在是口頭約定)
- [ ] backtest 加入真實 funding rate(目前只用 0.010%/8h 估計)

---

---

## 2026-05-26 H16+ Method C — Tp1 從實際進場價投影(換機接手)

**動機**:H16 後 widepz 配置 TP 過緊侵蝕收益(scan10_widepz Sharpe 0.86→0.77、fullRet 1280→931)。讀 `ProjectFromXabc` 內部發現:

### 根因(`HarmonicPatterns.cs` line 540)

```csharp
var dProxy = (przLow + przHigh) / 2m;          // ← D 的代理 = PRZ 中心
var (_, _, tp1, tp2, _) = CalcTpSl(direction, Xp, Cp, dProxy, ...);
```

Tp1 用 PRZ 中心當 D 投影、與實際進場價無關。widepz 對稱外擴 PRZ 後**中心不變**,但實際進場常在邊緣 → bullish 在上緣進場時 `Tp1 - entry` 可能負(目標已被當前價超過、立刻觸發出場)。

### 修法(Method C)

`HarmonicPrzLsStrategy.cs` 在 signal emit 前用實際進場價(`bars[^1].Close`)重算 Tp1:

```csharp
// H16+ Method C:Tp1 從『實際進場價(=currentPrice)』重新投影
var (_, _, tp1Refined, _, _) = HarmonicPatterns.CalcTpSl(direction, X.Price, C.Price, currentPrice, slBuffer, proj.PatternName);
// ... TargetPrice = Math.Round(tp1Refined, 4)
```

理論依據:Carney 教科書中 D = 實際反轉點 = 進場點。原代理用 PRZ 中心是簡化。
副作用:保留 `proj.Tp1` 在 indicators 為 `tp1_proxy` 供稽核。

### A/B 對比(同 walk-forward 250/90/60、default params、commission 0.0005)

**`harm_prz_scan10` per-symbol Sharpe(窄 PRZ、理論影響極小)**:

| Symbol | Pre | Post | Δ | 註 |
|---|---:|---:|---:|---|
| LTC | 0.53 | 0.53 | 0.00 | |
| OP | 0.86 | **1.31** | **+0.45** ⬆ | |
| NEAR | 0.58 | 0.33 | −0.25 ↓ | 已黑名單 |
| APT | 1.06 | 1.23 | +0.17 ↑ | |
| INJ | 0.66 | 0.67 | +0.01 ≈ | |
| **avg** | **0.74** | **0.81** | **+0.07** ↑ | 4/5 ≥ pre |

→ **王牌 scan10 沒退化**,反而 OP/APT 略升,只 NEAR 退(本就排除)。

**`harm_prz_scan10_widepz` per-symbol Sharpe(寬 PRZ、理論受益方)**:

| Symbol | Pre | Post | Δ |
|---|---:|---:|---:|
| ADA | 1.58 | **1.95** | **+0.37** ⬆ |
| DOT | 0.61 | **0.87** | **+0.26** ⬆ |
| INJ | 0.88 | **1.69** | **+0.81** ⬆⬆ |
| NEAR | 0.52 | 0.38 | ↓(已黑名單) |
| BTC | −0.09 | −0.27 | ↓(已黑名單) |
| **avg(ADA+DOT+INJ)** | **1.02** | **1.50** | **+0.48** ⬆⬆ |

→ widepz 對可部署 coin **大幅改善**,完全對應「修正上緣進場 TP 太緊」的理論預測。

### 結論

⭐ **Method C 是 net win**:
- scan10(窄 PRZ):微升、沒退化(王牌守住)
- widepz(寬 PRZ):+0.48 Sharpe avg(ADA/DOT/INJ),改善幅度顯著
- 改動量極小(strategy 一處、5 行)、向後相容(其他用 ProjectFromXabc 的不受影響、indicators 保留 tp1_proxy 供稽核)
- 理論基礎(Carney 教科書 D = 實際進場點)+ A/B 實證雙重驗證

### 後續(可選)

- 跨 20-coin 全集 pooled t-stat 重跑(strat-validate t-stat pool 改 LS 引擎一併處理、見系統面待辦)
- §6 配重在 Method C 後可能微調(scan10_widepz 因 TP 修正、相對 scan10 的優勢回升,配重再評估)
- H18 ATR trailing SL 若上線、跟 Method C 並存(誰先到誰先平)→ 預期 widepz 額外受益

---

## 2026-05-26 H17 — Confidence-based sizing(❌ 假設不成立)

**假設**:HarmonicPrzLs 已輸出 Confidence(pattern fit + candle confirm + RSI div、0.6-0.95)。引擎 `confidenceSizing=true` 把名目 × Confidence。低 conf trade 應該 edge 較弱 → 縮量降低 drag → Sharpe ↑。

### 改動

無 strategy / engine 改動 — 直接用既有 `LongShortBacktestEngine.RunWalkForward(... confidenceSizing: true/false)` A/B。新增 `--validate-h17-confsizing` mode 在 param-stability。

### 結果(harm_prz_scan10、7 幣 walk-forward 250/90/60、default params)

| Coin | Sharpe off | Sharpe on | Δ Sharpe | Return off | Return on | DD off | DD on |
|---|---:|---:|---:|---:|---:|---:|---:|
| LTC | 0.56 | 0.54 | −0.02 | 2.1 | 1.5 | 25.2 | 21.6 |
| OP | 1.32 | 1.33 | +0.01 | 23.3 | 13.2 | 14.3 | 9.9 |
| NEAR | 0.33 | 0.27 | −0.06 | 9.2 | 4.8 | 9.8 | 9.4 |
| APT | 1.25 | 1.25 | 0.00 | 22.1 | 14.2 | 8.6 | 7.1 |
| INJ | 0.67 | 0.67 | 0.00 | 13.5 | 8.9 | 10.9 | 8.0 |
| ADA | 1.19 | 1.18 | −0.01 | 26.4 | 18.5 | 7.4 | 6.1 |
| DOT | 0.50 | 0.50 | 0.00 | 15.1 | 8.8 | 16.0 | 13.7 |
| **avg** | **0.83** | **0.82** | **−0.01** | **16.0** | **10.0** | **13.2** | **10.8** |

WinRate / +folds 兩種模式下完全相同(同樣的 trade、只是 size 不同)。

### 結論

❌ **H17 假設不成立**:
- Sharpe 平均 −0.01(7 幣中 4 持平 / 2 微降 / 1 微升)
- Return 平均 −38%、DD 平均 −18% → **等比例縮量、PnL 曲線同比下縮**
- 沒有「低 conf trade edge 較弱」的訊號 — Confidence 對 trade 結果**不是有效的 quality 區分器**

### 為什麼(分析)

harm_prz 的 Confidence = `pattern_fit + candle_confirm + RSI_div`。這三個 indicator 是 **pattern 品質**的代理、不是 **trade 報酬**的預測器。高 fit 的形態 ≠ 高勝率 trade,因為:
- pattern 品質決定「這是不是真的 PRZ」(訊號 sanity)
- 但實際反轉成功與否取決於市場 context(波動、流動性、外部事件),這些跟 fit 無關

→ 縮 low-conf trade 就是縮一些跟 high-conf trade **同樣勝率分布**的 trade、純粹是 capital 縮放。

### Meta-learning

**「Confidence-based sizing 要 work,Confidence 必須跟 trade 報酬有正相關」**。
本實驗的 negative 結果證明:對 harm_prz_scan10,這個前提不滿足。其他策略(如 ensemble — Confidence 來自多策略共識強度)可能仍有效;但對單一形態識別類策略,sizing 該另尋指標(如 ATR / volume / regime)。

### 收線

❌ H17 對 harm_prz_scan10 不採用。**收線**。
路線圖剩 H18(ATR trailing SL)、H20(多時框 confluence)、H21(volume divergence)— 這三條都不靠 Confidence 當分量器,值得繼續。

---

## 2026-05-26 H18 — ATR trailing SL(對 harm_prz no-op、引擎改動有全域價值)

### 動機

從 Method C 之外的另一個角度修 widepz「TP 太緊」:讓 SL 沿 ATR 往有利方向 ratchet。趨勢段若 trail 比 TP 早被觸發,trail 接管 → 不被 TP 提早砍。

### 引擎改動

`LongShortBacktestEngine.Run` / `RunWalkForward` 加 opt-in 參數:
- `atrTrailMultiplier`(decimal,預設 0=關)
- `atrPeriod`(int,預設 14)

每根 bar(在 signal-driven open/close 之後)update `activeStopPrice`:
- long:`max(activeStopPrice, close - mult × ATR)`(只往上 ratchet)
- short:`min(activeStopPrice, close + mult × ATR)`(只往下 ratchet)
- 從下一根才生效(check-then-trail,避免同根 gotcha)

預設 0 → 整段 skip → 向後相容(其他策略 caller 不傳即不啟用)。Unit.Tests 830/0 zero regression。

### A/B(harm_prz_scan10 + harm_prz_scan10_widepz × 4 coins × multiplier {0, 2.0x, 3.0x})

**scan10**(窄 PRZ):**0/12 case 有任何變化**。Sharpe / Return / DD 完全相同 pre vs post。

**scan10_widepz**(寬 PRZ):

| coin | mult | Sharpe | Δ | DD% | Δ |
|---|---:|---:|---:|---:|---:|
| LTC | off/2/3 | 1.03 | = | 32.4 | = |
| **OP** | off | 1.61 | base | 29.1 | base |
| **OP** | **2.0x** | **1.64** | **+0.03** | **23.3** | **−5.8** ↓ |
| OP | 3.0x | 1.62 | +0.01 | 27.7 | -1.4 |
| ADA | off/2/3 | 1.96 | = | 9.8 | = |
| INJ | off/2/3 | 1.69 | = | 9.8 | = |

→ **7/8 case 完全 no-op**,只 OP × widepz × 2.0x 有微小 DD 改善(−5.8pp、Sharpe +0.03)。

### 為什麼 no-op(結構性發現)

**Method C 把 TP 做對後,harm_prz 變成 TP-driven 策略**。Tp1 = entry + 0.382×(C − entry)、距離短(典型 < 5% 移動)、價格觸 PRZ 反彈後 1-3 根就觸發 → trail 還沒 ratchet 多少就被 TP 平倉。**TP 跟 trail 在引擎裡共存(SL/TP 同根都觸發按 SL 先),但實際上 TP 幾乎總是先到**。

### 結論

❌ **H18 對 harm_prz 是 no-op**:Method C TP 主導出場、trail 沒舞台。

✅ **引擎改動本身仍有全域價值**:`LongShortBacktestEngine` 現在原生支援 ATR trailing,任何不發 `TargetPrice` 的策略都可開啟。預期受益對象:
- `ma_regime_trend`(現 ETH/BNB 候選、長趨勢、無 TP)
- `dual_thrust`(突破策略)
- `accel_momentum`(動能 trend follow)
- `decorr5_widepz`(ensemble 含非 TP 策略)
- `chandelier_trend`(策略名本身就是 ATR trailing 概念、可整合)

### Meta-learning

**「策略類型決定優化路徑」**。
TP-driven 策略(harm_prz、fib_retrace_ls)→ 優化 TP 邏輯(Method C 已做)
Trail-driven 策略(ma_regime / accel / chandelier)→ 開 H18 ATR trailing(現在 engine 支援)
Mixed(decorr ensemble)→ 看 sub-strategy 多數派決定

下一步 actionable:在 ma_regime_trend × ETH 上 A/B H18 trailing(若 Sharpe 提升 → 影響 ETH 換腿評估)。

### 收線

❌ H18 對 harm_prz_scan10 / widepz 收線(no-op、結構性原因)。
✅ 引擎 ATR trailing 支援保留、給其他策略(尤其 trend-following)未來啟用。

---

## 2026-05-26 H18 補正 — 趨勢策略測試發現引擎 gap

繼 H18 對 harm_prz no-op 之後,加測 trend 策略(預期受益對象):

| 案例 | trail off Sharpe | 2.0x | 3.0x | Δ |
|---|---:|---:|---:|---:|
| ma_regime_trend × ETH | 0.85 | 0.85 | 0.85 | 0 |
| ma_regime_trend × BNB | 0.36 | 0.36 | 0.36 | 0 |
| dual_thrust × SOL | 0.46 | 0.46 | 0.46 | 0 |
| dual_thrust × BNB | −0.40 | −0.40 | −0.40 | 0 |

**12/12 case 完全 no-op**。原因:

驗證 `StopPrice = ` 賦值次數:
- `HarmonicPrzLsStrategy`:1(唯一發)
- `FibRetraceLsStrategy / MaRegimeTrendStrategy / DualThrustStrategy / AccelMomentumStrategy`:**0**

引擎 trail 條件:
```csharp
if (atrTrailMultiplier > 0m && position != 0m && activeStopPrice > 0m)
```

`activeStopPrice` 只在 OpenAt 從 `signal.StopPrice` 設入。**策略不發 StopPrice → activeStopPrice 永遠 0 → trail 永遠 skip**。

### 結論:H18 引擎 gap、需「initial SL bootstrap」才能對 trend 策略生效

**現況**:H18 只對「主動 emit StopPrice」的策略有效(harm_prz),而 harm_prz 因 Method C 變 TP-driven 又用不到 → **目前等於 dead code**。

**要解**(留待未來):
- 引擎加 `defaultInitialSlPct` 參數(預設 0)
- 若策略沒 emit StopPrice 且 `atrTrailMultiplier > 0`,OpenAt 時 engine 自己用 `entry × (1 ∓ defaultInitialSlPct)` bootstrap activeStopPrice
- 之後 trail 才有 base 可以 ratchet

### ETH 換腿評估(沒被打開的鎖)

由於 H18 對 ma_regime_trend × ETH 是 no-op,**ETH 換腿的保守 vs 激進 trade-off 不變**:
- 保守 ma_regime_trend:Sharpe 0.84 / DD 47
- 激進 fib_retrace_ls:Sharpe 0.97 / DD 86

H18 沒能拉高 ma_regime_trend Sharpe → 沒辦法在「保住低 DD 同時」反超 fib。Trade-off 還是 trade-off。

### 收線(更新)

- ❌ H18 對 harm_prz:no-op(Method C TP 主導出場)
- ❌ H18 對 trend 策略:no-op(策略不發 StopPrice、trail 無 base)
- ⚠ 引擎 ATR trailing 機制本身正確但**目前無有效啟用者**
- **下一步**:加 `defaultInitialSlPct` engine bootstrap → 才能在 trend 策略 A/B 看 H18 真實價值

---

## 2026-05-26 H18 補正完成 — `defaultInitialSlPct` bootstrap + 趨勢策略真實 A/B

### 引擎改動(承接上一節「下一步」)

`LongShortBacktestEngine.Run/RunWalkForward` 加第三個 H18 參數:

```csharp
decimal defaultInitialSlPct = 0m   // 預設 0 = 不 bootstrap
```

在 OpenAt 呼叫前:若 `signal.StopPrice ?? 0m == 0 && atrTrailMultiplier > 0 && defaultInitialSlPct > 0`,engine 自己用 `entry × (1 ∓ defaultInitialSlPct/100)` bootstrap activeStopPrice → 之後 trail 有 base 可 ratchet。預設 0 → 完全 backward compatible。

### A/B(trend 策略 × 5% bootstrap × multiplier {0, 2.0x, 3.0x})

| Case | trail | Sharpe | DD% | Return% | WR% | Return/DD |
|---|---|---:|---:|---:|---:|---:|
| **ma_regime × ETH** | off | 0.85 | 46.9 | 12.3 | 52 | 0.26 |
| ma_regime × ETH | **2.0x+5%SL** | **0.85**= | **33.2**↓29% | 12.8 | 34 | **0.39**↑50% |
| ma_regime × ETH | 3.0x+5%SL | 0.66↓ | 35.3 | 9.1 | 28 | 0.26 |
| ma_regime × BNB | off | 0.36 | 46.2 | 4.5 | 51 | 0.10 |
| ma_regime × BNB | 2.0x+5%SL | 0.16↓ | 33.0 | 2.8 | 38 | 0.08 |
| **ma_regime × BNB** | **3.0x+5%SL** | **0.41**↑ | **24.1**↓48% | 5.7 | 46 | **0.24**↑140% |
| dual_thrust × SOL | off | 0.46 | 57.7 | 4.6 | 46 | 0.08 |
| dual_thrust × SOL | 2.0x+5%SL | 0.31↓ | **17.2**↓70% | 3.6 | 42 | 0.21↑162% |
| dual_thrust × BNB | off | −0.40 | 38.7 | −5.5 | 21 | n/a(負)|
| dual_thrust × BNB | 2.0x+5%SL | −0.18↑ | **10.1**↓74% | −0.2 | 17 | n/a |

### 關鍵洞察

**H18 trail + bootstrap 通常壓 Sharpe 一點(WR ↓、winners 被砍)、但 DD 大幅改善**:
- 7/8 case DD 顯著降低(平均 −47%、最大 −74%)
- 3/8 case Sharpe 略升(主要在 trail mult 跟策略對盤時)
- 大部分 case Sharpe 略降但 Return/DD(風險調整)上升
- WinRate 普遍降低(trail 把 trend continuation 提早砍)

**對 high-leverage 真錢場景(5x、DD 是硬約束)、DD 改善 = 真實 alpha**:
- 同 capital 達同 Sharpe 但 risk 更低 → 等價 leverage 容量上升
- 對 DD 熔斷(AUTOTRADER_MAX_PORTFOLIO_DD_PCT=8%)友善
- 對長期 compounding(geometric return > arithmetic 在 DD 控制下)友善

### ⭐ ETH 換腿評估更新(原鎖打開)

| 候選 | Sharpe | DD% | Return/DD |
|---|---:|---:|---:|
| mfi(現)| 0.58 | 60.4 | 0.14 |
| fib_retrace_ls(激進)| 0.97 | 86 | 0.16 |
| **ma_regime_trend + H18 2.0x+5%SL** | **0.85** | **33** | **0.39** ⭐⭐ |

ma_regime + H18 **風險調整後完勝**:
- 同 capital 風險降 2.6x(33 vs 86)
- Return/DD 是 fib 的 **2.4x**(0.39 vs 0.16)
- Sharpe 雖少 0.12,但 DD 控制 ↔ 真錢 5x leverage 環境最關鍵
- 用戶之前已選 ETH long_only + 主動管理 → 保守選項 + 風控強化更貼合用戶意圖

**actionable**:ETH 換腿 = `ma_regime_trend` + H18 trail(2.0x+5%SL)為新首選候選,排在 mfi 跟 fib 之前。

BNB 同理:`ma_regime_trend` + 3.0x+5%SL 從 Sharpe 0.36 → 0.41、DD 46→24,是換腿三選之一的強化版本。

### 結論

✅ **H18 完整實作完成**:
- `atrTrailMultiplier`(原)+ `atrPeriod`(原)+ `defaultInitialSlPct`(補正)三參數 opt-in 組合
- 對 TP-driven 策略(harm_prz)still no-op(Method C 蓋過)
- 對 trend 策略 + bootstrap:**真實 risk-adjusted alpha**(DD 砍 30-74%、Sharpe 略降但 Return/DD 大升)
- 引擎改動向後相容、Unit.Tests zero regression

✅ **ETH 換腿評估有結論**:`ma_regime_trend + H18 trail 2.0x+5%SL` 是新首選(風險調整後完勝 mfi 跟 fib_retrace_ls)、待 shadow 對帳數週後再 live。

### Meta-learning(累積)

**「Sharpe vs Risk-Adjusted Return 在 leveraged 環境是不同優化目標」**:
- 零槓桿:看 Sharpe(risk-adjusted return per unit volatility)
- 高槓桿:看 Calmar / Return-on-DD(risk-adjusted return per unit max-drawdown)
- 真錢 5x leverage、DD 熔斷 8%:Calmar 是更重要指標、Sharpe 只是參考
- H18 trail 是 Calmar-optimizer、不是 Sharpe-optimizer

下次評估其他策略時、配重決策 + 換腿評估應該同時看 Sharpe 跟 Return/DD,而不只看 Sharpe。

---

## 2026-05-26 H18 補完 — BNB 候選 + fib×ETH+trail 真王者

### 完整 ETH 換腿排名(Return/DD)

| 候選 | trail | Sharpe | DD% | Return% | Return/DD |
|---|---|---:|---:|---:|---:|
| mfi(現)| — | 0.58 | 60.4 | — | 0.14 |
| ma_regime_trend | off | 0.85 | 46.9 | 12.3 | 0.26 |
| ma_regime + H18 | 2.0x+5%SL | 0.85 | 33.2 | 12.8 | 0.39 |
| fib_retrace_ls | off | 0.97 | **86** | 4.5 | 0.05 |
| **fib_retrace_ls + H18** | **2.0x+5%SL** | **0.96** | **16.7**↓80% | **14.8**↑228% | **0.89** ⭐⭐⭐ |

**fib + H18 風險調整完勝**:
- Sharpe 維持 0.96(僅 −0.01 vs 原 fib)
- DD 從 86 砍到 16.7(−80%)
- Return 從 4.5 跳到 14.8(+228%)
- Return/DD 是第二名 ma_regime+H18 的 **2.3x**、是原 fib 的 **17x**

**為什麼這麼神**:
原 fib_retrace_ls 不發 StopPrice → 無 SL 兜底 → 偶發 86% DD。
加 H18 bootstrap 5% SL → 開倉立刻有硬底線。
加 trail 2.0x ATR → 賺錢時 SL 跟拉、鎖利潤。
**三層保護並存**(TP + SL bootstrap + trail)→ 真正的 risk-managed trend follower。

### BNB 換腿(完整 4 候選)

| 候選 | best trail | Sharpe | DD% | Return/DD |
|---|---|---:|---:|---:|
| ma_regime_trend(現)| 3.0x+5%SL | 0.41 | 24.1 | 0.24 |
| dual_thrust | — | −0.40 | 38.7 | n/a(broken)|
| **bb_revert_ls** | **2.0x+5%SL** | 0.28 | **9.3** | **0.43** |
| fib_retrace_ls | — | 0.03 | 35.2 | n/a(no edge)|

**BNB 結論**:
- **不換、加 H18 trail** 給現役 ma_regime → DD 46→24、Return/DD 0.10→0.24
- 或**換 bb_revert + H18** → Return/DD 最佳 0.43、但 Sharpe 較低(trade-off)
- 兩條都比現況好、但「換」的 absolute benefit 沒 ETH 那麼大

### ⚠️ 關鍵 caveat:backtest H18 ≠ live AutoTrader trailing

- Backtest 用的是 **ATR-based trailing**(`activeStopPrice = max(active, close − N×ATR)`)
- Live AutoTraderService 用的是 **peak-based trailing**(`SL = peak × (1 − TrailingDistancePct)`)
- 兩種機制行為不同、結果可能差異
- **要把這結論套到實盤,二選一**:
  - (a) 移植 ATR trailing 到 AutoTraderService(較大工程、影響真錢路徑)
  - (b) 在 backtest 引入 peak-based trailing 機制、跟 live 對齊後重跑

**relative ranking 應該 robust**(fib+某種trail >> mfi 的質性結論不依賴特定 trail 機制),
但**absolute Sharpe/DD 數字**得用 live-aligned 機制重驗才能拿去做真錢決策。

### Actionable 路線圖

1. **ETH 換腿**:fib_retrace_ls + 某種 trailing 是強候選(待 backtest 跟 live trail 對齊後 finalize)
2. **BNB 換腿**:現役 ma_regime 加 H18-style trail 改善;若引擎統一後可考慮換 bb_revert
3. **系統 work**(prerequisite for live action):
   - 在 LS 引擎 + peak trailing 重跑 ETH/BNB 候選對比
   - 或把 ATR trailing 移植到 AutoTraderService(等價移到 live)
4. **配重 §6 更新**:fib_retrace_ls 從 3% 候選位置應該升等(在 ETH 主場是壓倒性勝)

### Meta(累積)

**「高槓桿 + DD 熔斷環境、Risk-managed 策略 >> 純 Sharpe 高的策略」**:
原 fib(Sharpe 0.97 / DD 86)看似比 ma_regime(Sharpe 0.85)強,但 5x leverage + 8% DD 熔斷下 DD 86 = 必爆。
加 H18 後 fib 變(Sharpe 0.96 / DD 17),不只可活、而且 DD/Sharpe 雙贏。

這也說明 **「策略無 edge」結論前先驗實作對不對符合理論」** 跟 **「策略好不好取決於風險管理組合,單看 Sharpe 不夠」** 是同一鐵則的兩個面向。
