// 欄位型別白名單：page-gen 的型別清單與 generator-support-matrix（排除 out_of_catalog）的交集，
// 以程式計算，不另抄一份清單。本切片再扣掉瀏覽器實測無法在無後端原型中使用的型別
// （RUNTIME_BLOCKED_FIELD_TYPES）。說明文字與各型別可用的屬性是本生成器自己的通用說明表。
import { createRequire } from 'node:module';
import { MATRIX_PATH, PAGE_GEN_PATH } from './paths.mjs';
import { hasOwn, readJsonFile } from './json-util.mjs';
import { LIST_COLUMN_TYPES } from '../../templates/definition-site/site-model.js';

const require = createRequire(import.meta.url);

export function loadPageGenFieldTypes() {
    const pageGen = require(PAGE_GEN_PATH);
    if (!Array.isArray(pageGen.VALID_FIELD_TYPES)) {
        throw new Error('page-gen field type list is unavailable');
    }
    return [...pageGen.VALID_FIELD_TYPES];
}

/**
 * @returns {{ allowed: string[], excluded: { notInMatrix: string[], outOfCatalog: string[], notInPageGen: string[] } }}
 */
export function computeFieldTypeWhitelist(matrix = readJsonFile(MATRIX_PATH), pageGenTypes = loadPageGenFieldTypes()) {
    const support = matrix?.field_type_support;
    if (!support || typeof support !== 'object') {
        throw new Error('generator-support-matrix.json has no field_type_support');
    }
    const pageGenSet = new Set(pageGenTypes);
    const allowed = [];
    const notInMatrix = [];
    const outOfCatalog = [];
    for (const type of pageGenTypes) {
        if (!hasOwn(support, type)) {
            notInMatrix.push(type);
        } else if (support[type]?.status === 'out_of_catalog') {
            outOfCatalog.push(type);
        } else {
            allowed.push(type);
        }
    }
    const notInPageGen = Object.keys(support).filter(type => !pageGenSet.has(type));
    return { allowed, excluded: { notInMatrix, outOfCatalog, notInPageGen } };
}

/**
 * 交集內、但目前的執行期渲染路徑（DynamicPageRenderer + FieldResolver）在無後端原型中無法使用的型別。
 * 由 tools/scripts/definition-site-smoke.mjs 在瀏覽器逐一實測得出；修正對應元件或提供資料來源後再移除。
 * datetime：輸入元件的值是 {date,time} 物件，明細頁顯示與再編輯的往返都還不支援。
 * file：上傳元件沒有 getValue，表單不會收集也不會保存它的值。
 */
export const RUNTIME_BLOCKED_FIELD_TYPES = Object.freeze({
    richtext: 'the field renderer cannot mount the rich text editor yet',
    canvas: 'the field renderer cannot mount the drawing board yet',
    image: 'the field renderer cannot mount the image viewer yet',
    address: 'needs a region data loader that a backend-less prototype does not have',
    addresslist: 'needs a region data loader that a backend-less prototype does not have',
    organization: 'needs an organisation unit loader that a backend-less prototype does not have',
    datetime: 'the detail page and re-editing cannot show the combined date and time value yet',
    file: 'the prototype form does not collect or keep file values'
});

/**
 * 本切片實際開放的型別：交集扣掉執行期無法使用的型別。
 * @returns {{ allowed: string[], blocked: string[], intersection: ReturnType<typeof computeFieldTypeWhitelist> }}
 */
export function computeSliceFieldTypes(matrix = readJsonFile(MATRIX_PATH), pageGenTypes = loadPageGenFieldTypes()) {
    const intersection = computeFieldTypeWhitelist(matrix, pageGenTypes);
    const blocked = intersection.allowed.filter(type => hasOwn(RUNTIME_BLOCKED_FIELD_TYPES, type));
    return {
        allowed: intersection.allowed.filter(type => !hasOwn(RUNTIME_BLOCKED_FIELD_TYPES, type)),
        blocked,
        intersection
    };
}

