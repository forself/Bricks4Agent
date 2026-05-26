# Portfolio Scanner Hybrid — 混合制配置設計

**寫於**:2026-05-26 晚
**作者**:Anthony + Claude
**狀態**:設計階段、短期不動 AutoTrader、Phase 2 才實作

---

## 0. TL;DR

把現行「**1 watch = 1 coin × 1 strategy 固定配對**」模式擴成混合制:
- **核心腿(Mode B、固定配對)** 提供 decorr 骨幹、不動
- **Scanner 腿(Mode A 限定版)** 給 t-stat 顯著的「王牌策略」一個幣池、池內信號驅動

目的:既不浪費王牌策略的 edge(現在 widepz 在 OP/ADA/INJ 強但沒部署)、又不放棄 decorr(核心腿照跑)。

---

## 1. 動機 — 為什麼現行模式不夠

### 1.1 現行(Mode B)的痛點

`portfolio.json` 是「coin → strategy」字典:
```json
{ "BTC": "decorr4_ls", "ETH": "mfi", "SUI": "dual_mom_ls", ... }
```

每腿是「coin × strategy × budget」三元組。研究進化時:
- 發現 `harm_prz_scan10_widepz` (t=5.54) 在 OP / ADA / INJ / NEAR / LTC 都強(1d OOS 15-28%)
- 但 OP / ADA / INJ / NEAR 沒在 portfolio.json 裡 → **沒部署、edge 浪費**
- 要部署就得幫每個幣決定哪個策略、加新腿、改 budget → **配置摩擦**

### 1.2 純信號驅動(Mode A)的危險

如果直接讓 `harm_prz_scan10_widepz` 掃 20 幣、信號驅動下單:
- 同時段多幣 PRZ 同方向 = 集中爆倉風險
- XRP / BTC 等 widepz 邊際/負 edge 的幣也會被打到
- 策略 edge 死了 = 所有腿同時死(無 decorr)
- A/B 不同策略時撞幣池

### 1.3 混合制(Mode C)的取捨

把 Mode A 的「信號驅動」限定在「策略主場幣池」內:
- **池外不碰**(XRP / BTC 不會被 widepz 打到)
- **池內最多 N 腿**(集中爆倉 bound)
- **核心 6 腿維持**(decorr 骨幹不變)
- **多 scanner 並列**(每 scanner 一個策略 + 一個池)

---

## 2. 數據基礎(2026-05-26 t-stat 後)

### 2.1 通過 pool t-stat 的王牌(t ≥ 4、可當 scanner 主策略)

| Rank | 策略 | t-stat | pool mean% | 適合場景 |
|---:|---|---:|---:|---|
| 1 | `harm_prz_scan10` | **5.93** | 10.6 | TP-driven、低頻、1d |
| 2 | `harm_prz_scan10_widepz` | **5.54** | 17.0 | 同上、寬 PRZ 版 |
| 3 | `harm_prz_top2_scan10_widepz` | 4.82 | 12.4 | 限 butterfly / five_o |
| 4 | `tsmom_widepz` | 4.60 | 14.8 | ts_momentum + widepz combo |
| 5 | `harm_prz_top2_scan10` | 5.12 | 8.3 | 限 butterfly / five_o(窄) |
| 6 | `ts_momentum` | 4.10 | 12.9 | trend follow |
| 7 | `tsmom_harm_prz_scan10` | 4.19 | 13.6 | 組合 |
| 8 | `harm_prz_top2_scan5` | 4.71 | 6.7 | scan 縮窄 |

### 2.2 主場幣池(以 `harm_prz_scan10_widepz` 為例)

從 2026-05-26 跨幣 deep dive(`/tasks/bt5xoiu48.output`):

| 幣 | 1d OOS% | 跨時框正/5 | 評級 |
|---|---:|---:|---|
| OP | **28%** | 4/5 | A+(主推) |
| APT | 27% | 2/5 | B(僅 1d 強、觀察單) |
| SUI | 25% | 4/5 | A(已被核心腿 dual_mom_ls 占、避開) |
| ADA | 22% | 4/5 | A+ |
| INJ | 18% | 4/5 | A |
| NEAR | 15% | 4/5 | A |
| LTC | 5% | **5/5** | 跨時框冠軍 |
| BTC/ETH/BNB/UNI 等 | ≤ 2% | ≤ 3/5 | D(避開) |
| XRP | **−1%** | 1/5 | 黑名單 |

→ 主場池 = `[OP, ADA, INJ, NEAR, LTC]` + observation `[APT]`

---

## 3. 設計

### 3.1 概念模型

