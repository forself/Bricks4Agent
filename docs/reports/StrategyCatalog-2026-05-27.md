# 策略目錄 — 25 個 t-stat 顯著策略

**寫於**:2026-05-27
**資料來源**:`strat-validate --apply-funding` 跑出來的 pool t-stat(20 主場幣 × walk-forward 250/90/60 × bootstrap 2000、含真實 Binance funding)
**用途**:換腿 / 開新 scanner / 升 live 決策時、不用翻 log、直接查這份

---

## 📊 完整排名表(by t-stat × mean × 部署狀態)

| 排名 | 策略 | t | mean% | 95% CI | 家族 | 部署狀態 |
|--:|---|---:|---:|---|---|---|
| 1 | `harm_prz_scan10` | **5.93** | 10.6 | [7.3, 14.5] | harmonic | 🟡 shadow(scan10_scanner) |
| 2 | `harm_prz_scan10_widepz` | **5.54** | **17.0** | [11.4, 23.5] | harmonic | 🟡 shadow(widepz_scanner) |
| 3 | `harm_prz_top2_scan10` | 5.12 | 8.3 | [5.3, 11.7] | harm-top2 | — |
| 3 | `harm_prz_top2_scan20` | 5.12 | 8.3 | [5.4, 11.6] | harm-top2 | — |
| 5 | `harm_prz_top2_scan10_widepz` | 4.83 | 12.4 | [7.9, 17.8] | harm-top2 | 🟡 shadow(top2_widepz_scanner) |
| 6 | `harm_prz_top2_scan5` | 4.71 | 6.7 | [4.2, 9.6] | harm-top2 | — |
| 7 | `tsmom_widepz` | 4.53 | 14.4 | [8.4, 21.2] | combo | 🟡 shadow(tsmom_widepz_scanner) |
| 8 | `tsmom_harm_prz_scan10` | 4.11 | 13.2 | [7.3, 19.7] | combo | — |
| 9 | `ts_momentum` | 3.99 | 12.4 | [6.5, 18.5] | trend | 🟡 shadow(tsmom_1d/1w) + 🔴 真錢(UNI) |
| 10 | `harm_prz_butterfly_scan10` | 3.73 | 3.6 | [1.9, 5.6] | harm-pattern | — |
| 11 | `harm_prz_top2` | 3.37 | 1.1 | [0.6, 1.8] | harm-top2 | — |
| 12 | `decorr5_top2_scan10` | 3.18 | 10.3 | [4.3, 16.9] | ensemble | — |
| 13 | `decorr5_scan10` | 3.11 | 9.8 | [4.0, 16.2] | ensemble | 🟡 shadow(decorr5_scanner) |
| 14 | `decorr5_with_harmprz` | 3.07 | 9.9 | [3.7, 16.4] | ensemble | — |
| 15 | `decorr5_butterfly` | 3.06 | 9.8 | [3.7, 16.4] | ensemble | — |
| 16 | `dual_mom_ls` | 3.05 | 9.8 | [4.1, 16.4] | trend | 🔴 真錢(SUI) |
| 17 | `decorr5_widepz` | 2.99 | 9.3 | [3.3, 15.4] | ensemble | — |
| 18 | `decorr4_ls` | **2.96** ⚠ | 9.5 | [3.7, 16.1] | ensemble | 🔴 真錢(BTC、t 邊界) |
| 19 | `dual_thrust` | 2.67 | 6.1 | [1.5, 11.1] | breakout | 🔴 真錢(SOL) |
| 20 | `triple_pattern_mom` | 2.57 | 8.0 | [2.2, 14.1] | combo | — |
| 21 | `quad_widepz` | 2.54 | 8.0 | [2.0, 14.6] | combo | — |
| 22 | `ma_regime_trend` | **2.40** ⚠ | 6.6 | [1.3, 12.2] | trend | 🔴 真錢(BNB、t 最邊界) |
| 23 | `harmonic_prz_ls` | 2.41 | 0.8 | [0.2, 1.6] | harmonic-base | — |
| 24 | `harm_prz_butterfly` | 2.39 | 0.6 | [0.2, 1.2] | harm-pattern | — |
| 25 | `harm_prz_five_o` | 2.34 | 0.5 | [0.1, 1.0] | harm-pattern | — |

