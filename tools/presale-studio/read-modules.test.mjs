import test from 'node:test';
import assert from 'node:assert/strict';
import { starterDraft, compileDraft, prototypeConfig } from './model.mjs';
import { SqlitePrototypeBackend } from './sqlite-backend.mjs';

test('search, lookup, summary and CSV share the same read scope', () => {
    const draft = starterDraft('course');
    draft.confirmed = true;
    const compiled = compileDraft(draft);
    const config = prototypeConfig(draft, 'read-module-test');
    const backend = new SqlitePrototypeBackend({ dbPath: ':memory:', config,
        mapping: compiled.mapping, seed: compiled.seed });
    try {
        const api = config.resources[0].api;
        const fields = config.resources[0].fields;
        const [admin, staff, viewer] = config.roles;
        const code = fields[0].code, title = fields[1].code;
        const unit = fields[2].code, credits = fields.find(field => field.label === '學分').code;
        assert.equal(backend.query(api, {}, admin).total, 3);
        assert.equal(backend.query(api, {}, staff).total, 2);
        assert.equal(backend.query(api, {}, viewer).total, 1);
        assert.equal(backend.query(api, { filters: [{ field: code, op: 'eq', value: 'MA201' }] }, staff).total, 0);
        assert.equal(backend.query(api, { filters: [{ field: title, op: 'contains', value: '程式' }] }, staff).total, 1);
        assert.deepEqual(backend.query(api, { sort: { field: code, direction: 'desc' } }, staff)
            .items.map(row => row[code]), ['EN305', 'CS101']);
        assert.equal(backend.lookup(api, { field: unit }, staff).items.reduce((count, item) => count + item.count, 0), 2);
        assert.equal(backend.summary(api, { metric: { op: 'count' } }, staff).items[0].value, 2);
        assert.equal(backend.summary(api, { metric: { op: 'sum', field: credits } }, staff).items[0].value,
            backend.get(api, staff).items.reduce((total, row) => total + row[credits], 0));
        assert.equal(backend.summary(api, { metric: { op: 'count' }, group_by: unit }, viewer)
            .items.reduce((count, item) => count + item.value, 0), 1);
        const csv = backend.export(api, { fields: [code, title] }, staff);
        assert.equal(csv.row_count, 2);
        assert.ok(!csv.csv.includes('MA201'));
        assert.throws(() => backend.query(api, { filters: [{ field: 'F_000000000000', op: 'eq', value: 'x' }] }, admin),
            error => error.status === 400);
        assert.throws(() => backend.summary(api, { metric: { op: 'sum', field: title } }, admin),
            error => error.status === 400);
        assert.throws(() => backend.export(api, { fields: [code, code] }, admin),
            error => error.status === 400);
        const row = Object.fromEntries(fields.map(field => [field.code, compiled.seed[api][0][field.code]]));
        row[code] = 'FORMULA';
        row[title] = '=HYPERLINK("https://invalid.example")';
        backend.post(api, row, staff);
        assert.ok(backend.export(api, { fields: [title] }, staff).csv.includes("'=HYPERLINK"));
    } finally { backend.close(); }
});
