using Microsoft.Extensions.Logging;

namespace Broker.Services;

/// <summary>
/// 叢集 leader 守衛:讓「只該由 PRIMARY 做的工作」在 STANDBY 上自我跳過(self-fence 的應用層介面)。
///
/// 階段②(本階段):先 gate **非真錢**週期工作(健康 snapshot / 排程 / 之後 scanner)——
/// 試水溫、零風險,因為單機預設 IsPrimary 永遠 true、現行行為完全不變;只有真的設了 etcd 多節點時,
/// STANDBY 才會開始跳過這些工作(避免雙寫/雙送)。
///
/// 階段④會用「同一個」guard gate 真錢 dispatch(配合 idempotency key)——這裡先把模式建立、驗證無害。
/// </summary>
public sealed class LeaderGuard
{
    private readonly ILeaderElection _election;
    private readonly ILogger<LeaderGuard> _logger;

    public LeaderGuard(ILeaderElection election, ILogger<LeaderGuard> logger)
    {
        _election = election;
        _logger = logger;
    }

    /// <summary>
    /// PRIMARY(或單機)→ 回 true(可執行)。STANDBY → 回 false(呼叫端應 skip)+ 記一筆 debug log。
    /// </summary>
    public bool ShouldRun(string what)
    {
        if (_election.IsPrimary) return true;
        _logger.LogDebug("[leader-guard] 跳過 {What}:本節點非 PRIMARY(role={Role})", what, _election.Role);
        return false;
    }
}
