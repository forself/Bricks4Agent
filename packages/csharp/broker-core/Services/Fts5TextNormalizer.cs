using System.Globalization;
using System.Text;

namespace BrokerCore.Services;

public static class Fts5TextNormalizer
{
    public static string PrepareQuery(string query)
    {
        var normalized = NormalizeCjkSpacing(query);
        normalized = normalized.Replace("\"", "");
        normalized = normalized.Replace("*", "");
        normalized = normalized.Replace("(", "");
        normalized = normalized.Replace(")", "");
        normalized = normalized.Replace("^", "");
        normalized = normalized.Replace("-", " ");
        normalized = CollapseSpaces(normalized).Trim();
        return string.IsNullOrEmpty(normalized) ? query : normalized;
    }

    public static string PrepareContent(string text)
        => NormalizeCjkSpacing(text);

    private static string NormalizeCjkSpacing(string text)
    {
        var sb = new StringBuilder(text.Length * 2);
        var prevIsCjk = false;

        foreach (var ch in text)
        {
            var isCjk = char.GetUnicodeCategory(ch) == UnicodeCategory.OtherLetter;

            if (isCjk)
            {
                if (sb.Length > 0 && !prevIsCjk)
                    sb.Append(' ');
                else if (prevIsCjk)
                    sb.Append(' ');
                sb.Append(ch);
            }
            else if (ch is ' ' or '\t')
            {
                if (sb.Length > 0)
                    sb.Append(' ');
            }
            else if (ch is '\n' or '\r')
            {
                sb.Append(ch);
            }
            else
            {
                if (sb.Length > 0 && prevIsCjk)
                    sb.Append(' ');
                sb.Append(ch);
            }

            prevIsCjk = isCjk;
        }

        return CollapseSpaces(sb.ToString());
    }

    private static string CollapseSpaces(string value)
    {
        while (value.Contains("  ", StringComparison.Ordinal))
            value = value.Replace("  ", " ");
        return value;
    }
}
