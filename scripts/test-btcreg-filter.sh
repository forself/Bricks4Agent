#!/bin/bash
# 測試 tsmom_btc_not_up wrapper 真實作用(2026-05-27 C 路線 Path B 驗證腳本)
#
# 1. 拉 BTC + TRX 1d bars (Binance public API, no auth)
# 2. 構造 payload (含 ref_btc_bars)
# 3. POST broker /api/v1/strategy/signal
# 4. 看 reason 是否有 [BTCreg:X] tag
#
# 預期 vs 對照:
#   - 帶 ref_btc_bars:reason 開頭應有 "[BTCreg:up|sideways|down] ..."
#   - 不帶 ref_btc_bars:reason 沒 tag(wrapper pass-through)
#
# 跑法:
#   ssh b4a 'bash /opt/b4a/scripts/test-btcreg-filter.sh'
# 或本機:
#   bash scripts/test-btcreg-filter.sh   # 需 broker localhost:5100
#
# 首次驗證紀錄(2026-05-27 23:xx UTC):
#   action: buy, conf: 0.95
#   reason: [BTCreg:sideways] 標準化中期動量 z=3.10 ≥ 0.5、ATR 百分位 74 % 可控 — 做多
#   → BTC 當下 sideways、tsmom_btc_not_up allowedRegimes=["sideways","down"] 通過
#   對照組 reason 無 [BTCreg:] tag → 確認注入機制工作

set -e

BROKER="${BROKER_URL:-http://localhost:5100}"
SYMBOL="${SYMBOL:-TRXUSDT}"

echo "=== 拉 BTC bars ==="
curl -s 'https://api.binance.com/api/v3/klines?symbol=BTCUSDT&interval=1d&limit=200' > /tmp/btc-raw.json
echo "BTC raw bars: $(jq length /tmp/btc-raw.json)"

echo "=== 拉 $SYMBOL bars ==="
curl -s "https://api.binance.com/api/v3/klines?symbol=$SYMBOL&interval=1d&limit=200" > /tmp/tgt-raw.json
echo "$SYMBOL raw bars: $(jq length /tmp/tgt-raw.json)"

# Binance klines: [open_time_ms, open, high, low, close, volume, ...]
echo "=== 轉 broker format ==="
jq -c 'map({
  open_time: (.[0] / 1000 | strftime("%Y-%m-%dT%H:%M:%S.000Z")),
  open: (.[1] | tonumber),
  high: (.[2] | tonumber),
  low: (.[3] | tonumber),
  close: (.[4] | tonumber),
  volume: (.[5] | tonumber)
})' /tmp/btc-raw.json > /tmp/btc-bars.json
jq -c 'map({
  open_time: (.[0] / 1000 | strftime("%Y-%m-%dT%H:%M:%S.000Z")),
  open: (.[1] | tonumber),
  high: (.[2] | tonumber),
  low: (.[3] | tonumber),
  close: (.[4] | tonumber),
  volume: (.[5] | tonumber)
})' /tmp/tgt-raw.json > /tmp/tgt-bars.json

# 構造完整 payload(帶 ref_btc_bars)
jq -n \
  --arg sym "$SYMBOL" \
  --argjson tgt "$(cat /tmp/tgt-bars.json)" \
  --argjson btc "$(cat /tmp/btc-bars.json)" \
  '{
    strategy: "tsmom_btc_not_up",
    symbol: $sym,
    exchange: "binance",
    interval: "1d",
    bars: $tgt,
    ref_btc_bars: $btc
  }' > /tmp/test-payload.json

echo "=== POST $BROKER/api/v1/strategy/signal (with ref_btc_bars)==="
curl -s -X POST "$BROKER/api/v1/strategy/signal" \
  -H 'Content-Type: application/json' \
  -d @/tmp/test-payload.json | jq '{success, action: .data.action, confidence: .data.confidence, reason: .data.reason, error}'

echo ""
echo "=== 對照:無 ref_btc_bars(filter pass-through、reason 不該有 [BTCreg:])==="
jq 'del(.ref_btc_bars)' /tmp/test-payload.json > /tmp/test-no-btc.json
curl -s -X POST "$BROKER/api/v1/strategy/signal" \
  -H 'Content-Type: application/json' \
  -d @/tmp/test-no-btc.json | jq '{success, action: .data.action, confidence: .data.confidence, reason: .data.reason}'

echo ""
echo "=== 判定 ==="
WITH_REASON=$(curl -s -X POST "$BROKER/api/v1/strategy/signal" -H 'Content-Type: application/json' -d @/tmp/test-payload.json | jq -r '.data.reason // ""')
if echo "$WITH_REASON" | grep -qE '\[BTCreg:(up|sideways|down)\]'; then
  echo "✅ PASS — reason 包含 [BTCreg:X] tag、filter 注入機制正常"
  echo "   detected regime: $(echo "$WITH_REASON" | grep -oE '\[BTCreg:[a-z]+\]')"
else
  echo "❌ FAIL — reason 沒 [BTCreg:X] tag、filter 沒生效"
  echo "   actual reason: $WITH_REASON"
  exit 1
fi
