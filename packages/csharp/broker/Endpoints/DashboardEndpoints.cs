using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Broker.Helpers;

namespace Broker.Endpoints;

/// <summary>
/// Dashboard 補強 endpoints — 給 dashboard.html / metrics.html 拿系統面資料。
///
/// 答辯後 Benson 反饋：dashboard 該秀「基礎資源 / 網路 / 錯誤」。本 endpoint 不依賴
/// IContainerManager（那邊只看 broker 自己 spawn 的 worker、不含 broker / bot-node /
/// line-worker），改直接 shell `docker stats` 拿 host 上所有 container 的真實 stats。
///
/// 路由：
///   GET /api/v1/dashboard/docker-stats   — 所有 docker container 即時 CPU/Mem/Net/IO
///   GET /api/v1/dashboard/network-status — ping 已知對外服務（LLM provider / 交易所 / Cloudflare）
///   GET /api/v1/dashboard/system-info    — host OS / docker version / uptime
///
/// 前提：broker container 已掛 /var/run/docker.sock（compose.trading.yml）+ 裝 docker-ce-cli。
/// 若 broker 本機跑（非 Docker），endpoints 會回空陣列或錯訊息。
/// </summary>
public static class DashboardEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var d = group.MapGroup("/dashboard");

        d.MapGet("/docker-stats", async (HttpContext ctx, CancellationToken ct) =>
        {
            if (!RequestBodyHelper.IsAdmin(ctx))
                return Results.Json(ApiResponseHelper.Error("admin only", 403), statusCode: 403);

            try
            {
                // docker stats --no-stream --format "{{json .}}"
                var psi = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "stats --no-stream --format \"{{json .}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi)!;
                var sb = new StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                proc.BeginOutputReadLine();

                var done = await Task.Run(() => proc.WaitForExit(5000), ct);
                if (!done)
                {
                    try { proc.Kill(); } catch { }
                    return Results.Ok(ApiResponseHelper.Error("docker stats timeout"));
                }

                var stats = new List<object>();
                foreach (var line in sb.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var s = line.Trim();
                    if (string.IsNullOrEmpty(s)) continue;
                    try
                    {
                        using var jd = JsonDocument.Parse(s);
                        var r = jd.RootElement;
                        stats.Add(new
                        {
                            container_id = GetStr(r, "ID"),
                            name = GetStr(r, "Name"),
                            cpu_pct = ParsePct(GetStr(r, "CPUPerc")),
                            mem_pct = ParsePct(GetStr(r, "MemPerc")),
                            mem_usage = GetStr(r, "MemUsage"),  // "123MiB / 2GiB"
                            net_io = GetStr(r, "NetIO"),        // "1.2GB / 350MB"
                            block_io = GetStr(r, "BlockIO"),    // "10MB / 5MB"
                            pids = GetStr(r, "PIDs"),
                        });
                    }
                    catch { /* skip bad lines */ }
                }

                return Results.Ok(ApiResponseHelper.Success(new
                {
                    generated_at = DateTime.UtcNow,
                    count = stats.Count,
                    containers = stats,
                }));
            }
            catch (Exception ex)
            {
                return Results.Ok(ApiResponseHelper.Error("docker stats failed: " + ex.Message));
            }
        });

        d.MapGet("/network-status", async (HttpContext ctx, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (!RequestBodyHelper.IsAdmin(ctx))
                return Results.Json(ApiResponseHelper.Error("admin only", 403), statusCode: 403);

            var targets = new[]
            {
                new { name = "Cloudflare DNS",      url = "https://1.1.1.1" },
                new { name = "Google",              url = "https://www.google.com" },
                new { name = "Gemini API",          url = "https://generativelanguage.googleapis.com" },
                new { name = "OpenAI API",          url = "https://api.openai.com" },
                new { name = "BingX API",           url = "https://open-api.bingx.com" },
                new { name = "Anthropic API",       url = "https://api.anthropic.com" },
                new { name = "GitHub",              url = "https://github.com" },
                new { name = "Cloudflare Tunnel",   url = "https://b4a-trading.app" },
            };

            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(3);

            var results = await Task.WhenAll(targets.Select(async t =>
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Head, t.url);
                    using var resp = await client.SendAsync(req, ct);
                    sw.Stop();
                    return (object)new
                    {
                        name = t.name,
                        url = t.url,
                        status = "up",
                        http_code = (int)resp.StatusCode,
                        latency_ms = sw.ElapsedMilliseconds,
                    };
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    return (object)new
                    {
                        name = t.name,
                        url = t.url,
                        status = "down",
                        http_code = 0,
                        latency_ms = sw.ElapsedMilliseconds,
                        error = ex.Message.Length > 80 ? ex.Message.Substring(0, 80) : ex.Message,
                    };
                }
            }));

            return Results.Ok(ApiResponseHelper.Success(new
            {
                generated_at = DateTime.UtcNow,
                targets = results,
            }));
        });

        d.MapGet("/system-info", (HttpContext ctx) =>
        {
            if (!RequestBodyHelper.IsAdmin(ctx))
                return Results.Json(ApiResponseHelper.Error("admin only", 403), statusCode: 403);
            return Results.Ok(ApiResponseHelper.Success(new
            {
                os = Environment.OSVersion.ToString(),
                machine_name = Environment.MachineName,
                processor_count = Environment.ProcessorCount,
                dotnet_version = Environment.Version.ToString(),
                working_set_mb = Environment.WorkingSet / 1024 / 1024,
                process_id = Environment.ProcessId,
                process_uptime_s = (int)(DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime()).TotalSeconds,
                tick_count = Environment.TickCount64,
            }));
        });
    }

    private static string? GetStr(JsonElement el, string key)
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double ParsePct(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Trim().TrimEnd('%');
        return double.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }
}
