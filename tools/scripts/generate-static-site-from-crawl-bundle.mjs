#!/usr/bin/env node
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const REPO_ROOT = path.resolve(__dirname, '../..');
const DEFAULT_BUNDLE = 'projects/ntub-crawler-test/site-generator-bundle.json';
const DEFAULT_OUTPUT = 'projects/ntub-generated-site';
const UI_COMPONENTS_SOURCE = path.join(REPO_ROOT, 'packages/javascript/browser/ui_components');

function parseArgs(argv) {
  const args = {
    bundle: DEFAULT_BUNDLE,
    output: DEFAULT_OUTPUT,
  };

  for (let i = 2; i < argv.length; i += 1) {
    if (argv[i] === '--bundle') args.bundle = argv[++i];
    if (argv[i] === '--output') args.output = argv[++i];
  }

  return args;
}

function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function slugify(value, fallback = 'page') {
  const slug = String(value || fallback)
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
  return slug || fallback;
}

function canonicalUrl(value) {
  const url = new URL(value);
  url.hash = '';
  if (url.pathname !== '/' && url.pathname.endsWith('/')) {
    url.pathname = url.pathname.slice(0, -1);
  }
  return url.href;
}

function isInDomainScope(value, domainSuffix) {
  try {
    const url = value instanceof URL ? value : new URL(value);
    const hostname = url.hostname.toLowerCase();
    const suffix = String(domainSuffix || '').toLowerCase();
    return hostname === suffix || hostname.endsWith(`.${suffix}`);
  } catch {
    return false;
  }
}

function extensionForAsset(url, contentType = '') {
  const fromPath = path.extname(new URL(url).pathname).toLowerCase();
  if (fromPath && fromPath.length <= 8) return fromPath;
  if (contentType.includes('png')) return '.png';
  if (contentType.includes('jpeg') || contentType.includes('jpg')) return '.jpg';
  if (contentType.includes('gif')) return '.gif';
  if (contentType.includes('webp')) return '.webp';
  if (contentType.includes('svg')) return '.svg';
  return '.bin';
}

function sectionTitle(section) {
  return section?.text?.headline || '';
}

function sectionBody(section) {
  return section?.text?.body || '';
}

function firstUsefulImage(page) {
  for (const section of page.sections || []) {
    for (const image of section.media || []) {
      if (image?.url) return image;
    }
  }
  return null;
}

function meaningfulDescription(crawlPage) {
  const description = String(crawlPage.description || '').trim();
  const placeholders = ['請填寫網站簡述', '請填寫網站描述', 'website description'];
  if (description && !placeholders.some(item => description.toLowerCase().includes(item.toLowerCase()))) {
    return description;
  }
  return crawlPage.text_excerpt || '';
}

function isNoisyDescription(value) {
  const text = String(value || '');
  if (!text.trim()) return true;
  if (/跳到主要內容區|Menu Menu|Search Menu|:::/.test(text)) return true;
  return text.length > 260 && /活動看板|訊息中心|ANNOUNCEMENT|NTUBNEWS/.test(text);
}

