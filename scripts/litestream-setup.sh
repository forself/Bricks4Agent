#!/bin/bash
# Litestream 連續複製 broker.db → R2(RPO 秒級)。加在【每日 tarball 備份之上、不取代】。
#   - 日備份 = 整包災難復原(broker.db+quote.db+.env+secrets+cloudflared)
#   - Litestream = broker.db 連續 point-in-time(治理/audit/inbox 狀態 RPO 秒級)
# 用容器跑(不裝 host binary、同你其他容器信任路徑)。R2 憑證從現有 rclone.conf [r2] 取、不另存不外洩。
# 重跑安全(idempotent):會先移除舊 b4a-litestream 再起。
set -e
RCLONE_CONF=${RCLONE_CONF:-/root/.config/rclone/rclone.conf}
CFGDIR=/opt/b4a-litestream          # 設定+env 放這(repo 外、避免誤入 git)
VOL=b4a-trading_broker-data
IMG=litestream/litestream:latest

# 從 rclone [r2] 取 R2 憑證(只在本機操作、不 echo key)
AK=$(grep -E '^[[:space:]]*access_key_id'     "$RCLONE_CONF" | head -1 | sed 's/.*=[[:space:]]*//')
SK=$(grep -E '^[[:space:]]*secret_access_key' "$RCLONE_CONF" | head -1 | sed 's/.*=[[:space:]]*//')
EP=$(grep -E '^[[:space:]]*endpoint'          "$RCLONE_CONF" | head -1 | sed 's/.*=[[:space:]]*//')
if [ -z "$AK" ] || [ -z "$SK" ] || [ -z "$EP" ]; then
  echo "✗ 從 $RCLONE_CONF 取不到 R2 憑證(先設好 rclone 的 r2 remote)"; exit 1
fi
echo "R2 憑證:OK(endpoint=$EP)"     # 只印 endpoint(非機密)、不印 key

mkdir -p "$CFGDIR"; umask 077

# 憑證寫 env(root-only、Litestream 自動讀 LITESTREAM_ACCESS_KEY_ID/_SECRET_ACCESS_KEY)
cat > "$CFGDIR/.litestream.env" <<ENV
LITESTREAM_ACCESS_KEY_ID=$AK
LITESTREAM_SECRET_ACCESS_KEY=$SK
ENV

# Litestream config(無 key)
cat > "$CFGDIR/litestream.yml" <<YML
dbs:
  - path: /data/broker.db
    replicas:
      - type: s3
        bucket: b4a-backups
        path: litestream/broker
        endpoint: $EP
        region: auto
        retention: 72h
        snapshot-interval: 12h
        sync-interval: 10s
YML

docker pull "$IMG"
docker rm -f b4a-litestream 2>/dev/null || true
docker run -d --name b4a-litestream --restart unless-stopped \
  --env-file "$CFGDIR/.litestream.env" \
  -v "$VOL":/data \
  -v "$CFGDIR/litestream.yml":/etc/litestream.yml:ro \
  "$IMG" replicate -config /etc/litestream.yml

echo "啟動中… 等 8 秒看日誌"; sleep 8
echo "=== litestream 日誌 ==="; docker logs b4a-litestream --tail 18 2>&1
