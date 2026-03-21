using System.Text.Json;
using BrokerCore.Data;
using BrokerCore.Models;

namespace BrokerCore.Services;

/// <summary>
/// Agent 生成服務 — 建立 Agent 容器的完整流程
///
/// 流程：
/// 1. 列出可用能力清單（含風險等級、說明）
/// 2. 使用者選擇要授予的能力
/// 3. 建立 Principal + Task + CapabilityGrants
/// 4. 產生 RuntimeDescriptor（Agent 容器啟動參數）
/// 5. 呼叫 ContainerManager 生成 Agent 容器
///
/// 預設策略：最低權限（Low risk only），Medium+ 需明確選擇
/// </summary>
public class AgentSpawnService
{
    private readonly BrokerDb _db;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public AgentSpawnService(BrokerDb db)
    {
        _db = db;
    }

    /// <summary>取得所有可用能力清單（給前端 / Q&A 用）</summary>
    public List<CapabilityInfo> ListAvailableCapabilities()
    {
        var capabilities = _db.GetAll<Capability>();
        return capabilities.Select(c => new CapabilityInfo
        {
            CapabilityId = c.CapabilityId,
            Route = c.Route,
            ActionType = c.ActionType.ToString(),
            ResourceType = c.ResourceType,
            RiskLevel = c.RiskLevel.ToString(),
            RiskLevelValue = (int)c.RiskLevel,
            ApprovalPolicy = c.ApprovalPolicy,
            Description = DescribeCapability(c.CapabilityId),
            Category = CategorizeCapability(c.CapabilityId)
        }).OrderBy(c => c.RiskLevelValue).ThenBy(c => c.Category).ToList();
    }

    /// <summary>取得預設（最低權限）的能力集合</summary>
    public List<string> GetDefaultCapabilities()
    {
        var capabilities = _db.GetAll<Capability>();
        return capabilities
            .Where(c => c.RiskLevel == RiskLevel.Low)
            .Select(c => c.CapabilityId)
            .ToList();
    }

    /// <summary>
    /// 依 TaskType 取得預設能力集合
    ///
    /// 支援的類型：
    /// - analysis: 分析型（預設，最低權限）
    /// - rag: RAG 檢索增強型（含 memory + RAG 能力）
    /// - assistant: 助理型（含 memory + conv_log + communication）
    /// - full: 全能力（所有 Low + Medium 權限）
    /// </summary>
    public List<string> GetCapabilitiesForTaskType(string taskType)
    {
        var allCaps = _db.GetAll<Capability>().ToDictionary(c => c.CapabilityId);
        var baseCaps = allCaps.Values
            .Where(c => c.RiskLevel == RiskLevel.Low)
            .Select(c => c.CapabilityId)
            .ToList();

        switch (taskType.ToLowerInvariant())
        {
            case "rag":
                // RAG 代理：基礎權限 + 所有 memory/RAG 相關能力
                var ragCaps = new[] {
                    "memory.read", "memory.write",
                    "memory.fulltext_search", "memory.semantic_search",
                    "rag.retrieve", "rag.import", "rag.import_web",
                    "conv.log.write", "conv.log.read"
                };
                foreach (var cap in ragCaps)
                    if (allCaps.ContainsKey(cap) && !baseCaps.Contains(cap))
                        baseCaps.Add(cap);
                return baseCaps;

            case "assistant":
                // 助理代理：基礎權限 + memory + conv_log + communication
                var assistCaps = new[] {
                    "memory.read", "memory.write",
                    "memory.fulltext_search", "memory.semantic_search",
                    "rag.retrieve",
                    "conv.log.write", "conv.log.read",
                    "line.message.send", "line.message.read",
                    "line.notification.send"
                };
                foreach (var cap in assistCaps)
                    if (allCaps.ContainsKey(cap) && !baseCaps.Contains(cap))
                        baseCaps.Add(cap);
                return baseCaps;

            case "full":
                // 全能力代理：所有 Low + Medium 權限
                return allCaps.Values
                    .Where(c => c.RiskLevel <= RiskLevel.Medium)
                    .Select(c => c.CapabilityId)
                    .ToList();

            default: // "analysis" or any other
                return baseCaps;
        }
    }

