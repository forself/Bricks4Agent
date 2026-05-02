using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class TemplateCompiler
{
    private readonly ComponentLibraryManifest baseManifest;

    public TemplateCompiler(ComponentLibraryManifest componentLibrary)
    {
        baseManifest = CloneManifest(componentLibrary);
    }

    public GeneratorSiteDocument Compile(SiteCrawlResult crawl, SiteIntentModel intent, TemplatePlan plan)
    {
        ArgumentNullException.ThrowIfNull(crawl);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(plan);

        var manifest = CloneManifest(baseManifest);
        var localRoutes = BuildLocalRouteMap(crawl.Pages);
        var document = new GeneratorSiteDocument
        {
            Site = new GeneratorSiteMetadata
            {
                Title = ResolveSiteTitle(crawl),
                SourceUrl = crawl.Root.NormalizedStartUrl,
                CrawlRunId = crawl.CrawlRunId,
                Theme = new GeneratorTheme
                {
                    Colors = new Dictionary<string, string>(crawl.ExtractedModel.ThemeTokens.Colors, StringComparer.Ordinal),
                    Typography = new Dictionary<string, string>(crawl.ExtractedModel.ThemeTokens.Typography, StringComparer.Ordinal),
                },
            },
            ComponentLibrary = manifest,
            ComponentRequests = plan.ComponentRequests.Select(CloneComponentRequest).ToList(),
        };

        foreach (var page in crawl.Pages)
        {
            var intentPage = intent.Pages.FirstOrDefault(candidate =>
                string.Equals(candidate.PageUrl, page.FinalUrl, StringComparison.Ordinal));
            var pagePlan = plan.Pages.FirstOrDefault(candidate =>
                string.Equals(candidate.PageUrl, page.FinalUrl, StringComparison.Ordinal));
            document.Routes.Add(BuildRoute(crawl, document, page, intent, intentPage, pagePlan, localRoutes));
        }

        return document;
    }

    private static GeneratorRoute BuildRoute(
        SiteCrawlResult crawl,
        GeneratorSiteDocument document,
        SiteCrawlPage page,
        SiteIntentModel intent,
        SiteIntentPage? intentPage,
        TemplatePagePlan? pagePlan,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var pageTitle = string.IsNullOrWhiteSpace(page.Title) ? document.Site.Title : page.Title;
        var root = new ComponentNode
        {
            Id = BuildNodeId("page", page.FinalUrl),
            Type = "PageShell",
            Props =
            {
                ["title"] = pageTitle,
                ["source_url"] = page.FinalUrl,
            },
        };

        if (pagePlan is null || pagePlan.Slots.Count == 0)
        {
            root.Children.Add(BuildHeaderNode("MegaHeader", document.Site.Title, intent.GlobalHeader, crawl.Root.Origin, localRoutes, page.FinalUrl));
            AddDefaultContent(root, page);
            root.Children.Add(BuildFooterNode("InstitutionFooter", page.FinalUrl, intent.GlobalFooter, crawl.Root.Origin, localRoutes));
        }
        else
        {
            foreach (var slot in pagePlan.Slots)
            {
                root.Children.Add(BuildSlotNode(slot, document.Site.Title, page, intent, crawl.Root.Origin, localRoutes));
            }
        }

        foreach (var form in page.Forms.Take(4))
        {
            root.Children.Add(BuildFormNode(page, form));
        }

        return new GeneratorRoute
        {
            Path = BuildRoutePath(page.FinalUrl),
            Title = pageTitle,
            SourceUrl = page.FinalUrl,
            Root = root,
        };
    }

    private static ComponentNode BuildSlotNode(
        TemplateSlotPlan slot,
        string siteTitle,
        SiteCrawlPage page,
        SiteIntentModel intent,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return slot.SlotName switch
        {
            "header" => BuildHeaderNode(slot.ComponentType, siteTitle, intent.GlobalHeader, origin, localRoutes, page.FinalUrl),
            "footer" => BuildFooterNode(slot.ComponentType, page.FinalUrl, intent.GlobalFooter, origin, localRoutes),
            _ => BuildContentSlotNode(slot, page, origin, localRoutes),
        };
    }

    private static ComponentNode BuildHeaderNode(
        string componentType,
        string siteTitle,
        ExtractedHeader header,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes,
        string pageUrl)
    {
        if (componentType == "SiteHeader")
        {
            return new ComponentNode
            {
                Id = BuildNodeId("header", pageUrl),
                Type = "SiteHeader",
                Props =
                {
                    ["title"] = siteTitle,
                    ["logo_url"] = header.LogoUrl,
                    ["logo_alt"] = header.LogoAlt,
                    ["utility_links"] = BuildLinks(header.UtilityLinks, origin, localRoutes, 10),
                    ["primary_links"] = BuildLinks(header.PrimaryLinks, origin, localRoutes, 16),
                    ["links"] = new List<Dictionary<string, string>>(),
                },
            };
        }

        return new ComponentNode
        {
            Id = BuildNodeId("header", pageUrl),
            Type = "MegaHeader",
            Props =
            {
                ["title"] = siteTitle,
                ["logo_url"] = header.LogoUrl,
                ["logo_alt"] = header.LogoAlt,
                ["utility_links"] = BuildLinks(header.UtilityLinks, origin, localRoutes, 10),
                ["primary_links"] = BuildLinks(header.PrimaryLinks, origin, localRoutes, 16),
                ["search_enabled"] = true,
            },
        };
    }

    private static ComponentNode BuildFooterNode(
        string componentType,
        string pageUrl,
        ExtractedFooter footer,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        if (componentType == "SiteFooter")
        {
            return new ComponentNode
            {
                Id = BuildNodeId("footer", pageUrl),
                Type = "SiteFooter",
                Props =
                {
                    ["source_url"] = pageUrl,
                    ["notice"] = "Reconstructed from public visual structure using the declared component library.",
                    ["logo_url"] = footer.LogoUrl,
                    ["logo_alt"] = footer.LogoAlt,
                    ["contact_text"] = footer.Text,
                    ["links"] = BuildLinks(footer.Links, origin, localRoutes, 16),
                },
            };
        }

        return new ComponentNode
        {
            Id = BuildNodeId("footer", pageUrl),
            Type = "InstitutionFooter",
            Props =
            {
                ["source_url"] = pageUrl,
                ["logo_url"] = footer.LogoUrl,
                ["logo_alt"] = footer.LogoAlt,
                ["contact_text"] = footer.Text,
                ["links"] = BuildLinks(footer.Links, origin, localRoutes, 16),
            },
        };
    }

    private static ComponentNode BuildContentSlotNode(
        TemplateSlotPlan slot,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var section = slot.Block?.Section ?? new ExtractedSection
        {
            Id = slot.SlotName,
            Headline = string.IsNullOrWhiteSpace(page.Title) ? slot.SlotName : page.Title,
            Body = page.TextExcerpt,
            SourceSelector = $"route:{BuildRoutePath(page.FinalUrl)}",
        };

        return slot.ComponentType switch
        {
            "HeroCarousel" => BuildHeroCarouselNode(section, page, origin, localRoutes),
            "HeroBanner" => BuildHeroBannerNode(section, page),
            "QuickLinkRibbon" => BuildQuickLinkRibbonNode(section, origin, localRoutes),
            "NewsCardCarousel" => BuildItemCollectionNode("NewsCardCarousel", section, page, origin, localRoutes),
            "NewsGrid" => BuildItemCollectionNode("NewsGrid", section, page, origin, localRoutes),
            "MediaFeatureGrid" => BuildItemCollectionNode("MediaFeatureGrid", section, page, origin, localRoutes),
            "ArticleList" => BuildItemCollectionNode("ArticleList", section, page, origin, localRoutes),
            "ContentArticle" => BuildContentArticleNode(section, page),
            "CardGrid" => BuildCardGridNode(section, page, origin, localRoutes),
            "ContentSection" => BuildContentSectionNode(section, page),
            "AtomicSection" => BuildAtomicSectionNode(section, page, origin, localRoutes),
            _ => BuildAtomicSectionNode(section, page, origin, localRoutes),
        };
    }

    private static ComponentNode BuildHeroCarouselNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var slides = BuildSlides(section, origin, localRoutes);
        if (slides.Count == 0)
        {
            slides.Add(new Dictionary<string, string>
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["media_url"] = section.Media.FirstOrDefault()?.Url ?? string.Empty,
                ["media_alt"] = section.Media.FirstOrDefault()?.Alt ?? string.Empty,
                ["url"] = string.Empty,
                ["source_url"] = string.Empty,
                ["scope"] = "none",
            });
        }

        return new ComponentNode
        {
            Id = BuildNodeId("hero-carousel", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "HeroCarousel",
            Props =
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["slides"] = slides,
            },
        };
    }

    private static ComponentNode BuildHeroBannerNode(ExtractedSection section, SiteCrawlPage page)
    {
        var media = section.Media.FirstOrDefault();
        return new ComponentNode
        {
            Id = BuildNodeId("hero-banner", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "HeroBanner",
            Props =
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["media_url"] = media?.Url ?? string.Empty,
                ["media_alt"] = media?.Alt ?? string.Empty,
            },
        };
    }

    private static ComponentNode BuildQuickLinkRibbonNode(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("quick-links", section.SourceSelector),
            Type = "QuickLinkRibbon",
            Props =
            {
                ["title"] = section.Headline,
                ["links"] = BuildLinks(section.Actions, origin, localRoutes, 12),
            },
        };
    }

    private static ComponentNode BuildItemCollectionNode(
        string componentType,
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        return new ComponentNode
        {
            Id = BuildNodeId(componentType, $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = componentType,
            Props =
            {
                ["title"] = section.Headline,
                ["items"] = BuildItems(section, origin, localRoutes),
            },
        };
    }

    private static ComponentNode BuildContentArticleNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("content-article", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ContentArticle",
            Props =
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["media"] = section.Media
                    .Take(8)
                    .Select(media => new Dictionary<string, string>
                    {
                        ["url"] = media.Url,
                        ["alt"] = media.Alt,
                    })
                    .ToList(),
            },
        };
    }

    private static ComponentNode BuildContentSectionNode(ExtractedSection section, SiteCrawlPage page)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("content", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "ContentSection",
            Props =
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["source_selector"] = section.SourceSelector,
            },
        };
    }

    private static ComponentNode BuildCardGridNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var grid = new ComponentNode
        {
            Id = BuildNodeId("grid", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "CardGrid",
            Props =
            {
                ["title"] = section.Headline,
                ["layout"] = section.Role is "news" or "gallery" ? "carousel" : "grid",
            },
        };

        foreach (var item in BuildItems(section, origin, localRoutes))
        {
            grid.Children.Add(new ComponentNode
            {
                Id = BuildNodeId("card", $"{page.FinalUrl}:{section.SourceSelector}:{item["title"]}:{item["url"]}"),
                Type = "FeatureCard",
                Props =
                {
                    ["title"] = item["title"],
                    ["body"] = item["body"],
                    ["url"] = item["url"],
                    ["source_url"] = item["source_url"],
                    ["scope"] = item["scope"],
                    ["media_url"] = item["media_url"],
                    ["media_alt"] = item["media_alt"],
                },
            });
        }

        return grid;
    }

    private static ComponentNode BuildAtomicSectionNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var node = new ComponentNode
        {
            Id = BuildNodeId("atomic", $"{page.FinalUrl}:{section.SourceSelector}"),
            Type = "AtomicSection",
            Props =
            {
                ["variant"] = section.Role == "hero" ? "hero" : "standard",
                ["source_selector"] = section.SourceSelector,
            },
            Children =
            [
                new ComponentNode
                {
                    Id = BuildNodeId("text", $"{page.FinalUrl}:{section.SourceSelector}:text"),
                    Type = "TextBlock",
                    Props =
                    {
                        ["title"] = section.Headline,
                        ["body"] = section.Body,
                    },
                },
            ],
        };

        foreach (var media in section.Media.Take(2))
        {
            node.Children.Add(new ComponentNode
            {
                Id = BuildNodeId("image", media.Url),
                Type = "ImageBlock",
                Props =
                {
                    ["url"] = media.Url,
                    ["alt"] = media.Alt,
                },
            });
        }

        foreach (var action in section.Actions.Take(4))
        {
            var link = BuildLink(action.Url, origin, localRoutes);
            node.Children.Add(new ComponentNode
            {
                Id = BuildNodeId("action", $"{section.SourceSelector}:{action.Label}:{action.Url}"),
                Type = "ButtonLink",
                Props =
                {
                    ["label"] = action.Label,
                    ["url"] = link["url"],
                    ["source_url"] = link["source_url"],
                    ["scope"] = link["scope"],
                    ["kind"] = action.Kind,
                },
            });
        }

        return node;
    }

    private static ComponentNode BuildFormNode(SiteCrawlPage page, ExtractedForm form)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("form", $"{page.FinalUrl}:{form.Selector}:{form.Action}"),
            Type = "FormBlock",
            Props =
            {
                ["method"] = form.Method,
                ["action"] = form.Action,
                ["fields"] = form.Fields.Select(field => new Dictionary<string, object?>
                {
                    ["name"] = field.Name,
                    ["id"] = field.Id,
                    ["type"] = field.Type,
                    ["label"] = string.IsNullOrWhiteSpace(field.Label) ? field.Name : field.Label,
                    ["required"] = field.Required,
                }).ToList(),
            },
        };
    }

    private static void AddDefaultContent(ComponentNode root, SiteCrawlPage page)
    {
        if (string.IsNullOrWhiteSpace(page.TextExcerpt))
        {
            return;
        }

        root.Children.Add(new ComponentNode
        {
            Id = BuildNodeId("content", page.FinalUrl),
            Type = "ContentSection",
            Props =
            {
                ["title"] = page.Title,
                ["body"] = page.TextExcerpt,
                ["source_selector"] = $"route:{BuildRoutePath(page.FinalUrl)}",
            },
        });
    }

    private static List<Dictionary<string, string>> BuildSlides(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        if (section.Items.Count > 0)
        {
            return section.Items.Take(8).Select(item =>
            {
                var link = string.IsNullOrWhiteSpace(item.Url) ? EmptyLink() : BuildLink(item.Url, origin, localRoutes);
                return new Dictionary<string, string>
                {
                    ["title"] = item.Title,
                    ["body"] = item.Body,
                    ["media_url"] = item.MediaUrl,
                    ["media_alt"] = item.MediaAlt,
                    ["url"] = link["url"],
                    ["source_url"] = link["source_url"],
                    ["scope"] = link["scope"],
                };
            }).ToList();
        }

        return section.Media.Take(8).Select(media => new Dictionary<string, string>
        {
            ["title"] = string.IsNullOrWhiteSpace(media.Alt) ? section.Headline : media.Alt,
            ["body"] = section.Body,
            ["media_url"] = media.Url,
            ["media_alt"] = media.Alt,
            ["url"] = string.Empty,
            ["source_url"] = string.Empty,
            ["scope"] = "none",
        }).ToList();
    }

    private static List<Dictionary<string, string>> BuildItems(
        ExtractedSection section,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        if (section.Items.Count > 0)
        {
            return section.Items.Take(24).Select(item =>
            {
                var link = string.IsNullOrWhiteSpace(item.Url) ? EmptyLink() : BuildLink(item.Url, origin, localRoutes);
                return new Dictionary<string, string>
                {
                    ["title"] = item.Title,
                    ["body"] = item.Body,
                    ["url"] = link["url"],
                    ["source_url"] = link["source_url"],
                    ["scope"] = link["scope"],
                    ["media_url"] = item.MediaUrl,
                    ["media_alt"] = item.MediaAlt,
                };
            }).ToList();
        }

        if (section.Media.Count > 0)
        {
            return section.Media.Take(24).Select(media => new Dictionary<string, string>
            {
                ["title"] = string.IsNullOrWhiteSpace(media.Alt) ? section.Headline : media.Alt,
                ["body"] = section.Body,
                ["url"] = string.Empty,
                ["source_url"] = string.Empty,
                ["scope"] = "none",
                ["media_url"] = media.Url,
                ["media_alt"] = media.Alt,
            }).ToList();
        }

        return section.Actions.Take(24).Select(action =>
        {
            var link = BuildLink(action.Url, origin, localRoutes);
            return new Dictionary<string, string>
            {
                ["title"] = action.Label,
                ["body"] = string.Empty,
                ["url"] = link["url"],
                ["source_url"] = link["source_url"],
                ["scope"] = link["scope"],
                ["media_url"] = string.Empty,
                ["media_alt"] = string.Empty,
            };
        }).ToList();
    }

    private static List<Dictionary<string, string>> BuildLinks(
        IEnumerable<ExtractedAction> links,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes,
        int maxLinks)
    {
        return links
            .Where(link => !string.IsNullOrWhiteSpace(link.Label) && !string.IsNullOrWhiteSpace(link.Url))
            .DistinctBy(link => $"{link.Label}\n{link.Url}", StringComparer.Ordinal)
            .Take(maxLinks)
            .Select(link =>
            {
                var built = BuildLink(link.Url, origin, localRoutes);
                built["label"] = link.Label;
                return built;
            })
            .ToList();
    }

    private static Dictionary<string, string> BuildLink(
        string link,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var absolute = NormalizeAbsoluteUrl(link, origin);
        var normalized = NormalizeUrlForLookup(absolute);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            localRoutes.TryGetValue(normalized, out var localRoute))
        {
            return new Dictionary<string, string>
            {
                ["label"] = BuildLinkLabel(absolute, origin),
                ["url"] = localRoute,
                ["source_url"] = absolute,
                ["scope"] = "internal",
            };
        }

        if (IsSameOrigin(absolute, origin))
        {
            return new Dictionary<string, string>
            {
                ["label"] = BuildLinkLabel(absolute, origin),
                ["url"] = BuildRoutePath(absolute),
                ["source_url"] = absolute,
                ["scope"] = "internal",
            };
        }

        return new Dictionary<string, string>
        {
            ["label"] = BuildLinkLabel(absolute, origin),
            ["url"] = absolute,
            ["source_url"] = absolute,
            ["scope"] = "external",
        };
    }

    private static Dictionary<string, string> EmptyLink()
    {
        return new Dictionary<string, string>
        {
            ["label"] = string.Empty,
            ["url"] = string.Empty,
            ["source_url"] = string.Empty,
            ["scope"] = "none",
        };
    }

    private static Dictionary<string, string> BuildLocalRouteMap(IEnumerable<SiteCrawlPage> pages)
    {
        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            var key = NormalizeUrlForLookup(page.FinalUrl);
            if (!string.IsNullOrWhiteSpace(key) && !routes.ContainsKey(key))
            {
                routes[key] = BuildRoutePath(page.FinalUrl);
            }
        }

        return routes;
    }

    private static string NormalizeAbsoluteUrl(string link, string origin)
    {
        if (Uri.TryCreate(link, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var originUri) &&
            Uri.TryCreate(originUri, link, out var relative)
            ? relative.ToString()
            : link;
    }

    private static string NormalizeUrlForLookup(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }

    private static string BuildLinkLabel(string link, string origin)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            return link;
        }

        var path = uri.PathAndQuery;
        if (!string.IsNullOrWhiteSpace(origin) &&
            link.StartsWith(origin, StringComparison.OrdinalIgnoreCase))
        {
            return path == "/" ? "Home" : path.Trim('/').Replace('-', ' ').Replace('_', ' ');
        }

        return uri.Host;
    }

    private static bool IsSameOrigin(string link, string origin)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var linkUri) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return false;
        }

        return linkUri.Scheme.Equals(originUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            linkUri.IdnHost.Equals(originUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            linkUri.Port == originUri.Port;
    }

    private static string ResolveSiteTitle(SiteCrawlResult crawl)
    {
        var rootPage = crawl.Pages.FirstOrDefault(page => page.Depth == 0) ?? crawl.Pages.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(rootPage?.Title))
        {
            return rootPage.Title;
        }

        return string.IsNullOrWhiteSpace(crawl.Root.NormalizedStartUrl)
            ? "Generated Site"
            : crawl.Root.NormalizedStartUrl;
    }

    private static string BuildRoutePath(string finalUrl)
    {
        if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var uri))
        {
            return "/";
        }

        var path = uri.AbsolutePath;
        if (string.IsNullOrWhiteSpace(path) || path == "/")
        {
            return "/";
        }

        var extension = Path.GetExtension(path);
        if (extension.Equals(".aspx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".htm", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^extension.Length];
        }

        path = path.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            path = "/";
        }

        var querySlug = BuildQuerySlug(uri.Query);
        return string.IsNullOrWhiteSpace(querySlug)
            ? path
            : $"{path}/{querySlug}";
    }

    private static string BuildQuerySlug(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var tokens = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(part => part.Split('=', 2, StringSplitOptions.RemoveEmptyEntries))
            .Select(token => Regex.Replace(Uri.UnescapeDataString(token), "[^a-zA-Z0-9]+", "-").Trim('-'))
            .Where(token => !string.IsNullOrWhiteSpace(token));

        return string.Join('-', tokens);
    }

    private static string BuildNodeId(string prefix, string source)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return $"{prefix}-{System.Convert.ToHexString(hash)[..12].ToLowerInvariant()}";
    }

    private static ComponentRequest CloneComponentRequest(ComponentRequest request)
    {
        return new ComponentRequest
        {
            RequestId = request.RequestId,
            Role = request.Role,
            ComponentType = request.ComponentType,
            Reason = request.Reason,
            SourcePageUrl = request.SourcePageUrl,
            SourceSelector = request.SourceSelector,
        };
    }

    private static ComponentLibraryManifest CloneManifest(ComponentLibraryManifest manifest)
    {
        return new ComponentLibraryManifest
        {
            LibraryId = manifest.LibraryId,
            Version = manifest.Version,
            Components = manifest.Components.Select(component => new ComponentDefinition
            {
                Type = component.Type,
                Description = component.Description,
                SupportedRoles = component.SupportedRoles.ToList(),
                PropsSchema = ClonePropsSchema(component.PropsSchema),
                Generated = component.Generated,
            }).ToList(),
        };
    }

    private static ComponentPropsSchema ClonePropsSchema(ComponentPropsSchema schema)
    {
        return new ComponentPropsSchema
        {
            Required = schema.Required.ToList(),
            Properties = schema.Properties.ToDictionary(
                pair => pair.Key,
                pair => ClonePropSchema(pair.Value),
                StringComparer.Ordinal),
        };
    }

    private static ComponentPropSchema ClonePropSchema(ComponentPropSchema schema)
    {
        return new ComponentPropSchema
        {
            Type = schema.Type,
            Items = schema.Items is null ? null : ClonePropSchema(schema.Items),
            Required = schema.Required.ToList(),
            Properties = schema.Properties.ToDictionary(
                pair => pair.Key,
                pair => ClonePropSchema(pair.Value),
                StringComparer.Ordinal),
        };
    }
}
