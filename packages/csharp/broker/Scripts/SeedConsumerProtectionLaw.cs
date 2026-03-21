using System.Text.Json;
using System.Text.RegularExpressions;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Scripts;

/// <summary>
/// 消費者保護法 RAG 種子 — 從法規原文建立 FTS5 + 向量嵌入
///
/// 支援的法規：
/// - 消費者保護法
/// - 消費者保護法施行細則
/// - 消費爭議調解辦法
///
/// 呼叫方式：
///   await SeedConsumerProtectionLaw.SeedAsync(db, embeddingService, logger);
/// </summary>
public static class SeedConsumerProtectionLaw
{
    private const string TaskId = "global";
    private const string PrincipalId = "system_rag_seeder";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>從網路抓取法規並寫入 RAG 資料庫</summary>
    public static async Task<SeedResult> SeedAsync(
        BrokerDb db,
        EmbeddingService? embeddingService = null,
        ILogger? logger = null)
    {
        var result = new SeedResult();

        var laws = new[]
        {
            new LawSource("消費者保護法", "https://law.moj.gov.tw/LawClass/LawAll.aspx?pcode=J0170001"),
            new LawSource("消費者保護法施行細則", "https://law.moj.gov.tw/LawClass/LawAll.aspx?pcode=J0170002"),
            new LawSource("消費爭議調解辦法", "https://law.moj.gov.tw/LawClass/LawAll.aspx?pcode=J0170005"),
        };

        foreach (var law in laws)
        {
            logger?.LogInformation("Fetching {Name} from {Url}", law.Name, law.Url);

            try
            {
                var html = await _http.GetStringAsync(law.Url);
                var articles = ParseLawArticles(html, law.Name);

                logger?.LogInformation("Parsed {Count} articles from {Name}", articles.Count, law.Name);

                foreach (var article in articles)
                {
                    var key = $"消費者保護法:{law.Name}:{article.ArticleNo}";
                    var value = article.Content;

                    // 寫入 SharedContextEntry
                    var documentId = $"mem_{TaskId}_{key}";
                    var existing = db.GetAll<SharedContextEntry>()
                        .Where(e => e.TaskId == TaskId && e.Key == key)
                        .OrderByDescending(e => e.Version)
                        .FirstOrDefault();

                    var version = (existing?.Version ?? 0) + 1;

                    // 跳過內容未變更的
                    if (existing != null && existing.ContentRef == value)
                    {
                        result.Skipped++;
                        continue;
                    }

                    db.Insert(new SharedContextEntry
                    {
                        EntryId = IdGen.New("sce"),
                        DocumentId = documentId,
                        Version = version,
                        ParentVersion = existing?.Version,
                        Key = key,
                        ContentRef = value,
                        ContentType = "text/plain",
                        AuthorPrincipalId = PrincipalId,
                        TaskId = TaskId,
                        CreatedAt = DateTime.UtcNow
                    });

                    // FTS5 索引
                    try
                    {
                        db.Execute("DELETE FROM memory_fts WHERE source_key = @key AND task_id = @taskId",
                            new { key, taskId = TaskId });
                        // CJK 字元間插入空格，讓 unicode61 tokenizer 正確分詞
                        var ftsValue = PrepareFts5Content(value);
                        db.Execute("INSERT INTO memory_fts(source_key, content, task_id) VALUES(@key, @value, @taskId)",
                            new { key, value = ftsValue, taskId = TaskId });
                    }
                    catch { }

                    // 向量嵌入
                    if (embeddingService is { IsEnabled: true })
                    {
                        try
                        {
                            var hash = EmbeddingService.ComputeHash(value);
                            var existingVec = db.GetAll<VectorEntry>()
                                .FirstOrDefault(v => v.ContentHash == hash && v.TaskId == TaskId);

                            if (existingVec == null)
                            {
                                var embedText = $"{law.Name} {article.ArticleNo}: {value}";
                                var vector = await embeddingService.EmbedAsync(embedText);

                                if (vector != null)
                                {
                                    db.Execute("DELETE FROM vector_entries WHERE source_key = @key AND task_id = @taskId",
                                        new { key, taskId = TaskId });

                                    db.Insert(new VectorEntry
                                    {
                                        EntryId = IdGen.New("vec"),
                                        SourceKey = key,
                                        TaskId = TaskId,
                                        TextPreview = value.Length > 500 ? value[..500] : value,
                                        ContentHash = hash,
                                        Embedding = EmbeddingService.VectorToBytes(vector),
                                        EmbeddingModel = embeddingService.ModelName,
                                        Dimension = vector.Length,
                                        CreatedAt = DateTime.UtcNow
                                    });
                                    result.Embedded++;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning(ex, "Embedding failed for {Key}", key);
                        }
                    }

                    result.Inserted++;
                }

                result.LawsFetched++;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to fetch {Name}", law.Name);
                result.Errors.Add($"{law.Name}: {ex.Message}");
            }
        }

        logger?.LogInformation(
            "RAG seed complete: {Fetched} laws, {Inserted} articles inserted, {Skipped} skipped, {Embedded} embedded",
            result.LawsFetched, result.Inserted, result.Skipped, result.Embedded);

        return result;
    }

    /// <summary>
    /// 從法務部全國法規資料庫 HTML 解析法條
    ///
    /// HTML 結構：
    /// &lt;div class="row"&gt;
    ///   &lt;div class="col-no"&gt;第 X 條&lt;/div&gt;
    ///   &lt;div class="col-data"&gt;&lt;div class="law-article"&gt;...內容...&lt;/div&gt;&lt;/div&gt;
    /// &lt;/div&gt;
    /// </summary>
    private static List<LawArticle> ParseLawArticles(string html, string lawName)
    {
        var articles = new List<LawArticle>();

        // 匹配每個 row：col-no 取條號，col-data/law-article 取內容
        var rowPattern = new Regex(
            @"<div\s+class=""col-no"">\s*(?:<a[^>]*>)?\s*第\s*([\d\-]+)\s*條\s*(?:</a>)?\s*</div>" +
            @"\s*<div\s+class=""col-data"">(.+?)(?=<div\s+class=""col-no""|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var matches = rowPattern.Matches(html);

        foreach (Match m in matches)
        {
            var articleNo = $"第{m.Groups[1].Value}條";
            var rawContent = m.Groups[2].Value;

            // 清理 HTML → 純文字
            var content = rawContent;
            // 移除 script/style
            content = Regex.Replace(content, @"<(script|style)[^>]*>.*?</\1>", "",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            // <br> → \n
            content = Regex.Replace(content, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            // line-0000 → \n
            content = Regex.Replace(content, @"<div[^>]*class=""line-\d+[^""]*""[^>]*>", "\n", RegexOptions.IgnoreCase);
            // </div> → \n
            content = Regex.Replace(content, @"</div>", "\n", RegexOptions.IgnoreCase);
            // 移除所有標籤
            content = Regex.Replace(content, @"<[^>]+>", "");
            // 解碼
            content = System.Net.WebUtility.HtmlDecode(content);
            // 壓縮空白
            content = Regex.Replace(content, @"[ \t]+", " ");
            content = Regex.Replace(content, @"\n\s*\n+", "\n");
            content = content.Trim();

            if (content.Length < 3) continue;
            if (content.Length > 3000)
                content = content[..3000] + "...";

            articles.Add(new LawArticle
            {
                LawName = lawName,
                ArticleNo = articleNo,
                Content = $"【{lawName}】{articleNo}\n{content}"
            });
        }

        return articles;
    }

    /// <summary>
    /// CJK 字元間插入空格，讓 FTS5 unicode61 tokenizer 能正確分詞
    /// </summary>
    private static string PrepareFts5Content(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length * 2);
        bool prevIsCjk = false;
        foreach (var ch in text)
        {
            bool isCjk = char.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.OtherLetter;
            if (isCjk)
            {
                if (sb.Length > 0 && !prevIsCjk) sb.Append(' ');
                else if (prevIsCjk) sb.Append(' ');
                sb.Append(ch);
            }
            else if (ch == ' ' || ch == '\t')
            {
                if (sb.Length > 0) sb.Append(' ');
            }
            else if (ch == '\n' || ch == '\r')
            {
                sb.Append(ch); // 保留換行
            }
            else
            {
                if (sb.Length > 0 && prevIsCjk) sb.Append(' ');
                sb.Append(ch);
            }
            prevIsCjk = isCjk;
        }
        return sb.ToString();
    }

    // ── DTO ──

    private record LawSource(string Name, string Url);

    private class LawArticle
    {
        public string LawName { get; set; } = "";
        public string ArticleNo { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public class SeedResult
    {
        public int LawsFetched { get; set; }
        public int Inserted { get; set; }
        public int Skipped { get; set; }
        public int Embedded { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
