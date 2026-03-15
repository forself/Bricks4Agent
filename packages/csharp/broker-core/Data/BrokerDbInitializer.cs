using BrokerCore.Models;
using System.Text.Json;

namespace BrokerCore.Data;

/// <summary>
/// 資料庫初始化 —— EnsureTable + 種子資料
/// 啟動時呼叫，確保 17 張表存在並植入種子資料
/// </summary>
public class BrokerDbInitializer
{
    private readonly BrokerDb _db;

    public BrokerDbInitializer(BrokerDb db)
    {
        _db = db;
    }

    /// <summary>初始化所有表結構 + 種子資料</summary>
    public void Initialize()
    {
        EnsureTables();
        SeedSystemEpoch();
        SeedRoles();
        SeedCapabilities();
    }

    /// <summary>建立 17 張表</summary>
    private void EnsureTables()
    {
        // ── Phase 1：核心控制平面（12 張） ──
        _db.EnsureTable<Principal>();
        _db.EnsureTable<Role>();
        _db.EnsureTable<RoleBinding>();
        _db.EnsureTable<Capability>();
        _db.EnsureTable<CapabilityGrant>();
        _db.EnsureTable<BrokerTask>();
        _db.EnsureTable<ContainerSession>();
        _db.EnsureTable<ExecutionRequest>();
        _db.EnsureTable<AuditEvent>();
        _db.EnsureTable<SharedContextEntry>();
        _db.EnsureTable<Revocation>();
        _db.EnsureTable<SystemEpoch>();

        // ── Phase 4：因果工作流（4 張） + 觀測（1 張） ──
        _db.EnsureTable<Plan>();
        _db.EnsureTable<PlanNode>();
        _db.EnsureTable<PlanEdge>();
        _db.EnsureTable<Checkpoint>();
        _db.EnsureTable<ObservationEvent>();

        // 額外建立複合唯一約束 + 索引（EnsureTable 不自動建立）
        CreateUniqueConstraints();
    }

