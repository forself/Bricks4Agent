#!/bin/bash
# B4A 場景 C 災難復原 bootstrap — VPS 死了、在【全新機器】上一鍵重建。
#
# 前提(go-bag,從你 VPS 以外的 txt / 密碼管理器取):
#   R2 Access Key ID / Secret / endpoint、myorigin git URL。
# 需求:此機已裝 docker + git(缺會擋);rclone/cloudflared 缺則處理見下。
# 用法:bash bootstrap-restore.sh   (互動式、跟著提示貼 go-bag 的值;Secret 不顯示)
#
# ⚠ 這是「指南型」腳本:自動化可確定的步驟、危險/需判斷處會提示。
#   真正的保證 = 事前在便宜的拋棄式 VPS 跑過一次演練(未演練的 DR 等於沒有 DR)。
set -e
say(){ echo -e "\n===== $* ====="; }

PROJ=b4a-trading   # docker compose 專案名(volume 前綴 b4a-trading_*)

# 0. 前提檢查
for c in docker git; do command -v "$c" >/dev/null || { echo "✗ 缺 $c — 先裝好(docker 用官方安裝、git 用 apt)再重跑"; exit 1; }; done
command -v rclone >/dev/null || { say "裝 rclone (apt)"; apt-get update -qq && apt-get install -y rclone; }
HAS_CF=1; command -v cloudflared >/dev/null || { HAS_CF=0; echo "⚠ 無 cloudflared:tunnel 還原會 skip,稍後手動裝 + 放回 /etc/cloudflared"; }

# 1. go-bag 輸入(Secret 不顯示、不留 shell 歷史)
say "輸入 go-bag(來源:你 VPS 外的 txt)"
read -rp  "R2 Access Key ID: " AK
read -rsp "R2 Secret Access Key: " SK; echo
read -rp  "R2 endpoint (https://<acct>.r2.cloudflarestorage.com): " EP
read -rp  "git repo URL (myorigin): " GIT

# 2. rclone remote
say "設定 rclone remote r2"
rclone config create r2 s3 provider=Cloudflare env_auth=false \
  access_key_id="$AK" secret_access_key="$SK" region=auto endpoint="$EP" no_check_bucket=true >/dev/null
unset AK SK
rclone ls r2:b4a-backups >/dev/null && echo "  rclone → R2 OK" || { echo "✗ rclone 連 R2 失敗,檢查鑰匙/endpoint"; exit 1; }

# 3. clone 程式碼
say "clone 程式碼 → /opt/b4a"
[ -d /opt/b4a/.git ] || git clone "$GIT" /opt/b4a

# 4. 抓 + 解 + 驗最新備份
say "抓最新 R2 備份(restore-fetch)"
bash /opt/b4a/scripts/restore-fetch.sh
R=/tmp/b4a-restore/extracted
[ -f "$R/broker.db" ] || { echo "✗ 沒抓到 broker.db,中止"; exit 1; }

# 5. 放回設定 + 密鑰
say "放回 .env.trading / secrets"
cp "$R/.env.trading" /opt/b4a/tools/.env.trading
cp -r "$R/secrets"   /opt/b4a/tools/secrets

# 6. 建 volume + 注入 DB(volume 要先存在才塞得進;名稱對齊 compose 專案前綴)
say "建 volume + 注入 broker.db / quote.db"
docker volume create "${PROJ}_broker-data" >/dev/null
docker volume create "${PROJ}_quote-data"  >/dev/null
BV=$(docker volume inspect "${PROJ}_broker-data" --format '{{.Mountpoint}}')
QV=$(docker volume inspect "${PROJ}_quote-data"  --format '{{.Mountpoint}}')
rm -f "$BV"/broker.db-wal "$BV"/broker.db-shm
cp "$R/broker.db" "$BV/broker.db"
[ -f "$R/quote.db" ] && { rm -f "$QV"/quote.db-wal "$QV"/quote.db-shm; cp "$R/quote.db" "$QV/quote.db"; }

# 7. 還原 Cloudflare tunnel(放回憑證 + service install → 重連【同一】tunnel、DNS 免改)
if [ "$HAS_CF" = 1 ] && [ -d "$R/cloudflared-etc" ]; then
  say "還原 Cloudflare tunnel"
  mkdir -p /etc/cloudflared && cp -r "$R/cloudflared-etc/." /etc/cloudflared/
  [ -d "$R/cloudflared-root" ] && { mkdir -p /root/.cloudflared && cp -r "$R/cloudflared-root/." /root/.cloudflared/; }
  cloudflared service install 2>/dev/null || true
  systemctl enable --now cloudflared 2>/dev/null || true
  echo "  tunnel 重連中(同 tunnel ID、DNS 不用改)"
else
  echo "⚠ 跳過 tunnel:手動裝 cloudflared → 放回 $R/cloudflared-etc 到 /etc/cloudflared → cloudflared service install"
fi

# 8. build + 起
say "build + 起 stack"
cd /opt/b4a/tools
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml build
docker compose --env-file .env.trading -f compose.trading.yml -f compose.trading.secrets.yml up -d

say "完成 — 人工核對清單"
cat <<'NEXT'
  [ ] curl -s localhost:5100/api/v1/health        → 應 {"status":"ok"}
  [ ] broker 會從 BingX re-hydrate 倉位;AutoTrader 預設 disabled
  [ ] 核對 watchlist / 持倉無誤後 → 才【手動】武裝真錢
  [ ] 重設每日備份 cron:  0 18 * * * /opt/b4a/scripts/backup-daily.sh >> /var/log/b4a-backup.log 2>&1
  [ ] 設好 R2 bucket lifecycle(30天過期)若新 bucket
  [ ] 有 bot-node 的話:確認自動重連或 docker restart b4a-bot-node
NEXT
