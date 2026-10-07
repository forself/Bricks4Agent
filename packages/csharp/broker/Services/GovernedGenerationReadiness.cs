using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Container;
using FunctionPool.Registry;

namespace Broker.Services;

/// <summary>受治理生成就緒檢查的結果。<see cref="Reasons"/> 只寫進日誌，不回給使用者。</summary>
public sealed class GovernedGenerationReadinessResult
{
    public bool Ready => Reasons.Count == 0;
    public List<string> Reasons { get; } = new();

    public static GovernedGenerationReadinessResult ReadyResult() => new();
}

/// <summary>受治理生成的前置條件檢查。</summary>
public interface IGovernedGenerationReadiness
{
    Task<GovernedGenerationReadinessResult> CheckAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Governed 模式的前置條件：模式設定有效、ContainerManager 已啟用且容器執行環境可用、LlmProxy 已啟用、
/// 三個生成能力存在、每個能力都有已註冊的 generation-worker、Generation:OutputRoot 是存在的絕對路徑。
/// 任何一項不成立就不就緒：呼叫端回覆「暫不可用」，不建立任務，也不退回程序內生成。
/// </summary>
public sealed class GovernedGenerationReadiness : IGovernedGenerationReadiness
{
    private readonly HighLevelCoordinatorOptions _coordinatorOptions;
    private readonly GovernedGenerationOptions _options;
    private readonly IContainerManager _containerManager;
    private readonly ILlmProxyService _llmProxy;
    private readonly BrokerDb _db;
    private readonly IWorkerRegistry? _workerRegistry;

    public GovernedGenerationReadiness(
        HighLevelCoordinatorOptions coordinatorOptions,
        GovernedGenerationOptions options,
        IContainerManager containerManager,
        ILlmProxyService llmProxy,
        BrokerDb db,
        IEnumerable<IWorkerRegistry> workerRegistries)
    {
        _coordinatorOptions = coordinatorOptions;
        _options = options;
        _containerManager = containerManager;
        _llmProxy = llmProxy;
        _db = db;
        // FunctionPool 未啟用時沒有 worker registry：視為沒有 generation-worker。
        _workerRegistry = workerRegistries.FirstOrDefault();
    }

    public async Task<GovernedGenerationReadinessResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var result = new GovernedGenerationReadinessResult();

        if (!_coordinatorOptions.Generation.IsValidMode)
            result.Reasons.Add("HighLevelCoordinator:Generation:SystemScaffoldMode is neither Legacy nor Governed.");

        if (_containerManager is NoOpContainerManager)
            result.Reasons.Add("Container manager is not enabled.");

        if (!_llmProxy.IsEnabled)
            result.Reasons.Add("LLM proxy is not enabled.");

        foreach (var capabilityId in GenerationCapabilities.All)
        {
            if (_db.Get<Capability>(capabilityId) == null)
                result.Reasons.Add($"Capability {capabilityId} is not loaded.");
        }

        if (_workerRegistry == null)
        {
            result.Reasons.Add("Function pool is not enabled; no generation worker can register.");
        }
        else
        {
            foreach (var capabilityId in GenerationCapabilities.All)
            {
                if (_workerRegistry.GetWorkersByCapability(capabilityId).Count == 0)
                    result.Reasons.Add($"No registered worker serves {capabilityId}.");
            }
        }

        var outputRoot = _options.ResolveOutputRoot();
        if (outputRoot == null)
            result.Reasons.Add("Generation:OutputRoot is not an absolute path.");
        else if (!Directory.Exists(outputRoot))
            result.Reasons.Add("Generation:OutputRoot does not exist.");

        // 前面已不就緒時不必再呼叫容器執行環境。
        if (result.Ready && !await IsRuntimeAvailableAsync(cancellationToken))
            result.Reasons.Add("Container runtime is not available.");

        return result;
    }

    private async Task<bool> IsRuntimeAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _containerManager.IsRuntimeAvailableAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
