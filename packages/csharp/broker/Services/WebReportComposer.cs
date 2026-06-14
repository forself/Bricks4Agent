using System.Text;

namespace Broker.Services;

/// <summary>
/// Pure assembly of the web report: builds the LLM synthesis prompt and composes the final
/// Markdown (synthesized body + numbered source citations). No I/O, fully unit-testable.
/// </summary>
public static class WebReportComposer
{
    public const int DefaultExcerptLength = 1500;

    /// <summary>Builds a grounded synthesis prompt that lists each source with an excerpt and asks for [n] citations.</summary>
    public static string BuildSynthesisPrompt(
        string topic,
        IReadOnlyList<WebReportSource> sources,
        int excerptLength = DefaultExcerptLength)
    {
        var sb = new StringBuilder();
        sb.AppendLine("你是一位嚴謹的研究分析師。請僅根據以下「來源資料」,為指定主題撰寫一份結構化的繁體中文 Markdown 報告。");
        sb.AppendLine();
        sb.AppendLine($"主題:{topic}");
        sb.AppendLine();
        sb.AppendLine("要求:");
        sb.AppendLine("- 以 `##` 小標題分段,內容客觀、條理清楚,可使用重點清單。");
        sb.AppendLine("- 在引用具體事實或數據處標註來源編號,例如 [1]、[2]。");
        sb.AppendLine("- 僅使用來源資料中的資訊,不得杜撰或臆測未提供的內容。");
        sb.AppendLine("- 不要自行輸出參考來源清單(系統會自動附加)。");
        sb.AppendLine();
        sb.AppendLine("來源資料:");
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var body = string.IsNullOrWhiteSpace(source.Content) ? source.Snippet : source.Content;
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {Title(source)} — {source.Url}");
            sb.AppendLine(Truncate(body, excerptLength));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Composes the final Markdown report. When <paramref name="synthesizedBody"/> is empty,
    /// falls back to a deterministic per-source summary so a report is always produced.
    /// A numbered "參考來源" citation list is always appended.
    /// </summary>
    public static string Compose(string topic, string? synthesizedBody, IReadOnlyList<WebReportSource> sources)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {topic} 研究報告");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(synthesizedBody))
        {
            sb.AppendLine(synthesizedBody.Trim());
        }
        else
        {
            sb.AppendLine("> 註:未取得 LLM 綜合結果,以下為各來源摘要彙整。");
            sb.AppendLine();
            sb.AppendLine("## 來源摘要");
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                var body = string.IsNullOrWhiteSpace(source.Content) ? source.Snippet : source.Content;
                sb.AppendLine();
                sb.AppendLine($"### {i + 1}. {Title(source)}");
                sb.AppendLine(string.IsNullOrWhiteSpace(body) ? "(無摘要)" : Truncate(body, 600));
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 參考來源");
        if (sources.Count == 0)
        {
            sb.AppendLine("(無)");
        }
        else
        {
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                sb.AppendLine($"{i + 1}. [{Title(source)}]({source.Url})");
            }
        }

        return sb.ToString().TrimEnd() + "\n";
    }

    private static string Title(WebReportSource source)
        => string.IsNullOrWhiteSpace(source.Title) ? source.Url : source.Title.Trim();

    private static string Truncate(string? text, int max)
    {
        text = (text ?? string.Empty).Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }
}