/** 不開放型別的替代建議（catalog 與驗證錯誤的 hint 共用） */
export const FIELD_TYPE_SUBSTITUTES = Object.freeze({
    tel: 'text',
    url: 'text',
    rating: 'select',
    tags: 'multiselect',
    slider: 'number',
    memo: 'textarea',
    plaintext: 'textarea',
    rocDate: 'date',
    geolocation: 'text',
    weather: 'text',
    richtext: 'textarea',
    canvas: 'textarea',
    image: 'text',
    address: 'textarea',
    addresslist: 'textarea',
    organization: 'select',
    datetime: 'date',
    file: 'text'
});

/**
 * 常見的程式語言型別名稱（不是欄位型別）對應的欄位型別：模型常寫成 string、boolean、integer，
 * 被拒時的 hint 直接給出替代，不必再查一次型錄。
 */
export const COMMON_TYPE_ALIASES = Object.freeze({
    string: 'text',
    boolean: 'checkbox',
    bool: 'checkbox',
    integer: 'number',
    int: 'number',
    decimal: 'number',
    float: 'number'
});

const TEXT_LIMITS = ['maxLength'];
const NUMBER_LIMITS = ['min', 'max'];
const ITEM_LIMITS = ['minItems', 'maxItems'];

/**
 * 通用說明表。options 表示必須提供 options:[{value,label}]；validation 為可用的限制鍵。
 * 白名單內每個型別都必須在此有一筆（測試鎖定）。
 */
export const FIELD_TYPE_NOTES = {
    text: { note: 'single-line text', validation: TEXT_LIMITS },
    email: { note: 'e-mail address text', validation: TEXT_LIMITS },
    password: { note: 'masked text; never shown in list or detail', validation: TEXT_LIMITS },
    number: { note: 'numeric input', validation: NUMBER_LIMITS },
    textarea: { note: 'multi-line plain text', validation: TEXT_LIMITS },
    date: { note: 'date picker', validation: [] },
    time: { note: 'time picker', validation: [] },
    datetime: { note: 'date plus time input', validation: [] },
    select: { note: 'single choice dropdown', options: true, validation: [] },
    multiselect: { note: 'multiple choice dropdown', options: true, validation: [] },
    checkbox: { note: 'yes/no checkbox', validation: [] },
    toggle: { note: 'on/off switch', validation: [] },
    radio: { note: 'single choice radio group', options: true, validation: [] },
    richtext: { note: 'rich text editor; output is sanitized', validation: [] },
    canvas: { note: 'free drawing board', validation: [] },
    color: { note: 'colour picker', validation: [] },
    image: { note: 'image viewer', validation: [] },
    file: { note: 'file picker; the prototype form does not keep file values', validation: ITEM_LIMITS },
    address: { note: 'postal address with region selectors', validation: [] },
    addresslist: { note: 'repeatable postal addresses', validation: ITEM_LIMITS },
    chained: { note: 'dependent selects; not configurable in this slice, prefer select', validation: [] },
    list: { note: 'repeatable short text rows', validation: ITEM_LIMITS },
    personinfo: { note: 'repeatable person entries', validation: ITEM_LIMITS },
    phonelist: { note: 'repeatable phone numbers', validation: ITEM_LIMITS },
    socialmedia: { note: 'repeatable social media accounts', validation: ITEM_LIMITS },
    organization: { note: 'organisation unit selector', validation: [] },
    student: { note: 'student identity input', validation: [] },
    hidden: { note: 'hidden value; not displayed', validation: [] }
};

export const OPTION_TYPES = new Set(
    Object.entries(FIELD_TYPE_NOTES).filter(([, info]) => info.options).map(([type]) => type)
);

export function describeFieldTypes(matrix = readJsonFile(MATRIX_PATH), allowedTypes = computeSliceFieldTypes(matrix).allowed) {
    const support = matrix.field_type_support;
    return allowedTypes.map(type => {
        const info = FIELD_TYPE_NOTES[type] || { note: '', validation: [] };
        const entry = {
            type,
            component: support[type]?.default_component ?? null,
            note: info.note
        };
        if (info.options) entry.requires = 'options:[{value,label}] (1-100 items)';
        if (info.validation.length > 0) entry.validation = [...info.validation];
        entry.in_list = LIST_COLUMN_TYPES.has(type);
        return entry;
    });
}
