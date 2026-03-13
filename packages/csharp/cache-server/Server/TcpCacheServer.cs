using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace CacheServer.Server;

/// <summary>
/// TCP 快取伺服器 — 非同步 TCP listener + 連線管理
///
/// 職責：
/// 1. 監聽 TCP 端口
/// 2. Accept 新連線 → 建立 ClientSession
/// 3. 管理所有活躍 session
/// 4. 定時清理已斷線的 session
/// 5. 優雅關閉（drain 所有連線）
/// </summary>
public class TcpCacheServer : IAsyncDisposable
{
    private readonly CommandRouter _router;
    private readonly ILogger<TcpCacheServer> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly int _port;
    private readonly string _bindAddress;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _cleanupTask;

    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();

    // 配置
    private const int MaxConnections = 10_000;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(10);

    public TcpCacheServer(
        CommandRouter router,
        int port,
        string bindAddress,
        ILoggerFactory loggerFactory)
    {
        _router = router;
        _port = port;
        _bindAddress = bindAddress;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<TcpCacheServer>();
    }

    /// <summary>啟動伺服器</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var endpoint = _bindAddress == "0.0.0.0"
            ? new IPEndPoint(IPAddress.Any, _port)
            : new IPEndPoint(IPAddress.Parse(_bindAddress), _port);

        _listener = new TcpListener(endpoint);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();

        _logger.LogInformation("Cache server listening on {Address}:{Port}", _bindAddress, _port);

        // 啟動 accept 迴圈
        _acceptTask = AcceptLoopAsync(_cts.Token);

        // 啟動清理迴圈
        _cleanupTask = CleanupLoopAsync(_cts.Token);

        return Task.CompletedTask;
    }

    /// <summary>停止伺服器（優雅關閉）</summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping cache server...");

        _cts?.Cancel();
        _listener?.Stop();

        // 等待 accept 迴圈結束
        if (_acceptTask != null)
        {
            try { await _acceptTask; } catch { }
        }

        // 停止所有 session
        var tasks = new List<Task>();
        foreach (var (id, session) in _sessions)
        {
            session.Stop();
            tasks.Add(session.DisposeAsync().AsTask());
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }

        _sessions.Clear();
        _logger.LogInformation("Cache server stopped. Closed {Count} sessions.", tasks.Count);
    }

    /// <summary>接受連線迴圈</summary>
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _listener!.AcceptTcpClientAsync(ct);

                // 檢查連線上限
                if (_sessions.Count >= MaxConnections)
                {
                    _logger.LogWarning("Max connections reached ({Max}), rejecting", MaxConnections);
                    tcpClient.Dispose();
                    continue;
                }

                // 設定 TCP 選項
                tcpClient.NoDelay = true; // 關閉 Nagle，低延遲
                tcpClient.ReceiveTimeout = 0; // 由應用層控制超時
                tcpClient.SendTimeout = 5000; // 5 秒寫入超時

                // 建立 session
                var sessionLogger = _loggerFactory.CreateLogger<ClientSession>();
                var session = new ClientSession(tcpClient, _router, sessionLogger);
                _sessions[session.Id] = session;

                // 在背景處理此連線
                _ = ProcessSessionAsync(session, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting connection");
                await Task.Delay(100, ct); // 避免繁忙迴圈
            }
        }
    }

    /// <summary>處理單一 session（背景 task）</summary>
    private async Task ProcessSessionAsync(ClientSession session, CancellationToken ct)
    {
        try
        {
            await session.ProcessAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Session {Id} processing error", session.Id);
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            await session.DisposeAsync();
        }
    }

    /// <summary>定時清理閒置/斷線的 session</summary>
    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, ct);

                var now = DateTime.UtcNow;
                var toRemove = new List<string>();

                foreach (var (id, session) in _sessions)
                {
                    if (!session.IsActive || (now - session.LastActivityAt) > IdleTimeout)
                    {
                        toRemove.Add(id);
                    }
                }

                foreach (var id in toRemove)
                {
                    if (_sessions.TryRemove(id, out var session))
                    {
                        _logger.LogInformation("Cleaning up idle session {Id}", id);
                        session.Stop();
                        await session.DisposeAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in cleanup loop");
            }
        }
    }

    // ── 狀態查詢 ──

    /// <summary>當前活躍連線數</summary>
    public int ActiveSessionCount => _sessions.Count;

    /// <summary>伺服器端口</summary>
    public int Port => _port;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
