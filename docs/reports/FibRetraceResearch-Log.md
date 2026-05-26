# FibRetrace Research Log

`fib_retrace_ls` 是 decorr4 組合裡唯一非趨勢腿、跨時框 4/5 正、Sharpe 0.31——是組合改善最有槓桿的單一目標。研究目的：**保留 edge、降 DD（單腿 110% 是組合最大痛點）**。

baseline（[UsableCombos-2026-05-24.md](UsableCombos-2026-05-24.md)）：
- OOS 中位 8.4%、Sharpe 0.31、**DD 110%**、67% 檔正
- 跨時框中位 OOS%：1h=0、4h=1、**12h=−3**（唯一負）、1d=4、1w=13
- 與趨勢家族負相關 −0.07~−0.32（最佳對沖腿）

---

## 2026-05-26 H1-Fib — 加 RegimeDetector 趨勢確認

**假設**：fib_retrace_ls 自身的趨勢判定（`hi > li`、單看高低先後）**太鬆**，會把「微小高低差的橫盤」當成趨勢進場 → 接刀、DD 爆。RegimeDetector 用 SMA50 斜率 + ATR/BB 是更嚴格的「真趨勢」確認，應該過濾掉假趨勢、降 DD、保住 OOS edge。

**改動**：新策略 `fib_retrace_regime_ls` = `fib_retrace_ls` + RegimeDetector 閘門。`uptrend` 必須 `RegimeType.TrendingUp`，`downtrend` 必須 `TrendingDown`。其餘 regime（Squeeze / RangeBound / HighVol / Unclear）→ hold。

**成功標準**（與 harmonic H1 不同——這次有 prior edge）：
- DD 顯著降（baseline 110% → 目標 <80%）
- OOS 中位仍 > 0（即使打折也可接受）
- Sharpe 不顯著惡化（baseline 0.31 → 至少維持 0.25+）

**注意**：跟 harmonic H1 的差異——這次 prior 是「保留 edge + 降風險」而非「找出 edge」。即使 +fold% 降低也可能淨值得（DD 降幅大）。

**結果**（20 檔幣、walk-forward train250/test90/stride60、daily）：

| | OOSsym+ | OOSmed | +fold | fullRet | Sharpe | DD% | 判定 |
|---|---:|---:|---:|---:|---:|---:|:---:|
| **Long-only** | | | | | | | |
| `fib_retrace_ls`（baseline）| 70% | 4.5 | 36% | 48 | 0.28 | 59 | ✅ |
| `fib_retrace_regime_ls`（H1）| 20% | **0.0** | 5% | 12 | 0.11 | **17** | ❌ |
| **Long-short** | | | | | | | |
| `fib_retrace_ls`（baseline）| 70% | 7.5 | 56% | 66 | 0.29 | **96** | ✅ |
| `fib_retrace_regime_ls`（H1）| 25% | **0.0** | 10% | 0 | 0.06 | **43** | ❌ |

跨時框中位 OOS%：`fib_retrace_ls` 0/1/-3/4/13（4/5 正）vs `fib_retrace_regime_ls` 0/0/-2/0/0（0/5）。

**對成功標準**：
- ✅ DD < 80%（96 → 43、大幅降）
- ❌ OOS 中位 > 0（7.5 → **0.0**）
- ❌ Sharpe > 0.25（0.29 → **0.06**）

DD 真的降——**但 edge 也跟著被殺光**。

**結論**：❌ **假設不成立**（過 DD 標準、未過 edge 保留標準）。

**學到（跨實驗 meta-finding）**：

|  | DD 變化 | OOS edge 變化 |
|---|:---:|:---:|
| H1-harmonic（無 edge）| 133 → 23 ✅ | 一直是 0/負（沒救） |
| **H1-Fib（有 edge）**| 96 → 43 ✅ | **7.5 → 0**（殺死） |

**Filter 把策略等比例縮小**而非改善訊號質量。對沒 edge 的策略沒救、對有 edge 的策略殺 edge——**過濾路徑不適合**。

下一步要往「改善訊號質量」或「改善 trade management」方向，不要再加 filter：
- **核心痛點仍是 DD 96%**：根因是策略**沒有自帶 SL**（textbook Fib SL = 跌破 swing_low 失效）、engine 不認 Signal.StopPrice。
- 加 SL 是 H2-Fib 候選——但需要小工程（讓 engine 認 StopPrice、或變體策略追蹤狀態）、非單日實驗。
- 也可考慮 H3-Fib：confidence sizing（centerDist 已有訊號強度、可放大成 sizing 而不是 binary）。

**檔案**：[FibRetraceRegimeLsStrategy.cs](../../packages/csharp/workers/strategy-worker/Engine/FibRetraceRegimeLsStrategy.cs)、註冊於 strat-validate。Build 0 warnings。

