using System.Security.Cryptography;
using Broker.Configuration;
using BrokerCore.Crypto;
using BrokerCore.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Unit.Tests.Core;

public class BrokerSecretsValidatorTests
{
    private const string ScopedTokenKey = "Broker:ScopedToken:Secret";
    private const string MasterKeyKey = "Broker:Encryption:MasterKeyBase64";
    private const string EcdhKey = "Broker:Encryption:EcdhPrivateKeyBase64";

    // ── 佔位值：開發環境改用隨機金鑰，其他環境拒絕 ──

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("Testing")]
    public void Validate_DevelopmentLikeEnvironment_ReplacesPlaceholdersWithWorkingRandomKeys(string environment)
    {
        var logger = new CapturingLogger();

        var secrets = BrokerSecretsValidator.Validate(Build(TemplateSettings()), environment, logger);

        secrets.ScopedTokenSecretIsEphemeral.Should().BeTrue();
        secrets.MasterKeyIsEphemeral.Should().BeTrue();
        secrets.EcdhKeyIsEphemeral.Should().BeTrue();

        System.Text.Encoding.UTF8.GetByteCount(secrets.ScopedTokenSecret).Should().BeGreaterThanOrEqualTo(32);
        Convert.FromBase64String(secrets.MasterKeyBase64).Should().HaveCount(32);
        using var crypto = new EnvelopeCrypto(secrets.EcdhPrivateKeyBase64);
        crypto.GetBrokerPublicKey().Should().NotBeNullOrEmpty();
        var tokenService = () => new ScopedTokenService(secrets.ScopedTokenSecret, "issuer", "audience");
        tokenService.Should().NotThrow();

        var warnings = logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        warnings.Should().HaveCount(3, "the three broker keys behave the same way");
        warnings.Should().Contain(m => m.Contains(ScopedTokenKey));
        warnings.Should().Contain(m => m.Contains(MasterKeyKey));
        warnings.Should().Contain(m => m.Contains(EcdhKey));
    }

    [Fact]
    public void Validate_DevelopmentLikeEnvironment_GeneratesDifferentKeysPerRun()
    {
        var first = BrokerSecretsValidator.Validate(Build(TemplateSettings()), "Development", new CapturingLogger());
        var second = BrokerSecretsValidator.Validate(Build(TemplateSettings()), "Development", new CapturingLogger());

        second.ScopedTokenSecret.Should().NotBe(first.ScopedTokenSecret);
        second.MasterKeyBase64.Should().NotBe(first.MasterKeyBase64);
        second.EcdhPrivateKeyBase64.Should().NotBe(first.EcdhPrivateKeyBase64);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    public void Validate_NonDevelopmentEnvironment_RejectsTemplatePlaceholders(string environment)
    {
        var act = () => BrokerSecretsValidator.Validate(Build(TemplateSettings()), environment, new CapturingLogger());

        var problems = act.Should().Throw<BrokerSecretsValidationException>().Which.Problems;
        problems.Should().HaveCount(3);
        problems.Should().Contain(p => p.Contains("Broker__ScopedToken__Secret") && p.Contains("BROKER_SCOPED_TOKEN_SECRET"));
        problems.Should().Contain(p => p.Contains("Broker__Encryption__MasterKeyBase64") && p.Contains("BROKER_MASTER_KEY_BASE64"));
        problems.Should().Contain(p => p.Contains("Broker__Encryption__EcdhPrivateKeyBase64") && p.Contains("BROKER_ECDH_PRIVATE_KEY_BASE64"));
        problems.Should().OnlyContain(p =>
            p.Contains(BrokerSecretsValidator.GeneratorCommand) && p.Contains(BrokerSecretsValidator.AllowEphemeralKeysKey));
    }

    [Fact]
    public void Validate_NonDevelopmentEnvironment_RejectsReplaceWithPlaceholders()
    {
        var settings = TemplateSettings();
        settings[ScopedTokenKey] = "REPLACE_WITH_DEVELOPMENT_SCOPED_TOKEN_SECRET";
        settings[MasterKeyKey] = "REPLACE_WITH_BASE64_32_BYTE_MASTER_KEY";
        settings[EcdhKey] = "REPLACE_WITH_BASE64_PKCS8_PRIVATE_KEY";

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Production", new CapturingLogger());

        act.Should().Throw<BrokerSecretsValidationException>().Which.Problems.Should().HaveCount(3);
    }

    [Fact]
    public void Validate_NonDevelopmentEnvironment_AllowEphemeralKeysOptIn_UsesRandomKeys()
    {
        var settings = TemplateSettings();
        settings[BrokerSecretsValidator.AllowEphemeralKeysKey] = "true";
        var logger = new CapturingLogger();

        var secrets = BrokerSecretsValidator.Validate(Build(settings), "Production", logger);

        secrets.ScopedTokenSecretIsEphemeral.Should().BeTrue();
        secrets.MasterKeyIsEphemeral.Should().BeTrue();
        secrets.EcdhKeyIsEphemeral.Should().BeTrue();
        logger.Entries.Count(e => e.Level == LogLevel.Warning).Should().Be(3);
    }

    [Fact]
    public void Validate_NonDevelopmentEnvironment_RejectsUnparsableAllowEphemeralKeys()
    {
        var settings = RealKeySettings();
        settings[BrokerSecretsValidator.AllowEphemeralKeysKey] = "yes";

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Production", new CapturingLogger());

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains(BrokerSecretsValidator.AllowEphemeralKeysKey));
    }

    [Fact]
    public void Validate_NonDevelopmentEnvironment_AcceptsRealKeysUnchanged()
    {
        var settings = RealKeySettings();
        var logger = new CapturingLogger();

        var secrets = BrokerSecretsValidator.Validate(Build(settings), "Production", logger);

        secrets.ScopedTokenSecret.Should().Be(settings[ScopedTokenKey]);
        secrets.MasterKeyBase64.Should().Be(settings[MasterKeyKey]);
        secrets.EcdhPrivateKeyBase64.Should().Be(settings[EcdhKey]);
        secrets.ScopedTokenSecretIsEphemeral.Should().BeFalse();
        secrets.MasterKeyIsEphemeral.Should().BeFalse();
        secrets.EcdhKeyIsEphemeral.Should().BeFalse();
        logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public void Validate_DoesNotCheckTdxGoogleDriveOrDeploymentSecrets()
    {
        var settings = RealKeySettings();
        settings["Tdx:ClientId"] = "REPLACE_WITH_TDX_CLIENT_ID";
        settings["Tdx:ClientSecret"] = "REPLACE_WITH_TDX_CLIENT_SECRET";
        settings["GoogleDriveDelivery:ClientSecret"] = "REPLACE_WITH_GOOGLE_CLIENT_SECRET";
        settings["DeploymentSecrets:Password"] = "CHANGE_ME";

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Production", new CapturingLogger());

        act.Should().NotThrow();
    }

    // ── 格式檢查（所有環境） ──

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void Validate_RejectsScopedTokenSecretShorterThan32Bytes(string environment)
    {
        var settings = RealKeySettings();
        settings[ScopedTokenKey] = "too-short-secret";

        var act = () => BrokerSecretsValidator.Validate(Build(settings), environment, new CapturingLogger());

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains(ScopedTokenKey) && p.Contains("32"));
    }

    [Theory]
    [InlineData("not base64 at all!")]
    [InlineData("AAAA")]
    public void Validate_RejectsMasterKeyThatIsNotBase64Of32Bytes(string value)
    {
        var settings = RealKeySettings();
        settings[MasterKeyKey] = value;

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Development", new CapturingLogger());

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains(MasterKeyKey));
    }

    [Fact]
    public void Validate_RejectsEcdhKeyThatIsNotPkcs8()
    {
        var settings = RealKeySettings();
        settings[EcdhKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Development", new CapturingLogger());

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains(EcdhKey));
    }

    [Fact]
    public void Validate_RejectsEcdhKeyOnAnotherCurve()
    {
        using var p384 = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var settings = RealKeySettings();
        settings[EcdhKey] = Convert.ToBase64String(p384.ExportPkcs8PrivateKey());

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Development", new CapturingLogger());

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains(EcdhKey));
    }

    // ── 已外洩值：任何環境都拒絕 ──

    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    [InlineData("Production")]
    public void Validate_RejectsLeakedScopedTokenSecretInEveryEnvironment(string environment)
    {
        var settings = RealKeySettings();
        var leaked = Fingerprints(values: [settings[ScopedTokenKey]!]);
        settings[BrokerSecretsValidator.AllowEphemeralKeysKey] = "true";

        var act = () => BrokerSecretsValidator.Validate(Build(settings), environment, new CapturingLogger(), leaked);

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains(ScopedTokenKey));
    }

    [Fact]
    public void Validate_RejectsLeakedValueWithSurroundingWhitespace()
    {
        var settings = RealKeySettings();
        var leaked = Fingerprints(values: [settings[ScopedTokenKey]!]);
        settings[ScopedTokenKey] = "  " + settings[ScopedTokenKey] + "\n";

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Development", new CapturingLogger(), leaked);

        act.Should().Throw<BrokerSecretsValidationException>();
    }

    [Fact]
    public void Validate_RejectsLeakedMasterKeyMaterialEvenWhenReEncoded()
    {
        var material = RandomNumberGenerator.GetBytes(32);
        var settings = RealKeySettings();
        // 同一把金鑰換一種 base64 寫法（插入換行），字串雜湊不同但金鑰內容相同。
        settings[MasterKeyKey] = Convert.ToBase64String(material).Insert(8, "\n");
        var leaked = new LeakedSecretFingerprints(
            valueSha256: [],
            masterKeyMaterialSha256: [LeakedSecretFingerprints.Sha256Hex(material)],
            ecdhPublicKeySha256: []);

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Development", new CapturingLogger(), leaked);

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains(MasterKeyKey));
    }

    [Fact]
    public void Validate_RejectsLeakedEcdhKeyByItsPublicKey()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var settings = RealKeySettings();
        settings[EcdhKey] = Convert.ToBase64String(ecdh.ExportPkcs8PrivateKey());
        var leaked = new LeakedSecretFingerprints(
            valueSha256: [],
            masterKeyMaterialSha256: [],
            ecdhPublicKeySha256: [LeakedSecretFingerprints.Sha256Hex(ecdh.ExportSubjectPublicKeyInfo())]);

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Development", new CapturingLogger(), leaked);

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains(EcdhKey));
    }

    [Fact]
    public void Validate_ProblemMessagesNeverContainTheConfiguredValue()
    {
        var settings = RealKeySettings();
        var leakedValue = settings[ScopedTokenKey]!;
        var shortValue = "short-but-secret-value";
        settings[MasterKeyKey] = shortValue;
        var leaked = Fingerprints(values: [leakedValue]);

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Production", new CapturingLogger(), leaked);

        var message = act.Should().Throw<BrokerSecretsValidationException>().Which.Message;
        message.Should().NotContain(leakedValue);
        message.Should().NotContain(shortValue);
    }

    [Fact]
    public void DefaultFingerprints_AreWellFormedSha256AndDoNotMatchFreshKeys()
    {
        var fingerprints = LeakedSecretFingerprints.Default;

        fingerprints.ValueSha256.Should().HaveCount(6);
        fingerprints.MasterKeyMaterialSha256.Should().HaveCount(1);
        fingerprints.EcdhPublicKeySha256.Should().HaveCount(1);
        fingerprints.ValueSha256
            .Concat(fingerprints.MasterKeyMaterialSha256)
            .Concat(fingerprints.EcdhPublicKeySha256)
            .Should().OnlyContain(h => h.Length == 64 && h.All(c => "0123456789abcdef".Contains(c)));

        var fresh = RealKeySettings();
        var act = () => BrokerSecretsValidator.Validate(Build(fresh), "Production", new CapturingLogger());
        act.Should().NotThrow();
    }

    // ── WorkerAuth ──

    [Fact]
    public void Validate_WorkerAuthNotEnforced_IgnoresCredentials()
    {
        var settings = RealKeySettings();
        settings["WorkerAuth:Enforce"] = "false";
        var leakedWorkerSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        settings["WorkerAuth:Credentials:1:WorkerType"] = "file-worker";
        settings["WorkerAuth:Credentials:1:KeyId"] = "file-v1";
        settings["WorkerAuth:Credentials:1:SharedSecret"] = leakedWorkerSecret;

        var act = () => BrokerSecretsValidator.Validate(
            Build(settings), "Production", new CapturingLogger(), Fingerprints(values: [leakedWorkerSecret]));

        act.Should().NotThrow("credentials are only checked when WorkerAuth:Enforce=true");
    }

    [Fact]
    public void Validate_WorkerAuthEnforced_RejectsPlaceholderCredentialOutsideDevelopment()
    {
        var settings = RealKeySettings();
        settings["WorkerAuth:Enforce"] = "true";

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Production", new CapturingLogger());

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains("WorkerAuth__Credentials__0__"));
    }

    [Fact]
    public void Validate_WorkerAuthEnforced_AllowsPlaceholderCredentialInDevelopmentWithWarning()
    {
        var settings = RealKeySettings();
        settings["WorkerAuth:Enforce"] = "true";
        var logger = new CapturingLogger();

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Testing", logger);

        act.Should().NotThrow();
        logger.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("WorkerAuth:Credentials:0:SharedSecret"));
    }

    [Fact]
    public void Validate_WorkerAuthEnforced_RejectsLeakedCredentialInEveryEnvironment()
    {
        var settings = RealKeySettings();
        settings["WorkerAuth:Enforce"] = "true";
        var leakedWorkerSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        settings["WorkerAuth:Credentials:0:KeyId"] = "line-v1";
        settings["WorkerAuth:Credentials:0:SharedSecret"] = leakedWorkerSecret;

        var act = () => BrokerSecretsValidator.Validate(
            Build(settings), "Development", new CapturingLogger(), Fingerprints(values: [leakedWorkerSecret]));

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains("WorkerAuth:Credentials:0:SharedSecret"));
    }

    [Fact]
    public void Validate_WorkerAuthEnforced_IgnoresInactiveCredentialsAndAcceptsRealOnes()
    {
        var settings = RealKeySettings();
        settings["WorkerAuth:Enforce"] = "true";
        settings["WorkerAuth:Credentials:0:KeyId"] = "line-v1";
        settings["WorkerAuth:Credentials:0:SharedSecret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        settings["WorkerAuth:Credentials:1:WorkerType"] = "browser-worker";
        settings["WorkerAuth:Credentials:1:KeyId"] = "REPLACE_WITH_BROWSER_WORKER_KEY_ID";
        settings["WorkerAuth:Credentials:1:SharedSecret"] = "REPLACE_WITH_BROWSER_WORKER_SHARED_SECRET";
        settings["WorkerAuth:Credentials:1:Status"] = "revoked";

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Production", new CapturingLogger());

        act.Should().NotThrow();
    }

    // ── ArtifactDownload ──

    [Fact]
    public void Validate_EmptyArtifactSigningSecretMeansDisabled()
    {
        var settings = RealKeySettings();
        settings["ArtifactDownload:SigningSecret"] = "";

        var act = () => BrokerSecretsValidator.Validate(Build(settings), "Production", new CapturingLogger());

        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_PlaceholderArtifactSigningSecret_RejectedOutsideDevelopmentOnly()
    {
        var settings = RealKeySettings();
        settings["ArtifactDownload:SigningSecret"] = "REPLACE_WITH_RANDOM_ARTIFACT_SIGNING_SECRET";

        var production = () => BrokerSecretsValidator.Validate(Build(settings), "Production", new CapturingLogger());
        var development = () => BrokerSecretsValidator.Validate(Build(settings), "Development", new CapturingLogger());

        production.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains("ArtifactDownload__SigningSecret"));
        development.Should().NotThrow();
    }

    [Fact]
    public void Validate_LeakedArtifactSigningSecret_RejectedInDevelopment()
    {
        var settings = RealKeySettings();
        var leakedSigningSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        settings["ArtifactDownload:SigningSecret"] = leakedSigningSecret;

        var act = () => BrokerSecretsValidator.Validate(
            Build(settings), "Development", new CapturingLogger(), Fingerprints(values: [leakedSigningSecret]));

        act.Should().Throw<BrokerSecretsValidationException>()
            .Which.Problems.Should().ContainSingle(p => p.Contains("ArtifactDownload:SigningSecret"));
    }

    // ── 共用規則 ──

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("CHANGE_ME_IN_PRODUCTION_USE_256_BIT_KEY_MINIMUM", true)]
    [InlineData("change_me", true)]
    [InlineData("REPLACE_WITH_DEVELOPMENT_SCOPED_TOKEN_SECRET", true)]
    [InlineData("  REPLACE_WITH_X", true)]
    [InlineData("a-real-looking-secret-value-with-enough-bytes", false)]
    public void SecretPlaceholders_IsPlaceholder(string? value, bool expected)
    {
        SecretPlaceholders.IsPlaceholder(value).Should().Be(expected);
    }

    [Fact]
    public void ScopedTokenService_TreatsReplaceWithPlaceholderAsRandomKey()
    {
        // 佔位判斷統一後，REPLACE_WITH_* 不再被當成公開已知的簽章金鑰使用。
        const string placeholder = "REPLACE_WITH_DEVELOPMENT_SCOPED_TOKEN_SECRET";
        var first = new ScopedTokenService(placeholder, "issuer", "audience");
        var second = new ScopedTokenService(placeholder, "issuer", "audience");

        var token = first.GenerateToken(new ScopedTokenClaims
        {
            PrincipalId = "p-1",
            Jti = $"jti_{Guid.NewGuid():N}",
            TaskId = "task-1",
            SessionId = "sess-1",
            RoleId = "role_reader",
            CapabilityIds = Array.Empty<string>(),
            Scope = "{}",
            Epoch = 1
        });

        first.ValidateToken(token).Should().NotBeNull();
        second.ValidateToken(token).Should().BeNull();
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("DEVELOPMENT", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    [InlineData(null, false)]
    public void IsDevelopmentLike(string? environment, bool expected)
    {
        BrokerSecretsValidator.IsDevelopmentLike(environment).Should().Be(expected);
    }

    [Fact]
    public void ToEnvironmentVariableName_UsesDoubleUnderscore()
    {
        BrokerSecretsValidator.ToEnvironmentVariableName("WorkerAuth:Credentials:2:SharedSecret")
            .Should().Be("WorkerAuth__Credentials__2__SharedSecret");
    }

    [Fact]
    public void BrokerSecrets_ToStringRedactsValues()
    {
        var settings = RealKeySettings();
        var secrets = BrokerSecretsValidator.Validate(Build(settings), "Production", new CapturingLogger());

        var text = secrets.ToString();

        text.Should().NotContain(secrets.ScopedTokenSecret);
        text.Should().NotContain(secrets.MasterKeyBase64);
        text.Should().NotContain(secrets.EcdhPrivateKeyBase64);
    }

    // ── helpers ──

    /// <summary>與 broker/appsettings.json 相同的範本值（公開的佔位字串）。</summary>
    private static Dictionary<string, string?> TemplateSettings() => new()
    {
        [ScopedTokenKey] = "CHANGE_ME_IN_PRODUCTION_USE_256_BIT_KEY_MINIMUM",
        [MasterKeyKey] = "CHANGE_ME_IN_PRODUCTION_USE_AES256_KEY",
        [EcdhKey] = "",
        ["ArtifactDownload:SigningSecret"] = "",
        ["WorkerAuth:Enforce"] = "false",
        ["WorkerAuth:Credentials:0:WorkerType"] = "line-worker",
        ["WorkerAuth:Credentials:0:KeyId"] = "REPLACE_WITH_LINE_WORKER_KEY_ID",
        ["WorkerAuth:Credentials:0:SharedSecret"] = "REPLACE_WITH_LINE_WORKER_SHARED_SECRET",
        ["WorkerAuth:Credentials:0:Status"] = "active",
    };

    private static Dictionary<string, string?> RealKeySettings()
    {
        using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var settings = TemplateSettings();
        settings[ScopedTokenKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        settings[MasterKeyKey] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        settings[EcdhKey] = Convert.ToBase64String(ecdh.ExportPkcs8PrivateKey());
        return settings;
    }

    private static IConfiguration Build(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static LeakedSecretFingerprints Fingerprints(IEnumerable<string> values) =>
        new(
            valueSha256: values.Select(v => LeakedSecretFingerprints.Sha256Hex(v)),
            masterKeyMaterialSha256: [],
            ecdhPublicKeySha256: []);

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class CapturingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }
}