function normalizeSiteData(bundle) {
  const modelPages = bundle.extracted_site_model?.pages || [];
  const crawlPagesByUrl = new Map((bundle.crawl?.pages || []).map(page => [page.final_url, page]));
  const pageDefsById = new Map((bundle.definitions?.pages || []).map(page => [page.id, page.definition]));
  const routesById = new Map((bundle.extracted_site_model?.route_graph?.routes || []).map(route => [route.page_id, route]));

  const pages = modelPages.map((entry, index) => {
    const crawlPage = crawlPagesByUrl.get(entry.page_url) || {};
    const definition = pageDefsById.get(entry.page_id) || {};
    const route = routesById.get(entry.page_id) || {};
    const title = crawlPage.title || definition.description || entry.page_id;
    const sections = entry.sections || [];
    const image = firstUsefulImage({ sections });

    return {
      id: entry.page_id,
      route: index === 0 ? '/' : `/${slugify(entry.page_id.replace(/^ntub-/, ''))}/`,
      sourceUrl: entry.page_url,
      path: route.path || new URL(entry.page_url).pathname || '/',
      depth: route.depth ?? crawlPage.depth ?? 0,
      title,
      description: meaningfulDescription(crawlPage),
      image,
      sections,
      links: crawlPage.links || [],
    };
  });

  const domainSuffix = bundle.source?.scope?.domain_suffix
    || bundle.crawl?.root?.domain_suffix
    || new URL(bundle.source?.start_url).hostname;
  const routesBySourceUrl = new Map(pages.map(page => [canonicalUrl(page.sourceUrl), page.route]));

  for (const page of pages) {
    page.links = page.links.map(link => {
      const sameDomain = link.same_domain === true || isInDomainScope(link.url, domainSuffix);
      return {
        ...link,
        same_domain: sameDomain,
        localRoute: sameDomain ? (routesBySourceUrl.get(canonicalUrl(link.url)) || null) : null,
      };
    });
  }

  const navPages = pages
    .filter(page => page.depth <= 1)
    .slice(0, 12)
    .map(page => ({ id: page.id, title: page.title, route: page.route }));

  const generatedComponents = bundle.generated_components || [];

  return {
    meta: {
      title: pages[0]?.title || 'Generated Site',
      sourceUrl: bundle.source?.start_url || '',
      generatedAt: new Date().toISOString(),
      crawlRunId: bundle.source?.crawl_run_id || '',
      pageCount: pages.length,
      traversal: bundle.source?.scope?.traversal || 'breadth_first',
      maxDepth: bundle.source?.scope?.max_depth ?? null,
      domainSuffix,
      mirroredAssetCount: 0,
    },
    navPages,
    pages,
    themeTokens: bundle.theme_tokens || {},
    generatedComponents,
  };
}

async function mirrorSameDomainAssets(site, outputDir) {
  const assetDir = path.join(outputDir, 'assets/mirrored');
  await fs.mkdir(assetDir, { recursive: true });

  const imageRefs = [];
  for (const page of site.pages) {
    if (page.image?.url && isInDomainScope(page.image.url, site.meta.domainSuffix)) {
      imageRefs.push(page.image);
    }
    for (const section of page.sections || []) {
      for (const item of section.items || []) {
        if (item.image?.url && isInDomainScope(item.image.url, site.meta.domainSuffix)) {
          imageRefs.push(item.image);
        }
      }
      for (const image of section.media || []) {
        if (image?.url && isInDomainScope(image.url, site.meta.domainSuffix)) {
          imageRefs.push(image);
        }
      }
    }
  }

  const mirrored = new Map();
  for (const image of imageRefs) {
    if (mirrored.has(image.url)) {
      image.localUrl = mirrored.get(image.url);
      image.url = image.localUrl;
      continue;
    }

    try {
      const response = await fetch(image.url, {
        headers: { 'user-agent': 'Bricks4AgentStaticSiteGenerator/0.1' },
      });
      if (!response.ok) continue;
      const contentType = response.headers.get('content-type') || '';
      if (!contentType.startsWith('image/')) continue;

      const bytes = Buffer.from(await response.arrayBuffer());
      const fileName = `${slugify(new URL(image.url).hostname)}-${Buffer.from(image.url).toString('base64url').slice(0, 18)}${extensionForAsset(image.url, contentType)}`;
      await fs.writeFile(path.join(assetDir, fileName), bytes);

      const localUrl = `./assets/mirrored/${fileName}`;
      mirrored.set(image.url, localUrl);
      image.originalUrl = image.url;
      image.localUrl = localUrl;
      image.url = localUrl;
    } catch {
      // Best-effort mirroring; page content and local routing still work offline.
    }
  }

  site.meta.mirroredAssetCount = mirrored.size;
}

