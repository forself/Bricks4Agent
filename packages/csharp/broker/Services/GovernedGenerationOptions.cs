namespace Broker.Services;

/// <summary>
/// system_scaffold 的生成模式（設定 <c>HighLevelCoordinator:Generation:SystemScaffoldMode</c>）。
/// </summary>
public static class SystemScaffoldModes
{
    /// <summary>沿用 broker 程序內的佔位頁生成（預設）。</summary>
    public const string Legacy = "Legacy";

    /// <summary>broker 依任務啟動受控代理，經三個生成能力由 generation-worker 產出原型。</summary>
    public const string Governed = "Governed";
}

/// <summary><c>HighLevelCoordinator:Generation</c> 區段。</summary>
public sealed class HighLevelGenerationOptions
{
    /// <summary><see cref="SystemScaffoldModes.Legacy"/>（預設）或 <see cref="SystemScaffoldModes.Governed"/>。</summary>
    public string SystemScaffoldMode { get; set; } = SystemScaffoldModes.Legacy;

    /// <summary>
    /// 解析模式：空白或 Legacy 是舊路徑；Governed 是受治理生成；其他值視為設定錯誤，
    /// 走受治理路徑並由就緒檢查拒絕（fail-closed），不會靜默退回程序內生成。
    /// </summary>
    public string ResolveSystemScaffoldMode()
    {
        var value = (SystemScaffoldMode ?? string.Empty).Trim();
        if (value.Length == 0 || string.Equals(value, SystemScaffoldModes.Legacy, StringComparison.OrdinalIgnoreCase))
            return SystemScaffoldModes.Legacy;
        if (string.Equals(value, SystemScaffoldModes.Governed, StringComparison.OrdinalIgnoreCase))
            return SystemScaffoldModes.Governed;
        return "Invalid";
    }

    public bool UsesGovernedPath => ResolveSystemScaffoldMode() != SystemScaffoldModes.Legacy;

    public bool IsValidMode => ResolveSystemScaffoldMode() != "Invalid";
}

/// <summary>
/// 受治理生成的 broker 端設定（設定區段 <c>Generation</c>）。
/// broker 與 generation-worker 共用 <see cref="OutputRoot"/>：worker 寫入，broker 驗證後複製給使用者。
/// </summary>
public sealed class GovernedGenerationOptions
{
    public const string SectionName = "Generation";

    /// <summary>generation-worker 的輸出根目錄（絕對路徑，必須在各使用者工作區之外）。</summary>
    public string OutputRoot { get; set; } = string.Empty;

    /// <summary>從啟動代理起算，任務必須完成交付的期限（分鐘）；逾期由 watchdog 收掉。</summary>
    public int DeadlineMinutes { get; set; } = 15;

    /// <summary>代理的模型回合上限（AGENT_MAX_ITERATIONS）。</summary>
    public int AgentMaxIterations { get; set; } = 12;

    /// <summary>每個原型的頁數上限（寫進 generate 的 grant scope）。</summary>
    public int MaxPages { get; set; } = 12;

    /// <summary>交付與 watchdog 的檢查間隔（秒）。</summary>
    public int WatchdogIntervalSeconds { get; set; } = 10;

    /// <summary>broker 接受的 zip 大小上限（位元組）。</summary>
    public long MaxPackageBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// 解析後的輸出根目錄：未設定或不是絕對路徑時回傳 null（就緒檢查視為未就緒）。
    /// </summary>
    public string? ResolveOutputRoot()
    {
        if (string.IsNullOrWhiteSpace(OutputRoot))
            return null;

        var expanded = Environment.ExpandEnvironmentVariables(OutputRoot.Trim());
        return Path.IsPathFullyQualified(expanded)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded))
            : null;
    }

    public TimeSpan Deadline => TimeSpan.FromMinutes(Math.Clamp(DeadlineMinutes, 1, 24 * 60));

    public int ResolveAgentMaxIterations() => Math.Clamp(AgentMaxIterations, 1, 50);

    public int ResolveMaxPages() => Math.Clamp(MaxPages, 1, 100);

    public TimeSpan WatchdogInterval => TimeSpan.FromSeconds(Math.Clamp(WatchdogIntervalSeconds, 1, 300));
}

/// <summary>受治理生成的三個能力（以 tool-spec 為唯一來源；這裡只有 id、route 與設計 §5 的配額）。</summary>
public static class GenerationCapabilities
{
    public const string CatalogQuery = "generation.catalog.query";
    public const string DefinitionValidate = "generation.definition.validate";
    public const string ScaffoldGenerate = "generation.scaffold.generate";

    public const string CatalogRoute = "query_component_catalog";
    public const string ValidateRoute = "validate_definition";
    public const string GenerateRoute = "generate_scaffold";

    public const int CatalogQuota = 20;
    public const int ValidateQuota = 6;
    public const int GenerateQuota = 2;

    /// <summary>generate 唯一支援的產物格式。</summary>
    public const string Package = "definition-site-v1";

    /// <summary>受治理代理的角色（允許 system_scaffold）。</summary>
    public const string ExecutorRole = "role_executor";

    public static readonly IReadOnlyList<string> All = new[] { CatalogQuery, DefinitionValidate, ScaffoldGenerate };
}
