# Funding Rate Impact 分析 — 2026-05-27

## TL;DR

部分 perp 幣 funding rate **量級巨大**(APT 長單 +19.8%/年、ATOM +12.4%/年)、現 shadow PnL 完全沒計、嚴重低估某些幣的真實 alpha。Engine integration 是必補功課(P5 task)、可顯著改變策略排名。

## 為什麼是必補

shadow basket 4 週倒數中、計算 shadow PnL 給「升 live」決策用。
若 PnL 算錯 ±10%、升 live 決策很可能誤判:
- APT 長單看起來不夠強、其實 funding 補助下真 alpha 大
- LINK 長單看起來強、實際 funding drag 後沒那麼好

## 數據來源

`quote-worker` 已實作 `FetchFundingRateDeepAsync`(packages/csharp/workers/quote-worker/History/HistoricalDataFetcher.cs)、抓 Binance fapi/v1/fundingRate 並存到 quote.db `funding_rate` 表。
VPS quote.db 已有 16 主場幣的 funding 歷史(2025-06 ~ 2026-05、~1000 點/幣 = ~333 天 × 3 funding/day)。

## 完整量級表(16 主場幣 × avg funding × 11 個月)

| 幣 | avg/8h | 年化(長單成本) | 做多影響 | 備註 |
|---|---:|---:|---|---|
| LINK | +0.0047% | **+5.1%** | 高 drag | scan10 universe 之外 |
| LTC | +0.0037% | +4.0% | 中 drag | scan10/widepz 主場 |
| SUI | +0.0035% | +3.8% | 中 drag | 真錢 dual_mom + scan10 |
| ADA | +0.0033% | +3.6% | 中 drag | decorr5 scanner |
| BTC | +0.0032% | +3.5% | 中 drag | 真錢 decorr4_ls |
| ETH | +0.0027% | +2.9% | 中 drag | 真錢 mfi |
| XRP | +0.0017% | +1.8% | 微 drag | tsmom_1w universe |
| BNB | +0.0012% | +1.3% | 微 drag | 真錢 ma_regime |
| OP | +0.00023% | +0.25% | 忽略 | scan10/widepz universe |
| AVAX | ~0% | ~0% | 中性 | tsmom_1d、decorr5 |
| SOL | −0.0008% | −0.8% | 微 boost 做多 | 真錢 dual_thrust |
| TRX | −0.0025% | −2.7% | 中 boost 做多 | tsmom 1d/1w 都 active long |
| DOT | −0.0048% | −5.3% | 高 boost 做多 | decorr5 universe |
| INJ | −0.0077% | **−8.5%** | 大 boost 做多 | scan10/widepz universe |
| ATOM | −0.0113% | **−12.4%** | 巨 boost 做多 | decorr5 universe |
| APT | **−0.0181%** | **−19.8%** ⭐⭐⭐ | 史詩級 boost 做多 | scan10/widepz/tsmom_widepz 多 scanner 都有 |

## 對 7 scanner 的 PnL 修正預估

| Scanner | 主要會被低估/高估的腿 | 修正方向 |
|---|---|---|
| scan10_scanner | INJ/APT 長單 +8-20%/年 boost、LINK/LTC 長單 -4-5% drag | 真 PnL 顯著高於 shadow 顯示 |
| widepz_scanner | 同上(同 universe) | 同上 |
| top2_widepz_scanner | 同上(同 universe sub-set) | 同上 |
| tsmom_1d_scanner | TRX 長單(現 active)+2.7%/年 boost | TRX 真 PnL 高於 shadow |
| tsmom_1w_scanner | TRX 長單(active)+2.7%、XRP 空單(active)−1.8% | 微正 |
| tsmom_widepz_scanner | APT 長單 +19.8% boost、LTC drag | 顯著高估 |
| decorr5_scanner | ATOM/DOT 長單 +5-12% boost | 高於 shadow |

**整體推測**:
- 目前 7 scanner shadow 跑、4 週後評估若用無 funding PnL,可能誤判某些策略表現
- 特別是 alt-coin 主場的 scanner(scan10/widepz/decorr5)極可能被低估 alpha

## 工程現況

- ✅ `BacktestEngine`(舊、long-only)已支援 `applyFunding=true` 參數 + `bars[i].FundingRate`
- ❌ `LongShortBacktestEngine`(現用、雙向)**沒接** funding
- ✅ quote.db 已有 16 主場幣的 funding 歷史
- ❌ funding 沒注入 BarData(現 backtest bars.FundingRate=null)

## 完整整合工程計劃(估 2-3 小時)

### Step 1:funding 資料管道(0.5h)
- 加 `ToolsShared.FundingCache` — fetch from Binance API or quote.db sync
- 提供 `Dictionary<DateTime, decimal>` per symbol、interface 給 backtest 用

### Step 2:bars 注入 funding(0.5h)
- 在 `KlineCache.FetchOrLoad` 後、align funding 到每根 daily bar
- 一根 daily bar 通常含 3 個 8h funding 事件、求和或 avg

### Step 3:`LongShortBacktestEngine` 加 funding 支援(1h)
- 複製 `BacktestEngine` 的 funding 計費邏輯(line 73, 162, 206, 241)
- **關鍵差異**:short 方向 funding sign flip(positive funding → short 付、long 收)
- 加 `bool applyFunding = false` opt-in 參數

### Step 4:驗證 + 重跑(1h)
- `--validate-funding-impact` mode:同策略 with/without funding A/B
- 主要看 APT/INJ/ATOM(大 funding 幣)有多大 PnL 差
- 預期:long-heavy 策略在 APT/INJ/ATOM 上 Sharpe 顯著 ↑

### Step 5:Shadow 評估 SOP 更新
- 4 週後評 shadow PnL 時、必用 with-funding 版
- 加進 [[project_real_money_live_2026_05_23]] 的升 live checklist

## Actionable now

不需等完整整合、可先做:
1. 把 APT 加進更多 scanner universe(scan10/widepz 已有、考慮也加 decorr5)
2. APT 長單 sub-strategy 是不是值得單獨抽出來研究(funding alpha 太大)
3. Notify shadow 評估時、手動加 funding adjustment(粗估每 scanner 主場幣 funding × 持倉天數)

## 參考

- [[feedback_walkforward_vs_pool_tstat]] — 紀律:任何 shadow 評估必須用真實成本(funding 是其中之一)
- `tools/strat-validate/Program.cs` line 13(目前 funding 用 env 假設 0.01%/8h、與 APT 真實 −0.018%/8h 反向錯誤)
