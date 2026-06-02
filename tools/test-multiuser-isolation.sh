#!/usr/bin/env bash
# 多用戶隔離端到端驗證 (2026-06-02)。在 VPS 上跑(broker localhost:5100)。
# 用法:  ADMIN_PID=prn_dashboard ADMIN_PW='你的admin密碼' bash tools/test-multiuser-isolation.sh
# 做什麼:admin 登入 → 建一個非-admin 測試「朋友」→ 以朋友身分查 trading/notification endpoint,
#         斷言他【只看得到自己、看不到 admin 的倉/成交】→ admin 控制組對照 → 清掉測試帳號。
# 唯讀交易查詢、不下單、不碰真錢;測試帳號用完即刪。Claude 不經手 admin 密碼——你自己跑。
set -u
B=${BROKER:-http://localhost:5100/api/v1}
ADMIN_PID=${ADMIN_PID:-prn_dashboard}
: "${ADMIN_PW:?請設 ADMIN_PW(admin 密碼)}"
FRIEND=prn_iso_test_$$
FPW='Iso_Test_2026!x'
AC=$(mktemp); FC=$(mktemp)
pass=0; fail=0
ok(){ echo "  ✅ PASS: $1"; pass=$((pass+1)); }
no(){ echo "  ❌ FAIL: $1"; fail=$((fail+1)); }
cleanup(){ curl -s -b "$AC" -X POST "$B/admin/users/$FRIEND/disable" >/dev/null 2>&1
           curl -s -b "$AC" -X DELETE "$B/admin/users/$FRIEND" >/dev/null 2>&1
           rm -f "$AC" "$FC"; echo "(清理:測試帳號 $FRIEND 已停用/刪除)"; }
trap cleanup EXIT

echo "=== 1. admin 登入 ==="
r=$(curl -s -c "$AC" -X POST "$B/auth/login" -H "Content-Type: application/json" \
    -d "{\"principal_id\":\"$ADMIN_PID\",\"password\":\"$ADMIN_PW\"}")
echo "$r" | grep -q '"success":true' || { echo "admin 登入失敗:$r"; exit 1; }
echo "  admin 登入 OK"

echo "=== 2. 建非-admin 測試朋友 $FRIEND ==="
r=$(curl -s -b "$AC" -X POST "$B/admin/users" -H "Content-Type: application/json" \
    -d "{\"principal_id\":\"$FRIEND\",\"password\":\"$FPW\",\"role\":\"user\",\"display_name\":\"isolation-test\"}")
echo "$r" | grep -q '"success":true' || { echo "建用戶失敗:$r"; exit 1; }
echo "  建立 OK(role=user)"

echo "=== 3. 朋友登入 ==="
r=$(curl -s -c "$FC" -X POST "$B/auth/login" -H "Content-Type: application/json" \
    -d "{\"principal_id\":\"$FRIEND\",\"password\":\"$FPW\"}")
echo "$r" | grep -q '"success":true' || { echo "朋友登入失敗:$r"; exit 1; }
echo "  朋友登入 OK"

echo ""
echo "=== 隔離斷言(以朋友身分)==="
# Gap 1.5:朋友沒註冊憑證 → /account 應被 deny、不得看到 admin 真錢帳戶
r=$(curl -s -b "$FC" "$B/trading/account?exchange=bingx")
echo "$r" | grep -q '未註冊' && ok "Gap1.5 /account:朋友無憑證被 deny" || no "Gap1.5 /account 未 deny:$r"

# Gap 2:朋友的 pnl-summary 應 trade_count=0(看不到 admin 的成交)
r=$(curl -s -b "$FC" "$B/trading/pnl-summary?exchange=bingx")
tc=$(echo "$r" | grep -o '"trade_count":[0-9]*' | grep -o '[0-9]*')
[ "${tc:-x}" = "0" ] && ok "Gap2 /pnl-summary:朋友看到 0 筆(非 admin 的成交)" || no "Gap2 /pnl-summary 朋友看到 $tc 筆(應 0):$r"

# Gap 2:朋友 export.csv 應只有 header、無 admin 成交列
r=$(curl -s -b "$FC" "$B/trading/trades/export.csv?exchange=bingx")
lines=$(echo "$r" | grep -c ',')
[ "$lines" -le 1 ] && ok "Gap2 /export.csv:朋友只有表頭、無他人成交" || no "Gap2 /export.csv 朋友看到 $lines 列:$r"

# 推播:朋友的 notification-channels 應 count=0(只看自己、目前無)
r=$(curl -s -b "$FC" "$B/notification-channels")
cc=$(echo "$r" | grep -o '"count":[0-9]*' | grep -o '[0-9]*')
[ "${cc:-x}" = "0" ] && ok "推播 /notification-channels:朋友只看自己(0 個)" || no "推播 channels 朋友看到 $cc:$r"

echo ""
echo "=== 控制組(以 admin 身分,確認 filter 沒把功能弄壞)==="
r=$(curl -s -b "$AC" "$B/trading/pnl-summary?exchange=bingx")
tc=$(echo "$r" | grep -o '"trade_count":[0-9]*' | grep -o '[0-9]*')
[ "${tc:-0}" -gt 0 ] && ok "admin /pnl-summary 看得到全部成交($tc 筆)" || no "admin 看不到成交($tc)— filter 可能過度封鎖:$r"

echo ""
echo "=== 結果:$pass passed / $fail failed ==="
[ "$fail" -eq 0 ] && echo "✅ 多用戶隔離端到端通過" || echo "⚠ 有失敗項、檢視上面 FAIL"
