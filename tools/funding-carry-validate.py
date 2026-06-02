#!/usr/bin/env python3
"""
橫截面 funding carry — 完整驗證 (2026-06-02)。

比照 tools/xsec-factor 的嚴謹度(turnover-based 真實成本、hold-days 降換手、decorr、t-stat)
+ 加 bootstrap 95% CI。dollar-neutral、等權、tertile(long 低 funding / short 高 funding)。

無 lookahead:date t 收盤用 ≤t 的 funding 排序、賺 t+1 起持有期的價格 + funding;hold_days 才重排。
拆解 carry leg(收 funding 價差)vs price leg(擁擠回歸的價格價差)。
decorr:對照「市場 beta(等權全幣)」+「20d 動量因子」確認市場中性 + 邊際 Sharpe 貢獻。
"""
import json, glob, math, sys, random
from datetime import datetime, timezone
from collections import defaultdict

try: sys.stdout.reconfigure(encoding="utf-8")
except Exception: pass
random.seed(20260602)  # 固定種子、可重現

CACHE = "C:/Users/USER/.cache/brick4agent/klines/"
FUND  = "C:/Users/USER/.cache/brick4agent/funding/"
COINS = ["BTC","ETH","SOL","BNB","XRP","ADA","DOGE","AVAX","LINK","LTC",
         "DOT","ATOM","TRX","UNI","NEAR","APT","ARB","OP","SUI","INJ"]
COST_PER_SIDE = 0.0008   # 8bps/側(比照 xsec-factor 的真實假設)

def largest(pat):
    fs = sorted(glob.glob(pat), key=lambda p: int(p.split("-")[-1].split(".")[0]))
    return fs[-1] if fs else None
def dstr(ms): return datetime.fromtimestamp(ms/1000, tz=timezone.utc).strftime("%Y-%m-%d")

def load():
    data = {}
    for c in COINS:
        kf, ff = largest(f"{CACHE}{c}USDT-1d-*.json"), largest(f"{FUND}{c}USDT-*.json")
        if not kf or not ff: continue
        closes = {dstr(int(b[0])): float(b[4]) for b in json.load(open(kf))}
        fday = defaultdict(float)
        for f in json.load(open(ff)): fday[dstr(int(f["fundingTime"]))] += float(f["fundingRate"])
        data[c] = (closes, fday)
    return data

def mean(xs): return sum(xs)/len(xs) if xs else 0.0
def std(xs):
    if len(xs) < 2: return 0.0
    m = mean(xs); return math.sqrt(sum((x-m)**2 for x in xs)/(len(xs)-1))
def sharpe(xs): s = std(xs); return mean(xs)/s*math.sqrt(365) if s > 0 else 0.0
def tstat(xs): s = std(xs); return mean(xs)/(s/math.sqrt(len(xs))) if s > 0 and len(xs) > 1 else 0.0
def maxdd(series):
    eq = 0.0; peak = 0.0; dd = 0.0
    for r in series:
        eq += r; peak = max(peak, eq); dd = min(dd, eq-peak)
    return dd
def corr(a, b):
    n = min(len(a), len(b)); a, b = a[:n], b[:n]
    ma, mb = mean(a), mean(b)
    cov = sum((a[i]-ma)*(b[i]-mb) for i in range(n))
    va = math.sqrt(sum((x-ma)**2 for x in a)); vb = math.sqrt(sum((x-mb)**2 for x in b))
    return cov/(va*vb) if va > 0 and vb > 0 else 0.0

def block_bootstrap_sharpe(series, n_boot=2000, block=15):
    """block bootstrap(尊重自相關)→ 年化 Sharpe 的 95% CI。"""
    N = len(series)
    if N < block*3: return (0,0)
    sharpes = []
    nblocks = N // block + 1
    for _ in range(n_boot):
        samp = []
        for _ in range(nblocks):
            s = random.randint(0, N-block)
            samp.extend(series[s:s+block])
        samp = samp[:N]
        sharpes.append(sharpe(samp))
    sharpes.sort()
    return (sharpes[int(0.025*n_boot)], sharpes[int(0.975*n_boot)])

