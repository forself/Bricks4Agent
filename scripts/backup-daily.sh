#!/bin/bash
# B4A daily backup — broker.db / quote.db / secrets / .env 打包到 /opt/b4a-backups
# 保留 14 天、超過自動刪。你之後設 cron 從家機器 rsync 這個目錄回去。
set -e
DATE=$(date -u +%Y%m%d-%H%M%S)
DEST=/opt/b4a-backups/b4a-$DATE.tar.gz

# 從 docker volume 拉 db 出來、打包敏感檔
TMPDIR=$(mktemp -d)
docker cp b4a-broker:/data/broker.db "$TMPDIR/broker.db" 2>/dev/null || echo 'broker.db skip'
docker cp b4a-quote-worker:/data/quote.db "$TMPDIR/quote.db" 2>/dev/null || echo 'quote.db skip'
docker cp b4a-trading-worker:/data/ "$TMPDIR/trading-data" 2>/dev/null || echo 'trading skip'
cp /opt/b4a/tools/.env.trading "$TMPDIR/.env.trading"
cp -r /opt/b4a/tools/secrets "$TMPDIR/secrets"

tar czf "$DEST" -C "$TMPDIR" .
rm -rf "$TMPDIR"
echo "[$(date -u +%FT%TZ)] backup written: $DEST ($(du -h "$DEST" | cut -f1))"

# 保留最近 14 個
ls -1t /opt/b4a-backups/b4a-*.tar.gz 2>/dev/null | tail -n +15 | xargs -r rm -v
