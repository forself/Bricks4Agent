using System.Text.Json;
using WorkerSdk;
using TelemetryWorker.Storage;

namespace TelemetryWorker.Handlers;

/// <summary>
/// telemetry.history — 唯讀查 broker 健康時間序列(給 dashboard 拉圖)。
/// Routes:
///   query  → { minutes?: int=60, limit?: int=500 } 回時間窗內取樣(新到舊)
///   latest → 回最近一筆
/// 純讀本地 SQLite、不碰真錢、低風險 → tool.json 設 Read/low/auto-approve。
/// </summary>
public sealed class TelemetryHistoryHandler : ICapabilityHandler
{
    private readonly TelemetryDb _db;
    public string CapabilityId => "telemetry.history";

    public TelemetryHistoryHandler(TelemetryDb db) => _db = db;

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            if (route.Equals("latest", StringComparison.OrdinalIgnoreCase))
            {
                var latest = _db.Latest();
                return Ok(new { sample = latest is null ? null : ToDto(latest), total_samples = _db.Count() });
            }

            // 預設 query
            int minutes = 60, limit = 500;
            if (!string.IsNullOrWhiteSpace(payload))
            {
                try
                {
                    var root = JsonDocument.Parse(payload).RootElement;
                    if (root.TryGetProperty("minutes", out var m) && m.ValueKind == JsonValueKind.Number)
                        minutes = Math.Clamp(m.GetInt32(), 1, 60 * 24 * 30);
                    if (root.TryGetProperty("limit", out var l) && l.ValueKind == JsonValueKind.Number)
                        limit = Math.Clamp(l.GetInt32(), 1, 5000);
                }
                catch { /* 用預設 */ }
            }

            var rows = _db.QueryRecent(DateTime.UtcNow.AddMinutes(-minutes), limit);
            return Ok(new
            {
                minutes,
                count = rows.Count,
                total_samples = _db.Count(),
                samples = rows.Select(ToDto),
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string?, string?)>((false, null, "telemetry.history failed: " + ex.Message));
        }
    }

    private static object ToDto(TelemetrySample s) => new
    {
        sampled_at      = s.SampledAt,
        health_score    = s.HealthScore,
        status          = s.Status,
        workers_total   = s.WorkersTotal,
        workers_healthy = s.WorkersHealthy,
    };

    private static Task<(bool, string?, string?)> Ok(object o)
        => Task.FromResult<(bool, string?, string?)>((true, JsonSerializer.Serialize(o), null));
}
