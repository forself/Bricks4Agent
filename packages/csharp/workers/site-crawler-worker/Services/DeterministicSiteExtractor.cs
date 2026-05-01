using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class ExtractedPageResult
{
    public string Title { get; init; } = string.Empty;

    public List<string> Links { get; init; } = new();

    public List<ExtractedForm> Forms { get; init; } = new();

    public string TextExcerpt { get; init; } = string.Empty;

    public ExtractedThemeTokens ThemeTokens { get; init; } = new();

    public ExtractedPageModel Model { get; init; } = new();
}

public static class DeterministicSiteExtractor
{
    private const int TextExcerptLimit = 1000;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled, RegexTimeout);
    private static readonly Regex CssColorCustomPropertyRegex = new(
        @"--(?<name>[a-zA-Z0-9_-]+)\s*:\s*(?<value>#[0-9a-fA-F]{3,8})\b",
        RegexOptions.Compiled,
        RegexTimeout);
    private static readonly Regex FontFamilyRegex = new(
        @"font-family\s*:\s*(?<value>[^;{}]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout);

    public static ExtractedPageResult ExtractPage(Uri pageUri, string html)
    {
        ArgumentNullException.ThrowIfNull(pageUri);

        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);

        var model = new ExtractedPageModel
        {
            PageUrl = RemoveFragment(pageUri).ToString(),
            Sections = ExtractSections(document),
        };

        var themeTokens = ExtractThemeTokens(document);

        return new ExtractedPageResult
        {
            Title = ExtractTitle(document),
            Links = ExtractLinks(document, pageUri),
            Forms = ExtractForms(document),
            TextExcerpt = LimitText(CleanText(GetVisibleText(document.DocumentNode)), TextExcerptLimit),
            ThemeTokens = themeTokens,
            Model = model,
        };
    }

    private static string ExtractTitle(HtmlDocument document)
    {
        var title = document.DocumentNode.SelectSingleNode("//title");
        return title is null ? string.Empty : CleanText(title.InnerText);
    }

    private static List<string> ExtractLinks(HtmlDocument document, Uri pageUri)
    {
        var links = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in SelectNodes(document.DocumentNode, "//a[@href]"))
        {
            var href = CleanAttribute(anchor.GetAttributeValue("href", string.Empty));
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (!Uri.TryCreate(pageUri, href, out var resolved) || !IsHttpUri(resolved))
            {
                continue;
            }

            var normalized = RemoveFragment(resolved).ToString();
            if (seen.Add(normalized))
            {
                links.Add(normalized);
            }
        }

        return links;
    }

    private static List<ExtractedForm> ExtractForms(HtmlDocument document)
    {
        var forms = new List<ExtractedForm>();
        var index = 0;

        foreach (var formNode in SelectNodes(document.DocumentNode, "//form"))
        {
            index++;
            var method = CleanAttribute(formNode.GetAttributeValue("method", "get"));
            if (string.IsNullOrWhiteSpace(method))
            {
                method = "get";
            }

            forms.Add(new ExtractedForm
            {
                Selector = BuildSelector(formNode, "form", index),
                Method = method.ToLowerInvariant(),
                Action = CleanAttribute(formNode.GetAttributeValue("action", string.Empty)),
                Fields = ExtractFormFields(document, formNode),
            });
        }

        return forms;
    }

    private static List<ExtractedFormField> ExtractFormFields(HtmlDocument document, HtmlNode formNode)
    {
        var fields = new List<ExtractedFormField>();

        foreach (var fieldNode in SelectNodes(formNode, ".//input|.//textarea|.//select"))
        {
            var name = CleanAttribute(fieldNode.GetAttributeValue("name", string.Empty));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = CleanAttribute(fieldNode.GetAttributeValue("id", string.Empty));
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            fields.Add(new ExtractedFormField
            {
                Name = name,
                Type = NormalizeFieldType(fieldNode),
                Label = FindFieldLabel(document, formNode, fieldNode),
                Required = HasAttribute(fieldNode, "required"),
            });
        }

        return fields;
    }

    private static string NormalizeFieldType(HtmlNode fieldNode)
    {
        if (fieldNode.Name.Equals("textarea", StringComparison.OrdinalIgnoreCase))
        {
            return "textarea";
        }

        if (fieldNode.Name.Equals("select", StringComparison.OrdinalIgnoreCase))
        {
            return "select";
        }

        var type = CleanAttribute(fieldNode.GetAttributeValue("type", "text"));
        return string.IsNullOrWhiteSpace(type) ? "text" : type.ToLowerInvariant();
    }

    private static string FindFieldLabel(HtmlDocument document, HtmlNode formNode, HtmlNode fieldNode)
    {
        var id = CleanAttribute(fieldNode.GetAttributeValue("id", string.Empty));
        if (!string.IsNullOrWhiteSpace(id))
        {
            var label = formNode.SelectSingleNode($".//label[@for={ToXPathLiteral(id)}]") ??
                document.DocumentNode.SelectSingleNode($"//label[@for={ToXPathLiteral(id)}]");
            if (label is not null)
            {
                return CleanText(label.InnerText);
            }
        }

        var ancestorLabel = fieldNode.Ancestors()
            .FirstOrDefault(node => node.Name.Equals("label", StringComparison.OrdinalIgnoreCase));
        if (ancestorLabel is not null)
        {
            return CleanText(ancestorLabel.InnerText);
        }

        return CleanAttribute(fieldNode.GetAttributeValue("placeholder", string.Empty));
    }

    private static List<ExtractedSection> ExtractSections(HtmlDocument document)
    {
        var firstH1 = document.DocumentNode.Descendants()
            .FirstOrDefault(node => node.NodeType == HtmlNodeType.Element &&
                node.Name.Equals("h1", StringComparison.OrdinalIgnoreCase));
        var candidates = FindSectionCandidates(document).ToList();
        if (candidates.Count == 0)
        {
            var body = document.DocumentNode.SelectSingleNode("//body");
            if (body is not null)
            {
                candidates.Add(body);
            }
        }

        var sections = new List<ExtractedSection>();
        foreach (var candidate in candidates)
        {
            var body = CleanText(GetVisibleText(candidate));
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var id = CleanAttribute(candidate.GetAttributeValue("id", string.Empty));
            sections.Add(new ExtractedSection
            {
                Id = string.IsNullOrWhiteSpace(id) ? $"section-{sections.Count + 1}" : id,
                Tag = candidate.Name.ToLowerInvariant(),
                Role = InferSectionRole(candidate, firstH1),
                Headline = ExtractHeadline(candidate),
                Body = body,
                SourceSelector = BuildSelector(candidate, candidate.Name, sections.Count + 1),
            });
        }

        return sections;
    }

    private static IEnumerable<HtmlNode> FindSectionCandidates(HtmlDocument document)
    {
        foreach (var node in document.DocumentNode.Descendants()
            .Where(node => node.NodeType == HtmlNodeType.Element))
        {
            var name = node.Name.ToLowerInvariant();
            if (name is "section" or "main" or "header" or "article")
            {
                yield return node;
                continue;
            }

            if (name == "div" && (HasHeroSignal(node) || ContainsHeading(node, "h1")))
            {
                yield return node;
            }
        }
    }

    private static string InferSectionRole(HtmlNode node, HtmlNode? firstH1)
    {
        if (HasHeroSignal(node) || (firstH1 is not null && IsAncestorOrSelf(node, firstH1)))
        {
            return "hero";
        }

        return "content";
    }

    private static bool HasHeroSignal(HtmlNode node)
    {
        var id = CleanAttribute(node.GetAttributeValue("id", string.Empty));
        var classes = CleanAttribute(node.GetAttributeValue("class", string.Empty));
        var signature = $"{id} {classes}".ToLowerInvariant();

        return signature.Contains("hero", StringComparison.Ordinal) ||
            signature.Contains("masthead", StringComparison.Ordinal) ||
            signature.Contains("jumbotron", StringComparison.Ordinal) ||
            signature.Contains("intro", StringComparison.Ordinal);
    }

    private static bool ContainsHeading(HtmlNode node, string headingName)
    {
        return node.Descendants()
            .Any(descendant => descendant.NodeType == HtmlNodeType.Element &&
                descendant.Name.Equals(headingName, StringComparison.OrdinalIgnoreCase));
    }

    private static string ExtractHeadline(HtmlNode node)
    {
        var heading = node.Descendants()
            .FirstOrDefault(descendant => descendant.NodeType == HtmlNodeType.Element &&
                (descendant.Name.Equals("h1", StringComparison.OrdinalIgnoreCase) ||
                    descendant.Name.Equals("h2", StringComparison.OrdinalIgnoreCase) ||
                    descendant.Name.Equals("h3", StringComparison.OrdinalIgnoreCase)));

        return heading is null ? string.Empty : CleanText(heading.InnerText);
    }

    private static ExtractedThemeTokens ExtractThemeTokens(HtmlDocument document)
    {
        var tokens = new ExtractedThemeTokens();
        var css = string.Join(
            Environment.NewLine,
            SelectNodes(document.DocumentNode, "//style").Select(style => HtmlEntity.DeEntitize(style.InnerText)));

        foreach (Match match in CssColorCustomPropertyRegex.Matches(css))
        {
            var name = match.Groups["name"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(name) && !tokens.Colors.ContainsKey(name))
            {
                tokens.Colors[name] = value;
            }
        }

        var fontFamilyMatch = FontFamilyRegex.Match(css);
        if (fontFamilyMatch.Success)
        {
            var fontFamily = RemoveCssPriority(CleanText(fontFamilyMatch.Groups["value"].Value));
            if (!string.IsNullOrWhiteSpace(fontFamily))
            {
                tokens.Typography["font_family"] = fontFamily;
            }
        }

        return tokens;
    }

    private static string RemoveCssPriority(string value)
    {
        var priorityIndex = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
        return priorityIndex < 0 ? value : value[..priorityIndex].Trim();
    }

    private static string BuildSelector(HtmlNode node, string fallbackTag, int index)
    {
        var tag = string.IsNullOrWhiteSpace(node.Name) ? fallbackTag : node.Name.ToLowerInvariant();
        var id = CleanAttribute(node.GetAttributeValue("id", string.Empty));
        if (!string.IsNullOrWhiteSpace(id))
        {
            return $"{tag}#{id}";
        }

        var classes = CleanAttribute(node.GetAttributeValue("class", string.Empty))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (classes.Length > 0)
        {
            return $"{tag}.{classes[0]}";
        }

        return $"{tag}:nth-of-type({index})";
    }

    private static string GetVisibleText(HtmlNode node)
    {
        var builder = new StringBuilder();
        AppendVisibleText(node, builder);
        return builder.ToString();
    }

    private static void AppendVisibleText(HtmlNode node, StringBuilder builder)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            builder.Append(node.InnerText);
            builder.Append(' ');
            return;
        }

        if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document)
        {
            return;
        }

        if (IsInvisibleTextContainer(node))
        {
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendVisibleText(child, builder);
        }
    }

    private static bool IsInvisibleTextContainer(HtmlNode node)
    {
        return node.Name.Equals("script", StringComparison.OrdinalIgnoreCase) ||
            node.Name.Equals("style", StringComparison.OrdinalIgnoreCase) ||
            node.Name.Equals("noscript", StringComparison.OrdinalIgnoreCase) ||
            node.Name.Equals("template", StringComparison.OrdinalIgnoreCase) ||
            node.Name.Equals("svg", StringComparison.OrdinalIgnoreCase);
    }

    private static string CleanText(string value)
    {
        return WhitespaceRegex.Replace(HtmlEntity.DeEntitize(value), " ").Trim();
    }

    private static string CleanAttribute(string value)
    {
        return HtmlEntity.DeEntitize(value).Trim();
    }

    private static string LimitText(string text, int limit)
    {
        if (text.Length <= limit)
        {
            return text;
        }

        return text[..limit].TrimEnd();
    }

    private static bool HasAttribute(HtmlNode node, string attributeName)
    {
        return node.Attributes.Any(attribute =>
            attribute.Name.Equals(attributeName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHttpUri(Uri uri)
    {
        return uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    private static Uri RemoveFragment(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };

        return builder.Uri;
    }

    private static bool IsAncestorOrSelf(HtmlNode possibleAncestor, HtmlNode node)
    {
        for (var current = node; current is not null; current = current.ParentNode)
        {
            if (current == possibleAncestor)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<HtmlNode> SelectNodes(HtmlNode node, string xpath)
    {
        return node.SelectNodes(xpath) ?? Enumerable.Empty<HtmlNode>();
    }

    private static string ToXPathLiteral(string value)
    {
        if (!value.Contains('\'', StringComparison.Ordinal))
        {
            return $"'{value}'";
        }

        if (!value.Contains('"', StringComparison.Ordinal))
        {
            return $"\"{value}\"";
        }

        var parts = value.Split('\'');
        return $"concat('{string.Join("', \"'\", '", parts)}')";
    }
}
