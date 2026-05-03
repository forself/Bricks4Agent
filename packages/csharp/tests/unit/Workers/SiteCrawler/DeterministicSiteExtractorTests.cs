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
    public void ExtractPage_PreservesFormFieldIdWhenNameIsPresent()
    {
        var html = """
            <html>
            <body>
              <form>
                <input name="email" id="email-field" type="email" required />
              </form>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        var field = result.Forms.Should().ContainSingle().Subject.Fields.Should().ContainSingle().Subject;
        field.Name.Should().Be("email");
        field.Id.Should().Be("email-field");
    }

    [Fact]
    public void ExtractPage_PrefersExplicitHeroDivInsideSectionWrapper()
    {
        var html = """
            <html>
            <body>
              <section class="page">
                <div class="hero">
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

        var wrapper = result.Model.Sections.Should()
            .ContainSingle(section => section.Tag == "section")
            .Subject;
        wrapper.Role.Should().Be("content");

        var hero = result.Model.Sections.Should()
            .ContainSingle(section => section.Role == "hero")
            .Subject;
        hero.Tag.Should().Be("div");
        hero.SourceSelector.Should().Be("div.hero");
        hero.Headline.Should().Be("Build Faster");
    }

    [Fact]
    public void ExtractPage_BoundsPageTextExcerptAndSectionBody()
    {
        var longText = new string('x', 2500);
        var html = $"""
            <html>
            <body>
              <section>
                <h1>Long Content</h1>
                <p>{longText}</p>
              </section>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        result.TextExcerpt.Length.Should().BeLessThanOrEqualTo(1000);
        result.Model.Sections.Should().ContainSingle().Subject.Body.Length.Should().BeLessThanOrEqualTo(2000);
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
    public void ExtractPage_PreservesCaseSensitiveQueryLinksWhileDedupeIgnoresSchemeAndHostCase()
    {
        var html = """
            <html>
            <body>
              <a href="/docs/search?q=AI#top">Upper query</a>
              <a href="HTTPS://EXAMPLE.com/docs/search?q=AI#details">Same query with host case</a>
              <a href="/docs/search?q=ai#top">Lower query</a>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        result.Links.Should().Equal(
            "https://example.com/docs/search?q=AI",
            "https://example.com/docs/search?q=ai");
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

    [Fact]
    public void ExtractPage_ClassifiesSpecificVisualRegionsForGeneratedComponents()
    {
        var html = """
            <html>
            <body>
              <section class="news-list">
                <h2>Campus News</h2>
                <article>Story one</article>
                <article>Story two</article>
              </section>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(
            new Uri("https://example.com/"),
            html);

        result.Model.Sections.Should()
            .ContainSingle(section => section.SourceSelector == "section.news-list")
            .Which.Role.Should().Be("news");
    }

    [Fact]
    public void ExtractPage_ClassifiesSearchResultPatternSections()
    {
        var html = """
            <html>
            <body>
              <main>
                <section class="lookup-panel">
                  <h1>Search services</h1>
                  <form><input name="q" type="search"><button>Search</button></form>
                </section>
                <aside class="facets">
                  <h2>Filter results</h2>
                  <label><input type="checkbox"> News</label>
                </aside>
                <section class="search-results">
                  <h2>42 results</h2>
                  <article><a href="/one">First result</a><p>Result summary.</p></article>
                  <article><a href="/two">Second result</a><p>Result summary.</p></article>
                </section>
                <nav class="pagination"><a href="?page=2">Next</a></nav>
              </main>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(new Uri("https://example.com/"), html);

        result.Model.Sections.Select(section => section.Role).Should().Contain([
            "search",
            "filters",
            "results",
            "pagination",
        ]);
    }

    [Fact]
    public void ExtractPage_ClassifiesReportDashboardPatternSections()
    {
        var html = """
            <html>
            <body>
              <section class="dashboard filters">
                <h2>Report filters</h2>
                <button>Export</button>
              </section>
              <section class="kpi metrics">
                <h2>Key metrics</h2>
                <p>Total 1,200 Growth 12%</p>
              </section>
              <section class="chart visualization">
                <h2>Monthly trend</h2>
                <p>Jan 20 Feb 32 Mar 41</p>
              </section>
              <section class="data-table">
                <h2>Recent records</h2>
                <table><tr><th>Name</th><th>Status</th></tr><tr><td>Alpha</td><td>Open</td></tr></table>
              </section>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(new Uri("https://example.com/"), html);

        result.Model.Sections.Select(section => section.Role).Should().Contain([
            "filter_bar",
            "stats",
            "chart",
            "data_table",
        ]);
    }

    [Fact]
    public void ExtractPage_ClassifiesInputFlowPatternSections()
    {
        var html = """
            <html>
            <body>
              <section class="stepper">
                <h2>Step 1 of 3</h2>
                <ol><li>Applicant</li><li>Review</li><li>Submit</li></ol>
              </section>
              <section class="application-form">
                <h2>Applicant information</h2>
                <label>Name <input name="name" required></label>
                <label>Email <input name="email" type="email" required></label>
              </section>
              <section class="validation-summary">
                <h2>Required fields</h2>
                <p>Name and email are required.</p>
              </section>
              <section class="form-actions">
                <h2>Actions</h2>
                <button>Back</button><button>Continue</button>
              </section>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(new Uri("https://example.com/"), html);

        result.Model.Sections.Select(section => section.Role).Should().Contain([
            "steps",
            "form",
            "validation",
            "action_bar",
        ]);
    }

    [Fact]
    public void ExtractPage_ClassifiesCommercialShowcasePatternSections()
    {
        var html = """
            <html>
            <body>
              <section class="product-hero showcase">
                <h1>Launch faster</h1>
                <a href="/signup">Start free</a>
              </section>
              <section class="products">
                <h2>Products</h2>
                <article><h3>Starter</h3><p>For small teams.</p></article>
                <article><h3>Scale</h3><p>For growing teams.</p></article>
              </section>
              <section class="proof-strip testimonials">
                <h2>Trusted by teams</h2>
                <p>500 customers</p>
              </section>
              <section class="pricing">
                <h2>Pricing</h2>
                <article><h3>Starter</h3><p>$19 per month</p></article>
                <article><h3>Pro</h3><p>$49 per month</p></article>
              </section>
              <section class="cta">
                <h2>Ready to start?</h2>
                <a href="/signup">Start free</a>
              </section>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(new Uri("https://example.com/"), html);

        result.Model.Sections.Select(section => section.Role).Should().Contain([
            "product_hero",
            "products",
            "proof",
            "pricing",
            "cta",
        ]);
    }

    [Fact]
    public void ExtractPage_ExtractsReusableVisualAtoms()
    {
        var html = """
            <html>
            <body>
              <section class="hero">
                <img src="/assets/hero.jpg" alt="Campus gate">
                <h1>Study at SHU</h1>
                <p>Media and communication programs in Taipei.</p>
                <a class="btn primary" href="/apply">Apply now</a>
              </section>
              <section class="programs">
                <h2>Programs</h2>
                <article class="card">
                  <img src="/assets/journalism.jpg" alt="Journalism">
                  <h3>Journalism</h3>
                  <p>Reporting, editing, and multimedia storytelling.</p>
                  <a href="/programs/journalism">Explore</a>
                </article>
                <article class="card">
                  <h3>Public Relations</h3>
                  <p>Campaign strategy and communication planning.</p>
                </article>
              </section>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(new Uri("https://example.com/"), html);

        var hero = result.Model.Sections.Single(section => section.Role == "hero");
        hero.Media.Should().ContainSingle(media =>
            media.Url == "https://example.com/assets/hero.jpg" &&
            media.Alt == "Campus gate");
        hero.Actions.Should().ContainSingle(action =>
            action.Label == "Apply now" &&
            action.Url == "https://example.com/apply" &&
            action.Kind == "primary");

        var programs = result.Model.Sections.Single(section => section.Role == "program_grid");
        programs.Items.Should().HaveCount(2);
        programs.Items[0].Title.Should().Be("Journalism");
        programs.Items[0].Body.Should().Be("Reporting, editing, and multimedia storytelling.");
        programs.Items[0].MediaUrl.Should().Be("https://example.com/assets/journalism.jpg");
        programs.Items[0].Url.Should().Be("https://example.com/programs/journalism");
    }

    [Fact]
    public void ExtractPage_IgnoresNavigationChromeAndExtractsContentCards()
    {
        var html = """
            <html>
            <body>
              <div class="logosearch-area">
                <div class="n2021-area">
                  <p class="top-linkbox"><a href="/apply">Apply</a><a href="/system">System</a></p>
                  <h1><a href="/"><img src="/logo.png" alt="University logo"></a></h1>
                </div>
                <div class="navbar"><a href="/about">About</a></div>
              </div>
              <div class="m1-area spotlight">
                <div class="sm1-box">
                  <h2>Spotlight</h2>
                  <div class="sbl-box">
                    <img src="/news/main.jpg" alt="Main story">
                    <h5><a href="/news/main">Main campus story</a></h5>
                    <p>Lead story summary.</p>
                    <a href="/news/main" class="read-more">Read more</a>
                  </div>
                  <ul>
                    <li>
                      <img src="/news/one.jpg" alt="Story one">
                      <h6><a href="/news/one">Story one</a></h6>
                      <a href="/news/one" class="read-more">Read more</a>
                    </li>
                  </ul>
                </div>
              </div>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(new Uri("https://example.com/"), html);

        result.Model.Sections.Should().NotContain(section =>
            section.SourceSelector.Contains("logosearch", StringComparison.Ordinal) ||
            section.SourceSelector.Contains("n2021", StringComparison.Ordinal) ||
            section.Body.Contains("Apply System", StringComparison.Ordinal));

        var spotlight = result.Model.Sections.Should()
            .ContainSingle(section => section.SourceSelector == "div.m1-area")
            .Subject;
        spotlight.Role.Should().Be("gallery");
        spotlight.Headline.Should().Be("Spotlight");
        spotlight.Items.Should().HaveCount(2);
        spotlight.Items[0].Title.Should().Be("Main campus story");
        spotlight.Items[0].Body.Should().Be("Lead story summary.");
        spotlight.Items[0].MediaUrl.Should().Be("https://example.com/news/main.jpg");
        spotlight.Items[0].Url.Should().Be("https://example.com/news/main");
    }

    [Fact]
    public void ExtractPage_ExtractsVisualHeaderAndFooterChrome()
    {
        var html = """
            <html>
            <body>
              <div class="logosearch-area">
                <p class="top-linkbox">
                  <a href="/apply.aspx">Apply</a>
                  <a href="/contact.aspx">Contact</a>
                </p>
                <h1><a href="/default.aspx"><img src="/logo.png" alt="University logo"></a></h1>
                <div class="navbar">
                  <ul class="nav">
                    <li><a href="/about.aspx">About SHU</a></li>
                    <li><a href="/admission.aspx">Admissions</a></li>
                  </ul>
                </div>
              </div>
              <main>
                <section><h2>Welcome</h2><p>Main content.</p></section>
              </main>
              <div class="m1-area footer">
                <img src="/footer-logo.png" alt="Footer logo">
                <span>No. 1, University Road</span>
                <span>Tel: 02-2236-8225</span>
                <a href="/privacy.aspx">Privacy</a>
              </div>
            </body>
            </html>
            """;
        var extractor = new DeterministicSiteExtractor();

        var result = extractor.ExtractPage(new Uri("https://example.com/"), html);

        result.Model.Header.LogoUrl.Should().Be("https://example.com/logo.png");
        result.Model.Header.LogoAlt.Should().Be("University logo");
        result.Model.Header.UtilityLinks.Should().Contain(action => action.Label == "Apply" && action.Url == "https://example.com/apply.aspx");
        result.Model.Header.PrimaryLinks.Should().Contain(action => action.Label == "About SHU" && action.Url == "https://example.com/about.aspx");
        result.Model.Header.PrimaryLinks.Should().Contain(action => action.Label == "Admissions" && action.Url == "https://example.com/admission.aspx");

        result.Model.Footer.LogoUrl.Should().Be("https://example.com/footer-logo.png");
        result.Model.Footer.Text.Should().Contain("No. 1, University Road");
        result.Model.Footer.Text.Should().Contain("Tel: 02-2236-8225");
        result.Model.Footer.Links.Should().Contain(action => action.Label == "Privacy" && action.Url == "https://example.com/privacy.aspx");
    }
}
