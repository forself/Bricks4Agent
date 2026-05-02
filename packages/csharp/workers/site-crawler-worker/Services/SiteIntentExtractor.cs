using System.Text.RegularExpressions;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class SiteIntentExtractor
{
    public SiteIntentModel Extract(SiteCrawlResult crawl)
    {
        ArgumentNullException.ThrowIfNull(crawl);

        var intent = new SiteIntentModel
        {
            SiteKind = ResolveSiteKind(crawl),
            ThemeHints = new ExtractedThemeTokens
            {
                Colors = new Dictionary<string, string>(crawl.ExtractedModel.ThemeTokens.Colors, StringComparer.Ordinal),
                Typography = new Dictionary<string, string>(crawl.ExtractedModel.ThemeTokens.Typography, StringComparer.Ordinal),
            },
        };

        foreach (var page in crawl.Pages)
        {
            var extractedPage = FindExtractedPage(crawl, page);
            var blocks = BuildBlocks(page, extractedPage);
            var pageIntent = new SiteIntentPage
            {
                PageUrl = page.FinalUrl,
                PageType = ClassifyPageType(page, extractedPage, blocks),
                Depth = page.Depth,
                Title = page.Title,
                Blocks = blocks,
            };

            intent.Pages.Add(pageIntent);
            CaptureSharedHeaderFooter(intent, pageIntent, extractedPage);
        }

        return intent;
    }

    private static List<SiteIntentBlock> BuildBlocks(SiteCrawlPage page, ExtractedPageModel? extractedPage)
    {
        if (page.VisualSnapshot is { Regions.Count: > 0 } snapshot)
        {
            return snapshot.Regions
                .OrderBy(region => region.Bounds.Y)
                .ThenBy(region => region.Bounds.X)
                .Select((region, index) => BuildVisualBlock(page, region, index))
                .Where(block => block is not null)
                .Select(block => block!)
                .ToList();
        }

        return (extractedPage?.Sections ?? [])
            .Select((section, index) => BuildStaticBlock(page, section, index))
            .Where(block => block is not null)
            .Select(block => block!)
            .ToList();
    }

    private static SiteIntentBlock? BuildVisualBlock(SiteCrawlPage page, VisualRegion region, int index)
    {
        var section = new ExtractedSection
        {
            Id = string.IsNullOrWhiteSpace(region.Id) ? $"visual-block-{index + 1}" : region.Id,
            Tag = string.IsNullOrWhiteSpace(region.Tag) ? "section" : region.Tag,
            Role = NormalizeRole(region.Role),
            Headline = region.Headline,
            Body = CleanVisualBody(region.Text, region.Headline),
            SourceSelector = string.IsNullOrWhiteSpace(region.Selector)
                ? $"visual-region-{index + 1}"
                : region.Selector,
            Media = region.Media
                .Where(media => !string.IsNullOrWhiteSpace(media.Url))
                .DistinctBy(media => media.Url, StringComparer.Ordinal)
                .Take(12)
                .ToList(),
            Actions = region.Actions
                .Where(action => !string.IsNullOrWhiteSpace(action.Label) && !string.IsNullOrWhiteSpace(action.Url))
                .DistinctBy(action => $"{action.Label}\n{action.Url}", StringComparer.Ordinal)
                .Take(24)
                .ToList(),
            Items = region.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Title) ||
                    !string.IsNullOrWhiteSpace(item.Body) ||
                    !string.IsNullOrWhiteSpace(item.MediaUrl))
                .DistinctBy(item => $"{item.Title}\n{item.Url}\n{item.MediaUrl}", StringComparer.Ordinal)
                .Take(32)
                .ToList(),
        };

        if (string.IsNullOrWhiteSpace(section.Body) &&
            section.Media.Count == 0 &&
            section.Actions.Count == 0 &&
            section.Items.Count == 0 &&
            !IsRole(region, "header") &&
            !IsRole(region, "footer") &&
            !IsRole(region, "nav"))
        {
            return null;
        }

        var kind = ClassifyVisualBlockKind(page, region, section);
        return new SiteIntentBlock
        {
            Id = section.Id,
            Kind = kind,
            Slot = MapKindToSlot(kind),
            Section = section,
            Confidence = 0.86,
            Reasons = [$"visual_role:{region.Role}", $"visual_bounds_y:{region.Bounds.Y:0}"],
        };
    }

    private static SiteIntentBlock? BuildStaticBlock(SiteCrawlPage page, ExtractedSection section, int index)
    {
        if (string.IsNullOrWhiteSpace(section.Headline) &&
            string.IsNullOrWhiteSpace(section.Body) &&
            section.Media.Count == 0 &&
            section.Actions.Count == 0 &&
            section.Items.Count == 0)
        {
            return null;
        }

        var kind = ClassifyStaticBlockKind(page, section);
        return new SiteIntentBlock
        {
            Id = string.IsNullOrWhiteSpace(section.Id) ? $"static-block-{index + 1}" : section.Id,
            Kind = kind,
            Slot = MapKindToSlot(kind),
            Section = section,
            Confidence = 0.72,
            Reasons = [$"static_role:{section.Role}"],
        };
    }

    private static string ClassifyVisualBlockKind(SiteCrawlPage page, VisualRegion region, ExtractedSection section)
    {
        var role = NormalizeRole(region.Role);
        if (role is "header" or "nav")
        {
            return "header";
        }

        if (role == "footer")
        {
            return "footer";
        }

        if (role == "form")
        {
            return "form";
        }

        if (role == "hero")
        {
            return section.Media.Count > 1 || section.Items.Count > 1 ? "hero_carousel" : "hero_banner";
        }

        if (role == "carousel")
        {
            if (page.Depth == 0 && region.Bounds.Y <= 520 && (section.Media.Count > 0 || section.Items.Count > 0))
            {
                return "hero_carousel";
            }

            return section.Items.Count > 0 || HasNewsSignals(section) ? "news_carousel" : "media_feature_grid";
        }

        if (role == "news")
        {
            return section.Items.Count > 1 || section.Media.Count > 1 ? "news_carousel" : "news_grid";
        }

        if (role is "card_grid" or "visual_grid")
        {
            if (IsQuickLinks(section))
            {
                return "quick_links";
            }

            return HasNewsSignals(section) ? "news_grid" : "media_feature_grid";
        }

        if (role is "gallery" or "feature_grid")
        {
            return "media_feature_grid";
        }

        if (role is "article" or "main")
        {
            return "content_article";
        }

        return section.Items.Count >= 3 ? "article_list" : "content_article";
    }

    private static string ClassifyStaticBlockKind(SiteCrawlPage page, ExtractedSection section)
    {
        var role = NormalizeRole(section.Role);
        if (role == "footer")
        {
            return "footer";
        }

        if (role == "hero")
        {
            return section.Media.Count > 1 || section.Items.Count > 1 ? "hero_carousel" : "hero_banner";
        }

        if (role == "news")
        {
            return page.Depth == 0 ? "news_carousel" : "article_list";
        }

        if (role is "gallery" or "feature_grid" or "program_grid")
        {
            return "media_feature_grid";
        }

        if (role is "contact" or "form")
        {
            return "form";
        }

        if (section.Items.Count >= 3)
        {
            return "article_list";
        }

        return IsLongArticle(section) || role == "article" ? "content_article" : "content_article";
    }

    private static string ClassifyPageType(
        SiteCrawlPage page,
        ExtractedPageModel? extractedPage,
        IReadOnlyCollection<SiteIntentBlock> blocks)
    {
        if (page.Depth == 0)
        {
            return "home";
        }

        if (blocks.Any(block => block.Kind is "article_list" or "news_grid" or "news_carousel") ||
            (extractedPage?.Sections.Sum(section => section.Items.Count) ?? 0) >= 3)
        {
            return "listing";
        }

        if (blocks.Any(block => block.Kind == "content_article" && IsLongArticle(block.Section)))
        {
            return "article";
        }

        var textLength = (page.TextExcerpt?.Length ?? 0) + (extractedPage?.Sections.Sum(section => section.Body.Length) ?? 0);
        return textLength > 600 ? "article" : "unknown";
    }

    private static void CaptureSharedHeaderFooter(
        SiteIntentModel intent,
        SiteIntentPage pageIntent,
        ExtractedPageModel? extractedPage)
    {
        if (intent.GlobalHeader.PrimaryLinks.Count == 0 && intent.GlobalHeader.UtilityLinks.Count == 0)
        {
            var headerBlock = pageIntent.Blocks.FirstOrDefault(block => block.Kind == "header");
            if (headerBlock is not null)
            {
                var logo = headerBlock.Section.Media.FirstOrDefault();
                intent.GlobalHeader = new ExtractedHeader
                {
                    LogoUrl = logo?.Url ?? string.Empty,
                    LogoAlt = logo?.Alt ?? string.Empty,
                    PrimaryLinks = headerBlock.Section.Actions.ToList(),
                };
            }
            else if (extractedPage is not null &&
                (extractedPage.Header.PrimaryLinks.Count > 0 || extractedPage.Header.UtilityLinks.Count > 0))
            {
                intent.GlobalHeader = CloneHeader(extractedPage.Header);
            }
        }

        if (string.IsNullOrWhiteSpace(intent.GlobalFooter.Text) && intent.GlobalFooter.Links.Count == 0)
        {
            var footerBlock = pageIntent.Blocks.FirstOrDefault(block => block.Kind == "footer");
            if (footerBlock is not null)
            {
                var logo = footerBlock.Section.Media.FirstOrDefault();
                intent.GlobalFooter = new ExtractedFooter
                {
                    LogoUrl = logo?.Url ?? string.Empty,
                    LogoAlt = logo?.Alt ?? string.Empty,
                    Text = footerBlock.Section.Body,
                    Links = footerBlock.Section.Actions.ToList(),
                };
            }
            else if (extractedPage is not null &&
                (!string.IsNullOrWhiteSpace(extractedPage.Footer.Text) || extractedPage.Footer.Links.Count > 0))
            {
                intent.GlobalFooter = CloneFooter(extractedPage.Footer);
            }
        }
    }

    private static ExtractedPageModel? FindExtractedPage(SiteCrawlResult crawl, SiteCrawlPage page)
    {
        return crawl.ExtractedModel.Pages.FirstOrDefault(candidate =>
            string.Equals(candidate.PageUrl, page.FinalUrl, StringComparison.Ordinal));
    }

    private static string ResolveSiteKind(SiteCrawlResult crawl)
    {
        var terms = new List<string> { crawl.Root.NormalizedStartUrl };
        terms.AddRange(crawl.Pages.Select(page => page.Title));
        terms.AddRange(crawl.Pages.Take(5).Select(page => page.TextExcerpt));
        var haystack = string.Join(' ', terms).ToLowerInvariant();

        return haystack.Contains("university", StringComparison.Ordinal) ||
            haystack.Contains("college", StringComparison.Ordinal) ||
            haystack.Contains("school", StringComparison.Ordinal) ||
            haystack.Contains("大學", StringComparison.Ordinal) ||
            haystack.Contains("學院", StringComparison.Ordinal) ||
            haystack.Contains("學校", StringComparison.Ordinal)
            ? "university"
            : "institutional";
    }

    private static bool IsQuickLinks(ExtractedSection section)
    {
        if (section.Actions.Count < 3)
        {
            return false;
        }

        var averageLabelLength = section.Actions.Average(action => action.Label.Length);
        return averageLabelLength <= 18 && section.Media.Count <= 1 && section.Items.Count <= section.Actions.Count;
    }

    private static bool HasNewsSignals(ExtractedSection section)
    {
        return Regex.IsMatch($"{section.Headline} {section.Body}", @"\b(19|20)\d{2}[-/.年]\d{1,2}|\bnews\b|最新|公告|焦點", RegexOptions.IgnoreCase) ||
            section.Items.Any(item => Regex.IsMatch($"{item.Title} {item.Body}", @"\b(19|20)\d{2}[-/.年]\d{1,2}|最新|公告|焦點", RegexOptions.IgnoreCase));
    }

    private static bool IsLongArticle(ExtractedSection section)
    {
        return section.Body.Length > 600 && section.Items.Count == 0;
    }

    private static string MapKindToSlot(string kind)
    {
        return kind switch
        {
            "header" => "header",
            "hero_carousel" or "hero_banner" => "hero",
            "quick_links" => "quick_links",
            "news_grid" or "news_carousel" or "article_list" => "news",
            "media_feature_grid" => "features",
            "content_article" or "form" => "content",
            "footer" => "footer",
            _ => "content",
        };
    }

    private static bool IsRole(VisualRegion region, string role)
    {
        return string.Equals(region.Role, role, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRole(string role)
    {
        return string.IsNullOrWhiteSpace(role) ? "content" : role.Trim().ToLowerInvariant();
    }

    private static string CleanVisualBody(string text, string headline)
    {
        var body = (text ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(headline) && body.StartsWith(headline, StringComparison.Ordinal))
        {
            body = body[headline.Length..].Trim();
        }

        return body.Length <= 2200 ? body : body[..2200].TrimEnd();
    }

    private static ExtractedHeader CloneHeader(ExtractedHeader header)
    {
        return new ExtractedHeader
        {
            LogoUrl = header.LogoUrl,
            LogoAlt = header.LogoAlt,
            UtilityLinks = header.UtilityLinks.ToList(),
            PrimaryLinks = header.PrimaryLinks.ToList(),
        };
    }

    private static ExtractedFooter CloneFooter(ExtractedFooter footer)
    {
        return new ExtractedFooter
        {
            LogoUrl = footer.LogoUrl,
            LogoAlt = footer.LogoAlt,
            Text = footer.Text,
            Links = footer.Links.ToList(),
        };
    }
}
