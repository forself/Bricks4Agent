using System.Text.Json;
using BrokerCore.Crypto;
using BrokerCore.Services;
using FunctionPool.Container;

namespace Broker.Services;

/// <summary>一次代理容器啟動的參數（由 broker 自己組成，不從請求本文複製環境變數）。</summary>
public sealed class AgentLaunchRequest
{
    /// <summary>容器的 worker id（也用在容器名稱）。</summary>
    public required string WorkerId { get; init; }

    /// <summary>代理使用的模型；空白時用 broker 的高階預設模型。</summary>
    public string? Model { get; init; }

    /// <summary>模型回合上限（會限制在 1～<see cref="AgentContainerLauncher.MaxSpawnIterations"/>）。</summary>
    public int MaxIterations { get; init; } = 10;

    public bool Verbose { get; init; } = true;

    /// <summary>單次執行的工作項（AGENT_RUN）；空白時代理只回 AGENT_READY。</summary>
    public string? Run { get; init; }

    /// <summary>舊的 LINE 監聽模式（只限開發用途）；開啟時不帶 AGENT_RUN。</summary>
    public bool LegacyLineListen { get; init; }

    public int? LinePollIntervalMs { get; init; }
}

/// <summary>
/// 以 ContainerManager 啟動代理容器（<c>/agents/spawn</c> 與受治理生成共用）。
///
/// 每次啟動都簽發新的註冊憑證（<see cref="AgentSpawnService.SpawnWithCredentialAsync"/>）：
/// 明文只經 <see cref="ContainerSpawnRequest.SecretEnvironment"/> 交給容器（CLI 參數中只有 <c>-e NAME</c>），
/// 不出現在回傳值、log 或例外訊息中。代理一律連到設定的 AgentBrokerUrl。
/// </summary>
public sealed class AgentContainerLauncher
{
    /// <summary>
    /// The container environment variable that carries the registration secret. It is passed through
    /// ContainerSpawnRequest.SecretEnvironment, so only its name appears in the runtime CLI arguments.
    /// </summary>
    public const string RegistrationSecretEnvironmentVariable = "BROKER_REGISTRATION_SECRET";

    /// <summary>Upper bound for the max_iterations a spawn request may ask for.</summary>
    public const int MaxSpawnIterations = 50;

    private readonly AgentSpawnService _spawnService;
    private readonly IContainerManager _containerManager;
    private readonly IEnvelopeCrypto _crypto;
    private readonly IConfiguration _configuration;
    private readonly HighLevelLlmOptions _llmOptions;
    private readonly RegistrationCredentialOptions _credentialOptions;

    public AgentContainerLauncher(
        AgentSpawnService spawnService,
        IContainerManager containerManager,
        IEnvelopeCrypto crypto,
        IConfiguration configuration,
        HighLevelLlmOptions llmOptions,
        RegistrationCredentialOptions credentialOptions)
    {
        _spawnService = spawnService;
        _containerManager = containerManager;
        _crypto = crypto;
        _configuration = configuration;
        _llmOptions = llmOptions;
        _credentialOptions = credentialOptions;
    }

    public IContainerManager ContainerManager => _containerManager;

    public static int ClampMaxIterations(int requested)
        => Math.Clamp(requested, 1, MaxSpawnIterations);

