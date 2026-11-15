import test from 'node:test';
import assert from 'node:assert/strict';
import { starterDraft, compileDraft, prototypeConfig } from './model.mjs';
import { SqlitePrototypeBackend } from './sqlite-backend.mjs';

test('batch create validates all rows, commits atomically and is idempotent', () => {
    const draft = starterDraft('course');
    draft.confirmed = true;
    const compiled = compileDraft(draft);
    const config = prototypeConfig(draft, 'batch-test');
    const backend = new SqlitePrototypeBackend({ dbPath: ':memory:', config,
        mapping: compiled.mapping, seed: compiled.seed });
    try {
        const api = config.resources[0].api;
        const [admin, staff, viewer] = config.roles;
        const fields = config.resources[0].fields;
        const first = Object.fromEntries(fields.map(field => [field.code, compiled.seed[api][0][field.code]]));
        const second = { ...first, [fields[0].code]: 'BATCH2' };
        const body = { request_id: 'a'.repeat(32), items: [first, second] };
        assert.deepEqual(backend.batchCreate(api, { ...body, dry_run: true }, staff),
            { accepted: 2, dry_run: true });
        assert.equal(backend.get(api, admin).total, 3);
        const result = backend.batchCreate(api, body, staff);
        assert.equal(result.accepted, 2);
        assert.equal(result.replayed, false);
        assert.equal(backend.get(api, admin).total, 5);
        assert.deepEqual(backend.batchCreate(api, body, staff), { ...result, replayed: true });
        assert.equal(backend.get(api, admin).total, 5);
        assert.throws(() => backend.batchCreate(api, { ...body, items: [first] }, staff),
            error => error.status === 409);
        assert.throws(() => backend.batchCreate(api, { request_id: 'b'.repeat(32),
            items: [first, { ...second, [fields[1].code]: 123 }] }, staff), error => error.status === 400);
        assert.equal(backend.get(api, admin).total, 5);
        assert.throws(() => backend.batchCreate(api, { request_id: 'c'.repeat(32), items: [first] }, viewer),
            error => error.status === 403);
        assert.throws(() => backend.batchCreate(api, { request_id: 'd'.repeat(32),
            items: [{ ...first, scope_owner: 'PERSON_OTHER' }] }, staff), error => error.status === 400);
        backend.reset();
        assert.equal(backend.get(api, admin).total, 3);
        assert.equal(backend.batchCreate(api, body, staff).replayed, false);
        assert.equal(backend.get(api, admin).total, 5);
    } finally { backend.close(); }
});
