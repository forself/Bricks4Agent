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
