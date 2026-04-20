using System.Collections.Concurrent;
using System.Text.Json;

namespace Broker.Services;

/// <summary>
/// 回測歷史紀錄（記憶體內，最多保存 50 筆）。
/// </summary>
public class BacktestHistoryService
{
    private readonly ConcurrentQueue<BacktestRecord> _records = new();
    private const int MaxRecords = 50;

    public void Save(string id, string strategy, string symbol, JsonElement data)
    {
        _records.Enqueue(new BacktestRecord { Id = id, Strategy = strategy, Symbol = symbol, Data = data.Clone() });
        while (_records.Count > MaxRecords) _records.TryDequeue(out _);
    }

    public IEnumerable<BacktestRecord> GetAll() => _records.ToArray().Reverse();
    public BacktestRecord? Get(string id) => _records.FirstOrDefault(r => r.Id == id);
}

public class BacktestRecord
{
    public string Id { get; set; } = "";
    public string Strategy { get; set; } = "";
    public string Symbol { get; set; } = "";
    public JsonElement Data { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}
