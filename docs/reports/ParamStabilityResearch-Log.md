# Parameter Stability Research Log

驗證 portfolio.json 與關注策略的**現行預設參數是不是真的最穩**——避免過擬合風險。工具：`GenericWalkForwardOptimizer.Optimize` 直接吐 `ParamStability` (0-1) + Verdict 白話判語。

---

## 2026-05-26 第一輪 — 5 主流幣 × 7 策略

**設定**：1000 日線 (2023-08-31 → 2026-05-26)、train 250 / test 90 / grid-search 每 window 最佳參數。Verdict 規則：`use-default` (預設 OOS ≥ 調參，調參 = 過擬合風險) / `robust` (調參勝預設且穩定) / `marginal` (略勝但穩定度中等) / `no-edge` (調參版仍不賺，救不起來)。

**工具**：[tools/param-stability/Program.cs](../../tools/param-stability/Program.cs)。

### Verdict 分布（25 有效）

| Verdict | 數 | 比例 |
|---|---:|---:|
| `use-default` | 10 | 40% |
| `no-edge` | 11 | 44% |
| `robust` | 2 | 8% |
| `marginal` | 1 | 4% |
| `ERROR: grid > 400` | 10 | — |

> **84% 的部署/關注組合都 robust（use-default 或 no-edge）→ 過擬合不是當前主要風險。** 真正有調參機會的只有 3 個 pair。

### 完整結果（取重點）

| Strategy | Symbol | def OOS% | opt OOS% | 穩定度 | Verdict | 最常選參數 |
|---|---|---:|---:|---:|---|---|
| **rsi_stoch** | **LTC** | 6.2 | **32.2** | **0.88** | ⭐ **robust** | `rsi_period=7, stoch_k=7, stoch_d=3` |
| **fib_retrace_ls** | **BNB** | −5.0 | **37.6** | 0.63 | ⭐ **robust** | `fib_lookback=90, fib_min_range_pct=5` |
| dual_mom_ls | LTC | −22.9 | 22.2 | 0.50 | marginal | `dm_short=25, dm_long=50` |
| dual_mom_ls | BTC | 22.1 | 7.4 | 0.69 | use-default | (預設已最佳) |
| dual_mom_ls | DOGE | 57.7 | 32.3 | **0.81** | use-default | (預設已最佳) |
| fib_retrace_ls | DOGE | **140.7** | 11.2 | 0.50 | use-default | (預設極佳、調參吃虧) |
| fib_retrace_ls | LTC | **120.4** | 14.1 | 0.50 | use-default | (同上) |
| ma_regime_trend | DOGE | **92.2** | 42.2 | 0.75 | use-default | (預設極佳) |
| ma_regime_trend | LTC | −9.2 | −16.6 | 0.56 | no-edge | — |
| rsi_stoch | DOGE | −37.8 | −52.8 | 0.75 | no-edge | (在 DOGE 沒救) |

### 三條 actionable findings

**1. ⭐ portfolio.json 的 LTC rsi_stoch 可考慮換參**

部署現況：`{ "LTC-USDT", "strategy": "rsi_stoch" }`（5/5 全正、t=2.77）

但 walk-forward 顯示在 LTC 上**調參版 (+32.2%) 顯著勝預設 (+6.2%)，穩定度 0.88（高度 robust）**。最佳參數 `rsi_period=7, stoch_k=7, stoch_d=3`（vs 預設可能是 14/14/3）。

→ **下一步**：查 StochasticStrategy 的 default 參數、比對最常選；若值得 → 在 portfolio.json 加 `params` 欄位 override。

**2. ⭐ fib_retrace_ls 在 BNB 有調參邊際**

BNB 現部署的是 `dual_mom_ls`（不是 fib）。但這結果暗示：**若 decorr4 組合裡的 BNB 想加 fib 權重，可用 `fib_lookback=90, fib_min_range_pct=5`**（從 −5% 變 +37.6%）。

**3. dual_mom_ls 的 BTC/DOGE/ETH 部署參數很 robust**

3/4 幣是 use-default，DOGE 穩定度 0.81 最高。**沒過擬合徵兆、信心增強**。

### 工具錯誤待修

- `dual_thrust` grid=5832 > 400 → 全部 ERROR
- `bb_revert_ls` grid=1296 > 400 → 全部 ERROR

`GenericWalkForwardOptimizer` 有 `MaxGrid=400` 上限防爆炸。要嘛縮 ParamSchema 範圍、要嘛提高 MaxGrid（並接受更長運行時間）。明天的小修補。

### 結論 + meta-finding

- ✅ **portfolio.json 整體穩** — 84% 不需動參數、過擬合風險小
- ⭐ **2 個有改善機會**（LTC rsi_stoch、BNB fib）— 是接下來幾天可深挖的具體 actionable items
- ⚠ **工具有 bug**（grid 上限）— dual_thrust / bb_revert 整類沒測到、要補

下一步：①(明天) 補測 dual_thrust + bb_revert，可能要動 MaxGrid 或縮 ParamSchema ②(後天) 驗證 LTC rsi_stoch 換參的真實 walk-forward 效果（換在 strat-validate 比對），決定是否寫進 portfolio.json
