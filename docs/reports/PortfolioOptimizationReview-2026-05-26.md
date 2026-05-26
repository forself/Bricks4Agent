# Portfolio.json 優化空間評估（2026-05-26）

**資料來源**：[ParamStabilityResearch-Log.md](ParamStabilityResearch-Log.md) — 35 個 (strategy × symbol) walk-forward OOS（rounds 1+2）+ 5 個 LS 引擎驗證。**全部 1000 日線、train 250 / test 90 / stride 60**。

**Caveat**：param-stability 內部用 `BacktestEngine.Run`（long-only）。LS 驗證只跑了 5 個 robust pair（其餘可信度稍低）。下方「替代候選」未列 LS 驗證的、需後續確認。

---

## 1. 現行 portfolio.json（提示）

| Symbol | Strategy | Budget | Mode | 5x lev、總曝險 300% |
|---|---|---:|---|---|
| BTC-USDT | decorr4_ls（composite）| 70% | perp_both | |
| BNB-USDT | dual_mom_ls | 65% | perp_both | |
| DOGE-USDT | ma_regime_trend | 60% | perp_long_only | |
| ETH-USDT | mfi | 55% | perp_long_only | |
| LTC-USDT | rsi_stoch | 50% | perp_long_only | |

---

## 2. 逐 symbol 評估

### BTC × decorr4_ls — **維持 ✓**

`decorr4_ls` 是 composite（dual_mom 38% + dual_thrust 32% + bb_revert 19% + fib 10%）。4 個 component 在 BTC 上**全 use-default**：
- `dual_mom` def 22.1, stability 0.69
- `dual_thrust` def 14.2, stability 0.69
- `bb_revert` def 38.1, stability **0.88** ⭐
- `fib_retrace` def 44.8, stability 0.50

→ **參數選擇正確、無過擬合徵兆**。雖然 BTC 上單腿（如 fib_retrace 44.8、bb_revert 38.1）def OOS 比 decorr4 預期高，但 decorr4 換來跨幣穩定性。

**結論**：BTC 不動。

### BNB × dual_mom_ls — ⭐⭐ **強烈建議換策略**

現行：`dual_mom_ls` def OOS 14.3% / verdict **no-edge**。

**BNB 跨 7 策略 walk-forward**：

| Strategy | def OOS% | opt OOS% | Verdict |
|---|---:|---:|---|
| dual_mom_ls | 14.3 | −0.6 | ❌ no-edge（**現行**）|
| ma_regime_trend | 35.4 | −21.0 | ❌ no-edge |
| mfi | 25.1 | 16.5 | ✓ use-default |
| rsi_stoch | 11.9 | −19.9 | ❌ no-edge |
| **dual_thrust** | −52.5 | **+11.0** | ⭐ robust |
| **bb_revert_ls** | 43.3 | **+57.4** | ⭐ robust |
| **fib_retrace_ls** | −5 | **+37.6** | ⭐ robust |

**LS 驗證下 Sharpe**：dual_thrust(opt) 0.85 / bb_revert(opt) 0.84 / fib_retrace(opt) 0.66；現行 dual_mom_ls 沒做 LS 驗證但長線是 no-edge。

**Pattern**：BNB 動量類全 no-edge（dual_mom、ma_regime、rsi_stoch），但**均回/突破/回撤類**三條都 robust → BNB 是震盪型市場、不適合純動量策略。

**建議**（擇一或組合）：
- A. 換成 `bb_revert_ls(opt)`（Sharpe 0.84、WinRate 61%）— 單腿最穩
- B. 換成 `dual_thrust(opt)`（Sharpe 0.85、DD 24% 最低）
- C. 三腿小組合（bb_revert + dual_thrust + fib_retrace、類似 BNB 專屬 decorr3）

### DOGE × ma_regime_trend — **維持主腿、可考慮加 bb_revert(opt) 第二腿**

現行：`ma_regime_trend` def OOS **92.2%** / verdict **use-default**（stability 0.75）。**這個部署很好**。

