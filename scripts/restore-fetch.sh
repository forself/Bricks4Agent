#!/bin/bash
# B4A 危機還原:自動從 R2 抓【最新】備份 + 解開 + 驗完整性。在新機/復原機上跑。
#
# 前提(冷啟動 go-bag,須存在【VPS 以外】):
#   - rclone 已裝(apt install rclone)
#   - r2 remote 已設:rclone config create r2 s3 provider=Cloudflare env_auth=false \
#       access_key_id=<KEY> secret_access_key=<SECRET> region=auto \
#       endpoint=https://<account_id>.r2.cloudflarestorage.com no_check_bucket=true
#     ★ 這些鑰匙若只存在掛掉的 VPS 上=循環依賴、抓不到備份。務必另存密碼管理器/筆電。
#
# 用法:  ./restore-fetch.sh [remote路徑] [輸出目錄]
#   預設 remote=r2:b4a-backups/b4a  輸出=/tmp/b4a-restore
set -e
REMOTE=${1:-r2:b4a-backups/b4a}
OUT=${2:-/tmp/b4a-restore}
mkdir -p "$OUT"

echo "[1/4] 找最新備份 @ $REMOTE ..."
LATEST=$(rclone lsf "$REMOTE/" --files-only 2>/dev/null | grep -E '^b4a-.*\.tar\.gz$' | sort | tail -1)
[ -z "$LATEST" ] && { echo "✗ 找不到備份!檢查 rclone 設定 / remote 路徑(rclone ls $REMOTE/)"; exit 1; }
echo "      最新 = $LATEST"

echo "[2/4] 下載 ..."
rclone copy "$REMOTE/$LATEST" "$OUT/" --progress

echo "[3/4] 解開到 $OUT/extracted ..."
rm -rf "$OUT/extracted"; mkdir -p "$OUT/extracted"
tar xzf "$OUT/$LATEST" -C "$OUT/extracted"

echo "[4/4] 驗 broker.db 完整性 ..."
INTEG=$(python3 -c "import sqlite3;print(sqlite3.connect('$OUT/extracted/broker.db').execute('PRAGMA integrity_check').fetchone()[0])" 2>&1 || echo "FAILED")
echo "      integrity_check: $INTEG"
[ "$INTEG" != "ok" ] && { echo "✗ broker.db 完整性檢查未過($INTEG)— 這份備份可能壞了,改抓前一份:rclone lsf $REMOTE/"; exit 1; }

echo
echo "✓ 備份就緒於 $OUT/extracted/ :"
ls -la "$OUT/extracted/"
echo
echo "下一步(照 docs/runbooks/Backup-Restore-Runbook.md §3):"
echo "  停 stack → 放回 .env.trading / secrets / broker.db(先刪舊 -wal/-shm)→ build → up"
echo "  起來後 broker 從 BingX re-hydrate 倉位、AutoTrader 預設 disabled、手動確認後再武裝真錢。"
