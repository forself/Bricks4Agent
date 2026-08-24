import { createHash } from 'node:crypto';
import { createReadStream, existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath, pathToFileURL } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..', '..');
const definitionRelativePath = 'tools/theme-studio/studio.page.json';
const definitionPath = path.join(repoRoot, ...definitionRelativePath.split('/'));
const definitionUrlPath = `/${definitionRelativePath}`;
const staticOnly = process.argv.includes('--static-only');
const headed = process.argv.includes('--headed');

const results = [];
const failures = [];
const check = (name, pass, detail = '') => {
    const result = { name, pass: Boolean(pass), detail };
    results.push(result);
    if (!result.pass) failures.push(`${name}${detail ? `: ${detail}` : ''}`);
};

function walk(directory) {
    if (!existsSync(directory)) return [];
    const files = [];
    for (const name of readdirSync(directory)) {
        const target = path.join(directory, name);
        if (statSync(target).isDirectory()) files.push(...walk(target));
        else files.push(target);
    }
    return files;
}

function relative(file) {
    return path.relative(repoRoot, file).replaceAll('\\', '/');
}

function stripSourceComments(source, extension) {
    if (extension === '.html') return source.replace(/<!--[\s\S]*?-->/g, '');
    return source
        .replace(/\/\*[\s\S]*?\*\//g, '')
        .replace(/^\s*\/\/.*$/gm, '');
}

function findHandwrittenInteractiveSources() {
    const studioRoots = [
        path.join(repoRoot, 'tools', 'theme-studio'),
        path.join(repoRoot, 'tools', 'custom-component-studio'),
    ];
    const findings = [];
    const patterns = [
        { label: 'iframe markup', regex: /<iframe\b/gi },
        { label: 'iframe DOM construction', regex: /(?:document\s*\.\s*)?createElement\s*\(\s*['"]iframe['"]\s*\)/gi },
        { label: 'raw interactive markup', regex: /<(?:button|input|select|textarea)\b/gi },
        {
            label: 'raw interactive DOM construction',
            regex: /(?:document\s*\.\s*)?createElement\s*\(\s*['"](?:button|input|select|textarea)['"]\s*\)/gi,
        },
    ];

    for (const root of studioRoots) {
        for (const file of walk(root)) {
            const extension = path.extname(file).toLowerCase();
            if (!['.html', '.js', '.mjs'].includes(extension)) continue;
            const source = stripSourceComments(readFileSync(file, 'utf8'), extension);
            for (const pattern of patterns) {
                const count = (source.match(pattern.regex) ?? []).length;
                if (count > 0) findings.push(`${relative(file)}: ${pattern.label} x${count}`);
            }
        }
    }
    return findings;
}

function sha256(source) {
    return createHash('sha256').update(source).digest('hex');
}

let definition = null;
let definitionSource = '';
let definitionHash = '';

const definitionFiles = [
    ...walk(path.join(repoRoot, 'tools', 'theme-studio')),
    ...walk(path.join(repoRoot, 'tools', 'custom-component-studio')),
].filter((file) => file.endsWith('.page.json'));
check(
    'Studios have exactly one authoritative PageDefinition JSON',
    definitionFiles.length === 1 && path.resolve(definitionFiles[0]) === definitionPath,
    definitionFiles.map(relative).join(', ') || 'no *.page.json found',
);

if (existsSync(definitionPath)) {
    definitionSource = readFileSync(definitionPath, 'utf8');
    definitionHash = sha256(definitionSource);
    try {
        definition = JSON.parse(definitionSource);
        check('Authoritative Studio definition is valid JSON data', true, definitionHash);
    } catch (error) {
        check('Authoritative Studio definition is valid JSON data', false, error.message);
    }
} else {
    check('Authoritative Studio definition is valid JSON data', false, `${definitionRelativePath} is missing`);
}

if (definition) {
    try {
        const validatorUrl = pathToFileURL(path.join(
            repoRoot,
            'packages',
            'javascript',
            'browser',
            'page-generator',
            'ToolPageDefinition.js',
        )).href;
        const { validateToolPageDefinition } = await import(validatorUrl);
        const validation = validateToolPageDefinition(definition);
        check(
            'Authoritative Studio definition passes ToolPageDefinition validation',
            validation?.valid === true && Array.isArray(validation.errors) && validation.errors.length === 0,
            JSON.stringify(validation?.errors ?? validation),
        );
    } catch (error) {
        check('Authoritative Studio definition passes ToolPageDefinition validation', false, error.message);
    }

    const definitionNodes = new Map();
    const visitDefinition = (node) => {
        if (!node || typeof node !== 'object') return;
        if (node.id) definitionNodes.set(node.id, node);
        if (node.type === 'group') (node.children ?? []).forEach(visitDefinition);
        if (node.type === 'tabs') (node.tabs ?? []).forEach((tab) => visitDefinition(tab.content));
    };
    visitDefinition(definition.root);
    const componentHelp = definitionNodes.get('component-help');
    const galleryHelp = definitionNodes.get('theme-gallery-help');
    const publishCode = definitionNodes.get('component-help-commands')?.options?.code ?? '';
    check(
        'Studio JSON includes visible gallery and composition guidance with official links',
        galleryHelp?.type === 'group' && componentHelp?.type === 'group' &&
        definitionNodes.get('theme-open-components-link')?.component === 'Link' &&
        definitionNodes.get('theme-open-components-link')?.events?.onClick === 'studio.open-components' &&
        definitionNodes.get('theme-form-application-link')?.options?.href === '/tools/form-application-studio/index.html' &&
        definitionNodes.get('component-help-doc-link')?.options?.href === '/CUSTOM-COMPONENTS.md' &&
        definitionNodes.get('component-open-gallery-link')?.events?.onClick === 'studio.open-theme' &&
        definitionNodes.get('component-form-application-link')?.options?.href === '/tools/form-application-studio/index.html' &&
        ['custom-components:build', 'custom-components:check', 'test:custom-components']
            .every((command) => publishCode.includes(command)),
        [...definitionNodes.keys()].filter((id) => id.includes('help')).join(', '),
    );
}

const handwritten = findHandwrittenInteractiveSources();
check(
    'Studio sources contain no iframe or hand-built interactive controls',
    handwritten.length === 0,
    handwritten.join(' | '),
);

const mimeTypes = new Map([
    ['.css', 'text/css; charset=utf-8'],
    ['.html', 'text/html; charset=utf-8'],
    ['.js', 'text/javascript; charset=utf-8'],
    ['.json', 'application/json; charset=utf-8'],
    ['.mjs', 'text/javascript; charset=utf-8'],
]);

async function loadChromium() {
    const candidates = [
        'playwright',
        'playwright-core',
        new URL('../../../tim-web/poc/node_modules/playwright-core/index.js', import.meta.url).href,
    ];
    for (const candidate of candidates) {
        try {
            const module = await import(candidate);
            const chromium = module.chromium ?? module.default?.chromium;
            if (chromium) return chromium;
        } catch {
            // Use a pre-existing local runtime only; this audit never installs packages.
        }
    }
    return null;
}

function createStaticServer() {
    const rootPrefix = `${repoRoot}${path.sep}`;
    const server = createServer((request, response) => {
        try {
            const requestUrl = new URL(request.url ?? '/', 'http://127.0.0.1');
            const relativePath = decodeURIComponent(requestUrl.pathname).replace(/^\/+/, '');
            const resolvedPath = path.resolve(repoRoot, relativePath);
            if (resolvedPath !== repoRoot && !resolvedPath.startsWith(rootPrefix)) {
                response.writeHead(403).end('Forbidden');
                return;
            }
            if (!existsSync(resolvedPath) || !statSync(resolvedPath).isFile()) {
                response.writeHead(404).end('Not Found');
                return;
            }
            response.writeHead(200, {
                'Content-Type': mimeTypes.get(path.extname(resolvedPath).toLowerCase()) ?? 'application/octet-stream',
                'Cache-Control': 'no-store',
            });
            createReadStream(resolvedPath).pipe(response);
        } catch {
            response.writeHead(500).end('Server Error');
        }
    });
    return new Promise((resolve) => {
        server.listen(0, '127.0.0.1', () => {
            const address = server.address();
            resolve({ server, baseUrl: `http://127.0.0.1:${address.port}` });
        });
    });
}

function closeServer(server) {
    return new Promise((resolve, reject) => {
        server.close((error) => error ? reject(error) : resolve());
    });
}

async function launchBrowser(chromium) {
    try {
        return await chromium.launch({ channel: 'msedge', headless: !headed });
    } catch (edgeError) {
        try {
            return await chromium.launch({ headless: !headed });
        } catch (defaultError) {
            throw new Error(`Unable to launch Edge (${edgeError.message}) or bundled Chromium (${defaultError.message}).`);
        }
    }
}

async function auditBrowserEntry(browser, baseUrl, entry) {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage();
    const runtimeErrors = [];
    const failedResponses = [];
    const requestFailures = [];

    await page.addInitScript(() => {
        window.__selfHostCspViolations = [];
        window.addEventListener('securitypolicyviolation', (event) => {
            window.__selfHostCspViolations.push({
                blockedURI: event.blockedURI,
                violatedDirective: event.violatedDirective,
            });
        });
    });
    page.on('pageerror', (error) => runtimeErrors.push(`pageerror: ${error.message}`));
    page.on('console', (message) => {
        if (message.type() === 'error') runtimeErrors.push(`console: ${message.text()}`);
    });
    page.on('requestfailed', (request) => {
        requestFailures.push(`${request.failure()?.errorText ?? 'request failed'} ${request.url()}`);
    });
    page.on('response', (response) => {
        if (response.url().startsWith(baseUrl) && response.status() >= 400) {
            failedResponses.push(`${response.status()} ${response.url()}`);
        }
    });

    await page.goto(`${baseUrl}${entry.url}`, { waitUntil: 'load' });
    try {
        await page.waitForFunction(() => (
            Boolean(window.__studio?.renderer) ||
            Boolean(window.__studioError) ||
            Boolean(window.__customComponentStudioError)
        ), null, { timeout: 20000 });
    } catch (error) {
        check(`${entry.label}: renderer hook becomes ready`, false, [
            error.message,
            ...runtimeErrors,
            ...failedResponses,
            ...requestFailures,
        ].join(' | '));
        await context.close();
        return;
    }

    const evidence = await page.evaluate(async ({ expectedPath, expectedHash, expectedActive }) => {
        const studio = window.__studio;
        const renderer = studio?.renderer;
        const sourceUrl = studio?.definitionUrl ? new URL(studio.definitionUrl, location.href) : null;
        const definition = sourceUrl ? await fetch(sourceUrl).then((response) => response.json()) : null;

        const nodes = new Map();
        const visit = (node) => {
            if (!node || typeof node !== 'object') return;
            if (node.id) nodes.set(node.id, node);
            if (node.type === 'group') (node.children ?? []).forEach(visit);
            if (node.type === 'tabs') (node.tabs ?? []).forEach((tab) => visit(tab.content));
        };
        visit(definition?.root);

        const [{ ComponentFactory }, { TabContainer }] = await Promise.all([
            import('/packages/javascript/browser/ui_components/binding/ComponentFactory.js'),
            import('/packages/javascript/browser/ui_components/layout/TabContainer/TabContainer.js'),
        ]);
        const records = renderer?.controlRecords;
        const rendererRoot = renderer?.element ?? renderer?.root ?? renderer?.getHost?.(definition?.root?.id) ?? document.body;
        const galleryHost = window.__ts?.galleryHost;
        const isExcluded = (element) => (
            element.closest('.ccs-preview, .component-preview, [data-studio-zone="gallery"], .theme-gallery, .ts-gallery, #ts-gallery') !== null ||
            (galleryHost instanceof Element && galleryHost.contains(element))
        );
        const interactiveSelector = 'a[href],button,input,select,textarea,[role="button"],[role="switch"],[role="slider"]';
        const scanInteractive = () => rendererRoot instanceof Element
            ? [...rendererRoot.querySelectorAll(interactiveSelector)].filter((element) => !isExcluded(element))
            : [];
        const interactive = scanInteractive();

        const ownsElement = (instance, element) => {
            const candidates = [
                instance?.element,
                instance?.container,
                instance?.button,
                instance?.fileInput,
                instance?.input,
                instance?.selector,
                instance?.tabNav,
                instance?.tabContent,
            ].filter((candidate) => candidate instanceof Element);
            if (instance?.containerId) {
                const container = document.getElementById(instance.containerId);
                if (container) candidates.push(container);
            }
            return candidates.some((candidate) => candidate === element || candidate.contains(element));
        };

        const provenanceErrors = [];
        if (!(records instanceof Map)) {
            provenanceErrors.push('renderer.controlRecords is not a Map');
        } else {
            for (const element of interactive) {
                const record = records.get(element);
                if (!record) {
                    provenanceErrors.push(`unregistered ${element.tagName.toLowerCase()}#${element.id || '-'}`);
                    continue;
                }
                const node = nodes.get(record.nodeId);
                const expectedConstructor = node?.type === 'tabs'
                    ? TabContainer
                    : node?.type === 'component'
                        ? ComponentFactory.getComponentClass(node.component)
                        : null;
                if (!node) provenanceErrors.push(`unknown nodeId ${record.nodeId}`);
                if (!expectedConstructor || !(record.instance instanceof expectedConstructor)) {
                    provenanceErrors.push(`${record.nodeId}: instance is not its official constructor`);
                }
                if (record.renderer !== renderer) provenanceErrors.push(`${record.nodeId}: renderer mismatch`);
                const expectedCommands = Object.values(node?.events ?? {}).sort();
                const commandIds = Array.isArray(record.commandIds) ? [...record.commandIds].sort() : null;
                if (JSON.stringify(commandIds) !== JSON.stringify(expectedCommands)) {
                    provenanceErrors.push(`${record.nodeId}: commandIds mismatch`);
                }
                if (!ownsElement(record.instance, element)) {
                    provenanceErrors.push(`${record.nodeId}: interactive is outside instance root`);
                }
            }
        }

        const findBare = () => scanInteractive().filter((element) => !records?.has?.(element));
        const realBareCount = findBare().length;
        let negativeDetectorCaught = false;
        if (rendererRoot instanceof Element) {
            const raw = document.createElement('button');
            raw.type = 'button';
            rendererRoot.appendChild(raw);
            negativeDetectorCaught = findBare().includes(raw);
            raw.remove();
        }

        let outerTabs = null;
        for (const node of nodes.values()) {
            if (node.type === 'tabs') {
                const ids = (node.tabs ?? []).map((tab) => tab.id);
                if (ids.includes('theme') && ids.includes('components')) {
                    outerTabs = renderer.getComponent(node.id);
                    break;
                }
            }
        }
        const activeTabId = outerTabs?.getActiveTabId?.() ?? outerTabs?.activeTabId ?? null;

        return {
            iframeCount: document.querySelectorAll('iframe').length,
            hookIdentity: Boolean(
                renderer &&
                window.__ts?.renderer === renderer &&
                window.__customComponentStudio?.renderer === renderer
            ),
            definitionPath: sourceUrl?.pathname ?? null,
            definitionHash: studio?.definitionHash ?? null,
            expectedPath,
            expectedHash,
            rendererRootFound: rendererRoot instanceof Element,
            recordsIsMap: records instanceof Map,
            recordsSize: records instanceof Map ? records.size : 0,
            interactiveCount: interactive.length,
            provenanceErrors,
            realBareCount,
            negativeDetectorCaught,
            activeTabId,
            expectedActive,
            cspViolations: window.__selfHostCspViolations ?? [],
            startupError: window.__studioError ?? window.__customComponentStudioError ?? '',
        };
    }, {
        expectedPath: definitionUrlPath,
        expectedHash: definitionHash,
        expectedActive: entry.activeTab,
    });

    check(`${entry.label}: same-page renderer hooks share one instance`, evidence.hookIdentity, JSON.stringify(evidence));
    check(`${entry.label}: source URL and SHA-256 identify the authoritative JSON`, (
        evidence.definitionPath === evidence.expectedPath &&
        evidence.definitionHash === evidence.expectedHash
    ), JSON.stringify({ path: evidence.definitionPath, hash: evidence.definitionHash }));
    check(`${entry.label}: uses no iframe`, evidence.iframeCount === 0, `iframe=${evidence.iframeCount}`);
    check(`${entry.label}: opens the expected workspace tab`, evidence.activeTabId === evidence.expectedActive, JSON.stringify(evidence));
    check(`${entry.label}: every chrome control has official renderer provenance`, (
        evidence.rendererRootFound &&
        evidence.recordsIsMap &&
        evidence.recordsSize > 0 &&
        evidence.interactiveCount > 0 &&
        evidence.realBareCount === 0 &&
        evidence.provenanceErrors.length === 0
    ), evidence.provenanceErrors.join(' | ') || JSON.stringify(evidence));
    check(`${entry.label}: bare-control detector negative self-test works`, evidence.negativeDetectorCaught, JSON.stringify(evidence));
    check(`${entry.label}: CSP, page, console, request, and HTTP errors stay at zero`, (
        evidence.cspViolations.length === 0 &&
        !evidence.startupError &&
        runtimeErrors.length === 0 &&
        failedResponses.length === 0 &&
        requestFailures.length === 0
    ), [
        evidence.startupError,
        ...evidence.cspViolations.map((item) => `${item.violatedDirective}: ${item.blockedURI}`),
        ...runtimeErrors,
        ...failedResponses,
        ...requestFailures,
    ].filter(Boolean).join(' | '));

    await context.close();
}

if (!staticOnly && definition) {
    const chromium = await loadChromium();
    if (!chromium) {
        check('A local Playwright runtime is available for the mandatory browser audit', false, 'use --static-only only for an explicit static preflight');
    } else {
        const { server, baseUrl } = await createStaticServer();
        let browser;
        try {
            browser = await launchBrowser(chromium);
            await auditBrowserEntry(browser, baseUrl, {
                label: 'Theme Studio entry',
                url: '/tools/theme-studio/index.html',
                activeTab: 'theme',
            });
            await auditBrowserEntry(browser, baseUrl, {
                label: 'Custom Studio compatibility entry',
                url: '/tools/custom-component-studio/index.html',
                activeTab: 'components',
            });
        } catch (error) {
            check('Studio browser self-host audit completes', false, error.stack || error.message);
        } finally {
            if (browser) await browser.close();
            await closeServer(server);
        }
    }
}

for (const result of results) {
    console.log(`${result.pass ? 'ok  ' : 'FAIL'} ${result.name}${result.pass || !result.detail ? '' : ` :: ${result.detail}`}`);
}
console.log(`\nStudio self-host audit: ${results.length - failures.length}/${results.length} passed.`);
if (failures.length > 0) {
    throw new Error(`Studio self-host audit failed:\n- ${failures.join('\n- ')}`);
}
