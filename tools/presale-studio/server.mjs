import { createServer } from 'node:http';
import { createHash, randomBytes } from 'node:crypto';
import { existsSync, mkdtempSync, mkdirSync, readFileSync, realpathSync, readdirSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildSite } from '../generation/build-site.mjs';
import { validateRequest } from '../generation/validate-definition.mjs';
import SimpleZip from '../../packages/javascript/browser/ui_components/utils/SimpleZip.js';
import { compileDraft, prototypeConfig } from './model.mjs';
import { BUILTIN_CHOICES, FLOW_INPUT_TYPES, candidateNodes, defaultPagePlan, generateBindings,
    generateCandidates, validatePagePlan } from './page-planner.mjs';
import { validatePrototype, validateSqlitePrototype } from './functional-validation.mjs';
import { validateCustomComponentDefinition } from '../../packages/javascript/browser/custom_components/CustomComponentDefinition.js';
import { componentEvents } from './component-events.mjs';
import { SqlitePrototypeBackend } from './sqlite-backend.mjs';
import { DemoSessions, routePrototypeApi } from './prototype-api.mjs';
import { guardRoute, inspectRequest, NativeRouter, RequestLimiter, RouteError, sendJson } from './native-router.mjs';
import { applyLibraryEntry, LibraryConflictError, LocalTemplateLibrary,
    MAX_LIBRARY_BUNDLE_BYTES, MAX_LIBRARY_BUNDLE_NODES } from './local-library.mjs';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const MIME = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8',
    '.mjs': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8', '.md': 'text/markdown; charset=utf-8',
    '.txt': 'text/plain; charset=utf-8', '.png': 'image/png', '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg', '.gif': 'image/gif', '.webp': 'image/webp',
    '.woff': 'font/woff', '.woff2': 'font/woff2', '.ttf': 'font/ttf', '.otf': 'font/otf' };
const CACHE_VERSION_QUERY = [{ name: 'v', type: 'string', maxLength: 32, pattern: /^[A-Za-z0-9._-]+$/ }];
const WORKBENCH_ROUTES = new NativeRouter()
    .register({ path: /^\/api\/prototype\/(?<id>[a-f0-9]{32})(?:\/.*)?$/, methods: ['GET', 'POST', 'PUT', 'DELETE'], kind: 'prototype' })
    .register({ path: '/api/build', methods: ['POST'], kind: 'build', body: 'json' })
    .register({ path: '/api/library', methods: ['GET'], kind: 'library-list' })
    .register({ path: '/api/library/export', methods: ['GET'], kind: 'library-export' })
    .register({ path: '/api/library/import', methods: ['POST'], kind: 'library-import',
        body: { type: 'json', maxBytes: MAX_LIBRARY_BUNDLE_BYTES, maxNodes: MAX_LIBRARY_BUNDLE_NODES } })
    .register({ path: '/api/library/apply', methods: ['POST'], kind: 'library-apply', body: 'json' })
    .register({ path: /^\/api\/library\/(?<libraryKind>component|page)$/, methods: ['POST'],
        kind: 'library-save', body: 'json' })
    .register({ path: /^\/api\/library\/(?<libraryKind>component|page)\/(?<id>[a-f0-9]{32})$/,
        methods: ['GET', 'DELETE'], kind: 'library-item' })
    .register({ path: /^\/api\/agent\/plan\/(?<id>[a-f0-9]{32})$/, methods: ['POST'], kind: 'agent-plan', body: 'json' })
    .register({ path: '/', methods: ['GET'], kind: 'index' })
    .register({ path: '/index.html', methods: ['GET'], kind: 'index' })
    .register({ path: /^\/(?:theme|palette)\.css$/, methods: ['GET'], kind: 'ui-asset' })
    .register({ path: /^\/(?:studio\.js|studio\.css|model\.mjs|page-planner\.mjs|planner-ui\.js|project-file\.mjs|component-events\.mjs)$/, methods: ['GET'], kind: 'workbench-asset' })
    .register({ path: /^\/runtime\/ui_components\/(?<relative>.+)$/, methods: ['GET'], kind: 'ui-runtime', query: CACHE_VERSION_QUERY })
    .register({ path: /^\/runtime\/custom_components\/(?<relative>.+)$/, methods: ['GET'], kind: 'custom-runtime', query: CACHE_VERSION_QUERY })
    .register({ path: '/api/health', methods: ['GET'], kind: 'health' })
    .register({ path: /^\/api\/agent\/palette\/(?<id>[a-f0-9]{32})$/, methods: ['GET'], kind: 'agent-palette' })
    .register({ path: /^\/preview\/(?<id>[a-f0-9]{32})\/(?<relative>.*)$/, methods: ['GET'], kind: 'preview', query: CACHE_VERSION_QUERY })
    .register({ path: /^\/api\/download\/(?<id>[a-f0-9]{32})$/, methods: ['GET'], kind: 'download' });
