using SiteCrawlerWorker.Services;

namespace Unit.Tests.Workers.SiteCrawler;

public class DeterministicSiteExtractorTests
{
    [Fact]
    public void ExtractPage_ExtractsBaselinePageModel()
    {
        var html = """
            <!doctype html>
            <html>
            <head>
              <title>Docs Home</title>
              <style>
                :root { --brand: #3366ff; }
                body { font-family: Inter, sans-serif; }
              </style>
            </head>
            <body>
              <section id="top" class="hero">
                <h1>Build Faster</h1>
                <p>Create reliable agents from captured site structure.</p>
                <a href="/docs/start">Start</a>
              </section>
              <form method="POST" action="/subscribe">
                <label for="email">Email address</label>
                <input id="email" name="email" type="email" required />
              </form>
            </body>
            </html>
            """;

        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/docs/"),
            html);

        result.Title.Should().Be("Docs Home");
        result.Links.Should().ContainSingle().Which.Should().Be("https://example.com/docs/start");

        var form = result.Forms.Should().ContainSingle().Subject;
        form.Method.Should().Be("post");
        form.Action.Should().Be("/subscribe");
        var field = form.Fields.Should().ContainSingle().Subject;
        field.Name.Should().Be("email");
        field.Type.Should().Be("email");
        field.Required.Should().BeTrue();

        var section = result.Model.Sections.Should().ContainSingle().Subject;
        section.Role.Should().Be("hero");
        section.Headline.Should().Be("Build Faster");

        result.ThemeTokens.Colors.Should().ContainKey("brand").WhoseValue.Should().Be("#3366ff");
        result.ThemeTokens.Typography.Should().ContainKey("font_family").WhoseValue.Should().Be("Inter, sans-serif");
    }

    [Fact]
    public void ExtractPage_NormalizesHttpLinksAndSkipsUnsupportedTargets()
    {
        var html = """
            <html>
            <body>
              <a href="/docs/start#intro">Start</a>
              <a href="https://EXAMPLE.com/docs/start#top">Duplicate</a>
              <a href="#local">Hash</a>
              <a href="mailto:team@example.com">Email</a>
              <a href="javascript:void(0)">Script</a>
            </body>
            </html>
            """;

        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/docs/"),
            html);

        result.Links.Should().ContainSingle().Which.Should().Be("https://example.com/docs/start");
    }
}
