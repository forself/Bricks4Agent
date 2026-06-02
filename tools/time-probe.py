#!/usr/bin/env python3
"""
外生時間效應 probe (2026-06-02) — 虛擬貨幣的「時間」是不是獨立 alpha 源?

測三個 TradingView「時間/週期」工具背後唯一可能成立的東西:外生時間效應
(時間外生於價格、符合「結構性 alpha 要跟 price 弱相關」鐵律)。

方法論(比照 strat-validate 的紀律,不被跨幣相關性灌水):
  1. 每個幣各自算 bucket 的「超額報酬」(該 bucket 平均 - 全期平均)。
  2. 把 20 個幣的超額報酬當 20 個獨立觀測,做橫截面 t 檢定(保守:coins 高度相關,
     pooled-all-bars t 會嚴重高估,所以用 per-coin means 當樣本)。
  3. 報「跨幣同號比例」——真效應要跨幣方向一致,不只是聚合顯著。
  4. 對照組 + 經濟意義並陳。純讀本地 kline/funding cache、stdlib only。

probe A: hour-of-day (UTC 0-23) 季節性  —— 1h klines
probe B: day-of-week 季節性             —— 1d klines
probe C: funding 結算時點 timing        —— 結算邊界 bar(00/08/16 UTC)報酬 vs 其他,
                                            並依 funding 正負分層(極端 funding → 結算回歸?)
"""
import json, glob, math, os, sys
from datetime import datetime, timezone
from collections import defaultdict

try: sys.stdout.reconfigure(encoding="utf-8")
except Exception: pass

CACHE = "C:/Users/USER/.cache/brick4agent/klines/"
FUND  = "C:/Users/USER/.cache/brick4agent/funding/"
COINS = ["BTC","ETH","SOL","BNB","XRP","ADA","DOGE","AVAX","LINK","LTC",
         "DOT","ATOM","TRX","UNI","NEAR","APT","ARB","OP","SUI","INJ"]

def load_klines(coin, interval):
    files = sorted(glob.glob(f"{CACHE}{coin}USDT-{interval}-*.json"),
                   key=lambda p: int(p.split("-")[-1].split(".")[0]))
    if not files: return None
    return json.load(open(files[-1]))  # 取最多根那檔

def log_returns_with_time(bars):
    """回 [(open_ms, log_ret)],ret 是 close_{t-1}→close_t、accrue 在 bar t。"""
    out = []
    for i in range(1, len(bars)):
        c0 = float(bars[i-1][4]); c1 = float(bars[i][4])
        if c0 > 0 and c1 > 0:
            out.append((int(bars[i][0]), math.log(c1/c0)))
    return out

def mean(xs): return sum(xs)/len(xs) if xs else 0.0
def std(xs):
    if len(xs) < 2: return 0.0
    m = mean(xs); return math.sqrt(sum((x-m)**2 for x in xs)/(len(xs)-1))
def tstat(xs):
    if len(xs) < 2: return 0.0
    s = std(xs); return mean(xs)/(s/math.sqrt(len(xs))) if s > 0 else 0.0

def cross_sectional(excess_by_coin):
    """excess_by_coin: list of per-coin 超額值。回 (mean_bps, t, n_pos, n)。"""
    n = len(excess_by_coin)
    if n < 3: return (0,0,0,n)
    npos = sum(1 for x in excess_by_coin if x > 0)
    return (mean(excess_by_coin)*1e4, tstat(excess_by_coin), npos, n)

# ── Probe A: hour-of-day ──────────────────────────────────────────────
def probe_hour():
    # per coin: 每 hour bucket 的平均報酬 - 全期平均 = 超額
    excess = defaultdict(list)  # hour -> [per-coin excess]
    coverage = {}
    for c in COINS:
        bars = load_klines(c, "1h")
        if not bars: continue
        rets = log_returns_with_time(bars)
        coverage[c] = len(rets)
        by_h = defaultdict(list)
        for ms, r in rets:
            h = datetime.fromtimestamp(ms/1000, tz=timezone.utc).hour
            by_h[h].append(r)
        gm = mean([r for _, r in rets])
        for h in range(24):
            if by_h[h]:
                excess[h].append(mean(by_h[h]) - gm)
    rows = []
    for h in range(24):
        bps, t, npos, n = cross_sectional(excess[h])
        rows.append((h, bps, t, npos, n))
    return rows, coverage

