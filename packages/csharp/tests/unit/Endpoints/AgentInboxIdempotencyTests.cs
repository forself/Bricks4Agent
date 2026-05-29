using BrokerCore.Data;
using BrokerCore.Models;
using FluentAssertions;
using Unit.Tests.Helpers;

namespace Broker.Tests.Endpoints;

/// <summary>
/// AgentInbox push 冪等契約(2026-05-29, AnthonyLee)。
/// 鎖 idempotency_key 去重 + UNIQUE(agent_id, idempotency_key) index 行為,
/// 跟 /push handler 用同一套 SQL/index(不走 HTTP infra,測底層契約)。
/// </summary>
public class AgentInboxIdempotencyTests : IDisposable
{
    private readonly BrokerDb _db;
    public AgentInboxIdempotencyTests() { _db = TestDb.CreateInMemory(); }
    public void Dispose() => _db.Dispose();

    private AgentInboxTask Make(string agentId, string? idemKey, int seq = 1) => new()
    {
        TaskId = $"inbox_{Guid.NewGuid():N}"[..20],
        AgentId = agentId,
        Seq = seq,
        Prompt = "{}",
        Status = "pending",
        RequestedBy = "test",
        IdempotencyKey = idemKey,
        CreatedAt = DateTime.UtcNow,
    };

    // ── 去重 SELECT(handler 早退用的同一句)──
    [Fact]
    public void DedupSelect_FindsExistingByAgentAndKey()
    {
        _db.Insert(Make("agent_x", "k-123"));
        var dup = _db.QueryFirst<AgentInboxTask>(
            "SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid AND idempotency_key = @k LIMIT 1",
            new { aid = "agent_x", k = "k-123" });
        dup.Should().NotBeNull();
        dup!.IdempotencyKey.Should().Be("k-123");
    }

    // ── UNIQUE index = 並發去重的權威(第二筆同 key 必拋)──
    [Fact]
    public void UniqueIndex_RejectsDuplicateSameAgentAndKey()
    {
        _db.Insert(Make("agent_x", "k-dup"));
        Action second = () => _db.Insert(Make("agent_x", "k-dup", seq: 2));
        second.Should().Throw<Exception>("UNIQUE(agent_id, idempotency_key) 必須擋下同 key 第二筆(並發 race 的權威防線)");
    }

    // ── 向後相容:沒帶 key(NULL)可多筆並存(SQLite NULL 視為相異)──
    [Fact]
    public void NullKey_AllowsMultipleRows()
    {
        _db.Insert(Make("agent_x", null, seq: 1));
        _db.Insert(Make("agent_x", null, seq: 2));
        var n = _db.Query<AgentInboxTask>(
            "SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid", new { aid = "agent_x" });
        n.Should().HaveCount(2, "沒帶 idempotency_key 的請求維持原行為、不被去重");
    }

    // ── 不同 key → 不同任務(別把合法的兩筆誤併)──
    [Fact]
    public void DifferentKey_AllowsDistinctTasks()
    {
        _db.Insert(Make("agent_x", "k-a", seq: 1));
        Action other = () => _db.Insert(Make("agent_x", "k-b", seq: 2));
        other.Should().NotThrow();
    }

    // ── 同 key 但不同 agent → 不衝突(index 含 agent_id、scope 隔離)──
    [Fact]
    public void SameKeyDifferentAgent_NoConflict()
    {
        _db.Insert(Make("agent_a", "shared-key"));
        Action otherAgent = () => _db.Insert(Make("agent_b", "shared-key"));
        otherAgent.Should().NotThrow("UNIQUE 是 (agent_id, idempotency_key) 複合鍵、不同 agent 同 key 合法");
    }
}
