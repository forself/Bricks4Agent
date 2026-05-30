#!/usr/bin/env python3
"""
broker-sim — 模擬 B4A broker 在 3 節點 quorum 下的 leader election + lease 自我 fence。
對應真實架構:
  - 3 個 etcd  = (筆電 + 2 VPS) 的 quorum/見證層
  - node-a/b   = 2 個 broker 實例(主 / 熱備)
  - lease-guarded 寫 WORK_KEY = lease-gated 真錢副作用(自我 fence、防腦裂)
  - WORK_KEY 連續遞增 = 狀態交接 + 單一寫者證明(同一時刻只有一個主在動)
純 stdlib(urllib + base64),不裝任何 pip 套件 → 任何有 Docker 的機器都跑得起來。
"""
import os, time, json, base64, threading, http.server, urllib.request

NODE = os.environ.get("NODE_ID", "?")
ENDPOINTS = os.environ.get("ETCD", "etcd1:2379").split(",")
LEADER_KEY = "/b4a/leader"
WORK_KEY = "/b4a/work"
TTL = int(os.environ.get("LEASE_TTL", "5"))   # 秒;主死後備機最多等這麼久就接手(=RTO)

role = "STARTING"
last_work = "(none)"

def log(m): print(f"[{NODE}] {m}", flush=True)
def b64(s): return base64.b64encode(s.encode()).decode()
def unb64(s): return base64.b64decode(s).decode() if s else ""

def etcd(path, body):
    """打 etcd v3 gRPC-gateway JSON API;輪流試每個 endpoint(模擬連得到 quorum 的那側)。"""
    last = None
    for ep in ENDPOINTS:
        try:
            req = urllib.request.Request(
                f"http://{ep}/v3/{path}", data=json.dumps(body).encode(),
                headers={"Content-Type": "application/json"})
            with urllib.request.urlopen(req, timeout=3) as r:
                return json.loads(r.read())
        except Exception as e:
            last = e; continue
    raise RuntimeError(f"all etcd endpoints unreachable: {last}")

# ── 健康/狀態端點(給 demo 觀察誰是主)──
def serve():
    class H(http.server.BaseHTTPRequestHandler):
        def do_GET(s):
            s.send_response(200); s.send_header("Content-Type", "application/json"); s.end_headers()
            s.wfile.write(json.dumps({"node": NODE, "role": role, "last_work": last_work}).encode())
        def log_message(s, *a): pass
    http.server.HTTPServer(("0.0.0.0", 8080), H).serve_forever()
threading.Thread(target=serve, daemon=True).start()

log(f"啟動,etcd={ENDPOINTS} lease_ttl={TTL}s")

while True:
    try:
        # 1) 取 lease
        lease_id = etcd("lease/grant", {"TTL": TTL})["ID"]
        # 2) 選主:txn — 若 leader key 不存在(version==0)才搶下、綁我的 lease
        r = etcd("kv/txn", {
            "compare": [{"key": b64(LEADER_KEY), "result": "EQUAL", "target": "VERSION", "version": "0"}],
            "success": [{"requestPut": {"key": b64(LEADER_KEY), "value": b64(NODE), "lease": str(lease_id)}}],
        })
        if not r.get("succeeded"):
            role = "STANDBY"
            time.sleep(1); continue   # 已有主、待命;主的 lease 過期後這個 txn 才會成功

        role = "PRIMARY"; log("✅ 取得 leadership → PRIMARY(開始做有副作用的工作)")
        # 3) 主工作迴圈:每次副作用前都【再確認自己還是 leader】(lease 自我 fence)
        while True:
            etcd("lease/keepalive", {"ID": lease_id})       # 續 lease
            cur = etcd("kv/range", {"key": b64(WORK_KEY)})
            kvs = cur.get("kvs", [])
            old = int(unb64(kvs[0]["value"])) if kvs else 0
            ver = kvs[0]["version"] if kvs else "0"
            # SELF-FENCE:只有「leader 還是我 且 work 沒被別人改」才寫 → 失去主權就寫不進、自動停手
            w = etcd("kv/txn", {
                "compare": [
                    {"key": b64(LEADER_KEY), "result": "EQUAL", "target": "VALUE", "value": b64(NODE)},
                    {"key": b64(WORK_KEY), "result": "EQUAL", "target": "VERSION", "version": str(ver)},
                ],
                "success": [{"requestPut": {"key": b64(WORK_KEY), "value": b64(str(old + 1))}}],
            })
            if not w.get("succeeded"):
                role = "STANDBY"; log("⚠ 失去 leadership(lease 過期/被分區)→ 自我 fence、停止寫入 → STANDBY")
                break
            last_work = f"{old + 1} (by {NODE})"; log(f"work → {last_work}")
            time.sleep(1)
    except Exception as e:
        # 連不到 quorum(被分區)→ 無法續 lease/確認主權 → 不動作(自我 fence)
        if role != "STANDBY":
            log(f"⛔ etcd 不可達(可能被分區)→ 自我 fence、停止動作:{e}")
        role = "STANDBY"
        time.sleep(1)
