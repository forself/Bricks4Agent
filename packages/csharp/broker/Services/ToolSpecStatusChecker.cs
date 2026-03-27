using BrokerCore.Services;

namespace Broker.Services;

/// <summary>
/// IToolSpecStatusChecker 實作 —— 透過 IToolSpecRegistry 檢查 tool spec 狀態
/// </summary>
public sealed class ToolSpecStatusChecker : IToolSpecStatusChecker
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "ready", "beta"
    };

    private readonly IToolSpecRegistry _registry;
    private readonly ILogger<ToolSpecStatusChecker> _logger;

    public ToolSpecStatusChecker(IToolSpecRegistry registry, ILogger<ToolSpecStatusChecker> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public (bool Allowed, string? Reason) CheckStatus(string capabilityId)
    {
        // 嘗試以 capabilityId 直接查詢 tool spec
        var spec = _registry.Get(capabilityId);

        // 也嘗試透過 capability bindings 反查
        if (spec == null)
        {
            spec = FindSpecByCapabilityBinding(capabilityId);
        }

        // 無對應 tool spec → 放行（DB-seeded capability 或尚未建立 tool spec）
        if (spec == null)
            return (true, null);

        if (AllowedStatuses.Contains(spec.Status))
            return (true, null);

        _logger.LogWarning(
            "Tool spec {ToolId} has status '{Status}' — capability {CapabilityId} blocked by status check",
            spec.ToolId, spec.Status, capabilityId);

        return (false, $"Tool spec '{spec.ToolId}' has status '{spec.Status}' (requires active/ready/beta).");
    }

    private ToolSpecView? FindSpecByCapabilityBinding(string capabilityId)
    {
        var allSpecs = _registry.List();
        return allSpecs.FirstOrDefault(s =>
            s.CapabilityBindings.Any(b =>
                string.Equals(b.CapabilityId, capabilityId, StringComparison.OrdinalIgnoreCase)));
    }
}
