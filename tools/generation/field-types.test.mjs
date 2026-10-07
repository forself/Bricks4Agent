import assert from 'node:assert/strict';
import test from 'node:test';
import {
    computeFieldTypeWhitelist,
    computeSliceFieldTypes,
    describeFieldTypes,
    FIELD_TYPE_NOTES,
    FIELD_TYPE_SUBSTITUTES,
    loadPageGenFieldTypes,
    OPTION_TYPES,
    RUNTIME_BLOCKED_FIELD_TYPES
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

test('the slice blocks the types the browser smoke showed cannot render without a backend', () => {
    const slice = computeSliceFieldTypes();
    assert.deepEqual([...slice.blocked].sort(), ['address', 'addresslist', 'canvas', 'image', 'organization', 'richtext']);
    assert.equal(slice.allowed.length, 22);
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

test('field type descriptions carry the matrix default component', () => {
    const entries = describeFieldTypes();
    const select = entries.find(entry => entry.type === 'select');
    assert.equal(select.component, 'Dropdown');
    assert.match(select.requires, /options/);
    assert.equal(entries.find(entry => entry.type === 'hidden').component, null);
    assert.equal(entries.length, 22);
});
