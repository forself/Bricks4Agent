/**
 * 定義網站的純函式模型：路由解析、頁面之間的連結、列表欄位挑選。
 * 不碰 DOM，生成器與 node:test 也直接引用。
 */

export const SUPPORTED_PAGE_TYPES = Object.freeze(['list', 'detail', 'form']);
export const PAGE_ID_PATTERN = /^[a-z0-9][a-z0-9-]{0,63}$/;
export const RECORD_ID_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;

/** 列表表格只顯示能以純文字呈現的型別；其餘欄位仍保留在明細與表單 */
export const LIST_COLUMN_TYPES = new Set([
    'text', 'email', 'number', 'date', 'time', 'select', 'radio', 'checkbox', 'toggle', 'textarea'
]);
export const MAX_LIST_COLUMNS = 8;

const ENDPOINT_PRIORITY = {
    list: ['list', 'get', 'create', 'update', 'delete'],
    detail: ['get', 'list', 'update', 'create', 'delete'],
    form: ['create', 'update', 'get', 'list', 'delete']
};

/**
 * '#/page-id' 或 '#/page-id/record-id' → { pageId, recordId }；空字串回 { pageId: null }；格式不符回 null。
 */
export function parseRoute(hash) {
    let raw = typeof hash === 'string' ? hash : '';
    if (raw.startsWith('#')) raw = raw.slice(1);
    if (raw.startsWith('/')) raw = raw.slice(1);
    if (raw === '') return { pageId: null, recordId: null };

    const parts = raw.split('/');
    if (parts.length > 2) return null;
    const [pageId, encodedId] = parts;
    if (!PAGE_ID_PATTERN.test(pageId)) return null;

    let recordId = null;
    if (encodedId !== undefined && encodedId !== '') {
        try {
            recordId = decodeURIComponent(encodedId);
        } catch {
            return null;
        }
        if (!RECORD_ID_PATTERN.test(recordId)) return null;
    }
    return { pageId, recordId };
}

export function routeHref(pageId, recordId = null) {
    if (recordId === null || recordId === undefined || recordId === '') return `#/${pageId}`;
    return `#/${pageId}/${encodeURIComponent(String(recordId))}`;
}

/** 頁面代表的資源端點：同一端點的 list、detail、form 頁彼此連結 */
export function pageEndpoint(type, api) {
    if (!api || typeof api !== 'object') return null;
    for (const key of ENDPOINT_PRIORITY[type] || []) {
        const value = api[key];
        if (typeof value === 'string' && value !== '') return value;
    }
    return null;
}

export function resourceEndpoint(base, recordId) {
    return `${String(base).replace(/\/+$/, '')}/${encodeURIComponent(String(recordId))}`;
}

/**
 * @param {{ title?: string, pages: string[] }} site
 * @param {Record<string, object>} definitionsById - 原始頁定義（保留 api）
 */
export function buildSiteModel(site, definitionsById) {
    const pageIds = Array.isArray(site?.pages) ? site.pages : [];
    const pages = pageIds.map((id) => {
        const definition = definitionsById?.[id];
        if (!definition || typeof definition !== 'object') {
            throw new Error(`missing definition for page ${id}`);
        }
        const type = definition.type;
        if (!SUPPORTED_PAGE_TYPES.includes(type)) {
            throw new Error(`unsupported page type for page ${id}`);
        }
        return {
            id,
            type,
            title: definition.description || definition.name || id,
            definition,
            endpoint: pageEndpoint(type, definition.api),
            links: { list: null, detail: null, form: null }
        };
    });

    for (const page of pages) {
        if (!page.endpoint) continue;
        for (const type of SUPPORTED_PAGE_TYPES) {
            const target = pages.find(candidate => candidate.type === type && candidate.endpoint === page.endpoint);
            page.links[type] = target ? target.id : null;
        }
    }

    const firstList = pages.find(page => page.type === 'list');
    return {
        title: typeof site?.title === 'string' ? site.title : '',
        pages,
        byId: new Map(pages.map(page => [page.id, page])),
        defaultPageId: (firstList || pages[0])?.id ?? null
    };
}

const BOOLEAN_TYPES = new Set(['checkbox', 'toggle']);

/**
 * 依列表可顯示的型別重新指定 listOrder（回傳新陣列，不改動輸入）。
 * 是非欄位在表格中改以選項標籤呈現（列表渲染器對是非欄位回傳的 DOM 節點無法放進表格儲存格）。
 * @param {Array<{ fieldType?: string }>} fields - 新格式欄位
 * @param {{ yes?: string, no?: string }} [booleanLabels]
 */
export function applyListColumns(fields, booleanLabels = { yes: 'Yes', no: 'No' }) {
    let order = 0;
    return (Array.isArray(fields) ? fields : []).map((field) => {
        const visible = LIST_COLUMN_TYPES.has(field?.fieldType) && order < MAX_LIST_COLUMNS;
        if (visible) order += 1;
        const column = { ...field, listOrder: visible ? order : 0, isSearchable: false };
        if (visible && BOOLEAN_TYPES.has(field.fieldType)) {
            column.fieldType = 'select';
            column.optionsSource = {
                type: 'static',
                items: [
                    { value: true, label: booleanLabels.yes },
                    { value: false, label: booleanLabels.no },
                    { value: 'true', label: booleanLabels.yes },
                    { value: 'false', label: booleanLabels.no }
                ]
            };
        }
        return column;
    });
}
