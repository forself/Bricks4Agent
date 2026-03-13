using System.Collections.Concurrent;
using System.Text.Json;
using CacheProtocol;

namespace CacheServer.Cluster;

/// <summary>
/// Append-Only 複製日誌
///
/// Leader 在每次寫入操作後追加一條日誌。
/// Follower 依序套用日誌條目，保持資料同步。
///
/// 特性：
/// - 單調遞增 LSN（Log Sequence Number）
/// - 保留最近 100,000 筆（可配置）
/// - 新 Follower 加入：差距在 log 範圍內 → 增量；否則 → 全量快照
/// - 支援讀取指定 LSN 之後的所有條目（增量同步）
/// </summary>
public class ReplicationLog
{
    private readonly ConcurrentQueue<ReplicationEntry> _log = new();
    private readonly object _appendLock = new();
    private long _currentLsn;
    private readonly int _maxEntries;

    // 快速查找用的索引（LSN → 在 queue 中的位置不好用，改用 list snapshot）
    private readonly List<ReplicationEntry> _entries = new();
    private readonly ReaderWriterLockSlim _rwLock = new();

    public ReplicationLog(int maxEntries = 100_000)
    {
        _maxEntries = maxEntries;
    }

    /// <summary>當前最新 LSN</summary>
    public long CurrentLsn => Interlocked.Read(ref _currentLsn);

    /// <summary>日誌條目數</summary>
    public int Count
    {
        get
        {
            _rwLock.EnterReadLock();
            try { return _entries.Count; }
            finally { _rwLock.ExitReadLock(); }
        }
    }

    /// <summary>最早的 LSN（用於判斷增量同步是否可行）</summary>
    public long EarliestLsn
    {
        get
        {
            _rwLock.EnterReadLock();
            try { return _entries.Count > 0 ? _entries[0].Lsn : 0; }
            finally { _rwLock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// 追加一條複製日誌（Leader 呼叫）
    /// </summary>
    public ReplicationEntry Append(byte opCode, string key, JsonElement? value, long ttlMs = 0, long newValue = 0)
    {
        lock (_appendLock)
        {
            var lsn = Interlocked.Increment(ref _currentLsn);

            var entry = new ReplicationEntry
            {
                Lsn = lsn,
                OpCode = opCode,
                Key = key,
                Value = value,
                TtlMs = ttlMs,
                NewValue = newValue,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            _rwLock.EnterWriteLock();
            try
            {
                _entries.Add(entry);

                // 超過上限 → 移除最舊的
                while (_entries.Count > _maxEntries)
                {
                    _entries.RemoveAt(0);
                }
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }

            return entry;
        }
    }

    /// <summary>
    /// 取得 afterLsn 之後的所有條目（增量同步用）
    /// </summary>
    /// <param name="afterLsn">起始 LSN（不含）</param>
    /// <param name="maxCount">最多回傳筆數</param>
    /// <returns>增量條目列表</returns>
    public List<ReplicationEntry> GetEntriesAfter(long afterLsn, int maxCount = 1000)
    {
        _rwLock.EnterReadLock();
        try
        {
            var result = new List<ReplicationEntry>();

            // 二分搜尋找到起始位置
            int startIdx = 0;
            if (_entries.Count > 0 && afterLsn > 0)
            {
                startIdx = BinarySearchLsn(afterLsn);
                if (startIdx >= 0)
                    startIdx++; // 跳過 afterLsn 自身
                else
                    startIdx = ~startIdx; // 插入點 = 第一個 > afterLsn 的位置
            }

            for (int i = startIdx; i < _entries.Count && result.Count < maxCount; i++)
            {
                result.Add(_entries[i]);
            }

            return result;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// 取得所有條目（全量快照用）
    /// </summary>
    public List<ReplicationEntry> GetAllEntries()
    {
        _rwLock.EnterReadLock();
        try { return new List<ReplicationEntry>(_entries); }
        finally { _rwLock.ExitReadLock(); }
    }

    /// <summary>
    /// 設定 LSN（Follower 在套用複製後更新）
    /// </summary>
    public void SetLsn(long lsn)
    {
        long current;
        do
        {
            current = Interlocked.Read(ref _currentLsn);
            if (lsn <= current) return;
        }
        while (Interlocked.CompareExchange(ref _currentLsn, lsn, current) != current);
    }

    /// <summary>清空日誌（重新初始化時）</summary>
    public void Clear()
    {
        lock (_appendLock)
        {
            _rwLock.EnterWriteLock();
            try
            {
                _entries.Clear();
                Interlocked.Exchange(ref _currentLsn, 0);
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }
    }

    /// <summary>二分搜尋 LSN 在 _entries 中的位置</summary>
    private int BinarySearchLsn(long lsn)
    {
        int lo = 0, hi = _entries.Count - 1;

        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            long midLsn = _entries[mid].Lsn;

            if (midLsn == lsn) return mid;
            if (midLsn < lsn) lo = mid + 1;
            else hi = mid - 1;
        }

        return ~lo; // 回傳插入點的 bitwise complement
    }
}
