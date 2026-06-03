#!/usr/bin/env python3
"""
穩定幣供給 probe (2026-06-03) — Tier-3 前沿、免費 DeFiLlama 資料。

假設:穩定幣淨增發 = 等著進場的「乾火藥」/ 資金流入 → 領先/伴隨 crypto 漲。
這是市場「擇時 / regime」信號(方向性、非橫截面)。

紀律關卡(最關鍵):
  1. 預測性:supply 成長 → 「未來」報酬(非當期)。
  2. 結構性/獨立:corr(supply 成長, 當期價格) 要低 —— 否則它只是價格的滯後代理、不是獨立信號
     (你的鐵律:結構性 alpha 的 X 要跟 price 弱相關)。
  3. 穩定:split-half。
資料:DeFiLlama stablecoincharts/all(totalCirculatingUSD.peggedUSD)+ 本機 BTC 1d kline。
"""
import json, glob, math, sys
from datetime import datetime, timezone

try: sys.stdout.reconfigure(encoding="utf-8")
except Exception: pass

SC = r"C:/Users/USER/.cache/brick4agent/stablecoin_all.json"
CACHE = "C:/Users/USER/.cache/brick4agent/klines/"

def dstr(ms): return datetime.fromtimestamp(ms/1000, tz=timezone.utc).strftime("%Y-%m-%d")
def mean(xs): return sum(xs)/len(xs) if xs else 0.0
def std(xs):
    if len(xs)<2: return 0.0
    m=mean(xs); return math.sqrt(sum((x-m)**2 for x in xs)/(len(xs)-1))
def sharpe(xs): s=std(xs); return mean(xs)/s*math.sqrt(365) if s>0 else 0.0
def tstat(xs): s=std(xs); return mean(xs)/(s/math.sqrt(len(xs))) if s>0 and len(xs)>1 else 0.0
def corr(a,b):
    n=min(len(a),len(b)); a,b=a[:n],b[:n]; ma,mb=mean(a),mean(b)
    va=math.sqrt(sum((x-ma)**2 for x in a)); vb=math.sqrt(sum((x-mb)**2 for x in b))
    return sum((a[i]-ma)*(b[i]-mb) for i in range(n))/(va*vb) if va>0 and vb>0 else 0.0
def maxdd(series):
    eq=peak=dd=0.0
    for r in series: eq+=r; peak=max(peak,eq); dd=min(dd,eq-peak)
    return dd

# 穩定幣供給 by date
raw=json.load(open(SC))
supply={}
for r in raw:
    d=datetime.fromtimestamp(int(r["date"]),tz=timezone.utc).strftime("%Y-%m-%d")
    supply[d]=float(r["totalCirculatingUSD"]["peggedUSD"])
# BTC close by date
bf=sorted(glob.glob(f"{CACHE}BTCUSDT-1d-*.json"), key=lambda p:int(p.split("-")[-1].split(".")[0]))[-1]
btc={dstr(int(b[0])):float(b[4]) for b in json.load(open(bf))}

dates=sorted(set(supply)&set(btc))
print(f"穩定幣供給 probe — 共同窗口 {dates[0]} ~ {dates[-1]}({len(dates)} 天)")
print(f"供給 {supply[dates[0]]/1e9:.1f}B → {supply[dates[-1]]/1e9:.1f}B USD")

def growth(d_i, win):
    if d_i-win<0: return None
    s0=supply[dates[d_i-win]]; s1=supply[dates[d_i]]
    return (s1/s0-1.0) if s0>0 else None
def fwd_ret(d_i, win):
    if d_i+win>=len(dates): return None
    c0=btc[dates[d_i]]; c1=btc[dates[d_i+win]]
    return (c1/c0-1.0) if c0>0 else None
def cur_ret(d_i):
    if d_i<1: return None
    c0=btc[dates[d_i-1]]; c1=btc[dates[d_i]]
    return (c1/c0-1.0) if c0>0 else None

print("\n=== 1. 預測性 + 結構獨立性(supply 成長 vs 未來/當期 BTC 報酬)===")
print(f"{'供給成長窗':>10} {'corr→未來7d':>12} {'corr→當期':>10} {'判讀':>20}")
for gw in (7,14,30):
    g, fwd, cur = [], [], []
    for i in range(len(dates)):
        gv=growth(i,gw); fv=fwd_ret(i,7); cv=cur_ret(i)
        if gv is None or fv is None or cv is None: continue
        g.append(gv); fwd.append(fv); cur.append(cv)
    cf=corr(g,fwd); cc=corr(g,cur)
    note = "✅ 領先且獨立" if abs(cf)>0.05 and abs(cc)<0.3 else ("⚠ 像價格代理" if abs(cc)>=0.3 else "~ 預測力弱")
    print(f"{gw:>9}d {cf:>+12.3f} {cc:>+10.3f} {note:>20}")

print("\n=== 2. 擇時策略:供給成長>0 才做多 BTC(否則空手)vs buy&hold ===")
print(f"{'信號窗':>8} {'在市%':>6} {'年化%':>7} {'Sharpe':>7} {'t':>6} {'maxDD%':>7} {'split-half Sh':>16}")
# buy&hold 基準
bh=[cur_ret(i) for i in range(len(dates)) if cur_ret(i) is not None]
print(f"{'B&H':>8} {'100':>6} {mean(bh)*365*100:>7.0f} {sharpe(bh):>7.2f} {tstat(bh):>6.2f} {maxdd(bh)*100:>7.0f}")
for gw in (7,14,30):
    series=[]
    for i in range(len(dates)):
        g=growth(i,gw); r=fwd_ret(i,1)   # 用 i 的 supply 成長決定持有 i→i+1
        if g is None or r is None: continue
        series.append(r if g>0 else 0.0)
    if len(series)<60: continue
    inmkt=sum(1 for i in range(len(dates)) if (growth(i,gw) or -1)>0)/len(dates)
    h=len(series)//2
    print(f"{gw:>7}d {inmkt*100:>6.0f} {mean(series)*365*100:>7.0f} {sharpe(series):>7.2f} {tstat(series):>6.2f} {maxdd(series)*100:>7.0f} {sharpe(series[:h]):>7.2f}/{sharpe(series[h:]):>0.2f}")
print("\n判讀:擇時要贏,得 Sharpe>B&H 且 split-half 兩半正;corr(成長,當期)<0.3 才算結構性獨立信號。")
