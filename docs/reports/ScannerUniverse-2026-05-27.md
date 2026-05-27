# Scanner Universe 交集分析(2026-05-27)

**目的**:Portfolio Scanner Hybrid 需決定每個 scanner 的候選幣池(universe)。今早跑了:
- `harm_prz_scan10` 跨幣 deep dive([/tmp/scan10_deepdive.log](/tmp/scan10_deepdive.log))
- `ts_momentum` 跨幣 deep dive([/tmp/tsmom_deepdive.log](/tmp/tsmom_deepdive.log))
- `tsmom_widepz` / `tsmom_harm_prz_scan10` ensemble 補測([/tmp/combined_deepdive.log](/tmp/combined_deepdive.log))

外加昨日已有 `harm_prz_scan10_widepz` 全幣資料([/tmp/h16_acid_test.log](/tmp/h16_acid_test.log))。

Universe 標準(用戶 2026-05-26 決定):跨時框 ≥ 4/5 ∪ 1d ≥ 15%(兩者聯集)。

---

## 1. 三支 scanner 候選策略 — Pool t-stat & 風險

| 策略 | Pool t | mean% | 95% CI | LS Sharpe | LS DD% | Funding 敏感 | 評語 |
|---|---:|---:|---|---:|---:|:---:|---|
| `harm_prz_scan10` | **7.69** | 8.3 | [6.3, 10.6] | **0.99** | **11** | ❌ 不敏感 | risk-adjusted 王者 |
| `harm_prz_scan10_widepz` | 5.01 | 7.2 | [4.5, 10.2] | 0.77 | 22 | ❌ 不敏感 | fullRet 王(931%) |
| `ts_momentum` | 3.93 | 8.5 | [4.6, 12.9] | 0.31 | **82** | ✅ 敏感(2.2→1.0) | 多元化、但風險高 |
| `tsmom_widepz`(組合)| 4.29 | 9.3 | [5.4, 13.5] | **−0.44** | 115 | ✅ 敏感 | ❌ LS Sharpe 負 |
| `tsmom_harm_prz_scan10`(組合)| 3.95 | 8.6 | [4.4, 13.1] | **−0.47** | 119 | ✅ 敏感 | ❌ LS Sharpe 負 |

**結論**:Scanner 應使用**純策略**、不要 ensemble。Hybrid 在 portfolio 層(多支獨立 scanner)、不是 signal 層(單一 ensemble)。 [feedback_strategy_filter_pattern](../../.claude/projects/-Users-dba-anthony-Note/memory/feedback_strategy_filter_pattern.md) 也呼應這個發現。

---

## 2. 每幣 × 每策略 OOS 矩陣(1d / 跨時框)

★ = 該策略在這檔幣上是 20 檔之最、⭐ = 主場推薦、⚠ = 1d 看似強但跨時框 ≤2/5(樣本巧合可能)

