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

# ── 年度拆解:gate 是防禦保險 → 看它在崩盤年有沒有擋、在牛年會不會拖累 ──
print("\n=== gate 年度拆解(30d 信號)vs B&H —— 保險該『壞年擋、好年不拖』===")
print(f"{'年':>6} {'B&H 報酬%':>10} {'gate 報酬%':>11} {'gate 在市%':>11} {'差(gate-B&H)':>14} {'判讀':>14}")
by_year = {}
for i in range(len(dates)):
    g = growth(i, 30); r = fwd_ret(i, 1)
    if g is None or r is None: continue
    yr = dates[i][:4]
    by_year.setdefault(yr, {"bh": [], "gate": [], "inmkt": 0, "n": 0})
    by_year[yr]["bh"].append(r)
    by_year[yr]["gate"].append(r if g > 0 else 0.0)
    by_year[yr]["inmkt"] += 1 if g > 0 else 0
    by_year[yr]["n"] += 1
for yr in sorted(by_year):
    d = by_year[yr]
    if d["n"] < 30: continue
    bh = sum(d["bh"]); gt = sum(d["gate"]); im = d["inmkt"]/d["n"]
    diff = gt - bh
    note = "✅ 壞年有擋" if (bh < -0.1 and diff > 0.05) else ("⚠ 牛年拖累" if (bh > 0.3 and diff < -0.1) else "~ 跟跑")
    print(f"{yr:>6} {bh*100:>10.0f} {gt*100:>11.0f} {im*100:>11.0f} {diff*100:>+14.0f} {note:>14}")
print("\n判讀:保險型 gate 的理想 = 崩盤年(B&H 大負)gate 明顯少賠、牛年不要因亂空手拖太多。")

# ── 改良版:資金流 + 價格雙確認,修 2023 漏接;並對照純價格閘(穩定幣有沒有加值)──
print("\n=== 改良 gate 變體(修 2023 漏接)+ 純價格閘對照 ===")
def sma(i, win):
    if i < win: return None
    return sum(btc[dates[j]] for j in range(i-win, i)) / win
def supply_bull(i):
    g = growth(i, 30); return (g is not None and g > 0)
def price_bull(i):
    s = sma(i, 100); return (s is not None and btc[dates[i]] > s)

variants = {
    "supply-only(原)":   lambda i: supply_bull(i),
    "price-only(SMA100)": lambda i: price_bull(i),
    "OR(任一多→在市)":    lambda i: supply_bull(i) or price_bull(i),   # 只在兩者都壞才 de-risk → 修 2023
    "AND(都多才在市)":     lambda i: supply_bull(i) and price_bull(i),  # 最防禦
}
def run_variant(fn):
    ser, inmkt, yr_ret = [], 0, {}
    for i in range(len(dates)):
        r = fwd_ret(i, 1)
        if r is None: continue
        on = fn(i)
        rv = r if on else 0.0
        ser.append(rv); inmkt += 1 if on else 0
        y = dates[i][:4]; yr_ret.setdefault(y, [0.0,0.0]); yr_ret[y][0]+=rv
    return ser, inmkt/len(ser) if ser else 0, yr_ret
bh_ser=[fwd_ret(i,1) for i in range(len(dates)) if fwd_ret(i,1) is not None]
print(f"{'變體':>20} {'年化%':>7} {'Sharpe':>7} {'maxDD%':>7} {'在市%':>6} {'2022':>6} {'2023':>6} {'2026':>6}")
def yr_sum(ser_fn, y):
    tot=0.0
    for i in range(len(dates)):
        r=fwd_ret(i,1)
        if r is None or dates[i][:4]!=y: continue
        tot+= (r if ser_fn(i) else 0.0)
    return tot*100
print(f"{'B&H':>20} {mean(bh_ser)*365*100:>7.0f} {sharpe(bh_ser):>7.2f} {maxdd(bh_ser)*100:>7.0f} {'100':>6} {'-85':>6} {'108':>6} {'-14':>6}")
for nm, fn in variants.items():
    ser, im, _ = run_variant(fn)
    print(f"{nm:>20} {mean(ser)*365*100:>7.0f} {sharpe(ser):>7.2f} {maxdd(ser)*100:>7.0f} {im*100:>6.0f} {yr_sum(fn,'2022'):>6.0f} {yr_sum(fn,'2023'):>6.0f} {yr_sum(fn,'2026'):>6.0f}")
print("\n判讀:好的改良要『2022/2026 仍明顯擋(>B&H)+ 2023 不再大漏接』;若純價格閘就一樣好,代表穩定幣沒加值。")