# ── Probe B: day-of-week ──────────────────────────────────────────────
def probe_dow():
    excess = defaultdict(list)
    for c in COINS:
        bars = load_klines(c, "1d")
        if not bars: continue
        rets = log_returns_with_time(bars)
        by_d = defaultdict(list)
        for ms, r in rets:
            d = datetime.fromtimestamp(ms/1000, tz=timezone.utc).weekday()  # 0=Mon
            by_d[d].append(r)
        gm = mean([r for _, r in rets])
        for d in range(7):
            if by_d[d]:
                excess[d].append(mean(by_d[d]) - gm)
    names = ["Mon","Tue","Wed","Thu","Fri","Sat","Sun"]
    rows = []
    for d in range(7):
        bps, t, npos, n = cross_sectional(excess[d])
        rows.append((names[d], bps, t, npos, n))
    return rows

# ── Probe C: funding-settlement timing ────────────────────────────────
def load_funding(coin):
    files = sorted(glob.glob(f"{FUND}{coin}USDT-*.json"),
                   key=lambda p: int(p.split("-")[-1].split(".")[0]))
    if not files: return None
    return json.load(open(files[-1]))

def probe_settlement():
    """
    結算時點 = funding 結算(00/08/16 UTC)那根 1h bar。
    H1: 結算 bar 報酬 vs 非結算 bar(純 timing)。
    H2: 依「該結算當下的 funding 正負」分層:極端正 funding(多頭擁擠付費)→
        結算 bar 是否傾向下跌(回歸)?這結合 funding LEVEL + TIMING、跟現有 funding 策略不同。
    """
    settle_excess = []      # 結算 bar 超額(per coin)
    pos_fund_settle = []     # funding>0 時結算 bar 平均報酬(per coin)
    neg_fund_settle = []     # funding<0 時結算 bar 平均報酬(per coin)
    for c in COINS:
        bars = load_klines(c, "1h")
        fund = load_funding(c)
        if not bars or not fund: continue
        rets = log_returns_with_time(bars)
        # funding 時點→rate 的查表(step:用 ≤ bar 時間的最近一筆 funding)
        ft = sorted((int(f["fundingTime"]), float(f["fundingRate"])) for f in fund)
        import bisect
        times = [t for t, _ in ft]
        def last_fund(ms):
            i = bisect.bisect_right(times, ms) - 1
            return ft[i][1] if i >= 0 else None
        settle, nonsettle, posf, negf = [], [], [], []
        for ms, r in rets:
            h = datetime.fromtimestamp(ms/1000, tz=timezone.utc).hour
            is_settle = h in (0, 8, 16)
            (settle if is_settle else nonsettle).append(r)
            if is_settle:
                fr = last_fund(ms)
                if fr is not None:
                    (posf if fr > 0 else negf).append(r)
        if settle and nonsettle:
            settle_excess.append(mean(settle) - mean(nonsettle))
        if posf: pos_fund_settle.append(mean(posf))
        if negf: neg_fund_settle.append(mean(negf))
    return (cross_sectional(settle_excess),
            cross_sectional(pos_fund_settle),
            cross_sectional(neg_fund_settle))

def probe_hour_splithalf():
    """前半 vs 後半 100 天的 24-hour 效應向量是否一致(Pearson corr)。
    真季節性 → 兩半相關高(>0.5);樣本 artifact → 接近 0 或負。"""
    def hour_vec(half):  # half: 'first' / 'second'
        ex = defaultdict(list)
        for c in COINS:
            bars = load_klines(c, "1h")
            if not bars: continue
            mid = len(bars)//2
            seg = bars[:mid] if half == "first" else bars[mid:]
            rets = log_returns_with_time(seg)
            by_h = defaultdict(list)
            for ms, r in rets:
                by_h[datetime.fromtimestamp(ms/1000, tz=timezone.utc).hour].append(r)
            gm = mean([r for _, r in rets])
            for h in range(24):
                if by_h[h]: ex[h].append(mean(by_h[h]) - gm)
        return [mean(ex[h]) for h in range(24)]
    a, b = hour_vec("first"), hour_vec("second")
    ma, mb = mean(a), mean(b)
    cov = sum((a[i]-ma)*(b[i]-mb) for i in range(24))
    va = math.sqrt(sum((x-ma)**2 for x in a)); vb = math.sqrt(sum((x-mb)**2 for x in b))
    corr = cov/(va*vb) if va > 0 and vb > 0 else 0.0
    # 各半最強 hour 是否同一個方向
    return corr, a, b

