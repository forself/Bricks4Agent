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

# 2. 取備份 tarball — 用 restore-fetch.sh 自動抓【最新】+ 解開 + 驗完整性
#    (前提:此機已裝 rclone + 設好 r2 remote,見 §7 冷啟動 go-bag)
bash /opt/b4a/scripts/restore-fetch.sh
#    → 解開在 /tmp/b4a-restore/extracted/(broker.db / quote.db / .env.trading / secrets / trading-data)
#    本機若還活著、直接用本機備份更快:tar xzf /opt/b4a-backups/b4a-*.tar.gz -C /tmp/b4a-restore/extracted
RESTORE=/tmp/b4a-restore/extracted

# 3. 停 stack(確保 broker 沒在寫 DB)
cd /opt/b4a/tools
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml down

# 4. 放回設定 + 密鑰
cp "$RESTORE/.env.trading" /opt/b4a/tools/.env.trading
cp -r "$RESTORE/secrets"   /opt/b4a/tools/secrets

# 5. 放回 DB 進 volume(★先刪舊 -wal/-shm:備份是自足一致 .db,留舊 wal 會衝突)
V=/var/lib/docker/volumes/b4a-trading_broker-data/_data
rm -f $V/broker.db-wal $V/broker.db-shm
cp "$RESTORE/broker.db" $V/broker.db
# quote.db 同理 → b4a-trading_quote-data volume

# 6. build + 起
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml build
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml up -d
```

啟動後:broker 跑 migration(冪等)→ 從 BingX re-hydrate 倉位 → **AutoTrader 預設 disabled(真錢不自動武裝)**。檢查 `/api/v1/health`、watchlist、持倉對得上 → 再**手動**決定是否重新武裝真錢。別忘了 restart bot-node(自動重連已有,通常自己好)。

## 3.5 從 Litestream 還原 broker.db(連續 PITR、RPO ~10s)

日 tarball 是「整包 DR」;Litestream 是 broker.db 的**連續 point-in-time**(治理/audit/inbox 狀態只丟秒級)。
要把 broker.db 還原到【最新】或【某個時間點】:
```bash
# 停 broker(別讓它寫 DB)
cd /opt/b4a/tools && docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml stop broker
# 從 R2 還原(最新);要時點加 -timestamp 2026-05-30T06:00:00Z
docker run --rm --user 10002:10002 --env-file /opt/b4a-litestream/.litestream.env \
  -v b4a-trading_broker-data:/data -v /opt/b4a-litestream/litestream.yml:/etc/litestream.yml:ro \
  litestream/litestream restore -config /etc/litestream.yml -o /data/broker-restored.db /data/broker.db
# 驗 + 換上(先刪舊 -wal/-shm)
V=/var/lib/docker/volumes/b4a-trading_broker-data/_data
python3 -c "import sqlite3;print(sqlite3.connect('$V/broker-restored.db').execute('PRAGMA integrity_check').fetchone()[0])"
rm -f $V/broker.db-wal $V/broker.db-shm && mv $V/broker.db-restored.db $V/broker.db  # 確認 ok 再換
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml start broker
```
新機(場景 C)要從 litestream 還原:先 `litestream-setup.sh` 設好(或手動配 /opt/b4a-litestream),再跑上面 restore。

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

## 6. 危機處理決策樹(出事先對號入座)

先判斷是哪一類,再走對應動作。核心分野:**VPS 還活著嗎?**

```
出事了
 │
 ├─ broker 一直 crash / DB 損毀,但 VPS 還能 ssh
 │     → 場景 A:單機 DB 還原(最常見、最快、不用新機)
 │
 ├─ 磁碟滿(容器起不來、ALTER/寫入失敗)
 │     → 場景 B:清空間(不是還原)
 │
 ├─ VPS 整台連不上(Contabo 宕/網路斷/kernel panic)
 │     → 場景 C:新機重建(才動到 R2 抓備份)
 │
 └─ 誤刪/壞 deploy 想退到某個時間點
       → 場景 D:指定舊備份還原
