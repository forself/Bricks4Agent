using BrokerCore.Contracts;

namespace BrokerCore.Services;

/// <summary>
/// 單一路由處理器 —— InProcessDispatcher 的組成單元
///
/// 每個 IRouteHandler 負責處理一個特定的 route（如 read_file、web_search_google）。
/// 透過 DI 註冊後由 InProcessDispatcher 的 handler registry 查找並呼叫。
///
/// 取代原本 InProcessDispatcher 中的 monolithic switch statement。
/// </summary>
public interface IRouteHandler
{
    /// <summary>此 handler 負責的路由名稱</summary>
    string Route { get; }

    /// <summary>處理已批准的執行請求</summary>
    Task<ExecutionResult> HandleAsync(ApprovedRequest request, CancellationToken ct = default);
}
