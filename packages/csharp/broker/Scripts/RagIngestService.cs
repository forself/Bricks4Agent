using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BrokerCore;
using BrokerCore.Data;
using BrokerCore.Models;
using BrokerCore.Services;

namespace Broker.Scripts;

/// <summary>
/// RAG 資料匯入服務 —— 支援兩種來源：
///
/// 1. 結構化匯入（CSV / JSON）
///    - CSV 格式：key,content,tag（首列為標題列）
///    - JSON 格式：[{ "key": "...", "content": "...", "tag": "..." }]
///    - tag 用於 source_key 前綴分類（例如 "消費者保護法"）
///
/// 2. 網路搜尋匯入
///    - 根據關鍵字搜尋網路
///    - 抓取頁面內容，分段後寫入 RAG 資料庫
///
/// 每筆資料同時寫入：SharedContextEntry + FTS5 索引 + 向量嵌入
/// </summary>
public static class RagIngestService
{
    private const string PrincipalId = "system_rag_ingest";
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // ════════════════════════════════════════════════
    //  1. 結構化匯入（CSV / JSON）
    // ════════════════════════════════════════════════

    /// <summary>
    /// 從 JSON 陣列匯入 RAG 資料
    ///
    /// 格式：
    /// [
    ///   { "key": "文件標題或ID", "content": "文件內容...", "tag": "分類標籤" },
    ///   ...
    /// ]
    ///
    /// tag 會作為 source_key 前綴：{tag}:{key}
    /// </summary>
    public static async Task<IngestResult> ImportJsonAsync(
        string jsonContent,
        string defaultTag,
        string taskId,
        BrokerDb db,
        EmbeddingService? embeddingService = null,
        ILogger? logger = null)
    {
        var result = new IngestResult();

        List<RagEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<RagEntry>>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            result.Errors.Add($"JSON 解析失敗: {ex.Message}");
            return result;
        }

        if (entries == null || entries.Count == 0)
        {
            result.Errors.Add("JSON 陣列為空或格式不正確");
            return result;
        }