---

## 2026-05-26 H2-Fib — 加 textbook Fib 失效停損（DD 真正修法）

**假設**：baseline fib_retrace_ls 沒自帶 SL → 趨勢一旦破策略不知道、抱單到反向訊號 → DD 失控（LS DD 96%）。改動：emit `Signal.StopPrice` = `swing_low × (1−0.5%)`(升勢) 或 `swing_high × (1+0.5%)`(跌勢)。**Engine 改動**：[LongShortBacktestEngine.cs](../../packages/csharp/workers/strategy-worker/Engine/LongShortBacktestEngine.cs) 加 SL 處理（mirror long-only engine 已有的邏輯）；不發 StopPrice 的策略 activeStopPrice=0、完全無回歸。

**檔案**：[FibRetraceSlLsStrategy.cs](../../packages/csharp/workers/strategy-worker/Engine/FibRetraceSlLsStrategy.cs)。註冊於 strat-validate。

### 結果（LS 引擎、20 檔幣 walk-forward）

| | fullRet% | Sharpe | DD% | OOSmed% | +fold% | 判定 |
|---|---:|---:|---:|---:|---:|:---:|
| `fib_retrace_ls`（baseline）| 68 | **0.29** | **96** | **7.6** | **57%** | ✅ |
| **`fib_retrace_sl_ls`（H2）** | **82** ⭐ | 0.26 | **67** ⭐ | 5.8 | 51% | ✅ |

**關鍵發現**：
- ✅ **fullRet 從 68 提升到 82**（+14%）— 反而更賺
- ✅ **DD 從 96 降到 67**（−29pp、-30% 風險）
- ⚠ Sharpe 微降（0.29→0.26）、OOSmed 微降（7.6→5.8）

**真正價值（meta-finding）**：
**SL 救的是「baseline 賠大錢的尾巴 fold」、不是 median fold**。所以 `fullRet ↑` 但 `OOSmed →`。SL 把 worst-case 從巨虧砍成小虧 → 整體更賺、但中位數沒變。

**報酬/DD 比**：
- baseline: 68/96 = **0.71**
- H2: 82/67 = **1.22**（**+72% 風險調整後改善**）

### Long-only 反向結果

| | fullRet% | Sharpe | DD% | 判定 |
|---|---:|---:|---:|:---:|
| baseline | 50 | 0.28 | 59 | ✅ |
| H2 SL | **0** | 0.10 | 54 | ❌ |

Long-only 下 SL 反而拖累——可能假突破觸發 SL 過早出場。**Long-only 不適合這個 SL 規則**、LS 才是它的場景。

### 跨時框

`fib_retrace_sl_ls`: 1h=0/4h=0/12h=-3/1d=2/1w=10、平均 1.8、3/5 正（baseline 3.0、4/5）。**LINK 上跨時框最佳**（H2=15%）、其他大致與 baseline 相當。

### 結論

✅ **H2-Fib SL 部分成功**：
- LS 引擎下：風險調整後勝（報酬/DD 比 +72%）
- baseline 在 OOSmed/Sharpe 略高、但 DD 96% 是實質約束
- H2 把 DD 砍到 67%、同時報酬還升 14%—— **可作為 fib 在「DD 控制重要」場景的替代版本**

⚠ **不是無條件 upgrade**：
- Sharpe 微降、median OOS 微降——非「全面更好」、是 trade-off
- portfolio.json 真錢部署若想用 H2，要先 shadow 模式比對 live ≈ 回測

**actionable**：
- DOGE/LTC 等 DD 敏感的 symbol，部署時可考慮 `fib_retrace_sl_ls` 替代 baseline
- 但 ETH 上是激進選 baseline / 保守選 ma_regime_trend、SL 版會吃 edge
- engine 改動已落地、不影響現有策略（往後其他策略也可發 StopPrice）

### Engine 改動（zero-regression）

`LongShortBacktestEngine.Run` 加：
1. `decimal activeStopPrice` 跟蹤當前持倉 SL
2. `OpenAt` 新增 `decimal stop` 參數、從 `signal.StopPrice` 取值
3. 主迴圈在 evaluate 後、equity 前：檢查 `bars[i].Low/High` 觸 SL → 在 SL 價平倉、設 `stoppedOutThisBar` flag 跳過本根新單

**不發 StopPrice 的策略 `activeStopPrice=0`、`position!=0 && activeStopPrice>0` 不成立 → 整段 skip → 無行為變化**。零回歸。


---

## 2026-05-26 H16-Fib — TP 觸發救活 fib_retrace_ls(換機 Claude 接手驗證)

