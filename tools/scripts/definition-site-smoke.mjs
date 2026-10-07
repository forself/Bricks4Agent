// 定義網站（tools/generation build 的產物）的瀏覽器 smoke。
//
// 1. 以 golden 範例經 CLI build 到 .test-output/，用帶嚴格 CSP 標頭的本機伺服器提供 site/。
// 2. 以 Edge（playwright-core，沿用 studio smoke 的載入方式）逐一走訪每個路由，確認渲染出預期數量的欄位或欄。
// 3. 記憶體資料流程：列表新增一筆 → 明細看得到 → 表單編輯後列表更新 → 刪除。
// 4. 另以涵蓋本切片全部開放欄位型別的定義重跑一次表單、列表、明細，確認每種型別在瀏覽器中可渲染；
//    可直接輸入的型別與列表類型別都填入值（列表類的每一列都必須有輸入框），存檔後在明細頁核對存回的值。
// 5. 只能新增的表單（只有 api.create，沒有列表或明細）可以連續送出多筆，每次送出後回到空白的新增表單。
// 全程 console error、pageerror、HTTP 錯誤、CSP 違規為 0；DOM 中 svg、<style>、inline handler 為 0。
// 結束時刪除本次的 .test-output 子目錄（.test-output 若因此變空也一併刪除）。
import { spawnSync } from 'node:child_process';
import { createReadStream, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync } from 'node:fs';
import { createServer } from 'node:http';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { applyListColumns } from '../../templates/definition-site/site-model.js';
import { computeSliceFieldTypes, FIELD_TYPE_NOTES, OPTION_TYPES } from '../generation/field-types.mjs';

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDir, '..', '..');
const cliPath = path.join(repoRoot, 'tools', 'generation', 'cli.mjs');
const goldenPath = path.join(repoRoot, 'tools', 'generation', 'examples', 'golden.definition-template.json');
const testOutputRoot = path.join(repoRoot, '.test-output');
const workRoot = path.join(testOutputRoot, `definition-site-smoke-${process.pid}`);
const headed = process.argv.includes('--headed');

const CSP = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
const mimeTypes = new Map([
    ['.css', 'text/css; charset=utf-8'],
    ['.html', 'text/html; charset=utf-8'],
    ['.js', 'text/javascript; charset=utf-8'],
    ['.json', 'application/json; charset=utf-8'],
    ['.png', 'image/png'],
    ['.txt', 'text/plain; charset=utf-8'],
]);

const results = [];
function check(name, pass, detail = '') {
    results.push({ name, pass: Boolean(pass), detail });
    console.log(`${pass ? 'ok  ' : 'FAIL'} ${name}${!pass && detail ? `\n     ${detail}` : ''}`);
}

async function loadChromium() {
    const candidates = ['playwright', 'playwright-core'];
    // 與其他 studio smoke 相同，沿用 repo 旁 tim-web/poc 既有的 playwright-core；
    // 往上逐層尋找，讓 git worktree 內的副本也能找到同一份。
    let dir = repoRoot;
    for (let depth = 0; depth < 8; depth += 1) {
        const candidate = path.join(dir, '..', 'tim-web', 'poc', 'node_modules', 'playwright-core', 'index.js');
        if (existsSync(candidate)) {
            candidates.push(pathToFileURL(candidate).href);
            break;
        }
        const parent = path.dirname(dir);
        if (parent === dir) break;
        dir = parent;
    }
    for (const candidate of candidates) {
        try {
            const module = await import(candidate);
            const chromium = module.chromium ?? module.default?.chromium;
            if (chromium) return chromium;
        } catch {
            // 本 repo 不安裝瀏覽器相依套件，只使用既有的 Playwright
        }
    }
    return null;
}

