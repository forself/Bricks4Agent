using System.Text.RegularExpressions;

namespace Broker.Services;

/// <summary>
/// 高階回覆（LINE、portal）不帶主機絕對路徑。
/// 受管工作區內的路徑改寫成相對於工作區根目錄的名稱；
/// 失敗訊息裡其他來源的絕對路徑只保留最後一段。
/// </summary>
public static class HighLevelReplyRedactor
{
    private const string PathCharClass = @"[^\s""'<>|*?]";
    private const string SegmentCharClass = @"[^\s""'<>|*?\\/]";

    private static readonly Regex WindowsDrivePath = new(
        @"(?<![A-Za-z0-9])[A-Za-z]:[\\/]" + PathCharClass + "*",
        RegexOptions.CultureInvariant);

    private static readonly Regex UncPath = new(
        @"(?<![\\/])\\\\" + SegmentCharClass + @"+[\\/]" + PathCharClass + "*",
        RegexOptions.CultureInvariant);

    // 至少兩段的 POSIX 絕對路徑；前一個字元是英數、冒號或斜線時（URL 的一部分）不算。
    private static readonly Regex PosixPath = new(
        @"(?<![\w:/.~\\-])/(?:[\w.@+-]+/)+[\w.@+-]*",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// 把 <paramref name="root"/> 底下的路徑改寫成相對名稱（以 / 分隔），根目錄本身改寫成「.」。
    /// </summary>
    public static string RedactRoot(string? text, string? root)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(root))
            return text ?? string.Empty;

        var normalizedRoot = root.Trim().TrimEnd('\\', '/');
        if (normalizedRoot.Length < 2)
            return text;

        var variants = new[]
        {
            normalizedRoot,
            normalizedRoot.Replace('\\', '/'),
            normalizedRoot.Replace('/', '\\')
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        var result = text;
        foreach (var variant in variants)
        {
            if (result.IndexOf(variant, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            // 靜態 Regex.Replace 會快取已編譯的樣式；根目錄在程序生命週期內固定
            result = Regex.Replace(
                result,
                Regex.Escape(variant) + @"(?<sep>[\\/])?(?<rest>" + PathCharClass + "*)",
                RewriteRootMatch,
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// 失敗訊息（例外訊息、子程序 stderr）中的絕對路徑只保留最後一段。
    /// </summary>
    public static string SanitizeDetail(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        var result = UncPath.Replace(text, match => LeafName(match.Value));
        result = WindowsDrivePath.Replace(result, match => LeafName(match.Value));
        result = PosixPath.Replace(result, match => LeafName(match.Value));
        return result;
    }

    /// <summary>路徑的最後一段（資料夾名或檔名）；空白時回傳空字串。</summary>
    public static string LeafName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var trimmed = path.Trim().TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        var leaf = index >= 0 ? trimmed[(index + 1)..] : trimmed;
        return string.IsNullOrWhiteSpace(leaf) ? "." : leaf;
    }

    /// <summary>
    /// <paramref name="path"/> 相對於 <paramref name="baseDirectory"/> 的名稱（以 / 分隔）；
    /// 不在其下時只回傳最後一段。
    /// </summary>
    public static string RelativeName(string? baseDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            try
            {
                var relative = Path.GetRelativePath(baseDirectory, path);
                if (!Path.IsPathRooted(relative) &&
                    !relative.StartsWith("..", StringComparison.Ordinal))
                {
                    return relative.Replace('\\', '/');
                }
            }
            catch (ArgumentException)
            {
                // 無法計算時退回最後一段
            }
        }

        return LeafName(path);
    }

    private static string RewriteRootMatch(Match match)
    {
        var rest = match.Groups["rest"].Value;
        if (!match.Groups["sep"].Success)
        {
            // 只比對到前綴（例如 root2、root.bak）就不是這個根目錄
            return ContinuesName(rest) ? match.Value : "." + rest;
        }

        return rest.Replace('\\', '/');
    }

    private static bool ContinuesName(string rest)
    {
        if (rest.Length == 0)
            return false;

        var first = rest[0];
        if (char.IsLetterOrDigit(first) || first is '_' or '-')
            return true;

        // 句尾的句點不算，「.bak」這類副檔名才算
        return first == '.' && rest.Length > 1 && char.IsLetterOrDigit(rest[1]);
    }
}
