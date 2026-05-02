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

    public StaticSitePackageGenerator()
        : this(new ComponentSchemaValidator())
    {
    }

    public StaticSitePackageGenerator(ComponentSchemaValidator validator)
    {
        this.validator = validator;
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

        var outputDirectory = ResolveOutputDirectory(options);
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

        return new StaticSitePackageResult
        {
            OutputDirectory = outputDirectory,
            EntryPoint = Path.Combine(outputDirectory, "index.html"),
            SiteJsonPath = Path.Combine(outputDirectory, "site.json"),
            ManifestPath = Path.Combine(outputDirectory, "components", "manifest.json"),
            Files = files,
        };
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
            main { max-width: 1180px; margin: 0 auto; padding: 0 20px 48px; }
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
            .image-block { margin: 0; min-height: 220px; border-radius: 8px; overflow: hidden; background: var(--band); }
            .image-block img { width: 100%; height: 100%; min-height: 220px; object-fit: cover; display: block; }
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
            .footer-logo { max-width: 180px; max-height: 72px; object-fit: contain; }
            .footer-links { display: flex; flex-wrap: wrap; gap: 8px 14px; margin-top: 10px; }
            .runtime-error { max-width: 760px; margin: 48px auto; padding: 18px; border: 1px solid #e5484d; color: #8f1d22; background: #fff7f7; }
            @media (max-width: 760px) {
              .site-header-inner { grid-template-columns: 1fr; grid-template-areas: "brand" "utility" "primary"; }
              .utility-links, .primary-links { justify-content: flex-start; }
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
          HeroSection: renderHeroSection,
          ContentSection: renderContentSection,
          LinkList: renderLinkList,
          FormBlock: renderFormBlock,
          SiteFooter: renderSiteFooter,
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
        const knownTypes = new Set((manifest.components || []).map(component => component.type));
        const generatedRenderers = await loadGeneratedRenderers(manifest);

        renderCurrentRoute();
        window.addEventListener('popstate', () => {
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
          const path = window.location.pathname || '/';
          return routes.find(route => route.path === path) || routes[0];
        }

        function navigateToRoute(path) {
          const nextRoute = site.routes.find(route => route.path === path);
          if (!nextRoute) return false;

          currentRoute = nextRoute;
          if (window.location.pathname !== path) {
            history.pushState({ path }, '', path);
          }
          renderCurrentRoute();
          window.scrollTo({ top: 0, behavior: 'smooth' });
          return true;
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
            if (child.type === 'SiteHeader' || child.type === 'SiteFooter') {
              shell.appendChild(rendered);
            } else {
              main.appendChild(rendered);
            }
          }
          shell.insertBefore(main, shell.querySelector('.site-footer'));
          return shell;
        }

        function renderSiteHeader(node) {
          const header = element('header', 'site-header');
          const inner = element('div', 'site-header-inner');
          const brand = element('a', 'brand', node.props?.title || 'Generated Site');
          brand.href = './';
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

        function renderHeroSection(node) {
          const section = element('section', 'hero-section');
          section.append(element('h1', '', node.props?.title || ''));
          if (node.props?.body) section.append(element('p', '', node.props.body));
          return section;
        }

        function renderContentSection(node) {
          const section = element('section', 'content-section');
          section.append(element('h2', '', node.props?.title || 'Section'));
          if (node.props?.body) section.append(element('p', '', node.props.body));
          return section;
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
          img.loading = 'lazy';
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
            img.loading = 'lazy';
            card.appendChild(img);
          }
          const body = element('div', 'feature-card-body');
          body.append(element('h3', '', node.props?.title || ''));
          if (node.props?.body) body.append(element('p', '', node.props.body));
          if (node.props?.url) {
            const link = { label: 'Open', url: node.props.url, scope: node.props.url.startsWith('/') ? 'internal' : 'external' };
            const anchor = element('a', 'feature-card-link', link.label);
            configureLink(anchor, link);
            body.append(anchor);
          }
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
          if (node.props?.source_url) {
            const source = element('a', '', node.props.source_url);
            source.href = node.props.source_url;
            content.append(source);
          }
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
          const url = normalizeInternalRoute(link.url || '#');
          anchor.href = toStaticHref(url);
          if (link.scope === 'internal' && url.startsWith('/')) {
            anchor.setAttribute('data-local-route', url);
            anchor.addEventListener('click', event => {
              if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) return;
              event.preventDefault();
              navigateToRoute(url);
            });
          }
        }

        function normalizeInternalRoute(url) {
          if (!url?.startsWith('/')) return url || '#';
          return url
            .replace(/\/index$/i, '/')
            .replace(/\.(aspx|html|htm)(?=\/|$|\?)/i, '')
            .replace(/[?&=]+/g, '-');
        }

        function toStaticHref(url) {
          if (!url?.startsWith('/')) return url || '#';
          return `#${url}`;
        }
        """;
}
