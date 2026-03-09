/**
 * BaseLogger - 輕量級日誌元件
 *
 * 功能：
 * - 多種日誌等級 (Trace, Debug, Info, Warn, Error, Fatal)
 * - 多種輸出目標 (Console, File, Database, Memory)
 * - 結構化日誌 (JSON 格式)
 * - 非同步寫入 (不阻塞主執行緒)
 * - 檔案自動輪替
 * - 與 BaseCache 整合 (緩衝、即時串流)
 * - 與 BaseOrm 整合 (資料庫儲存)
 *
 * 用法：
 *   var logger = new Logger();
 *   logger.Info("Application started");
 *   logger.Error("Something went wrong", exception);
 */

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BaseLogger;

#region Enums

/// <summary>
/// 日誌等級
/// </summary>
public enum LogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warn = 3,
    Error = 4,
    Fatal = 5,
    None = 6
}

#endregion

#region Log Entry

/// <summary>
/// 日誌項目
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Exception { get; set; }
    public Dictionary<string, object?>? Properties { get; set; }
    public string? TraceId { get; set; }
    public string? ThreadId { get; set; }

    [JsonIgnore]
    public string LevelName => Level.ToString().ToUpper();

    /// <summary>
    /// 格式化為文字
    /// </summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append($"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
        sb.Append($"[{LevelName,-5}] ");

        if (!string.IsNullOrEmpty(Category))
            sb.Append($"[{Category}] ");

        sb.Append(Message);

        if (!string.IsNullOrEmpty(Exception))
            sb.Append($"\n{Exception}");

        return sb.ToString();
    }

    /// <summary>
    /// 格式化為 JSON
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}

#endregion

#region Logger Interface

/// <summary>
/// 日誌介面
/// </summary>
public interface ILogger
{
    void Log(LogLevel level, string message, Exception? exception = null, Dictionary<string, object?>? properties = null);
    void Trace(string message, Dictionary<string, object?>? properties = null);
    void Debug(string message, Dictionary<string, object?>? properties = null);
    void Info(string message, Dictionary<string, object?>? properties = null);
    void Warn(string message, Dictionary<string, object?>? properties = null);
    void Error(string message, Exception? exception = null, Dictionary<string, object?>? properties = null);
    void Fatal(string message, Exception? exception = null, Dictionary<string, object?>? properties = null);
    ILogger ForCategory(string category);
    ILogger WithProperty(string key, object? value);
    ILogger WithTraceId(string traceId);
}

#endregion

#region Logger Implementation

/// <summary>
/// 日誌記錄器
/// </summary>
public class Logger : ILogger, IDisposable
{
    private readonly LoggerOptions _options;
    private readonly List<ILogTarget> _targets = new();
    private readonly ConcurrentQueue<LogEntry> _buffer = new();
    private readonly Timer? _flushTimer;
    private readonly string? _category;
    private readonly Dictionary<string, object?> _properties = new();
    private readonly string? _traceId;

    private bool _disposed;

    #region Constructors

    public Logger() : this(new LoggerOptions()) { }

    public Logger(LoggerOptions options)
    {
        _options = options ?? new LoggerOptions();

        // 預設加入 Console 輸出
        if (_options.EnableConsole)
        {
            _targets.Add(new ConsoleLogTarget(_options));
        }

        // 啟用非同步刷新
        if (_options.AsyncFlush && _options.FlushInterval > TimeSpan.Zero)
        {
            _flushTimer = new Timer(FlushBuffer, null, _options.FlushInterval, _options.FlushInterval);
        }
    }

    private Logger(Logger parent, string? category, Dictionary<string, object?>? properties, string? traceId)
    {
        _options = parent._options;
        _targets = parent._targets;
        _buffer = parent._buffer;
        _flushTimer = null; // 子 Logger 不管理 Timer
        _category = category ?? parent._category;
        _traceId = traceId ?? parent._traceId;

        // 合併屬性
        foreach (var kvp in parent._properties)
            _properties[kvp.Key] = kvp.Value;

        if (properties != null)
        {
            foreach (var kvp in properties)
                _properties[kvp.Key] = kvp.Value;
        }
    }

    #endregion

    #region Configuration

