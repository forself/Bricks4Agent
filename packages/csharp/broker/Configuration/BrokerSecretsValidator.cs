using System.Security.Cryptography;
using System.Text;
using BrokerCore.Crypto;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Broker.Configuration;

/// <summary>
/// broker 啟動時解析出的實際金鑰。
/// 設定值是佔位值且環境允許時，這裡放的是本次程序隨機產生的金鑰。
/// ToString 不輸出任何金鑰內容，避免被誤寫進 log。
/// </summary>
public sealed class BrokerSecrets
{
    internal BrokerSecrets(
        string scopedTokenSecret,
        string masterKeyBase64,
        string ecdhPrivateKeyBase64,
        bool scopedTokenSecretIsEphemeral,
        bool masterKeyIsEphemeral,
        bool ecdhKeyIsEphemeral)
    {
        ScopedTokenSecret = scopedTokenSecret;
        MasterKeyBase64 = masterKeyBase64;
        EcdhPrivateKeyBase64 = ecdhPrivateKeyBase64;
        ScopedTokenSecretIsEphemeral = scopedTokenSecretIsEphemeral;
        MasterKeyIsEphemeral = masterKeyIsEphemeral;
        EcdhKeyIsEphemeral = ecdhKeyIsEphemeral;
    }

    /// <summary>Broker:ScopedToken:Secret 的實際值（至少 32 個 UTF-8 bytes）。</summary>
    public string ScopedTokenSecret { get; }

    /// <summary>Broker:Encryption:MasterKeyBase64 的實際值（base64，解碼後 32 bytes）。</summary>
    public string MasterKeyBase64 { get; }

    /// <summary>Broker:Encryption:EcdhPrivateKeyBase64 的實際值（PKCS#8 P-256 私鑰，base64）。</summary>
    public string EcdhPrivateKeyBase64 { get; }

    public bool ScopedTokenSecretIsEphemeral { get; }

    public bool MasterKeyIsEphemeral { get; }

    public bool EcdhKeyIsEphemeral { get; }

    public override string ToString() => $"{nameof(BrokerSecrets)} {{ values redacted }}";
}

