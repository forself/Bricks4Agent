#!/bin/bash
# B4A 每日 BingX 真錢權益快照 → CSV(誠實資金紀錄;唯讀 account endpoint、不碰交易)
LOG=/opt/b4a/equity-log.csv
TS=$(date -u +"%Y-%m-%dT%H:%M:%SZ")
RESP=$(curl -s --max-time 20 "http://localhost:5100/api/v1/perpetual/account?exchange=bingx")
OK=$(echo "$RESP" | jq -r '.success // false' 2>/dev/null)
if [ "$OK" != "true" ]; then
  echo "$TS,ERR,ERR,ERR,ERR,fetch_failed" >> "$LOG"
  exit 0
fi
EQ=$(echo "$RESP"   | jq -r '.data.equity')
BAL=$(echo "$RESP"  | jq -r '.data.balance')
UPNL=$(echo "$RESP" | jq -r '.data.unrealized_pnl')
POS=$(echo "$RESP"  | jq -r '.data.open_positions_count')
UPD=$(echo "$RESP"  | jq -r '.data.updated_at')
echo "$TS,$EQ,$BAL,$UPNL,$POS,$UPD" >> "$LOG"
