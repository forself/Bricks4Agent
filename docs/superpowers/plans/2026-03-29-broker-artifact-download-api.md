# Broker Artifact Download API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a broker-owned signed artifact download API that can serve local artifacts through short-lived anonymous links and use that link as the LINE fallback path when Google Drive delivery fails.

**Architecture:** Keep artifact persistence where it already lives and add one narrow broker-side download service that (1) resolves the active public broker base URL from current sidecar/ngrok state, (2) signs and validates public artifact download URLs, and (3) streams a recorded artifact file through a new public endpoint. Update the artifact delivery service so Drive remains the primary path, while broker download links are only used for user-facing fallback when Drive upload fails and broker public download is available.

**Tech Stack:** C# /.NET 8 minimal API, existing broker services/endpoints, SQLite-backed `SharedContextEntry` artifact records, PowerShell sidecar/ngrok state, broker verify suite in `packages/csharp/broker/verify/Program.cs`

---

## File Map

### Create

- `packages/csharp/broker/Services/BrokerArtifactDownloadOptions.cs`
  - Dedicated configuration object for download signing secret, TTL, and optional sidecar public URL source settings.
- `packages/csharp/broker/Services/SidecarPublicUrlResolver.cs`
  - Focused resolver that reads the current sidecar/ngrok public URL from known sidecar state and returns a safe broker base URL.
- `packages/csharp/broker/Services/BrokerArtifactDownloadService.cs`
  - Generates signed URLs, validates signatures/expiry, sanitizes file names, resolves artifact files, and exposes a small download lookup model for the endpoint.
- `packages/csharp/broker/Endpoints/ArtifactDownloadEndpoints.cs`
  - Public anonymous GET download endpoint using the service above.

### Modify

- `packages/csharp/broker/Program.cs`
  - Register the new options/services and map the new endpoint.
- `packages/csharp/broker/appsettings.json`
  - Add default `ArtifactDownload` section with non-secret placeholders and safe defaults.
- `packages/csharp/broker/appsettings.Development.example.json`
  - Add development example values for the new settings.
- `packages/csharp/broker/Services/LineArtifactDeliveryService.cs`
  - Inject the new broker download service and replace the current Drive-failure notification fallback with a broker download link when available, without exposing internal paths.
- `packages/csharp/broker/verify/Program.cs`
  - Add TDD coverage for signed URL generation, download validation, expiry/signature/file-not-found cases, and notification path-leak prevention.

### Reuse Without Structural Change

- `packages/csharp/broker/Services/HighLevelLineWorkspaceService.cs`
  - Reuse `ReadArtifactById` and existing artifact records as the source of truth.
- `packages/csharp/broker/Services/HighLevelLineArtifactRecord.cs`
  - Reuse current persisted fields; do not add a new artifact table for v1.

---

### Task 1: Add Download Options and Public URL Resolution

**Files:**
- Create: `packages/csharp/broker/Services/BrokerArtifactDownloadOptions.cs`
- Create: `packages/csharp/broker/Services/SidecarPublicUrlResolver.cs`
- Modify: `packages/csharp/broker/Program.cs`
- Modify: `packages/csharp/broker/appsettings.json`
- Modify: `packages/csharp/broker/appsettings.Development.example.json`
- Test: `packages/csharp/broker/verify/Program.cs`

- [ ] **Step 1: Write the failing verify tests for option defaults and ngrok URL resolution**

Add verify coverage in `packages/csharp/broker/verify/Program.cs` for:

```csharp
var downloadOptions = new BrokerArtifactDownloadOptions();
AssertTrue(downloadOptions.LinkTtlMinutes == 60, "artifact download defaults to one-hour ttl");
AssertTrue(downloadOptions.AllowRepeatedDownloads, "artifact download defaults to repeated downloads");

var sidecarStateRoot = Path.Combine(tempRoot, "line-worker");
Directory.CreateDirectory(sidecarStateRoot);
var lastUrlPath = Path.Combine(sidecarStateRoot, ".last-tunnel-url");
await File.WriteAllTextAsync(lastUrlPath, "https://example-sidecar.ngrok-free.dev/webhook/line/", new UTF8Encoding(false));

var resolver = new SidecarPublicUrlResolver(
    new BrokerArtifactDownloadOptions
    {
        SidecarLastTunnelUrlPath = lastUrlPath
    });

var publicBaseUrl = resolver.TryGetPublicBaseUrl();
AssertTrue(publicBaseUrl == "https://example-sidecar.ngrok-free.dev", "sidecar public url resolver strips webhook suffix");
```

