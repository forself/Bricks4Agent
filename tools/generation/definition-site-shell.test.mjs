// templates/definition-site 中不依賴 DOM 的模組（記憶體資料來源與網站模型）
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { MemoryApiError, MemoryStore, MAX_RECORDS_PER_COLLECTION } from '../../templates/definition-site/memory-store.js';
import {
    applyListColumns,
    buildSiteModel,
    MAX_LIST_COLUMNS,
    pageEndpoint,
    parseRoute,
    resourceEndpoint,
    routeHref
} from '../../templates/definition-site/site-model.js';
import { GOLDEN_EXAMPLE_PATH } from './paths.mjs';

const golden = JSON.parse(readFileSync(GOLDEN_EXAMPLE_PATH, 'utf8'));

test('memory store supports create, list, get, update and delete on one collection', async () => {
    const store = new MemoryStore({ collections: ['/api/contacts'] });
    const created = await store.post('/api/contacts', { fullName: 'Avery', id: 99 });
    assert.deepEqual(created, { id: 1, fullName: 'Avery' });
    await store.post('/api/contacts', { fullName: 'Blake' });

    const page = await store.get('/api/contacts?page=1&pageSize=1');
    assert.equal(page.total, 2);
    assert.deepEqual(page.items, [{ id: 1, fullName: 'Avery' }]);
    assert.equal((await store.get('/api/contacts?fullName=bla')).total, 1);

    const updated = await store.put(resourceEndpoint('/api/contacts', 1), { fullName: 'Avery Updated', id: 5 });
    assert.deepEqual(updated, { id: 1, fullName: 'Avery Updated' });
    assert.deepEqual(await store.get('/api/contacts/1'), { id: 1, fullName: 'Avery Updated' });

    await store.delete('/api/contacts/1');
    assert.equal(store.count('/api/contacts'), 1);
    await assert.rejects(store.get('/api/contacts/1'), (error) => error instanceof MemoryApiError && error.status === 404);
});

test('memory store returns copies and refuses unknown endpoints', async () => {
    const store = new MemoryStore({ collections: ['/api/items'] });
    const record = await store.post('/api/items', { tags: ['a'] });
    record.tags.push('mutated');
    assert.deepEqual((await store.get('/api/items/1')).tags, ['a']);
    await assert.rejects(store.get('/api/other'), (error) => error.status === 404);
    await assert.rejects(store.post('/api/items/1', {}), (error) => error.status === 405);
    await assert.rejects(store.put('/api/items', {}), (error) => error.status === 405);
    const polluted = await store.post('/api/items', JSON.parse('{"__proto__":{"x":1},"name":"n"}'));
    assert.equal(polluted.name, 'n');
    assert.equal({}.x, undefined);
});

test('memory store caps the records per collection', async () => {
    const store = new MemoryStore({ collections: ['/api/items'] });
    for (let i = 0; i < MAX_RECORDS_PER_COLLECTION; i += 1) await store.post('/api/items', { i });
    await assert.rejects(store.post('/api/items', {}), (error) => error.status === 413);
});

test('routes parse only page ids and safe record ids', () => {
    assert.deepEqual(parseRoute(''), { pageId: null, recordId: null });
    assert.deepEqual(parseRoute('#/contacts-list'), { pageId: 'contacts-list', recordId: null });
    assert.deepEqual(parseRoute('#/contact-form/12'), { pageId: 'contact-form', recordId: '12' });
    assert.equal(parseRoute('#/../etc'), null);
    assert.equal(parseRoute('#/a/b/c'), null);
    assert.equal(parseRoute('#/Contacts'), null);
    assert.equal(parseRoute('#/contact-form/%3Cscript%3E'), null);
    assert.equal(parseRoute('#/contact-form/%E0%A4%A'), null);
    assert.equal(routeHref('contact-form', 3), '#/contact-form/3');
    assert.equal(routeHref('contacts-list'), '#/contacts-list');
});

