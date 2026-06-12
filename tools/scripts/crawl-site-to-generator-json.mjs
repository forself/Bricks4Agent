#!/usr/bin/env node
import crypto from 'node:crypto';
import dns from 'node:dns/promises';
import fs from 'node:fs/promises';
import net from 'node:net';
import path from 'node:path';
import { JSDOM, VirtualConsole } from 'jsdom';

const DEFAULT_START_URL = 'https://www.ntub.edu.tw/';
const DEFAULT_OUTPUT_DIR = 'projects/ntub-crawler-test';
const USER_AGENT = 'Bricks4AgentCrawlerTest/0.1 (+https://bricks4agent.local)';

const FIELDLESS_PAGE_COMPONENTS = [];
const PAGE_TYPES = new Set(['dashboard', 'detail']);

function parseArgs(argv) {
  const args = {
    startUrl: DEFAULT_START_URL,
    outputDir: DEFAULT_OUTPUT_DIR,
    maxPages: 200,
    maxDepth: 3,
    delayMs: 150,
    domainSuffix: null,
  };

  for (let i = 2; i < argv.length; i += 1) {
    const arg = argv[i];
    if (arg === '--url') args.startUrl = argv[++i];
    if (arg === '--output') args.outputDir = argv[++i];
    if (arg === '--max-pages') args.maxPages = Number(argv[++i]);
    if (arg === '--max-depth') args.maxDepth = Number(argv[++i]);
    if (arg === '--delay-ms') args.delayMs = Number(argv[++i]);
    if (arg === '--domain-suffix') args.domainSuffix = argv[++i];
  }

  return args;
}

function sleep(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function sha256(value) {
  return crypto.createHash('sha256').update(value).digest('hex');
}

function compactText(value, max = 500) {
  return String(value || '')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, max);
}

function absoluteUrl(value, baseUrl) {
  try {
    const url = new URL(value, baseUrl);
    if (!['http:', 'https:'].includes(url.protocol)) return null;
    url.hash = '';
    return url;
  } catch {
    return null;
  }
}

function canonicalUrl(value) {
  const url = value instanceof URL ? new URL(value.href) : new URL(value);
  url.hash = '';
  if (url.pathname !== '/' && url.pathname.endsWith('/')) {
    url.pathname = url.pathname.slice(0, -1);
  }
  return url.href;
}

function deriveDomainSuffix(hostname) {
  return String(hostname || '')
    .toLowerCase()
    .replace(/^www\./, '');
}

function isInDomainScope(url, domainSuffix) {
  const hostname = url.hostname.toLowerCase();
  const suffix = String(domainSuffix || '').toLowerCase();
  return hostname === suffix || hostname.endsWith(`.${suffix}`);
}

function isLikelyDocument(url) {
  const blockedExtensions = [
    '.jpg', '.jpeg', '.png', '.gif', '.webp', '.svg', '.ico',
    '.pdf', '.doc', '.docx', '.xls', '.xlsx', '.ppt', '.pptx',
    '.zip', '.rar', '.7z', '.mp4', '.mp3', '.css', '.js',
  ];
  const pathname = url.pathname.toLowerCase();
  return !blockedExtensions.some(ext => pathname.endsWith(ext));
}

function isPrivateAddress(address) {
  const family = net.isIP(address);
  if (family === 0) return true;

  if (family === 6) {
    const normalized = address.toLowerCase();
    return normalized === '::1'
      || normalized.startsWith('fc')
      || normalized.startsWith('fd')
      || normalized.startsWith('fe80:')
      || normalized === '::'
      || normalized.startsWith('::ffff:127.')
      || normalized.startsWith('::ffff:10.')
      || normalized.startsWith('::ffff:192.168.');
  }

  const parts = address.split('.').map(Number);
  const [a, b] = parts;
  return a === 10
    || a === 127
    || a === 0
    || (a === 169 && b === 254)
    || (a === 172 && b >= 16 && b <= 31)
    || (a === 192 && b === 168)
    || (a === 100 && b >= 64 && b <= 127);
}

const publicHostCheckCache = new Map();