    /// <summary>
    /// 加入輸出目標
    /// </summary>
    public Logger AddTarget(ILogTarget target)
    {
        _targets.Add(target);
        return this;
    }

    /// <summary>
    /// 加入檔案輸出
    /// </summary>
    public Logger AddFile(string filePath, LogLevel minLevel = LogLevel.Debug)
    {
        _targets.Add(new FileLogTarget(filePath, _options, minLevel));
        return this;
    }

    /// <summary>
    /// 加入記憶體快取輸出 (需要 BaseCache)
    /// </summary>
    public Logger AddMemoryCache(object cache, string keyPrefix = "log:", int maxEntries = 1000)
    {
        _targets.Add(new MemoryCacheLogTarget(cache, keyPrefix, maxEntries, _options));
        return this;
    }

    /// <summary>
    /// 加入資料庫輸出 (需要 BaseOrm)
    /// </summary>
    public Logger AddDatabase(object db, string tableName = "Logs")
    {
        _targets.Add(new DatabaseLogTarget(db, tableName, _options));
        return this;
    }

    /// <summary>
    /// 加入自訂輸出目標
    /// </summary>
    public Logger AddCustomTarget(Action<LogEntry> handler, LogLevel minLevel = LogLevel.Debug)
    {
        _targets.Add(new CustomLogTarget(handler, minLevel));
        return this;
    }

    #endregion

    #region Logging Methods

    public void Log(LogLevel level, string message, Exception? exception = null, Dictionary<string, object?>? properties = null)
    {
        if (level < _options.MinLevel)
            return;

        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = level,
            Message = SanitizeMessage(message),
            Category = _category,
            Exception = exception?.ToString(),
            TraceId = _traceId,
            ThreadId = Environment.CurrentManagedThreadId.ToString()
        };

        // 合併屬性
        if (_properties.Count > 0 || properties != null)
        {
            entry.Properties = new Dictionary<string, object?>(_properties);
            if (properties != null)
            {
                foreach (var kvp in properties)
                    entry.Properties[kvp.Key] = kvp.Value;
            }
        }

