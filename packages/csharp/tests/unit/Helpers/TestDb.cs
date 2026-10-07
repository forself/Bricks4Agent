using System.Collections.Concurrent;
using BrokerCore.Data;
using Microsoft.Data.Sqlite;

namespace Unit.Tests.Helpers;

/// <summary>
/// 測試用的 SQLite 資料庫。檔案一律由測試刪除：
/// <see cref="CreateIn"/> 建在呼叫端自己的暫存目錄（呼叫端以 <see cref="Delete"/> 刪除資料庫，再刪目錄）；
/// <see cref="CreateInMemory"/> 建在 %TEMP%，測試程序結束時統一刪除（也可提早以 <see cref="Delete"/> 刪除）。
/// </summary>
public static class TestDb
{
    private const string FilePrefix = "broker_test_";
    private static int _counter;
    private static readonly ConcurrentDictionary<string, byte> PendingTempFiles = new(StringComparer.OrdinalIgnoreCase);

    static TestDb()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeletePendingTempFiles();
    }

    /// <summary>
    /// 在 %TEMP% 建立獨立的測試資料庫（broker_test_*.db）。檔案在測試程序結束時刪除。
    /// </summary>
    public static BrokerDb CreateInMemory()
    {
        var path = NewDatabasePath(Path.GetTempPath());
        PendingTempFiles[path] = 0;
        return Open(path);
    }

    /// <summary>在 <paramref name="directory"/> 建立測試資料庫，回傳資料庫與檔案路徑。</summary>
    public static (BrokerDb Db, string Path) CreateIn(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = NewDatabasePath(directory);
        return (Open(path), path);
    }

    /// <summary>
    /// 刪除資料庫檔與它的 -wal、-shm。先釋放 SQLite 連線池（Windows 上連線池會占住檔案），刪除失敗時短暫重試。
    /// 呼叫前要先 Dispose 資料庫。
    /// </summary>
    public static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (File.Exists(file))
                        File.Delete(file);
                    break;
                }
                catch (IOException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // 最後一次仍失敗：留在暫存目錄，由程序結束時的清理或下次測試處理
                    break;
                }
            }
        }

        PendingTempFiles.TryRemove(path, out _);
    }

    private static string NewDatabasePath(string directory)
    {
        var id = Interlocked.Increment(ref _counter);
        return Path.Combine(directory, $"{FilePrefix}{id}_{Guid.NewGuid():N}.db");
    }

    private static BrokerDb Open(string path)
    {
        var db = new BrokerDb($"Data Source={path}");
        var initializer = new BrokerDbInitializer(db);
        initializer.Initialize();
        return db;
    }

    private static void DeletePendingTempFiles()
    {
        if (PendingTempFiles.IsEmpty)
            return;

        try
        {
            foreach (var path in PendingTempFiles.Keys.ToArray())
                Delete(path);
        }
        catch
        {
            // 程序結束時的盡力清理
        }
    }
}
