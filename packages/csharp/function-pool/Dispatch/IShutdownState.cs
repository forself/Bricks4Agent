namespace FunctionPool.Dispatch;

/// <summary>
/// Broker 是否正在關閉的共享旗標。
///
/// 用途：broker shutdown（SIGTERM / Ctrl+C / docker stop）→ ASP.NET host
/// 觸發 ApplicationStopping → 設 IsStopping=true → PoolDispatcher 看到後不再
/// 派發新請求、直接 Fail 回覆。避免 in-flight dispatch 在 broker 正在收尾時
/// 進到 worker、讓 worker 看到 IOException 滿天飛。
///
/// 也是給 audit chain 一個寫 BROKER_SHUTDOWN 事件的事件源。
///
/// 簡單實作：volatile bool；不需要重啟 reset，每次 broker 啟動是新 instance。
/// </summary>
public interface IShutdownState
{
    bool IsStopping { get; }
    void MarkStopping();
}

public sealed class ShutdownState : IShutdownState
{
    private volatile bool _isStopping;
    public bool IsStopping => _isStopping;
    public void MarkStopping() => _isStopping = true;
}
