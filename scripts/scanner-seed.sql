-- Scanner Legs 初始 seed(2026-05-27 Phase 1 + 2026-05-27 下午擴 7 支)
--
-- 對應設計:
--   - docs/designs/portfolio-scanner-hybrid.md
--   - docs/reports/ScannerUniverse-2026-05-27.md
--   - docs/reports/PortfolioOptimizationReview-2026-05-26.md(t-stat 25 顯著名單)
--
-- 7 個 scanner 全部 shadow=1 啟動(只記不下單)、shadow 4 週紀律期過後再升 live。
-- INSERT OR REPLACE 是 idempotent — 重複跑會以新值覆寫(改 universe / budget 等隨時改)。
--
-- 跑法:
--   sqlite3 ~/broker.db < scripts/scanner-seed.sql
-- (broker.db 路徑視部署位置,常見:VPS /data/broker.db、本機 ~/Brick4Agent/broker.db)
--
-- 預算總計:190 USDT(BingX equity ~347 的 ~55%、預留 headroom 防誤切 real 爆倉)
--
-- Universe 選擇依據:跨時框 ≥4/5 ∪ 1d ≥15%
--   harm-prz 系列  : LTC/OP/NEAR/INJ/APT/SUI/ARB(scan10 主場)
--   top2 (butterfly+five_o): LTC/OP/INJ/APT/SUI(top-2 pattern 更稀缺、縮小池)
--   tsmom_1d        : TRX/SUI/AVAX(1d alt-coin trending)
--   tsmom_1w        : BTC/BNB/XRP/TRX(週線 large-cap)
--   tsmom_widepz    : LTC/OP/APT(雙條件主場)
--   decorr5         : ADA/AVAX/DOT/LINK/ATOM(中型幣、避 alt-coin 衝突)

INSERT OR REPLACE INTO scanner_legs
  (id, name, strategy, universe,
   budget_total, max_concurrent, per_leg_cap,
   mode, interval, leverage, shadow, enabled,
   owner_principal_id, created_at, updated_at)
