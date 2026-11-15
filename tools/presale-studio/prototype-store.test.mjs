import test from 'node:test';
import assert from 'node:assert/strict';
import { starterDraft, compileDraft, prototypeConfig } from './model.mjs';
import { PrototypeStore, testStorage } from './prototype-store.mjs';
import { validatePrototype } from './functional-validation.mjs';

function fixture() {
    const draft = starterDraft('course');
    draft.confirmed = true;
    const seed = compileDraft(draft).seed;
    const config = prototypeConfig(draft, 'b4a-presale-test');
    return { draft, seed, config, resource: config.resources[0] };
}

test('planned and generated pages can share persistent scoped CRUD', async () => {
    const { draft, seed, config, resource } = fixture();
    const storage = testStorage();
    const store = new PrototypeStore(config, seed, storage);
    const [admin, staff, viewer] = draft.roles;
    store.setRole(admin.code);
    assert.equal((await store.get(resource.api)).total, 3);
    store.setRole(staff.code);
    const staffRows = await store.get(resource.api);
    assert.deepEqual(staffRows.items.map(row => row[resource.fields[0].code]), ['CS101', 'EN305']);
    const hidden = seed[resource.api][1];
    const filter = `${resource.api}?${resource.fields[0].code}=${hidden[resource.fields[0].code]}`;
    assert.equal((await store.get(filter)).total, 0);
    await assert.rejects(store.get(`${resource.api}/${hidden.id}`), error => error.status === 404);
    const name = resource.fields[1].code;
    await store.put(`${resource.api}/1`, { [name]: '新課程名稱' });
    const reopened = new PrototypeStore(config, seed, storage);
    reopened.setRole(staff.code);
    assert.equal((await reopened.get(`${resource.api}/1`))[name], '新課程名稱');
    reopened.setRole(viewer.code);
    assert.equal((await reopened.get(resource.api)).total, 1);
    await assert.rejects(reopened.put(`${resource.api}/1`, { [name]: '不應成功' }), error => error.status === 404);
    reopened.setRole(admin.code);
    await assert.rejects(reopened.put(`${resource.api}/1`, { [name]: 123 }), error => error.status === 400);
    assert.equal((await reopened.get(`${resource.api}/1`))[name], '新課程名稱');
    reopened.resetData();
    const reset = new PrototypeStore(config, seed, storage);
    reset.setRole(admin.code);
    assert.equal((await reset.get(`${resource.api}/1`))[name], '程式設計導論');
});

test('generated functional validation reports checked cases', async () => {
    const { seed, config } = fixture();
    const report = await validatePrototype(config, seed);
    assert.equal(report.result, 'passed', JSON.stringify(report));
    assert.deepEqual(report.cases.map(item => item.status), ['passed', 'passed', 'passed', 'passed']);
});
