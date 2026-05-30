#!/bin/bash
# demo 時另開一個視窗跑這個 → 觀眾一眼看「誰是主 + work 計數」即時變化(殺主/分區時看它跳)。
trap 'echo' EXIT
while true; do
  L=$(docker exec fsim-etcd1 etcdctl get /b4a/leader --print-value-only 2>/dev/null | tr -d '\n')
  W=$(docker exec fsim-etcd1 etcdctl get /b4a/work   --print-value-only 2>/dev/null | tr -d '\n')
  printf "\r  ▶ LEADER = node-%-2s    work 計數 = %-6s   (Ctrl-C 結束)" "${L:-?}" "${W:-?}"
  sleep 1
done
