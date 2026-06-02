#!/usr/bin/env python3
"""
funding carry — 最後一關:decorr 對照現有 crowding 策略 (2026-06-02)。

問題:funding carry 的 price 腿(long 低 funding / short 高 funding 的價格價差)
      會不會跟你現有的 retail_ls Delta / oi_contrarian 重疊?carry 腿幾乎一定獨立。

用本機 oi-metrics cache(cls=retail 全域多空比、ctls=大戶、oi=未平倉)建相同結構的
cross-sectional dollar-neutral tertile 因子,跟 funding carry 算 corr + 殘差 alpha。

|corr|<0.3 = 去相關、是新 alpha;高 corr = 重疊。殘差 alpha 顯著 = funding carry 在
移除 crowding 因子後仍有獨立報酬。窗口 = oi-metrics 起點(2022-11)之後。
"""
import json, glob, math, sys, os
from datetime import datetime, timezone
from collections import defaultdict

try: sys.stdout.reconfigure(encoding="utf-8")
except Exception: pass

CACHE = "C:/Users/USER/.cache/brick4agent/klines/"
FUND  = "C:/Users/USER/.cache/brick4agent/funding/"
OIM   = "C:/Users/USER/.cache/brick4agent/oi-metrics/"
COINS = ["BTC","ETH","SOL","BNB","XRP","ADA","DOGE","AVAX","LINK","LTC",
         "DOT","ATOM","TRX","UNI","NEAR","APT","ARB","OP","SUI","INJ"]

def largest(pat):
    fs = sorted(glob.glob(pat), key=lambda p: int(p.split("-")[-1].split(".")[0]))
    return fs[-1] if fs else None
def dstr(ms): return datetime.fromtimestamp(ms/1000, tz=timezone.utc).strftime("%Y-%m-%d")

def load_coin(c):
    kf, ff = largest(f"{CACHE}{c}USDT-1d-*.json"), largest(f"{FUND}{c}USDT-*.json")
    if not kf or not ff: return None
    closes = {dstr(int(b[0])): float(b[4]) for b in json.load(open(kf))}
    fday = defaultdict(float)
    for f in json.load(open(ff)): fday[dstr(int(f["fundingTime"]))] += float(f["fundingRate"])
    # oi-metrics:每日檔取 end-of-day cls(retail LS)、oi
    rls, oi = {}, {}
    for path in glob.glob(f"{OIM}{c}USDT/*.json"):
        d = os.path.basename(path)[:10]
        try:
            arr = json.load(open(path))
            if arr:
                last = arr[-1]
                rls[d] = float(last["cls"]); oi[d] = float(last["oi"])
        except Exception: pass
    return closes, fday, rls, oi

def mean(xs): return sum(xs)/len(xs) if xs else 0.0
def std(xs):
    if len(xs) < 2: return 0.0
    m = mean(xs); return math.sqrt(sum((x-m)**2 for x in xs)/(len(xs)-1))
def sharpe(xs): s = std(xs); return mean(xs)/s*math.sqrt(365) if s > 0 else 0.0
def tstat(xs): s = std(xs); return mean(xs)/(s/math.sqrt(len(xs))) if s > 0 and len(xs) > 1 else 0.0
def corr(a, b):
    n = min(len(a), len(b)); a, b = a[:n], b[:n]
    ma, mb = mean(a), mean(b)
    va = math.sqrt(sum((x-ma)**2 for x in a)); vb = math.sqrt(sum((x-mb)**2 for x in b))
    return sum((a[i]-ma)*(b[i]-mb) for i in range(n))/(va*vb) if va > 0 and vb > 0 else 0.0

