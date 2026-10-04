namespace Broker.Services;

/// <summary>
/// broker 系統元件自己維護的 shared-context 文件（高階流程的使用者資料與設定、通知、對話紀錄、
/// plan 節點輸出、執行證據等）。
/// - 這些 document_id 前綴經由 <c>/api/v1/context/write</c> 只限管理員寫入；
/// - <c>hlm.</c>、<c>convlog:</c> 文件由高階流程（LINE、portal）的系統元件讀取，只採信系統元件（作者以 <c>system:</c> 開頭）寫入的版本，
///   其中大多數還限定在 <see cref="GlobalTaskId"/> 範圍（見 <see cref="TrustedGlobalCondition"/>）；
/// - <c>node_output_</c> 與 <c>browser.execution.</c>、<c>deployment.execution.</c> 執行證據由 broker
///   以執行者的身分寫入，讀取端依任務、key 或 document_id 讀取，不以系統作者為條件；
///   保留前綴對它們的作用只是不讓非管理員經由 context API 以這些 document_id 寫入。
/// </summary>
public static class SystemContextDocuments
{
    /// <summary>系統文件使用的範圍；沒有對應的任務，只有管理員能透過 context API 存取。</summary>
    public const string GlobalTaskId = "global";

    private static readonly string[] ReservedDocumentIdPrefixes =
    {
        "hlm.",
        "convlog:",
        "node_output_",
        "browser.execution.",
        "deployment.execution.",
    };

    /// <summary>
    /// document_id 是否落在系統保留的前綴內。比對不分大小寫，與讀取端以 LIKE 做的前綴查詢一致。
    /// </summary>
    public static bool IsReservedDocumentId(string? documentId)
    {
        if (string.IsNullOrEmpty(documentId))
            return false;

        var trimmed = documentId.TrimStart();
        foreach (var prefix in ReservedDocumentIdPrefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// SQL 條件：系統元件寫入 global 範圍的版本。
    /// <paramref name="alias"/> 為資料表別名（例如 <c>"e"</c>），未指定時直接用欄位名稱。
    /// </summary>
    public static string TrustedGlobalCondition(string? alias = null)
    {
        var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
        return $"{prefix}task_id = '{GlobalTaskId}' AND {SystemAuthoredCondition(alias)}";
    }

    /// <summary>SQL 條件：版本的作者是系統元件（不分任務範圍，供任務範圍內的系統文件使用）。</summary>
    public static string SystemAuthoredCondition(string? alias = null)
    {
        var prefix = string.IsNullOrEmpty(alias) ? string.Empty : alias + ".";
        return $"substr({prefix}author_principal_id, 1, 7) = 'system:'";
    }
}
