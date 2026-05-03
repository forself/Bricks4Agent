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
                .SelectMany((region, index) => BuildVisualBlocks(page, region, index))
                .ToList();
        }

        return (extractedPage?.Sections ?? [])
            .Select((section, index) => BuildStaticBlock(page, section, index))
            .Where(block => block is not null)
            .Select(block => block!)
            .ToList();
    }

    private static IEnumerable<SiteIntentBlock> BuildVisualBlocks(SiteCrawlPage page, VisualRegion region, int index)
    {
        var block = BuildVisualBlock(page, region, index);
        if (block is null)
        {
            return [];
        }

        if (IsTopHeaderLike(region, block.Section))
        {
            block.Kind = "header";
            block.Slot = "header";
            block.Reasons.Add("override:top_navigation_form");
            return [block];
        }

        if (IsFooterLike(region, block.Section))
        {
            block.Kind = "footer";
            block.Slot = "footer";
            block.Reasons.Add("override:copyright_footer");
            return [block];
        }

        if (IsLargeCommercialVisualRegion(page, region, block.Section))
        {
            return SplitLargeCommercialVisualRegion(block);
        }

        if (IsLargeHomeMainRegion(page, region, block.Section))
        {
            var split = new List<SiteIntentBlock>();
            var heroSection = CloneSection(block.Section, $"{block.Section.Id}-hero", "hero");
            heroSection.Items.Clear();
            heroSection.Actions.Clear();
            heroSection.Media = heroSection.Media.Take(6).ToList();
            split.Add(new SiteIntentBlock
            {
                Id = $"{block.Id}-hero",
                Kind = "hero_carousel",
                Slot = "hero",
                Section = heroSection,
                Confidence = 0.74,
                Reasons = [.. block.Reasons, "split:large_home_region_hero"],
            });

            if (block.Section.Items.Count > 0 || HasNewsSignals(block.Section))
            {
                var newsSection = CloneSection(block.Section, $"{block.Section.Id}-news", "news");
                newsSection.Media.Clear();
                newsSection.Actions.Clear();
                split.Add(new SiteIntentBlock
                {
                    Id = $"{block.Id}-news",
                    Kind = "news_carousel",
                    Slot = "news",
                    Section = newsSection,
                    Confidence = 0.72,
                    Reasons = [.. block.Reasons, "split:large_home_region_news"],
                });
            }

            return split;
        }

        return [block];
    }

    private static IEnumerable<SiteIntentBlock> SplitLargeCommercialVisualRegion(SiteIntentBlock block)
    {
        var split = new List<SiteIntentBlock>();
        var sourceItems = block.Section.Items.ToList();
        var pricingItems = sourceItems.Where(IsPricingItem).ToList();
        var productItems = sourceItems.Where(item => !IsPricingItem(item)).Take(8).ToList();

        var heroSection = CloneSection(block.Section, $"{block.Section.Id}-showcase", "showcase_hero");
        heroSection.Items.Clear();
        heroSection.Actions = block.Section.Actions.Take(2).ToList();
        heroSection.Media = block.Section.Media.Take(1).ToList();
        heroSection.Body = LimitCommercialBody(heroSection.Body);
        split.Add(BuildSplitBlock(block, "showcase", "showcase_hero", "showcase_hero", heroSection));

        if (productItems.Count > 0 || ContainsAny($"{block.Section.Headline} {block.Section.Body}", "features", "products", "solutions", "available out of the box"))
        {
            var productSection = CloneSection(block.Section, $"{block.Section.Id}-products", "products");
            productSection.Headline = string.IsNullOrWhiteSpace(productSection.Headline) ? "Products" : productSection.Headline;
            productSection.Items = productItems.Count > 0 ? productItems : sourceItems.Take(6).ToList();
            productSection.Actions.Clear();
            productSection.Media.Clear();
            productSection.Body = LimitCommercialBody(productSection.Body);
            split.Add(BuildSplitBlock(block, "products", "product_cards", "product_cards", productSection));
        }

        var pricingSection = CloneSection(block.Section, $"{block.Section.Id}-pricing", "pricing");
        pricingSection.Headline = string.IsNullOrWhiteSpace(pricingSection.Headline) ? "Pricing" : pricingSection.Headline;
        pricingSection.Items = pricingItems.Count > 0 ? pricingItems : sourceItems.Take(6).ToList();
        pricingSection.Media.Clear();
        pricingSection.Body = LimitCommercialBody(pricingSection.Body);
        split.Add(BuildSplitBlock(block, "pricing", "pricing_panel", "pricing_panel", pricingSection));

        if (block.Section.Actions.Count > 0 || ContainsAny($"{block.Section.Headline} {block.Section.Body}", "get started", "start now", "start free", "contact sales", "ready to start"))
        {
            var ctaSection = CloneSection(block.Section, $"{block.Section.Id}-cta", "cta");
            ctaSection.Headline = "Ready to start?";
            ctaSection.Items.Clear();
            ctaSection.Media.Clear();
            ctaSection.Actions = block.Section.Actions.Take(3).ToList();
            ctaSection.Body = LimitCommercialBody(ctaSection.Body);
            split.Add(BuildSplitBlock(block, "cta", "cta_band", "cta_band", ctaSection));
        }

        return split;
    }

    private static SiteIntentBlock BuildSplitBlock(
        SiteIntentBlock source,
        string idSuffix,
        string kind,
        string slot,
        ExtractedSection section)
    {
        return new SiteIntentBlock
        {
            Id = $"{source.Id}-{idSuffix}",
            Kind = kind,
            Slot = slot,
            Section = section,
            Confidence = Math.Min(0.84, source.Confidence),
            Reasons = [.. source.Reasons, $"split:large_commercial_region_{slot}"],
        };
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
        if (IsStructuralFooter(region, section))
        {
            return "footer";
        }

        if (IsStructuralNavigation(region, section) && !IsSearchHero(page, region, section))
        {
            return "header";
        }

        if (role is "header" or "nav")
        {
            return "header";
        }

        if (role == "footer")
        {
            return "footer";
        }

        if (role is "search_box" or "search")
        {
            return "search_box";
        }

        if (role is "filters" or "filter" or "facets" or "facet")
        {
            return "filter_panel";
        }

        if (role is "results" or "result_list")
        {
            return "result_list";
        }

        if (role is "pagination" or "pager")
        {
            return "pagination";
        }

        if (role is "filter_bar" or "dashboard_filters")
        {
            return "filter_bar";
        }

        if (role is "stats" or "kpi" or "metrics")
        {
            return "metric_summary";
        }

        if (role is "chart" or "visualization")
        {
            return "chart_panel";
        }

        if (role is "table" or "data_table")
        {
            return "data_table";
        }

        if (role is "steps" or "step_indicator" or "progress")
        {
            return "step_indicator";
        }

        if (role is "validation" or "validation_summary")
        {
            return "validation_summary";
        }

        if (role is "action_bar" or "form_actions")
        {
            return "action_bar";
        }

        if (role is "product_hero" or "showcase_hero")
        {
            return "showcase_hero";
        }

        if (role is "products" or "product_cards" or "offers")
        {
            return "product_cards";
        }

        if (role is "proof" or "trust" or "proof_strip")
        {
            return "proof_strip";
        }

        if (role is "pricing" or "plans" or "pricing_panel")
        {
            return "pricing_panel";
        }

        if (role is "cta" or "cta_band")
        {
            return "cta_band";
        }

        if (IsSearchHero(page, region, section))
        {
            return "search_hero";
        }

        if (role == "form")
        {
            return "form_fields";
        }

        var functionKind = ClassifyByFunctionalText(page, section);
        if (!string.IsNullOrWhiteSpace(functionKind))
        {
            return functionKind;
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
            if (IsPublicServiceOrHealthcarePage(page))
            {
                return "tabbed_news";
            }

            return section.Items.Count > 1 || section.Media.Count > 1 ? "news_carousel" : "news_grid";
        }

        if (role is "card_grid" or "visual_grid")
        {
            if (IsServiceActionGrid(page, section))
            {
                return "service_action_grid";
            }

            if (IsServiceCategoryGrid(page, section))
            {
                return "service_category_grid";
            }

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

        if (role is "search" or "portal_search")
        {
            return "search_box";
        }

        if (role is "filters" or "filter" or "facets" or "facet")
        {
            return "filter_panel";
        }

        if (role is "results" or "result_list")
        {
            return "result_list";
        }

        if (role is "pagination" or "pager")
        {
            return "pagination";
        }

        if (role is "filter_bar" or "dashboard_filters")
        {
            return "filter_bar";
        }

        if (role is "stats" or "kpi" or "metrics")
        {
            return "metric_summary";
        }

        if (role is "chart" or "visualization")
        {
            return "chart_panel";
        }

        if (role is "table" or "data_table")
        {
            return "data_table";
        }

        if (role is "steps" or "step_indicator" or "progress")
        {
            return "step_indicator";
        }

        if (role is "validation" or "validation_summary")
        {
            return "validation_summary";
        }

        if (role is "action_bar" or "form_actions")
        {
            return "action_bar";
        }

        if (role is "product_hero" or "showcase_hero")
        {
            return "showcase_hero";
        }

        if (role is "products" or "product_cards" or "offers")
        {
            return "product_cards";
        }

        if (role is "proof" or "trust" or "proof_strip")
        {
            return "proof_strip";
        }

        if (role is "pricing" or "plans" or "pricing_panel")
        {
            return "pricing_panel";
        }

        if (role is "cta" or "cta_band")
        {
            return "cta_band";
        }

        if (role == "news")
        {
            if (IsPublicServiceOrHealthcarePage(page))
            {
                return "tabbed_news";
            }

            return page.Depth == 0 ? "news_carousel" : "article_list";
        }

        if (role is "service_categories" or "category_grid")
        {
            return "service_category_grid";
        }

        if (role is "service_actions" or "quick_actions")
        {
            return "service_action_grid";
        }

        if (role is "gallery" or "feature_grid" or "program_grid")
        {
            return "media_feature_grid";
        }

        if (role is "contact" or "form")
        {
            return "form_fields";
        }

        var functionKind = ClassifyByFunctionalText(page, section);
        if (!string.IsNullOrWhiteSpace(functionKind))
        {
            return functionKind;
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
        var startUrl = crawl.Root.NormalizedStartUrl.ToLowerInvariant();
        var explicitHealthcareHost = ContainsAny(startUrl, "cgh.org.tw", "ntuh.gov.tw");

        if (!explicitHealthcareHost &&
            ContainsAny(
                haystack,
                "gov.tw",
                "gov.taipei",
                "government",
                "public service",
                "service portal",
                "application service",
                "行政院",
                "市政府",
                "政府",
                "申辦",
                "便民"))
        {
            return "public_service";
        }

        if (explicitHealthcareHost ||
            ContainsAny(
            haystack,
            "hospital",
            "medical center",
            "cgh.org.tw",
            "ntuh.gov.tw",
            "醫院",
            "門診",
            "掛號",
            "病患"))
        {
            return "healthcare";
        }

        if (ContainsAny(
            haystack,
            "gov.tw",
            "gov.taipei",
            "government",
            "public service",
            "service portal",
            "application service",
            "行政院",
            "市政府",
            "政府",
            "申辦",
            "便民"))
        {
            return "public_service";
        }

        if (ContainsAny(haystack, "大學", "學校", "學院"))
        {
            return "university";
        }

        return haystack.Contains("university", StringComparison.Ordinal) ||
            haystack.Contains("college", StringComparison.Ordinal) ||
            haystack.Contains("school", StringComparison.Ordinal) ||
            haystack.Contains("大學", StringComparison.Ordinal) ||
            haystack.Contains("學院", StringComparison.Ordinal) ||
            haystack.Contains("學校", StringComparison.Ordinal)
            ? "university"
            : "institutional";
    }

    private static bool IsSearchHero(SiteCrawlPage page, VisualRegion region, ExtractedSection section)
    {
        var role = NormalizeRole(region.Role);
        var text = $"{section.Headline} {section.Body} {page.Title} {page.TextExcerpt}";
        return region.Bounds.Y <= 560 &&
            role is "content" or "form" or "hero" or "search" &&
            ContainsAny(text, "search", "keyword", "how can we help", "站台檢索", "搜尋", "熱門關鍵字");
    }

    private static bool IsServiceCategoryGrid(SiteCrawlPage page, ExtractedSection section)
    {
        if (section.Items.Count < 2)
        {
            return false;
        }

        var text = $"{page.FinalUrl} {page.Title} {page.TextExcerpt} {section.Headline} {section.Body}";
        return ContainsAny(
            text,
            "citizen services",
            "public services",
            "service categories",
            "household",
            "transport",
            "employment",
            "tax",
            "application",
            "服務",
            "市民",
            "申辦",
            "戶籍",
            "交通");
    }

    private static bool IsServiceActionGrid(SiteCrawlPage page, ExtractedSection section)
    {
        if (section.Actions.Count < 3)
        {
            return false;
        }

        var text = $"{page.FinalUrl} {page.Title} {page.TextExcerpt} {section.Headline} {section.Body}";
        return ContainsAny(
            text,
            "registration",
            "appointment",
            "find a doctor",
            "departments",
            "emergency",
            "portal",
            "contact",
            "service lookup",
            "online service",
            "門診",
            "掛號",
            "醫師",
            "科別",
            "急診",
            "聯絡");
    }

    private static bool IsPublicServiceOrHealthcarePage(SiteCrawlPage page)
    {
        var text = $"{page.FinalUrl} {page.Title} {page.TextExcerpt}";
        return ContainsAny(
            text,
            "gov.tw",
            "gov.taipei",
            "government",
            "public service",
            "hospital",
            "medical",
            "outpatient",
            "醫院",
            "政府",
            "申辦");
    }

    private static string ClassifyByFunctionalText(SiteCrawlPage page, ExtractedSection section)
    {
        var localText = BuildLocalFunctionalText(section);
        var text = $"{page.FinalUrl} {page.Title} {page.TextExcerpt} {localText}";
        var actionText = string.Join(' ', section.Actions.Select(action => action.Label));

        if (ContainsAny(localText, "pricing", "price", "prices", "per month", "per successful transaction", "custom pricing"))
        {
            return "pricing_panel";
        }

        if (ContainsAny(localText, "trusted by", "customers", "testimonials", "uptime", "compliance") &&
            ContainsAny(localText, "%", "customers", "teams", "uptime"))
        {
            return "proof_strip";
        }

        if (ContainsAny(actionText, "get started", "start now", "start free", "contact sales", "sign up") ||
            ContainsAny(localText, "ready to start", "contact sales"))
        {
            return "cta_band";
        }

        if ((section.Items.Count >= 2 || section.Actions.Count >= 2) &&
            ContainsAny(localText, "products", "features", "solutions", "available out of the box", "platform"))
        {
            return "product_cards";
        }

        if (ContainsAny(text, "search") && HasFormLikeText(text))
        {
            return "search_box";
        }

        if (ContainsAny(text, "filter", "filters", "facet", "facets", "refine"))
        {
            return ContainsAny(text, "dashboard", "report", "analytics") ? "filter_bar" : "filter_panel";
        }

        if (ContainsAny(text, "results", "result") && (section.Items.Count >= 2 || ContainsAny(text, "search results")))
        {
            return "result_list";
        }

        if (ContainsAny(text, "pagination", "next page", "previous page") ||
            ContainsAny(actionText, "next", "previous"))
        {
            return "pagination";
        }

        if (ContainsAny(text, "show table", "hide table", "data table", "table") &&
            ContainsAny(text, "chart", "charts", "data", "month", "rate"))
        {
            return "data_table";
        }

        if (ContainsAny(text, "chart", "charts", "trend", "visualization", "visualize data"))
        {
            return "chart_panel";
        }

        if (ContainsAny(text, "dashboard", "analytics", "metric", "metrics", "kpi", "rate", "total") &&
            Regex.IsMatch(text, @"\d"))
        {
            return "metric_summary";
        }

        if (ContainsAny(text, "step 1", "step 2", "step 3", "step of", "progress"))
        {
            return "step_indicator";
        }

        if (ContainsAny(text, "required field", "required fields", "validation", "errors"))
        {
            return "validation_summary";
        }

        if (ContainsAny(actionText, "submit", "continue", "back") && section.Actions.Count > 0)
        {
            return "action_bar";
        }

        return string.Empty;
    }

    private static string BuildLocalFunctionalText(ExtractedSection section)
    {
        return string.Join(
            ' ',
            section.Headline,
            section.Body,
            string.Join(' ', section.Actions.Select(action => action.Label)),
            string.Join(' ', section.Items.Select(item => $"{item.Title} {item.Body}")));
    }

    private static bool HasFormLikeText(string text)
    {
        return ContainsAny(text, "keyword", "keywords", "query", "search button", "search:");
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token =>
            value.Contains(token, StringComparison.OrdinalIgnoreCase));
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
            "search_hero" => "search",
            "service_category_grid" => "service_categories",
            "service_action_grid" => "service_actions",
            "tabbed_news" => "tabbed_news",
            "search_box" => "search_box",
            "filter_panel" => "filter_panel",
            "result_list" => "result_list",
            "pagination" => "pagination",
            "filter_bar" => "filter_bar",
            "metric_summary" => "metric_summary",
            "chart_panel" => "chart_panel",
            "data_table" => "data_table",
            "step_indicator" => "step_indicator",
            "form_fields" => "form_fields",
            "validation_summary" => "validation_summary",
            "action_bar" => "action_bar",
            "showcase_hero" => "showcase_hero",
            "product_cards" => "product_cards",
            "proof_strip" => "proof_strip",
            "pricing_panel" => "pricing_panel",
            "cta_band" => "cta_band",
            "quick_links" => "quick_links",
            "news_grid" or "news_carousel" or "article_list" => "news",
            "media_feature_grid" => "features",
            "content_article" or "form" => "content",
            "footer" => "footer",
            _ => "content",
        };
    }

    private static bool IsTopHeaderLike(VisualRegion region, ExtractedSection section)
    {
        var text = $"{section.Headline} {section.Body}";
        return !ContainsAny(text, "search", "keyword", "站台檢索", "搜尋", "熱門關鍵字") &&
            region.Bounds.Y <= 180 &&
            section.Actions.Count >= 3 &&
            NormalizeRole(region.Role) is "form" or "content";
    }

    private static bool IsFooterLike(VisualRegion region, ExtractedSection section)
    {
        var text = $"{section.Headline} {section.Body}";
        return IsStructuralFooter(region, section) ||
            text.Contains("copyright", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("all rights reserved", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("版權", StringComparison.OrdinalIgnoreCase) ||
            (region.Bounds.Y > 1600 && text.Contains("University", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStructuralFooter(VisualRegion region, ExtractedSection section)
    {
        var selector = section.SourceSelector;
        return NormalizeRole(region.Role) == "footer" ||
            string.Equals(section.Tag, "footer", StringComparison.OrdinalIgnoreCase) ||
            selector.StartsWith("footer", StringComparison.OrdinalIgnoreCase) ||
            selector.Contains("#footer", StringComparison.OrdinalIgnoreCase) ||
            selector.Contains(".footer", StringComparison.OrdinalIgnoreCase) ||
            selector.Contains("__footer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsStructuralNavigation(VisualRegion region, ExtractedSection section)
    {
        var selector = section.SourceSelector;
        return NormalizeRole(region.Role) == "nav" ||
            string.Equals(section.Tag, "nav", StringComparison.OrdinalIgnoreCase) ||
            selector.StartsWith("nav", StringComparison.OrdinalIgnoreCase) ||
            selector.Contains(" nav", StringComparison.OrdinalIgnoreCase) ||
            selector.Contains("#nav", StringComparison.OrdinalIgnoreCase) ||
            selector.Contains(".nav", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLargeHomeMainRegion(SiteCrawlPage page, VisualRegion region, ExtractedSection section)
    {
        return page.Depth == 0 &&
            NormalizeRole(region.Role) is "card_grid" or "visual_grid" or "content" &&
            region.Bounds.Height >= 900 &&
            (section.Media.Count >= 2 || section.Items.Count >= 2);
    }

    private static bool IsLargeCommercialVisualRegion(SiteCrawlPage page, VisualRegion region, ExtractedSection section)
    {
        if (page.Depth != 0 || NormalizeRole(region.Role) is "header" or "footer" or "nav")
        {
            return false;
        }

        var text = $"{page.FinalUrl} {page.Title} {page.TextExcerpt} {section.Headline} {section.Body}";
        var isLarge = region.Bounds.Height >= 850 || section.Body.Length >= 700 || section.Items.Count >= 4;
        if (!isLarge)
        {
            return false;
        }

        return ContainsAny(text, "pricing", "price", "prices", "per month", "per successful transaction", "custom pricing") &&
            ContainsAny(text, "product", "products", "features", "solutions", "platform", "businesses", "get started", "contact sales", "start now");
    }

    private static bool IsPricingItem(ExtractedItem item)
    {
        var text = $"{item.Title} {item.Body}";
        return ContainsAny(text, "$", "per month", "pricing", "price", "plan", "starter", "pro", "custom");
    }

    private static string LimitCommercialBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        return body.Length <= 650 ? body : body[..650].TrimEnd();
    }

    private static ExtractedSection CloneSection(ExtractedSection section, string id, string role)
    {
        return new ExtractedSection
        {
            Id = id,
            Tag = section.Tag,
            Role = role,
            Headline = section.Headline,
            Body = section.Body,
            SourceSelector = section.SourceSelector,
            Media = section.Media.ToList(),
            Actions = section.Actions.ToList(),
            Items = section.Items.ToList(),
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
