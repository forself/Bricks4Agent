using BrokerCore;
using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Tests;

/// <summary>
/// §18.2-B 審批生命週期:RequireApproval 擱置的執行請求 → 管理員核准(dispatch)/駁回(deny)。
/// 直接驗證 BrokerService 的 ListPendingApprovals / ApproveExecutionAsync / RejectExecution。
/// </summary>
public static class ApprovalLifecycleTests
{
    private static int _passed;
    private static int _failed;

    /// <summary>假 dispatcher:任何核准的請求都回成功。</summary>
    private sealed class OkDispatcher : IExecutionDispatcher
    {
        public Task<ExecutionResult> DispatchAsync(ApprovedRequest r)
            => Task.FromResult(ExecutionResult.Ok(r.RequestId, "{\"ok\":true}"));
    }

    public static (int passed, int failed) Run()
    {
        _passed = 0;
        _failed = 0;

        Console.WriteLine("=== Approval Lifecycle Tests (§18.2-B) ===");
        Console.WriteLine();

        TestApproveDispatchesHeldRequest();
        TestRejectDeniesHeldRequest();
        TestApproveUnknownReturnsNull();

        Console.WriteLine();
        Console.WriteLine($"=== Approval Lifecycle Results: {_passed} passed, {_failed} failed ===");
        return (_passed, _failed);
    }

    private static BrokerService NewBroker(BrokerDb db)
    {
        var catalog = new CapabilityCatalog(db);
        var audit = new AuditService(db);
        var revocation = new RevocationService(db);
        var session = new SessionService(db);
        var router = new TaskRouter();
        var policy = new PolicyEngine(new SchemaValidator(), new PolicyEngineOptions());
        return new BrokerService(db, policy, audit, catalog, session, revocation, router, new OkDispatcher());
    }

    // 擱置一個待審請求(模擬 PolicyEngine 已回 RequireApproval 後的狀態)
    private static string SeedPending(BrokerDb db, string capabilityId)
    {
        new CapabilityCatalog(db).CreateGrant(
            "task_a", "ses_a", "prn_a", capabilityId, "{}", -1, DateTime.UtcNow.AddHours(1));

        var req = new ExecutionRequest
        {
            RequestId = IdGen.New("req"),
            TaskId = "task_a",
            SessionId = "ses_a",
            PrincipalId = "prn_a",
            CapabilityId = capabilityId,
            Intent = "x",
            RequestPayload = "{\"route\":\"read_file\",\"args\":{}}",
            ExecutionState = ExecutionState.PendingApproval,
            TraceId = "tr_" + IdGen.New("t"),
            IdempotencyKey = IdGen.New("idem"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Insert(req);

        var apr = new ApprovalRequest
        {
            ApprovalId = IdGen.New("apr"),
            RequestId = req.RequestId,
            TaskId = "task_a",
            SessionId = "ses_a",
            PrincipalId = "prn_a",
            CapabilityId = capabilityId,
            Reason = "risk requires approval",
            Status = ApprovalStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            TraceId = req.TraceId
        };
        db.Insert(apr);
        return apr.ApprovalId;
    }

    private static void TestApproveDispatchesHeldRequest()
    {
        Console.WriteLine("--- Approve dispatches the held request ---");
        var dir = NewTempDir("approve");
        try
        {
            using var db = new BrokerDb($"Data Source={Path.Combine(dir, "broker.db")}");
            new BrokerDbInitializer(db).Initialize();
            var broker = NewBroker(db);
            var approvalId = SeedPending(db, "file.read");

            AssertEqual("pending-listed-before", broker.ListPendingApprovals().Count.ToString(), "1");

            var result = broker.ApproveExecutionAsync(approvalId, "admin_1", "looks fine").GetAwaiter().GetResult();

            AssertTrue("approve-returns-request", result != null);
            AssertEqual("approve-state-succeeded", result?.ExecutionState.ToString(), "Succeeded");

            var apr = db.Get<ApprovalRequest>(approvalId);
            AssertEqual("approval-status-approved", apr?.Status.ToString(), "Approved");
            AssertEqual("approval-approver-recorded", apr?.ApproverId, "admin_1");
            AssertEqual("pending-empty-after", broker.ListPendingApprovals().Count.ToString(), "0");
        }
        finally { TryDelete(dir); }
    }

    private static void TestRejectDeniesHeldRequest()
    {
        Console.WriteLine("--- Reject denies the held request ---");
        var dir = NewTempDir("reject");
        try
        {
            using var db = new BrokerDb($"Data Source={Path.Combine(dir, "broker.db")}");
            new BrokerDbInitializer(db).Initialize();
            var broker = NewBroker(db);
            var approvalId = SeedPending(db, "file.read");

            var result = broker.RejectExecution(approvalId, "admin_1", "not allowed");

            AssertTrue("reject-returns-request", result != null);
            AssertEqual("reject-state-denied", result?.ExecutionState.ToString(), "Denied");

            var apr = db.Get<ApprovalRequest>(approvalId);
            AssertEqual("approval-status-rejected", apr?.Status.ToString(), "Rejected");
            AssertEqual("pending-empty-after-reject", broker.ListPendingApprovals().Count.ToString(), "0");
        }
        finally { TryDelete(dir); }
    }

    private static void TestApproveUnknownReturnsNull()
    {
        Console.WriteLine("--- Approve/Reject of unknown approval returns null ---");
        var dir = NewTempDir("unknown");
        try
        {
            using var db = new BrokerDb($"Data Source={Path.Combine(dir, "broker.db")}");
            new BrokerDbInitializer(db).Initialize();
            var broker = NewBroker(db);

            var approve = broker.ApproveExecutionAsync("apr_does_not_exist", "admin_1", "x").GetAwaiter().GetResult();
            AssertTrue("approve-unknown-null", approve == null);

            var reject = broker.RejectExecution("apr_does_not_exist", "admin_1", "x");
            AssertTrue("reject-unknown-null", reject == null);
        }
        finally { TryDelete(dir); }
    }

    private static string NewTempDir(string label)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"b4a-approval-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void AssertTrue(string name, bool condition)
    {
        if (condition) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected true"); _failed++; }
    }

    private static void AssertEqual(string name, string? actual, string? expected)
    {
        if (actual == expected) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected \"{expected}\", got \"{actual}\""); _failed++; }
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* best-effort */ }
    }
}
