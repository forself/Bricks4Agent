# Portfolio 2000-bar Acid Test(2026-05-26)

**目的**:把 1000-bar 找到的 `harm_prz_scan10*` 王牌 + 現役 20 支策略丟進 2000-bar(~5.5 年)的酸測,辨識:
1. 王牌是不是樣本運氣
2. 現役有哪些冗員可下架
3. 下一輪 portfolio.json 草案 + shadow 計畫

**資料來源**:
- [/tmp/acid_test_2000.log](/tmp/acid_test_2000.log) — 26 支 `harm_prz_*` 變體 × 20 幣 × 2000 bars
- [/tmp/deployed_strategies_2000.log](/tmp/deployed_strategies_2000.log) — 20 支現役/候選 × 10 幣 × 5 時框(1d 主驗、1h/4h/12h/1w 多時框穩定度)

**Walk-forward 配置**:train=250 / test=90 / stride=60(全部一致)、long-only 為主、LS 引擎針對王牌另跑全期回測。

---

## 1. 王牌酸測 — 通過

資料加倍後 t 值反而更強,排除樣本運氣的可能。

| 策略 | 1000 bars t | 2000 bars t | 2000 fullRet | 2000 Sharpe | 2000 DD |
|---|---|---|---|---|---|
| `harm_prz_scan10` | 4.03 | **5.86** ⬆ | 393% | 0.73 | 28% |
| `harm_prz_scan10_widepz` | 3.56 | **5.01** ⬆ | **1280%** | 0.86 | 30% |
| `harm_prz_top2_scan10` | — | 4.22 | 192% | 0.77 | 18% |
| `harm_prz_top2_scan10_widepz` | (邊緣) | 3.25 | 379% | 0.77 | 29% |
| `harm_prz_butterfly_scan10` | — | 3.26 | 58% | 0.74 | 0% |

**Bootstrap 95% CI(scan10_widepz)**:[4.5%, 10.2%] — 下界遠離 0。
**11 / 26 支諧波變體** 95% CI 下界 > 0。

**1d 全期相關矩陣(BTC 權益報酬)**:
- `scan10` × `scan10_widepz` = 0.77(同源、不獨立)
- `scan10` × `top2_scan10` = 0.56(中度去相關)
- `scan10_widepz` × `top2_scan10_widepz` = 0.05(去相關有效)
- `scan10_widepz` × `butterfly_scan10` = 0.05(去相關有效)

**Per-symbol robustness check**(自 [/tmp/widepz_validate.log](/tmp/widepz_validate.log) — 4 種 walk-forward 配置):
- ✅ **OP**:Sharpe 1.22 → 2.01,4 配置都正
- ✅ **ADA**:Sharpe 1.54 → 1.91,4 配置都正
- ✅ **INJ**:Sharpe 1.48 → 1.83,4 配置都正
- ⚠ **DOT**:Sharpe -0.36 → 1.08,**參數敏感** — Sharp 跨配置不穩
- ❌ **NEAR**:Sharpe -0.12 → 0.60,**樣本運氣** — 從 portfolio 拿掉
- ❌ **BTC**:Sharpe -0.36 → -0.44,4 配置都失敗

**結論**:王牌真的是王牌、但只在「對的幣」上。對 BTC 完全無用(諧波在 BTC 連訊號都很少觸發)。

---

## 2. 現役策略冗員報告(1d 為主,跨時框參考)

### 確認下架(1d OOSmed 負 或 fullRet 大幅虧損,跨時框 ≤2/5)

| 策略 | 1d OOSmed | 1d fullRet | 跨 TF 正 | 為何下架 |
|---|---|---|---|---|
| `rsi_stoch` | **−5.8%** | **−167%** | 2/5 | 全期重虧、僅 1w 13% 微正 |
| `rsi2_rev` | 1.7% | **−140%** | 0/5 | 跨時框完全無正 |
| `boll_rev` | 1.6% | **−150%** | 2/5 | 全期重虧 |
| `bb_revert_ls` | 1.6% | **−106%** | 1/5 | 1d/全期都不行 |
| `fib_retrace_ls` | −0.2% | −44% | 1/5 | **僅 1w 13% 正**;若你跑 1w 才保留 |
| `squeeze_breakout` | 0.1% | −3% | 2/5 | 觸發太少且邊緣 |
| `mfi` | −5.8% | −13% | 4/5 | 1d 失效;只在低訊號頻時框微正 |
| `di_trend_ls` | 6.0% | **−19%** | 2/5 | OOS 正但 fullRet 翻負 = 過擬合風險 |

### 保留主力(1d OOSmed ≥ 5% 且 fullRet 正)

| 策略 | 1d OOSmed | 1d fullRet | Sharpe | 評語 |
|---|---|---|---|---|
| `dual_mom_ls` | **9.1%** | 361% | 0.54 | Trx 1w +46%、最均衡 |
| `decorr5_scan10` | **9.0%** | 454% | 0.54 | decorr 家族最強 |
| `ma_regime_trend` | **8.9%** | 267% | 0.50 | regime-aware、跨幣穩 |
| `decorr5_top2_scan10` | 8.9% | 437% | 0.56 | 與 scan10 同源、選一即可 |
| `decorr5_widepz` | **8.2%** | **528%** | 0.53 | decorr 家族 fullRet 王 |
| `accel_momentum` | 7.6% | **1084%** | 0.52 | fullRet 王、波動高、輕量倉 |
| `decorr4_ls` | 7.5% | 349% | 0.48 | NetWeightedEnsemble、多元化基底 |
| `ts_momentum` | 7.3% | 74% | 0.31 | 補多元化 |
| `chandelier_trend` | 3.6% | 559% | 0.41 | 趨勢 trailing、低相關 |
| `dual_thrust` | 3.2% | 0% | 0.27 | 邊緣、考慮觀察 |
| `don_trend` | 5.1% | 28% | 0.22 | 邊緣 |
| `supertrend_ls` | 5.6% | **1449%** | 0.20 | 1w 表現異常突出、需單獨驗 |

