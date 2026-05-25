#!/usr/bin/env python3
"""
B4A Docker Watchdog — host 端獨立容器監看 + 自癒 + 主動推播。

為什麼在 host(不住在 broker 裡):broker 自己可能就是掛掉的那個,
住在 broker 裡的監看 broker 一死就跟著死、報不了案。這支用 systemd 常駐 host。

三件事:
  1) 每 POLL 秒輪詢所有 b4a-* 容器的 docker 狀態(status / health / restartcount)
  2) 透過 broker /api/v1/health/workers 偵測「容器 running 但其實卡死」的 worker
  3) 狀態轉變 → 推 Discord webhook;broker 掛 / crash-loop = CRITICAL(@here 讓人不在也響)

自癒分工:
  - broker(有 healthcheck + label autoheal=true)→ autoheal 容器負責重啟
  - worker(image 無 curl、沒 healthcheck)→ 本 watchdog 偵測卡死後 docker restart
  - 硬崩潰 → 各 service 的 restart:unless-stopped 自動拉起,watchdog 只負責通報

只用 Python 3 標準庫,無第三方相依。
"""
import json
import os
import subprocess
import time
import urllib.request
import collections
from datetime import datetime, timezone

ENV_FILE = os.environ.get("B4A_ENV_FILE", "/opt/b4a/tools/.env.trading")
POLL = int(os.environ.get("WATCHDOG_POLL", "30"))          # 輪詢秒數
DOWN_CONFIRM = 2          # 連續幾次判定 down 才告警(濾掉 deploy / 短暫 flap)
CRASHLOOP_N = 3           # 視窗內幾次重啟 = crash-loop
CRASHLOOP_WINDOW = 600    # crash-loop 視窗(秒)
WORKER_WEDGE_POLLS = 3    # broker 連續幾次回報 worker 卡死才動手
WORKER_RESTART_COOLDOWN = 600  # 同一 worker 自動重啟冷卻(秒)
BROKER = "b4a-broker"

# broker /api/v1/health/workers 的 worker 名稱 → 容器名稱(剛好差一個 b4a- 前綴)
WORKER_MAP = {
    "quote-worker": "b4a-quote-worker",
    "strategy-worker": "b4a-strategy-worker",
    "risk-worker": "b4a-risk-worker",
    "trading-worker": "b4a-trading-worker",
}

RED = 0xF6465D
YEL = 0xF0B90B
GRN = 0x0ECB81


def log(m):
    print(f"[{datetime.now().isoformat(timespec='seconds')}] {m}", flush=True)


def load_webhook():
    try:
        with open(ENV_FILE, encoding="utf-8", errors="ignore") as f:
            for line in f:
                line = line.strip()
                if line.startswith("DISCORD_WEBHOOK_URL="):
                    return line.split("=", 1)[1].strip().strip('"').strip("'")
    except Exception as e:
        log(f"env 讀取失敗: {e}")
    return None


WEBHOOK = load_webhook()


def discord(title, desc, color, critical=False):
    if not WEBHOOK:
        log(f"(無 webhook) {title}: {desc}")
        return
    payload = {
        "embeds": [{
            "title": title,
            "description": desc,
            "color": color,
            "footer": {"text": "B4A Docker Watchdog"},
            "timestamp": datetime.now(timezone.utc).isoformat(),
        }]
    }
    if critical:
        payload["content"] = "@here"   # 強制推播、即使頻道被靜音
    try:
        req = urllib.request.Request(
            WEBHOOK, data=json.dumps(payload).encode(),
            headers={"Content-Type": "application/json",
                     # Discord/Cloudflare 會 403 擋掉 urllib 預設 UA、必須帶自訂 User-Agent
                     "User-Agent": "B4A-Watchdog/1.0"})
        urllib.request.urlopen(req, timeout=15).read()
    except Exception as e:
        log(f"discord 推播失敗: {e}")


def sh(args, timeout=20):
    try:
        return subprocess.run(args, capture_output=True, text=True, timeout=timeout)
    except Exception as e:
        log(f"指令失敗 {args}: {e}")
        return None


def list_containers():
    r = sh(["docker", "ps", "-a", "--filter", "name=b4a-", "--format", "{{.Names}}"])
    if not r or r.returncode != 0:
        return []
    return [n for n in r.stdout.split() if n]


def inspect(name):
    fmt = ("{{.State.Status}}|"
           "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}|"
           "{{.RestartCount}}")
    r = sh(["docker", "inspect", "--format", fmt, name])
    if not r or r.returncode != 0:
        return None
    parts = r.stdout.strip().split("|")
    if len(parts) < 3:
        return None
    try:
        restarts = int(parts[2] or 0)
    except ValueError:
        restarts = 0
    return {"status": parts[0], "health": parts[1], "restarts": restarts}


def broker_worker_health():
    """只在 broker 看起來活著時呼叫;回 {worker_name: status} 或 None。"""
    r = sh(["docker", "exec", BROKER, "curl", "-fsS",
            "http://localhost:5000/api/v1/health/workers"])
    if not r or r.returncode != 0:
        return None
    try:
        d = json.loads(r.stdout)
        d = d.get("data", d)
        return {w["worker"]: w.get("status") for w in d.get("workers", [])}
    except Exception:
        return None


