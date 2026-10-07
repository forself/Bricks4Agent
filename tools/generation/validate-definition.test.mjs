import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { GOLDEN_EXAMPLE_PATH, VALIDATOR_VERSION } from './paths.mjs';
import { LIMITS, validateRequest } from './validate-definition.mjs';

const goldenText = readFileSync(GOLDEN_EXAMPLE_PATH, 'utf8');
const golden = () => JSON.parse(goldenText);
const formFields = template => template.definitions.pages[2].definition.fields;

function errorAt(result, code) {
    return result.errors.find(error => error.code === code);
}

function assertRejected(result, code, pathFragment) {
    assert.equal(result.ok, false, `expected ${code}`);
    const error = errorAt(result, code);
    assert.ok(error, `expected ${code}, got ${JSON.stringify(result.errors)}`);
    if (pathFragment !== undefined) assert.ok(error.path.includes(pathFragment), `${code} path ${error.path} should include ${pathFragment}`);
    for (const entry of result.errors) {
        assert.deepEqual(Object.keys(entry).sort(), ['code', 'hint', 'message', 'path']);
    }
    return error;
}

test('the golden example passes every layer', async () => {
    const result = await validateRequest({ template: golden() });
    assert.equal(result.ok, true, JSON.stringify(result.errors));
    assert.deepEqual(result.errors, []);
    assert.deepEqual(result.pages, [
        { id: 'contacts-list', type: 'list', field_count: 5 },
        { id: 'contact-detail', type: 'detail', field_count: 7 },
        { id: 'contact-form', type: 'form', field_count: 7 }
    ]);
    assert.equal(result.validator_version, VALIDATOR_VERSION);
    assert.match(result.validation_digest, /^[0-9a-f]{64}$/);
});

test('validation_digest ignores key order but follows content and page selection', async () => {
    const a = await validateRequest({ template: golden() });
    const reordered = golden();
    const page = reordered.definitions.pages[0];
    reordered.definitions.pages[0] = { definition: page.definition, id: page.id };
    const b = await validateRequest({ template: reordered });
    assert.equal(a.validation_digest, b.validation_digest);

    const changed = golden();
    formFields(changed)[0].label = '全名';
    const c = await validateRequest({ template: changed });
    assert.notEqual(a.validation_digest, c.validation_digest);

    const subset = await validateRequest({ template: golden(), page_ids: ['contact-form'] });
    assert.notEqual(a.validation_digest, subset.validation_digest);
    assert.deepEqual(subset.pages.map(entry => entry.id), ['contact-form']);
});

test('layer 1 rejects oversized templates', async () => {
    const template = golden();
    template.definitions.pages[0].definition.description = 'x'.repeat(LIMITS.maxTemplateBytes);
    const result = await validateRequest({ template });
    assertRejected(result, 'TEMPLATE_TOO_LARGE');
});

test('layer 1 rejects __proto__ anywhere, as produced by JSON.parse', async () => {
    const template = JSON.parse(goldenText.replace('"name": "fullName", "type": "text", "label": "姓名", "required": true', '"name": "fullName", "type": "text", "label": "姓名", "required": true, "__proto__": { "polluted": true }'));
    assert.ok(Object.prototype.hasOwnProperty.call(formFields(template)[0], '__proto__'));
    const result = await validateRequest({ template });
    assertRejected(result, 'DANGEROUS_KEY', 'definitions.pages[2].definition.fields[0].__proto__');
    assert.equal({}.polluted, undefined);
});

test('layer 1 caps nesting depth and page count', async () => {
    const deep = golden();
    let node = {};
    deep.meta.description = node;
    for (let i = 0; i < LIMITS.maxDepth + 4; i += 1) {
        node.next = {};
        node = node.next;
    }
    assertRejected(await validateRequest({ template: deep }), 'TEMPLATE_TOO_DEEP');

    const many = golden();
    const base = many.definitions.pages[2];
    many.definitions.pages = Array.from({ length: LIMITS.maxPages + 1 }, (_, index) => ({ id: `form-${index}`, definition: base.definition }));
    assertRejected(await validateRequest({ template: many }), 'TOO_MANY_PAGES');
});

test('layer 2 rejects unknown keys at every level', async () => {
    const template = golden();
    template.extra = true;
    template.definitions.pages[0].definition.behaviors = { onInit: 'load' };
    formFields(template)[0].placeholderText = 'x';
    const result = await validateRequest({ template });
    assertRejected(result, 'UNKNOWN_KEY');
    assert.ok(result.errors.some(error => error.code === 'UNKNOWN_KEY' && error.path === 'extra'));
    assert.ok(result.errors.some(error => error.code === 'UNKNOWN_KEY' && error.path === 'definitions.pages[0].definition.behaviors'));
    assert.ok(result.errors.some(error => error.code === 'UNKNOWN_KEY' && error.path === 'definitions.pages[2].definition.fields[0].placeholderText'));
});

