// 5 支全新策略 × 深日線(~2500 根)完整驗證:跨時段一致性 + 1×/5× 全倉強平存活 + 相關矩陣。
// 全 long-only(crypto 做空必死)。教科書參數、不調。只回報通過的。
using BrokerCore.Trading;
using StrategyWorker.Engine;
using System.Globalization;
using System.Text.Json;

var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var symbols = new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" };
const decimal Mmr = 0.005m, Cost = 0.0006m;
const int Chunk = 90;

async Task<List<BarData>> Fetch(string sym, int total)
{
    var all = new List<BarData>(); long? endTime = null;
    for (int g = 0; all.Count < total && g < 6; g++)
    {
        var url = $"https://api.binance.com/api/v3/klines?symbol={sym}&interval=1d&limit=1000" + (endTime is long e ? $"&endTime={e}" : "");
        var json = await http.GetStringAsync(url); using var doc = JsonDocument.Parse(json);
        var batch = new List<BarData>();
        foreach (var k in doc.RootElement.EnumerateArray())
            batch.Add(new BarData {
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64()).UtcDateTime,
                Open = decimal.Parse(k[1].GetString()!, CultureInfo.InvariantCulture), High = decimal.Parse(k[2].GetString()!, CultureInfo.InvariantCulture),
                Low = decimal.Parse(k[3].GetString()!, CultureInfo.InvariantCulture), Close = decimal.Parse(k[4].GetString()!, CultureInfo.InvariantCulture),
                Volume = decimal.Parse(k[5].GetString()!, CultureInfo.InvariantCulture) });
        if (batch.Count == 0) break;
        all.InsertRange(0, batch);
        endTime = new DateTimeOffset(batch[0].OpenTime, TimeSpan.Zero).ToUnixTimeMilliseconds() - 1;
        if (batch.Count < 1000) break; await Task.Delay(200);
    }
    return all.GroupBy(b => b.OpenTime).Select(g => g.First()).OrderBy(b => b.OpenTime).ToList();
}

decimal[] Sma(List<BarData> b, int n) { var r = new decimal[b.Count]; decimal s = 0; for (int i = 0; i < b.Count; i++) { s += b[i].Close; if (i >= n) s -= b[i - n].Close; if (i >= n - 1) r[i] = s / n; } return r; }
decimal[] Ema(List<BarData> b, int n) { var r = new decimal[b.Count]; decimal k = 2m / (n + 1), e = b[0].Close; for (int i = 0; i < b.Count; i++) { e = i == 0 ? b[0].Close : b[i].Close * k + e * (1 - k); r[i] = e; } return r; }
decimal[] Rsi(List<BarData> b, int n) { var r = new decimal[b.Count]; decimal ag = 0, al = 0; for (int i = 1; i < b.Count; i++) { decimal ch = b[i].Close - b[i - 1].Close, g = Math.Max(0, ch), l = Math.Max(0, -ch); if (i <= n) { ag += g; al += l; if (i == n) { ag /= n; al /= n; r[i] = al == 0 ? 100 : 100 - 100 / (1 + ag / al); } } else { ag = (ag * (n - 1) + g) / n; al = (al * (n - 1) + l) / n; r[i] = al == 0 ? 100 : 100 - 100 / (1 + ag / al); } } return r; }
decimal[] Atr(List<BarData> b, int n) { var tr = new decimal[b.Count]; var a = new decimal[b.Count]; for (int i = 0; i < b.Count; i++) { decimal hl = b[i].High - b[i].Low; tr[i] = i == 0 ? hl : Math.Max(hl, Math.Max(Math.Abs(b[i].High - b[i - 1].Close), Math.Abs(b[i].Low - b[i - 1].Close))); if (i >= n) { decimal s = 0; for (int k = i - n + 1; k <= i; k++) s += tr[k]; a[i] = s / n; } } return a; }
decimal Std(List<BarData> b, int i, int n, decimal mean) { decimal s = 0; for (int k = i - n + 1; k <= i; k++) s += (b[k].Close - mean) * (b[k].Close - mean); return (decimal)Math.Sqrt((double)(s / n)); }