**部署摘要**:6 支真錢、6 支 shadow scanner、13 支顯著但未部署(可挑做新 scanner)

### 🆕 結構性 alpha 追加(2026-05-27 深夜)

新發現、不在原 25 排名表內、抗 decay 性高。Funding momentum 家族經 param sweep 後找到 **xtight 變體 t=5.93 跟 harm_prz_scan10 並列家族最高**:

| # | 策略 | t | mean% | Sharpe | PF | 部署狀態 |
|--:|---|---:|---:|---:|---:|---|
| 🆕⭐ | `fundmom_ls_xtight`(0.05/0.95)| **+5.93** | **17.4** | **0.71** | **3.09** | 🟡 shadow(fundmom_xtight_scanner)— 王者級 |
| 🆕 | `fundmom_ls_tight`(0.10/0.90) | 3.48 | 11.3 | 0.56 | 1.75 | — |
| 🆕 | `fundmom_ls_loose`(0.20/0.80) | 3.48 | 11.1 | 0.55 | 1.44 | — |
| 🆕 | `funding_momentum_ls`(0.15/0.85、預設)| 3.25 | 10.1 | 0.53 | 1.47 | 🟡 shadow(fundmom_scanner)|
| 🆕 | `funding_extreme`(contrarian、反向)| **−3.76** | -13.8 | — | — | ❌ 顯著為負、收線(反向證實 momentum 才對) |

**機制重大發現**:funding 極端時 = **羊群延續、非均值回歸**

- contrarian buy on low funding(funding_extreme):catch falling knife、t=−3.76
- momentum follow trend on extreme funding(funding_momentum_ls):跟對方向、t=+3.25
- 兩個對稱、合理

**xtight 變體的 selectivity-quality tradeoff**:閾值從 15%/85% 收緊到 5%/95%、trade 數/年從 8 砍到 3、但 Sharpe +34% / PF +110%(極端 funding 訊號稀缺但強)

**為什麼是結構性 alpha**:
- funding 是 perp 強制收費機制、永遠存在(只要 perp 存在)
- 「funding 極端 → 趨勢延續」反映人性(羊群、不止損)、不會 decay
- 跟既有諧波 / trend / decorr 完全不同訊號源、decorr 紅利大

→ **funding momentum 家族整體是除 harm_prz_scan10 之外的第二個 "頂級" 訊號源、且抗 decay 比 harm_prz 強**

**機制重大發現**:funding 極端時 = **羊群延續、非均值回歸**
- contrarian buy on low funding(funding_extreme):catch falling knife、t=−3.76
- momentum follow trend on extreme funding(funding_momentum_ls):跟對方向、t=+3.25
- 兩個對稱、合理

**為什麼是結構性 alpha**:
- funding 是 perp 強制收費機制、永遠存在(只要 perp 存在)
- 「funding 極端 → 趨勢延續」反映人性(羊群、不止損)、不會 decay
- 跟既有諧波 / trend / decorr 完全不同訊號源、decorr 紅利大

---

## 🔬 按家族分組:策略詳細卡片

### 諧波 PRZ 家族(harmonic) — 王牌、低頻、TP-driven

特性:Carney 諧波 PRZ(Potential Reversal Zone)反轉策略、XABC 4 點投影、純 LS 多空雙向。
**funding 影響:0**(LS net-neutral)。

#### ⭐ `harm_prz_scan10`(t=5.93、家族王者)
- **機制**:scanWindows=10 個窗口掃 pattern + Carney 比例校驗
- **mean OOS**:10.6%、低 DD 11%
- **主場幣**:LTC(28%)、ADA(22%)、OP(28%)、INJ(18%)、APT(27%)、SUI(25%)、NEAR(15%)
- **黑名單**:XRP(-1%)、BTC(1%)、UNI(1%)
- **caveat**:scanWindows ≥10 飽和、再加無效(2026-05-27 paramsweep 驗)
- **Code**:[`HarmonicPrzLsStrategy.cs`](../../packages/csharp/workers/strategy-worker/Engine/HarmonicPrzLsStrategy.cs)
- **部署**:scan10_scanner(7 alt-coin、shadow)

