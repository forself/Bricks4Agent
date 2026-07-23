const IDENTIFIER = /^[A-Za-z_][A-Za-z0-9_]*$/;
const SAFE_TOKEN = /^[A-Za-z0-9][A-Za-z0-9_.-]*$/;
const ROUTE = /^\/api\/[a-z0-9][a-z0-9/_-]*$/;
const RAW_HTML = /<\/?[A-Za-z!][^>]*>|javascript\s*:|\bon[a-z]+\s*=/i;
const PROTOTYPE_KEYS = new Set(['__proto__', 'prototype', 'constructor']);

export const FORM_APPLICATION_SCHEMA_VERSION = 1;
export const PROVIDERS = Object.freeze(['sqlite', 'sqlserver', 'postgresql', 'mysql']);
export const DATA_TYPES = Object.freeze([
    'integer', 'long', 'decimal', 'number', 'boolean', 'text', 'date', 'datetime', 'guid', 'binary', 'json'
]);
export const COMPONENTS = Object.freeze([
    'TextInput', 'TextArea', 'NumberInput', 'Checkbox', 'ToggleSwitch', 'DatePicker', 'DateTimeInput',
    'Dropdown', 'MultiSelectDropdown', 'Radio', 'BatchUploader', 'HiddenInput', 'WebTextEditor',
    'ColorPicker', 'Slider'
]);
export const FIELD_TYPES = Object.freeze([
    'hidden', 'text', 'textarea', 'email', 'password', 'tel', 'url', 'number', 'checkbox', 'toggle',
    'date', 'datetime', 'select', 'multiselect', 'radio', 'file', 'richtext', 'color', 'slider'
]);
export const OPERATIONS = Object.freeze(['list', 'get', 'create', 'update', 'delete']);
export const GENERATION_TARGETS = Object.freeze(['spa-net8']);

const TOP_LEVEL_KEYS = new Set([
    'schema_version', 'application_id', 'display_name', 'source', 'persistence', 'table', 'fields', 'form', 'api', 'generation'
]);

const DB_TYPE_ALIASES = Object.freeze({
    int: 'integer', integer: 'integer', smallint: 'integer', tinyint: 'integer', int32: 'integer',
    bigint: 'long', int64: 'long',
    decimal: 'decimal', numeric: 'decimal', money: 'decimal',
    number: 'number', float: 'number', double: 'number', real: 'number',
    bool: 'boolean', boolean: 'boolean', bit: 'boolean',
    string: 'text', text: 'text', email: 'text', varchar: 'text', nvarchar: 'text', char: 'text', nchar: 'text', clob: 'text',
    date: 'date', datetime: 'datetime', datetime2: 'datetime', timestamp: 'datetime',
    guid: 'guid', uuid: 'guid', uniqueidentifier: 'guid',
    binary: 'binary', blob: 'binary', varbinary: 'binary', bytea: 'binary',
    json: 'json', jsonb: 'json'
});

const DEFAULT_INPUTS = Object.freeze({
    integer: ['number', 'NumberInput'],
    long: ['number', 'NumberInput'],
    decimal: ['number', 'NumberInput'],
    number: ['number', 'NumberInput'],
    boolean: ['toggle', 'ToggleSwitch'],
    text: ['text', 'TextInput'],
    date: ['date', 'DatePicker'],
    datetime: ['datetime', 'DateTimeInput'],
    guid: ['text', 'TextInput'],
    binary: ['file', 'BatchUploader'],
    json: ['textarea', 'TextArea']
});

const ALLOWED_COMPONENT_FIELD_TYPES = Object.freeze({
    TextInput: ['hidden', 'text', 'email', 'password', 'tel', 'url', 'date', 'datetime'],
    TextArea: ['textarea'],
    NumberInput: ['number'],
    Checkbox: ['checkbox'],
    ToggleSwitch: ['toggle'],
    DatePicker: ['date'],
    DateTimeInput: ['datetime'],
    Dropdown: ['select'],
    MultiSelectDropdown: ['multiselect'],
    Radio: ['radio'],
    BatchUploader: ['file'],
    HiddenInput: ['hidden'],
    WebTextEditor: ['richtext'],
    ColorPicker: ['color'],
    Slider: ['slider']
});

