#!/bin/bash
# B4A daily backup — broker.db / quote.db(WAL-safe 線上備份)/ trading-data / secrets / .env
# 本機保留 14 天 + 可選 off-box 送出(設 .env.trading 的 BACKUP_OFFBOX_DEST=scp:user@host:/path)。
#
# 為何改 WAL-safe(2026-05-30):原本 `docker cp` 直接複製【運行中】的 WAL SQLite,
# 最近 commit 還在 -wal(沒被 copy)→ 還原少交易;checkpoint 中途 copy → 內部撕裂、還原不了。
# 改用 sqlite3 線上備份 API(host python3),對 live DB 產生【一致】快照(處理 WAL + 並發寫)。
set -e
DATE=$(date -u +%Y%m%d-%H%M%S)
DEST=/opt/b4a-backups/b4a-$DATE.tar.gz
ENV_FILE=/opt/b4a/tools/.env.trading
TMPDIR=$(mktemp -d)

# WAL-safe SQLite 線上備份。$1=volume名 $2=容器內 db 檔名 $3=輸出名。
# 失敗不中止整個備份(set -e 下用 || 守衛)、退而求其次 raw cp(至少有東西)。
snapshot_sqlite() {
  local mp src
  mp=$(docker volume inspect "$1" --format '{{.Mountpoint}}' 2>/dev/null) || { echo "$3 skip (volume $1 不存在)"; return 0; }
  src="$mp/$2"
  [ -f "$src" ] || { echo "$3 skip (無 $src)"; return 0; }
  if python3 - "$src" "$TMPDIR/$3" <<'PY' 2>/dev/null
import sqlite3, sys
src, dst = sys.argv[1], sys.argv[2]
s = sqlite3.connect(f"file:{src}?mode=ro", uri=True)
d = sqlite3.connect(dst)
with d:
    s.backup(d)          # 線上備份 API:一致、WAL-safe
s.close(); d.close()
PY
  then
    echo "$3 ok WAL-safe ($(du -h "$TMPDIR/$3" | cut -f1))"
  else
    echo "$3 線上備份失敗、退而求其次 raw cp(可能不一致)"
    cp "$src" "$TMPDIR/$3" 2>/dev/null || echo "$3 skip (raw cp 也失敗)"
  fi
}

snapshot_sqlite b4a-trading_broker-data broker.db broker.db
snapshot_sqlite b4a-trading_quote-data  quote.db  quote.db

# trading-data(小、非主庫)維持 docker cp
docker cp b4a-trading-worker:/data/ "$TMPDIR/trading-data" 2>/dev/null || echo 'trading skip'
cp "$ENV_FILE" "$TMPDIR/.env.trading" 2>/dev/null || echo '.env skip'
cp -r /opt/b4a/tools/secrets "$TMPDIR/secrets" 2>/dev/null || echo 'secrets skip'
# Cloudflare tunnel 設定 + 憑證(scenario C 新機重建 tunnel 用;含 tunnel credential = secret,
# 故只進這個本就含密鑰、只上 private R2 的 tarball)。新機放回 /etc/cloudflared + cloudflared service install 即重連同一 tunnel、DNS 免改。
cp -r /etc/cloudflared   "$TMPDIR/cloudflared-etc"  2>/dev/null || echo 'cloudflared-etc skip'
cp -r /root/.cloudflared "$TMPDIR/cloudflared-root" 2>/dev/null || echo 'cloudflared-root skip'

tar czf "$DEST" -C "$TMPDIR" .
rm -rf "$TMPDIR"
echo "[$(date -u +%FT%TZ)] backup written: $DEST ($(du -h "$DEST" | cut -f1))"

# 本機保留最近 14 個
ls -1t /opt/b4a-backups/b4a-*.tar.gz 2>/dev/null | tail -n +15 | xargs -r rm -v

# ── 可選 off-box 送出 ──
# VPS 一掛、本機備份跟著死 → 離機副本是「重啟治不好/整機掛」失效情境的命脈,也是 warm-standby 前提。
# 設 .env.trading 的 BACKUP_OFFBOX_DEST(無引號)才啟用;未設=僅本機(原行為)。格式:
#   rclone:remote:bucket/path   物件儲存(Cloudflare R2 / Backblaze B2 / S3,需先 rclone config)
#   scp:user@host:/remote/path  另一台機器
# ⚠ tarball 含 secrets/.env → off-box 目的地必須安全(傳輸已加密,落地端 bucket 要私有、token 要 scoped)。
# 遠端保留:用 bucket 端 lifecycle rule 自動過期(比腳本刪遠端安全),本機仍保留 14 份。
OFFBOX=$(grep -E '^BACKUP_OFFBOX_DEST=' "$ENV_FILE" 2>/dev/null | head -1 | cut -d= -f2-)
if [ -n "$OFFBOX" ]; then
  case "$OFFBOX" in
    rclone:*)
      remote=${OFFBOX#rclone:}
      if command -v rclone >/dev/null 2>&1; then
        if rclone copy "$DEST" "$remote/" --contimeout 20s --timeout 120s >/dev/null 2>&1; then
          echo "[off-box] rclone → $remote ok"
        else
          echo "[off-box] rclone 失敗 → $remote(本機備份仍在、不影響)"
        fi
      else
        echo "[off-box] rclone 未安裝、無法送 $remote(本機備份仍在)"
      fi ;;
    scp:*)
      target=${OFFBOX#scp:}
      if scp -o StrictHostKeyChecking=accept-new -o ConnectTimeout=20 "$DEST" "$target/" >/dev/null 2>&1; then
        echo "[off-box] scp → $target ok"
      else
        echo "[off-box] scp 失敗 → $target(本機備份仍在、不影響)"
      fi ;;
    *) echo "[off-box] 不支援的 DEST 格式:$OFFBOX(需 rclone:remote:bucket/path 或 scp:user@host:/path)" ;;
  esac
else
  echo "[off-box] 未設 BACKUP_OFFBOX_DEST → 僅本機備份(VPS 掛則無離機副本)"
fi