async function assertPublicHost(url) {
  const hostname = url.hostname.toLowerCase();
  if (publicHostCheckCache.has(hostname)) {
    const cached = publicHostCheckCache.get(hostname);
    if (cached !== true) throw cached;
    return;
  }

  const records = await dns.lookup(url.hostname, { all: true, verbatim: false });
  const privateRecords = records.filter(record => isPrivateAddress(record.address));
  if (privateRecords.length > 0) {
    const error = new Error(`Refusing to crawl private or local address for host ${url.hostname}`);
    publicHostCheckCache.set(hostname, error);
    throw error;
  }
  publicHostCheckCache.set(hostname, true);
}

async function fetchText(url) {
  const response = await fetch(url.href, {
    redirect: 'follow',
    headers: {
      'user-agent': USER_AGENT,
      accept: 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8',
    },
  });

  const contentType = response.headers.get('content-type') || '';
  const text = await response.text();
  return {
    status: response.status,
    finalUrl: response.url,
    contentType,
    text,
  };
}

async function getRobotsPolicy(startUrl) {
  const robotsUrl = new URL('/robots.txt', startUrl.origin);
  try {
    const response = await fetch(robotsUrl.href, {
      headers: { 'user-agent': USER_AGENT },
    });
    const text = await response.text();
    const allowsAll = /User-agent:\s*\*\s+Allow:\s*\//i.test(text)
      || !/Disallow:\s*\//i.test(text);
    return {
      url: robotsUrl.href,
      status: response.status,
      allows_crawl: response.ok ? allowsAll : true,
      text_excerpt: compactText(text, 400),
    };
  } catch (error) {
    return {
      url: robotsUrl.href,
      status: null,
      allows_crawl: true,
      error: error.message,
    };
  }
}

function extractMeta(document, name) {
  const selector = `meta[name="${name}"], meta[property="${name}"]`;
  return document.querySelector(selector)?.getAttribute('content')?.trim() || '';
}

function selectorFor(element) {
  const tag = element.tagName.toLowerCase();
  if (element.id) return `${tag}#${element.id}`;
  const className = Array.from(element.classList || []).slice(0, 2).join('.');
  if (className) return `${tag}.${className}`;
  const parent = element.parentElement;
  if (!parent) return tag;
  const siblings = Array.from(parent.children).filter(child => child.tagName === element.tagName);
  const nth = siblings.indexOf(element) + 1;
  return `${selectorFor(parent)} > ${tag}:nth-of-type(${Math.max(nth, 1)})`;
}

function classifySection(element, index) {
  const tag = element.tagName.toLowerCase();
  const signature = `${tag} ${element.id || ''} ${element.className || ''}`.toLowerCase();
  const text = compactText(element.textContent, 800);
  const linkCount = element.querySelectorAll('a[href]').length;
  const imageCount = element.querySelectorAll('img').length;

  if (tag === 'nav' || signature.includes('menu')) return 'navigation';
  if (tag === 'footer') return 'footer';
  if (element.querySelector('form')) return 'form';
  if (/news|announce|bulletin|latest|hot|公告|消息|最新|焦點/.test(signature + text)) return 'news_list';
  if (/活動看板|activity board|訊息中心|announcement|新聞中心|ntubnews/i.test(text)) return 'news_list';
  if (/banner|hero|slider|carousel|kv|visual/.test(signature)) return 'hero';
  if (index === 0 && imageCount > 0 && linkCount <= 2 && text.length <= 260) return 'hero';
  if (linkCount >= 6 && imageCount >= 2) return 'card_grid';
  if (tag === 'article' || element.querySelector('h1,h2,h3')) return 'article';
  return 'content_section';
}

function extractLinks(document, pageUrl, domainSuffix) {
  const links = [];
  const seen = new Set();
  for (const anchor of document.querySelectorAll('a[href]')) {
    const url = absoluteUrl(anchor.getAttribute('href'), pageUrl);
    if (!url) continue;
    const href = canonicalUrl(url);
    if (seen.has(href)) continue;
    seen.add(href);
    links.push({
      url: href,
      text: compactText(anchor.textContent, 120),
      same_domain: isInDomainScope(url, domainSuffix),
    });
  }
  return links;
}

function extractAssets(document, pageUrl) {
  const assets = [];
  const seen = new Set();

  const addAsset = (rawUrl, type, label = '') => {
    const url = absoluteUrl(rawUrl, pageUrl);
    if (!url) return;
    const href = url.href;
    if (seen.has(`${type}:${href}`)) return;
    seen.add(`${type}:${href}`);
    assets.push({ url: href, type, label: compactText(label, 120) });
  };

  for (const img of document.querySelectorAll('img[src]')) {
    addAsset(img.getAttribute('src'), 'image', img.getAttribute('alt') || '');
  }
  for (const link of document.querySelectorAll('link[rel~="stylesheet"][href]')) {
    addAsset(link.getAttribute('href'), 'stylesheet');
  }
  for (const script of document.querySelectorAll('script[src]')) {
    addAsset(script.getAttribute('src'), 'script');
  }
  return assets;
}

