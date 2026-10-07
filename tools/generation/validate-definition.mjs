// DefinitionTemplate 的分層驗證（fail-closed）。validate 與 build 共用這一份程式。
//   第 1 層 外殼與大小：位元組、深度、節點數、頁數、欄位數、危險鍵
//   第 2 層 嚴格鍵白名單與型別（等同 additionalProperties:false）
//   第 3 層 結構：tools/lib/definition-template.js 的 validateDefinitionTemplate
//   第 4 層 page-gen 規則：轉成新格式後跑 page-gen 的 validateNewDefinition
//   第 5 層 識別字：記憶體內呼叫 PageGenerator.generate()，補上 --validate 漏掉的檢查
//   第 6 層 型錄：欄位型別白名單（交集扣掉執行期無法使用的型別）、明示元件、頁型與 components 欄位
//   第 7 層 切片規則：api 基底路徑、頁 id 作檔名、保留名稱
// 錯誤格式 { code, path, message, hint }；path 以 template 為根，例如 definitions.pages[1].definition.fields[3].type
import { createRequire } from 'node:module';
import { readFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
import process from 'node:process';
import {
    CATALOG_PATH,
    DEFINITION_TEMPLATE_LIB_PATH,
    MATRIX_PATH,
    PAGE_GEN_PATH,
    PAGE_GENERATOR_DIR,
    VALIDATOR_VERSION
} from './paths.mjs';
import { canonicalJson, hasOwn, isPlainObject, sha256Hex } from './json-util.mjs';
import {
    computeSliceFieldTypes,
    FIELD_TYPE_NOTES,
    FIELD_TYPE_SUBSTITUTES,
    OPTION_TYPES,
    RUNTIME_BLOCKED_FIELD_TYPES
} from './field-types.mjs';
import {
    LIST_COLUMN_TYPES,
    PAGE_ID_PATTERN,
    SUPPORTED_PAGE_TYPES
} from '../../templates/definition-site/site-model.js';

const require = createRequire(import.meta.url);

export const LIMITS = Object.freeze({
    maxTemplateBytes: 256 * 1024,
    maxPages: 12,
    maxFieldsPerPage: 60,
    maxOptionsPerField: 100,
    maxDepth: 16,
    maxNodes: 20000,
    maxStringLength: 2000,
    maxTitleLength: 120
});

export const API_PATH_PATTERN = /^\/api\/[a-z0-9/_-]+$/;
const API_KEYS = ['list', 'get', 'create', 'update', 'delete'];
const DANGEROUS_KEYS = new Set(['__proto__', 'constructor', 'prototype']);
const RESERVED_PAGE_IDS = new Set([
    'con', 'prn', 'aux', 'nul',
    ...Array.from({ length: 9 }, (_, i) => `com${i + 1}`),
    ...Array.from({ length: 9 }, (_, i) => `lpt${i + 1}`)
]);
const RESERVED_FIELD_NAMES = new Set(['id', 'prototype', ...Object.getOwnPropertyNames(Object.prototype)]);
const PAGE_NAME_PATTERN = /^[A-Z][a-zA-Z0-9]*Page$/;
const COMPONENT_NAME_PATTERN = /^[A-Za-z][A-Za-z0-9]{0,63}$/;
// 只允許一般文字：不得含標記符號或控制字元（定義是宣告式資料，不承載 HTML 或程式碼）
const MARKUP_PATTERN = /[<>]/;
// eslint-disable-next-line no-control-regex
const CONTROL_PATTERN = /[\u0000-\u0008\u000B\u000C\u000E-\u001F\u007F]/;

// 由驗證層產生、可能與更具體錯誤重複的泛用代碼
const GENERIC_CODES = new Set(['STRUCTURE_INVALID', 'PAGEGEN_RULE', 'GENERATOR_REJECTED']);

function makeError(code, errorPath, message, hint = '') {
    return { code, path: errorPath, message, hint };
}

// ------------------------------------------------------------
// 第 2 層的鍵白名單
// ------------------------------------------------------------

const S = {
    object: (props, options = {}) => ({ kind: 'object', props, required: options.required || [], forbidden: options.forbidden || {} }),
    array: (items, options = {}) => ({ kind: 'array', items, min: options.min ?? 0, max: options.max ?? Infinity }),
    string: (options = {}) => ({ kind: 'string', max: options.max ?? LIMITS.maxStringLength, pattern: options.pattern || null, values: options.values || null, min: options.min ?? 0 }),
    integer: (options = {}) => ({ kind: 'integer', min: options.min ?? -Number.MAX_SAFE_INTEGER, max: options.max ?? Number.MAX_SAFE_INTEGER }),
    number: () => ({ kind: 'number' }),
    boolean: () => ({ kind: 'boolean' }),
    scalar: (options = {}) => ({ kind: 'scalar', max: options.max ?? 200 })
};

const API_SCHEMA = S.object(Object.fromEntries(API_KEYS.map(key => [key, S.string({ max: 120, min: 1 })])));
const OPTION_SCHEMA = S.object({
    value: S.scalar({ max: 100 }),
    label: S.string({ max: 100, min: 1 })
}, { required: ['value', 'label'] });
const VALIDATION_SCHEMA = S.object({
    maxLength: S.integer({ min: 1, max: 100000 }),
    min: S.number(),
    max: S.number(),
    minItems: S.integer({ min: 0, max: 1000 }),
    maxItems: S.integer({ min: 0, max: 1000 })
});
const FIELD_SCHEMA = S.object({
    name: S.string({ max: 64, min: 1 }),
    type: S.string({ max: 32, min: 1 }),
    label: S.string({ max: 100, min: 1 }),
    required: S.boolean(),
    default: S.scalar({ max: 500 }),
    options: S.array(OPTION_SCHEMA, { min: 1, max: LIMITS.maxOptionsPerField }),
    validation: VALIDATION_SCHEMA,
    component: S.string({ max: 64, min: 1 })
}, { required: ['name', 'type', 'label'] });
const PAGE_DEFINITION_SCHEMA = S.object({
    name: S.string({ max: 64, pattern: PAGE_NAME_PATTERN }),
    type: S.string({ max: 32, min: 1 }),
    description: S.string({ max: 200 }),
    fields: S.array(FIELD_SCHEMA, { min: 1, max: LIMITS.maxFieldsPerPage }),
    api: API_SCHEMA
}, {
    required: ['name', 'type', 'fields'],
    forbidden: {
        components: ['COMPONENTS_NOT_ALLOWED', 'The components list is not available in this slice; pick field types instead.'],
        page: ['PAGE_ENTITY_NOT_ALLOWED', 'Write page definitions in the template format (name/type/fields/api); output file names always come from the page id.'],
        entity: ['PAGE_ENTITY_NOT_ALLOWED', 'Output file names always come from the page id; remove entity.']
    }
});
const PAGE_ENTRY_SCHEMA = S.object({
    id: S.string({ max: 64, pattern: PAGE_ID_PATTERN }),
    definition: PAGE_DEFINITION_SCHEMA
}, { required: ['id', 'definition'] });
const TEMPLATE_SCHEMA = S.object({
    kind: S.string({ values: ['definition-template'] }),
    version: S.string({ values: ['0.1.0'] }),
    meta: S.object({
        title: S.string({ max: LIMITS.maxTitleLength }),
        description: S.string({ max: 500 })
    }),
    definitions: S.object({
        pages: S.array(PAGE_ENTRY_SCHEMA, { min: 1, max: LIMITS.maxPages }),
        apps: S.array(S.object({}), { max: 0 })
    }, { required: ['pages'] })
}, { required: ['kind', 'version', 'definitions'] });

function describeType(value) {
    if (value === null) return 'null';
    if (Array.isArray(value)) return 'array';
    return typeof value;
}

function checkText(value, valuePath, errors) {
    if (MARKUP_PATTERN.test(value)) {
        errors.push(makeError('MARKUP_NOT_ALLOWED', valuePath, 'Text must not contain < or >.', 'Definitions are declarative data; write plain text only.'));
    }
    if (CONTROL_PATTERN.test(value)) {
        errors.push(makeError('CONTROL_CHARACTER', valuePath, 'Text contains a control character.', 'Remove control characters.'));
    }
}

function checkSchema(schema, value, valuePath, errors) {
    switch (schema.kind) {
        case 'object': {
            if (!isPlainObject(value)) {
                errors.push(makeError('TYPE_MISMATCH', valuePath, `Expected an object, got ${describeType(value)}.`));
                return;
            }
            const allowed = Object.keys(schema.props);
            for (const key of Object.keys(value)) {
                const childPath = valuePath ? `${valuePath}.${key}` : key;
                if (hasOwn(schema.forbidden, key)) {
                    const [code, hint] = schema.forbidden[key];
                    errors.push(makeError(code, childPath, `Key "${key}" is not allowed here.`, hint));
                    continue;
                }
                if (!hasOwn(schema.props, key)) {
                    errors.push(makeError('UNKNOWN_KEY', childPath, `Unknown key "${key}".`, `Allowed keys: ${allowed.join(', ') || '(none)'}.`));
                    continue;
                }
                if (schema === TEMPLATE_SCHEMA.props.definitions && key === 'apps') {
                    // DefinitionTemplate 允許 apps；本切片只生成頁面，空陣列可接受
                    if (!Array.isArray(value.apps) || value.apps.length > 0) {
                        errors.push(makeError('APPS_NOT_SUPPORTED', childPath, 'definitions.apps is not supported in this slice.', 'Remove apps or leave it as an empty array; only pages are generated.'));
                    }
                    continue;
                }
                checkSchema(schema.props[key], value[key], childPath, errors);
            }
            for (const key of schema.required) {
                if (!hasOwn(value, key)) {
                    errors.push(makeError('MISSING_KEY', valuePath ? `${valuePath}.${key}` : key, `Missing required key "${key}".`));
                }
            }
            return;
        }
        case 'array': {
            if (!Array.isArray(value)) {
                errors.push(makeError('TYPE_MISMATCH', valuePath, `Expected an array, got ${describeType(value)}.`));
                return;
            }
            if (value.length < schema.min) {
                errors.push(makeError('VALUE_INVALID', valuePath, `Expected at least ${schema.min} item(s).`));
            }
            if (value.length > schema.max) {
                errors.push(makeError('VALUE_INVALID', valuePath, `Expected at most ${schema.max} item(s).`));
            }
            value.forEach((item, index) => checkSchema(schema.items, item, `${valuePath}[${index}]`, errors));
            return;
        }
        case 'string': {
            if (typeof value !== 'string') {
                errors.push(makeError('TYPE_MISMATCH', valuePath, `Expected a string, got ${describeType(value)}.`));
                return;
            }
            if (value.length < schema.min || value.length > schema.max) {
                errors.push(makeError('VALUE_INVALID', valuePath, `String length must be between ${schema.min} and ${schema.max}.`));
            }
            if (schema.values && !schema.values.includes(value)) {
                errors.push(makeError('VALUE_INVALID', valuePath, `Must be one of: ${schema.values.join(', ')}.`));
            }
            if (schema.pattern && !schema.pattern.test(value)) {
                errors.push(makeError('VALUE_INVALID', valuePath, `Does not match ${schema.pattern.source}.`));
            }
            checkText(value, valuePath, errors);
            return;
        }
        case 'integer':
            if (!Number.isInteger(value) || value < schema.min || value > schema.max) {
                errors.push(makeError('TYPE_MISMATCH', valuePath, `Expected an integer between ${schema.min} and ${schema.max}.`));
            }
            return;
        case 'number':
            if (typeof value !== 'number' || !Number.isFinite(value)) {
                errors.push(makeError('TYPE_MISMATCH', valuePath, 'Expected a finite number.'));
            }
            return;
        case 'boolean':
            if (typeof value !== 'boolean') {
                errors.push(makeError('TYPE_MISMATCH', valuePath, `Expected a boolean, got ${describeType(value)}.`));
            }
            return;
        case 'scalar':
            if (typeof value === 'string') {
                if (value.length > schema.max) {
                    errors.push(makeError('VALUE_INVALID', valuePath, `String length must be at most ${schema.max}.`));
                }
                checkText(value, valuePath, errors);
            } else if (!(typeof value === 'boolean' || (typeof value === 'number' && Number.isFinite(value)))) {
                errors.push(makeError('TYPE_MISMATCH', valuePath, `Expected a string, number or boolean, got ${describeType(value)}.`));
            }
            return;
        default:
            throw new Error(`unknown schema kind ${schema.kind}`);
    }
}

// ------------------------------------------------------------
// 第 1 層：外殼與大小（迭代走訪，深度與節點數超限立即停止）
// ------------------------------------------------------------

function scanEnvelope(template) {
    const errors = [];
    const stack = [{ value: template, depth: 1, path: '' }];
    let nodes = 0;
    while (stack.length > 0) {
        const { value, depth, path: valuePath } = stack.pop();
        nodes += 1;
        if (nodes > LIMITS.maxNodes) {
            return { errors: [makeError('TEMPLATE_TOO_COMPLEX', '', `The template has more than ${LIMITS.maxNodes} values.`, 'Reduce pages, fields or options.')], fatal: true };
        }
        if (depth > LIMITS.maxDepth) {
            return { errors: [makeError('TEMPLATE_TOO_DEEP', valuePath, `Nesting deeper than ${LIMITS.maxDepth} levels.`, 'Use the documented flat page and field structure.')], fatal: true };
        }
        if (value === null || typeof value === 'boolean') continue;
        if (typeof value === 'number') {
            if (!Number.isFinite(value)) errors.push(makeError('NON_JSON_VALUE', valuePath, 'Numbers must be finite.'));
            continue;
        }
        if (typeof value === 'string') {
            if (value.length > LIMITS.maxStringLength) {
                errors.push(makeError('STRING_TOO_LONG', valuePath, `Strings are limited to ${LIMITS.maxStringLength} characters.`));
            }
            continue;
        }
        if (Array.isArray(value)) {
            for (let index = value.length - 1; index >= 0; index -= 1) {
                stack.push({ value: value[index], depth: depth + 1, path: `${valuePath}[${index}]` });
            }
            continue;
        }
        if (!isPlainObject(value)) {
            errors.push(makeError('NON_JSON_VALUE', valuePath, 'Only JSON values are accepted.'));
            continue;
        }
        const keys = Object.keys(value);
        for (let index = keys.length - 1; index >= 0; index -= 1) {
            const key = keys[index];
            const childPath = valuePath ? `${valuePath}.${key}` : key;
            if (DANGEROUS_KEYS.has(key)) {
                errors.push(makeError('DANGEROUS_KEY', childPath, `Key "${key}" is not allowed anywhere in a definition.`, 'Remove the key.'));
                continue;
            }
            const descriptor = Object.getOwnPropertyDescriptor(value, key);
            if (!descriptor || descriptor.get || descriptor.set) {
                errors.push(makeError('NON_JSON_VALUE', childPath, 'Only JSON data properties are accepted.'));
                continue;
            }
            stack.push({ value: descriptor.value, depth: depth + 1, path: childPath });
        }
    }

    let bytes;
    try {
        bytes = Buffer.byteLength(JSON.stringify(template), 'utf8');
    } catch {
        return { errors: [...errors, makeError('NON_JSON_VALUE', '', 'The template cannot be serialised as JSON.')], fatal: true };
    }
    if (bytes > LIMITS.maxTemplateBytes) {
        errors.push(makeError('TEMPLATE_TOO_LARGE', '', `The template is ${bytes} bytes; the limit is ${LIMITS.maxTemplateBytes}.`, 'Reduce pages, fields or text.'));
    }

    const pages = template?.definitions?.pages;
    if (Array.isArray(pages)) {
        if (pages.length > LIMITS.maxPages) {
            errors.push(makeError('TOO_MANY_PAGES', 'definitions.pages', `At most ${LIMITS.maxPages} pages are allowed.`));
        }
        pages.forEach((entry, index) => {
            const fields = entry?.definition?.fields;
            if (Array.isArray(fields) && fields.length > LIMITS.maxFieldsPerPage) {
                errors.push(makeError('TOO_MANY_FIELDS', `definitions.pages[${index}].definition.fields`, `At most ${LIMITS.maxFieldsPerPage} fields per page are allowed.`));
            }
            if (Array.isArray(fields)) {
                fields.forEach((field, fieldIndex) => {
                    if (Array.isArray(field?.options) && field.options.length > LIMITS.maxOptionsPerField) {
                        errors.push(makeError('TOO_MANY_OPTIONS', `definitions.pages[${index}].definition.fields[${fieldIndex}].options`, `At most ${LIMITS.maxOptionsPerField} options are allowed.`));
                    }
                });
            }
        });
    }
    return { errors, fatal: errors.some(error => error.code === 'DANGEROUS_KEY' || error.code === 'NON_JSON_VALUE') };
}

// ------------------------------------------------------------
// 共用載入
// ------------------------------------------------------------

let contextPromise = null;

/** 載入型錄、support matrix 與 repo 內既有的驗證器（同一程序內只載入一次） */
export function loadValidationContext() {
    if (!contextPromise) {
        contextPromise = (async () => {
            const catalogBytes = readFileSync(CATALOG_PATH);
            const matrixBytes = readFileSync(MATRIX_PATH);
            const catalog = JSON.parse(catalogBytes.toString('utf8'));
            const matrix = JSON.parse(matrixBytes.toString('utf8'));
            const pageGen = require(PAGE_GEN_PATH);
            const templateLib = require(DEFINITION_TEMPLATE_LIB_PATH);
            const importModule = (name) => import(pathToFileURL(path.join(PAGE_GENERATOR_DIR, name)).href);
            const [adapterModule, generatorModule] = await Promise.all([
                importModule('PageDefinitionAdapter.js'),
                importModule('PageGenerator.js')
            ]);
            const slice = computeSliceFieldTypes(matrix, pageGen.VALID_FIELD_TYPES);
            return {
                catalog,
                matrix,
                catalogSha256: sha256Hex(catalogBytes),
                matrixSha256: sha256Hex(matrixBytes),
                allowedFieldTypes: new Set(slice.allowed),
                validateNewDefinition: pageGen.validateNewDefinition,
                validateDefinitionTemplate: templateLib.validateDefinitionTemplate,
                PageDefinitionAdapter: adapterModule.PageDefinitionAdapter,
                PageGenerator: generatorModule.PageGenerator
            };
        })();
        contextPromise.catch(() => { contextPromise = null; });
    }
    return contextPromise;
}

// ------------------------------------------------------------
// 第 3～5 層：沿用 repo 內既有的驗證器
// ------------------------------------------------------------

function pagePath(index) {
    return `definitions.pages[${index}].definition`;
}

function layerStructure(context, template, pages) {
    return context.validateDefinitionTemplate(template).then(result => {
        if (result.valid) return [];
        return result.errors.map((message) => {
            const match = /^page ([^:]+): /.exec(message);
            const index = match ? pages.findIndex(entry => entry?.id === match[1]) : -1;
            return makeError('STRUCTURE_INVALID', index >= 0 ? pagePath(index) : '', String(message));
        });
    });
}

function layerPageGenRules(context, pages) {
    const errors = [];
    pages.forEach((entry, index) => {
        if (!isPlainObject(entry?.definition)) return;
        const converted = context.PageDefinitionAdapter.toNewFormat(entry.definition);
        const result = context.validateNewDefinition(converted);
        if (result.valid) return;
        for (const message of result.errors) {
            const match = /^fields\[(\d+)\]/.exec(message);
            errors.push(makeError('PAGEGEN_RULE', match ? `${pagePath(index)}.fields[${match[1]}]` : pagePath(index), String(message)));
        }
    });
    return errors;
}

function withoutExplicitComponents(definition) {
    const copy = { ...definition };
    delete copy.components;
    if (Array.isArray(copy.fields)) {
        copy.fields = copy.fields.map(field => {
            if (!isPlainObject(field)) return field;
            const { component: _component, ...rest } = field;
            return rest;
        });
    }
    return copy;
}

const IDENTIFIER_MESSAGE = /IdentifierName|reserved word/;

function layerIdentifiers(context, pages) {
    const errors = [];
    const generator = new context.PageGenerator();
    pages.forEach((entry, index) => {
        if (!isPlainObject(entry?.definition)) return;
        // 明示元件在第 6 層依型錄檢查；這裡只取生成器的識別字與保留字判斷
        const definition = withoutExplicitComponents(entry.definition);
        const result = generator.generate(definition);
        let messages = Array.isArray(result.errors) ? result.errors.map(String) : [];
        if (messages.length === 0) return;
        // generate() 在結構或元件匯入檢查失敗時會提早返回，識別字錯誤因此被遮住；
        // 這時直接取生成器同一份識別字檢查，讓代理一次看到全部問題
        if (!messages.some(text => IDENTIFIER_MESSAGE.test(text)) && typeof generator._collectIdentifierErrors === 'function') {
            messages = [...messages, ...generator._collectIdentifierErrors(definition).map(String)];
        }
        const fields = Array.isArray(entry.definition.fields) ? entry.definition.fields : [];
        for (const text of messages) {
            if (IDENTIFIER_MESSAGE.test(text)) {
                let errorPath = `${pagePath(index)}.name`;
                if (text.startsWith('field.name ')) {
                    const fieldIndex = fields.findIndex(field => text.startsWith(`field.name "${field?.name}" `));
                    errorPath = fieldIndex >= 0 ? `${pagePath(index)}.fields[${fieldIndex}].name` : `${pagePath(index)}.fields`;
                }
                errors.push(makeError('IDENTIFIER_INVALID', errorPath, text, 'Names are emitted as JavaScript identifiers: letters (CJK allowed), digits, _ or $; no hyphens or spaces.'));
            } else {
                errors.push(makeError('GENERATOR_REJECTED', pagePath(index), text));
            }
        }
    });
    return errors;
}

// ------------------------------------------------------------
// 第 6 層：型錄
// ------------------------------------------------------------

function layerCatalog(context, pages) {
    const errors = [];
    const byName = context.catalog.by_registry_name || {};
    pages.forEach((entry, index) => {
        const definition = entry?.definition;
        if (!isPlainObject(definition)) return;
        const base = pagePath(index);
        const pageType = definition.type;
        if (typeof pageType === 'string' && !SUPPORTED_PAGE_TYPES.includes(pageType)) {
            errors.push(makeError('PAGE_TYPE_UNSUPPORTED', `${base}.type`, `Page type "${pageType}" is not available in this slice.`, `Use one of: ${SUPPORTED_PAGE_TYPES.join(', ')}.`));
        }
        if (!Array.isArray(definition.fields)) return;
        definition.fields.forEach((field, fieldIndex) => {
            if (!isPlainObject(field)) return;
            const fieldPath = `${base}.fields[${fieldIndex}]`;
            const type = field.type;
            if (typeof type === 'string' && !context.allowedFieldTypes.has(type)) {
                const substitute = hasOwn(FIELD_TYPE_SUBSTITUTES, type) ? `Use "${FIELD_TYPE_SUBSTITUTES[type]}" instead. ` : '';
                const reason = hasOwn(RUNTIME_BLOCKED_FIELD_TYPES, type) ? ` (${RUNTIME_BLOCKED_FIELD_TYPES[type]})` : '';
                errors.push(makeError('FIELD_TYPE_UNSUPPORTED', `${fieldPath}.type`, `Field type "${type}" is not supported in this slice${reason}.`, `${substitute}The field_types catalog section lists the supported types.`));
            }
            const notes = FIELD_TYPE_NOTES[type];
            if (OPTION_TYPES.has(type)) {
                if (!Array.isArray(field.options) || field.options.length === 0) {
                    errors.push(makeError('OPTIONS_REQUIRED', `${fieldPath}.options`, `Field type "${type}" needs options.`, 'Add options:[{value,label}].'));
                }
            } else if (hasOwn(field, 'options')) {
                errors.push(makeError('OPTIONS_NOT_ALLOWED', `${fieldPath}.options`, `Field type "${type}" does not take options.`, 'Use select, radio or multiselect for choices.'));
            }
            if (isPlainObject(field.validation) && notes) {
                for (const key of Object.keys(field.validation)) {
                    if (!notes.validation.includes(key)) {
                        errors.push(makeError('VALIDATION_KEY_UNSUPPORTED', `${fieldPath}.validation.${key}`, `validation.${key} does not apply to "${type}".`, notes.validation.length > 0 ? `Allowed: ${notes.validation.join(', ')}.` : 'This type takes no validation keys.'));
                    }
                }
            }
            if (typeof field.component === 'string') {
                const componentPath = `${fieldPath}.component`;
                if (!COMPONENT_NAME_PATTERN.test(field.component) || !hasOwn(byName, field.component)) {
                    errors.push(makeError('COMPONENT_UNKNOWN', componentPath, `Component "${field.component}" is not in the component catalog.`, 'Omit component; the field type selects the component.'));
                    return;
                }
                const component = context.catalog.components[byName[field.component]];
                const generator = component?.generator || {};
                if (generator.usable !== true) {
                    errors.push(makeError('COMPONENT_NOT_USABLE', componentPath, `Component "${field.component}" cannot be used by the generator.`, 'Omit component; the field type selects the component.'));
                    return;
                }
                const pageTypes = Array.isArray(generator.supported_page_types) ? generator.supported_page_types : [];
                const fieldTypes = Array.isArray(generator.supported_field_types) ? generator.supported_field_types : [];
                if (!pageTypes.includes(pageType) || !fieldTypes.includes(type)) {
                    errors.push(makeError('COMPONENT_TYPE_MISMATCH', componentPath, `Component "${field.component}" does not support field type "${type}" on a ${pageType} page.`, 'Omit component; the field type selects the component.'));
                }
            }
        });
    });
    return errors;
}

// ------------------------------------------------------------
// 第 7 層：切片規則
// ------------------------------------------------------------

function layerSlice(pages) {
    const errors = [];
    const warnings = [];
    const seenIds = new Map();
    pages.forEach((entry, index) => {
        const id = entry?.id;
        if (typeof id === 'string') {
            if (RESERVED_PAGE_IDS.has(id.toLowerCase())) {
                errors.push(makeError('PAGE_ID_RESERVED', `definitions.pages[${index}].id`, `Page id "${id}" is a reserved file name.`, 'Choose another page id.'));
            }
            if (seenIds.has(id)) {
                errors.push(makeError('PAGE_ID_DUPLICATE', `definitions.pages[${index}].id`, `Page id "${id}" is used more than once.`));
            } else {
                seenIds.set(id, index);
            }
        }
        const definition = entry?.definition;
        if (!isPlainObject(definition)) return;
        const base = pagePath(index);
        const type = definition.type;

        const api = isPlainObject(definition.api) ? definition.api : null;
        if (api) {
            for (const key of API_KEYS) {
                if (typeof api[key] === 'string' && !API_PATH_PATTERN.test(api[key])) {
                    errors.push(makeError('API_PATH_INVALID', `${base}.api.${key}`, `API path "${api[key]}" is not a base path.`, 'Use a base path such as /api/contacts; ids are appended automatically, so do not write {id} or :id.'));
                }
            }
            const distinct = new Set(API_KEYS.map(key => api[key]).filter(value => typeof value === 'string'));
            if (distinct.size > 1) {
                warnings.push(makeError('API_BASE_MISMATCH', `${base}.api`, 'API paths differ; the in-browser store keeps one collection per path.', 'Use the same base path for all operations of one resource.'));
            }
        }
        const requiredApi = { list: 'list', detail: 'get', form: 'create' }[type];
        if (requiredApi && (!api || typeof api[requiredApi] !== 'string')) {
            errors.push(makeError('API_REQUIRED', `${base}.api.${requiredApi}`, `A ${type} page needs api.${requiredApi}.`, 'Give every page of one resource the same base path, e.g. /api/contacts.'));
        }
        if (type === 'form' && api && typeof api.create === 'string' && typeof api.update !== 'string') {
            warnings.push(makeError('FORM_WITHOUT_UPDATE', `${base}.api`, 'The form can create records but cannot edit them.', 'Add api.update with the same base path to allow editing.'));
        }

        if (!Array.isArray(definition.fields)) return;
        const names = new Set();
        let listColumns = 0;
        definition.fields.forEach((field, fieldIndex) => {
            if (!isPlainObject(field) || typeof field.name !== 'string') return;
            const fieldPath = `${base}.fields[${fieldIndex}].name`;
            if (RESERVED_FIELD_NAMES.has(field.name)) {
                errors.push(makeError('FIELD_NAME_RESERVED', fieldPath, `Field name "${field.name}" is reserved.`, field.name === 'id' ? 'Record ids are assigned automatically; remove this field.' : 'Choose another field name.'));
            }
            if (names.has(field.name)) {
                errors.push(makeError('FIELD_NAME_DUPLICATE', fieldPath, `Field name "${field.name}" is used more than once on this page.`));
            }
            names.add(field.name);
            if (LIST_COLUMN_TYPES.has(field.type)) listColumns += 1;
            if (field.type === 'chained') {
                warnings.push(makeError('FIELD_TYPE_LIMITED', `${base}.fields[${fieldIndex}].type`, 'chained fields cannot be configured in this slice and render empty.', 'Prefer select.'));
            }
        });
        if (type === 'list' && listColumns === 0) {
            warnings.push(makeError('LIST_NO_COLUMNS', `${base}.fields`, 'None of the list fields can be shown as a table column.', `Table columns support: ${[...LIST_COLUMN_TYPES].join(', ')}.`));
        }
    });
    return { errors, warnings };
}

// ------------------------------------------------------------
// 請求層與主流程
// ------------------------------------------------------------

const REQUEST_KEYS = {
    validate: new Set(['template', 'page_ids', 'title']),
    build: new Set(['template', 'page_ids', 'title', 'out_dir'])
};

function validateRequestShape(request, command) {
    const errors = [];
    if (!isPlainObject(request)) {
        return [makeError('INPUT_INVALID', '', 'The input must be a JSON object.')];
    }
    for (const key of Object.keys(request)) {
        if (!REQUEST_KEYS[command].has(key)) {
            errors.push(makeError('INPUT_UNKNOWN_KEY', key, `Unknown input key "${key}".`, `Allowed keys: ${[...REQUEST_KEYS[command]].join(', ')}.`));
        }
    }
    if (!hasOwn(request, 'template') || !isPlainObject(request.template)) {
        errors.push(makeError('TEMPLATE_REQUIRED', 'template', 'template must be a DefinitionTemplate object.'));
    }
    if (hasOwn(request, 'page_ids') && request.page_ids !== null) {
        const ids = request.page_ids;
        if (!Array.isArray(ids) || ids.length === 0 || ids.length > LIMITS.maxPages
            || ids.some(id => typeof id !== 'string' || id.length > 64 || !PAGE_ID_PATTERN.test(id))) {
            errors.push(makeError('PAGE_IDS_INVALID', 'page_ids', `page_ids must be 1-${LIMITS.maxPages} page ids.`));
        } else if (new Set(ids).size !== ids.length) {
            errors.push(makeError('PAGE_IDS_DUPLICATE', 'page_ids', 'page_ids contains duplicates.'));
        }
    }
    if (hasOwn(request, 'title') && request.title !== null) {
        const title = request.title;
        if (typeof title !== 'string' || title.trim() === '' || title.length > LIMITS.maxTitleLength || CONTROL_PATTERN.test(title)) {
            errors.push(makeError('TITLE_INVALID', 'title', `title must be 1-${LIMITS.maxTitleLength} characters without control characters.`));
        }
    }
    return errors;
}

// 泛用錯誤（既有驗證器的文字訊息）所在路徑之下已有更具體的錯誤時省略，避免同一問題回報兩次；
// 路徑為根（''）的泛用錯誤在有任何具體錯誤時省略。結果仍為 ok:false。
function dropShadowedGenericErrors(errors) {
    const specific = errors.filter(error => !GENERIC_CODES.has(error.code));
    const covers = (genericPath, otherPath) => genericPath === ''
        || otherPath === genericPath
        || otherPath.startsWith(`${genericPath}.`)
        || otherPath.startsWith(`${genericPath}[`);
    return errors.filter(error => !GENERIC_CODES.has(error.code)
        || !specific.some(other => covers(error.path, other.path)));
}

function dedupe(errors) {
    const seen = new Set();
    return errors.filter((error) => {
        const key = `${error.code}\u0000${error.path}\u0000${error.message}`;
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
    });
}

function computeDigest(context, template, selectedIds, layerOneFailed) {
    let body;
    if (!layerOneFailed) {
        try {
            body = canonicalJson(template);
        } catch {
            body = null;
        }
    }
    if (body === null || body === undefined) {
        try {
            body = `unvalidated:${JSON.stringify(template)}`;
        } catch {
            body = 'unserialisable';
        }
    }
    return sha256Hex(canonicalJson({
        validator_version: VALIDATOR_VERSION,
        catalog_sha256: context?.catalogSha256 ?? null,
        matrix_sha256: context?.matrixSha256 ?? null,
        page_ids: selectedIds,
        template_canonical_sha256: sha256Hex(body)
    }));
}

/**
 * validate 指令的主體；build 先呼叫同一個函式。
 * @param {object} request - { template, page_ids?, title?, out_dir? }
 * @param {'validate'|'build'} command
 */
export async function validateRequest(request, command = 'validate') {
    const context = await loadValidationContext();
    const result = {
        ok: false,
        errors: [],
        warnings: [],
        pages: [],
        validation_digest: '',
        validator_version: VALIDATOR_VERSION
    };

    const requestErrors = validateRequestShape(request, command);
    const template = isPlainObject(request) ? request.template : undefined;
    const requestedIds = isPlainObject(request) && Array.isArray(request.page_ids) ? request.page_ids : null;

    if (!isPlainObject(template)) {
        result.errors = requestErrors;
        result.validation_digest = computeDigest(context, template ?? null, requestedIds, true);
        return result;
    }

    // 第 1 層
    const envelope = scanEnvelope(template);
    if (envelope.errors.length > 0) {
        result.errors = [...requestErrors, ...envelope.errors];
        result.validation_digest = computeDigest(context, template, requestedIds, true);
        return result;
    }

    const errors = [...requestErrors];
    const warnings = [];

    // 第 2 層
    checkSchema(TEMPLATE_SCHEMA, template, '', errors);

    const pages = Array.isArray(template.definitions?.pages) ? template.definitions.pages : [];
    const runLayer = async (fn) => {
        try {
            return await fn();
        } catch (error) {
            // 前面已有錯誤時，畸形輸入讓既有驗證器拋例外是預期內的；否則視為驗證器內部錯誤（fail-closed）
            if (errors.length > 0) return [];
            // 細節只寫到 stderr（診斷），不放進回給代理的結果
            process.stderr.write(`validation layer failed: ${error?.stack || error}\n`);
            return [makeError('VALIDATOR_INTERNAL', '', 'A validation layer failed unexpectedly; the definition was not accepted.')];
        }
    };

    errors.push(...await runLayer(() => layerStructure(context, template, pages)));   // 第 3 層
    errors.push(...await runLayer(() => layerPageGenRules(context, pages)));          // 第 4 層
    errors.push(...await runLayer(() => layerIdentifiers(context, pages)));           // 第 5 層
    errors.push(...await runLayer(() => layerCatalog(context, pages)));               // 第 6 層
    const slice = await runLayer(() => {
        const sliceResult = layerSlice(pages);
        warnings.push(...sliceResult.warnings);
        return sliceResult.errors;
    });
    errors.push(...slice);                                                            // 第 7 層

    // page_ids 必須存在於 template
    const knownIds = pages.map(entry => entry?.id).filter(id => typeof id === 'string');
    if (requestedIds && !requestErrors.some(error => error.path === 'page_ids')) {
        for (const id of requestedIds) {
            if (!knownIds.includes(id)) {
                errors.push(makeError('PAGE_ID_NOT_FOUND', 'page_ids', `Page id "${id}" is not in the template.`));
            }
        }
    }

    const selected = (requestedIds ? pages.filter(entry => requestedIds.includes(entry?.id)) : pages)
        .filter(entry => typeof entry?.id === 'string' && isPlainObject(entry?.definition));
    result.pages = selected.map(entry => ({
        id: entry.id,
        type: typeof entry.definition.type === 'string' ? entry.definition.type : null,
        field_count: Array.isArray(entry.definition.fields) ? entry.definition.fields.length : 0
    }));
    result.errors = dedupe(dropShadowedGenericErrors(errors));
    result.warnings = dedupe(warnings);
    result.ok = result.errors.length === 0;
    result.validation_digest = computeDigest(context, template, requestedIds, false);
    return result;
}
