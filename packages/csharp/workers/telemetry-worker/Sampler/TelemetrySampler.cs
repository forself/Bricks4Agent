using System.Text.Json;
using Microsoft.Extensions.Logging;
using TelemetryWorker.Storage;

namespace TelemetryWorker.Sampler;

/// <summary>
/// 背景取樣迴圈 —— 每 N 秒打 broker 自己的健康端點(唯讀 GET),寫一筆時間序列進 telemetry.db。
/// 取不到(broker 掛/不可達)→ 仍寫一筆 status="unreachable",這樣時間序列會【捕捉到故障時刻】。
/// 純唯讀、不下單、零真錢副作用。
/// </summary>
public sealed class TelemetrySampler
{
    private readonly TelemetryDb _db;
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly int _intervalSec;
    private readonly int _retentionHours;
    private readonly ILogger _log;
    private DateTime _lastPruneUtc = DateTime.MinValue;

    public TelemetrySampler(TelemetryDb db, HttpClient http, string baseUrl,
        int intervalSec, int retentionHours, ILogger log)
    {
        _db = db; _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _intervalSec = Math.Max(5, intervalSec);
        _retentionHours = Math.Max(1, retentionHours);
        _log = log;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _log.LogInformation("TelemetrySampler started: {Url}, every {Sec}s, retention {H}h",
            _baseUrl, _intervalSec, _retentionHours);
        while (!ct.IsCancellationRequested)
        {
            try { await SampleOnceAsync(ct); }
            catch (Exception ex) { _log.LogWarning(ex, "sample cycle error"); }

            try { await Task.Delay(TimeSpan.FromSeconds(_intervalSec), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task SampleOnceAsync(CancellationToken ct)
    {
        double? score = null;
        string status = "unreachable";
        var rawParts = new List<string>();

        // ── /api/v1/health/score → score + status ──
        var scoreJson = await TryGetAsync("/api/v1/health/score", ct);
        if (scoreJson != null)
        {
            rawParts.Add(scoreJson.Length > 2000 ? scoreJson[..2000] : scoreJson);
            try
            {
                var root = Unwrap(JsonDocument.Parse(scoreJson).RootElement);
                if (TryGetNumber(root, out var sc, "score", "health_score", "value")) score = sc;
                if (TryGetString(root, out var st, "status", "level")) status = st;
                else if (score is { } s) status = s >= 80 ? "healthy" : s >= 50 ? "degraded" : "critical";
            }
            catch (Exception ex) { _log.LogDebug(ex, "score parse failed"); status = "parse_error"; }
        }

        // ── /api/v1/health/workers → total + healthy ──
        int total = 0, healthy = 0;
        var workersJson = await TryGetAsync("/api/v1/health/workers", ct);
        if (workersJson != null)
        {
            rawParts.Add(workersJson.Length > 2000 ? workersJson[..2000] : workersJson);
            try
            {
                var root = Unwrap(JsonDocument.Parse(workersJson).RootElement);
                if (root.TryGetProperty("workers", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var w in arr.EnumerateArray())
                    {
                        total++;
                        if (TryGetString(w, out var ws, "status") &&
                            (ws.Equals("healthy", StringComparison.OrdinalIgnoreCase) ||
                             ws.Equals("ok", StringComparison.OrdinalIgnoreCase)))
                            healthy++;
                    }
                }
            }
            catch (Exception ex) { _log.LogDebug(ex, "workers parse failed"); }
        }

        var sample = new TelemetrySample(
            SampledAt:      DateTime.UtcNow.ToString("o"),
            HealthScore:    score,
            Status:         status,
            WorkersTotal:   total,
            WorkersHealthy: healthy,
            Raw:            string.Join("\n", rawParts));
        _db.Insert(sample);
        _log.LogInformation("sample: status={Status} score={Score} workers={H}/{T}",
            status, score?.ToString("F1") ?? "-", healthy, total);

        // 每小時剪一次過期資料
        var now = DateTime.UtcNow;
        if ((now - _lastPruneUtc).TotalHours >= 1)
        {
            _lastPruneUtc = now;
            var removed = _db.Prune(now.AddHours(-_retentionHours));
            if (removed > 0) _log.LogInformation("pruned {N} old samples", removed);
        }
    }

    private async Task<string?> TryGetAsync(string path, CancellationToken ct)
    {
        try
        {
            using var resp = await _http.GetAsync(_baseUrl + path, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch { return null; }   // broker 掛/逾時/連不到 → null → 記 unreachable
    }

    /// <summary>剝掉常見的 { "data": {...} } 外殼。</summary>
    private static JsonElement Unwrap(JsonElement root)
        => root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) ? d : root;

    private static bool TryGetNumber(JsonElement e, out double val, params string[] names)
    {
        foreach (var n in names)
            if (e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number)
            { val = v.GetDouble(); return true; }
        val = 0; return false;
    }

    private static bool TryGetString(JsonElement e, out string val, params string[] names)
    {
        foreach (var n in names)
            if (e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String)
            { val = v.GetString() ?? ""; return true; }
        val = ""; return false;
    }
}