    /// <summary>
    /// 建立 Agent（Principal + Task + Grants），回傳啟動配置
    /// </summary>
    public AgentSpawnResult CreateAgent(AgentSpawnRequest request)
    {
        var agentId = request.AgentId ?? $"agent_{Guid.NewGuid():N}"[..24];
        var principalId = $"prn_{agentId}";
        var taskId = $"task_{agentId}";
        var now = DateTime.UtcNow;

        // 若未指定能力，依 TaskType 自動選擇
        var requestedCapIds = request.CapabilityIds;
        if (requestedCapIds.Count == 0 && !string.IsNullOrEmpty(request.TaskType))
        {
            requestedCapIds = GetCapabilitiesForTaskType(request.TaskType);
        }
        else if (requestedCapIds.Count == 0)
        {
            requestedCapIds = GetDefaultCapabilities();
        }

        // 驗證所選能力
        var allCapabilities = _db.GetAll<Capability>().ToDictionary(c => c.CapabilityId);
        var selectedCapabilities = new List<Capability>();
        var warnings = new List<string>();

        foreach (var capId in requestedCapIds.Distinct())
        {
            if (!allCapabilities.TryGetValue(capId, out var cap))
            {
                warnings.Add($"Unknown capability: {capId} (skipped)");
                continue;
            }
            selectedCapabilities.Add(cap);

            if (cap.RiskLevel >= RiskLevel.High)
            {
                warnings.Add($"High-risk capability granted: {capId} — use with caution");
            }
        }

        if (selectedCapabilities.Count == 0)
        {
            return new AgentSpawnResult
            {
                Success = false,
                Error = "No valid capabilities selected. Agent must have at least one capability."
            };
        }

        // 決定角色（基於最高風險等級）
        var maxRisk = selectedCapabilities.Max(c => c.RiskLevel);
        var roleId = maxRisk switch
        {
            RiskLevel.Low => "role_reader",
            RiskLevel.Medium => "role_executor",
            _ => "role_executor"
        };

        // 建立 Principal
        var principal = new Principal
        {
            PrincipalId = principalId,
            ActorType = ActorType.AI,
            DisplayName = request.DisplayName ?? $"Agent {agentId}",
            Status = EntityStatus.Active,
            CreatedAt = now
        };

        if (_db.Get<Principal>(principalId) == null)
            _db.Insert(principal);

        // 建立 RuntimeDescriptor
        var capabilityGrants = selectedCapabilities.Select(cap => new
        {
            capability_id = cap.CapabilityId,
            scope = BuildScope(cap),
            quota = request.QuotaPerCapability
        }).ToList();

        var runtimeDescriptor = JsonSerializer.Serialize(new
        {
            llm = new { },
            capability_grants = capabilityGrants
        }, JsonOptions);

        // 建立 Task
        var task = new BrokerTask
        {
            TaskId = taskId,
            TaskType = request.TaskType ?? "analysis",
            SubmittedBy = request.RequestedBy ?? "system",
            RiskLevel = maxRisk,
            State = TaskState.Active,
            ScopeDescriptor = request.ScopeDescriptor ?? "{}",
            RuntimeDescriptor = runtimeDescriptor,
            AssignedPrincipalId = principalId,
            AssignedRoleId = roleId,
            CreatedAt = now
        };

        if (_db.Get<BrokerTask>(taskId) == null)
            _db.Insert(task);

        return new AgentSpawnResult
        {
            Success = true,
            AgentId = agentId,
            PrincipalId = principalId,
            TaskId = taskId,
            RoleId = roleId,
            GrantedCapabilities = selectedCapabilities.Select(c => c.CapabilityId).ToList(),
            RuntimeDescriptor = runtimeDescriptor,
            Warnings = warnings,
            MaxRiskLevel = maxRisk.ToString()
        };
    }

    /// <summary>停用 Agent（標記 Principal 和 Task 為 Inactive）</summary>
    public bool DeactivateAgent(string agentId)
    {
        var principalId = $"prn_{agentId}";
        var taskId = $"task_{agentId}";

        var principal = _db.Get<Principal>(principalId);
        if (principal != null)
        {
            principal.Status = EntityStatus.Disabled;
            _db.Update(principal);
        }

        var task = _db.Get<BrokerTask>(taskId);
        if (task != null)
        {
            task.State = TaskState.Completed;
            _db.Update(task);
            return true;
        }
        return false;
    }

