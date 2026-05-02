using System.Text.Json;
using SiteCrawlerWorker.Models;
using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class ComponentLibraryLoaderTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"b4a-component-library-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void LoadDefault_LoadsBundledDefaultManifest()
    {
        var loader = new ComponentLibraryLoader();

        var manifest = loader.LoadDefault();

        manifest.LibraryId.Should().Be("bricks4agent.default");
        manifest.Components.Should().Contain(component => component.Type == "PageShell");
        manifest.Components.Should().OnlyContain(component => component.PropsSchema.Properties.Count > 0);
    }

    [Fact]
    public void Load_LoadsExternalManifest()
    {
        Directory.CreateDirectory(tempRoot);
        var path = Path.Combine(tempRoot, "manifest.json");
        var manifest = DefaultComponentLibrary.Create();
        manifest.LibraryId = "custom.library";
        manifest.Components.Add(DefaultComponentLibrary.Define(
            "CustomNotice",
            "Custom notice component.",
            ["notice"],
            new ComponentPropsSchema
            {
                Required = ["title"],
                Properties = { ["title"] = new ComponentPropSchema { Type = "string" } },
            }));
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var loader = new ComponentLibraryLoader();

        var loaded = loader.Load(path);

        loaded.LibraryId.Should().Be("custom.library");
        loaded.Components.Should().Contain(component => component.Type == "CustomNotice");
    }

    [Fact]
    public void Load_WhenPathIsDirectory_LoadsManifestInsideDirectory()
    {
        Directory.CreateDirectory(tempRoot);
        var path = Path.Combine(tempRoot, "manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(DefaultComponentLibrary.Create(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var loader = new ComponentLibraryLoader();

        var loaded = loader.Load(tempRoot);

        loaded.LibraryId.Should().Be("bricks4agent.default");
        loaded.Components.Should().Contain(component => component.Type == "PageShell");
    }

    [Fact]
    public void Load_WhenManifestOmitsPropsSchema_Throws()
    {
        Directory.CreateDirectory(tempRoot);
        var path = Path.Combine(tempRoot, "manifest.json");
        File.WriteAllText(path, """
            {
              "library_id": "broken",
              "version": "1.0.0",
              "components": [
                {
                  "type": "BrokenComponent",
                  "description": "Broken",
                  "supported_roles": ["broken"]
                }
              ]
            }
            """);
        var loader = new ComponentLibraryLoader();

        var act = () => loader.Load(path);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*props_schema*");
    }
}
