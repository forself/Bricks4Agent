import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { mkdtempSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { starterDraft } from './model.mjs';
import { defaultCandidateWiring, generateBindings, generateCandidates } from './page-planner.mjs';
import { createPresaleServer } from './server.mjs';
import { LocalTemplateLibrary } from './local-library.mjs';

test('local library HTTP routes save, list, rebind and remove component and page JSON', async () => {
    const root = mkdtempSync(path.join(os.tmpdir(), 'b4a-library-api-'));
    const server = createPresaleServer({ libraryRoot: root });
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const base = `http://127.0.0.1:${server.address().port}`;
    const importedRoot = mkdtempSync(path.join(os.tmpdir(), 'b4a-library-api-import-'));
    const importedServer = createPresaleServer({ libraryRoot: importedRoot });
    importedServer.listen(0, '127.0.0.1');
    await once(importedServer, 'listening');
    const importedBase = `http://127.0.0.1:${importedServer.address().port}`;
    try {
        const draft = starterDraft('blank'), target = starterDraft('blank');
        draft.confirmed = true; target.confirmed = true;
        const candidate = generateCandidates(draft)[0], binding = generateBindings(draft)[0];
        const instance_id = 'I_111111111111';
        const plan = { schema_version: 1, items: [{ kind: 'candidate', ref: candidate.registry_name,
            span: 12, instance_id,
            wiring: defaultCandidateWiring(candidate, binding, instance_id) }] };
        const post = (url, body) => fetch(`${base}${url}`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base }, body: JSON.stringify(body) });
        const savedResponse = await post('/api/library/component',
            { title: '資料查詢元件', draft, plan, instance_id });
        assert.equal(savedResponse.status, 201);
        const saved = await savedResponse.json();
        const pagesResponse = await post('/api/library/page', { title: '資料查詢頁', draft, plan });
        assert.equal(pagesResponse.status, 201);
        const page = await pagesResponse.json();
        const list = await (await fetch(`${base}/api/library`)).json();
        assert.deepEqual(new Set(list.items.map(item => item.kind)), new Set(['component', 'page']));
        const exportedResponse = await fetch(`${base}/api/library/export`);
        assert.equal(exportedResponse.status, 200);
        assert.match(exportedResponse.headers.get('content-disposition'), /b4a-local-library\.json/);
        const bundle = await exportedResponse.json();
        assert.equal(bundle.schema_version, 1);
        assert.equal(bundle.items.length, 2);
        const importBundle = () => fetch(`${importedBase}/api/library/import`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: importedBase }, body: JSON.stringify(bundle) });
        const imported = await importBundle();
        assert.equal(imported.status, 200);
        assert.equal((await imported.json()).imported, 2);
        assert.equal((await (await fetch(`${importedBase}/api/library`)).json()).items.length, 2);
        assert.equal((await importBundle()).status, 409);
        const unknownKey = await fetch(`${importedBase}/api/library/import`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: importedBase },
            body: JSON.stringify({ ...bundle, path: '../outside' }) });
        assert.equal(unknownKey.status, 400);
        const generated = await (await post('/api/build', { draft, plan })).json();
        const agentText = await (await fetch(`${base}${generated.agent_palette_url}`)).text();
        const agentPalette = JSON.parse(agentText);
        assert.equal(agentPalette.saved_templates.length, 2);
        assert.equal(agentText.includes('資料查詢元件'), false);
        assert.equal(agentText.includes('資料查詢頁'), false);
        const item = await (await fetch(`${base}/api/library/component/${saved.id}`)).json();
        assert.equal(item.plan.items.length, 1);
        const mapping = { entities: { [draft.entities[0].code]: target.entities[0].code },
            fields: Object.fromEntries(draft.entities[0].fields.map((field, index) =>
                [field.code, target.entities[0].fields[index].code])) };
        const appliedResponse = await post('/api/library/apply',
            { kind: 'component', id: saved.id, draft: target, mapping });
        assert.equal(appliedResponse.status, 200);
        assert.equal((await appliedResponse.json()).plan.items[0].ref,
            generateCandidates(target)[0].registry_name);
        const bad = await post('/api/library/page',
            { title: '壞頁面', draft, plan: { ...plan, script: 'alert(1)' } });
        assert.equal(bad.status, 400);
        const crossOrigin = await fetch(`${base}/api/library/page`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: 'http://example.test' },
            body: JSON.stringify({ title: '跨站', draft, plan }) });
        assert.equal(crossOrigin.status, 403);
        const removed = await fetch(`${base}/api/library/page/${page.id}`,
            { method: 'DELETE', headers: { origin: base } });
        assert.equal(removed.status, 200);
        assert.equal((await fetch(`${base}/api/library/page/${page.id}`)).status, 404);
        assert.equal((await (await fetch(`${base}/api/library`)).json()).items.length, 1);
    } finally {
        server.close(); await once(server, 'close');
        importedServer.close(); await once(importedServer, 'close');
        for (const target of [root, importedRoot]) {
            const resolved = path.resolve(target), temp = path.resolve(os.tmpdir());
            if (!resolved.startsWith(`${temp}${path.sep}`) || !path.basename(resolved).startsWith('b4a-library-api'))
                throw new Error('Unexpected library test path');
            rmSync(resolved, { recursive: true, force: true });
        }
    }
});

