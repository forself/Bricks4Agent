using Broker.Services;

namespace Broker.Tests;

/// <summary>
/// 2026-06-13:bracket SL/TP 價格計算的單元測試（交易所端真錢保護價、最該測的純邏輯）。
/// 比照組長 DiaryApp 風格 + 特別測「**安全關鍵的方向性**」:
///   long 倉 SL 必在 entry「下方」、short 倉 SL 必在「上方」——
///   方向搞反 = SL 掛錯邊 = 形同虛設(永不觸發)或立刻觸發 = 真錢災難。
/// 全是 pure static、無外部相依 → 最好測（對應組長 Pbkdf2 那組純函式）。
/// </summary>
public static class BracketPriceTests
{
    public static (int passed, int failed) Run()
    {
        int passed = 0, failed = 0;
        void Check(string name, bool cond)
        {
            if (cond) { Console.WriteLine($"  [PASS] {name}"); passed++; }
            else { Console.Error.WriteLine($"  [FAIL] {name}"); failed++; }
        }

        Console.WriteLine("--- Bracket SL/TP Price Tests ---");

        // ---- ComputeBracketSlPrice:停損價 ----
        // ★安全關鍵:long 倉 SL 必在 entry「下方」(跌破才停損)。entry100/5% → 95
        Check("sl-long-below-entry", Sl(100m, 5m, true) == 95m);
        // ★安全關鍵:short 倉 SL 必在 entry「上方」(漲破才停損)。entry100/5% → 105
        Check("sl-short-above-entry", Sl(100m, 5m, false) == 105m);
        // 方向不可搞反:long SL 一定 < entry、short SL 一定 > entry(不同價位再驗一次)
        Check("sl-long-<-entry", Sl(50000m, 3m, true) < 50000m);
        Check("sl-short->-entry", Sl(50000m, 3m, false) > 50000m);
        // 防呆:entry / slPct ≤ 0 → null(不下無效保護單)
        Check("sl-zero-entry→null", AutoTraderService.ComputeBracketSlPrice(0m, 5m, true) == null);
        Check("sl-zero-pct→null", AutoTraderService.ComputeBracketSlPrice(100m, 0m, true) == null);

        // ---- ComputeBracketTpPrice:停利價(方向跟 SL 相反)----
        // long TP 在 entry「上方」、short TP 在「下方」(獲利方向)
        Check("tp-long-above-entry", Tp(100m, 5m, true) == 105m);
        Check("tp-short-below-entry", Tp(100m, 5m, false) == 95m);
        // ★SL 跟 TP 必在 entry 的「相反兩側」(long:SL 下、TP 上)
        Check("long-sl-tp-opposite-sides", Sl(100m, 5m, true) < 100m && Tp(100m, 5m, true) > 100m);

        // ---- ResolveBracketTpPct:R:R 模式 vs 固定 ----
        // R:R 模式(tpRr>0):TP 距離 = tpRr × SL 距離。2:1 + SL5% → TP10%
        Check("tp-pct-rr-mode", AutoTraderService.ResolveBracketTpPct(2m, 8m, 5m) == 10m);
        // 固定模式(tpRr=0):用 tpPct
        Check("tp-pct-fixed-mode", AutoTraderService.ResolveBracketTpPct(0m, 8m, 5m) == 8m);

        // ---- RoundPrice:精度(BingX 對精度過長的價格直接拒單)----
        Check("round-precision-2", AutoTraderService.RoundPrice(123.456789m, 2) == 123.46m);
        Check("round-null→6dp", AutoTraderService.RoundPrice(1.2345678901m, null) == 1.234568m);

        // ---- RoundQtyToStep:數量往下取(寧可略小、不超 notional/risk 預算)----
        Check("qty-floors-to-step", AutoTraderService.RoundQtyToStep(1.27m, 0.1m) == 1.2m);
        Check("qty-step-zero→unchanged", AutoTraderService.RoundQtyToStep(1.27m, 0m) == 1.27m);

        Console.WriteLine($"--- Bracket: {passed} passed, {failed} failed ---");
        return (passed, failed);

        // 小幫手:解開 nullable 讓斷言乾淨(null 視為 -1、必然不等於任何有效價)
        static decimal Sl(decimal e, decimal pct, bool isLong) => AutoTraderService.ComputeBracketSlPrice(e, pct, isLong) ?? -1m;
        static decimal Tp(decimal e, decimal pct, bool isLong) => AutoTraderService.ComputeBracketTpPrice(e, pct, isLong) ?? -1m;
    }
}