VALUES
  -- 起步 4 個(Phase 1 P3 入)
  ('scan10_scanner', 'scan10_scanner', 'harm_prz_scan10',
   '["LTCUSDT","OPUSDT","NEARUSDT","INJUSDT","APTUSDT","SUIUSDT","ARBUSDT"]',
   30, 3, 10,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  ('widepz_scanner', 'widepz_scanner', 'harm_prz_scan10_widepz',
   '["LTCUSDT","OPUSDT","NEARUSDT","INJUSDT","APTUSDT","SUIUSDT","ARBUSDT"]',
   30, 3, 10,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  ('tsmom_1d_scanner', 'tsmom_1d_scanner', 'ts_momentum',
   '["TRXUSDT","SUIUSDT","AVAXUSDT"]',
   20, 2, 10,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  ('tsmom_1w_scanner', 'tsmom_1w_scanner', 'ts_momentum',
   '["BTCUSDT","BNBUSDT","XRPUSDT","TRXUSDT"]',
   30, 3, 10,
   'spot', '1w', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  -- 2026-05-27 下午擴 3 個(跨機制 + t-stat ≥ 3.2 顯著)
  ('top2_widepz_scanner', 'top2_widepz_scanner', 'harm_prz_top2_scan10_widepz',
   '["LTCUSDT","OPUSDT","INJUSDT","APTUSDT","SUIUSDT"]',
   30, 3, 10,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  ('decorr5_scanner', 'decorr5_scanner', 'decorr5_scan10',
   '["ADAUSDT","AVAXUSDT","DOTUSDT","LINKUSDT","ATOMUSDT"]',
   30, 3, 10,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  ('tsmom_widepz_scanner', 'tsmom_widepz_scanner', 'tsmom_widepz',
   '["LTCUSDT","OPUSDT","APTUSDT"]',
   20, 2, 10,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  -- 2026-05-27 晚 C 路線部署:tsmom_btc_not_up(ts_momentum + 只在 BTC sideways/down 開倉)
  -- A/B 數據:Sharpe 0.66 → 0.82 (+0.16),機制:tsmom 在 BTC up 期 Sharpe 只 0.31 過濾掉
  -- 跟 tsmom_1d_scanner 平行 A/B:同 universe(TRX/SUI/AVAX)、看 filter 是否在 shadow 期復現
  ('tsmom_btcnotup_scanner', 'tsmom_btcnotup_scanner', 'tsmom_btc_not_up',
   '["TRXUSDT","SUIUSDT","AVAXUSDT"]',
   20, 2, 10,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  -- 2026-05-27 深夜 結構性 alpha 第一支:funding_momentum_ls
  -- 機制:funding 極端時跟同方向(羊群延續、非 contrarian)、t=+3.25 顯著、年化 37%、Sharpe 0.54
  -- 對照 funding_extreme(contrarian) t=-3.76 anti-edge,證實「funding 極端 = 趨勢延續」結構性
  ('fundmom_scanner', 'fundmom_scanner', 'funding_momentum_ls',
   '["APTUSDT","INJUSDT","ATOMUSDT","LINKUSDT","LTCUSDT","SUIUSDT","TRXUSDT","AVAXUSDT"]',
   30, 3, 10,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  -- 2026-05-27 深夜 — fundmom_xtight 變體(threshold 5%/95% 極端)
  -- param sweep 發現:極端閾值版 t=5.93 跟 harm_prz_scan10 並列最高、mean 17.4%、Sharpe 0.71、PF 3.09
  -- 選擇性換質量:trade 數/年 8 → 3、但每筆品質爆高
  -- 同 universe 跟 fundmom_scanner 平行 A/B、看 live 是否復現 selectivity 增益
  ('fundmom_xtight_scanner', 'fundmom_xtight_scanner', 'fundmom_ls_xtight',
   '["APTUSDT","INJUSDT","ATOMUSDT","LINKUSDT","LTCUSDT","SUIUSDT","TRXUSDT","AVAXUSDT"]',
   30, 3, 10,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  -- 2026-05-28 Q2 retail_ls_contrarian_tight(IS+OOS 雙確認 t=-2.89/-2.25、strat-validate Sharpe 0.69 / 年化 93%)
  -- ⚠ enabled=0 PARK 等待基建:quote-worker 還沒接 retail_ls 抓+對齊管道(類似 funding 路徑)
  --   需求:新增 retail_long_short_ratio 表 + Binance metrics 抓取(data.binance.vision daily zip + recent API)
  --        + AlignRetailLs + 加進 ohlcv JSON。tools/shared/OiMetricsCache.cs 是 backfill 範本可參考
  -- 暫存 entry 用於文件化,不參與 scanner sweep。Production 管道完成後改 enabled=1 即可上 shadow
  ('retail_ls_scanner', 'retail_ls_scanner', 'retail_ls_contrarian_tight',
   '["BTCUSDT","ETHUSDT","BNBUSDT","LTCUSDT","OPUSDT","SUIUSDT","APTUSDT","INJUSDT"]',
   25, 3, 10,
   'spot', '1d', 5, 1, 0,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  -- 2026-05-28 Q2 翻案2:retail_ls_delta_contrarian(Δ版,oi-validate OOS t=-3.65 + strat-validate 20 幣 pool t=+3.30 ✅ 顯著)
  -- 唯一通過 95% CI 下界>0 的策略;經濟意義:散戶意見變化方向比絕對位置更前瞻(動量耗盡頂底)
  -- 跟 retail_ls_contrarian 同源但測量角度不同(level vs delta)、可組互補 sleeve
  ('retail_ls_delta_scanner', 'retail_ls_delta_scanner', 'retail_ls_delta_contrarian',
   '["BTCUSDT","ETHUSDT","BNBUSDT","LTCUSDT","OPUSDT","SUIUSDT","APTUSDT","INJUSDT"]',
   25, 3, 10,
   'spot', '1d', 5, 1, 0,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  -- 2026-05-29 Q2 翻案:oi_contrarian_ls(OI 暴衝後 mean revert、非 momentum)
  -- strat-validate 可用(60% 幣 OOS 正 / Sharpe 0.41 / full 16%)、跟 retail_ls corr -0.18 去相關
  -- pool t=0.45 弱 → 純 shadow 收數據驗證,不升真錢;靠 open_interest_hist 表 + AlignOi 注入
  ('oi_contrarian_scanner', 'oi_contrarian_scanner', 'oi_contrarian_ls',
   '["BTCUSDT","ETHUSDT","BNBUSDT","LTCUSDT","OPUSDT","SUIUSDT","APTUSDT","INJUSDT"]',
   25, 3, 10,
   'spot', '1d', 5, 1, 0,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now'));

-- ── 多市場 paper-shadow scanner(美股 alpaca + 台股 twse)──────────────────────────
-- 平台「資產無關治理化多市場」demo:同一套 scanner / 保護鏈 / governance 跑多市場 paper-shadow。
-- 策略:harm_prz_scan10_widepz(harmonic PRZ 形態)。多市場驗證(2026-05-28、--stocks/--twstocks/--fx):
--   美股 pool t=5.45 ✅ / 台股 t=3.37 ⚠️(廣度53%<60%、邊緣) / 外匯 t=0.74 ❌(不接)。
--   ⚠ 不用 ts_momentum:股票 t=1.59 不顯著(動量在股票被 mean-reversion + 上漲 drift 削弱)。
-- universe = 全驗證集、求廣(scanner 每 cycle 挑訊號最強 N 個),**刻意不按歷史報酬挑股**:
--   逐檔 OOS 是噪音(BA 單檔 15% 運氣、中位僅 1%),照表現挑=selection bias 不會復現。
--   美股 = strat-validate --stocks 全 24 檔(跨 6 sector、max breadth);台股 = --twstocks 全 15 檔。
-- universe 必須 ⊆ quote-worker 抓的 StockSymbols(scanner 讀 DB bars、沒 bars 永遠 pending);已同步 appsettings.json。
--   VPS 用 env 覆寫 Worker__Quote__StockSymbols,須含這些 symbol 才會有 bars。
-- 都 shadow=1、leverage=1。**台股是 shadow-only 觀察**(t 顯著但過不了廣度閘、不升 real;exchange='twse' 無真實 client、
--   shadow 不下單、bars/關腿都走 quote-worker symbol 查、不碰交易所 client)。美股升 paper-live 前才設 budget。
-- 預算單位是 paper(非 USDT),sanity check 用 per-exchange 分組、不跟 crypto USDT 混加。
INSERT OR REPLACE INTO scanner_legs
  (id, name, strategy, universe,
   budget_total, max_concurrent, per_leg_cap,
   mode, interval, leverage, shadow, enabled, exchange,
   owner_principal_id, created_at, updated_at)
VALUES
  ('usstock_harmprz_scanner', 'usstock_harmprz_scanner', 'harm_prz_scan10_widepz',
   '["AAPL","MSFT","GOOGL","AMZN","NVDA","AMD","AVGO","CRM","INTC","META","JPM","BAC","V","MA","UNH","JNJ","XOM","CVX","WMT","KO","PG","DIS","CAT","BA"]',
   3000, 3, 1000,
   'spot', '1d', 1, 1, 1, 'alpaca',
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  -- 台股 shadow-only 觀察(t=3.37 顯著但廣度<60%、不升 real;exchange='twse' 純 metadata、shadow 不下單)
  ('twstock_harmprz_scanner', 'twstock_harmprz_scanner', 'harm_prz_scan10_widepz',
   '["2330.TW","2317.TW","2454.TW","2308.TW","2303.TW","2881.TW","2882.TW","2891.TW","2412.TW","1301.TW","1303.TW","2002.TW","1216.TW","2912.TW","0050.TW"]',
   0, 3, 0,
   'spot', '1d', 1, 1, 1, 'twse',
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now'));

-- 全新 DB 補正:EnsureTable 建 exchange 欄不帶 SQL DEFAULT(BaseOrm 不從 model initializer 產 DEFAULT、
-- 且 ScannerLegEntry.Exchange 非 [Required]),上面 crypto INSERT 省略 exchange 欄會塞 NULL。
-- 既有 DB 走 migration 的 DEFAULT 'binance' 沒這問題;此 UPDATE 兜底全新 DB(alpaca 列已顯式設、不受影響)。
UPDATE scanner_legs SET exchange='binance' WHERE exchange IS NULL OR exchange='';

-- Sanity check(per exchange:binance=USDT、alpaca=paper USD,單位不同不混加)
SELECT printf('seed 完成 [%s] enabled scanner: %d、budget: %.0f',
              exchange, COUNT(*), SUM(budget_total))
FROM scanner_legs WHERE enabled=1 GROUP BY exchange;
