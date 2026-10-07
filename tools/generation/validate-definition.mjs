// DefinitionTemplate 的分層驗證（fail-closed）。validate 與 build 共用這一份程式。
//   第 1 層 外殼與大小：位元組、深度、節點數、頁數、欄位數、危險鍵
//   第 2 層 嚴格鍵白名單與型別（等同 additionalProperties:false）
//   第 3 層 結構：tools/lib/definition-template.js 的 validateDefinitionTemplate
//   第 4 層 page-gen 規則：轉成新格式後跑 page-gen 的 validateNewDefinition
//   第 5 層 識別字：記憶體內呼叫 PageGenerator.generate()，補上 --validate 漏掉的檢查
//   第 6 層 型錄：欄位型別白名單（交集扣掉執行期無法使用的型別）、明示元件、頁型與 components 欄位
//   第 7 層 切片規則：api 基底路徑、頁 id 作檔名、保留名稱
// 錯誤格式 { code, path, message, hint }；path 以 template 為根，例如 definitions.pages[1].definition.fields[3].type
// 回給代理的錯誤有上限：相同的錯誤合併成一筆並註明出現次數與前幾個路徑（paths），合併後最多
// MAX_REPORTED_ISSUES 筆，截斷時每個錯誤代碼至少保留一筆（超過時 truncated:true，total_errors 為合併前的筆數），
// 讓一個系統性錯誤不會產生數百 KB 的回應。第 1 層另限制物件鍵長與收集的錯誤筆數，path 在建立時就截短，
// 讓單次驗證的工作量與輸入大小成正比。
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
    COMMON_TYPE_ALIASES,
    computeSliceFieldTypes,
    DEFAULT_KIND_DESCRIPTIONS,
    FIELD_TYPE_NOTES,
    FIELD_TYPE_SUBSTITUTES,
    OPTION_TYPES,
    RUNTIME_BLOCKED_FIELD_TYPES,
    supportsRequired
} from './field-types.mjs';
import {
    LIST_COLUMN_TYPES,
    PAGE_ID_PATTERN,
    SUPPORTED_PAGE_TYPES,
    pageEndpoint
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
    maxTitleLength: 120,
    // 物件鍵長上限：定義的鍵都是短名稱；超過即視為無效輸入並立即停止
    maxKeyLength: 128,
    // 第 1 層最多收集的錯誤筆數；達到時停止走訪並標記 truncated
    maxEnvelopeIssues: 200
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

export const MAX_REPORTED_ISSUES = 50;
const MAX_ISSUE_PATH_LENGTH = 300;
const MAX_ISSUE_MESSAGE_LENGTH = 400;
const MAX_ISSUE_HINT_LENGTH = 600;
const MAX_LISTED_UNKNOWN_KEYS = 10;
const MAX_LISTED_PATHS = 5;

function clip(text, max) {
    return typeof text === 'string' && text.length > max ? `${text.slice(0, max - 1)}…` : text;
}

// path、message、hint 在建立時就截短：之後的去重與合併都以截短後的值為鍵，大小有固定上限
function makeError(code, errorPath, message, hint = '') {
    return {
        code,
        path: clip(errorPath, MAX_ISSUE_PATH_LENGTH),
        message: clip(message, MAX_ISSUE_MESSAGE_LENGTH),
        hint: clip(hint, MAX_ISSUE_HINT_LENGTH)
    };
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
    // minItems 沒有任何型別開放（執行期不檢查列數）；保留在這裡，讓寫了它的定義得到具體的 hint
    minItems: S.integer({ min: 0, max: 1000 }),
    maxItems: S.integer({ min: 1, max: 1000 })
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
    // 收集到上限就停止走訪：結果已經是 ok:false，其餘錯誤標記為 truncated
    const add = (error) => {
        errors.push(error);
        return errors.length >= LIMITS.maxEnvelopeIssues;
    };
    const stopped = () => ({ errors, fatal: true, truncated: true });
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
            if (!Number.isFinite(value) && add(makeError('NON_JSON_VALUE', valuePath, 'Numbers must be finite.'))) return stopped();
            continue;
        }
        if (typeof value === 'string') {
            if (value.length > LIMITS.maxStringLength
                && add(makeError('STRING_TOO_LONG', valuePath, `Strings are limited to ${LIMITS.maxStringLength} characters.`))) {
                return stopped();
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
            if (add(makeError('NON_JSON_VALUE', valuePath, 'Only JSON values are accepted.'))) return stopped();
            continue;
        }
        const keys = Object.keys(value);
        for (let index = keys.length - 1; index >= 0; index -= 1) {
            const key = keys[index];
            if (key.length > LIMITS.maxKeyLength) {
                // 鍵名本身不放進錯誤，path 指向它所在的物件
                errors.push(makeError('KEY_TOO_LONG', valuePath, `An object key is longer than ${LIMITS.maxKeyLength} characters.`, 'Use the documented key names only.'));
                return { errors, fatal: true, truncated: false };
            }
            const childPath = valuePath ? `${valuePath}.${key}` : key;
            if (DANGEROUS_KEYS.has(key)) {
                if (add(makeError('DANGEROUS_KEY', childPath, `Key "${key}" is not allowed anywhere in a definition.`, 'Remove the key.'))) return stopped();
                continue;
            }
            const descriptor = Object.getOwnPropertyDescriptor(value, key);
            if (!descriptor || descriptor.get || descriptor.set) {
                if (add(makeError('NON_JSON_VALUE', childPath, 'Only JSON data properties are accepted.'))) return stopped();
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
    return { errors, fatal: errors.some(error => error.code === 'DANGEROUS_KEY' || error.code === 'NON_JSON_VALUE'), truncated: false };
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

// 第 5 層只看識別字：明示元件在第 6 層檢查；name 不是字串的欄位已由第 2 層回報（缺少或型別不符），
// 不再交給生成器，否則會多出一筆以 "undefined" 為名的識別字錯誤
function prepareForIdentifierCheck(definition) {
    const copy = { ...definition };
    delete copy.components;
    if (Array.isArray(copy.fields)) {
        copy.fields = copy.fields
            .filter(field => !isPlainObject(field) || typeof field.name === 'string')
            .map(field => {
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
        const definition = prepareForIdentifierCheck(entry.definition);
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

const DATE_DEFAULT_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;
const TIME_DEFAULT_PATTERN = /^([01]\d|2[0-3]):[0-5]\d$/;

function isCalendarDate(text) {
    const match = DATE_DEFAULT_PATTERN.exec(text);
    if (!match) return false;
    const [year, month, day] = match.slice(1).map(Number);
    const date = new Date(Date.UTC(year, month - 1, day));
    return date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
}

/**
 * 依型別檢查 default（FIELD_TYPE_NOTES[type].default）。執行期只正確處理這些形式，
 * 其他值會被忽略、對不上選項或存成型別不對的值。回傳錯誤或 null。
 */
function checkDefault(field, type, notes, defaultPath) {
    const kind = notes?.default ?? null;
    const value = field.default;
    if (kind === null) {
        return makeError('DEFAULT_NOT_ALLOWED', defaultPath, `default is not available for "${type}" fields.`, 'Remove default.');
    }
    const invalid = (detail) => makeError('DEFAULT_INVALID', defaultPath, `default for "${type}" must be ${DEFAULT_KIND_DESCRIPTIONS[kind]}${detail}.`, 'The field_types catalog section lists the default form for each type.');
    switch (kind) {
        case 'string': {
            if (typeof value !== 'string') return invalid('');
            const maxLength = isPlainObject(field.validation) ? field.validation.maxLength : undefined;
            if (Number.isInteger(maxLength) && value.length > maxLength) return invalid(` of at most ${maxLength} characters (validation.maxLength)`);
            return null;
        }
        case 'number': {
            if (typeof value !== 'number' || !Number.isFinite(value)) return invalid('');
            const limits = isPlainObject(field.validation) ? field.validation : {};
            if ((typeof limits.min === 'number' && value < limits.min) || (typeof limits.max === 'number' && value > limits.max)) {
                return invalid(' within validation.min and validation.max');
            }
            return null;
        }
        case 'boolean':
            return typeof value === 'boolean' ? null : invalid('');
        case 'option': {
            const options = Array.isArray(field.options) ? field.options : [];
            const matches = typeof value === 'string' && options.some(option => isPlainObject(option) && option.value === value);
            return matches ? null : invalid('; option values that are numbers cannot be preselected');
        }
        case 'date':
            return value === 'today' || (typeof value === 'string' && isCalendarDate(value)) ? null : invalid('');
        case 'time':
            return typeof value === 'string' && TIME_DEFAULT_PATTERN.test(value) ? null : invalid('');
        default:
            return invalid('');
    }
}

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
                const replacement = hasOwn(FIELD_TYPE_SUBSTITUTES, type)
                    ? FIELD_TYPE_SUBSTITUTES[type]
                    : hasOwn(COMMON_TYPE_ALIASES, type) ? COMMON_TYPE_ALIASES[type] : null;
                const substitute = replacement ? `Use "${replacement}" instead. ` : '';
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
            const typeOpen = typeof type === 'string' && context.allowedFieldTypes.has(type);
            if (typeOpen && field.required === true && !supportsRequired(type)) {
                errors.push(makeError('REQUIRED_NOT_SUPPORTED', `${fieldPath}.required`, `required is not available for "${type}" fields.`, 'The form cannot tell whether this kind of field is empty, so required would never be enforced; remove it.'));
            }
            if (typeOpen && hasOwn(field, 'default')) {
                const defaultError = checkDefault(field, type, notes, `${fieldPath}.default`);
                if (defaultError) errors.push(defaultError);
            }
            if (isPlainObject(field.validation) && notes) {
                for (const key of Object.keys(field.validation)) {
                    // 未知的鍵已由第 2 層以 UNKNOWN_KEY 回報，這裡只看已知的限制鍵是否適用於這個型別
                    if (!hasOwn(VALIDATION_SCHEMA.props, key)) continue;
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
    warnings.push(...checkResources(pages));
    return { errors, warnings };
}

function optionsSignature(options) {
    return JSON.stringify(options.map(option => (isPlainObject(option) ? [option.value, option.label] : option)));
}

/**
 * 跨頁一致性（只發 warning，不擋生成）。頁面之間的連結與外殼相同：以 pageEndpoint 算出的 api 路徑
 * 完全相等才算同一資源；列表與明細依欄位名讀取表單存下的值，記憶體 store 每個路徑各存一份集合。
 */
function checkResources(pages) {
    const warnings = [];
    const entries = pages
        .map((entry, index) => ({ entry, index, definition: entry?.definition }))
        .filter(({ definition }) => isPlainObject(definition) && SUPPORTED_PAGE_TYPES.includes(definition.type))
        .map(item => ({
            ...item,
            type: item.definition.type,
            endpoint: pageEndpoint(item.definition.type, isPlainObject(item.definition.api) ? item.definition.api : null),
            fields: Array.isArray(item.definition.fields) ? item.definition.fields.filter(field => isPlainObject(field) && typeof field.name === 'string') : []
        }))
        .filter(item => typeof item.endpoint === 'string');

    for (const page of entries) {
        const base = pagePath(page.index);
        const sameResource = entries.filter(other => other !== page && other.endpoint === page.endpoint);
        const forms = sameResource.filter(other => other.type === 'form');
        if (page.type === 'list' || page.type === 'detail') {
            if (forms.length === 0) {
                warnings.push(makeError('RESOURCE_WITHOUT_FORM', `${base}.api`, `No form page uses the api path "${page.endpoint}", so this ${page.type} page never has records to show.`, 'Give the form page of this resource the same api base path as its list and detail pages.'));
            } else {
                const formNames = new Set(forms.flatMap(form => form.fields.map(field => field.name)));
                page.fields.forEach((field) => {
                    if (!formNames.has(field.name)) {
                        const fieldIndex = page.definition.fields.indexOf(field);
                        warnings.push(makeError('FIELD_NOT_IN_FORM', `${base}.fields[${fieldIndex}].name`, `Field "${field.name}" is not a field of the form page for "${page.endpoint}", so it is always empty.`, 'List and detail pages read the values the form saved by field name; use the same field names on every page of a resource.'));
                    }
                });
            }
        }
        if (page.type === 'form' && !sameResource.some(other => other.type === 'list')) {
            warnings.push(makeError('FORM_WITHOUT_LIST', `${base}.api`, `No list page uses the api path "${page.endpoint}", so records saved by this form are not listed anywhere.`, 'Add a list page with the same api base path, or ignore this for a create-only form.'));
        }
        // 同一資源中同名欄位的選項要一致：列表與明細以自己定義中的選項把值換成標籤
        const firstWithOptions = new Map();
        for (const candidate of [page, ...sameResource].sort((a, b) => a.index - b.index)) {
            for (const field of candidate.fields) {
                if (!Array.isArray(field.options) || firstWithOptions.has(field.name)) continue;
                firstWithOptions.set(field.name, { page: candidate, signature: optionsSignature(field.options) });
            }
        }
        page.fields.forEach((field) => {
            if (!Array.isArray(field.options)) return;
            const first = firstWithOptions.get(field.name);
            if (first && first.page !== page && first.signature !== optionsSignature(field.options)) {
                const fieldIndex = page.definition.fields.indexOf(field);
                warnings.push(makeError('OPTIONS_MISMATCH', `${base}.fields[${fieldIndex}].options`, `Field "${field.name}" has different options than on page "${String(first.page.entry?.id ?? first.page.index)}" of the same resource.`, 'Use the same options for one field on every page of a resource; each page shows labels from its own options.'));
            }
        });
    }
    return warnings;
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

const UNKNOWN_KEY_MESSAGE = /^Unknown key "(.*)"\.$/s;

// 未知鍵以所在層級（hint 列出的允許鍵）合併，不論鍵名：允許鍵清單每一種只列一次；
// 識別字錯誤的訊息帶有欄位名，以代碼與 hint 合併；其他錯誤在代碼、訊息與 hint 都相同時合併。
const MERGE_BY_HINT = new Set(['UNKNOWN_KEY', 'IDENTIFIER_INVALID']);

function mergeKey(issue) {
    return MERGE_BY_HINT.has(issue.code)
        ? `${issue.code}\u0000${issue.hint}`
        : `${issue.code}\u0000${issue.message}\u0000${issue.hint}`;
}

// 截斷時先讓每個錯誤代碼保留第一筆，剩下的名額再依出現順序補滿；輸出維持原本的順序
function selectWithinLimit(items, limit) {
    if (items.length <= limit) return items;
    const chosen = new Set();
    const seenCodes = new Set();
    for (const item of items) {
        if (chosen.size >= limit) break;
        if (seenCodes.has(item.code)) continue;
        seenCodes.add(item.code);
        chosen.add(item);
    }
    for (const item of items) {
        if (chosen.size >= limit) break;
        chosen.add(item);
    }
    return items.filter(item => chosen.has(item));
}

/**
 * 合併重複的錯誤並設上限。保留第一次出現的順序；合併的項目在 message 註明出現次數，
 * 並以 paths 列出前幾個不同的路徑（path 仍是第一個）。
 * @returns {{ issues: object[], total: number, merged: boolean, truncated: boolean }}
 */
export function compactIssues(issues, limit = MAX_REPORTED_ISSUES) {
    const groups = new Map();
    for (const issue of issues) {
        const key = mergeKey(issue);
        let group = groups.get(key);
        if (!group) {
            group = { first: issue, count: 0, keys: new Set(), paths: [] };
            groups.set(key, group);
        }
        group.count += 1;
        if (group.paths.length < MAX_LISTED_PATHS && !group.paths.includes(issue.path)) group.paths.push(issue.path);
        if (issue.code === 'UNKNOWN_KEY') {
            const name = UNKNOWN_KEY_MESSAGE.exec(issue.message)?.[1];
            if (name !== undefined) group.keys.add(name);
        }
    }
    const merged = [...groups.values()].map(({ first, count, keys, paths }) => {
        let message = first.message;
        if (first.code === 'UNKNOWN_KEY' && keys.size > 1) {
            const listed = [...keys].slice(0, MAX_LISTED_UNKNOWN_KEYS).map(name => `"${clip(name, 40)}"`).join(', ');
            message = `Unknown keys ${listed}${keys.size > MAX_LISTED_UNKNOWN_KEYS ? ', ...' : ''}.`;
        }
        if (count > 1) {
            message += ` The same error occurs at ${count} paths; paths lists the first ${paths.length}.`;
        }
        const entry = {
            code: first.code,
            path: clip(first.path, MAX_ISSUE_PATH_LENGTH),
            message: clip(message, MAX_ISSUE_MESSAGE_LENGTH),
            hint: clip(first.hint, MAX_ISSUE_HINT_LENGTH)
        };
        if (count > 1) entry.paths = paths.map(item => clip(item, MAX_ISSUE_PATH_LENGTH));
        return entry;
    });
    return {
        issues: selectWithinLimit(merged, limit),
        total: issues.length,
        merged: merged.length !== issues.length,
        truncated: merged.length > limit
    };
}

// 把錯誤與警告放進結果：有合併或截斷時附上合併前的筆數（total_errors／total_warnings）與截斷旗標。
// collectionTruncated：第 1 層收集到上限後停止走訪，實際的錯誤比收集到的多。
function applyIssues(result, errors, warnings, collectionTruncated = false) {
    const compactErrors = compactIssues(errors);
    const compactWarnings = compactIssues(warnings);
    result.errors = compactErrors.issues;
    result.warnings = compactWarnings.issues;
    if (compactErrors.merged || compactErrors.truncated || collectionTruncated) {
        result.total_errors = compactErrors.total;
        result.truncated = compactErrors.truncated || collectionTruncated;
    }
    if (compactWarnings.merged || compactWarnings.truncated) {
        result.total_warnings = compactWarnings.total;
        result.warnings_truncated = compactWarnings.truncated;
    }
    result.ok = errors.length === 0;
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
        applyIssues(result, requestErrors, []);
        result.ok = false;
        result.validation_digest = computeDigest(context, template ?? null, requestedIds, true);
        return result;
    }

    // 第 1 層
    const envelope = scanEnvelope(template);
    if (envelope.errors.length > 0) {
        applyIssues(result, dedupe([...requestErrors, ...envelope.errors]), [], envelope.truncated);
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
    applyIssues(result, dedupe(dropShadowedGenericErrors(errors)), dedupe(warnings));
    result.validation_digest = computeDigest(context, template, requestedIds, false);
    return result;
}
