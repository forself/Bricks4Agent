using Broker.Services;
using BrokerCore.Contracts;

// Make HighLevelCoordinator.BuildArtifactReply accessible
using static Broker.Services.HighLevelCoordinator;

Console.WriteLine("=== Broker.Tests: Artifact Delivery UX ===");
Console.WriteLine();

var passed = 0;
var failed = 0;

// ---------- Test 1: Drive 成功時的通知格式 ----------
{
    var driveResult = new GoogleDriveShareResult
    {
        Success = true,
        WebViewLink = "https://drive.google.com/file/d/ABC123/view",
        DownloadLink = "https://drive.google.com/uc?export=download&id=ABC123"
    };

    var body = LineArtifactDeliveryService.BuildNotificationBody("report.md", "/path/report.md", driveResult);

    AssertContains("Test1-title", body, "您的文件已準備完成");
    AssertContains("Test1-filename", body, "文件名稱：report.md");
    AssertContains("Test1-download-label", body, "點擊下載：");
    AssertContains("Test1-download-link", body, "https://drive.google.com/uc?export=download&id=ABC123");
    AssertContains("Test1-preview-label", body, "線上預覽：");
    AssertContains("Test1-preview-link", body, "https://drive.google.com/file/d/ABC123/view");
    AssertNotContains("Test1-no-local-path", body, "本機路徑");
    AssertNotContains("Test1-no-raw-path", body, "/path/report.md");

    Console.WriteLine($"  Test 1 body output:");
    Console.WriteLine("  ---");
    foreach (var line in body.Split(Environment.NewLine))
        Console.WriteLine($"  | {line}");
    Console.WriteLine("  ---");
    Console.WriteLine();
}

// ---------- Test 2: Drive 失敗時的通知格式 ----------
{
    var body = LineArtifactDeliveryService.BuildNotificationBody("data.csv", "/path/data.csv", null);

    AssertContains("Test2-title", body, "您的文件已準備完成");
    AssertContains("Test2-filename", body, "文件名稱：data.csv");
    AssertContains("Test2-fallback", body, "雲端上傳未完成");
    AssertContains("Test2-admin-help", body, "管理員將協助");
    AssertNotContains("Test2-no-local-path", body, "本機路徑");
    AssertNotContains("Test2-no-drive-link", body, "drive.google.com");

    Console.WriteLine($"  Test 2 body output:");
    Console.WriteLine("  ---");
    foreach (var line in body.Split(Environment.NewLine))
        Console.WriteLine($"  | {line}");
    Console.WriteLine("  ---");
    Console.WriteLine();
}

// ---------- Test 3: Drive 成功但只有 WebViewLink（無 DownloadLink）----------
{
    var driveResult = new GoogleDriveShareResult
    {
        Success = true,
        WebViewLink = "https://drive.google.com/file/d/XYZ/view",
        DownloadLink = ""
    };

    var body = LineArtifactDeliveryService.BuildNotificationBody("notes.txt", "/p/notes.txt", driveResult);

    AssertContains("Test3-title", body, "您的文件已準備完成");
    AssertContains("Test3-preview", body, "線上預覽：");
    AssertNotContains("Test3-no-download-section", body, "點擊下載：");

    Console.WriteLine($"  Test 3 body output:");
    Console.WriteLine("  ---");
    foreach (var line in body.Split(Environment.NewLine))
        Console.WriteLine($"  | {line}");
    Console.WriteLine("  ---");
    Console.WriteLine();
}

// ---------- Test 4: Drive Success=false ----------
{
    var driveResult = new GoogleDriveShareResult
    {
        Success = false,
        Message = "quota_exceeded"
    };

    var body = LineArtifactDeliveryService.BuildNotificationBody("big.json", "/p/big.json", driveResult);

    AssertContains("Test4-title", body, "您的文件已準備完成");
    AssertContains("Test4-fallback", body, "雲端上傳未完成");
    AssertNotContains("Test4-no-drive-link", body, "drive.google.com");

    Console.WriteLine($"  Test 4 body output:");
    Console.WriteLine("  ---");
    foreach (var line in body.Split(Environment.NewLine))
        Console.WriteLine($"  | {line}");
    Console.WriteLine("  ---");
    Console.WriteLine();
}

