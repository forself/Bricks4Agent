using BrokerCore.Services;
using FluentAssertions;

namespace Unit.Tests.Core;

public class WorkerIdentityAuthServiceTests
{
    private static WorkerIdentityAuthOptions BuildOptions() => new()
    {
        Enforce = true,
        ClockSkewSeconds = 300,
        Credentials =
        [
            new WorkerCredentialRecord
            {
                WorkerType = "line-worker",
                KeyId = "line-v1",
                SharedSecret = "line-secret",
                Status = "active"
            },
            new WorkerCredentialRecord
            {
                WorkerType = "file-worker",
                KeyId = "file-v1",
                SharedSecret = "file-secret",
                Status = "active"
            }
        ],
        HttpRoutes =
        [
            new WorkerRouteRule
            {
                WorkerType = "line-worker",
                Paths = ["/api/v1/high-level/line/process"]
            }
        ]
    };

    [Fact]
    public void ValidateHttpRequest_ValidSignature_ReturnsAuthorized()
    {
        var nonceStore = new WorkerAuthNonceStore();
        var sut = new WorkerIdentityAuthService(BuildOptions(), nonceStore);
        var body = """{"user_id":"U1","message":"hello"}""";
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var signature = sut.SignHttp(
            "line-worker",
            "line-v1",
            "line-secret",
            "POST",
            "/api/v1/high-level/line/process",
            body,
            timestamp,
            nonce);

        var result = sut.ValidateHttpRequest(new WorkerHttpAuthRequest
        {
            WorkerType = "line-worker",
            KeyId = "line-v1",
            Method = "POST",
            Path = "/api/v1/high-level/line/process",
            Body = body,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = signature
        });

        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void ValidateHttpRequest_UnknownKeyId_ReturnsDenied()
    {
        var nonceStore = new WorkerAuthNonceStore();
        var sut = new WorkerIdentityAuthService(BuildOptions(), nonceStore);

        var result = sut.ValidateHttpRequest(new WorkerHttpAuthRequest
        {
            WorkerType = "line-worker",
            KeyId = "missing",
            Method = "POST",
            Path = "/api/v1/high-level/line/process",
            Body = "{}",
            Timestamp = DateTimeOffset.UtcNow,
            Nonce = Guid.NewGuid().ToString("N"),
            Signature = "invalid"
        });

        result.IsAuthorized.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public void ValidateHttpRequest_ExpiredTimestamp_ReturnsDenied()
    {
        var nonceStore = new WorkerAuthNonceStore();
        var sut = new WorkerIdentityAuthService(BuildOptions(), nonceStore);
        var body = "{}";
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10);
        var nonce = Guid.NewGuid().ToString("N");
        var signature = sut.SignHttp(
            "line-worker",
            "line-v1",
            "line-secret",
            "POST",
            "/api/v1/high-level/line/process",
            body,
            timestamp,
            nonce);

        var result = sut.ValidateHttpRequest(new WorkerHttpAuthRequest
        {
            WorkerType = "line-worker",
            KeyId = "line-v1",
            Method = "POST",
            Path = "/api/v1/high-level/line/process",
            Body = body,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = signature
        });

        result.IsAuthorized.Should().BeFalse();
        result.StatusCode.Should().Be(401);
    }

    [Fact]
    public void ValidateHttpRequest_ReplayedNonce_ReturnsDenied()
    {
        var nonceStore = new WorkerAuthNonceStore();
        var sut = new WorkerIdentityAuthService(BuildOptions(), nonceStore);
        var body = "{}";
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var signature = sut.SignHttp(
            "line-worker",
            "line-v1",
            "line-secret",
            "POST",
            "/api/v1/high-level/line/process",
            body,
            timestamp,
            nonce);

        var request = new WorkerHttpAuthRequest
        {
            WorkerType = "line-worker",
            KeyId = "line-v1",
            Method = "POST",
            Path = "/api/v1/high-level/line/process",
            Body = body,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = signature
        };

        sut.ValidateHttpRequest(request).IsAuthorized.Should().BeTrue();
        var replay = sut.ValidateHttpRequest(request);

        replay.IsAuthorized.Should().BeFalse();
        replay.StatusCode.Should().Be(401);
    }

    [Fact]
    public void ValidateHttpRequest_DisallowedRoute_ReturnsForbidden()
    {
        var nonceStore = new WorkerAuthNonceStore();
        var sut = new WorkerIdentityAuthService(BuildOptions(), nonceStore);
        var body = "{}";
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var signature = sut.SignHttp(
            "file-worker",
            "file-v1",
            "file-secret",
            "POST",
            "/api/v1/high-level/line/process",
            body,
            timestamp,
            nonce);

        var result = sut.ValidateHttpRequest(new WorkerHttpAuthRequest
        {
            WorkerType = "file-worker",
            KeyId = "file-v1",
            Method = "POST",
            Path = "/api/v1/high-level/line/process",
            Body = body,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = signature
        });

        result.IsAuthorized.Should().BeFalse();
        result.StatusCode.Should().Be(403);
    }

    [Fact]
    public void ValidateWorkerRegister_ValidSignature_ReturnsAuthorized()
    {
        var nonceStore = new WorkerAuthNonceStore();
        var sut = new WorkerIdentityAuthService(BuildOptions(), nonceStore);
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var signature = sut.SignWorkerRegister(
            "file-worker",
            "file-v1",
            "file-secret",
            "file-wkr-01",
            ["file.read", "file.write"],
            4,
            timestamp,
            nonce);

        var result = sut.ValidateWorkerRegister(new WorkerRegisterAuthRequest
        {
            WorkerType = "file-worker",
            KeyId = "file-v1",
            WorkerId = "file-wkr-01",
            Capabilities = ["file.read", "file.write"],
            MaxConcurrent = 4,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = signature
        });

        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void ValidateWorkerRegister_ReplayedNonce_ReturnsDenied()
    {
        var nonceStore = new WorkerAuthNonceStore();
        var sut = new WorkerIdentityAuthService(BuildOptions(), nonceStore);
        var timestamp = DateTimeOffset.UtcNow;
        var nonce = Guid.NewGuid().ToString("N");
        var signature = sut.SignWorkerRegister(
            "file-worker",
            "file-v1",
            "file-secret",
            "file-wkr-02",
            ["file.read"],
            2,
            timestamp,
            nonce);

        var request = new WorkerRegisterAuthRequest
        {
            WorkerType = "file-worker",
            KeyId = "file-v1",
            WorkerId = "file-wkr-02",
            Capabilities = ["file.read"],
            MaxConcurrent = 2,
            Timestamp = timestamp,
            Nonce = nonce,
            Signature = signature
        };

        sut.ValidateWorkerRegister(request).IsAuthorized.Should().BeTrue();
        var replay = sut.ValidateWorkerRegister(request);

        replay.IsAuthorized.Should().BeFalse();
        replay.StatusCode.Should().Be(401);
    }
}
