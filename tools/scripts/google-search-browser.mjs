import puppeteer from 'puppeteer';

function parseArgs(argv) {
  const args = {};
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (!token.startsWith('--')) continue;
    const key = token.slice(2);
    const value = i + 1 < argv.length && !argv[i + 1].startsWith('--') ? argv[i + 1] : 'true';
    args[key] = value;
    if (value !== 'true') i += 1;
  }
  return args;
}

function sanitizeLimit(value) {
  const parsed = Number.parseInt(value, 10);
  if (Number.isNaN(parsed)) return 5;
  return Math.max(1, Math.min(10, parsed));
}

function safeToParam(value) {
  switch ((value || '').toLowerCase()) {
    case 'off':
      return 'off';
    case 'strict':
      return 'active';
    default:
      return 'active';
  }
}

function decodeUtf8Base64(value) {
  return Buffer.from(value, 'base64').toString('utf8');
}

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function launchBrowser() {
  const candidates = [
    { headless: true, channel: 'chrome' },
    { headless: true }
  ];

  let lastError = null;
  for (const candidate of candidates) {
    try {
      return await puppeteer.launch({
        ...candidate,
        ignoreDefaultArgs: ['--enable-automation'],
        args: [
          '--no-sandbox',
          '--disable-setuid-sandbox',
          '--disable-dev-shm-usage',
          '--lang=zh-TW',
          '--disable-blink-features=AutomationControlled'
        ]
      });
    } catch (error) {
      lastError = error;
    }
  }

  throw lastError ?? new Error('Unable to launch browser');
}

async function maybeAcceptConsent(page) {
  const accepted = await page.evaluate(() => {
    const labels = [
      'Accept all',
      'I agree',
      '同意',
      '接受',
      '接受全部',
      '全部接受'
    ];

    const elements = Array.from(document.querySelectorAll('button, [role="button"], input[type="submit"]'));
    const target = elements.find((element) => {
      const text = (element.innerText || element.value || '').trim();
      return labels.some((label) => text.includes(label));
    });

    if (!target) return false;
    target.click();
    return true;
  });

  if (accepted) {
    await sleep(1200);
  }
}

async function extractResults(page, limit) {
  return await page.evaluate((maxResults) => {
    const blockedTitleFragments = [
      'Accessibility help',
      'Maps',
      'Flights',
      'Google Weather',
      '服務條款',
      '瞭解詳情',
      '為什麼會發生這種情況'
    ];
    const blockedHosts = new Set([
      'support.google.com',
      'maps.google.com',
      'policies.google.com',
      'www.facebook.com',
      'facebook.com',
      'm.facebook.com'
    ]);
    const collected = [];
    const containers = Array.from(document.querySelectorAll('#search .g, #search .MjjYud, #rso > div, #search [data-snc]'));

    for (const container of containers) {
      if (collected.length >= maxResults) break;

      const anchor = container.querySelector('a[href]');
      const h3 = container.querySelector('h3');
      if (!anchor || !h3) continue;

      const href = anchor.href || '';
      if (!href.startsWith('http')) continue;
      if (href.includes('/search?') || href.includes('accounts.google.com')) continue;

      const title = (h3.innerText || anchor.innerText || '').trim();
      if (!title) continue;

      let url;
      try {
        url = new URL(href);
      } catch {
        continue;
      }

      if (blockedHosts.has(url.hostname)) continue;
      if (blockedTitleFragments.some((fragment) => title.includes(fragment))) continue;

      const snippetNode = container.querySelector('div[data-sncf], div[style], span, div');
      const snippet = (snippetNode?.innerText || '').trim();
      if (snippet.includes('People also ask')) continue;

      if (collected.some((item) => item.url === href)) continue;

      collected.push({
        rank: collected.length + 1,
        title,
        url: href,
        snippet
      });
    }

    return collected;
  }, limit);
}

async function preparePage(page, locale) {
  await page.setViewport({ width: 1440, height: 1080 });
  await page.setUserAgent('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36');
  await page.setExtraHTTPHeaders({ 'Accept-Language': `${locale},en;q=0.8` });
  await page.evaluateOnNewDocument(() => {
    Object.defineProperty(navigator, 'webdriver', { get: () => false });
    Object.defineProperty(navigator, 'language', { get: () => 'zh-TW' });
    Object.defineProperty(navigator, 'languages', { get: () => ['zh-TW', 'zh', 'en-US', 'en'] });
    Object.defineProperty(navigator, 'plugins', { get: () => [1, 2, 3, 4] });
  });
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const query = args['query-base64-utf8']
    ? decodeUtf8Base64(args['query-base64-utf8'])
    : (args.query || '');
  const locale = args.locale || 'zh-TW';
  const limit = sanitizeLimit(args.limit);
  const safe = safeToParam(args['safe-mode'] || args.safe_mode || 'moderate');

  if (!query.trim()) {
    throw new Error('query is required');
  }

  const browser = await launchBrowser();
  try {
    const page = await browser.newPage();
    await preparePage(page, locale);
    await page.goto('https://www.google.com/ncr', { waitUntil: 'domcontentloaded', timeout: 30000 });
    await maybeAcceptConsent(page);
    await page.waitForSelector('textarea[name="q"], input[name="q"]', { timeout: 10000 });
    const inputSelector = await page.$('textarea[name="q"], input[name="q"]');
    if (!inputSelector) {
      throw new Error('Google search input not found');
    }

    await inputSelector.click({ clickCount: 3 });
    await inputSelector.type(query, { delay: 80 });
    await page.keyboard.press('Enter');
    await page.waitForNavigation({ waitUntil: 'domcontentloaded', timeout: 30000 }).catch(() => null);
    await maybeAcceptConsent(page);
    await sleep(1800);

    const results = await extractResults(page, limit);
    const finalUrl = page.url();
    if (finalUrl.includes('/sorry/') || results.some((item) => item.url.includes('/sorry/'))) {
      throw new Error('Google blocked browser search with sorry page');
    }

    process.stdout.write(JSON.stringify({
      engine: 'google',
      query,
      results
    }));
  } finally {
    await browser.close();
  }
}

main().catch((error) => {
  process.stderr.write(`${error?.message || String(error)}\n`);
  process.exit(1);
});
