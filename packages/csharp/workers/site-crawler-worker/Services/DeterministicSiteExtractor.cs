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

public sealed class DeterministicSiteExtractor
{
    private const int TextExcerptLimit = 1000;
    private const int SectionBodyLimit = 2000;
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

    public ExtractedPageResult ExtractPage(Uri pageUri, string html)
    {
        ArgumentNullException.ThrowIfNull(pageUri);

        var document = new HtmlDocument();
        document.LoadHtml(html ?? string.Empty);

        var model = new ExtractedPageModel
        {
            PageUrl = RemoveFragment(pageUri).ToString(),
            Sections = ExtractSections(document, pageUri),
        };

        var themeTokens = ExtractThemeTokens(document);

        return new ExtractedPageResult
        {
            Title = ExtractTitle(document),
            Links = ExtractLinks(document, pageUri),
            Forms = ExtractForms(document),
            TextExcerpt = LimitText(CleanText(GetVisibleText(document.DocumentNode, TextExcerptLimit + 1)), TextExcerptLimit),
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
        var seen = new HashSet<string>(StringComparer.Ordinal);

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

            var normalized = RemoveFragment(resolved);
            var distinctKey = BuildLinkDistinctKey(normalized);
            if (seen.Add(distinctKey))
            {
                links.Add(normalized.ToString());
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
            var id = CleanAttribute(fieldNode.GetAttributeValue("id", string.Empty));
            var name = CleanAttribute(fieldNode.GetAttributeValue("name", string.Empty));
            if (string.IsNullOrWhiteSpace(name))
            {
                name = id;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            fields.Add(new ExtractedFormField
            {
                Name = name,
                Id = id,
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

    private static List<ExtractedSection> ExtractSections(HtmlDocument document, Uri pageUri)
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

        var firstH1HeroCandidate = FindNearestCandidateContainingFirstH1(candidates, firstH1);
        var sections = new List<ExtractedSection>();
        foreach (var candidate in candidates)
        {
            if (IsNavigationChromeContainer(candidate))
            {
                continue;
            }

            var items = ExtractItems(candidate, pageUri, maxItems: 12);
            var bodyText = items.Count > 0
                ? GetVisibleTextWithoutNestedItems(candidate, SectionBodyLimit + 1)
                : GetVisibleText(candidate, SectionBodyLimit + 1);
            var body = LimitText(CleanText(bodyText), SectionBodyLimit);
            if (string.IsNullOrWhiteSpace(body))
            {
                continue;
            }

            var id = CleanAttribute(candidate.GetAttributeValue("id", string.Empty));
            sections.Add(new ExtractedSection
            {
                Id = string.IsNullOrWhiteSpace(id) ? $"section-{sections.Count + 1}" : id,
                Tag = candidate.Name.ToLowerInvariant(),
                Role = InferSectionRole(candidate, firstH1HeroCandidate),
                Headline = ExtractHeadline(candidate),
                Body = body,
                SourceSelector = BuildSelector(candidate, candidate.Name, sections.Count + 1),
                Media = ExtractMedia(candidate, pageUri, maxItems: 6),
                Actions = ExtractActions(candidate, pageUri, maxItems: 6),
                Items = items,
            });
        }

        return sections;
    }

    private static List<ExtractedMedia> ExtractMedia(HtmlNode node, Uri pageUri, int maxItems)
    {
        return SelectNodes(node, ".//img[@src or @data-src or @data-original]")
            .Select(img =>
            {
                var rawUrl = FirstNonEmptyAttribute(img, "src", "data-src", "data-original");
                var url = ResolveHttpUrl(pageUri, rawUrl);
                if (string.IsNullOrWhiteSpace(url))
                {
                    return null;
                }

                return new ExtractedMedia
                {
                    Kind = "image",
                    Url = url,
                    Alt = CleanAttribute(img.GetAttributeValue("alt", string.Empty)),
                };
            })
            .Where(media => media is not null)
            .Select(media => media!)
            .DistinctBy(media => media.Url, StringComparer.Ordinal)
            .Take(maxItems)
            .ToList();
    }

    private static List<ExtractedAction> ExtractActions(HtmlNode node, Uri pageUri, int maxItems)
    {
        return SelectNodes(node, ".//a[@href]")
            .Where(anchor => !IsNavigationChromeContainer(anchor))
            .Select(anchor =>
            {
                var label = CleanText(anchor.InnerText);
                var url = ResolveHttpUrl(pageUri, CleanAttribute(anchor.GetAttributeValue("href", string.Empty)));
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(url))
                {
                    return null;
                }

                return new ExtractedAction
                {
                    Label = label,
                    Url = url,
                    Kind = InferActionKind(anchor),
                };
            })
            .Where(action => action is not null)
            .Select(action => action!)
            .DistinctBy(action => $"{action.Label}\n{action.Url}", StringComparer.Ordinal)
            .Take(maxItems)
            .ToList();
    }

    private static List<ExtractedItem> ExtractItems(HtmlNode node, Uri pageUri, int maxItems)
    {
        var candidates = node.Descendants()
            .Where(candidate => IsItemCandidate(candidate) && !IsNavigationChromeContainer(candidate))
            .Where(candidate => !ReferenceEquals(candidate, node))
            .Where(candidate => !candidate.Ancestors().Any(ancestor =>
                !ReferenceEquals(ancestor, node) && IsItemCandidate(ancestor)))
            .ToList();

        if (candidates.Count == 0)
        {
            return [];
        }

        return candidates
            .Select(candidate => ExtractItem(candidate, pageUri))
            .Where(item => item is not null)
            .Select(item => item!)
            .DistinctBy(item => $"{item.Title}\n{item.Url}\n{item.MediaUrl}", StringComparer.Ordinal)
            .Take(maxItems)
            .ToList();
    }

    private static ExtractedItem? ExtractItem(HtmlNode node, Uri pageUri)
    {
        var title = ExtractHeadline(node);
        var body = LimitText(CleanText(GetVisibleTextWithoutNestedItems(node, 800)), 500);
        if (!string.IsNullOrWhiteSpace(title) && body.StartsWith(title, StringComparison.Ordinal))
        {
            body = body[title.Length..].Trim();
        }

        var links = SelectNodes(node, ".//a[@href]").ToList();
        foreach (var anchor in links.AsEnumerable().Reverse())
        {
            var linkText = CleanText(anchor.InnerText);
            if (!string.IsNullOrWhiteSpace(linkText) && body.EndsWith(linkText, StringComparison.Ordinal))
            {
                body = body[..^linkText.Length].Trim();
            }
        }

        var link = links.FirstOrDefault();

        var img = SelectNodes(node, ".//img[@src or @data-src or @data-original]").FirstOrDefault();
        var mediaUrl = img is null
            ? string.Empty
            : ResolveHttpUrl(pageUri, FirstNonEmptyAttribute(img, "src", "data-src", "data-original"));

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(mediaUrl))
        {
            return null;
        }

        return new ExtractedItem
        {
            Title = title,
            Body = body,
            Url = link is null ? string.Empty : ResolveHttpUrl(pageUri, CleanAttribute(link.GetAttributeValue("href", string.Empty))),
            MediaUrl = mediaUrl,
            MediaAlt = img is null ? string.Empty : CleanAttribute(img.GetAttributeValue("alt", string.Empty)),
        };
    }

    private static string GetVisibleTextWithoutNestedItems(HtmlNode node, int maxCharacters)
    {
        var clone = node.Clone();
        foreach (var nested in clone.Descendants().Where(IsItemCandidate).ToList())
        {
            if (!ReferenceEquals(nested, clone))
            {
                nested.Remove();
            }
        }

        return GetVisibleText(clone, maxCharacters);
    }

    private static IEnumerable<HtmlNode> FindSectionCandidates(HtmlDocument document)
    {
        var elements = document.DocumentNode.Descendants()
            .Where(node => node.NodeType == HtmlNodeType.Element)
            .Where(node => !IsNavigationChromeContainer(node))
            .ToList();
        var semanticCandidates = elements
            .Where(IsSemanticSectionCandidate)
            .ToHashSet();
        var nonSemanticDivCandidates = elements
            .Where(node => IsNonSemanticDivCandidate(node, semanticCandidates))
            .ToHashSet();

        foreach (var node in elements)
        {
            if (IsSemanticSectionCandidate(node))
            {
                yield return node;
                continue;
            }

            if (nonSemanticDivCandidates.Contains(node) &&
                IsCanonicalNonSemanticDivCandidate(node, nonSemanticDivCandidates))
            {
                yield return node;
            }
        }
    }

    private static bool IsSemanticSectionCandidate(HtmlNode node)
    {
        var name = node.Name.ToLowerInvariant();
        return name is "section" or "main" or "article";
    }

    private static bool IsNonSemanticDivCandidate(
        HtmlNode node,
        IReadOnlySet<HtmlNode> semanticCandidates)
    {
        if (!node.Name.Equals("div", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsNavigationChromeContainer(node))
        {
            return false;
        }

        var hasHeroSignal = HasHeroSignal(node);
        if (HasBlockingSemanticCandidateAncestor(node, semanticCandidates, hasHeroSignal))
        {
            return false;
        }

        return hasHeroSignal ||
            ContainsHeading(node, "h1") ||
            (ContainsContentHeading(node) && HasContentContainerSignal(node)) ||
            HasVisualSectionSignal(node);
    }

    private static bool IsCanonicalNonSemanticDivCandidate(
        HtmlNode node,
        IReadOnlySet<HtmlNode> nonSemanticDivCandidates)
    {
        if (HasNonSemanticCandidateAncestor(node, nonSemanticDivCandidates, HasHeroSignal))
        {
            return false;
        }

        return HasHeroSignal(node) ||
            !HasNonSemanticCandidateDescendant(node, nonSemanticDivCandidates, _ => true);
    }

    private static bool HasBlockingSemanticCandidateAncestor(
        HtmlNode node,
        IReadOnlySet<HtmlNode> semanticCandidates,
        bool nodeHasHeroSignal)
    {
        for (var current = node.ParentNode; current is not null; current = current.ParentNode)
        {
            if (semanticCandidates.Contains(current) &&
                (!nodeHasHeroSignal || !IsBroadSemanticContainer(current)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBroadSemanticContainer(HtmlNode node)
    {
        var name = node.Name.ToLowerInvariant();
        return name is "main" or "section" or "article";
    }

    private static bool HasNonSemanticCandidateAncestor(
        HtmlNode node,
        IReadOnlySet<HtmlNode> nonSemanticDivCandidates,
        Func<HtmlNode, bool> predicate)
    {
        for (var current = node.ParentNode; current is not null; current = current.ParentNode)
        {
            if (nonSemanticDivCandidates.Contains(current) && predicate(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNonSemanticCandidateDescendant(
        HtmlNode node,
        IReadOnlySet<HtmlNode> nonSemanticDivCandidates,
        Func<HtmlNode, bool> predicate)
    {
        return node.Descendants()
            .Any(descendant => nonSemanticDivCandidates.Contains(descendant) && predicate(descendant));
    }

    private static HtmlNode? FindNearestCandidateContainingFirstH1(
        IReadOnlyCollection<HtmlNode> candidates,
        HtmlNode? firstH1)
    {
        if (firstH1 is null)
        {
            return null;
        }

        var candidateSet = new HashSet<HtmlNode>(candidates);
        for (var current = firstH1; current is not null; current = current.ParentNode)
        {
            if (candidateSet.Contains(current))
            {
                return current;
            }
        }

        return null;
    }

    private static string InferSectionRole(HtmlNode node, HtmlNode? firstH1HeroCandidate)
    {
        if (HasHeroSignal(node) || ReferenceEquals(node, firstH1HeroCandidate))
        {
            return "hero";
        }

        var tag = node.Name.ToLowerInvariant();
        if (tag is "footer")
        {
            return "footer";
        }

        var visualRole = InferVisualRole(node);
        if (!string.IsNullOrWhiteSpace(visualRole))
        {
            return visualRole;
        }

        return "content";
    }

    private static string InferVisualRole(HtmlNode node)
    {
        var tokens = GetRoleSignalTokens(node);
        if (tokens.Any(IsNewsToken))
        {
            return "news";
        }

        if (tokens.Any(IsGalleryToken))
        {
            return "gallery";
        }

        if (tokens.Any(IsFeatureGridToken))
        {
            return "feature_grid";
        }

        if (tokens.Any(IsFaqToken))
        {
            return "faq";
        }

        if (tokens.Any(IsContactToken))
        {
            return "contact";
        }

        if (tokens.Any(IsProgramToken))
        {
            return "program_grid";
        }

        if (tokens.Any(IsProcessToken))
        {
            return "process";
        }

        if (tokens.Any(IsBenefitsToken))
        {
            return "benefits";
        }

        if (tokens.Any(IsStatsToken))
        {
            return "stats";
        }

        return string.Empty;
    }

    private static List<string> GetRoleSignalTokens(HtmlNode node)
    {
        var values = new[]
        {
            node.Name,
            CleanAttribute(node.GetAttributeValue("id", string.Empty)),
            CleanAttribute(node.GetAttributeValue("class", string.Empty)),
            CleanAttribute(node.GetAttributeValue("role", string.Empty)),
            CleanAttribute(node.GetAttributeValue("aria-label", string.Empty)),
        };

        return values
            .SelectMany(value => Regex.Split(value, "[^a-zA-Z0-9]+", RegexOptions.None, RegexTimeout))
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.ToLowerInvariant())
            .ToList();
    }

    private static bool IsNewsToken(string token)
        => token is "news" or "announcement" or "announcements" or "event" or "events" or "latest" or "press";

    private static bool IsGalleryToken(string token)
        => token is "gallery" or "galleries" or "photo" or "photos" or "album" or "media" or "spotlight";

    private static bool IsFeatureGridToken(string token)
        => token is "cards" or "card" or "grid" or "feature" or "features" or "service" or "services" or
            "classification" or "catalog" or "category" or "categories" or "catloga" or "clasfi";

    private static bool IsStatsToken(string token)
        => token is "stats" or "stat" or "metrics" or "metric" or "numbers" or "counter";

    private static bool IsFaqToken(string token)
        => token is "faq" or "faqs" or "question" or "questions" or "accordion";

    private static bool IsContactToken(string token)
        => token is "contact" or "contacts" or "cta" or "ctabox";

    private static bool IsProgramToken(string token)
        => token is "program" or "programs" or "degree" or "degrees" or "major" or "majors";

    private static bool IsProcessToken(string token)
        => token is "apply" or "application" or "process" or "steps" or "step" or "journey";

    private static bool IsBenefitsToken(string token)
        => token is "why" or "benefit" or "benefits" or "reason" or "reasons";

    private static bool HasHeroSignal(HtmlNode node)
    {
        var id = CleanAttribute(node.GetAttributeValue("id", string.Empty));
        var classes = CleanAttribute(node.GetAttributeValue("class", string.Empty));
        return HasHeroToken(id) || HasHeroToken(classes);
    }

    private static bool HasHeroToken(string value)
    {
        return value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Equals("hero", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsHeading(HtmlNode node, string headingName)
    {
        return node.Descendants()
            .Any(descendant => descendant.NodeType == HtmlNodeType.Element &&
                descendant.Name.Equals(headingName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsContentHeading(HtmlNode node)
    {
        return ContainsHeading(node, "h2") || ContainsHeading(node, "h3");
    }

    private static bool HasVisualSectionSignal(HtmlNode node)
    {
        var tokens = GetRoleSignalTokens(node);
        return tokens.Any(token =>
            IsNewsToken(token) ||
            IsGalleryToken(token) ||
            IsFeatureGridToken(token) ||
            IsFaqToken(token) ||
            IsContactToken(token) ||
            IsProgramToken(token) ||
            IsProcessToken(token) ||
            IsBenefitsToken(token) ||
            IsStatsToken(token) ||
            token is "remind" or "reminder" or "sloga" or "notice");
    }

    private static bool HasContentContainerSignal(HtmlNode node)
    {
        var tokens = GetRoleSignalTokens(node);
        return tokens.Any(token =>
            token is "area" or "section" or "content" or "main" or "panel" or "spotlight" or
                "news" or "newsa" or "classification" or "catalog" or "category" or "catloga" or
                "remind" or "reminder" or "sloga" or "notice");
    }

    private static bool IsItemCandidate(HtmlNode node)
    {
        if (node.NodeType != HtmlNodeType.Element)
        {
            return false;
        }

        if (node.Name.Equals("article", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (node.Name.Equals("li", StringComparison.OrdinalIgnoreCase) &&
            SelectNodes(node, ".//a[@href]").Any())
        {
            return true;
        }

        var tokens = GetRoleSignalTokens(node);
        return tokens.Any(token => token is "card" or "cards" or "item" or "snews" or "sbl");
    }

    private static bool IsNavigationChromeContainer(HtmlNode node)
    {
        for (var current = node; current is not null; current = current.ParentNode)
        {
            if (current.NodeType != HtmlNodeType.Element)
            {
                continue;
            }

            var name = current.Name.ToLowerInvariant();
            if (name is "nav" or "header" or "footer")
            {
                return true;
            }

            var role = CleanAttribute(current.GetAttributeValue("role", string.Empty));
            if (role.Equals("navigation", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("banner", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("contentinfo", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var tokens = GetRoleSignalTokens(current);
            if (tokens.Any(IsNavigationChromeToken))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNavigationChromeToken(string token)
        => token is "nav" or "navbar" or "menu" or "dropdown" or "top" or "topsec" or
            "toplinkbox" or "logosearch" or "logo" or "search" or "footer" or "breadcrumb" or
            "breadcrumbs" or "skip" or "sr" or "sr-only";

    private static string ExtractHeadline(HtmlNode node)
    {
        var heading = node.Descendants()
            .FirstOrDefault(descendant => descendant.NodeType == HtmlNodeType.Element &&
                (descendant.Name.Equals("h1", StringComparison.OrdinalIgnoreCase) ||
                    descendant.Name.Equals("h2", StringComparison.OrdinalIgnoreCase) ||
                    descendant.Name.Equals("h3", StringComparison.OrdinalIgnoreCase) ||
                    descendant.Name.Equals("h4", StringComparison.OrdinalIgnoreCase) ||
                    descendant.Name.Equals("h5", StringComparison.OrdinalIgnoreCase) ||
                    descendant.Name.Equals("h6", StringComparison.OrdinalIgnoreCase)));

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

    private static string GetVisibleText(HtmlNode node, int maxCharacters)
    {
        var builder = new StringBuilder();
        AppendVisibleText(node, builder, maxCharacters);
        return builder.ToString();
    }

    private static void AppendVisibleText(HtmlNode node, StringBuilder builder, int maxCharacters)
    {
        if (builder.Length >= maxCharacters)
        {
            return;
        }

        if (node.NodeType == HtmlNodeType.Text)
        {
            AppendBounded(builder, node.InnerText, maxCharacters);
            builder.Append(' ');
            return;
        }

        if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document)
        {
            return;
        }

        if (IsInvisibleTextContainer(node) || IsNavigationChromeContainer(node))
        {
            return;
        }

        foreach (var child in node.ChildNodes)
        {
            AppendVisibleText(child, builder, maxCharacters);
            if (builder.Length >= maxCharacters)
            {
                break;
            }
        }
    }

    private static void AppendBounded(StringBuilder builder, string value, int maxCharacters)
    {
        var remaining = maxCharacters - builder.Length;
        if (remaining <= 0)
        {
            return;
        }

        builder.Append(value.Length <= remaining ? value : value[..remaining]);
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

    private static string FirstNonEmptyAttribute(HtmlNode node, params string[] names)
    {
        foreach (var name in names)
        {
            var value = CleanAttribute(node.GetAttributeValue(name, string.Empty));
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ResolveHttpUrl(Uri pageUri, string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("#", StringComparison.Ordinal) ||
            value.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(pageUri, value, out var resolved) || !IsHttpUri(resolved))
        {
            return string.Empty;
        }

        return RemoveFragment(resolved).ToString();
    }

    private static string InferActionKind(HtmlNode anchor)
    {
        var classes = CleanAttribute(anchor.GetAttributeValue("class", string.Empty));
        var id = CleanAttribute(anchor.GetAttributeValue("id", string.Empty));
        var signal = $"{classes} {id}";
        return signal.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(token => token.Equals("primary", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("btn-primary", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("apply", StringComparison.OrdinalIgnoreCase))
            ? "primary"
            : "secondary";
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

    private static string BuildLinkDistinctKey(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.IdnHost.ToLowerInvariant(),
            Fragment = string.Empty,
        };

        return builder.Uri.ToString();
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