// ---------- Test 5: BuildArtifactReply — Drive 成功 ----------
{
    Console.WriteLine("--- BuildArtifactReply Tests ---");
    var result = new HighLevelDocumentArtifactResult
    {
        Success = true,
        FileName = "report.md",
        Delivery = new LineArtifactDeliveryResult
        {
            Success = true,
            FilePath = "/path/report.md",
            GoogleDrive = new GoogleDriveShareResult
            {
                Success = true,
                WebViewLink = "https://drive.google.com/file/d/X/view",
                DownloadLink = "https://drive.google.com/uc?export=download&id=X"
            }
        }
    };

    var reply = HighLevelCoordinator.BuildArtifactReply(result);
    AssertContains("Test5-user-friendly", reply, "report.md");
    AssertContains("Test5-mentions-download", reply, "下載連結");
    AssertNotContains("Test5-no-artifact-path", reply, "artifact_path");
    AssertNotContains("Test5-no-artifact-file", reply, "artifact_file:");
    AssertNotContains("Test5-no-raw-path", reply, "/path/report.md");
    Console.WriteLine($"  Reply: {reply}");
    Console.WriteLine();
}

// ---------- Test 6: BuildArtifactReply — Drive 失敗 ----------
{
    var result = new HighLevelDocumentArtifactResult
    {
        Success = true,
        FileName = "data.csv",
        Delivery = new LineArtifactDeliveryResult
        {
            Success = true,
            FilePath = "/path/data.csv",
            GoogleDrive = new GoogleDriveShareResult { Success = false }
        }
    };

    var reply = HighLevelCoordinator.BuildArtifactReply(result);
    AssertContains("Test6-mentions-file", reply, "data.csv");
    AssertContains("Test6-mentions-upload-issue", reply, "雲端上傳未完成");
    AssertNotContains("Test6-no-technical-ids", reply, "artifact_delivery");
    Console.WriteLine($"  Reply: {reply}");
    Console.WriteLine();
}

// ---------- Test 7: BuildArtifactReply — 生成失敗 ----------
{
    var result = new HighLevelDocumentArtifactResult
    {
        Success = false,
        Message = "llm_timeout",
        FileName = "fail.md",
        Delivery = new LineArtifactDeliveryResult { Success = false, FilePath = "/p/f" }
    };

    var reply = HighLevelCoordinator.BuildArtifactReply(result);
    AssertContains("Test7-failure-message", reply, "文件生成失敗");
    AssertContains("Test7-reason", reply, "llm_timeout");
    AssertNotContains("Test7-no-artifact-path", reply, "artifact_path");
    Console.WriteLine($"  Reply: {reply}");
    Console.WriteLine();
}

// ---------- Test 8: BuildArtifactReply — no delivery ----------
{
    var result = new HighLevelDocumentArtifactResult
    {
        Success = true,
        FileName = "test.md",
        Delivery = null
    };

    var reply = HighLevelCoordinator.BuildArtifactReply(result);
    AssertContains("Test8-success-no-delivery", reply, "下載連結");
    Console.WriteLine($"  Reply: {reply}");
    Console.WriteLine();
}

// ---------- Summary (Unit Tests) ----------
Console.WriteLine($"=== Unit Test Results: {passed} passed, {failed} failed ===");
if (failed > 0)
{
    Console.Error.WriteLine($"FAILED: {failed} unit test assertion(s) did not pass.");
    Environment.Exit(1);
}
Console.WriteLine("UNIT TESTS ALL PASSED.");
Console.WriteLine();

// ---------- Integration Tests ----------
if (args.Length > 0 && args[0] == "--integration")
{
    var brokerUrl = args.Length > 1 ? args[1] : "http://localhost:5000";
    var (iPassed, iFailed) = await Broker.Tests.IntegrationTest.RunAsync(brokerUrl);
    passed += iPassed;
    failed += iFailed;

    Console.WriteLine();
    Console.WriteLine($"=== Total Results: {passed} passed, {failed} failed ===");
    if (failed > 0)
    {
        Console.Error.WriteLine($"FAILED: {failed} total assertion(s) did not pass.");
        Environment.Exit(1);
    }
    Console.WriteLine("ALL TESTS PASSED.");
}
else
{
    Console.WriteLine("Tip: Run with --integration [broker-url] for end-to-end tests.");
}

void AssertContains(string name, string actual, string expected)
{
    if (actual.Contains(expected))
    {
        Console.WriteLine($"  [PASS] {name}");
        passed++;
    }
    else
    {
        Console.Error.WriteLine($"  [FAIL] {name}: expected to contain \"{expected}\"");
        Console.Error.WriteLine($"         actual: \"{actual[..Math.Min(200, actual.Length)]}\"");
        failed++;
    }
}

void AssertNotContains(string name, string actual, string notExpected)
{
    if (!actual.Contains(notExpected))
    {
        Console.WriteLine($"  [PASS] {name}");
        passed++;
    }
    else
    {
        Console.Error.WriteLine($"  [FAIL] {name}: expected NOT to contain \"{notExpected}\"");
        failed++;
    }
}