function isPlainObject(value) {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return false;
    const prototype = Object.getPrototypeOf(value);
    return prototype === Object.prototype || prototype === null;
}

function object(value, path) {
    if (!isPlainObject(value)) throw new TypeError(`${path} must be a plain object.`);
    return value;
}

function keys(value, allowed, path) {
    for (const key of Object.keys(value)) {
        if (!allowed.has(key)) throw new TypeError(`${path} contains unsupported property '${key}'.`);
    }
}

function string(value, path, fallback) {
    if (value === undefined && fallback !== undefined) value = fallback;
    if (typeof value !== 'string' || value.trim() === '') throw new TypeError(`${path} must be a non-blank string.`);
    const result = value.trim();
    if (RAW_HTML.test(result)) throw new TypeError(`${path} must not contain HTML or executable markup.`);
    return result;
}

function identifier(value, path) {
    const result = string(value, path);
    if (result.length > 128 || !IDENTIFIER.test(result)) throw new TypeError(`${path} is not a safe identifier.`);
    return result;
}

function boolean(value, path, fallback = false) {
    if (value === undefined) return fallback;
    if (typeof value !== 'boolean') throw new TypeError(`${path} must be boolean.`);
    return value;
}

function finite(value, path, fallback = null) {
    if (value === undefined || value === null) return fallback;
    if (typeof value !== 'number' || !Number.isFinite(value)) throw new TypeError(`${path} must be a finite number or null.`);
    return value;
}

function boundedInteger(value, minimum, maximum, fallback) {
    if (!Number.isFinite(value)) return fallback;
    return Math.min(maximum, Math.max(minimum, Math.round(value)));
}

function validateJson(value, path) {
    if (value === null || ['string', 'boolean'].includes(typeof value)) {
        if (typeof value === 'string' && RAW_HTML.test(value)) throw new TypeError(`${path} contains unsafe markup.`);
        return value;
    }
    if (typeof value === 'number') {
        if (!Number.isFinite(value)) throw new TypeError(`${path} contains a non-finite number.`);
        return value;
    }
    if (Array.isArray(value)) return value.map((entry, index) => validateJson(entry, `${path}[${index}]`));
    if (!isPlainObject(value)) throw new TypeError(`${path} must contain JSON-only data.`);
    const result = {};
    for (const key of Object.keys(value).sort()) {
        if (PROTOTYPE_KEYS.has(key)) throw new TypeError(`${path} contains prototype-sensitive key '${key}'.`);
        result[key] = validateJson(value[key], `${path}.${key}`);
    }
    return result;
}

function normalizeDbType(value, path = 'field.db_type') {
    const raw = string(value, path).toLowerCase();
    const base = raw.replace(/\s*\(\s*\d+(?:\s*,\s*\d+)?\s*\)$/, '');
    const normalized = DB_TYPE_ALIASES[base];
    if (!normalized || !DATA_TYPES.includes(normalized)) throw new TypeError(`${path} is not supported: ${raw}`);
    return normalized;
}

function extractMaxLength(value) {
    if (typeof value !== 'string') return null;
    const match = value.trim().match(/^(?:var)?(?:n?char|string)\s*\(\s*(\d+)\s*\)$/i);
    return match ? Number(match[1]) : null;
}