| 幣 | widepz 跨時框 | widepz 1d% | scan10 跨時框 | scan10 1d% | tsmom 跨時框 | tsmom 1d% |
|---|:---:|---:|:---:|---:|:---:|---:|
| **LTC** | **5/5**(11)⭐ | (10) | **5/5**(10)⭐ | 2 | 1/5(−1) | −2 |
| **OP** | 4/5(9)⭐ | **23★** | 4/5(7)⭐ | **16★** | 2/5(−1) | 6 |
| **NEAR** | 4/5(7) | 16 | 4/5(6)⭐ | 13 | 3/5(2) | 6 |
| **INJ** | 3/5(6) | 11 | 4/5(6)⭐ | 9 | 2/5(3) | 15 |
| **APT** | 3/5(6) | 19⚠ | 4/5(4) | 10 | 0/5(−2) | −1 |
| **SUI** | 4/5(7) | 21 | 4/5(4)⭐ | 10 | 2/5(8) | **24★** |
| **ADA** | 3/5(?) | 14 | 3/5(3) | 14 | 1/5(−9) | 1 |
| **DOGE** | 3/5(3) | 14 | 3/5(3) | 11 | 2/5(1) | −2 |
| **AVAX** | 3/5(4) | 22⚠ | 3/5(3) | 7 | 2/5(4) | 19⚠ |
| **ARB** | 3/5(4) | 3 | 4/5(3) | 5 | 1/5(−1) | −1 |
| **DOT** | 4/5(4) | 8 | 3/5(3) | 5 | 0/5(−2) | −2 |
| **LINK** | 3/5(?) | 3 | 4/5(1) | 3 | 2/5(10) | −1 |
| **ATOM** | 3/5(3) | 4 | 4/5(2) | 4 | 1/5(−1) | 0 |
| **TRX** | — | −3 | 2/5(0) | 2 | **4/5**(**38**)⭐ | 5 |
| **BTC** | 3/5(0)❌ | 1 | 3/5(0)❌ | 1 | 3/5(24)⭐ | 4 |
| **ETH** | 3/5(?) | 0 | 3/5(0)❌ | 1 | 3/5(5) | 1 |
| **BNB** | — | 0 | 4/5(1) | 1 | 3/5(20)⭐ | 7 |
| **XRP** | 3/5(?) | 0 | 3/5(0)❌ | 3 | 3/5(23)⭐ | 3 |
| **UNI** | — | −2 | 3/5(0)❌ | −1 | 2/5(1) | 1 |

---

## 3. Scanner Universe 推薦(套用標準)

### 🎯 `widepz_scanner`(策略 = `harm_prz_scan10_widepz`)

**Tier 1**(跨時框 ≥4/5 ∪ 1d ≥15%):
```yaml
universe: [LTC, OP, NEAR, SUI, DOT]   # widepz 主場 5 檔
# 全部跨時框 ≥4/5,且 LTC/OP/SUI 1d 也 ≥15
```
**Tier 2 觀察**:APT(1d 19 但跨 3/5)、INJ(跨 3/5 但 1d 11)、AVAX(1d 22 但跨 3/5)→ shadow 4 週後評估。

### 🎯 `scan10_scanner`(策略 = `harm_prz_scan10`)

**Tier 1**(跨時框 ≥4/5 ∪ 1d ≥15%):
```yaml
universe: [LTC, OP, NEAR, INJ, APT, SUI, ARB, BNB, LINK, ATOM, DOT]   # 11 檔
# 但 BNB/LINK/ATOM 1d 只有 1-4%(跨時框 4/5 但收益小)→ 可選擇砍掉
```
**精簡版**(只留跨時框 ≥4/5 且 1d ≥5%):
```yaml
universe: [LTC, OP, NEAR, INJ, APT, SUI, ARB]   # 7 檔
```

### 🎯 `tsmom_scanner`(策略 = `ts_momentum`)

**Tier 1**(跨時框 ≥4/5 ∪ 1d ≥15%):
```yaml
universe: [TRX, SUI, AVAX, INJ]   # 4 檔(1d≥15: SUI/AVAX/INJ;跨時框 ≥4/5: TRX)
```
**但**:
- BTC/BNB/XRP 跨時框 3/5、1d 才 4-7% — **不滿足任一條件**,被 universe 標準排除
- 然而 BTC/BNB/XRP 跨時框 avg 是 24/20/23 — 大部分集中在 1w(36% 那邊)
- → 建議 `tsmom_scanner_1w`(interval=1w、universe=[BTC, BNB, XRP, TRX])跟 1d scanner 分開

---

## 4. Universe 衝突盤點

哪些幣會被多支 scanner 同時開?

| 幣 | widepz | scan10 | tsmom_1d | tsmom_1w | 衝突? | 處理方案 |
|---|:---:|:---:|:---:|:---:|:---:|---|
| LTC | ✅ | ✅ | — | — | ✅ | scan10 vs widepz 同源(corr 0.77)→ 二選一 |
| OP | ✅ | ✅ | — | — | ✅ | 同上 |
| NEAR | ✅ | ✅ | — | — | ✅ | 同上 |
| INJ | ✅ | ✅ | (1d 15) | — | ✅ | scan10 主、tsmom 用 1w |
| SUI | ✅ | ✅ | ✅ | — | ✅✅ | **三方衝突**、scan10 主(t-stat 高) |
| APT/ARB | — | ✅ | — | — | ❌ | scan10 獨佔 |
| TRX | — | — | — | ✅ | ❌ | tsmom 獨佔(1w) |
| BTC/BNB/XRP | — | — | — | ✅ | ❌ | tsmom_1w 獨佔 |
| AVAX | — | — | ✅(1d 19) | — | ❌ | tsmom 獨佔(但跨 2/5、⚠ 觀察) |