def probe_session():
    """session 區塊(UTC):Asia 0-7、Europe 8-14、US 15-23。比單一 hour 穩、有經濟意義。"""
    blocks = {"Asia(0-7)": range(0,8), "Europe(8-14)": range(8,15), "US(15-23)": range(15,24)}
    excess = {k: [] for k in blocks}
    for c in COINS:
        bars = load_klines(c, "1h")
        if not bars: continue
        rets = log_returns_with_time(bars)
        gm = mean([r for _, r in rets])
        by_block = {k: [] for k in blocks}
        for ms, r in rets:
            h = datetime.fromtimestamp(ms/1000, tz=timezone.utc).hour
            for k, rng in blocks.items():
                if h in rng: by_block[k].append(r)
        for k in blocks:
            if by_block[k]: excess[k].append(mean(by_block[k]) - gm)
    rows = []
    for k in blocks:
        bps, t, npos, n = cross_sectional(excess[k])
        rows.append((k, bps, t, npos, n))
    return rows

def fmt(rows, label):
    print(f"\n=== {label} ===")
    print(f"{'bucket':>8} {'excess(bps)':>12} {'xsec_t':>8} {'sign_agree':>12}")
    for r in rows:
        b, bps, t, npos, n = r
        flag = "  <-- |t|>2 + 一致" if abs(t) > 2 and (npos >= 0.7*n or npos <= 0.3*n) else ""
        print(f"{str(b):>8} {bps:>12.3f} {t:>8.2f} {f'{npos}/{n}':>12}{flag}")

if __name__ == "__main__":
    print("外生時間效應 probe — 20 幣、本地 cache、橫截面 t(保守、不被跨幣相關灌水)")
    print("判讀:|xsec_t|>2 且跨幣同號 >=70% 才算可能真效應;否則 = 噪音/無 edge。")

    hour_rows, cov = probe_hour()
    print(f"\n1h 覆蓋:{min(cov.values())}~{max(cov.values())} 根/幣 ({len(cov)} 幣)")
    fmt(hour_rows, "Probe A: Hour-of-day (UTC) 超額報酬")

    corr, a, b = probe_hour_splithalf()
    print(f"\n--- Probe A 穩定性:前半 vs 後半 24-hour 效應 Pearson corr = {corr:.3f} ---")
    print("    (>0.5=季節性穩定可信;~0/負=樣本 artifact、不可交易)")

    fmt(probe_session(), "Probe A': Session 區塊(UTC、比單一 hour 穩)")

    fmt(probe_dow(), "Probe B: Day-of-week 超額報酬 (1d)")

    s, pf, nf = probe_settlement()
    print("\n=== Probe C: Funding 結算時點 timing ===")
    print(f"{'case':>28} {'val(bps)':>10} {'xsec_t':>8} {'sign_agree':>12}")
    for name, r in [("結算 bar vs 非結算(超額)", s),
                    ("funding>0 時結算 bar 報酬", pf),
                    ("funding<0 時結算 bar 報酬", nf)]:
        bps, t, npos, n = r
        flag = "  <-- 顯著" if abs(t) > 2 and (npos >= 0.7*n or npos <= 0.3*n) else ""
        print(f"{name:>28} {bps:>10.3f} {t:>8.2f} {f'{npos}/{n}':>12}{flag}")
    print("\n(經濟意義:H2 若 funding>0 結算 bar 顯著為負 = 多頭擁擠在結算前後被壓 = 可交易的回歸 timing)")