- [ ] **Step 2: Run verify to confirm the new tests fail for the expected missing types**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- FAIL because `BrokerArtifactDownloadOptions` and `SidecarPublicUrlResolver` do not exist yet

- [ ] **Step 3: Add the minimal options class**

Create `packages/csharp/broker/Services/BrokerArtifactDownloadOptions.cs`:

```csharp
namespace Broker.Services;

public sealed class BrokerArtifactDownloadOptions
{
    public string SigningSecret { get; set; } = string.Empty;
    public int LinkTtlMinutes { get; set; } = 60;
    public bool AllowRepeatedDownloads { get; set; } = true;
    public string SidecarLastTunnelUrlPath { get; set; } = ".\\packages\\csharp\\workers\\line-worker\\.last-tunnel-url";
}
```

- [ ] **Step 4: Add the minimal sidecar public URL resolver**

Create `packages/csharp/broker/Services/SidecarPublicUrlResolver.cs`:

```csharp
namespace Broker.Services;

public sealed class SidecarPublicUrlResolver
{
    private readonly BrokerArtifactDownloadOptions _options;

    public SidecarPublicUrlResolver(BrokerArtifactDownloadOptions options)
    {
        _options = options;
    }

    public string? TryGetPublicBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(_options.SidecarLastTunnelUrlPath))
            return null;

        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(_options.SidecarLastTunnelUrlPath));
        if (!File.Exists(path))
            return null;

        var raw = File.ReadAllText(path, new UTF8Encoding(false)).Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return null;

        var builder = new UriBuilder(uri)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri.ToString().TrimEnd('/');
    }
}
```

- [ ] **Step 5: Register the options and resolver in broker startup**

Modify `packages/csharp/broker/Program.cs`:

```csharp
var artifactDownloadOptions = builder.Configuration.GetSection("ArtifactDownload").Get<Broker.Services.BrokerArtifactDownloadOptions>()
    ?? new Broker.Services.BrokerArtifactDownloadOptions();
builder.Services.AddSingleton(artifactDownloadOptions);
builder.Services.AddSingleton<Broker.Services.SidecarPublicUrlResolver>();
```

- [ ] **Step 6: Add configuration sections**

Modify `packages/csharp/broker/appsettings.json`:

```json
"ArtifactDownload": {
  "SigningSecret": "",
  "LinkTtlMinutes": 60,
  "AllowRepeatedDownloads": true,
  "SidecarLastTunnelUrlPath": ".\\packages\\csharp\\workers\\line-worker\\.last-tunnel-url"
},
```

Modify `packages/csharp/broker/appsettings.Development.example.json`:

```json
"ArtifactDownload": {
  "SigningSecret": "REPLACE_WITH_DEVELOPMENT_DOWNLOAD_SIGNING_SECRET",
  "LinkTtlMinutes": 60,
  "AllowRepeatedDownloads": true,
  "SidecarLastTunnelUrlPath": ".\\packages\\csharp\\workers\\line-worker\\.last-tunnel-url"
},
```

- [ ] **Step 7: Run verify to confirm the resolver tests pass**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- PASS for the new option default and public URL resolver assertions
- overall suite still fails later because the signed download service and endpoint do not exist yet

- [ ] **Step 8: Commit**

```powershell
git add packages/csharp/broker/Services/BrokerArtifactDownloadOptions.cs packages/csharp/broker/Services/SidecarPublicUrlResolver.cs packages/csharp/broker/Program.cs packages/csharp/broker/appsettings.json packages/csharp/broker/appsettings.Development.example.json packages/csharp/broker/verify/Program.cs
git commit -m "feat: add broker artifact download config and public url resolver"
```

### Task 2: Implement Signed Artifact Download Service

