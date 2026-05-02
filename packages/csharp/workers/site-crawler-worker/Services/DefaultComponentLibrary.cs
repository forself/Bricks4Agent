using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public static class DefaultComponentLibrary
{
    public static ComponentLibraryManifest Create()
    {
        return new ComponentLibraryManifest
        {
            LibraryId = "bricks4agent.default",
            Version = "1.0.0",
            Components =
            [
                Define("PageShell", "Top-level page container.", ["page"], new()
                {
                    Required = ["title", "source_url"],
                    Properties =
                    {
                        ["title"] = String(),
                        ["source_url"] = String(),
                    },
                }),
                Define("SiteHeader", "Site title and navigation links.", ["navigation", "header"], new()
                {
                    Required = ["title", "links"],
                    Properties =
                    {
                        ["title"] = String(),
                        ["links"] = LinkArray(),
                    },
                }),
                Define("HeroSection", "Primary visual introduction section.", ["hero"], new()
                {
                    Required = ["title", "body"],
                    Properties =
                    {
                        ["title"] = String(),
                        ["body"] = String(),
                        ["source_selector"] = String(),
                    },
                }),
                Define("ContentSection", "General content block.", ["content", "article", "main"], new()
                {
                    Required = ["title", "body"],
                    Properties =
                    {
                        ["title"] = String(),
                        ["body"] = String(),
                        ["source_selector"] = String(),
                    },
                }),
                Define("LinkList", "List of related links.", ["links"], new()
                {
                    Required = ["title", "links"],
                    Properties =
                    {
                        ["title"] = String(),
                        ["links"] = LinkArray(),
                    },
                }),
                Define("FormBlock", "Non-submitting form representation.", ["form"], new()
                {
                    Required = ["method", "action", "fields"],
                    Properties =
                    {
                        ["method"] = String(),
                        ["action"] = String(),
                        ["fields"] = new ComponentPropSchema
                        {
                            Type = "array",
                            Items = new ComponentPropSchema
                            {
                                Type = "object",
                                Required = ["name", "label", "type", "required"],
                                Properties =
                                {
                                    ["name"] = String(),
                                    ["id"] = String(),
                                    ["label"] = String(),
                                    ["type"] = String(),
                                    ["required"] = Boolean(),
                                },
                            },
                        },
                    },
                }),
                Define("SiteFooter", "Footer block with source attribution.", ["footer"], new()
                {
                    Required = ["source_url", "notice"],
                    Properties =
                    {
                        ["source_url"] = String(),
                        ["notice"] = String(),
                    },
                }),
            ],
        };
    }

    public static ComponentDefinition Define(
        string type,
        string description,
        IEnumerable<string> supportedRoles,
        ComponentPropsSchema propsSchema,
        bool generated = false)
    {
        return new ComponentDefinition
        {
            Type = type,
            Description = description,
            SupportedRoles = supportedRoles.ToList(),
            PropsSchema = propsSchema,
            Generated = generated,
        };
    }

    private static ComponentPropSchema String() => new() { Type = "string" };

    private static ComponentPropSchema Boolean() => new() { Type = "boolean" };

    private static ComponentPropSchema LinkArray()
    {
        return new ComponentPropSchema
        {
            Type = "array",
            Items = new ComponentPropSchema
            {
                Type = "object",
                Required = ["label", "url"],
                Properties =
                {
                    ["label"] = String(),
                    ["url"] = String(),
                },
            },
        };
    }
}
