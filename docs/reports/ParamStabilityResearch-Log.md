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

---

## 2026-05-26 第二輪 — 修 grid bug + 補測 dual_thrust / bb_revert_ls

**修法**：`GenericWalkForwardOptimizer.Optimize` 加 optional `maxGrid` 參數（預設 `DefaultMaxGrid=400` 保留現行行為）；param-stability 工具顯式傳 `maxGrid: 6000` 讓 dual_thrust(5832)、bb_revert_ls(1296)能跑。

### 結果（10 個新組合）

| Strategy | Symbol | def OOS% | opt OOS% | 穩定度 | Verdict | 最常選參數 |
|---|---|---:|---:|---:|---|---|
| **dual_thrust** | **BNB** | **−52.5** | **+11.0** | 0.66 | ⭐ **robust** | `lookback=5, sma=80, k1=0.8, k2=0.5` |
| dual_thrust | ETH | 17.1 | **+48.6** | 0.56 | marginal | `lookback=15, sma=20, k1=0.4, k2=0.5` |
| dual_thrust | BTC | 14.2 | 3.6 | 0.69 | use-default | (預設已最佳) |
| dual_thrust | DOGE | −61.7 | −40.7 | 0.47 | no-edge | — |
| dual_thrust | LTC | 47.8 | −41.2 | 0.59 | no-edge | (調參反而虧) |
| ⭐⭐ **bb_revert_ls** | **DOGE** | **−55.3** | **+63.7** | **0.83** | **robust** | `period=10, sma=60, z=1.25` |
| **bb_revert_ls** | **BNB** | 43.3 | **+57.4** | 0.63 | ⭐ robust | `period=15, sma=70, z=1` |
| bb_revert_ls | BTC | 38.1 | 33.6 | **0.88** | use-default | (預設極穩、不換) |
| bb_revert_ls | ETH | −4.3 | −8.6 | 0.54 | no-edge | — |
| bb_revert_ls | LTC | 3.9 | −28.1 | 0.54 | no-edge | — |

### 兩輪累計（35 組合）

| Verdict | 第一輪 | 第二輪 | 合計 | % |
|---|---:|---:|---:|---:|
| `use-default` | 10 | 2 | 12 | 34% |
| `no-edge` | 11 | 4 | 15 | 43% |
| **`robust`** | 2 | 3 | **5** | **14%** |
| **`marginal`** | 1 | 1 | **2** | **6%** |
| ERROR | 10 | 0 | 0 | 0% |

→ **20% 真有調參邊際**（5 robust + 2 marginal）。其餘 80% 不該動參。

**5 個 robust pair（按發現順序）**：
1. ⭐⭐ **DOGE × bb_revert_ls**：def **−55.3%** → opt **+63.7%**、穩定度 **0.83** — **最強發現**，巨虧轉大賺
2. **LTC × rsi_stoch**：def 6.2% → opt 32.2%、穩定度 **0.88**
3. **BNB × bb_revert_ls**：def 43.3% → opt 57.4%、穩定度 0.63
4. **BNB × dual_thrust**：def **−52.5%** → opt **+11%**、穩定度 0.66
5. **BNB × fib_retrace_ls**：def −5% → opt 37.6%、穩定度 0.63

### 跨組合 pattern：BNB 是「需要客製化」的市場

BNB 跨 7 支策略：3 支 robust + 1 use-default + 3 no-edge：

| 策略 | def | opt | Verdict |
|---|---:|---:|---|
| dual_mom_ls | 14.3 | −0.6 | no-edge ❌ |
| ma_regime_trend | 35.4 | −21.0 | no-edge ❌ |
| mfi | 25.1 | 16.5 | use-default ✓ |
| rsi_stoch | 11.9 | −19.9 | no-edge ❌ |
| **dual_thrust** | −52.5 | +11.0 | ⭐ **robust** |
| **bb_revert_ls** | 43.3 | 57.4 | ⭐ **robust** |
| **fib_retrace_ls** | −5 | +37.6 | ⭐ **robust** |

**觀察**：portfolio.json 的 BNB 用 `dual_mom_ls`（no-edge），但 BNB **更適合均回 / 突破 / 回撤類**（bb_revert / dual_thrust / fib_retrace）。可能 BNB 的市場特性（震盪而非趨勢）讓純動量沒 edge、形態 / 均回類才有 edge。

**這是 portfolio.json 可能該重新評估 BNB 策略選擇的訊號**——不只是換參、是**換策略類別**。

### 結論 + 後續

- ✅ Tool bug 修了、5832 grid 跑得動
- ⭐ 新增 3 個 robust pair，**DOGE × bb_revert_ls** 是這兩輪最大發現
- ⭐ Cross-strategy pattern：**BNB 可能該換策略類別**
- 下一步：① 在 strat-validate 跑 LS 引擎驗證 robust pair 的最佳參數效果（確認 walk-forward optimizer 結果一致）② 評估 BNB 是否該換策略類別

**檔案**：[tools/param-stability/Program.cs](../../tools/param-stability/Program.cs) 加 `--all` 旗標（預設只跑前次 ERROR 兩支）。[GenericWalkForwardOptimizer.cs](../../packages/csharp/workers/strategy-worker/Engine/GenericWalkForwardOptimizer.cs) 加 `maxGrid` optional 參數（向後相容、所有現有測試不變）。
