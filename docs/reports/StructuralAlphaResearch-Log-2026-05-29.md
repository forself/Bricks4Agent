# Structural Alpha Research Log（Q2)

每日小實驗、累積知識。**就算失敗也是知識** —— 避免下次重踩。
格式:每條一個假設、改動、結果、結論、學到。

**主軸**:找「跟價格弱相關、來自市場結構/人性、decay 慢」的 alpha([[feedback_structural_alpha_requires_x_uncorrelated_with_price]])。
**資料源**:`tools/shared/OiMetricsCache.cs` 拉 data.binance.vision daily metrics zip(5min 粒度、回 2-3 年、免費)。
**探勘工具**:`tools/oi-validate` — 跨幣 pool t-stat(linear Pearson + quantile top/bot 20% + cross-sectional)、IS/OOS 切分。
**驗證工具**:`tools/strat-validate` — walk-forward OOS、20 幣、realistic 成本、95% CI bootstrap。

---

## Baseline(Q2 wave 0,2026-05-27)— funding momentum

`funding_momentum_ls`:funding 極端時跟趨勢(非 contrarian)。pool t=+3.25、xtight 變體 t=+5.93。✅ 已 live。
對照 `funding_extreme`(contrarian)t=−3.76 anti-edge → 證實「funding 極端 = 趨勢延續」結構性。

---

## 2026-05-28 第一彈 — OI / L-S / Taker 探勘(BTC 單幣)

**假設**:OI momentum、Top/Retail L/S、Taker vol 各自有 forward edge。
**結果**(BTC 1y、raw Pearson):全部 t<1.4。OI vs todayRet corr **0.65**(= price 代理)、Top L/S vs funding corr **0.61**(= 同源)。
**結論**:❌ BTC 單幣全無。但 corr 結構透露:OI 是 price proxy、Top L/S 跟 funding 重複。
**學到**:單幣不夠,要跨幣 pool;先看 corr 結構濾掉「不是新 alpha」的。

---

## 2026-05-28 第二彈 — 跨 8 幣 pool t-stat

**改動**:oi-validate 加 cross-coin pool（8 幣 × 365 天 = 2920 樣本）。
**結果**:
| 指標 | IS linear t | IS quantile t |
|---|---:|---:|
| **Retail L/S** | **−2.24 ✅** | **−2.18 ✅** |
| OI %change | +1.25 | +2.01 ✅ |
| Funding | −2.09 ✅ | 0.00 |
| Taker | +1.47 | +1.88 ~ |

Retail L/S 8 幣 linear t 全為負(方向全一致)→ 最強候選。
**結論**:✅ Retail L/S contrarian 浮現。**學到**:即使單幣不顯著,8 幣方向一致 + pool t 是去噪標準法。

---

## 2026-05-28 OOS 驗證 — Retail L/S 雙確認

**測法**:`--days-back 365` 跑前一年(2024-05~2025-05)。
**結果**:Retail L/S OOS linear t=**−2.89**、quantile **−2.25**(比 IS 還強)。
**結論**:✅ 真 alpha、跨年穩定。寫 `RetailLsContrarianStrategy`。strat-validate 20 幣:`retail_ls_contrarian_tight` 年化 27% / Sharpe 0.45 / DD 55%。**已部署 shadow**。

---

## 2026-05-28 翻案 — OI 方向錯了

**假設**:OI momentum 蓋棺太早,也許方向反了(contrarian)。
**改動**:`OiMomentumLsStrategy` 加 `invertSignal`,測 `oi_contrarian_ls`。
**結果**:strat-validate 20 幣 — oi_contrarian_ls 年化 16% / Sharpe 0.41 / 60% 幣 OOS 正 ✅ 可用(pool t=0.45 弱);oi_momentum 全死。
**結論**:✅ **OI 用 contrarian 而非 momentum**。跟 retail_ls corr **−0.18**(去相關)。**已部署 shadow**(2026-05-29)。
**學到**:蓋棺前先試方向翻轉。

---

## 2026-05-28 翻案2 — 衍生指標 Δ