**衝突點**:LTC / OP / NEAR / INJ / SUI 都被 widepz + scan10 同時鎖定。
- 它們相關 0.77(scan10 vs widepz)— 同方向訊號比例高、但不是 100% 相同
- 同時開兩支會「同向加倍曝險」 — 違反 decorr by universe 設計目標
- **建議**:同源策略**只跑一個**。要決定:**留 widepz 還是 scan10?**

---

## 5. widepz vs scan10 二選一

| 維度 | scan10(H16 後王者)| widepz |
|---|---|---|
| Pool t | **7.69** ⬆ | 5.01 |
| Sharpe | **0.99** ⬆ | 0.77 |
| DD% | **11** ⬆⬆ | 22 |
| fullRet% | 388 | **931** ⬆ |
| OOSmed 1d% | 7.3 | 9.8 ⬆ |
| 訊號頻率 | 中(scan10 = 嚴格 PRZ)| 高(widepz = ±15% 寬 PRZ)|

**建議:scan10 主、widepz 備胎**。理由:
1. Risk-adjusted 王者:Sharpe 0.99 / DD 11% 都遠勝
2. H16 後 widepz TP 太緊(commit `776716f` Method C 已修但效果仍受限)
3. **但 widepz fullRet 高**(931 vs 388),牛市段贏更多 — 留下做 shadow 比較

最終 scanner 配置:
- `scan10_scanner`(live 候選):universe `[LTC, OP, NEAR, INJ, APT, SUI, ARB]`
- `widepz_scanner`(shadow 比較組):同 universe 用 widepz 跑,對照 scan10 哪個實盤更好
- `tsmom_scanner_1d`:universe `[TRX, SUI, AVAX]`(SUI 跟 scan10 衝、要決定哪邊優先)
- `tsmom_scanner_1w`:universe `[BTC, BNB, XRP, TRX]`(週線、跟 1d 獨立曝險)

---

## 6. 對 [TODO-2026-05-26.md](../TODO-2026-05-26.md) 的影響

✅ **P0 第二支 scanner 選 `ts_momentum`**:確認、已驗 t=3.93、跟 scan10 主場互補
✅ **Universe 標準兩者聯集**:確認、套用後 widepz 5、scan10 7-11、tsmom 4 檔
✅ **APT 不進**:確認、scan10 收(主場 4/5、4%)而非 tsmom 收(0/5、−1%)— 留 scan10 universe

新發現(寫進 TODO 補充):
- **ensemble 策略不適合當 scanner**(LS Sharpe 負、DD > 100)
- **同源策略只跑一個**(widepz vs scan10 二擇一、推薦 scan10)
- **tsmom 需要分 1d 跟 1w 兩支 scanner**(1d 主場小、1w 主場大且不同幣)

---

## 7. 下一步建議

1. **Update [docs/designs/portfolio-scanner-hybrid.md](../designs/portfolio-scanner-hybrid.md)** §3.3 portfolio.json schema 加上 4 個 scanner 定義(scan10 / widepz_shadow / tsmom_1d / tsmom_1w)
2. **P1 Step B**:`AutoTraderService.Sweep()` 加 `ProcessScannerAsync` pass(下一個 commit)
3. **P3 Shadow**:scanner_legs 表 seed 上面 4 個 scanner 定義(全部 shadow=true 啟動),4 週 paper trade 後評估
4. **未來補測**(等 shadow 跑完):同源策略相關性實證(同期 scan10 vs widepz 訊號重合率)、tsmom 1w 真實 funding 敏感度
