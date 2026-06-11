#!/bin/bash
# demo 時另開一個視窗跑這個 → 觀眾一眼看「誰是主 + work 計數」即時變化(殺主/分區時看它跳)。
trap 'echo' EXIT
# 讀 brokersim 自己的 /status 端點(etcdctl 對 v3 JSON-gateway 寫入的 key 讀不到、已驗證)
st(){ docker exec "fsim-node-$1" python3 -c "import urllib.request,json;d=json.load(urllib.request.urlopen('http://localhost:8080',timeout=2));w=d['last_work'];print(d['role'],d['node'],(w.split()[0] if w not in ('','(none)') else ''))" 2>/dev/null; }
while true; do
  L=""; W=""
  for n in a b; do set -- $(st "$n"); [ "${1:-}" = "PRIMARY" ] && { L="$2"; W="$3"; break; }; done
  printf "\r  ▶ LEADER = node-%-2s    work 計數 = %-6s   (Ctrl-C 結束)" "${L:-?}" "${W:-?}"
  sleep 1
done