test('pages sharing an api base path are linked into one resource', () => {
    const definitions = Object.fromEntries(golden.definitions.pages.map(entry => [entry.id, entry.definition]));
    const model = buildSiteModel({ title: 't', pages: Object.keys(definitions) }, definitions);
    assert.equal(model.defaultPageId, 'contacts-list');
    const list = model.byId.get('contacts-list');
    assert.deepEqual(list.links, { list: 'contacts-list', detail: 'contact-detail', editForm: 'contact-form', createForm: 'contact-form' });
    assert.equal(model.byId.get('contact-form').endpoint, '/api/contacts');
    assert.equal(pageEndpoint('detail', { get: '/api/a', list: '/api/b' }), '/api/a');
    assert.equal(pageEndpoint('form', undefined), null);
    assert.throws(() => buildSiteModel({ pages: ['missing'] }, {}), /missing definition/);
    assert.throws(() => buildSiteModel({ pages: ['t'] }, { t: { type: 'tool' } }), /unsupported page type/);
});

test('a resource with two forms links edits to the form with api.update and creates to the form with api.create', () => {
    const field = (name) => ({ name, type: 'text', label: name });
    const definitions = {
        'signups-list': { name: 'SignupList', type: 'list', fields: [field('fullName'), field('status')], api: { list: '/api/signups', delete: '/api/signups' } },
        'signup-detail': { name: 'SignupDetail', type: 'detail', fields: [field('fullName'), field('status')], api: { get: '/api/signups' } },
        // 頁序第一個表單只能新增（公開填寫），第二個表單用來處理（可編輯）。
        'signup-public': { name: 'SignupPublic', type: 'form', fields: [field('fullName')], api: { create: '/api/signups' } },
        'signup-process': { name: 'SignupProcess', type: 'form', fields: [field('fullName'), field('status')], api: { get: '/api/signups', create: '/api/signups', update: '/api/signups' } }
    };
    const model = buildSiteModel({ title: 't', pages: Object.keys(definitions) }, definitions);
    for (const id of Object.keys(definitions)) {
        const links = model.byId.get(id).links;
        assert.equal(links.editForm, 'signup-process', `${id} edits through the form that has api.update`);
        assert.equal(links.createForm, 'signup-public', `${id} creates through the first form with api.create`);
        assert.equal(links.list, 'signups-list');
        assert.equal(links.detail, 'signup-detail');
    }

    // 沒有任何表單有 api.update：編輯沒有目標，新增仍連到第一個表單。
    const createOnly = { ...definitions, 'signup-process': { ...definitions['signup-process'], api: { create: '/api/signups' } } };
    const createOnlyModel = buildSiteModel({ pages: Object.keys(createOnly) }, createOnly);
    assert.equal(createOnlyModel.byId.get('signups-list').links.editForm, null);
    assert.equal(createOnlyModel.byId.get('signups-list').links.createForm, 'signup-public');
});

test('list columns keep only text-friendly fields, capped', () => {
    const fields = [
        { fieldType: 'text' }, { fieldType: 'password' }, { fieldType: 'color' }, { fieldType: 'select' },
        ...Array.from({ length: 10 }, () => ({ fieldType: 'number' }))
    ];
    const columns = applyListColumns(fields);
    assert.equal(columns.length, fields.length);
    assert.equal(columns[1].listOrder, 0);
    assert.equal(columns[2].listOrder, 0);
    assert.deepEqual(columns.filter(field => field.listOrder > 0).map(field => field.listOrder), Array.from({ length: MAX_LIST_COLUMNS }, (_, i) => i + 1));
    assert.ok(columns.every(field => field.isSearchable === false));
    assert.equal(fields[0].listOrder, undefined, 'input is not mutated');
});

test('boolean list columns are shown through yes/no option labels', () => {
    const [toggle, hiddenCheckbox] = applyListColumns(
        [{ fieldType: 'toggle', fieldName: 'active' }, ...Array.from({ length: MAX_LIST_COLUMNS }, () => ({ fieldType: 'text' })), { fieldType: 'checkbox' }],
        { yes: 'Y', no: 'N' }
    ).filter((field, index, all) => index === 0 || index === all.length - 1);
    assert.equal(toggle.fieldType, 'select');
    assert.deepEqual(toggle.optionsSource.items.slice(0, 2), [{ value: true, label: 'Y' }, { value: false, label: 'N' }]);
    assert.equal(hiddenCheckbox.listOrder, 0);
    assert.equal(hiddenCheckbox.fieldType, 'checkbox', 'columns beyond the cap keep their type');
});