#### ⭐ `harm_prz_scan10_widepz`(t=5.54、mean 最高)
- **機制**:scan10 + PRZ ±15% widening(撈更多訊號)+ Method C TP(從進場價投影)
- **mean OOS**:**17.0%**、DD 22(高頻多 trade)
- **主場幣**:同 scan10
- **caveat**:przWidening 15→20 微正 +0.08 Sharpe(可考慮升 0.20 激進版)
- **Code**:同上、constructor params `(scanWindows: 10, przWidening: 0.15)`
- **部署**:widepz_scanner(同 universe、shadow A/B)

#### `harm_prz_top2_scan10` / `_scan20` / `_scan5` / `_scan10_widepz`(top-2 pattern 限定)
- **機制**:只保留 butterfly + five_o 兩種 pattern(高 RR 子集)
- **t-stat**:scan10=5.12、scan20=5.12、scan5=4.71、widepz=4.83
- **mean**:6.7-12.4%
- **適合**:稀缺 pattern、訊號少但品質高
- **Code**:`new HarmonicPrzLsStrategy(new[] {"butterfly", "five_o"}, ...)`
- **部署**:top2_widepz_scanner

#### `harm_prz_butterfly_scan10` / `harm_prz_butterfly` / `harm_prz_five_o` / `harm_prz_top2` / `harmonic_prz_ls`(單型態 / 基底)
- 單 pattern 變體、mean 都 < 4%
- 適合研究、不建議單獨部署(信號密度太低)

---

### 趨勢 / 動量家族(trend / breakout)

特性:long-heavy(順 trend 跟著走)、funding 微跌 0.1-0.2 t-stat(BTC/ETH 正 funding drag)。

#### ⭐ `ts_momentum`(t=3.99、跨機制 alpha)
- **機制**:Time-series momentum + 波動率管理絕對動量、12 個月 lookback
- **mean OOS**:12.4%、DD 60(高)
- **主場幣**:TRX、SUI、AVAX(1d);BTC、BNB、XRP、TRX(1w)
- **caveat**:在 BTC up regime 表現差(Sharpe 0.31)、用 `tsmom_btc_not_up` filter 可提升 +0.16
- **Code**:[`TsMomentumStrategy.cs`](../../packages/csharp/workers/strategy-worker/Engine/TsMomentumStrategy.cs)
- **部署**:tsmom_1d_scanner + tsmom_1w_scanner + tsmom_btcnotup_scanner + 🔴 UNI 真錢腿

#### `dual_mom_ls`(t=3.05)
- **機制**:雙時框動量共振、短週期 + 長週期 momentum 都同向才開
- **mean**:9.8%
- **部署**:🔴 SUI 真錢腿

#### `dual_thrust`(t=2.67)
- **機制**:range breakout + 趨勢過濾、Larry Williams 經典
- **mean**:6.1%
- **部署**:🔴 SOL 真錢腿

#### `ma_regime_trend`(t=**2.40** ⚠ 最邊界)
- **機制**:均線斜率 regime + trend follow
- **mean**:6.6%
- **caveat**:funding 後 t 從 2.52 跌到 2.40、下次跌就要考慮換腿(BNB)
- **部署**:🔴 BNB 真錢腿(候選替換:tsmom_widepz)

---

### Ensemble 家族(去相關組合)

特性:多支策略加權集成、靠去相關降 DD、funding 影響中等(內含 trend 成分)。

