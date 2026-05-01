# Site Crawler Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first working slice of the site crawler: a safe path-depth crawler worker that returns raw crawl source and deterministic extraction output.

**Architecture:** Add a new `site-crawler-worker` executable using the existing `WorkerSdk.WorkerHost` pattern. The worker owns URL safety, path-depth traversal, page fetching, source/resource capture, and deterministic extraction. Broker integration in this phase is limited to registering a tool spec/capability; LINE orchestration and generator conversion are later phases.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, `WorkerSdk`, `HttpClient`, `System.Text.Json`, deterministic HTML parsing with `HtmlAgilityPack`.

---

## File Structure

Create:

- `packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj`
- `packages/csharp/workers/site-crawler-worker/Program.cs`
- `packages/csharp/workers/site-crawler-worker/appsettings.json`
- `packages/csharp/workers/site-crawler-worker/Models/SiteCrawlContracts.cs`
- `packages/csharp/workers/site-crawler-worker/Services/SafeUrlPolicy.cs`
- `packages/csharp/workers/site-crawler-worker/Services/PathDepthScope.cs`
- `packages/csharp/workers/site-crawler-worker/Services/SiteCrawlerService.cs`
- `packages/csharp/workers/site-crawler-worker/Services/DeterministicSiteExtractor.cs`
- `packages/csharp/workers/site-crawler-worker/Services/HttpPageFetcher.cs`
- `packages/csharp/workers/site-crawler-worker/Handlers/SiteCrawlSourceHandler.cs`
- `packages/csharp/broker/tool-specs/site.crawl.source/tool.json`
- `packages/csharp/broker/tool-specs/site.crawl.source/TOOL.md`
- `packages/csharp/tests/unit/Workers/SiteCrawler/SafeUrlPolicyTests.cs`
- `packages/csharp/tests/unit/Workers/SiteCrawler/PathDepthScopeTests.cs`
- `packages/csharp/tests/unit/Workers/SiteCrawler/DeterministicSiteExtractorTests.cs`
- `packages/csharp/tests/unit/Workers/SiteCrawler/SiteCrawlerServiceTests.cs`
- `packages/csharp/tests/unit/Workers/SiteCrawler/SiteCrawlSourceHandlerTests.cs`

Modify:

- `packages/csharp/ControlPlane.slnx`
- `packages/csharp/tests/unit/Unit.Tests.csproj`

---

### Task 1: Add Project Shell

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj`
- Create: `packages/csharp/workers/site-crawler-worker/appsettings.json`
- Modify: `packages/csharp/ControlPlane.slnx`
- Modify: `packages/csharp/tests/unit/Unit.Tests.csproj`

- [ ] **Step 1: Create the worker project file**

Create `packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>SiteCrawlerWorker</RootNamespace>
    <Version>1.0.0</Version>
    <Description>Site crawler worker for safe path-depth website source capture and deterministic extraction</Description>
    <Authors>Bricks4Agent</Authors>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\worker-sdk\WorkerSdk.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="HtmlAgilityPack" Version="1.11.71" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration.CommandLine" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="appsettings.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create default worker config**

Create `packages/csharp/workers/site-crawler-worker/appsettings.json`:

```json
{
  "Worker": {
    "BrokerHost": "localhost",
    "BrokerPort": 7000,
    "WorkerId": "",
    "MaxConcurrent": 2,
    "HeartbeatIntervalSeconds": 5,
    "Auth": {
      "WorkerType": "site-crawler-worker",
      "KeyId": "",
      "SharedSecret": ""
    }
  },
  "Crawler": {
    "DefaultMaxPages": 50,
    "DefaultMaxTotalBytes": 10485760,
    "DefaultMaxAssetBytes": 2097152,
    "DefaultWallClockTimeoutSeconds": 180,
    "UserAgent": "Bricks4Agent-SiteCrawler/1.0"
  }
}
```

- [ ] **Step 3: Add project to solution**

Modify `packages/csharp/ControlPlane.slnx` by adding this folder entry under `/workers/`:

```xml
  <Folder Name="/workers/site-crawler-worker/">
    <Project Path="workers/site-crawler-worker/SiteCrawlerWorker.csproj" />
  </Folder>
```

- [ ] **Step 4: Reference worker project from unit tests**

Modify `packages/csharp/tests/unit/Unit.Tests.csproj` and add:

```xml
    <ProjectReference Include="../../workers/site-crawler-worker/SiteCrawlerWorker.csproj" />
```

Place it with the other worker project references.

- [ ] **Step 5: Verify project shell builds**

Run:

```powershell
dotnet build packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj
```

Expected: build fails because `Program.cs` is missing. This confirms the project is included and ready for implementation.

- [ ] **Step 6: Commit**

```powershell
git add packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj packages/csharp/workers/site-crawler-worker/appsettings.json packages/csharp/ControlPlane.slnx packages/csharp/tests/unit/Unit.Tests.csproj
git commit -m "feat: add site crawler worker project shell"
```

---

### Task 2: Define Contracts

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Models/SiteCrawlContracts.cs`

- [ ] **Step 1: Add contracts**

Create `packages/csharp/workers/site-crawler-worker/Models/SiteCrawlContracts.cs`:

```csharp
using System.Text.Json.Serialization;

namespace SiteCrawlerWorker.Models;

public sealed class SiteCrawlRequest
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("start_url")]
    public string StartUrl { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public SiteCrawlScope Scope { get; set; } = new();

    [JsonPropertyName("capture")]
    public SiteCrawlCaptureOptions Capture { get; set; } = new();

    [JsonPropertyName("budgets")]
    public SiteCrawlBudgets Budgets { get; set; } = new();
}

public sealed class SiteCrawlScope
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "path_depth";

    [JsonPropertyName("max_depth")]
    public int MaxDepth { get; set; }

    [JsonPropertyName("same_origin_only")]
    public bool SameOriginOnly { get; set; } = true;

    [JsonPropertyName("path_prefix_lock")]
    public bool PathPrefixLock { get; set; } = true;
}

public sealed class SiteCrawlCaptureOptions
{
    [JsonPropertyName("html")]
    public bool Html { get; set; } = true;

    [JsonPropertyName("rendered_dom")]
    public bool RenderedDom { get; set; } = true;

    [JsonPropertyName("css")]
    public bool Css { get; set; } = true;

    [JsonPropertyName("scripts")]
    public bool Scripts { get; set; } = true;

    [JsonPropertyName("assets")]
    public bool Assets { get; set; } = true;