    /// <summary>
    /// The agent container always talks to the configured AgentBrokerUrl. A request may repeat
    /// that value but cannot point the agent (and the credentials handed to it) somewhere else.
    /// </summary>
    public static (bool Ok, string BrokerUrl, string? Error) ResolveAgentBrokerUrl(
        JsonElement body,
        IConfiguration configuration)
    {
        var configured = (configuration.GetValue(
            "FunctionPool:ContainerManager:AgentBrokerUrl",
            "http://broker:5000") ?? "http://broker:5000").Trim().TrimEnd('/');

        if (string.IsNullOrWhiteSpace(configured))
            return (false, string.Empty, "FunctionPool:ContainerManager:AgentBrokerUrl must not be empty.");

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return (false, string.Empty, "FunctionPool:ContainerManager:AgentBrokerUrl must be an absolute http(s) URL.");
        }

        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("broker_url", out var brokerUrlEl))
        {
            var requested = brokerUrlEl.ValueKind == JsonValueKind.String
                ? (brokerUrlEl.GetString() ?? string.Empty).Trim().TrimEnd('/')
                : null;
            if (requested == null || !string.Equals(requested, configured, StringComparison.OrdinalIgnoreCase))
                return (false, string.Empty, "Agent broker_url must match the configured AgentBrokerUrl.");
        }

        return (true, configured, null);
    }

    /// <summary>設定的 AgentBrokerUrl（請求不能另行指定）。</summary>
    public (bool Ok, string BrokerUrl, string? Error) ResolveConfiguredBrokerUrl()
        => ResolveAgentBrokerUrl(default, _configuration);

    public Task<bool> IsRuntimeAvailableAsync(CancellationToken ct = default)
        => _containerManager.IsRuntimeAvailableAsync(ct);

    /// <summary>broker 為代理組成的環境變數（不含註冊憑證）。</summary>
    public Dictionary<string, string> BuildTrustedEnvironment(AgentSummary agent, string brokerUrl, AgentLaunchRequest request)
    {
        var environment = new Dictionary<string, string>
        {
            ["BROKER_URL"] = brokerUrl,
            ["BROKER_PUB_KEY"] = _crypto.GetBrokerPublicKey(),
            ["BROKER_PRINCIPAL_ID"] = agent.PrincipalId,
            ["BROKER_TASK_ID"] = agent.TaskId,
            ["BROKER_ROLE_ID"] = agent.RoleId,
            ["BROKER_WAIT_FOR_HEALTH"] = "1",
            ["AGENT_NO_CONFIRM"] = "1",
            ["AGENT_LINE_LISTEN"] = "0",
            ["AGENT_MAX_ITERATIONS"] = ClampMaxIterations(request.MaxIterations).ToString(),
            ["AGENT_VERBOSE"] = request.Verbose ? "1" : "0",
            // Agent containers use the broker high-level model by default.
            ["AGENT_MODEL"] = string.IsNullOrWhiteSpace(request.Model) ? _llmOptions.DefaultModel : request.Model.Trim()
        };

        if (request.LegacyLineListen)
        {
            environment["AGENT_LINE_LISTEN"] = "1";
            environment["AGENT_ENABLE_LEGACY_LINE_LISTEN"] = "1";
            if (request.LinePollIntervalMs is { } pollInterval)
                environment["AGENT_LINE_POLL_INTERVAL"] = Math.Max(500, pollInterval).ToString();
        }
        else
        {
            environment["AGENT_RUN"] = string.IsNullOrWhiteSpace(request.Run)
                ? "Reply with the exact text AGENT_READY."
                : request.Run.Trim();
        }

        return environment;
    }

    /// <summary>
    /// 簽發新的註冊憑證並啟動代理容器。啟動成功後才撤銷這個代理先前 spawn 的憑證；
    /// 啟動失敗時只撤銷新的這把並重新丟出例外。
    /// </summary>
    public Task<SpawnedAgentCredential> SpawnAsync(
        AgentSummary agent,
        string brokerUrl,
        AgentLaunchRequest request,
        string issuedBy,
        CancellationToken ct = default)
    {
        var trustedEnvironment = BuildTrustedEnvironment(agent, brokerUrl, request);
        return _spawnService.SpawnWithCredentialAsync(
            agent,
            string.IsNullOrWhiteSpace(issuedBy) ? "agents-spawn" : issuedBy,
            _credentialOptions.SpawnedAgentLifetime,
            secret => _containerManager.SpawnWorkerAsync(new ContainerSpawnRequest
            {
                WorkerType = "agent",
                WorkerId = request.WorkerId,
                TrustedEnvironment = trustedEnvironment,
                SecretEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RegistrationSecretEnvironmentVariable] = secret
                },
            }, ct));
    }
}
