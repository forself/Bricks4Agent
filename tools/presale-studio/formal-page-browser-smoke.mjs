// Real browser + isolated SQLite, with a deterministic fake Broker verifier.
// This validates the formal UI integration; it does not use a live Broker identity.
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { existsSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { starterDraft } from './model.mjs';
import { buildPresalePackage } from './server.mjs';
import { createStandaloneServer } from './standalone-server.mjs';

const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
if (!existsSync(edge)) throw new Error('Microsoft Edge is required');
const playwright = await import(new URL('../../../tim-web/poc/node_modules/playwright-core/index.js', import.meta.url));
const chromium = playwright.chromium ?? playwright.default?.chromium;
const draft = starterDraft('blank');
draft.confirmed = true;
const entity = draft.entities[0], [name, description] = entity.fields;
const ids = Array.from({ length: 6 }, (_, index) => `I_${String(index + 1).repeat(12)}`);
const plan = { schema_version: 1, items: [
    { kind: 'builtin', ref: 'TextInput', span: 6, instance_id: ids[0] },
    { kind: 'builtin', ref: 'TextArea', span: 6, instance_id: ids[1] },
    { kind: 'builtin', ref: 'DataTable', span: 12, instance_id: ids[2] },
    ...ids.slice(3).map(instance_id => ({ kind: 'builtin', ref: 'BasicButton', span: 6, instance_id }))
], flows: [
    { entity_code: entity.code, trigger_instance_id: '@page', event: 'load', operation: 'query',
        filters: [], target_instance_id: ids[2], columns: [name.code, description.code] },
    { entity_code: entity.code, trigger_instance_id: ids[3], event: 'click', operation: 'create',
        values: [{ input_instance_id: ids[0], field_code: name.code }, { input_instance_id: ids[1], field_code: description.code }] },
    { entity_code: entity.code, trigger_instance_id: ids[4], event: 'click', operation: 'update',
        source_table_instance_id: ids[2], values: [{ input_instance_id: ids[1], field_code: description.code }] },
    { entity_code: entity.code, trigger_instance_id: ids[5], event: 'click', operation: 'delete', source_table_instance_id: ids[2] }
] };
const built = await buildPresalePackage(draft, plan);
const token = 'test_only_broker_token_0123456789';
const policyPath = path.join(built.temporaryRoot, 'formal-policy.json');
const binding = { principal_id: 'test_principal', task_id: 'test_task', role_id: 'test_role',
    subject: { owner: 'PERSON_A', unit: 'UNIT_A', function: 'FUNCTION_A', descendants: [] },
    roles: [{ ...draft.roles[0], vertical: 'self', horizontal: 'same_unit' }] };
writeFileSync(policyPath, JSON.stringify({ schema_version: 1, principals: [binding] }));
let revoked = false, readOnly = false, checks = 0, server, browser;
const fetcher = async (_url, options) => {
    checks++;
    if (revoked || options.headers.Authorization !== `Bearer ${token}`) return new Response('{}', { status: 401 });
    return Response.json({ success: true, data: { principal_id: 'test_principal', task_id: 'test_task',
        role_id: readOnly ? 'test_reader' : 'test_role', session_id: 'test_session' } });
};
try {
    const reader = { ...binding, role_id: 'test_reader', roles: [{ ...binding.roles[0], actions: ['read'] }] };
    writeFileSync(policyPath, JSON.stringify({ schema_version: 1, principals: [binding, reader] }));
    const startServer = async () => {
        server = createStandaloneServer(built.outDir, { mode: 'formal', policyPath,
            verifyUrl: 'http://127.0.0.1:5099/api/v1/sessions/verify', fetcher });
        server.listen(0, '127.0.0.1');
        await once(server, 'listening');
        return `http://127.0.0.1:${server.address().port}/planned-page.html`;
    };
    let url = await startServer();
    browser = await chromium.launch({ executablePath: edge, headless: true });
    const page = await browser.newPage();
    page.on('dialog', dialog => { void dialog.accept(dialog.type() === 'prompt' ? token : undefined); });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(url);
    const create = page.getByRole('button', { name: '新增資料項目' });
    await create.waitFor();
    assert.equal(await create.isEnabled(), true);
    assert.equal(await page.locator('#test-role').isVisible(), false);
    assert.equal(await page.locator('#reset-prototype').isVisible(), false);
    await page.locator('section.plan-item').nth(0).locator('input').fill('正式模式測試');
    await page.locator('section.plan-item').nth(1).locator('textarea').fill('版本一');
    await create.click();
    await page.waitForFunction(() => document.querySelectorAll('section.plan-item')[2]?.textContent.includes('正式模式測試'));

    // Restart the packaged service and reauthenticate: persisted data must survive.
    server.close(); await once(server, 'close');
    url = await startServer();
    await page.goto(url);
    await page.waitForFunction(() => document.querySelectorAll('section.plan-item')[2]?.textContent.includes('正式模式測試'));
    await page.locator('.b4a-dt__checkbox[data-action="select-row"]').check();
    await page.locator('section.plan-item').nth(1).locator('textarea').fill('版本二');
    await page.getByRole('button', { name: '修改資料項目' }).click();
    await page.waitForFunction(() => document.querySelectorAll('section.plan-item')[2]?.textContent.includes('版本二'));
    readOnly = true;
    await page.reload();
    await create.waitFor();
    assert.equal(await create.isDisabled(), true);
    const api = `/api/prototype/api/presale/e${entity.code.slice(2).toLowerCase()}`;
    const denied = await page.evaluate(async ({ api, code }) => (await fetch(api, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ [code]: 'forged' })
    })).status, { api, code: name.code });
    assert.equal(denied, 403);
    readOnly = false;
    await page.reload();
    await page.locator('.b4a-dt__checkbox[data-action="select-row"]').check();
    await page.getByRole('button', { name: '刪除資料項目' }).click();
    await page.waitForFunction(() => !document.querySelectorAll('section.plan-item')[2]?.textContent.includes('正式模式測試'));
    revoked = true;
    assert.equal(await page.evaluate(async target => (await fetch(target)).status, api), 401);
    assert.ok(checks > 10);
    assert.deepEqual(errors, []);
    console.log('Formal page browser smoke passed: CRUD, restart persistence, read-only denial, token revocation (mock Broker).');
} finally {
    await browser?.close();
    if (server?.listening) { server.close(); await once(server, 'close'); }
    const target = path.resolve(built.temporaryRoot);
    if (!target.startsWith(path.resolve(tmpdir()) + path.sep + 'b4a-presale-')) throw new Error('Unexpected test output');
    rmSync(target, { recursive: true, force: true });
}
