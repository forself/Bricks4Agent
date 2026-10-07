using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Broker.Services;
using BrokerCore.Data;
using BrokerCore.Models;
using FluentAssertions;
using Integration.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Tests.Api;

/// <summary>
/// /proj 起手與 /ok 的 AllowProduction 權限閘、/ok 改建立 draft 走 ConfirmDraft，
/// 以及高階回覆不帶主機絕對路徑。
/// </summary>
public class ProjectInterviewGateTests : IClassFixture<BrokerFixture>
{
    private const string ProductionDeniedReply = "目前你的帳戶不能建立 production 任務";
    private readonly BrokerFixture _fixture;

    public ProjectInterviewGateTests(BrokerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ProjectInterview_BasicUserStart_IsDenied()
    {
        var userId = $"line-proj-basic-{Guid.NewGuid():N}";
        using (await _fixture.SendHighLevelLineTextAsync("hello", userId)) { }

        // Basic 層即使原始旗標打開也一律遮罩
        WithCoordinator(coordinator => coordinator.SetLineUserPermissions(userId, new HighLevelUserPermissionsPatch
        {
            AllowProduction = true
        }));

        using var start = await _fixture.SendHighLevelLineTextAsync("/proj", userId);
        var data = start.RootElement.GetProperty("data");

        data.GetProperty("error").GetString().Should().Be("production_disabled");
        data.GetProperty("reply").GetString().Should().Contain(ProductionDeniedReply)
            .And.NotContain("專案訪談已開始");

        var state = await _fixture.ReadProjectInterviewRequirementsAsync("line", userId);
        state.IsActiveSession.Should().BeFalse();
        state.SessionState.CurrentPhase.Should().Be(ProjectInterviewPhase.Idle);
    }

    [Fact]
    public async Task ProjectInterview_BasicUserApprove_IsDenied()
    {
        var basicUserId = $"line-proj-basic-ok-{Guid.NewGuid():N}";
        using (await _fixture.SendHighLevelLineTextAsync("hello", basicUserId)) { }
        using var direct = await _fixture.SendHighLevelLineTextAsync("/ok", basicUserId);
        direct.RootElement.GetProperty("data").GetProperty("error").GetString().Should().Be("production_disabled");

        // 走到審查階段後被降為 Basic：/ok 不得建立 draft
        var demotedUserId = $"line-proj-demoted-{Guid.NewGuid():N}";
        using (await _fixture.CompleteProjectInterviewToReviewAsync(demotedUserId)) { }
        WithCoordinator(coordinator => coordinator.ReviewLineUserRegistration(demotedUserId, "demote"));

        using var approve = await _fixture.SendHighLevelLineTextAsync("/ok", demotedUserId);
        var data = approve.RootElement.GetProperty("data");

        data.GetProperty("error").GetString().Should().Be("production_disabled");
        data.GetProperty("reply").GetString().Should().Contain(ProductionDeniedReply);
        IsNullOrMissing(data, "draft").Should().BeTrue();

        var state = await _fixture.ReadProjectInterviewReviewAsync("line", demotedUserId);
        state.SessionState.CurrentPhase.Should().Be(ProjectInterviewPhase.AwaitUserReview);
        WithCoordinator(coordinator => coordinator.GetLineDraft(demotedUserId)).Should().BeNull();
    }

    [Fact]
    public async Task ProjectInterview_Approve_CreatesDraft_ThenConfirmCreatesTask()
    {
        var userId = $"line-proj-approve-{Guid.NewGuid():N}";
        using (await _fixture.CompleteProjectInterviewToReviewAsync(userId)) { }

        using var approve = await _fixture.SendHighLevelLineTextAsync("/ok", userId);
        var approveData = approve.RootElement.GetProperty("data");

        approveData.GetProperty("mode").GetString().Should().Be("production");
        IsNullOrMissing(approveData, "error").Should().BeTrue();
        IsNullOrMissing(approveData, "created_task").Should().BeTrue();
        approveData.GetProperty("reply").GetString().Should().Contain("已建立系統雛形 draft").And.Contain("y");
        approveData.GetProperty("follow_up_messages").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("y");

        var draft = approveData.GetProperty("draft");
        GetProperty(draft, "taskType").GetString().Should().Be("system_scaffold");
        GetProperty(draft, "projectName").GetString().Should().StartWith("AlphaPortal");
        var projectRoot = GetProperty(GetProperty(draft, "managedPaths"), "projectRoot").GetString();
        projectRoot.Should().NotBeNullOrWhiteSpace();
        Directory.Exists(projectRoot).Should().BeFalse("/ok 只建立 draft，回 y 之前不建置");

        var interview = await _fixture.ReadProjectInterviewReviewAsync("line", userId);
        interview.SessionState.CurrentPhase.Should().Be(ProjectInterviewPhase.Confirmed);
        WithCoordinator(coordinator => coordinator.GetLineDraft(userId)).Should().NotBeNull();

        using var confirm = await _fixture.SendHighLevelLineTextAsync("y", userId);
        var confirmData = confirm.RootElement.GetProperty("data");

        var task = confirmData.GetProperty("created_task");
        task.ValueKind.Should().Be(JsonValueKind.Object);
        GetProperty(task, "taskType").GetString().Should().Be("system_scaffold");
        var taskId = GetProperty(task, "taskId").GetString();
        taskId.Should().NotBeNullOrWhiteSpace();
        confirmData.GetProperty("created_plan").ValueKind.Should().Be(JsonValueKind.Object);
        GetProperty(confirmData.GetProperty("handoff"), "taskId").GetString().Should().Be(taskId);
        confirmData.GetProperty("reply").GetString().Should().Contain("已確認任務");

        WithCoordinator(coordinator => coordinator.GetLineDraft(userId)).Should().BeNull();
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BrokerDb>();
            db.Get<BrokerTask>(taskId!).Should().NotBeNull();
            db.Query<SharedContextEntry>(
                    "SELECT * FROM shared_context_entries WHERE document_id = @docId",
                    new { docId = $"hlm.handoff.{taskId}" })
                .Should().NotBeEmpty();
            db.Query<SharedContextEntry>(
                    "SELECT * FROM shared_context_entries WHERE document_id = @docId",
                    new { docId = HighLevelExecutionIntentStore.BuildDocumentId("line", userId) })
                .Should().NotBeEmpty("升格閘通過後才會寫入 execution intent");
        }
    }

