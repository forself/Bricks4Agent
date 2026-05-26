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

