import { createHash } from 'node:crypto';
import { createReadStream, existsSync, readFileSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import { isDeepStrictEqual } from 'node:util';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..', '..');
const definitionRelativePath = 'tools/theme-studio/studio.page.json';
const definitionPath = path.join(repoRoot, ...definitionRelativePath.split('/'));
const expectedDefinitionPath = `/${definitionRelativePath}`;
const expectedDefinitionHash = createHash('sha256').update(readFileSync(definitionPath)).digest('hex');
const headed = process.argv.includes('--headed');

const mimeTypes = new Map([
    ['.css', 'text/css; charset=utf-8'],
    ['.html', 'text/html; charset=utf-8'],
    ['.js', 'text/javascript; charset=utf-8'],
    ['.json', 'application/json; charset=utf-8'],
    ['.mjs', 'text/javascript; charset=utf-8'],
    ['.png', 'image/png'],
    ['.woff2', 'font/woff2'],
]);

const results = [];
const failures = [];
function check(name, pass, detail = '') {
    const result = { name, pass: Boolean(pass), detail };
    results.push(result);
    if (!result.pass) failures.push(`${name}${detail ? `: ${detail}` : ''}`);
}

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
            // This smoke consumes a pre-existing Playwright runtime only.
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
                'Cache-Control': 'no-store, no-cache, must-revalidate, max-age=0',
                Pragma: 'no-cache',
                Expires: '0',
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

async function clickRendererControl(page, nodeId) {
    return page.evaluate((requestedNodeId) => {
        const renderer = window.__studio?.renderer;
        const instance = renderer?.getComponent?.(requestedNodeId);
        const records = renderer?.controlRecords;
        if (!instance || !(records instanceof Map)) {
            return { ok: false, detail: 'renderer instance or controlRecords is missing' };
        }
        const matches = [...records.entries()].filter(([element, record]) => (
            element?.isConnected &&
            record?.nodeId === requestedNodeId &&
            record.instance === instance &&
            record.renderer === renderer
        ));
        const entry = matches.find(([element]) => element.matches?.('a[href],button,[role="button"]')) ?? matches[0];
        if (!entry) return { ok: false, detail: `no connected provenance control for ${requestedNodeId}` };
        const [element, record] = entry;
        element.click();
        return {
            ok: true,
            tag: element.tagName,
            nodeId: record.nodeId,
            commandIds: record.commandIds,
            constructor: record.instance?.constructor?.name,
        };
    }, nodeId);
}

async function downloadFromRendererControl(page, nodeId) {
    const downloadPromise = page.waitForEvent('download', { timeout: 15000 });
    const proof = await clickRendererControl(page, nodeId);
    if (!proof.ok) {
        await downloadPromise.catch(() => {});
        throw new Error(proof.detail);
    }
    const download = await downloadPromise;
    const downloadPath = await download.path();
    if (!downloadPath) throw new Error(`${nodeId} download has no local path.`);
    return { download, downloadPath, proof };
}

async function uploadThroughRendererControl(page, nodeId, file) {
    const handle = await page.evaluateHandle((requestedNodeId) => {
        const renderer = window.__studio?.renderer;
        const instance = renderer?.getComponent?.(requestedNodeId);
        const input = instance?.fileInput;
        const record = renderer?.controlRecords?.get?.(input);
        if (
            !(input instanceof HTMLInputElement) || input.type !== 'file' ||
            record?.nodeId !== requestedNodeId ||
            record?.instance !== instance ||
            record?.renderer !== renderer
        ) return null;
        return input;
    }, nodeId);
    try {
        const element = handle.asElement();
        if (!element) throw new Error(`No provenance-backed UploadButton input for ${nodeId}.`);
        await element.setInputFiles(file);
    } finally {
        await handle.dispose();
    }
}

const chromium = await loadChromium();
if (!chromium) {
    throw new Error('Studio integration smoke requires a local Playwright runtime and Microsoft Edge.');
}

const { server, baseUrl } = await createStaticServer();
let browser;
let context;
try {
    // A new browser process and a new non-persistent context make this a fresh Edge run.
    browser = await chromium.launch({ channel: 'msedge', headless: !headed });
    context = await browser.newContext({
        viewport: { width: 1600, height: 1100 },
        acceptDownloads: true,
    });
    const page = await context.newPage();
    const consoleErrors = [];
    const pageErrors = [];
    const failedResponses = [];
    const requestFailures = [];

    await page.addInitScript(() => {
        window.__integrationCspViolations = [];
        window.addEventListener('securitypolicyviolation', (event) => {
            window.__integrationCspViolations.push({
                blockedURI: event.blockedURI,
                violatedDirective: event.violatedDirective,
            });
        });
    });
    page.on('console', (message) => {
        if (message.type() === 'error') consoleErrors.push(message.text());
    });
    page.on('pageerror', (error) => pageErrors.push(error.message));
    page.on('response', (response) => {
        if (response.status() >= 400) failedResponses.push(`${response.status()} ${response.url()}`);
    });
    page.on('requestfailed', (request) => {
        requestFailures.push(`${request.failure()?.errorText ?? 'request failed'} ${request.url()}`);
    });

    await page.goto(`${baseUrl}/tools/theme-studio/index.html`, { waitUntil: 'load' });
    try {
        await page.waitForFunction(() => window.__studioReady === true || Boolean(window.__studioError), null, { timeout: 30000 });
    } catch (error) {
        const browserError = await page.evaluate(() => window.__studioError || window.__studioBootPhase || '');
        throw new Error(`Studio did not become ready: ${browserError || error.message}`);
    }
    const startup = await page.evaluate(() => ({
        ready: window.__studioReady === true,
        error: window.__studioError || '',
        phase: window.__studioBootPhase || '',
        studioErrors: window.__studioErrors || [],
    }));
    check('Studio reaches __studioReady in fresh Edge', startup.ready && !startup.error, JSON.stringify(startup));

    const outer = await page.evaluate(() => {
        const renderer = window.__studio?.renderer;
        const tabs = renderer?.getComponent?.('workspace-tabs');
        const theme = tabs?.tabMap?.get('theme');
        const components = tabs?.tabMap?.get('components');
        const records = renderer?.controlRecords;
        const themeRecord = records?.get?.(theme?.tabButton);
        const componentRecord = records?.get?.(components?.tabButton);
        window.__integrationIdentity = {
            renderer,
            tabs,
            rendererElement: renderer?.element,
            themePanel: theme?.panel,
            componentsPanel: components?.panel,
            themeWorkspace: renderer?.getHost?.('theme-workspace'),
            componentWorkspace: renderer?.getHost?.('component-workspace'),
            tokenPrimary: renderer?.getComponent?.('token-primary'),
            customApi: window.__customComponentStudio,
        };
        return {
            hookIdentity: Boolean(
                renderer &&
                window.__ts?.renderer === renderer &&
                window.__customComponentStudio?.renderer === renderer
            ),
            tabIdentity: Boolean(
                tabs && theme?.tabButton && components?.tabButton &&
                themeRecord?.instance === tabs && themeRecord?.renderer === renderer && themeRecord?.nodeId === 'workspace-tabs' &&
                componentRecord?.instance === tabs && componentRecord?.renderer === renderer && componentRecord?.nodeId === 'workspace-tabs'
            ),
            bothPanelsConnected: Boolean(theme?.panel?.isConnected && components?.panel?.isConnected),
            activeTab: tabs?.getActiveTabId?.() ?? tabs?.activeTabId,
            iframeCount: document.querySelectorAll('iframe').length,
        };
    });
    check(
        'Outer theme/components tabs share one renderer and stay in one document',
        outer.hookIdentity && outer.tabIdentity && outer.bothPanelsConnected && outer.activeTab === 'theme' && outer.iframeCount === 0,
        JSON.stringify(outer),
    );

    const helpNavigation = await page.evaluate(async () => {
        const renderer = window.__studio.renderer;
        const tabs = renderer.getComponent('workspace-tabs');
        const records = renderer.controlRecords;
        const themeLink = renderer.getComponent('theme-open-components-link');
        const docsLink = renderer.getComponent('component-help-doc-link');
        const backLink = renderer.getComponent('component-open-gallery-link');
        const themeHelp = renderer.getHost('theme-gallery-help');
        const componentHelp = renderer.getHost('component-help');
        const themeLinkRecord = records.get(themeLink?.element);
        const docsLinkRecord = records.get(docsLink?.element);
        const backLinkRecord = records.get(backLink?.element);
        const rendererElement = renderer.element;

        themeLink?.element?.click();
        const afterForward = {
            active: tabs.getActiveTabId?.() ?? tabs.activeTabId,
            query: new URL(location.href).searchParams.get('tab'),
        };
        const componentText = componentHelp?.textContent ?? '';
        const docsResponses = await Promise.all([
            fetch('/CUSTOM-COMPONENTS.md').then((response) => response.status),
            fetch('/AGENT-UI-GUIDE.md').then((response) => response.status),
        ]);
        backLink?.element?.click();

        return {
            themeHelpVisible: Boolean(themeHelp && !themeHelp.hidden && themeHelp.textContent.includes('元件導覽與展示')),
            componentHelpVisible: Boolean(componentHelp && !componentHelp.hidden && componentText.includes('元件組合使用說明')),
            tierRulesVisible: ['原子元件 atomic', '複合元件 composite', '模板元件 template']
                .every((label) => componentText.includes(label)),
            commandsVisible: ['custom-components:build', 'custom-components:check', 'test:custom-components']
                .every((command) => componentText.includes(command)),
            hrefs: [themeLink?.getHref?.(), docsLink?.getHref?.(), backLink?.getHref?.()],
            officialProvenance: [
                [themeLinkRecord, themeLink, 'theme-open-components-link', 'studio.open-components'],
                [docsLinkRecord, docsLink, 'component-help-doc-link', null],
                [backLinkRecord, backLink, 'component-open-gallery-link', 'studio.open-theme'],
            ].every(([record, instance, nodeId, commandId]) => (
                record?.instance === instance && record?.renderer === renderer && record?.nodeId === nodeId &&
                (commandId ? record.commandIds?.includes(commandId) : record.commandIds?.length === 0)
            )),
            afterForward,
            afterBack: {
                active: tabs.getActiveTabId?.() ?? tabs.activeTabId,
                hasTabQuery: new URL(location.href).searchParams.has('tab'),
            },
            rendererPreserved: renderer === window.__studio.renderer && renderer.element === rendererElement,
            docsResponses,
        };
    });
    check(
        'JSON help uses official components, documents all tiers, and links between both workspaces without reload',
        helpNavigation.themeHelpVisible && helpNavigation.componentHelpVisible &&
        helpNavigation.tierRulesVisible && helpNavigation.commandsVisible &&
        helpNavigation.hrefs.join('|') === '?tab=components|/CUSTOM-COMPONENTS.md|?tab=theme' &&
        helpNavigation.officialProvenance &&
        helpNavigation.afterForward.active === 'components' && helpNavigation.afterForward.query === 'components' &&
        helpNavigation.afterBack.active === 'theme' && !helpNavigation.afterBack.hasTabQuery &&
        helpNavigation.rendererPreserved && helpNavigation.docsResponses.every((status) => status === 200),
        JSON.stringify(helpNavigation),
    );

    const sentinel = '#13579b';
    const sentinelSet = await page.evaluate((value) => {
        window.__ts.setToken('--cl-primary', value);
        return {
            override: window.__ts.overrides['--cl-primary'],
            computed: getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim(),
            componentIdentity: window.__studio.renderer.getComponent('token-primary') === window.__integrationIdentity.tokenPrimary,
        };
    }, sentinel);
    check(
        'Theme sentinel is applied through the self-hosted Theme controller',
        sentinelSet.override === sentinel && sentinelSet.computed === sentinel && sentinelSet.componentIdentity,
        JSON.stringify(sentinelSet),
    );

    const customBuild = await page.evaluate(async () => {
        const renderer = window.__studio.renderer;
        const tabs = renderer.getComponent('workspace-tabs');
        tabs.tabMap.get('components').tabButton.click();
        const custom = window.__customComponentStudio;
        const atomic = { definition: custom.getDefinition(), analysis: custom.analyze(), valid: custom.validate().valid };
        custom.actions.addBuiltIn('BasicButton');
        const composite = { definition: custom.getDefinition(), analysis: custom.analyze(), valid: custom.validate().valid };
        custom.actions.wrapSelected();
        custom.actions.wrapSelected();
        custom.actions.wrapSelected();
        await custom.actions.rebuildPreview();
        const template = {
            definition: custom.getDefinition(),
            analysis: custom.analyze(),
            valid: custom.validate().valid,
            selectedId: custom.state.selectedId,
        };
        window.__integrationTemplate = structuredClone(template.definition);
        const currentTabs = renderer.getComponent('workspace-tabs');
        return {
            activeTab: currentTabs?.getActiveTabId?.() ?? currentTabs?.activeTabId,
            atomicKind: atomic.definition.kind,
            atomicDepth: atomic.analysis.max_depth,
            atomicValid: atomic.valid,
            compositeKind: composite.definition.kind,
            compositeDepth: composite.analysis.max_depth,
            compositeChildren: composite.definition.root?.children?.length,
            compositeValid: composite.valid,
            templateKind: template.definition.kind,
            templateDepth: template.analysis.max_depth,
            templateValid: template.valid,
            selectedId: template.selectedId,
        };
    });
    check(
        'Custom Studio builds atomic to composite to depth-4 template',
        customBuild.activeTab === 'components' &&
        customBuild.atomicKind === 'atomic' && customBuild.atomicDepth === 0 && customBuild.atomicValid &&
        customBuild.compositeKind === 'composite' && customBuild.compositeDepth === 1 && customBuild.compositeChildren === 2 && customBuild.compositeValid &&
        customBuild.templateKind === 'template' && customBuild.templateDepth === 4 && customBuild.templateValid,
        JSON.stringify(customBuild),
    );

    const retained = await page.evaluate((value) => {
        const renderer = window.__studio.renderer;
        const tabs = renderer.getComponent('workspace-tabs');
        tabs.tabMap.get('theme').tabButton.click();
        const themeState = {
            active: tabs.getActiveTabId?.() ?? tabs.activeTabId,
            override: window.__ts.overrides['--cl-primary'],
            computed: getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim(),
        };
        tabs.tabMap.get('components').tabButton.click();
        const refs = window.__integrationIdentity;
        return {
            themeState,
            active: tabs.getActiveTabId?.() ?? tabs.activeTabId,
            definitionEqual: JSON.stringify(window.__customComponentStudio.getDefinition()) === JSON.stringify(window.__integrationTemplate),
            rendererIdentity: renderer === refs.renderer,
            tabsIdentity: tabs === refs.tabs,
            rendererElementIdentity: renderer.element === refs.rendererElement,
            themePanelIdentity: tabs.tabMap.get('theme').panel === refs.themePanel,
            componentsPanelIdentity: tabs.tabMap.get('components').panel === refs.componentsPanel,
            themeWorkspaceIdentity: renderer.getHost('theme-workspace') === refs.themeWorkspace,
            componentWorkspaceIdentity: renderer.getHost('component-workspace') === refs.componentWorkspace,
            customApiIdentity: window.__customComponentStudio === refs.customApi,
            bothPanelsConnected: refs.themePanel.isConnected && refs.componentsPanel.isConnected,
            expected: value,
        };
    }, sentinel);
    check(
        'Tab round-trip preserves Theme state, Custom definition, and DOM identity',
        retained.themeState.active === 'theme' &&
        retained.themeState.override === sentinel && retained.themeState.computed === sentinel &&
        retained.active === 'components' && retained.definitionEqual && retained.rendererIdentity && retained.tabsIdentity &&
        retained.rendererElementIdentity && retained.themePanelIdentity && retained.componentsPanelIdentity &&
        retained.themeWorkspaceIdentity && retained.componentWorkspaceIdentity && retained.customApiIdentity && retained.bothPanelsConnected,
        JSON.stringify(retained),
    );

    const customExport = await downloadFromRendererControl(page, 'component-export-json');
    const exportedCustom = JSON.parse(readFileSync(customExport.downloadPath, 'utf8'));
    const expectedCustom = await page.evaluate(() => window.__integrationTemplate);
    check(
        'Renderer-provenance Custom export control downloads the exact depth-4 definition',
        customExport.proof.commandIds?.includes('custom.export-json') &&
        customExport.download.suggestedFilename() === 'CustomTextInput.json' &&
        isDeepStrictEqual(exportedCustom, expectedCustom),
        `${JSON.stringify(customExport.proof)} ${customExport.download.suggestedFilename()}`,
    );

    await page.evaluate(() => {
        window.__customComponentStudio.actions.setDefinition({
            schema_version: 1,
            component_id: 'custom.mutated',
            registry_name: 'CustomMutated',
            display_name: 'Mutated',
            version: '1.0.0',
            kind: 'atomic',
            root: { id: 'node-1', type: 'component', component: 'TextInput', options: {} },
        });
    });
    await page.waitForFunction(() => window.__customComponentStudio.getDefinition().registry_name === 'CustomMutated');
    await uploadThroughRendererControl(page, 'component-import-json', {
        name: customExport.download.suggestedFilename(),
        mimeType: 'application/json',
        buffer: readFileSync(customExport.downloadPath),
    });
    await page.waitForFunction((expected) => (
        JSON.stringify(window.__customComponentStudio.getDefinition()) === JSON.stringify(expected)
    ), expectedCustom, { timeout: 15000 });
    const customRoundTrip = await page.evaluate(() => ({
        definition: window.__customComponentStudio.getDefinition(),
        kind: window.__customComponentStudio.getDefinition().kind,
        depth: window.__customComponentStudio.analyze().max_depth,
        valid: window.__customComponentStudio.validate().valid,
    }));
    check(
        'Renderer UploadButton round-trips Custom JSON without semantic drift',
        isDeepStrictEqual(customRoundTrip.definition, exportedCustom) &&
        customRoundTrip.kind === 'template' && customRoundTrip.depth === 4 && customRoundTrip.valid,
        JSON.stringify({ kind: customRoundTrip.kind, depth: customRoundTrip.depth, valid: customRoundTrip.valid }),
    );

    await page.evaluate(() => {
        window.__studio.renderer.getComponent('workspace-tabs').tabMap.get('theme').tabButton.click();
    });
    const themeExport = await downloadFromRendererControl(page, 'theme-export-json');
    const exportedTheme = JSON.parse(readFileSync(themeExport.downloadPath, 'utf8'));
    check(
        'Renderer-provenance Theme export contains the sentinel token',
        themeExport.proof.commandIds?.includes('theme.export-json') &&
        themeExport.download.suggestedFilename() === 'theme.tokens.json' &&
        exportedTheme.tokens?.['--cl-primary'] === sentinel,
        `${JSON.stringify(themeExport.proof)} ${JSON.stringify(exportedTheme)}`,
    );

    const mutation = '#abcdef';
    await page.evaluate((value) => window.__ts.setToken('--cl-primary', value), mutation);
    await page.waitForFunction((value) => window.__ts.overrides['--cl-primary'] === value, mutation);
    await uploadThroughRendererControl(page, 'theme-import-json', {
        name: themeExport.download.suggestedFilename(),
        mimeType: 'application/json',
        buffer: readFileSync(themeExport.downloadPath),
    });
    await page.waitForFunction((value) => (
        window.__ts.overrides['--cl-primary'] === value &&
        getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim() === value
    ), sentinel, { timeout: 15000 });
    const themeRoundTrip = await page.evaluate(() => ({
        exported: JSON.parse(window.__ts.tokensJson()),
        override: window.__ts.overrides['--cl-primary'],
        computed: getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim(),
    }));
    check(
        'Renderer UploadButton restores Theme JSON after a conflicting mutation',
        themeRoundTrip.override === sentinel && themeRoundTrip.computed === sentinel &&
        isDeepStrictEqual(themeRoundTrip.exported, exportedTheme),
        JSON.stringify(themeRoundTrip),
    );

    const safeThemeBeforeRejectedImport = await page.evaluate(() => ({
        json: window.__ts.tokensJson(),
        css: window.__ts.customCss(),
        computed: getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim(),
    }));
    const unsafeTheme = {
        tokens: { '--cl-primary': '#112233' },
        components: {
            BasicButton: {
                className: 'bad class',
                tokens: { '--cl-primary': 'red; } html { opacity: 0' },
            },
            NotInCatalog: {
                className: 'x} body{display:none}/*',
                tokens: { '--cl-bg': 'url(https://example.invalid/tracker)' },
            },
        },
    };
    await uploadThroughRendererControl(page, 'theme-import-json', {
        name: 'unsafe-theme.json',
        mimeType: 'application/json',
        buffer: Buffer.from(JSON.stringify(unsafeTheme)),
    });
    await page.waitForTimeout(150);
    const rejectedThemeImport = await page.evaluate(() => ({
        json: window.__ts.tokensJson(),
        css: window.__ts.customCss(),
        computed: getComputedStyle(document.documentElement).getPropertyValue('--cl-primary').trim(),
        hasBadClass: Object.values(window.__ts.componentTweaks).some((tweak) => tweak?.className === 'bad class'),
        hasUnknownComponent: Object.prototype.hasOwnProperty.call(window.__ts.componentTweaks, 'NotInCatalog'),
    }));
    check(
        'Theme import rejects selector/declaration injection without partial mutation',
        rejectedThemeImport.json === safeThemeBeforeRejectedImport.json &&
        rejectedThemeImport.css === safeThemeBeforeRejectedImport.css &&
        rejectedThemeImport.computed === safeThemeBeforeRejectedImport.computed &&
        !rejectedThemeImport.hasBadClass && !rejectedThemeImport.hasUnknownComponent &&
        !rejectedThemeImport.css.includes('body{display:none}') &&
        !rejectedThemeImport.css.includes('opacity: 0'),
        JSON.stringify(rejectedThemeImport),
    );

    const defensiveThemeExport = await page.evaluate(() => {
        const safePayload = JSON.parse(window.__ts.tokensJson());
        window.__ts.componentTweaks.BasicButton = {
            className: 'x} body{display:none}/*',
            tokens: { '--cl-primary': 'red; } html { opacity: 0' },
        };
        let cssRejected = false;
        let jsonRejected = false;
        try { window.__ts.customCss(); } catch { cssRejected = true; }
        try { window.__ts.tokensJson(); } catch { jsonRejected = true; }
        delete window.__ts.componentTweaks.BasicButton;
        window.__ts.applyImported(safePayload);
        return {
            cssRejected,
            jsonRejected,
            restored: window.__ts.tokensJson() === JSON.stringify(safePayload, null, 2),
        };
    });
    check(
        'Theme CSS/JSON exporters defensively reject corrupted in-memory tweaks',
        defensiveThemeExport.cssRejected && defensiveThemeExport.jsonRejected && defensiveThemeExport.restored,
        JSON.stringify(defensiveThemeExport),
    );

    const provenance = await page.evaluate(async () => {
        const renderer = window.__studio.renderer;
        const records = renderer.controlRecords;
        const definition = window.__studio.definition;
        const nodes = new Map();
        const visit = (node) => {
            if (!node || typeof node !== 'object') return;
            nodes.set(node.id, node);
            if (node.type === 'group') (node.children || []).forEach(visit);
            if (node.type === 'tabs') (node.tabs || []).forEach((tab) => visit(tab.content));
        };
        visit(definition.root);
        const [{ ComponentFactory }, { TabContainer }] = await Promise.all([
            import('/packages/javascript/browser/ui_components/binding/ComponentFactory.js'),
            import('/packages/javascript/browser/ui_components/layout/TabContainer/TabContainer.js'),
        ]);
        const root = renderer.element;
        const selector = [
            'a[href]', 'button', 'input', 'select', 'textarea',
            '[role="button"]', '[role="tab"]', '[role="checkbox"]', '[role="switch"]', '[role="slider"]', '[tabindex]',
        ].join(',');
        const excluded = (element) => element.closest(
            '.ccs-preview, .component-preview, [data-studio-zone="gallery"], .theme-gallery, .ts-gallery, #ts-gallery'
        ) !== null;
        const interactive = [...root.querySelectorAll(selector)].filter((element) => !excluded(element));
        const errors = [];
        for (const element of interactive) {
            const record = records.get(element);
            if (!record) {
                errors.push(`unregistered:${element.tagName.toLowerCase()}#${element.id || '-'}`);
                continue;
            }
            const node = nodes.get(record.nodeId);
            const Constructor = node?.type === 'tabs'
                ? TabContainer
                : node?.type === 'component'
                    ? ComponentFactory.getComponentClass(node.component)
                    : null;
            if (!Constructor || !(record.instance instanceof Constructor)) errors.push(`constructor:${record.nodeId}`);
            if (record.renderer !== renderer) errors.push(`renderer:${record.nodeId}`);
            const expectedCommands = Object.values(node?.events || {}).sort();
            const actualCommands = Array.isArray(record.commandIds) ? [...record.commandIds].sort() : null;
            if (JSON.stringify(expectedCommands) !== JSON.stringify(actualCommands)) errors.push(`commands:${record.nodeId}`);
            const instanceRoots = [
                record.instance?.element,
                record.instance?.container,
                record.instance?.button,
                record.instance?.input,
                record.instance?.textarea,
                record.instance?.select,
                record.instance?.selector,
                record.instance?.fileInput,
                record.instance?.tabNav,
                record.instance?.tabContent,
                record.instance?.containerId ? document.getElementById(record.instance.containerId) : null,
            ].filter((candidate) => candidate instanceof Element);
            if (!instanceRoots.some((candidate) => candidate === element || candidate.contains(element))) {
                errors.push(`ownership:${record.nodeId}`);
            }
        }
        const stale = [...records.entries()].filter(([element, record]) => record?.renderer === renderer && !element.isConnected);
        return {
            recordsIsMap: records instanceof Map,
            recordCount: records.size,
            interactiveCount: interactive.length,
            staleCount: stale.length,
            errors,
            svg: document.querySelectorAll('svg').length,
            canvas: document.querySelectorAll('canvas').length,
            iframe: document.querySelectorAll('iframe').length,
            csp: window.__integrationCspViolations || [],
            studioErrors: window.__studioErrors || [],
        };
    });
    check(
        'All Studio chrome controls retain official renderer provenance',
        provenance.recordsIsMap && provenance.recordCount > 0 && provenance.interactiveCount > 0 &&
        provenance.staleCount === 0 && provenance.errors.length === 0,
        JSON.stringify(provenance),
    );
    check(
        'Integrated Studio remains iframe-free, SVG-zero, and visibly Canvas-backed',
        provenance.iframe === 0 && provenance.svg === 0 && provenance.canvas > 0,
        JSON.stringify({ iframe: provenance.iframe, svg: provenance.svg, canvas: provenance.canvas }),
    );

    const definitionEvidence = await page.evaluate(async () => {
        const sourceUrl = new URL(window.__studio.definitionUrl, location.href);
        const text = await fetch(sourceUrl, { cache: 'no-store' }).then((response) => response.text());
        const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(text));
        const hash = [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
        return {
            path: sourceUrl.pathname,
            hookHash: window.__studio.definitionHash,
            fetchedHash: hash,
        };
    });
    check(
        'Studio definition URL and SHA-256 match the authoritative JSON bytes',
        definitionEvidence.path === expectedDefinitionPath &&
        definitionEvidence.hookHash === expectedDefinitionHash &&
        definitionEvidence.fetchedHash === expectedDefinitionHash,
        JSON.stringify(definitionEvidence),
    );

    check(
        'Browser run has zero console, page, HTTP, request, CSP, or captured Studio errors',
        consoleErrors.length === 0 && pageErrors.length === 0 && failedResponses.length === 0 && requestFailures.length === 0 &&
        provenance.csp.length === 0 && provenance.studioErrors.length === 0,
        [
            ...consoleErrors.map((value) => `console: ${value}`),
            ...pageErrors.map((value) => `page: ${value}`),
            ...failedResponses,
            ...requestFailures,
            ...provenance.csp.map((value) => `CSP ${value.violatedDirective}: ${value.blockedURI}`),
            ...provenance.studioErrors.map((value) => `studio: ${value}`),
        ].join(' | '),
    );
} catch (error) {
    check('Studio integration smoke completes', false, error?.stack || error?.message || String(error));
} finally {
    if (context) await context.close();
    if (browser) await browser.close();
    await closeServer(server);
}

for (const result of results) {
    console.log(`${result.pass ? 'ok  ' : 'FAIL'} ${result.name}${result.pass || !result.detail ? '' : ` :: ${result.detail}`}`);
}
console.log(`\nStudio integration smoke: ${results.length - failures.length}/${results.length} passed.`);
if (failures.length > 0) {
    throw new Error(`Studio integration smoke failed:\n- ${failures.join('\n- ')}`);
}
