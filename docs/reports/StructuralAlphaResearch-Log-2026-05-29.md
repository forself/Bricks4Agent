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

---

## 2026-05-29 存活剖析(4 alpha 一起上真錢會不會死)

工具:strat-validate 新增 `--sl`(固定止損)、`PortfolioDefended()`(CB + DrawdownAwareSizer overlay)、FundingCache 深度自動放大。

**深歷史(--bars=1300 含 2022 LUNA/FTX)**:風險加權組合裸 maxDD 41%→46%(2022 只加 5pp、去相關在深熊撐住)。
**funding 深 6.5 年(--bars=2400)**:pool t **3.44→1.43 不顯著**、DD 70% — 近窗顯著恐 regime 過擬合,對 funding_momentum 要謙虛(live 真錢中)。

**相關矩陣(去相關真實)**:最高 funding×retail_ls 0.35,其餘近零/負(oi vs delta −0.36)。
**配重生死線**:等權 DD **327%** vs 風險加權 DD **46%**。retail_ls_tight 單腿 DD 97% 是毒、反波動率自動掐到 4%。

**止損 sweep(0/6/10/15%、風險加權)**:DD 鎖死 46% 不管止損幾%,只砍 Sharpe(6% 最兇 0.60→0.25、15% 回 0.42)。
→ 46% 是「崩盤多腿一起 grind-down」非單筆 blowup,固定止損擋不了。**單筆止損不在保命關鍵路徑**。

**總體防禦 sweep(CB × 方法、裸 46%/138%)** — 保命關鍵:

| CB | poly DD/ret(效率) | step DD/ret | linear DD/ret |
|---|---|---|---|
| 5% | 15%/29% (×2.0) | 29%/49% (×1.7) | **16%/34% (×2.2)** |
| 8% | 16%/33% (×2.1) | 29%/49% (×1.7) | 18%/37% (×2.1) |
| 12% | 18%/36% (×2.0) | 29%/49% (×1.7) | 21%/39% (×1.8) |
| 20% | 21%/41% (×2.0) | 29%/49% (×1.7) | 25%/45% (×1.8) |

**結論**:
1. **存活鏈三層**:風險加權(不爆 327%)→ CB+DD-aware(46%→16%)→ 有效槓桿~3x(16%→48% < 強平)。**3x + 防禦活得下來**。
2. **3x 下別用 step**(DD 29%→×3=87% 貼強平);用 poly/linear 緊 CB(DD 16%→×3=48% 有 margin)。
3. **linear CB5-8% 微勝 poly**(效率 ×2.2)。
4. **防禦逼近效率天花板 ×2 報酬/DD** — 上限由 alpha 品質決定、非防禦旋鈕。突破只能靠**更好 alpha / 更多去相關腿**。
5. **regime-aware CB 已測、推翻**:自身 SMA gate ×2.5 是自我參照假象;BTC 外生 gate ×1.5 反比 plain(×2.1)差(逆勢 alpha 在 BTC 跌時正賺、gate 砍錯時機)。**plain DD-aware ×2.1 = 最佳誠實防禦**。教訓 [[feedback_self_referential_regime_overlay]]:regime overlay 信號必外生。
6. caveat:backtest CB 乾淨成交、真實 gap 穿透更深;CB 縮倉 = 踏空反彈(報酬代價)。

詳見 memory [[q2-portfolio-survival]]。

---

## 2026-05-29 全策略熊市審計(12 條部署策略 × 含 2022 LUNA/FTX)

起因:發現部署策略多用 bars=1000(=2023-09 起、漏 2022 熊)驗證、只 4 條跑過防禦。用 --bars=1300 跑全部。

**結果:11 跑成、10/11 pool t 顯著(含 2022)**:
| 策略 | pool t | LS DD |
|---|---|---|
| harm_prz_scan10 / _widepz / top2 | 6.16 / 5.58 / 4.76 | 9% / 11% / 7% |
| fundmom_ls_xtight / tsmom_widepz / decorr5 | 5.32 / 4.85 / 4.64 | 65% / 64% / 66% |
| ts_momentum / funding_momentum_ls | 4.49 / 4.15 | 69% / 71% |
| retail_ls_contrarian_tight / _delta | 2.47 / 2.41 | 91% / 72% |
| **oi_contrarian_ls** ❌ | **0.43** | 79% |
| **tsmom_btc_not_up** ❌(補註冊後驗) | **0.87** | **116%** |

**2 條失敗該下架**:oi_contrarian_ls(熊市 LS 不顯著)、tsmom_btc_not_up(plain ts_momentum t=4.49 但加 BTC 濾網 t→0.87,濾網過擬合到無熊窗)。

**非平穩性**:funding bars=1000(無熊)t=3.44 / 1300(含 2022)t=4.15 / 2400(含 2019-21)t=1.43。熊市不破策略、上古市場才破。

**🌟 分散組合 >> 任何子集**:
| 組合(風險加權) | Sharpe | 裸 DD | 防禦後 |
|---|---|---|---|
| 4 條 Q2 | 0.60 | 46% | 16% |
| 全 10 顯著條分散 | **1.24** | **19%** | **8%** |
加 harmonic 家族(Sharpe 1.0+/DD 7-11%)讓組合品質跳級、防禦效率 ×2→×8-9。**實證防禦天花板突破靠更好 alpha/更多去相關腿,非防禦旋鈕。** 3x:分散組合裸 19%×3=57% 不用防禦就活。

