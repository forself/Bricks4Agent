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
        };

        foreach (var page in crawl.Pages)
        {
            var extractedPage = FindExtractedPage(crawl, page);
            document.Routes.Add(BuildRoute(crawl, page, extractedPage, document, manifest));
        }

        return document;
    }

    private static GeneratorRoute BuildRoute(
        SiteCrawlResult crawl,
        SiteCrawlPage page,
        ExtractedPageModel? extractedPage,
        GeneratorSiteDocument document,
        ComponentLibraryManifest manifest)
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
                ["links"] = BuildLinks(page.Links, crawl.Root.Origin, maxLinks: 12),
            },
        });

        foreach (var section in extractedPage?.Sections ?? [])
        {
            root.Children.Add(BuildSectionNode(section, page, document, manifest));
        }

        if (page.Links.Count > 0)
        {
            root.Children.Add(new ComponentNode
            {
                Id = BuildNodeId("links", page.FinalUrl),
                Type = "LinkList",
                Props =
                {
                    ["title"] = "Related links",
                    ["links"] = BuildLinks(page.Links, crawl.Root.Origin, maxLinks: 24),
                },
            });
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
        ComponentLibraryManifest manifest)
    {
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
                    ["title"] = "string",
                    ["body"] = "string",
                    ["source_selector"] = "string",
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

    private static List<Dictionary<string, string>> BuildLinks(IEnumerable<string> links, string origin, int maxLinks)
    {
        return links
            .Where(link => Uri.TryCreate(link, UriKind.Absolute, out _))
            .Distinct(StringComparer.Ordinal)
            .Take(maxLinks)
            .Select(link => new Dictionary<string, string>
            {
                ["label"] = BuildLinkLabel(link, origin),
                ["url"] = link,
            })
            .ToList();
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

    private static ExtractedPageModel? FindExtractedPage(SiteCrawlResult crawl, SiteCrawlPage page)
    {
        return crawl.ExtractedModel.Pages.FirstOrDefault(candidate =>
            string.Equals(candidate.PageUrl, page.FinalUrl, StringComparison.Ordinal));
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

        var path = uri.PathAndQuery;
        return string.IsNullOrWhiteSpace(path) ? "/" : path;
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
                Props = new Dictionary<string, string>(component.Props, StringComparer.Ordinal),
                Generated = component.Generated,
            }).ToList(),
        };
    }
}
