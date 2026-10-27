import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { PageGenerator } from '../../../packages/javascript/browser/page-generator/PageGenerator.js';
import { validateToolPageDefinition } from '../../../packages/javascript/browser/page-generator/ToolPageDefinition.js';

const root = fileURLToPath(new URL('../../../', import.meta.url));
const contracts = {
    GisSearch: { id: 'search', options: [], events: { onChange: 'gis.search', onSelect: 'gis.locate' } },
    GisScope: { id: 'scope', options: [], events: { onChange: 'gis.mode' } },
    OfflineGisMap: { id: 'map', options: ['center', 'zoom', 'fixture'], events: { onChange: 'gis.marker', onSelect: 'gis.select' } },
    GisTimeline: { id: 'timeline', options: ['min', 'max'], events: { onChange: 'gis.time' } },
};
export function validateGis(definition) {
    const valid = validateToolPageDefinition(definition);
    if (!valid.valid) throw new Error(valid.errors.join('; '));
    const seen = new Set();
    function visit(node) {
        if (node.type === 'group') return node.children.forEach(visit);
        const contract = contracts[node.component];
        if (node.type !== 'component' || !contract || node.id !== contract.id || seen.has(node.component)) throw new Error('Unsupported GIS node');
        seen.add(node.component);
        if (node.bindings || Object.keys(node.options ?? {}).some(key => !contract.options.includes(key))) throw new Error('Unsupported GIS options/bindings');
        if (Object.keys(node.events ?? {}).length !== Object.keys(contract.events).length
            || Object.entries(contract.events).some(([key, value]) => node.events?.[key] !== value)) throw new Error('GIS commands must match the trusted runtime contract');
        const opts = node.options || {};
        if (node.component === 'OfflineGisMap' && (opts.center !== undefined && (!Array.isArray(opts.center) || opts.center.length !== 2
            || !opts.center.every(Number.isFinite) || Math.abs(opts.center[0]) > 180 || Math.abs(opts.center[1]) > 85)
            || opts.zoom !== undefined && (!Number.isInteger(opts.zoom) || opts.zoom < 0 || opts.zoom > 18)
            || opts.fixture !== undefined && typeof opts.fixture !== 'boolean')) throw new Error('Invalid map options');
        if (node.component === 'GisTimeline' && (!Number.isSafeInteger(opts.min) || !Number.isSafeInteger(opts.max)
            || opts.min < 0 || opts.max <= opts.min || opts.max > 8640000000000000)) throw new Error('Invalid timeline options');
    }
    visit(definition.root);
    if (seen.size !== 4) throw new Error('This application runtime requires map, scope, search and timeline');
    return definition;
}
export function generate(definition, output, { serviceBase = 'api/gis/' } = {}) {
    validateGis(definition);
    const serviceUrl = new URL(serviceBase, 'http://b4a.invalid/');
    if (!['http:', 'https:'].includes(serviceUrl.protocol) || serviceUrl.username || serviceUrl.password || serviceUrl.search
        || serviceUrl.hash || !serviceUrl.pathname.endsWith('/')) throw new Error('Invalid service base URL');
    const connectSource = serviceUrl.origin === 'http://b4a.invalid' ? '' : ` ${serviceUrl.origin}`;
    output = path.resolve(output);
    for (const source of ['packages/javascript/browser', 'modules/offline-gis/frontend']) {
        const directory = path.join(root, source);
        if (output === directory || output.startsWith(directory + path.sep)) throw new Error('Output cannot be inside an input source tree');
    }
    const result = new PageGenerator({ toolRendererImportPath: './packages/javascript/browser/page-generator/DynamicToolRenderer.js' }).generate(definition);
    if (result.errors.length) throw new Error(result.errors.join('; '));
    if (fs.existsSync(output)) throw new Error('Output must not exist (no overwrite)');
    fs.mkdirSync(output, { recursive: true });
    try {
        const browser = path.join(root, 'packages/javascript/browser');
        fs.cpSync(browser, path.join(output, 'packages/javascript/browser'), { recursive: true,
            filter: p => !['node_modules', '__tests__', 'bin', 'obj'].includes(path.basename(p)) });
        fs.cpSync(path.join(root, 'modules/offline-gis/frontend'), path.join(output, 'modules/offline-gis/frontend'), { recursive: true });
        fs.writeFileSync(path.join(output, 'GisPage.js'), result.code);
        fs.writeFileSync(path.join(output, 'definition.json'), JSON.stringify(definition, null, 2));
        fs.writeFileSync(path.join(output, 'service-config.js'), `export const serviceBase = ${JSON.stringify(serviceBase)};\n`);
        fs.writeFileSync(path.join(output, 'app.js'), `import Page from './GisPage.js';\nimport { mountGis } from './modules/offline-gis/frontend/runtime.js';\nimport { GisApi } from './modules/offline-gis/frontend/api.js';\nimport { serviceBase } from './service-config.js';\nconst app = await mountGis(Page, document.querySelector('main'), { api: new GisApi(serviceBase) });\nwindow.addEventListener('pagehide', () => app.destroy(), { once: true });\n`);
        fs.writeFileSync(path.join(output, 'index.html'), `<!doctype html>\n<html lang="zh-TW"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob:; connect-src 'self'${connectSource}; object-src 'none'; base-uri 'self'"><title>Offline GIS</title><link rel="stylesheet" href="./packages/javascript/browser/ui_components/theme.css"><link rel="stylesheet" href="./modules/offline-gis/frontend/gis.css"></head><body><main></main><script type="module" src="./app.js"></script></body></html>\n`);
    } catch (error) { fs.rmSync(output, { recursive: true, force: true }); throw error; }
    return output;
}
if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    if (process.argv.length < 4 || process.argv.length > 5) throw new Error('Usage: node generate.mjs definition.json NEW_OUTPUT_DIRECTORY [SERVICE_BASE_URL]');
    generate(JSON.parse(fs.readFileSync(process.argv[2], 'utf8')), path.resolve(process.argv[3]),
        process.argv[4] ? { serviceBase: process.argv[4] } : {});
}