    [Fact]
    public async Task ProjectInterview_Approve_WhenWorkspaceHasSameProject_AsksForNewName()
    {
        var userId = $"line-proj-collide-{Guid.NewGuid():N}";
        using (await _fixture.CompleteProjectInterviewToReviewAsync(userId)) { }
        var review = await _fixture.ReadProjectInterviewReviewAsync("line", userId);
        var folder = review.SessionState.ProjectFolderName;
        folder.Should().NotBeNullOrWhiteSpace();
        Directory.CreateDirectory(Path.Combine(_fixture.AccessRoot, "line", userId, "projects", folder!));

        using var approve = await _fixture.SendHighLevelLineTextAsync("/ok", userId);
        var approveData = approve.RootElement.GetProperty("data");
        approveData.GetProperty("reply").GetString().Should().Contain("同名專案");
        IsNullOrMissing(approveData, "created_task").Should().BeTrue();
        var draft = WithCoordinator(coordinator => coordinator.GetLineDraft(userId));
        draft.Should().NotBeNull();
        draft!.TaskType.Should().Be("system_scaffold");
        draft.ProjectName.Should().BeNull();

        var newName = $"RenamedPortal{Guid.NewGuid():N}";
        using var named = await _fixture.SendHighLevelLineTextAsync($"#{newName}", userId);
        GetProperty(named.RootElement.GetProperty("data").GetProperty("draft"), "projectName").GetString().Should().Be(newName);

        using var confirm = await _fixture.SendHighLevelLineTextAsync("y", userId);
        var task = confirm.RootElement.GetProperty("data").GetProperty("created_task");
        task.ValueKind.Should().Be(JsonValueKind.Object);
        GetProperty(task, "taskType").GetString().Should().Be("system_scaffold");
    }

    [Fact]
    public async Task HighLevelReplies_DoNotExposeHostPaths()
    {
        var userId = $"line-proj-paths-{Guid.NewGuid():N}";
        var replies = new List<string>();
        async Task SendAsync(string message)
        {
            using var response = await _fixture.SendHighLevelLineTextAsync(message, userId);
            CollectReplies(response.RootElement.GetProperty("data"), replies);
        }

        await _fixture.EnableLineProductionAsync(userId);
        // 斷言要有意義：coordinator 實際使用的根目錄必須就是 fixture 的暫存根目錄
        var actualRoot = WithCoordinator(coordinator => coordinator.GetLineManagedPaths(userId, ensureExists: false))!.AccessRoot;
        Path.GetFullPath(actualRoot).Should().Be(Path.GetFullPath(_fixture.AccessRoot));

        await SendAsync("?profile");
        await SendAsync("/name 路徑檢查");
        await SendAsync($"/id pathcheck{Guid.NewGuid():N}"[..20]);
        await SendAsync("/proj");
        await SendAsync($"#PathInterview{Guid.NewGuid():N}");
        await SendAsync("2");
        await SendAsync("3");
        await SendAsync("/ok");
        await SendAsync("y");
        await SendAsync($"/建立 完整系統雛形 #pathscaffold{Guid.NewGuid():N}");
        await SendAsync("y");
        await SendAsync("?profile");

        replies.Should().NotBeEmpty();
        replies.Should().Contain(reply => reply.Contains("已生成並封裝系統雛形", StringComparison.Ordinal));
        replies.Should().Contain(reply => reply.Contains("workspace: line/", StringComparison.Ordinal));
        AssertNoHostPath(replies);
    }