```

### 場景 A — 單機 DB 還原(VPS 活著)
偵測:broker crash-loop、watchdog 推 CRITICAL。本機 `/opt/b4a-backups` 就有備份(比 R2 快)。
```bash
cd /opt/b4a/tools && docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml stop broker
mkdir -p /tmp/b4a-restore/extracted
tar xzf "$(ls -1t /opt/b4a-backups/b4a-*.tar.gz | head -1)" -C /tmp/b4a-restore/extracted ./broker.db
python3 -c "import sqlite3;print(sqlite3.connect('/tmp/b4a-restore/extracted/broker.db').execute('PRAGMA integrity_check').fetchone()[0])"  # 應 ok
V=/var/lib/docker/volumes/b4a-trading_broker-data/_data; rm -f $V/broker.db-wal $V/broker.db-shm
cp /tmp/b4a-restore/extracted/broker.db $V/broker.db
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml up -d broker
```

### 場景 B — 磁碟滿(治不好失敗,還原沒用)
還原解決不了滿磁碟。先清:
```bash
docker builder prune -af   # 清 build cache(本系統最大宗,曾佔 ~70G)
docker image prune -af     # 清 dangling image
df -h /                    # 確認降下來
```
根治:設 /etc/docker/daemon.json 的 `builder.gc.defaultKeepStorage` 上限(見 ContainerResilience doc)。

### 場景 C — 新機重建(VPS 死了)
偵測:dead-man(healthchecks.io)告警 / Discord 靜默 / ssh 不通。需 §7 go-bag。

**一鍵路徑(推薦)**:新機裝好 docker + git 後,clone 程式碼或直接抓腳本,跑:
```bash
bash bootstrap-restore.sh
```
它會:提示貼 go-bag 的 R2 鑰匙 + git URL → 設 rclone → clone → 抓最新備份 → 放回 .env/secrets/DB → **還原 Cloudflare tunnel(憑證已隨備份、重連同一 tunnel、DNS 免改)** → build + up → 印人工核對清單。

> tunnel 重連原理:備份已含 `/etc/cloudflared`(tunnel 憑證 + config),bootstrap 放回後 `cloudflared service install` 即用【同一個 tunnel ID】從新機連出 → CF 端 DNS(CNAME 指向 tunnel ID)不變、自動指到新機。**不用改 DNS。**

手動路徑(bootstrap 出錯時逐步來):裝 docker/rclone/cloudflared → `rclone config create r2 ...`(鑰匙來自 go-bag)→ `git clone` → `restore-fetch.sh` → §3 步驟 3-6 放回 + 建 volume 注入 DB → 放回 `/etc/cloudflared` + `cloudflared service install` → build + up。
完成後:broker 從 BingX re-hydrate 倉位 → 核對 → **手動**武裝真錢 → 重設備份 cron(`0 18 * * * /opt/b4a/scripts/backup-daily.sh`)。

### 場景 D — 退到某個時間點
```bash
rclone lsf r2:b4a-backups/b4a/   # 列所有備份、挑日期
bash /opt/b4a/scripts/restore-fetch.sh   # 預設抓最新;要指定舊的就手動 rclone copy 那一份再 tar
```

## 7. 冷啟動 go-bag(必須存在【VPS 以外】,否則場景 C 卡死)

VPS 死了你會需要這些,而它們**不能只存在死掉的 VPS 上**。現在就存進密碼管理器 / 你筆電:

- [ ] **R2 鑰匙**:Access Key ID + Secret Access Key + endpoint(`https://<account_id>.r2.cloudflarestorage.com`)— 沒這個抓不到 R2 備份 = 循環依賴,**最致命**
- [ ] **myorigin git URL**(clone 程式碼用)
- [ ] **Contabo 登入**(開新 VPS)
- [ ] **BingX 登入**(還原後人工核對持倉)
- [ ] **Cloudflare 登入**(備援:萬一要手動重建 tunnel / 改 DNS)
- [x] ~~named tunnel token/設定~~ → **已隨備份**(`/etc/cloudflared` 進 tarball,bootstrap 自動還原、重連同一 tunnel)
- [x] 本 runbook + 腳本在 git(從筆電 GitHub 讀得到)

> 自我測試:假設你現在只有一台全新筆電 + 上面這些,你能在 1 小時內把平台在新 VPS 上拉起來嗎?能 = go-bag 完整。卡在哪一項 = 那項就是你的 DR 盲點。
