# tsmom_btcNotUp Scanner 部署設計 — 待 next session 動工

**寫於**:2026-05-27(C 路線 regime filter 發現後)
**狀態**:設計、未實作。預估 ~2h 工程

## 為什麼上這個

[BtcRegimeFilterStrategy](../../packages/csharp/workers/strategy-worker/Engine/BtcRegimeFilterStrategy.cs) A/B 結果(見 [HarmonicResearch-Log.md](../reports/HarmonicResearch-Log.md) C 段):

- **`tsmom_btcNotUp`(只在 BTC sideways/down 開倉)**:Sharpe 0.66 → **0.82(+0.16)** ✅
- 機制:tsmom 在 BTC up regime Sharpe 只 0.31(進場晚被 SL 殺)、過濾掉真實受益
- 其他 2/3 策略(scan10/widepz)套 regime filter 反而傷,只有 tsmom 受益

## 部署的卡點

**現行 scanner 走 strategy.signal worker、payload 只給 target symbol 的 bars**:
- `BtcRegimeFilterStrategy` 需要 BTC bars 才能判 regime
- 沒注入 BTC bars → wrapper 預設 pass-through(等於 baseline、白費)

## 兩條路

### Path A:strategy-worker 內部抓 BTC(較重)

工程:
1. strategy-worker 新增 quote-worker dependency / direct binance fetch
2. 在 evaluate "tsmom_btc_not_up" 時、worker 自己拉 BTC bars + cache
3. 注入 BtcRegimeFilterStrategy.BtcBarsRef

問題:
- strategy-worker 變成依賴 quote-worker / 外部 API、違背「無狀態 evaluator」設計
- BTC fetch cache 同步問題(stale BTC bars → 錯誤 regime)

### Path B:broker scanner 拉 BTC、payload 注入(較輕、推薦)

工程:
1. **broker** `ProcessScannerAsync` / `DispatchScannerLegAsync`:
   - 多 fetch 一次 BTC bars(同 scanner.interval、limit 200)
   - 加入 signal payload:`["ref_btc_bars"] = btcBars`
2. **strategy-worker** evaluate handler:
   - 收 payload 時、解析 `ref_btc_bars`
   - 設 `BtcRegimeFilterStrategy.BtcBarsRef = parsedBtcBars`(thread-local 或 AsyncLocal、避免 cross-request race)
3. **strategy-worker registry**:
   - 註冊新 strategy name `tsmom_btc_not_up`、wrapper TsMomentumStrategy with allowedRegimes=["sideways","down"]
4. **scanner_legs seed**:
   - 新 scanner `tsmom_btcnotup_scanner`、strategy=tsmom_btc_not_up
   - Universe 跟 tsmom_1d 一樣或互補

工程估時:1.5-2h(broker 1h + worker 0.5h + seed 5min)

## 推薦做法

**Path B**、理由:
- broker 已知道 scanner.interval、自然加 BTC fetch
- payload 注入避免 strategy-worker 變胖
- BtcBarsRef 改 AsyncLocal 避免 worker 多請求 race

## 部署順序

1. 先實作 Path B 工程
2. 加 scanner seed shadow=1
3. Run 7 天看 shadow 累積、Sharpe 與 backtest 接近(0.82 ± 0.2)
4. 達標升 live(同其他 scanner 流程)

## 風險 / 注意

- ⚠ BTC bars fetch 失敗 → BtcBarsRef=null → wrapper pass-through(等於 baseline、不報錯但沒 filter)
- ⚠ regime 判定有 EMA cross lag、極端切換期可能誤判
- ⚠ Live 部署前必跑 with-funding A/B(D 路線已備好工具)

## Done 條件

- [ ] broker 加 BTC fetch + payload 注入
- [ ] strategy-worker handle ref_btc_bars + 註冊 tsmom_btc_not_up
- [ ] BtcBarsRef 改 AsyncLocal
- [ ] scanner seed 新增 tsmom_btcnotup_scanner
- [ ] VPS deploy + 驗證第一次 sweep
- [ ] log 出 [BTCreg:up/sideways/down] 標記、確認 filter 真實作用

## 參考

- 設計來源:[BtcRegimeFilterStrategy](../../packages/csharp/workers/strategy-worker/Engine/BtcRegimeFilterStrategy.cs)
- A/B 數據:[HarmonicResearch-Log.md](../reports/HarmonicResearch-Log.md) 「C 路線」段
- 紀律:[[feedback_descriptive_vs_prescriptive_filter]] — 提醒 deploy 後再 A/B 驗
