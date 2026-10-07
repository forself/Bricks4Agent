using System.Text.RegularExpressions;

namespace GenerationWorker.Support;

/// <summary>
/// 確認要回給 broker 的內容不含這台主機的路徑。回傳結果會存進執行紀錄並原樣交給代理的模型，
/// 所以 worker 只回相對路徑與摘要。
/// </summary>
public static partial class LocalPathGuard
{
    [GeneratedRegex(@"^[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex DriveRootedRegex();

    /// <summary>
    /// 文字中是否出現任一個本機根目錄。兩種分隔符號與 JSON 跳脫後的寫法都比對，不分大小寫。
    /// 只算「完整的路徑」：前面不是路徑字元、後面是分隔符號、引號、空白或結尾，避免短根目錄（例如 /out）
    /// 誤判一般文字中的片段。
    /// </summary>
    public static bool ContainsLocalPath(string text, params string?[] roots)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                continue;

            var full = Path.GetFullPath(root).TrimEnd('/', '\\');
            if (full.Length <= 1)
                continue;

            var forward = full.Replace('\\', '/');
            var backward = full.Replace('/', '\\');
            foreach (var candidate in new[] { full, forward, backward, backward.Replace("\\", "\\\\") })
            {
                if (ContainsWholePath(text, candidate))
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsWholePath(string text, string candidate)
    {
        var index = 0;
        while ((index = text.IndexOf(candidate, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 ? '\0' : text[index - 1];
            var afterIndex = index + candidate.Length;
            var after = afterIndex >= text.Length ? '\0' : text[afterIndex];

            var startsPath = before == '\0' || !(char.IsLetterOrDigit(before) || before is '/' or '\\' or '.' or '-' or '_');
            var endsPath = after == '\0' || after is '/' or '\\' or '"' or '\'' || char.IsWhiteSpace(after);
            if (startsPath && endsPath)
                return true;

            index++;
        }

        return false;
    }

    /// <summary>單一值是否像絕對路徑：以 / 或 \ 開頭，或以磁碟機代號開頭。</summary>
    public static bool LooksAbsolute(string value)
        => value.StartsWith('/') || value.StartsWith('\\') || DriveRootedRegex().IsMatch(value);

    /// <summary>
    /// 生成器回傳的相對檔案路徑是否安全：以 / 分隔、不是絕對路徑、沒有空段、<c>.</c>、<c>..</c>、反斜線、冒號或控制字元。
    /// </summary>
    public static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 1024 || LooksAbsolute(value))
            return false;
        if (value.Contains('\\') || value.Contains(':') || value.Any(char.IsControl))
            return false;

        foreach (var segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                return false;
        }

        return true;
    }
}
