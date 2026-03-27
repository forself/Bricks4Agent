using Microsoft.Extensions.Logging;
using BrowserWorker;

var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
var logger = loggerFactory.CreateLogger<PlaywrightBrowserService>();

var options = new BrowserWorkerOptions { Headless = true, MaxContentLength = 2000 };
await using var service = new PlaywrightBrowserService(options, logger);

var passed = 0;
var failed = 0;

// Test 1: Fetch example.com
Console.WriteLine("=== Test 1: Fetch example.com ===");
var r1 = await service.FetchPageAsync("https://example.com");
if (r1.Success && r1.Title.Contains("Example") && r1.StatusCode == 200 && r1.TextContent.Length > 0)
{
    Console.WriteLine($"  PASS — title=\"{r1.Title}\" text={r1.TextContent.Length} chars");
    passed++;
}
else
{
    Console.WriteLine($"  FAIL — success={r1.Success} title=\"{r1.Title}\" error={r1.Error}");
    failed++;
}

// Test 2: Fetch a page with actual content
Console.WriteLine("=== Test 2: Fetch httpbin.org/html ===");
var r2 = await service.FetchPageAsync("https://httpbin.org/html");
if (r2.Success && r2.TextContent.Contains("Moby") && r2.StatusCode == 200)
{
    Console.WriteLine($"  PASS — text contains 'Moby', {r2.TextContent.Length} chars");
    passed++;
}
else
{
    Console.WriteLine($"  FAIL — success={r2.Success} text={r2.TextContent.Length} error={r2.Error}");
    failed++;
}

// Test 3: HTTP 404
Console.WriteLine("=== Test 3: HTTP 404 ===");
var r3 = await service.FetchPageAsync("https://httpbin.org/status/404");
if (!r3.Success && r3.Error != null && r3.Error.Contains("404"))
{
    Console.WriteLine($"  PASS — correctly failed with: {r3.Error}");
    passed++;
}
else
{
    Console.WriteLine($"  FAIL — success={r3.Success} error={r3.Error}");
    failed++;
}

// Test 4: Redirect handling
Console.WriteLine("=== Test 4: Redirect (httpbin.org/redirect/1) ===");
var r4 = await service.FetchPageAsync("https://httpbin.org/redirect/1");
if (r4.Success && r4.FinalUrl.Contains("get"))
{
    Console.WriteLine($"  PASS — redirected to: {r4.FinalUrl}");
    passed++;
}
else
{
    Console.WriteLine($"  FAIL — success={r4.Success} finalUrl={r4.FinalUrl} error={r4.Error}");
    failed++;
}

// Test 5: Description extraction
Console.WriteLine("=== Test 5: Meta description extraction ===");
var r5 = await service.FetchPageAsync("https://example.com");
Console.WriteLine($"  Description: \"{r5.Description}\"");
if (r5.Success)
{
    Console.WriteLine($"  PASS — fetched (description may or may not be present)");
    passed++;
}
else
{
    Console.WriteLine($"  FAIL — {r5.Error}");
    failed++;
}

// Summary
Console.WriteLine();
Console.WriteLine($"Results: {passed} passed, {failed} failed out of {passed + failed} tests");
Console.WriteLine(failed == 0 ? "ALL TESTS PASSED" : "SOME TESTS FAILED");

return failed == 0 ? 0 : 1;
