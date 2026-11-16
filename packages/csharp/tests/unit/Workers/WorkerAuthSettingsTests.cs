using FluentAssertions;
using WorkerSdk;
using Xunit;

namespace Unit.Tests.Workers;

public sealed class WorkerAuthSettingsTests
{
    [Fact]
    public void Explicit_unauthenticated_flag_clears_even_configured_appsettings_credentials()
    {
        var auth = WorkerAuthSettings.Resolve(
            allowUnauthenticated: true,
            workerType: "site-crawler-worker",
            defaultWorkerType: "site-crawler-worker",
            keyId: "configured-key",
            sharedSecret: "configured-secret",
            defaultKeyId: "placeholder-key",
            defaultSharedSecret: "placeholder-secret",
            whitespaceUsesFallback: true);

        auth.WorkerType.Should().Be("site-crawler-worker");
        auth.KeyId.Should().BeEmpty();
        auth.SharedSecret.Should().BeEmpty();
    }

    [Fact]
    public void Without_opt_in_configured_credentials_are_preserved()
    {
        var auth = WorkerAuthSettings.Resolve(
            allowUnauthenticated: false,
            workerType: null,
            defaultWorkerType: "file-worker",
            keyId: "configured-key",
            sharedSecret: "configured-secret");

        auth.WorkerType.Should().Be("file-worker");
        auth.KeyId.Should().Be("configured-key");
        auth.SharedSecret.Should().Be("configured-secret");
    }

    [Fact]
    public void Site_crawler_keeps_its_fail_closed_placeholder_fallback_without_opt_in()
    {
        var auth = WorkerAuthSettings.Resolve(
            allowUnauthenticated: false,
            workerType: null,
            defaultWorkerType: "site-crawler-worker",
            keyId: "",
            sharedSecret: " ",
            defaultKeyId: "placeholder-key",
            defaultSharedSecret: "placeholder-secret",
            whitespaceUsesFallback: true);

        auth.KeyId.Should().Be("placeholder-key");
        auth.SharedSecret.Should().Be("placeholder-secret");
    }
}
