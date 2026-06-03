#!/usr/bin/env python3
"""
擁擠度因子完整驗證 (2026-06-03) — retail_ls / retail_ls_delta / oi 橫截面 contrarian。

decorr 測時這兩個比 funding carry 強(Sharpe 1.71 / 1.42),但只用 per-coin/directional,
沒給過 funding carry 那套完整 gauntlet。這裡補:每因子 split-half / regime(含熊)/ bootstrap CI /
capacity / 成本壓力 + 彼此 + 對 funding/動量 的 decorr。

資料:本機 oi-metrics(cls=retail 全域多空比、oi=未平倉)+ kline + funding。窗口 ~2022-11 起。
方向:contrarian = long 低擁擠 / short 高擁擠(dollar-neutral tertile)。無 lookahead。
"""
import json, glob, math, sys, os, random
from datetime import datetime, timezone
from collections import defaultdict

try: sys.stdout.reconfigure(encoding="utf-8")
except Exception: pass
random.seed(20260603)

CACHE = "C:/Users/USER/.cache/brick4agent/klines/"
FUND  = "C:/Users/USER/.cache/brick4agent/funding/"
OIM   = "C:/Users/USER/.cache/brick4agent/oi-metrics/"
COINS = ["BTC","ETH","SOL","BNB","XRP","ADA","DOGE","AVAX","LINK","LTC",
         "DOT","ATOM","TRX","UNI","NEAR","APT","ARB","OP","SUI","INJ"]
LIQ10 = {"BTC","ETH","SOL","BNB","XRP","DOGE","ADA","AVAX","LINK","LTC"}

def largest(pat):
    fs = sorted(glob.glob(pat), key=lambda p: int(p.split("-")[-1].split(".")[0]))
    return fs[-1] if fs else None
def dstr(ms): return datetime.fromtimestamp(ms/1000, tz=timezone.utc).strftime("%Y-%m-%d")

def load_coin(c):
    kf, ff = largest(f"{CACHE}{c}USDT-1d-*.json"), largest(f"{FUND}{c}USDT-*.json")
    if not kf: return None
    closes = {dstr(int(b[0])): float(b[4]) for b in json.load(open(kf))}
    fday = defaultdict(float)
    if ff:
        for f in json.load(open(ff)): fday[dstr(int(f["fundingTime"]))] += float(f["fundingRate"])
    rls, oi = {}, {}
    for path in glob.glob(f"{OIM}{c}USDT/*.json"):
        d = os.path.basename(path)[:10]
        try:
            arr = json.load(open(path))
            if arr: rls[d] = float(arr[-1]["cls"]); oi[d] = float(arr[-1]["oi"])
        except Exception: pass
    return closes, fday, rls, oi

def mean(xs): return sum(xs)/len(xs) if xs else 0.0
def std(xs):
    if len(xs) < 2: return 0.0
    m = mean(xs); return math.sqrt(sum((x-m)**2 for x in xs)/(len(xs)-1))
def sharpe(xs): s = std(xs); return mean(xs)/s*math.sqrt(365) if s > 0 else 0.0
def tstat(xs): s = std(xs); return mean(xs)/(s/math.sqrt(len(xs))) if s > 0 and len(xs) > 1 else 0.0
def maxdd(series):
    eq=peak=dd=0.0
    for r in series: eq+=r; peak=max(peak,eq); dd=min(dd,eq-peak)
    return dd
def corr(a,b):
    n=min(len(a),len(b)); a,b=a[:n],b[:n]; ma,mb=mean(a),mean(b)
    va=math.sqrt(sum((x-ma)**2 for x in a)); vb=math.sqrt(sum((x-mb)**2 for x in b))
    return sum((a[i]-ma)*(b[i]-mb) for i in range(n))/(va*vb) if va>0 and vb>0 else 0.0
def boot_ci(series,n=2000,block=15):
    N=len(series)
    if N<block*3: return (0,0)
    out=[]; nb=N//block+1
    for _ in range(n):
        s=[]
        for _ in range(nb):
            st=random.randint(0,N-block); s.extend(series[st:st+block])
        out.append(sharpe(s[:N]))
    out.sort(); return (out[int(0.025*n)], out[int(0.975*n)])