    [Fact]
    public async Task PortalReplies_DoNotExposeHostPaths()
    {
        var userId = $"portal-paths-{Guid.NewGuid():N}"[..30];
        using var register = await _fixture.Client.PostAsJsonAsync("/api/v1/portal/auth/register", new
        {
            user_id = userId,
            password = "correct-horse-battery",
            display_name = "Portal Path User"
        });
        register.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = ReadPortalCookie(register);
        await _fixture.EnableLineProductionAsync(userId);

        var replies = new List<string>();
        foreach (var message in new[] { "?profile", "/建立 完整系統雛形 #portalpaths", "y" })
        {
            using var command = new HttpRequestMessage(HttpMethod.Post, "/api/v1/portal/commands")
            {
                Content = JsonContent.Create(new { message })
            };
            command.Headers.Add("Cookie", cookie);
            using var response = await _fixture.Client.SendAsync(command);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            CollectReplies(json.RootElement.GetProperty("data").GetProperty("result"), replies);
        }

        using var results = new HttpRequestMessage(HttpMethod.Get, "/api/v1/portal/results?limit=20");
        results.Headers.Add("Cookie", cookie);
        using var resultsResponse = await _fixture.Client.SendAsync(results);
        resultsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var resultsJson = JsonDocument.Parse(await resultsResponse.Content.ReadAsStringAsync());
        foreach (var item in resultsJson.RootElement.GetProperty("data").GetProperty("items").EnumerateArray())
        {
            replies.Add(item.GetProperty("reply").GetString() ?? string.Empty);
        }

        replies.Should().Contain(reply => reply.Contains("已生成並封裝系統雛形", StringComparison.Ordinal));
        AssertNoHostPath(replies);
    }

    [Fact]
    public void ArtifactReplyBuilders_UseRelativeNames()
    {
        var projectRoot = Path.Combine(_fixture.AccessRoot, "line", "builder-user", "projects", "demo");
        var packagePath = Path.Combine(_fixture.AccessRoot, "line", "builder-user", "documents", "demo-scaffold.zip");

        var scaffold = HighLevelCoordinator.BuildSystemScaffoldReply(new HighLevelSystemScaffoldResult
        {
            Success = true,
            ProjectRoot = projectRoot,
            PackageFilePath = packagePath
        });
        var code = HighLevelCoordinator.BuildCodeArtifactReply(new HighLevelCodeArtifactResult
        {
            Success = true,
            ProjectRoot = projectRoot,
            EntryFilePath = Path.Combine(projectRoot, "index.html"),
            PackageFilePath = packagePath
        });
        var failed = HighLevelCoordinator.BuildSystemScaffoldReply(new HighLevelSystemScaffoldResult
        {
            Success = false,
            Message = $"cannot write {Path.Combine(projectRoot, "frontend", "app.js")}"
        });

        scaffold.Should().Contain("project_folder: demo").And.Contain("package_file: demo-scaffold.zip");
        code.Should().Contain("project_folder: demo").And.Contain("entry_file: index.html");
        failed.Should().Contain("app.js");
        AssertNoHostPath(new[] { scaffold, code, failed });
    }

    private void AssertNoHostPath(IEnumerable<string> replies)
    {
        var root = _fixture.AccessRoot.TrimEnd('\\', '/');
        var forbidden = new[]
        {
            root,
            root.Replace('\\', '/'),
            Path.GetTempPath().TrimEnd('\\', '/'),
            Path.GetTempPath().TrimEnd('\\', '/').Replace('\\', '/')
        };

        foreach (var reply in replies)
        {
            foreach (var value in forbidden)
            {
                reply.Should().NotContainEquivalentOf(value, "高階回覆不得帶主機絕對路徑");
            }
        }
    }

    private static void CollectReplies(JsonElement data, List<string> replies)
    {
        if (data.TryGetProperty("reply", out var reply) && reply.ValueKind == JsonValueKind.String)
            replies.Add(reply.GetString() ?? string.Empty);

        if (data.TryGetProperty("follow_up_messages", out var followUps) && followUps.ValueKind == JsonValueKind.Array)
            replies.AddRange(followUps.EnumerateArray().Select(item => item.GetString() ?? string.Empty));
    }

    private T WithCoordinator<T>(Func<HighLevelCoordinator, T> action)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        return action(scope.ServiceProvider.GetRequiredService<HighLevelCoordinator>());
    }

    private static JsonElement GetProperty(JsonElement element, string camelCaseName)
    {
        if (element.TryGetProperty(camelCaseName, out var value))
            return value;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, camelCaseName, StringComparison.OrdinalIgnoreCase))
                return property.Value;
        }

        throw new KeyNotFoundException(camelCaseName);
    }

    private static bool IsNullOrMissing(JsonElement element, string name)
        => !element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null;

    private static string ReadPortalCookie(HttpResponseMessage response)
    {
        response.Headers.TryGetValues("Set-Cookie", out var values).Should().BeTrue();
        var cookie = values!.FirstOrDefault(value => value.StartsWith($"{PortalAuthService.SessionCookieName}=", StringComparison.Ordinal));
        cookie.Should().NotBeNullOrWhiteSpace();
        return cookie!.Split(';', 2)[0];
    }
}