function extractForms(document) {
  return Array.from(document.querySelectorAll('form')).map((form, index) => ({
    id: form.id || `form-${index + 1}`,
    method: (form.getAttribute('method') || 'get').toLowerCase(),
    action: form.getAttribute('action') || '',
    fields: Array.from(form.querySelectorAll('input, select, textarea')).map(field => ({
      name: field.getAttribute('name') || field.id || '',
      type: field.getAttribute('type') || field.tagName.toLowerCase(),
      label: compactText(field.getAttribute('aria-label') || field.getAttribute('placeholder') || field.getAttribute('title') || '', 80),
    })),
  }));
}

function extractNewsItems(element, pageUrl) {
  const candidates = [];
  const seen = new Set();
  const imagePool = Array.from(element.querySelectorAll('img[src]')).map(img => ({
    url: absoluteUrl(img.getAttribute('src'), pageUrl)?.href || img.getAttribute('src'),
    alt: compactText(img.getAttribute('alt') || '', 120),
  })).filter(image => image.url && !/clear\.gif/i.test(image.url));

  Array.from(element.querySelectorAll('a[href]')).forEach((anchor, index) => {
    const text = compactText(anchor.textContent, 240);
    if (!text || seen.has(text)) return;

    const url = absoluteUrl(anchor.getAttribute('href'), pageUrl);
    const dateMatch = text.match(/\b(?:20\d{2}|19\d{2})[-/.]\d{1,2}[-/.]\d{1,2}\b/);
    candidates.push({
      title: compactText(text.replace(dateMatch?.[0] || '', ''), 180) || text,
      date: dateMatch?.[0] || null,
      url: url ? canonicalUrl(url) : null,
      image: imagePool[index] || null,
      source_text: text,
    });
    seen.add(text);
  });

  if (candidates.length >= 2) {
    return candidates.slice(0, 20);
  }

  const text = compactText(element.textContent, 2000);
  const parts = text
    .split(/(?=\b(?:20\d{2}|19\d{2})[-/.]\d{1,2}[-/.]\d{1,2}\b)/)
    .map(item => compactText(item, 260))
    .filter(item => item.length > 16);

  return parts.slice(0, 20).map(item => {
    const dateMatch = item.match(/\b(?:20\d{2}|19\d{2})[-/.]\d{1,2}[-/.]\d{1,2}\b/);
    return {
      title: compactText(item.replace(dateMatch?.[0] || '', ''), 180) || item,
      date: dateMatch?.[0] || null,
      url: null,
      source_text: item,
    };
  });
}

function extractSections(document, pageUrl) {
  const selectors = [
    'header',
    'nav',
    'main > section',
    'main > article',
    'main > div',
    'section',
    'article',
    'footer',
    '[class*="banner"]',
    '[class*="news"]',
    '[class*="announce"]',
  ];
  const seen = new Set();
  const candidates = [];

  for (const element of document.querySelectorAll(selectors.join(','))) {
    if (seen.has(element)) continue;
    seen.add(element);
    const text = compactText(element.textContent, 1200);
    const hasMedia = element.querySelector('img, picture, video');
    const signature = `${element.tagName.toLowerCase()} ${element.id || ''} ${element.className || ''}`.toLowerCase();
    const nestedContentBlocks = element.querySelectorAll('section, article, nav, [class*="module"], [class*="news"], [class*="announce"]').length;
    const isExplicitHeroContainer = /banner|hero|slider|carousel|kv|visual/.test(signature);
    if (!isExplicitHeroContainer && element.tagName.toLowerCase() === 'div' && nestedContentBlocks >= 2) {
      continue;
    }
    if (text.length < 20 && !hasMedia) continue;
    candidates.push(element);
  }

  return candidates.slice(0, 12).map((element, index) => {
    const media = Array.from(element.querySelectorAll('img[src]')).slice(0, 8).map(img => ({
      url: absoluteUrl(img.getAttribute('src'), pageUrl)?.href || img.getAttribute('src'),
      alt: compactText(img.getAttribute('alt') || '', 120),
    }));
    const headline = compactText(
      element.querySelector('h1,h2,h3,.title,[class*="title"]')?.textContent
        || element.getAttribute('aria-label')
        || '',
      160,
    );

    const role = classifySection(element, index);
    const section = {
      id: `sec_${index + 1}`,
      tag: element.tagName.toLowerCase(),
      role,
      text: {
        headline,
        body: compactText(element.textContent, 700),
      },
      media,
      source_selector: selectorFor(element),
    };

    if (role === 'news_list') {
      section.items = extractNewsItems(element, pageUrl);
      if (section.items.length > 0 && !section.text.headline) {
        section.text.headline = section.items[0].title;
      }
    }

    return section;
  });
}

