import { DynamicPageRenderer } from '../../packages/javascript/browser/page-generator/DynamicPageRenderer.js';
import { createCustomComponentStudioController } from '../custom-component-studio/controller.js';
import { createThemeStudioController } from './controller.js';

const DEFINITION_URL = new URL('./studio.page.json', import.meta.url);

function mergeState(...sources) {
    const output = {};
    for (const source of sources) {
        for (const [key, value] of Object.entries(source || {})) {
            if (
                value && typeof value === 'object' && !Array.isArray(value) &&
                output[key] && typeof output[key] === 'object' && !Array.isArray(output[key])
            ) {
                output[key] = mergeState(output[key], value);
            } else {
                output[key] = value;
            }
        }
    }
    return output;
}

async function sha256(text) {
    const bytes = new TextEncoder().encode(text);
    const digest = await crypto.subtle.digest('SHA-256', bytes);
    return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}

function setWorkspaceQuery(tabId) {
    const url = new URL(window.location.href);
    if (tabId === 'components') url.searchParams.set('tab', 'components');
    else url.searchParams.delete('tab');
    history.replaceState(null, '', url);
}

async function main() {
    window.__studioBootPhase = 'definition';
    const app = document.getElementById('app');
    if (!app) throw new Error('Theme Studio requires #app.');

    const response = await fetch(DEFINITION_URL);
    if (!response.ok) throw new Error(`Studio definition request failed (${response.status}).`);
    const definitionText = await response.text();
    const definition = JSON.parse(definitionText);

    const theme = createThemeStudioController();
    const custom = createCustomComponentStudioController();
    const [themeState, customState] = await Promise.all([theme.prepare(), custom.prepare()]);
    window.__studioBootPhase = 'renderer-init';

    const studioControls = window.__studioControls = { records: new Map() };
    const requestedTab = new URL(window.location.href).searchParams.get('tab') === 'components' ? 'components' : 'theme';
    let suppressWorkspaceHistory = true;
    let workspaceTabs = null;
    const openWorkspace = (tabId, event) => {
        event?.preventDefault?.();
        workspaceTabs?.activateTab?.(tabId);
    };
    const commandRegistry = {
        ...theme.commands,
        ...custom.commands,
        'studio.open-components': (_context, event) => openWorkspace('components', event),
        'studio.open-theme': (_context, event) => openWorkspace('theme', event),
        'studio.workspace-change': (_context, event) => {
            if (!suppressWorkspaceHistory) setWorkspaceQuery(event?.tabId || 'theme');
        },
    };

    const pageRenderer = new DynamicPageRenderer({
        definition,
        mode: 'tool',
        commandRegistry,
        state: mergeState(themeState, customState),
        controlRegistry: studioControls,
    });
    await pageRenderer.init();
    pageRenderer.mount(app);
    window.__studioBootPhase = 'theme-attach';

    const renderer = pageRenderer.getRenderer();
    await theme.attachRenderer(renderer);
    window.__studioBootPhase = 'custom-attach';
    custom.attachRenderer(renderer);

    workspaceTabs = renderer.getComponent('workspace-tabs');
    workspaceTabs?.tabMap?.get('theme')?.tabButton?.setAttribute('data-studio-tab', 'theme');
    workspaceTabs?.tabMap?.get('components')?.tabButton?.setAttribute('data-studio-tab', 'custom');
    if (requestedTab === 'components') workspaceTabs?.activateTab?.('components');
    suppressWorkspaceHistory = false;
    setWorkspaceQuery(requestedTab);

    const definitionHash = await sha256(definitionText);
    window.__studioBootPhase = 'hooks';
    const studio = {
        definition,
        definitionUrl: DEFINITION_URL.href,
        definitionHash,
        pageRenderer,
        renderer,
        controls: studioControls,
        theme,
        custom,
        destroy() {
            custom.destroy();
            theme.destroy();
            pageRenderer.destroy();
        },
    };

    theme.workspaceTabs = workspaceTabs;
    theme.componentStudio = custom;
    window.__studio = studio;
    window.__toolPageRenderer = renderer;
    window.__ts = theme;
    window.__customComponentStudio = custom;
    window.__studioReady = true;
    window.__studioBootPhase = 'ready';
    window.dispatchEvent(new CustomEvent('bricks-studio-ready', { detail: { definitionHash } }));
}

window.addEventListener('error', (event) => {
    if (!window.__studioReady) return;
    (window.__studioErrors ||= []).push(String(event.error?.stack || event.message || event.error));
});

main().catch((error) => {
    window.__studioError = String(error?.stack || error);
    console.error('[ThemeStudio] Startup failed:', error);
});
