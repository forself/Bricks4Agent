using Broker.Services;
using BrokerCore.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace Broker.Tests;

/// <summary>
/// §18.2-C2 使用者審批連結 token:roundtrip / 竄改 / 過期 / 換密鑰。
/// </summary>
public static class ApprovalLinkTests
{
    private static int _passed;
    private static int _failed;

    private static ApprovalLinkService NewSvc(string secret = "approval-link-test-secret-0123456789")
        => new(new BrokerArtifactDownloadOptions { SigningSecret = secret });

    public static (int passed, int failed) Run()
    {
        _passed = 0;
        _failed = 0;

        Console.WriteLine("=== Approval Link Token Tests (§18.2-C2) ===");
        Console.WriteLine();

        var now = DateTimeOffset.UtcNow;
        var svc = NewSvc();

        var token = svc.CreateToken("U_user1", now);
        AssertTrue("token-created", token != null);
        AssertEqual("token-roundtrip", svc.Validate(token, now), "U_user1");

        AssertTrue("tampered-sig-null", svc.Validate(token + "ff", now) == null);
        AssertTrue("garbage-null", svc.Validate("not-a-token", now) == null);
        AssertTrue("empty-null", svc.Validate("", now) == null);

        var expired = svc.CreateToken("U_user1", now.AddHours(-1));
        AssertTrue("expired-null", svc.Validate(expired, now) == null);

        AssertTrue("wrong-secret-null", NewSvc("a-totally-different-secret-987654321").Validate(token, now) == null);
        AssertTrue("empty-secret-no-token", NewSvc("").CreateToken("U", now) == null);

        TestNotifierBuildsLink();

        Console.WriteLine();
        Console.WriteLine($"=== Approval Link Results: {_passed} passed, {_failed} failed ===");
        return (_passed, _failed);
    }

    private static void TestNotifierBuildsLink()
    {
        var tunnelFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tunnelFile, "https://test.example.ngrok.io");
            var options = new BrokerArtifactDownloadOptions
            {
                SigningSecret = "secret-0123456789-abcdefgh",
                SidecarLastTunnelUrlPath = tunnelFile
            };
            var link = new ApprovalLinkService(options);
            var url = new SidecarPublicUrlResolver(options);
            var notifier = new LineApprovalNotifier(link, url, null!, NullLogger<LineApprovalNotifier>.Instance);
            var approval = new ApprovalRequest
            {
                OwnerPrincipalId = "U_user1",
                ApproverTier = ApproverTier.User,
                CapabilityId = "file.write",
                Reason = "confirm"
            };

            var built = notifier.TryBuildLink(approval);
            AssertTrue("notifier-link-not-null", built != null);
            AssertTrue("notifier-link-has-page", built != null && built.Contains("/user-approvals.html#token="));
            AssertTrue("notifier-link-has-base", built != null && built.StartsWith("https://test.example.ngrok.io"));

            var token = built!.Substring(built.IndexOf("token=", StringComparison.Ordinal) + 6);
            AssertEqual("notifier-token-resolves-owner",
                link.Validate(Uri.UnescapeDataString(token), DateTimeOffset.UtcNow), "U_user1");

            var noUrl = new SidecarPublicUrlResolver(new BrokerArtifactDownloadOptions
            {
                SigningSecret = options.SigningSecret,
                SidecarLastTunnelUrlPath = tunnelFile + ".missing"
            });
            AssertTrue("notifier-no-url-null",
                new LineApprovalNotifier(link, noUrl, null!, NullLogger<LineApprovalNotifier>.Instance).TryBuildLink(approval) == null);
        }
        finally { try { File.Delete(tunnelFile); } catch { /* best-effort */ } }
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
}