    /// <summary>建立 EnsureTable 無法自動產生的唯一約束 + 查詢索引</summary>
    private void CreateUniqueConstraints()
    {
        // ── Phase 1 ──

        // ExecutionRequest: (task_id, idempotency_key) 防重複執行
        TryExecute(@"CREATE UNIQUE INDEX IF NOT EXISTS idx_execution_requests_idempotency
                      ON execution_requests(task_id, idempotency_key)");

        // AuditEvent: (trace_id, trace_seq) per-trace chain 序列化
        TryExecute(@"CREATE UNIQUE INDEX IF NOT EXISTS idx_audit_events_trace_seq
                      ON audit_events(trace_id, trace_seq)");

        // ── Phase 4：因果工作流索引 ──

        // PlanNode: (plan_id, ordinal) 拓撲序查詢
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_plan_nodes_plan
                      ON plan_nodes(plan_id, ordinal)");

        // PlanEdge: plan_id 查詢 + from/to 依賴查詢
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_plan_edges_plan
                      ON plan_edges(plan_id)");
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_plan_edges_from
                      ON plan_edges(from_node_id)");
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_plan_edges_to
                      ON plan_edges(to_node_id)");

        // Checkpoint: (plan_id, node_id) 快照查詢
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_checkpoints_plan
                      ON checkpoints(plan_id, node_id)");

        // ── SharedContext 查詢索引 ──

        // (document_id, version) 版本查詢
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_shared_context_doc_ver
                      ON shared_context_entries(document_id, version)");

        // (key, task_id) node output 查詢
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_shared_context_key_task
                      ON shared_context_entries(key, task_id)");

        // ── 觀測事件索引 ──

        // trace_id correlation 查詢
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_observations_trace
                      ON observation_events(trace_id)");

        // plan_id correlation 查詢
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_observations_plan
                      ON observation_events(plan_id)");

        // severity + observed_at 告警查詢
        TryExecute(@"CREATE INDEX IF NOT EXISTS idx_observations_severity
                      ON observation_events(severity, observed_at)");
    }

    /// <summary>初始化 SystemEpoch（僅一行，epoch=1）</summary>
    private void SeedSystemEpoch()
    {
        var existing = _db.Get<SystemEpoch>(1);
        if (existing == null)
        {
            _db.Insert(new SystemEpoch
            {
                EpochId = 1,
                CurrentEpoch = 1,
                UpdatedBy = "system"
            });
        }
    }

    /// <summary>Phase 1 角色種子</summary>
    private void SeedRoles()
    {
        var roles = new[]
        {
            new Role
            {
                RoleId = "role_reader",
                DisplayName = "Reader",
                AllowedTaskTypes = ToJson(new[] { "query", "analysis" }),
                DefaultCapabilityIds = ToJson(new[] { "file.read", "file.list", "file.search_name", "file.search_content" }),
                Status = EntityStatus.Active
            },
            new Role
            {
                RoleId = "role_pm",
                DisplayName = "Project Manager",
                AllowedTaskTypes = ToJson(new[] { "task_management", "planning" }),
                DefaultCapabilityIds = ToJson(Array.Empty<string>()),
                Status = EntityStatus.Active
            },
            new Role
            {
                RoleId = "role_sa",
                DisplayName = "Solution Architect",
                AllowedTaskTypes = ToJson(new[] { "architecture", "review" }),
                DefaultCapabilityIds = ToJson(new[] { "file.read", "file.list", "file.search_name", "file.search_content" }),
                Status = EntityStatus.Active
            },
            new Role
            {
                RoleId = "role_executor",
                DisplayName = "Executor",
                AllowedTaskTypes = ToJson(new[] { "code_gen", "doc_gen" }),
                DefaultCapabilityIds = ToJson(new[] { "file.read", "file.list", "file.search_name", "file.search_content" }),
                Status = EntityStatus.Active
            },
            new Role
            {
                RoleId = "role_auditor",
                DisplayName = "Auditor",
                AllowedTaskTypes = ToJson(new[] { "audit" }),
                DefaultCapabilityIds = ToJson(Array.Empty<string>()),
                Status = EntityStatus.Active
            },
            new Role
            {
                RoleId = "role_admin",
                DisplayName = "Administrator",
                AllowedTaskTypes = ToJson(new[] { "*" }),
                DefaultCapabilityIds = ToJson(new[] { "*" }),
                Status = EntityStatus.Active
            }
        };

        foreach (var role in roles)
        {
            if (_db.Get<Role>(role.RoleId) == null)
                _db.Insert(role);
        }
    }

    /// <summary>Phase 1 能力種子（4 Low + 2 Medium/High）</summary>
    private void SeedCapabilities()
    {
        var capabilities = new[]
        {
            // ── Low 風險：Phase 1 自動放行 ──
            new Capability
            {
                CapabilityId = "file.read",
                Route = "read_file",
                ActionType = ActionType.Read,
                ResourceType = "file",
                RiskLevel = RiskLevel.Low,
                ApprovalPolicy = "auto",
                TtlSeconds = 900,
                ParamSchema = ToJson(new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        offset = new { type = "integer" },
                        limit = new { type = "integer" }
                    },
                    required = new[] { "path" }
                })
            },
            new Capability
            {
                CapabilityId = "file.list",
                Route = "list_directory",
                ActionType = ActionType.Read,
                ResourceType = "file",
                RiskLevel = RiskLevel.Low,
                ApprovalPolicy = "auto",
                TtlSeconds = 900,
                ParamSchema = ToJson(new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        depth = new { type = "integer" }
                    },
                    required = Array.Empty<string>()
                })
            },
            new Capability
            {
                CapabilityId = "file.search_name",
                Route = "search_files",
                ActionType = ActionType.Read,
                ResourceType = "file",
                RiskLevel = RiskLevel.Low,
                ApprovalPolicy = "auto",
                TtlSeconds = 900,
                ParamSchema = ToJson(new
                {
                    type = "object",
                    properties = new
                    {
                        directory = new { type = "string" },
                        pattern = new { type = "string" }
                    },
                    required = new[] { "pattern" }
                })
            },
            new Capability
            {
                CapabilityId = "file.search_content",
                Route = "search_content",
                ActionType = ActionType.Read,
                ResourceType = "file",
                RiskLevel = RiskLevel.Low,
                ApprovalPolicy = "auto",
                TtlSeconds = 900,
                ParamSchema = ToJson(new
                {
                    type = "object",
                    properties = new
                    {
                        directory = new { type = "string" },
                        pattern = new { type = "string" },
                        file_pattern = new { type = "string" }
                    },
                    required = new[] { "pattern" }
                })
            },

            // ── Medium 風險：Phase 3 功能池允許 ──
            new Capability
            {
                CapabilityId = "file.write",
                Route = "write_file",
                ActionType = ActionType.Write,
                ResourceType = "file",
                RiskLevel = RiskLevel.Medium,
                ApprovalPolicy = "require_approval",
                TtlSeconds = 300,
                ParamSchema = ToJson(new
                {
                    type = "object",
                    properties = new
                    {
                        path = new { type = "string" },
                        content = new { type = "string" },
                        mode = new { type = "string", @enum = new[] { "rewrite", "append" } }
                    },
                    required = new[] { "path", "content" }
                })
            },
            new Capability
            {
                CapabilityId = "file.delete",
                Route = "delete_file",
                ActionType = ActionType.Write,
                ResourceType = "file",
                RiskLevel = RiskLevel.Medium,
                ApprovalPolicy = "require_approval",
                TtlSeconds = 300,
                ParamSchema = ToJson(new
                {
                    type = "object",
                    properties = new { path = new { type = "string" } },
                    required = new[] { "path" }
                })
            },

            // ── High 風險：仍然拒絕 ──
            new Capability
            {
                CapabilityId = "command.execute",
                Route = "run_command",
                ActionType = ActionType.Execute,
                ResourceType = "command",
                RiskLevel = RiskLevel.High,
                ApprovalPolicy = "deny",
                TtlSeconds = 60,
                ParamSchema = ToJson(new
                {
                    type = "object",
                    properties = new
                    {
                        command = new { type = "string" },
                        cwd = new { type = "string" }
                    },
                    required = new[] { "command" }
                })
            }
        };

        foreach (var cap in capabilities)
        {
            var existing = _db.Get<Capability>(cap.CapabilityId);
            if (existing == null)
            {
                _db.Insert(cap);
                continue;
            }

            cap.Version = existing.Version;
            if (CapabilityChanged(existing, cap))
            {
                cap.Version = existing.Version + 1;
                _db.Update(cap);
            }
        }
    }

    // ── 輔助 ──

    private static string ToJson(object value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = false });

    private static bool CapabilityChanged(Capability existing, Capability candidate)
    {
        return existing.Route != candidate.Route ||
               existing.ActionType != candidate.ActionType ||
               existing.ResourceType != candidate.ResourceType ||
               existing.ParamSchema != candidate.ParamSchema ||
               existing.RiskLevel != candidate.RiskLevel ||
               existing.ApprovalPolicy != candidate.ApprovalPolicy ||
               existing.TtlSeconds != candidate.TtlSeconds ||
               existing.Quota != candidate.Quota ||
               existing.AuditLevel != candidate.AuditLevel;
    }

    private void TryExecute(string sql)
    {
        try { _db.Execute(sql); }
        catch { /* 索引已存在等情況忽略 */ }
    }
}