function buildIndexHtml(site) {
  const assetVersion = encodeURIComponent(site.meta.generatedAt || Date.now());
  return `<!doctype html>
<html lang="zh-Hant">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>${escapeHtml(site.meta.title)}</title>
  <link rel="stylesheet" href="./runtime/ui_components/theme.css?v=${assetVersion}">
  <link rel="stylesheet" href="./styles/site.css?v=${assetVersion}">
</head>
<body>
  <div id="app" class="site-shell">
    <header class="site-topbar" data-topbar>
      <a class="site-brand" href="#/" aria-label="${escapeHtml(site.meta.title)}">
        <span class="site-brand-mark">NTUB</span>
        <span class="site-brand-text">${escapeHtml(site.meta.title)}</span>
      </a>
    </header>
    <div class="site-body">
      <aside class="site-sidebar" id="site-nav"></aside>
      <div class="site-main">
        <div class="breadcrumb-host" id="breadcrumb-root"></div>
        <main id="page-root" class="page-root" tabindex="-1"></main>
      </div>
    </div>
  </div>
  <script type="module" src="./app.js?v=${assetVersion}"></script>
</body>
</html>
`;
}

function buildDataModule(site) {
  return `export const siteData = ${JSON.stringify(site, null, 2)};

export default siteData;
`;
}