function normalizeInput(value, dbType, primaryKey, path) {
    const input = value === undefined ? {} : object(value, path);
    keys(input, new Set(['field_type', 'component', 'options']), path);
    const defaults = primaryKey ? ['hidden', 'TextInput'] : DEFAULT_INPUTS[dbType];
    const fieldType = string(input.field_type, `${path}.field_type`, defaults[0]).toLowerCase();
    const component = string(input.component, `${path}.component`, defaults[1]);
    if (!FIELD_TYPES.includes(fieldType)) throw new TypeError(`${path}.field_type is not supported: ${fieldType}`);
    if (!COMPONENTS.includes(component)) throw new TypeError(`${path}.component is not supported: ${component}`);
    if (!ALLOWED_COMPONENT_FIELD_TYPES[component].includes(fieldType)) {
        throw new TypeError(`${path} component '${component}' cannot render field type '${fieldType}'.`);
    }
    return {
        field_type: fieldType,
        component,
        options: validateJson(input.options ?? {}, `${path}.options`)
    };
}

function normalizeLayout(value, index) {
    const layout = value === undefined ? {} : object(value, `fields[${index}].layout`);
    keys(layout, new Set(['row', 'column', 'column_span', 'row_span', 'width', 'height']), `fields[${index}].layout`);
    const column = boundedInteger(layout.column, 1, 12, 1);
    const span = boundedInteger(layout.column_span, 1, 12, 12);
    return {
        row: boundedInteger(layout.row, 1, 10000, index + 1),
        column,
        column_span: Math.min(span, 13 - column),
        row_span: boundedInteger(layout.row_span, 1, 12, 1),
        width: layout.width === undefined || layout.width === null ? null : Math.max(1, finite(layout.width, `fields[${index}].layout.width`)),
        height: layout.height === undefined || layout.height === null ? null : Math.max(1, finite(layout.height, `fields[${index}].layout.height`))
    };
}

function normalizeValidation(value, nullable, path) {
    const validation = value === undefined ? {} : object(value, path);
    keys(validation, new Set(['required', 'min_length', 'max_length', 'minimum', 'maximum', 'pattern']), path);
    const result = { required: boolean(validation.required, `${path}.required`, !nullable) };
    for (const key of ['min_length', 'max_length', 'minimum', 'maximum']) {
        if (validation[key] !== undefined) result[key] = finite(validation[key], `${path}.${key}`);
    }
    if (result.min_length !== undefined && result.min_length < 0) throw new TypeError(`${path}.min_length cannot be negative.`);
    if (result.max_length !== undefined && result.max_length < 1) throw new TypeError(`${path}.max_length must be positive.`);
    if (result.minimum !== undefined && result.maximum !== undefined && result.minimum > result.maximum) {
        throw new TypeError(`${path}.minimum cannot exceed maximum.`);
    }
    if (validation.pattern !== undefined) result.pattern = string(validation.pattern, `${path}.pattern`);
    return result;
}

function normalizeField(value, index) {
    const field = object(value, `fields[${index}]`);
    keys(field, new Set([
        'field_id', 'column_name', 'display_name', 'db_type', 'nullable', 'primary_key', 'identity', 'default',
        'icon', 'input', 'validation', 'layout', 'order', 'max_length'
    ]), `fields[${index}]`);
    const columnName = identifier(field.column_name, `fields[${index}].column_name`);
    const primaryKey = boolean(field.primary_key, `fields[${index}].primary_key`);
    const dbType = normalizeDbType(field.db_type, `fields[${index}].db_type`);
    const identity = boolean(field.identity, `fields[${index}].identity`);
    if (identity && (!primaryKey || !['integer', 'long'].includes(dbType))) {
        throw new TypeError(`fields[${index}].identity requires an integer primary key.`);
    }
    const defaultValue = field.default === undefined ? null : validateJson(field.default, `fields[${index}].default`);
    const normalized = {
        field_id: identifier(field.field_id ?? `field_${columnName.toLowerCase()}`, `fields[${index}].field_id`),
        column_name: columnName,
        display_name: string(field.display_name, `fields[${index}].display_name`, columnName),
        db_type: dbType,
        nullable: primaryKey ? false : boolean(field.nullable, `fields[${index}].nullable`),
        primary_key: primaryKey,
        identity,
        default: defaultValue,
        icon: string(field.icon, `fields[${index}].icon`, primaryKey ? 'number' : DEFAULT_INPUTS[dbType][0]),
        input: normalizeInput(field.input, dbType, primaryKey, `fields[${index}].input`),
        validation: normalizeValidation(field.validation, primaryKey ? true : boolean(field.nullable, `fields[${index}].nullable`), `fields[${index}].validation`),
        layout: normalizeLayout(field.layout, index),
        order: boundedInteger(field.order, 1, 10000, index + 1)
    };
    if (field.max_length !== undefined) normalized.max_length = boundedInteger(field.max_length, 1, 10485760, 255);
    return normalized;
}