function json(res, status, data) {
    sendJson(res, status, data);
}

function fileList(root, relative = '') {
    const result = [];
    for (const entry of readdirSync(path.join(root, relative), { withFileTypes: true })) {
        if (entry.isSymbolicLink()) throw new Error('雛型檔案不可包含連結。');
        const name = relative ? `${relative}/${entry.name}` : entry.name;
        if (entry.isDirectory()) result.push(...fileList(root, name));
        else if (entry.isFile()) result.push(name);
    }
    return result.sort();
}

function refreshManifest(outDir) {
    const manifestPath = path.join(outDir, 'report', 'manifest.json');
    const previous = JSON.parse(readFileSync(manifestPath, 'utf8'));
    const files = fileList(outDir).filter(name => name !== 'report/manifest.json').map(name => {
        const data = readFileSync(path.join(outDir, ...name.split('/')));
        return { path: name, sha256: createHash('sha256').update(data).digest('hex'), size: data.length };
    });
    const manifest = { ...previous, presale_studio_version: 1,
        file_count: files.length, total_size: files.reduce((sum, file) => sum + file.size, 0), files };
    writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
    return manifest;
}

export async function buildPresalePackage(draft, pagePlan = defaultPagePlan()) {
    const compiled = compileDraft(draft);
    const candidates = generateCandidates(draft);
    for (const candidate of candidates) {
        const result = validateCustomComponentDefinition(candidate, { builtinNames: [
            'Heading', 'Text', 'TextInput', 'BasicButton', 'Divider', 'NumberInput', 'DatePicker',
            'TextArea', 'ToggleSwitch', 'Dropdown', 'DataTable'
        ] });
        if (!result.valid || result.derived_kind !== 'composite')
            throw new TypeError(`複合元件定義無效：${candidate.registry_name} ${result.errors.map(error => error.message).join('；')}`);
    }
    validatePagePlan(pagePlan, candidates, draft);
    const storageKey = `b4a-presale-${createHash('sha256').update(JSON.stringify(draft)).digest('hex').slice(0, 24)}`;
    const prototype = prototypeConfig(draft, storageKey);
    const bindings = generateBindings(draft);
    const browserValidation = await validatePrototype(prototype, compiled.seed);
    const sqliteValidation = await validateSqlitePrototype(prototype, compiled.mapping, compiled.seed);
    const functionalValidation = { schema_version: 1,
        result: [browserValidation, sqliteValidation].some(report => report.result === 'failed') ? 'failed'
            : [browserValidation, sqliteValidation].some(report => report.result === 'incomplete') ? 'incomplete' : 'passed',
        cases: [...browserValidation.cases, ...sqliteValidation.cases] };
    if (functionalValidation.result === 'failed')
        throw new Error('雛型功能驗證未通過。');
    const validation = await validateRequest({ template: compiled.template }, 'validate');
    if (!validation.ok) {
        throw new TypeError(`頁面定義未通過驗證：${validation.errors.map(error => error.message).join('；')}`);
    }
    const temporaryRoot = mkdtempSync(path.join(tmpdir(), 'b4a-presale-'));
    const outDir = path.join(temporaryRoot, 'package');
    try {
        const build = await buildSite({ template: compiled.template, out_dir: outDir });
        if (!build.ok) throw new TypeError(`雛型生成失敗：${build.errors.map(error => error.message).join('；')}`);
        const sitePath = path.join(outDir, 'site', 'site.json');
        const siteIndex = path.join(outDir, 'site', 'index.html');
        const html = readFileSync(siteIndex, 'utf8');
        const stylesheet = '<link rel="stylesheet" href="app.css">';
        if (!html.includes(stylesheet)) throw new Error('找不到雛型樣式插入點。');
        const nav = '<nav class="ds-nav" data-site-nav></nav>';
        if (!html.includes(nav)) throw new Error('找不到雛型導覽插入點。');
        writeFileSync(siteIndex, html.replace(stylesheet, `${stylesheet}\n    <link rel="stylesheet" href="prototype.css">`)
            .replace(nav, `${nav}\n            <a class="ds-plan-link" href="planned-page.html">頁面規劃</a>`));
        writeFileSync(path.join(outDir, 'site', 'prototype.css'), readFileSync(path.join(HERE, 'prototype.css')));
        const site = JSON.parse(readFileSync(sitePath, 'utf8'));
        site.demo_seed = 'demo-seed.json';
        site.prototype_config = 'prototype-config.json';
        site.prototype_backend = 'sqlite';
        writeFileSync(sitePath, `${JSON.stringify(site, null, 2)}\n`);
        writeFileSync(path.join(outDir, 'site', 'demo-seed.json'), `${JSON.stringify(compiled.seed, null, 2)}\n`);
        const siteDir = path.join(outDir, 'site');
        writeFileSync(path.join(siteDir, 'prototype-config.json'), `${JSON.stringify(prototype, null, 2)}\n`);
        writeFileSync(path.join(siteDir, 'candidate-bindings.json'), `${JSON.stringify(bindings, null, 2)}\n`);
        writeFileSync(path.join(siteDir, 'prototype-store.mjs'), readFileSync(path.join(HERE, 'prototype-store.mjs')));
        for (const name of ['prototype-rules.mjs', 'remote-store.mjs', 'component-events.mjs'])
            writeFileSync(path.join(siteDir, name), readFileSync(path.join(HERE, name)));
        const backendDir = path.join(outDir, 'backend');
        mkdirSync(backendDir);
        for (const name of ['prototype-rules.mjs', 'sqlite-backend.mjs', 'scoped-read.mjs',
            'query-module.mjs', 'report-module.mjs', 'batch-module.mjs', 'native-router.mjs',
            'prototype-router.mjs',
            'prototype-api.mjs', 'broker-principal.mjs', 'standalone-server.mjs'])
            writeFileSync(path.join(backendDir, name), readFileSync(path.join(HERE, name)));
        writeFileSync(path.join(outDir, 'package.json'), '{"private":true,"type":"module","scripts":{"start":"node backend/standalone-server.mjs"}}\n');
        writeFileSync(path.join(outDir, 'README.txt'), [
            'Bricks4Agent SQLite 展示雛型',
            '',
            '需求：Node.js 22.13 或更新版本。解壓後在本目錄執行 npm start，',
            '再開啟 http://127.0.0.1:3092/。不需要 npm install。',
            '資料存於 data/prototype.sqlite；頁面上的「重設展示資料」會還原展示資料。',
            '預設模式提供可切換的測試角色，並非正式身分驗證，也未連接任何正式資料來源。',
            '正式模式：由管理員核定獨立的 JSON 權限檔，再設定 B4A_PROTOTYPE_MODE=formal、',
            'B4A_FORMAL_POLICY=該檔絕對路徑、B4A_BROKER_VERIFY_URL=',
            'http://127.0.0.1:<Broker 埠>/api/v1/sessions/verify，最後執行 npm start。',
            '正式模式使用 data/formal.sqlite，不匯入展示資料，也禁止切換角色及重設資料。',
            '每次請求由 Broker 檢查權杖，再以核定權限檔決定資料操作範圍。',
            '每個 /api/presale/e... 資源另有 POST /query、/lookup、/summary、/export、/batch。',
            '前四者要求 read 權限，批次新增要求 create 權限；全部使用相同的伺服器端範圍。',
            '請依 review/architecture.agent.json 與 review/field-mapping.json 使用代碼欄位。',
            '查詢支援 eq、contains、gte、lte，摘要支援 count 或數值欄位 sum；匯出 CSV 至多 2000 筆。',
            '批次新增至多 100 筆，須附 32 字元小寫十六進位 request_id；可先設定 dry_run=true。',
            '相同 request_id 與內容重送不會再次寫入，不同內容則回覆衝突。',
            'review/page-plan.json 可含複合元件實例的 wiring.fields、wiring.columns 與 wiring.actions。',
            '欄位、內部節點事件、資料來源與結果節點均經白名單驗證，頁面只解譯 JSON，不執行自訂腳本。',
            '目前 Broker scoped token 屬代理工作階段；一般人員的權杖簽發與權限對照仍待核定。',
            'Node 22 的內建 SQLite 可能顯示實驗功能提示。',
            ''
        ].join('\n'));
        const customSource = path.resolve(HERE, '../../packages/javascript/browser/custom_components');
        const customRuntime = path.join(siteDir, 'runtime', 'custom_components');
        mkdirSync(customRuntime, { recursive: true });
        for (const name of ['CustomComponentDefinition.js', 'CustomComponentRenderer.js',
            'CustomComponentRegistry.js', 'custom-components.css'])
            writeFileSync(path.join(customRuntime, name), readFileSync(path.join(customSource, name)));
        // The custom registry imports the eager B4A factory; its RegionMap export needs these JS modules.
        // No maps, SVG files, or external data are bundled for the presale page.
        const dataRoot = path.resolve(HERE, '../../packages/javascript/browser/ui_components/data');
        for (const name of ['index.js', 'RegionMap/index.js', 'RegionMap/RegionMap.js',
            'RegionMap/LegacyRegionMapLayouts.js']) {
            const target = path.join(siteDir, 'runtime', 'ui_components', 'data', ...name.split('/'));
            mkdirSync(path.dirname(target), { recursive: true });
            writeFileSync(target, readFileSync(path.join(dataRoot, ...name.split('/'))));
        }
        writeFileSync(path.join(siteDir, 'candidate-components.json'), `${JSON.stringify(candidates, null, 2)}\n`);
        writeFileSync(path.join(siteDir, 'page-plan.json'), `${JSON.stringify(pagePlan, null, 2)}\n`);
        for (const name of ['planned-page.html', 'planned-page.js', 'planned-page.css'])
            writeFileSync(path.join(siteDir, name), readFileSync(path.join(HERE, name)));
        const reviewDir = path.join(outDir, 'review');
        mkdirSync(reviewDir);
        writeFileSync(path.join(reviewDir, 'architecture.agent.json'), `${JSON.stringify(compiled.architecture, null, 2)}\n`);
        writeFileSync(path.join(reviewDir, 'labels.json'), `${JSON.stringify(compiled.labels, null, 2)}\n`);
        writeFileSync(path.join(reviewDir, 'field-mapping.json'), `${JSON.stringify(compiled.mapping, null, 2)}\n`);
        writeFileSync(path.join(reviewDir, 'interview.md'), compiled.review);
        writeFileSync(path.join(reviewDir, 'page-plan.json'), `${JSON.stringify(pagePlan, null, 2)}\n`);
        writeFileSync(path.join(outDir, 'report', 'functional-validation.json'),
            `${JSON.stringify(functionalValidation, null, 2)}\n`);
        writeFileSync(path.join(siteDir, 'functional-validation.json'),
            `${JSON.stringify(functionalValidation, null, 2)}\n`);
        const componentDir = path.join(reviewDir, 'custom-components');
        mkdirSync(componentDir);
        for (const candidate of candidates)
            writeFileSync(path.join(componentDir, `${candidate.registry_name}.json`),
                `${JSON.stringify(candidate, null, 2)}\n`);
        const manifest = refreshManifest(outDir);
        return { temporaryRoot, outDir, compiled, manifest, functionalValidation, pageCount: build.pages.length,
            componentCount: candidates.length };
    } catch (error) {
        if (temporaryRoot.startsWith(`${tmpdir()}${path.sep}b4a-presale-`) && existsSync(temporaryRoot))
            rmSync(temporaryRoot, { recursive: true, force: true });
        throw error;
    }
}

