import assert from 'node:assert/strict';
import test from 'node:test';

import {
    canonicalizeFormApplication,
    generateFormApplicationBundle,
    normalizeFormApplication,
    schemaToFormApplication,
    validateFormApplication,
} from './index.js';

const SECRET = 'Server=db.example.test;Database=Forms;User Id=agent;Password=do-not-export';

function definition(overrides = {}) {
    return {
        schema_version: 1,
        application_id: 'customer_intake',
        display_name: 'Customer intake',
        source: {
            mode: 'new',
            dialect: 'sqlite',
        },
        persistence: {
            provider: 'sqlite',
            connection_string: null,
            connection_string_name: 'DefaultConnection',
            sqlite_file: 'data/customer_intake.db',
        },
        table: {
            name: 'CustomerIntake',
            mode: 'create',
            primary_key: 'Id',
        },
        fields: [
            {
                field_id: 'field_id',
                column_name: 'Id',
                display_name: 'Id',
                db_type: 'integer',
                nullable: false,
                primary_key: true,
                identity: true,
                default: null,
                icon: 'number',
                input: {
                    field_type: 'hidden',
                    component: 'TextInput',
                    options: {},
                },
                validation: { required: false },
                layout: {
                    row: 1,
                    column: 1,
                    column_span: 12,
                    row_span: 1,
                    width: null,
                    height: null,
                },
                order: 1,
            },
            {
                field_id: 'field_name',
                column_name: 'Name',
                display_name: 'Name',
                db_type: 'text',
                nullable: false,
                primary_key: false,
                identity: false,
                default: null,
                icon: 'text',
                input: {
                    field_type: 'text',
                    component: 'TextInput',
                    options: { placeholder: 'Customer name' },
                },
                validation: {
                    required: true,
                    min_length: 1,
                    max_length: 100,
                },
                layout: {
                    row: 2,
                    column: 1,
                    column_span: 6,
                    row_span: 1,
                    width: null,
                    height: null,
                },
                order: 2,
            },
        ],
        form: {
            page_name: 'CustomerIntakeFormPage',
            submit_label: 'Save',
        },
        api: {
            route: '/api/customer-intake',
            operations: ['list', 'get', 'create', 'update', 'delete'],
            auth_required: true,
        },
        generation: {
            target: 'spa-net8',
            output_name: 'customer-intake',
        },
        ...overrides,
    };
}

function assertInvalid(value, description) {
    const result = validateFormApplication(value);
    assert.equal(result?.valid, false, description);
    assert.ok(Array.isArray(result?.errors) && result.errors.length > 0, `${description}: errors are required`);
}

function allArtifactText(bundle) {
    const texts = [];
    const visit = (value) => {
        if (typeof value === 'string') texts.push(value);
        else if (Array.isArray(value)) value.forEach(visit);
        else if (value && typeof value === 'object') Object.values(value).forEach(visit);
    };
    visit(bundle);
    return texts.join('\n');
}

test('a normalized minimal definition satisfies the v1 contract', () => {
    const normalized = normalizeFormApplication(definition());
    const validation = validateFormApplication(normalized);

    assert.equal(validation.valid, true, validation.errors?.join('\n'));
    assert.equal(normalized.schema_version, 1);
    assert.equal(normalized.application_id, 'customer_intake');
    assert.equal(normalized.fields.length, 2);
});

test('schema mapping produces stable database fields and input components', () => {
    const mapped = schemaToFormApplication({
        name: 'CustomerIntake',
        columns: [
            { name: 'Id', type: 'INTEGER', nullable: false, primary_key: true, identity: true },
            { name: 'Name', type: 'VARCHAR(100)', nullable: false },
            { name: 'CreditLimit', type: 'DECIMAL(12,2)', nullable: true },
            { name: 'Subscribed', type: 'BOOLEAN', nullable: false },
            { name: 'CreatedAt', type: 'DATETIME', nullable: false },
        ],
    }, {
        applicationId: 'customer_intake',
        displayName: 'Customer intake',
        provider: 'sqlite',
    });

    assert.equal(validateFormApplication(mapped).valid, true);
    assert.deepEqual(
        mapped.fields.map((field) => [field.column_name, field.db_type, field.input.field_type, field.input.component]),
        [
            ['Id', 'integer', 'hidden', 'TextInput'],
            ['Name', 'text', 'text', 'TextInput'],
            ['CreditLimit', 'decimal', 'number', 'NumberInput'],
            ['Subscribed', 'boolean', 'toggle', 'ToggleSwitch'],
            ['CreatedAt', 'datetime', 'datetime', 'DateTimeInput'],
        ],
    );
});

test('only the four explicit BaseOrm providers are accepted', () => {
    for (const provider of ['sqlite', 'sqlserver', 'postgresql', 'mysql']) {
        const candidate = definition({
            source: { mode: 'new', dialect: provider },
            persistence: {
                provider,
                connection_string: provider === 'sqlite' ? 'Data Source=data/provided.db' : SECRET,
                connection_string_name: 'DefaultConnection',
                sqlite_file: provider === 'sqlite' ? 'data/provided.db' : null,
            },
        });
        assert.equal(validateFormApplication(candidate).valid, true, provider);
    }

    assertInvalid(definition({
        persistence: {
            provider: 'oracle',
            connection_string: 'anything',
            connection_string_name: 'DefaultConnection',
            sqlite_file: null,
        },
    }), 'unknown provider must fail closed');
});