**但** DOGE × bb_revert_ls(opt) 在 LS 驗證下：
- DD **277.7% → 71.6%**（巨幅）
- Sharpe **0.47 → 1.13**
- WinRate 67%
- OOSmed +9.7%

可考慮：DOGE 加第二腿 `bb_revert_ls(opt)`，類似 BNB 的多策略組合方向。或在 decorr4-like 配方裡為 DOGE 客製化。

**結論**：主腿不動。第二腿是 actionable 機會。

### ETH × mfi — ⚠ **可能該換**

現行：`mfi` def OOS **4.4%** ← **整個 portfolio 部署裡最低**。verdict 是 no-edge。

**ETH 跨 7 策略**：

| Strategy | def OOS% | Verdict |
|---|---:|---|
| **ma_regime_trend** | **45.9** | use-default |
| **fib_retrace_ls** | **45.6** | no-edge（但 def 高）|
| dual_mom_ls | 31.4 | use-default |
| **rsi_stoch** | 28.5 | no-edge |
| dual_thrust | 17.1 → opt 48.6 | marginal |
| mfi | 4.4 | ❌（**現行**）|
| bb_revert_ls | −4.3 | no-edge |

ETH 的最強單腿是 `ma_regime_trend`（45.9）和 `fib_retrace_ls`（45.6）。**ETH mfi 部署 OOS 顯著低於同 portfolio 內所有其他幣的部署主腿**。

⚠ portfolio.json 註解寫「資金流、統計最穩 t=3.72、maxDD 最低」——t=3.72 是顯著性，但 OOS 報酬可能低於替代。**這需要 LS 驗證才能確認**——目前資料只是長線徵兆。

**建議**：跑 LS 驗證 `ma_regime_trend × ETH` 與 `fib_retrace_ls × ETH`，若 LS 下穩定勝 mfi，考慮換腿。

### LTC × rsi_stoch — ⚠ **fib_retrace 是強候選**

現行：`rsi_stoch` def OOS 6.2 / opt OOS 32.2 / verdict robust（stability 0.88）。LS 驗證：opt OOSmed 6.6, AvgRet **−2.7**（mixed）。

**LTC 跨 7 策略**：

| Strategy | def OOS% | Verdict |
|---|---:|---|
| ⭐⭐ **fib_retrace_ls** | **120.4** | use-default |
| dual_thrust | 47.8 → opt −41.2 | no-edge |
| mfi | 26.1 | use-default |
| dual_mom_ls | −22.9 → opt 22.2 | marginal |
| rsi_stoch | 6.2 → opt 32.2 | robust（**現行**）|
| bb_revert_ls | 3.9 | no-edge |
| ma_regime_trend | −9.2 | no-edge |

**LTC × fib_retrace_ls def OOS 120.4% 是整個 35 結果裡最高的單一 default**。verdict use-default 表示**現行參數就是最佳、沒過擬合**。

**建議**：在 LS 引擎跑 `fib_retrace_ls × LTC` 驗證。若 LS 下也勝 rsi_stoch，**這是 portfolio.json 第二個可考慮的換腿**。

---

## 3. 總結：5 個部署的優化空間

| 部署 | 現狀 | 優化機會 | 影響度 |
|---|---|---|---|
| BTC × decorr4_ls | ✓ 穩 | 無 | — |
| **BNB × dual_mom_ls** | ❌ no-edge | **換成 bb_revert/dual_thrust/fib_retrace（已 LS 驗證）** | **大** |
| DOGE × ma_regime_trend | ✓ def 92.2 | 加第二腿 bb_revert(opt)（LS Sharpe 1.13） | 中 |
| **ETH × mfi** | ⚠ def 4.4 最弱 | 考慮 ma_regime/fib_retrace 替代（需 LS 驗證） | **大** |
| LTC × rsi_stoch | OK 但 mixed | 考慮 fib_retrace（def 120.4、需 LS 驗證） | 中-大 |

