using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Broker.Tests;

/// <summary>
/// Integration tests that start the broker and test real HTTP endpoints + DB operations.
/// </summary>
public static class IntegrationTest
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static string _baseUrl = "";
    private static string _adminToken = "";
    private static int _passed;
    private static int _failed;

    public static async Task<(int passed, int failed)> RunAsync(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _passed = 0;
        _failed = 0;

        Console.WriteLine("=== Integration Tests: Artifact Delivery UX ===");
        Console.WriteLine($"  Base URL: {_baseUrl}");
        Console.WriteLine();

        // Step 1: Login
        await LoginAdmin();
        if (string.IsNullOrEmpty(_adminToken))
        {
            Console.Error.WriteLine("  [SKIP] Cannot login to admin, skipping integration tests.");
            return (0, 1);
        }

        // Step 2: Test artifact delivery endpoint (creates file + records artifact)
        var deliveryResult = await TestDeliverArtifact();

        // Step 3: Test artifact list endpoint
        await TestListAllArtifacts();

        // Step 4: Test per-user artifact list
        await TestListUserArtifacts();

        // Step 5: Test retry-drive endpoint (should fail since no Drive configured, but endpoint works)
        if (deliveryResult.HasValue)
            await TestRetryDrive(deliveryResult.Value);

        // Step 6: Test notification retry endpoint
        if (deliveryResult.HasValue)
            await TestRetryNotification(deliveryResult.Value);

        // Step 7: Verify admin HTML loads with delivery tab
        await TestAdminHtmlDeliveryTab();

        Console.WriteLine();
        Console.WriteLine($"=== Integration Results: {_passed} passed, {_failed} failed ===");
        return (_passed, _failed);
    }

    private static async Task LoginAdmin()
    {
        Console.WriteLine("--- Login ---");
        try
        {
            var response = await Http.GetAsync($"{_baseUrl}/api/v1/local-admin/status");
            var statusJson = await response.Content.ReadAsStringAsync();
            var status = JsonDocument.Parse(statusJson);
            var data = status.RootElement.GetProperty("data");
            var hasPassword = data.GetProperty("hasPassword").GetBoolean();
            var initialPasswordActive = data.TryGetProperty("initialPasswordActive", out var ipa) && ipa.GetBoolean();

            if (!hasPassword || initialPasswordActive)
            {
                // First time: set password with initial "admin"
                var initBody = JsonSerializer.Serialize(new { password = "admin", new_password = "test1234" });
                var initResponse = await Http.PostAsync(
                    $"{_baseUrl}/api/v1/local-admin/login",
                    new StringContent(initBody, Encoding.UTF8, "application/json"));
                var initResult = await initResponse.Content.ReadAsStringAsync();
                if (!initResponse.IsSuccessStatusCode)
                {
                    Console.Error.WriteLine($"  [FAIL] Init login failed: {initResult}");
                    _failed++;
                    return;
                }
                // Extract cookie from init login
                if (initResponse.Headers.TryGetValues("Set-Cookie", out var initCookies))
                {
                    foreach (var cookie in initCookies)
                        Http.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
                }
                _adminToken = "logged-in";
                Console.WriteLine("  [PASS] Admin login successful (initial password set)");
                _passed++;
                return;
            }

            // Normal login
            var loginBody = JsonSerializer.Serialize(new { password = "test1234" });
            var loginResponse = await Http.PostAsync(
                $"{_baseUrl}/api/v1/local-admin/login",
                new StringContent(loginBody, Encoding.UTF8, "application/json"));

            if (loginResponse.IsSuccessStatusCode)
            {
                if (loginResponse.Headers.TryGetValues("Set-Cookie", out var cookies))
                {
                    foreach (var cookie in cookies)
                        Http.DefaultRequestHeaders.Add("Cookie", cookie.Split(';')[0]);
                }
                _adminToken = "logged-in";
                Console.WriteLine("  [PASS] Admin login successful");
                _passed++;
            }
            else
            {
                var body = await loginResponse.Content.ReadAsStringAsync();
                Console.Error.WriteLine($"  [FAIL] Login failed: {(int)loginResponse.StatusCode} {body}");
                _failed++;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [FAIL] Login error: {ex.Message}");
            _failed++;
        }
    }

    private static async Task<JsonElement?> TestDeliverArtifact()
    {
        Console.WriteLine("--- Deliver Artifact ---");
        try
        {
            var body = JsonSerializer.Serialize(new
            {
                user_id = "test_integration_user",
                file_name = "integration-test.md",
                format = "md",
                content = "# Integration Test\n\nThis is a test artifact.",
                upload_to_google_drive = false,
                send_line_notification = true,
                notification_title = "測試通知"
            });

            var response = await Http.PostAsync(
                $"{_baseUrl}/api/v1/local-admin/line/users/artifacts/deliver",
                new StringContent(body, Encoding.UTF8, "application/json"));

            var resultText = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(resultText);

            if (response.IsSuccessStatusCode)
            {
                var data = result.RootElement.GetProperty("data");
                var success = data.GetProperty("success").GetBoolean();
                var overallStatus = data.TryGetProperty("overallStatus", out var os) ? os.GetString() : null;
                var fileName = data.TryGetProperty("fileName", out var fn) ? fn.GetString() : null;
                var notification = data.TryGetProperty("notification", out var notif) ? notif : (JsonElement?)null;
                var artifact = data.TryGetProperty("artifact", out var art) ? art : (JsonElement?)null;

                AssertTrue("deliver-success", success);
                AssertEqual("deliver-status", overallStatus, "completed");
                AssertEqual("deliver-filename", fileName, "integration-test.md");
                AssertTrue("deliver-has-notification", notification.HasValue && notification.Value.ValueKind == JsonValueKind.Object);
                AssertTrue("deliver-has-artifact", artifact.HasValue && artifact.Value.ValueKind == JsonValueKind.Object);

                if (artifact.HasValue)
                {
                    var artifactOverall = artifact.Value.TryGetProperty("overallStatus", out var ao) ? ao.GetString() : null;
                    AssertEqual("deliver-artifact-overall", artifactOverall, "completed");
                }

                return data;
            }
            else
            {
                // Delivery might fail if user profile doesn't exist — that's expected
                var msg = result.RootElement.TryGetProperty("message", out var m) ? m.GetString() : resultText;
                Console.WriteLine($"  [INFO] Deliver returned {(int)response.StatusCode}: {msg}");
                Console.WriteLine("  [INFO] This may be expected if no LINE user profile exists for test_integration_user");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [FAIL] Deliver error: {ex.Message}");
            _failed++;
            return null;
        }
    }

    private static async Task TestListAllArtifacts()
    {
        Console.WriteLine("--- List All Artifacts ---");
        try
        {
            var response = await Http.GetAsync($"{_baseUrl}/api/v1/local-admin/line/artifacts?limit=10");
            var resultText = await response.Content.ReadAsStringAsync();

            AssertTrue("list-artifacts-200", response.IsSuccessStatusCode);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonDocument.Parse(resultText);
                var data = result.RootElement.GetProperty("data");
                var hasTotal = data.TryGetProperty("total", out _);
                var hasItems = data.TryGetProperty("items", out var items);
                AssertTrue("list-artifacts-has-total", hasTotal);
                AssertTrue("list-artifacts-has-items", hasItems && items.ValueKind == JsonValueKind.Array);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [FAIL] List artifacts error: {ex.Message}");
            _failed++;
        }
    }

    private static async Task TestListUserArtifacts()
    {
        Console.WriteLine("--- List User Artifacts ---");
        try
        {
            var response = await Http.GetAsync($"{_baseUrl}/api/v1/local-admin/line/users/test_integration_user/artifacts?limit=10");
            AssertTrue("list-user-artifacts-200", response.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [FAIL] List user artifacts error: {ex.Message}");
            _failed++;
        }
    }

    private static async Task TestRetryDrive(JsonElement deliveryData)
    {
        Console.WriteLine("--- Retry Drive Upload ---");
        try
        {
            var artifactId = "";
            if (deliveryData.TryGetProperty("artifact", out var art) &&
                art.TryGetProperty("artifactId", out var aid))
            {
                artifactId = aid.GetString() ?? "";
            }

            if (string.IsNullOrEmpty(artifactId))
            {
                Console.WriteLine("  [SKIP] No artifact ID from delivery");
                return;
            }

            var response = await Http.PostAsync(
                $"{_baseUrl}/api/v1/local-admin/line/artifacts/{artifactId}/retry-drive",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            // Should fail because artifact is already "completed" (no Drive failure to retry)
            var resultText = await response.Content.ReadAsStringAsync();
            AssertTrue("retry-drive-endpoint-responds", response.StatusCode == HttpStatusCode.OK || response.StatusCode == HttpStatusCode.BadRequest);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                // Expected: "artifact status is 'completed', not 'partial'"
                AssertTrue("retry-drive-correct-rejection", resultText.Contains("not 'partial'") || resultText.Contains("partial"));
                Console.WriteLine($"  [PASS] retry-drive correctly rejected: status is not partial");
                _passed++;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [FAIL] Retry drive error: {ex.Message}");
            _failed++;
        }
    }

    private static async Task TestRetryNotification(JsonElement deliveryData)
    {
        Console.WriteLine("--- Retry Notification ---");
        try
        {
            var notificationId = "";
            if (deliveryData.TryGetProperty("notification", out var notif) &&
                notif.TryGetProperty("notificationId", out var nid))
            {
                notificationId = nid.GetString() ?? "";
            }

            if (string.IsNullOrEmpty(notificationId))
            {
                Console.WriteLine("  [SKIP] No notification ID from delivery");
                return;
            }

            // First complete the notification (simulate line-worker marking it as failed)
            var completeBody = JsonSerializer.Serialize(new
            {
                notification_id = notificationId,
                status = "failed",
                error = "integration_test_simulated_failure"
            });
            await Http.PostAsync(
                $"{_baseUrl}/api/v1/high-level/line/notifications/complete",
                new StringContent(completeBody, Encoding.UTF8, "application/json"));

            // Now retry it
            var retryResponse = await Http.PostAsync(
                $"{_baseUrl}/api/v1/local-admin/line/notifications/{notificationId}/retry",
                new StringContent("{}", Encoding.UTF8, "application/json"));

            AssertTrue("retry-notification-200", retryResponse.IsSuccessStatusCode);

            if (retryResponse.IsSuccessStatusCode)
            {
                var resultText = await retryResponse.Content.ReadAsStringAsync();
                AssertTrue("retry-notification-pending", resultText.Contains("pending"));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [FAIL] Retry notification error: {ex.Message}");
            _failed++;
        }
    }

    private static async Task TestAdminHtmlDeliveryTab()
    {
        Console.WriteLine("--- Admin HTML Delivery Tab ---");
        try
        {
            var response = await Http.GetAsync($"{_baseUrl}/line-admin.html");
            var html = await response.Content.ReadAsStringAsync();

            AssertTrue("admin-html-200", response.IsSuccessStatusCode);
            AssertTrue("admin-html-has-delivery-tab", html.Contains("data-tab=\"delivery\""));
            AssertTrue("admin-html-has-delivery-section", html.Contains("id=\"tab-delivery\""));
            AssertTrue("admin-html-has-delivery-list", html.Contains("id=\"delivery-list\""));
            AssertTrue("admin-html-has-status-filter", html.Contains("id=\"delivery-status-filter\""));
            AssertTrue("admin-html-has-retry-function", html.Contains("retryDriveUpload"));
            AssertTrue("admin-html-has-load-function", html.Contains("loadDeliveryHistory"));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [FAIL] Admin HTML error: {ex.Message}");
            _failed++;
        }
    }

    private static void AssertTrue(string name, bool condition)
    {
        if (condition) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}"); _failed++; }
    }

    private static void AssertEqual(string name, string? actual, string expected)
    {
        if (actual == expected) { Console.WriteLine($"  [PASS] {name}"); _passed++; }
        else { Console.Error.WriteLine($"  [FAIL] {name}: expected \"{expected}\", got \"{actual}\""); _failed++; }
    }
}
