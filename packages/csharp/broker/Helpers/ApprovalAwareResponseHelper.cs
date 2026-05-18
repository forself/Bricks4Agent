using System.Text.Json;
using System.Text.RegularExpressions;
using BrokerCore.Contracts;

namespace Broker.Helpers;

/// <summary>
/// 把 dispatcher 的 ExecutionResult 包成 ASP.NET IResult、特別處理「卡在 approval gate」的情況。
///
/// 為什麼存在：
///   PoolDispatcher 對 trading.order / trading.perpetual::place_order 在 approval gate 拒下、
///   回 `ExecutionResult.Fail(requestId, "Pending admin approval (approval_id=apr_XXX). Retry...")`
///   舊版 endpoint 一律把它當失敗、dashboard 第二次下單會看到同樣字串、誤以為「上一單還在審核」、
///   bot-node tool 也得用 regex 解 error 字串才分得出「真失敗 vs 卡審」。
///
/// 這個 helper 在 endpoint 層攔截、改回結構化：
///   - 卡審 → 200 + `{success:true, message:"pending_approval",
///                       data:{status:"pending_approval", approval_id:"apr_XXX", note:"..."}}`
///     語意：dispatch 本身成功（單已送進審核佇列）、不是失敗。client 看 data.status 走 happy path。
///   - admin rejected → 維持 error（語意明確、admin 已決定 no）。
///   - 其他失敗 → 維持 error。
///   - 真正成功（dispatch 後 worker 回 payload）→ 維持 success + 原 payload。
///
/// 不動 broker-core/ExecutionResult.cs（Benson 原作、加欄位會擴散到所有 IExecutionDispatcher 實作）。
/// 只在 broker endpoint 層攔字串重塑、blast radius 控在這一個 helper。
/// </summary>
public static class ApprovalAwareResponseHelper
{
    /// <summary>
    /// 解 PoolDispatcher 寫的「Pending admin approval (approval_id=apr_XXX). Retry the same trace_id...」
    /// 字串 → 抓 approval_id。寫死字串對應 [PoolDispatcher.cs:149]。
    /// 那邊改字串、這邊 regex 也要跟改、有 test 鎖住格式。
    /// </summary>
    private static readonly Regex PendingApprovalPattern = new(
        @"^Pending admin approval \(approval_id=(?<aid>apr_[A-Za-z0-9_-]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IResult Shape(ExecutionResult result)
    {
        // Happy path：dispatch 真成功、回原 payload
        if (result.Success)
        {
            try
            {
                var data = JsonDocument.Parse(result.ResultPayload ?? "{}");
                return Results.Ok(ApiResponseHelper.Success(data.RootElement));
            }
            catch
            {
                return Results.Ok(ApiResponseHelper.Success(result.ResultPayload ?? "{}"));
            }
        }

        var err = result.ErrorMessage ?? "dispatch failed";

        // 卡 approval gate → 重塑成 success + status="pending_approval"
        var m = PendingApprovalPattern.Match(err);
        if (m.Success)
        {
            var aid = m.Groups["aid"].Value;
            return Results.Ok(ApiResponseHelper.Success(new
            {
                status = "pending_approval",
                approval_id = aid,
                note = "Order has been queued for admin approval. Please review in the admin approvals tab.",
            }, message: "pending_approval"));
        }

        // 其他失敗（含 admin rejected、ACL denied、worker unreachable 等）→ 維持 error
        return Results.Ok(ApiResponseHelper.Error(err));
    }
}