**假設**:retail_ls「變化率」可能比「絕對位置」更前瞻(level 是 lag、delta 是 acceleration)。
**結果**:Retail L/S Δ — IS pool t=**−3.51** / OOS **−3.65**(比 raw 全面更強)。strat-validate 20 幣 `retail_ls_delta_contrarian` pool t=**+3.30** ✅ **首支 95% CI 下界>0 顯著**。
**U 形**:default(0.80/0.20)> xtight(0.95/0.05)> tight(0.90/0.10),中間最差。
**結論**:✅ Retail L/S Δ 是進化版。**已部署 shadow**,首發 APT/OP/SUI SHORT。
**學到**:衍生指標(Δ)常勝過 raw;threshold 不一定越極端越好(U 形)。

---

## 2026-05-29 第三波 — 複合/多日/加速度(收斂)

**假設**:funding×retail 交互、retail 5日動量、retail 加速度(Δ的Δ)可能更強。
**結果**:
| 信號 | IS t | OOS t | 判定 |
|---|---:|---:|:---:|
| Retail L/S 加速度 | −3.42 | −2.12 | ⚠ 顯著但跟 Δ 高相關、冗餘 |
| rls×funding 複合 | +0.45 | −0.76 | ❌ 交互假設錯 |
| retail 5日動量 | −0.03 | −1.43 | ❌ 太平滑 |
**結論**:retail_ls 信號家族掃完。精華 = **level + Δ**(已部署),衍生品冗餘或雜訊。

---

## 2026-05-29 第三波2 — Cross-sectional 市場中性(驗死)

**假設**:每天跨幣排名、long 最不擁擠 / short 最擁擠(相對而非絕對)= 去相關新結構。
**規則(預設)**:IS+OOS t 都>2 才建 harness,否則永久 shelve。
**結果**:
| Universe | IS level t | OOS level t |
|---|---:|---:|
| 20 幣 | +0.71 | +1.99 |
| 38 幣 | **−0.29** | +1.37 |
加 breadth 反而變弱、IS 趨近零 → 真效應應隨幣數變強,這是反的。
**結論**:❌ **永久 shelve**。20 幣的 OOS +1.99 是 regime 雜訊。retail_ls 適合絕對 per-coin、不適合相對排名。
**學到**:breadth 測試是分辨「真效應 vs 雜訊」的利器 — 真的會隨樣本變強。

---

## 2026-05-29 死路紀錄(免費數據邊界)

- **Liquidation cascade**:Binance 兩條路全關(data.binance.vision 404 + REST `allForceOrders` "out of maintenance")。免費爆倉歷史拿不到。
- **On-chain / whale flow**:CryptoQuant 401(付費)、Coinglass 500、blockchain.com 只有 BTC 通用鏈上統計(非資金流、BTC-only)。需付費(~$30-100/月)。
- **Cross-exchange basis**:perp-spot basis ≈ funding(冗餘);cross-exchange 需多所 infra。

---

## Q2 結論(免費結構性 alpha 完結)

**4 條真 alpha 部署**:funding_momentum(live)、retail_ls level、retail_ls Δ、oi_contrarian(shadow)。
**3 支 shadow scanner 平行 A/B**,去相關實證:delta short APT vs oi_contrarian long APT(corr −0.18)。

**免費 Binance 衍生數據已榨乾**。要更多結構性 alpha 需:
1. **付費數據**(CryptoQuant/Glassnode = on-chain;多所 = cross-exchange basis)— spend 決策
2. **Q3 microstructure**(data.binance.vision bookDepth/aggTrades,免費但 intraday、跟現有 1d cadence 錯配)

**方法論收穫**(已入 memory):
- [[feedback_signal_probe_vs_strategy_two_filters]] — 顯著 ≠ 通過,需「跨幣方向一致 + 經濟意義 + corr<0.3」三濾
- [[feedback_structural_alpha_requires_x_uncorrelated_with_price]]
- 翻案紀律:蓋棺前試方向翻轉 / 衍生指標 / threshold U 形 / breadth 測試