test('a full 200-item library exported by the workbench can be imported back over HTTP', async () => {
    const roots = [0, 1].map(() => mkdtempSync(path.join(os.tmpdir(), 'b4a-library-api-bulk-')));
    const servers = roots.map(libraryRoot => createPresaleServer({ libraryRoot }));
    for (const server of servers) { server.listen(0, '127.0.0.1'); await once(server, 'listening'); }
    const [source, target] = servers.map(server => `http://127.0.0.1:${server.address().port}`);
    try {
        const draft = starterDraft('course');
        draft.confirmed = true;
        const candidates = generateCandidates(draft), bindings = generateBindings(draft);
        const items = candidates.map((candidate, index) => {
            const instance_id = `I_${String(index + 1).padStart(12, '0')}`;
            return { kind: 'candidate', ref: candidate.registry_name, span: 12, instance_id,
                wiring: defaultCandidateWiring(candidate, bindings[index], instance_id) };
        });
        const plan = { schema_version: 1, items };
        const library = new LocalTemplateLibrary(roots[0]);
        for (let index = 0; index < 200; index++)
            library.save({ kind: 'page', title: `課程頁面 ${index + 1}`, draft, plan });
        const exported = await fetch(`${source}/api/library/export`);
        assert.equal(exported.status, 200);
        const text = await exported.text();
        const bundle = JSON.parse(text);
        assert.equal(bundle.items.length, 200);
        let nodes = 0;
        JSON.stringify(bundle, (key, value) => { nodes++; return value; });
        assert.ok(nodes > 10_000, 'the bundle must exceed the default JSON node budget to prove the route override');
        const imported = await fetch(`${target}/api/library/import`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: target }, body: text });
        assert.equal(imported.status, 200, await imported.clone().text());
        assert.equal((await imported.json()).imported, 200);
        assert.equal((await (await fetch(`${target}/api/library`)).json()).items.length, 200);
        // 其他路由仍套用預設節點上限。
        const flood = await fetch(`${target}/api/build`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: target },
            body: JSON.stringify({ draft: { filler: Array.from({ length: 12_000 }, () => 0) } }) });
        assert.equal(flood.status, 400);
        assert.equal((await flood.json()).error, 'JSON structure limit exceeded');
    } finally {
        for (const server of servers) { server.close(); await once(server, 'close'); }
        for (const root of roots) {
            const resolved = path.resolve(root), temp = path.resolve(os.tmpdir());
            if (!resolved.startsWith(`${temp}${path.sep}`) || !path.basename(resolved).startsWith('b4a-library-api-bulk-'))
                throw new Error('Unexpected library test path');
            rmSync(resolved, { recursive: true, force: true });
        }
    }
});