function connectionFrom(definition, options) {
    const option = options.connectionString;
    const persisted = definition.persistence?.connection_string;
    const value = option !== undefined ? option : persisted;
    if (value === undefined || value === null) return '';
    if (typeof value !== 'string') throw new TypeError('connectionString must be a string, null, or undefined.');
    return value.trim();
}

export function normalizeFormApplication(input, options = {}) {
    const definition = object(input, 'input');
    keys(definition, TOP_LEVEL_KEYS, 'input');
    if (definition.schema_version !== undefined && definition.schema_version !== FORM_APPLICATION_SCHEMA_VERSION) {
        throw new TypeError(`schema_version must be ${FORM_APPLICATION_SCHEMA_VERSION}.`);
    }
    const applicationId = identifier(definition.application_id, 'application_id');
    const displayName = string(definition.display_name, 'display_name', applicationId);

    const source = object(definition.source ?? {}, 'source');
    keys(source, new Set(['mode', 'dialect']), 'source');
    const sourceMode = string(source.mode, 'source.mode', 'new');
    if (!['new', 'import'].includes(sourceMode)) throw new TypeError(`source.mode is not supported: ${sourceMode}`);

    const persistence = object(definition.persistence ?? {}, 'persistence');
    keys(persistence, new Set(['provider', 'connection_string', 'connection_string_name', 'sqlite_file']), 'persistence');
    const connectionString = connectionFrom(definition, options);
    const provider = connectionString === '' ? 'sqlite' : string(persistence.provider, 'persistence.provider').toLowerCase();
    if (!PROVIDERS.includes(provider)) throw new TypeError(`persistence.provider is not supported: ${provider}`);
    const sourceDialect = string(source.dialect, 'source.dialect', provider).toLowerCase();
    if (!PROVIDERS.includes(sourceDialect)) throw new TypeError(`source.dialect is not supported: ${sourceDialect}`);
    const connectionStringName = string(persistence.connection_string_name, 'persistence.connection_string_name', 'DefaultConnection');
    if (!SAFE_TOKEN.test(connectionStringName)) throw new TypeError('persistence.connection_string_name is unsafe.');

    const table = object(definition.table, 'table');
    keys(table, new Set(['name', 'mode', 'primary_key']), 'table');
    const tableName = identifier(table.name, 'table.name');
    const tableMode = string(table.mode, 'table.mode', 'create');
    if (tableMode !== 'create') throw new TypeError('Only table.mode=create is supported in preview/generate-only mode.');

    if (!Array.isArray(definition.fields) || definition.fields.length === 0) throw new TypeError('fields must be a non-empty array.');
    const fields = definition.fields.map(normalizeField).sort((left, right) => left.order - right.order || left.field_id.localeCompare(right.field_id));
    const fieldIds = new Set();
    const columnNames = new Set();
    for (const field of fields) {
        if (fieldIds.has(field.field_id)) throw new TypeError(`Duplicate field_id: ${field.field_id}`);
        if (columnNames.has(field.column_name)) throw new TypeError(`Duplicate column_name: ${field.column_name}`);
        fieldIds.add(field.field_id);
        columnNames.add(field.column_name);
    }
    const primaryKeys = fields.filter(field => field.primary_key);
    if (primaryKeys.length !== 1) throw new TypeError('Exactly one primary-key field is required.');
    const tablePrimaryKey = identifier(table.primary_key ?? primaryKeys[0].column_name, 'table.primary_key');
    if (tablePrimaryKey !== primaryKeys[0].column_name) throw new TypeError('table.primary_key must reference the primary-key field.');

    const form = object(definition.form ?? {}, 'form');
    keys(form, new Set(['page_name', 'submit_label']), 'form');
    const pageName = identifier(form.page_name ?? `${applicationId}FormPage`, 'form.page_name');
    if (!/^[A-Z][A-Za-z0-9]*Page$/.test(pageName)) throw new TypeError('form.page_name must be PascalCase and end in Page.');

    const api = object(definition.api ?? {}, 'api');
    keys(api, new Set(['route', 'operations', 'auth_required']), 'api');
    const route = string(api.route, 'api.route', `/api/${applicationId.replaceAll('_', '-')}`).toLowerCase();
    if (!ROUTE.test(route) || route.includes('//') || route.includes('..')) throw new TypeError('api.route is unsafe.');
    const requestedOperations = api.operations ?? OPERATIONS;
    if (!Array.isArray(requestedOperations) || requestedOperations.length === 0) throw new TypeError('api.operations must be non-empty.');
    const operationSet = new Set();
    for (const operation of requestedOperations) {
        if (!OPERATIONS.includes(operation)) throw new TypeError(`api operation is not supported: ${operation}`);
        if (operationSet.has(operation)) throw new TypeError(`Duplicate api operation: ${operation}`);
        operationSet.add(operation);
    }

    const generation = object(definition.generation ?? {}, 'generation');
    keys(generation, new Set(['target', 'output_name', 'mode', 'apply_database']), 'generation');
    const target = string(generation.target, 'generation.target', 'spa-net8');
    if (!GENERATION_TARGETS.includes(target)) throw new TypeError(`generation.target is not supported: ${target}`);
    const outputName = string(generation.output_name, 'generation.output_name', applicationId.replaceAll('_', '-'));
    if (!SAFE_TOKEN.test(outputName)) throw new TypeError('generation.output_name is unsafe.');
    if (generation.apply_database === true) throw new TypeError('Database apply is forbidden; generation is preview-only.');
    const mode = string(generation.mode, 'generation.mode', 'preview');
    if (!['preview', 'generate-only'].includes(mode)) throw new TypeError(`generation.mode is not supported: ${mode}`);

    return {
        schema_version: FORM_APPLICATION_SCHEMA_VERSION,
        application_id: applicationId,
        display_name: displayName,
        source: { mode: sourceMode, dialect: sourceDialect },
        persistence: {
            provider,
            connection_string: null,
            connection_string_name: connectionStringName,
            sqlite_file: provider === 'sqlite' ? `data/${applicationId}.db` : null
        },
        table: { name: tableName, mode: tableMode, primary_key: tablePrimaryKey },
        fields,
        form: { page_name: pageName, submit_label: string(form.submit_label, 'form.submit_label', 'Save') },
        api: {
            route,
            operations: OPERATIONS.filter(operation => operationSet.has(operation)),
            auth_required: boolean(api.auth_required, 'api.auth_required', true)
        },
        generation: {
            target,
            output_name: outputName,
            mode,
            apply_database: false
        }
    };
}

export function validateFormApplication(input) {
    try {
        normalizeFormApplication(input);
        return { valid: true, errors: [] };
    } catch (error) {
        return { valid: false, errors: [error instanceof Error ? error.message : String(error)] };
    }
}

function sortJson(value) {
    if (Array.isArray(value)) return value.map(sortJson);
    if (!isPlainObject(value)) return value;
    const result = {};
    for (const key of Object.keys(value).sort()) result[key] = sortJson(value[key]);
    return result;
}

export function canonicalizeFormApplication(definition, options = {}) {
    const normalized = normalizeFormApplication(definition, options);
    return `${JSON.stringify(sortJson(normalized), null, 2)}\n`;
}

export const normalizeFormApplicationDataType = normalizeDbType;
export const inferFormApplicationMaxLength = extractMaxLength;
export const FormApplicationDefaultInputs = DEFAULT_INPUTS;
