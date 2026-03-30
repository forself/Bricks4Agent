using BrokerCore.Data;

namespace Unit.Tests.Helpers;

public static class TestDb
{
    private static int _counter;

    /// <summary>
    /// 建立獨立的 SQLite 測試資料庫（臨時檔案，測試後刪除）
    /// </summary>
    public static BrokerDb CreateInMemory()
    {
        var id = Interlocked.Increment(ref _counter);
        var path = Path.Combine(Path.GetTempPath(), $"broker_test_{id}_{Guid.NewGuid():N}.db");
        var db = new BrokerDb($"Data Source={path}");
        var initializer = new BrokerDbInitializer(db);
        initializer.Initialize();
        return db;
    }
}