```
Portfolio
├── 核心腿(Mode B)               ← decorr 骨幹、不動
│   ├── BTC × decorr4_ls    [120%]
│   ├── BNB × ma_regime     [60%]
│   ├── SUI × dual_mom_ls   [35%]
│   ├── SOL × dual_thrust   [30%]
│   ├── UNI × ts_momentum   [30%]
│   └── ETH × mfi → 換腿待定 [25%]
│
└── Scanner 腿(Mode A 限定版)   ← 新增、王牌策略
    ├── widepz_scanner
    │   ├── strategy: harm_prz_scan10_widepz
    │   ├── universe: [OP, ADA, INJ, NEAR, LTC]
    │   ├── budget_total: 50%
    │   ├── max_concurrent: 2 legs
    │   └── per_leg_cap: 25%
    │
    └── scan10_scanner(後續加)
        ├── strategy: harm_prz_scan10
        ├── universe: [LTC, OP, ADA, ATOM, ...]   ← 待 deep dive
        ├── budget_total: 30%
        ├── max_concurrent: 2 legs
        └── per_leg_cap: 15%
```

### 3.2 Scanner leg 運作流程

```
每 sweep(目前 300s)
  ↓
For each scanner (widepz, scan10, ...):
  1. 拉 universe 內所有幣的 bars
  2. 把策略對每幣跑一遍 GenerateSignal
  3. 收集所有 "signal != Hold" 的 (coin, signal) 對
  4. 按優先順序(信號 confidence、coin rank、time-since-last-trade)排
  5. 在 max_concurrent 限制內、per_leg_cap 限制內 dispatch
  6. 該 scanner 已開倉的 leg 不重複(同 coin 同方向已開 → skip)
```

關鍵約束:
- **池外幣絕對不打** — universe 是強制 whitelist
- **同 scanner 最多 N 腿** — 防集中爆倉
- **與核心腿幣不交集** — 防 BingX 撞單(見 §5.2)
- **每腿仍走 approval gate** — 真錢 governance 不繞過

### 3.3 portfolio.json 擴充 schema

```json
{
  "core_legs": [
    { "symbol": "BTCUSDT", "strategy": "decorr4_ls", "budget_pct": 120, "mode": "perp_both" },
    { "symbol": "ETHUSDT", "strategy": "mfi", "budget_pct": 25, "mode": "perp_long_only" }
  ],
  "scanner_legs": [
    {
      "name": "widepz_scanner",
      "strategy": "harm_prz_scan10_widepz",
      "universe": ["OPUSDT", "ADAUSDT", "INJUSDT", "NEARUSDT", "LTCUSDT"],
      "budget_total_pct": 50,
      "max_concurrent_legs": 2,
      "per_leg_cap_pct": 25,
      "mode": "perp_both",
      "interval": "1d"
    }
  ]
}
```

向後相容:沒 `scanner_legs` 就退化成現行 Mode B。

---

## 4. AutoTrader 工程改動 spec

### 4.1 現況

`AutoTraderService.Sweep()` 流程(packages/csharp/broker/Services/AutoTraderService.cs):
1. 讀 `watches` 表(每 row 一個 fixed watch:symbol + strategy + budget)
2. For each watch:
   a. 拉 bars
   b. 跑 strategy.GenerateSignal
   c. 訊號 != Hold → 走 approval gate → 下單
3. 沒 universe / scanner 概念

### 4.2 改動清單

**Step A — 加新表 `scanner_legs`**
```sql
CREATE TABLE scanner_legs (
  id              TEXT PRIMARY KEY,
  name            TEXT UNIQUE NOT NULL,
  strategy        TEXT NOT NULL,
  universe        TEXT NOT NULL,   -- JSON array of symbols
  budget_total    INT NOT NULL,
  max_concurrent  INT NOT NULL,
  per_leg_cap     INT NOT NULL,
  mode            TEXT NOT NULL,
  interval        TEXT NOT NULL,
  enabled         INT NOT NULL DEFAULT 1,
  created_at      TEXT NOT NULL,
  updated_at      TEXT NOT NULL
);

CREATE TABLE scanner_active_legs (
  id              TEXT PRIMARY KEY,
  scanner_id      TEXT NOT NULL REFERENCES scanner_legs(id),
  symbol          TEXT NOT NULL,
  opened_at       TEXT NOT NULL,
  entry_signal    TEXT NOT NULL,
  -- 鏡像 watches 的 lifecycle 欄位(SL/TP/peak/...)
  ...
);
```

