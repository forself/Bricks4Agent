using System.Text.Json;
using Broker.Services;

namespace Unit.Tests.Services;

/// <summary>
/// H3 — ApprovalTemplateMatcher payload 匹配器測試。
///
/// 邊界跟坑：
/// - dot-path 取值：args.symbol、args.nested.deep
/// - 運算子：$eq / $lte / $lt / $gte / $gt / $in
/// - 缺欄位 / 型別錯 → 回 false（不丟例外、不誤判命中）
/// - 字串比對 case-insensitive（"BUY" == "buy"）
///
/// 不測 DB 查詢路徑（FindMatch）— 那部分要 BrokerDb integration test。
/// 純測 PayloadMatches() 純函式，足以鎖定運算子語意。
/// </summary>
public class ApprovalTemplateMatcherTests
{
    private static JsonElement P(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void EmptyMatch_AlwaysTrue()
    {
        ApprovalTemplateMatcher.PayloadMatches(P("""{"args":{"symbol":"BTC"}}"""), "{}").Should().BeTrue();
        ApprovalTemplateMatcher.PayloadMatches(P("{}"), "").Should().BeTrue();
    }

    [Fact]
    public void StringEquality_CaseInsensitive()
    {
        var payload = P("""{"args":{"side":"BUY"}}""");
        ApprovalTemplateMatcher.PayloadMatches(payload, """{"args.side":"buy"}""").Should().BeTrue();
        ApprovalTemplateMatcher.PayloadMatches(payload, """{"args.side":"sell"}""").Should().BeFalse();
    }

    [Fact]
    public void NumberEquality_DecimalCompare()
    {
        var payload = P("""{"args":{"qty":0.05}}""");
        ApprovalTemplateMatcher.PayloadMatches(payload, """{"args.qty":0.05}""").Should().BeTrue();
        ApprovalTemplateMatcher.PayloadMatches(payload, """{"args.qty":0.06}""").Should().BeFalse();
    }

    [Fact]
    public void Lte_AllowsEqualAndLess()
    {
        var p = P("""{"args":{"qty":0.01}}""");
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.qty":{"$lte":0.01}}""").Should().BeTrue();
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.qty":{"$lte":0.02}}""").Should().BeTrue();
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.qty":{"$lte":0.005}}""").Should().BeFalse();
    }

    [Fact]
    public void Lt_StrictlyLess()
    {
        var p = P("""{"args":{"qty":0.01}}""");
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.qty":{"$lt":0.01}}""").Should().BeFalse();
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.qty":{"$lt":0.02}}""").Should().BeTrue();
    }

    [Fact]
    public void Gte_And_Gt()
    {
        var p = P("""{"args":{"leverage":5}}""");
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.leverage":{"$gte":5}}""").Should().BeTrue();
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.leverage":{"$gt":5}}""").Should().BeFalse();
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.leverage":{"$gt":4}}""").Should().BeTrue();
    }

    [Fact]
    public void In_StringSet_CaseInsensitive()
    {
        var p = P("""{"args":{"side":"BUY"}}""");
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.side":{"$in":["buy","sell"]}}""").Should().BeTrue();
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.side":{"$in":["short","long"]}}""").Should().BeFalse();
    }

    [Fact]
    public void MultipleConditions_AndLogic()
    {
        var p = P("""{"args":{"symbol":"BTC-USDT","qty":0.01,"side":"buy"}}""");
        var match = """{"args.symbol":"BTC-USDT","args.qty":{"$lte":0.01},"args.side":{"$in":["buy"]}}""";
        ApprovalTemplateMatcher.PayloadMatches(p, match).Should().BeTrue();

        // 任一條 fail 整體 fail
        var match2 = """{"args.symbol":"BTC-USDT","args.qty":{"$lte":0.001}}""";
        ApprovalTemplateMatcher.PayloadMatches(p, match2).Should().BeFalse();
    }

    [Fact]
    public void CombinedOperator_AndOnSameField()
    {
        var p = P("""{"args":{"leverage":5}}""");
        // leverage in (0, 5]
        var m = """{"args.leverage":{"$gt":0,"$lte":5}}""";
        ApprovalTemplateMatcher.PayloadMatches(p, m).Should().BeTrue();

        var p2 = P("""{"args":{"leverage":10}}""");
        ApprovalTemplateMatcher.PayloadMatches(p2, m).Should().BeFalse();
    }

    [Fact]
    public void NestedDotPath_Resolves()
    {
        var p = P("""{"args":{"order":{"symbol":"ETH","quantity":2.5}}}""");
        var m = """{"args.order.quantity":{"$lte":3}}""";
        ApprovalTemplateMatcher.PayloadMatches(p, m).Should().BeTrue();
    }

    [Fact]
    public void MissingField_DoesNotMatch_ReturnsFalse_NoThrow()
    {
        var p = P("""{"args":{"symbol":"BTC"}}""");
        // args.qty 不存在 → 條件失敗、不 throw
        var act = () => ApprovalTemplateMatcher.PayloadMatches(p, """{"args.qty":{"$lte":1}}""");
        act.Should().NotThrow();
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.qty":{"$lte":1}}""").Should().BeFalse();
    }

    [Fact]
    public void TypeMismatch_StringExpectedButNumber_NoThrow_False()
    {
        var p = P("""{"args":{"qty":0.05}}""");
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.qty":"0.05"}""").Should().BeFalse();
    }

    [Fact]
    public void MalformedMatchJson_ReturnsFalse()
    {
        var p = P("""{"args":{"x":1}}""");
        ApprovalTemplateMatcher.PayloadMatches(p, "not json at all").Should().BeFalse();
    }

    [Fact]
    public void RootObject_NotObject_ReturnsFalse()
    {
        var p = P("""{"args":{"x":1}}""");
        // 根層必須是 object
        ApprovalTemplateMatcher.PayloadMatches(p, """[1,2,3]""").Should().BeFalse();
    }

    [Fact]
    public void In_WithNumbers()
    {
        var p = P("""{"args":{"leverage":5}}""");
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.leverage":{"$in":[1,3,5,10]}}""").Should().BeTrue();
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.leverage":{"$in":[2,4,6]}}""").Should().BeFalse();
    }

    [Fact]
    public void EqOperator_ExplicitForm()
    {
        var p = P("""{"args":{"market":"spot"}}""");
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.market":{"$eq":"spot"}}""").Should().BeTrue();
        ApprovalTemplateMatcher.PayloadMatches(p, """{"args.market":{"$eq":"perp"}}""").Should().BeFalse();
    }
}