test('missing, null, and blank connection strings all select the same local SQLite destination', () => {
    const values = [undefined, null, '', '   \r\n  '];
    const normalized = values.map((connectionString) => {
        const persistence = {
            provider: 'postgresql',
            connection_string_name: 'DefaultConnection',
            sqlite_file: null,
        };
        if (connectionString !== undefined) persistence.connection_string = connectionString;
        return normalizeFormApplication(definition({ persistence }));
    });

    for (const value of normalized) {
        assert.equal(value.persistence.provider, 'sqlite');
        assert.equal(value.persistence.sqlite_file, 'data/customer_intake.db');
        assert.ok(value.persistence.connection_string == null);
    }
    assert.deepEqual(normalized, normalized.map(() => normalized[0]));
});

test('validation rejects unsafe identifiers and every non-JSON or injection-bearing value', () => {
    assertInvalid(definition({ table: { name: 'Customers; DROP TABLE Users', mode: 'create', primary_key: 'Id' } }), 'SQL-bearing identifier');
    assertInvalid({ ...definition(), unexpected: true }, 'unknown top-level key');

    const prototypeKey = JSON.parse(JSON.stringify(definition()));
    prototypeKey.fields[1].input.options = JSON.parse('{"__proto__":{"polluted":true}}');
    assertInvalid(prototypeKey, 'prototype-sensitive key');

    const rawHtml = structuredClone(definition());
    rawHtml.fields[1].display_name = '<img src=x onerror=alert(1)>';
    assertInvalid(rawHtml, 'raw HTML');

    const callback = structuredClone(definition());
    callback.fields[1].default = () => {};
    assertInvalid(callback, 'function callback');

    const nonFinite = structuredClone(definition());
    nonFinite.fields[1].default = Number.NaN;
    assertInvalid(nonFinite, 'non-finite number');
});

test('component and icon replacement preserves the database column binding', () => {
    const before = normalizeFormApplication(definition());
    const edited = structuredClone(before);
    edited.fields[1].icon = 'mail';
    edited.fields[1].input = {
        field_type: 'email',
        component: 'TextInput',
        options: { inputType: 'email' },
    };
    const after = normalizeFormApplication(edited);

    assert.equal(after.fields[1].column_name, before.fields[1].column_name);
    assert.equal(after.fields[1].field_id, before.fields[1].field_id);
    assert.equal(after.fields[1].icon, 'mail');
    assert.equal(after.fields[1].input.field_type, 'email');
});

test('layout normalization clamps rows, columns, and spans to the twelve-column canvas', () => {
    const candidate = definition();
    candidate.fields[1].layout = {
        row: -5,
        column: 99,
        column_span: 99,
        row_span: 0,
        width: -100,
        height: -100,
    };

    const layout = normalizeFormApplication(candidate).fields[1].layout;
    assert.equal(layout.row, 1);
    assert.equal(layout.column, 12);
    assert.equal(layout.column_span, 1);
    assert.equal(layout.row_span, 1);
    assert.ok(layout.width == null || layout.width >= 1);
    assert.ok(layout.height == null || layout.height >= 1);
});

test('connection secrets never enter normalized JSON or PageDefinition', () => {
    const withSecret = definition({
        source: { mode: 'new', dialect: 'sqlserver' },
        persistence: {
            provider: 'sqlserver',
            connection_string: SECRET,
            connection_string_name: 'DefaultConnection',
            sqlite_file: null,
        },
    });

    const normalized = normalizeFormApplication(withSecret);
    const canonical = canonicalizeFormApplication(withSecret);
    const bundle = generateFormApplicationBundle(withSecret);

    assert.equal(JSON.stringify(normalized).includes(SECRET), false);
    assert.equal(canonical.includes(SECRET), false);
    assert.equal(JSON.stringify(bundle.pageDefinition).includes(SECRET), false);
});

test('external-provider bundle keeps its provider while removing the connection secret', () => {
    const source = definition({
        source: { mode: 'new', dialect: 'postgresql' },
        persistence: {
            provider: 'postgresql',
            connection_string: null,
            connection_string_name: 'DefaultConnection',
            sqlite_file: null,
        },
    });
    const bundle = generateFormApplicationBundle(source, { connectionString: SECRET });
    const exported = JSON.parse(bundle.files['definition/form-application.json']);

    assert.equal(bundle.definition.persistence.provider, 'postgresql');
    assert.equal(exported.persistence.provider, 'postgresql');
    assert.equal(exported.persistence.connection_string, null);
    assert.equal(JSON.stringify(bundle).includes(SECRET), false);
    assert.ok(bundle.files['database/postgresql/001_create_CustomerIntake.sql']);
});

test('canonical JSON is deterministic and round-trips without semantic drift', () => {
    const source = definition();
    const first = canonicalizeFormApplication(source);
    const second = canonicalizeFormApplication(structuredClone(source));

    assert.equal(first, second);
    assert.ok(first.endsWith('\n'));
    assert.deepEqual(
        normalizeFormApplication(JSON.parse(first)),
        normalizeFormApplication(source),
    );
    assert.equal(canonicalizeFormApplication(JSON.parse(first)), first);
});

test('bundle generation is deterministic and includes SQL, C#, API, and PageDefinition artifacts', () => {
    const source = definition();
    const first = generateFormApplicationBundle(source);
    const second = generateFormApplicationBundle(structuredClone(source));
    const artifactText = allArtifactText(first);

    assert.deepEqual(first, second);
    assert.ok(first.pageDefinition && typeof first.pageDefinition === 'object');
    assert.match(artifactText, /CREATE\s+TABLE/i);
    assert.match(artifactText, /CustomerIntake/);
    assert.match(artifactText, /class|record/);
    assert.match(artifactText, /Map(Post|Get|Put|Delete)|\/api\/customer-intake/);
    assert.equal(first.pageDefinition.type, 'form');
    assert.equal(first.pageDefinition.api.create, '/api/customer-intake');
});
