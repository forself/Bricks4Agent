import { createReadStream, existsSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const mimeTypes = new Map([
    ['.css', 'text/css; charset=utf-8'],
    ['.html', 'text/html; charset=utf-8'],
    ['.js', 'text/javascript; charset=utf-8'],
    ['.json', 'application/json; charset=utf-8'],
]);

async function loadChromium() {
    for (const candidate of ['playwright', 'playwright-core']) {
        try {
            const module = await import(candidate);
            const chromium = module.chromium ?? module.default?.chromium;
            if (chromium) return chromium;
        } catch {
            // Try the next test-only Playwright package.
        }
    }
    throw new Error('Playwright is required for the SPA template browser smoke.');
}

function createStaticServer() {
    const rootPrefix = `${repoRoot}${path.sep}`;
    const server = createServer((request, response) => {
        try {
            const requestUrl = new URL(request.url ?? '/', 'http://127.0.0.1');
            if (requestUrl.pathname === '/favicon.ico') {
                response.writeHead(204, { 'Cache-Control': 'no-store' }).end();
                return;
            }
            const relativePath = decodeURIComponent(requestUrl.pathname).replace(/^\/+/, '');
            const filePath = path.resolve(repoRoot, relativePath);
            if (filePath !== repoRoot && !filePath.startsWith(rootPrefix)) {
                response.writeHead(403).end('Forbidden');
                return;
            }
            if (!existsSync(filePath) || !statSync(filePath).isFile()) {
                response.writeHead(404).end('Not Found');
                return;
            }
            response.writeHead(200, {
                'Cache-Control': 'no-store',
                'Content-Type': mimeTypes.get(path.extname(filePath).toLowerCase()) ?? 'application/octet-stream',
                'Content-Security-Policy': "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'",
            });
            createReadStream(filePath).pipe(response);
        } catch {
            response.writeHead(500).end('Server Error');
        }
    });
    return new Promise((resolve, reject) => {
        server.once('error', reject);
        server.listen(0, '127.0.0.1', () => {
            const address = server.address();
            resolve({ server, baseUrl: `http://127.0.0.1:${address.port}` });
        });
    });
}

function closeServer(server) {
    return new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
}

const chromium = await loadChromium();
const { server, baseUrl } = await createStaticServer();
let browser;
try {
    browser = await chromium.launch({ channel: 'msedge', headless: true });
    const page = await browser.newPage();
    const failures = [];
    page.on('pageerror', (error) => failures.push(`pageerror: ${error.message}`));
    page.on('console', (message) => {
        if (message.type() === 'error') {
            failures.push(`console: ${message.text()} ${message.location().url || ''}`.trim());
        }
    });
    page.on('response', (response) => {
        if (response.status() >= 400) failures.push(`HTTP ${response.status()}: ${response.url()}`);
    });

    await page.goto(`${baseUrl}/templates/spa/frontend/index.html#/`, { waitUntil: 'networkidle' });
    await page.waitForSelector('.home-page');
    const state = await page.evaluate(async () => {
        const { createCanvasIcon } = await import('./components/CanvasIcon.js');
        const icon = createCanvasIcon('check', 20, 'template smoke icon');
        document.body.appendChild(icon);
        return {
            appLayout: document.querySelectorAll('.app-layout').length,
            homePage: document.querySelectorAll('.home-page').length,
            canvasReady: icon.dataset.canvasIconReady,
            svg: document.querySelectorAll('svg').length,
            styleElements: document.querySelectorAll('style').length,
            inlineHandlers: document.querySelectorAll('[onclick],[onchange],[oninput],[onload],[onerror]').length,
        };
    });

    const assertions = [
        ['SPA template boots through its real route graph', state.appLayout === 1 && state.homePage === 1],
        ['Canvas template icon renders', state.canvasReady === 'true'],
        ['Runtime remains SVG and inline-markup hard-zero', state.svg === 0 && state.styleElements === 0 && state.inlineHandlers === 0],
        ['Browser run has no page, console, or HTTP errors', failures.length === 0],
    ];
    for (const [name, pass] of assertions) console.log(`${pass ? 'ok  ' : 'FAIL'} ${name}`);
    if (assertions.some(([, pass]) => !pass)) {
        if (failures.length > 0) console.error(failures.join('\n'));
        process.exitCode = 1;
    } else {
        console.log(`\nSPA template browser smoke: ${assertions.length}/${assertions.length} passed.`);
    }
} finally {
    await browser?.close();
    await closeServer(server);
}
