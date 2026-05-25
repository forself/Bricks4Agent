#!/usr/bin/env bash
# 在 VPS 上安裝 / 更新 B4A Docker Watchdog systemd 服務。
# 用法(在 VPS):  bash /opt/b4a/tools/install-watchdog.sh
set -euo pipefail

REPO_DIR="/opt/b4a"
UNIT_SRC="$REPO_DIR/tools/b4a-watchdog.service"
UNIT_DST="/etc/systemd/system/b4a-watchdog.service"

if [ ! -f "$UNIT_SRC" ]; then
  echo "找不到 $UNIT_SRC,先 git pull"; exit 1
fi

# 快速語法檢查
python3 -c "import ast; ast.parse(open('$REPO_DIR/tools/docker-watchdog.py').read())"
echo "✓ docker-watchdog.py 語法 OK"

cp "$UNIT_SRC" "$UNIT_DST"
systemctl daemon-reload
systemctl enable b4a-watchdog
systemctl restart b4a-watchdog
sleep 2
systemctl --no-pager status b4a-watchdog | head -12
echo "---- 最新 log ----"
journalctl -u b4a-watchdog -n 15 --no-pager
