namespace StrategyWorker.Engine;

/// <summary>
/// 策略中心目錄——所有 IStrategy 實作集中註冊、用 Name 查。
/// 取代散落各處的 switch (strategy) { case "sma_cross": ... } 模式。
///
/// 不在 ctor 內部 new 各個策略，因為部分策略需要外部依賴（CompositeStrategy 要餵 constituents、
/// LlmStrategy 要 HttpClient + broker URL、AutoSelectStrategy 要先建好的成員 dict）。
/// 這些 wiring 邏輯保留在 Program.cs，registry 只當容器：
///
///   var reg = new DefaultStrategyRegistry();
///   reg.Register(new SmaCrossStrategy());
///   reg.Register(WeightedEnsembleStrategy.Build(...));   // 各種需要組裝的也由外部丟進來
///
/// 加新策略只要：
///   1. 寫一個 class : IStrategy（必要時 override metadata properties）
///   2. 在 Program.cs 加一行 reg.Register(new MyStrategy())
/// 不必 touch StrategyConfig（用 Params dictionary）、不必 touch /strategy/list、
/// 不必 touch ParameterOptimizer（從 ParamSchema 自動掃）。
/// </summary>
public interface IStrategyRegistry
{
    IStrategy? Get(string name);
    IReadOnlyList<IStrategy> All();
    IReadOnlyList<string> Names();
}

public sealed class DefaultStrategyRegistry : IStrategyRegistry
{
    private readonly Dictionary<string, IStrategy> _byName = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IStrategy strategy)
    {
        if (string.IsNullOrEmpty(strategy.Name))
            throw new InvalidOperationException("Strategy.Name must be set");
        _byName[strategy.Name] = strategy;
    }

    public IStrategy? Get(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return _byName.TryGetValue(name, out var s) ? s : null;
    }

    public IReadOnlyList<IStrategy> All() => _byName.Values.ToList();
    public IReadOnlyList<string> Names() => _byName.Keys.ToList();
}
