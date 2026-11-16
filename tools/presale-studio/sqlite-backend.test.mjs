import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { once } from 'node:events';
import { starterDraft, compileDraft, prototypeConfig } from './model.mjs';
import { SqlitePrototypeBackend } from './sqlite-backend.mjs';
import { buildPresalePackage } from './server.mjs';

function fixture() {
    const draft = starterDraft('course');
    draft.confirmed = true;
    const compiled = compileDraft(draft);
    const config = prototypeConfig(draft, 'b4a-presale-sqlite-test');
    return { draft, config, mapping: compiled.mapping, seed: compiled.seed,
        resource: config.resources[0], roles: config.roles };
}

test('SQLite backend reflects allowlisted columns, persists CRUD and intersects role scope with filters', () => {
    const root = mkdtempSync(path.join(tmpdir(), 'b4a-sqlite-test-'));
    const dbPath = path.join(root, 'prototype.sqlite');
    const data = fixture();
    let backend;
    try {
        backend = new SqlitePrototypeBackend({ dbPath, ...data });
        const [admin, staff, viewer] = data.roles;
        const api = data.resource.api;
        const code = data.resource.fields[0].code;
        const title = data.resource.fields[1].code;
        const table = data.mapping[0].table_code;
        const reflected = backend.db.prepare(`PRAGMA table_info("${table}")`).all();
        assert.deepEqual(reflected.slice(4).map(column => column.name), data.mapping[0].fields.map(field => field.column_code));
        assert.equal(backend.get(api, admin).total, 3);
        assert.deepEqual(backend.get(api, staff).items.map(row => row[code]), ['CS101', 'EN305']);
        assert.equal(backend.get(`${api}?${code}=MA201`, staff).total, 0);
        assert.equal(backend.get(`${api}?${code}=CS101`, staff).total, 1);
        assert.throws(() => backend.get(`${api}/2`, staff), error => error.status === 404);
        assert.throws(() => backend.get(`${api}?DROP_TABLE=1`, admin), error => error.status === 400);
        assert.throws(() => backend.post(api, { [code]: 'TEST', [title]: 1 }, admin), error => error.status === 400);
        backend.put(`${api}/1`, { [title]: 'SQLite 保存' }, staff);
        assert.equal(backend.get(`${api}/1`, staff)[title], 'SQLite 保存');
        assert.throws(() => backend.put(`${api}/1`, { [title]: '不應成功' }, viewer), error => error.status === 404);
        backend.close(); backend = null;
        backend = new SqlitePrototypeBackend({ dbPath, ...data });
        assert.equal(backend.get(`${api}/1`, admin)[title], 'SQLite 保存');
        backend.reset();
        assert.equal(backend.get(`${api}/1`, admin)[title], '程式設計導論');
    } finally {
        backend?.close();
        rmSync(root, { recursive: true, force: true });
    }
});

test('multiple role scopes union within an action and update never grants read implicitly', () => {
    const data = fixture();
    const backend = new SqlitePrototypeBackend({ dbPath: ':memory:', ...data });
    try {
        const api = data.resource.api;
        const field = data.resource.fields[1].code;
        const subject = data.config.subject;
        const self = { code: 'R_111111111111', actions: ['read'], vertical: 'self', horizontal: 'all' };
        const descendants = { code: 'R_222222222222', actions: ['read'], vertical: 'descendants', horizontal: 'all' };
        const principal = { subject, roles: [self, descendants] };
        assert.equal(backend.get(api, { subject, roles: [self] }).total, 1);
        assert.equal(backend.get(api, { subject, roles: [descendants] }).total, 1);
        assert.equal(backend.get(api, principal).total, 2);
        assert.equal(backend.get(`${api}?${data.resource.fields[0].code}=MA201`, principal).total, 0);
        const updateOnly = { subject, roles: [{ code: 'R_333333333333', actions: ['update'],
            vertical: 'self', horizontal: 'all' }] };
        assert.deepEqual(backend.put(`${api}/1`, { [field]: '已修改' }, updateOnly), { ok: true });
        assert.throws(() => backend.get(`${api}/1`, updateOnly), error => error.status === 403 || error.status === 404);
        assert.equal(backend.get(`${api}/1`, principal)[field], '已修改');
    } finally { backend.close(); }
});