**動機**:H16 commit(`2a66892`)讓 `LongShortBacktestEngine` 讀 `Signal.TargetPrice`。`FibRetraceLsStrategy` 早就在 signal 設 Fib 1.272 擴展為 TP,但 pre-H16 引擎完全忽略 → 該策略在 long-only pool 拿 t=0.25、判定失敗。換機後接手驗證 H16 是否翻案。

### 重跑配置
- 工具:`param-stability --validate-ltc-fib-robust` + `--validate-candidates`(post-H16 LS 引擎)
- 走 LongShortBacktestEngine.RunWalkForward(default params、commission 0.0005、slip 0.0003)

### LTC × fib_retrace_ls 跨時框 + 跨配置

跨時框(walk-forward 250/90/60):

| interval | OOSmed% | AvgSh | +folds | WinRate |
|---|---:|---:|---|---:|
| 1h | 0.2 | 0.46 | 7/12 | 58% |
| 4h | 0.3 | 0.16 | 6/12 | 47% |
| 12h | 3.7 | 0.35 | 8/12 | 56% |
| **1d** | **25.0** | **1.41** | **10/12** | **80.5%** |
| 1w | 33.0 | 1.28 | 2/2 | 83% |

跨 7 個 1d 配置(default params)Sharpe **1.07-1.45**、+folds 9-12/N、WinRate **71-90%** → 不是配置運氣。

對照 `rsi_stoch`(現 LTC 部署)同 7 配置:Sharpe 0.38-0.97 / WinRate 36-61% / DD **56-93%**(基本爆倉) → fib 全面壓制。

### ETH / LTC 換腿候選對比(--validate-candidates)

ETH(現 mfi def 4.4% 是 portfolio 最弱):

| 策略 | OOSmed% | Sharpe | DD% | WinRate |
|---|---:|---:|---:|---:|
| mfi(現)| 4.8 | 0.58 | 60.4 | 54% |
| ma_regime_trend(保守候選)| 8.9 | 0.84 | **46.9** | 52% |
| **fib_retrace_ls(激進候選)** | **13.8** | **0.97** | 85.9 | **68%** |

LTC(現 rsi_stoch mixed):

| 策略 | OOSmed% | Sharpe | DD% | WinRate |
|---|---:|---:|---:|---:|
| rsi_stoch(現)| 2.3 | 0.50 | 89.8 | 48% |
| **fib_retrace_ls** | **25.0** | **1.41** | **46.4** | **80.5%** |
| mfi | 0.0 | 0.54 | 27.3 | 41% |

### H16 前後 fib_retrace_ls 對比

| 指標 | Pre-H16(long-only pool)| Post-H16(LS+TP)|
|---|---|---|
| 整體 pool t-stat | **0.25**(失敗)| 未重跑(需把 strat-validate pool 改 LS) |
| ETH(default LS) | n/a | OOSmed **13.8** / Sharpe **0.97** |
| LTC(default LS) | n/a | OOSmed **25.0** / Sharpe **1.41** |
| Pool mean / CI | 0.4% / [−2.5, 3.4] | per-symbol 分化大、需重跑 pool |

### 結論(⚠ 翻案 fib_retrace_ls)

⭐ **H16 救了 fib_retrace_ls,但 edge 高度 symbol-dependent**:
- LTC 是全面壓倒(每欄都勝 rsi_stoch、DD 半砍)→ 若 LTC 進實盤,fib_retrace_ls 是首選
- ETH 是「激進選項」(報酬+勝率最高,DD 比 ma_regime 高 25pp)
- 跨時框只有 1d(+1w 有限樣本)有 edge —— worker 排程**必須鎖 1d**
- Pool 失敗是 pre-H16 引擎不讀 TP 的實作問題、不是訊號問題(再次驗證 [feedback_verify_implementation_first])

### Actionable

1. ⭐ **fib_retrace_ls 應該加進 shadow §6 配重**(目前未列、但 H16 後在 ETH/LTC 是最大受益者)。建議配重 5-10%、限定 ETH/LTC 主場
2. ETH 實盤換腿選項(已有資料):
   - 保守 ma_regime_trend(DD 46.9 最低、Sharpe 0.84)
   - 激進 fib_retrace_ls(Sharpe 0.97 / WinRate 68% 最高、DD 85.9 高)
3. LTC 不在實盤,結論僅供未來重組參考
4. **Pool t-stat 重跑待辦**:strat-validate 改用 LS 引擎才能在 pool 層看到 H16 影響(系統面待辦項)

### Meta

連續第二次驗證「結論前先驗實作」鐵則:`harmonic_ls` 是進場時機晚、`fib_retrace_ls` 是 TP 沒被引擎讀。**多 hypothesis 失敗未必是訊號問題、可能是實作問題**。每次「策略無 edge」結論落地前,先確認引擎/訊號實作是否完整 honor 策略 spec。