詳見 memory [[bear-market-audit-2026-05-29]]。

---

## 2026-05-29 第四波 — cross-asset funding spread(驗死)

**假設**:某幣 funding 相對 BTC funding(spread = coin_funding − BTC_funding)= 市場中性的擁擠定位信號,可能比 funding 絕對值更前瞻、且去相關。
**改動**:`oi-validate` 加 cross-asset funding spread(同日)+ pool t-stat + corr vs funding-level(防重包)。
**結果**(8 幣、2912 天):
| 檢定 | 值 | 判定 |
|---|---|---|
| Pool linear t | −1.53 | ❌ |
| Pool quantile t | −0.77 | ❌ |
| spread vs funding-level corr | 大型幣 0.50-0.60 / **alt 0.95-0.997** | 重包 |

**結論**:❌ **雙重失敗(不顯著 + 冗餘)**。spread 對 alt ≈ coin funding(BTC funding 量級相對小、減掉幾乎不改變),不是獨立信號。
**學到**:裸 spread 不行;市場中性的 funding 信號要去相關得用「**跨幣相對排名 / z-score 標準化**」而非裸減。再次印證 [[feedback_structural_alpha_requires_x_uncorrelated_with_price]] —— 新信號先過「跟既有源 corr<0.3」這關、裸 spread 過不了。
(順帶:pool quantile 確認已部署信號仍活 — Retail L/S Δ t=−3.64、Retail L/S t=−2.08、funding t=−2.05。)

---

## 2026-05-29 第五波 — xsec 價格動量「衰減」成因診斷(regime 依賴、非擁擠)

(注:這支是 `tools/xsec-factor` 的**價格動量排名**因子 Sharpe 1.28/t=2.40,**不是**上面第三波2 那條已 shelve 的 oi 相對擁擠探勘。兩者同叫「cross-sectional」但不同信號。)

**問題**:[[xsec-momentum-factor]] split-half 顯示前半 Sh 1.83 → 後半 0.14 衰減。先前歸因「經典擁擠因子 / alpha decay」。但「擁擠」是強假設、會誤導決策(擁擠=退役;regime 依賴=擇時/閘控)。要用資料分清成因。
**改動**:`strat-validate --xsmom` 加切 4 等分衰減診斷 — 逐段比 **net / gross / 同期等權 B&H 的 Sharpe**。三種成因可分辨:① gross 也降=真 alpha 衰減 ② gross 穩、只 net 降=成本 ③ 衰減跟著 B&H 走=regime/離散度依賴。
**結果**(lookback20/rebal7、20 幣含 2022):

| 區段 | net Sh | gross Sh | 成本拖累 | 同期 B&H Sh |
|---|---:|---:|---:|---:|
| Q1 | 2.02 | 2.06 | 0.04 | 2.26 |
| Q2 | 1.67 | 1.73 | 0.06 | 0.96 |
| Q3 | 0.41 | 0.49 | 0.08 | −0.08 |
| Q4 | −0.11 | −0.04 | 0.07 | −1.22 |

**結論**:**不是擁擠** —— ① 成本拖累全程 ~0.05 恆定(非成本)② gross 跟 net 一起崩(非單純成本)③ **gross 崩與同期大盤崩【完全同步】**(因子 net 1.83→0.14、大盤 B&H 1.56→−0.60)= **regime/離散度依賴**:相關性升高的熊市裡橫斷面離散度消失、沒 spread 可吃,XS 動量結構性失效。
**決策**:不退役、保留小權重去相關 sleeve(corr decorr4 僅 0.22),但**裸跑會在相關熊市流血**(Q4 net 轉負)。

**離散度閘修復測試(同 session 直接驗、不留懸念)**:加外生閘 = 橫斷面 lookback 報酬離散度 ≥ 自身 trailing 90 日中位才在場(門檻固定、嚴格 t 之前、絕不 fit 後半;離散度外生於因子權益、不犯 [[feedback_self_referential_regime_overlay]]):

| | ungated ret/Sh/DD | gated ret/Sh/DD |
|---|---|---|
| 全期 | 380% / 1.08 / 40% | 103% / 0.70 / 35% |
| 後半 | −5% / 0.14 / 37% | −8% / −0.03 / 29% |

**❌ 閘不划算**:中位切縮倉(在場 46%)砍掉牛市、全期 Sharpe 1.08→0.70;後半 DD 只削 37→29% 但 risk-adj 反更差(−5%→−8%、Sh 0.14→−0.03)。離散度中位切不是有效擇時信號。
**最終決策**:**不加因子層閘**;這條當**小權重裸 sleeve**,靠【組合層防禦】(CB/DD-aware overlay,已驗)+ 去相關紅利(corr 0.22)。更花俏的 regime 信號或許行,但簡單離散度閘已證實不付費。
**學到**:① 「衰減」先別跳「擁擠」—— 切段比 net/gross/同期大盤,分清擁擠 vs 成本 vs regime(處方各異:退役/調 rebal/加閘)。② fair-weather 因子別急著加擇時閘 —— 中位切縮倉的踏空成本常 > 省下的 DD;**組合層防禦 + 去相關**通常比因子層擇時更划算。
