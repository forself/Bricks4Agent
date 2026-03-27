using System.Globalization;
using System.Text;

namespace Broker.Helpers;

/// <summary>
/// FTS5 查詢語法工具
/// </summary>
public static class Fts5Utility
{
    /// <summary>
    /// 將中文文字轉為 FTS5 查詢語法
    /// unicode61 tokenizer 將 CJK 字元逐字切割，所以 "退貨" → "退 貨"
    /// 用空格隔開 → FTS5 預設 AND 語意
    /// </summary>
    public static string PrepareFts5Query(string query)
    {
        var sb = new StringBuilder(query.Length * 2);
        bool prevIsCjk = false;

        foreach (var ch in query)
        {
            bool isCjk = char.GetUnicodeCategory(ch) == UnicodeCategory.OtherLetter;

            if (isCjk)
            {
                if (sb.Length > 0 && !prevIsCjk)
                    sb.Append(' ');
                else if (prevIsCjk)
                    sb.Append(' ');
                sb.Append(ch);
            }
            else if (ch == ' ' || ch == '\t')
            {
                if (sb.Length > 0) sb.Append(' ');
            }
            else
            {
                if (sb.Length > 0 && prevIsCjk)
                    sb.Append(' ');
                sb.Append(ch);
            }

            prevIsCjk = isCjk;
        }

        var result = sb.ToString().Trim();
        result = result.Replace("\"", "");
        result = result.Replace("*", "");
        result = result.Replace("(", "");
        result = result.Replace(")", "");
        result = result.Replace("^", "");
        result = result.Replace("-", " ");
        while (result.Contains("  "))
            result = result.Replace("  ", " ");
        result = result.Trim();
        return string.IsNullOrEmpty(result) ? query : result;
    }
}
