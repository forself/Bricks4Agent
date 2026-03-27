using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Broker.Services;

public sealed class HighLevelRelationQueryService
{
    private static readonly string[] NearbyRelationKeywords =
    [
        "附近",
        "鄰近",
        "相鄰",
        "周邊",
        "nearby",
        "neighboring",
        "neighbouring",
        "adjacent",
        "bordering"
    ];

    private static readonly string[] AdministrativeRelationKeywords =
    [
        "行政區",
        "行政區劃",
        "行政區域",
        "區劃",
        "district",
        "districts",
        "county",
        "counties",
        "borough",
        "boroughs",
        "administrative division",
        "administrative divisions"
    ];

    private static readonly string[] AdministrativeEvidenceKeywords =
    [
        "行政區",
        "行政區劃",
        "行政區域",
        "區劃",
        "轄區",
        "區",
        "district",
        "districts",
        "county",
        "counties",
        "borough",
        "boroughs",
        "administrative division",
        "administrative divisions"
    ];

    private static readonly string[] NearbyEvidenceKeywords =
    [
        "附近",
        "鄰近",
        "相鄰",
        "周邊",
        "毗鄰",
        "接壤",
        "neighboring",
        "neighbouring",
        "adjacent",
        "bordering"
    ];

    private static readonly string[] AdministrativeNameSuffixes =
    [
        "區",
        "縣",
        "市",
        "州",
        "省",
        "郡",
        "鎮",
        "鄉",
        "村",
        "City",
        "County",
        "District",
        "State",
        "Province",
        "Borough",
        "Town",
        "Village"
    ];

    private static readonly string[] NoiseTitleKeywords =
    [
        "機場",
        "機場站",
        "airport",
        "weather",
        "氣象",
        "天氣",
        "station",
        "車站",
        "youtube",
        "reddit"
    ];

