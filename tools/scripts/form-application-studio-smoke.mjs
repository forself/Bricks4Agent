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
    ['.md', 'text/markdown; charset=utf-8'],
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
            // This repository never installs browser dependencies during validation.
        }
    }
    return null;
}

function createStaticServer() {
    const prefix = `${repoRoot}${path.sep}`;
    const server = createServer((request, response) => {
        try {
            const requestUrl = new URL(request.url ?? '/', 'http://127.0.0.1');
            const relativePath = decodeURIComponent(requestUrl.pathname).replace(/^\/+/, '');
            const resolvedPath = path.resolve(repoRoot, relativePath);
            if (resolvedPath !== repoRoot && !resolvedPath.startsWith(prefix)) {
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
    return new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
}

const chromium = await loadChromium();
if (!chromium) {
    if (requireBrowser) throw new Error('Form Application Studio browser smoke requires a local Playwright runtime.');
    console.log('Form Application Studio browser smoke skipped: Playwright is unavailable.');
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
    const context = await browser.newContext({ viewport: { width: 1440, height: 1050 }, acceptDownloads: true });
    const page = await context.newPage();
    const browserErrors = [];
    const failedResponses = [];
    page.on('pageerror', (error) => browserErrors.push(`pageerror: ${error.message}`));
    page.on('console', (message) => {
        if (message.type() === 'error') browserErrors.push(`console: ${message.text()}`);
    });
    page.on('response', (response) => {
        if (response.url().startsWith(baseUrl) && response.status() >= 400) failedResponses.push(`${response.status()} ${response.url()}`);
    });

    await page.goto(`${baseUrl}/tools/form-application-studio/index.html`, { waitUntil: 'load' });
    await page.waitForFunction(
        () => window.__formApplicationStudioReady === true || Boolean(window.__formApplicationStudioError),
        null,
        { timeout: 20000 },
    );
    const startupError = await page.evaluate(() => window.__formApplicationStudioError || '');
    if (startupError) throw new Error(`Studio startup error: ${startupError}`);

    const initial = await page.evaluate(() => {
        const api = window.__formApplicationStudio;
        const renderer = api.renderer;
        const designer = renderer.getComponent('form-designer');
        return {
            fields: api.getDefinition().fields.length,
            rows: document.querySelectorAll('.form-designer__field-row').length,
            tiles: document.querySelectorAll('.form-designer__tile').length,
            canvas: document.querySelectorAll('.form-designer canvas').length,
            svg: document.querySelectorAll('svg').length,
            target: api.controller.effectiveTarget,
            sameDesigner: designer === renderer.getComponent('form-designer'),
            definitionUrl: api.definitionUrl,
        };
    });
    check('JSON self-host Studio loads schema into the official FormDesigner', initial.fields === 6 && initial.rows === 6 && initial.tiles === 6 && initial.sameDesigner, JSON.stringify(initial));
    check('Blank connection string visibly selects local SQLite', /SQLite/.test(initial.target) && /data\/customer_requests\.db/.test(initial.target), initial.target);
    check('Designer uses Canvas icons and keeps SVG hard-zero', initial.canvas > 0 && initial.svg === 0, JSON.stringify(initial));
    check('The authoritative page is studio.page.json', initial.definitionUrl.endsWith('/tools/form-application-studio/studio.page.json'), initial.definitionUrl);

    await page.evaluate(() => window.__formApplicationStudio.renderer.getComponent('workflow-tabs').activateTab('design'));
    await page.waitForFunction(() => {
        const tile = document.querySelector('.form-designer__tile');
        return Boolean(tile && tile.getBoundingClientRect().width > 0);
    });

    const beforeMove = await page.evaluate(() => window.__formApplicationStudio.getDefinition().fields.find((field) => field.field_id === 'field_email')?.layout);
    const dragHandle = page.locator('.form-designer__field-row[data-field-id="field_email"] .form-designer__drag-handle');
    const canvas = page.locator('.form-designer__canvas');
    await dragHandle.dragTo(canvas, { targetPosition: { x: 650, y: 350 } });
    const afterMove = await page.evaluate(() => window.__formApplicationStudio.getDefinition().fields.find((field) => field.field_id === 'field_email')?.layout);
    check('Mouse drag moves a schema field on the design canvas', JSON.stringify(beforeMove) !== JSON.stringify(afterMove), `${JSON.stringify(beforeMove)} -> ${JSON.stringify(afterMove)}`);

    const nameInput = page.locator('.form-designer__field-row[data-field-id="field_name"] input').first();
    await nameInput.fill('FullName');
    await nameInput.blur();
    await page.waitForFunction(() => window.__formApplicationStudio.getDefinition().fields.some((field) => field.column_name === 'FullName'));
    const rename = await page.evaluate(() => window.__formApplicationStudio.getDefinition().fields.find((field) => field.field_id === 'field_name'));
    check('Field list renames the mapped database column without losing field identity', rename?.column_name === 'FullName' && rename.field_id === 'field_name', JSON.stringify(rename));

    const beforeComponent = rename.input.component;
    await page.locator('.form-designer__field-row[data-field-id="field_name"] .form-designer__field-icon canvas').click();
    await page.waitForFunction((before) => window.__formApplicationStudio.getDefinition().fields.find((field) => field.field_id === 'field_name')?.input.component !== before, beforeComponent);
    const componentChange = await page.evaluate(() => window.__formApplicationStudio.getDefinition().fields.find((field) => field.field_id === 'field_name'));
    check('Clicking the Canvas icon changes input component but preserves the column mapping', componentChange.column_name === 'FullName' && componentChange.input.component !== beforeComponent, JSON.stringify(componentChange));

    const resizeHandle = page.locator('.form-designer__tile[data-field-id="field_notes"] .form-designer__resize-handle');
    const resizeBox = await resizeHandle.boundingBox();
    const beforeResize = await page.evaluate(() => window.__formApplicationStudio.getDefinition().fields.find((field) => field.field_id === 'field_notes')?.layout);
    if (resizeBox) {
        await page.mouse.move(resizeBox.x + resizeBox.width / 2, resizeBox.y + resizeBox.height / 2);
        await page.mouse.down();
        await page.mouse.move(resizeBox.x + 180, resizeBox.y + resizeBox.height / 2, { steps: 5 });
        await page.mouse.up();
    }
    const afterResize = await page.evaluate(() => window.__formApplicationStudio.getDefinition().fields.find((field) => field.field_id === 'field_notes')?.layout);
    check('Pointer resize changes field width or height within the 12-column contract', Boolean(resizeBox) && (afterResize.column_span !== beforeResize.column_span || afterResize.row_span !== beforeResize.row_span) && afterResize.column_span <= 12, `${JSON.stringify(beforeResize)} -> ${JSON.stringify(afterResize)}`);

    await page.locator('.form-designer__tile[data-field-id="field_birthdate"]').press('Alt+ArrowDown');
    await resizeHandle.press('ArrowDown');
    const keyboard = await page.evaluate(() => ({
        birth: window.__formApplicationStudio.getDefinition().fields.find((field) => field.field_id === 'field_birthdate')?.layout,
        notes: window.__formApplicationStudio.getDefinition().fields.find((field) => field.field_id === 'field_notes')?.layout,
    }));
    check('Keyboard alternatives move and resize fields', keyboard.birth?.row >= 1 && keyboard.notes?.row_span >= afterResize.row_span, JSON.stringify(keyboard));

    const added = await page.evaluate(() => window.__formApplicationStudio.renderer.getComponent('form-designer').addField());
    const afterAdd = await page.evaluate(() => window.__formApplicationStudio.getDefinition());
    check('New fields use a safe deterministic identifier and remain generation-valid', /^field_[0-9]+$/.test(added?.field_id || '') && afterAdd.fields.length === 7, JSON.stringify(added));

    const generation = await page.evaluate(async () => {
        const api = window.__formApplicationStudio;
        const secret = 'Host=db.example.test;Database=forms;Username=agent;Password=browser-secret';
        api.controller.commands['form.provider-change'](null, 'postgresql');
        api.controller.commands['form.connection-change'](null, secret);
        const bundle = api.controller.generate();
        const text = JSON.stringify(bundle);
        const host = document.createElement('div');
        document.body.appendChild(host);
        const { DynamicPageRenderer } = await import('/packages/javascript/browser/page-generator/DynamicPageRenderer.js');
        const preview = new DynamicPageRenderer({ mode: 'form', definition: bundle.pageDefinition });
        await preview.init();
        preview.mount(host);
        const inputCount = host.querySelectorAll('input,textarea,button').length;
        preview.destroy();
        const remaining = host.childNodes.length;
        host.remove();
        return {
            provider: bundle.sql.provider,
            definitionProvider: JSON.parse(bundle.files['definition/form-application.json']).persistence.provider,
            files: Object.keys(bundle.files).length,
            secretLeaked: text.includes(secret),
            pageFields: bundle.pageDefinition.fields.length,
            inputCount,
            remaining,
        };
    });
    check('Generation emits form, API, C#, SQL and provider-consistent artifacts', generation.provider === 'postgresql' && generation.definitionProvider === 'postgresql' && generation.files >= 9 && generation.pageFields === 7, JSON.stringify(generation));
    check('Generated PageDefinition renders through DynamicPageRenderer and cleans up', generation.inputCount >= 5 && generation.remaining === 0, JSON.stringify(generation));
    check('Connection secret is excluded unless backend-only opt-in is enabled', generation.secretLeaked === false, JSON.stringify(generation));

    const [designDownload] = await Promise.all([
        page.waitForEvent('download'),
        page.evaluate(() => window.__formApplicationStudio.controller.commands['form.export-design']()),
    ]);
    const designText = readFileSync(await designDownload.path(), 'utf8');
    check('Downloaded design JSON keeps the provider but excludes the session connection string', !designText.includes('browser-secret') && JSON.parse(designText).persistence.provider === 'postgresql' && JSON.parse(designText).persistence.connection_string == null, designDownload.suggestedFilename());

    const provenance = await page.evaluate(() => {
        const api = window.__formApplicationStudio;
        const records = [...api.controls.records.values()];
        const formDesigner = api.renderer.getComponent('form-designer');
        return {
            recordCount: records.length,
            formDesignerRecorded: records.some((record) => record.instance === formDesigner && record.nodeId === 'form-designer'),
            sameRenderer: records.every((record) => record.renderer === api.renderer),
        };
    });
    check('JSON chrome controls retain official component provenance', provenance.recordCount > 10 && provenance.formDesignerRecorded && provenance.sameRenderer, JSON.stringify(provenance));
    check('Browser run has no page, console, HTTP, CSP, or SVG errors', browserErrors.length === 0 && failedResponses.length === 0 && await page.locator('svg').count() === 0, [...browserErrors, ...failedResponses].join(' | '));

    const cleanup = await page.evaluate(() => {
        window.__formApplicationStudio.destroy();
        return document.getElementById('app')?.childNodes.length ?? -1;
    });
    check('Destroy removes the self-host tool tree and listeners', cleanup === 0, `remaining=${cleanup}`);
    await context.close();
} finally {
    if (browser) await browser.close();
    await closeServer(server);
}

for (const result of results) console.log(`${result.pass ? 'ok  ' : 'FAIL'} ${result.name}${result.pass || !result.detail ? '' : ` :: ${result.detail}`}`);
console.log(`\nForm Application Studio browser smoke: ${results.length - failures.length}/${results.length} passed.`);
if (failures.length > 0) throw new Error(`Form Application Studio browser smoke failed:\n- ${failures.join('\n- ')}`);
