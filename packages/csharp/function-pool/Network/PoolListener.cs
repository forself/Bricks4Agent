using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BrokerCore.Services;
using FunctionPool.Models;
using FunctionPool.Registry;
using Microsoft.Extensions.Logging;

namespace FunctionPool.Network;

/// <summary>
/// 功能池 TCP Listener — Worker 連入端點
///
/// 複用 TcpCacheServer 模式：
/// 1. Accept Loop → 接受 Worker TCP 連線
/// 2. 為每個連線建立 WorkerSession
/// 3. Cleanup Loop → 清理已斷線 session
/// 4. 優雅關閉
/// </summary>
public class PoolListener : IAsyncDisposable
{
    private readonly PoolConfig _config;
    private readonly IWorkerRegistry _registry;
    private readonly WorkerIdentityAuthService _workerIdentityAuth;
    private readonly ILogger<PoolListener> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _cleanupTask;

    private readonly ConcurrentDictionary<string, WorkerSession> _sessions = new();

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

    public PoolListener(
        PoolConfig config,
        IWorkerRegistry registry,
        WorkerIdentityAuthService workerIdentityAuth,
        ILoggerFactory loggerFactory)
    {
        _config = config;
        _registry = registry;
        _workerIdentityAuth = workerIdentityAuth;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PoolListener>();
    }

    /// <summary>啟動 TCP listener</summary>
    public Task StartAsync(CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var endpoint = _config.BindAddress == "0.0.0.0"
            ? new IPEndPoint(IPAddress.Any, _config.ListenPort)
            : new IPEndPoint(IPAddress.Parse(_config.BindAddress), _config.ListenPort);

        _listener = new TcpListener(endpoint);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Start();

        _logger.LogInformation("Function pool listener started on {Address}:{Port}",
            _config.BindAddress, _config.ListenPort);

        // 啟動 accept 迴圈
        _acceptTask = AcceptLoopAsync(_cts.Token);

        // 啟動清理迴圈
        _cleanupTask = CleanupLoopAsync(_cts.Token);

        return Task.CompletedTask;
    }

    /// <summary>停止 listener</summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping function pool listener...");

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
            await Task.WhenAll(tasks);

        _sessions.Clear();
        _logger.LogInformation("Function pool listener stopped. Closed {Count} worker sessions.", tasks.Count);
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
                if (_sessions.Count >= _config.MaxWorkers)
                {
                    _logger.LogWarning("Max worker connections reached ({Max}), rejecting",
                        _config.MaxWorkers);
                    tcpClient.Dispose();
                    continue;
                }

                // 設定 TCP 選項
                tcpClient.NoDelay = true;
                tcpClient.ReceiveTimeout = 0;
                tcpClient.SendTimeout = 5000;

                // 建立 WorkerConnection 和 WorkerSession
                var connLogger = _loggerFactory.CreateLogger<WorkerConnection>();
                var connection = new WorkerConnection(tcpClient, connLogger);

                var sessionLogger = _loggerFactory.CreateLogger<WorkerSession>();
                var session = new WorkerSession(connection, _registry, _workerIdentityAuth, sessionLogger);
                _sessions[session.Id] = session;

                _logger.LogDebug("Worker connected from {Ep}, session={Id}",
                    connection.RemoteEndpoint, session.Id);

                // 在背景處理此 session
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
                _logger.LogError(ex, "Error accepting worker connection");
                await Task.Delay(100, ct);
            }
        }
    }

    /// <summary>處理單一 session（背景 task）</summary>
    private async Task ProcessSessionAsync(WorkerSession session, CancellationToken ct)
    {
        try
        {
            await session.ProcessAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Worker session {Id} processing error", session.Id);
        }
        finally
        {
            _sessions.TryRemove(session.Id, out _);
            await session.DisposeAsync();
        }
    }

    /// <summary>定時清理已斷線 session</summary>
    private async Task CleanupLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, ct);

                var toRemove = new List<string>();
                foreach (var (id, session) in _sessions)
                {
                    if (!session.IsActive)
                    {
                        toRemove.Add(id);
                    }
                }

                foreach (var id in toRemove)
                {
                    if (_sessions.TryRemove(id, out var session))
                    {
                        _logger.LogInformation("Cleaning up inactive worker session {Id}", id);
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
                _logger.LogWarning(ex, "Error in worker session cleanup loop");
            }
        }
    }

    /// <summary>當前 Worker session 數</summary>
    public int ActiveSessionCount => _sessions.Count;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
