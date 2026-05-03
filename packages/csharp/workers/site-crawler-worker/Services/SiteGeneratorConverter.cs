using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class SiteGeneratorConverter
{
    private readonly ComponentLibraryManifest baseManifest;

    public SiteGeneratorConverter(ComponentLibraryManifest componentLibrary)
    {
        baseManifest = CloneManifest(componentLibrary);
    }

    public GeneratorSiteDocument Convert(SiteCrawlResult crawl)
    {
        ArgumentNullException.ThrowIfNull(crawl);

        var manifest = CloneManifest(baseManifest);
        var intent = new SiteIntentExtractor().Extract(crawl);
        var templates = new TemplateFrameworkLoader().LoadDefault();
        var plan = new TemplateMatcher(templates, manifest).Match(intent);
        return new TemplateCompiler(manifest).Compile(crawl, intent, plan);
    }

    private static GeneratorRoute BuildRoute(
        SiteCrawlResult crawl,
        SiteCrawlPage page,
        ExtractedPageModel? extractedPage,
        GeneratorSiteDocument document,
        ComponentLibraryManifest manifest,
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

        root.Children.Add(new ComponentNode
        {
            Id = BuildNodeId("header", page.FinalUrl),
            Type = "SiteHeader",
            Props =
            {
                ["title"] = document.Site.Title,
                ["logo_url"] = extractedPage?.Header.LogoUrl ?? string.Empty,
                ["logo_alt"] = extractedPage?.Header.LogoAlt ?? string.Empty,
                ["utility_links"] = BuildLinks(extractedPage?.Header.UtilityLinks ?? [], crawl.Root.Origin, localRoutes, maxLinks: 10),
                ["primary_links"] = BuildLinks(extractedPage?.Header.PrimaryLinks ?? [], crawl.Root.Origin, localRoutes, maxLinks: 12),
                ["links"] = new List<Dictionary<string, string>>(),
            },
        });

        foreach (var section in extractedPage?.Sections ?? [])
        {
            root.Children.Add(BuildSectionNode(section, page, document, manifest, localRoutes, crawl.Root.Origin));
        }

        foreach (var form in page.Forms.Take(4))
        {
            root.Children.Add(new ComponentNode
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
            });
        }

        root.Children.Add(new ComponentNode
        {
            Id = BuildNodeId("footer", page.FinalUrl),
            Type = "SiteFooter",
            Props =
            {
                ["source_url"] = page.FinalUrl,
                ["notice"] = "Reconstructed from static public source cues. Not an equivalent clone.",
                ["logo_url"] = extractedPage?.Footer.LogoUrl ?? string.Empty,
                ["logo_alt"] = extractedPage?.Footer.LogoAlt ?? string.Empty,
                ["contact_text"] = extractedPage?.Footer.Text ?? string.Empty,
                ["links"] = BuildLinks(extractedPage?.Footer.Links ?? [], crawl.Root.Origin, localRoutes, maxLinks: 16),
            },
        });

        return new GeneratorRoute
        {
            Path = BuildRoutePath(page.FinalUrl),
            Title = pageTitle,
            SourceUrl = page.FinalUrl,
            Root = root,
        };
    }

    private static ComponentNode BuildSectionNode(
        ExtractedSection section,
        SiteCrawlPage page,
        GeneratorSiteDocument document,
        ComponentLibraryManifest manifest,
        IReadOnlyDictionary<string, string> localRoutes,
        string origin)
    {
        if (section.Media.Count > 0 || section.Actions.Count > 0 || section.Items.Count > 0 ||
            section.Role is "program_grid" or "news" or "gallery" or "faq" or "process" or "benefits" or "contact" or "feature_grid" or "stats")
        {
            return BuildAtomicSectionNode(section, page, origin, localRoutes);
        }

        var type = section.Role switch
        {
            "hero" => "HeroSection",
            "content" or "main" or "article" => "ContentSection",
            "footer" => "SiteFooter",
            _ => EnsureGeneratedComponent(section, page, document, manifest),
        };

        var node = new ComponentNode
        {
            Id = string.IsNullOrWhiteSpace(section.Id)
                ? BuildNodeId(section.Role, $"{page.FinalUrl}:{section.SourceSelector}:{section.Headline}")
                : SanitizeId(section.Id),
            Type = type,
            Props =
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
                ["source_selector"] = section.SourceSelector,
            },
        };

        if (type == "SiteFooter")
        {
            node.Props["source_url"] = page.FinalUrl;
            node.Props["notice"] = section.Body;
        }

        return node;
    }

    private static ComponentNode BuildAtomicSectionNode(
        ExtractedSection section,
        SiteCrawlPage page,
        string crawlOrigin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var sectionNode = new ComponentNode
        {
            Id = string.IsNullOrWhiteSpace(section.Id)
                ? BuildNodeId(section.Role, $"{page.FinalUrl}:{section.SourceSelector}:{section.Headline}")
                : SanitizeId(section.Id),
            Type = "AtomicSection",
            Props =
            {
                ["variant"] = section.Role == "hero" ? "hero" : "standard",
                ["source_selector"] = section.SourceSelector,
            },
        };

        if (section.Role == "hero" && section.Media.Count > 0)
        {
            sectionNode.Children.Add(BuildImageBlock(section.Media[0]));
        }

        sectionNode.Children.Add(new ComponentNode
        {
            Id = BuildNodeId("text", $"{page.FinalUrl}:{section.SourceSelector}:text"),
            Type = "TextBlock",
            Props =
            {
                ["title"] = section.Headline,
                ["body"] = section.Body,
            },
        });

        var mediaAsCarouselCards = section.Role is "gallery" or "news" &&
            section.Items.Count == 0 &&
            section.Media.Count > 1;

        foreach (var media in section.Media
                     .Skip(section.Role == "hero" ? 1 : 0)
                     .Take(mediaAsCarouselCards ? 0 : 3))
        {
            sectionNode.Children.Add(BuildImageBlock(media));
        }

        foreach (var action in section.Actions.Take(3))
        {
            var link = BuildLink(action.Url, crawlOrigin, localRoutes);
            sectionNode.Children.Add(new ComponentNode
            {
                Id = BuildNodeId("action", $"{page.FinalUrl}:{section.SourceSelector}:{action.Label}:{action.Url}"),
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

        var cardItems = section.Items.Count > 0
            ? section.Items
            : mediaAsCarouselCards
                ? section.Media.Select(media => new ExtractedItem
                {
                    Title = string.IsNullOrWhiteSpace(media.Alt) ? "Image" : media.Alt,
                    Body = string.Empty,
                    MediaUrl = media.Url,
                    MediaAlt = media.Alt,
                }).ToList()
                : new List<ExtractedItem>();

        if (cardItems.Count > 0)
        {
            var grid = new ComponentNode
            {
                Id = BuildNodeId("grid", $"{page.FinalUrl}:{section.SourceSelector}:items"),
                Type = "CardGrid",
                Props =
                {
                    ["title"] = section.Headline,
                    ["layout"] = section.Role is "news" or "gallery" ? "carousel" : "grid",
                },
            };

            foreach (var item in cardItems.Take(24))
            {
                var link = string.IsNullOrWhiteSpace(item.Url)
                    ? new Dictionary<string, string>
                    {
                        ["url"] = string.Empty,
                        ["source_url"] = string.Empty,
                        ["scope"] = "none",
                    }
                    : BuildLink(item.Url, crawlOrigin, localRoutes);
                grid.Children.Add(new ComponentNode
                {
                    Id = BuildNodeId("card", $"{page.FinalUrl}:{section.SourceSelector}:{item.Title}:{item.Url}"),
                    Type = "FeatureCard",
                    Props =
                    {
                        ["title"] = item.Title,
                        ["body"] = item.Body,
                        ["url"] = link["url"],
                        ["source_url"] = link["source_url"],
                        ["scope"] = link["scope"],
                        ["media_url"] = item.MediaUrl,
                        ["media_alt"] = item.MediaAlt,
                    },
                });
            }

            sectionNode.Children.Add(grid);
        }

        return sectionNode;
    }

    private static ComponentNode BuildImageBlock(ExtractedMedia media)
    {
        return new ComponentNode
        {
            Id = BuildNodeId("image", media.Url),
            Type = "ImageBlock",
            Props =
            {
                ["url"] = media.Url,
                ["alt"] = media.Alt,
            },
        };
    }

    private static string EnsureGeneratedComponent(
        ExtractedSection section,
        SiteCrawlPage page,
        GeneratorSiteDocument document,
        ComponentLibraryManifest manifest)
    {
        var role = string.IsNullOrWhiteSpace(section.Role) ? "custom" : section.Role.Trim();
        var type = $"Generated{ToPascalCase(role)}Section";
        if (manifest.Components.All(component => component.Type != type))
        {
            manifest.Components.Add(DefaultComponentLibrary.Define(
                type,
                $"Generated local component for source role '{role}'.",
                [role],
                new()
                {
                    Required = ["title", "body"],
                    Properties =
                    {
                        ["title"] = new ComponentPropSchema { Type = "string" },
                        ["body"] = new ComponentPropSchema { Type = "string" },
                        ["source_selector"] = new ComponentPropSchema { Type = "string" },
                    },
                },
                generated: true));
        }

        if (document.ComponentRequests.All(request =>
                !string.Equals(request.ComponentType, type, StringComparison.Ordinal) ||
                !string.Equals(request.SourceSelector, section.SourceSelector, StringComparison.Ordinal)))
        {
            document.ComponentRequests.Add(new ComponentRequest
            {
                RequestId = $"component-request-{document.ComponentRequests.Count + 1}",
                Role = role,
                ComponentType = type,
                Reason = $"No built-in component supports role '{role}'. Generated a local component definition.",
                SourcePageUrl = page.FinalUrl,
                SourceSelector = section.SourceSelector,
            });
        }

        return type;
    }

    private static List<Dictionary<string, string>> BuildLinks(
        IEnumerable<string> links,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes,
        int maxLinks)
    {
        return links
            .Where(link => Uri.TryCreate(link, UriKind.Absolute, out _))
            .Distinct(StringComparer.Ordinal)
            .Take(maxLinks)
            .Select(link => BuildLink(link, origin, localRoutes))
            .ToList();
    }

    private static List<Dictionary<string, string>> BuildLinks(
        IEnumerable<ExtractedAction> links,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes,
        int maxLinks)
    {
        return links
            .Where(link => !string.IsNullOrWhiteSpace(link.Url) && !string.IsNullOrWhiteSpace(link.Label))
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

    private static Dictionary<string, string> BuildLocalRouteMap(IEnumerable<SiteCrawlPage> pages)
    {
        var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in pages)
        {
            var key = NormalizeUrlForLookup(page.FinalUrl);
            if (string.IsNullOrWhiteSpace(key) || routes.ContainsKey(key))
            {
                continue;
            }

            routes[key] = BuildRoutePath(page.FinalUrl);
        }

        return routes;
    }

    private static Dictionary<string, string> BuildLink(
        string link,
        string origin,
        IReadOnlyDictionary<string, string> localRoutes)
    {
        var normalized = NormalizeUrlForLookup(link);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            localRoutes.TryGetValue(normalized, out var localRoute))
        {
            return new Dictionary<string, string>
            {
                ["label"] = BuildLinkLabel(link, origin),
                ["url"] = localRoute,
                ["source_url"] = link,
                ["scope"] = "internal",
            };
        }

        if (IsSameOrigin(link, origin))
        {
            return new Dictionary<string, string>
            {
                ["label"] = BuildLinkLabel(link, origin),
                ["url"] = BuildRoutePath(link),
                ["source_url"] = link,
                ["scope"] = "internal",
            };
        }

        return new Dictionary<string, string>
        {
            ["label"] = BuildLinkLabel(link, origin),
            ["url"] = link,
            ["source_url"] = link,
            ["scope"] = "external",
        };
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

    private static ExtractedPageModel? FindExtractedPage(SiteCrawlResult crawl, SiteCrawlPage page)
    {
        return crawl.ExtractedModel.Pages.FirstOrDefault(candidate =>
            string.Equals(candidate.PageUrl, page.FinalUrl, StringComparison.Ordinal));
    }

    private static ExtractedPageModel? BuildVisualExtractedPage(SiteCrawlPage page)
    {
        var snapshot = page.VisualSnapshot;
        if (snapshot is null || snapshot.Regions.Count == 0)
        {
            return null;
        }

        var model = new ExtractedPageModel
        {
            PageUrl = page.FinalUrl,
        };

        var headerRegion = snapshot.Regions.FirstOrDefault(region => IsVisualRole(region, "header")) ??
            snapshot.Regions.FirstOrDefault(region => IsVisualRole(region, "nav"));
        if (headerRegion is not null)
        {
            var logo = FindVisualLogo(headerRegion);
            model.Header = new ExtractedHeader
            {
                LogoUrl = logo?.Url ?? string.Empty,
                LogoAlt = logo?.Alt ?? string.Empty,
                PrimaryLinks = headerRegion.Actions
                    .Where(action => !string.IsNullOrWhiteSpace(action.Label) && !string.IsNullOrWhiteSpace(action.Url))
                    .DistinctBy(action => $"{action.Label}\n{action.Url}", StringComparer.Ordinal)
                    .Take(16)
                    .ToList(),
            };
        }

        var footerRegion = snapshot.Regions.FirstOrDefault(region => IsVisualRole(region, "footer"));
        if (footerRegion is not null)
        {
            var logo = FindVisualLogo(footerRegion);
            model.Footer = new ExtractedFooter
            {
                LogoUrl = logo?.Url ?? string.Empty,
                LogoAlt = logo?.Alt ?? string.Empty,
                Text = LimitVisualText(footerRegion.Text, 600),
                Links = footerRegion.Actions
                    .Where(action => !string.IsNullOrWhiteSpace(action.Label) && !string.IsNullOrWhiteSpace(action.Url))
                    .DistinctBy(action => $"{action.Label}\n{action.Url}", StringComparer.Ordinal)
                    .Take(16)
                    .ToList(),
            };
        }

        foreach (var region in snapshot.Regions)
        {
            if (IsVisualRole(region, "header") || IsVisualRole(region, "footer") || IsVisualRole(region, "nav"))
            {
                continue;
            }

            var body = CleanVisualBody(region.Text, region.Headline);
            if (string.IsNullOrWhiteSpace(body) &&
                region.Media.Count == 0 &&
                region.Actions.Count == 0 &&
                region.Items.Count == 0)
            {
                continue;
            }

            model.Sections.Add(new ExtractedSection
            {
                Id = string.IsNullOrWhiteSpace(region.Id) ? $"visual-section-{model.Sections.Count + 1}" : region.Id,
                Tag = string.IsNullOrWhiteSpace(region.Tag) ? "section" : region.Tag,
                Role = NormalizeVisualRole(region),
                Headline = region.Headline,
                Body = body,
                SourceSelector = string.IsNullOrWhiteSpace(region.Selector)
                    ? $"visual-region-{model.Sections.Count + 1}"
                    : region.Selector,
                Media = region.Media
                    .Where(media => !string.IsNullOrWhiteSpace(media.Url))
                    .DistinctBy(media => media.Url, StringComparer.Ordinal)
                    .Take(8)
                    .ToList(),
                Actions = region.Actions
                    .Where(action => !string.IsNullOrWhiteSpace(action.Label) && !string.IsNullOrWhiteSpace(action.Url))
                    .DistinctBy(action => $"{action.Label}\n{action.Url}", StringComparer.Ordinal)
                    .Take(8)
                    .ToList(),
                Items = region.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Title) ||
                        !string.IsNullOrWhiteSpace(item.Body) ||
                        !string.IsNullOrWhiteSpace(item.MediaUrl))
                    .DistinctBy(item => $"{item.Title}\n{item.Url}\n{item.MediaUrl}", StringComparer.Ordinal)
                    .Take(24)
                    .ToList(),
            });
        }

        return model.Sections.Count == 0 &&
            string.IsNullOrWhiteSpace(model.Header.LogoUrl) &&
            model.Header.PrimaryLinks.Count == 0 &&
            string.IsNullOrWhiteSpace(model.Footer.Text)
            ? null
            : model;
    }

    private static bool IsVisualRole(VisualRegion region, string role)
    {
        return string.Equals(region.Role, role, StringComparison.OrdinalIgnoreCase);
    }

    private static ExtractedMedia? FindVisualLogo(VisualRegion region)
    {
        return region.Media.FirstOrDefault(media =>
            media.Alt.Contains("logo", StringComparison.OrdinalIgnoreCase) ||
            media.Url.Contains("logo", StringComparison.OrdinalIgnoreCase)) ??
            region.Media.FirstOrDefault();
    }

    private static string NormalizeVisualRole(VisualRegion region)
    {
        var role = region.Role.ToLowerInvariant();
        if (role is "card_grid" or "visual_grid")
        {
            var uniqueItemMedia = region.Items
                .Select(item => item.MediaUrl)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.Ordinal)
                .Count();
            var uniqueRegionMedia = region.Media
                .Select(media => media.Url)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (Math.Max(uniqueItemMedia, uniqueRegionMedia) >= 3)
            {
                return "gallery";
            }
        }

        return role switch
        {
            "carousel" => "gallery",
            "card_grid" => "feature_grid",
            "visual_grid" => "feature_grid",
            "header" => "content",
            "nav" => "content",
            "footer" => "footer",
            "hero" => "hero",
            "news" => "news",
            "form" => "contact",
            _ => "content",
        };
    }

    private static string CleanVisualBody(string text, string headline)
    {
        var body = (text ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(headline) && body.StartsWith(headline, StringComparison.Ordinal))
        {
            body = body[headline.Length..].Trim();
        }

        return LimitVisualText(body, 2000);
    }

    private static string LimitVisualText(string text, int limit)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length <= limit ? value : value[..limit].TrimEnd();
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
            var rootQuerySlug = BuildQuerySlug(uri.Query);
            return string.IsNullOrWhiteSpace(rootQuerySlug) ? "/" : $"/{rootQuerySlug}";
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

    private static string SanitizeId(string value)
    {
        var sanitized = Regex.Replace(value.Trim(), "[^a-zA-Z0-9_-]+", "-");
        return string.IsNullOrWhiteSpace(sanitized) ? "node" : sanitized;
    }

    private static string ToPascalCase(string value)
    {
        var words = Regex.Split(value, "[^a-zA-Z0-9]+")
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToArray();
        if (words.Length == 0)
        {
            return "Custom";
        }

        return string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
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