**Files:**
- Create: `packages/csharp/broker/Services/BrokerArtifactDownloadService.cs`
- Modify: `packages/csharp/broker/Program.cs`
- Test: `packages/csharp/broker/verify/Program.cs`

- [ ] **Step 1: Write the failing verify tests for signed URL generation and validation**

Add verify coverage in `packages/csharp/broker/verify/Program.cs` for:

```csharp
var artifactFilePath = Path.Combine(tempRoot, "artifact.txt");
await File.WriteAllTextAsync(artifactFilePath, "download-bytes", new UTF8Encoding(false));

var workspace = new HighLevelLineWorkspaceService(coordinatorDb, new HighLevelCoordinatorOptions
{
    AccessRoot = tempRoot
});

var recorded = workspace.RecordArtifact(new HighLevelLineArtifactRecord
{
    UserId = "download-user",
    FileName = "artifact.txt",
    Format = "txt",
    FilePath = artifactFilePath,
    DocumentsRoot = tempRoot,
    OverallStatus = "completed",
    Success = true
});

var service = new BrokerArtifactDownloadService(
    workspace,
    new SidecarPublicUrlResolver(new BrokerArtifactDownloadOptions
    {
        SigningSecret = "verify-signing-secret",
        LinkTtlMinutes = 60,
        SidecarLastTunnelUrlPath = lastUrlPath
    }),
    new BrokerArtifactDownloadOptions
    {
        SigningSecret = "verify-signing-secret",
        LinkTtlMinutes = 60
    });

var signed = service.CreateSignedDownloadUrl(recorded.ArtifactId);
AssertTrue(signed is not null && signed.Contains("/api/v1/artifacts/download/", StringComparison.Ordinal), "download service creates signed artifact url");

var resolved = service.ValidateAndResolve(recorded.ArtifactId, signedExp, signedSig, DateTimeOffset.UtcNow);
AssertTrue(resolved?.Artifact.ArtifactId == recorded.ArtifactId, "download service validates signed artifact request");
AssertTrue(!resolved!.FilePath.Contains("managed-workspaces", StringComparison.OrdinalIgnoreCase), "resolved download model does not expose internal path to caller");
```

- [ ] **Step 2: Run verify to confirm the tests fail because the service is missing**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- FAIL because `BrokerArtifactDownloadService` and its methods do not exist yet

- [ ] **Step 3: Implement the minimal download service**

