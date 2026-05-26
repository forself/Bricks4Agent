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