#### `decorr4_ls`(t=**2.96** ⚠ 邊界)
- **機制**:dual_mom 38% + dual_thrust 32% + bb_revert 19% + fib 10% 反波動率加權淨曝險
- **mean**:9.5%
- **caveat**:bb_revert / fib 個別不顯著、但 ensemble 顯著(decorr 紅利)
- **caveat**:funding 後 t 滑下 3.0 邊界
- **Code**:Program.cs 用 `NetWeightedEnsembleStrategy`
- **部署**:🔴 BTC 真錢腿(120% budget)

#### `decorr5_*` 系列(scan10 / widepz / top2_scan10 / with_harmprz / butterfly)
- **機制**:decorr4 + 加 1 支諧波變體 = 5 支去相關
- **t-stat**:2.99 - 3.18(都在邊界附近)
- **mean**:9.3-10.3%
- **部署**:decorr5_scanner(用 decorr5_scan10、shadow)

#### `tsmom_widepz`(t=4.53) / `tsmom_harm_prz_scan10`(t=4.11)
- **機制**:ts_momentum + 諧波 widepz/scan10 加權
- **mean**:13.2-14.4%(全名單第 2 / 3 高)
- **部署**:tsmom_widepz_scanner(shadow)

#### `triple_pattern_mom`(t=2.57) / `quad_widepz`(t=2.54)
- 多 pattern 組合、邊際顯著、可研究

---

## 🎯 換腿 / 新 scanner 決策參考

### 真錢腿警戒(優先處理)
1. **BNB ma_regime_trend(t=2.40)**:候選替換 `tsmom_widepz`(t=4.53、mean 14.4)或 `decorr5_scan10`(t=3.11)。但要 shadow 4 週驗證
2. **BTC decorr4_ls(t=2.96)**:還沒到 < 2 警戒、繼續觀察、下次 t-stat 再評
3. **ETH mfi**(不顯著):shadow 期任一 scanner 給出 ETH 上強訊號就考慮換

### 新 scanner 候選(從 13 支顯著未部署裡挑)
- 跨機制 decorr 優先:`tsmom_harm_prz_scan10`(t=4.11)、`tsmom_widepz`(已用)
- 高 mean / 高 t 雙佳:`decorr5_top2_scan10`(t=3.18、mean 10.3)
- 補機制空缺:`harm_prz_butterfly_scan10`(t=3.73、純 butterfly pattern)

---

## ❌ t-stat 不顯著的常見策略(避免部署)

從之前研究記錄(都試過、都不顯著):
- `bb_revert_ls`(t=1.59)— 雖然 ETH 單路徑 Sharpe 2.00 但 pool 站不住
- `fib_retrace_ls`(t=1.28)— LTC/ETH 翻案是局部 edge
- `mfi`(t=-0.16)— ETH 現役、待換
- `rsi_stoch`(t=-0.26)
- `bollinger_bands`(t=-2.05)— 顯著為負(別 reverse、別用)
- `harmonic_ls`(t=-3.27)— 顯著為負
- 全部 fib 變體(`harm_prz_fib_*` 系列、t 約 1.1)
- 全部 `*_regime_ls` / `chandelier_*`(t 約 0.5-1.5)

---

## 📚 參考

- 完整研究歷程:[`HarmonicResearch-Log.md`](HarmonicResearch-Log.md)
- 配重分析:[`PortfolioOptimizationReview-2026-05-26.md`](PortfolioOptimizationReview-2026-05-26.md)
- Funding 影響:[`FundingRateImpact-2026-05-27.md`](FundingRateImpact-2026-05-27.md)
- Scanner 配置:[`scripts/scanner-seed.sql`](../../scripts/scanner-seed.sql)
- 驗證工具:[`tools/strat-validate/Program.cs`](../../tools/strat-validate/Program.cs)
- 核心引擎:[`LongShortBacktestEngine.cs`](../../packages/csharp/workers/strategy-worker/Engine/LongShortBacktestEngine.cs)

---

## 🔄 維護指引

這份是手動快照、跟著 `strat-validate --apply-funding` 跑出的最新 t-stat 走。
下次重跑、把表格內 mean / t / 95% CI 三欄更新即可。

未來考慮做自動生成(parse strat-validate output → markdown)、避免手動維護過時。
