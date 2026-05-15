using BrokerCore.Data;
using BrokerCore.Models;
using Unit.Tests.Helpers;

namespace Broker.Tests.Endpoints;

/// <summary>
/// AgentInboxEndpoints 用的 SQL contract — 不直接呼 endpoint（要 HTTP infra）、
/// 改測底層 BrokerDb SQL 跟 endpoint handler 用同一句的行為。
///
/// 9 個 BackgroundService agent 全部跑這套（push/pull/complete/list）、
/// SQL 退化會讓所有 agent 同時失效、必須有 regression net。
/// </summary>
public class AgentInboxQueryTests : IDisposable
{
    private readonly BrokerDb _db;
    public AgentInboxQueryTests() { _db = TestDb.CreateInMemory(); }
    public void Dispose() => _db.Dispose();

    private void InsertTask(string agentId, int seq, string status = "pending", string? reply = null)
    {
        _db.Insert(new AgentInboxTask
        {
            TaskId = $"inbox_{Guid.NewGuid():N}"[..20],
            AgentId = agentId,
            Seq = seq,
            Prompt = "{}",
            Status = status,
            Reply = reply,
            RequestedBy = "test",
            CreatedAt = DateTime.UtcNow.AddSeconds(-seq),  // 較新的 seq 在 created_at 較大
        });
    }

    [Fact]
    public void PullSql_ReturnsOldestPendingFirst()
    {
        // FIFO：seq 1 push 先、seq 5 push 後、pull 應該回 seq 1
        InsertTask("agent_x", 5);
        InsertTask("agent_x", 1);
        InsertTask("agent_x", 3);

        var pending = _db.QueryFirst<AgentInboxTask>(
            @"SELECT * FROM agent_inbox_tasks
              WHERE agent_id = @aid AND status = 'pending'
              ORDER BY seq ASC LIMIT 1",
            new { aid = "agent_x" });
        pending.Should().NotBeNull();
        pending!.Seq.Should().Be(1);
    }

    [Fact]
    public void PullSql_ScopedByAgentId_NoLeakAcrossAgents()
    {
        InsertTask("agent_a", 1);
        InsertTask("agent_b", 1);

        var pendingForA = _db.QueryFirst<AgentInboxTask>(
            @"SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid AND status = 'pending' ORDER BY seq ASC LIMIT 1",
            new { aid = "agent_a" });

        pendingForA.Should().NotBeNull();
        pendingForA!.AgentId.Should().Be("agent_a", "pull 必須只看自己的 inbox、不能洩漏到其他 agent");
    }

    [Fact]
    public void PullSql_SkipsProcessingAndDoneAndFailed()
    {
        InsertTask("agent_x", 1, status: "done");
        InsertTask("agent_x", 2, status: "failed");
        InsertTask("agent_x", 3, status: "processing");
        InsertTask("agent_x", 4, status: "pending");

        var pending = _db.QueryFirst<AgentInboxTask>(
            @"SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid AND status = 'pending' ORDER BY seq ASC LIMIT 1",
            new { aid = "agent_x" });

        pending!.Seq.Should().Be(4, "只有 status='pending' 的應該被 pull");
    }

    [Fact]
    public void AtomicMarkProcessing_SecondPullerGetsNothing()
    {
        // 模擬兩個 instance 同時 pull、guarded UPDATE 確保只有一個搶到
        InsertTask("agent_x", 1);
        var pending = _db.QueryFirst<AgentInboxTask>(
            @"SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid AND status = 'pending' ORDER BY seq ASC LIMIT 1",
            new { aid = "agent_x" });

        // First puller marks processing
        var rows1 = _db.Execute(
            "UPDATE agent_inbox_tasks SET status='processing', started_at=@ts WHERE task_id=@tid AND status='pending'",
            new { tid = pending!.TaskId, ts = DateTime.UtcNow });
        rows1.Should().Be(1, "第一個 puller 應該成功 mark processing");

        // Second puller tries same task
        var rows2 = _db.Execute(
            "UPDATE agent_inbox_tasks SET status='processing', started_at=@ts WHERE task_id=@tid AND status='pending'",
            new { tid = pending.TaskId, ts = DateTime.UtcNow });
        rows2.Should().Be(0, "第二個 puller 撞同一筆、guard 條件 status='pending' 不再成立、UPDATE 0 rows");
    }

    [Fact]
    public void CompleteUpdate_PersistsReplyAndStatus()
    {
        InsertTask("agent_x", 1);
        var task = _db.QueryFirst<AgentInboxTask>(
            "SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid LIMIT 1", new { aid = "agent_x" })!;

        task.Status = "done";
        task.Reply = "test reply content";
        task.Model = "gemini-2.5-flash";
        task.LatencyMs = 1234;
        task.CompletedAt = DateTime.UtcNow;
        _db.Update(task);

        var fresh = _db.Get<AgentInboxTask>(task.TaskId);
        fresh.Should().NotBeNull();
        fresh!.Status.Should().Be("done");
        fresh.Reply.Should().Be("test reply content");
        fresh.Model.Should().Be("gemini-2.5-flash");
        fresh.LatencyMs.Should().Be(1234);
        fresh.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void ListSql_ScopedAndOrderedDescBySeq()
    {
        for (int i = 1; i <= 5; i++) InsertTask("agent_x", i);
        InsertTask("agent_y", 99);  // 不該洩漏

        var list = _db.Query<AgentInboxTask>(
            @"SELECT * FROM agent_inbox_tasks
              WHERE agent_id = @aid
              ORDER BY seq DESC LIMIT @lim",
            new { aid = "agent_x", lim = 50 });

        list.Should().HaveCount(5);
        list.All(t => t.AgentId == "agent_x").Should().BeTrue();
        list[0].Seq.Should().Be(5, "newest first");
        list.Last().Seq.Should().Be(1, "oldest last");
    }

    [Fact]
    public void ListSql_RespectsLimit()
    {
        for (int i = 1; i <= 20; i++) InsertTask("agent_x", i);

        var list = _db.Query<AgentInboxTask>(
            @"SELECT * FROM agent_inbox_tasks WHERE agent_id = @aid ORDER BY seq DESC LIMIT @lim",
            new { aid = "agent_x", lim = 7 });

        list.Should().HaveCount(7);
    }

    [Fact]
    public void MaxSeqQuery_ReturnsCurrentHigh()
    {
        // 用於 PushScheduled 算下一個 seq
        InsertTask("agent_x", 1);
        InsertTask("agent_x", 5);
        InsertTask("agent_x", 3);

        // BaseOrm 對 inline DTO 需要 AS alias、跟 endpoint 端用法一致
        var maxSeq = _db.QueryFirst<MaxSeqRow>(
            "SELECT COALESCE(MAX(seq), 0) AS Seq FROM agent_inbox_tasks WHERE agent_id = @aid",
            new { aid = "agent_x" });
        maxSeq.Should().NotBeNull();
        maxSeq!.Seq.Should().Be(5);

        // empty case
        var empty = _db.QueryFirst<MaxSeqRow>(
            "SELECT COALESCE(MAX(seq), 0) AS Seq FROM agent_inbox_tasks WHERE agent_id = @aid",
            new { aid = "agent_nonexistent" });
        empty!.Seq.Should().Be(0);
    }

    private class MaxSeqRow { public int Seq { get; set; } }
}