**Step B — `AutoTraderService.Sweep()` 加 scanner pass**
```csharp
async Task SweepAsync() {
    // 既有 fixed watches
    foreach (var w in fixedWatches) {
        await ProcessFixedWatchAsync(w);
    }

    // 新:scanner legs
    foreach (var scanner in scannerLegs.Where(s => s.Enabled)) {
        await ProcessScannerAsync(scanner);
    }
}

async Task ProcessScannerAsync(ScannerLeg sc) {
    var active = activeLegsByScanner[sc.Id];
    if (active.Count >= sc.MaxConcurrent) return;   // 滿了

    var candidates = new List<(string symbol, Signal sig, decimal conf)>();
    foreach (var sym in sc.Universe) {
        if (active.Any(a => a.Symbol == sym)) continue;   // 已開、不重複
        if (CoreLegsHave(sym)) continue;                  // 核心腿占的、避開
        var bars = await GetBarsAsync(sym, sc.Interval);
        var sig = sc.Strategy.GenerateSignal(bars);
        if (sig.Side != Hold) candidates.Add((sym, sig, sig.Confidence));
    }

    var picks = candidates
        .OrderByDescending(c => c.conf)
        .Take(sc.MaxConcurrent - active.Count);

    foreach (var (sym, sig, _) in picks) {
        var notional = Math.Min(sc.PerLegCap, sc.BudgetTotal - active.Sum(a => a.Notional));
        if (notional <= 0) break;
        await DispatchScannerLegAsync(sc, sym, sig, notional);   // 走既有 approval gate
    }
}
```

**Step C — 管理 UI**

在 `trading-manage.html` 加「Scanner Legs」分頁:
- 列出 scanner_legs(name / strategy / universe / budget / active count)
- 編輯(strategy 下拉、universe multi-select、budget number、enable toggle)
- 即時顯示 active_legs(scanner_id × symbol × pnl)

**Step D — Approval flow 相容**

Scanner 的單也走 `IApprovalService` chain(現有 four-tier decorator chain、不繞過)。
Approval 對話需顯示「來自 widepz_scanner、universe ADA」、不是「來自 watch_ADAUSDT_xxx」。

### 4.3 工程估算

| 工作項 | 估時 | 風險 |
|---|---:|---|
| Schema 加 2 表 + migration | 1d | 低 |
| AutoTraderService scanner pass | 2-3d | 中(要小心 transaction、避免 [[feedback_baseorm_async_local]] 重演) |
| 管理 UI | 1-2d | 低 |
| Approval flow 整合 | 1d | 中(four-tier chain 別漏層) |
| 整合測試 + shadow 驗證 | 2d | 中 |
| **總計** | **7-9 天** | 中 |

---

## 5. 已知約束 & 風險

### 5.1 BingX 持倉模型(同標的多策略撞單)

詳見 [[reference_bingx_position_model_api]]:
- 同 BingX 帳號同 symbol 只有 1 個 position(全倉)或 1 對 long/short(逐倉 / hedge)
- 即使 hedge 模式也只分多空 2 個槽、不分策略
- **唯一自動化解 = 子帳戶**(每子帳戶各跑自己的策略)

對 scanner 影響:
- ✅ Scanner universe 與核心腿幣不交集 → 不會撞單(目前 widepz universe `[OP, ADA, INJ, NEAR, LTC]` 已避開核心 `[BTC, BNB, SUI, SOL, UNI, ETH]`)
- ⚠ 兩個 scanner 共用幣 → 還是會撞(例:widepz_scanner + scan10_scanner 都掃 LTC → 同時觸發會撞)
- 解法 1:兩 scanner universe 也不交集(設計時硬切)
- 解法 2:scanner 之間加優先級(t-stat 高的優先、低的 skip)
- 解法 3:子帳戶分離(每 scanner 一個子帳戶、無撞單)— 長期方向

### 5.2 真錢動作必須冪等

詳見 [[feedback_real_money_idempotency]]:
- Scanner dispatch 一定要冪等 lock(idempotency key 用 `scanner_id + symbol + signal_bar_ts`)
- 同一根 K 棒不重複下單
- Approval gate 通過後 dispatch 之前也要 lock(approve-and-dispatch 是熱區)

### 5.3 Pool t-stat 必須先過(紀律)

詳見 [[feedback_walkforward_vs_pool_tstat]]:
- 任何新 scanner 啟用前、策略必須過 `strat-validate` pool t-stat(t ≥ 3)
- Universe 內每幣的 (策略 × 幣) walk-forward 數字只當「主場排序」用、不當「上線決策」
- 已通過 t-stat 的策略才有資格當 scanner 主策略

### 5.4 全倉 vs 逐倉 / 槓桿與有效槓桿

