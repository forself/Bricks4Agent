import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { GOLDEN_EXAMPLE_PATH, VALIDATOR_VERSION } from './paths.mjs';
import { LIMITS, MAX_REPORTED_ISSUES, validateRequest } from './validate-definition.mjs';

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
    for (const entry of result.errors) assertIssueShape(entry);
    return error;
}

// 每筆錯誤是 {code, path, message, hint}；合併的項目另有 paths（前幾個路徑，第一個等於 path）
function assertIssueShape(entry) {
    const keys = Object.keys(entry).sort();
    if (keys.includes('paths')) {
        assert.deepEqual(keys, ['code', 'hint', 'message', 'path', 'paths']);
        assert.ok(Array.isArray(entry.paths) && entry.paths.length >= 1 && entry.paths.length <= 5);
        assert.equal(entry.paths[0], entry.path);
    } else {
        assert.deepEqual(keys, ['code', 'hint', 'message', 'path']);
    }
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

test('datetime, file, list and chained are closed in this slice and point to open substitutes', async () => {
    for (const [type, substitute] of [['datetime', 'date'], ['file', 'text'], ['list', 'textarea'], ['chained', 'select']]) {
        const template = golden();
        formFields(template)[4].type = type;
        const error = assertRejected(await validateRequest({ template }), 'FIELD_TYPE_UNSUPPORTED', 'definitions.pages[2].definition.fields[4].type');
        assert.match(error.hint, new RegExp(`Use "${substitute}" instead`));
    }
});

test('programming type names are rejected with the matching field type in the hint', async () => {
    for (const [type, substitute] of [['string', 'text'], ['boolean', 'checkbox'], ['integer', 'number']]) {
        const template = golden();
        formFields(template)[0].type = type;
        const error = assertRejected(await validateRequest({ template }), 'FIELD_TYPE_UNSUPPORTED', 'fields[0].type');
        assert.match(error.hint, new RegExp(`Use "${substitute}" instead`));
    }
});

// 12 頁 × 60 欄，每個欄位多兩個未知鍵：1,440 筆相同性質的錯誤。
function systematicErrorTemplate(fieldFor) {
    const pages = Array.from({ length: LIMITS.maxPages }, (_, pageIndex) => ({
        id: `page-${pageIndex}`,
        definition: {
            name: `Item${pageIndex}FormPage`,
            type: 'form',
            fields: Array.from({ length: LIMITS.maxFieldsPerPage }, (_, fieldIndex) => fieldFor(pageIndex, fieldIndex)),
            api: { get: '/api/items', create: '/api/items', update: '/api/items' }
        }
    }));
    return { kind: 'definition-template', version: '0.1.0', definitions: { pages } };
}

test('a systematic error is reported once with its count, inside the response budget', async () => {
    const template = systematicErrorTemplate((pageIndex, fieldIndex) => ({
        name: `field${fieldIndex}`,
        type: 'text',
        label: `欄位 ${fieldIndex}`,
        placeholder: 'x',
        helpText: 'y'
    }));
    const result = await validateRequest({ template });

    assert.equal(result.ok, false);
    assert.ok(result.total_errors >= LIMITS.maxPages * LIMITS.maxFieldsPerPage * 2, `total_errors ${result.total_errors}`);
    assert.equal(result.truncated, false);
    const unknown = result.errors.filter(entry => entry.code === 'UNKNOWN_KEY');
    assert.equal(unknown.length, 1, JSON.stringify(result.errors));
    assert.equal(unknown[0].path, 'definitions.pages[0].definition.fields[0].placeholder');
    assert.match(unknown[0].message, /"placeholder", "helpText"/);
    assert.match(unknown[0].message, /occurs at 1440 paths/);
    assert.equal(result.errors.filter(entry => /Allowed keys:/.test(entry.hint)).length, 1, 'the allowed key list appears once');
    assert.deepEqual(unknown[0].paths.slice(0, 2), [
        'definitions.pages[0].definition.fields[0].placeholder',
        'definitions.pages[0].definition.fields[0].helpText'
    ]);
    assert.equal(unknown[0].paths.length, 5);
    for (const entry of result.errors) assertIssueShape(entry);
    assert.ok(Buffer.byteLength(JSON.stringify(result)) < 16 * 1024, `response is ${Buffer.byteLength(JSON.stringify(result))} bytes`);
});

test('many distinct errors are capped and flagged as truncated, keeping every error code', async () => {
    // 每個欄位一種不同的未知型別（訊息各不相同，不會合併）；最後一頁另有一個保留頁 id，出現在所有型別錯誤之後
    const template = systematicErrorTemplate((pageIndex, fieldIndex) => ({
        name: `field${fieldIndex}`,
        type: `kind${pageIndex}x${fieldIndex}`,
        label: `欄位 ${fieldIndex}`
    }));
    template.definitions.pages[LIMITS.maxPages - 1].id = 'nul';
    const result = await validateRequest({ template });

    assert.equal(result.ok, false);
    assert.equal(result.errors.length, MAX_REPORTED_ISSUES);
    assert.equal(result.truncated, true);
    assert.ok(result.total_errors > MAX_REPORTED_ISSUES);
    assert.ok(result.errors.some(entry => entry.code === 'PAGE_ID_RESERVED'), 'a code that appears late still keeps one entry');
    assert.equal(result.errors.filter(entry => entry.code === 'FIELD_TYPE_UNSUPPORTED').length, MAX_REPORTED_ISSUES - 1);
    assert.ok(Buffer.byteLength(JSON.stringify(result)) < 64 * 1024, `response is ${Buffer.byteLength(JSON.stringify(result))} bytes`);
});

test('a merged error lists the paths where it occurs', async () => {
    const template = golden();
    delete template.definitions.pages[0].definition.fields[0].label;
    delete formFields(template)[1].label;
    const result = await validateRequest({ template });
    const missing = result.errors.filter(entry => entry.code === 'MISSING_KEY');
    assert.equal(missing.length, 1);
    assert.deepEqual(missing[0].paths, [
        'definitions.pages[0].definition.fields[0].label',
        'definitions.pages[2].definition.fields[1].label'
    ]);
    assert.match(missing[0].message, /occurs at 2 paths/);
});

test('fieldName/fieldType style fields are reported without a bogus identifier error', async () => {
    const template = golden();
    formFields(template)[0] = { fieldName: 'fullName', fieldType: 'text', label: '姓名' };
    const result = await validateRequest({ template });
    assert.equal(result.ok, false);
    assert.ok(result.errors.some(entry => entry.code === 'UNKNOWN_KEY'));
    assert.ok(result.errors.some(entry => entry.code === 'MISSING_KEY'));
    assert.ok(!result.errors.some(entry => entry.code === 'IDENTIFIER_INVALID'), JSON.stringify(result.errors));
});

test('identifier errors on many fields merge into one entry with their paths', async () => {
    const template = golden();
    formFields(template).push({ name: 'a-b', type: 'text', label: '一' }, { name: 'c d', type: 'text', label: '二' });
    const result = await validateRequest({ template });
    const identifiers = result.errors.filter(entry => entry.code === 'IDENTIFIER_INVALID');
    assert.equal(identifiers.length, 1);
    assert.deepEqual(identifiers[0].paths, [
        'definitions.pages[2].definition.fields[7].name',
        'definitions.pages[2].definition.fields[8].name'
    ]);
});

test('unknown validation keys are reported once as UNKNOWN_KEY, not again per key', async () => {
    const template = golden();
    formFields(template)[0].validation = { maxLength: 50, required: true, minLength: 2, pattern: '^a' };
    const result = await validateRequest({ template });
    assert.equal(result.ok, false);
    assert.deepEqual(result.errors.map(entry => entry.code), ['UNKNOWN_KEY'], JSON.stringify(result.errors));
    assert.match(result.errors[0].message, /"required", "minLength", "pattern"/);
});

test('a passing result carries no truncation fields', async () => {
    const result = await validateRequest({ template: golden() });
    assert.equal(result.ok, true);
    assert.equal('total_errors' in result, false);
    assert.equal('truncated' in result, false);
});

// 第 1 層的鍵長上限：一個超長的鍵在任何位置都立即判為無效，不論其下有多少其他錯誤；
// 執行時間與回應大小都有上限。
test('layer 1 rejects an overlong key at once, however many other errors it carries', async () => {
    const longKey = 'k'.repeat(LIMITS.maxKeyLength * 200);
    const items = Array.from({ length: 6000 }, () => '{"__proto__":0}').join(',');
    const text = `{"kind":"definition-template","version":"0.1.0","definitions":{"pages":[]},"${longKey}":[${items}]}`;
    assert.ok(Buffer.byteLength(text) < LIMITS.maxTemplateBytes);
    const started = process.hrtime.bigint();
    const result = await validateRequest({ template: JSON.parse(text) });
    const elapsedMs = Number(process.hrtime.bigint() - started) / 1e6;

    assert.equal(result.ok, false);
    assert.deepEqual(result.errors.map(entry => entry.code), ['KEY_TOO_LONG']);
    assert.equal(result.errors[0].path, '');
    assert.ok(!result.errors[0].message.includes(longKey.slice(0, 200)), 'the key itself is not echoed');
    assert.ok(Buffer.byteLength(JSON.stringify(result)) < 4 * 1024);
    assert.ok(elapsedMs < 2000, `validation took ${elapsedMs} ms`);
});

test('layer 1 accepts keys up to the limit and reports the path of an overlong nested key', async () => {
    const template = golden();
    formFields(template)[0]['x'.repeat(LIMITS.maxKeyLength + 1)] = 1;
    const result = await validateRequest({ template });
    assert.deepEqual(result.errors.map(entry => entry.code), ['KEY_TOO_LONG']);
    assert.equal(result.errors[0].path, 'definitions.pages[2].definition.fields[0]');

    const atLimit = golden();
    formFields(atLimit)[0]['x'.repeat(LIMITS.maxKeyLength)] = 1;
    const unknown = await validateRequest({ template: atLimit });
    assert.deepEqual(unknown.errors.map(entry => entry.code), ['UNKNOWN_KEY'], 'a key at the limit reaches the key whitelist');
});

test('layer 1 stops collecting at its issue limit and marks the result as truncated', async () => {
    const items = Array.from({ length: LIMITS.maxEnvelopeIssues * 3 }, () => '{"__proto__":0}').join(',');
    const template = JSON.parse(`{"kind":"definition-template","version":"0.1.0","definitions":{"pages":[]},"extra":[${items}]}`);
    const started = process.hrtime.bigint();
    const result = await validateRequest({ template });
    const elapsedMs = Number(process.hrtime.bigint() - started) / 1e6;

    assert.equal(result.ok, false);
    assert.equal(result.truncated, true);
    assert.equal(result.total_errors, LIMITS.maxEnvelopeIssues);
    const dangerous = result.errors.filter(entry => entry.code === 'DANGEROUS_KEY');
    assert.equal(dangerous.length, 1, 'identical errors still merge into one entry');
    assert.match(dangerous[0].message, new RegExp(`occurs at ${LIMITS.maxEnvelopeIssues} paths`));
    assert.ok(elapsedMs < 2000, `validation took ${elapsedMs} ms`);
});

test('defaults must have the form each field type handles at runtime', async () => {
    const reject = [
        [{ type: 'checkbox', default: 'yes' }, 'DEFAULT_INVALID'],
        [{ type: 'toggle', default: 1 }, 'DEFAULT_INVALID'],
        [{ type: 'date', default: 'tomorrow' }, 'DEFAULT_INVALID'],
        [{ type: 'date', default: '2026-02-30' }, 'DEFAULT_INVALID'],
        [{ type: 'time', default: '9:30' }, 'DEFAULT_INVALID'],
        [{ type: 'number', default: 'ten' }, 'DEFAULT_INVALID'],
        [{ type: 'number', default: 200, validation: { max: 100 } }, 'DEFAULT_INVALID'],
        [{ type: 'text', default: 5 }, 'DEFAULT_INVALID'],
        [{ type: 'text', default: 'abcdef', validation: { maxLength: 3 } }, 'DEFAULT_INVALID'],
        [{ type: 'select', options: [{ value: 'a', label: 'A' }], default: 'b' }, 'DEFAULT_INVALID'],
        [{ type: 'radio', options: [{ value: 1, label: 'A' }, { value: 2, label: 'B' }], default: 1 }, 'DEFAULT_INVALID'],
        [{ type: 'multiselect', options: [{ value: 'a', label: 'A' }], default: 'a' }, 'DEFAULT_NOT_ALLOWED'],
        [{ type: 'phonelist', default: 'x' }, 'DEFAULT_NOT_ALLOWED'],
        [{ type: 'color', default: '#ff0000' }, 'DEFAULT_NOT_ALLOWED'],
        [{ type: 'password', default: 'secret' }, 'DEFAULT_NOT_ALLOWED']
    ];
    for (const [extra, code] of reject) {
        const template = golden();
        formFields(template).push({ name: 'extraField', label: '額外', ...extra });
        assertRejected(await validateRequest({ template }), code, 'definitions.pages[2].definition.fields[7].default');
    }

    const accept = [
        { type: 'checkbox', default: false },
        { type: 'toggle', default: true },
        { type: 'date', default: 'today' },
        { type: 'date', default: '2026-01-31' },
        { type: 'time', default: '09:30' },
        { type: 'number', default: 2.5, validation: { min: 0, max: 10 } },
        { type: 'text', default: 'abc', validation: { maxLength: 3 } },
        { type: 'select', options: [{ value: 'a', label: 'A' }, { value: 'b', label: 'B' }], default: 'b' },
        { type: 'radio', options: [{ value: 'a', label: 'A' }], default: 'a' },
        { type: 'hidden', default: 'fixed' }
    ];
    for (const extra of accept) {
        const template = golden();
        formFields(template).push({ name: 'extraField', label: '額外', ...extra });
        const result = await validateRequest({ template });
        assert.equal(result.ok, true, `${JSON.stringify(extra)}: ${JSON.stringify(result.errors)}`);
    }
});

test('required is rejected where the form cannot tell an empty value, and minItems is not offered', async () => {
    for (const type of ['phonelist', 'personinfo', 'socialmedia', 'checkbox', 'toggle', 'student']) {
        const template = golden();
        formFields(template).push({ name: 'extraField', type, label: '額外', required: true });
        assertRejected(await validateRequest({ template }), 'REQUIRED_NOT_SUPPORTED', 'definitions.pages[2].definition.fields[7].required');
    }
    const optional = golden();
    formFields(optional).push({ name: 'extraField', type: 'phonelist', label: '電話', required: false, validation: { maxItems: 3 } });
    assert.equal((await validateRequest({ template: optional })).ok, true);

    const minItems = golden();
    formFields(minItems).push({ name: 'extraField', type: 'phonelist', label: '電話', validation: { minItems: 2 } });
    const error = assertRejected(await validateRequest({ template: minItems }), 'VALIDATION_KEY_UNSUPPORTED', 'fields[7].validation.minItems');
    assert.match(error.hint, /maxItems/);
});

test('the golden example has no cross-page warnings', async () => {
    const result = await validateRequest({ template: golden() });
    assert.deepEqual(result.warnings, []);
});

test('with page_ids the cross-page checks look only at the selected pages, but every page is still validated', async () => {
    // 只選列表：同資源的表單不會被生成，列表永遠沒有資料，要發 warning（不擋生成）。
    const listOnly = await validateRequest({ template: golden(), page_ids: ['contacts-list'] });
    assert.equal(listOnly.ok, true, JSON.stringify(listOnly.errors));
    assert.deepEqual(listOnly.pages.map(page => page.id), ['contacts-list']);
    assert.deepEqual(listOnly.warnings.map(entry => [entry.code, entry.path]),
        [['RESOURCE_WITHOUT_FORM', 'definitions.pages[0].definition.api']], JSON.stringify(listOnly.warnings));

    // 只選表單：沒有同資源的列表，記錄不會出現在任何地方。
    const formOnly = await validateRequest({ template: golden(), page_ids: ['contact-form'] });
    assert.equal(formOnly.ok, true);
    assert.deepEqual(formOnly.warnings.map(entry => [entry.code, entry.path]),
        [['FORM_WITHOUT_LIST', 'definitions.pages[2].definition.api']], JSON.stringify(formOnly.warnings));

    // 列表與表單一起選：同一資源的流程完整，沒有 warning；沒選的明細頁不影響結果。
    const listAndForm = await validateRequest({ template: golden(), page_ids: ['contacts-list', 'contact-form'] });
    assert.equal(listAndForm.ok, true);
    assert.deepEqual(listAndForm.warnings, []);

    // 沒被選取的頁仍逐層驗證：它的錯誤照樣讓結果不通過。
    const brokenDetail = golden();
    brokenDetail.definitions.pages[1].definition.fields[0].type = 'slider';
    const selectedOk = await validateRequest({ template: brokenDetail, page_ids: ['contacts-list', 'contact-form'] });
    assertRejected(selectedOk, 'FIELD_TYPE_UNSUPPORTED', 'definitions.pages[1].definition.fields[0].type');
});

test('layer 7 warns when pages of one resource do not line up', async () => {
    const moved = golden();
    moved.definitions.pages[2].definition.api = { get: '/api/people', create: '/api/people', update: '/api/people' };
    const result = await validateRequest({ template: moved });
    assert.equal(result.ok, true, 'cross-page checks only warn');
    const withoutForm = result.warnings.filter(entry => entry.code === 'RESOURCE_WITHOUT_FORM').map(entry => entry.path);
    assert.deepEqual(withoutForm, ['definitions.pages[0].definition.api', 'definitions.pages[1].definition.api'], JSON.stringify(result.warnings));
    assert.ok(result.warnings.some(entry => entry.code === 'FORM_WITHOUT_LIST' && entry.path === 'definitions.pages[2].definition.api'),
        JSON.stringify(result.warnings));

    const renamed = golden();
    renamed.definitions.pages[0].definition.fields[0].name = 'name';
    const fieldResult = await validateRequest({ template: renamed });
    assert.equal(fieldResult.ok, true);
    assert.ok(fieldResult.warnings.some(entry => entry.code === 'FIELD_NOT_IN_FORM' && entry.path === 'definitions.pages[0].definition.fields[0].name'),
        JSON.stringify(fieldResult.warnings));

    const options = golden();
    options.definitions.pages[1].definition.fields[3].options = [{ value: 'customer', label: '顧客' }];
    const optionResult = await validateRequest({ template: options });
    assert.equal(optionResult.ok, true);
    assert.ok(optionResult.warnings.some(entry => entry.code === 'OPTIONS_MISMATCH' && entry.path === 'definitions.pages[1].definition.fields[3].options'),
        JSON.stringify(optionResult.warnings));
});