    [JsonPropertyName("screenshots")]
    public bool Screenshots { get; set; }
}

public sealed class SiteCrawlBudgets
{
    [JsonPropertyName("max_pages")]
    public int MaxPages { get; set; } = 50;

    [JsonPropertyName("max_total_bytes")]
    public long MaxTotalBytes { get; set; } = 10 * 1024 * 1024;

    [JsonPropertyName("max_asset_bytes")]
    public long MaxAssetBytes { get; set; } = 2 * 1024 * 1024;

    [JsonPropertyName("wall_clock_timeout_seconds")]
    public int WallClockTimeoutSeconds { get; set; } = 180;
}

public sealed class SiteCrawlResult
{
    [JsonPropertyName("crawl_run_id")]
    public string CrawlRunId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "completed";

    [JsonPropertyName("root")]
    public SiteCrawlRoot Root { get; set; } = new();

    [JsonPropertyName("pages")]
    public List<SiteCrawlPage> Pages { get; set; } = new();

    [JsonPropertyName("assets")]
    public List<SiteCrawlAsset> Assets { get; set; } = new();

    [JsonPropertyName("excluded")]
    public List<SiteCrawlExcludedUrl> Excluded { get; set; } = new();

    [JsonPropertyName("extracted_model")]
    public ExtractedSiteModel ExtractedModel { get; set; } = new();

    [JsonPropertyName("limits")]
    public SiteCrawlLimitState Limits { get; set; } = new();
}

public sealed class SiteCrawlRoot
{
    [JsonPropertyName("start_url")]
    public string StartUrl { get; set; } = string.Empty;

    [JsonPropertyName("normalized_start_url")]
    public string NormalizedStartUrl { get; set; } = string.Empty;

    [JsonPropertyName("origin")]
    public string Origin { get; set; } = string.Empty;

    [JsonPropertyName("path_prefix")]
    public string PathPrefix { get; set; } = "/";
}

public sealed class SiteCrawlPage
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("final_url")]
    public string FinalUrl { get; set; } = string.Empty;

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("html")]
    public string Html { get; set; } = string.Empty;

    [JsonPropertyName("text_excerpt")]
    public string TextExcerpt { get; set; } = string.Empty;

    [JsonPropertyName("links")]
    public List<string> Links { get; set; } = new();

    [JsonPropertyName("forms")]
    public List<ExtractedForm> Forms { get; set; } = new();

    [JsonPropertyName("resources")]
    public List<string> Resources { get; set; } = new();
}

public sealed class SiteCrawlAsset
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class SiteCrawlExcludedUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;
}

public sealed class SiteCrawlLimitState
{
    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    [JsonPropertyName("page_limit_hit")]
    public bool PageLimitHit { get; set; }

    [JsonPropertyName("byte_limit_hit")]
    public bool ByteLimitHit { get; set; }
}

public sealed class ExtractedSiteModel
{
    [JsonPropertyName("pages")]
    public List<ExtractedPageModel> Pages { get; set; } = new();

    [JsonPropertyName("theme_tokens")]
    public ExtractedThemeTokens ThemeTokens { get; set; } = new();

    [JsonPropertyName("route_graph")]
    public ExtractedRouteGraph RouteGraph { get; set; } = new();
}

public sealed class ExtractedPageModel
{
    [JsonPropertyName("page_url")]
    public string PageUrl { get; set; } = string.Empty;

    [JsonPropertyName("sections")]
    public List<ExtractedSection> Sections { get; set; } = new();
}

public sealed class ExtractedSection
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "content";

    [JsonPropertyName("headline")]
    public string Headline { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("source_selector")]
    public string SourceSelector { get; set; } = string.Empty;
}

public sealed class ExtractedForm
{
    [JsonPropertyName("selector")]
    public string Selector { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = "get";

    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("fields")]
    public List<ExtractedFormField> Fields { get; set; } = new();
}

public sealed class ExtractedFormField
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

public sealed class ExtractedThemeTokens
{
    [JsonPropertyName("colors")]
    public Dictionary<string, string> Colors { get; set; } = new();

    [JsonPropertyName("typography")]
    public Dictionary<string, string> Typography { get; set; } = new();
}

public sealed class ExtractedRouteGraph
{
    [JsonPropertyName("routes")]
    public List<ExtractedRoute> Routes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<ExtractedRouteEdge> Edges { get; set; } = new();
}

public sealed class ExtractedRoute
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("page_id")]
    public string PageId { get; set; } = string.Empty;

    [JsonPropertyName("depth")]
    public int Depth { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;
}

public sealed class ExtractedRouteEdge
{
    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "internal_link";
}
```

- [ ] **Step 2: Build to verify contracts compile**

Run:

```powershell
dotnet build packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj
```

Expected: still fails because `Program.cs` is missing, but no errors should reference `SiteCrawlContracts.cs`.

- [ ] **Step 3: Commit**

```powershell
git add packages/csharp/workers/site-crawler-worker/Models/SiteCrawlContracts.cs
git commit -m "feat: define site crawl contracts"
```

---

### Task 3: URL Safety Policy

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Services/SafeUrlPolicy.cs`
- Create: `packages/csharp/tests/unit/Workers/SiteCrawler/SafeUrlPolicyTests.cs`

- [ ] **Step 1: Write failing tests**

Create `packages/csharp/tests/unit/Workers/SiteCrawler/SafeUrlPolicyTests.cs`:

```csharp
using FluentAssertions;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SafeUrlPolicyTests
{
    [Theory]
    [InlineData("https://example.com/docs")]
    [InlineData("http://example.com/")]
    public void ValidateAcceptsPublicHttpUrls(string url)
    {
        var result = SafeUrlPolicy.Validate(url);

        result.IsAllowed.Should().BeTrue();
        result.Uri.Should().NotBeNull();
        result.Reason.Should().BeEmpty();
    }

    [Theory]
    [InlineData("file:///c:/secret.txt", "unsupported_scheme")]
    [InlineData("data:text/plain,hello", "unsupported_scheme")]
    [InlineData("ftp://example.com/file", "unsupported_scheme")]
    [InlineData("https://localhost/admin", "blocked_host")]
    [InlineData("http://127.0.0.1/", "blocked_host")]
    [InlineData("http://[::1]/", "blocked_host")]
    [InlineData("http://169.254.169.254/latest/meta-data", "blocked_host")]
    [InlineData("http://10.0.0.1/", "blocked_host")]
    [InlineData("http://172.16.0.1/", "blocked_host")]
    [InlineData("http://192.168.0.1/", "blocked_host")]
    public void ValidateRejectsUnsafeUrls(string url, string expectedReason)
    {
        var result = SafeUrlPolicy.Validate(url);

        result.IsAllowed.Should().BeFalse();
        result.Reason.Should().Be(expectedReason);
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SafeUrlPolicyTests
```