def simulate(data, signal_kind, hold_days, cost_per_side):
    """
    signal_kind: 'funding'(long 低)/ 'momentum'(long 高 trailing-20d)/ 'market'(等權全幣 long-only)。
    回 (net_daily, carry_daily, price_daily, avg_turnover)。
    """
    dates = sorted(set().union(*[set(d[0].keys()) for d in data.values()]))
    net, carry_s, price_s, turns, dout = [], [], [], [], []
    curL, curS = [], []
    days_held = 0
    MOM_LB = 20
    for i in range(len(dates)-1):
        t, t1 = dates[i], dates[i+1]
        # 重排?(hold_days 到期 或 首日)
        if days_held == 0 or days_held >= hold_days:
            rows = []
            for c, (closes, fday) in data.items():
                if t not in closes or t1 not in closes: continue
                if signal_kind == "funding":
                    sig = fday.get(t, 0.0)
                elif signal_kind == "momentum":
                    di = i - MOM_LB
                    if di < 0 or dates[di] not in closes: continue
                    sig = closes[t]/closes[dates[di]] - 1.0
                else:  # market
                    sig = 0.0
                rows.append((c, sig))
            if signal_kind == "market":
                newL = [r[0] for r in rows]; newS = []
            else:
                if len(rows) < 6: continue
                rows.sort(key=lambda x: x[1])
                third = max(1, len(rows)//3)
                if signal_kind == "funding":
                    newL = [r[0] for r in rows[:third]]      # 低 funding → long
                    newS = [r[0] for r in rows[-third:]]     # 高 funding → short
                else:  # momentum: long 贏家
                    newL = [r[0] for r in rows[-third:]]
                    newS = [r[0] for r in rows[:third]]
            # turnover(換手比例)
            allcur = set(curL+curS); allnew = set(newL+newS)
            turnover = (len(allcur ^ allnew) / max(1, len(allcur)+len(allnew))) if allcur else 1.0
            curL, curS = newL, newS; days_held = 0
            this_cost = turnover * cost_per_side * 2
        else:
            this_cost = 0.0; turnover = 0.0
        days_held += 1
        # 當日報酬
        def ret(c):
            cl = data[c][0]; return cl[t1]/cl[t]-1.0 if (t in cl and t1 in cl) else None
        Lr = [ret(c) for c in curL if ret(c) is not None]
        Sr = [ret(c) for c in curS if ret(c) is not None]
        if not Lr: continue
        price = mean(Lr) - (mean(Sr) if Sr else 0.0)
        # carry:long 付 -f、short 收 +f(t+1 結算)
        def fnd(c): return data[c][1].get(t1, 0.0)
        carry = (mean([-fnd(c) for c in curL]) + (mean([fnd(c) for c in curS]) if curS else 0.0)) if signal_kind != "market" else 0.0
        net.append(price + carry - this_cost); carry_s.append(carry); price_s.append(price); dout.append(t1)
        if this_cost > 0: turns.append(turnover)
    return net, carry_s, price_s, (mean(turns) if turns else 0.0), dout

def line(label, s):
    print(f"  {label:28} 年化 {mean(s)*365*100:7.1f}%  Sh {sharpe(s):5.2f}  t {tstat(s):6.2f}  maxDD {maxdd(s)*100:6.0f}%")

if __name__ == "__main__":
    print("橫截面 funding carry — 完整驗證(dollar-neutral, tertile, 比照 xsec-factor 嚴謹度)")
    data = load()
    print(f"載入 {len(data)} 幣\n")

    print("=== 1. hold-days × 真實成本(8bps/側 × turnover)===")
    best = None
    for hold in (1, 3, 5, 10):
        net, carry, price, turn, _ = simulate(data, "funding", hold, COST_PER_SIDE)
        print(f"\n  hold={hold}d (avg turnover {turn*100:.0f}%/rebal, n={len(net)})")
        line("淨(carry+price-cost)", net)
        line("├ carry leg", carry)
        line("└ price leg", price)
        h = len(net)//2
        print(f"  {'split-half Sh':28} 前 {sharpe(net[:h]):5.2f} / 後 {sharpe(net[h:]):5.2f}"
              + ("  ✅ 兩半都正" if sharpe(net[:h])>0 and sharpe(net[h:])>0 else "  ⚠ 不穩"))
        if best is None or sharpe(net) > sharpe(best[0]): best = (net, hold, carry, price)

    net, hold, carry, price = best
    print(f"\n=== 2. 最佳配置 hold={hold}d:bootstrap 95% CI(block=15, 2000 次)===")
    lo, hi = block_bootstrap_sharpe(net)
    print(f"  淨年化 Sharpe {sharpe(net):.2f},95% CI [{lo:.2f}, {hi:.2f}]  {'✅ 下界>0 顯著' if lo>0 else '⚠ CI 跨 0'}")
    loc, hic = block_bootstrap_sharpe(carry)
    print(f"  carry leg Sharpe {sharpe(carry):.2f},95% CI [{loc:.2f}, {hic:.2f}]  {'✅ 下界>0' if loc>0 else '⚠'}")

    print(f"\n=== 3. 去相關 / 邊際貢獻(對照市場 beta + 20d 動量因子)===")
    mkt, _, _, _, _ = simulate(data, "market", hold, 0.0)
    mom, _, _, _, _ = simulate(data, "momentum", hold, COST_PER_SIDE)
    n = min(len(net), len(mkt), len(mom))
    cm = corr(net[-n:], mkt[-n:]); cx = corr(net[-n:], mom[-n:])
    print(f"  corr(carry, 市場 beta)  = {cm:+.3f}  {'✅ 市場中性' if abs(cm)<0.3 else '⚠ 有 beta'}")
    print(f"  corr(carry, 20d 動量因子) = {cx:+.3f}  {'✅ 跟動量去相關' if abs(cx)<0.3 else '⚠ 重疊'}")
    sA, sB = sharpe(net), sharpe(mom)
    for rho in (cx,):
        comb = (sA+sB)/math.sqrt(2*(1+rho)) if (1+rho) > 0 else 0
        print(f"  邊際:carry(Sh {sA:.2f}) + 動量(Sh {sB:.2f}) @corr {rho:+.2f} 等波動合併 → 組合 Sharpe ~{comb:.2f}")

    # ── 4. regime 穩定性(含 2022 熊市)+ drop-one 廣度 ──
    print(f"\n=== 4. Regime 穩定性(hold={hold}d net、含熊市)===")
    netd, _, _, _, dts = simulate(data, "funding", hold, COST_PER_SIDE)
    by_year = defaultdict(list)
    for r, d in zip(netd, dts): by_year[d[:4]].append(r)
    for yr in sorted(by_year):
        s = by_year[yr]
        if len(s) < 20: continue
        print(f"  {yr}: 年化 {mean(s)*365*100:7.1f}%  Sh {sharpe(s):5.2f}  t {tstat(s):5.2f}  (n={len(s)})")
    # 牛熊:BTC 90d trailing return 正=牛、負=熊
    btc = data.get("BTC", (None,))[0]
    if btc:
        bdates = sorted(btc.keys())
        idx = {d: i for i, d in enumerate(bdates)}
        bull, bear = [], []
        for r, d in zip(netd, dts):
            i = idx.get(d)
            if i is None or i < 90: continue
            reg = btc[d]/btc[bdates[i-90]] - 1.0
            (bull if reg >= 0 else bear).append(r)
        print(f"  BTC 牛市段: 年化 {mean(bull)*365*100:6.1f}%  Sh {sharpe(bull):5.2f}  t {tstat(bull):5.2f}  (n={len(bull)})")
        print(f"  BTC 熊市段: 年化 {mean(bear)*365*100:6.1f}%  Sh {sharpe(bear):5.2f}  t {tstat(bear):5.2f}  (n={len(bear)})")
        print(f"  {'✅ 牛熊都正 = 真市場中性' if sharpe(bull)>0 and sharpe(bear)>0 else '⚠ 偏某一 regime'}")

    print(f"\n=== 5. drop-one 廣度(逐一剔除單幣、看 Sharpe 是否靠某幾檔)===")
    base_sh = sharpe(netd); res = []
    for c in list(data.keys()):
        sub = {k: v for k, v in data.items() if k != c}
        n2, _, _, _, _ = simulate(sub, "funding", hold, COST_PER_SIDE)
        res.append((c, sharpe(n2)))
    res.sort(key=lambda x: x[1])
    print(f"  全幣 Sharpe {base_sh:.2f}。剔除後 Sharpe 範圍 [{res[0][1]:.2f}, {res[-1][1]:.2f}]")
    print(f"  最關鍵(剔了最傷): {res[0][0]} → {res[0][1]:.2f};最拖累(剔了反升): {res[-1][0]} → {res[-1][1]:.2f}")
    drop = base_sh - res[0][1]
    print(f"  {'✅ 廣度好(剔任一幣 Sharpe 掉<0.3)' if drop < 0.3 else f'⚠ 集中:最關鍵幣貢獻 {drop:.2f} Sharpe'}")

    # ── 6. 防禦機制:BTC 200d SMA regime gate(外生、未調參、硬 on/off、cap scalar=1)──
    print(f"\n=== 6. 防禦:BTC 200d-SMA regime gate(外生、未調參、只降風險不加槓桿)===")
    btc = data["BTC"][0]; bd = sorted(btc.keys())
    sma = {}
    for i, d in enumerate(bd):
        if i >= 200: sma[d] = sum(btc[bd[j]] for j in range(i-200, i))/200
    bidx = {d: i for i, d in enumerate(bd)}
    def bull_prior(d):
        i = bidx.get(d)
        if i is None or i-1 < 0: return True
        p = bd[i-1]
        return (p in sma) and btc[p] > sma[p]
    gated, prev_on, on_days = [], True, 0
    for r, d in zip(netd, dts):
        on = bull_prior(d)
        flip_cost = (COST_PER_SIDE*2) if on != prev_on else 0.0   # 開/關整本一次換手成本
        gated.append((r if on else 0.0) - (flip_cost if (on or prev_on) else 0.0))
        prev_on = on; on_days += 1 if on else 0
    print(f"  在市比例 {on_days/len(gated)*100:.0f}%(其餘抱現金)")
    line("無閘門(baseline)", netd)
    line("有 200d-SMA 閘門", gated)
    print(f"  maxDD: 無閘門 {maxdd(netd)*100:.0f}%  →  有閘門 {maxdd(gated)*100:.0f}%")
    h = len(gated)//2
    print(f"  閘門版 split-half Sh: 前 {sharpe(gated[:h]):.2f} / 後 {sharpe(gated[h:]):.2f}"
          + ("  ✅ 兩半都正、閘門通用" if sharpe(gated[:h])>0 and sharpe(gated[h:])>0 else "  ⚠ 閘門可能只 fit 一段"))
    lo2, hi2 = block_bootstrap_sharpe(gated)
    print(f"  閘門版 bootstrap 95% CI: [{lo2:.2f}, {hi2:.2f}]  {'✅ 下界>0' if lo2>0 else '⚠ 跨0'}")
    gy = defaultdict(list)
    for r, d in zip(gated, dts): gy[d[:4]].append(r)
    print("  閘門版年度(看 2022 熊市有沒有被擋下):")
    for yr in sorted(gy):
        s = gy[yr]
        if len(s) < 20: continue
        print(f"    {yr}: 年化 {mean(s)*365*100:7.1f}%  Sh {sharpe(s):5.2f}  (n={len(s)})")

    # ── 7. 第二層:vol-target(動態縮高波動期、scalar 上限 1.0 = 只降不加槓桿)──
    print(f"\n=== 7. 疊第二層 vol-target(cap 1.0、因果、你的紀律:improve Sharpe 不能靠槓桿)===")
    WIN = 30
    rollv = [0.0]*len(gated)
    for i in range(len(gated)):
        if i >= WIN:
            seg = gated[i-WIN:i]
            rollv[i] = std(seg)*math.sqrt(365)
    valid = [v for v in rollv if v > 0]
    target = sorted(valid)[len(valid)//2] if valid else 0.0   # 中位數波動當目標(不調參)
    voltgt = []
    for i in range(len(gated)):
        scale = 1.0
        if i >= WIN and rollv[i] > 0:
            scale = min(1.0, target/rollv[i])   # cap 1.0:只在高波動期縮、絕不放大
        voltgt.append(gated[i]*scale)
    line("閘門 only", gated)
    line("閘門 + vol-target", voltgt)
    print(f"  maxDD: 閘門 {maxdd(gated)*100:.0f}%  →  +vol-target {maxdd(voltgt)*100:.0f}%")
    h = len(voltgt)//2
    print(f"  最終版 split-half Sh: 前 {sharpe(voltgt[:h]):.2f} / 後 {sharpe(voltgt[h:]):.2f}")
    print(f"  Sharpe:閘門 {sharpe(gated):.2f} → +vol-target {sharpe(voltgt):.2f}  "
          + ("✅ Sharpe 沒掉=縮的是真波動叢、非擇時" if sharpe(voltgt) >= sharpe(gated)-0.05 else "⚠ Sharpe 掉=vol-cut 傷到、退回只用閘門"))

    print("\n判讀:防禦兩層 = 外生 regime 閘門(擋 bear regime tail)+ vol-target cap1.0(縮 regime 內波動叢)。"
          "\n合法防禦的標誌:maxDD 大降、Sharpe 不靠槓桿硬拉。最終再 vol-target 到目標年化波動(如 10%)決定實際 sleeve 大小。")

    # ── 8. 成本壓力:滑價多高就死(break-even)──
    print(f"\n=== 8. 成本壓力(hold=3d、淨 Sharpe vs 每側成本)===")
    for cps in (0.0008, 0.0012, 0.0016, 0.0020, 0.0030):
        n8, _, _, _, _ = simulate(data, "funding", 3, cps)
        print(f"  {cps*1e4:4.0f} bps/側: 淨年化 {mean(n8)*365*100:6.1f}%  Sharpe {sharpe(n8):5.2f}  t {tstat(n8):5.2f}"
              + ("  ← 邊際" if 1.0 <= tstat(n8) < 2.0 else ("  ← 死" if tstat(n8) < 1.0 else "")))

    # ── 9. 流動性/容量:只用最大 10 幣 vs 全 20 幣 ──
    print(f"\n=== 9. 容量檢驗(hold=3d、8bps):edge 在大幣還是靠小幣灌?===")
    LIQ10 = {"BTC","ETH","SOL","BNB","XRP","DOGE","ADA","AVAX","LINK","LTC"}
    SMALL = set(COINS) - LIQ10
    for label, keep in [("全 20 幣", set(COINS)), ("大 10 幣(可上量)", LIQ10), ("小 10 幣", SMALL)]:
        sub = {k: v for k, v in data.items() if k in keep}
        if len(sub) < 6: print(f"  {label}: 幣數不足"); continue
        n9, _, _, _, _ = simulate(sub, "funding", 3, COST_PER_SIDE)
        print(f"  {label:16}: 年化 {mean(n9)*365*100:6.1f}%  Sharpe {sharpe(n9):5.2f}  t {tstat(n9):5.2f}")

    print("\n=== 還沒驗、需要 C# pipeline / 新資料的 ===")
    print("  🔸 decorr 對照 retail_ls Delta / oi_contrarian:需 OI/retail 資料(本機 cache 只有 kline+funding)。")
    print("     price 腿的「擁擠回歸」可能跟它們重疊;carry 腿幾乎一定獨立。→ 港 C# 進 pipeline 才驗得了。")