        if (_options.AsyncFlush)
        {
            _buffer.Enqueue(entry);

            // 超過緩衝大小時立即刷新
            if (_buffer.Count >= _options.BufferSize)
            {
                FlushBuffer(null);
            }
        }
        else
        {
            WriteToTargets(entry);
        }
    }

    public void Trace(string message, Dictionary<string, object?>? properties = null)
        => Log(LogLevel.Trace, message, null, properties);

    public void Debug(string message, Dictionary<string, object?>? properties = null)
        => Log(LogLevel.Debug, message, null, properties);

    public void Info(string message, Dictionary<string, object?>? properties = null)
        => Log(LogLevel.Info, message, null, properties);

    public void Warn(string message, Dictionary<string, object?>? properties = null)
        => Log(LogLevel.Warn, message, null, properties);

    public void Error(string message, Exception? exception = null, Dictionary<string, object?>? properties = null)
        => Log(LogLevel.Error, message, exception, properties);

    public void Fatal(string message, Exception? exception = null, Dictionary<string, object?>? properties = null)
        => Log(LogLevel.Fatal, message, exception, properties);

    #endregion

    #region Fluent API

    public ILogger ForCategory(string category)
        => new Logger(this, category, null, null);

    public ILogger WithProperty(string key, object? value)
        => new Logger(this, null, new Dictionary<string, object?> { [key] = value }, null);

    public ILogger WithTraceId(string traceId)
        => new Logger(this, null, null, traceId);

    #endregion

    #region Internal Methods

    /// <summary>
    /// 清理訊息，防止日誌注入
    /// </summary>
    private static string SanitizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        // 限制訊息長度
        if (message.Length > 10000)
            message = message[..10000] + "...[truncated]";

        // 替換可能用於日誌注入的字元
        return message
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ")
            .Replace("\t", " ");
    }

    private void WriteToTargets(LogEntry entry)
    {
        foreach (var target in _targets)
        {
            try
            {
                if (entry.Level >= target.MinLevel)
                {
                    target.Write(entry);
                }
            }
            catch
            {
                // 忽略寫入錯誤，避免日誌系統本身崩潰
            }
        }
    }

    private void FlushBuffer(object? state)
    {
        var entries = new List<LogEntry>();

        while (_buffer.TryDequeue(out var entry))
        {
            entries.Add(entry);
        }

        foreach (var entry in entries)
        {
            WriteToTargets(entry);
        }
    }

    /// <summary>
    /// 立即刷新所有緩衝的日誌
    /// </summary>
    public void Flush()
    {
        FlushBuffer(null);

        foreach (var target in _targets)
        {
            try
            {
                target.Flush();
            }
            catch
            {
                // 忽略刷新錯誤
            }
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;

        _flushTimer?.Dispose();
        Flush();

        foreach (var target in _targets)
        {
            if (target is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}

#endregion

#region Logger Options

/// <summary>
/// 日誌選項
/// </summary>
public class LoggerOptions
{
    /// <summary>
    /// 最低日誌等級
    /// </summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Debug;

    /// <summary>
    /// 啟用 Console 輸出
    /// </summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>
    /// 使用 JSON 格式
    /// </summary>
    public bool UseJsonFormat { get; set; } = false;

    /// <summary>
    /// 啟用非同步刷新
    /// </summary>
    public bool AsyncFlush { get; set; } = true;

    /// <summary>
    /// 刷新間隔
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 緩衝大小 (達到時立即刷新)
    /// </summary>
    public int BufferSize { get; set; } = 100;

    /// <summary>
    /// 啟用彩色輸出 (Console)
    /// </summary>
    public bool EnableColors { get; set; } = true;

    /// <summary>
    /// 檔案大小上限 (bytes)，超過時輪替
    /// </summary>
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// 保留的輪替檔案數量
    /// </summary>
    public int MaxRollingFiles { get; set; } = 5;

    /// <summary>
    /// 包含時間戳
    /// </summary>
    public bool IncludeTimestamp { get; set; } = true;

    /// <summary>
    /// 時間格式
    /// </summary>
    public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";
}

#endregion

#region Log Targets

/// <summary>
/// 日誌輸出目標介面
/// </summary>
public interface ILogTarget
{
    LogLevel MinLevel { get; }
    void Write(LogEntry entry);
    void Flush();
}

/// <summary>
/// Console 輸出
/// </summary>
public class ConsoleLogTarget : ILogTarget
{
    private readonly LoggerOptions _options;
    private readonly object _lock = new();

    public LogLevel MinLevel { get; }

    public ConsoleLogTarget(LoggerOptions options, LogLevel minLevel = LogLevel.Debug)
    {
        _options = options;
        MinLevel = minLevel;
    }

    public void Write(LogEntry entry)
    {
        lock (_lock)
        {
            if (_options.EnableColors)
            {
                Console.ForegroundColor = GetColor(entry.Level);
            }

            var text = _options.UseJsonFormat ? entry.ToJson() : entry.ToText();
            Console.WriteLine(text);

            if (_options.EnableColors)
            {
                Console.ResetColor();
            }
        }
    }

    public void Flush() { }

    private static ConsoleColor GetColor(LogLevel level) => level switch
    {
        LogLevel.Trace => ConsoleColor.DarkGray,
        LogLevel.Debug => ConsoleColor.Gray,
        LogLevel.Info => ConsoleColor.White,
        LogLevel.Warn => ConsoleColor.Yellow,
        LogLevel.Error => ConsoleColor.Red,
        LogLevel.Fatal => ConsoleColor.DarkRed,
        _ => ConsoleColor.White
    };
}

/// <summary>
/// 檔案輸出 (含輪替)
/// </summary>
public class FileLogTarget : ILogTarget, IDisposable
{
    private readonly string _basePath;
    private readonly LoggerOptions _options;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private string _currentPath;
    private long _currentSize;

    public LogLevel MinLevel { get; }

    public FileLogTarget(string filePath, LoggerOptions options, LogLevel minLevel = LogLevel.Debug)
    {
        ValidateFilePath(filePath);
        _basePath = filePath;
        _options = options;
        _currentPath = filePath;
        MinLevel = minLevel;

        EnsureWriter();
    }

    private static void ValidateFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

        // 取得完整路徑
        var fullPath = Path.GetFullPath(filePath);

        // 檢查是否包含危險路徑遍歷
        if (filePath.Contains("..") || fullPath.Contains(".."))
            throw new ArgumentException("Path traversal is not allowed", nameof(filePath));

        // 檢查副檔名
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var allowedExtensions = new[] { ".log", ".txt", ".json" };
        if (!allowedExtensions.Contains(ext))
            throw new ArgumentException($"Only {string.Join(", ", allowedExtensions)} extensions are allowed", nameof(filePath));
    }

    public void Write(LogEntry entry)
    {
        lock (_lock)
        {
            EnsureWriter();
            CheckRotation();

            var text = _options.UseJsonFormat ? entry.ToJson() : entry.ToText();
            _writer?.WriteLine(text);
            _currentSize += Encoding.UTF8.GetByteCount(text) + 2;
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            _writer?.Flush();
        }
    }

    private void EnsureWriter()
    {
        if (_writer != null) return;

        var directory = Path.GetDirectoryName(_currentPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var fileInfo = new FileInfo(_currentPath);
        _currentSize = fileInfo.Exists ? fileInfo.Length : 0;

        _writer = new StreamWriter(_currentPath, append: true, Encoding.UTF8)
        {
            AutoFlush = false
        };
    }

    private void CheckRotation()
    {
        if (_currentSize < _options.MaxFileSize)
            return;

        _writer?.Close();
        _writer = null;

        // 輪替檔案
        var dir = Path.GetDirectoryName(_basePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(_basePath);
        var ext = Path.GetExtension(_basePath);

        // 刪除最舊的檔案
        for (int i = _options.MaxRollingFiles - 1; i >= 1; i--)
        {
            var oldPath = Path.Combine(dir, $"{name}.{i}{ext}");
            var newPath = Path.Combine(dir, $"{name}.{i + 1}{ext}");

            if (File.Exists(newPath))
                File.Delete(newPath);

            if (File.Exists(oldPath))
                File.Move(oldPath, newPath);
        }

        // 重新命名當前檔案
        var rotatePath = Path.Combine(dir, $"{name}.1{ext}");
        if (File.Exists(_currentPath))
            File.Move(_currentPath, rotatePath);

        _currentSize = 0;
        EnsureWriter();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 記憶體快取輸出 (使用 BaseCache)
/// </summary>
public class MemoryCacheLogTarget : ILogTarget
{
    private readonly dynamic _cache;
    private readonly string _keyPrefix;
    private readonly int _maxEntries;
    private readonly LoggerOptions _options;
    private readonly string _listKey;
    private readonly string _channelKey;

    public LogLevel MinLevel { get; }

    public MemoryCacheLogTarget(object cache, string keyPrefix, int maxEntries, LoggerOptions options, LogLevel minLevel = LogLevel.Debug)
    {
        _cache = cache;
        _keyPrefix = keyPrefix;
        _maxEntries = maxEntries;
        _options = options;
        _listKey = $"{keyPrefix}entries";
        _channelKey = $"{keyPrefix}stream";
        MinLevel = minLevel;
    }

    public void Write(LogEntry entry)
    {
        try
        {
            // 儲存到列表
            _cache.RPush(_listKey, entry);

            // 限制列表大小
            var length = (int)_cache.LLen(_listKey);
            if (length > _maxEntries)
            {
                // 移除最舊的項目
                var removeCount = length - _maxEntries;
                for (int i = 0; i < removeCount; i++)
                {
                    _cache.LPop<LogEntry>(_listKey);
                }
            }

            // 發布到即時串流 (Pub/Sub)
            _cache.Publish(_channelKey, entry);

            // 按等級分類儲存
            var levelKey = $"{_keyPrefix}level:{entry.LevelName.ToLower()}";
            _cache.Increment(levelKey);

            // 儲存最後一筆各等級的日誌
            var lastKey = $"{_keyPrefix}last:{entry.LevelName.ToLower()}";
            _cache.Set(lastKey, entry, TimeSpan.FromHours(24));
        }
        catch
        {
            // 忽略快取錯誤
        }
    }

    public void Flush() { }

    /// <summary>
    /// 取得最近的日誌
    /// </summary>
    public List<LogEntry> GetRecentLogs(int count = 100)
    {
        try
        {
            return _cache.LRange<LogEntry>(_listKey, -count, -1);
        }
        catch
        {
            return new List<LogEntry>();
        }
    }

    /// <summary>
    /// 訂閱即時日誌串流
    /// </summary>
    public void Subscribe(Action<LogEntry> handler)
    {
        try
        {
            Action<string, object?> callback = (string channel, object? message) =>
            {
                if (message is LogEntry entry)
                {
                    handler(entry);
                }
            };
            _cache.Subscribe(_channelKey, callback);
        }
        catch
        {
            // 忽略訂閱錯誤
        }
    }

    /// <summary>
    /// 取得日誌統計
    /// </summary>
    public Dictionary<string, long> GetStats()
    {
        var stats = new Dictionary<string, long>();
        foreach (var level in Enum.GetValues<LogLevel>())
        {
            if (level == LogLevel.None) continue;
            var key = $"{_keyPrefix}level:{level.ToString().ToLower()}";
            try
            {
                var count = _cache.Get<long?>(key) ?? 0;
                stats[level.ToString()] = count;
            }
            catch
            {
                stats[level.ToString()] = 0;
            }
        }
        return stats;
    }
}

/// <summary>
/// 資料庫輸出 (使用 BaseOrm)
/// </summary>
public class DatabaseLogTarget : ILogTarget
{
    private readonly dynamic _db;
    private readonly string _tableName;
    private readonly LoggerOptions _options;
    private readonly ConcurrentQueue<LogEntry> _queue = new();
    private readonly Timer _flushTimer;
    private bool _tableCreated;

    public LogLevel MinLevel { get; }

    public DatabaseLogTarget(object db, string tableName, LoggerOptions options, LogLevel minLevel = LogLevel.Info)
    {
        ValidateTableName(tableName);
        _db = db;
        _tableName = tableName;
        _options = options;
        MinLevel = minLevel;

        // 定期批次寫入
        _flushTimer = new Timer(_ => FlushToDatabase(), null,
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 驗證表名是否安全 (只允許字母、數字、底線)
    /// </summary>
    private static void ValidateTableName(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            throw new ArgumentException("Table name cannot be null or empty", nameof(tableName));

        if (tableName.Length > 128)
            throw new ArgumentException("Table name cannot exceed 128 characters", nameof(tableName));

        // 第一個字元必須是字母或底線
        if (!char.IsLetter(tableName[0]) && tableName[0] != '_')
            throw new ArgumentException("Table name must start with a letter or underscore", nameof(tableName));

        // 其餘字元只能是字母、數字、底線
        foreach (var c in tableName)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                throw new ArgumentException("Table name can only contain letters, digits, and underscores", nameof(tableName));
        }
    }

    public void Write(LogEntry entry)
    {
        _queue.Enqueue(entry);
    }

    public void Flush()
    {
        FlushToDatabase();
    }

    private void FlushToDatabase()
    {
        if (_queue.IsEmpty) return;

        try
        {
            EnsureTableCreated();

            var entries = new List<LogEntry>();
            while (_queue.TryDequeue(out var entry))
            {
                entries.Add(entry);
            }

            if (entries.Count == 0) return;

            // 批次插入
            var sql = $@"INSERT INTO {_tableName}
                (Timestamp, Level, Message, Category, Exception, Properties, TraceId, ThreadId)
                VALUES (@Timestamp, @Level, @Message, @Category, @Exception, @Properties, @TraceId, @ThreadId)";

            foreach (var entry in entries)
            {
                _db.Execute(sql, new
                {
                    entry.Timestamp,
                    Level = (int)entry.Level,
                    entry.Message,
                    entry.Category,
                    entry.Exception,
                    Properties = entry.Properties != null ? JsonSerializer.Serialize(entry.Properties) : null,
                    entry.TraceId,
                    entry.ThreadId
                });
            }
        }
        catch
        {
            // 忽略資料庫錯誤
        }
    }

    private void EnsureTableCreated()
    {
        if (_tableCreated) return;

        try
        {
            var createSql = $@"CREATE TABLE IF NOT EXISTS {_tableName} (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Timestamp TEXT NOT NULL,
                Level INTEGER NOT NULL,
                Message TEXT NOT NULL,
                Category TEXT,
                Exception TEXT,
                Properties TEXT,
                TraceId TEXT,
                ThreadId TEXT
            )";

            _db.Execute(createSql);

            // 建立索引
            _db.Execute($"CREATE INDEX IF NOT EXISTS IX_{_tableName}_Timestamp ON {_tableName}(Timestamp)");
            _db.Execute($"CREATE INDEX IF NOT EXISTS IX_{_tableName}_Level ON {_tableName}(Level)");

            _tableCreated = true;
        }
        catch
        {
            // 忽略建表錯誤
        }
    }
}

/// <summary>
/// 自訂輸出目標
/// </summary>
public class CustomLogTarget : ILogTarget
{
    private readonly Action<LogEntry> _handler;

    public LogLevel MinLevel { get; }

    public CustomLogTarget(Action<LogEntry> handler, LogLevel minLevel = LogLevel.Debug)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        MinLevel = minLevel;
    }

    public void Write(LogEntry entry)
    {
        _handler(entry);
    }

    public void Flush() { }
}

#endregion

#region Extensions

/// <summary>
/// 日誌擴充方法
/// </summary>
public static class LoggerExtensions
{
    /// <summary>
    /// 使用 Scope 記錄執行時間
    /// </summary>
    public static IDisposable BeginScope(this ILogger logger, string operationName)
    {
        return new LogScope(logger, operationName);
    }

    /// <summary>
    /// 記錄方法執行
    /// </summary>
    public static T LogExecution<T>(this ILogger logger, string operationName, Func<T> action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            logger.Debug($"Starting: {operationName}");
            var result = action();
            sw.Stop();
            logger.Debug($"Completed: {operationName}", new Dictionary<string, object?>
            {
                ["duration_ms"] = sw.ElapsedMilliseconds
            });
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.Error($"Failed: {operationName}", ex, new Dictionary<string, object?>
            {
                ["duration_ms"] = sw.ElapsedMilliseconds
            });
            throw;
        }
    }

    /// <summary>
    /// 記錄非同步方法執行
    /// </summary>
    public static async Task<T> LogExecutionAsync<T>(this ILogger logger, string operationName, Func<Task<T>> action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            logger.Debug($"Starting: {operationName}");
            var result = await action();
            sw.Stop();
            logger.Debug($"Completed: {operationName}", new Dictionary<string, object?>
            {
                ["duration_ms"] = sw.ElapsedMilliseconds
            });
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.Error($"Failed: {operationName}", ex, new Dictionary<string, object?>
            {
                ["duration_ms"] = sw.ElapsedMilliseconds
            });
            throw;
        }
    }

    private class LogScope : IDisposable
    {
        private readonly ILogger _logger;
        private readonly string _operationName;
        private readonly System.Diagnostics.Stopwatch _sw;

        public LogScope(ILogger logger, string operationName)
        {
            _logger = logger;
            _operationName = operationName;
            _sw = System.Diagnostics.Stopwatch.StartNew();
            _logger.Debug($"Begin: {operationName}");
        }

        public void Dispose()
        {
            _sw.Stop();
            _logger.Debug($"End: {_operationName}", new Dictionary<string, object?>
            {
                ["duration_ms"] = _sw.ElapsedMilliseconds
            });
        }
    }
}

#endregion

#region Static Logger

/// <summary>
/// 靜態日誌存取器
/// </summary>
public static class Log
{
    private static Logger? _default;
    private static readonly object _lock = new();

    /// <summary>
    /// 預設日誌實例
    /// </summary>
    public static Logger Default
    {
        get
        {
            if (_default == null)
            {
                lock (_lock)
                {
                    _default ??= new Logger();
                }
            }
            return _default;
        }
        set
        {
            lock (_lock)
            {
                _default = value;
            }
        }
    }

    public static void Trace(string message) => Default.Trace(message);
    public static void Debug(string message) => Default.Debug(message);
    public static void Info(string message) => Default.Info(message);
    public static void Warn(string message) => Default.Warn(message);
    public static void Error(string message, Exception? ex = null) => Default.Error(message, ex);
    public static void Fatal(string message, Exception? ex = null) => Default.Fatal(message, ex);

    /// <summary>
    /// 關閉並釋放預設日誌
    /// </summary>
    public static void CloseAndFlush()
    {
        lock (_lock)
        {
            _default?.Dispose();
            _default = null;
        }
    }
}

#endregion