test('downloaded package starts its own native Node and SQLite service', async () => {
    const data = fixture();
    const result = await buildPresalePackage(data.draft);
    let server;
    try {
        const moduleUrl = pathToFileURL(path.join(result.outDir, 'backend', 'standalone-server.mjs')).href;
        const { createStandaloneServer } = await import(moduleUrl);
        server = createStandaloneServer(result.outDir);
        server.listen(0, '127.0.0.1');
        await once(server, 'listening');
        let base = `http://127.0.0.1:${server.address().port}`;
        assert.equal((await fetch(`${base}/`)).status, 200);
        const resource = data.resource.api;
        assert.equal((await fetch(`${base}/api/prototype${resource}`)).status, 401);
        const login = await fetch(`${base}/api/prototype/demo/session`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base },
            body: JSON.stringify({ role_code: data.roles[1].code }) });
        assert.equal(login.status, 200);
        const cookie = login.headers.get('set-cookie').split(';')[0];
        const list = await fetch(`${base}/api/prototype${resource}`, { headers: { cookie } });
        assert.equal(list.status, 200);
        assert.equal((await list.json()).total, 2);
        const moduleHeaders = { cookie, origin: base, 'content-type': 'application/json' };
        const query = await fetch(`${base}/api/prototype${resource}/query`, {
            method: 'POST', headers: moduleHeaders, body: '{}' });
        assert.equal(query.status, 200);
        assert.equal((await query.json()).total, 2);
        const summary = await fetch(`${base}/api/prototype${resource}/summary`, {
            method: 'POST', headers: moduleHeaders, body: JSON.stringify({ metric: { op: 'count' } }) });
        assert.equal(summary.status, 200);
        assert.equal((await summary.json()).items[0].value, 2);
        const exported = await fetch(`${base}/api/prototype${resource}/export`, {
            method: 'POST', headers: moduleHeaders,
            body: JSON.stringify({ fields: [data.resource.fields[0].code] }) });
        assert.equal(exported.status, 200);
        assert.match(exported.headers.get('content-type'), /^text\/csv/);
        assert.ok(!(await exported.text()).includes('MA201'));
        const batch = await fetch(`${base}/api/prototype${resource}/batch`, {
            method: 'POST', headers: moduleHeaders, body: JSON.stringify({ request_id: 'f'.repeat(32),
                items: [Object.fromEntries(data.resource.fields.map(field =>
                    [field.code, data.seed[resource][0][field.code]]))] }) });
        assert.equal(batch.status, 201);
        assert.equal((await batch.json()).accepted, 1);
        const hidden = await fetch(`${base}/api/prototype${resource}/2`, { headers: { cookie } });
        assert.equal(hidden.status, 404);
        const badOrigin = await fetch(`${base}/api/prototype${resource}`, { method: 'POST',
            headers: { cookie, 'content-type': 'application/json', origin: 'http://outside.invalid' }, body: '{}' });
        assert.equal(badOrigin.status, 403);

        server.close(); await once(server, 'close');
        server = createStandaloneServer(result.outDir);
        server.listen(0, '127.0.0.1'); await once(server, 'listening');
        base = `http://127.0.0.1:${server.address().port}`;
        const restartedLogin = await fetch(`${base}/api/prototype/demo/session`, { method: 'POST',
            headers: { 'content-type': 'application/json', origin: base },
            body: JSON.stringify({ role_code: data.roles[1].code }) });
        assert.equal(restartedLogin.status, 200);
        const restartedCookie = restartedLogin.headers.get('set-cookie').split(';')[0];
        const persisted = await fetch(`${base}/api/prototype${resource}/query`, {
            method: 'POST', headers: { cookie: restartedCookie, origin: base, 'content-type': 'application/json' },
            body: '{}' });
        assert.equal(persisted.status, 200);
        assert.equal((await persisted.json()).total, 3, 'the downloaded package should read its committed SQLite write after restart');
    } finally {
        if (server) { server.close(); await once(server, 'close'); }
        rmSync(result.temporaryRoot, { recursive: true, force: true });
    }
});