**注意**:`decorr5_*` 三胞胎(scan10 / top2_scan10 / widepz)同源、相關高、挑一個代表即可 — 推薦 `decorr5_widepz`(fullRet 最高)。

---

## 3. 下輪 portfolio.json 草案 — Two-Stage Shadow

### Tier 1 — Shadow(紙交易 4-8 週、不動真錢)

```yaml
harm_prz_scan10_widepz:   30%   # 新王牌、t=5.01、1280% fullRet
harm_prz_scan10:          15%   # 姐妹策略、t=5.86;相關 0.77 但 mean 更高
decorr5_widepz:           15%   # decorr 家族代表、528% fullRet
ma_regime_trend:          15%   # Sharpe 0.50、regime-aware
accel_momentum:           10%   # 1084% fullRet、輕量倉
dual_mom_ls:              10%   # 0.54 Sharpe、跨幣穩
ts_momentum:               5%   # 多元化補位
# total                  100%
```

**幣池限制**:不要在 BTC / NEAR 上開 `harm_prz_*`(已驗無 edge 或不穩);其他幣可全開。

### Tier 2 — 4 週後若 shadow PnL ≥ +5% 且 DD < 20% 升 live

把 Tier 1 配重砍半上 live、其餘 49% 留現金 / 舊 portfolio 持有:

```yaml
harm_prz_scan10_widepz:   15%
harm_prz_scan10:           8%
decorr5_widepz:            8%
ma_regime_trend:           8%
accel_momentum:            5%
dual_mom_ls:               5%
ts_momentum:               2%
# new live total            51%   (剩 49% 留現金或保留現有 portfolio)
```

### 下架清單(下輪 portfolio.json 修訂)

`rsi_stoch`, `rsi2_rev`, `boll_rev`, `bb_revert_ls`, `squeeze_breakout`, `mfi`, `di_trend_ls`(1d)
`fib_retrace_ls` 視時框決定 — 若 worker 跑 1w 才留。

---

## 4. 風險與待驗證項目

### 已知 caveat(報告自帶)

1. **滑點/funding 未完全模型化**:`scan10_widepz` realistic → +funding 從 4.1% → 4.0%(可),`bb_revert_ls` realistic → +funding 從 -0.7% → -1.0%(資金費把長抱拖垮)。
2. **Fold 非獨立**:重疊窗 + 幣高相關 → 真實 t 比帳面低、CI 偏窄。
3. **多重檢定膨脹**:測 26 支諧波 → 純運氣假陽性 ~1 支。只信前 3 名(`scan10` / `scan10_widepz` / `top2_scan10`)。
4. **5.5 年資料不夠**:2000 bars 1d 涵蓋牛/熊各一段,但下次熊市真實表現待觀察。
5. **portfolio.json 是真錢** — 不要直接改,先 shadow 4-8 週。

### 上 shadow 前還想驗的事

- [ ] **1h / 4h 表現**:目前王牌只在 1d 驗過 2000 bars;若 worker 排程跑非 1d 時框會出事。
- [ ] **per-symbol 確認**:已驗 OP / ADA / INJ / DOT / NEAR / BTC,**LTC / APT / SUI / TRX** 未在 widepz 配置下驗,建議下次跑時補上。
- [ ] **TP(target price)engine 支援**:`LongShortBacktestEngine` 目前讀 SL 但忽略 TP — 加入後 `harm_prz_scan10` 預期 Sharpe 應再升。
- [ ] **真實 funding 數據**:現在用 0.010%/8h 估計,應該抓 Binance 歷史 funding 跑回測。
- [ ] **decorr5_widepz 內部組合**:該策略是 `harm_prz_scan10_widepz` 的 ensemble 成員,需確認與 stand-alone scan10_widepz 的相關度,免得重複下單。

---

## 5. 後續路線圖(摘要)

| 標籤 | 內容 | 預期效益 |
|---|---|---|
| H16 | LS engine 讀 `Signal.TargetPrice`(TP 支援) | Sharpe ↑、提早出場 |
| H17 | Confidence-based 倉位 sizing | 大訊號重押、小訊號試水 |
| H18 | ATR trailing SL | 趨勢段最大化、回撤 ↓ |
| H20-21 | 多時框 confluence 過濾 | 假訊號 ↓、勝率 ↑ |
| H9 | Volume divergence 確認 | 在 PRZ 觸發前驗證 |
| 資金費 alpha | OI / funding 偏離 → 反向押 | 新策略類別 |

---

**Meta-finding(再次強調)**:結論「策略無 edge」前先驗實作對不對符合理論。今天 5/26 H5 翻案案例(`harmonic_ls` 進場時機晚 3-8 bars 違反 Carney textbook PRZ 進場)已記在 [feedback_verify_implementation_first](../../.claude/projects/-Users-dba-anthony-Note/memory/feedback_verify_implementation_first.md);這次酸測再次印證 — 多 hypothesis 失敗未必是訊號問題,可能是實作問題。
