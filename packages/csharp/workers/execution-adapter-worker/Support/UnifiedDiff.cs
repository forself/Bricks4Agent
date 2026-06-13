namespace ExecutionAdapterWorker.Support;

/// <summary>
/// 極簡 unified diff 檢視器：抽出受影響檔案路徑、判斷是否確為 patch。
/// 用於 §14.2「不得直接接受自由 shell」「不得在未授權路徑寫入」的前置檢查。
/// 實際套用仍交給 `git apply`（含 --check）。
/// </summary>
public static class UnifiedDiff
{
    /// <summary>
    /// 看起來是否為 unified diff：需含 hunk 標記(@@)且有檔案標頭
    /// (diff --git 或 ---/+++)。空字串或純命令字串會回 false。
    /// </summary>
    public static bool IsLikelyPatch(string? patch)
    {
        if (string.IsNullOrWhiteSpace(patch)) return false;
        var hasHunk = patch.Contains("\n@@ ") || patch.StartsWith("@@ ");
        var hasHeader = patch.Contains("diff --git ")
            || (patch.Contains("\n--- ") || patch.StartsWith("--- "))
            && (patch.Contains("\n+++ ") || patch.Contains("+++ "));
        return hasHunk && hasHeader;
    }

    /// <summary>
    /// 抽出 patch 影響的目標檔案路徑（去掉 a//b/ 前綴、忽略 /dev/null）。
    /// 同時看 "diff --git a/X b/Y"、"--- a/X"、"+++ b/Y"。
    /// </summary>
    public static IReadOnlyList<string> ExtractPaths(string patch)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in patch.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.StartsWith("diff --git "))
            {
                // diff --git a/<x> b/<y>
                var rest = line.Substring("diff --git ".Length).Trim();
                foreach (var token in rest.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    AddStripped(paths, token);
            }
            else if (line.StartsWith("--- ") || line.StartsWith("+++ "))
            {
                var token = line.Substring(4).Trim();
                // 去掉時間戳尾巴（"\t2020-..."）
                var tab = token.IndexOf('\t');
                if (tab >= 0) token = token[..tab];
                AddStripped(paths, token);
            }
        }
        return paths.ToList();
    }

    private static void AddStripped(HashSet<string> set, string token)
    {
        if (string.IsNullOrEmpty(token)) return;
        if (token == "/dev/null") return;
        // 去掉 git 的 a/ b/ 前綴
        if (token.StartsWith("a/") || token.StartsWith("b/"))
            token = token[2..];
        token = token.Replace('\\', '/').TrimStart('/');
        if (token.Length > 0) set.Add(token);
    }

    /// <summary>
    /// 路徑是否落在任一 allowedPath 之下（皆以 '/' 正規化的相對路徑比較）。
    /// allowedPaths 為空代表「未限制」(由呼叫端決定是否視為拒絕)。
    /// </summary>
    public static bool IsUnderAllowed(string path, IReadOnlyList<string> allowedPaths)
    {
        var p = path.Replace('\\', '/').TrimStart('/');
        foreach (var allowedRaw in allowedPaths)
        {
            var allowed = allowedRaw.Replace('\\', '/').Trim().TrimStart('/').TrimEnd('/');
            if (allowed.Length == 0) continue;
            if (p == allowed) return true;
            if (p.StartsWith(allowed + "/", StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
