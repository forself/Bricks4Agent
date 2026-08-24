import { DynamicPageRenderer } from '../../packages/javascript/browser/page-generator/DynamicPageRenderer.js';
import { createFormApplicationStudioController } from './controller.js';

const DEFINITION_URL = new URL('./studio.page.json', import.meta.url);

async function sha256(text) {
    const bytes = new TextEncoder().encode(text);
    const digest = await crypto.subtle.digest('SHA-256', bytes);
    return [...new Uint8Array(digest)].map((byte) => byte.toString(16).padStart(2, '0')).join('');
}

async function main() {
    window.__formApplicationStudioBootPhase = 'definition';
    const app = document.getElementById('app');
    if (!app) throw new Error('Form Application Studio requires #app.');

    const response = await fetch(DEFINITION_URL);
    if (!response.ok) throw new Error(`Studio definition request failed (${response.status}).`);
    const definitionText = await response.text();
    const definition = JSON.parse(definitionText);

    const controller = createFormApplicationStudioController();
    const state = await controller.prepare();
    const controls = { records: new Map() };

    window.__formApplicationStudioBootPhase = 'renderer';
    const pageRenderer = new DynamicPageRenderer({
        definition,
        mode: 'tool',
        commandRegistry: controller.commands,
        state,
        controlRegistry: controls,
    });
    await pageRenderer.init();
    pageRenderer.mount(app);

    const renderer = pageRenderer.getRenderer();
    controller.attachRenderer(renderer);
    const definitionHash = await sha256(definitionText);

    const api = {
        definition,
        definitionUrl: DEFINITION_URL.href,
        definitionHash,
        pageRenderer,
        renderer,
        controls,
        controller,
        getDefinition: () => controller.getDefinition(),
        getBundle: () => controller.getBundle(),
        destroy() {
            controller.destroy();
            pageRenderer.destroy();
        },
    };

    window.__formApplicationStudio = api;
    window.__formApplicationToolPageRenderer = renderer;
    window.__formApplicationStudioReady = true;
    window.__formApplicationStudioBootPhase = 'ready';
    window.dispatchEvent(new CustomEvent('bricks-form-application-studio-ready', {
        detail: { definitionHash },
    }));
}

window.addEventListener('error', (event) => {
    if (!window.__formApplicationStudioReady) return;
    (window.__formApplicationStudioErrors ||= []).push(String(event.error?.stack || event.message || event.error));
});

main().catch((error) => {
    window.__formApplicationStudioError = String(error?.stack || error);
    console.error('[FormApplicationStudio] Startup failed:', error);
});
