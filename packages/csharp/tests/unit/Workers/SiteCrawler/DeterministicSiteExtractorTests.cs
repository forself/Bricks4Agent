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

    [Fact]
    public void ExtractPage_PrefersSemanticHeroSectionOverInnerContainerDiv()
    {
        var html = """
            <html>
            <body>
              <section class="hero">
                <div class="container">
                  <h1>Build Faster</h1>
                  <p>Create reliable agents.</p>
                </div>
              </section>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        var section = result.Model.Sections.Should().ContainSingle().Subject;
        section.Tag.Should().Be("section");
        section.Role.Should().Be("hero");
        section.Headline.Should().Be("Build Faster");
    }

    [Fact]
    public void ExtractPage_PrefersOuterExplicitHeroDivOverInnerContainerDiv()
    {
        var html = """
            <html>
            <body>
              <div class="hero">
                <div class="container">
                  <h1>Build Faster</h1>
                  <p>Create reliable agents.</p>
                </div>
              </div>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        var section = result.Model.Sections.Should().ContainSingle().Subject;
        section.Tag.Should().Be("div");
        section.Role.Should().Be("hero");
        section.Headline.Should().Be("Build Faster");
    }

    [Fact]
    public void ExtractPage_PrefersExplicitHeroDivInsideMainOverMainWrapper()
    {
        var html = """
            <html>
            <body>
              <main>
                <div class="hero">
                  <h1>Build Faster</h1>
                  <p>Create reliable agents.</p>
                </div>
              </main>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        var main = result.Model.Sections.Should()
            .ContainSingle(section => section.Tag == "main")
            .Subject;
        main.Role.Should().Be("content");

        var hero = result.Model.Sections.Should()
            .ContainSingle(section => section.Role == "hero")
            .Subject;
        hero.Tag.Should().Be("div");
        hero.SourceSelector.Should().Be("div.hero");
        hero.Headline.Should().Be("Build Faster");
    }

    [Fact]
    public void ExtractPage_PreservesCaseSensitivePathLinksWhileDedupeIgnoresSchemeAndHostCase()
    {
        var html = """
            <html>
            <body>
              <a href="/Docs">Upper path</a>
              <a href="HTTPS://EXAMPLE.com/Docs#top">Same upper path with host case</a>
              <a href="/docs">Lower path</a>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        result.Links.Should().Equal(
            "https://example.com/Docs",
            "https://example.com/docs");
    }

    [Fact]
    public void ExtractPage_WhenHeroSectionIsInsideMain_AssignsHeroOnlyToSection()
    {
        var html = """
            <html>
            <body>
              <main>
                <section class="hero">
                  <h1>Build Faster</h1>
                  <p>Create reliable agents.</p>
                </section>
              </main>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        var main = result.Model.Sections.Should()
            .ContainSingle(section => section.Tag == "main")
            .Subject;
        main.Role.Should().Be("content");

        var hero = result.Model.Sections.Should()
            .ContainSingle(section => section.Role == "hero")
            .Subject;
        hero.Tag.Should().Be("section");
        hero.Headline.Should().Be("Build Faster");
    }

    [Fact]
    public void ExtractPage_DoesNotTreatBroadMarketingWordsAsHeroSignals()
    {
        var html = """
            <html>
            <body>
              <section id="intro">
                <h2>Intro</h2>
                <p>Opening copy.</p>
              </section>
              <header class="masthead">
                <p>Top navigation.</p>
              </header>
              <article class="jumbotron">
                <h2>Feature</h2>
                <p>Feature copy.</p>
              </article>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        result.Model.Sections.Should().OnlyContain(section => section.Role == "content");
    }
}