Create `packages/csharp/broker/Services/BrokerArtifactDownloadService.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Broker.Services;

public sealed class BrokerArtifactDownloadService
{
    private readonly HighLevelLineWorkspaceService _workspace;
    private readonly SidecarPublicUrlResolver _publicUrlResolver;
    private readonly BrokerArtifactDownloadOptions _options;

    public BrokerArtifactDownloadService(
        HighLevelLineWorkspaceService workspace,
        SidecarPublicUrlResolver publicUrlResolver,
        BrokerArtifactDownloadOptions options)
    {
        _workspace = workspace;
        _publicUrlResolver = publicUrlResolver;
        _options = options;
    }

    public string? CreateSignedDownloadUrl(string artifactId, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(_options.SigningSecret))
            return null;

        var artifact = _workspace.ReadArtifactById(artifactId);
        if (artifact == null || string.IsNullOrWhiteSpace(artifact.FilePath) || !File.Exists(artifact.FilePath))
            return null;

        var publicBaseUrl = _publicUrlResolver.TryGetPublicBaseUrl();
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
            return null;

        var issuedAt = now ?? DateTimeOffset.UtcNow;
        var exp = issuedAt.AddMinutes(_options.LinkTtlMinutes <= 0 ? 60 : _options.LinkTtlMinutes).ToUnixTimeSeconds();
        var sig = ComputeSignature(artifact.ArtifactId, artifact.FileName, exp);
        return $"{publicBaseUrl}/api/v1/artifacts/download/{Uri.EscapeDataString(artifact.ArtifactId)}?exp={exp}&sig={Uri.EscapeDataString(sig)}";
    }

    public BrokerArtifactDownloadResolution? ValidateAndResolve(string artifactId, long exp, string sig, DateTimeOffset now)
    {
        if (now.ToUnixTimeSeconds() > exp)
            return BrokerArtifactDownloadResolution.Expired();

        var artifact = _workspace.ReadArtifactById(artifactId);
        if (artifact == null || string.IsNullOrWhiteSpace(artifact.FilePath) || !File.Exists(artifact.FilePath))
            return BrokerArtifactDownloadResolution.NotFound();

        var expected = ComputeSignature(artifact.ArtifactId, artifact.FileName, exp);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(sig ?? string.Empty)))
            return BrokerArtifactDownloadResolution.Invalid();

        return BrokerArtifactDownloadResolution.Success(
            artifact,
            artifact.FilePath,
            SanitizeFileName(artifact.FileName));
    }

    private string ComputeSignature(string artifactId, string fileName, long exp)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.SigningSecret));
        var payload = $"{artifactId}\n{fileName}\n{exp}";
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static string SanitizeFileName(string fileName)
    {
        var safe = string.IsNullOrWhiteSpace(fileName) ? "artifact.bin" : fileName.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            safe = safe.Replace(invalid, '_');
        return safe;
    }
}

public sealed class BrokerArtifactDownloadResolution
{
    public bool IsValid { get; init; }
    public bool IsExpired { get; init; }
    public bool IsMissing { get; init; }
    public HighLevelLineArtifactRecord? Artifact { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string SafeFileName { get; init; } = string.Empty;

    public static BrokerArtifactDownloadResolution Invalid() => new();
    public static BrokerArtifactDownloadResolution Expired() => new() { IsExpired = true };
    public static BrokerArtifactDownloadResolution NotFound() => new() { IsMissing = true };
    public static BrokerArtifactDownloadResolution Success(HighLevelLineArtifactRecord artifact, string filePath, string safeFileName)
        => new() { IsValid = true, Artifact = artifact, FilePath = filePath, SafeFileName = safeFileName };
}
```

- [ ] **Step 4: Register the new service**

Modify `packages/csharp/broker/Program.cs`:

```csharp
builder.Services.AddSingleton<Broker.Services.BrokerArtifactDownloadService>();
```

- [ ] **Step 5: Add verify coverage for invalid signature, expiry, and missing file**

Extend `packages/csharp/broker/verify/Program.cs` with assertions like:

```csharp
AssertTrue(service.ValidateAndResolve(recorded.ArtifactId, signedExp, "bad-signature", DateTimeOffset.UtcNow)!.IsValid == false, "download service rejects invalid signature");
AssertTrue(service.ValidateAndResolve(recorded.ArtifactId, signedExp, signedSig, DateTimeOffset.FromUnixTimeSeconds(signedExp + 1))!.IsExpired, "download service rejects expired link");
File.Delete(artifactFilePath);
AssertTrue(service.ValidateAndResolve(recorded.ArtifactId, signedExp, signedSig, DateTimeOffset.UtcNow)!.IsMissing, "download service rejects missing artifact file");
```

- [ ] **Step 6: Run verify to confirm service-level tests pass**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- PASS for URL generation and validation assertions
- overall suite still fails later because the public endpoint and notification fallback are not wired yet

- [ ] **Step 7: Commit**

```powershell
git add packages/csharp/broker/Services/BrokerArtifactDownloadService.cs packages/csharp/broker/Program.cs packages/csharp/broker/verify/Program.cs
git commit -m "feat: add signed broker artifact download service"
```

### Task 3: Expose Public Download Endpoint

**Files:**
- Create: `packages/csharp/broker/Endpoints/ArtifactDownloadEndpoints.cs`
- Modify: `packages/csharp/broker/Program.cs`
- Test: `packages/csharp/broker/verify/Program.cs`

- [ ] **Step 1: Write the failing endpoint tests**

Add endpoint-level verify coverage in `packages/csharp/broker/verify/Program.cs` using the existing in-process broker host pattern:

```csharp
using var response = await httpClient.GetAsync($"/api/v1/artifacts/download/{recorded.ArtifactId}?exp={signedExp}&sig={signedSig}");
AssertTrue(response.IsSuccessStatusCode, "artifact download endpoint serves signed request");
AssertTrue(response.Content.Headers.ContentDisposition?.FileName?.Contains("artifact.txt", StringComparison.OrdinalIgnoreCase) == true, "artifact download endpoint uses safe file name");
AssertTrue((await response.Content.ReadAsStringAsync()) == "download-bytes", "artifact download endpoint returns artifact bytes");
```

Add failure assertions:

```csharp
AssertTrue((int)(await httpClient.GetAsync($"/api/v1/artifacts/download/{recorded.ArtifactId}?exp={signedExp}&sig=bad")).StatusCode == 403, "artifact download endpoint rejects invalid signature");
AssertTrue((int)(await httpClient.GetAsync($"/api/v1/artifacts/download/{recorded.ArtifactId}?exp={expiredExp}&sig={expiredSig}")).StatusCode == 410, "artifact download endpoint rejects expired link");
AssertTrue((int)(await httpClient.GetAsync($"/api/v1/artifacts/download/missing-artifact?exp={signedExp}&sig={signedSig}")).StatusCode == 404, "artifact download endpoint rejects missing artifact");
```

- [ ] **Step 2: Run verify to confirm the endpoint tests fail because the route is missing**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- FAIL with 404 or missing route assertions for `/api/v1/artifacts/download/...`

- [ ] **Step 3: Add the public endpoint**

Create `packages/csharp/broker/Endpoints/ArtifactDownloadEndpoints.cs`:

```csharp
namespace Broker.Endpoints;

public static class ArtifactDownloadEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/artifacts/download/{artifactId}", async (
            string artifactId,
            HttpContext ctx,
            BrokerArtifactDownloadService service,
            CancellationToken cancellationToken) =>
        {
            var expRaw = ctx.Request.Query["exp"].ToString();
            var sig = ctx.Request.Query["sig"].ToString();
            if (!long.TryParse(expRaw, out var exp) || string.IsNullOrWhiteSpace(sig))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var resolved = service.ValidateAndResolve(artifactId, exp, sig, DateTimeOffset.UtcNow);
            if (resolved == null || (!resolved.IsValid && !resolved.IsExpired && !resolved.IsMissing))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (resolved.IsExpired)
                return Results.StatusCode(StatusCodes.Status410Gone);
            if (resolved.IsMissing || string.IsNullOrWhiteSpace(resolved.FilePath))
                return Results.NotFound();

            var stream = File.OpenRead(resolved.FilePath);
            return Results.File(
                stream,
                contentType: "application/octet-stream",
                fileDownloadName: resolved.SafeFileName,
                enableRangeProcessing: false);
        });
    }
}
```

- [ ] **Step 4: Map the endpoint in startup**

Modify `packages/csharp/broker/Program.cs` near other API mappings:

```csharp
var api = app.MapGroup("/api/v1");
ArtifactDownloadEndpoints.Map(api);
```

- [ ] **Step 5: Run verify to confirm endpoint tests pass**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- PASS for valid download, invalid signature, expired link, and missing artifact endpoint assertions
- suite may still fail on notification fallback until Task 4 is complete

- [ ] **Step 6: Commit**

```powershell
git add packages/csharp/broker/Endpoints/ArtifactDownloadEndpoints.cs packages/csharp/broker/Program.cs packages/csharp/broker/verify/Program.cs
git commit -m "feat: expose signed broker artifact download endpoint"
```

### Task 4: Switch Drive Failure Fallback to Broker Download Links

**Files:**
- Modify: `packages/csharp/broker/Services/LineArtifactDeliveryService.cs`
- Modify: `packages/csharp/broker/verify/Program.cs`
- Test: `packages/csharp/broker/verify/Program.cs`

- [ ] **Step 1: Write the failing tests for notification fallback behavior and path leak prevention**

Add verify coverage in `packages/csharp/broker/verify/Program.cs` for:

```csharp
var failedDriveResult = new GoogleDriveShareResult
{
    Success = false,
    Message = "google_drive_upload_failed"
};

var fallbackBody = LineArtifactDeliveryService.BuildNotificationBody(
    "artifact.txt",
    artifactFilePath,
    failedDriveResult,
    "https://example-sidecar.ngrok-free.dev/api/v1/artifacts/download/artifact_verify?exp=111&sig=abc");

AssertTrue(fallbackBody.Contains("https://example-sidecar.ngrok-free.dev/api/v1/artifacts/download/", StringComparison.Ordinal), "notification fallback uses broker link when available");
AssertTrue(!fallbackBody.Contains(artifactFilePath, StringComparison.OrdinalIgnoreCase), "notification fallback never exposes internal path");

var degradedBody = LineArtifactDeliveryService.BuildNotificationBody(
    "artifact.txt",
    artifactFilePath,
    failedDriveResult,
    null);

AssertTrue(!degradedBody.Contains(artifactFilePath, StringComparison.OrdinalIgnoreCase), "degraded fallback also avoids exposing internal path");
```

- [ ] **Step 2: Run verify to confirm the tests fail against the current notification builder**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- FAIL because the current `BuildNotificationBody` still emits the local file path and does not accept a broker URL parameter

- [ ] **Step 3: Inject the broker download service into artifact delivery**

Modify the constructor in `packages/csharp/broker/Services/LineArtifactDeliveryService.cs`:

```csharp
private readonly BrokerArtifactDownloadService _brokerArtifactDownloadService;

public LineArtifactDeliveryService(
    HighLevelLineWorkspaceService workspaceService,
    GoogleDriveShareService googleDriveShareService,
    BrokerArtifactDownloadService brokerArtifactDownloadService,
    ILogger<LineArtifactDeliveryService> logger)
{
    _workspaceService = workspaceService;
    _googleDriveShareService = googleDriveShareService;
    _brokerArtifactDownloadService = brokerArtifactDownloadService;
    _logger = logger;
}
```

- [ ] **Step 4: Update notification body builder so it never exposes internal paths**

Modify `packages/csharp/broker/Services/LineArtifactDeliveryService.cs`:

```csharp
internal static string BuildNotificationBody(
    string fileName,
    string filePath,
    GoogleDriveShareResult? driveResult,
    string? brokerDownloadUrl = null)
{
    var lines = new List<string>();

    if (driveResult?.Success == true)
    {
        lines.Add("檔案已完成並上傳到 Google Drive。");
        lines.Add(string.Empty);
        lines.Add($"檔名：{fileName}");
        if (!string.IsNullOrWhiteSpace(driveResult.DownloadLink))
        {
            lines.Add(string.Empty);
            lines.Add("下載連結：");
            lines.Add(driveResult.DownloadLink);
        }
        if (!string.IsNullOrWhiteSpace(driveResult.WebViewLink))
        {
            lines.Add(string.Empty);
            lines.Add("預覽連結：");
            lines.Add(driveResult.WebViewLink);
        }
        return string.Join(Environment.NewLine, lines);
    }

    lines.Add("檔案已完成。");
    lines.Add(string.Empty);
    lines.Add($"檔名：{fileName}");

    if (!string.IsNullOrWhiteSpace(brokerDownloadUrl))
    {
        lines.Add(string.Empty);
        lines.Add("下載連結：");
        lines.Add(brokerDownloadUrl);
        return string.Join(Environment.NewLine, lines);
    }

    lines.Add(string.Empty);
    lines.Add("雲端下載連結暫時不可用，請稍後由管理員協助重新交付。");
    return string.Join(Environment.NewLine, lines);
}
```

- [ ] **Step 5: Wire broker fallback URL creation into both delivery paths**

Update both `GenerateAndDeliverAsync` and `DeliverExistingFileAsync` in `packages/csharp/broker/Services/LineArtifactDeliveryService.cs`:

```csharp
var artifact = _workspaceService.RecordArtifact(new HighLevelLineArtifactRecord
{
    // existing fields
});

var brokerDownloadUrl = driveOk
    ? null
    : _brokerArtifactDownloadService.CreateSignedDownloadUrl(artifact.ArtifactId);

if (request.SendLineNotification)
{
    notification = _workspaceService.QueueLineNotification(
        request.UserId,
        string.IsNullOrWhiteSpace(request.NotificationTitle) ? "檔案已完成" : request.NotificationTitle.Trim(),
        BuildNotificationBody(fileName, filePath, driveResult, brokerDownloadUrl));
}
```