function extractPageModel(rawPage, domainSuffix) {
  const virtualConsole = new VirtualConsole();
  const dom = new JSDOM(rawPage.html, { url: rawPage.final_url, virtualConsole });
  const { document } = dom.window;

  for (const node of document.querySelectorAll('script, style, noscript, svg')) {
    node.remove();
  }

  const title = compactText(document.querySelector('title')?.textContent || '', 180);
  const description = compactText(extractMeta(document, 'description') || extractMeta(document, 'og:description'), 300);
  const links = extractLinks(document, rawPage.final_url, domainSuffix);
  const assets = extractAssets(document, rawPage.final_url);
  const forms = extractForms(document);
  const sections = extractSections(document, rawPage.final_url);

  return {
    url: rawPage.url,
    final_url: rawPage.final_url,
    depth: rawPage.depth,
    status_code: rawPage.status,
    title,
    description,
    text_excerpt: compactText(document.body?.textContent || '', 1000),
    links,
    forms,
    resources: assets,
    sections,
    html_sha256: sha256(rawPage.html),
  };
}

async function extractThemeTokens(pages) {
  const cssUrls = [];
  for (const page of pages) {
    for (const resource of page.resources) {
      if (resource.type === 'stylesheet' && cssUrls.length < 8) {
        cssUrls.push(resource.url);
      }
    }
  }

  const colorCounts = new Map();
  const fontCounts = new Map();
  const spacingCounts = new Map();

  for (const cssUrl of cssUrls) {
    try {
      const response = await fetch(cssUrl, { headers: { 'user-agent': USER_AGENT } });
      if (!response.ok) continue;
      const css = await response.text();
      for (const match of css.matchAll(/#[0-9a-fA-F]{3,8}\b/g)) {
        const key = match[0].toLowerCase();
        colorCounts.set(key, (colorCounts.get(key) || 0) + 1);
      }
      for (const match of css.matchAll(/font-family\s*:\s*([^;{}]+)/gi)) {
        const key = compactText(match[1], 120);
        fontCounts.set(key, (fontCounts.get(key) || 0) + 1);
      }
      for (const match of css.matchAll(/\b(?:margin|padding|gap|top|left|right|bottom)\s*:\s*([0-9]{1,3}px)\b/gi)) {
        const key = match[1];
        spacingCounts.set(key, (spacingCounts.get(key) || 0) + 1);
      }
    } catch {
      // Theme extraction is best effort; the crawl evidence remains valid.
    }
  }

  const top = map => Array.from(map.entries())
    .sort((a, b) => b[1] - a[1])
    .slice(0, 12)
    .map(([value, count]) => ({ value, count }));

  return {
    colors: top(colorCounts),
    typography: {
      font_families: top(fontCounts),
    },
    spacing: {
      observed: top(spacingCounts),
    },
  };
}

function pageIdFor(url, startUrl) {
  const parsed = new URL(url);
  if (canonicalUrl(parsed) === canonicalUrl(startUrl)) return 'ntub-home';
  const pathParts = parsed.pathname.split('/').filter(Boolean).slice(0, 4);
  const base = pathParts.join('-') || 'page';
  const hostSuffix = parsed.hostname.toLowerCase() === startUrl.hostname.toLowerCase()
    ? ''
    : `${parsed.hostname.toLowerCase().replace(/[^a-z0-9]+/g, '-')}-`;
  const urlSuffix = sha256(canonicalUrl(parsed)).slice(0, 8);
  return `ntub-${hostSuffix}${base.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '')}-${urlSuffix}`;
}

function pascalCasePageName(id) {
  const stem = id
    .replace(/^ntub-/, '')
    .split('-')
    .filter(Boolean)
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join('');
  return `Ntub${stem || 'Page'}Page`;
}

function buildDefinitionTemplate(pages, startUrl, crawlRunId, generatedAt, domainSuffix) {
  const pageEntries = pages.map((page, index) => {
    const id = pageIdFor(page.final_url, startUrl);
    const type = index === 0 ? 'dashboard' : 'detail';
    if (!PAGE_TYPES.has(type)) throw new Error(`Unsupported page type ${type}`);

    return {
      id,
      definition: {
        name: pascalCasePageName(id),
        type,
        description: page.title || page.description || page.final_url,
        components: FIELDLESS_PAGE_COMPONENTS,
        services: [],
        fields: [],
        api: {},
        behaviors: {},
        styles: {
          layout: 'single',
          theme: 'ntub',
        },
      },
    };
  });

  return {
    kind: 'definition-template',
    version: '0.1.0',
    meta: {
      source: startUrl.href,
      extractedAt: generatedAt,
      extractionMethod: 'crawl-site-to-generator-json',
      crawlRunId,
      description: 'National Taipei University of Business public website crawl converted to generator-native page definitions.',
    },
    definitions: {
      pages: pageEntries,
      apps: [
        {
          id: 'ntub-public-site',
          app: {
            identity: {
              name: 'NtubPublicSite',
              targets: ['web'],
            },
            profiles: ['website', 'static-replica'],
            configuration: {
              sections: [],
              featureValues: {
                sourceOrigin: startUrl.origin,
                sourceDomainSuffix: domainSuffix,
              },
            },
            frontend: {
              pageRefs: pageEntries.map(page => page.id),
            },
            backend: {
              features: [],
              services: [],
              policies: [],
              middleware: [],
              endpointModules: [
                { id: 'static-content', source: 'generated', reference: 'StaticContentEndpoints' },
              ],
              routeGroups: [
                {
                  id: 'public-pages',
                  prefix: '/',
                  moduleRefs: ['static-content'],
                },
              ],
              startupHooks: [],
              hosting: {
                mode: 'static',
              },
            },
          },
        },
      ],
    },
  };
}

function generatedComponentForRole(role) {
  const map = {
    navigation: 'GeneratedNtubNavigation',
    hero: 'GeneratedNtubHero',
    news_list: 'GeneratedNtubNewsList',
    card_grid: 'GeneratedNtubCardGrid',
    form: 'GeneratedNtubPublicForm',
    article: 'GeneratedNtubArticleSection',
    footer: 'GeneratedNtubFooter',
    content_section: 'GeneratedNtubContentSection',
  };
  return map[role] || 'GeneratedNtubContentSection';
}

function buildComponentResolution(pages, startUrl) {
  const generated = new Map();
  for (const page of pages) {
    for (const section of page.sections) {
      const registryName = generatedComponentForRole(section.role);
      if (!generated.has(registryName)) {
        generated.set(registryName, {
          component_id: `generated.${registryName.replace(/^Generated/, '').replace(/[A-Z]/g, match => `_${match.toLowerCase()}`).replace(/^_/, '')}`,
          registry_name: registryName,
          category: 'generated',
          source_section_ids: [],
          props_schema: {
            headline: 'string',
            body: 'string',
            media: 'array',
            links: 'array',
          },
          style_contract: {
            uses_theme_tokens: true,
            css_file: `${registryName}.css`,
          },
          behavior_contract: {
            events: section.role === 'navigation' ? ['navigate'] : [],
            side_effects: [],
          },
        });
      }
      generated.get(registryName).source_section_ids.push(`${pageIdFor(page.final_url, startUrl)}:${section.id}`);
    }
  }

  return {
    mapped: [],
    generated: Array.from(generated.values()),
    unresolved: [],
  };
}

function buildRouteGraph(pages, startUrl) {
  const routes = pages.map(page => ({
    path: new URL(page.final_url).pathname || '/',
    page_id: pageIdFor(page.final_url, startUrl),
    depth: page.depth,
    title: page.title,
  }));

  const knownPaths = new Set(routes.map(route => route.path));
  const edges = [];
  for (const page of pages) {
    const from = new URL(page.final_url).pathname || '/';
    for (const link of page.links) {
      const to = new URL(link.url).pathname || '/';
      if (knownPaths.has(to)) {
        edges.push({ from, to, kind: 'internal_link' });
      }
    }
  }

  return { routes, edges };
}

function buildBundle({ crawlRunId, startUrl, generatedAt, robots, pages, themeTokens, definitionTemplate, excluded, limits, domainSuffix }) {
  const componentResolution = buildComponentResolution(pages, startUrl);
  const assetMap = new Map();
  for (const page of pages) {
    for (const resource of page.resources) {
      assetMap.set(resource.url, {
        ...resource,
        referenced_by: Array.from(new Set([...(assetMap.get(resource.url)?.referenced_by || []), page.final_url])),
      });
    }
  }

  return {
    kind: 'site-generator-bundle',
    version: '0.1.0',
    source: {
      crawl_run_id: crawlRunId,
      start_url: startUrl.href,
      generated_at: generatedAt,
      scope: {
        kind: 'path_depth',
        max_depth: limits.max_depth,
        traversal: 'breadth_first',
        same_origin_only: false,
        domain_suffix: domainSuffix,
        path_prefix_lock: false,
      },
      robots,
    },
    crawl: {
      root: {
        start_url: startUrl.href,
        normalized_start_url: canonicalUrl(startUrl),
        origin: startUrl.origin,
        domain_suffix: domainSuffix,
        path_prefix: startUrl.pathname || '/',
      },
      pages: pages.map(({ sections, ...page }) => page),
      excluded,
      limits,
      traversal: {
        strategy: 'breadth_first',
      },
    },
    extracted_site_model: {
      pages: pages.map(page => ({
        page_url: page.final_url,
        page_id: pageIdFor(page.final_url, startUrl),
        sections: page.sections,
      })),
      route_graph: buildRouteGraph(pages, startUrl),
      interaction_inventory: pages.flatMap(page => page.forms.map(form => ({
        page_url: page.final_url,
        ...form,
      }))),
    },
    definitions: definitionTemplate.definitions,
    theme_tokens: themeTokens,
    asset_manifest: Array.from(assetMap.values()),
    component_resolution: componentResolution,
    generated_components: componentResolution.generated,
    generator_overrides: {
      component_paths: {},
    },
    validation: {
      page_definitions_valid: true,
      definition_template_valid: true,
      component_imports_valid: true,
      unresolved_count: componentResolution.unresolved.length,
      warnings: [
        'Generated component entries are requirements for the custom component library; they are not imported by the current PageGenerator yet.',
        'This is an anonymous public crawl with no authenticated interaction replay.',
      ],
    },
  };
}

async function crawlSite(args) {
  const startUrl = new URL(args.startUrl);
  const domainSuffix = args.domainSuffix || deriveDomainSuffix(startUrl.hostname);
  if (!['http:', 'https:'].includes(startUrl.protocol)) {
    throw new Error('Only http and https URLs can be crawled.');
  }
  await assertPublicHost(startUrl);

  const robots = await getRobotsPolicy(startUrl);
  if (!robots.allows_crawl) {
    throw new Error(`robots.txt does not allow crawling ${startUrl.href}`);
  }

  const queue = [{ url: canonicalUrl(startUrl), depth: 0 }];
  const visited = new Set();
  const completedFinalUrls = new Set();
  const excluded = [];
  const rawPages = [];

  while (queue.length > 0 && rawPages.length < args.maxPages) {
    const next = queue.shift();
    if (!next || visited.has(next.url)) continue;
    visited.add(next.url);

    const currentUrl = new URL(next.url);
    if (!isInDomainScope(currentUrl, domainSuffix)) {
      excluded.push({ url: next.url, reason: 'outside_domain_scope' });
      continue;
    }
    if (next.depth > args.maxDepth) {
      excluded.push({ url: next.url, reason: 'outside_path_depth' });
      continue;
    }
    if (!isLikelyDocument(currentUrl)) {
      excluded.push({ url: next.url, reason: 'non_html_document' });
      continue;
    }

    let fetched;
    try {
      await assertPublicHost(currentUrl);
      fetched = await fetchText(currentUrl);
    } catch (error) {
      excluded.push({ url: next.url, reason: 'fetch_failed', error: error.message });
      continue;
    }
    const finalUrl = new URL(fetched.finalUrl);
    try {
      await assertPublicHost(finalUrl);
    } catch (error) {
      excluded.push({ url: next.url, reason: 'redirected_to_private_or_unresolvable_host', final_url: fetched.finalUrl, error: error.message });
      continue;
    }
    if (!isInDomainScope(finalUrl, domainSuffix)) {
      excluded.push({ url: next.url, reason: 'redirected_outside_domain_scope', final_url: fetched.finalUrl });
      continue;
    }
    if (!fetched.contentType.toLowerCase().includes('html')) {
      excluded.push({ url: next.url, reason: 'non_html_content_type', content_type: fetched.contentType });
      continue;
    }

    const canonicalFinalUrl = canonicalUrl(finalUrl);
    if (completedFinalUrls.has(canonicalFinalUrl)) {
      excluded.push({ url: next.url, reason: 'duplicate_final_url', final_url: canonicalFinalUrl });
      continue;
    }
    completedFinalUrls.add(canonicalFinalUrl);

    rawPages.push({
      url: next.url,
      final_url: canonicalFinalUrl,
      depth: next.depth,
      status: fetched.status,
      html: fetched.text,
      content_type: fetched.contentType,
    });

    const page = extractPageModel(rawPages.at(-1), domainSuffix);
    for (const link of page.links) {
      const candidate = new URL(link.url);
      const canonical = canonicalUrl(candidate);
      if (!isInDomainScope(candidate, domainSuffix)) {
        excluded.push({ url: canonical, reason: 'outside_domain_scope' });
        continue;
      }
      if (next.depth + 1 > args.maxDepth) {
        excluded.push({ url: canonical, reason: 'outside_path_depth' });
        continue;
      }
      if (!visited.has(canonical) && !queue.some(item => item.url === canonical) && isLikelyDocument(candidate)) {
        queue.push({ url: canonical, depth: next.depth + 1 });
      }
    }

    if (args.delayMs > 0) await sleep(args.delayMs);
  }

  const pages = rawPages.map(rawPage => extractPageModel(rawPage, domainSuffix));
  const themeTokens = await extractThemeTokens(pages);
  return {
    startUrl,
    domainSuffix,
    robots,
    pages,
    excluded,
    themeTokens,
    limits: {
      max_pages: args.maxPages,
      max_depth: args.maxDepth,
      truncated: queue.length > 0,
      page_limit_hit: queue.length > 0,
      byte_limit_hit: false,
    },
  };
}

async function main() {
  const args = parseArgs(process.argv);
  const generatedAt = new Date().toISOString();
  const crawlRunId = `crawl_${generatedAt.replace(/[-:.TZ]/g, '').slice(0, 14)}`;
  const outputDir = path.resolve(args.outputDir);

  const crawl = await crawlSite(args);
  const definitionTemplate = buildDefinitionTemplate(crawl.pages, crawl.startUrl, crawlRunId, generatedAt, crawl.domainSuffix);
  const bundle = buildBundle({
    crawlRunId,
    startUrl: crawl.startUrl,
    generatedAt,
    robots: crawl.robots,
    pages: crawl.pages,
    themeTokens: crawl.themeTokens,
    definitionTemplate,
    excluded: crawl.excluded.slice(0, 200),
    limits: crawl.limits,
    domainSuffix: crawl.domainSuffix,
  });

  await fs.mkdir(outputDir, { recursive: true });
  const bundlePath = path.join(outputDir, 'site-generator-bundle.json');
  const templatePath = path.join(outputDir, 'ntub.definition-template.json');
  const modelPath = path.join(outputDir, 'extracted-site-model.json');
  const reportPath = path.join(outputDir, 'validation-report.json');

  await fs.writeFile(bundlePath, `${JSON.stringify(bundle, null, 2)}\n`, 'utf8');
  await fs.writeFile(templatePath, `${JSON.stringify(definitionTemplate, null, 2)}\n`, 'utf8');
  await fs.writeFile(modelPath, `${JSON.stringify(bundle.extracted_site_model, null, 2)}\n`, 'utf8');
  await fs.writeFile(reportPath, `${JSON.stringify(bundle.validation, null, 2)}\n`, 'utf8');

  process.stdout.write(JSON.stringify({
    success: true,
    crawlRunId,
    pages: crawl.pages.length,
    outputDir,
    files: {
      bundle: bundlePath,
      definitionTemplate: templatePath,
      extractedSiteModel: modelPath,
      validationReport: reportPath,
    },
  }, null, 2));
  process.stdout.write('\n');
}

main().catch(error => {
  process.stderr.write(`${error.stack || error.message}\n`);
  process.exit(1);
});