async function zipPackage(outDir) {
    const zip = new SimpleZip();
    for (const name of fileList(outDir)) zip.addFile(name, new Uint8Array(readFileSync(path.join(outDir, ...name.split('/')))));
    return Buffer.from(await (await zip.generateAsync()).arrayBuffer());
}

function serveFile(res, root, relative, extra = {}) {
    if (!relative || relative.split('/').some(segment => !segment || segment === '.' || segment === '..' || segment.includes('\\')))
        return json(res, 404, { error: '找不到檔案。' });
    const target = path.resolve(root, ...relative.split('/'));
    if (!target.startsWith(`${path.resolve(root)}${path.sep}`) || !existsSync(target) || !statSync(target).isFile())
        return json(res, 404, { error: '找不到檔案。' });
    const realRoot = realpathSync(root), realTarget = realpathSync(target);
    if (!realTarget.startsWith(`${realRoot}${path.sep}`))
        return json(res, 404, { error: '找不到檔案。' });
    const ext = path.extname(target).toLowerCase();
    if (!MIME[ext]) return json(res, 404, { error: '不支援此檔案。' });
    res.writeHead(200, { 'Content-Type': MIME[ext], 'X-Content-Type-Options': 'nosniff', ...extra });
    res.end(readFileSync(realTarget));
}