詳見 [[feedback_effective_leverage_is_real_risk]]:
- Scanner 開新腿時、「max_concurrent × per_leg_cap」加上核心腿 budget,**總名目曝險不能超過權益 1×**
- 例:核心 300% + scanner 50% = 350% notional / equity(假設 5x 真槓桿)= 70% 真實 leverage,還是危險
- 設計時 budget 數字必須以「真實 leverage = notional × bingx_leverage / equity」計、不是 budget_pct 直接相加

---

## 6. 上線路徑(時間表)

### Phase 0(下個月、不動 AutoTrader)
- 用 strat-validate / 外部腳本模擬 scanner 邏輯(paper trade)
- 每天記錄 widepz scanner 在 [OP, ADA, INJ, NEAR, LTC] 的「會打哪幣」+ 模擬 PnL
- 累積 4 週數據、驗證 shadow PnL 接近回測 mean(17%)

### Phase 1(2-3 個月)
- 工程實作 AutoTrader scanner 支援(§4.2)
- 上線第一個 scanner:`widepz_scanner`,budget 30%(保守、半 budget 起跑)
- 核心 6 腿維持不動
- Shadow → live 流程同 [[project_real_money_live_2026_05_23]] 的紀律
- 觀察 1 個月

### Phase 2(4-6 個月)
- 加第二個 scanner:`scan10_scanner` 或 `ts_momentum_scanner`
- 兩 scanner universe 不交集(或加優先級)
- 總 scanner budget ~60-80%
- 核心腿縮到 50% 以下、scanner 為主、核心為輔

### Phase 3(子帳戶、長期方向)
- 每個 scanner 開獨立 BingX 子帳戶
- 無撞單問題、可隨意擴 scanner
- 風險完全隔離

---

## 7. 升 live 決策表(per scanner)

任何 scanner 升 live 都必須過全部以下閘:

| 閘 | 標準 |
|---|---|
| 1. Pool t-stat | t ≥ 3、95% CI 下界 > 0(strat-validate 確認) |
| 2. 跨幣 deep dive | universe 內每幣 1d OOS ≥ 10% 或跨時框 ≥ 4/5 正 |
| 3. Shadow 期 | 4 週 paper trade、總 PnL ≥ +5%、單腿 DD < 20% |
| 4. 相關性 | scanner 內 active legs 之間 |ρ| < 0.6 |
| 5. 用戶授權 | 明確簽核(不是「繼續」、要「OK 上 live」) |
| 6. 初始配重 | 目標 budget 的 50% 起跑、跑 1 個月再考慮回到 100% |

---

## 8. 待決事項(請用戶 review)

- [ ] Scanner universe 用「跨時框 ≥ 4/5 正」標準、還是「1d OOS ≥ 15%」標準?(目前草案兩個都用、取聯集)
- [ ] APT(1d 27% 但跨時框 2/5)放 universe 還是只觀察?(草案:觀察單、不放)
- [ ] 第二支 scanner 用 `harm_prz_scan10`(t 最高)還是 `ts_momentum`(非諧波、機制 decorr)?(我傾向 ts_momentum、跨機制 decorr 更強)
- [ ] Scanner budget 上限要設多硬?(草案:單 scanner ≤ 50%、所有 scanner 總和 ≤ 80%)
- [ ] 子帳戶要不要 Phase 2 就引入、還是等真有撞單再說?(子帳戶開通有 BingX 流程、需時間)

---

## 9. 不在本設計範圍

- ❌ 子帳戶 BingX API 整合(獨立設計、見 [[reference_bingx_position_model_api]])
- ❌ Strategy 自身的指標 / 參數(這是策略研究範疇、不是配置架構)
- ❌ Vol-targeting / risk parity 配重(獨立議題、與 scanner 正交)
- ❌ Approval gate 內部機制(已存在、scanner 接到既有 IApprovalService 即可)

---

**參考**:
- [[project_quant_direction_pro]] — 去相關 / vol-target / 算成本 / 活著優先主軸
- [[project_real_money_live_2026_05_23]] — 真錢上線六層 blocker + 操作態
- [[reference_bingx_position_model_api]] — BingX 持倉模型 / API 限制
- [[feedback_walkforward_vs_pool_tstat]] — 換腿必須先過 pool t-stat 紀律
- [[feedback_real_money_idempotency]] — 真錢 endpoint 必須冪等鎖
- [[feedback_effective_leverage_is_real_risk]] — 全倉真風險 = 有效槓桿
- `docs/reports/PortfolioOptimizationReview-2026-05-26.md` — 今天的 t-stat verdict + shadow basket 草案
- `docs/reports/HarmonicResearch-Log.md` — H1-H21 諧波研究完整紀錄
