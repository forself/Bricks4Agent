// Real-browser proof that standalone JSON write flows use only the demo SQLite resource.
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { existsSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { starterDraft } from './model.mjs';
import { buildPresalePackage, createPresaleServer } from './server.mjs';
import { createStandaloneServer } from './standalone-server.mjs';

const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
if (!existsSync(edge)) throw new Error('Microsoft Edge is required for this optional browser check');
const playwright = await import(new URL('../../../tim-web/poc/node_modules/playwright-core/index.js', import.meta.url));
const chromium = playwright.chromium ?? playwright.default?.chromium;
if (!chromium) throw new Error('An existing playwright-core runtime is required');

const draft = starterDraft('blank');
draft.confirmed = true;
draft.roles.push({ code: 'R_111111111111', label: '唯讀', actions: ['read'],
    vertical: 'all', horizontal: 'all' });
const [name, description] = draft.entities[0].fields;
const ids = ['I_111111111111', 'I_222222222222', 'I_333333333333',
    'I_444444444444', 'I_555555555555', 'I_666666666666'];
const plan = { schema_version: 1, items: [
    { kind: 'builtin', ref: 'TextInput', span: 6, instance_id: ids[0] },
    { kind: 'builtin', ref: 'TextArea', span: 6, instance_id: ids[1] },
    { kind: 'builtin', ref: 'DataTable', span: 12, instance_id: ids[2] },
    ...ids.slice(3).map(instance_id => ({ kind: 'builtin', ref: 'BasicButton', span: 6, instance_id }))
], flows: [
    { entity_code: draft.entities[0].code, trigger_instance_id: '@page', event: 'load',
        operation: 'query', filters: [], target_instance_id: ids[2],
        columns: [name.code, description.code] },
    { entity_code: draft.entities[0].code, trigger_instance_id: ids[3], event: 'click',
        operation: 'create', values: [
            { input_instance_id: ids[0], field_code: name.code },
            { input_instance_id: ids[1], field_code: description.code }
        ] },
    { entity_code: draft.entities[0].code, trigger_instance_id: ids[4], event: 'click',
        operation: 'update', source_table_instance_id: ids[2],
        values: [{ input_instance_id: ids[1], field_code: description.code }] },
    { entity_code: draft.entities[0].code, trigger_instance_id: ids[5], event: 'click',
        operation: 'delete', source_table_instance_id: ids[2] }
] };

const built = await buildPresalePackage(draft, plan);
let server, workbench, browser;
try {
    server = createStandaloneServer(built.outDir);
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    browser = await chromium.launch({ executablePath: edge, headless: true, args: ['--no-sandbox'] });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(`http://127.0.0.1:${server.address().port}/planned-page.html`);
    const api = `/api/prototype/api/presale/e${draft.entities[0].code.slice(2).toLowerCase()}`;
    const readRows = () => page.evaluate(async target => (await fetch(target)).json(), api);
    await page.locator('section.plan-item').nth(0).locator('input').fill('測試項目');
    await page.locator('section.plan-item').nth(1).locator('textarea').fill('版本一');
    await page.getByRole('button', { name: '新增資料項目' }).click();
    await page.waitForFunction(() => document.querySelectorAll('section.plan-item')[2]
        ?.textContent?.includes('測試項目'));
    let rows = await readRows();
    assert.equal(rows.total, 1);
    assert.equal(rows.items[0][description.code], '版本一');
    const selectedRow = page.locator('section.plan-item').nth(2)
        .locator('.b4a-dt__checkbox[data-action="select-row"]');
    await selectedRow.check();
    await page.getByRole('button', { name: '修改資料項目' }).waitFor({ state: 'visible' });
    assert.equal(await page.locator('section.plan-item').nth(1).locator('textarea').inputValue(), '版本一');
    await page.locator('section.plan-item').nth(1).locator('textarea').fill('版本二');
    await page.getByRole('button', { name: '修改資料項目' }).click();
    await page.waitForFunction(() => document.querySelectorAll('section.plan-item')[2]
        ?.textContent?.includes('版本二'));
    rows = await readRows();
    assert.equal(rows.items[0][description.code], '版本二');
    await page.locator('section.plan-item').nth(2)
        .locator('.b4a-dt__checkbox[data-action="select-row"]').check();
    page.once('dialog', dialog => { void dialog.accept(); });
    await page.getByRole('button', { name: '刪除資料項目' }).click();
    await page.waitForFunction(() => !document.querySelectorAll('section.plan-item')[2]
        ?.textContent?.includes('測試項目'));
    rows = await readRows();
    assert.equal(rows.total, 0);
    await page.locator('#test-role').selectOption('R_111111111111');
    await page.waitForFunction(() => document.querySelector('#test-role')?.value === 'R_111111111111');
    await page.getByRole('button', { name: '新增資料項目' }).waitFor({ state: 'visible' });
    assert.equal(await page.getByRole('button', { name: '新增資料項目' }).isDisabled(), true);
    assert.deepEqual(errors, []);

    workbench = createPresaleServer({ libraryRoot: path.join(built.temporaryRoot, 'library') });
    workbench.listen(0, '127.0.0.1');
    await once(workbench, 'listening');
    const studio = await browser.newPage();
    await studio.goto(`http://127.0.0.1:${workbench.address().port}/`);
    await studio.locator('#presets').getByRole('button', { name: /空白開始/ }).click();
    await studio.locator('#confirmed').check();
    for (const component of ['文字輸入', '多行輸入', '資料表',
        '操作按鈕', '操作按鈕', '操作按鈕'])
        await studio.getByText(component, { exact: true }).locator('..').locator('..')
            .getByRole('button', { name: '加入' }).click();
    for (const button of ['新增查詢流程', '新增資料', '修改資料', '刪除資料'])
        await studio.locator('#planner-panel').getByRole('button', { name: button, exact: true }).click();
    await studio.locator('#page-flows details').nth(1).locator('summary').click();
    await studio.locator('#page-flows details').nth(1).getByRole('button', { name: '增加寫入欄' }).click();
    await studio.locator('#generate').click();
    await studio.locator('#result').waitFor({ state: 'visible' });
    const href = await studio.locator('#open-planned').getAttribute('href');
    const generated = await studio.evaluate(async target =>
        (await fetch(target.replace('planned-page.html', 'page-plan.json'))).json(), href);
    assert.deepEqual(generated.flows.map(flow => flow.operation), ['query', 'create', 'update', 'delete']);
    assert.equal(generated.flows[1].values.length, 2);
    await studio.locator('#page-flows details').first().locator('summary').click();
    await studio.locator('#page-flows details').first()
        .getByRole('button', { name: '移除此流程' }).click();
    assert.equal(await studio.locator('#page-flows details').count(), 1);
    assert.match(await studio.locator('#page-flows details summary').first().textContent(), /新增/);
    await studio.locator('#generate').click();
    await studio.waitForFunction(previous => document.querySelector('#open-planned')?.getAttribute('href') !== previous,
        href);
    const revisedHref = await studio.locator('#open-planned').getAttribute('href');
    const revised = await studio.evaluate(async target =>
        (await fetch(target.replace('planned-page.html', 'page-plan.json'))).json(), revisedHref);
    assert.deepEqual(revised.flows.map(flow => flow.operation), ['create']);
    process.stdout.write('Presale write flow browser smoke passed: workbench wiring, SQLite create/update/delete, role gate.\n');
} finally {
    if (browser) await browser.close();
    if (workbench) { workbench.close(); await once(workbench, 'close'); }
    if (server) { server.close(); await once(server, 'close'); }
    const tempRoot = path.resolve(os.tmpdir()), target = path.resolve(built.temporaryRoot);
    if (!target.startsWith(`${tempRoot}${path.sep}`)) throw new Error('Unexpected temporary package path');
    rmSync(target, { recursive: true, force: true });
}