function buildAppJs() {
  return `import { BasicButton, Breadcrumb, ButtonGroup, FeatureCard, ImageViewer, PhotoCard, SideMenu } from './runtime/ui_components/index.js';
import { siteData } from './data/site-data.js';

const pageRoot = document.querySelector('#page-root');
const navRoot = document.querySelector('#site-nav');
const breadcrumbRoot = document.querySelector('#breadcrumb-root');

function escapeHtml(value) {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}

function compact(value, max = 240) {
  const text = String(value || '').replace(/\\s+/g, ' ').trim();
  return text.length > max ? text.slice(0, max - 1) + '…' : text;
}

function isNoisyDescription(value) {
  const text = String(value || '');
  if (!text.trim()) return true;
  if (/跳到主要內容區|Menu Menu|Search Menu|:::/.test(text)) return true;
  return text.length > 260 && /活動看板|訊息中心|ANNOUNCEMENT|NTUBNEWS/.test(text);
}

function currentRoute() {
  return location.hash.replace(/^#/, '') || '/';
}

function pageForRoute(route) {
  return siteData.pages.find(page => page.route === route) || siteData.pages[0];
}

function isSameDomainUrl(url) {
  try {
    const parsed = new URL(url);
    const host = parsed.hostname.toLowerCase();
    const suffix = String(siteData.meta.domainSuffix || '').toLowerCase();
    return host === suffix || host.endsWith('.' + suffix);
  } catch {
    return false;
  }
}

function unavailableOffline(url) {
  console.warn('Same-domain page was not included in the offline crawl cap:', url);
  window.alert('這個同網域頁面未包含在離線封裝內。');
}

function navigate(route) {
  location.hash = route;
}

function openLink(link) {
  if (link.localRoute) {
    navigate(link.localRoute);
    return;
  }
  if (link.same_domain || isSameDomainUrl(link.url)) {
    unavailableOffline(link.url);
    return;
  }
  window.open(link.url, '_blank', 'noopener,noreferrer');
}

function renderNavigation(activePage) {
  navRoot.innerHTML = '';
  const menuItems = siteData.navPages.map(page => ({
    id: page.id,
    text: page.title,
    href: '#' + page.route,
    badge: page.depth === 0 ? 'Home' : null,
  }));
  new SideMenu({
    items: menuItems,
    activeId: activePage.id,
    width: '260px',
    collapsedWidth: '56px',
    accordion: false,
    onSelect: (item) => {
      const page = siteData.pages.find(candidate => candidate.id === item.id);
      if (page) navigate(page.route);
    },
  }).mount(navRoot);
}

function renderBreadcrumb(activePage) {
  breadcrumbRoot.innerHTML = '';
  new Breadcrumb({
    showHome: true,
    homeHref: '#/',
    homeIcon: '',
    separator: '/',
    items: [
      { text: activePage.title, active: true },
    ],
    onNavigate: (item) => {
      if (item.href) {
        const route = item.href.replace(/^#/, '') || '/';
        navigate(route);
      }
    },
  }).mount(breadcrumbRoot);
}

function roleLabel(role) {
  const labels = {
    hero: '焦點',
    navigation: '導覽',
    news_list: '消息',
    card_grid: '連結群組',
    article: '內容',
    content_section: '區塊',
    footer: '頁尾',
    form: '表單',
  };
  return labels[role] || role;
}

function renderHero(page) {
  const hero = page.sections.find(section => section.role === 'hero') || page.sections[0] || {};
  const hasHero = hero.role === 'hero';
  const image = hasHero ? (hero.media?.[0] || page.image) : page.image;
  const candidateBody = hasHero ? hero.text?.body : page.description;
  const body = isNoisyDescription(candidateBody)
    ? '離線頁面，來源：' + page.sourceUrl
    : compact(candidateBody, 380);
  return \`
    <section class="hero-section">
      \${image?.url ? \`<img class="hero-image" src="\${escapeHtml(image.url)}" alt="\${escapeHtml(image.alt || page.title)}">\` : ''}
      <div class="hero-content">
        <div class="hero-kicker">\${escapeHtml(siteData.meta.title)}</div>
        <h1>\${escapeHtml(page.title)}</h1>
        <p>\${escapeHtml(body)}</p>
        <div class="hero-actions" id="hero-actions"></div>
      </div>
    </section>
  \`;
}

function mountHeroActions(page) {
  const target = document.querySelector('#hero-actions');
  if (!target) return;
  new ButtonGroup({
    gap: '10px',
    wrap: true,
    buttons: [
      new BasicButton({
        type: 'custom',
        customLabel: '回到首頁',
        variant: 'primary',
        showIcon: false,
        onClick: () => navigate('/'),
      }),
      new BasicButton({
        type: 'custom',
        customLabel: '瀏覽頁面索引',
        variant: 'secondary',
        showIcon: false,
        onClick: () => {
          document.querySelector('.page-index')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
        },
      }),
    ],
  }).mount(target);
}

function sectionLinks(section, page) {
  const body = section.text?.body || '';
  const terms = body.split(/\\s+/).filter(Boolean).slice(0, 12);
  return page.links
    .filter(link => link.text && terms.some(term => link.text.includes(term) || term.includes(link.text)))
    .slice(0, 6);
}

function renderMedia(media) {
  if (!media?.length) return '';
  return \`
    <div class="media-strip">
      \${media.slice(0, 4).map((image, index) => \`
        <button class="media-thumb" data-image-url="\${escapeHtml(image.url)}" data-image-alt="\${escapeHtml(image.alt || '')}" type="button">
          <img src="\${escapeHtml(image.url)}" alt="\${escapeHtml(image.alt || '') || 'section image ' + (index + 1)}">
        </button>
      \`).join('')}
    </div>
  \`;
}

function renderCards(section, page) {
  const links = sectionLinks(section, page);
  if (links.length === 0) return '';
  return \`<div class="generated-card-grid" data-card-grid="\${escapeHtml(section.id)}"></div>\`;
}

function renderNewsItems(section) {
  if (!Array.isArray(section.items) || section.items.length === 0) return '';
  const hasImages = section.items.some(item => item.image?.url);
  if (hasImages) {
    return \`
      <div class="news-card-grid" data-news-card-grid="\${escapeHtml(section.id)}"></div>
    \`;
  }
  return \`
    <div class="news-list">
      \${section.items.slice(0, 12).map(item => \`
        <article class="news-item">
          \${item.date ? \`<time>\${escapeHtml(item.date)}</time>\` : ''}
          <h3>\${escapeHtml(item.title)}</h3>
          \${item.url ? \`<button class="news-link" type="button" data-news-url="\${escapeHtml(item.url)}">查看</button>\` : ''}
        </article>
      \`).join('')}
    </div>
  \`;
}

function mountNewsCards(page) {
  for (const grid of document.querySelectorAll('[data-news-card-grid]')) {
    const section = page.sections.find(item => item.id === grid.dataset.newsCardGrid);
    if (!section?.items?.length) continue;

    for (const item of section.items.slice(0, 12)) {
      const card = document.createElement('article');
      card.className = 'news-card';
      const photoHost = document.createElement('div');
      photoHost.className = 'news-card-photo';
      const textHost = document.createElement('div');
      textHost.className = 'news-card-text';
      textHost.innerHTML = \`
        \${item.date ? \`<time>\${escapeHtml(item.date)}</time>\` : ''}
        <h3>\${escapeHtml(item.title)}</h3>
      \`;
      card.appendChild(photoHost);
      card.appendChild(textHost);
      grid.appendChild(card);

      new PhotoCard({
        type: 'portrait',
        src: item.image?.url || null,
        alt: item.image?.alt || item.title,
        clickable: Boolean(item.image?.url),
        width: '100%',
      }).mount(photoHost);

      card.addEventListener('click', () => {
        if (!item.url) return;
        const target = siteData.pages.find(candidate => candidate.sourceUrl === item.url);
        if (target) navigate(target.route);
        else if (isSameDomainUrl(item.url)) unavailableOffline(item.url);
        else window.open(item.url, '_blank', 'noopener,noreferrer');
      });
    }
  }
}

function mountCards(page) {
  for (const grid of document.querySelectorAll('[data-card-grid]')) {
    const section = page.sections.find(item => item.id === grid.dataset.cardGrid);
    const links = section ? sectionLinks(section, page) : [];
    for (const link of links) {
      const card = new FeatureCard({
        title: link.text || 'Link',
        description: link.url,
        tags: ['source link'],
        badge: link.same_domain ? (link.localRoute ? 'LOCAL' : 'NTUB') : 'EXT',
        elevated: false,
        onClick: () => openLink(link),
      });
      card.setLightMode(true);
      card.mount(grid);
    }
  }
}

function renderSection(section, page) {
  const title = section.text?.headline || roleLabel(section.role);
  const hasStructuredItems = section.role === 'news_list' && Array.isArray(section.items) && section.items.length > 0;
  const body = hasStructuredItems ? '' : compact(section.text?.body || '', section.role === 'news_list' ? 900 : 620);
  const roleClass = \`section--\${section.role.replace(/_/g, '-')}\`;

  return \`
    <section class="generated-section \${roleClass}" data-role="\${escapeHtml(section.role)}">
      <div class="section-meta">\${escapeHtml(roleLabel(section.role))}</div>
      <h2>\${escapeHtml(title)}</h2>
      \${body ? \`<p>\${escapeHtml(body)}</p>\` : ''}
      \${renderNewsItems(section)}
      \${renderMedia(section.media || [])}
      \${renderCards(section, page)}
    </section>
  \`;
}

function renderPageIndex(activePage) {
  const related = siteData.pages
    .filter(page => page.id !== activePage.id && page.depth <= activePage.depth + 1)
    .slice(0, 12);
  return \`
    <aside class="page-index">
      <h2>站台頁面</h2>
      <div class="page-index-list">
        \${related.map(page => \`
          <a class="page-index-link" href="#\${escapeHtml(page.route)}">
            <span>\${escapeHtml(page.title)}</span>
            <small>Depth \${escapeHtml(page.depth)}</small>
          </a>
        \`).join('')}
      </div>
    </aside>
  \`;
}

function renderDataPanel(page) {
  return \`
    <section class="data-panel" id="page-data">
      <h2>頁面資訊</h2>
      <dl>
        <div><dt>Page ID</dt><dd>\${escapeHtml(page.id)}</dd></div>
        <div><dt>Depth</dt><dd>\${escapeHtml(page.depth)}</dd></div>
        <div><dt>Source</dt><dd>\${escapeHtml(page.sourceUrl)}</dd></div>
        <div><dt>Mirrored assets</dt><dd>\${escapeHtml(siteData.meta.mirroredAssetCount || 0)}</dd></div>
        <div><dt>Sections</dt><dd>\${escapeHtml(page.sections.length)}</dd></div>
      </dl>
    </section>
  \`;
}

function bindImageViewer() {
  for (const button of document.querySelectorAll('[data-image-url]')) {
    button.addEventListener('click', () => {
      ImageViewer.open(button.dataset.imageUrl, { maxZoom: 4 });
    });
  }
}

function bindNewsLinks() {
  for (const button of document.querySelectorAll('[data-news-url]')) {
    button.addEventListener('click', () => {
      const url = button.dataset.newsUrl;
      const page = siteData.pages.find(candidate => candidate.sourceUrl === url);
      if (page) {
        navigate(page.route);
      } else if (isSameDomainUrl(url)) {
        unavailableOffline(url);
      } else {
        window.open(url, '_blank', 'noopener,noreferrer');
      }
    });
  }
}

function render() {
  const page = pageForRoute(currentRoute());
  document.title = page.title ? \`\${page.title} | \${siteData.meta.title}\` : siteData.meta.title;
  renderNavigation(page);
  renderBreadcrumb(page);
  const visibleSections = page.sections.filter(section => section.role !== 'hero');

  pageRoot.innerHTML = \`
    \${renderHero(page)}
    <div class="content-layout">
      <div class="section-stack">
        \${visibleSections.slice(0, 10).map(section => renderSection(section, page)).join('')}
        \${renderDataPanel(page)}
      </div>
      \${renderPageIndex(page)}
    </div>
  \`;

  mountHeroActions(page);
  mountCards(page);
  mountNewsCards(page);
  bindImageViewer();
  bindNewsLinks();
  pageRoot.focus({ preventScroll: true });
}

window.addEventListener('hashchange', render);
render();
`;
}

