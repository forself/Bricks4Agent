using Broker.Services;

namespace Broker.Tests;

public static class BrowserAndDeployTests
{
    private static int _passed;
    private static int _failed;

    public static (int passed, int failed) Run()
    {
        _passed = 0;
        _failed = 0;

        Console.WriteLine("=== Browser & Deployment Tests ===");
        Console.WriteLine();

        TestHtmlExtractorTitle();
        TestHtmlExtractorDescription();
        TestHtmlExtractorTextStripsNavigation();
        TestHealthCheckRetrySignature();

        Console.WriteLine();
        Console.WriteLine($"=== Browser & Deploy Results: {_passed} passed, {_failed} failed ===");
        return (_passed, _failed);
    }

    private static void TestHtmlExtractorTitle()
    {
        Console.WriteLine("--- HTML Extractor: Title ---");
        var html = "<html><head><title>Test Page</title></head><body>Hello</body></html>";
        AssertEqual("title-basic", BrowserExecutionHtmlExtractor.ExtractTitle(html), "Test Page");

        var htmlEncoded = "<html><head><title>A &amp; B</title></head><body></body></html>";
        AssertEqual("title-decoded", BrowserExecutionHtmlExtractor.ExtractTitle(htmlEncoded), "A & B");

        AssertEqual("title-empty", BrowserExecutionHtmlExtractor.ExtractTitle("<html><body>no title</body></html>"), "");
        Console.WriteLine();
    }

    private static void TestHtmlExtractorDescription()
    {
        Console.WriteLine("--- HTML Extractor: Description ---");

        var html1 = """<html><head><meta name="description" content="This is a test page"></head><body></body></html>""";
        AssertEqual("desc-standard", BrowserExecutionHtmlExtractor.ExtractDescription(html1), "This is a test page");

        var html2 = """<html><head><meta content="Reversed order" name="description"></head><body></body></html>""";
        AssertEqual("desc-reversed", BrowserExecutionHtmlExtractor.ExtractDescription(html2), "Reversed order");

        AssertEqual("desc-missing", BrowserExecutionHtmlExtractor.ExtractDescription("<html><head></head><body></body></html>"), "");
        Console.WriteLine();
    }

    private static void TestHtmlExtractorTextStripsNavigation()
    {
        Console.WriteLine("--- HTML Extractor: Text strips nav/header/footer ---");

        var html = "<html><body><nav>Menu Item 1 | Menu Item 2</nav><main><p>Main content here</p></main><footer>Copyright 2026</footer></body></html>";
        var text = BrowserExecutionHtmlExtractor.ExtractText(html);

        AssertContains("text-has-main", text, "Main content here");
        AssertNotContains("text-no-nav", text, "Menu Item");
        AssertNotContains("text-no-footer", text, "Copyright");
        Console.WriteLine();
    }

    private static void TestHealthCheckRetrySignature()
    {
        Console.WriteLine("--- Health Check: AttemptCount field exists ---");
        var result = AzureIisDeploymentHealthCheckResult.Skipped();
        AssertTrue("health-skipped-attempt-0", result.AttemptCount == 0);
        AssertTrue("health-skipped-not-attempted", !result.Attempted);

        var result2 = new AzureIisDeploymentHealthCheckResult
        {
            Attempted = true,
            Success = true,
            AttemptCount = 2
        };
        AssertTrue("health-attempt-count", result2.AttemptCount == 2);
        Console.WriteLine();
    }

    private static void AssertTrue(string name, bool condition)
    {
        if (condition) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}"); _failed++; }
    }

    private static void AssertEqual(string name, string actual, string expected)
    {
        if (actual == expected) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected \"{expected}\", got \"{actual}\""); _failed++; }
    }

    private static void AssertContains(string name, string actual, string expected)
    {
        if (actual.Contains(expected)) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected to contain \"{expected}\""); _failed++; }
    }

    private static void AssertNotContains(string name, string actual, string notExpected)
    {
        if (!actual.Contains(notExpected)) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected NOT to contain \"{notExpected}\""); _failed++; }
    }
}
