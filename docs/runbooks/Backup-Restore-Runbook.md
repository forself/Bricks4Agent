# B4A 備份 / 還原 Runbook

> 出事時照這份做,別臨時拼指令。對應 `scripts/backup-daily.sh`(每日 18:00 UTC cron)。
> 最後更新 2026-05-30(WAL-safe 線上備份 + rclone off-box)。

## 1. 備份內容(tarball `/opt/b4a-backups/b4a-YYYYMMDD-HHMMSS.tar.gz`)

| 檔 | 內容 | 備註 |
|---|---|---|
| `broker.db` | 平台權威狀態(64 表):授權鏈/principals/roles/capability_grants、execution_requests 狀態機、配額、**AES 加密的 session_keys**、audit 鏈、workflow DAG(plans/checkpoints)、notification_dedup、交易元資料(watchlist/perpetual_position_state/alert_rules)、agent_inbox(含 idempotency idx) | ★核心。**WAL-safe 線上備份**(非 raw cp) |
| `quote.db` | 行情快取 OHLCV | 可重抓 |
| `trading-data/` | trading-worker 本地持久檔 | 小 |
| `.env.trading` | 環境設定(含 master key、tokens) | ★**含 secret** |
| `secrets/` | docker secrets(master key / scoped token / 交易所 API key) | ★**含 secret** |

**為何必含 secrets**:broker.db 的 `session_keys` 用 master key AES 加密 → 沒 master key 解不開、也連不上交易所。→ off-box 目的地必須 private + token scoped。

## 2. 備份【沒有】什麼

- **程式碼 / docker images** → 在 GitHub(myorigin),還原時 `git clone` + rebuild。
- **真錢持倉** → 在 BingX 交易所,**不在備份**。broker 啟動時從交易所 re-hydrate。
- **記憶體易失狀態**(WorkerRegistry / peak-mark / trailing)→ 不在任何備份,啟動重建。

## 3. 還原步驟(VPS 掛 / DB 損毀 / 搬新機)

```bash
# 1. 程式碼(備份裡沒有)
git clone <myorigin-url> /opt/b4a && cd /opt/b4a

# 2. 取備份 tarball(R2 設好後 / 或本機 /opt/b4a-backups)
#    rclone copy r2:<bucket>/b4a/b4a-YYYYMMDD-HHMMSS.tar.gz .
mkdir -p /tmp/restore && tar xzf b4a-YYYYMMDD-HHMMSS.tar.gz -C /tmp/restore

# 3. 停 stack(確保 broker 沒在寫 DB)
cd /opt/b4a/tools
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml down

# 4. 放回設定 + 密鑰
cp /tmp/restore/.env.trading /opt/b4a/tools/.env.trading
cp -r /tmp/restore/secrets   /opt/b4a/tools/secrets

# 5. 放回 DB 進 volume(★先刪舊 -wal/-shm:備份是自足一致 .db,留舊 wal 會衝突)
V=/var/lib/docker/volumes/b4a-trading_broker-data/_data
rm -f $V/broker.db-wal $V/broker.db-shm
cp /tmp/restore/broker.db $V/broker.db
# quote.db 同理 → b4a-trading_quote-data volume

# 6. build + 起
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml build
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml up -d
```

啟動後:broker 跑 migration(冪等)→ 從 BingX re-hydrate 倉位 → **AutoTrader 預設 disabled(真錢不自動武裝)**。檢查 `/api/v1/health`、watchlist、持倉對得上 → 再**手動**決定是否重新武裝真錢。別忘了 restart bot-node(自動重連已有,通常自己好)。

## 4. 最關鍵觀念

**還原 = 救回「控制器的治理/設定狀態」,不是救回持倉。** 持倉永遠以 BingX 為準、broker 啟動對帳。用三天前的備份還原,broker 看到的仍是【當下】交易所真實倉位。備份的交易元資料只是輔助,真相在交易所。

## 5. 驗證備份可用(別等真出事才發現壞掉)

```bash
# 抽一份備份、驗 broker.db 完整性(應回 ok)
LATEST=$(ls -1t /opt/b4a-backups/b4a-*.tar.gz | head -1); T=$(mktemp -d)
tar xzf "$LATEST" -C "$T" ./broker.db
python3 -c "import sqlite3;print(sqlite3.connect('$T/broker.db').execute('PRAGMA integrity_check').fetchone()[0])"
rm -rf "$T"
```

建議每隔一陣子跑一次第 5 節(備份的價值 = 能還原,沒驗過的備份等於沒有)。