Expected: compile failure because `SafeUrlPolicy` does not exist.

- [ ] **Step 3: Implement URL policy**

Create `packages/csharp/workers/site-crawler-worker/Services/SafeUrlPolicy.cs`:

```csharp
using System.Net;
using System.Net.Sockets;

namespace SiteCrawlerWorker.Services;

public sealed record SafeUrlValidationResult(bool IsAllowed, Uri? Uri, string Reason)
{
    public static SafeUrlValidationResult Allow(Uri uri) => new(true, uri, string.Empty);
    public static SafeUrlValidationResult Deny(string reason) => new(false, null, reason);
}

public static class SafeUrlPolicy
{
    public static SafeUrlValidationResult Validate(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return SafeUrlValidationResult.Deny("url_required");

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            return SafeUrlValidationResult.Deny("invalid_url");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return SafeUrlValidationResult.Deny("unsupported_scheme");

        if (IsBlockedHost(uri.Host))
            return SafeUrlValidationResult.Deny("blocked_host");

        return SafeUrlValidationResult.Allow(Normalize(uri));
    }

    public static Uri Normalize(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };

        if (string.IsNullOrEmpty(builder.Path))
            builder.Path = "/";

        return builder.Uri;
    }

    private static bool IsBlockedHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;

        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IPAddress.TryParse(host, out var ip))
            return IsBlockedIp(ip);

        return false;
    }

    private static bool IsBlockedIp(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast;

        var bytes = ip.GetAddressBytes();
        var first = bytes[0];
        var second = bytes[1];

        return first == 10 ||
               first == 127 ||
               (first == 169 && second == 254) ||
               (first == 172 && second >= 16 && second <= 31) ||
               (first == 192 && second == 168) ||
               first == 0;
    }
}
```

- [ ] **Step 4: Run tests and verify pass**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SafeUrlPolicyTests
```

Expected: all `SafeUrlPolicyTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add packages/csharp/workers/site-crawler-worker/Services/SafeUrlPolicy.cs packages/csharp/tests/unit/Workers/SiteCrawler/SafeUrlPolicyTests.cs
git commit -m "feat: add site crawler url safety policy"
```

---

### Task 4: Path Depth Scope

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Services/PathDepthScope.cs`
- Create: `packages/csharp/tests/unit/Workers/SiteCrawler/PathDepthScopeTests.cs`

- [ ] **Step 1: Write failing tests**

Create `packages/csharp/tests/unit/Workers/SiteCrawler/PathDepthScopeTests.cs`:

```csharp
using FluentAssertions;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class PathDepthScopeTests
{
    [Fact]
    public void CreateLocksStartPathPrefix()
    {
        var scope = PathDepthScope.Create(
            new Uri("https://example.com/docs/"),
            new SiteCrawlScope { MaxDepth = 1 });

        scope.PathPrefix.Should().Be("/docs/");
        scope.Origin.Should().Be("https://example.com");
    }

    [Theory]
    [InlineData("https://example.com/docs/", 0, true)]
    [InlineData("https://example.com/docs/a", 1, true)]
    [InlineData("https://example.com/docs/b/", 1, true)]
    [InlineData("https://example.com/docs/a/detail", 2, false)]
    [InlineData("https://example.com/other", -1, false)]
    [InlineData("https://other.example.com/docs/a", -1, false)]
    public void EvaluateUsesPathDepth(string url, int expectedDepth, bool expectedAllowed)
    {
        var scope = PathDepthScope.Create(
            new Uri("https://example.com/docs/"),
            new SiteCrawlScope
            {
                MaxDepth = 1,
                SameOriginOnly = true,
                PathPrefixLock = true
            });

        var result = scope.Evaluate(new Uri(url));

        result.Depth.Should().Be(expectedDepth);
        result.IsAllowed.Should().Be(expectedAllowed);
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter PathDepthScopeTests
```

Expected: compile failure because `PathDepthScope` does not exist.

- [ ] **Step 3: Implement path scope**

Create `packages/csharp/workers/site-crawler-worker/Services/PathDepthScope.cs`:

```csharp
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed record PathDepthEvaluation(bool IsAllowed, int Depth, string Reason)
{
    public static PathDepthEvaluation Allow(int depth) => new(true, depth, string.Empty);
    public static PathDepthEvaluation Deny(string reason) => new(false, -1, reason);
}

public sealed class PathDepthScope
{
    private readonly SiteCrawlScope _scope;

    private PathDepthScope(Uri startUri, SiteCrawlScope scope)
    {
        StartUri = startUri;
        _scope = scope;
        Origin = $"{startUri.Scheme}://{startUri.Authority}";
        PathPrefix = NormalizePrefix(startUri.AbsolutePath);
    }

    public Uri StartUri { get; }
    public string Origin { get; }
    public string PathPrefix { get; }

    public static PathDepthScope Create(Uri startUri, SiteCrawlScope scope)
        => new(startUri, scope);

    public PathDepthEvaluation Evaluate(Uri candidate)
    {
        if (_scope.SameOriginOnly &&
            !string.Equals(candidate.Scheme, StartUri.Scheme, StringComparison.OrdinalIgnoreCase))
            return PathDepthEvaluation.Deny("outside_origin");

        if (_scope.SameOriginOnly &&
            !string.Equals(candidate.Authority, StartUri.Authority, StringComparison.OrdinalIgnoreCase))
            return PathDepthEvaluation.Deny("outside_origin");

        var candidatePath = NormalizePath(candidate.AbsolutePath);
        if (_scope.PathPrefixLock &&
            !candidatePath.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase))
            return PathDepthEvaluation.Deny("outside_path_prefix");

        var depth = CalculateDepth(candidatePath);
        if (depth > _scope.MaxDepth)
            return new PathDepthEvaluation(false, depth, "outside_path_depth");

        return PathDepthEvaluation.Allow(depth);
    }

    private int CalculateDepth(string candidatePath)
    {
        var relative = candidatePath[PathPrefix.Length..].Trim('/');
        if (string.IsNullOrWhiteSpace(relative))
            return 0;

        return relative.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string NormalizePrefix(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.EndsWith('/') ? normalized : normalized + "/";
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "/";

        var normalized = Uri.UnescapeDataString(path);
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        return normalized;
    }
}
```

