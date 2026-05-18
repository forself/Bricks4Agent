using System.Text.Json;
using Broker.Helpers;
using BrokerCore.Contracts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Unit.Tests.Helpers;

/// <summary>
/// 鎖住 ApprovalAwareResponseHelper.Shape 對四種 ExecutionResult 的 reshape 契約：
///   1. dispatch 真成功 → success + 原 payload（透傳）
///   2. ApprovalGate 卡住（"Pending admin approval (approval_id=apr_..."）→ success + status=pending_approval + approval_id
///   3. ApprovalGate 拒絕（"Approval rejected by..."）→ 維持 error（admin 已決定 no、不該重塑成 success）
///   4. 其他失敗（worker timeout 等）→ 維持 error
///
/// 這條 reshape 是 dashboard 「上一單還在審核」誤判 bug 的根治：
/// PoolDispatcher.cs:149 字串改 → 這 helper 的 regex 也要跟改、test 鎖死訊息格式。
/// </summary>
public class ApprovalAwareResponseHelperTests
{
    private static (int statusCode, object? value) Extract(IResult result)
    {
        // Results.Ok(...) 在 minimal API 回傳 Ok&lt;TValue&gt; — 用反射拆出 StatusCode + Value
        var t = result.GetType();
        var status = (int?)t.GetProperty("StatusCode")?.GetValue(result) ?? 0;
        var value = t.GetProperty("Value")?.GetValue(result);
        return (status, value);
    }

    // ASP.NET Core minimal API 預設用 camelCase serialize（JsonSerializerDefaults.Web）。
    // 直接呼 JsonSerializer 不指定 options 會用 PascalCase、跟 dashboard / bot 收到的不一致、
    // test 就失準。對齊 web defaults 才能驗 "client 看到的鍵名"。
    private static JsonElement ToElement(object? value)
        => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public void Shape_DispatchSuccess_PassesThroughPayload()
    {
        var er = ExecutionResult.Ok("req_1", "{\"order_id\":\"ord_abc\",\"status\":\"filled\"}");
        var shaped = ApprovalAwareResponseHelper.Shape(er);
        var (sc, val) = Extract(shaped);
        sc.Should().Be(200);

        var json = ToElement(val);
        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("data").GetProperty("order_id").GetString().Should().Be("ord_abc");
        json.GetProperty("data").GetProperty("status").GetString().Should().Be("filled");
    }

    [Fact]
    public void Shape_PendingApproval_ReshapesToSuccessWithStatus()
    {
        // PoolDispatcher.cs:149 寫死的訊息格式
        var er = ExecutionResult.Fail("req_2",
            "Pending admin approval (approval_id=apr_abc123XYZ). Retry the same trace_id after approval.");
        var shaped = ApprovalAwareResponseHelper.Shape(er);
        var (sc, val) = Extract(shaped);
        sc.Should().Be(200);

        var json = ToElement(val);
        json.GetProperty("success").GetBoolean().Should().BeTrue("pending 對 caller 是『已送出待審』、不是失敗");
        json.GetProperty("message").GetString().Should().Be("pending_approval");

        var data = json.GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("pending_approval");
        data.GetProperty("approval_id").GetString().Should().Be("apr_abc123XYZ");
        data.GetProperty("note").GetString().Should().Contain("admin");
    }

    [Fact]
    public void Shape_RejectedByAdmin_RemainsError()
    {
        // PoolDispatcher rejected case 的訊息格式（見 PoolDispatcher.cs:140）
        var er = ExecutionResult.Fail("req_3", "Approval rejected by admin1: too risky");
        var shaped = ApprovalAwareResponseHelper.Shape(er);
        var (sc, val) = Extract(shaped);
        sc.Should().Be(200);

        var json = ToElement(val);
        json.GetProperty("success").GetBoolean().Should().BeFalse("admin 已 reject、語意明確、不該 reshape 成 success");
        json.GetProperty("message").GetString().Should().Contain("rejected");
    }

    [Fact]
    public void Shape_OtherFailure_RemainsError()
    {
        var er = ExecutionResult.Fail("req_4", "trading-worker not connected");
        var shaped = ApprovalAwareResponseHelper.Shape(er);
        var (sc, val) = Extract(shaped);
        sc.Should().Be(200);

        var json = ToElement(val);
        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("message").GetString().Should().Contain("trading-worker");
    }

    [Fact]
    public void Shape_PendingApproval_RegexIsAnchored_NotFooledByPrefix()
    {
        // 防 false positive：error 字串前面有別的內容、不該被當成 pending（regex ^ anchored）
        var er = ExecutionResult.Fail("req_5",
            "Worker rejected: Pending admin approval (approval_id=apr_fake) was a prior failure");
        var shaped = ApprovalAwareResponseHelper.Shape(er);
        var (sc, val) = Extract(shaped);
        var json = ToElement(val);
        json.GetProperty("success").GetBoolean().Should()
            .BeFalse("regex anchored 在開頭、prefix 內容應該維持 error 路徑");
    }

    [Fact]
    public void Shape_PendingApproval_NullPayload_StillSuccess()
    {
        // ExecutionResult.Ok 允許 null payload — defensive、helper 不該 crash
        var er = ExecutionResult.Ok("req_6", null!);
        var shaped = ApprovalAwareResponseHelper.Shape(er);
        var (sc, _) = Extract(shaped);
        sc.Should().Be(200);
    }
}
