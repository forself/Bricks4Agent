using BrokerCore.Contracts;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;
using Broker.Services;
using FluentAssertions;
using Xunit;

namespace Unit.Tests.Core;

public class AzureIisDeploymentRollbackTests
{
    // ── Script builder: backup + rollback are deterministic, test directly ──

    [Fact]
    public void DeployScript_BacksUpBeforeOverwrite()
    {
        var script = AzureIisPowerShellScriptBuilder.Build(SampleRequest());

        script.Should().Contain("_b4a_backup");
        // backup copy must appear before the cleanup remove
        var backupIdx = script.IndexOf("Copy-Item -Destination $backupPath", StringComparison.Ordinal);
        var cleanupIdx = script.IndexOf("Get-ChildItem -Path $physicalPath -Force | Remove-Item", StringComparison.Ordinal);
        backupIdx.Should().BeGreaterThan(0);
        cleanupIdx.Should().BeGreaterThan(backupIdx);
    }

    [Fact]
    public void RollbackScript_RestoresBackupAndRestartsPool()
    {
        var script = AzureIisPowerShellScriptBuilder.BuildRollback(SampleRequest());

        script.Should().Contain("if (!(Test-Path $backupPath)) { throw");
        script.Should().Contain("Copy-Item -Destination $physicalPath");
        script.Should().Contain("Start-WebAppPool");
        // must stop the pool before restoring
        var stopIdx = script.IndexOf("Stop-WebAppPool", StringComparison.Ordinal);
        var restoreIdx = script.IndexOf("Copy-Item -Destination $physicalPath", StringComparison.Ordinal);
        stopIdx.Should().BeGreaterThan(0);
        restoreIdx.Should().BeGreaterThan(stopIdx);
    }

    // ── Execution: deploy failure triggers rollback (same path as health fail) ──

    [Fact]
    public async Task RemoteDeployFailure_TriggersRollback()
    {
        var runner = new ScriptedRunner
        {
            DeployExitCode = 1,
            RollbackExitCode = 0
        };
        var (service, _) = BuildService(runner);

        var envelope = await service.ExecuteAsync("deploy.azure-vm-iis", BuildInput(), dryRun: false, CancellationToken.None);

        envelope.Success.Should().BeFalse();
        envelope.Result!.Stage.Should().Be("remote_deploy_rolled_back");
        runner.RanRollback.Should().BeTrue();
    }

    [Fact]
    public async Task RollbackFailure_IsReportedDistinctly()
    {
        var runner = new ScriptedRunner
        {
            DeployExitCode = 1,
            RollbackExitCode = 2
        };
        var (service, _) = BuildService(runner);

        var envelope = await service.ExecuteAsync("deploy.azure-vm-iis", BuildInput(), dryRun: false, CancellationToken.None);

        envelope.Result!.Stage.Should().Be("remote_deploy_rollback_failed");
        runner.RanRollback.Should().BeTrue();
    }

    [Fact]
    public async Task SuccessfulDeployWithoutHealthCheck_DoesNotRollBack()
    {
        var runner = new ScriptedRunner
        {
            DeployExitCode = 0
        };
        var (service, _) = BuildService(runner);

        var envelope = await service.ExecuteAsync("deploy.azure-vm-iis", BuildInput(), dryRun: false, CancellationToken.None);

        envelope.Success.Should().BeTrue();
        envelope.Result!.Stage.Should().Be("deployed");
        runner.RanRollback.Should().BeFalse();
    }

    // ── helpers ──

    private static AzureIisDeploymentRequest SampleRequest() => new()
    {
        RequestId = "req_test",
        TargetId = "target_test",
        VmHost = "vm.example.com",
        SiteName = "Default Web Site",
        DeploymentMode = "site_root",
        PhysicalPath = "C:\\inetpub\\wwwroot\\app",
        AppPoolName = "AppPool",
        RestartSite = true,
        CleanupTarget = true
    };

    private (AzureIisDeploymentExecutionService Service, string PublishRoot) BuildService(ScriptedRunner runner)
    {
        var publishRoot = Path.Combine(Path.GetTempPath(), $"b4a-deploy-{Guid.NewGuid():N}");
        var publishOutput = Path.Combine(publishRoot, "publish");
        Directory.CreateDirectory(publishOutput);
        File.WriteAllText(Path.Combine(publishOutput, "app.dll"), "binary");

        var request = SampleRequest();
        request.ProjectFile = Path.Combine(publishRoot, "App.csproj");
        request.PublishOutputPath = publishOutput;
        request.PackagePath = Path.Combine(publishRoot, "deploy.zip");
        request.SecretRef = "secret/test";
        request.PrincipalId = "system:test";
        request.TaskId = "global";
        // no health check path -> health check skipped

        var db = new BrokerDb("Data Source=:memory:");
        var service = new AzureIisDeploymentExecutionService(
            new FakeBuilder(request),
            new FakeSecretResolver(),
            runner,
            new AzureIisDeploymentHealthCheckService(new HttpClient()),
            new FakeSharedContext(),
            db);
        return (service, publishRoot);
    }

    private static AzureIisDeploymentBuildInput BuildInput() => new();

    private sealed class ScriptedRunner : IProcessRunner
    {
        public int DeployExitCode { get; set; }
        public int RollbackExitCode { get; set; }
        public bool RanRollback { get; private set; }

        public Task<ProcessRunResult> RunAsync(ProcessRunSpec spec, CancellationToken cancellationToken = default)
        {
            var args = spec.Arguments ?? string.Empty;
            // dotnet publish
            if (string.Equals(spec.FileName, "dotnet", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new ProcessRunResult { ExitCode = 0 });

            if (args.Contains("rollback.ps1", StringComparison.OrdinalIgnoreCase))
            {
                RanRollback = true;
                return Task.FromResult(new ProcessRunResult { ExitCode = RollbackExitCode, StandardError = RollbackExitCode == 0 ? "" : "rollback boom" });
            }

            // deploy.ps1
            return Task.FromResult(new ProcessRunResult { ExitCode = DeployExitCode, StandardError = DeployExitCode == 0 ? "" : "deploy boom" });
        }
    }

    private sealed class FakeBuilder : IAzureIisDeploymentRequestBuilder
    {
        private readonly AzureIisDeploymentRequest _request;
        public FakeBuilder(AzureIisDeploymentRequest request) => _request = request;
        public AzureIisDeploymentRequestBuildResult TryBuild(string toolId, AzureIisDeploymentBuildInput input)
            => AzureIisDeploymentRequestBuildResult.Ok(_request);
    }

    private sealed class FakeSecretResolver : IAzureIisDeploymentSecretResolver
    {
        public AzureIisDeploymentSecret? Resolve(string secretRef)
            => new() { UserName = "deployer", Password = "pw" };
    }

    private sealed class FakeSharedContext : ISharedContextService
    {
        public SharedContextEntry Write(string authorPrincipalId, string documentId, string key,
            string contentRef, string contentType, string acl, string? taskId)
            => new() { DocumentId = documentId, Key = key };
        public SharedContextEntry? ReadLatest(string documentId, string readerPrincipalId) => null;
        public SharedContextEntry? ReadByKey(string key, string? taskId, string readerPrincipalId) => null;
        public List<SharedContextEntry> ListVersions(string documentId, string readerPrincipalId) => new();
        public List<SharedContextEntry> ListByTask(string taskId, string readerPrincipalId) => new();
    }
}
