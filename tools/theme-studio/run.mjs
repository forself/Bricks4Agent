import { spawn } from 'node:child_process';
import { createServer } from 'node:http';
import { existsSync } from 'node:fs';
import { mkdtemp, readFile, rm, stat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const MIME_TYPES = new Map([
    ['.css', 'text/css; charset=utf-8'],
    ['.html', 'text/html; charset=utf-8'],
    ['.js', 'text/javascript; charset=utf-8'],
    ['.json', 'application/json; charset=utf-8'],
    ['.mjs', 'text/javascript; charset=utf-8'],
    ['.png', 'image/png'],
    ['.txt', 'text/plain; charset=utf-8'],
    ['.woff', 'font/woff'],
    ['.woff2', 'font/woff2'],
]);
const COMMAND_TIMEOUT_MS = 12_000;
const STARTUP_TIMEOUT_MS = 25_000;

function sleep(milliseconds) {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function edgeCandidates() {
    const candidates = [];
    for (const variable of ['BRICKS_EDGE_PATH', 'EDGE_PATH']) {
        if (process.env[variable]) candidates.push(process.env[variable]);
    }

    if (process.platform === 'win32') {
        for (const base of [process.env.ProgramFiles, process.env['ProgramFiles(x86)'], process.env.LOCALAPPDATA]) {
            if (base) candidates.push(path.join(base, 'Microsoft', 'Edge', 'Application', 'msedge.exe'));
        }
    } else if (process.platform === 'darwin') {
        candidates.push('/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge');
    } else {
        candidates.push(
            '/usr/bin/microsoft-edge',
            '/usr/bin/microsoft-edge-stable',
            '/usr/bin/microsoft-edge-beta',
            '/usr/bin/microsoft-edge-dev',
        );
    }

    const executableNames = process.platform === 'win32'
        ? ['msedge.exe']
        : ['microsoft-edge', 'microsoft-edge-stable', 'microsoft-edge-beta', 'microsoft-edge-dev'];
    for (const directory of String(process.env.PATH || '').split(path.delimiter).filter(Boolean)) {
        for (const name of executableNames) candidates.push(path.join(directory, name));
    }
    return [...new Set(candidates.map((candidate) => path.resolve(candidate)))];
}

function findEdgeExecutable() {
    const executable = edgeCandidates().find((candidate) => existsSync(candidate));
    if (!executable) {
        throw new Error('Microsoft Edge not found. Set BRICKS_EDGE_PATH or EDGE_PATH to its executable.');
    }
    return executable;
}

async function createNoStoreServer() {
    const server = createServer(async (request, response) => {
        const noStoreHeaders = {
            'Cache-Control': 'no-store, no-cache, must-revalidate, max-age=0',
            Expires: '0',
            Pragma: 'no-cache',
        };
        try {
            if (request.method !== 'GET' && request.method !== 'HEAD') {
                response.writeHead(405, { ...noStoreHeaders, Allow: 'GET, HEAD' });
                response.end('Method Not Allowed');
                return;
            }

            const url = new URL(request.url || '/', 'http://127.0.0.1');
            const relative = decodeURIComponent(url.pathname).replace(/^\/+/, '');
            let filePath = path.resolve(repoRoot, relative);
            const withinRoot = filePath === repoRoot || filePath.startsWith(`${repoRoot}${path.sep}`);
            if (!withinRoot) {
                response.writeHead(403, noStoreHeaders);
                response.end('Forbidden');
                return;
            }

            let fileStat = await stat(filePath);
            if (fileStat.isDirectory()) {
                filePath = path.join(filePath, 'index.html');
                fileStat = await stat(filePath);
            }
            if (!fileStat.isFile()) throw new Error('Not a file');

            const content = await readFile(filePath);
            const contentType = MIME_TYPES.get(path.extname(filePath).toLowerCase()) || 'application/octet-stream';
            response.writeHead(200, {
                ...noStoreHeaders,
                'Content-Length': content.byteLength,
                'Content-Type': contentType,
            });
            response.end(request.method === 'HEAD' ? undefined : content);
        } catch {
            response.writeHead(404, { ...noStoreHeaders, 'Content-Type': 'text/plain; charset=utf-8' });
            response.end('Not Found');
        }
    });

    await new Promise((resolve, reject) => {
        server.once('error', reject);
        server.listen(0, '127.0.0.1', resolve);
    });
    const address = server.address();
    return {
        server,
        baseUrl: `http://127.0.0.1:${address.port}`,
    };
}

function closeServer(server) {
    if (!server.listening) return Promise.resolve();
    return new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
}

class CdpPipe {
    constructor(browserProcess) {
        this.browserProcess = browserProcess;
        this.input = browserProcess.stdio[3];
        this.output = browserProcess.stdio[4];
        this.buffer = Buffer.alloc(0);
        this.nextId = 1;
        this.pending = new Map();
        this.listeners = new Map();
        this.closed = false;
        this.output.on('data', (chunk) => this._consume(chunk));
        this.output.on('error', (error) => this._failAll(error));
        browserProcess.once('exit', (code, signal) => {
            this.closed = true;
            this._failAll(new Error(`Edge exited unexpectedly (code=${code}, signal=${signal}).`));
        });
    }

    _consume(chunk) {
        this.buffer = Buffer.concat([this.buffer, chunk]);
        let boundary;
        while ((boundary = this.buffer.indexOf(0)) >= 0) {
            const packet = this.buffer.subarray(0, boundary).toString('utf8');
            this.buffer = this.buffer.subarray(boundary + 1);
            if (!packet) continue;
            let message;
            try {
                message = JSON.parse(packet);
            } catch (error) {
                this._failAll(new Error(`Invalid CDP response: ${error.message}`));
                continue;
            }
            if (message.id) {
                const pending = this.pending.get(message.id);
                if (!pending) continue;
                this.pending.delete(message.id);
                clearTimeout(pending.timeout);
                if (message.error) pending.reject(new Error(`${pending.method}: ${message.error.message}`));
                else pending.resolve(message.result || {});
                continue;
            }
            for (const listener of this.listeners.get(message.method) || []) listener(message);
        }
    }

    _failAll(error) {
        for (const pending of this.pending.values()) {
            clearTimeout(pending.timeout);
            pending.reject(error);
        }
        this.pending.clear();
    }

    on(method, listener) {
        const listeners = this.listeners.get(method) || new Set();
        listeners.add(listener);
        this.listeners.set(method, listeners);
        return () => listeners.delete(listener);
    }

    send(method, params = {}, sessionId = undefined) {
        if (this.closed) return Promise.reject(new Error('CDP pipe is closed.'));
        const id = this.nextId++;
        const message = { id, method, params };
        if (sessionId) message.sessionId = sessionId;
        return new Promise((resolve, reject) => {
            const timeout = setTimeout(() => {
                this.pending.delete(id);
                reject(new Error(`${method} timed out after ${COMMAND_TIMEOUT_MS} ms.`));
            }, COMMAND_TIMEOUT_MS);
            this.pending.set(id, { method, resolve, reject, timeout });
            this.input.write(`${JSON.stringify(message)}\0`, 'utf8', (error) => {
                if (!error) return;
                clearTimeout(timeout);
                this.pending.delete(id);
                reject(error);
            });
        });
    }
}

async function launchEdge(executable) {
    const profileDirectory = await mkdtemp(path.join(tmpdir(), 'bricks-theme-studio-'));
    const args = [
        '--headless=new',
        '--remote-debugging-pipe',
        `--user-data-dir=${profileDirectory}`,
        '--window-size=1440,1000',
        '--disable-background-networking',
        '--disable-component-update',
        '--disable-default-apps',
        '--disable-extensions',
        '--disable-features=Translate',
        '--disable-gpu',
        '--disable-sync',
        '--metrics-recording-only',
        '--no-default-browser-check',
        '--no-first-run',
    ];
    if (typeof process.getuid === 'function' && process.getuid() === 0) args.push('--no-sandbox');
    args.push('about:blank');

    const browserProcess = spawn(executable, args, {
        stdio: ['ignore', 'ignore', 'pipe', 'pipe', 'pipe'],
        windowsHide: true,
    });
    let stderr = '';
    browserProcess.stderr.setEncoding('utf8');
    browserProcess.stderr.on('data', (chunk) => {
        stderr = `${stderr}${chunk}`.slice(-20_000);
    });
    await new Promise((resolve, reject) => {
        browserProcess.once('spawn', resolve);
        browserProcess.once('error', reject);
    });
    const cdp = new CdpPipe(browserProcess);
    try {
        await cdp.send('Browser.getVersion');
    } catch (error) {
        throw new Error(`Could not connect to Edge DevTools pipe: ${error.message}\n${stderr}`);
    }
    return { browserProcess, cdp, profileDirectory, getStderr: () => stderr };
}

async function stopEdge(browserProcess, cdp, profileDirectory) {
    try {
        await cdp.send('Browser.close');
    } catch {
        browserProcess.kill();
    }
    if (browserProcess.exitCode === null) {
        await Promise.race([
            new Promise((resolve) => browserProcess.once('exit', resolve)),
            sleep(2_000).then(() => browserProcess.kill()),
        ]);
    }
    await rm(profileDirectory, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
}

async function attachPage(cdp) {
    await cdp.send('Target.setDiscoverTargets', { discover: true });
    let { targetInfos } = await cdp.send('Target.getTargets');
    let target = targetInfos.find((candidate) => candidate.type === 'page');
    if (!target) {
        const created = await cdp.send('Target.createTarget', { url: 'about:blank' });
        targetInfos = (await cdp.send('Target.getTargets')).targetInfos;
        target = targetInfos.find((candidate) => candidate.targetId === created.targetId);
    }
    if (!target) throw new Error('Edge did not expose a page target.');
    const { sessionId } = await cdp.send('Target.attachToTarget', { targetId: target.targetId, flatten: true });
    await Promise.all([
        cdp.send('Page.enable', {}, sessionId),
        cdp.send('Runtime.enable', {}, sessionId),
        cdp.send('Network.enable', {}, sessionId),
        cdp.send('Log.enable', {}, sessionId),
    ]);
    return sessionId;
}

async function evaluate(cdp, sessionId, expression) {
    const response = await cdp.send('Runtime.evaluate', {
        expression,
        awaitPromise: true,
        returnByValue: true,
        userGesture: true,
    }, sessionId);
    if (response.exceptionDetails) {
        const detail = response.exceptionDetails.exception?.description || response.exceptionDetails.text;
        throw new Error(`Browser evaluation failed: ${detail}`);
    }
    return response.result?.value;
}

function consoleText(args = []) {
    return args.map((argument) => {
        if (argument.value !== undefined) return String(argument.value);
        return argument.description || argument.type || '';
    }).join(' ');
}

function addResult(results, name, pass, detail = '') {
    results.push({ name, pass: Boolean(pass), detail: detail == null ? '' : String(detail) });
}

async function run() {
    const executable = findEdgeExecutable();
    const { server, baseUrl } = await createNoStoreServer();
    let launched;
    try {
        launched = await launchEdge(executable);
        const { browserProcess, cdp, profileDirectory } = launched;
        const sessionId = await attachPage(cdp);
        const fatal = [];

        cdp.on('Runtime.exceptionThrown', (message) => {
            if (message.sessionId !== sessionId) return;
            const detail = message.params.exceptionDetails;
            fatal.push(`pageerror: ${detail.exception?.description || detail.text}`);
        });
        cdp.on('Runtime.consoleAPICalled', (message) => {
            if (message.sessionId !== sessionId || message.params.type !== 'error') return;
            fatal.push(`console: ${consoleText(message.params.args)}`);
        });
        cdp.on('Log.entryAdded', (message) => {
            if (message.sessionId !== sessionId || message.params.entry.level !== 'error') return;
            fatal.push(`log: ${message.params.entry.text}`);
        });
        cdp.on('Network.responseReceived', (message) => {
            if (message.sessionId !== sessionId) return;
            const response = message.params.response;
            if (response.status >= 400) fatal.push(`HTTP ${response.status}: ${response.url}`);
        });
        cdp.on('Network.loadingFailed', (message) => {
            if (message.sessionId !== sessionId || message.params.canceled) return;
            fatal.push(`network: ${message.params.errorText} ${message.params.blockedReason || ''}`.trim());
        });

        const url = `${baseUrl}/tools/theme-studio/index.html?tab=components`;
        await cdp.send('Page.navigate', { url }, sessionId);
        const deadline = Date.now() + STARTUP_TIMEOUT_MS;
        let startup;
        while (Date.now() < deadline) {
            try {
                startup = await evaluate(cdp, sessionId, `({
                    ready: window.__studioReady === true,
                    error: window.__studioError || '',
                    phase: window.__studioBootPhase || ''
                })`);
                if (startup.ready || startup.error) break;
            } catch {
                // Navigation may replace the execution context between polls.
            }
            await sleep(100);
        }

        const results = [];
        addResult(
            results,
            'Studio 由 JSON renderer 啟動完成',
            startup?.ready && !startup?.error,
            startup?.error || `phase=${startup?.phase || 'unknown'}`,
        );
        if (!startup?.ready || startup?.error) {
            addResult(results, '無 console/page/HTTP error', false, fatal.join(' | '));
            return { results, fatal, executable };
        }

        const checks = await evaluate(cdp, sessionId, `(async () => {
            const ts = window.__ts;
            const studio = window.__studio;
            const renderer = ts?.renderer;
            const workspaceTabs = ts?.workspaceTabs;
            const themeHost = renderer?.getHost?.('theme-workspace');
            const componentHost = renderer?.getHost?.('component-workspace');
            const initialThemeHost = themeHost;
            const initialComponentHost = componentHost;
            const beforePrimary = getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim();

            const tabIds = workspaceTabs?.getAllTabIds?.() || [];
            const themeTab = workspaceTabs?.tabMap?.get('theme')?.tabButton;
            const componentTab = workspaceTabs?.tabMap?.get('components')?.tabButton;
            const initialTab = workspaceTabs?.getActiveTabId?.();
            workspaceTabs?.activateTab?.('theme');
            const themeUrl = new URL(location.href);
            const themeConnected = Boolean(themeHost?.isConnected);
            const themeHelpHost = renderer?.getHost?.('theme-gallery-help');
            const componentHelpHost = renderer?.getHost?.('component-help');
            const openComponentsLink = renderer?.getComponent?.('theme-open-components-link');
            const openGalleryLink = renderer?.getComponent?.('component-open-gallery-link');
            const helpRendererElement = renderer?.element;
            openComponentsLink?.element?.click();
            const componentUrl = new URL(location.href);
            const componentConnected = Boolean(componentHost?.isConnected);
            const componentHelpText = componentHelpHost?.textContent || '';
            openGalleryLink?.element?.click();
            const themeRestored = workspaceTabs?.getActiveTabId?.() === 'theme' && Boolean(themeHost?.isConnected);

            ts.setToken('--cl-primary', 'rgb(12, 34, 56)');
            const livePrimary = getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim();
            const resolvedPrimary = ts.resolveColor('--cl-primary');
            const tokenJson = ts.tokensJson();
            const rootCss = ts.customCss();

            ts.commands['theme.select-component']({}, 'BasicButton');
            ts.commands['theme.component-class']({}, 'smoke-basic-button');
            ts.setScopedToken('BasicButton', '--cl-radius-md', '3px');
            const basicHost = ts.galleryHosts.get('BasicButton');
            const scopedClass = ts.componentTweaks.BasicButton?.className || '';
            const scopedLive = basicHost
                ? getComputedStyle(basicHost).getPropertyValue('--cl-radius-md').trim()
                : '';
            const scopedJson = ts.tokensJson();
            const scopedCss = ts.customCss();
            const rootIndex = scopedCss.indexOf(':root');
            const scopedIndex = scopedCss.indexOf('.smoke-basic-button');

            const gallery = ts.gallery;
            const galleryHost = ts.galleryHost;
            const csp = document.querySelector('meta[http-equiv="Content-Security-Policy"]')?.content || '';
            return {
                renderer: {
                    exposed: Boolean(renderer && studio?.renderer === renderer && ts.renderer === renderer),
                    page: document.querySelector('[data-tool-page="ThemeStudioPage"]') != null,
                    definition: studio?.definition?.name || '',
                },
                tabs: {
                    ids: tabIds,
                    initialTab,
                    themeMarker: themeTab?.dataset.studioTab || '',
                    componentMarker: componentTab?.dataset.studioTab || '',
                    themeQueryCleared: !themeUrl.searchParams.has('tab'),
                    componentQuery: componentUrl.searchParams.get('tab'),
                    hostsPreserved: initialThemeHost === renderer?.getHost?.('theme-workspace')
                        && initialComponentHost === renderer?.getHost?.('component-workspace')
                        && themeConnected && componentConnected && themeRestored,
                },
                controls: {
                    colors: renderer?.getHost?.('theme-colors')?.querySelectorAll('input').length || 0,
                    tokenTabs: Boolean(renderer?.getComponent?.('theme-token-tabs')),
                },
                help: {
                    themeText: themeHelpHost?.textContent || '',
                    componentText: componentHelpText,
                    hrefs: [openComponentsLink?.getHref?.(), openGalleryLink?.getHref?.()],
                    forwardActive: componentUrl.searchParams.get('tab') === 'components',
                    backActive: workspaceTabs?.getActiveTabId?.() === 'theme' && !new URL(location.href).searchParams.has('tab'),
                    rendererPreserved: renderer?.element === helpRendererElement,
                },
                gallery: {
                    ...gallery,
                    canvas: galleryHost?.querySelectorAll('canvas').length || 0,
                    svg: document.querySelectorAll('svg').length,
                    zone: galleryHost?.dataset.studioZone || '',
                },
                token: { beforePrimary, livePrimary, resolvedPrimary, tokenJson, rootCss },
                scoped: {
                    scopedClass,
                    hostClass: basicHost?.classList.contains('smoke-basic-button') || false,
                    scopedLive,
                    scopedJson,
                    scopedCss,
                    rootIndex,
                    scopedIndex,
                },
                csp,
                inlinePolicy: {
                    htmlStyleElements: document.querySelectorAll('style').length,
                    inlineHandlers: document.querySelectorAll('[onclick], [onchange], [oninput], [onload], [onerror]').length,
                },
            };
        })()`);

        addResult(results, 'renderer hook 與 ToolPageDefinition 正確', checks.renderer.exposed && checks.renderer.page && checks.renderer.definition === 'ThemeStudioPage', JSON.stringify(checks.renderer));
        addResult(results, 'outer tabs 固定為 theme/components', checks.tabs.ids.join(',') === 'theme,components' && checks.tabs.themeMarker === 'theme' && checks.tabs.componentMarker === 'custom', JSON.stringify(checks.tabs));
        addResult(results, '?tab=components deep link 正確啟用', checks.tabs.initialTab === 'components', `initial=${checks.tabs.initialTab}`);
        addResult(results, '切頁使用 history replace 且保留兩側 DOM/state', checks.tabs.themeQueryCleared && checks.tabs.componentQuery === 'components' && checks.tabs.hostsPreserved, JSON.stringify(checks.tabs));
        addResult(results, 'JSON token controls 與內層 tabs 已渲染', checks.controls.colors >= 10 && checks.controls.tokenTabs, JSON.stringify(checks.controls));
        addResult(results, '導覽與組合頁均有說明且 Link 同頁往返', checks.help.themeText.includes('元件導覽與展示') && checks.help.componentText.includes('元件組合使用說明') && checks.help.componentText.includes('原子元件 atomic') && checks.help.componentText.includes('複合元件 composite') && checks.help.componentText.includes('模板元件 template') && checks.help.hrefs.join('|') === '?tab=components|?tab=theme' && checks.help.forwardActive && checks.help.backActive && checks.help.rendererPreserved, JSON.stringify(checks.help));
        addResult(results, 'gallery 精確覆蓋 catalog 115', checks.gallery.total === 115 && checks.gallery.rendered + checks.gallery.skipped + checks.gallery.failed === 115 && checks.gallery.zone === 'gallery', JSON.stringify(checks.gallery));
        addResult(results, 'gallery 預覽無內部 render failure', checks.gallery.failed === 0, `failed=${checks.gallery.failed}`);
        addResult(results, 'Canvas 實際渲染且 SVG 硬零', checks.gallery.canvas > 0 && checks.gallery.svg === 0, `canvas=${checks.gallery.canvas}, svg=${checks.gallery.svg}`);
        addResult(results, 'global token set/resolve 即時一致', checks.token.livePrimary === 'rgb(12, 34, 56)' && checks.token.resolvedPrimary === 'rgb(12, 34, 56)' && checks.token.beforePrimary !== checks.token.livePrimary, JSON.stringify({ before: checks.token.beforePrimary, live: checks.token.livePrimary, resolved: checks.token.resolvedPrimary }));
        addResult(results, 'tokensJson 含 global override', /"--cl-primary"\s*:\s*"rgb\(12, 34, 56\)"/.test(checks.token.tokenJson));
        addResult(results, 'customCss 含 :root global override', /:root\s*\{[^}]*--cl-primary:\s*rgb\(12, 34, 56\)/s.test(checks.token.rootCss));
        addResult(results, 'component scoped token/class 即時生效', checks.scoped.scopedClass === 'smoke-basic-button' && checks.scoped.hostClass && checks.scoped.scopedLive === '3px', JSON.stringify(checks.scoped));
        addResult(results, 'tokensJson 含 scoped component 資料', /"BasicButton"/.test(checks.scoped.scopedJson) && /"className"\s*:\s*"smoke-basic-button"/.test(checks.scoped.scopedJson) && /"--cl-radius-md"\s*:\s*"3px"/.test(checks.scoped.scopedJson));
        addResult(results, 'customCss scoped class 位於 :root 之後', checks.scoped.scopedIndex > checks.scoped.rootIndex && /\.smoke-basic-button\s*\{[^}]*--cl-radius-md:\s*3px/s.test(checks.scoped.scopedCss), `root=${checks.scoped.rootIndex}, scoped=${checks.scoped.scopedIndex}`);
        addResult(results, 'strict CSP 且無 HTML inline style/handler', /script-src 'self'/.test(checks.csp) && /style-src 'self'/.test(checks.csp) && /object-src 'none'/.test(checks.csp) && !/unsafe-inline|unsafe-eval/.test(checks.csp) && checks.inlinePolicy.htmlStyleElements === 0 && checks.inlinePolicy.inlineHandlers === 0, checks.csp);

        await sleep(500);
        addResult(results, '無 console/page/HTTP error', fatal.length === 0, fatal.slice(0, 8).join(' | '));
        return { results, fatal, executable };
    } finally {
        if (launched) {
            await stopEdge(launched.browserProcess, launched.cdp, launched.profileDirectory);
        }
        await closeServer(server);
    }
}

let outcome;
try {
    outcome = await run();
} catch (error) {
    console.error(`Theme Studio smoke could not run: ${error.stack || error}`);
    process.exitCode = 1;
}

if (outcome) {
    // No screenshot or repository-local test artifact is produced by this harness.
    let passed = 0;
    let failed = 0;
    for (const result of outcome.results) {
        if (result.pass) {
            passed += 1;
            console.log(`ok   ${result.name}`);
        } else {
            failed += 1;
            console.log(`FAIL ${result.name}${result.detail ? ` — ${result.detail}` : ''}`);
        }
    }
    console.log(`\nTheme Studio self-host smoke: ${passed}/${passed + failed} passed (${path.basename(outcome.executable)})`);
    process.exitCode = failed === 0 ? 0 : 1;
}
