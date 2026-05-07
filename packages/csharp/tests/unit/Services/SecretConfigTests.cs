using BrokerCore.Services;
using Microsoft.Extensions.Configuration;

namespace Unit.Tests.Services;

/// <summary>
/// SecretConfig.GetSecret 解析優先順序：
///   1. {key}File 設定值 → 讀檔
///   2. {key} 直接值
///   3. null
/// 還測試 trim 行為（Docker secrets 檔案常帶 trailing newline）。
/// </summary>
public class SecretConfigTests
{
    private static IConfiguration Build(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    [Fact]
    public void GetSecret_WhenOnlyDirectValue_ReturnsDirectValue()
    {
        var config = Build(new() { ["Foo:ApiKey"] = "direct-value" });

        config.GetSecret("Foo:ApiKey").Should().Be("direct-value");
    }

    [Fact]
    public void GetSecret_WhenNothingSet_ReturnsNull()
    {
        var config = Build(new());

        config.GetSecret("Foo:ApiKey").Should().BeNull();
    }

    [Fact]
    public void GetSecret_WhenFilePathSet_ReadsFromFile()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "from-file-secret");
            var config = Build(new() { ["Foo:ApiKeyFile"] = tmpFile });

            config.GetSecret("Foo:ApiKey").Should().Be("from-file-secret");
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void GetSecret_FilePathTakesPrecedenceOverDirectValue()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "from-file");
            var config = Build(new()
            {
                ["Foo:ApiKey"] = "from-env",      // 直接值
                ["Foo:ApiKeyFile"] = tmpFile,      // 檔案路徑——優先
            });

            config.GetSecret("Foo:ApiKey").Should().Be("from-file",
                "file path should win over direct env when both set");
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void GetSecret_TrimsTrailingNewline()
    {
        // Docker secrets 檔案常帶 trailing \n、不 trim 會破壞 API call
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "secret-with-newline\n");
            var config = Build(new() { ["Foo:ApiKeyFile"] = tmpFile });

            config.GetSecret("Foo:ApiKey").Should().Be("secret-with-newline");
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void GetSecret_FilePathDoesNotExist_FallsBackToDirect()
    {
        var config = Build(new()
        {
            ["Foo:ApiKey"] = "fallback-direct",
            ["Foo:ApiKeyFile"] = "/path/that/definitely/does/not/exist.txt",
        });

        config.GetSecret("Foo:ApiKey").Should().Be("fallback-direct",
            "missing file should silently fall through to direct value, not crash");
    }

    [Fact]
    public void GetSecret_EmptyFileFallsBackToDirect()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "");  // 空檔
            var config = Build(new()
            {
                ["Foo:ApiKey"] = "fallback-direct",
                ["Foo:ApiKeyFile"] = tmpFile,
            });

            config.GetSecret("Foo:ApiKey").Should().Be("fallback-direct",
                "empty file should not be treated as a valid secret");
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void GetSecretWithSource_DirectValueReportedAsEnv()
    {
        var config = Build(new() { ["Foo:ApiKey"] = "v1" });

        var (value, source) = config.GetSecretWithSource("Foo:ApiKey");
        value.Should().Be("v1");
        source.Should().Be("env");
    }

    [Fact]
    public void GetSecretWithSource_FileValueReportedAsFile()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "v2");
            var config = Build(new() { ["Foo:ApiKeyFile"] = tmpFile });

            var (value, source) = config.GetSecretWithSource("Foo:ApiKey");
            value.Should().Be("v2");
            source.Should().Be("file");
        }
        finally { File.Delete(tmpFile); }
    }

    [Fact]
    public void GetSecretWithSource_NothingSetReportedAsMissing()
    {
        var config = Build(new());

        var (value, source) = config.GetSecretWithSource("Foo:ApiKey");
        value.Should().BeNull();
        source.Should().Be("missing");
    }
}
