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
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now'));

-- Sanity check
SELECT printf('seed 完成,enabled scanner: %d,total budget: %.0f USDT',
              COUNT(*), SUM(budget_total))
FROM scanner_legs WHERE enabled=1;
