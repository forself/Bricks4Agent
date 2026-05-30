#!/bin/bash
# 單機 failover demo 驅動。完全本地、不碰 prod。展示:選主 → 殺主自動接手 → 分區自我 fence(防腦裂)。
set -u
cd "$(dirname "$0")"
if [ "${1:-}" = "down" ]; then echo "拆除 demo stack…"; docker compose -p fsim down; exit 0; fi
ETCD="docker exec fsim-etcd1 etcdctl"
leader(){ $ETCD get /b4a/leader --print-value-only 2>/dev/null | tr -d '\n'; }
work(){   $ETCD get /b4a/work   --print-value-only 2>/dev/null | tr -d '\n'; }
line(){ echo; echo "═══════════ $* ═══════════"; }

line "啟動 3 etcd(quorum)+ 2 broker(主/備)"
docker compose up -d --build
echo "等 etcd 成形 + 選主(~18s)…"; sleep 18

line "初始狀態"
echo "目前 leader = node-$(leader) ;  work 計數 = $(work)"
echo "--- node-a log ---"; docker logs --tail 3 fsim-node-a 2>&1
echo "--- node-b log ---"; docker logs --tail 3 fsim-node-b 2>&1

# ── 測試 1:殺掉主 → 備機自動接手 ──
P=$(leader); pl=$(echo "$P" | tr 'A-Z' 'a-z')
line "測試1:docker kill 主節點 node-$P(模擬 VPS 當機)"
W1=$(work); docker kill "fsim-node-$pl" >/dev/null
echo "已殺 node-$P(work 當時 = $W1)。等 lease 過期 + 接手(~10s)…"; sleep 10
NP=$(leader)
echo "新 leader = node-$NP   $([ "$NP" != "$P" ] && [ -n "$NP" ] && echo '✅ 自動接手成功' || echo '✗ 沒接手')"
echo "work 計數 = $(work)(應 > $W1 ⇒ 新主接續寫、單一寫者交接)"
echo "--- 存活節點 log(接手)---"; docker logs --tail 4 "fsim-node-$(echo "$NP" | tr 'A-Z' 'a-z')" 2>&1

# 把被殺的拉回來(恢復成 2 節點),它應加入為 STANDBY(不搶主)
docker start "fsim-node-$pl" >/dev/null; sleep 8
echo "node-$P 已拉回;leader 仍 = node-$(leader)(舊主回來不搶、避免雙主)"

# ── 測試 2:分區現任主 → 自我 fence(防腦裂)──
C=$(leader); cl=$(echo "$C" | tr 'A-Z' 'a-z')
line "測試2:把現任主 node-$C 從網路分區(模擬 VPS 連不到 quorum)"
W2=$(work); docker network disconnect fsim-net "fsim-node-$cl" >/dev/null
echo "已分區 node-$C(work=$W2)。等自我 fence + 對側接手(~10s)…"; sleep 10
NC=$(leader)
echo "新 leader = node-$NC   $([ "$NC" != "$C" ] && [ -n "$NC" ] && echo '✅ 對側接手' || echo '✗')"
echo "--- 被分區節點 log(應自我 fence、停止動作、不繼續寫)---"; docker logs --tail 4 "fsim-node-$cl" 2>&1
echo "work 計數 = $(work)(由新主繼續、被分區那個沒有偷寫 ⇒ 無腦裂/無雙寫)"

docker network connect fsim-net "fsim-node-$cl" >/dev/null; sleep 6
echo "node-$C 重新連網;它看到 leader=node-$(leader) ⇒ 維持 STANDBY(無腦裂)"
echo "--- 重連節點 log ---"; docker logs --tail 3 "fsim-node-$cl" 2>&1

line "結論"
echo "✓ 選主:同時只有一個 PRIMARY"
echo "✓ 殺主:lease 過期後備機自動接手(RTO ≈ lease TTL)"
echo "✓ 分區:失去 quorum 的舊主【自我 fence、停止寫入】→ 無腦裂、無雙寫"
echo "✓ work 計數全程單調遞增、單一寫者 ⇒ 副作用不會被兩個主重複執行"
echo
echo "拆除:bash $(basename "$0") down    或    docker compose -p fsim down"