def condition(st):
    """把 inspect 結果歸納成 OK / DOWN / UNHEALTHY。"""
    if st is None:
        return "DOWN"
    if st["status"] in ("exited", "dead", "restarting", "created"):
        return "DOWN"
    if st["health"] == "unhealthy":
        return "UNHEALTHY"
    # running 且 health 為 healthy/none/starting → 視為 OK(starting 不告警、給開機緩衝)
    return "OK"


def main():
    prev_restarts = {}                                   # name -> restartcount
    bad_streak = collections.defaultdict(int)            # name -> 連續非 OK 次數
    alerted_bad = {}                                     # name -> 目前告警中的狀態(DOWN/UNHEALTHY)
    restart_times = collections.defaultdict(collections.deque)  # name -> deque[ts]
    crashloop_alerted = {}                              # name -> ts
    wedge_count = collections.defaultdict(int)          # 容器 -> 連續卡死次數
    last_worker_restart = {}                            # 容器 -> ts

    log(f"watchdog 啟動,webhook={'已設定' if WEBHOOK else '缺失!'},poll={POLL}s")
    discord("🟢 B4A Watchdog 上線",
            f"Docker 容器監看已啟動,每 {POLL}s 輪詢一次。掛掉 / 卡死 / crash-loop 會即時推播。",
            GRN)

    first_round = True
    while True:
        names = list_containers()
        now = time.time()
        cur = {}
        for n in names:
            st = inspect(n)
            cur[n] = st
            crit = (n == BROKER)
            cond = condition(st)

            # 第一輪只建基線、不告警(避免重啟 watchdog 就洗一輪)
            if first_round:
                prev_restarts[n] = st["restarts"] if st else 0
                continue

            # ── 狀態機:DOWN / UNHEALTHY 需連續 DOWN_CONFIRM 次才告警 ──
            if cond == "OK":
                if alerted_bad.get(n):
                    discord(f"✅ {n} 已恢復",
                            f"狀態 running / health {st['health']}。", GRN)
                    alerted_bad.pop(n, None)
                bad_streak[n] = 0
            else:
                bad_streak[n] += 1
                if bad_streak[n] >= DOWN_CONFIRM and alerted_bad.get(n) != cond:
                    alerted_bad[n] = cond
                    if cond == "DOWN":
                        extra = ("**broker 掛掉 = 真錢持倉暫無 SL 管理,請盡快確認。**"
                                 if crit else "restart policy / autoheal 會嘗試自動拉起。")
                        discord(f"{'🔴 CRITICAL ' if crit else '⚠️ '}{n} 掛了",
                                f"狀態 → {st['status'] if st else 'missing'}。{extra}",
                                RED, critical=crit)
                    else:  # UNHEALTHY
                        extra = "autoheal 會自動重啟。" if crit else "watchdog 監看中。"
                        desc = ("healthcheck 連續失敗 — process 還活著但沒回應"
                                f"(卡死 / deadlock)。{extra}")
                        discord(f"{'🔴 ' if crit else '⚠️ '}{n} unhealthy",
                                desc, YEL, critical=crit)

            # ── RestartCount 上升 = 真的被 Docker 重啟過(自家 rebuild 是新容器、不會觸發)──
            pr = prev_restarts.get(n)
            if st and pr is not None and st["restarts"] > pr:
                dq = restart_times[n]
                dq.append(now)
                while dq and now - dq[0] > CRASHLOOP_WINDOW:
                    dq.popleft()
                if len(dq) >= CRASHLOOP_N:
                    if now - crashloop_alerted.get(n, 0) > CRASHLOOP_WINDOW:
                        crashloop_alerted[n] = now
                        discord(f"🔴 CRITICAL {n} CRASH-LOOP",
                                f"{len(dq)} 次重啟 / {CRASHLOOP_WINDOW // 60} 分鐘 — "
                                "自動重啟救不回,需人工介入。",
                                RED, critical=True)
                else:
                    discord(f"🔁 {n} 重啟了",
                            f"RestartCount {pr} → {st['restarts']}(已自動拉起)。", YEL)
            if st:
                prev_restarts[n] = st["restarts"]

        # 消失的容器(compose down / rm)→ 清狀態、不糾纏
        for n in list(prev_restarts.keys()):
            if n not in cur:
                prev_restarts.pop(n, None)
                bad_streak.pop(n, None)
                alerted_bad.pop(n, None)

        # ── worker 卡死但 running(broker 回報)→ watchdog 自動 docker restart ──
        bst = cur.get(BROKER)
        if not first_round and bst and condition(bst) == "OK":
            wh = broker_worker_health()
            if wh:
                for wname, cname in WORKER_MAP.items():
                    status = wh.get(wname)
                    cst = cur.get(cname)
                    if cst and cst["status"] == "running" and status in ("error", "disconnected"):
                        wedge_count[cname] += 1
                        if wedge_count[cname] == WORKER_WEDGE_POLLS:
                            if now - last_worker_restart.get(cname, 0) > WORKER_RESTART_COOLDOWN:
                                last_worker_restart[cname] = now
                                discord(f"⚠️ {cname} 卡死,自動重啟",
                                        f"容器 running 但 broker 連續 {WORKER_WEDGE_POLLS} 次回報 "
                                        f"{status} — watchdog 執行 docker restart。", YEL)
                                sh(["docker", "restart", cname], timeout=60)
                            else:
                                log(f"{cname} 卡死但在冷卻期、暫不重啟")
                    else:
                        wedge_count[cname] = 0

        first_round = False
        time.sleep(POLL)


if __name__ == "__main__":
    main()