function runBuild(template, outDir, title) {
    const started = Date.now();
    const child = spawnSync(process.execPath, [cliPath, 'build'], {
        input: JSON.stringify({ template, title, out_dir: outDir }),
        encoding: 'utf8',
        maxBuffer: 16 * 1024 * 1024
    });
    const elapsed = Date.now() - started;
    let result = null;
    try {
        result = JSON.parse(child.stdout);
    } catch {
        // 下方以 check 回報
    }
    return { status: child.status, result, elapsed, stderr: child.stderr };
}

function createStaticServer(siteRoot) {
    const rootPrefix = `${siteRoot}${path.sep}`;
    const server = createServer((request, response) => {
        try {
            const requestUrl = new URL(request.url ?? '/', 'http://127.0.0.1');
            let relativePath = decodeURIComponent(requestUrl.pathname).replace(/^\/+/, '');
            if (relativePath === '') relativePath = 'index.html';
            const filePath = path.resolve(siteRoot, relativePath);
            if (!filePath.startsWith(rootPrefix)) {
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
                'Content-Security-Policy': CSP,
                'X-Content-Type-Options': 'nosniff'
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
    return new Promise((resolve, reject) => server.close((error) => (error ? reject(error) : resolve())));
}

function cleanup() {
    rmSync(workRoot, { recursive: true, force: true });
    if (existsSync(testOutputRoot) && readdirSync(testOutputRoot).length === 0) {
        rmSync(testOutputRoot, { recursive: true, force: true });
    }
}

function expectedListColumns(definition) {
    const runtimeFields = definition.fields.map(field => ({ fieldType: field.type }));
    return applyListColumns(runtimeFields).filter(field => field.listOrder > 0).length;
}

// all-types 表單中直接輸入的值（存檔後在明細頁核對）。列表類欄位填在第一列的第一個文字輸入框。
const TYPED_VALUES = Object.freeze({
    text: 'Sample text',
    email: 'sample@example.test',
    textarea: 'Sample notes',
    number: '73',
    personinfo: 'Row Person',
    phonelist: '0911222333',
    socialmedia: 'row_social'
});

/** 列表類欄位：以 maxItems 限制列數的型別（每一列由元件自己的輸入框組成）。 */
function isRepeatableType(type) {
    return FIELD_TYPE_NOTES[type]?.validation.includes('maxItems') === true;
}

function buildAllTypesTemplate() {
    const { allowed } = computeSliceFieldTypes();
    const fields = allowed.map((type) => {
        const field = { name: `f_${type}`, type, label: `${type} 欄位` };
        if (OPTION_TYPES.has(type)) {
            field.options = [{ value: 'a', label: '選項 A' }, { value: 'b', label: '選項 B' }];
        }
        return field;
    });
    const page = (id, name, type, description, api) => ({ id, definition: { name, type, description, fields, api } });
    return {
        kind: 'definition-template',
        version: '0.1.0',
        meta: { title: '欄位型別總覽' },
        definitions: {
            pages: [
                page('items-list', 'ItemListPage', 'list', '項目列表', { list: '/api/items', delete: '/api/items' }),
                page('item-detail', 'ItemDetailPage', 'detail', '項目明細', { get: '/api/items' }),
                page('item-form', 'ItemFormPage', 'form', '編輯項目', { get: '/api/items', create: '/api/items', update: '/api/items' })
            ]
        }
    };
}

function buildCreateOnlyTemplate() {
    return {
        kind: 'definition-template',
        version: '0.1.0',
        meta: { title: '意見回饋' },
        definitions: {
            pages: [
                {
                    id: 'feedback-form',
                    definition: {
                        name: 'FeedbackFormPage',
                        type: 'form',
                        description: '意見回饋',
                        fields: [
                            { name: 'subject', type: 'text', label: '主旨', required: true },
                            { name: 'message', type: 'textarea', label: '內容' }
                        ],
                        api: { create: '/api/feedback' }
                    }
                }
            ]
        }
    };
}

async function openBrowserSession(browser, baseUrl) {
    const context = await browser.newContext({ viewport: { width: 1400, height: 1000 } });
    const page = await context.newPage();
    const problems = [];
    await page.addInitScript(() => {
        window.__cspViolations = [];
        window.addEventListener('securitypolicyviolation', (event) => {
            window.__cspViolations.push(`${event.violatedDirective} ${event.blockedURI}`);
        });
    });
    page.on('pageerror', (error) => problems.push(`pageerror: ${error.message}`));
    page.on('console', (message) => {
        if (message.type() === 'error') problems.push(`console: ${message.text()}`);
    });
    page.on('response', (response) => {
        if (response.status() >= 400) problems.push(`HTTP ${response.status()}: ${response.url()}`);
    });
    page.on('requestfailed', (request) => problems.push(`requestfailed: ${request.url()}`));
    page.on('request', (request) => {
        const url = request.url();
        if (!url.startsWith(baseUrl) && !url.startsWith('data:') && !url.startsWith('blob:')) {
            problems.push(`external request: ${url}`);
        }
    });
    return { context, page, problems };
}

async function waitForRoute(page, pageId) {
    await page.waitForSelector(`[data-site-main][data-route-state="ready"][data-page-id="${pageId}"]`, { timeout: 15000 });
}

/**
 * style 屬性的判定：嚴格 CSP（style-src 'self'）會擋下來自標記或 setAttribute 的 style 屬性，並發出
 * style-src-attr 違規；元件以 CSSOM（element.style）設定的樣式是本 repo 允許的作法，不算在內。
 * 因此以「CSP 違規為 0」判定沒有任何標記 style 屬性，另列出 CSSOM 樣式數量供參考。
 */
async function domAudit(page) {
    return page.evaluate(() => {
        const all = [...document.querySelectorAll('*')];
        const violations = window.__cspViolations.slice();
        return {
            svg: document.querySelectorAll('svg').length,
            styleElements: document.querySelectorAll('style').length,
            markupStyleAttributes: violations.filter(entry => entry.startsWith('style-src')).length,
            inlineHandlers: all.filter(element => [...element.attributes].some(attribute => /^on/i.test(attribute.name))).length,
            javascriptUrls: document.querySelectorAll('[href^="javascript:" i], [src^="javascript:" i]').length,
            cssomStyledElements: document.querySelectorAll('[style]').length,
            cspViolations: violations
        };
    });
}

function auditPasses(audit) {
    return audit.svg === 0 && audit.styleElements === 0 && audit.markupStyleAttributes === 0
        && audit.inlineHandlers === 0 && audit.javascriptUrls === 0 && audit.cspViolations.length === 0;
}

async function goldenScenario(browser, baseUrl, golden) {
    const pagesById = Object.fromEntries(golden.definitions.pages.map(entry => [entry.id, entry.definition]));
    const { context, page, problems } = await openBrowserSession(browser, baseUrl);
    const audits = [];
    try {
        await page.goto(`${baseUrl}/`, { waitUntil: 'load' });
        await waitForRoute(page, 'contacts-list');
        const listInfo = await page.evaluate(() => ({
            hash: location.hash,
            title: document.title,
            nav: [...document.querySelectorAll('[data-site-nav] a')].map(link => link.getAttribute('href')),
            headers: document.querySelectorAll('.dynamic-list thead th:not(.b4a-dt__th--select)').length,
            createButton: Boolean(document.querySelector('[data-site-action="create"]'))
        }));
        const expectedColumns = expectedListColumns(pagesById['contacts-list']) + 1; // + 操作欄
        check('Default route opens the first list page with the site title and nav', listInfo.hash === '#/contacts-list'
            && listInfo.title === '聯絡人原型' && listInfo.nav.length === 3, JSON.stringify(listInfo));
        check(`List route renders ${expectedColumns} columns and a create button`, listInfo.headers === expectedColumns && listInfo.createButton, JSON.stringify(listInfo));
        audits.push(['list', await domAudit(page)]);

        await page.goto(`${baseUrl}/#/contact-detail`, { waitUntil: 'load' });
        await waitForRoute(page, 'contact-detail');
        const detailFields = await page.locator('.dynamic-detail__field').count();
        const detailExpected = pagesById['contact-detail'].fields.filter(field => field.type !== 'hidden').length;
        check(`Detail route renders ${detailExpected} fields`, detailFields === detailExpected, `got ${detailFields}`);
        audits.push(['detail', await domAudit(page)]);

        await page.goto(`${baseUrl}/#/contact-form`, { waitUntil: 'load' });
        await waitForRoute(page, 'contact-form');
        const formFields = await page.locator('.dynamic-form .form-field').count();
        const formExpected = pagesById['contact-form'].fields.length;
        check(`Form route renders ${formExpected} fields`, formFields === formExpected, `got ${formFields}`);
        audits.push(['form', await domAudit(page)]);

        // 新增 → 列表
        await page.goto(`${baseUrl}/#/contacts-list`, { waitUntil: 'load' });
        await waitForRoute(page, 'contacts-list');
        await page.click('[data-site-action="create"]');
        await waitForRoute(page, 'contact-form');
        await page.fill('.form-field[data-field="fullName"] input', 'Avery Example');
        await page.fill('.form-field[data-field="email"] input', 'avery@example.test');
        await page.getByRole('button', { name: '儲存' }).click();
        await waitForRoute(page, 'contacts-list');
        await page.waitForSelector('[data-site-main][data-record-count="1"]', { timeout: 10000 });
        const firstRow = await page.locator('.dynamic-list tbody tr').first().innerText();
        check('Create from the list adds a row to the in-memory store', firstRow.includes('Avery Example') && firstRow.includes('avery@example.test'), firstRow);
        check('List cells render as text (the toggle shows a yes/no label)', firstRow.includes('是') && !firstRow.includes('[object'), firstRow);
        audits.push(['list after create', await domAudit(page)]);

        // 檢視 → 明細
        await page.locator('.dynamic-list tbody tr').first().locator('[data-legacy-action="view"]').click();
        await waitForRoute(page, 'contact-detail');
        const detailText = await page.locator('.dynamic-detail').innerText();
        const detailHash = await page.evaluate(() => location.hash);
        check('Row view opens the detail page with the created record', detailHash === '#/contact-detail/1' && detailText.includes('Avery Example'), `${detailHash} ${detailText.slice(0, 200)}`);
        audits.push(['detail with record', await domAudit(page)]);

        // 明細編輯 → 表單 → 儲存 → 列表更新
        await page.locator('.dynamic-detail__footer button', { hasText: '編輯' }).click();
        await waitForRoute(page, 'contact-form');
        const prefilled = await page.inputValue('.form-field[data-field="fullName"] input');
        check('Edit opens the form prefilled from the store', prefilled === 'Avery Example', prefilled);
        await page.fill('.form-field[data-field="fullName"] input', 'Avery Example Updated');
        await page.getByRole('button', { name: '儲存' }).click();
        await waitForRoute(page, 'contacts-list');
        await page.waitForSelector('[data-site-main][data-record-count="1"]', { timeout: 10000 });
        const updatedRow = await page.locator('.dynamic-list tbody tr').first().innerText();
        check('Saving the form updates the list row', updatedRow.includes('Avery Example Updated'), updatedRow);

        // 刪除（確認對話框）
        await page.locator('.dynamic-list tbody tr').first().locator('[data-legacy-action="delete"]').click();
        await page.getByRole('button', { name: '確認' }).click();
        await page.waitForSelector('[data-site-main][data-record-count="0"]', { timeout: 10000 });
        check('Delete removes the record after confirmation', true);
        audits.push(['list after delete', await domAudit(page)]);

        // 未知路由
        await page.goto(`${baseUrl}/#/no-such-page`, { waitUntil: 'load' });
        await page.waitForSelector('[data-site-main][data-route-state="not-found"]', { timeout: 10000 });
        check('Unknown routes show a not-found notice', true);

        // 連續換頁：前一頁仍在初始化時就被取代，不得留下錯誤或殘留的頁面
        await page.evaluate(() => {
            for (const hash of ['#/contact-form', '#/contact-detail', '#/contacts-list', '#/contact-detail', '#/contact-form']) {
                window.location.hash = hash;
            }
        });
        await waitForRoute(page, 'contact-form');
        const pagesMounted = await page.locator('[data-site-main] > .ds-page').count();
        check('Rapid route changes settle on the last route with one page mounted', pagesMounted === 1, `got ${pagesMounted}`);
    } finally {
        await context.close();
    }
    reportAudits('Golden', audits, problems);
}

async function allTypesScenario(browser, baseUrl, template) {
    const fields = template.definitions.pages[2].definition.fields;
    const { context, page, problems } = await openBrowserSession(browser, baseUrl);
    const audits = [];
    try {
        await page.goto(`${baseUrl}/#/item-form`, { waitUntil: 'load' });
        await waitForRoute(page, 'item-form');
        const rendered = await page.evaluate(() => [...document.querySelectorAll('.dynamic-form .form-field')].map(node => node.dataset.field));
        const missing = fields.map(field => field.name).filter(name => !rendered.includes(name));
        check(`Form renders every field type open in this slice (${fields.length})`, missing.length === 0 && rendered.length === fields.length, `missing: ${missing.join(', ')}`);
        audits.push(['all-types form', await domAudit(page)]);

        // 每種可直接輸入的型別都填一個值。列表類欄位的每一列都必須有輸入框：沒有輸入框的列只會存下空物件
        // （list 型別因此被擋下），所以先確認有列（沒有就按新增），再在第一列的文字輸入框填值。
        const typed = [];
        const repeatable = fields.filter(field => isRepeatableType(field.type));
        check('All-types form has the repeatable list types to fill in', repeatable.length > 0, JSON.stringify(fields.map(field => field.type)));
        for (const field of fields) {
            const value = TYPED_VALUES[field.type];
            const fieldSelector = `.form-field[data-field="${field.name}"]`;
            if (isRepeatableType(field.type)) {
                const rows = page.locator(`${fieldSelector} .list-input__item`);
                if (await rows.count() === 0) {
                    await page.locator(`${fieldSelector} button`, { hasText: '+' }).last().click();
                }
                const rowCount = await rows.count();
                const rowInputs = rowCount > 0 ? await rows.first().locator('input.text-input').count() : 0;
                check(`${field.type} rows have input fields`, rowCount > 0 && rowInputs > 0, `rows ${rowCount}, text inputs in the first row ${rowInputs}`);
                if (rowInputs > 0 && value) {
                    await rows.first().locator('input.text-input').first().fill(value);
                    typed.push([field, value]);
                }
            } else if (value) {
                await page.locator(`${fieldSelector} input, ${fieldSelector} textarea`).first().fill(value);
                typed.push([field, value]);
            }
        }
        check('Every repeatable list type got a value', repeatable.every(field => typed.some(([entry]) => entry === field)),
            JSON.stringify(typed.map(([field]) => field.type)));

        await page.getByRole('button', { name: '儲存' }).click();
        await waitForRoute(page, 'items-list');
        await page.waitForSelector('[data-site-main][data-record-count="1"]', { timeout: 10000 });
        const expectedColumns = expectedListColumns(template.definitions.pages[0].definition) + 1;
        const headers = await page.locator('.dynamic-list thead th:not(.b4a-dt__th--select)').count();
        const rowText = await page.locator('.dynamic-list tbody tr').first().innerText();
        check(`List shows ${expectedColumns} text-friendly columns for all types`, headers === expectedColumns && !rowText.includes('[object'), `got ${headers}: ${rowText}`);
        audits.push(['all-types list', await domAudit(page)]);

        await page.locator('.dynamic-list tbody tr').first().locator('[data-legacy-action="view"]').click();
        await waitForRoute(page, 'item-detail');
        const detailCount = await page.locator('.dynamic-detail__field').count();
        const expectedDetail = fields.filter(field => field.type !== 'hidden').length;
        check(`Detail renders ${expectedDetail} stored values for all types`, detailCount === expectedDetail, `got ${detailCount}`);
        // 每種開放型別存下的值在明細頁都要顯示成文字（例如 {date,time} 這類物件值不得變成 [object Object]）。
        const detailText = await page.locator('.dynamic-detail').innerText();
        check('Detail shows every open type as text, never a raw object', !detailText.includes('[object'), detailText);
        const missingValues = typed.filter(([, value]) => !detailText.includes(value)).map(([field, value]) => `${field.type}=${value}`);
        check(`Detail shows the ${typed.length} values typed into the form, including list rows`, typed.length > 0 && missingValues.length === 0,
            `missing: ${missingValues.join(', ')}; detail: ${detailText.replace(/\s+/g, ' ')}`);
        audits.push(['all-types detail', await domAudit(page)]);

        await page.locator('.dynamic-detail__footer button', { hasText: '編輯' }).click();
        await waitForRoute(page, 'item-form');
        const prefilled = await page.inputValue('.form-field[data-field="f_text"] input');
        check('All-types record reloads into the form', prefilled === 'Sample text', prefilled);
        audits.push(['all-types edit form', await domAudit(page)]);
    } finally {
        await context.close();
    }
    reportAudits('All-types', audits, problems);
}

async function createOnlyScenario(browser, baseUrl) {
    const { context, page, problems } = await openBrowserSession(browser, baseUrl);
    const audits = [];
    const toastTexts = () => page.evaluate(() => [...document.querySelectorAll('.toast')].map(node => node.textContent));
    try {
        await page.goto(`${baseUrl}/#/feedback-form`, { waitUntil: 'load' });
        await waitForRoute(page, 'feedback-form');
        audits.push(['create-only form', await domAudit(page)]);

        for (const [index, subject] of ['First note', 'Second note'].entries()) {
            await page.fill('.form-field[data-field="subject"] input', subject);
            await page.getByRole('button', { name: '儲存' }).click();
            await page.waitForFunction((count) => [...document.querySelectorAll('.toast')]
                .filter(node => node.textContent.includes('已新增一筆資料')).length >= count, index + 1, { timeout: 10000 });
            await waitForRoute(page, 'feedback-form');
            const state = await page.evaluate(() => ({
                hash: location.hash,
                subject: document.querySelector('.form-field[data-field="subject"] input')?.value ?? null,
                notice: [...document.querySelectorAll('.ds-notice')].filter(node => !node.hidden).map(node => node.textContent)
            }));
            check(`Create-only form saves record ${index + 1} and returns to an empty create form`,
                state.hash === '#/feedback-form' && state.subject === '' && state.notice.length === 0, JSON.stringify(state));
        }
        const toasts = await toastTexts();
        check('Create-only form never reports that it cannot edit', !toasts.some(text => text.includes('只能新增')), JSON.stringify(toasts));
        audits.push(['create-only form after saves', await domAudit(page)]);
    } finally {
        await context.close();
    }
    reportAudits('Create-only', audits, problems);
}

function reportAudits(label, audits, problems) {
    const failedAudits = audits.filter(([, audit]) => !auditPasses(audit));
    const totals = audits.reduce((sum, [, audit]) => ({
        svg: sum.svg + audit.svg,
        styleElements: sum.styleElements + audit.styleElements,
        markupStyleAttributes: sum.markupStyleAttributes + audit.markupStyleAttributes,
        inlineHandlers: sum.inlineHandlers + audit.inlineHandlers,
        cspViolations: sum.cspViolations + audit.cspViolations.length
    }), { svg: 0, styleElements: 0, markupStyleAttributes: 0, inlineHandlers: 0, cspViolations: 0 });
    console.log(`     ${label} DOM audit over ${audits.length} views: ${JSON.stringify(totals)}`);
    check(`${label} site DOM has no svg, <style>, markup style attributes, inline handlers, javascript: URLs or CSP violations`,
        failedAudits.length === 0, JSON.stringify(failedAudits));
    check(`${label} run has no console errors, page errors, HTTP errors or external requests`,
        problems.length === 0, problems.join('\n     '));
}

const chromium = await loadChromium();
if (!chromium) {
    console.error('Definition site smoke needs an existing Playwright runtime (playwright-core) and Microsoft Edge.');
    process.exit(1);
}

let browser;
const servers = [];
try {
    cleanup();
    mkdirSync(workRoot, { recursive: true });
    const golden = JSON.parse(readFileSync(goldenPath, 'utf8'));
    const goldenOut = path.join(workRoot, 'golden');
    const goldenBuild = runBuild(golden, goldenOut, '聯絡人原型');
    check('Golden example builds through the CLI', goldenBuild.status === 0 && goldenBuild.result?.ok === true,
        `${goldenBuild.status} ${goldenBuild.stderr} ${JSON.stringify(goldenBuild.result?.errors ?? null)}`);
    if (goldenBuild.result?.ok) {
        console.log(`     build: ${goldenBuild.result.file_count} files, ${goldenBuild.result.total_size} bytes, ${goldenBuild.elapsed} ms (CLI wall clock)`);
    }

    const allTypes = buildAllTypesTemplate();
    const allTypesOut = path.join(workRoot, 'all-types');
    const allTypesBuild = runBuild(allTypes, allTypesOut, '欄位型別總覽');
    check('All-types template builds through the CLI', allTypesBuild.status === 0 && allTypesBuild.result?.ok === true,
        `${allTypesBuild.status} ${JSON.stringify(allTypesBuild.result?.errors ?? null)}`);

    const createOnly = buildCreateOnlyTemplate();
    const createOnlyOut = path.join(workRoot, 'create-only');
    const createOnlyBuild = runBuild(createOnly, createOnlyOut, '意見回饋');
    check('Create-only template builds through the CLI', createOnlyBuild.status === 0 && createOnlyBuild.result?.ok === true,
        `${createOnlyBuild.status} ${JSON.stringify(createOnlyBuild.result?.errors ?? null)}`);

    if (goldenBuild.result?.ok && allTypesBuild.result?.ok && createOnlyBuild.result?.ok) {
        browser = await chromium.launch({ channel: 'msedge', headless: !headed });
        const goldenServer = await createStaticServer(path.join(goldenOut, 'site'));
        servers.push(goldenServer.server);
        await goldenScenario(browser, goldenServer.baseUrl, golden);

        const allTypesServer = await createStaticServer(path.join(allTypesOut, 'site'));
        servers.push(allTypesServer.server);
        await allTypesScenario(browser, allTypesServer.baseUrl, allTypes);

        const createOnlyServer = await createStaticServer(path.join(createOnlyOut, 'site'));
        servers.push(createOnlyServer.server);
        await createOnlyScenario(browser, createOnlyServer.baseUrl);
    }
} catch (error) {
    check('Smoke run completes without an exception', false, error?.stack || String(error));
} finally {
    await browser?.close();
    for (const server of servers) await closeServer(server);
    cleanup();
}

const failed = results.filter(result => !result.pass);
console.log(`\nDefinition site browser smoke: ${results.length - failed.length}/${results.length} passed.`);
process.exitCode = failed.length === 0 ? 0 : 1;