- [ ] **Step 6: Add a verify scenario that simulates Drive failure plus broker URL availability**

Add a focused verify case in `packages/csharp/broker/verify/Program.cs` with a fake failing `GoogleDriveShareService` input path or direct notification-body assertion, then verify:

```csharp
AssertTrue(fallbackNotification.Body.Contains("/api/v1/artifacts/download/", StringComparison.Ordinal), "drive failure plus broker public url available produces broker download fallback content");
AssertTrue(!fallbackNotification.Body.Contains("C:\\", StringComparison.OrdinalIgnoreCase), "fallback notification body does not leak internal path");
```

- [ ] **Step 7: Run verify to confirm fallback behavior passes**

Run:

```powershell
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- PASS for broker-link fallback and path-leak assertions

- [ ] **Step 8: Commit**

```powershell
git add packages/csharp/broker/Services/LineArtifactDeliveryService.cs packages/csharp/broker/verify/Program.cs
git commit -m "feat: use broker download links for drive fallback delivery"
```

### Task 5: Final Verification and Documentation Refresh

**Files:**
- Modify: `docs/designs/GoogleDriveDelivery.md`
- Modify: `docs/manuals/line-sidecar-runbook.md`
- Modify: `docs/manuals/line-sidecar-runbook.zh-TW.md`
- Test: `packages/csharp/broker/verify/Program.cs`

- [ ] **Step 1: Update design and runbook docs to reflect the new fallback path**

Add concise updates:

`docs/designs/GoogleDriveDelivery.md`

```md
- broker-signed artifact download URLs now exist as a fallback when Drive upload fails
- first version uses active sidecar ngrok public URL
- signed links are short-lived and must not expose internal paths
```

`docs/manuals/line-sidecar-runbook.md`

```md
- if Google Drive upload fails but the active ngrok public URL is available, broker can now issue a signed first-party artifact download link
- user-facing fallback messages must not expose local paths
```

`docs/manuals/line-sidecar-runbook.zh-TW.md`

```md
- 若 Google Drive 上傳失敗，但目前 ngrok 公開網址可用，broker 會改提供短效簽名下載連結
- 使用者可見訊息不得暴露本機內部路徑
```

- [ ] **Step 2: Run the required verification commands**

Run:

```powershell
dotnet build packages/csharp/ControlPlane.slnx -c Release --disable-build-servers -nodeReuse:false
dotnet run --project packages/csharp/broker/verify/Broker.Verify.csproj
```

Expected:

- build succeeds with `0` errors
- broker verify passes, including the new signed download and path-leak checks

- [ ] **Step 3: Check git state before claiming completion**

Run:

```powershell
git status --short
git log --oneline -n 5
```

Expected:

- only intended implementation/doc files are modified or committed
- no accidental staging of unrelated untracked files

- [ ] **Step 4: Commit**

```powershell
git add docs/designs/GoogleDriveDelivery.md docs/manuals/line-sidecar-runbook.md docs/manuals/line-sidecar-runbook.zh-TW.md
git commit -m "docs: document broker artifact download fallback"
```

## Self-Review

### Spec coverage

- Signed anonymous URL: covered in Task 2 and Task 3
- One-hour expiry and repeat downloads: covered in Task 1 and Task 2
- ngrok-based public base URL: covered in Task 1
- Only use broker link when Drive fails: covered in Task 4
- Never expose internal paths: covered in Task 4 and Task 5

### Placeholder scan

- No `TBD`
- No `TODO`
- No implicit “write tests later” steps
- Every code-touching step includes concrete file paths and example code

### Type consistency

- `BrokerArtifactDownloadOptions`, `SidecarPublicUrlResolver`, `BrokerArtifactDownloadService`, and `BrokerArtifactDownloadResolution` are defined before later tasks reference them
- Endpoint uses `ValidateAndResolve` from the download service
- Notification fallback calls the updated `BuildNotificationBody(..., brokerDownloadUrl)`

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-03-29-broker-artifact-download-api.md`. Two execution options:

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