- [ ] **Step 4: Run tests and verify pass**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter PathDepthScopeTests
```

Expected: all `PathDepthScopeTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add packages/csharp/workers/site-crawler-worker/Services/PathDepthScope.cs packages/csharp/tests/unit/Workers/SiteCrawler/PathDepthScopeTests.cs
git commit -m "feat: add path depth crawl scope"
```

---

### Task 5: Deterministic Extractor

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Services/DeterministicSiteExtractor.cs`
- Create: `packages/csharp/tests/unit/Workers/SiteCrawler/DeterministicSiteExtractorTests.cs`

- [ ] **Step 1: Write failing tests**

Create `packages/csharp/tests/unit/Workers/SiteCrawler/DeterministicSiteExtractorTests.cs`:

```csharp
using FluentAssertions;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class DeterministicSiteExtractorTests
{
    [Fact]
    public void ExtractPageFindsHeroLinksAndFormFields()
    {
        const string html = """
            <!doctype html>
            <html>
              <head>
                <title>Docs Home</title>
                <style>:root { --brand: #3366ff; } body { font-family: Inter, sans-serif; }</style>
              </head>
              <body>
                <main>
                  <section class="hero">
                    <h1>Build Faster</h1>
                    <p>Convert source pages into generator definitions.</p>
                    <a href="/docs/start">Start</a>
                  </section>
                  <form action="/contact" method="post">
                    <label>Email <input type="email" name="email" required></label>
                  </form>
                </main>
              </body>
            </html>
            """;

        var extractor = new DeterministicSiteExtractor();
        var page = extractor.ExtractPage(new Uri("https://example.com/docs/"), html);

        page.Title.Should().Be("Docs Home");
        page.Links.Should().Contain("https://example.com/docs/start");
        page.Forms.Should().ContainSingle();
        page.Forms[0].Fields.Should().ContainSingle(field =>
            field.Name == "email" && field.Type == "email" && field.Required);
        page.Model.Sections.Should().ContainSingle(section =>
            section.Role == "hero" && section.Headline == "Build Faster");
        page.ThemeTokens.Colors.Should().ContainKey("brand");
        page.ThemeTokens.Typography.Should().ContainKey("font_family");
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter DeterministicSiteExtractorTests
```

Expected: compile failure because `DeterministicSiteExtractor` does not exist.

- [ ] **Step 3: Implement extractor**

Create `packages/csharp/workers/site-crawler-worker/Services/DeterministicSiteExtractor.cs`:

```csharp
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class DeterministicSiteExtractor
{
    private static readonly Regex CssVarColorRegex = new(@"--(?<name>[a-zA-Z0-9_-]+)\s*:\s*(?<value>#[0-9a-fA-F]{3,8})", RegexOptions.Compiled);
    private static readonly Regex FontFamilyRegex = new(@"font-family\s*:\s*(?<value>[^;]+);", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public ExtractedPageResult ExtractPage(Uri pageUri, string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html ?? string.Empty);

        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim() ?? string.Empty;
        var links = ExtractLinks(pageUri, doc);
        var forms = ExtractForms(doc);
        var sections = ExtractSections(doc);
        var tokens = ExtractThemeTokens(doc);
        var text = HtmlEntity.DeEntitize(doc.DocumentNode.InnerText ?? string.Empty);
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return new ExtractedPageResult
        {
            Title = title,
            Links = links,
            Forms = forms,
            TextExcerpt = text.Length <= 1000 ? text : text[..1000],
            ThemeTokens = tokens,
            Model = new ExtractedPageModel
            {
                PageUrl = pageUri.ToString(),
                Sections = sections
            }
        };
    }

    private static List<string> ExtractLinks(Uri pageUri, HtmlDocument doc)
    {
        var links = new List<string>();
        foreach (var anchor in doc.DocumentNode.SelectNodes("//a[@href]") ?? Enumerable.Empty<HtmlNode>())
        {
            var href = anchor.GetAttributeValue("href", string.Empty).Trim();
            if (href.Length == 0 || href.StartsWith("#", StringComparison.Ordinal))
                continue;

            if (Uri.TryCreate(pageUri, href, out var resolved) &&
                (resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps))
            {
                var builder = new UriBuilder(resolved) { Fragment = string.Empty };
                links.Add(builder.Uri.ToString());
            }
        }

        return links.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<ExtractedForm> ExtractForms(HtmlDocument doc)
    {
        var forms = new List<ExtractedForm>();
        var nodes = doc.DocumentNode.SelectNodes("//form") ?? Enumerable.Empty<HtmlNode>();
        var index = 0;
        foreach (var form in nodes)
        {
            index++;
            var extracted = new ExtractedForm
            {
                Selector = $"form:nth-of-type({index})",
                Method = form.GetAttributeValue("method", "get").ToLowerInvariant(),
                Action = form.GetAttributeValue("action", string.Empty)
            };

            foreach (var input in form.SelectNodes(".//input|.//textarea|.//select") ?? Enumerable.Empty<HtmlNode>())
            {
                var name = input.GetAttributeValue("name", string.Empty);
                if (string.IsNullOrWhiteSpace(name))
                    name = input.GetAttributeValue("id", string.Empty);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                extracted.Fields.Add(new ExtractedFormField
                {
                    Name = name,
                    Type = NormalizeFieldType(input),
                    Label = FindLabel(input),
                    Required = input.Attributes["required"] != null
                });
            }

            forms.Add(extracted);
        }

        return forms;
    }

    private static string NormalizeFieldType(HtmlNode input)
    {
        if (input.Name.Equals("textarea", StringComparison.OrdinalIgnoreCase))
            return "textarea";
        if (input.Name.Equals("select", StringComparison.OrdinalIgnoreCase))
            return "select";
        return input.GetAttributeValue("type", "text").ToLowerInvariant();
    }

    private static string FindLabel(HtmlNode input)
    {
        var id = input.GetAttributeValue("id", string.Empty);
        if (!string.IsNullOrWhiteSpace(id))
        {
            var doc = input.OwnerDocument;
            var label = doc.DocumentNode.SelectSingleNode($"//label[@for='{id}']");
            if (label != null)
                return CleanText(label.InnerText);
        }

        var parent = input.ParentNode;
        while (parent != null)
        {
            if (parent.Name.Equals("label", StringComparison.OrdinalIgnoreCase))
                return CleanText(parent.InnerText);
            parent = parent.ParentNode;
        }

        return input.GetAttributeValue("placeholder", string.Empty);
    }

    private static List<ExtractedSection> ExtractSections(HtmlDocument doc)
    {
        var candidates = doc.DocumentNode.SelectNodes("//main//section|//section|//article|//header|//footer") ??
            Enumerable.Empty<HtmlNode>();
        var sections = new List<ExtractedSection>();
        var index = 0;
        foreach (var node in candidates)
        {
            var body = CleanText(node.InnerText);
            if (body.Length == 0)
                continue;

            index++;
            sections.Add(new ExtractedSection
            {
                Id = $"sec_{index}",
                Tag = node.Name,
                Role = InferRole(node),
                Headline = CleanText(node.SelectSingleNode(".//h1|.//h2|.//h3")?.InnerText ?? string.Empty),
                Body = body.Length <= 500 ? body : body[..500],
                SourceSelector = $"{node.Name}:nth-of-type({index})"
            });
        }

        return sections;
    }

    private static ExtractedThemeTokens ExtractThemeTokens(HtmlDocument doc)
    {
        var tokens = new ExtractedThemeTokens();
        var css = string.Join("\n", (doc.DocumentNode.SelectNodes("//style") ?? Enumerable.Empty<HtmlNode>())
            .Select(node => node.InnerText));

        foreach (Match match in CssVarColorRegex.Matches(css))
        {
            tokens.Colors[match.Groups["name"].Value] = match.Groups["value"].Value;
        }

        var font = FontFamilyRegex.Match(css);
        if (font.Success)
            tokens.Typography["font_family"] = font.Groups["value"].Value.Trim();

        return tokens;
    }

    private static string InferRole(HtmlNode node)
    {
        var cls = node.GetAttributeValue("class", string.Empty).ToLowerInvariant();
        var id = node.GetAttributeValue("id", string.Empty).ToLowerInvariant();
        var marker = $"{cls} {id}";
        if (node.Name.Equals("header", StringComparison.OrdinalIgnoreCase) || marker.Contains("hero"))
            return "hero";
        if (node.Name.Equals("footer", StringComparison.OrdinalIgnoreCase))
            return "footer";
        if (node.Name.Equals("article", StringComparison.OrdinalIgnoreCase))
            return "article";
        if (marker.Contains("card") || marker.Contains("grid"))
            return "card_grid";
        if (node.SelectSingleNode(".//form") != null)
            return "form";
        return "content";
    }

    private static string CleanText(string value)
        => Regex.Replace(HtmlEntity.DeEntitize(value ?? string.Empty), @"\s+", " ").Trim();
}

public sealed class ExtractedPageResult
{
    public string Title { get; set; } = string.Empty;
    public string TextExcerpt { get; set; } = string.Empty;
    public List<string> Links { get; set; } = new();
    public List<ExtractedForm> Forms { get; set; } = new();
    public ExtractedThemeTokens ThemeTokens { get; set; } = new();
    public ExtractedPageModel Model { get; set; } = new();
}
```