def factor(data, sigfn, dates, add_carry=False):
    """long 低 signal / short 高 signal(contrarian 結構),dollar-neutral tertile。回 daily price(+carry) 序列(對齊 dates)。"""
    out = []
    for i in range(len(dates)-1):
        t, t1 = dates[i], dates[i+1]
        rows = []
        for c, d in data.items():
            sig = sigfn(c, d, t, dates, i)
            if sig is None: continue
            cl = d[0]
            if t not in cl or t1 not in cl: continue
            rows.append((c, sig, cl[t1]/cl[t]-1.0, d[1].get(t1, 0.0)))
        if len(rows) < 6: out.append(None); continue
        rows.sort(key=lambda x: x[1])
        third = max(1, len(rows)//3)
        lo, hi = rows[:third], rows[-third:]
        price = mean([r[2] for r in lo]) - mean([r[2] for r in hi])
        carry = (mean([-r[3] for r in lo]) + mean([r[3] for r in hi])) if add_carry else 0.0
        out.append(price + carry)
    return out

if __name__ == "__main__":
    print("funding carry decorr vs retail_ls / oi(cross-sectional dollar-neutral、同結構)")
    data = {c: load_coin(c) for c in COINS}
    data = {c: v for c, v in data.items() if v}
    # 共同日期 = 有 oi-metrics 的窗口
    oidates = set()
    for c, d in data.items(): oidates |= set(d[2].keys())
    dates = sorted(d for d in set().union(*[set(v[0].keys()) for v in data.values()]) if d in oidates)
    print(f"載入 {len(data)} 幣,共同窗口 {dates[0]} ~ {dates[-1]}({len(dates)} 天)\n")

    # signal funcs
    def f_funding(c, d, t, ds, i): return d[1].get(t)            # funding level(low→long)
    def f_rls(c, d, t, ds, i):     return d[2].get(t)            # retail LS level(high crowded→short)
    def f_rls_delta(c, d, t, ds, i):
        p = ds[i-1] if i > 0 else None
        return (d[2][t]-d[2][p]) if (p and t in d[2] and p in d[2]) else None
    def f_oi_chg(c, d, t, ds, i):
        p = ds[i-1] if i > 0 else None
        return (d[3][t]/d[3][p]-1.0) if (p and t in d[3] and p in d[3] and d[3].get(p,0)>0) else None

    carry  = factor(data, f_funding,   dates, add_carry=True)
    fprice = factor(data, f_funding,   dates, add_carry=False)   # 只 price 腿
    rls    = factor(data, f_rls,       dates)
    rlsd   = factor(data, f_rls_delta, dates)
    oic    = factor(data, f_oi_chg,    dates)

    def align(*series):
        idx = [i for i in range(len(series[0])) if all(s[i] is not None for s in series)]
        return [[s[i] for i in idx] for s in series]

    print("=== 各因子(同窗口、dollar-neutral tertile)獨立表現 ===")
    for name, s in [("funding carry(含carry)", carry), ("funding price 腿", fprice),
                    ("retail_ls 反向", rls), ("retail_ls Δ 反向", rlsd), ("oi %chg 反向", oic)]:
        ss = [x for x in s if x is not None]
        print(f"  {name:22} 年化 {mean(ss)*365*100:6.1f}%  Sharpe {sharpe(ss):5.2f}  t {tstat(ss):5.2f}  (n={len(ss)})")

    print("\n=== decorr:funding carry vs 現有 crowding 因子 ===")
    for name, other in [("retail_ls", rls), ("retail_ls Δ", rlsd), ("oi %chg", oic)]:
        a, b = align(carry, other)
        ap, bp = align(fprice, other)
        cc, cp = corr(a, b), corr(ap, bp)
        print(f"  corr(funding carry, {name:12}) = {cc:+.3f}   |   corr(price 腿, {name:10}) = {cp:+.3f}"
              + ("  ⚠ 重疊" if abs(cp) > 0.5 else "  ✅ 去相關" if abs(cp) < 0.3 else "  ~ 中度"))

    # 殘差 alpha:把 funding carry 對「最相關的 crowding 因子」回歸、看殘差還有沒有報酬
    print("\n=== 殘差 alpha:funding carry 移除最相關 crowding 因子後 ===")
    best = max([("retail_ls", rls), ("retail_ls Δ", rlsd), ("oi %chg", oic)],
               key=lambda kv: abs(corr(*align(carry, kv[1]))))
    a, b = align(carry, best[1])
    mb = mean(b); vb = sum((x-mb)**2 for x in b)
    ma = mean(a)
    beta = sum((a[i]-ma)*(b[i]-mb) for i in range(len(a)))/vb if vb > 0 else 0
    resid = [a[i] - beta*b[i] for i in range(len(a))]
    print(f"  最相關 = {best[0]}(corr {corr(a,b):+.3f}, beta {beta:+.2f})")
    print(f"  殘差:年化 {mean(resid)*365*100:6.1f}%  Sharpe {sharpe(resid):5.2f}  t {tstat(resid):5.2f}"
          + ("  ✅ 移除後仍有獨立 alpha" if tstat(resid) > 2 else "  ⚠ 殘差不顯著=主要靠重疊那塊"))
