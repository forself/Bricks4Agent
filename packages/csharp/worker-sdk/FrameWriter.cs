namespace WorkerSdk;

/// <summary>
/// 對一條 stream 寫入完整 frame，同一時間只允許一個寫入者。
///
/// WorkerHost 會從多處寫入同一條連線：分派結果（每個請求一個背景工作）、心跳計時器、狀態查詢回覆。
/// 沒有寫入鎖時，兩個寫入可能交錯，broker 端讀到的 frame 就會損毀。
/// 這裡在整個「寫入 + Flush」期間持有鎖，所以每個 frame 都是連續的位元組。
/// </summary>
internal sealed class FrameWriter
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FrameWriter(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>寫入一個已編碼的完整 frame（含 header）。</summary>
    public async Task WriteFrameAsync(byte[] frame, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(frame);

        await _writeLock.WaitAsync(ct);
        try
        {
            await _stream.WriteAsync(frame, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
