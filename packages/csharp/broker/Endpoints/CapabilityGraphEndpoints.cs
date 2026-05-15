using Broker.Helpers;
using BrokerCore.Models;
using BrokerCore.Services;
using FunctionPool.Models;
using FunctionPool.Registry;

namespace Broker.Endpoints;

/// <summary>
/// G3 — Capability Registry Graph
///
/// GET /api/v1/capabilities/graph
///
/// 把 Benson 設計的 CapabilityCatalog（白名單 + 風險等級 + approval policy + schema）
/// 跟 IWorkerRegistry 動態註冊的 worker 兩邊聯起來、給 dashboard 畫成 sankey / 樹狀圖。
///
/// 跟既有 /api/v1/audit/topology 的差別：
///   - audit/topology = runtime traffic（過去 N 分鐘 principal → capability 呼叫量）
///   - capabilities/graph = static design（capability 設計 + 哪些 worker 物理上會服務它）
///
/// 答辯時：「Benson 的 capability 框架不只是 spec、broker 啟動時把每筆 capability 載進
/// catalog、worker 註冊時聲明自己 serves 哪些、broker 自動配對。這張圖把這個 mapping 攤開。」
/// </summary>
public static class CapabilityGraphEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/capabilities/graph", (HttpContext ctx,
            ICapabilityCatalog catalog, IWorkerRegistry registry) =>
        {
            // 沒登入也能看（無敏感資料；schema 是 public design）
            var caps = catalog.ListCapabilities();
            var workers = registry.GetAllWorkers();

            // 建 capability_id → workers 的反向索引
            var capToWorkers = new Dictionary<string, List<WorkerInfo>>();
            foreach (var c in caps)
            {
                capToWorkers[c.CapabilityId] = registry.GetWorkersByCapability(c.CapabilityId);
            }

            // 統計分布
            var byRisk = caps.GroupBy(c => c.RiskLevel.ToString())
                .ToDictionary(g => g.Key, g => g.Count());
            var byApproval = caps.GroupBy(c => c.ApprovalPolicy ?? "auto")
                .ToDictionary(g => g.Key, g => g.Count());
            var byResource = caps.GroupBy(c => string.IsNullOrWhiteSpace(c.ResourceType) ? "(none)" : c.ResourceType)
                .ToDictionary(g => g.Key, g => g.Count());

            return Results.Ok(ApiResponseHelper.Success(new
            {
                generated_at = DateTime.UtcNow,
                stats = new
                {
                    total_capabilities = caps.Count,
                    total_workers = workers.Count,
                    by_risk = byRisk,
                    by_approval = byApproval,
                    by_resource = byResource,
                    unbacked_capabilities = capToWorkers.Count(kv => kv.Value.Count == 0),
                },
                capabilities = caps.OrderBy(c => c.RiskLevel).ThenBy(c => c.CapabilityId).Select(c => new
                {
                    capability_id   = c.CapabilityId,
                    route           = c.Route,
                    resource_type   = c.ResourceType,
                    action_type     = c.ActionType.ToString(),
                    risk_level      = c.RiskLevel.ToString(),
                    risk_value      = (int)c.RiskLevel,
                    approval_policy = c.ApprovalPolicy,
                    audit_level     = c.AuditLevel,
                    ttl_seconds     = c.TtlSeconds,
                    quota           = c.Quota,
                    param_schema    = c.ParamSchema,
                    backing_workers = capToWorkers[c.CapabilityId].Select(w => new
                    {
                        worker_id = w.WorkerId,
                        capabilities = w.Capabilities,
                    }).ToList(),
                    has_worker      = capToWorkers[c.CapabilityId].Count > 0,
                }),
                workers = workers.Select(w => new
                {
                    worker_id = w.WorkerId,
                    capabilities = w.Capabilities ?? new List<string>(),
                    capability_count = (w.Capabilities?.Count ?? 0),
                }).OrderBy(w => w.worker_id),
            }));
        });
    }
}