// 5 支策略 → desired[](0/1 long-only)
// 海龜式無狀態出場(20 日高進場、10 日低出場 + SMA100 濾)→ 可 port 成 bot 無狀態 Evaluate
int[] DonTrend(List<BarData> b) { var sma = Sma(b, 100); var d = new int[b.Count]; int pos = 0; for (int i = 101; i < b.Count; i++) { decimal hh = 0, ll = decimal.MaxValue; for (int k = i - 20; k < i; k++) hh = Math.Max(hh, b[k].High); for (int k = i - 10; k < i; k++) ll = Math.Min(ll, b[k].Low); if (pos == 1 && b[i].Close < ll) pos = 0; if (pos == 0 && b[i].Close > hh && b[i].Close > sma[i]) pos = 1; d[i] = pos; } return d; }
int[] Rsi2Rev(List<BarData> b) { var rsi = Rsi(b, 2); var s200 = Sma(b, 200); var s5 = Sma(b, 5); var d = new int[b.Count]; int pos = 0; for (int i = 201; i < b.Count; i++) { if (pos == 1 && b[i].Close > s5[i]) pos = 0; if (pos == 0 && rsi[i] < 10 && b[i].Close > s200[i]) pos = 1; d[i] = pos; } return d; }
int[] Mom120(List<BarData> b) { var s50 = Sma(b, 50); var d = new int[b.Count]; for (int i = 120; i < b.Count; i++) d[i] = (b[i].Close > b[i - 120].Close && b[i].Close > s50[i]) ? 1 : 0; return d; }
int[] BollRev(List<BarData> b) { var s20 = Sma(b, 20); var s200 = Sma(b, 200); var d = new int[b.Count]; int pos = 0; for (int i = 201; i < b.Count; i++) { decimal sd = Std(b, i, 20, s20[i]); decimal lo = s20[i] - 2 * sd; if (pos == 1 && b[i].Close >= s20[i]) pos = 0; if (pos == 0 && b[i].Close < lo && b[i].Close > s200[i]) pos = 1; d[i] = pos; } return d; }
int[] MaCross(List<BarData> b) { var e20 = Ema(b, 20); var e50 = Ema(b, 50); var s200 = Sma(b, 200); var d = new int[b.Count]; for (int i = 200; i < b.Count; i++) d[i] = (e20[i] > e50[i] && b[i].Close > s200[i]) ? 1 : 0; return d; }

(string n, Func<List<BarData>, int[]> f)[] strats =
{ ("DonTrend", DonTrend), ("Rsi2Rev", Rsi2Rev), ("Mom120", Mom120), ("BollRev", BollRev), ("MaCross", MaCross) };

// 槓桿 long-only + 強平。回 (ret%, maxDD%, liqs, trades)
(decimal ret, decimal dd, int liq, int n) RunLev(List<BarData> b, int[] d, decimal lev, int from)
{
    decimal eq = 1m, peak = 1m, dd = 0m; int pos = 0; decimal entry = 0, liqPx = 0; int liq = 0, n = 0;
    for (int i = from; i < b.Count; i++)
    {
        decimal px = b[i].Close;
        if (pos == 1) { if (b[i].Low <= liqPx) { eq *= 0.01m; liq++; n++; pos = 0; } }
        if (eq > 0.0001m)
        {
            if (pos == 1 && d[i] == 0) { eq *= 1 + ((px - entry) / entry * lev - 2 * Cost * lev); n++; pos = 0; }
            else if (pos == 0 && d[i] == 1) { pos = 1; entry = px; liqPx = entry * (1 - 1m / lev + Mmr); }
        }
        decimal op = pos == 1 ? (px - entry) / entry * lev : 0; decimal e2 = eq * (1 + op);
        if (e2 > peak) peak = e2; var x = peak > 0 ? (peak - e2) / peak : 0; if (x > dd) dd = x;
    }
    return ((eq - 1m) * 100m, dd * 100, liq, n);
}

