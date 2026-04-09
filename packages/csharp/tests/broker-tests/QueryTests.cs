using Broker.Services;

namespace Broker.Tests;

public static class QueryTests
{
    private static int _passed;
    private static int _failed;

    public static (int passed, int failed) Run()
    {
        _passed = 0;
        _failed = 0;

        Console.WriteLine("=== Query Quality Tests ===");
        Console.WriteLine();

        TestIsReasonableAdministrativeTerm();
        TestBuildTransportReplyEmpty();
        TestBuildTdxTransportReplyEmpty();
        TestBuildSearchReplyEmpty();

        Console.WriteLine();
        Console.WriteLine($"=== Query Test Results: {_passed} passed, {_failed} failed ===");
        return (_passed, _failed);
    }

    private static void TestIsReasonableAdministrativeTerm()
    {
        Console.WriteLine("--- IsReasonableAdministrativeTerm ---");

        // Should accept: standard admin names
        AssertTrue("term-taipei", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("台北市"));
        AssertTrue("term-xinyi", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("信義區"));
        AssertTrue("term-taichung-city", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("臺中市"));

        // Should accept: longer names with admin suffix (was filtered before at >5)
        AssertTrue("term-6char-with-suffix", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("臺北市信義區"));
        AssertTrue("term-7char-with-suffix", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("新北市板橋區"));

        // Should accept: English names
        AssertTrue("term-english", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("Taipei City"));
        AssertTrue("term-english-district", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("Xinyi District"));

        // Should reject: too long CJK without suffix
        AssertFalse("term-too-long-no-suffix", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("這是一個超級長的名稱但沒有行政區後綴"));

        // Should reject: contains particles
        AssertFalse("term-with-de", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("台北的"));
        AssertFalse("term-with-comma", HighLevelRelationQueryService.IsReasonableAdministrativeTerm("台北，台中"));

        // Should reject: empty
        AssertFalse("term-empty", HighLevelRelationQueryService.IsReasonableAdministrativeTerm(""));
        AssertFalse("term-null", HighLevelRelationQueryService.IsReasonableAdministrativeTerm(null!));

        Console.WriteLine();
    }

    private static void TestBuildTransportReplyEmpty()
    {
        Console.WriteLine("--- BuildTransportReply (empty results) ---");

        var reply = HighLevelQueryToolMediator.BuildTransportReply(
            "rail", "台北 台中", Array.Empty<HighLevelQuerySearchResult>(), "", "");

        AssertContains("transport-empty-no-results", reply, "\u6c92\u6709\u627e\u5230\u4efb\u4f55\u53ef\u7528\u7d50\u679c");
        AssertContains("transport-empty-has-example", reply, "?rail");
        AssertContains("transport-empty-has-format", reply, "台北 台中");

        Console.WriteLine($"  Reply preview: {reply[..Math.Min(100, reply.Length)]}...");
        Console.WriteLine();
    }

    private static void TestBuildTdxTransportReplyEmpty()
    {
        Console.WriteLine("--- BuildTdxTimetableReply (empty buses) ---");

        using var doc = System.Text.Json.JsonDocument.Parse(
            """
            {
              "origin": "臺北市",
              "destination": "307",
              "date": "2026-04-08",
              "bus_count": 0,
              "buses": []
            }
            """);

        var reply = HighLevelQueryToolMediator.BuildTdxTimetableReply(
            "bus",
            "臺北市 307",
            doc.RootElement,
            "TDX 公車預估到站 API",
            "2026-04-08T10:00:00+08:00");

        AssertContains("tdx-empty-bus-source", reply, "TDX 公車預估到站 API");
        AssertContains("tdx-empty-bus-no-results", reply, "目前沒有取得可用結果");
        AssertContains("tdx-empty-bus-query", reply, "臺北市");

        Console.WriteLine($"  Reply preview: {reply[..Math.Min(100, reply.Length)]}...");
        Console.WriteLine();
    }

    private static void TestBuildSearchReplyEmpty()
    {
        Console.WriteLine("--- BuildSearchReply (empty results) ---");

        var reply = HighLevelQueryToolMediator.BuildSearchReply(
            "duckduckgo", "test query", Array.Empty<HighLevelQuerySearchResult>());

        AssertContains("search-empty-no-results", reply, "\u6c92\u6709\u627e\u5230\u4efb\u4f55\u53ef\u7528\u7d50\u679c");
        AssertContains("search-empty-tip-keywords", reply, "\u8abf\u6574\u95dc\u9375\u8a5e");
        AssertContains("search-empty-tip-english", reply, "英文搜尋");

        Console.WriteLine($"  Reply preview: {reply[..Math.Min(100, reply.Length)]}...");
        Console.WriteLine();
    }

    private static void AssertTrue(string name, bool condition)
    {
        if (condition) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected true"); _failed++; }
    }

    private static void AssertFalse(string name, bool condition)
    {
        if (!condition) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected false"); _failed++; }
    }

    private static void AssertContains(string name, string actual, string expected)
    {
        if (actual.Contains(expected)) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected to contain \"{expected}\""); _failed++; }
    }
}