/// <summary>
/// 已外洩密鑰的 SHA-256 指紋。repo 是公開的，因此只保存雜湊、不保存原值。
/// </summary>
public sealed class LeakedSecretFingerprints
{
    public LeakedSecretFingerprints(
        IEnumerable<string> valueSha256,
        IEnumerable<string> masterKeyMaterialSha256,
        IEnumerable<string> ecdhPublicKeySha256)
    {
        ArgumentNullException.ThrowIfNull(valueSha256);
        ArgumentNullException.ThrowIfNull(masterKeyMaterialSha256);
        ArgumentNullException.ThrowIfNull(ecdhPublicKeySha256);

        ValueSha256 = new HashSet<string>(valueSha256, StringComparer.OrdinalIgnoreCase);
        MasterKeyMaterialSha256 = new HashSet<string>(masterKeyMaterialSha256, StringComparer.OrdinalIgnoreCase);
        EcdhPublicKeySha256 = new HashSet<string>(ecdhPublicKeySha256, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>設定字串（UTF-8）本身的 SHA-256。適用於任何密鑰欄位。</summary>
    public IReadOnlySet<string> ValueSha256 { get; }

    /// <summary>MasterKey 解碼後 32 bytes 的 SHA-256（同一把金鑰換一種 base64 寫法也能辨識）。</summary>
    public IReadOnlySet<string> MasterKeyMaterialSha256 { get; }

    /// <summary>ECDH 私鑰推得之公鑰（SubjectPublicKeyInfo DER）的 SHA-256（同一把私鑰換一種 PKCS#8 編碼也能辨識）。</summary>
    public IReadOnlySet<string> EcdhPublicKeySha256 { get; }

    /// <summary>
    /// 曾以預設值形式提交在 tools/agent/container/compose*.yml 的開發用金鑰。
    /// 這些值已在 git 歷史中公開，任何環境都不得再使用。
    /// tools/agent/tests/test-agent-container-config.js 會比對這份清單與 compose 檔，兩邊要同步維護。
    /// </summary>
    public static LeakedSecretFingerprints Default { get; } = new(
        valueSha256:
        [
            // broker ScopedToken secret
            "d4735fc78bf0409eb9a424099eca948453529e39e8b541eaedf70fa341aa81fe",
            // broker MasterKeyBase64（字串）
            "5bfc312a1c45de453aa9fa235af0650488a52faf21ea66dd231af31b0269a2f1",
            // broker ECDH 私鑰（PKCS#8 base64 字串）
            "7a5dd0ecd40874c4f914c0a49a5edbfde8136989d72e84a113025313f517f932",
            // line-worker 開發用共享密鑰
            "3662bfe012750afdccb756357ecbf04e49f047ab409f214d3ec5cfeb52472c0f",
            // file-worker 開發用共享密鑰
            "56d92e694fa1eec508fd8ef41ebe01aacc980e2140575797d3900b7763635283",
            // execution-adapter-worker 開發用共享密鑰
            "bbac8d268d5976b6dc3bdfd123fc86e0d1ee539210efd507c2aabead1c5c74f9",
        ],
        masterKeyMaterialSha256:
        [
            "460be2f8c98a9941f8e119fd860ea8eddec20958a35996be65fe5ef4e22f3cb3",
        ],
        ecdhPublicKeySha256:
        [
            "2619ec9f4b62f31d7afac9a62d14850d20bdf6cc17bc7687b074455e9796faff",
        ]);

    public static string Sha256Hex(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    public static string Sha256Hex(string value) => Sha256Hex(Encoding.UTF8.GetBytes(value));

    /// <summary>比對設定字串；原值與去除前後空白後的值都會比對。</summary>
    public bool MatchesValue(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        if (ValueSha256.Contains(Sha256Hex(value)))
            return true;

        var trimmed = value.Trim();
        return trimmed.Length > 0
            && trimmed.Length != value.Length
            && ValueSha256.Contains(Sha256Hex(trimmed));
    }

    public bool MatchesMasterKeyMaterial(ReadOnlySpan<byte> material) =>
        MasterKeyMaterialSha256.Contains(Sha256Hex(material));

    public bool MatchesEcdhPublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo) =>
        EcdhPublicKeySha256.Contains(Sha256Hex(subjectPublicKeyInfo));
}

/// <summary>broker 密鑰設定不合格時拋出；訊息列出所有問題，不含任何密鑰內容。</summary>
public sealed class BrokerSecretsValidationException : InvalidOperationException
{
    public BrokerSecretsValidationException(IReadOnlyList<string> problems)
        : base(BuildMessage(problems))
    {
        Problems = problems;
    }

    public IReadOnlyList<string> Problems { get; }

    private static string BuildMessage(IReadOnlyList<string> problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        var builder = new StringBuilder("Broker secret configuration is invalid:");
        foreach (var problem in problems)
        {
            builder.AppendLine();
            builder.Append(" - ").Append(problem);
        }

        return builder.ToString();
    }
}

/// <summary>
/// broker 啟動時的密鑰驗證（必須在 Program.cs 讀取這些設定之前呼叫）。
///
/// 規則：
/// - 已外洩值（compose 曾提交的開發預設值）：任何環境都拒絕。
/// - 佔位值（空白、CHANGE_ME*、REPLACE_WITH_*）：
///   Development／Testing 環境改用本次程序的隨機金鑰並記 LogWarning（三把金鑰一致）；
///   其他環境拒絕，除非明確設定 Broker:AllowEphemeralKeys=true。
/// - 格式：ScopedToken secret ≥ 32 UTF-8 bytes；MasterKey 為 base64 且 32 bytes；ECDH 私鑰為可匯入的 PKCS#8 P-256。
/// - WorkerAuth：只在 Enforce=true 時檢查 active 憑證；外洩值任何環境拒絕，佔位值在非開發環境拒絕。
/// - ArtifactDownload:SigningSecret：空白代表停用（合法）；外洩值任何環境拒絕，佔位值在非開發環境拒絕。
/// - 不檢查 Tdx、GoogleDriveDelivery、DeploymentSecrets（這些可能合法地維持範本值）。
/// </summary>
public static class BrokerSecretsValidator
{
    public const string TestingEnvironmentName = "Testing";
    public const string AllowEphemeralKeysKey = "Broker:AllowEphemeralKeys";
    public const string ScopedTokenSecretKey = "Broker:ScopedToken:Secret";
    public const string MasterKeyKey = "Broker:Encryption:MasterKeyBase64";
    public const string EcdhPrivateKeyKey = "Broker:Encryption:EcdhPrivateKeyBase64";
    public const string WorkerAuthEnforceKey = "WorkerAuth:Enforce";
    public const string WorkerAuthCredentialsKey = "WorkerAuth:Credentials";
    public const string ArtifactSigningSecretKey = "ArtifactDownload:SigningSecret";
    public const string GeneratorCommand = "node tools/agent/container/gen-stack-secrets.mjs";
    public const int MinScopedTokenSecretBytes = 32;
    public const int MasterKeyBytes = 32;

    private const string ScopedTokenComposeVariable = "BROKER_SCOPED_TOKEN_SECRET";
    private const string MasterKeyComposeVariable = "BROKER_MASTER_KEY_BASE64";
    private const string EcdhPrivateKeyComposeVariable = "BROKER_ECDH_PRIVATE_KEY_BASE64";
    private const int EphemeralScopedTokenSecretBytes = 48;

    public static BrokerSecrets Validate(IConfiguration configuration, IHostEnvironment environment, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Validate(configuration, environment.EnvironmentName, logger);
    }

    public static BrokerSecrets Validate(
        IConfiguration configuration,
        string? environmentName,
        ILogger logger,
        LeakedSecretFingerprints? leakedFingerprints = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        var run = new ValidationRun(
            configuration,
            environmentName ?? string.Empty,
            logger,
            leakedFingerprints ?? LeakedSecretFingerprints.Default);
        return run.Execute();
    }

    /// <summary>Development 或 Testing（不分大小寫）視為開發環境。</summary>
    public static bool IsDevelopmentLike(string? environmentName) =>
        string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase)
        || string.Equals(environmentName, TestingEnvironmentName, StringComparison.OrdinalIgnoreCase);

    /// <summary>設定鍵對應的環境變數名稱，例如 Broker:ScopedToken:Secret → Broker__ScopedToken__Secret。</summary>
    public static string ToEnvironmentVariableName(string configurationKey) =>
        configurationKey.Replace(":", "__", StringComparison.Ordinal);

    private sealed class ValidationRun
    {
        private readonly IConfiguration _configuration;
        private readonly string _environmentName;
        private readonly ILogger _logger;
        private readonly LeakedSecretFingerprints _leaked;
        private readonly bool _developmentLike;
        private readonly List<string> _problems = new();
        private bool _allowEphemeralKeys;

        public ValidationRun(
            IConfiguration configuration,
            string environmentName,
            ILogger logger,
            LeakedSecretFingerprints leaked)
        {
            _configuration = configuration;
            _environmentName = environmentName;
            _logger = logger;
            _leaked = leaked;
            _developmentLike = IsDevelopmentLike(environmentName);
        }

        public BrokerSecrets Execute()
        {
            _allowEphemeralKeys = ReadBool(AllowEphemeralKeysKey);

            var scopedTokenSecret = ResolveScopedTokenSecret(out var scopedTokenEphemeral);
            var masterKey = ResolveMasterKey(out var masterKeyEphemeral);
            var ecdhPrivateKey = ResolveEcdhPrivateKey(out var ecdhEphemeral);
            ValidateWorkerAuth();
            ValidateArtifactSigningSecret();

            if (_problems.Count > 0)
                throw new BrokerSecretsValidationException(_problems.ToArray());

            return new BrokerSecrets(
                scopedTokenSecret,
                masterKey,
                ecdhPrivateKey,
                scopedTokenEphemeral,
                masterKeyEphemeral,
                ecdhEphemeral);
        }

        private string ResolveScopedTokenSecret(out bool ephemeral)
        {
            ephemeral = false;
            var value = _configuration[ScopedTokenSecretKey];

            if (_leaked.MatchesValue(value))
            {
                AddLeakedProblem(ScopedTokenSecretKey, ScopedTokenComposeVariable);
                return string.Empty;
            }

            if (SecretPlaceholders.IsPlaceholder(value))
            {
                if (!TryAcceptEphemeral(ScopedTokenSecretKey, ScopedTokenComposeVariable))
                    return string.Empty;

                ephemeral = true;
                return Convert.ToBase64String(RandomNumberGenerator.GetBytes(EphemeralScopedTokenSecretBytes));
            }

            if (Encoding.UTF8.GetByteCount(value) < MinScopedTokenSecretBytes)
            {
                _problems.Add(
                    $"{ScopedTokenSecretKey} must be at least {MinScopedTokenSecretBytes} UTF-8 bytes. " +
                    WhereToSet(ScopedTokenSecretKey, ScopedTokenComposeVariable));
            }

            return value;
        }

        private string ResolveMasterKey(out bool ephemeral)
        {
            ephemeral = false;
            var value = _configuration[MasterKeyKey];

            if (_leaked.MatchesValue(value))
            {
                AddLeakedProblem(MasterKeyKey, MasterKeyComposeVariable);
                return string.Empty;
            }

            if (SecretPlaceholders.IsPlaceholder(value))
            {
                if (!TryAcceptEphemeral(MasterKeyKey, MasterKeyComposeVariable))
                    return string.Empty;

                ephemeral = true;
                return Convert.ToBase64String(RandomNumberGenerator.GetBytes(MasterKeyBytes));
            }

            byte[] material;
            try
            {
                material = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                _problems.Add(
                    $"{MasterKeyKey} must be base64 that decodes to exactly {MasterKeyBytes} bytes (AES-256). " +
                    WhereToSet(MasterKeyKey, MasterKeyComposeVariable));
                return string.Empty;
            }

            try
            {
                if (material.Length != MasterKeyBytes)
                {
                    _problems.Add(
                        $"{MasterKeyKey} must decode to exactly {MasterKeyBytes} bytes (AES-256). " +
                        WhereToSet(MasterKeyKey, MasterKeyComposeVariable));
                    return string.Empty;
                }

                if (_leaked.MatchesMasterKeyMaterial(material))
                {
                    AddLeakedProblem(MasterKeyKey, MasterKeyComposeVariable);
                    return string.Empty;
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(material);
            }

            return value;
        }

        private string ResolveEcdhPrivateKey(out bool ephemeral)
        {
            ephemeral = false;
            var value = _configuration[EcdhPrivateKeyKey];

            if (_leaked.MatchesValue(value))
            {
                AddLeakedProblem(EcdhPrivateKeyKey, EcdhPrivateKeyComposeVariable);
                return string.Empty;
            }

            if (SecretPlaceholders.IsPlaceholder(value))
            {
                if (!TryAcceptEphemeral(EcdhPrivateKeyKey, EcdhPrivateKeyComposeVariable))
                    return string.Empty;

                ephemeral = true;
                using var generated = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                return Convert.ToBase64String(generated.ExportPkcs8PrivateKey());
            }

            byte[] der;
            try
            {
                der = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                AddInvalidEcdhProblem();
                return string.Empty;
            }

            try
            {
                using var ecdh = ECDiffieHellman.Create();
                ecdh.ImportPkcs8PrivateKey(der, out _);

                // 以一把 P-256 公鑰試做一次金鑰協商：與 EnvelopeCrypto 實際使用的運算相同，
                // 非 P-256 的私鑰會在這裡失敗，而不是等到第一個 session 註冊時才失敗。
                using var probe = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
                using var probePublicKey = probe.PublicKey;
                var agreement = ecdh.DeriveRawSecretAgreement(probePublicKey);
                CryptographicOperations.ZeroMemory(agreement);

                if (_leaked.MatchesEcdhPublicKey(ecdh.ExportSubjectPublicKeyInfo()))
                {
                    AddLeakedProblem(EcdhPrivateKeyKey, EcdhPrivateKeyComposeVariable);
                    return string.Empty;
                }
            }
            catch (Exception ex) when (ex is CryptographicException
                                           or ArgumentException
                                           or System.Formats.Asn1.AsnContentException)
            {
                // 不同平台對「非 PKCS#8／非 P-256」丟出的例外型別不同，一律回報為格式錯誤。
                AddInvalidEcdhProblem();
                return string.Empty;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(der);
            }

            return value;
        }

        private void ValidateWorkerAuth()
        {
            if (!ReadBool(WorkerAuthEnforceKey))
                return;

            foreach (var credential in _configuration.GetSection(WorkerAuthCredentialsKey).GetChildren())
            {
                // 與 WorkerCredentialRecord 的預設一致：未設定 Status 視為 active。
                var status = credential["Status"];
                if (!string.IsNullOrWhiteSpace(status)
                    && !string.Equals(status.Trim(), "active", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sharedSecretKey = $"{WorkerAuthCredentialsKey}:{credential.Key}:SharedSecret";
                var keyIdKey = $"{WorkerAuthCredentialsKey}:{credential.Key}:KeyId";
                var sharedSecret = credential["SharedSecret"];

                if (_leaked.MatchesValue(sharedSecret))
                {
                    AddLeakedProblem(sharedSecretKey, composeVariable: null);
                    continue;
                }

                string? placeholderKey = null;
                if (SecretPlaceholders.IsPlaceholder(sharedSecret))
                    placeholderKey = sharedSecretKey;
                else if (SecretPlaceholders.IsPlaceholder(credential["KeyId"]))
                    placeholderKey = keyIdKey;

                if (placeholderKey is null)
                    continue;

                if (_developmentLike)
                {
                    _logger.LogWarning(
                        "{ConfigKey} is empty or a placeholder while {EnforceKey}=true ({Environment} environment). This worker credential offers no protection.",
                        placeholderKey,
                        WorkerAuthEnforceKey,
                        _environmentName);
                    continue;
                }

                _problems.Add(
                    $"{placeholderKey} is empty or a placeholder (CHANGE_ME*/REPLACE_WITH_*) on an active credential while {WorkerAuthEnforceKey}=true; " +
                    $"this is only accepted in the Development or Testing environment (current: '{_environmentName}'). " +
                    $"Set {ToEnvironmentVariableName(placeholderKey)} to a real value, or mark the credential inactive. " +
                    $"`{GeneratorCommand}` generates worker credentials for the compose stacks.");
            }
        }

        private void ValidateArtifactSigningSecret()
        {
            var value = _configuration[ArtifactSigningSecretKey];

            // 空白代表停用 artifact 下載連結與審批連結簽章，是合法設定。
            if (string.IsNullOrWhiteSpace(value))
                return;

            if (_leaked.MatchesValue(value))
            {
                AddLeakedProblem(ArtifactSigningSecretKey, composeVariable: null);
                return;
            }

            if (!SecretPlaceholders.IsPlaceholder(value))
                return;

            if (_developmentLike)
            {
                _logger.LogWarning(
                    "{ConfigKey} is a placeholder ({Environment} environment); download and approval links are signed with a publicly known value.",
                    ArtifactSigningSecretKey,
                    _environmentName);
                return;
            }

            _problems.Add(
                $"{ArtifactSigningSecretKey} is a placeholder (CHANGE_ME*/REPLACE_WITH_*), which is only accepted in the Development or Testing environment (current: '{_environmentName}'). " +
                $"Set {ToEnvironmentVariableName(ArtifactSigningSecretKey)} to a random secret, or leave it empty to disable signed links.");
        }

        private bool TryAcceptEphemeral(string configurationKey, string composeVariable)
        {
            if (_developmentLike || _allowEphemeralKeys)
            {
                var reason = _developmentLike
                    ? $"{_environmentName} environment"
                    : $"{AllowEphemeralKeysKey}=true";
                _logger.LogWarning(
                    "{ConfigKey} is empty or a placeholder; using a random key for this process only ({Reason}). Sessions and tokens issued with it do not survive a restart.",
                    configurationKey,
                    reason);
                return true;
            }

            _problems.Add(
                $"{configurationKey} is empty or a placeholder (CHANGE_ME*/REPLACE_WITH_*), which is only accepted in the Development or Testing environment (current: '{_environmentName}'). " +
                WhereToSet(configurationKey, composeVariable) +
                $" To run with per-process random keys instead, set {AllowEphemeralKeysKey}=true ({ToEnvironmentVariableName(AllowEphemeralKeysKey)}).");
            return false;
        }

        private void AddLeakedProblem(string configurationKey, string? composeVariable)
        {
            _problems.Add(
                $"{configurationKey} matches a development value that was published in this repository and is rejected in every environment; replace it with a newly generated secret. " +
                WhereToSet(configurationKey, composeVariable));
        }

        private void AddInvalidEcdhProblem()
        {
            _problems.Add(
                $"{EcdhPrivateKeyKey} must be a base64 PKCS#8 P-256 private key. " +
                WhereToSet(EcdhPrivateKeyKey, EcdhPrivateKeyComposeVariable));
        }

        private bool ReadBool(string configurationKey)
        {
            var raw = _configuration[configurationKey];
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            if (bool.TryParse(raw.Trim(), out var parsed))
                return parsed;

            _problems.Add($"{configurationKey} must be 'true' or 'false'.");
            return false;
        }

        private static string WhereToSet(string configurationKey, string? composeVariable)
        {
            var compose = composeVariable is null ? string.Empty : $" (compose: {composeVariable})";
            return $"Set it through configuration or the environment variable {ToEnvironmentVariableName(configurationKey)}{compose}; " +
                   $"`{GeneratorCommand}` generates a full set of development keys.";
        }
    }
}