    /// <summary>列出所有已建立的 Agent</summary>
    public List<AgentSummary> ListAgents()
    {
        var tasks = _db.GetAll<BrokerTask>()
            .Where(t => t.AssignedPrincipalId?.StartsWith("prn_agent_") == true)
            .ToList();

        return tasks.Select(t =>
        {
            var agentId = t.TaskId.StartsWith("task_") ? t.TaskId[5..] : t.TaskId;
            List<string> capabilities;
            try
            {
                var desc = JsonDocument.Parse(t.RuntimeDescriptor ?? "{}");
                capabilities = desc.RootElement
                    .GetProperty("capability_grants")
                    .EnumerateArray()
                    .Select(g => g.GetProperty("capability_id").GetString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            catch
            {
                capabilities = new List<string>();
            }

            return new AgentSummary
            {
                AgentId = agentId,
                PrincipalId = t.AssignedPrincipalId ?? "",
                TaskId = t.TaskId,
                RoleId = t.AssignedRoleId ?? "",
                State = t.State.ToString(),
                TaskType = t.TaskType,
                CapabilityCount = capabilities.Count,
                Capabilities = capabilities,
                CreatedAt = t.CreatedAt
            };
        }).ToList();
    }

    // ── 輔助方法 ──

    private static object BuildScope(Capability cap)
    {
        // 根據能力類型建構預設 scope
        return cap.ResourceType switch
        {
            "file" => new
            {
                paths = new[] { "/workspace" },
                routes = new[] { cap.Route }
            },
            "communication" => new
            {
                routes = new[] { cap.Route }
            },
            _ => new
            {
                routes = new[] { cap.Route }
            }
        };
    }

    private static string DescribeCapability(string capabilityId) => capabilityId switch
    {
        "file.read" => "Read file contents|讀取檔案內容",
        "file.list" => "List directory structure|列出目錄結構",
        "file.search_name" => "Search files by name|依檔名搜尋檔案",
        "file.search_content" => "Search file contents|搜尋檔案內容",
        "file.write" => "Write/create files|寫入或建立檔案",
        "file.delete" => "Delete files|刪除檔案",
        "command.execute" => "Execute system commands (high risk)|執行系統命令（高風險）",
        "line.message.send" => "Send LINE text message|發送 LINE 文字訊息",
        "line.notification.send" => "Send LINE structured notification|發送 LINE 結構化通知",
        "line.message.read" => "Read LINE inbound messages|讀取 LINE 入站訊息",
        "line.approval.request" => "Send approval request and wait|發送審批請求並等待回覆",
        "line.audio.send" => "Send LINE audio message|發送 LINE 語音訊息",
        "conv.log.write" => "Auto-record conversation messages|自動記錄對話訊息（日誌層）",
        "conv.log.read" => "Read conversation history log|讀取對話歷史日誌",
        "memory.read" => "Read/search intelligent memory|讀取/搜尋智慧記憶（判斷層）",
        "memory.write" => "Write to intelligent memory|寫入智慧記憶（判斷層）",
        "memory.delete" => "Delete intelligent memory entries|刪除智慧記憶條目",
        "memory.fulltext_search" => "BM25 full-text search|BM25 全文檢索（記憶/日誌）",
        "memory.semantic_search" => "Vector semantic search|向量語意搜尋",
        "rag.retrieve" => "RAG hybrid retrieval (BM25+Vector)|RAG 混合檢索",
        "rag.import" => "Import structured data into RAG (CSV/JSON)|匯入結構化資料至 RAG",
        "rag.import_web" => "Import web content into RAG via search|從網路搜尋匯入 RAG 資料庫",
        "web.search" => "Search the web|搜尋網路資訊",
        "web.fetch" => "Fetch web page content|擷取網頁內容",
        _ => capabilityId
    };

    private static string CategorizeCapability(string capabilityId)
    {
        if (capabilityId.StartsWith("file.")) return "File|檔案";
        if (capabilityId.StartsWith("line.")) return "Communication|通訊";
        if (capabilityId.StartsWith("command.")) return "System|系統";
        if (capabilityId.StartsWith("conv.")) return "ConvLog|對話日誌";
        if (capabilityId.StartsWith("memory.")) return "Memory|智慧記憶";
        if (capabilityId.StartsWith("rag.")) return "RAG|檢索增強生成";
        if (capabilityId.StartsWith("web.")) return "Web|網路";
        if (capabilityId.StartsWith("agent.")) return "Agent|代理管理";
        return "Other|其他";
    }
}

// ── DTO ──

public class CapabilityInfo
{
    public string CapabilityId { get; set; } = "";
    public string Route { get; set; } = "";
    public string ActionType { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public string RiskLevel { get; set; } = "";
    public int RiskLevelValue { get; set; }
    public string ApprovalPolicy { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
}

public class AgentSpawnRequest
{
    public string? AgentId { get; set; }
    public string? DisplayName { get; set; }
    public List<string> CapabilityIds { get; set; } = new();
    public string? TaskType { get; set; }
    public string? ScopeDescriptor { get; set; }
    public string? RequestedBy { get; set; }
    public int QuotaPerCapability { get; set; } = -1; // -1 = unlimited
}

public class AgentSpawnResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string AgentId { get; set; } = "";
    public string PrincipalId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string RoleId { get; set; } = "";
    public List<string> GrantedCapabilities { get; set; } = new();
    public string RuntimeDescriptor { get; set; } = "{}";
    public string MaxRiskLevel { get; set; } = "";
    public List<string> Warnings { get; set; } = new();
}

public class AgentSummary
{
    public string AgentId { get; set; } = "";
    public string PrincipalId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string State { get; set; } = "";
    public string TaskType { get; set; } = "";
    public int CapabilityCount { get; set; }
    public List<string> Capabilities { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}