- [ ] **Step 4: Run tests and verify pass**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter DeterministicSiteExtractorTests
```

Expected: all `DeterministicSiteExtractorTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add packages/csharp/workers/site-crawler-worker/Services/DeterministicSiteExtractor.cs packages/csharp/tests/unit/Workers/SiteCrawler/DeterministicSiteExtractorTests.cs
git commit -m "feat: add deterministic site extraction"
```

---

### Task 6: BFS Crawler Service

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Services/HttpPageFetcher.cs`
- Create: `packages/csharp/workers/site-crawler-worker/Services/SiteCrawlerService.cs`
- Create: `packages/csharp/tests/unit/Workers/SiteCrawler/SiteCrawlerServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `packages/csharp/tests/unit/Workers/SiteCrawler/SiteCrawlerServiceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteCrawlerServiceTests
{
    [Fact]
    public async Task CrawlAsyncFollowsOnlyAllowedPathDepth()
    {
        var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://example.com/docs/"] = """
                <html><head><title>Root</title></head><body>
                <section><h1>Root</h1><a href="/docs/a">A</a><a href="/docs/a/detail">Detail</a><a href="/other">Other</a></section>
                </body></html>
                """,
            ["https://example.com/docs/a"] = """
                <html><head><title>A</title></head><body><section><h1>A</h1></section></body></html>
                """
        };

        var service = new SiteCrawlerService(
            new FakePageFetcher(pages),
            new DeterministicSiteExtractor(),
            NullLogger<SiteCrawlerService>.Instance);

        var result = await service.CrawlAsync(new SiteCrawlRequest
        {
            StartUrl = "https://example.com/docs/",
            Scope = new SiteCrawlScope { MaxDepth = 1 },
            Budgets = new SiteCrawlBudgets { MaxPages = 10 }
        }, CancellationToken.None);

        result.Pages.Select(p => p.FinalUrl).Should().BeEquivalentTo(
            "https://example.com/docs/",
            "https://example.com/docs/a");
        result.Excluded.Should().Contain(e => e.Url == "https://example.com/docs/a/detail" && e.Reason == "outside_path_depth");
        result.Excluded.Should().Contain(e => e.Url == "https://example.com/other" && e.Reason == "outside_path_prefix");
        result.ExtractedModel.Pages.Should().HaveCount(2);
        result.ExtractedModel.RouteGraph.Routes.Should().HaveCount(2);
    }

    private sealed class FakePageFetcher : IPageFetcher
    {
        private readonly Dictionary<string, string> _pages;

        public FakePageFetcher(Dictionary<string, string> pages)
        {
            _pages = pages;
        }

        public Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
        {
            return _pages.TryGetValue(uri.ToString(), out var html)
                ? Task.FromResult(PageFetchResult.Ok(uri, 200, "text/html", html))
                : Task.FromResult(PageFetchResult.Fail(uri, 404, "missing"));
        }
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SiteCrawlerServiceTests
```

Expected: compile failure because `SiteCrawlerService`, `IPageFetcher`, and `PageFetchResult` do not exist.

- [ ] **Step 3: Implement page fetcher**

Create `packages/csharp/workers/site-crawler-worker/Services/HttpPageFetcher.cs`:

```csharp
namespace SiteCrawlerWorker.Services;

public interface IPageFetcher
{
    Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct);
}

public sealed class PageFetchResult
{
    public bool Success { get; set; }
    public Uri Uri { get; set; } = new("https://invalid.local/");
    public Uri FinalUri { get; set; } = new("https://invalid.local/");
    public int StatusCode { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string Html { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;

    public static PageFetchResult Ok(Uri finalUri, int statusCode, string contentType, string html)
        => new()
        {
            Success = true,
            Uri = finalUri,
            FinalUri = finalUri,
            StatusCode = statusCode,
            ContentType = contentType,
            Html = html
        };

    public static PageFetchResult Fail(Uri uri, int statusCode, string error)
        => new()
        {
            Success = false,
            Uri = uri,
            FinalUri = uri,
            StatusCode = statusCode,
            Error = error
        };
}

public sealed class HttpPageFetcher : IPageFetcher
{
    private readonly HttpClient _httpClient;

    public HttpPageFetcher(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
    {
        using var response = await _httpClient.GetAsync(uri, ct);
        var finalUri = response.RequestMessage?.RequestUri ?? uri;
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!response.IsSuccessStatusCode)
            return PageFetchResult.Fail(finalUri, (int)response.StatusCode, $"http_{(int)response.StatusCode}");

        if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            return PageFetchResult.Fail(finalUri, (int)response.StatusCode, "non_html");

        var html = await response.Content.ReadAsStringAsync(ct);
        return PageFetchResult.Ok(finalUri, (int)response.StatusCode, contentType, html);
    }
}
```

- [ ] **Step 4: Implement crawler service**

Create `packages/csharp/workers/site-crawler-worker/Services/SiteCrawlerService.cs`:

```csharp
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class SiteCrawlerService
{
    private readonly IPageFetcher _fetcher;
    private readonly DeterministicSiteExtractor _extractor;
    private readonly ILogger<SiteCrawlerService> _logger;

    public SiteCrawlerService(
        IPageFetcher fetcher,
        DeterministicSiteExtractor extractor,
        ILogger<SiteCrawlerService> logger)
    {
        _fetcher = fetcher;
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<SiteCrawlResult> CrawlAsync(SiteCrawlRequest request, CancellationToken ct)
    {
        var validation = SafeUrlPolicy.Validate(request.StartUrl);
        if (!validation.IsAllowed || validation.Uri == null)
            throw new InvalidOperationException(validation.Reason);

        var startUri = validation.Uri;
        var scope = PathDepthScope.Create(startUri, request.Scope);
        var crawlRunId = string.IsNullOrWhiteSpace(request.RequestId)
            ? $"crawl_{Guid.NewGuid():N}"[..24]
            : $"crawl_{request.RequestId}";

        var result = new SiteCrawlResult
        {
            CrawlRunId = crawlRunId,
            Root = new SiteCrawlRoot
            {
                StartUrl = request.StartUrl,
                NormalizedStartUrl = startUri.ToString(),
                Origin = scope.Origin,
                PathPrefix = scope.PathPrefix
            }
        };

        var queue = new Queue<Uri>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue(startUri);
        seen.Add(startUri.ToString());

        while (queue.Count > 0 && !ct.IsCancellationRequested)
        {
            if (result.Pages.Count >= request.Budgets.MaxPages)
            {
                result.Limits.PageLimitHit = true;
                result.Limits.Truncated = true;
                break;
            }

            var current = queue.Dequeue();
            var eval = scope.Evaluate(current);
            if (!eval.IsAllowed)
            {
                result.Excluded.Add(new SiteCrawlExcludedUrl { Url = current.ToString(), Reason = eval.Reason });
                continue;
            }

            var safety = SafeUrlPolicy.Validate(current.ToString());
            if (!safety.IsAllowed)
            {
                result.Excluded.Add(new SiteCrawlExcludedUrl { Url = current.ToString(), Reason = safety.Reason });
                continue;
            }

            _logger.LogInformation("Crawling {Url}", current);
            var fetch = await _fetcher.FetchAsync(current, ct);
            if (!fetch.Success)
            {
                result.Excluded.Add(new SiteCrawlExcludedUrl { Url = current.ToString(), Reason = fetch.Error });
                continue;
            }

            var extracted = _extractor.ExtractPage(fetch.FinalUri, fetch.Html);
            var page = new SiteCrawlPage
            {
                Url = current.ToString(),
                FinalUrl = fetch.FinalUri.ToString(),
                Depth = eval.Depth,
                StatusCode = fetch.StatusCode,
                Title = extracted.Title,
                Html = request.Capture.Html ? fetch.Html : string.Empty,
                TextExcerpt = extracted.TextExcerpt,
                Links = extracted.Links,
                Forms = extracted.Forms
            };
            result.Pages.Add(page);
            result.ExtractedModel.Pages.Add(extracted.Model);
            MergeThemeTokens(result.ExtractedModel.ThemeTokens, extracted.ThemeTokens);
            result.ExtractedModel.RouteGraph.Routes.Add(new ExtractedRoute
            {
                Path = fetch.FinalUri.AbsolutePath,
                PageId = ToPageId(fetch.FinalUri.AbsolutePath),
                Depth = eval.Depth,
                Title = extracted.Title
            });

            foreach (var link in extracted.Links)
            {
                if (!Uri.TryCreate(link, UriKind.Absolute, out var linkUri))
                    continue;

                var linkSafety = SafeUrlPolicy.Validate(linkUri.ToString());
                if (!linkSafety.IsAllowed || linkSafety.Uri == null)
                {
                    result.Excluded.Add(new SiteCrawlExcludedUrl { Url = link, Reason = linkSafety.Reason });
                    continue;
                }

                var linkEval = scope.Evaluate(linkSafety.Uri);
                if (!linkEval.IsAllowed)
                {
                    result.Excluded.Add(new SiteCrawlExcludedUrl { Url = linkSafety.Uri.ToString(), Reason = linkEval.Reason });
                    continue;
                }

                result.ExtractedModel.RouteGraph.Edges.Add(new ExtractedRouteEdge
                {
                    From = fetch.FinalUri.AbsolutePath,
                    To = linkSafety.Uri.AbsolutePath
                });

                if (seen.Add(linkSafety.Uri.ToString()))
                    queue.Enqueue(linkSafety.Uri);
            }
        }

        return result;
    }

    private static void MergeThemeTokens(ExtractedThemeTokens target, ExtractedThemeTokens source)
    {
        foreach (var item in source.Colors)
            target.Colors.TryAdd(item.Key, item.Value);
        foreach (var item in source.Typography)
            target.Typography.TryAdd(item.Key, item.Value);
    }

    private static string ToPageId(string path)
    {
        var cleaned = path.Trim('/').Replace('/', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "home" : cleaned;
    }
}
```

- [ ] **Step 5: Run tests and verify pass**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SiteCrawlerServiceTests
```

Expected: all `SiteCrawlerServiceTests` pass.

- [ ] **Step 6: Commit**

```powershell
git add packages/csharp/workers/site-crawler-worker/Services/HttpPageFetcher.cs packages/csharp/workers/site-crawler-worker/Services/SiteCrawlerService.cs packages/csharp/tests/unit/Workers/SiteCrawler/SiteCrawlerServiceTests.cs
git commit -m "feat: add path depth site crawler service"
```

---

### Task 7: Worker Handler

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Handlers/SiteCrawlSourceHandler.cs`
- Create: `packages/csharp/tests/unit/Workers/SiteCrawler/SiteCrawlSourceHandlerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `packages/csharp/tests/unit/Workers/SiteCrawler/SiteCrawlSourceHandlerTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SiteCrawlerWorker.Handlers;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class SiteCrawlSourceHandlerTests
{
    [Fact]
    public async Task ExecuteAsyncReturnsSerializedCrawlResult()
    {
        var pages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://example.com/docs/"] = "<html><head><title>Root</title></head><body><section><h1>Root</h1></section></body></html>"
        };
        var service = new SiteCrawlerService(
            new FakePageFetcher(pages),
            new DeterministicSiteExtractor(),
            NullLogger<SiteCrawlerService>.Instance);
        var handler = new SiteCrawlSourceHandler(service, NullLogger<SiteCrawlSourceHandler>.Instance);

        var payload = JsonSerializer.Serialize(new
        {
            args = new
            {
                start_url = "https://example.com/docs/",
                scope = new { kind = "path_depth", max_depth = 0, same_origin_only = true, path_prefix_lock = true }
            }
        });

        var result = await handler.ExecuteAsync("req_1", "site_crawl_source", payload, "{}", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ResultPayload.Should().NotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(result.ResultPayload!);
        doc.RootElement.GetProperty("pages").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsyncRejectsMissingUrl()
    {
        var service = new SiteCrawlerService(
            new FakePageFetcher(new Dictionary<string, string>()),
            new DeterministicSiteExtractor(),
            NullLogger<SiteCrawlerService>.Instance);
        var handler = new SiteCrawlSourceHandler(service, NullLogger<SiteCrawlSourceHandler>.Instance);

        var result = await handler.ExecuteAsync("req_1", "site_crawl_source", "{}", "{}", CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("start_url is required.");
    }

    private sealed class FakePageFetcher : IPageFetcher
    {
        private readonly Dictionary<string, string> _pages;

        public FakePageFetcher(Dictionary<string, string> pages)
        {
            _pages = pages;
        }

        public Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
        {
            return _pages.TryGetValue(uri.ToString(), out var html)
                ? Task.FromResult(PageFetchResult.Ok(uri, 200, "text/html", html))
                : Task.FromResult(PageFetchResult.Fail(uri, 404, "missing"));
        }
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SiteCrawlSourceHandlerTests
```

Expected: compile failure because `SiteCrawlSourceHandler` does not exist.

- [ ] **Step 3: Implement handler**

Create `packages/csharp/workers/site-crawler-worker/Handlers/SiteCrawlSourceHandler.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;
using WorkerSdk;

namespace SiteCrawlerWorker.Handlers;

public sealed class SiteCrawlSourceHandler : ICapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly SiteCrawlerService _crawler;
    private readonly ILogger<SiteCrawlSourceHandler> _logger;

    public SiteCrawlSourceHandler(SiteCrawlerService crawler, ILogger<SiteCrawlSourceHandler> logger)
    {
        _crawler = crawler;
        _logger = logger;
    }

    public string CapabilityId => "site.crawl_source";

    public async Task<(bool Success, string? ResultPayload, string? Error)> ExecuteAsync(
        string requestId,
        string route,
        string payload,
        string scope,
        CancellationToken ct)
    {
        try
        {
            var request = ParseRequest(requestId, payload);
            if (string.IsNullOrWhiteSpace(request.StartUrl))
                return (false, null, "start_url is required.");

            var result = await _crawler.CrawlAsync(request, ct);
            return (true, JsonSerializer.Serialize(result, JsonOptions), null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "site.crawl_source failed for request {RequestId}", requestId);
            return (false, null, ex.Message);
        }
    }

    private static SiteCrawlRequest ParseRequest(string requestId, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return new SiteCrawlRequest { RequestId = requestId };

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var source = root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object
            ? args
            : root;

        var request = source.Deserialize<SiteCrawlRequest>(JsonOptions) ?? new SiteCrawlRequest();
        request.RequestId = string.IsNullOrWhiteSpace(request.RequestId) ? requestId : request.RequestId;
        request.Scope ??= new SiteCrawlScope();
        request.Capture ??= new SiteCrawlCaptureOptions();
        request.Budgets ??= new SiteCrawlBudgets();
        return request;
    }
}
```

- [ ] **Step 4: Run tests and verify pass**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SiteCrawlSourceHandlerTests
```

Expected: all `SiteCrawlSourceHandlerTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add packages/csharp/workers/site-crawler-worker/Handlers/SiteCrawlSourceHandler.cs packages/csharp/tests/unit/Workers/SiteCrawler/SiteCrawlSourceHandlerTests.cs
git commit -m "feat: add site crawl source handler"
```

---

### Task 8: Worker Program

**Files:**
- Create: `packages/csharp/workers/site-crawler-worker/Program.cs`

- [ ] **Step 1: Add executable entry point**

Create `packages/csharp/workers/site-crawler-worker/Program.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiteCrawlerWorker.Handlers;
using SiteCrawlerWorker.Services;
using WorkerSdk;

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables("WORKER_")
    .AddCommandLine(args)
    .Build();

using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<WorkerHost>();
var workerAuthType = config.GetValue<string>("Worker:Auth:WorkerType") ?? "site-crawler-worker";
var workerAuthKeyId = config.GetValue<string>("Worker:Auth:KeyId") ?? string.Empty;
var workerAuthSharedSecret = config.GetValue<string>("Worker:Auth:SharedSecret") ?? string.Empty;
var userAgent = config.GetValue<string>("Crawler:UserAgent") ?? "Bricks4Agent-SiteCrawler/1.0";

var workerOptions = new WorkerHostOptions
{
    BrokerHost = config.GetValue<string>("Worker:BrokerHost") ?? "localhost",
    BrokerPort = config.GetValue("Worker:BrokerPort", 7000),
    WorkerId = config.GetValue<string>("Worker:WorkerId") ?? $"site-crawler-wkr-{Guid.NewGuid():N}"[..24],
    MaxConcurrent = config.GetValue("Worker:MaxConcurrent", 2),
    HeartbeatIntervalSeconds = config.GetValue("Worker:HeartbeatIntervalSeconds", 5),
    WorkerType = workerAuthType,
    WorkerAuthKeyId = workerAuthKeyId,
    WorkerAuthSharedSecret = workerAuthSharedSecret
};

var httpClient = new HttpClient
{
    Timeout = TimeSpan.FromSeconds(config.GetValue("Crawler:DefaultWallClockTimeoutSeconds", 180))
};
httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

var fetcher = new HttpPageFetcher(httpClient);
var extractor = new DeterministicSiteExtractor();
var crawler = new SiteCrawlerService(
    fetcher,
    extractor,
    loggerFactory.CreateLogger<SiteCrawlerService>());

var host = new WorkerHost(workerOptions, logger);
host.RegisterHandler(new SiteCrawlSourceHandler(
    crawler,
    loggerFactory.CreateLogger<SiteCrawlSourceHandler>()));

logger.LogInformation(
    "SiteCrawlerWorker starting: broker={Host}:{Port} capability=site.crawl_source",
    workerOptions.BrokerHost,
    workerOptions.BrokerPort);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
    logger.LogInformation("Shutdown signal received.");
};

await host.RunAsync(cts.Token);
```

- [ ] **Step 2: Build worker**

Run:

```powershell
dotnet build packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj
```

Expected: build succeeds.

- [ ] **Step 3: Commit**

```powershell
git add packages/csharp/workers/site-crawler-worker/Program.cs
git commit -m "feat: wire site crawler worker host"
```

---

### Task 9: Tool Spec Capability

**Files:**
- Create: `packages/csharp/broker/tool-specs/site.crawl.source/tool.json`
- Create: `packages/csharp/broker/tool-specs/site.crawl.source/TOOL.md`

- [ ] **Step 1: Add tool spec JSON**

Create `packages/csharp/broker/tool-specs/site.crawl.source/tool.json`:

```json
{
  "tool_id": "site.crawl.source",
  "display_name": "Site Crawl Source",
  "summary": "Safely crawl a public website path-depth scope and return source plus deterministic extraction output.",
  "kind": "crawler",
  "status": "active",
  "version": "2026-05-02",
  "tags": ["site", "crawler", "source", "path-depth", "generator"],
  "capability_template": {
    "action_type": "read",
    "resource_type": "web",
    "risk_level": "medium",
    "approval_policy": "auto",
    "ttl_seconds": 900,
    "audit_level": "summary",
    "quota": { "max_calls": 10, "per_window_seconds": 3600 }
  },
  "capability_bindings": [
    {
      "capability_id": "site.crawl_source",
      "route": "site_crawl_source",
      "purpose": "Broker-mediated path-depth website source capture for generator conversion."
    }
  ],
  "input_schema": {
    "type": "object",
    "properties": {
      "start_url": { "type": "string" },
      "scope": {
        "type": "object",
        "properties": {
          "kind": { "type": "string", "enum": ["path_depth"] },
          "max_depth": { "type": "integer" },
          "same_origin_only": { "type": "boolean" },
          "path_prefix_lock": { "type": "boolean" }
        },
        "required": ["kind", "max_depth"]
      },
      "budgets": {
        "type": "object",
        "properties": {
          "max_pages": { "type": "integer" },
          "max_total_bytes": { "type": "integer" },
          "max_asset_bytes": { "type": "integer" },
          "wall_clock_timeout_seconds": { "type": "integer" }
        }
      }
    },
    "required": ["start_url", "scope"]
  },
  "output_schema": {
    "type": "object",
    "properties": {
      "crawl_run_id": { "type": "string" },
      "status": { "type": "string" },
      "pages": { "type": "array" },
      "assets": { "type": "array" },
      "excluded": { "type": "array" },
      "extracted_model": { "type": "object" },
      "limits": { "type": "object" }
    }
  },
  "source_policy": {
    "allowed_sources": ["public_web"]
  },
  "execution_rules": {
    "runtime_required": "site_crawler_worker",
    "requires_user_scope_confirmation": true,
    "scope_model": "path_depth",
    "allow_private_network": false,
    "allow_authenticated_access": false
  },
  "response_contract": {
    "must_include_crawl_run_id": true,
    "must_include_excluded_urls": true,
    "must_mark_truncation": true
  }
}
```

- [ ] **Step 2: Add tool documentation**

Create `packages/csharp/broker/tool-specs/site.crawl.source/TOOL.md`:

```markdown
# Site Crawl Source

Safely crawls a public website path scope and returns raw page source plus deterministic extraction output.

## Capability

- Capability ID: `site.crawl_source`
- Route: `site_crawl_source`
- Risk: medium

## Scope Model

The caller must provide a confirmed path-depth scope before execution. Page count is only a safety budget, not the user's crawl intent.

## Safety

The worker rejects private network hosts, localhost, loopback, link-local addresses, unsupported schemes, and URLs outside the confirmed path prefix.
```

- [ ] **Step 3: Build broker to verify tool spec compiles into content**

Run:

```powershell
dotnet build packages/csharp/broker/Broker.csproj
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```powershell
git add packages/csharp/broker/tool-specs/site.crawl.source/tool.json packages/csharp/broker/tool-specs/site.crawl.source/TOOL.md
git commit -m "feat: register site crawl source capability"
```

---

### Task 10: Full Verification

**Files:**
- No new files.

- [ ] **Step 1: Run focused unit tests**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --filter SiteCrawler
```

Expected: all site crawler tests pass.

- [ ] **Step 2: Run worker build**

Run:

```powershell
dotnet build packages/csharp/workers/site-crawler-worker/SiteCrawlerWorker.csproj
```

Expected: build succeeds with 0 errors.

- [ ] **Step 3: Run solution build**

Run:

```powershell
dotnet build packages/csharp/ControlPlane.slnx
```

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Run existing unit test suite**

Run:

```powershell
dotnet test packages/csharp/tests/unit/Unit.Tests.csproj --no-restore
```

Expected: all unit tests pass.

- [ ] **Step 5: Inspect git status**

Run:

```powershell
git status --short
```

Expected: only unrelated pre-existing dirty files remain. No uncommitted site crawler implementation files should remain.

---

## Scope Boundary for the Next Plan

This Phase 1 plan stops after safe crawl and deterministic extraction. The next implementation plan should cover:

- Dynamic generator component registry.
- Component resolver.
- Generated component synthesizer.
- `site.convert_to_generator_json` worker.
- LINE `/clone` high-level intake and path-depth clarification.
