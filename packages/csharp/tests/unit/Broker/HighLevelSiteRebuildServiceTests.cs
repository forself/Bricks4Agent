using System.Net;
using System.Text;
using System.Text.Json;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;
using Microsoft.Extensions.Logging.Abstractions;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Broker;

public sealed class HighLevelSiteRebuildServiceTests : IDisposable
{
    private const int FakeLinkedPageCount = 35;
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"b4a-site-rebuild-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch (IOException)
            {
                // SQLite can briefly hold the file after disposal on Windows test runners.
            }
        }
    }

    [Fact]
    public async Task GenerateAndDeliverAsync_CrawlsPackagesUploadsAndReturnsDriveLink()
    {
        Directory.CreateDirectory(tempRoot);
        var dbPath = Path.Combine(tempRoot, "broker.db");
        using var db = BrokerDb.UseSqlite($"Data Source={dbPath}");
        new BrokerDbInitializer(db).Initialize();

        WriteProfile(db, "line-site-user");
        var googleClientPath = WriteOAuthClient();
        var googleHttp = new HttpClient(new FakeGoogleDriveHandler());
        var driveOptions = new GoogleDriveDeliveryOptions
        {
            OAuthClientJsonPath = googleClientPath,
            DefaultFolderId = "drive-folder",
            DefaultShareMode = "anyone_with_link",
            DefaultIdentityMode = "shared_delegated",
            SharedDelegatedChannel = "line",
            SharedDelegatedUserId = "shared-owner",
            DelegatedRedirectUri = "http://localhost/google/callback",
        };
        var oauth = new GoogleDriveOAuthService(db, driveOptions, googleHttp, NullLogger<GoogleDriveOAuthService>.Instance);
        var start = oauth.StartAuthorization("line", "shared-owner");
        var completed = await oauth.CompleteAuthorizationAsync(ExtractState(start.AuthorizationUrl), "fake-code", null);
        completed.Success.Should().BeTrue();

        var drive = new GoogleDriveShareService(driveOptions, oauth, googleHttp, NullLogger<GoogleDriveShareService>.Instance);
        var workspace = new HighLevelLineWorkspaceService(db, new HighLevelCoordinatorOptions
        {
            AccessRoot = Path.Combine(tempRoot, "managed"),
        });
        var delivery = new LineArtifactDeliveryService(workspace, drive, NullLogger<LineArtifactDeliveryService>.Instance);
        var service = new HighLevelSiteRebuildService(
            workspace,
            delivery,
            new FakePageFetcher(),
            NullLogger<HighLevelSiteRebuildService>.Instance);
        var draft = new HighLevelTaskDraft
        {
            DraftId = "draft-site",
            Channel = "line",
            UserId = "line-site-user",
            TaskType = "site_rebuild",
            ProjectName = "SiteCopy",
            ProjectFolderName = "sitecopy",
            OriginalMessage = "/重製網站 https://example.edu/ 深度3 #SiteCopy",
            Summary = "重製網站 https://example.edu/",
            ManagedPaths = new HighLevelManagedPaths
            {
                ProjectRoot = Path.Combine(tempRoot, "managed", "line", "line-site-user", "projects", "sitecopy"),
                DocumentsRoot = Path.Combine(tempRoot, "managed", "line", "line-site-user", "documents"),
            },
        };

        var result = await service.GenerateAndDeliverAsync(draft, new HighLevelUserProfile
        {
            Channel = "line",
            UserId = "line-site-user",
        }, "task-site");

        result.Success.Should().BeTrue(result.Message);
        result.PagesCrawled.Should().Be(FakeLinkedPageCount + 1);
        result.RoutesGenerated.Should().Be(FakeLinkedPageCount + 1);
        result.PackageFileName.Should().EndWith(".zip");
        File.Exists(result.PackageFilePath).Should().BeTrue();
        result.Delivery?.GoogleDrive?.Success.Should().BeTrue();
        result.Delivery?.GoogleDrive?.DownloadLink.Should().Contain("drive.google.com");
        workspace.ListArtifacts("line-site-user")
            .Should()
            .ContainSingle(item => item.RelatedTaskType == "site_rebuild" && item.UploadedToGoogleDrive);
    }

    private string WriteOAuthClient()
    {
        var path = Path.Combine(tempRoot, "oauth-client.json");
        File.WriteAllText(path, """
            {
              "installed": {
                "client_id": "fake-client",
                "project_id": "fake-project",
                "auth_uri": "https://accounts.google.com/o/oauth2/v2/auth",
                "token_uri": "https://oauth2.googleapis.com/token",
                "client_secret": "fake-secret"
              }
            }
            """, Encoding.UTF8);
        return path;
    }

    private static void WriteProfile(BrokerDb db, string userId)
    {
        db.Insert(new SharedContextEntry
        {
            EntryId = "ctx-profile",
            DocumentId = $"hlm.profile.line.{userId}",
            Version = 1,
            Key = $"hlm.profile.line.{userId}",
            ContentRef = JsonSerializer.Serialize(new HighLevelUserProfile
            {
                Channel = "line",
                UserId = userId,
            }),
            ContentType = "application/json",
            Acl = """{"read":["*"],"write":["system:test"]}""",
            AuthorPrincipalId = "system:test",
            TaskId = "global",
            CreatedAt = DateTime.UtcNow,
        });
    }

    private static string ExtractState(string authorizationUrl)
    {
        var uri = new Uri(authorizationUrl);
        return uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .Where(parts => parts[0] == "state")
            .Select(parts => Uri.UnescapeDataString(parts[1]))
            .Single();
    }

    private sealed class FakePageFetcher : IPageFetcher
    {
        public Task<PageFetchResult> FetchAsync(Uri uri, CancellationToken ct)
            => FetchAsync(uri, long.MaxValue, ct);

        public Task<PageFetchResult> FetchAsync(Uri uri, long maxBytes, CancellationToken ct)
        {
            var html = uri.AbsolutePath switch
            {
                "/" => BuildRootHtml(),
                _ => string.Empty,
            };
            if (string.IsNullOrWhiteSpace(html) &&
                uri.AbsolutePath.StartsWith("/page-", StringComparison.OrdinalIgnoreCase))
            {
                html = $"""
                    <!doctype html><html><head><title>{uri.AbsolutePath.Trim('/')}</title></head>
                    <body><main><section><h1>{uri.AbsolutePath.Trim('/')}</h1><p>Generated page.</p></section></main></body></html>
                    """;
            }

            return Task.FromResult(string.IsNullOrWhiteSpace(html)
                ? PageFetchResult.Fail(uri, 404, "not_found")
                : PageFetchResult.Ok(uri, 200, "text/html", html));
        }

        private static string BuildRootHtml()
        {
            var links = string.Concat(Enumerable.Range(1, FakeLinkedPageCount)
                .Select(index => $"""<a href="/page-{index:00}">Page {index:00}</a>"""));
            return $"""
                <!doctype html><html><head><title>Example University</title></head>
                <body><header>{links}</header>
                <main><section><h1>Example University</h1><p>Welcome.</p></section></main></body></html>
                """;
        }
    }

    private sealed class FakeGoogleDriveHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (url.Contains("oauth2.googleapis.com/token", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new
                {
                    access_token = "fake-access-token",
                    refresh_token = "fake-refresh-token",
                    scope = "https://www.googleapis.com/auth/drive.file",
                });
            }

            if (url.Contains("oauth2/v2/userinfo", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { email = "owner@example.com" });
            }

            if (url.Contains("/upload/drive/v3/files", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new
                {
                    id = "drive-site-package",
                    name = "sitecopy-site-package.zip",
                    mimeType = "application/octet-stream",
                    webViewLink = "https://drive.google.com/file/d/drive-site-package/view",
                    webContentLink = "https://drive.google.com/uc?id=drive-site-package&export=download",
                });
            }

            if (url.Contains("/permissions", StringComparison.OrdinalIgnoreCase))
            {
                return Json(new { id = "permission-id" });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}"),
            });
        }

        private static Task<HttpResponseMessage> Json(object body)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            });
        }
    }
}