test('layer 2 rejects page.entity style definitions so file names never come from them', async () => {
    const template = golden();
    template.definitions.pages[0].definition.page = { entity: '../escaped', view: 'list' };
    template.definitions.pages[1].definition.entity = '../../outside';
    const result = await validateRequest({ template });
    assertRejected(result, 'PAGE_ENTITY_NOT_ALLOWED', 'definitions.pages[0].definition.page');
    assert.ok(result.errors.some(error => error.code === 'PAGE_ENTITY_NOT_ALLOWED' && error.path === 'definitions.pages[1].definition.entity'));
});

test('layer 2 rejects markup, wrong types and missing keys', async () => {
    const template = golden();
    formFields(template)[0].label = '<b>姓名</b>';
    formFields(template)[1].required = 'yes';
    delete formFields(template)[2].label;
    const result = await validateRequest({ template });
    assertRejected(result, 'MARKUP_NOT_ALLOWED', 'fields[0].label');
    assertRejected(result, 'TYPE_MISMATCH', 'fields[1].required');
    assertRejected(result, 'MISSING_KEY', 'fields[2].label');
});

test('layer 2 rejects non-empty apps', async () => {
    const template = golden();
    template.definitions.apps = [{ id: 'app', app: {} }];
    assertRejected(await validateRequest({ template }), 'APPS_NOT_SUPPORTED', 'definitions.apps');
    const empty = golden();
    empty.definitions.apps = [];
    assert.equal((await validateRequest({ template: empty })).ok, true);
});

test('layer 3 rejects an unsupported template version', async () => {
    const template = golden();
    template.version = '0.2.0';
    const result = await validateRequest({ template });
    assertRejected(result, 'VALUE_INVALID', 'version');
    assert.ok(!result.errors.some(error => error.code === 'STRUCTURE_INVALID'), 'the generic layer-3 message is shadowed by the specific error');
});

test('generic messages from the reused validators are not reported twice', async () => {
    const template = golden();
    formFields(template).push({ name: 'level', type: 'slider', label: '程度' });
    const result = await validateRequest({ template });
    assert.deepEqual(result.errors.map(error => error.code), ['FIELD_TYPE_UNSUPPORTED']);
});

test('layer 5 rejects field names that are not JavaScript identifiers (a-b) and accepts CJK names', async () => {
    const template = golden();
    formFields(template).push({ name: 'a-b', type: 'text', label: '連字號' });
    formFields(template).push({ name: '備註二', type: 'text', label: '中文名稱' });
    const result = await validateRequest({ template });
    const error = assertRejected(result, 'IDENTIFIER_INVALID', 'definitions.pages[2].definition.fields[7].name');
    assert.match(error.message, /IdentifierName/);
    assert.equal(result.errors.filter(entry => entry.code === 'IDENTIFIER_INVALID').length, 1);
});

test('layer 5 still reports identifier errors when another layer also fails on the same page', async () => {
    const template = golden();
    formFields(template).push({ name: 'a-b', type: 'slider', label: '滑桿' });
    const result = await validateRequest({ template });
    assertRejected(result, 'IDENTIFIER_INVALID', 'fields[7].name');
    assertRejected(result, 'FIELD_TYPE_UNSUPPORTED', 'fields[7].type');
});

test('layer 6 rejects field types outside the whitelist (slider, tel) and runtime-blocked types', async () => {
    for (const type of ['slider', 'tel', 'rating', 'rocDate', 'weather', 'richtext', 'address']) {
        const template = golden();
        formFields(template).push({ name: 'extraField', type, label: '額外' });
        const error = assertRejected(await validateRequest({ template }), 'FIELD_TYPE_UNSUPPORTED', 'definitions.pages[2].definition.fields[7].type');
        assert.match(error.hint, /instead/);
    }
});

test('layer 6 rejects tool and dashboard pages and the components field', async () => {
    const tool = golden();
    tool.definitions.pages.push({ id: 'tools', definition: { name: 'ToolsPage', type: 'tool', root: { type: 'group', id: 'root', children: [] } } });
    assertRejected(await validateRequest({ template: tool }), 'PAGE_TYPE_UNSUPPORTED', 'definitions.pages[3].definition.type');

    const dashboard = golden();
    dashboard.definitions.pages[1].definition.type = 'dashboard';
    assertRejected(await validateRequest({ template: dashboard }), 'PAGE_TYPE_UNSUPPORTED', 'definitions.pages[1].definition.type');

    const components = golden();
    components.definitions.pages[0].definition.components = ['DataTable'];
    assertRejected(await validateRequest({ template: components }), 'COMPONENTS_NOT_ALLOWED', 'definitions.pages[0].definition.components');
});

