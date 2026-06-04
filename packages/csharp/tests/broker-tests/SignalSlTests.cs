using Broker.Services;

namespace Broker.Tests;

/// <summary>
/// 2026-06-04:UseSignalSl(策略結構性停損,如諧波 PRZ 失效價)的 SL-pct 決策單元測試。
/// 驗 ResolveBaseSlPct 的 base 距離% + 確認仍會被 LeverageAwareSlPct 收緊到強平距離內(安全不變)。
/// </summary>
public static class SignalSlTests
{
    public static (int passed, int failed) Run()
    {
        int passed = 0, failed = 0;
        void Check(string name, bool cond)
        {
            if (cond) { Console.WriteLine($"  [PASS] {name}"); passed++; }
            else { Console.Error.WriteLine($"  [FAIL] {name}"); failed++; }
        }
        static decimal R2(decimal x) => Math.Round(x, 2);

        Console.WriteLine("--- Signal-SL (ResolveBaseSlPct) Tests ---");

        // flag off → 固定 fallback(signal stop 完全忽略、既有行為不變)
        Check("flag-off→fixed", AutoTraderService.ResolveBaseSlPct(100m, true, false, 92m, 5m) == 5m);
        // flag on 但無 signal stop → 固定
        Check("on-null→fixed", AutoTraderService.ResolveBaseSlPct(100m, true, true, null, 5m) == 5m);
        // long、stop 在下方(有效)→ 隱含風險%(100→92 = 8%)
        Check("long-valid→8%", R2(AutoTraderService.ResolveBaseSlPct(100m, true, true, 92m, 5m)) == 8m);
        // long、stop 在上方(方向錯)→ 固定(忽略無效 stop)
        Check("long-wrongside→fixed", AutoTraderService.ResolveBaseSlPct(100m, true, true, 105m, 5m) == 5m);
        // short、stop 在上方(有效)→ 隱含風險%(100→108 = 8%)
        Check("short-valid→8%", R2(AutoTraderService.ResolveBaseSlPct(100m, false, true, 108m, 5m)) == 8m);
        // stop=0 / entry=0 → 固定
        Check("zero-stop→fixed", AutoTraderService.ResolveBaseSlPct(100m, true, true, 0m, 5m) == 5m);
        Check("zero-entry→fixed", AutoTraderService.ResolveBaseSlPct(0m, true, true, 92m, 5m) == 5m);
        // 緊 stop(3% < 固定 5%)→ 照用(更緊=更安全)
        Check("tight-honored→3%", R2(AutoTraderService.ResolveBaseSlPct(100m, true, true, 97m, 5m)) == 3m);

        // ★ 安全關鍵:過寬的 signal stop 仍被 LeverageAwareSlPct 收緊到強平距離內。
        //   entry100/stop80 = 20% base;5x 槓桿 cap = 100/5*0.6 = 12% → clamp 到 12%(不會超過災難線)
        var wideBase = AutoTraderService.ResolveBaseSlPct(100m, true, true, 80m, 5m);
        var clamped  = AutoTraderService.LeverageAwareSlPct(wideBase, 5m, false);
        Check("wide-signal→base20%", wideBase == 20m);
        Check("wide-signal→clamped12%", clamped == 12m);
        // 全倉模式(disableCap)→ 不收緊、用 signal 值(抱反彈)
        Check("wide-signal-crossmargin→20%", AutoTraderService.LeverageAwareSlPct(wideBase, 5m, true) == 20m);

        Console.WriteLine($"--- Signal-SL: {passed} passed, {failed} failed ---");
        return (passed, failed);
    }
}
