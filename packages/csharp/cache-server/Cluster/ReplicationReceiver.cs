using System.Text.Json;
using CacheProtocol;
using CacheServer.Engine;
using Microsoft.Extensions.Logging;

namespace CacheServer.Cluster;

/// <summary>
/// 複製接收器（Follower 端）
///
/// 職責：
/// 1. 接收 Leader 發來的 REPLICATE frame
/// 2. 解析 ReplicationEntry
/// 3. 套用到本地 CacheEngine
/// 4. 回傳 REPLICATE_ACK
///
/// 套用邏輯：
/// - SET → DirectSet
/// - DEL → DirectDelete
/// - CAS_GT → DirectCasIfGreater
/// - DECR_POS → DirectDecrIfPositive
/// - INCR → DirectIncrement
/// - 其他寫入操作 → DirectSet（通用）
///
/// 冪等保證：
/// - 每個 entry 有唯一 LSN
/// - Follower 追蹤 appliedLsn
/// - 若 entry.Lsn <= appliedLsn → 跳過（已套用）
/// </summary>
public class ReplicationReceiver
{
    private readonly CacheEngine _engine;
    private readonly ReplicationLog _replicationLog;
    private readonly ClusterConfig _config;
    private readonly ILogger<ReplicationReceiver> _logger;

    public ReplicationReceiver(
        CacheEngine engine,
        ReplicationLog replicationLog,
        ClusterConfig config,
        ILogger<ReplicationReceiver> logger)
    {
        _engine = engine;
        _replicationLog = replicationLog;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 處理收到的 REPLICATE frame
    /// </summary>
    /// <param name="payload">ReplicationEntry JSON payload</param>
    /// <returns>REPLICATE_ACK frame bytes</returns>
    public byte[] HandleReplicate(ReadOnlySpan<byte> payload)
    {
        ReplicationEntry? entry;
        try
        {
            entry = CacheSerializer.Deserialize<ReplicationEntry>(payload);
            if (entry == null)
            {
                return EncodeAck(false, 0, "Failed to deserialize replication entry");
            }
        }
        catch (Exception ex)
        {
            return EncodeAck(false, 0, $"Deserialization error: {ex.Message}");
        }

        return ApplyEntry(entry);
    }

    /// <summary>
    /// 處理已解析的 ReplicationEntry（供 SnapshotTransfer 呼叫）
    /// </summary>
    public byte[] ApplyEntry(ReplicationEntry entry)
    {
        // 冪等檢查：跳過已套用的條目
        if (entry.Lsn <= _replicationLog.CurrentLsn)
        {
            _logger.LogDebug(
                "Skipping already-applied entry: LSN {EntryLsn} <= {CurrentLsn}",
                entry.Lsn, _replicationLog.CurrentLsn);
            return EncodeAck(true, _replicationLog.CurrentLsn, null);
        }

        try
        {
            // 套用到 CacheEngine
            ApplyToEngine(entry);

            // 更新 Follower LSN
            _replicationLog.SetLsn(entry.Lsn);

            _logger.LogDebug("Applied replication entry: LSN={Lsn} Op={Op} Key={Key}",
                entry.Lsn, OpCodes.GetName(entry.OpCode), entry.Key);

            return EncodeAck(true, entry.Lsn, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply replication entry: LSN={Lsn} Op={Op} Key={Key}",
                entry.Lsn, OpCodes.GetName(entry.OpCode), entry.Key);
            return EncodeAck(false, _replicationLog.CurrentLsn, $"Apply error: {ex.Message}");
        }
    }

    /// <summary>
    /// 將複製條目套用到本地 CacheEngine
    /// </summary>
    private void ApplyToEngine(ReplicationEntry entry)
    {
        var ttl = entry.TtlMs > 0 ? TimeSpan.FromMilliseconds(entry.TtlMs) : (TimeSpan?)null;

        switch (entry.OpCode)
        {
            case OpCodes.SET:
                if (entry.Value.HasValue)
                    _engine.DirectSet(entry.Key, entry.Value.Value, ttl);
                break;

            case OpCodes.DEL:
                _engine.DirectDelete(entry.Key);
                break;

            case OpCodes.CAS_GT:
                _engine.DirectCasIfGreater(entry.Key, entry.NewValue, ttl);
                break;

            case OpCodes.DECR_POS:
                _engine.DirectDecrIfPositive(entry.Key);
                break;

            case OpCodes.INCR:
                _engine.DirectIncrement(entry.Key, entry.NewValue != 0 ? entry.NewValue : 1);
                break;

            case OpCodes.CAS:
                // CAS 複製：直接 SET 為新值（Leader 已確認 CAS 成功）
                if (entry.Value.HasValue)
                    _engine.DirectSet(entry.Key, entry.Value.Value, ttl);
                break;

            case OpCodes.EXPIRE:
                // EXPIRE 複製：直接設定 TTL
                if (entry.TtlMs > 0)
                    _engine.DirectExpire(entry.Key, TimeSpan.FromMilliseconds(entry.TtlMs));
                break;

            default:
                // 通用 SET 處理（LOCK/UNLOCK 等不需要複製的操作）
                _logger.LogWarning(
                    "Unhandled replication OpCode: 0x{Op:X2} ({Name}) for key={Key}",
                    entry.OpCode, OpCodes.GetName(entry.OpCode), entry.Key);
                if (entry.Value.HasValue)
                    _engine.DirectSet(entry.Key, entry.Value.Value, ttl);
                break;
        }
    }

    /// <summary>
    /// 批量套用（增量同步 / 快照恢復）
    /// </summary>
    public int ApplyBatch(List<ReplicationEntry> entries)
    {
        int applied = 0;

        foreach (var entry in entries)
        {
            if (entry.Lsn <= _replicationLog.CurrentLsn)
                continue; // 跳過已套用

            try
            {
                ApplyToEngine(entry);
                _replicationLog.SetLsn(entry.Lsn);
                applied++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Batch apply failed at LSN {Lsn}: {Error}",
                    entry.Lsn, ex.Message);
                break; // 中斷批次，避免跳過條目
            }
        }

        _logger.LogInformation("Batch applied {Applied}/{Total} entries (currentLsn={Lsn})",
            applied, entries.Count, _replicationLog.CurrentLsn);

        return applied;
    }

    /// <summary>
    /// 編碼 REPLICATE_ACK frame
    /// </summary>
    private static byte[] EncodeAck(bool ok, long appliedLsn, string? error)
    {
        var response = new CacheResponse
        {
            Id = CacheSerializer.NewRequestId(),
            Ok = ok,
            NumValue = appliedLsn,
            Error = error
        };

        var payload = CacheSerializer.SerializeResponse(response);
        return FrameCodec.Encode(OpCodes.REPLICATE_ACK, payload);
    }
}
