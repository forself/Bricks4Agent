import assert from 'node:assert/strict';
import test from 'node:test';
import {
    COMMON_TYPE_ALIASES,
    computeFieldTypeWhitelist,
    computeSliceFieldTypes,
    DEFAULT_KINDS,
    describeFieldTypes,
    FIELD_TYPE_NOTES,
    FIELD_TYPE_SUBSTITUTES,
    loadPageGenFieldTypes,
    OPTION_TYPES,
    RUNTIME_BLOCKED_FIELD_TYPES,
    supportsRequired
} from './field-types.mjs';

const EXPECTED_INTERSECTION = [
    'text', 'email', 'password', 'number', 'textarea',
    'date', 'time', 'datetime',
    'select', 'multiselect', 'checkbox', 'toggle', 'radio',
    'richtext', 'canvas', 'color', 'image', 'file',
    'address', 'addresslist', 'chained', 'list',
    'personinfo', 'phonelist', 'socialmedia', 'organization', 'student',
    'hidden'
];

test('the whitelist is the computed intersection of page-gen types and the support matrix (28 types)', () => {
    const { allowed, excluded } = computeFieldTypeWhitelist();
    assert.equal(allowed.length, 28);
    assert.deepEqual([...allowed].sort(), [...EXPECTED_INTERSECTION].sort());
    assert.deepEqual([...excluded.notInMatrix].sort(), ['rating', 'tags', 'tel', 'url']);
    assert.deepEqual([...excluded.outOfCatalog].sort(), ['geolocation', 'weather']);
    assert.deepEqual([...excluded.notInPageGen].sort(), ['memo', 'plaintext', 'rocDate', 'slider']);
});

test('the intersection is computed from the inputs, not copied', () => {
    const matrix = {
        field_type_support: {
            text: { status: 'supported' },
            weather: { status: 'out_of_catalog' },
            onlyInMatrix: { status: 'supported' }
        }
    };
    const { allowed, excluded } = computeFieldTypeWhitelist(matrix, ['text', 'weather', 'onlyInPageGen']);
    assert.deepEqual(allowed, ['text']);
    assert.deepEqual(excluded, {
        notInMatrix: ['onlyInPageGen'],
        outOfCatalog: ['weather'],
        notInPageGen: ['onlyInMatrix']
    });
});

test('page-gen exposes its field type list without running its CLI', () => {
    const types = loadPageGenFieldTypes();
    assert.ok(types.includes('text'));
    assert.equal(types.length, 34);
});

test('the slice blocks the types that cannot render, round-trip or keep a value without a backend', () => {
    const slice = computeSliceFieldTypes();
    assert.deepEqual([...slice.blocked].sort(), [
        'address', 'addresslist', 'canvas', 'chained', 'datetime', 'file', 'image', 'list', 'organization', 'richtext'
    ]);
    assert.equal(slice.allowed.length, 18);
    assert.deepEqual([...slice.allowed].sort(), [
        'checkbox', 'color', 'date', 'email', 'hidden', 'multiselect', 'number', 'password', 'personinfo',
        'phonelist', 'radio', 'select', 'socialmedia', 'student', 'text', 'textarea', 'time', 'toggle'
    ]);
    for (const type of slice.blocked) assert.ok(!slice.allowed.includes(type));
    for (const type of Object.keys(RUNTIME_BLOCKED_FIELD_TYPES)) {
        assert.ok(EXPECTED_INTERSECTION.includes(type), `${type} must come from the intersection`);
    }
});

test('every intersection type has a note, and every closed type has a substitute', () => {
    for (const type of EXPECTED_INTERSECTION) {
        assert.ok(FIELD_TYPE_NOTES[type], `missing note for ${type}`);
        assert.ok(Array.isArray(FIELD_TYPE_NOTES[type].validation));
    }
    const { excluded } = computeFieldTypeWhitelist();
    const closed = [...excluded.notInMatrix, ...excluded.outOfCatalog, ...excluded.notInPageGen, ...Object.keys(RUNTIME_BLOCKED_FIELD_TYPES)];
    for (const type of closed) {
        const substitute = FIELD_TYPE_SUBSTITUTES[type];
        assert.ok(substitute, `missing substitute for ${type}`);
        assert.ok(computeSliceFieldTypes().allowed.includes(substitute), `${type} substitute must be open`);
    }
    assert.deepEqual([...OPTION_TYPES].sort(), ['multiselect', 'radio', 'select']);
});

test('common programming type names map to open field types', () => {
    const { allowed } = computeSliceFieldTypes();
    for (const [alias, type] of Object.entries(COMMON_TYPE_ALIASES)) {
        assert.ok(!allowed.includes(alias), `${alias} must not be a field type`);
        assert.ok(allowed.includes(type), `${alias} maps to ${type}, which must be open`);
    }
    assert.deepEqual(Object.keys(COMMON_TYPE_ALIASES).sort(), ['bool', 'boolean', 'decimal', 'float', 'int', 'integer', 'string']);
});

test('field type descriptions carry the matrix default component', () => {
    const entries = describeFieldTypes();
    const select = entries.find(entry => entry.type === 'select');
    assert.equal(select.component, 'Dropdown');
    assert.match(select.requires, /options/);
    assert.equal(entries.find(entry => entry.type === 'hidden').component, null);
    assert.equal(entries.length, 18);
});

test('list and chained are closed: their rows or levels cannot be filled in, so they never store a value', () => {
    const { allowed } = computeSliceFieldTypes();
    assert.ok(!allowed.includes('list'));
    assert.ok(!allowed.includes('chained'));
    assert.equal(FIELD_TYPE_SUBSTITUTES.list, 'textarea');
    assert.equal(FIELD_TYPE_SUBSTITUTES.chained, 'select');
    assert.ok(!describeFieldTypes().some(entry => entry.type === 'list' || entry.type === 'chained'));
});

test('repeatable list types offer maxItems only and no required, because empty rows count as values', () => {
    for (const type of ['list', 'personinfo', 'phonelist', 'socialmedia']) {
        assert.deepEqual(FIELD_TYPE_NOTES[type].validation, ['maxItems'], type);
        assert.equal(supportsRequired(type), false, type);
        assert.equal(FIELD_TYPE_NOTES[type].default, null, type);
    }
    for (const type of computeSliceFieldTypes().allowed) {
        assert.ok(!FIELD_TYPE_NOTES[type].validation.includes('minItems'), `${type} must not claim minItems`);
    }
});

test('every open type states its default form, and the catalog lists default and required per type', () => {
    for (const type of computeSliceFieldTypes().allowed) {
        const kind = FIELD_TYPE_NOTES[type].default;
        assert.ok(kind === null || DEFAULT_KINDS.includes(kind), `${type} default kind ${kind}`);
    }
    const entries = describeFieldTypes();
    const byType = Object.fromEntries(entries.map(entry => [entry.type, entry]));
    assert.equal(byType.checkbox.default, 'true or false');
    assert.equal(byType.select.default, 'one of the option values, written as a string');
    assert.equal(byType.date.default, '"today" or YYYY-MM-DD');
    assert.equal(byType.multiselect.default, 'not available');
    assert.equal(byType.phonelist.required, 'not available');
    assert.equal('required' in byType.text, false, 'required is listed only where it is not available');
});

test('the student type is described as an is-student flag, not a student id or name', () => {
    assert.match(FIELD_TYPE_NOTES.student.note, /is-student flag/);
    assert.match(FIELD_TYPE_NOTES.student.note, /not a student ID/);
    assert.match(FIELD_TYPE_NOTES.student.note, /use text/);
});
