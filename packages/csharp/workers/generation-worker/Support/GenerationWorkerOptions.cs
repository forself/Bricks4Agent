namespace GenerationWorker.Support;

/// <summary>
/// generation-worker 的設定（設定區段 <c>Generation</c>）。
/// 輸出位置只由 <see cref="OutputRoot"/> 加上 grant scope 決定；請求參數中的任何路徑都不採用。
/// </summary>
public sealed class GenerationWorkerOptions
{
    /// <summary>生成器 CLI 相對 <see cref="ToolsRoot"/> 的位置（固定，不可由請求改變）。</summary>
    public const string CliRelativePath = "tools/generation/cli.mjs";

    /// <summary>node 執行檔（絕對路徑，或 PATH 上的指令名稱）。</summary>
    public string NodePath { get; set; } = "node";

    /// <summary>含 <c>tools/generation/cli.mjs</c> 的 repo 子集根目錄（設定 <c>Generation:ToolsRoot</c>）。</summary>
    public string ToolsRoot { get; set; } = string.Empty;

    /// <summary>產物根目錄（設定 <c>Generation:OutputRoot</c>）；實際輸出為 <c>{OutputRoot}/{output_slot}/{requestId}/</c>。</summary>
    public string OutputRoot { get; set; } = string.Empty;

    /// <summary>catalog 與 validate 的逾時。</summary>
    public TimeSpan QueryTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>build 的逾時。</summary>
    public TimeSpan BuildTimeout { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>送進 CLI 的 stdin 上限（位元組）。生成器自己的定義上限更小，超過這裡的請求直接拒絕。</summary>
    public int MaxInputBytes { get; set; } = 1024 * 1024;

    /// <summary>CLI stdout 上限（位元組）；超過時終止子程序並視為失敗。</summary>
    public int MaxStdoutBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>CLI stderr 保留上限（位元組）；超過的部分丟棄，只用於診斷日誌。</summary>
    public int MaxStderrBytes { get; set; } = 64 * 1024;

    /// <summary>node 子程序的 V8 heap 上限（MB，<c>--max-old-space-size</c>）。</summary>
    public int MaxOldSpaceMegabytes { get; set; } = 256;

    /// <summary>catalog／validate 回傳給 broker 的 JSON 上限（位元組）。</summary>
    public int MaxResultBytes { get; set; } = 256 * 1024;

    /// <summary>單一產物的檔案數上限。</summary>
    public int MaxPackageFiles { get; set; } = 20000;

    /// <summary>單一產物未壓縮總大小上限（位元組）。</summary>
    public long MaxPackageBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>保留期限的上限（小時，一年）。</summary>
    public const int MaxRetentionHours = 24 * 365;

    /// <summary>
    /// 產物在 <see cref="OutputRoot"/> 保留的時數（設定 <c>Generation:RetentionHours</c>，預設 24，範圍 1～8760）。
    /// 超過期限的 <c>{output_slot}/{requestId}/</c> 目錄在 worker 啟動時與每次 generate 之前刪除。
    /// </summary>
    public int RetentionHours { get; set; } = 24;

    public TimeSpan Retention => TimeSpan.FromHours(RetentionHours);

    public string CliPath => Path.Combine(ToolsRoot, CliRelativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// 解析 node 執行檔：設定值優先；未設時用環境變數 <c>B4A_NODE_PATH</c>（檔案存在時）；
    /// 都沒有時用 PATH 上的 <c>node</c>。
    /// </summary>
    public static string ResolveNodePath(string? configured, Func<string, string?>? getEnvironmentVariable = null)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim();

        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        var fromEnvironment = getEnvironmentVariable("B4A_NODE_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnvironment) && File.Exists(fromEnvironment.Trim()))
            return fromEnvironment.Trim();

        return OperatingSystem.IsWindows() ? "node.exe" : "node";
    }

    /// <summary>啟動前檢查：ToolsRoot 內要有 CLI，OutputRoot 要是絕對路徑。回傳錯誤訊息（null 表示通過）。</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ToolsRoot))
            return "Generation:ToolsRoot is not configured.";
        if (!File.Exists(CliPath))
            return $"Generation:ToolsRoot does not contain {CliRelativePath}.";
        if (string.IsNullOrWhiteSpace(OutputRoot))
            return "Generation:OutputRoot is not configured.";
        if (!Path.IsPathFullyQualified(OutputRoot))
            return "Generation:OutputRoot must be an absolute path.";
        if (QueryTimeout <= TimeSpan.Zero || BuildTimeout <= TimeSpan.Zero)
            return "Generation timeouts must be positive.";
        if (MaxInputBytes <= 0 || MaxStdoutBytes <= 0 || MaxStderrBytes <= 0 || MaxResultBytes <= 0)
            return "Generation size limits must be positive.";
        if (MaxOldSpaceMegabytes < 64)
            return "Generation:MaxOldSpaceMegabytes must be at least 64.";
        if (RetentionHours < 1 || RetentionHours > MaxRetentionHours)
            return $"Generation:RetentionHours must be between 1 and {MaxRetentionHours}.";
        return null;
    }
}