function buildCss(site) {
  const colors = site.themeTokens?.colors?.map(item => item.value) || [];
  const primary = colors.find(color => !['#fff', '#ffffff', '#000', '#000000'].includes(color)) || '#006f60';
  const accent = colors.find(color => color !== primary && !['#fff', '#ffffff', '#000', '#000000', '#eee', '#ddd'].includes(color)) || '#c9352b';

  return `:root {
  --ntub-primary: ${primary};
  --ntub-accent: ${accent};
  --ntub-ink: #18211f;
  --ntub-muted: #5e6a66;
  --ntub-line: #d9e2df;
  --ntub-surface: #ffffff;
  --ntub-soft: #f5f8f7;
  --ntub-raised: #ffffff;
}

* {
  box-sizing: border-box;
}

body {
  margin: 0;
  color: var(--ntub-ink);
  background: var(--ntub-soft);
  font-family: "Noto Sans TC", "Microsoft JhengHei", system-ui, sans-serif;
  line-height: 1.65;
}

a {
  color: inherit;
}

.site-topbar {
  position: sticky;
  top: 0;
  z-index: 40;
  min-height: 72px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 10px clamp(16px, 4vw, 48px);
  background: rgba(255, 255, 255, 0.95);
  border-bottom: 1px solid var(--ntub-line);
  backdrop-filter: blur(12px);
}

.site-brand {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  min-width: 230px;
  text-decoration: none;
  font-weight: 700;
}

.site-brand-mark {
  display: grid;
  place-items: center;
  width: 48px;
  height: 48px;
  border-radius: 8px;
  background: var(--ntub-primary);
  color: #fff;
  font-size: 0.85rem;
  letter-spacing: 0;
}

.site-brand-text {
  font-size: 1rem;
}

.site-body {
  display: grid;
  grid-template-columns: 260px minmax(0, 1fr);
  align-items: start;
}

.site-sidebar {
  position: sticky;
  top: 72px;
  height: calc(100vh - 72px);
  border-right: 1px solid var(--ntub-line);
  background: #fff;
  overflow: hidden;
}

.site-sidebar .side-menu {
  border-right: none;
}

.site-main {
  min-width: 0;
}

.breadcrumb-host {
  width: min(1180px, calc(100% - 32px));
  margin: 14px auto 0;
}

.page-root {
  outline: none;
}

.hero-section {
  position: relative;
  min-height: min(62vh, 620px);
  display: grid;
  align-items: end;
  overflow: hidden;
  background: #11211f;
}

.hero-image {
  position: absolute;
  inset: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  opacity: 0.52;
}

.hero-section::after {
  content: "";
  position: absolute;
  inset: 0;
  background: linear-gradient(90deg, rgba(8, 27, 24, 0.88), rgba(8, 27, 24, 0.42));
}

.hero-content {
  position: relative;
  z-index: 1;
  width: min(980px, calc(100% - 32px));
  margin: 0 auto;
  padding: 72px 0 64px;
  color: #fff;
}

.hero-kicker {
  margin-bottom: 10px;
  color: #d7fff4;
  font-size: 0.8rem;
  font-weight: 700;
  text-transform: uppercase;
}

.hero-content h1 {
  max-width: 840px;
  margin: 0;
  font-size: clamp(2.4rem, 7vw, 5.2rem);
  line-height: 1.05;
  letter-spacing: 0;
}

.hero-content p {
  max-width: 760px;
  margin: 20px 0 0;
  color: rgba(255, 255, 255, 0.9);
  font-size: 1.05rem;
}

.hero-actions {
  margin-top: 28px;
}

.content-layout {
  width: min(1180px, calc(100% - 32px));
  margin: 28px auto 80px;
  display: grid;
  grid-template-columns: minmax(0, 1fr) 320px;
  gap: 24px;
}

.section-stack {
  display: grid;
  gap: 16px;
}

.generated-section,
.data-panel,
.page-index {
  background: var(--ntub-surface);
  border: 1px solid var(--ntub-line);
  border-radius: 8px;
  padding: clamp(18px, 3vw, 30px);
}

.generated-section h2,
.data-panel h2,
.page-index h2 {
  margin: 0 0 12px;
  font-size: 1.35rem;
  line-height: 1.3;
  letter-spacing: 0;
}

.section-meta {
  margin-bottom: 8px;
  color: var(--ntub-primary);
  font-size: 0.78rem;
  font-weight: 700;
}

.generated-section p {
  margin: 0;
  color: var(--ntub-muted);
}

.media-strip {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 10px;
  margin-top: 18px;
}

.media-thumb {
  aspect-ratio: 16 / 10;
  padding: 0;
  overflow: hidden;
  border: 1px solid var(--ntub-line);
  border-radius: 8px;
  background: #f1f5f4;
  cursor: zoom-in;
}

.media-thumb img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  display: block;
}

.generated-card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
  margin-top: 18px;
}

.news-list {
  display: grid;
  gap: 10px;
  margin-top: 12px;
}

.news-item {
  display: grid;
  grid-template-columns: 104px minmax(0, 1fr) auto;
  align-items: center;
  gap: 12px;
  padding: 12px 0;
  border-bottom: 1px solid var(--ntub-line);
}

.news-item time {
  color: var(--ntub-primary);
  font-size: 0.86rem;
  font-weight: 700;
}

.news-item h3 {
  margin: 0;
  font-size: 1rem;
  line-height: 1.45;
  letter-spacing: 0;
}

.news-link {
  border: 1px solid var(--ntub-line);
  border-radius: 8px;
  background: #fff;
  padding: 6px 10px;
  cursor: pointer;
}

.news-link:hover {
  border-color: var(--ntub-primary);
  color: var(--ntub-primary);
}

.news-card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 14px;
  margin-top: 16px;
}

.news-card {
  display: grid;
  gap: 10px;
  padding: 12px;
  border: 1px solid var(--ntub-line);
  border-radius: 8px;
  background: #fff;
  cursor: pointer;
  transition: border-color 0.2s ease, transform 0.2s ease;
}

.news-card:hover {
  border-color: var(--ntub-primary);
  transform: translateY(-2px);
}

.news-card-photo {
  min-width: 0;
}

.news-card-text time {
  display: block;
  margin-bottom: 6px;
  color: var(--ntub-primary);
  font-size: 0.82rem;
  font-weight: 700;
}

.news-card-text h3 {
  margin: 0;
  font-size: 0.96rem;
  line-height: 1.45;
  letter-spacing: 0;
}

.page-index {
  position: sticky;
  top: 96px;
  align-self: start;
}

.page-index-list {
  display: grid;
  gap: 8px;
}

.page-index-link {
  display: grid;
  gap: 2px;
  padding: 10px 12px;
  border: 1px solid var(--ntub-line);
  border-radius: 8px;
  text-decoration: none;
  background: #fff;
}

.page-index-link:hover {
  border-color: var(--ntub-primary);
}

.page-index-link span {
  font-weight: 650;
}

.page-index-link small {
  color: var(--ntub-muted);
}

.data-panel dl {
  display: grid;
  gap: 10px;
  margin: 0;
}

.data-panel dl div {
  display: grid;
  grid-template-columns: 120px minmax(0, 1fr);
  gap: 12px;
  padding-bottom: 10px;
  border-bottom: 1px solid var(--ntub-line);
}

.data-panel dt {
  color: var(--ntub-muted);
  font-weight: 700;
}

.data-panel dd {
  margin: 0;
  min-width: 0;
  overflow-wrap: anywhere;
}

@media (max-width: 860px) {
  .site-topbar {
    position: static;
    align-items: flex-start;
    flex-direction: column;
  }

  .site-body {
    grid-template-columns: 1fr;
  }

  .site-sidebar {
    position: static;
    width: 100%;
    height: auto;
    border-right: none;
    border-bottom: 1px solid var(--ntub-line);
  }

  .content-layout {
    grid-template-columns: 1fr;
  }

  .page-index {
    position: static;
  }
}
`;
}

