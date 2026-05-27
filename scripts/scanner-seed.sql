-- Scanner Legs 初始 seed(2026-05-27 Phase 1 起步用)。
--
-- 對應設計:
--   - docs/designs/portfolio-scanner-hybrid.md
--   - docs/reports/ScannerUniverse-2026-05-27.md
--
-- 4 個 scanner 全部 shadow=1 啟動(只記不下單)、shadow 4 週紀律期過後再升 live。
-- INSERT OR REPLACE 是 idempotent — 重複跑會以新值覆寫(改 universe / budget 等隨時改)。
--
-- 跑法:
--   sqlite3 ~/broker.db < scripts/scanner-seed.sql
-- (broker.db 路徑視部署位置,常見:VPS /opt/brick4agent/broker.db、本機 ~/Brick4Agent/broker.db)
--
-- Universe 選擇依據(都套用「跨時框 ≥4/5 ∪ 1d ≥15%」標準):
--   scan10_scanner    : LTC/OP/NEAR/INJ/APT/SUI/ARB(7 alt-coin,scan10 主場)
--   widepz_scanner    : 同 universe,shadow 並行做 A/B 對比(scan10 vs widepz)
--   tsmom_1d_scanner  : TRX/SUI/AVAX(1d ≥15% 或 跨時框 ≥4/5)
--   tsmom_1w_scanner  : BTC/BNB/XRP/TRX(週線主場、large-cap trending)

INSERT OR REPLACE INTO scanner_legs
  (id, name, strategy, universe,
   budget_total, max_concurrent, per_leg_cap,
   mode, interval, leverage, shadow, enabled,
   owner_principal_id, created_at, updated_at)
VALUES
  ('scan10_scanner', 'scan10_scanner', 'harm_prz_scan10',
   '["LTCUSDT","OPUSDT","NEARUSDT","INJUSDT","APTUSDT","SUIUSDT","ARBUSDT"]',
   10000, 3, 2000,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  ('widepz_scanner', 'widepz_scanner', 'harm_prz_scan10_widepz',
   '["LTCUSDT","OPUSDT","NEARUSDT","INJUSDT","APTUSDT","SUIUSDT","ARBUSDT"]',
   10000, 3, 2000,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  ('tsmom_1d_scanner', 'tsmom_1d_scanner', 'ts_momentum',
   '["TRXUSDT","SUIUSDT","AVAXUSDT"]',
   5000, 2, 2500,
   'spot', '1d', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now')),

  ('tsmom_1w_scanner', 'tsmom_1w_scanner', 'ts_momentum',
   '["BTCUSDT","BNBUSDT","XRPUSDT","TRXUSDT"]',
   5000, 3, 1700,
   'spot', '1w', 5, 1, 1,
   'prn_dashboard', strftime('%Y-%m-%dT%H:%M:%fZ','now'), strftime('%Y-%m-%dT%H:%M:%fZ','now'));

-- Sanity check(跑完手動執行確認 4 列):
-- SELECT id, strategy, json_array_length(universe) AS u_count, max_concurrent, shadow, enabled
-- FROM scanner_legs ORDER BY id;
