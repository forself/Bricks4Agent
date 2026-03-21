using System.Text.Json;
using WorkerSdk;

namespace LineWorker.Handlers;

/// <summary>
/// line.message.read 能力處理器 — 讀取暫存的入站訊息
///
/// 從 pending_messages/ 資料夾讀取尚未消費的訊息。
///
/// payload:
///   limit    (int, optional)    — 最多讀取幾則，預設 10
///   consume  (bool, optional)   — 讀取後是否刪除檔案，預設 true
/// </summary>
public class ReadMessagesHandler : ICapabilityHandler
{
    private readonly string _pendingDir;

    public string CapabilityId => "line.message.read";

    public ReadMessagesHandler()
    {
        _pendingDir = Path.Combine(AppContext.BaseDirectory, "pending_messages");
        Directory.CreateDirectory(_pendingDir);
    }

    public Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId, string route, string payload, string scope, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            // Broker 傳來的 payload 格式為 { route, args }，實際參數在 args 內
            var root = doc.RootElement.TryGetProperty("args", out var argsEl)
                ? argsEl
                : doc.RootElement;

            var limit = root.TryGetProperty("limit", out var lim) ? lim.GetInt32() : 10;
            var consume = root.TryGetProperty("consume", out var con) ? con.GetBoolean() : true;

            var files = Directory.GetFiles(_pendingDir, "*.json")
                .OrderBy(f => File.GetCreationTimeUtc(f))
                .Take(limit)
                .ToList();

            var messages = new List<JsonElement>();
            foreach (var file in files)
            {
                var content = File.ReadAllText(file);
                using var msgDoc = JsonDocument.Parse(content);
                messages.Add(msgDoc.RootElement.Clone());

                if (consume)
                    File.Delete(file);
            }

            var result = JsonSerializer.Serialize(new
            {
                count = messages.Count,
                messages
            });

            return Task.FromResult<(bool, string?, string?)>((true, result, null));
        }
        catch (Exception ex)
        {
            return Task.FromResult<(bool, string?, string?)>(
                (false, null, $"ReadMessages error: {ex.Message}"));
        }
    }
}