async function rmrf(target) {
  await fs.rm(target, { recursive: true, force: true });
}

async function main() {
  const args = parseArgs(process.argv);
  const bundlePath = path.resolve(args.bundle);
  const outputDir = path.resolve(args.output);
  const bundle = JSON.parse(await fs.readFile(bundlePath, 'utf8'));

  if (bundle.kind !== 'site-generator-bundle') {
    throw new Error(`Expected site-generator-bundle, got ${bundle.kind || '(missing kind)'}`);
  }

  const site = normalizeSiteData(bundle);
  await rmrf(outputDir);
  await fs.mkdir(path.join(outputDir, 'styles'), { recursive: true });
  await fs.mkdir(path.join(outputDir, 'data'), { recursive: true });
  await fs.mkdir(path.join(outputDir, 'runtime'), { recursive: true });
  await mirrorSameDomainAssets(site, outputDir);
  await fs.cp(UI_COMPONENTS_SOURCE, path.join(outputDir, 'runtime/ui_components'), {
    recursive: true,
    force: true,
    filter: source => !source.includes(`${path.sep}demo`) && !source.endsWith('.html'),
  });

  await fs.writeFile(path.join(outputDir, 'index.html'), buildIndexHtml(site), 'utf8');
  await fs.writeFile(path.join(outputDir, 'data/site-data.js'), buildDataModule(site), 'utf8');
  await fs.writeFile(path.join(outputDir, 'app.js'), buildAppJs(), 'utf8');
  await fs.writeFile(path.join(outputDir, 'styles/site.css'), buildCss(site), 'utf8');
  await fs.writeFile(path.join(outputDir, 'site-generator-bundle.snapshot.json'), `${JSON.stringify(bundle, null, 2)}\n`, 'utf8');

  process.stdout.write(JSON.stringify({
    success: true,
    outputDir,
    pages: site.pages.length,
    mirroredAssets: site.meta.mirroredAssetCount || 0,
    sourceBundle: bundlePath,
    entry: path.join(outputDir, 'index.html'),
    customComponentRuntime: path.join(outputDir, 'runtime/ui_components'),
  }, null, 2));
  process.stdout.write('\n');
}

main().catch(error => {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exit(1);
});
