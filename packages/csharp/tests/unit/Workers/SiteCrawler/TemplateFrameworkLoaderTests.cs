using System.Text.Json;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class TemplateFrameworkLoaderTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"b4a-template-framework-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadDefault_LoadsBundledInstitutionalTemplate()
    {
        var loader = new TemplateFrameworkLoader();

        var templates = loader.LoadDefault();

        templates.Templates.Should().ContainSingle(template => template.TemplateId == "institutional_site");
        var institutional = templates.Templates.Single(template => template.TemplateId == "institutional_site");
        institutional.SupportedSiteKinds.Should().Contain(["university", "school", "public_agency"]);
        institutional.PageTypes.Should().ContainKey("home");
        institutional.PageTypes["home"].Slots.Should().Contain(slot =>
            slot.Name == "hero" &&
            slot.Accepts.Contains("HeroCarousel") &&
            slot.Accepts.Contains("HeroBanner") &&
            slot.Accepts.Contains("AtomicSection"));
        institutional.PageTypes["home"].Slots.Should().Contain(slot =>
            slot.Name == "news" &&
            slot.Accepts.Contains("NewsCardCarousel") &&
            slot.Accepts.Contains("NewsGrid"));
    }

    [Fact]
    public void Load_LoadsExternalTemplateFile()
    {
        Directory.CreateDirectory(tempRoot);
        var path = Path.Combine(tempRoot, "custom_template.json");
        var manifest = new TemplateFrameworkManifest
        {
            Templates =
            [
                new TemplateDefinition
                {
                    TemplateId = "custom_site",
                    SupportedSiteKinds = ["custom"],
                    PageTypes =
                    {
                        ["home"] = new TemplatePageTypeDefinition
                        {
                            Slots =
                            [
                                new TemplateSlotDefinition
                                {
                                    Name = "content",
                                    Required = true,
                                    Accepts = ["ContentSection"],
                                    Fallback = "ContentSection",
                                },
                            ],
                        },
                    },
                },
            ],
        };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var loader = new TemplateFrameworkLoader();

        var loaded = loader.Load(path);

        loaded.Templates.Should().ContainSingle(template => template.TemplateId == "custom_site");
        loaded.Templates[0].PageTypes["home"].Slots[0].Accepts.Should().Contain("ContentSection");
    }

    [Fact]
    public void Load_WhenSlotHasNoAcceptedComponents_Throws()
    {
        Directory.CreateDirectory(tempRoot);
        var path = Path.Combine(tempRoot, "broken_template.json");
        File.WriteAllText(path, """
            {
              "templates": [
                {
                  "template_id": "broken",
                  "supported_site_kinds": ["custom"],
                  "page_types": {
                    "home": {
                      "slots": [
                        { "name": "content", "required": true, "accepts": [] }
                      ]
                    }
                  }
                }
              ]
            }
            """);
        var loader = new TemplateFrameworkLoader();

        var act = () => loader.Load(path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*accepted component*");
    }
}
