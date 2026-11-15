import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { writeFileSync, rmSync } from 'node:fs';
import path from 'node:path';
import { starterDraft, prototypeConfig } from './model.mjs';
import { buildPresalePackage } from './server.mjs';
import { createStandaloneServer } from './standalone-server.mjs';
import { validateFormalPolicy } from './broker-principal.mjs';

const bearerA = 'broker.signed.agent_token_a_0123456789';
const bearerB = 'broker.signed.agent_token_b_0123456789';
const brokerReply = (principal, token) => new Response(JSON.stringify({ success: true, data: {
    principal_id: principal, task_id: 'task_presale', role_id: 'role_presale', session_id: token
} }), { status: 200, headers: { 'content-type': 'application/json' } });

test('formal policy rejects missing, ambiguous and unapproved grants', () => {
    assert.throws(() => validateFormalPolicy({ schema_version: 1, principals: [] }));
    const binding = { principal_id: 'prn_a', task_id: 'task_a', role_id: 'role_a',
        subject: { owner: 'PERSON_A', unit: 'UNIT_A', function: 'FUNCTION_A', descendants: [] },
        roles: [{ code: 'R_123456789ABC', label: '授權角色', actions: ['read'],
            vertical: 'self', horizontal: 'same_unit' }] };
    assert.throws(() => validateFormalPolicy({ schema_version: 1, principals: [binding, binding] }));
    assert.throws(() => validateFormalPolicy({ schema_version: 1, principals: [
        { ...binding, subject: { ...binding.subject, extra: 'untrusted' } }
    ] }));
});

test('formal Node service rechecks Broker and intersects per-principal scope', async () => {
    const draft = starterDraft('course');
    draft.confirmed = true;
    const config = prototypeConfig(draft, 'formal-test');
    const resource = config.resources[0];
    const role = config.roles[1];
    const result = await buildPresalePackage(draft);
    const policyPath = path.join(result.temporaryRoot, 'approved-policy.json');
    const policy = { schema_version: 1, principals: ['a', 'b'].map(letter => ({
        principal_id: `prn_${letter}`, task_id: 'task_presale', role_id: 'role_presale',
        subject: { owner: `PERSON_${letter.toUpperCase()}`, unit: 'UNIT_A',
            function: 'FUNCTION_A', descendants: [] }, roles: [role]
    })) };
    writeFileSync(policyPath, JSON.stringify(policy));
    let revoked = false, checks = 0, server;
    const fetcher = async (_url, options) => {
        checks++;
        const token = options.headers.Authorization.slice('Bearer '.length);
        if (revoked || ![bearerA, bearerB].includes(token)) return new Response('{}', { status: 401 });
        return brokerReply(token === bearerA ? 'prn_a' : 'prn_b', token);
    };
    try {
        assert.throws(() => createStandaloneServer(result.outDir, { mode: 'formal' }));
        server = createStandaloneServer(result.outDir, { mode: 'formal', policyPath,
            verifyUrl: 'http://127.0.0.1:5099/api/v1/sessions/verify', fetcher });
        server.listen(0, '127.0.0.1');
        await once(server, 'listening');
        const base = `http://127.0.0.1:${server.address().port}`;
        const endpoint = `${base}/api/prototype`;
        assert.equal((await (await fetch(`${base}/site.json`)).json()).prototype_backend, 'sqlite-formal');
        assert.deepEqual((await (await fetch(`${base}/prototype-config.json`)).json()).roles, []);
        assert.deepEqual(await (await fetch(`${base}/demo-seed.json`)).json(), {});
        assert.equal((await fetch(`${endpoint}${resource.api}`)).status, 401);
        assert.equal((await fetch(`${endpoint}/demo/session`, { method: 'POST',
            headers: { origin: base, 'content-type': 'application/json' },
            body: JSON.stringify({ role_code: role.code }) })).status, 404);
        assert.equal((await fetch(`${endpoint}/reset`, { method: 'POST',
            headers: { origin: base, 'content-type': 'application/json' }, body: '{}' })).status, 404);
        const login = async token => fetch(`${endpoint}/auth/exchange`, { method: 'POST',
            headers: { origin: base, 'content-type': 'application/json' },
            body: JSON.stringify({ scoped_token: token }) });
        assert.equal((await login('invalid_token_01234567890')).status, 401);
        const loginA = await login(bearerA), loginB = await login(bearerB);
        assert.equal(loginA.status, 200);
        assert.equal(loginB.status, 200);
        const cookieA = loginA.headers.get('set-cookie').split(';')[0];
        const cookieB = loginB.headers.get('set-cookie').split(';')[0];
        assert.equal((await (await fetch(`${endpoint}${resource.api}`, { headers: { cookie: cookieA } })).json()).total, 0);
        const sample = result.compiled.seed[resource.api][0];
        const body = Object.fromEntries(resource.fields.map(field => [field.code, sample[field.code]]));
        const create = await fetch(`${endpoint}${resource.api}`, { method: 'POST',
            headers: { origin: base, cookie: cookieA, 'content-type': 'application/json' },
            body: JSON.stringify(body) });
        assert.equal(create.status, 201);
        const row = await create.json();
        assert.equal((await (await fetch(`${endpoint}${resource.api}`, { headers: { cookie: cookieA } })).json()).total, 1);
        assert.equal((await (await fetch(`${endpoint}${resource.api}`, { headers: { cookie: cookieB } })).json()).total, 0);
        assert.equal((await fetch(`${endpoint}${resource.api}/${row.id}`, { headers: { cookie: cookieB } })).status, 404);
        const modulePost = (suffix, cookie, payload) => fetch(`${endpoint}${resource.api}/${suffix}`, {
            method: 'POST', headers: { origin: base, cookie, 'content-type': 'application/json' },
            body: JSON.stringify(payload)
        });
        assert.equal((await (await modulePost('query', cookieA, {})).json()).total, 1);
        assert.equal((await (await modulePost('query', cookieB, {})).json()).total, 0);
        assert.equal((await (await modulePost('summary', cookieB, { metric: { op: 'count' } })).json()).items[0].value, 0);
        assert.equal((await (await modulePost('export', cookieB, { fields: [resource.fields[0].code] })).text()).includes(sample[resource.fields[0].code]), false);
        const batch = await modulePost('batch', cookieA, { request_id: 'f'.repeat(32), items: [body] });
        assert.equal(batch.status, 201);
        assert.equal((await (await modulePost('query', cookieA, {})).json()).total, 2);
        assert.equal((await (await modulePost('query', cookieB, {})).json()).total, 0);
        revoked = true;
        assert.equal((await fetch(`${endpoint}${resource.api}`, { headers: { cookie: cookieA } })).status, 401);
        assert.ok(checks >= 7, 'Broker should validate every authenticated operation');
    } finally {
        if (server) { server.close(); await once(server, 'close'); }
        rmSync(result.temporaryRoot, { recursive: true, force: true });
    }
});
