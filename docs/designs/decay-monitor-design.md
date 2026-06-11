# Decay Monitor 設計(漸進式 edge 衰退早期預警)

2026-06-11。目的:在 edge「慢慢退」時早期察覺(現有 `StrategyHealthMonitor` 只抓「災難性崩壞」)。

## 動機 / Gap
| 既有 | 抓什麼 | 缺口 |
|---|---|---|
| `StrategyHealthMonitor` | 連虧 N / winrate<30% → 自動 pause | 只抓**災難**(崩了才停) |
| Shadow scanner report | live 勝率 vs backtest 預期、偏離 | **看整體、非 regime-aware**(忽略市況) |
| **(新)Decay Monitor** | **regime 校正後的漸進衰退** | 補早期預警 |

## 核心創新:regime-aware baseline
策略的「該有表現」依市況不同(見 research 的 regime 簽名:同一策略在高波動 vs 低波動、盤整 vs 趨勢、報酬差數倍)。
→ **比較 live 表現要「同市況比同市況」**,不是拿整體 live 勝率比整體 backtest(會被市況組合誤導:最近剛好都低波動 → 表現低是正常、不是衰退)。

## 三層監測
- **T1 災難硬停(沿用現有)**:連虧 N / winrate<門檻 → 立即 pause。
- **T2 漸進衰退(新)**:rolling 最近 N 筆 closed(N~20-30)的 **regime-校正實現報酬** 持續 < baseline 的 Y%(如 50%)→ 告警 + 建議減倉(非自動 pause,人決定)。
- **T3 甜蜜點檢查(最強訊號)**:策略在「它最該強的 regime」表現 < baseline → 最清楚的衰退紅旗。例:harmonic 的甜蜜點是高波動-盤整(見 research regime 簽名),若在那裡都賺不到 = edge 真退,不是市況問題。

## 指標計算
1. 每筆 closed trade 標記其進場時的 regime(vol 高低 / ER 趨勢度 / direction)。
2. 該 regime 的 baseline = research 的 regime 簽名(per 策略 per 市況的預期報酬/勝率)。
3. `decay_score = rolling_realized / regime_expected`(同市況下實現 / 預期)。
4. 持續(連續 K 個 rolling 窗)< 門檻 → 觸發對應層級。

## 資料 + 告警
- **資料**:消費 broker closed trades(擴充現有 shadow report 的 live-vs-backtest 比較、加上 regime tag + baseline)。
- **告警**:走現有 Discord digest 通道(每日健康彙整加一段 decay 狀態)。
- **baseline 來源**:research 的 regime 簽名表(per 策略 per 市況);harmonic 已有(跨 crypto/美股/商品驗證一致)。

## 部署依賴 / 順序
- **harmonic shadow 需累積 ≥N closed**(目前 0、累積中)→ T2/T3 要等資料。
- **live 真錢書(ma_regime 等)有歷史 closed** → 可先上 T1/T2、驗證監測管線。
- 實作:擴充 `AutoTraderService` 的 shadow report 計算(加 regime tag + baseline 比較)+ digest 輸出一段。

## 與既有的關係
不取代 `StrategyHealthMonitor`(T1 硬停留著);Decay Monitor 是疊加的「漸進預警層」(T2/T3),給人「edge 開始退」的早期訊號去主動減倉,而非等災難硬停。