function readJson(body) {
    if (!body || typeof body !== 'object' || Array.isArray(body) ||
        !['draft', 'draft|plan'].includes(Object.keys(body).sort().join('|')))
        throw new TypeError('請傳入指定格式的訪談資料。');
    return body;
}

export function createPresaleServer({ libraryRoot = path.join(HERE, '.local-library') } = {}) {
    const builds = new Map();
    const limiter = new RequestLimiter();
    const assetLimiter = new RequestLimiter({ maxRequests: 2400 });
    let library;
    const getLibrary = () => (library ||= new LocalTemplateLibrary(libraryRoot));
    const activate = (result, draft) => {
        const config = JSON.parse(readFileSync(path.join(result.outDir, 'site', 'prototype-config.json'), 'utf8'));
        const backend = new SqlitePrototypeBackend({ dbPath: path.join(result.temporaryRoot, 'prototype.sqlite'),
            config, mapping: result.compiled.mapping, seed: result.compiled.seed });
        return { ...result, draft, backend, sessions: new DemoSessions(config) };
    };
    const server = createServer(async (req, res) => {
        try {
            const request = inspectRequest(req, res);
            const route = WORKBENCH_ROUTES.resolve(req.method, request.pathname);
            if (['index', 'ui-asset', 'workbench-asset', 'ui-runtime', 'custom-runtime', 'preview'].includes(route.kind))
                assetLimiter.check(req);
            else limiter.check(req);
            if (route.kind === 'not-found') return json(res, 404, { error: '找不到頁面。' });
            if (route.kind === 'method-not-allowed')
                return sendJson(res, 405, { error: '不支援此方法。' }, { Allow: route.allow });
            if (route.kind === 'prototype') {
                const source = builds.get(route.params.id);
                if (!source) return json(res, 404, { error: '雛型已不存在。' });
                if (await routePrototypeApi(req, res, { base: `/api/prototype/${route.params.id}`,
                    backend: source.backend, sessions: source.sessions, context: request })) return;
            }
            const { body } = await guardRoute(req, request, route);
            if (route.kind === 'library-list') return json(res, 200,
                { schema_version: 1, items: getLibrary().list() });
            if (route.kind === 'library-export') {
                const bundle = getLibrary().exportBundle();
                res.writeHead(200, { 'Content-Type': 'application/json; charset=utf-8',
                    'Content-Disposition': 'attachment; filename="b4a-local-library.json"',
                    'X-Content-Type-Options': 'nosniff' });
                return res.end(JSON.stringify(bundle, null, 2) + '\n');
            }
            if (route.kind === 'library-import')
                return json(res, 200, getLibrary().importBundle(body));
            if (route.kind === 'library-item') {
                const item = getLibrary().get(route.params.libraryKind, route.params.id);
                if (!item) return json(res, 404, { error: '本地庫項目不存在。' });
                if (req.method === 'DELETE') {
                    getLibrary().remove(route.params.libraryKind, route.params.id);
                    return json(res, 200, { removed: true });
                }
                return json(res, 200, item);
            }
            if (route.kind === 'library-save') {
                const required = route.params.libraryKind === 'component'
                    ? 'draft|instance_id|plan|title' : 'draft|plan|title';
                if (!body || Object.keys(body).sort().join('|') !== required)
                    throw new TypeError('本地庫儲存格式無效。');
                const saved = getLibrary().save({ kind: route.params.libraryKind, ...body });
                return json(res, 201, saved);
            }
            if (route.kind === 'library-apply') {
                if (!body || Object.keys(body).sort().join('|') !== 'draft|id|kind|mapping')
                    throw new TypeError('本地庫套用格式無效。');
                const item = getLibrary().get(body.kind, body.id);
                if (!item) return json(res, 404, { error: '本地庫項目不存在。' });
                return json(res, 200, { schema_version: 1,
                    plan: applyLibraryEntry(item, body.draft, body.mapping) });
            }
            if (route.kind === 'agent-plan') {
                const source = builds.get(route.params.id);
                if (!source) return json(res, 404, { error: '雛型已不存在。' });
                validatePagePlan(body, generateCandidates(source.draft), source.draft);
                const result = await buildPresalePackage(source.draft, body);
                const id = randomBytes(16).toString('hex');
                builds.set(id, activate(result, source.draft));
                return json(res, 200, { schema_version: 1, preview_url: `/preview/${id}/`,
                    planned_page_url: `/preview/${id}/planned-page.html`,
                    download_url: `/api/download/${id}`, functional_validation: result.functionalValidation });
            }
            if (route.kind === 'build') {
                const { draft, plan } = readJson(body);
                const result = await buildPresalePackage(draft, plan ?? defaultPagePlan());
                const id = randomBytes(16).toString('hex');
                builds.set(id, activate(result, structuredClone(draft)));
                return json(res, 200, { ok: true, preview_url: `/preview/${id}/`,
                    planned_page_url: `/preview/${id}/planned-page.html`, download_url: `/api/download/${id}`,
                    agent_palette_url: `/api/agent/palette/${id}`, agent_plan_url: `/api/agent/plan/${id}`,
                    page_count: result.pageCount, module_count: draft.entities.length,
                    component_count: result.componentCount,
                    review: result.compiled.review, functional_validation: result.functionalValidation });
            }
            if (route.kind === 'index') return serveFile(res, HERE, 'index.html');
            if (route.kind === 'ui-asset') return serveFile(res,
                path.resolve(HERE, '../../packages/javascript/browser/ui_components'), request.pathname.slice(1));
            if (route.kind === 'workbench-asset') return serveFile(res, HERE, request.pathname.slice(1));
            if (route.kind === 'ui-runtime') return serveFile(res,
                path.resolve(HERE, '../../packages/javascript/browser/ui_components'), route.params.relative);
            if (route.kind === 'custom-runtime') return serveFile(res,
                path.resolve(HERE, '../../packages/javascript/browser/custom_components'), route.params.relative);
            if (route.kind === 'health') return json(res, 200, { ok: true });
            if (route.kind === 'agent-palette') {
                const source = builds.get(route.params.id);
                if (!source) return json(res, 404, { error: '雛型已不存在。' });
                const candidates = generateCandidates(source.draft);
                return json(res, 200, { schema_version: 1, architecture: source.compiled.architecture,
                    saved_templates: existsSync(libraryRoot) ? getLibrary().agentCatalog() : [],
                    candidates: generateBindings(source.draft).map(binding => ({ ...binding,
                        nodes: candidateNodes(candidates.find(candidate => candidate.registry_name === binding.ref))
                            .map(node => ({ node_id: node.id, component: node.component,
                                events: componentEvents(node.component),
                                result_ports: node.component === 'DataTable' ? ['rows']
                                    : node.component === 'Text' ? ['text']
                                        : node.component === 'Dropdown' ? ['options'] : [] })) })),
                    builtins: BUILTIN_CHOICES.map(choice => ({ kind: 'builtin', ref: choice.ref,
                        events: componentEvents(choice.ref), input_types: FLOW_INPUT_TYPES[choice.ref] || [],
                        result_ports: choice.ref === 'DataTable' ? ['rows']
                            : choice.ref === 'Text' ? ['text']
                                : choice.ref === 'Dropdown' ? ['options'] : [] })),
                    plan_schema: { schema_version: 1, max_items: 24, spans: [6, 12],
                        candidate_field_binding: ['search', 'summary'],
                        button_operations: ['search', 'new', 'save'],
                        wiring: { instance_id_pattern: 'I_[A-F0-9]{12}',
                            field_operators: ['eq', 'contains', 'gte', 'lte'],
                            operations: ['query', 'summary', 'lookup', 'save', 'new', 'delete', 'export'],
                            page_event: { node_id: '@page', event: 'load' },
                            max_actions: 8 },
                        page_flows: { max_flows: 8, max_filters: 6,
                            operations: ['query', 'summary', 'lookup', 'create', 'update', 'delete'],
                            targets: { query: 'DataTable', summary: 'Text', lookup: 'Dropdown' },
                            required: ['entity_code', 'trigger_instance_id', 'event', 'operation'],
                            filter_required: ['input_instance_id', 'field_code', 'operator'],
                            operation_fields: { query: ['filters', 'target_instance_id', 'columns'],
                                summary: ['filters', 'target_instance_id', 'metric'],
                                lookup: ['filters', 'target_instance_id', 'field_code'], create: ['values'],
                                update: ['source_table_instance_id', 'values'],
                                delete: ['source_table_instance_id'] },
                            write: { backends: ['sqlite', 'sqlite-formal'], formal_authorization: 'formal_policy',
                                trigger_component: 'BasicButton',
                                event: 'click', value_fields: ['input_instance_id', 'field_code'],
                                max_values: 12, create_required_fields: 'all',
                                source: { component: 'DataTable', query_operation: 'query',
                                    entity_match: 'same', selected_row_required: true } },
                            trigger: { page: { trigger_instance_id: '@page', event: 'load' },
                                builtin: { trigger_instance_id_pattern: 'I_[A-F0-9]{12}',
                                    event_source: 'builtins[].events' } } } } });
            }
            if (route.kind === 'preview') {
                const item = builds.get(route.params.id);
                if (!item) return json(res, 404, { error: '雛型已不存在。' });
                return serveFile(res, path.join(item.outDir, 'site'), route.params.relative || 'index.html',
                    { 'Cache-Control': 'no-store' });
            }
            if (route.kind === 'download') {
                const item = builds.get(route.params.id);
                if (!item) return json(res, 404, { error: '雛型已不存在。' });
                const data = await zipPackage(item.outDir);
                res.writeHead(200, { 'Content-Type': 'application/zip', 'Content-Length': data.length,
                    'Content-Disposition': 'attachment; filename="bricks4agent-presale.zip"',
                    'X-Content-Type-Options': 'nosniff' });
                return res.end(data);
            }
            return json(res, 404, { error: '找不到頁面。' });
        } catch (error) {
            const status = error instanceof RouteError ? error.status
                : error instanceof LibraryConflictError ? 409
                : error instanceof TypeError || error instanceof SyntaxError ? 400 : 500;
            return json(res, status, { error: status === 500 ? '產生雛型時發生錯誤。' : error.message });
        }
    });
    server.on('close', () => {
        limiter.clear();
        assetLimiter.clear();
        for (const item of builds.values()) {
            item.sessions.clear();
            item.backend.close();
            const root = path.resolve(item.temporaryRoot);
            if (path.dirname(root) === path.resolve(tmpdir()) && path.basename(root).startsWith('b4a-presale-'))
                rmSync(root, { recursive: true, force: true });
        }
        builds.clear();
    });
    return server;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const port = Number.parseInt(process.env.B4A_PRESALE_PORT || '3091', 10);
    if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('連接埠無效。');
    createPresaleServer().listen(port, '127.0.0.1', () => {
        process.stdout.write(`Presale 工作台：http://127.0.0.1:${port}/\n`);
    });
}