    private static readonly HashSet<string> GenericAdministrativeTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "行政區",
        "行政區域",
        "行政區劃",
        "區劃",
        "district",
        "districts",
        "county",
        "counties",
        "borough",
        "boroughs"
    };

    private static readonly string[] ChineseAdministrativeNoiseFragments =
    [
        "行政區劃列表",
        "行政區列表",
        "附近景點",
        "機場",
        "車站",
        "天氣",
        "氣象",
        "影片",
        "討論區"
    ];

    private readonly HighLevelQueryToolMediator _queryToolMediator;
    private readonly HighLevelLlmOptions _highLevelLlmOptions;
    private readonly HttpClient _httpClient;
    private readonly ILogger<HighLevelRelationQueryService> _logger;

    public HighLevelRelationQueryService(
        HighLevelQueryToolMediator queryToolMediator,
        HighLevelLlmOptions highLevelLlmOptions,
        IHttpClientFactory httpClientFactory,
        ILogger<HighLevelRelationQueryService> logger)
    {
        _queryToolMediator = queryToolMediator;
        _highLevelLlmOptions = highLevelLlmOptions;
        _httpClient = httpClientFactory.CreateClient("high-level-llm");
        _logger = logger;
    }

    public static bool TryExtractAdministrativeRelationQuery(string query, out HighLevelRelationQueryPlan plan)
    {
        plan = new HighLevelRelationQueryPlan();
        var trimmed = NormalizeWhitespace(query);
        if (string.IsNullOrWhiteSpace(trimmed))
            return false;

        var relationType = DetectRelationType(trimmed);
        if (relationType == null)
            return false;

        var subject = ExtractSubject(trimmed);
        if (string.IsNullOrWhiteSpace(subject))
            return false;

        plan = new HighLevelRelationQueryPlan
        {
            OriginalQuery = trimmed,
            RelationType = relationType,
            Subject = subject,
            WikipediaQueries = BuildWikipediaQueries(subject, relationType),
            WebFallbackQueries = BuildWebFallbackQueries(subject, relationType)
        };
        return true;
    }

    public async Task<HighLevelRelationQueryResult> TryAnswerAsync(
        string channel,
        string userId,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!TryExtractAdministrativeRelationQuery(query, out var plan))
            return HighLevelRelationQueryResult.NotHandled();

        var evidence = new List<HighLevelRelationEvidenceItem>();

        foreach (var plannedQuery in plan.WikipediaQueries)
        {
            var wikipediaResult = await _queryToolMediator.SearchWikipediaAsync(channel, userId, plannedQuery, cancellationToken);
            if (!wikipediaResult.Success)
                continue;

            AddEvidence(evidence, plan.Subject, plan.RelationType, plannedQuery, wikipediaResult.Results);
        }

        if (CountStrongEvidence(evidence) < 2)
        {
            foreach (var plannedQuery in plan.WebFallbackQueries)
            {
                var searchResult = await _queryToolMediator.SearchWebAsync(channel, userId, plannedQuery, cancellationToken);
                if (!searchResult.Success)
                    continue;

                AddEvidence(evidence, plan.Subject, plan.RelationType, plannedQuery, searchResult.Results);
            }
        }

        var rankedEvidence = evidence
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

        if (rankedEvidence.Count == 0)
            return HighLevelRelationQueryResult.NotHandled();

        var nearbyTerms = ExtractAdministrativeTerms(plan.Subject, rankedEvidence, nearbyOnly: true)
            .Take(10)
            .ToArray();
        var candidateTerms = ExtractAdministrativeTerms(plan.Subject, rankedEvidence, nearbyOnly: false)
            .Take(10)
            .ToArray();

        string reply;
        if (ShouldUseLlmSynthesis(plan, rankedEvidence, nearbyTerms, candidateTerms))
        {
            reply = await ReasonAdministrativeRelationAsync(plan, rankedEvidence, nearbyTerms, candidateTerms, cancellationToken)
                ?? BuildDeterministicAdministrativeReply(plan, rankedEvidence, nearbyTerms, candidateTerms);
        }
        else
        {
            reply = BuildDeterministicAdministrativeReply(plan, rankedEvidence, nearbyTerms, candidateTerms);
        }

        return new HighLevelRelationQueryResult
        {
            Handled = true,
            Reply = reply,
            DecisionReason = "high-level relation query reasoning over wikipedia-first broker-mediated evidence"
        };
    }

    private static void AddEvidence(
        ICollection<HighLevelRelationEvidenceItem> evidence,
        string subject,
        string relationType,
        string plannedQuery,
        IReadOnlyList<HighLevelQuerySearchResult> results)
    {
        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.Url) || string.IsNullOrWhiteSpace(result.Title))
                continue;

            var score = ScoreAdministrativeEvidence(subject, relationType, result);
            if (score <= 0)
                continue;

            evidence.Add(new HighLevelRelationEvidenceItem
            {
                Query = plannedQuery,
                Title = result.Title,
                Url = result.Url,
                Snippet = result.Snippet,
                Score = score
            });
        }
    }

    private static int CountStrongEvidence(IEnumerable<HighLevelRelationEvidenceItem> evidence)
        => evidence.Count(item => item.Score >= 10);

    private static string NormalizeWhitespace(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : Regex.Replace(text.Trim(), @"\s+", " ");

    private static string? DetectRelationType(string query)
    {
        var hasNearby = NearbyRelationKeywords.Any(keyword => query.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        var hasAdministrative = AdministrativeRelationKeywords.Any(keyword => query.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        if (!hasNearby && !hasAdministrative)
            return null;

        return hasNearby
            ? "administrative_neighbor_relation"
            : "administrative_relation";
    }

    private static string ExtractSubject(string query)
    {
        if (TryMatchSubject(
                query,
                @"^(?<subject>.+?)\s+(nearby|neighboring|neighbouring|adjacent|bordering)\s+(administrative divisions?|districts?|counties?|boroughs?)$",
                out var englishNearbySubject))
        {
            return englishNearbySubject;
        }

        if (TryMatchSubject(
                query,
                @"^(?<subject>.+?)\s+(administrative divisions?|districts?|counties?|boroughs?)$",
                out var englishAdministrativeSubject))
        {
            return englishAdministrativeSubject;
        }

        var cleaned = query;
        foreach (var keyword in NearbyRelationKeywords.Concat(AdministrativeRelationKeywords))
            cleaned = cleaned.Replace(keyword, "", StringComparison.OrdinalIgnoreCase);

        cleaned = Regex.Replace(cleaned, @"^(請問|請幫我查|幫我查|查一下)\s*", "", RegexOptions.IgnoreCase);
        cleaned = cleaned
            .Replace("有哪些", "", StringComparison.OrdinalIgnoreCase)
            .Replace("有哪些？", "", StringComparison.OrdinalIgnoreCase)
            .Replace("有哪些?", "", StringComparison.OrdinalIgnoreCase)
            .Replace("哪一些", "", StringComparison.OrdinalIgnoreCase)
            .Replace("的", "", StringComparison.OrdinalIgnoreCase)
            .Trim(' ', '？', '?', '，', ',', '。', ':', '：');

        return NormalizeWhitespace(cleaned);
    }

    private static bool TryMatchSubject(string input, string pattern, out string subject)
    {
        var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success)
        {
            subject = NormalizeWhitespace(match.Groups["subject"].Value);
            return !string.IsNullOrWhiteSpace(subject);
        }

        subject = string.Empty;
        return false;
    }

    private static IReadOnlyList<string> BuildWikipediaQueries(string subject, string relationType)
    {
        var queries = new List<string>();
        var isAscii = subject.All(ch => ch <= 127);

        if (isAscii)
        {
            if (relationType == "administrative_neighbor_relation")
            {
                queries.Add($"{subject} administrative divisions");
                queries.Add($"{subject} neighboring administrative divisions");
                queries.Add($"{subject} bordering districts");
            }
            else
            {
                queries.Add($"{subject} administrative divisions");
                queries.Add($"{subject} districts");
                queries.Add($"{subject} borough county district list");
            }
        }
        else
        {
            if (relationType == "administrative_neighbor_relation")
            {
                queries.Add($"{subject} 行政區劃");
                queries.Add($"{subject} 鄰近 行政區");
                queries.Add($"{subject} 相鄰 行政區");
            }
            else
            {
                queries.Add($"{subject} 行政區劃");
                queries.Add($"{subject} 行政區");
                queries.Add($"{subject} 轄區");
            }
        }

        return queries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IReadOnlyList<string> BuildWebFallbackQueries(string subject, string relationType)
    {
        var queries = new List<string>();
        var isAscii = subject.All(ch => ch <= 127);

        if (isAscii)
        {
            if (relationType == "administrative_neighbor_relation")
            {
                queries.Add($"{subject} nearby administrative divisions");
                queries.Add($"{subject} neighboring districts");
            }
            else
            {
                queries.Add($"{subject} administrative divisions");
                queries.Add($"{subject} districts list");
            }
        }
        else
        {
            if (relationType == "administrative_neighbor_relation")
            {
                queries.Add($"{subject} 附近 行政區");
                queries.Add($"{subject} 鄰近 行政區");
            }
            else
            {
                queries.Add($"{subject} 行政區劃");
                queries.Add($"{subject} 行政區 名單");
            }
        }

        return queries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<string?> ReasonAdministrativeRelationAsync(
        HighLevelRelationQueryPlan plan,
        IReadOnlyList<HighLevelRelationEvidenceItem> evidence,
        IReadOnlyList<string> nearbyTerms,
        IReadOnlyList<string> candidateTerms,
        CancellationToken ct)
    {
        try
        {
            var prompt = BuildAdministrativeRelationPrompt(plan, evidence, nearbyTerms, candidateTerms);
            var messages = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "system",
                    ["content"] =
                        "你是高階查詢協調者。你只能根據 broker-mediated 證據整理回覆，不能把推測寫成事實。" +
                        "若證據不足，必須直接說證據不足，並保留來源列表。" +
                        "請避免把行政區劃頁面誤當成鄰接關係證明。"
                },
                new JsonObject
                {
                    ["role"] = "user",
                    ["content"] = prompt
                }
            };

            return await CallLlmAsync(messages, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to synthesize administrative relation answer for {Subject}", plan.Subject);
            return null;
        }
    }

    private static string BuildAdministrativeRelationPrompt(
        HighLevelRelationQueryPlan plan,
        IReadOnlyList<HighLevelRelationEvidenceItem> evidence,
        IReadOnlyList<string> nearbyTerms,
        IReadOnlyList<string> candidateTerms)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"原始問題：{plan.OriginalQuery}");
        sb.AppendLine($"核心主詞：{plan.Subject}");
        sb.AppendLine($"關係類型：{plan.RelationType}");
        sb.AppendLine($"附近或相鄰候選：{(nearbyTerms.Count == 0 ? "無" : string.Join("、", nearbyTerms))}");
        sb.AppendLine($"行政區候選：{(candidateTerms.Count == 0 ? "無" : string.Join("、", candidateTerms))}");
        sb.AppendLine();
        sb.AppendLine("證據：");

        var index = 1;
        foreach (var item in evidence)
        {
            sb.AppendLine($"{index}. 查詢：{item.Query}");
            sb.AppendLine($"標題：{item.Title}");
            sb.AppendLine($"網址：{item.Url}");
            if (!string.IsNullOrWhiteSpace(item.Snippet))
                sb.AppendLine($"摘要：{item.Snippet}");
            sb.AppendLine();
            index++;
        }

        sb.AppendLine("回覆規則：");
        sb.AppendLine("1. 只能根據證據整理，不要補完未知地理關係。");
        sb.AppendLine("2. 如果只是行政區劃頁面，不能直接推定哪些行政區彼此相鄰。");
        sb.AppendLine("3. 請用 1 到 3 段短文字回答，最後保留來源。");
        return sb.ToString();
    }

    private async Task<string?> CallLlmAsync(JsonArray messages, CancellationToken ct)
    {
        var provider = (_highLevelLlmOptions.Provider ?? "ollama").Trim().ToLowerInvariant();
        return provider switch
        {
            "ollama" => await SendOllamaChatAsync(messages, ct),
            _ => string.Equals(_highLevelLlmOptions.ApiFormat, "responses", StringComparison.OrdinalIgnoreCase)
                ? await SendResponsesApiAsync(messages, ct)
                : await SendChatCompletionsAsync(messages, ct)
        };
    }

    private async Task<string?> SendOllamaChatAsync(JsonArray messages, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["messages"] = messages,
            ["stream"] = false
        };

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("api/chat", content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString();
    }

    private async Task<string?> SendChatCompletionsAsync(JsonArray messages, CancellationToken ct)
    {
        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["messages"] = messages,
            ["stream"] = false
        };

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("v1/chat/completions", content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        var choices = doc.RootElement.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
            return null;
        return choices[0].GetProperty("message").GetProperty("content").GetString();
    }

    private async Task<string?> SendResponsesApiAsync(JsonArray messages, CancellationToken ct)
    {
        var input = new JsonArray();
        foreach (var msg in messages.OfType<JsonObject>())
        {
            var role = msg["role"]?.GetValue<string>() ?? "user";
            var text = msg["content"]?.GetValue<string>() ?? string.Empty;
            var contentType = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? "output_text"
                : "input_text";

            input.Add(new JsonObject
            {
                ["role"] = role,
                ["content"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = contentType,
                        ["text"] = text
                    }
                }
            });
        }

        var request = new JsonObject
        {
            ["model"] = _highLevelLlmOptions.DefaultModel,
            ["input"] = input
        };

        using var content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync("v1/responses", content, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement.TryGetProperty("output_text", out var outputText)
            ? outputText.GetString()
            : null;
    }

    private static int ScoreAdministrativeEvidence(string subject, string relationType, HighLevelQuerySearchResult result)
    {
        var merged = $"{result.Title}\n{result.Snippet}";
        var score = 0;
        var hasStrongSubjectAssociation = HasStrongSubjectAssociation(subject, result);
        var isNeighborQuery = string.Equals(relationType, "administrative_neighbor_relation", StringComparison.OrdinalIgnoreCase);

        if (hasStrongSubjectAssociation)
            score += 4;

        if (result.Url.Contains("wikipedia.org", StringComparison.OrdinalIgnoreCase))
            score += 6;

        if (result.Url.Contains(".gov", StringComparison.OrdinalIgnoreCase) ||
            result.Url.Contains("gov.", StringComparison.OrdinalIgnoreCase) ||
            result.Url.Contains("gov.tw", StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        if (AdministrativeEvidenceKeywords.Any(keyword => merged.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            score += 4;

        if (NearbyEvidenceKeywords.Any(keyword => merged.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            score += 4;

        if (result.Title.Contains(subject, StringComparison.OrdinalIgnoreCase))
            score += 3;

        var isAdminListPage = merged.Contains("list of administrative divisions", StringComparison.OrdinalIgnoreCase) ||
                              merged.Contains("行政區劃", StringComparison.OrdinalIgnoreCase);
        if (isAdminListPage)
            score += isNeighborQuery ? 2 : 5;

        if (!hasStrongSubjectAssociation && !LooksLikeAdministrativeListPage(subject, result))
            score -= 4;

        if (NoiseTitleKeywords.Any(keyword => result.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            score -= 6;

        if (merged.Contains("People also ask", StringComparison.OrdinalIgnoreCase))
            score -= 3;

        return score;
    }

    private static IEnumerable<string> ExtractAdministrativeTerms(
        string subject,
        IReadOnlyList<HighLevelRelationEvidenceItem> evidence,
        bool nearbyOnly)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in evidence.Where(ShouldUseForTermExtraction))
        {
            var merged = $"{item.Title}\n{item.Snippet}";
            if (nearbyOnly &&
                !NearbyEvidenceKeywords.Any(keyword => merged.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var tokenSource = nearbyOnly
                ? ExtractAdministrativeTermsFromText(item.Title)
                : ExtractAdministrativeTermsFromText(item.Title)
                    .Concat(ExtractAdministrativeTermsFromText(item.Snippet));

            foreach (var token in tokenSource)
            {
                if (token.Equals(subject, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (seen.Add(token))
                    yield return token;
            }
        }
    }

    private static bool ShouldUseForTermExtraction(HighLevelRelationEvidenceItem item)
    {
        if (item.Score < 8)
            return false;

        var merged = $"{item.Title}\n{item.Snippet}";
        if (!AdministrativeEvidenceKeywords.Any(keyword => merged.Contains(keyword, StringComparison.OrdinalIgnoreCase)) &&
            !NearbyEvidenceKeywords.Any(keyword => merged.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !NoiseTitleKeywords.Any(keyword => item.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ExtractAdministrativeTermsFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        foreach (var suffix in AdministrativeNameSuffixes)
        {
            var pattern = suffix.All(ch => ch <= 127)
                ? $@"\b([A-Z][A-Za-z]+(?: [A-Z][A-Za-z]+)* {Regex.Escape(suffix)})\b"
                : $@"([\p{{IsCJKUnifiedIdeographs}}]{{1,4}}{Regex.Escape(suffix)})";

            foreach (Match match in Regex.Matches(text, pattern))
            {
                var value = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(value) &&
                    IsReasonableAdministrativeTerm(value) &&
                    !GenericAdministrativeTerms.Contains(value) &&
                    !value.Contains('、') &&
                    !value.Contains('，') &&
                    !value.Contains(',') &&
                    !value.Contains('與') &&
                    !value.Contains('和'))
                {
                    yield return value;
                }
            }
        }
    }

    private static bool HasStrongSubjectAssociation(string subject, HighLevelQuerySearchResult result)
    {
        return result.Title.Contains(subject, StringComparison.OrdinalIgnoreCase) ||
               result.Snippet.Contains(subject, StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeAdministrativeListPage(string subject, HighLevelQuerySearchResult result)
    {
        var merged = $"{result.Title}\n{result.Snippet}";
        return merged.Contains(subject, StringComparison.OrdinalIgnoreCase) &&
               AdministrativeEvidenceKeywords.Any(keyword => merged.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildDeterministicAdministrativeReply(
        HighLevelRelationQueryPlan plan,
        IReadOnlyList<HighLevelRelationEvidenceItem> evidence,
        IReadOnlyList<string> nearbyTerms,
        IReadOnlyList<string> candidateTerms)
    {
        var lines = new List<string>();

        if (plan.RelationType == "administrative_neighbor_relation")
        {
            var confidentNearbyTerms = nearbyTerms
                .Where(term => !GenericAdministrativeTerms.Contains(term))
                .Take(8)
                .ToArray();

            if (confidentNearbyTerms.Length >= 2)
            {
                lines.Add($"目前我已找到和「{plan.Subject}」附近或相鄰行政區有關的候選名稱。");
                lines.Add(string.Join("、", confidentNearbyTerms));
            }
            else if (confidentNearbyTerms.Length == 1)
            {
                lines.Add($"找到一個可能和「{plan.Subject}」相鄰的行政區：{confidentNearbyTerms[0]}，但無法確認完整鄰接名單。");
                lines.Add("建議查看下列來源確認。");
            }
            else
            {
                lines.Add($"未找到足夠證據確認和「{plan.Subject}」相鄰的行政區。");
                lines.Add("建議直接查看下列來源。");
            }
        }
        else
        {
            var effectiveTerms = candidateTerms
                .Where(term => !GenericAdministrativeTerms.Contains(term))
                .Take(8)
                .ToArray();

            lines.Add($"目前我已找到和「{plan.Subject}」行政區劃有關的證據。");
            if (effectiveTerms.Length > 0)
            {
                lines.Add(string.Join("、", effectiveTerms));
            }
            else
            {
                lines.Add("但目前證據還不足以穩定抽出一組可信的行政區名稱，較適合直接查看下列來源。");
            }
        }

        lines.Add(string.Empty);
        lines.Add("來源：");
        foreach (var item in evidence.Take(3))
        {
            lines.Add($"- {item.Title}");
            lines.Add(item.Url);
        }

        return string.Join('\n', lines);
    }

    private static bool ShouldUseLlmSynthesis(
        HighLevelRelationQueryPlan plan,
        IReadOnlyList<HighLevelRelationEvidenceItem> evidence,
        IReadOnlyList<string> nearbyTerms,
        IReadOnlyList<string> candidateTerms)
    {
        var strongEvidenceCount = evidence.Count(item => item.Score >= 10);
        if (strongEvidenceCount < 2)
            return false;

        if (plan.RelationType == "administrative_neighbor_relation")
            return strongEvidenceCount >= 3 && nearbyTerms.Count >= 3;

        return candidateTerms.Count >= 2;
    }

    internal static bool IsReasonableAdministrativeTerm(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length > 20)
            return false;

        var isMostlyCjk = trimmed.All(ch => ch >= 0x2E80 && ch <= 0x9FFF);
        if (isMostlyCjk)
        {
            var hasAdminSuffix = AdministrativeNameSuffixes.Any(s =>
                s.Length == 1 && trimmed.EndsWith(s, StringComparison.Ordinal));
            var maxLen = hasAdminSuffix ? 10 : 8;
            if (trimmed.Length > maxLen)
                return false;
        }

        if (trimmed.Any(ch => char.IsWhiteSpace(ch)) && trimmed.Length > 40)
            return false;

        if (ChineseAdministrativeNoiseFragments.Any(fragment => trimmed.Contains(fragment, StringComparison.Ordinal)))
            return false;

        return !trimmed.Contains('的') &&
               !trimmed.Contains('位') &&
               !trimmed.Contains('與') &&
               !trimmed.Contains('和') &&
               !trimmed.Contains('，') &&
               !trimmed.Contains(',') &&
               !trimmed.Contains('。') &&
               !trimmed.Contains('.');
    }
}

public sealed class HighLevelRelationQueryPlan
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string RelationType { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public IReadOnlyList<string> WikipediaQueries { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> WebFallbackQueries { get; set; } = Array.Empty<string>();
}

public sealed class HighLevelRelationQueryResult
{
    public bool Handled { get; set; }
    public string Reply { get; set; } = string.Empty;
    public string DecisionReason { get; set; } = string.Empty;

    public static HighLevelRelationQueryResult NotHandled()
        => new()
        {
            Handled = false
        };
}

public sealed class HighLevelRelationEvidenceItem
{
    public string Query { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public int Score { get; set; }
}
