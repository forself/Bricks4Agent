import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { request as httpRequest } from 'node:http';
import { createPresaleServer } from './server.mjs';
import { starterDraft } from './model.mjs';
import { defaultCandidateWiring, generateBindings, generateCandidates } from './page-planner.mjs';

function rawStatus(base, target) {
    return new Promise((resolve, reject) => {
        const req = httpRequest(new URL(base), { path: target, method: 'GET' }, res => {
            res.resume();
            res.on('end', () => resolve(res.statusCode));
        });
        req.on('error', reject);
        req.end();
    });
}

test('business draft builds a seeded preview, reviewed package and downloadable ZIP', async () => {
    const server = createPresaleServer();
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const base = `http://127.0.0.1:${server.address().port}`;
    try {
        assert.equal(await rawStatus(base, '/%2e%2e/api/build'), 400);
        const wrongMethod = await fetch(`${base}/api/build`);
        assert.equal(wrongMethod.status, 405);
        assert.equal(wrongMethod.headers.get('allow'), 'POST');
        const draft = starterDraft('course');
        draft.confirmed = true;
        const firstCandidate = generateCandidates(draft)[0];
        const plan = { schema_version: 1, items: [
            { kind: 'candidate', ref: firstCandidate.registry_name, span: 12 },
            { kind: 'builtin', ref: 'Heading', span: 6 }
        ] };
        const response = await fetch(`${base}/api/build`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base }, body: JSON.stringify({ draft, plan }) });
        assert.equal(response.status, 200);
        const result = await response.json();
        assert.equal(result.page_count, 9);
        assert.equal(result.module_count, 3);
        assert.equal(result.component_count, 9);
        assert.match(result.review, /課程/);
        const site = await (await fetch(`${base}${result.preview_url}site.json`)).json();
        assert.equal(site.demo_seed, 'demo-seed.json');
        assert.equal(site.prototype_backend, 'sqlite');
        assert.equal(site.pages.length, 9);
        const seed = await (await fetch(`${base}${result.preview_url}demo-seed.json`)).json();
        assert.equal(Object.keys(seed).length, 3);
        assert.equal(Object.values(seed)[0].length, 3);
        const savedPlan = await (await fetch(`${base}${result.preview_url}page-plan.json`)).json();
        assert.deepEqual(savedPlan, plan);
        const definitions = await (await fetch(`${base}${result.preview_url}candidate-components.json`)).json();
        assert.equal(definitions.length, 9);
        const config = await (await fetch(`${base}${result.preview_url}prototype-config.json`)).json();
        assert.equal(config.roles.length, 3);
        const apiBase = `/api/prototype/${result.preview_url.split('/')[2]}`;
        const resource = config.resources[0];
        assert.equal((await fetch(`${base}${apiBase}${resource.api}`)).status, 401);
        const login = await fetch(`${base}${apiBase}/demo/session`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base },
            body: JSON.stringify({ role_code: config.roles[1].code }) });
        assert.equal(login.status, 200);
        const cookie = login.headers.get('set-cookie').split(';')[0];
        const scoped = await fetch(`${base}${apiBase}${resource.api}`, { headers: { cookie } });
        assert.equal((await scoped.json()).total, 2);
        assert.equal(scoped.headers.get('x-content-type-options'), 'nosniff');
        assert.equal(scoped.headers.get('referrer-policy'), 'no-referrer');
        assert.equal((await fetch(`${base}${apiBase}${resource.api}?page=1&page=2`,
            { headers: { cookie } })).status, 400);
        assert.equal((await fetch(`${base}${apiBase}${resource.api}?unexpected=1`,
            { headers: { cookie } })).status, 400);
        assert.equal((await fetch(`${base}${apiBase}${resource.api}?page=abc`,
            { headers: { cookie } })).status, 400);
        assert.equal((await fetch(`${base}${apiBase}${resource.api}?pageSize=101`,
            { headers: { cookie } })).status, 400);
        assert.equal((await fetch(`${base}${apiBase}${resource.api}/1?page=1`,
            { headers: { cookie } })).status, 400);
        assert.equal((await fetch(`${base}${apiBase}${resource.api}`, { method: 'POST',
            headers: { cookie, 'content-type': 'text/plain', origin: base }, body: '{}' })).status, 415);
        assert.equal((await fetch(`${base}${result.preview_url}prototype-store.mjs`)).status, 200);
        assert.equal((await fetch(`${base}${result.preview_url}candidate-bindings.json`)).status, 200);
        const report = await (await fetch(`${base}${result.preview_url}functional-validation.json`)).json();
        assert.equal(report.result, 'passed');
        assert.equal(result.functional_validation.result, 'passed');
        const paletteResponse = await fetch(`${base}${result.agent_palette_url}`);
        const paletteText = await paletteResponse.text();
        assert.equal(paletteResponse.status, 200);
        assert.equal(paletteText.includes('課程'), false);
        const palette = JSON.parse(paletteText);
        assert.ok(Array.isArray(palette.saved_templates));
        assert.equal(palette.candidates.length, 9);
        assert.deepEqual(palette.architecture.backend_modules,
            ['query', 'lookup', 'summary', 'csv_export', 'batch_create']);
        assert.equal(palette.candidates[0].nodes.find(node => node.node_id === 'results').result_ports[0], 'rows');
        assert.ok(palette.plan_schema.wiring.operations.includes('summary'));
        assert.deepEqual(palette.builtins.find(item => item.ref === 'DataTable').result_ports, ['rows']);
        assert.ok(palette.plan_schema.page_flows.operations.includes('query'));
        assert.deepEqual(palette.plan_schema.page_flows.required,
            ['entity_code', 'trigger_instance_id', 'event', 'operation']);
        assert.deepEqual(palette.plan_schema.page_flows.operation_fields.update,
            ['source_table_instance_id', 'values']);
        assert.deepEqual(palette.plan_schema.page_flows.write.source,
            { component: 'DataTable', query_operation: 'query', entity_match: 'same',
                selected_row_required: true });
        const boundProposal = { schema_version: 1, items: [
            { kind: 'candidate', ref: palette.candidates[0].ref, span: 12,
                binding: { field_codes: [draft.entities[0].fields[1].code, draft.entities[0].fields[2].code] } },
            { kind: 'builtin', ref: 'BasicButton', span: 6,
                binding: { entity_code: draft.entities[0].code, operation: 'search' } }
        ] };
        const boundResponse = await fetch(`${base}${result.agent_plan_url}`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base }, body: JSON.stringify(boundProposal) });
        assert.equal(boundResponse.status, 200);
        const boundResult = await boundResponse.json();
        assert.deepEqual(await (await fetch(`${base}${boundResult.preview_url}page-plan.json`)).json(), boundProposal);
        const agentProposal = { schema_version: 1, items: [
            { kind: 'candidate', ref: palette.candidates[0].ref, span: 12 }
        ] };
        const agentResultResponse = await fetch(`${base}${result.agent_plan_url}`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base }, body: JSON.stringify(agentProposal) });
        assert.equal(agentResultResponse.status, 200);
        const agentResult = await agentResultResponse.json();
        assert.deepEqual(await (await fetch(`${base}${agentResult.preview_url}page-plan.json`)).json(), agentProposal);
        const wiredId = 'I_123456789ABC';
        const wiredPlan = { schema_version: 1, items: [{ kind: 'candidate',
            ref: palette.candidates[0].ref, span: 12, instance_id: wiredId,
            wiring: defaultCandidateWiring(generateCandidates(draft)[0], generateBindings(draft)[0], wiredId) }] };
        const wiredResponse = await fetch(`${base}${result.agent_plan_url}`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base }, body: JSON.stringify(wiredPlan) });
        assert.equal(wiredResponse.status, 200);
        const wiredResult = await wiredResponse.json();
        assert.deepEqual(await (await fetch(`${base}${wiredResult.preview_url}page-plan.json`)).json(), wiredPlan);
        const flowPlan = { schema_version: 1, items: [
            { kind: 'builtin', ref: 'TextInput', span: 6, instance_id: 'I_111111111111' },
            { kind: 'builtin', ref: 'BasicButton', span: 6, instance_id: 'I_222222222222' },
            { kind: 'builtin', ref: 'DataTable', span: 12, instance_id: 'I_333333333333' }
        ], flows: [{ entity_code: draft.entities[0].code, trigger_instance_id: 'I_222222222222',
            event: 'click', operation: 'query', filters: [{ input_instance_id: 'I_111111111111',
                field_code: draft.entities[0].fields[0].code, operator: 'contains' }],
            target_instance_id: 'I_333333333333', columns: [draft.entities[0].fields[0].code] }] };
        const flowResponse = await fetch(`${base}${result.agent_plan_url}`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base }, body: JSON.stringify(flowPlan) });
        assert.equal(flowResponse.status, 200);
        const flowResult = await flowResponse.json();
        assert.deepEqual(await (await fetch(`${base}${flowResult.preview_url}page-plan.json`)).json(), flowPlan);
        const writePlan = structuredClone(flowPlan);
        writePlan.items.push({ kind: 'builtin', ref: 'BasicButton', span: 6,
            instance_id: 'I_444444444444' });
        writePlan.flows.push({ entity_code: draft.entities[0].code,
            trigger_instance_id: 'I_444444444444', event: 'click', operation: 'update',
            source_table_instance_id: 'I_333333333333',
            values: [{ input_instance_id: 'I_111111111111', field_code: draft.entities[0].fields[0].code }] });
        const writeResponse = await fetch(`${base}${result.agent_plan_url}`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base }, body: JSON.stringify(writePlan) });
        assert.equal(writeResponse.status, 200);
        const writeResult = await writeResponse.json();
        assert.deepEqual(await (await fetch(`${base}${writeResult.preview_url}page-plan.json`)).json(), writePlan);
        writePlan.flows[1].script = 'alert(1)';
        const rejectedWrite = await fetch(`${base}${result.agent_plan_url}`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base }, body: JSON.stringify(writePlan) });
        assert.equal(rejectedWrite.status, 400);
        const badAgentResponse = await fetch(`${base}${result.agent_plan_url}`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base },
            body: JSON.stringify({ ...agentProposal, instruction: 'run code' }) });
        assert.equal(badAgentResponse.status, 400);
        const plannedPage = await (await fetch(`${base}${result.planned_page_url}`)).text();
        assert.match(plannedPage, /planned-page\.js/);
        assert.equal((await fetch(`${base}${result.preview_url}runtime/ui_components/data/index.js`)).status, 200);
        const html = await (await fetch(`${base}${result.preview_url}`)).text();
        assert.match(html, /boot\.js/);
        assert.match(html, /href="planned-page\.html"/);
        const zip = new Uint8Array(await (await fetch(`${base}${result.download_url}`)).arrayBuffer());
        assert.ok(zip.length > 100_000);
        assert.deepEqual([...zip.slice(0, 4)], [0x50, 0x4b, 0x03, 0x04]);
        assert.ok(Buffer.from(zip).includes(Buffer.from('backend/standalone-server.mjs')));
        assert.ok(Buffer.from(zip).includes(Buffer.from('backend/prototype-router.mjs')));
        assert.ok(Buffer.from(zip).includes(Buffer.from('backend/native-router.mjs')));
        assert.ok(Buffer.from(zip).includes(Buffer.from('backend/query-module.mjs')));
        assert.ok(Buffer.from(zip).includes(Buffer.from('backend/report-module.mjs')));
        assert.ok(Buffer.from(zip).includes(Buffer.from('backend/batch-module.mjs')));
        assert.ok(Buffer.from(zip).includes(Buffer.from('README.txt')));
        const rejected = await fetch(`${base}/api/build`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: 'http://example.test' },
            body: JSON.stringify({ draft }) });
        assert.equal(rejected.status, 403);
        const forged = await fetch(`${base}/api/build`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base },
            body: JSON.stringify({ draft, plan: { schema_version: 1, items: [
                { kind: 'candidate', ref: 'PresaleForged', span: 12 }
            ] } }) });
        assert.equal(forged.status, 400);
    } finally {
        server.close();
        await once(server, 'close');
    }
});