def factor(data, sigfn, dates, keep=None, cost=0.0008, hold=3):
    """long 低 signal / short 高 signal、dollar-neutral tertile、hold-day 重排、turnover 成本。回 daily net + dates。"""
    use = {c:d for c,d in data.items() if (keep is None or c in keep)}
    net, dout = [], []
    curL, curS, held = [], [], 0
    for i in range(len(dates)-1):
        t, t1 = dates[i], dates[i+1]
        this_cost = 0.0
        if held == 0 or held >= hold:
            rows=[]
            for c,d in use.items():
                sig = sigfn(c,d,t,dates,i)
                if sig is None: continue
                cl=d[0]
                if t in cl and t1 in cl: rows.append((c,sig,cl[t1]/cl[t]-1.0))
            if len(rows) < 6: held+=1; continue
            rows.sort(key=lambda x:x[1]); third=max(1,len(rows)//3)
            newL=[r[0] for r in rows[:third]]; newS=[r[0] for r in rows[-third:]]
            cur=set(curL+curS); nw=set(newL+newS)
            turn=(len(cur^nw)/max(1,len(cur)+len(nw))) if cur else 1.0
            this_cost=turn*cost*2; curL,curS=newL,newS; held=0
        held+=1
        def ret(c):
            cl=use[c][0]; return cl[t1]/cl[t]-1.0 if (t in cl and t1 in cl) else None
        Lr=[ret(c) for c in curL if ret(c) is not None]; Sr=[ret(c) for c in curS if ret(c) is not None]
        if not Lr: continue
        net.append(mean(Lr)-(mean(Sr) if Sr else 0.0)-this_cost); dout.append(t1)
    return net, dout

def gauntlet(name, data, sigfn, dates, btc_sma):
    net, dts = factor(data, sigfn, dates)
    if len(net) < 60: print(f"  {name}: 樣本不足"); return None, None
    h=len(net)//2
    lo,hi=boot_ci(net)
    # regime
    bidx={d:i for i,d in enumerate(sorted(btc_sma['bd']))}
    bull,bear=[],[]
    for r,d in zip(net,dts):
        on=btc_sma['bull'].get(d)
        if on is True: bull.append(r)
        elif on is False: bear.append(r)
    netL,_=factor(data,sigfn,dates,keep=LIQ10)
    print(f"\n  ── {name} ──")
    print(f"  全期: 年化 {mean(net)*365*100:6.1f}%  Sharpe {sharpe(net):5.2f}  t {tstat(net):5.2f}  maxDD {maxdd(net)*100:5.0f}%  (n={len(net)})")
    print(f"  split-half Sharpe: 前 {sharpe(net[:h]):5.2f} / 後 {sharpe(net[h:]):5.2f}  {'✅' if sharpe(net[:h])>0 and sharpe(net[h:])>0 else '⚠'}")
    print(f"  bootstrap 95% CI Sharpe: [{lo:.2f}, {hi:.2f}]  {'✅ 下界>0' if lo>0 else '⚠ 跨0'}")
    print(f"  regime: 牛 Sh {sharpe(bull):5.2f}(n{len(bull)}) / 熊 Sh {sharpe(bear):5.2f}(n{len(bear)})  {'✅ 牛熊都正' if sharpe(bull)>0 and sharpe(bear)>0 else '⚠ 偏一邊'}")
    print(f"  容量(大10幣): Sharpe {sharpe(netL):5.2f}  {'✅ 可上量' if sharpe(netL)>0.3 else '⚠ 靠小幣'}")
    return net, dts

if __name__ == "__main__":
    print("擁擠度因子完整 gauntlet — retail_ls / retail_ls_delta / oi(橫截面 contrarian、dollar-neutral)")
    data={c:load_coin(c) for c in COINS}; data={c:v for c,v in data.items() if v}
    oid=set()
    for c,d in data.items(): oid|=set(d[2].keys())
    dates=sorted(d for d in set().union(*[set(v[0].keys()) for v in data.values()]) if d in oid)
    print(f"載入 {len(data)} 幣,窗口 {dates[0]} ~ {dates[-1]}({len(dates)} 天)")

    # BTC 200d SMA regime(外生)
    btc=data["BTC"][0]; bd=sorted(btc.keys()); sma={}
    for i,d in enumerate(bd):
        if i>=200: sma[d]=sum(btc[bd[j]] for j in range(i-200,i))/200
    bull={d:(btc[bd[i-1]]>sma[bd[i-1]]) if (i>0 and bd[i-1] in sma) else None for i,d in enumerate(bd)}
    btc_sma={'bd':bd,'bull':bull}

    def f_rls(c,d,t,ds,i): return d[2].get(t)
    def f_rlsd(c,d,t,ds,i):
        p=ds[i-1] if i>0 else None
        return (d[2][t]-d[2][p]) if (p and t in d[2] and p in d[2]) else None
    def f_oi(c,d,t,ds,i):
        p=ds[i-1] if i>0 else None
        return (d[3][t]/d[3][p]-1.0) if (p and t in d[3] and p in d[3] and d[3].get(p,0)>0) else None
    def f_fund(c,d,t,ds,i): return d[1].get(t)
    def f_mom(c,d,t,ds,i):
        di=i-20; return (d[0][t]/d[0][ds[di]]-1.0) if (di>=0 and ds[di] in d[0] and t in d[0]) else None

    print("\n=== 各擁擠度因子完整 gauntlet ===")
    nr,_=gauntlet("retail_ls 反向", data, f_rls, dates, btc_sma)
    nrd,_=gauntlet("retail_ls Δ 反向", data, f_rlsd, dates, btc_sma)
    no,_=gauntlet("oi %chg 反向", data, f_oi, dates, btc_sma)

    print("\n=== 成本壓力(retail_ls / oi、淨 Sharpe vs 每側成本)===")
    for nm,fn in [("retail_ls",f_rls),("oi",f_oi)]:
        row=[]
        for cps in (0.0008,0.0016,0.0030):
            n,_=factor(data,fn,dates,cost=cps); row.append(f"{cps*1e4:.0f}bps→Sh{sharpe(n):.2f}")
        print(f"  {nm:10}: "+"  ".join(row))

    print("\n=== decorr 矩陣(這些 + funding + 動量;都低相關=各自獨立 alpha)===")
    nf,_=factor(data,f_fund,dates); nm_,_=factor(data,f_mom,dates)
    series={"retail_ls":nr,"retail_lsΔ":nrd,"oi":no,"funding":nf,"動量20d":nm_}
    series={k:v for k,v in series.items() if v}
    keys=list(series); print("           "+"  ".join(f"{k:>9}" for k in keys))
    for a in keys:
        print(f"  {a:9} "+"  ".join(f"{corr(series[a],series[b]):+9.2f}" for b in keys))
