using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SiteCrawlerWorker.Models;

namespace SiteCrawlerWorker.Services;

public sealed class StaticSitePackageGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly ComponentSchemaValidator validator;
    private readonly SiteGenerationQualityAnalyzer qualityAnalyzer;

    public StaticSitePackageGenerator()
        : this(new ComponentSchemaValidator())
    {
    }

    public StaticSitePackageGenerator(ComponentSchemaValidator validator)
    {
        this.validator = validator;
        qualityAnalyzer = new SiteGenerationQualityAnalyzer(validator);
    }

    public StaticSitePackageResult Generate(GeneratorSiteDocument document, StaticSitePackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var validation = validator.Validate(document);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Invalid site document: {string.Join("; ", validation.Errors)}");
        }

        var quality = qualityAnalyzer.Analyze(document);
        if (options.EnforceQualityGate)
        {
            if (!quality.IsPassed)
            {
                throw new InvalidOperationException($"Site generation quality gate failed: {string.Join("; ", quality.Errors)}");
            }
        }

        var outputDirectory = ResolveOutputDirectory(options);
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(Path.Combine(outputDirectory, "components"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "components", "generated"));

        var files = new List<string>();
        WriteFile(outputDirectory, "index.html", BuildIndexHtml(document), files);
        WriteFile(outputDirectory, "runtime.js", RuntimeJavaScript, files);
        WriteFile(outputDirectory, "styles.css", BuildStylesCss(document), files);
        WriteFile(outputDirectory, "site.json", JsonSerializer.Serialize(document, JsonOptions), files);
        WriteFile(outputDirectory, Path.Combine("components", "manifest.json"), JsonSerializer.Serialize(document.ComponentLibrary, JsonOptions), files);
        WriteGeneratedComponentAssets(outputDirectory, document, files);
        WriteFile(outputDirectory, "README.md", BuildReadme(document), files);

        var archivePath = string.Empty;
        if (options.CreateArchive)
        {
            archivePath = ResolveArchivePath(options, outputDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            ZipFile.CreateFromDirectory(
                outputDirectory,
                archivePath,
                CompressionLevel.SmallestSize,
                includeBaseDirectory: false,
                entryNameEncoding: Encoding.UTF8);
        }

        var result = new StaticSitePackageResult
        {
            OutputDirectory = outputDirectory,
            EntryPoint = Path.Combine(outputDirectory, "index.html"),
            SiteJsonPath = Path.Combine(outputDirectory, "site.json"),
            ManifestPath = Path.Combine(outputDirectory, "components", "manifest.json"),
            ArchivePath = archivePath,
            Files = files,
            QualityReport = quality,
        };
        result.VerificationReport = new StaticSitePackageVerifier(qualityAnalyzer).Verify(result);
        return result;
    }

    private static string ResolveOutputDirectory(StaticSitePackageOptions options)
    {
        var baseDirectory = string.IsNullOrWhiteSpace(options.OutputDirectory)
            ? Path.Combine(Path.GetTempPath(), "bricks4agent-generated-sites")
            : options.OutputDirectory;
        var packageName = SanitizePathSegment(string.IsNullOrWhiteSpace(options.PackageName)
            ? "generated-site"
            : options.PackageName);

        return Path.GetFullPath(Path.Combine(baseDirectory, packageName));
    }

    private static string ResolveArchivePath(StaticSitePackageOptions options, string outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(options.ArchivePath))
        {
            return Path.GetFullPath(options.ArchivePath);
        }

        return $"{outputDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}.zip";
    }

    private static void WriteFile(string outputDirectory, string relativePath, string content, List<string> files)
    {
        var path = Path.Combine(outputDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        files.Add(path);
    }

    private static void WriteGeneratedComponentAssets(
        string outputDirectory,
        GeneratorSiteDocument document,
        List<string> files)
    {
        foreach (var component in document.ComponentLibrary.Components.Where(component => component.Generated))
        {
            WriteFile(
                outputDirectory,
                Path.Combine("components", "generated", $"{component.Type}.json"),
                JsonSerializer.Serialize(component, JsonOptions),
                files);
            WriteFile(
                outputDirectory,
                Path.Combine("components", "generated", $"{component.Type}.js"),
                BuildGeneratedComponentModule(component),
                files);
        }
    }

    private static string BuildGeneratedComponentModule(ComponentDefinition component)
    {
        var typeLiteral = JsonSerializer.Serialize(component.Type);
        var classNameLiteral = JsonSerializer.Serialize($"generated-section {ToKebabCase(component.Type)}");
        return $$"""
            export function render(node, helpers) {
              const section = helpers.element('section', {{classNameLiteral}});
              section.setAttribute('data-component', {{typeLiteral}});
              if (node.props?.source_selector) {
                section.setAttribute('data-source-selector', node.props.source_selector);
              }

              section.append(helpers.element('h2', '', node.props?.title || {{typeLiteral}}));
              if (node.props?.body) {
                section.append(helpers.element('p', '', node.props.body));
              }
              return section;
            }
            """;
    }

    private static string BuildIndexHtml(GeneratorSiteDocument document)
    {
        var title = EscapeHtml(string.IsNullOrWhiteSpace(document.Site.Title) ? "Generated Site" : document.Site.Title);
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>{{title}}</title>
              <link rel="stylesheet" href="./styles.css">
            </head>
            <body>
              <div id="app"></div>
              <script type="module" src="./runtime.js"></script>
            </body>
            </html>
            """;
    }

    private static string BuildStylesCss(GeneratorSiteDocument document)
    {
        var brand = document.Site.Theme.Colors.TryGetValue("brand", out var brandColor)
            ? brandColor
            : "#2454d6";
        var font = document.Site.Theme.Typography.TryGetValue("font_family", out var fontFamily)
            ? fontFamily
            : "Inter, system-ui, -apple-system, BlinkMacSystemFont, \"Segoe UI\", sans-serif";

        return $$"""
            :root {
              --brand: {{brand}};
              --ink: #18202f;
              --muted: #5d6678;
              --line: #d8deea;
              --surface: #ffffff;
              --band: #f4f7fb;
              font-family: {{font}};
            }

            * { box-sizing: border-box; }
            body { margin: 0; color: var(--ink); background: var(--surface); line-height: 1.6; }
            a { color: var(--brand); text-decoration: none; }
            a:hover { text-decoration: underline; }
            .site-shell { min-height: 100vh; }
            .site-header { position: sticky; top: 0; z-index: 2; background: rgba(255,255,255,.96); border-bottom: 1px solid var(--line); backdrop-filter: blur(10px); }
            .site-header-inner { max-width: 1180px; margin: 0 auto; padding: 10px 20px 12px; display: grid; grid-template-columns: auto minmax(0, 1fr); grid-template-areas: "brand utility" "brand primary"; gap: 8px 24px; align-items: center; }
            .brand { grid-area: brand; display: inline-flex; align-items: center; gap: 10px; font-weight: 700; color: var(--ink); white-space: nowrap; }
            .brand-logo { max-width: 220px; max-height: 58px; width: auto; height: auto; object-fit: contain; display: block; }
            .utility-links { grid-area: utility; justify-content: flex-end; font-size: 13px; color: var(--muted); }
            .primary-links { grid-area: primary; justify-content: flex-end; font-weight: 700; }
            .nav-links { display: flex; flex-wrap: wrap; gap: 8px 16px; font-size: 14px; }
            .mega-header .search-chip { justify-self: end; align-self: center; min-height: 34px; padding: 6px 10px; border: 1px solid var(--line); border-radius: 6px; color: var(--muted); font-size: 13px; }
            main { max-width: 1180px; margin: 0 auto; padding: 0 20px 48px; }
            .template-hero { display: grid; grid-template-columns: minmax(0, .86fr) minmax(320px, 1.14fr); gap: 28px; align-items: center; padding: 28px 0 34px; border-bottom: 1px solid var(--line); }
            .template-hero-copy { align-self: center; display: grid; gap: 12px; }
            .template-hero h1 { margin: 0; font-size: clamp(30px, 4.2vw, 56px); line-height: 1.08; letter-spacing: 0; }
            .template-hero p { margin: 0; color: var(--muted); font-size: 17px; }
            .template-hero-media { height: clamp(260px, 36vw, 430px); margin: 0; border-radius: 8px; overflow: hidden; background: var(--band); }
            .template-hero-media img { width: 100%; height: 100%; object-fit: cover; display: block; }
            .template-hero--visual-banner { grid-template-columns: 1fr; display: grid; padding-top: 0; }
            .template-hero--visual-banner .template-hero-media { width: 100%; height: clamp(320px, 42vw, 560px); border-radius: 0; }
            .template-hero--visual-banner .template-hero-copy { max-width: 720px; }
            .template-hero-slides { display: flex; gap: 12px; overflow-x: auto; padding-top: 12px; }
            .template-hero-slide { min-width: min(240px, 72vw); border: 1px solid var(--line); border-radius: 8px; padding: 10px; background: var(--surface); }
            .template-hero-slide h3 { margin: 0 0 4px; font-size: 15px; letter-spacing: 0; }
            .quick-link-ribbon { padding: 18px 0; border-bottom: 1px solid var(--line); }
            .quick-link-ribbon h2, .template-card-section h2, .content-article h1 { margin: 0 0 14px; font-size: 24px; letter-spacing: 0; }
            .quick-link-list { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 10px; }
            .quick-link-list a { min-height: 44px; display: flex; align-items: center; padding: 10px 12px; border: 1px solid var(--line); border-left: 4px solid var(--brand); border-radius: 6px; background: var(--surface); font-weight: 700; }
            .service-search-hero { padding: 32px 0; border-bottom: 1px solid var(--line); display: grid; grid-template-columns: minmax(0, .9fr) minmax(320px, 1.1fr); gap: 28px; align-items: center; }
            .service-search-copy { display: grid; gap: 10px; }
            .service-search-copy h1 { margin: 0; font-size: clamp(30px, 4vw, 52px); line-height: 1.08; letter-spacing: 0; }
            .service-search-copy p { margin: 0; color: var(--muted); font-size: 17px; }
            .service-search-panel { display: grid; gap: 14px; padding: 18px; border: 1px solid var(--line); border-radius: 8px; background: var(--band); }
            .service-search-input { width: 100%; min-height: 48px; padding: 10px 12px; border: 1px solid var(--line); border-radius: 6px; background: var(--surface); color: var(--muted); font: inherit; }
            .service-keywords, .service-hero-actions, .service-category-links, .tabbed-news-links { display: flex; flex-wrap: wrap; gap: 8px; }
            .service-keywords a, .service-hero-actions a, .service-category-links a, .tabbed-news-links a { min-height: 36px; display: inline-flex; align-items: center; padding: 7px 10px; border: 1px solid var(--line); border-radius: 6px; background: var(--surface); font-weight: 700; }
            .service-category-grid, .service-action-grid, .tabbed-news-board { padding: 30px 0; border-bottom: 1px solid var(--line); display: grid; gap: 16px; }
            .service-category-grid h2, .service-action-grid h2, .tabbed-news-board h2 { margin: 0; font-size: 24px; letter-spacing: 0; }
            .service-category-list, .service-action-list { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 14px; }
            .service-category-card, .service-action-card { border: 1px solid var(--line); border-radius: 8px; padding: 14px; background: var(--surface); display: grid; gap: 8px; align-content: start; }
            .service-category-card h3, .service-action-card h3, .tabbed-news-item h3 { margin: 0; font-size: 18px; letter-spacing: 0; }
            .service-category-card p, .tabbed-news-item p { margin: 0; color: var(--muted); }
            .service-action-card--primary { border-left: 4px solid var(--brand); }
            .tabbed-news-controls { display: flex; flex-wrap: wrap; gap: 0; border-bottom: 2px solid #a71483; }
            .tabbed-news-controls button { min-height: 44px; padding: 9px 18px; border: 0; border-radius: 0; background: var(--surface); color: var(--ink); font: inherit; font-weight: 700; }
            .tabbed-news-controls button[aria-selected="true"] { border-color: var(--brand); color: #fff; background: var(--brand); }
            .tabbed-news-panel[hidden] { display: none; }
            .tabbed-news-list { display: grid; gap: 0; }
            .tabbed-news-item { border: 0; border-bottom: 1px solid var(--line); border-radius: 0; padding: 12px 4px 12px 22px; background: var(--surface); display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 12px; align-items: baseline; position: relative; }
            .tabbed-news-item::before { content: ''; position: absolute; left: 4px; top: 1.05em; border-left: 7px solid #b8beca; border-top: 6px solid transparent; border-bottom: 6px solid transparent; }
            .tabbed-news-item p { justify-self: end; white-space: nowrap; }
            .search-box-panel, .showcase-hero { padding: 32px 0; border-bottom: 1px solid var(--line); display: grid; grid-template-columns: minmax(0, .9fr) minmax(300px, 1.1fr); gap: 24px; align-items: center; }
            .search-box-copy h1, .showcase-copy h1 { margin: 0 0 10px; font-size: clamp(30px, 4vw, 52px); line-height: 1.08; letter-spacing: 0; }
            .search-box-copy p, .showcase-copy p { margin: 0; color: var(--muted); font-size: 17px; }
            .search-box-input { width: 100%; min-height: 48px; padding: 10px 12px; border: 1px solid var(--line); border-radius: 6px; font: inherit; }
            .search-suggestions, .pagination-links, .dashboard-actions, .form-action-links, .showcase-actions, .cta-actions { display: flex; flex-wrap: wrap; gap: 8px; }
            .search-suggestions a, .pagination-links a, .dashboard-actions a, .form-action-links a, .showcase-actions a, .cta-actions a { min-height: 36px; display: inline-flex; align-items: center; padding: 7px 10px; border: 1px solid var(--line); border-radius: 6px; background: var(--surface); font-weight: 700; }
            .facet-filter-panel, .result-list, .dashboard-filter-bar, .metric-summary-grid, .chart-panel, .data-table-preview, .step-indicator, .structured-form-panel, .validation-summary, .form-action-bar, .proof-strip, .pricing-panel, .cta-band { padding: 28px 0; border-bottom: 1px solid var(--line); display: grid; gap: 14px; }
            .facet-filter-panel h2, .result-list h2, .dashboard-filter-bar h2, .metric-summary-grid h2, .chart-panel h2, .data-table-preview h2, .structured-form-panel h2, .validation-summary h2, .proof-strip h2, .pricing-panel h2, .cta-band h2 { margin: 0; font-size: 24px; letter-spacing: 0; }
            .facet-filter-list { display: flex; flex-wrap: wrap; gap: 8px; }
            .facet-filter-chip { display: inline-flex; align-items: center; min-height: 32px; padding: 5px 9px; border: 1px solid var(--line); border-radius: 999px; background: var(--band); color: var(--muted); font-size: 13px; }
            .result-list-items { display: grid; gap: 12px; }
            .metric-grid, .proof-list, .pricing-list { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 14px; }
            .metric-card, .proof-item, .pricing-card { border: 1px solid var(--line); border-radius: 8px; padding: 14px; background: var(--surface); display: grid; gap: 7px; align-content: start; }
            .metric-label { color: var(--muted); font-size: 13px; }
            .metric-value, .pricing-price { font-size: 28px; line-height: 1.1; }
            .chart-bars { display: grid; gap: 8px; }
            .chart-bar { display: grid; grid-template-columns: minmax(100px, .3fr) minmax(0, .7fr); gap: 10px; align-items: center; padding: 8px 10px; border: 1px solid var(--line); border-radius: 6px; background: var(--band); }
            .data-table-preview table { width: 100%; border-collapse: collapse; border: 1px solid var(--line); }
            .data-table-preview th, .data-table-preview td { padding: 9px 10px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: top; }
            .step-list { display: flex; flex-wrap: wrap; gap: 8px; padding: 0; margin: 0; list-style: none; }
            .step-item { min-height: 34px; display: inline-flex; align-items: center; padding: 6px 10px; border: 1px solid var(--line); border-radius: 999px; background: var(--surface); }
            .step-item--current { border-color: var(--brand); background: var(--brand); color: #fff; }
            .validation-list, .pricing-features { margin: 0; padding-left: 18px; }
            .product-card-grid .template-card { border-left: 4px solid var(--brand); }
            .proof-item strong { font-size: 24px; }
            .cta-band { padding: 34px 20px; border-radius: 8px; border: 1px solid var(--line); background: var(--band); margin: 28px 0; }
            .template-card-section { padding: 30px 0; border-bottom: 1px solid var(--line); display: grid; gap: 16px; }
            .template-card-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px; }
            .template-card-grid--carousel { display: flex; gap: 16px; overflow-x: auto; scroll-snap-type: x mandatory; padding-bottom: 8px; }
            .template-card-grid--carousel .template-card { min-width: min(320px, 82vw); scroll-snap-align: start; }
            .template-card { border: 1px solid var(--line); border-radius: 8px; overflow: hidden; background: var(--surface); min-height: 100%; display: flex; flex-direction: column; }
            .template-card img { width: 100%; aspect-ratio: 16 / 9; object-fit: cover; background: var(--band); }
            .template-card-body { padding: 14px; display: grid; gap: 7px; }
            .template-card h3 { margin: 0; font-size: 18px; letter-spacing: 0; }
            .template-card-title-link { color: var(--ink); }
            .template-card p { margin: 0; color: var(--muted); }
            .media-feature-grid .template-card { position: relative; border: 0; background: #10131a; min-height: 220px; }
            .media-feature-grid .template-card img { height: 100%; min-height: 220px; aspect-ratio: 4 / 3; }
            .media-feature-grid .template-card-body { position: absolute; inset: auto 0 0; padding: 54px 16px 14px; background: linear-gradient(180deg, transparent, rgba(0,0,0,.74)); color: #fff; }
            .media-feature-grid .template-card-title-link { color: #fff; }
            .media-feature-grid .template-card p { color: rgba(255,255,255,.84); }
            .content-article { padding: 32px 0; border-bottom: 1px solid var(--line); }
            .content-article p { color: var(--muted); max-width: 850px; }
            .content-article-media { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 280px)); justify-content: start; gap: 14px; margin-top: 18px; }
            .content-article-media img { width: 100%; aspect-ratio: 4 / 3; max-height: 180px; border-radius: 8px; object-fit: cover; background: var(--band); }
            .hero-section { padding: 72px 0 44px; border-bottom: 1px solid var(--line); }
            .hero-section h1 { max-width: 840px; margin: 0 0 16px; font-size: clamp(34px, 5vw, 64px); line-height: 1.05; letter-spacing: 0; }
            .hero-section p { max-width: 760px; margin: 0; color: var(--muted); font-size: 18px; }
            .content-section, .generated-section, .link-list, .form-block { padding: 32px 0; border-bottom: 1px solid var(--line); }
            .content-section h2, .generated-section h2, .link-list h2, .form-block h2 { margin: 0 0 10px; font-size: 24px; letter-spacing: 0; }
            .content-section p, .generated-section p { max-width: 840px; margin: 0; color: var(--muted); }
            .atomic-section { padding: 36px 0; border-bottom: 1px solid var(--line); display: grid; gap: 22px; }
            .atomic-section--hero { min-height: 440px; grid-template-columns: minmax(0, 1.05fr) minmax(280px, .95fr); align-items: center; gap: 34px; }
            .atomic-section--hero .image-block { order: 2; }
            .text-block h2 { margin: 0 0 12px; font-size: clamp(28px, 4vw, 52px); line-height: 1.08; letter-spacing: 0; }
            .text-block p { max-width: 760px; margin: 0; color: var(--muted); font-size: 17px; }
            .image-block { margin: 0; border-radius: 8px; overflow: hidden; background: var(--band); }
            .image-block img { display: block; max-width: 100%; }
            .atomic-section--standard .image-block { justify-self: start; width: min(260px, 100%); background: transparent; }
            .atomic-section--standard .image-block img { width: auto; height: auto; max-height: 160px; object-fit: contain; }
            .atomic-section--hero .image-block { min-height: 260px; }
            .atomic-section--hero .image-block img { width: 100%; height: 100%; min-height: 260px; object-fit: cover; }
            .button-link { justify-self: start; display: inline-flex; align-items: center; min-height: 42px; padding: 9px 15px; border-radius: 6px; border: 1px solid var(--brand); font-weight: 700; }
            .button-link--primary { color: #fff; background: var(--brand); }
            .button-link--secondary { color: var(--brand); background: var(--surface); }
            .card-grid-section { display: grid; gap: 16px; }
            .card-grid-section h2 { margin: 0; font-size: 24px; letter-spacing: 0; }
            .card-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(240px, 1fr)); gap: 16px; }
            .card-grid--carousel { display: flex; gap: 16px; overflow-x: auto; scroll-snap-type: x mandatory; padding-bottom: 8px; }
            .card-grid--carousel .feature-card { min-width: min(320px, 82vw); scroll-snap-align: start; }
            .feature-card { min-height: 100%; border: 1px solid var(--line); border-radius: 8px; overflow: hidden; background: var(--surface); display: flex; flex-direction: column; }
            .feature-card img { width: 100%; aspect-ratio: 16 / 9; object-fit: cover; background: var(--band); }
            .feature-card-body { padding: 16px; display: grid; gap: 8px; }
            .feature-card h3 { margin: 0; font-size: 18px; letter-spacing: 0; }
            .feature-card p { margin: 0; color: var(--muted); }
            .feature-card-link { font-weight: 700; }
            .link-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 10px; padding: 0; margin: 14px 0 0; list-style: none; }
            .link-grid a { display: block; min-height: 44px; padding: 10px 12px; border: 1px solid var(--line); background: var(--band); border-radius: 6px; }
            .form-fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; max-width: 780px; }
            .field label { display: block; margin-bottom: 4px; font-size: 13px; color: var(--muted); }
            .field input, .field textarea, .field select { width: 100%; min-height: 40px; border: 1px solid var(--line); border-radius: 6px; padding: 8px 10px; font: inherit; }
            .form-note { color: var(--muted); font-size: 13px; }
            .site-footer { padding: 26px 20px; border-top: 1px solid var(--line); background: var(--band); color: var(--muted); font-size: 13px; }
            .site-footer-inner { max-width: 1180px; margin: 0 auto; display: grid; grid-template-columns: auto minmax(0, 1fr); gap: 16px 24px; align-items: start; }
            .institution-footer { padding: 38px 20px; border-top: 0; background: #a71483; color: rgba(255,255,255,.92); font-size: 13px; }
            .institution-footer .site-footer-inner { grid-template-columns: auto minmax(0, 1fr); }
            .institution-footer a { color: #fff; }
            .institution-footer .footer-logo { filter: brightness(0) invert(1); opacity: .95; }
            .footer-logo { max-width: 180px; max-height: 72px; object-fit: contain; }
            .footer-links { display: flex; flex-wrap: wrap; gap: 8px 14px; margin-top: 10px; }
            .runtime-error { max-width: 760px; margin: 48px auto; padding: 18px; border: 1px solid #e5484d; color: #8f1d22; background: #fff7f7; }
            @media (max-width: 760px) {
              .site-header-inner { grid-template-columns: 1fr; grid-template-areas: "brand" "utility" "primary"; }
              .utility-links, .primary-links { justify-content: flex-start; }
              .template-hero { grid-template-columns: 1fr; }
              .service-search-hero { grid-template-columns: 1fr; }
              .template-hero-media { height: clamp(220px, 58vw, 320px); }
              .template-hero--visual-banner .template-hero-media { height: auto; }
              .template-hero--visual-banner .template-hero-media img { height: auto; object-fit: contain; }
              .atomic-section--hero { grid-template-columns: 1fr; min-height: 0; }
              .atomic-section--hero .image-block { order: 0; }
            }
            """;
    }

    private static string BuildReadme(GeneratorSiteDocument document)
    {
        return $$"""
            # {{document.Site.Title}}

            This package is rendered from `site.json` through `runtime.js`.
            `index.html` is only the entry shell.

            The generated website uses only components declared in `components/manifest.json`.
            Generated local component definitions, if any, are under `components/generated/`.

            Source URL: {{document.Site.SourceUrl}}
            """;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalidChars.Contains(ch) ? '-' : ch).ToArray();
        var sanitized = new string(chars).Trim('.', ' ', '-');
        return string.IsNullOrWhiteSpace(sanitized) ? "generated-site" : sanitized;
    }

    private static string EscapeHtml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "generated-component";
        }

        var chars = new List<char>();
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (char.IsUpper(ch) && index > 0)
            {
                chars.Add('-');
            }

            chars.Add(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-');
        }

        return string.Join(
            '-',
            new string(chars.ToArray()).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private const string RuntimeJavaScript = """
        const app = document.getElementById('app');

        const componentRenderers = {
          PageShell: renderPageShell,
          SiteHeader: renderSiteHeader,
          MegaHeader: renderMegaHeader,
          HeroSection: renderHeroSection,
          HeroCarousel: renderHeroCarousel,
          HeroBanner: renderHeroBanner,
          ContentSection: renderContentSection,
          LinkList: renderLinkList,
          FormBlock: renderFormBlock,
          SiteFooter: renderSiteFooter,
          InstitutionFooter: renderInstitutionFooter,
          QuickLinkRibbon: renderQuickLinkRibbon,
          ServiceSearchHero: renderServiceSearchHero,
          ServiceCategoryGrid: renderServiceCategoryGrid,
          ServiceActionGrid: renderServiceActionGrid,
          TabbedNewsBoard: renderTabbedNewsBoard,
          SearchBoxPanel: renderSearchBoxPanel,
          FacetFilterPanel: renderFacetFilterPanel,
          ResultList: renderResultList,
          PaginationNav: renderPaginationNav,
          DashboardFilterBar: renderDashboardFilterBar,
          MetricSummaryGrid: renderMetricSummaryGrid,
          ChartPanel: renderChartPanel,
          DataTablePreview: renderDataTablePreview,
          StepIndicator: renderStepIndicator,
          StructuredFormPanel: renderStructuredFormPanel,
          ValidationSummary: renderValidationSummary,
          FormActionBar: renderFormActionBar,
          ShowcaseHero: renderShowcaseHero,
          ProductCardGrid: renderProductCardGrid,
          ProofStrip: renderProofStrip,
          PricingPanel: renderPricingPanel,
          CtaBand: renderCtaBand,
          NewsCardCarousel: renderNewsCardCarousel,
          NewsGrid: renderNewsGrid,
          MediaFeatureGrid: renderMediaFeatureGrid,
          ArticleList: renderArticleList,
          ContentArticle: renderContentArticle,
          AtomicSection: renderAtomicSection,
          TextBlock: renderTextBlock,
          ImageBlock: renderImageBlock,
          ButtonLink: renderButtonLink,
          CardGrid: renderCardGrid,
          FeatureCard: renderFeatureCard
        };

        const site = await fetch('./site.json').then(response => response.json());
        const manifest = await fetch('./components/manifest.json').then(response => response.json());
        let currentRoute = resolveRoute(site.routes);
        const sourceRouteMap = buildSourceRouteMap(site.routes);
        const knownTypes = new Set((manifest.components || []).map(component => component.type));
        const generatedRenderers = await loadGeneratedRenderers(manifest);

        renderCurrentRoute();
        window.addEventListener('popstate', () => {
          currentRoute = resolveRoute(site.routes);
          renderCurrentRoute();
        });
        window.addEventListener('hashchange', () => {
          currentRoute = resolveRoute(site.routes);
          renderCurrentRoute();
        });

        function renderCurrentRoute() {
          try {
            app.replaceChildren(renderNode(currentRoute.root, knownTypes, manifest));
          } catch (error) {
            const box = document.createElement('div');
            box.className = 'runtime-error';
            box.textContent = error.message;
            app.replaceChildren(box);
          }
        }

        function resolveRoute(routes) {
          const path = routePathFromLocation(routes);
          return routes.find(route => route.path === path) || routes[0];
        }

        function routePathFromLocation(routes) {
          const hash = window.location.hash || '';
          if (hash.startsWith('#/')) {
            return normalizeInternalRoute(hash.slice(1));
          }

          const path = normalizeInternalRoute(window.location.pathname || '/');
          return (routes || []).some(route => route.path === path) ? path : '/';
        }

        function navigateToRoute(path) {
          const nextRoute = site.routes.find(route => route.path === path);
          if (!nextRoute) return false;

          currentRoute = nextRoute;
          const nextHref = toStaticHref(path);
          if (window.location.hash !== nextHref) {
            history.pushState({ path }, '', nextHref);
          }
          renderCurrentRoute();
          window.scrollTo({ top: 0, behavior: 'smooth' });
          return true;
        }

        function buildSourceRouteMap(routes) {
          const map = new Map();
          for (const route of routes || []) {
            const sourceUrl = route.root?.props?.source_url || route.source_url || '';
            const normalized = normalizeSourceUrl(sourceUrl);
            if (normalized && route.path && !map.has(normalized)) {
              map.set(normalized, route.path);
            }
          }
          return map;
        }

        function renderNode(node, knownTypes, manifest) {
          if (!knownTypes.has(node.type)) {
            throw new Error(`Unknown component type: ${node.type}`);
          }

          const renderer = componentRenderers[node.type];
          if (renderer) {
            return renderer(node, knownTypes, manifest);
          }

          const generatedRenderer = generatedRenderers.get(node.type);
          if (generatedRenderer) {
            return generatedRenderer(node, {
              element,
              renderChildren: (childNode, parent) => renderChildren(childNode, parent, knownTypes, manifest)
            });
          }

          return renderGeneratedComponent(node);
        }

        async function loadGeneratedRenderers(manifest) {
          const renderers = new Map();
          for (const component of manifest.components || []) {
            if (!component.generated) continue;
            try {
              const module = await import(`./components/generated/${component.type}.js`);
              if (typeof module.render === 'function') {
                renderers.set(component.type, module.render);
              }
            } catch (error) {
              console.warn(`Generated component renderer unavailable: ${component.type}`, error);
            }
          }
          return renderers;
        }

        function renderChildren(node, parent, knownTypes, manifest) {
          for (const child of node.children || []) {
            parent.appendChild(renderNode(child, knownTypes, manifest));
          }
        }

        function renderPageShell(node, knownTypes, manifest) {
          const shell = element('div', 'site-shell');
          const main = element('main');
          for (const child of node.children || []) {
            const rendered = renderNode(child, knownTypes, manifest);
            if (child.type === 'SiteHeader' || child.type === 'MegaHeader' || child.type === 'SiteFooter' || child.type === 'InstitutionFooter') {
              shell.appendChild(rendered);
            } else {
              main.appendChild(rendered);
            }
          }
          const footer = shell.querySelector('.site-footer, .institution-footer');
          if (footer) {
            shell.insertBefore(main, footer);
          } else {
            shell.appendChild(main);
          }
          return shell;
        }

        function renderSiteHeader(node) {
          const header = element('header', 'site-header');
          const inner = element('div', 'site-header-inner');
          const brand = element('a', 'brand', node.props?.title || 'Generated Site');
          brand.href = toStaticHref('/');
          brand.setAttribute('data-local-route', '/');
          brand.addEventListener('click', event => {
            event.preventDefault();
            navigateToRoute('/');
          });
          if (node.props?.logo_url) {
            brand.textContent = '';
            const logo = document.createElement('img');
            logo.className = 'brand-logo';
            logo.src = node.props.logo_url;
            logo.alt = node.props.logo_alt || node.props?.title || 'Site logo';
            brand.appendChild(logo);
          }
          const utility = renderLinkSet(node.props?.utility_links || [], 'nav', 'nav-links utility-links');
          const primaryLinks = node.props?.primary_links?.length ? node.props.primary_links : node.props?.links || [];
          const primary = renderLinkSet(primaryLinks, 'nav', 'nav-links primary-links');
          inner.append(brand, utility, primary);
          header.appendChild(inner);
          return header;
        }

        function renderMegaHeader(node) {
          const header = element('header', 'site-header mega-header');
          const inner = element('div', 'site-header-inner');
          const brand = element('a', 'brand', node.props?.title || 'Generated Site');
          brand.href = toStaticHref('/');
          brand.setAttribute('data-local-route', '/');
          brand.addEventListener('click', event => {
            event.preventDefault();
            navigateToRoute('/');
          });
          if (node.props?.logo_url) {
            brand.textContent = '';
            const logo = document.createElement('img');
            logo.className = 'brand-logo';
            logo.src = node.props.logo_url;
            logo.alt = node.props.logo_alt || node.props?.title || 'Site logo';
            brand.appendChild(logo);
          }
          const utility = renderLinkSet(node.props?.utility_links || [], 'nav', 'nav-links utility-links');
          const primary = renderLinkSet(node.props?.primary_links || [], 'nav', 'nav-links primary-links');
          inner.append(brand, utility, primary);
          if (node.props?.search_enabled) {
            primary.appendChild(element('span', 'search-chip', 'Search'));
          }
          header.appendChild(inner);
          return header;
        }

        function renderHeroSection(node) {
          const section = element('section', 'hero-section');
          section.append(element('h1', '', node.props?.title || ''));
          if (node.props?.body) section.append(element('p', '', node.props.body));
          return section;
        }

        function renderHeroCarousel(node) {
          const slides = node.props?.slides || [];
          const primary = slides.find(slide => slide.media_url) || slides[0] || {};
          const hasCopy = hasMeaningfulHeroText(node.props?.title, primary.title, node.props?.body, primary.body);
          const visualBanner = primary.media_url && !hasCopy;
          const section = element('section', `template-hero template-hero--carousel${visualBanner ? ' template-hero--visual-banner' : ''}`);
          if (hasCopy) {
            const copy = element('div', 'template-hero-copy');
            const title = meaningfulText(node.props?.title, primary.title);
            const body = meaningfulText(node.props?.body, primary.body);
            if (title) copy.append(element('h1', '', title));
            if (body) copy.append(element('p', '', body));
            section.appendChild(copy);
          }
          if (!visualBanner && slides.length > 1) {
            const strip = element('div', 'template-hero-slides');
            for (const slide of slides.slice(0, 8)) {
              const slideTitle = meaningfulText(slide.title);
              if (!slideTitle && !slide.url) continue;
              const card = element('article', 'template-hero-slide');
              if (slideTitle) card.append(element('h3', '', slideTitle));
              if (slide.url) {
                const anchor = element('a', '', slideTitle || 'More');
                configureLink(anchor, slide);
                card.append(anchor);
              }
              strip.appendChild(card);
            }
            if (strip.children.length) section.appendChild(strip);
          }
          section.appendChild(renderHeroMedia(primary.media_url, primary.media_alt || primary.title || ''));
          return section;
        }

        function renderHeroBanner(node) {
          const section = element('section', 'template-hero template-hero--banner');
          const copy = element('div', 'template-hero-copy');
          copy.append(element('h1', '', node.props?.title || ''));
          if (node.props?.body) copy.append(element('p', '', node.props.body));
          section.appendChild(copy);
          section.appendChild(renderHeroMedia(node.props?.media_url || '', node.props?.media_alt || ''));
          return section;
        }

        function hasMeaningfulHeroText(...values) {
          return values.some(value => Boolean(meaningfulText(value)));
        }

        function meaningfulText(...values) {
          for (const value of values) {
            const text = String(value || '').trim();
            if (text && !isControlText(text)) return text;
          }
          return '';
        }

        function isControlText(value) {
          const text = String(value || '').trim();
          if (!text || text.length > 96) return false;
          return text.replace(/[\s.。·•\-–—_:/\\|<>\[\]\(\){}‹›«»]+/g, '').length === 0;
        }

        function renderHeroMedia(url, alt) {
          const figure = element('figure', 'template-hero-media');
          if (url) {
            const img = document.createElement('img');
            img.src = url;
            img.alt = alt || '';
            img.loading = 'eager';
            figure.appendChild(img);
          }
          return figure;
        }

        function renderContentSection(node) {
          const section = element('section', 'content-section');
          section.append(element('h2', '', node.props?.title || 'Section'));
          if (node.props?.body) section.append(element('p', '', node.props.body));
          return section;
        }

        function renderQuickLinkRibbon(node) {
          const section = element('section', 'quick-link-ribbon');
          if (node.props?.title) section.append(element('h2', '', node.props.title));
          const links = element('div', 'quick-link-list');
          for (const link of node.props?.links || []) {
            const anchor = element('a', '', link.label || link.url);
            configureLink(anchor, link);
            links.appendChild(anchor);
          }
          section.appendChild(links);
          return section;
        }

        function renderServiceSearchHero(node) {
          const section = element('section', 'service-search-hero');
          const copy = element('div', 'service-search-copy');
          copy.append(element('h1', '', node.props?.title || 'Search'));
          if (node.props?.body) copy.append(element('p', '', node.props.body));
          const panel = element('div', 'service-search-panel');
          const input = document.createElement('input');
          input.className = 'service-search-input';
          input.type = 'search';
          input.placeholder = node.props?.query_placeholder || 'Search';
          input.disabled = true;
          panel.appendChild(input);
          panel.appendChild(renderLinkSet(node.props?.hot_keywords || [], 'div', 'service-keywords'));
          panel.appendChild(renderLinkSet(node.props?.actions || [], 'div', 'service-hero-actions'));
          section.append(copy, panel);
          return section;
        }

        function renderServiceCategoryGrid(node) {
          const section = element('section', 'service-category-grid');
          if (node.props?.title) section.append(element('h2', '', node.props.title));
          const grid = element('div', 'service-category-list');
          for (const category of node.props?.categories || []) {
            const card = element('article', 'service-category-card');
            card.append(element('h3', '', category.title || 'Service'));
            if (category.body) card.append(element('p', '', category.body));
            card.appendChild(renderLinkSet(category.links || [], 'div', 'service-category-links'));
            grid.appendChild(card);
          }
          section.appendChild(grid);
          return section;
        }

        function renderServiceActionGrid(node) {
          const section = element('section', 'service-action-grid');
          if (node.props?.title) section.append(element('h2', '', node.props.title));
          const grid = element('div', 'service-action-list');
          for (const action of node.props?.actions || []) {
            const card = element('article', `service-action-card service-action-card--${action.kind || 'secondary'}`);
            const label = action.label || action.url || '';
            const heading = element('h3');
            if (action.url && label) {
              const anchor = element('a', 'template-card-title-link', label);
              configureLink(anchor, action);
              heading.appendChild(anchor);
            } else {
              heading.textContent = label;
            }
            card.append(heading);
            grid.appendChild(card);
          }
          section.appendChild(grid);
          return section;
        }

        function renderTabbedNewsBoard(node) {
          const section = element('section', 'tabbed-news-board');
          if (node.props?.title) section.append(element('h2', '', node.props.title));
          const tabs = node.props?.tabs || [];
          const controls = element('div', 'tabbed-news-controls');
          const panels = element('div', 'tabbed-news-panels');
          tabs.forEach((tab, index) => {
            const panelId = `${node.id || 'tabbed-news'}-${index}`;
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = tab.label || `Tab ${index + 1}`;
            button.setAttribute('aria-selected', index === 0 ? 'true' : 'false');
            button.setAttribute('aria-controls', panelId);
            const panel = element('div', 'tabbed-news-panel');
            panel.id = panelId;
            if (index !== 0) panel.hidden = true;
            const list = element('div', 'tabbed-news-list');
            for (const item of tab.items || []) {
              const article = element('article', 'tabbed-news-item');
              const title = item.title || '';
              const heading = element('h3');
              if (item.url && title) {
                const anchor = element('a', 'template-card-title-link', title);
                configureLink(anchor, item);
                heading.appendChild(anchor);
              } else {
                heading.textContent = title;
              }
              article.append(heading);
              if (item.body) article.append(element('p', '', item.body));
              list.appendChild(article);
            }
            panel.appendChild(list);
            button.addEventListener('click', () => {
              [...controls.children].forEach(child => child.setAttribute('aria-selected', 'false'));
              [...panels.children].forEach(child => { child.hidden = true; });
              button.setAttribute('aria-selected', 'true');
              panel.hidden = false;
            });
            controls.appendChild(button);
            panels.appendChild(panel);
          });
          section.append(controls, panels);
          return section;
        }

        function renderSearchBoxPanel(node) {
          const section = element('section', 'search-box-panel');
          const copy = element('div', 'search-box-copy');
          copy.append(element('h1', '', node.props?.title || 'Search'));
          if (node.props?.body) copy.append(element('p', '', node.props.body));
          const input = document.createElement('input');
          input.className = 'search-box-input';
          input.type = 'search';
          input.placeholder = node.props?.query_placeholder || 'Search';
          input.disabled = true;
          section.append(copy, input, renderLinkSet(node.props?.suggestions || [], 'div', 'search-suggestions'));
          return section;
        }

        function renderFacetFilterPanel(node) {
          const aside = element('aside', 'facet-filter-panel');
          aside.append(element('h2', '', node.props?.title || 'Filters'));
          const list = element('div', 'facet-filter-list');
          for (const filter of node.props?.filters || []) {
            const chip = element('span', 'facet-filter-chip', filter.count ? `${filter.label} (${filter.count})` : filter.label || filter.value || 'Filter');
            list.appendChild(chip);
          }
          aside.appendChild(list);
          return aside;
        }

        function renderResultList(node) {
          const section = element('section', 'result-list');
          section.append(element('h2', '', node.props?.title || 'Results'));
          if (node.props?.summary) section.append(element('p', 'result-summary', node.props.summary));
          const list = element('div', 'result-list-items');
          for (const item of node.props?.items || []) {
            list.appendChild(renderTemplateCard(item));
          }
          section.appendChild(list);
          return section;
        }

        function renderPaginationNav(node) {
          const nav = element('nav', 'pagination-nav');
          nav.setAttribute('aria-label', 'Pagination');
          nav.appendChild(renderLinkSet(node.props?.links || [], 'div', 'pagination-links'));
          return nav;
        }

        function renderDashboardFilterBar(node) {
          const section = element('section', 'dashboard-filter-bar');
          section.append(element('h2', '', node.props?.title || 'Filters'));
          section.appendChild(renderFacetFilterPanel({ props: { title: '', filters: node.props?.filters || [] } }));
          section.appendChild(renderLinkSet(node.props?.actions || [], 'div', 'dashboard-actions'));
          return section;
        }

        function renderMetricSummaryGrid(node) {
          const section = element('section', 'metric-summary-grid');
          if (node.props?.title) section.append(element('h2', '', node.props.title));
          const grid = element('div', 'metric-grid');
          for (const metric of node.props?.metrics || []) {
            const card = element('article', 'metric-card');
            card.append(element('span', 'metric-label', metric.label || 'Metric'));
            card.append(element('strong', 'metric-value', metric.value || ''));
            if (metric.detail) card.append(element('p', '', metric.detail));
            grid.appendChild(card);
          }
          section.appendChild(grid);
          return section;
        }

        function renderChartPanel(node) {
          const section = element('section', 'chart-panel');
          section.append(element('h2', '', node.props?.title || 'Chart'));
          if (node.props?.body) section.append(element('p', '', node.props.body));
          const bars = element('div', 'chart-bars');
          for (const point of node.props?.series || []) {
            const bar = element('div', 'chart-bar');
            bar.append(element('span', '', point.label || 'Value'));
            bar.append(element('strong', '', point.value || ''));
            bars.appendChild(bar);
          }
          section.appendChild(bars);
          return section;
        }

        function renderDataTablePreview(node) {
          const section = element('section', 'data-table-preview');
          section.append(element('h2', '', node.props?.title || 'Data'));
          const table = document.createElement('table');
          const thead = document.createElement('thead');
          const headRow = document.createElement('tr');
          for (const column of node.props?.columns || []) headRow.append(element('th', '', column));
          thead.appendChild(headRow);
          const tbody = document.createElement('tbody');
          for (const row of node.props?.rows || []) {
            const tr = document.createElement('tr');
            for (const cell of row.cells || []) tr.append(element('td', '', cell));
            tbody.appendChild(tr);
          }
          table.append(thead, tbody);
          section.appendChild(table);
          return section;
        }

        function renderStepIndicator(node) {
          const nav = element('nav', 'step-indicator');
          nav.setAttribute('aria-label', 'Progress');
          const list = element('ol', 'step-list');
          for (const step of node.props?.steps || []) {
            const item = element('li', `step-item step-item--${step.status || 'upcoming'}`, step.label || 'Step');
            list.appendChild(item);
          }
          nav.appendChild(list);
          return nav;
        }

        function renderStructuredFormPanel(node) {
          const section = element('section', 'structured-form-panel');
          section.append(element('h2', '', node.props?.title || 'Form'));
          const fields = element('div', 'form-fields');
          for (const field of node.props?.fields || []) {
            const wrap = element('div', 'field');
            wrap.append(element('label', '', field.label || field.name || 'Field'));
            const input = document.createElement(field.type === 'textarea' ? 'textarea' : 'input');
            if (input.tagName === 'INPUT') input.type = field.type || 'text';
            input.name = field.name || '';
            input.required = Boolean(field.required);
            input.disabled = true;
            wrap.appendChild(input);
            fields.appendChild(wrap);
          }
          section.append(fields, element('p', 'form-note', 'Form is shown as a non-submitting reconstruction.'));
          return section;
        }

        function renderValidationSummary(node) {
          const section = element('section', 'validation-summary');
          section.append(element('h2', '', node.props?.title || 'Notice'));
          const list = element('ul', 'validation-list');
          for (const message of node.props?.messages || []) list.append(element('li', '', message));
          section.appendChild(list);
          return section;
        }

        function renderFormActionBar(node) {
          const section = element('section', 'form-action-bar');
          section.appendChild(renderLinkSet(node.props?.actions || [], 'div', 'form-action-links'));
          return section;
        }

        function renderShowcaseHero(node) {
          const section = element('section', 'showcase-hero');
          const copy = element('div', 'showcase-copy');
          copy.append(element('h1', '', node.props?.title || ''));
          if (node.props?.body) copy.append(element('p', '', node.props.body));
          copy.appendChild(renderLinkSet(node.props?.actions || [], 'div', 'showcase-actions'));
          section.appendChild(copy);
          section.appendChild(renderHeroMedia(node.props?.media_url || '', node.props?.media_alt || ''));
          return section;
        }

        function renderProductCardGrid(node) {
          return renderTemplateCardSection(node, 'product-card-grid', 'grid');
        }

        function renderProofStrip(node) {
          const section = element('section', 'proof-strip');
          if (node.props?.title) section.append(element('h2', '', node.props.title));
          const grid = element('div', 'proof-list');
          for (const item of node.props?.items || []) {
            const proof = element('article', 'proof-item');
            proof.append(element('strong', '', item.value || item.label || ''));
            proof.append(element('span', '', item.label || ''));
            if (item.detail) proof.append(element('p', '', item.detail));
            grid.appendChild(proof);
          }
          section.appendChild(grid);
          return section;
        }

        function renderPricingPanel(node) {
          const section = element('section', 'pricing-panel');
          if (node.props?.title) section.append(element('h2', '', node.props.title));
          const grid = element('div', 'pricing-list');
          for (const plan of node.props?.plans || []) {
            const card = element('article', 'pricing-card');
            card.append(element('h3', '', plan.title || 'Plan'));
            if (plan.price) card.append(element('strong', 'pricing-price', plan.price));
            if (plan.body) card.append(element('p', '', plan.body));
            const features = element('ul', 'pricing-features');
            for (const feature of plan.features || []) features.append(element('li', '', feature));
            card.appendChild(features);
            if (plan.action) {
              const anchor = element('a', `button-link button-link--${plan.action.kind || 'primary'}`, plan.action.label || 'Choose');
              configureLink(anchor, plan.action);
              card.appendChild(anchor);
            }
            grid.appendChild(card);
          }
          section.appendChild(grid);
          return section;
        }

        function renderCtaBand(node) {
          const section = element('section', 'cta-band');
          section.append(element('h2', '', node.props?.title || ''));
          if (node.props?.body) section.append(element('p', '', node.props.body));
          section.appendChild(renderLinkSet(node.props?.actions || [], 'div', 'cta-actions'));
          return section;
        }

        function renderNewsCardCarousel(node) {
          return renderTemplateCardSection(node, 'news-card-carousel', 'carousel');
        }

        function renderNewsGrid(node) {
          return renderTemplateCardSection(node, 'news-grid', 'grid');
        }

        function renderMediaFeatureGrid(node) {
          return renderTemplateCardSection(node, 'media-feature-grid', 'grid');
        }

        function renderArticleList(node) {
          return renderTemplateCardSection(node, 'article-list', 'list');
        }

        function renderTemplateCardSection(node, className, layout) {
          const section = element('section', `template-card-section ${className}`);
          if (node.props?.title) section.append(element('h2', '', node.props.title));
          const gridClass = layout === 'carousel'
            ? 'template-card-grid template-card-grid--carousel'
            : 'template-card-grid template-card-grid--grid';
          const grid = element('div', gridClass);
          for (const item of node.props?.items || []) {
            grid.appendChild(renderTemplateCard(item));
          }
          section.appendChild(grid);
          return section;
        }

        function renderTemplateCard(item) {
          const card = element('article', 'template-card');
          if (item.media_url) {
            const img = document.createElement('img');
            img.src = item.media_url;
            img.alt = item.media_alt || '';
            img.loading = 'eager';
            card.appendChild(img);
          }
          const body = element('div', 'template-card-body');
          const title = item.title || '';
          const heading = element('h3');
          if (item.url && title) {
            const anchor = element('a', 'template-card-title-link', title);
            configureLink(anchor, item);
            heading.appendChild(anchor);
          } else {
            heading.textContent = title;
          }
          body.append(heading);
          if (item.body) body.append(element('p', '', item.body));
          card.appendChild(body);
          return card;
        }

        function renderContentArticle(node) {
          const article = element('article', 'content-article');
          article.append(element('h1', '', node.props?.title || ''));
          if (node.props?.body) article.append(element('p', '', node.props.body));
          const media = element('div', 'content-article-media');
          for (const item of node.props?.media || []) {
            if (!item.url) continue;
            const img = document.createElement('img');
            img.src = item.url;
            img.alt = item.alt || '';
            img.loading = 'eager';
            media.appendChild(img);
          }
          if (media.children.length) article.appendChild(media);
          return article;
        }

        function renderAtomicSection(node, knownTypes, manifest) {
          const section = element('section', `atomic-section atomic-section--${node.props?.variant || 'standard'}`);
          if (node.props?.source_selector) section.dataset.sourceSelector = node.props.source_selector;
          renderChildren(node, section, knownTypes, manifest);
          return section;
        }

        function renderTextBlock(node) {
          const block = element('div', 'text-block');
          if (node.props?.title) block.append(element('h2', '', node.props.title));
          if (node.props?.body) block.append(element('p', '', node.props.body));
          return block;
        }

        function renderImageBlock(node) {
          const figure = element('figure', 'image-block');
          const img = document.createElement('img');
          img.src = node.props?.url || '';
          img.alt = node.props?.alt || '';
          img.loading = 'eager';
          figure.appendChild(img);
          return figure;
        }

        function renderButtonLink(node) {
          const link = {
            label: node.props?.label || 'Open',
            url: node.props?.url || '#',
            scope: node.props?.url?.startsWith('/') ? 'internal' : 'external'
          };
          const anchor = element('a', `button-link button-link--${node.props?.kind || 'secondary'}`, link.label);
          configureLink(anchor, link);
          return anchor;
        }

        function renderCardGrid(node, knownTypes, manifest) {
          const wrap = element('section', 'card-grid-section');
          if (node.props?.title) wrap.append(element('h2', '', node.props.title));
          const layout = node.props?.layout === 'carousel' ? 'carousel' : 'grid';
          const gridClass = layout === 'carousel' ? 'card-grid card-grid--carousel' : 'card-grid card-grid--grid';
          const grid = element('div', gridClass);
          if (layout === 'carousel') {
            grid.setAttribute('aria-label', node.props?.title || 'Carousel');
          }
          renderChildren(node, grid, knownTypes, manifest);
          wrap.appendChild(grid);
          return wrap;
        }

        function renderFeatureCard(node) {
          const card = element('article', 'feature-card');
          if (node.props?.media_url) {
            const img = document.createElement('img');
            img.src = node.props.media_url;
            img.alt = node.props.media_alt || '';
            img.loading = 'eager';
            card.appendChild(img);
          }
          const body = element('div', 'feature-card-body');
          const title = node.props?.title || '';
          const heading = element('h3');
          if (node.props?.url && title) {
            const link = { label: title, url: node.props.url, scope: node.props.url.startsWith('/') ? 'internal' : 'external' };
            const anchor = element('a', 'feature-card-link', link.label);
            configureLink(anchor, link);
            heading.appendChild(anchor);
          } else {
            heading.textContent = title;
          }
          body.append(heading);
          if (node.props?.body) body.append(element('p', '', node.props.body));
          card.appendChild(body);
          return card;
        }

        function renderGeneratedComponent(node) {
          const section = element('section', 'generated-section');
          section.dataset.component = node.type;
          section.append(element('h2', '', node.props?.title || node.type));
          if (node.props?.body) section.append(element('p', '', node.props.body));
          return section;
        }

        function renderLinkList(node) {
          const section = element('section', 'link-list');
          section.append(element('h2', '', node.props?.title || 'Links'));
          const list = element('ul', 'link-grid');
          for (const link of node.props?.links || []) {
            const item = element('li');
            const a = element('a', '', link.label || link.url);
            configureLink(a, link);
            item.appendChild(a);
            list.appendChild(item);
          }
          section.appendChild(list);
          return section;
        }

        function renderFormBlock(node) {
          const section = element('section', 'form-block');
          section.append(element('h2', '', 'Form'));
          const fields = element('div', 'form-fields');
          for (const field of node.props?.fields || []) {
            const wrap = element('div', 'field');
            wrap.append(element('label', '', field.label || field.name || 'Field'));
            const input = document.createElement(field.type === 'textarea' ? 'textarea' : 'input');
            if (input.tagName === 'INPUT') input.type = field.type || 'text';
            input.name = field.name || '';
            input.required = Boolean(field.required);
            input.disabled = true;
            wrap.appendChild(input);
            fields.appendChild(wrap);
          }
          section.append(fields, element('p', 'form-note', 'Form is shown as a non-submitting placeholder in the reconstructed package.'));
          return section;
        }

        function renderSiteFooter(node) {
          const footer = element('footer', 'site-footer');
          const inner = element('div', 'site-footer-inner');
          if (node.props?.logo_url) {
            const logo = document.createElement('img');
            logo.className = 'footer-logo';
            logo.src = node.props.logo_url;
            logo.alt = node.props.logo_alt || 'Footer logo';
            inner.appendChild(logo);
          }
          const content = element('div', 'footer-content');
          if (node.props?.contact_text) content.append(element('div', '', node.props.contact_text));
          content.append(element('div', '', node.props?.notice || 'Generated reference package.'));
          const footerLinks = renderLinkSet(node.props?.links || [], 'div', 'footer-links');
          content.appendChild(footerLinks);
          inner.appendChild(content);
          footer.appendChild(inner);
          return footer;
        }

        function renderInstitutionFooter(node) {
          const footer = element('footer', 'institution-footer');
          const inner = element('div', 'site-footer-inner');
          if (node.props?.logo_url) {
            const logo = document.createElement('img');
            logo.className = 'footer-logo';
            logo.src = node.props.logo_url;
            logo.alt = node.props.logo_alt || 'Footer logo';
            inner.appendChild(logo);
          }
          const content = element('div', 'footer-content');
          if (node.props?.contact_text) content.append(element('div', '', node.props.contact_text));
          const footerLinks = renderLinkSet(node.props?.links || [], 'div', 'footer-links');
          content.appendChild(footerLinks);
          inner.appendChild(content);
          footer.appendChild(inner);
          return footer;
        }

        function renderLinkSet(links, tag, className) {
          const container = element(tag, className);
          for (const link of links || []) {
            const a = element('a', '', link.label || link.url);
            configureLink(a, link);
            container.appendChild(a);
          }
          return container;
        }

        function element(tag, className = '', text = '') {
          const node = document.createElement(tag);
          if (className) node.className = className;
          if (text) node.textContent = text;
          return node;
        }

        function configureLink(anchor, link) {
          const originalUrl = link.url || '#';
          const sourceUrl = link.source_url || originalUrl;
          const mappedRoute = sourceRouteMap.get(normalizeSourceUrl(sourceUrl)) || sourceRouteMap.get(normalizeSourceUrl(originalUrl));
          const url = normalizeInternalRoute(mappedRoute || originalUrl);
          if (mappedRoute || (link.scope === 'internal' && url.startsWith('/'))) {
            anchor.href = toStaticHref(url);
            anchor.setAttribute('data-local-route', url);
            anchor.addEventListener('click', event => {
              if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) return;
              event.preventDefault();
              navigateToRoute(url);
            });
            return;
          }

          if (link.scope === 'external' || /^https?:\/\//i.test(originalUrl)) {
            anchor.removeAttribute('href');
            anchor.dataset.sourceUrl = sourceUrl;
            anchor.setAttribute('aria-disabled', 'true');
            anchor.setAttribute('tabindex', '-1');
            anchor.addEventListener('click', event => event.preventDefault());
            return;
          }

          anchor.href = toStaticHref(url);
        }

        function normalizeInternalRoute(url) {
          if (!url?.startsWith('/')) return url || '#';
          return url
            .replace(/\/index$/i, '/')
            .replace(/\.(aspx|php|html|htm)(?=\/|$|\?)/i, '')
            .replace(/[?&=]+/g, '-');
        }

        function toStaticHref(url) {
          if (!url?.startsWith('/')) return url || '#';
          return `#${url}`;
        }

        function normalizeSourceUrl(value) {
          if (!value) return '';
          try {
            const parsed = new URL(value, window.location.origin);
            parsed.hash = '';
            return parsed.href;
          } catch {
            return '';
          }
        }
        """;
}