// 跨時段一致性:切 90 根 chunk、各跑 1× long、回 (正chunk%, chunk數)
(decimal posPct, int chunks) Consistency(List<BarData> b, int[] d, int from)
{
    int pos2 = 0, neg = 0;
    for (int c = from; c + Chunk <= b.Count; c += Chunk)
    {
        decimal eq = 1m; int p = 0; decimal entry = 0;
        for (int i = c; i < c + Chunk; i++)
        { decimal px = b[i].Close; if (p == 1 && d[i] == 0) { eq *= 1 + ((px - entry) / entry - 2 * Cost); p = 0; } else if (p == 0 && d[i] == 1) { p = 1; entry = px; } }
        if (p == 1) eq *= 1 + ((b[c + Chunk - 1].Close - entry) / entry - Cost);
        if (eq > 1m) pos2++; else if (eq < 1m) neg++;
    }
    int tot = pos2 + neg; return (tot > 0 ? (decimal)pos2 / tot * 100 : 0, tot);
}

decimal[] Equity1x(List<BarData> b, int[] d, int from)
{ var eq = new List<decimal>(); decimal e = 1m; int p = 0; decimal entry = 0; for (int i = from; i < b.Count; i++) { decimal px = b[i].Close; if (p == 1 && d[i] == 0) { e *= 1 + ((px - entry) / entry - 2 * Cost); p = 0; } else if (p == 0 && d[i] == 1) { p = 1; entry = px; } decimal op = p == 1 ? (px - entry) / entry : 0; eq.Add(e * (1 + op)); } return eq.ToArray(); }

var data = new Dictionary<string, List<BarData>>();
foreach (var s in symbols) { try { data[s] = await Fetch(s, 2500); } catch (Exception ex) { Console.WriteLine($"{s}: {ex.Message}"); } }
foreach (var s in symbols) if (data.ContainsKey(s)) Console.WriteLine($"  {s}: {data[s].Count} bars  {data[s][0].OpenTime:yyyy-MM-dd}→{data[s][^1].OpenTime:yyyy-MM-dd}");

Console.WriteLine($"\n=== 5 支新策略驗證(深日線、跨 BTC/ETH/SOL 池化)===");
Console.WriteLine($"  {"strategy",-12}{"一致%",7}{"1×ret%",9}{"1×DD%",8}{"5×ret%",9}{"5×liq",7}  判定");
foreach (var (name, f) in strats)
{
    decimal cons = 0, r1 = 0, dd1 = 0, r5 = 0; int liq5 = 0, cnt = 0;
    foreach (var sym in symbols)
    { if (!data.TryGetValue(sym, out var b) || b.Count < 400) continue; var d = f(b); int from = 205; var c = Consistency(b, d, from); var a1 = RunLev(b, d, 1m, from); var a5 = RunLev(b, d, 5m, from); cons += c.posPct; r1 += a1.ret; dd1 += a1.dd; r5 += a5.ret; liq5 += a5.liq; cnt++; }
    if (cnt == 0) continue; cons /= cnt; r1 /= cnt; dd1 /= cnt; r5 /= cnt;
    bool passOOS = cons >= 55m && r1 > 0m; bool surv5 = liq5 == 0;
    string verdict = passOOS && surv5 ? "✅ 過(一致+5×存活)" : passOOS ? "△ 一致但 5× 會爆" : r1 > 0 ? "△ 賺但不夠一致" : "❌";
    Console.WriteLine($"  {name,-12}{cons,7:F0}{r1,9:F0}{dd1,8:F0}{r5,9:F0}{liq5,7}  {verdict}");
}

// 相關矩陣(BTC 1× 權益報酬)
if (data.ContainsKey("BTCUSDT"))
{
    var b = data["BTCUSDT"]; var eqs = strats.ToDictionary(s => s.n, s => Equity1x(b, s.f(b), 205).ToList());
    int len = eqs.Values.Min(e => e.Count);
    Console.WriteLine($"\n=== 相關矩陣(BTC 1× 權益報酬)===");
    Console.WriteLine("  " + new string(' ', 12) + string.Join("", strats.Select(s => $"{s.n,10}")));
    foreach (var (n, _) in strats)
    { var ea = eqs[n].Take(len).ToList(); Console.WriteLine($"  {n,-12}" + string.Join("", strats.Select(s => $"{CorrelationGuard.PearsonOfReturns(ea, eqs[s.n].Take(len).ToList()),10:F2}"))); }
}
Console.WriteLine("\n判定:✅ = 一致%≥55 + 1× 賺 + 5× 不強平。挑 ✅ 且彼此相關<0.4 的 = 可直接用的去相關組合。");