**結論**：portfolio 有 **3 個明確優化機會**（BNB / ETH / LTC 換腿）+ 1 個加腿機會（DOGE）。BNB 那個已有 LS 驗證資料、最 actionable；ETH 與 LTC 換腿需先跑 LS 驗證才能下決定。

---

## 4. 建議的下一步驟（按優先順序）

1. **跑 LS 驗證 ETH × {ma_regime_trend, fib_retrace_ls} 與 LTC × fib_retrace_ls**（直接擴展 `--validate-robust` 模式）— 1 次跑、補齊資料
2. **準備 portfolio.json 變更草案**（BNB 換策略 + ETH 換策略 + LTC 換策略 + DOGE 加第二腿）— 用 shadow 模式先跑數週對帳、再 live
3. 思考 portfolio-level：總曝險 300%、5 倉的配置是否該重整（多增 1 倉？budget 重配？）— 留作獨立研究

⚠ **portfolio.json 屬實盤、不直接動**。需用戶明示授權 + shadow 對帳。

---

**檔案**：本檔自包含、無需新跑 code。下一步若要 LS 驗證 ETH/LTC 候選，在 `tools/param-stability/Program.cs` 的 `--validate-robust` pair list 加入即可。

---

## 附錄：ETH/LTC 候選 LS 驗證（2026-05-26 補完）

加 `--validate-candidates` 模式跑 LS walk-forward、對比現部署 vs 候選（皆 default 參數）：

### ETH（現 mfi）

| Strategy | OOSmed% | Sharpe | DD% | +folds | WinRate |
|---|---:|---:|---:|---:|---:|
| `mfi`（現）| 4.8 | 0.58 | 60.4 | 7/12 | 54.17 |
| **`ma_regime_trend`（候選）**| **8.9** | **0.84** | **46.9** | **9/12** | 52.08 |
| **`fib_retrace_ls`（候選）**| **13.2** | **0.96** | 85.9 | 8/12 | **68.05** |

**兩個候選全勝**：
- ma_regime_trend：**保守選**。Sharpe +0.26、DD 降 13.5pp、+folds 多 2 個（穩定性增強）
- fib_retrace_ls：**激進選**。Sharpe +0.38、OOSmed 提升 +8.4%、WinRate 從 54 → **68%**，但 DD 升 25pp

→ ETH 換腿選項：① `ma_regime_trend`（穩）② `fib_retrace_ls`（強但 DD 高）③ 兩者組合

### LTC（現 rsi_stoch）

| Strategy | OOSmed% | Sharpe | DD% | +folds | WinRate |
|---|---:|---:|---:|---:|---:|
| `rsi_stoch`（現）| 2.3 | 0.49 | 89.8 | 7/12 | 45.84 |
| ⭐⭐⭐ **`fib_retrace_ls`（候選）**| **25.0** | **1.40** | **46.4** | **10/12** | **80.56** |
| `mfi`（候選）| 0.0 | 0.54 | 27.3 | 5/12 | 41.67 |

⭐⭐⭐ **LTC × fib_retrace_ls(default) 是這整輪研究的最強發現**：
- OOSmed **25.0%** vs 2.3%（**11x 提升**）
- Sharpe **1.40** vs 0.49（**接近 3x 提升**、>1 是極佳）
- DD **46.4%** vs 89.8%（**砍 48%**）
- +folds **10/12** vs 7/12（最穩）
- WinRate **80.56%** vs 45.84%（從 coin-flip 到 **4/5 勝率**）

而且**用 default 參數就最佳**（不需調參、無過擬合風險）。

---

## 更新的優化建議全貌

| 部署 | 現狀 | 優化機會 | LS 驗證 |
|---|---|---|:---:|
| BTC × decorr4_ls | ✓ 穩 | 無 | — |
| **BNB × dual_mom_ls** | ❌ no-edge | 換 bb_revert/dual_thrust/fib_retrace(opt) | ✅ Sharpe 0.66-0.85 |
| DOGE × ma_regime_trend | ✓ def 92.2 | 加第二腿 bb_revert(opt) | ✅ Sharpe 1.13 |
| **ETH × mfi** | ⚠ def 4.4 最弱 | 換 ma_regime(穩) 或 fib_retrace(強) | ✅ Sharpe 0.84/0.96 |
| **LTC × rsi_stoch** | ⚠ LS mixed | **換 fib_retrace_ls** | ⭐⭐⭐ Sharpe **1.40** |