test('layer 6 checks explicit components against the catalog', async () => {
    const unknown = golden();
    formFields(unknown)[0].component = 'NoSuchComponent';
    assertRejected(await validateRequest({ template: unknown }), 'COMPONENT_UNKNOWN', 'fields[0].component');

    const notUsable = golden();
    formFields(notUsable)[0].component = 'DataTable';
    assertRejected(await validateRequest({ template: notUsable }), 'COMPONENT_NOT_USABLE', 'fields[0].component');

    const mismatch = golden();
    formFields(mismatch)[0].component = 'NumberInput';
    assertRejected(await validateRequest({ template: mismatch }), 'COMPONENT_TYPE_MISMATCH', 'fields[0].component');

    const matching = golden();
    formFields(matching)[0].component = 'TextInput';
    assert.equal((await validateRequest({ template: matching })).ok, true);
});

test('layer 6 requires options exactly for choice fields and checks validation keys per type', async () => {
    const missing = golden();
    delete formFields(missing)[3].options;
    assertRejected(await validateRequest({ template: missing }), 'OPTIONS_REQUIRED', 'fields[3].options');

    const extra = golden();
    formFields(extra)[0].options = [{ value: 'a', label: 'A' }];
    assertRejected(await validateRequest({ template: extra }), 'OPTIONS_NOT_ALLOWED', 'fields[0].options');

    const validation = golden();
    formFields(validation)[4].validation = { maxLength: 10 };
    assertRejected(await validateRequest({ template: validation }), 'VALIDATION_KEY_UNSUPPORTED', 'fields[4].validation.maxLength');
});

test('layer 7 enforces base api paths, required endpoints and reserved names', async () => {
    const withId = golden();
    withId.definitions.pages[1].definition.api.get = '/api/contacts/{id}';
    assertRejected(await validateRequest({ template: withId }), 'API_PATH_INVALID', 'definitions.pages[1].definition.api.get');

    const traversal = golden();
    traversal.definitions.pages[0].definition.api.list = '/api/../secrets';
    assertRejected(await validateRequest({ template: traversal }), 'API_PATH_INVALID', 'api.list');

    const absent = golden();
    delete absent.definitions.pages[0].definition.api;
    assertRejected(await validateRequest({ template: absent }), 'API_REQUIRED', 'definitions.pages[0].definition.api.list');

    const reservedId = golden();
    reservedId.definitions.pages[0].id = 'con';
    assertRejected(await validateRequest({ template: reservedId }), 'PAGE_ID_RESERVED', 'definitions.pages[0].id');

    const reservedField = golden();
    formFields(reservedField)[0].name = 'id';
    assertRejected(await validateRequest({ template: reservedField }), 'FIELD_NAME_RESERVED', 'fields[0].name');

    const duplicate = golden();
    formFields(duplicate)[1].name = 'fullName';
    assertRejected(await validateRequest({ template: duplicate }), 'FIELD_NAME_DUPLICATE', 'fields[1].name');

    const badId = golden();
    badId.definitions.pages[0].id = '../list';
    assertRejected(await validateRequest({ template: badId }), 'VALUE_INVALID', 'definitions.pages[0].id');
});

test('layer 7 warns about mismatched api paths and create-only forms', async () => {
    const template = golden();
    template.definitions.pages[2].definition.api = { create: '/api/contacts', get: '/api/people' };
    const result = await validateRequest({ template });
    assert.equal(result.ok, true);
    assert.ok(result.warnings.some(warning => warning.code === 'API_BASE_MISMATCH'));
    assert.ok(result.warnings.some(warning => warning.code === 'FORM_WITHOUT_UPDATE'));
});

test('the request envelope is strict about keys, page_ids and title', async () => {
    assertRejected(await validateRequest({ template: golden(), out_dir: 'x' }, 'validate'), 'INPUT_UNKNOWN_KEY', 'out_dir');
    assertRejected(await validateRequest({}), 'TEMPLATE_REQUIRED', 'template');
    assertRejected(await validateRequest([]), 'INPUT_INVALID');
    assertRejected(await validateRequest({ template: golden(), page_ids: ['missing-page'] }), 'PAGE_ID_NOT_FOUND', 'page_ids');
    assertRejected(await validateRequest({ template: golden(), page_ids: ['contact-form', 'contact-form'] }), 'PAGE_IDS_DUPLICATE');
    assertRejected(await validateRequest({ template: golden(), page_ids: [] }), 'PAGE_IDS_INVALID');
    assertRejected(await validateRequest({ template: golden(), title: 'a\u0007b' }), 'TITLE_INVALID');
    assert.equal((await validateRequest({ template: golden(), page_ids: null, title: '聯絡人' })).ok, true);
});
