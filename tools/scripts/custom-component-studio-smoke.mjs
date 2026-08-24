import { createReadStream, existsSync, readFileSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..', '..');
const requireBrowser = process.argv.includes('--require-browser');

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
            // Try the next local runtime. This script never installs dependencies.
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

const chromium = await loadChromium();
if (!chromium) {
    if (requireBrowser) {
        throw new Error('Custom component browser smoke requires a local Playwright runtime.');
    }
    console.log('Custom component browser smoke skipped: Playwright is unavailable.');
    process.exit(0);
}

const { server, baseUrl } = await createStaticServer();
let browser;
const results = [];
const failures = [];
const check = (name, pass, detail = '') => {
    results.push({ name, pass: Boolean(pass), detail });
    if (!pass) failures.push(`${name}${detail ? `: ${detail}` : ''}`);
};

try {
    browser = await chromium.launch({ channel: 'msedge', headless: true });
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, acceptDownloads: true });
    const page = await context.newPage();
    const browserErrors = [];
    const failedResponses = [];

    page.on('pageerror', (error) => browserErrors.push(`pageerror: ${error.message}`));
    page.on('console', (message) => {
        if (message.type() === 'error') browserErrors.push(`console: ${message.text()}`);
    });
    page.on('response', (response) => {
        if (response.url().startsWith(baseUrl) && response.status() >= 400) {
            failedResponses.push(`${response.status()} ${response.url()}`);
        }
    });

    await page.goto(`${baseUrl}/tools/custom-component-studio/index.html`, { waitUntil: 'load' });
    try {
        await page.waitForFunction(
            () => window.__customComponentStudio?.ready === true || Boolean(window.__customComponentStudioError),
            null,
            { timeout: 20000 },
        );
    } catch (error) {
        throw new Error(`Studio did not become ready: ${[...browserErrors, ...failedResponses].join(' | ') || error.message}`);
    }
    const startupError = await page.evaluate(() => window.__customComponentStudioError || '');
    if (startupError) throw new Error(`Studio startup error: ${startupError}`);
    await page.waitForFunction(() => window.__customComponentStudio?.preview != null, null, { timeout: 10000 });

    const initial = await page.evaluate(() => ({
        catalogCount: window.__customComponentStudio.catalogCount,
        registryCount: window.__customComponentStudio.registry.list().length,
        kind: window.__customComponentStudio.getDefinition().kind,
        valid: window.__customComponentStudio.validate().valid,
        inputs: document.querySelectorAll('.ccs-preview input').length,
        svg: document.querySelectorAll('svg').length,
    }));
    check('Studio loads the built-in catalog and custom folder', initial.catalogCount === 116 && initial.registryCount === 4, JSON.stringify(initial));
    check('Default draft is a valid atomic component with a live preview', initial.kind === 'atomic' && initial.valid && initial.inputs >= 1, JSON.stringify(initial));
    check('Studio and preview keep the SVG hard-zero rule', initial.svg === 0, `svg=${initial.svg}`);

    await page.evaluate(() => window.__customComponentStudio.actions.addBuiltIn('BasicButton'));
    await page.waitForFunction(() => window.__customComponentStudio.getDefinition().kind === 'composite');
    const composite = await page.evaluate(() => ({
        definition: window.__customComponentStudio.getDefinition(),
        analysis: window.__customComponentStudio.analyze(),
        hosts: document.querySelectorAll('.ccs-preview .custom-component__host').length,
    }));
    check(
        'Adding a second atom creates a composite group',
        composite.definition.root.type === 'group' && composite.definition.root.children.length === 2 && composite.hosts >= 2,
        JSON.stringify(composite.analysis),
    );

    await page.evaluate(async () => {
        const actions = window.__customComponentStudio.actions;
        actions.wrapSelected();
        actions.wrapSelected();
        actions.wrapSelected();
        await actions.rebuildPreview();
    });
    await page.waitForFunction(() => window.__customComponentStudio.getDefinition().kind === 'template');
    const templateBoundary = await page.evaluate(() => ({
        kind: window.__customComponentStudio.getDefinition().kind,
        analysis: window.__customComponentStudio.analyze(),
        valid: window.__customComponentStudio.validate().valid,
    }));
    check(
        'Four composition layers are automatically promoted to template',
        templateBoundary.kind === 'template' && templateBoundary.analysis.max_depth === 4 && templateBoundary.valid,
        JSON.stringify(templateBoundary),
    );

    const [download] = await Promise.all([
        page.waitForEvent('download'),
        page.evaluate(() => window.__customComponentStudio.actions.exportJson()),
    ]);
    const downloadPath = await download.path();
    const exported = JSON.parse(readFileSync(downloadPath, 'utf8'));
    check(
        'Export downloads validated JSON with the inferred kind',
        download.suggestedFilename() === 'CustomTextInput.json' && exported.kind === 'template' && exported.schema_version === 1,
        `${download.suggestedFilename()} ${JSON.stringify(exported)}`,
    );

    const samplePath = path.join(
        repoRoot,
        'packages',
        'javascript',
        'browser',
        'custom_components',
        'definitions',
        'application-toolbar.json',
    );
    const sampleBuffer = readFileSync(samplePath);
    const definitionBeforeRejectedImports = await page.evaluate(
        () => window.__customComponentStudio.getDefinition().registry_name,
    );
    const rejectedImports = [
        {
            name: 'wrong-extension.txt',
            mimeType: 'application/json',
            buffer: sampleBuffer,
        },
        {
            name: 'malformed.json',
            mimeType: 'application/json',
            buffer: Buffer.from('{"broken":'),
        },
        {
            name: 'unsafe.json',
            mimeType: 'application/json',
            buffer: Buffer.from('{"__proto__":{"polluted":true}}'),
        },
        {
            name: 'oversize.json',
            mimeType: 'application/json',
            buffer: Buffer.alloc((1024 * 1024) + 1, 0x20),
        },
    ];
    const rejectedImportResults = [];
    for (const rejectedImport of rejectedImports) {
        await page.evaluate(() => {
            window.__customComponentStudio.state.diagnostics = [];
        });
        await page.setInputFiles('#ccs-import-file', rejectedImport);
        await page.waitForFunction(() => window.__customComponentStudio.state.diagnostics.length > 0);
        rejectedImportResults.push(await page.evaluate(() => ({
            registryName: window.__customComponentStudio.getDefinition().registry_name,
            diagnostics: window.__customComponentStudio.state.diagnostics.length,
        })));
    }
    check(
        'Import rejects wrong extensions, malformed JSON, unsafe keys, and oversized files without changing the draft',
        rejectedImportResults.every((result) => (
            result.registryName === definitionBeforeRejectedImports && result.diagnostics > 0
        )),
        JSON.stringify(rejectedImportResults),
    );

    await page.setInputFiles('#ccs-import-file', samplePath);
    await page.waitForFunction(() => window.__customComponentStudio.getDefinition().registry_name === 'CustomApplicationToolbar');
    await page.waitForFunction(() => window.__customComponentStudio.preview != null);
    const imported = await page.evaluate(() => ({
        definition: window.__customComponentStudio.getDefinition(),
        valid: window.__customComponentStudio.validate().valid,
        previewError: window.__customComponentStudio.state.previewError,
        svg: document.querySelectorAll('svg').length,
    }));
    check('Studio imports and previews a two-composite template', imported.definition.kind === 'template' && imported.valid && !imported.previewError, JSON.stringify(imported));
    check('Imported template remains SVG-free', imported.svg === 0, `svg=${imported.svg}`);

    const runtime = await page.evaluate(async () => {
        const [{ CustomComponentRegistry }, { ComponentFactory }] = await Promise.all([
            import('/packages/javascript/browser/custom_components/index.js'),
            import('/packages/javascript/browser/ui_components/binding/ComponentFactory.js'),
        ]);
        const loadedRegistry = new CustomComponentRegistry({ registerWithFactory: true });
        await loadedRegistry.loadFolder('/packages/javascript/browser/custom_components/');
        const host = document.createElement('div');
        document.body.appendChild(host);
        const component = ComponentFactory.create('CustomSearchActions', {
            value: { 'search-input': 'initial value' },
        });
        component.mount(host);
        component.setValue({ 'search-input': 'round trip' });
        const value = component.getValue();
        const canvasCount = host.querySelectorAll('canvas').length;
        const svgCount = host.querySelectorAll('svg').length;
        component.destroy();
        const remaining = host.childNodes.length;
        loadedRegistry.dispose();
        const factoryRegistrationRemoved = !Object.hasOwn(ComponentFactory.registry, 'CustomSearchActions');
        host.remove();
        return { value, canvasCount, svgCount, remaining, factoryRegistrationRemoved };
    });
    check(
        'Folder load registers synchronous factory components with value round-trip',
        runtime.value?.['search-input'] === 'round trip' && runtime.canvasCount >= 1 && runtime.factoryRegistrationRemoved,
        JSON.stringify(runtime),
    );
    check('Runtime destroy removes the whole custom tree', runtime.remaining === 0 && runtime.svgCount === 0, JSON.stringify(runtime));

    const dynamic = await page.evaluate(async () => {
        const { DynamicPageRenderer } = await import('/packages/javascript/browser/page-generator/DynamicPageRenderer.js');
        const definition = {
            schema_version: 1,
            component_id: 'custom.dynamic_name',
            registry_name: 'CustomDynamicName',
            display_name: 'Dynamic name',
            version: '1.0.0',
            kind: 'atomic',
            root: {
                type: 'component',
                id: 'name-input',
                component: 'TextInput',
                options: { placeholder: 'Dynamic custom field' },
            },
        };
        const host = document.createElement('div');
        document.body.appendChild(host);
        const pageRenderer = new DynamicPageRenderer({
            mode: 'form',
            customComponents: [definition],
            definition: {
                fields: [{
                    fieldName: 'nickname',
                    fieldType: 'text',
                    component: 'CustomDynamicName',
                    label: 'Nickname',
                    defaultValue: 'Ada',
                    formRow: 1,
                    formCol: 12,
                    isRequired: true,
                    isReadonly: false,
                }],
            },
        });
        await pageRenderer.init();
        pageRenderer.mount(host);
        const initialValue = pageRenderer.getRenderer().getValues().nickname;
        pageRenderer.getRenderer().setValues({ nickname: 'Grace' });
        const updatedValue = pageRenderer.getRenderer().getValues().nickname;
        pageRenderer.destroy();
        const remaining = host.childNodes.length;
        host.remove();
        return { initialValue, updatedValue, remaining };
    });
    check(
        'DynamicPageRenderer waits for custom definitions and wires FieldResolver values',
        dynamic.initialValue === 'Ada' && dynamic.updatedValue === 'Grace' && dynamic.remaining === 0,
        JSON.stringify(dynamic),
    );

    check('Browser run has no page, console, or same-origin HTTP errors', browserErrors.length === 0 && failedResponses.length === 0, [...browserErrors, ...failedResponses].join(' | '));
    await context.close();
} finally {
    if (browser) await browser.close();
    await closeServer(server);
}

for (const result of results) {
    console.log(`${result.pass ? 'ok  ' : 'FAIL'} ${result.name}${result.pass || !result.detail ? '' : ` :: ${result.detail}`}`);
}
console.log(`\nCustom component browser smoke: ${results.length - failures.length}/${results.length} passed.`);
if (failures.length > 0) {
    throw new Error(`Custom component browser smoke failed:\n- ${failures.join('\n- ')}`);
}