        logger?.LogInformation("Importing {Count} entries from JSON with tag '{Tag}'", entries.Count, defaultTag);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Content))
            {
                result.Skipped++;
                continue;
            }

            var tag = string.IsNullOrWhiteSpace(entry.Tag) ? defaultTag : entry.Tag;
            await IngestSingleAsync(tag, entry.Key, entry.Content, taskId, db, embeddingService, result, logger, entry.Tags);
        }

        logger?.LogInformation("JSON import done: {Inserted} inserted, {Skipped} skipped, {Embedded} embedded",
            result.Inserted, result.Skipped, result.Embedded);

        return result;
    }

    /// <summary>
    /// 從 CSV 匯入 RAG 資料
    ///
    /// 格式（首列為標題列）：
    /// key,content,tag
    /// "文件標題","文件內容...","分類標籤"
    ///
    /// - tag 欄位可選，缺失時使用 defaultTag
    /// - 支援雙引號包圍的欄位（含逗號、換行）
    /// - 支援 UTF-8 BOM
    /// </summary>
    public static async Task<IngestResult> ImportCsvAsync(
        string csvContent,
        string defaultTag,
        string taskId,
        BrokerDb db,
        EmbeddingService? embeddingService = null,
        ILogger? logger = null)
    {
        var result = new IngestResult();

        // 移除 BOM
        if (csvContent.Length > 0 && csvContent[0] == '\uFEFF')
            csvContent = csvContent[1..];

        var rows = ParseCsv(csvContent);

        if (rows.Count < 2) // 至少需要標題列 + 一筆資料
        {
            result.Errors.Add("CSV 至少需要標題列和一筆資料列");
            return result;
        }

        // 解析標題列
        var header = rows[0];
        var keyIdx = FindColumnIndex(header, "key", "標題", "title", "id", "name");
        var contentIdx = FindColumnIndex(header, "content", "內容", "text", "body", "description");
        var tagIdx = FindColumnIndex(header, "tag", "標籤", "label", "category", "分類");

        if (keyIdx < 0)
        {
            result.Errors.Add("找不到 key 欄位（可接受：key, 標題, title, id, name）");
            return result;
        }
        if (contentIdx < 0)
        {
            result.Errors.Add("找不到 content 欄位（可接受：content, 內容, text, body, description）");
            return result;
        }

        logger?.LogInformation("Importing {Count} rows from CSV with tag '{Tag}'", rows.Count - 1, defaultTag);

        for (int i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count == 0 || (row.Count == 1 && string.IsNullOrWhiteSpace(row[0])))
            {
                result.Skipped++;
                continue;
            }

            var key = keyIdx < row.Count ? row[keyIdx].Trim() : "";
            var content = contentIdx < row.Count ? row[contentIdx].Trim() : "";
            var tag = (tagIdx >= 0 && tagIdx < row.Count && !string.IsNullOrWhiteSpace(row[tagIdx]))
                ? row[tagIdx].Trim()
                : defaultTag;

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(content))
            {
                result.Skipped++;
                continue;
            }

            await IngestSingleAsync(tag, key, content, taskId, db, embeddingService, result, logger);
        }

        logger?.LogInformation("CSV import done: {Inserted} inserted, {Skipped} skipped, {Embedded} embedded",
            result.Inserted, result.Skipped, result.Embedded);

        return result;
    }

    // ════════════════════════════════════════════════
    //  2. 網路搜尋匯入
    // ════════════════════════════════════════════════

    /// <summary>
    /// 根據搜尋條件從網路抓取內容並匯入 RAG
    ///
    /// 流程：
    /// 1. 使用搜尋引擎取得相關 URL
    /// 2. 逐一抓取頁面內容
    /// 3. 清理 HTML → 純文字
    /// 4. 分段（chunk）後寫入 RAG 資料庫
    /// </summary>
    public static async Task<IngestResult> ImportFromWebAsync(
        WebSearchRequest request,
        string taskId,
        BrokerDb db,
        EmbeddingService? embeddingService = null,
        ILogger? logger = null)
    {
        var result = new IngestResult();
        var tag = request.Tag ?? request.Query;

        // ── Step 1: 搜尋取得 URL ──
        List<SearchResultItem> searchResults;
        try
        {
            searchResults = await SearchWebAsync(request.Query, request.MaxPages, logger);
            logger?.LogInformation("Web search returned {Count} results for '{Query}'",
                searchResults.Count, request.Query);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"搜尋失敗: {ex.Message}");
            return result;
        }

        if (searchResults.Count == 0)
        {
            result.Errors.Add("搜尋無結果");
            return result;
        }

        // 如果使用者提供了特定 URL 列表，合併進去
        if (request.Urls is { Count: > 0 })
        {
            foreach (var url in request.Urls)
            {
                if (!searchResults.Any(r => r.Url == url))
                    searchResults.Add(new SearchResultItem { Url = url, Title = url });
            }
        }

        // ── Step 2: 逐一抓取 + 分段 + 寫入 ──
        var pageCount = 0;
        foreach (var item in searchResults)
        {
            if (pageCount >= request.MaxPages) break;

            try
            {
                logger?.LogInformation("Fetching {Url}", item.Url);
                var html = await FetchPageAsync(item.Url);
                if (string.IsNullOrWhiteSpace(html))
                {
                    result.Skipped++;
                    continue;
                }

                var plainText = HtmlToPlainText(html);
                if (plainText.Length < 50) // 太短的頁面跳過
                {
                    result.Skipped++;
                    continue;
                }

                // 分段
                var chunks = ChunkText(plainText, request.ChunkSize, request.ChunkOverlap);
                var pageTitle = item.Title ?? ExtractTitle(html) ?? item.Url;

                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunkKey = chunks.Count == 1
                        ? pageTitle
                        : $"{pageTitle} [{i + 1}/{chunks.Count}]";
                    var chunkContent = $"【{tag}】來源: {item.Url}\n{chunks[i]}";

                    await IngestSingleAsync(tag, chunkKey, chunkContent, taskId, db, embeddingService, result, logger);
                }

                pageCount++;
                result.PagesFetched++;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to fetch {Url}", item.Url);
                result.Errors.Add($"{item.Url}: {ex.Message}");
            }
        }

        logger?.LogInformation(
            "Web import done: {Pages} pages, {Inserted} chunks inserted, {Embedded} embedded",
            result.PagesFetched, result.Inserted, result.Embedded);

        return result;
    }

    /// <summary>
    /// 直接從指定 URL 列表抓取內容匯入（不經搜尋引擎）
    /// </summary>
    public static async Task<IngestResult> ImportFromUrlsAsync(
        List<string> urls,
        string tag,
        string taskId,
        BrokerDb db,
        int chunkSize = 1000,
        int chunkOverlap = 100,
        EmbeddingService? embeddingService = null,
        ILogger? logger = null)
    {
        var request = new WebSearchRequest
        {
            Query = tag,
            Tag = tag,
            Urls = urls,
            MaxPages = urls.Count,
            ChunkSize = chunkSize,
            ChunkOverlap = chunkOverlap
        };
        return await ImportFromWebAsync(request, taskId, db, embeddingService, logger);
    }

    // ════════════════════════════════════════════════
    //  共用：單筆寫入 SharedContextEntry + FTS5 + Vector
    // ════════════════════════════════════════════════

    /// <summary>自動分塊門檻（超過此字數的內容自動分塊）</summary>
    private const int AutoChunkThreshold = 800;
    private const int DefaultChunkSize = 500;
    private const int DefaultChunkOverlap = 80;

    private static async Task IngestSingleAsync(
        string tag, string key, string content, string taskId,
        BrokerDb db, EmbeddingService? embeddingService,
        IngestResult result, ILogger? logger,
        List<string>? tags = null)
    {
        var sourceKey = $"{tag}:{key}";
        var tagsJson = tags != null && tags.Count > 0
            ? JsonSerializer.Serialize(tags)
            : JsonSerializer.Serialize(new[] { tag });

        // ── 智慧分塊：長內容自動分段 ──
        if (content.Length > AutoChunkThreshold)
        {
            var chunks = ChunkText(content, DefaultChunkSize, DefaultChunkOverlap);
            if (chunks.Count > 1)
            {
                logger?.LogInformation("Auto-chunking '{Key}': {Total} chunks", sourceKey, chunks.Count);

                // 先寫入完整文件（作為父文件）
                await IngestSingleEntryAsync(sourceKey, content, taskId, db, embeddingService,
                    result, logger, tagsJson, parentKey: "", chunkIndex: 0, chunkTotal: chunks.Count);

                // 再寫入各分塊
                for (int i = 0; i < chunks.Count; i++)
                {
                    var chunkKey = $"{sourceKey}#chunk_{i + 1}";
                    await IngestSingleEntryAsync(chunkKey, chunks[i], taskId, db, embeddingService,
                        result, logger, tagsJson, parentKey: sourceKey, chunkIndex: i + 1, chunkTotal: chunks.Count);
                }
                return;
            }
        }

        // 短內容：直接寫入
        await IngestSingleEntryAsync(sourceKey, content, taskId, db, embeddingService,
            result, logger, tagsJson, parentKey: "", chunkIndex: 0, chunkTotal: 0);
    }

    private static async Task IngestSingleEntryAsync(
        string sourceKey, string content, string taskId,
        BrokerDb db, EmbeddingService? embeddingService,
        IngestResult result, ILogger? logger,
        string tagsJson, string parentKey, int chunkIndex, int chunkTotal)
    {
        // 檢查重複
        var existing = db.GetAll<SharedContextEntry>()
            .Where(e => e.TaskId == taskId && e.Key == sourceKey)
            .OrderByDescending(e => e.Version)
            .FirstOrDefault();

        if (existing != null && existing.ContentRef == content)
        {
            result.Skipped++;
            return;
        }

        var version = (existing?.Version ?? 0) + 1;

        // SharedContextEntry
        db.Insert(new SharedContextEntry
        {
            EntryId = IdGen.New("sce"),
            DocumentId = $"mem_{taskId}_{sourceKey}",
            Version = version,
            ParentVersion = existing?.Version,
            Key = sourceKey,
            ContentRef = content,
            ContentType = "text/plain",
            AuthorPrincipalId = PrincipalId,
            TaskId = taskId,
            Tags = tagsJson,
            CreatedAt = DateTime.UtcNow
        });

        // FTS5
        try
        {
            db.Execute("DELETE FROM memory_fts WHERE source_key = @key AND task_id = @taskId",
                new { key = sourceKey, taskId });
            var ftsContent = PrepareFts5Content(content);
            db.Execute("INSERT INTO memory_fts(source_key, content, task_id) VALUES(@key, @value, @taskId)",
                new { key = sourceKey, value = ftsContent, taskId });
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "FTS5 index failed for {Key}", sourceKey);
        }

        // Vector embedding
        if (embeddingService is { IsEnabled: true })
        {
            try
            {
                var hash = EmbeddingService.ComputeHash(content);
                var existingVec = db.GetAll<VectorEntry>()
                    .FirstOrDefault(v => v.ContentHash == hash && v.TaskId == taskId);

                if (existingVec == null)
                {
                    var embedText = $"{sourceKey}: {content}";
                    if (embedText.Length > 2048) embedText = embedText[..2048];

                    var vector = await embeddingService.EmbedAsync(embedText);
                    if (vector != null)
                    {
                        db.Execute("DELETE FROM vector_entries WHERE source_key = @key AND task_id = @taskId",
                            new { key = sourceKey, taskId });

                        db.Insert(new VectorEntry
                        {
                            EntryId = IdGen.New("vec"),
                            SourceKey = sourceKey,
                            TaskId = taskId,
                            TextPreview = content.Length > 500 ? content[..500] : content,
                            ContentHash = hash,
                            Embedding = EmbeddingService.VectorToBytes(vector),
                            EmbeddingModel = embeddingService.ModelName,
                            Dimension = vector.Length,
                            Tags = tagsJson,
                            ChunkIndex = chunkIndex,
                            ChunkTotal = chunkTotal,
                            ParentKey = parentKey,
                            CreatedAt = DateTime.UtcNow
                        });
                        result.Embedded++;
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Embedding failed for {Key}", sourceKey);
            }
        }

        result.Inserted++;
    }

    // ════════════════════════════════════════════════
    //  網路搜尋 helper
    // ════════════════════════════════════════════════

    private static async Task<List<SearchResultItem>> SearchWebAsync(string query, int maxResults, ILogger? logger)
    {
        var results = new List<SearchResultItem>();

        // 使用 DuckDuckGo HTML 搜尋（無需 API key）
        var encoded = Uri.EscapeDataString(query);
        var url = $"https://html.duckduckgo.com/html/?q={encoded}";

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "text/html");
            request.Headers.Add("Accept-Language", "zh-TW,zh;q=0.9,en;q=0.8");

            var response = await _http.SendAsync(request);
            var html = await response.Content.ReadAsStringAsync();

            // 解析 DuckDuckGo 結果
            var linkPattern = new Regex(
                @"<a[^>]+class=""result__a""[^>]+href=""([^""]+)""[^>]*>(.+?)</a>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match m in linkPattern.Matches(html))
            {
                if (results.Count >= maxResults) break;

                var href = m.Groups[1].Value;
                var title = Regex.Replace(m.Groups[2].Value, @"<[^>]+>", "").Trim();

                // DuckDuckGo 的 href 可能是重定向 URL
                if (href.Contains("uddg="))
                {
                    var uddg = Regex.Match(href, @"uddg=([^&]+)");
                    if (uddg.Success)
                        href = Uri.UnescapeDataString(uddg.Groups[1].Value);
                }

                if (Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    results.Add(new SearchResultItem
                    {
                        Url = href,
                        Title = System.Net.WebUtility.HtmlDecode(title)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "DuckDuckGo search failed, trying Google");

            // Fallback: Google 搜尋
            try
            {
                var gUrl = $"https://www.google.com/search?q={encoded}&num={maxResults}&hl=zh-TW";
                var gRequest = new HttpRequestMessage(HttpMethod.Get, gUrl);
                gRequest.Headers.Add("Accept", "text/html");
                gRequest.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                var gResponse = await _http.SendAsync(gRequest);
                var gHtml = await gResponse.Content.ReadAsStringAsync();

                var gPattern = new Regex(
                    @"<a[^>]+href=""/url\?q=([^&""]+)",
                    RegexOptions.IgnoreCase);

                foreach (Match gm in gPattern.Matches(gHtml))
                {
                    if (results.Count >= maxResults) break;
                    var href = Uri.UnescapeDataString(gm.Groups[1].Value);
                    if (Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
                        (uri.Scheme == "http" || uri.Scheme == "https"))
                    {
                        results.Add(new SearchResultItem { Url = href, Title = href });
                    }
                }
            }
            catch (Exception gex)
            {
                logger?.LogWarning(gex, "Google search also failed");
            }
        }

        return results;
    }

    private static async Task<string> FetchPageAsync(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "text/html,application/xhtml+xml");
        request.Headers.Add("Accept-Language", "zh-TW,zh;q=0.9,en;q=0.8");

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // 限制大小（最大 2MB）
        var content = await response.Content.ReadAsStringAsync();
        if (content.Length > 2 * 1024 * 1024)
            content = content[..(2 * 1024 * 1024)];

        return content;
    }

    // ════════════════════════════════════════════════
    //  文字處理 helper
    // ════════════════════════════════════════════════

    private static string HtmlToPlainText(string html)
    {
        // 移除 script/style
        var text = Regex.Replace(html, @"<(script|style|noscript)[^>]*>.*?</\1>", "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        // <br> → \n
        text = Regex.Replace(text, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        // 區塊標籤 → \n
        text = Regex.Replace(text, @"</(p|div|h[1-6]|li|tr|td|th|blockquote|pre)>", "\n",
            RegexOptions.IgnoreCase);
        // 移除所有標籤
        text = Regex.Replace(text, @"<[^>]+>", " ");
        // HTML entities
        text = System.Net.WebUtility.HtmlDecode(text);
        // 壓縮空白
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @"\n[ \t]+", "\n");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string? ExtractTitle(string html)
    {
        var m = Regex.Match(html, @"<title[^>]*>(.+?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success)
            return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
        return null;
    }

    /// <summary>
    /// 將長文字分段（chunk），支援重疊（overlap）
    /// </summary>
    private static List<string> ChunkText(string text, int chunkSize = 1000, int overlap = 100)
    {
        if (text.Length <= chunkSize)
            return new List<string> { text };

        var chunks = new List<string>();
        int start = 0;

        while (start < text.Length)
        {
            var end = Math.Min(start + chunkSize, text.Length);

            // 試圖在段落/句子邊界切割
            if (end < text.Length)
            {
                var breakPoint = text.LastIndexOf('\n', end - 1, Math.Min(end - start, 200));
                if (breakPoint <= start)
                    breakPoint = text.LastIndexOf('。', end - 1, Math.Min(end - start, 200));
                if (breakPoint <= start)
                    breakPoint = text.LastIndexOf('.', end - 1, Math.Min(end - start, 200));
                if (breakPoint > start)
                    end = breakPoint + 1;
            }

            chunks.Add(text[start..end].Trim());
            start = end - overlap;
            if (start <= 0 && chunks.Count > 0) break; // 安全防護
        }

        return chunks.Where(c => c.Length > 10).ToList();
    }

    // ════════════════════════════════════════════════
    //  CSV 解析
    // ════════════════════════════════════════════════

    /// <summary>RFC 4180 相容的 CSV 解析器（支援引號內含逗號、換行）</summary>
    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        int i = 0;

        while (i < csv.Length)
        {
            var ch = csv[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                    }
                    else
                    {
                        inQuotes = false;
                        i++;
                    }
                }
                else
                {
                    field.Append(ch);
                    i++;
                }
            }
            else
            {
                if (ch == '"' && field.Length == 0)
                {
                    inQuotes = true;
                    i++;
                }
                else if (ch == ',')
                {
                    currentRow.Add(field.ToString());
                    field.Clear();
                    i++;
                }
                else if (ch == '\r' || ch == '\n')
                {
                    currentRow.Add(field.ToString());
                    field.Clear();
                    if (currentRow.Any(f => !string.IsNullOrWhiteSpace(f)))
                        rows.Add(currentRow);
                    currentRow = new List<string>();
                    if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                        i += 2;
                    else
                        i++;
                }
                else
                {
                    field.Append(ch);
                    i++;
                }
            }
        }

        // 最後一筆
        if (field.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(field.ToString());
            if (currentRow.Any(f => !string.IsNullOrWhiteSpace(f)))
                rows.Add(currentRow);
        }

        return rows;
    }

    private static int FindColumnIndex(List<string> header, params string[] candidates)
    {
        for (int i = 0; i < header.Count; i++)
        {
            var col = header[i].Trim().ToLowerInvariant();
            foreach (var c in candidates)
            {
                if (col == c.ToLowerInvariant()) return i;
            }
        }
        return -1;
    }

    /// <summary>CJK 字元間插入空格，讓 FTS5 unicode61 tokenizer 正確分詞</summary>
    private static string PrepareFts5Content(string text)
    {
        var sb = new StringBuilder(text.Length * 2);
        bool prevIsCjk = false;
        foreach (var ch in text)
        {
            bool isCjk = char.GetUnicodeCategory(ch) == UnicodeCategory.OtherLetter;
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
                sb.Append(ch);
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

    // ════════════════════════════════════════════════
    //  DTO
    // ════════════════════════════════════════════════

    public class RagEntry
    {
        public string Key { get; set; } = "";
        public string Content { get; set; } = "";
        public string? Tag { get; set; }
        /// <summary>分類標籤陣列（可選，JSON 格式或逗號分隔）</summary>
        public List<string>? Tags { get; set; }
    }

    public class WebSearchRequest
    {
        /// <summary>搜尋關鍵字</summary>
        public string Query { get; set; } = "";
        /// <summary>分類標籤（預設使用 Query）</summary>
        public string? Tag { get; set; }
        /// <summary>最大抓取頁面數</summary>
        public int MaxPages { get; set; } = 5;
        /// <summary>指定 URL 列表（直接抓取，不經搜尋引擎）</summary>
        public List<string>? Urls { get; set; }
        /// <summary>分段大小（字元數）</summary>
        public int ChunkSize { get; set; } = 1000;
        /// <summary>分段重疊（字元數）</summary>
        public int ChunkOverlap { get; set; } = 100;
    }

    public class SearchResultItem
    {
        public string Url { get; set; } = "";
        public string? Title { get; set; }
    }

    public class IngestResult
    {
        public int Inserted { get; set; }
        public int Skipped { get; set; }
        public int Embedded { get; set; }
        public int PagesFetched { get; set; }
        public List<string> Errors { get; set; } = new();
    }
}
