using BrokerCore.Models;
using System.Text.Json;

namespace BrokerCore.Data;

/// <summary>
/// 資料庫初始化 —— EnsureTable + 種子資料
/// 啟動時呼叫，確保 13 張表存在並植入 Phase 1 種子
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

    /// <summary>建立 13 張表</summary>
    private void EnsureTables()
    {
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

        // 額外建立複合唯一約束（EnsureTable 不自動建立）
        CreateUniqueConstraints();
    }

    /// <summary>建立 EnsureTable 無法自動產生的唯一約束</summary>
    private void CreateUniqueConstraints()
    {
        // ExecutionRequest: (task_id, idempotency_key) 防重複執行
        TryExecute(@"CREATE UNIQUE INDEX IF NOT EXISTS idx_execution_requests_idempotency
                      ON execution_requests(task_id, idempotency_key)");

        // AuditEvent: (trace_id, trace_seq) per-trace chain 序列化
        TryExecute(@"CREATE UNIQUE INDEX IF NOT EXISTS idx_audit_events_trace_seq
                      ON audit_events(trace_id, trace_seq)");
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
                    properties = new { path = new { type = "string" } },
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
                    properties = new { path = new { type = "string" } },
                    required = new[] { "path" }
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
                        path = new { type = "string" },
                        pattern = new { type = "string" }
                    },
                    required = new[] { "path", "pattern" }
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
                        path = new { type = "string" },
                        pattern = new { type = "string" }
                    },
                    required = new[] { "path", "pattern" }
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
                        content = new { type = "string" }
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
                        working_directory = new { type = "string" }
                    },
                    required = new[] { "command" }
                })
            }
        };

        foreach (var cap in capabilities)
        {
            if (_db.Get<Capability>(cap.CapabilityId) == null)
                _db.Insert(cap);
        }
    }

    // ── 輔助 ──

    private static string ToJson(object value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = false });

    private void TryExecute(string sql)
    {
        try { _db.Execute(sql); }
        catch { /* 索引已存在等情況忽略 */ }
    }
}
