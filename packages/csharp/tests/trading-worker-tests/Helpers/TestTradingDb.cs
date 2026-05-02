using Microsoft.Extensions.Logging.Abstractions;
using TradingWorker.Storage;

namespace TradingWorker.Tests.Helpers;

/// <summary>
/// 給每個測試一個獨立的 SQLite 檔案，避免測試之間污染。
/// 用 IDisposable 收尾、檔案在 Dispose 時清掉。
/// </summary>
public sealed class TestTradingDb : IDisposable
{
    public TradingDbStorage Db { get; }
    private readonly string _path;

    public TestTradingDb()
    {
        _path = Path.Combine(Path.GetTempPath(), $"trading_test_{Guid.NewGuid():N}.db");
        Db = new TradingDbStorage(_path, NullLogger<TradingDbStorage>.Instance);
    }

    public void Dispose()
    {
        Db.Dispose();
        try { File.Delete(_path); } catch { /* 收尾失敗不重要 */ }
    }
}