**5 個部署 → 4 個有 LS 驗證的優化機會**。最強單一發現：**LTC × fib_retrace_ls**。

**fib_retrace_ls 出現在 4 個 symbol 的優化建議裡**（BNB opt / LTC def / ETH def / decorr4_ls 組件）——它是真正跨多幣 robust 的非趨勢腿。

---

## 附錄 2：LTC × fib_retrace_ls Robustness 補測

`--validate-ltc-fib-robust` 模式跑跨時框 + 不同 walk-forward 配置，確認不是 1d 假象。

### 跨時框（5 個）

| 時框 | bars | OOSmed | Sharpe | +folds | WinRate |
|---|---:|---:|---:|---:|---:|
| 1h | 1000 | 0.2 | 0.19 | 7/12 | 58.3% |
| 4h | 1000 | −0.2 | 0.13 | 6/12 | 45.8% |
| 12h | 1000 | 3.7 | 0.35 | 8/12 | 55.6% |
| **1d** | 1000 | **25.0** | **1.40** | **10/12** | **80.6%** |
| **1w** | 442 | **33.0** | **1.28** | 2/2 | **83.3%** |

→ Edge 集中在 **日線/週線**。Fib 金分割是「日級行情心理」現象，低時框雜訊蓋過信號。**portfolio.json 的部署本來就是 daily、不受此限制影響**。

### 1d 不同 walk-forward 配置（7 種）

| train/test/stride | folds | OOSmed | Sharpe | +folds |
|---|---:|---:|---:|---:|
| 200/60/40 | **19** | 10.6 | 1.28 | 12/19 |
| 200/90/60 | 12 | 21.1 | **1.45** | 10/12 |
| 250/90/60（baseline）| 12 | 25.0 | 1.40 | 10/12 |
| 250/120/60 | 11 | 25.9 | 1.32 | 9/11 |
| 300/90/60 | 11 | 14.5 | 1.07 | 9/11 |
| 300/120/90 | 7 | 28.4 | 1.13 | 5/7 |
| 400/90/60 | 9 | 20.8 | 1.26 | 7/9 |

→ **7 種配置全部 Sharpe ≥ 1.07**、最大 19 folds 統計量大依然 Sharpe 1.28。**baseline 不是運氣**。

### vs rsi_stoch 對照（同樣 7 種配置）

| Config | fib Sharpe | rsi_stoch Sharpe | Δ |
|---|---:|---:|---:|
| 200/60/40 | 1.28 | 0.60 | +0.68 |
| 200/90/60 | 1.45 | 0.47 | **+0.98** |
| 250/90/60 | 1.40 | 0.49 | +0.91 |
| 250/120/60 | 1.32 | 0.54 | +0.78 |
| 300/90/60 | 1.07 | 0.38 | +0.69 |
| 300/120/90 | 1.13 | 0.62 | +0.51 |
| 400/90/60 | 1.26 | 0.97 | +0.29 |

→ **每個配置 fib 都顯著勝**（最小差 +0.29、最大 +0.98）。換腿決定 robustly 被支持。

### 結論

✅ LTC × fib_retrace_ls 通過：
- 跨時框（日/週都 robust）
- 跨 walk-forward 配置（7 種全 Sharpe ≥ 1.07）
- 系統性勝過 rsi_stoch（每個配置都贏）

⚠ Caveats：
- 1w 只 2 folds（資料不夠、信度較低，但與 1d 結果一致、不衝突）
- Fib 仍是 long-only DD 高的策略（雖然 LS 引擎下 LTC 上 DD 46% 合理、比 rsi 90% 低）

**換腿可以 robustly 進行**。下一步：portfolio.json 變更草案（用戶授權後動）。
