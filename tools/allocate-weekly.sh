#!/bin/bash
# 每週 --allocate 報告 → Discord。存日期檔 + 跟上次 diff 畢業名單變化。
# 跑在 VPS host(cron)。StratValidate 是 self-contained linux build、不需裝 dotnet。
# 部署:scp 本檔 + scripts/strat-validate-bin/ 到 VPS,cron 週跑。
set -uo pipefail

BIN=/opt/b4a/scripts/strat-validate-bin/StratValidate
REPORTS=/opt/b4a/allocate-reports
ENVF=/opt/b4a/tools/.env.trading
mkdir -p "$REPORTS"
TS=$(date -u +%Y%m%d-%H%M)
OUT="$REPORTS/allocate-$TS.txt"

# Discord webhook(從 env 讀、不 echo 出來)
WEBHOOK=$(grep -E '^DISCORD_WEBHOOK_URL=' "$ENVF" 2>/dev/null | head -1 | cut -d= -f2-)

# forward 實盤證據:在 broker 容器內 curl(loopback 守衛只認容器內)→ 暫存檔 → 餵給 --allocate。
# ⚠ 全交易所(不濾):目前只有 bingx 真錢有 realized_pnl,paper 現貨不記已實現損益。
# 真錢實際在賠的腿(≥ALLOC_FORWARD_MIN_TRADES 筆)會被 forward 否決。
FWD=/tmp/b4a-forward.json
docker exec b4a-broker sh -c "curl -s -m 10 'http://localhost:5000/api/v1/trading/strategy-pnl?days=30'" > "$FWD" 2>/dev/null || true

# 跑 allocate(ALLOC_TARGET_VOL_ANNUAL=1.0 → 曝險頂 3x、對齊真錢書;ALLOC_FORWARD_FILE → 接實盤證據)
cd /opt/b4a
ALLOC_TARGET_VOL_ANNUAL=1.0 ALLOC_FORWARD_FILE="$FWD" "$BIN" --allocate > "$OUT" 2>&1 || true

# 通過腿集合(strategy@coin,從可貼 SQL 的註解抽)
CURR=$(grep -oE '\-\- [a-z0-9_]+@[A-Z0-9]+' "$OUT" | sed 's/-- //' | sort -u)

# 上一份報告(第二新)→ diff 畢業名單
PREVFILE=$(ls -1t "$REPORTS"/allocate-*.txt 2>/dev/null | sed -n '2p')
if [ -n "${PREVFILE:-}" ]; then
  PREV=$(grep -oE '\-\- [a-z0-9_]+@[A-Z0-9]+' "$PREVFILE" | sed 's/-- //' | sort -u)
  ADDED=$(comm -23 <(echo "$CURR") <(echo "$PREV") | tr '\n' ' ')
  REMOVED=$(comm -13 <(echo "$CURR") <(echo "$PREV") | tr '\n' ' ')
  if [ -z "${ADDED// }" ] && [ -z "${REMOVED// }" ]; then
    DIFFLINE="無變化(同上次)"
  else
    DIFFLINE="🆕 新進: ${ADDED:-無}｜❌ 掉出: ${REMOVED:-無}"
  fi
else
  DIFFLINE="(第一次跑、無對照)"
fi

# 摘要行
GATE=$(grep "入場閘" "$OUT" | head -1 | sed -E 's/^=== //;s/ ===.*//')
NEFF=$(grep "有效獨立押注數" "$OUT" | head -1 | sed 's/^[[:space:]]*//')
LEGS=$(grep "UPDATE auto_trade_watchlist SET budget_pct" "$OUT" | sed -E "s/.*budget_pct=([0-9]+).*-- (.+)$/• \2: \1%/")

CONTENT=$(printf '📊 **每週 --allocate**(%s UTC)\n%s\n%s\n\n**建議配置(3x):**\n%s\n\n**vs 上次:** %s' \
  "$TS" "${GATE:-(無入場閘行)}" "${NEFF:-}" "${LEGS:-(無通過腿)}" "$DIFFLINE")

# 推 Discord(jq 安全跳脫)
if [ -n "${WEBHOOK:-}" ]; then
  curl -s -m 15 -H "Content-Type: application/json" \
    -d "$(jq -n --arg c "$CONTENT" '{content:$c}')" "$WEBHOOK" >/dev/null && echo "[allocate-weekly] pushed to discord"
else
  echo "[allocate-weekly] no webhook; report saved at $OUT"
fi

# 只留最近 20 份報告
ls -1t "$REPORTS"/allocate-*.txt 2>/dev/null | tail -n +21 | xargs -r rm -f
