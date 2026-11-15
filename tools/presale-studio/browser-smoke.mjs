// Optional real-browser check for the generated declarative node wiring.
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { existsSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { starterDraft } from './model.mjs';
import { defaultCandidateWiring, generateBindings, generateCandidates } from './page-planner.mjs';
import { buildPresalePackage, createPresaleServer } from './server.mjs';
import { createStandaloneServer } from './standalone-server.mjs';

const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
if (!existsSync(edge)) throw new Error('Microsoft Edge is required for this optional browser check');
const playwright = await import(new URL('../../../tim-web/poc/node_modules/playwright-core/index.js', import.meta.url));
const chromium = playwright.chromium ?? playwright.default?.chromium;
if (!chromium) throw new Error('An existing playwright-core runtime is required');
const draft = starterDraft('course');
draft.confirmed = true;
const candidates = generateCandidates(draft), bindings = generateBindings(draft);
const plan = { schema_version: 1, items: candidates.slice(0, 3).map((candidate, index) => {
    const instance_id = `I_${String(index + 1).repeat(12)}`;
    return { kind: 'candidate', ref: candidate.registry_name, span: 12, instance_id,
        wiring: defaultCandidateWiring(candidate, bindings[index], instance_id) };
}) };
plan.items.push({ kind: 'builtin', ref: 'BasicButton', span: 12,
    binding: { entity_code: draft.entities[0].code, operation: 'search' } });
plan.items.push(
    { kind: 'builtin', ref: 'TextInput', span: 6, instance_id: 'I_444444444444' },
    { kind: 'builtin', ref: 'BasicButton', span: 6, instance_id: 'I_555555555555' },
    { kind: 'builtin', ref: 'DataTable', span: 12, instance_id: 'I_666666666666' },
    { kind: 'builtin', ref: 'Text', span: 6, instance_id: 'I_777777777777' },
    { kind: 'builtin', ref: 'Dropdown', span: 6, instance_id: 'I_888888888888' },
    { kind: 'builtin', ref: 'NumberInput', span: 6, instance_id: 'I_999999999999' },
    { kind: 'builtin', ref: 'DataTable', span: 12, instance_id: 'I_AAAAAAAAAAAA' },
    { kind: 'builtin', ref: 'ToggleSwitch', span: 6, instance_id: 'I_BBBBBBBBBBBB' },
    { kind: 'builtin', ref: 'DataTable', span: 12, instance_id: 'I_CCCCCCCCCCCC' }
);
plan.flows = [
    { entity_code: draft.entities[0].code, trigger_instance_id: 'I_555555555555', event: 'click',
        operation: 'query', filters: [{ input_instance_id: 'I_444444444444',
            field_code: draft.entities[0].fields[0].code, operator: 'contains' }],
        target_instance_id: 'I_666666666666', columns: draft.entities[0].fields.slice(0, 4).map(field => field.code) },
    { entity_code: draft.entities[0].code, trigger_instance_id: '@page', event: 'load',
        operation: 'summary', filters: [], target_instance_id: 'I_777777777777', metric: { op: 'count' } },
    { entity_code: draft.entities[0].code, trigger_instance_id: '@page', event: 'load',
        operation: 'lookup', filters: [], target_instance_id: 'I_888888888888',
        field_code: draft.entities[0].fields[0].code },
    { entity_code: draft.entities[0].code, trigger_instance_id: 'I_999999999999', event: 'change',
        operation: 'query', filters: [{ input_instance_id: 'I_999999999999',
            field_code: draft.entities[0].fields[4].code, operator: 'gte' }],
        target_instance_id: 'I_AAAAAAAAAAAA', columns: draft.entities[0].fields.slice(0, 4).map(field => field.code) },
    { entity_code: draft.entities[1].code, trigger_instance_id: '@page', event: 'load',
        operation: 'query', filters: [{ input_instance_id: 'I_BBBBBBBBBBBB',
            field_code: draft.entities[1].fields[4].code, operator: 'eq' }],
        target_instance_id: 'I_CCCCCCCCCCCC', columns: draft.entities[1].fields.slice(0, 4).map(field => field.code) },
    { entity_code: draft.entities[1].code, trigger_instance_id: 'I_BBBBBBBBBBBB', event: 'change',
        operation: 'query', filters: [{ input_instance_id: 'I_BBBBBBBBBBBB',
            field_code: draft.entities[1].fields[4].code, operator: 'eq' }],
        target_instance_id: 'I_CCCCCCCCCCCC', columns: draft.entities[1].fields.slice(0, 4).map(field => field.code) }
];
const built = await buildPresalePackage(draft, plan);
let server, workbench, browser;
try {
    server = createStandaloneServer(built.outDir);
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const base = `http://127.0.0.1:${server.address().port}`;
    browser = await chromium.launch({ executablePath: edge, headless: true, args: ['--no-sandbox'] });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('console', message => { if (message.type() === 'error' &&
        !message.text().includes('401 (Unauthorized)')) errors.push(message.text()); });
    page.on('response', response => { if (response.status() >= 400 &&
        !(response.status() === 401 && response.url().endsWith('/api/prototype/session')))
        errors.push(`${response.status()} ${response.url()}`); });
    await page.goto(`${base}/planned-page.html`);
    await page.waitForTimeout(1200);
    if (errors.length || !await page.locator('[data-custom-component-node="results"]').count())
        throw new Error(`Page did not mount: ${JSON.stringify(errors)} ${await page.locator('body').innerText()}`);
    await page.locator('[data-custom-component-node="results"] table').waitFor();
    await page.waitForFunction(() => document.querySelector('.plan-results')?.textContent?.includes('符合 3 筆'));
    const result = page.locator('[data-custom-component-node="results"]');
    assert.match(await result.innerText(), /CS101/);
    assert.match(await page.locator('[data-custom-component-node="metric"]').innerText(), /筆數：3/);
    assert.match(await page.locator('section.plan-item').nth(7).innerText(), /筆數：3/);
    await page.locator('section.plan-item').nth(8).locator('.dropdown__selector').click();
    await page.waitForFunction(() => document.querySelectorAll('section.plan-item')[8]
        ?.querySelectorAll('.dropdown__option:not(.dropdown__option--empty)').length === 3);
    await page.locator('section.plan-item').nth(9).locator('input').fill('3');
    await page.locator('section.plan-item').nth(9).locator('input').press('Tab');
    await page.waitForFunction(() => document.querySelectorAll('section.plan-item')[10]?.textContent?.includes('CS101'));
    assert.doesNotMatch(await page.locator('section.plan-item').nth(10).innerText(), /EN305/);
    assert.match(await page.locator('section.plan-item').nth(12).innerText(), /陳老師/);
    await page.locator('section.plan-item').nth(11).locator('.toggle-switch').click();
    await page.locator('section.plan-item').nth(11).locator('.toggle-switch').click();
    await page.waitForFunction(() => !document.querySelectorAll('section.plan-item')[12]
        ?.textContent?.includes('陳老師'), null, { timeout: 4000 });
    await page.locator('section.plan-item').nth(4).locator('input').fill('CS101');
    await page.locator('section.plan-item').nth(5).getByRole('button', { name: '操作' }).click();
    await page.waitForFunction(() => document.querySelectorAll('section.plan-item')[6]?.textContent?.includes('CS101'));
    assert.doesNotMatch(await page.locator('section.plan-item').nth(6).innerText(), /EN305/);
    await page.locator('[data-custom-component-node="filter-0"] input').fill('CS101');
    await page.locator('[data-custom-component-node="submit"] button').click();
    await page.waitForFunction(() => document.querySelector('.plan-results')?.textContent?.includes('符合 1 筆'));
    assert.doesNotMatch(await result.innerText(), /EN305/);
    await page.locator('[data-custom-component-node="filter-0"] input').fill('MA201');
    await page.getByRole('button', { name: '查詢課程' }).click();
    await page.waitForFunction(() => document.querySelector('[data-custom-component-node="results"]')?.textContent?.includes('MA201'));
    assert.doesNotMatch(await result.innerText(), /CS101/);
    await page.locator('[data-custom-component-node="field-1"] input').fill('瀏覽器驗證課程');
    await page.locator('[data-custom-component-node="save"] button').click();
    await page.waitForFunction(() => document.querySelector('#plan-status')?.textContent?.includes('已儲存'));
    const resource = built.compiled.mapping[0].entity_code;
    const api = `/api/presale/e${resource.slice(2).toLowerCase()}`;
    const saved = await page.evaluate(async value => (await fetch(`/api/prototype${value}/1`)).json(), api);
    assert.equal(saved[draft.entities[0].fields[1].code], '瀏覽器驗證課程');
    assert.deepEqual(errors, []);
    workbench = createPresaleServer({ libraryRoot: path.join(built.temporaryRoot, 'library') });
    workbench.listen(0, '127.0.0.1');
    await once(workbench, 'listening');
    const studio = await browser.newPage();
    const studioErrors = [];
    studio.on('pageerror', error => studioErrors.push(error.message));
    studio.on('response', response => { if (response.status() >= 400 &&
        !(response.status() === 401 && response.url().includes('/session')))
        studioErrors.push(`${response.status()} ${response.url()}`); });
    const studioBase = `http://127.0.0.1:${workbench.address().port}`;
    await studio.goto(studioBase);
    await studio.locator('#confirmed').check();
    await studio.locator('#component-palette .palette-item').first().getByText('加入').click();
    await studio.locator('.wiring-panel').first().waitFor();
    const fieldSelect = studio.getByLabel('filter-0 欄位');
    const chosenCode = await fieldSelect.evaluate(node => node.options[2]?.value);
    assert.match(chosenCode, /^F_[A-F0-9]{12}$/);
    await fieldSelect.selectOption(chosenCode);
    await studio.locator('.wiring-panel summary').click();
    await studio.locator('.wiring-action').first().getByLabel('觸發節點').selectOption('filter-0');
    await studio.locator('#generate').click();
    await studio.locator('#result').waitFor({ state: 'visible' });
    const planned = await studio.locator('#open-planned').getAttribute('href');
    const generatedPlan = await studio.evaluate(async href =>
        (await fetch(href.replace('planned-page.html', 'page-plan.json'))).json(), planned);
    assert.equal(generatedPlan.items[0].wiring.fields[0].field_code, chosenCode);
    assert.equal(generatedPlan.items[0].wiring.actions[0].node_id, 'filter-0');
    assert.equal(generatedPlan.items[0].wiring.actions[0].event, 'change');
    await studio.goto(`${studioBase}${planned}`);
    await studio.waitForTimeout(1000);
    if (!await studio.locator('[data-custom-component-node="filter-0"] input').count())
        throw new Error(`Generated page did not mount: ${JSON.stringify(studioErrors)} ${await studio.locator('body').innerText()}`);
    await studio.locator('[data-custom-component-node="filter-0"] input').fill('資訊系');
    await studio.locator('[data-custom-component-node="filter-0"] input').press('Tab');
    await studio.waitForFunction(() => document.querySelector('.plan-results')?.textContent?.includes('符合 1 筆'));
    const buildId = /\/preview\/([a-f0-9]{32})\//.exec(planned)?.[1];
    assert.ok(buildId);
    const legacy = await studio.evaluate(async ({ id, ref }) => {
        const response = await fetch(`/api/agent/plan/${id}`, { method: 'POST',
            headers: { 'content-type': 'application/json' },
            body: JSON.stringify({ schema_version: 1, items: [{ kind: 'candidate', ref, span: 12 }] }) });
        if (!response.ok) throw new Error(`Legacy plan HTTP ${response.status}`);
        return response.json();
    }, { id: buildId, ref: generatedPlan.items[0].ref });
    await studio.goto(`${studioBase}${legacy.planned_page_url}`);
    await studio.waitForFunction(() => document.querySelector('.plan-results')?.textContent?.includes('符合 3 筆'));
    await studio.locator('[data-custom-component-node="filter-0"] input').fill('CS101');
    await studio.locator('[data-custom-component-node="submit"] button').click();
    await studio.waitForFunction(() => document.querySelector('.plan-results')?.textContent?.includes('符合 1 筆'));
    assert.deepEqual(studioErrors, []);
    const flowStudio = await browser.newPage();
    const flowErrors = [];
    flowStudio.on('pageerror', error => flowErrors.push(error.message));
    await flowStudio.goto(studioBase);
    await flowStudio.locator('#confirmed').check();
    for (const name of ['文字輸入', '操作按鈕', '資料表']) {
        await flowStudio.getByText(name, { exact: true }).locator('..').locator('..')
            .getByRole('button', { name: '加入' }).click();
    }
    await flowStudio.locator('#add-flow').click();
    await flowStudio.locator('#page-flows summary').click();
    const triggerValue = await flowStudio.getByLabel('觸發元件與事件').evaluate(node =>
        [...node.options].find(option => option.textContent.includes('操作按鈕'))?.value);
    assert.match(triggerValue, /^I_[A-F0-9]{12}\|click$/);
    await flowStudio.getByLabel('觸發元件與事件').selectOption(triggerValue);
    await flowStudio.getByRole('button', { name: '增加查詢條件' }).click();
    await flowStudio.locator('#generate').click();
    await flowStudio.locator('#result').waitFor({ state: 'visible' });
    const flowPage = await flowStudio.locator('#open-planned').getAttribute('href');
    const flowPlan = await flowStudio.evaluate(async href =>
        (await fetch(href.replace('planned-page.html', 'page-plan.json'))).json(), flowPage);
    assert.equal(flowPlan.flows.length, 1);
    assert.equal(flowPlan.flows[0].trigger_instance_id, triggerValue.split('|')[0]);
    assert.equal(flowPlan.flows[0].filters.length, 1);
    await flowStudio.goto(`${studioBase}${flowPage}`);
    await flowStudio.locator('section.plan-item').first().locator('input').fill('CS101');
    await flowStudio.locator('section.plan-item').nth(1).getByRole('button', { name: '操作' }).click();
    await flowStudio.waitForFunction(() => document.querySelectorAll('section.plan-item')[2]?.textContent?.includes('CS101'));
    assert.doesNotMatch(await flowStudio.locator('section.plan-item').nth(2).innerText(), /EN305/);
    assert.deepEqual(flowErrors, []);
    process.stdout.write('Presale browser smoke passed: composite wiring, standalone query/summary/lookup flows, workbench binding, save, legacy plan.\n');
} finally {
    if (browser) await browser.close();
    if (workbench) { workbench.close(); await once(workbench, 'close'); }
    if (server) { server.close(); await once(server, 'close'); }
    const tempRoot = path.resolve(os.tmpdir());
    const target = path.resolve(built.temporaryRoot);
    if (!target.startsWith(`${tempRoot}${path.sep}`)) throw new Error('Unexpected temporary package path');
    rmSync(target, { recursive: true, force: true });
}
