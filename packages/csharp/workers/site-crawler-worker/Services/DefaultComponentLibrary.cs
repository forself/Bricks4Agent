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
                    ["title"] = "string",
                    ["source_url"] = "string",
                }),
                Define("SiteHeader", "Site title and navigation links.", ["navigation", "header"], new()
                {
                    ["title"] = "string",
                    ["links"] = "array<{label:string,url:string}>",
                }),
                Define("HeroSection", "Primary visual introduction section.", ["hero"], new()
                {
                    ["title"] = "string",
                    ["body"] = "string",
                    ["source_selector"] = "string",
                }),
                Define("ContentSection", "General content block.", ["content", "article", "main"], new()
                {
                    ["title"] = "string",
                    ["body"] = "string",
                    ["source_selector"] = "string",
                }),
                Define("LinkList", "List of related links.", ["links"], new()
                {
                    ["title"] = "string",
                    ["links"] = "array<{label:string,url:string}>",
                }),
                Define("FormBlock", "Non-submitting form representation.", ["form"], new()
                {
                    ["method"] = "string",
                    ["action"] = "string",
                    ["fields"] = "array<{name:string,label:string,type:string,required:boolean}>",
                }),
                Define("SiteFooter", "Footer block with source attribution.", ["footer"], new()
                {
                    ["source_url"] = "string",
                    ["notice"] = "string",
                }),
            ],
        };
    }

    public static ComponentDefinition Define(
        string type,
        string description,
        IEnumerable<string> supportedRoles,
        Dictionary<string, string> props,
        bool generated = false)
    {
        return new ComponentDefinition
        {
            Type = type,
            Description = description,
            SupportedRoles = supportedRoles.ToList(),
            Props = props,
            Generated = generated,
        };
    }
}
