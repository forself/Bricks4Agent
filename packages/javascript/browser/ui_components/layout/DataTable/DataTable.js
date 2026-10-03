/**
 * DataTable - 通用資料表格元件
 *
 * 支援排序、分頁、行選擇（checkbox）、自訂工具列、自訂渲染、主題
 *
 * 支援三種 columns 格式：
 *   1. 標準格式：[{name, label, options: {customBodyRender, display, setCellProps}}]
 *   2. Audit 格式：[{key, title, render, hidden, html, width, sortable}]（data 為物件陣列）
 *   3. Search 格式：[{title, visible, width}]（data 為 2D 陣列）
 *
 * 使用方式：
 *   // 方式 1：單參數物件
 *   const dt = new DataTable({
 *       container: document.querySelector('.table-area'),
 *       title: '搜尋結果',
 *       data: [[1, '名稱', '值'], ...],
 *       columns: [{ name: 'ID' }, { name: '名稱', options: { customBodyRender: (val) => `<a>${val}</a>` } }],
 *       options: { selectableRows: 'multiple' }
 *   });
 *
 *   // 方式 2：兩參數（container, config）
 *   const dt = new DataTable(container, { columns, data, pageSize: 20 });
 *
 *   // 方式 3：不帶 container，稍後 mount
 *   const dt = new DataTable({ columns, data });
 *   dt.mount(document.querySelector('.table-area'));
 *
 *   dt.setData(newData);
 *
 * 選用進階功能（全部預設關閉，未啟用時渲染與行為與既有版本完全相同）：
 *   serverSide + dataSource(query)   伺服器端分頁/排序/搜尋（含載入中、錯誤重試、過期回應忽略）
 *   stickyHeader + maxHeight          表頭固定、表身在元件內捲動
 *   欄位 sticky: 'left' | 'right'     左右固定欄
 *   columnToggle                      工具列「欄位」選單切換欄位顯示
 *   expandable                        列展開明細
 *   rowKey                            依 key 跨頁/跨排序/跨搜尋保留選取
 *
 * @module DataTable
 */

import { escapeHtml, isRawHtml, raw } from '../../utils/security.js';
import { Link } from '../../common/Link/index.js';
import { Badge } from '../../common/Badge/index.js';
import { LoadingSpinner } from '../../common/LoadingSpinner/index.js';
import { nextUid } from '../../utils/uid.js';

import Locale from '../../i18n/index.js';

// CSP（style-src 'self'）合規：視覺樣式集中於同目錄 DataTable.css，
// 建構時自動注入同源 <link>（固定 id 去重）；動態樣式一律走 CSSOM（el.style）。
const STYLE_LINK_ID = 'b4a-datatable-styles';

function ensureStyleSheet() {
    if (document.getElementById(STYLE_LINK_ID)) return;
    const link = document.createElement('link');
    link.id = STYLE_LINK_ID;
    link.rel = 'stylesheet';
    link.href = new URL('./DataTable.css', import.meta.url).href;
    document.head.appendChild(link);
}

// 主題定義（使用 CSS 變數，支援深色主題）
// 注意：樣式實作已移至 DataTable.css 的 .b4a-dt--default / .b4a-dt--search，
// 此表保留供 variant 驗證與既有程式讀取 _theme 的相容用途，兩處需同步維護。
const THEMES = {
    default: {
        headerBg: 'var(--cl-light-green)', headerFontSize: '1.10rem',
        cellFontSize: '1.00rem', oddRowBg: 'var(--cl-bg-secondary)', evenRowBg: 'var(--cl-bg-tertiary)',
        hoverBg: 'var(--cl-bg-hover)', selectedBg: 'var(--cl-bg-active)', rowHeight: 'auto',
        headerHeight: 'auto', wordBreak: 'normal',
    },
    search: {
        headerBg: 'var(--cl-light-green)', headerFontSize: '.90rem',
        cellFontSize: 'inherit', oddRowBg: 'var(--cl-bg-secondary)', evenRowBg: 'var(--cl-bg-tertiary)',
        hoverBg: 'var(--cl-bg-hover)', selectedBg: 'var(--cl-bg-active)', rowHeight: '40px',
        headerHeight: '50px', wordBreak: 'break-word',
    },
};

/**
 * 進階功能選項預設值（文件紀錄來源）。全部預設關閉；可寫在 config 頂層或 config.options 內
 * （與 selectableRows / search 相同，頂層值只在 options 未指定時生效）。執行期一律即時讀取 this.options。
 */
const ADVANCED_DEFAULTS = Object.freeze({
    serverSide: false,              // true：分頁、排序、快速搜尋交給 dataSource(query) 在伺服器端處理
    dataSource: null,               // async (query) => ({ rows, total })；query 見 getQuery() 另含 signal
    searchDebounce: 300,            // 伺服器端模式快速搜尋輸入的防抖毫秒數
    onQueryChange: null,            // (query) => void；伺服器端模式每次送出查詢前呼叫
    stickyHeader: false,            // true：表頭固定，表身在元件內捲動
    maxHeight: null,                // 捲動區最大高度（CSS 長度或 px 數字）；stickyHeader 未指定時用 60vh
    columnToggle: false,            // true：工具列顯示「欄位」選單，切換欄位顯示
    onColumnVisibilityChange: null, // (visibility) => void；使用者由選單切換欄位後呼叫
    expandable: null,               // { render(row, index), rowExpandable?(row, index), expandOnRowClick?: false }
    rowKey: null,                   // 欄位名稱字串或 (row, index) => key；啟用後選取依 key 跨頁保留
    selectAllScope: 'page',         // rowKey 模式表頭全選範圍：'page'（目前頁）| 'filtered'（快速篩選後全部）
});
const ADVANCED_OPTION_KEYS = Object.keys(ADVANCED_DEFAULTS);
const DEFAULT_STICKY_MAX_HEIGHT = '60vh';
// 系統欄（展開鈕、勾選框）無法量測時的後備寬度（px），與 DataTable.css 的宣告寬度一致
const EXPAND_COLUMN_FALLBACK_WIDTH = 40;
const SELECT_COLUMN_FALLBACK_WIDTH = 48;
// 伺服器端模式非同步重繪後可還原焦點的控制項（僅限元件自己產生的 data-action）
const FOCUS_RESTORABLE_ACTIONS = new Set([
    'quick-search', 'quick-search-submit', 'rows-per-page', 'retry', 'column-toggle',
    'page-first', 'page-prev', 'page-next', 'page-last', 'select-all', 'select-row', 'toggle-expand',
]);
const PAGE_FOCUS_FALLBACK = Object.freeze({
    'page-next': 'page-prev', 'page-last': 'page-first', 'page-prev': 'page-next', 'page-first': 'page-last',
});
// expandOnRowClick 時，點在這些互動元素上不切換展開
const ROW_CLICK_IGNORE_SELECTOR = 'a, button, input, select, textarea, label, summary, [contenteditable=""], [contenteditable="true"], [role="button"], [role="link"], [role="checkbox"], [data-action]';

function parsePixelLength(value) {
    if (typeof value === 'number') return Number.isFinite(value) && value > 0 ? value : 0;
    const match = /^\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*$/i.exec(String(value ?? ''));
    return match ? Number(match[1]) : 0;
}

function isDomNode(value) {
    return value !== null && typeof value === 'object' && typeof value.nodeType === 'number';
}

const DEFAULT_TEXT_LABELS = {
    pagination: { rowsPerPage: Locale.t('dataTable.rowsPerPage'), displayRows: Locale.t('dataTable.displayRows') },
    body: { noMatch: Locale.t('dataTable.noMatch') },
    selectedRows: { text: Locale.t('dataTable.selectedUnit') },
    search: {
        placeholder: Locale.t('dataTable.searchPlaceholder'),
        resultCount: Locale.t('dataTable.searchResultCount'),
        buttonLabel: Locale.t('dataTable.searchButtonLabel'),
    },
};

export function linkCell(text, href, options = {}) {
    const scope = options.external === false ? 'internal' : 'external';
    const className = String(options.className || '').replace(/[^A-Za-z0-9_-]/g, ' ');
    return raw(`<span data-b4a-link-cell="" data-link-text="${escapeHtml(String(text ?? ''))}" data-link-href="${escapeHtml(String(href ?? ''))}" data-link-scope="${scope}" data-link-class="${escapeHtml(className)}"></span>`);
}

export function badgeCell(text, options = {}) {
    const variant = Object.values(Badge.VARIANTS).includes(options.variant) ? options.variant : Badge.VARIANTS.DEFAULT;
    const className = String(options.className || '').replace(/[^A-Za-z0-9_-]/g, ' ');
    return raw(`<span data-b4a-badge-cell="" data-badge-text="${escapeHtml(String(text ?? ''))}" data-badge-variant="${variant}" data-badge-class="${escapeHtml(className)}"></span>`);
}

function normalizeSearchText(value) {
    return String(value ?? '')
        .normalize('NFKC')
        .trim()
        .toLocaleLowerCase('zh-Hant');
}

function renderedSearchText(value) {
    if (value == null) return '';
    if (isRawHtml(value)) {
        const template = document.createElement('template');
        template.innerHTML = value.__html;
        const visibleText = template.content.textContent?.trim();
        if (visibleText) return visibleText;

        // Some controlled cells are hydrated after the table HTML is mounted.
        // Their user-facing labels are carried in data attributes until then.
        return Array.from(template.content.querySelectorAll(
            '[data-link-text],[data-link-label],[data-badge-text]'
        )).map(node => node.dataset.linkText
            || node.dataset.linkLabel
            || node.dataset.badgeText
            || '').join(' ');
    }
    if (value && typeof value === 'object' && value.nodeType) {
        return value.textContent || '';
    }
    return String(value);
}

export class DataTable {
    /**
     * @param {Object|HTMLElement} configOrContainer - 設定物件，或容器元素（兩參數模式）
     * @param {Object} [legacyConfig] - 兩參數模式的設定物件
     */
    constructor(configOrContainer = {}, legacyConfig) {
        let config;

        // 偵測兩參數呼叫：第一個參數是 HTMLElement
        if (configOrContainer instanceof HTMLElement || (configOrContainer && configOrContainer.nodeType === 1)) {
            config = { container: configOrContainer, ...(legacyConfig || {}) };
        } else {
            config = configOrContainer;
        }

        // 偵測並轉換簡化 columns 格式
        const { columns: rawCols, data: rawData } = this._normalizeColumnsAndData(config.columns || [], config.data || []);

        this.container = config.container || null;
        this.title = config.title || '';
        this.data = rawData;
        this.columns = rawCols;
        this.options = config.options || {};
        this.variant = config.variant || 'default';

        // 相容兩種呼叫方式：從 top-level config 合併到 options
        for (const key of ['selectableRows', 'customToolbar', 'customToolbarSelect', 'rowsPerPageOptions', 'sortOrder', 'search', ...ADVANCED_OPTION_KEYS]) {
            if (config[key] !== undefined && this.options[key] === undefined) {
                this.options[key] = config[key];
            }
        }

        // 相容 pageSize / pageSizeOptions
        if (config.pageSize || config.pageSizeOptions) {
            if (!this.options.rowsPerPageOptions && config.pageSizeOptions) {
                this.options.rowsPerPageOptions = config.pageSizeOptions;
            }
        }

        // 支援 pagination: false 停用分頁
        if (config.pagination === false) {
            this._paginationEnabled = false;
        } else {
            this._paginationEnabled = true;
        }

        // 支援 emptyText
        if (config.emptyText) {
            if (!this.options.textLabels) this.options.textLabels = {};
            if (!this.options.textLabels.body) this.options.textLabels.body = {};
            this.options.textLabels.body.noMatch = config.emptyText;
        }

        // 支援 striped / hoverable（CL 版介面相容）
        if (config.striped !== undefined) this._striped = config.striped;
        if (config.hoverable !== undefined) this._hoverable = config.hoverable;

        this._theme = THEMES[this.variant] || THEMES.default;
        this._page = 0;
        const defaultPageSize = config.pageSize || (this.options.rowsPerPageOptions || [10, 20, 100, 500, 1000])[0] || 10;
        this._rowsPerPage = defaultPageSize;
        this._sortCol = null;
        this._sortDir = null;
        this._selectedRows = [];
        this._hoveredRow = null;
        this._cellComponents = [];
        this._sortedCache = null;
        this._searchText = '';
        this._searchDraft = '';
        this._searchIndex = null;
        this._quickSearchComposing = false;

        // ── 進階功能狀態（未啟用對應選項時完全不參與渲染） ──
        // 呼叫端傳入的原始列（物件或陣列），與 this.data 同索引；rowKey / expandable 取完整列時使用
        this._sourceRows = Array.isArray(config.data) ? config.data : this.data;
        this._destroyed = false;
        this._uid = null;                   // 需要 DOM id 時才向 nextUid 取號，預設路徑不影響其他元件的 id
        this._columnVisibility = new Map(); // 欄位 key → 使用者/程式覆寫的顯示狀態
        this._rowKeyIds = null;             // rowKey 模式：dataIndex → 正規化 key 字串
        this._rowKeyValues = null;          // rowKey 模式：dataIndex → 原始 key 值
        this._rowKeyIndex = null;           // rowKey 模式：正規化 key → dataIndex
        this._selectedKeyMap = new Map();   // rowKey 模式：正規化 key → { key, row }
        this._expandedKeys = new Map();     // 正規化 key → 原始 key
        this._serverTotal = this.data.length;
        this._serverLoaded = false;
        this._loading = false;
        this._loadError = null;
        this._requestSeq = 0;
        this._abortController = null;
        this._queuedLoadToken = null;
        this._searchTimer = null;
        this._renderDeferredByComposition = false;
        this._stickyLayout = null;
        this._resizeObserver = null;
        this._observedTable = null;
        this._columnMenu = null;
        this._columnMenuTrigger = null;
        this._columnMenuHandlers = null;
        this._pendingFocus = null;
        this._missingKeyWarned = false;
        this._rebuildRowKeys();

        // 合併 textLabels
        const tl = this.options.textLabels || {};
        this._textLabels = {
            pagination: { ...DEFAULT_TEXT_LABELS.pagination, ...(tl.pagination) },
            body: { ...DEFAULT_TEXT_LABELS.body, ...(tl.body) },
            selectedRows: { ...DEFAULT_TEXT_LABELS.selectedRows, ...(tl.selectedRows) },
            search: { ...DEFAULT_TEXT_LABELS.search, ...(tl.search) },
        };

        // 初始排序
        if (this.options.sortOrder) {
            const colIdx = this.columns.findIndex(c => c.name === this.options.sortOrder.name);
            if (colIdx >= 0) {
                this._sortCol = colIdx;
                this._sortDir = this.options.sortOrder.direction || 'asc';
            }
        }

        // 注入元件樣式表（CSP 合規：同源 <link> 樣式表）
        ensureStyleSheet();

        // 建立 element（不帶 container 時也能使用）
        this.element = document.createElement('div');

        // 伺服器端模式：首次查詢延到建構完成後的 microtask 才送出（呼叫端的 dataSource /
        // onQueryChange 可安全引用剛建立的實例）；首次渲染先呈現載入中狀態。
        const serverSide = this._isServerSide();
        if (serverSide) this._loading = typeof this._opt('dataSource') === 'function';

        if (this.container) {
            this.render();
        } else if (this.data.length > 0 || this.columns.length > 0) {
            // 無 container 但有資料：渲染到 element
            this._renderToElement();
        }

        if (serverSide) this._queueServerLoad();
    }

    /**
     * 正規化 columns 和 data 格式
     */
    _normalizeColumnsAndData(columns, data) {
        if (!columns || columns.length === 0) return { columns, data };

        const first = columns[0];

        // 已是標準格式（有 name 屬性）→ 不需要轉換
        if (first.name !== undefined) return { columns, data };

        // Audit 格式：有 key 屬性 → 物件陣列 data 需轉為 2D 陣列
        if (first.key !== undefined) {
            const keys = columns.map(c => c.key);
            const newCols = columns.map(col => {
                const opts = {};
                if (col.hidden) opts.display = false;
                if (col.width) opts.setCellProps = () => ({ style: { width: col.width } });
                if (col.sortable === false) opts.sort = false;
                if (col.searchable === false || col.action) opts.searchable = false;
                if (col.sticky === 'left' || col.sticky === 'right') opts.sticky = col.sticky;
                if (col.hideable === false) opts.hideable = false;
                if (col.render) {
                    const renderFn = col.render;
                    const colKeys = keys;
                    opts.customBodyRender = (value, tableMeta) => {
                        const rowObj = {};
                        colKeys.forEach((k, i) => { rowObj[k] = tableMeta.rowData[i]; });
                        return renderFn(value, rowObj);
                    };
                }
                if (col.html) {
                    // ⚠️ 安全提醒:col.html:true 讓此欄的儲存格值以 raw() 標記,
                    // 繞過預設的 escapeHtml 直接寫入 innerHTML(見下方 _renderCell 的 isRawHtml 分支)。
                    // 僅可對「可信/已清洗」的欄位啟用;若值可能來自使用者或 API,
                    // 請改用 col.render 回傳 escapeHtml() 後的字串,或先經 sanitizeHTML。
                    if (!opts.customBodyRender) {
                        opts.customBodyRender = (value) => raw(value == null ? '' : String(value));
                    }
                }
                return {
                    name: col.key,
                    label: col.title || col.key,
                    options: Object.keys(opts).length > 0 ? opts : undefined
                };
            });
            const newData = Array.isArray(data) ? data.map(row => {
                if (Array.isArray(row)) return row;
                return keys.map(k => row[k] !== undefined ? row[k] : '');
            }) : [];
            return { columns: newCols, data: newData };
        }

        // Search 格式：有 title 但沒有 key 和 name
        if (first.title !== undefined) {
            const newCols = columns.map((col, i) => {
                const opts = {};
                if (col.visible === false) opts.display = false;
                if (col.hidden) opts.display = false;
                if (col.width) opts.setCellProps = () => ({ style: { width: col.width } });
                if (col.searchable === false || col.action) opts.searchable = false;
                if (col.sticky === 'left' || col.sticky === 'right') opts.sticky = col.sticky;
                if (col.hideable === false) opts.hideable = false;
                if (col.render) {
                    opts.customBodyRender = col.render;
                } else {
                    opts.customBodyRender = (value) => value == null ? '' : String(value);
                }
                return {
                    name: `col_${i}`,
                    label: col.title || '',
                    options: opts
                };
            });
            return { columns: newCols, data };
        }

        return { columns, data };
    }

    /**
     * 更新資料並重新渲染
     *
     * 伺服器端模式：rows 視為「目前查詢那一頁」的資料，total 為符合查詢的總筆數（省略時取 rows.length）；
     * 不會回到第一頁，並會取消尚未回來的 dataSource 請求（以呼叫端推入的資料為準）。
     */
    setData(data, total) {
        this._assignData(data);
        this._searchIndex = null;
        if (this._isServerSide()) {
            this._cancelServerRequest();
            this._loadError = null;
            this._serverLoaded = true;
            const count = Number(total);
            this._serverTotal = total !== undefined && Number.isFinite(count) && count >= 0
                ? Math.floor(count)
                : this.data.length;
        } else {
            this._page = 0;
        }
        if (this._hasRowKey()) {
            this._syncSelectedRowsFromKeys();
        } else {
            this._selectedRows = [];
            this._expandedKeys.clear();
        }
        if (this.container) {
            this.render();
        } else {
            this._renderToElement();
        }
        return this;
    }

    /**
     * 掛載到容器（CL mount 慣例）
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) {
            // destroy() 之後再次 mount 視為重新啟用（與既有行為一致：仍會重新渲染）；
            // 伺服器端模式若首次查詢在 destroy 時被中止，重新送出
            const reload = this._destroyed && this._isServerSide() && !this._serverLoaded;
            this._destroyed = false;
            if (reload) this._loading = typeof this._opt('dataSource') === 'function';
            this.container = target;
            this.render();
            if (reload) this._queueServerLoad();
        }
        return this;
    }

    /**
     * 銷毀表格
     */
    destroy() {
        this._destroyed = true;
        this._cancelServerRequest();
        this._clearSearchTimer();
        this._closeColumnMenu(false);
        this._disconnectStickyObserver();
        this._stickyLayout = null;
        this._cellComponents.splice(0).forEach(component => component.destroy?.());
        if (this.container) {
            this.container.innerHTML = '';
        }
        if (this.element) {
            this.element.innerHTML = '';
        }
    }

    /**
     * 取得選取列。未設定 rowKey：回傳 dataIndex 陣列（既有行為）；
     * 設定 rowKey：回傳列物件陣列（含已不在目前頁面／已重新載入的列，依選取先後排序）。
     */
    getSelectedRows() {
        if (this._hasRowKey()) {
            const rows = [];
            this._selectedKeyMap.forEach(entry => {
                if (entry.row !== undefined) rows.push(entry.row);
            });
            return rows;
        }
        return [...this._selectedRows];
    }

    setSelectedRows(indices) {
        if (this._hasRowKey()) {
            // rowKey 模式：indices 仍是目前已載入資料的 dataIndex，轉成 key 後取代整個選取
            this._selectedKeyMap.clear();
            this._setKeyedSelection(indices || [], true);
            this.render();
            return;
        }
        this._selectedRows = indices || [];
        this.render();
    }

    /**
     * 取得選取列的 key。設定 rowKey 時回傳 key 陣列（跨頁），未設定時回傳 dataIndex 陣列。
     */
    getSelectedKeys() {
        if (this._hasRowKey()) return [...this._selectedKeyMap.values()].map(entry => entry.key);
        return [...this._selectedRows];
    }

    /**
     * 以 key 取代目前選取（不觸發 onRowSelectionChange）。尚未載入的 key 也會保留，
     * 該列載入後自動勾選；未設定 rowKey 時 keys 視為 dataIndex。
     */
    setSelectedKeys(keys) {
        const list = Array.isArray(keys) ? keys : [];
        if (!this._hasRowKey()) {
            this._selectedRows = list
                .map(key => Number(key))
                .filter(index => Number.isInteger(index) && index >= 0 && index < this.data.length);
            this._rerender();
            return this;
        }
        this._selectedKeyMap.clear();
        list.forEach(key => {
            if (key === undefined || key === null) return;
            const id = String(key);
            const dataIndex = this._rowKeyIndex?.get(id);
            this._selectedKeyMap.set(id, dataIndex === undefined
                ? { key, row: undefined }
                : { key: this._rowKeyValues[dataIndex], row: this._sourceRow(dataIndex) });
        });
        this._syncSelectedRowsFromKeys();
        this._rerender();
        return this;
    }

    /**
     * 清除全部選取（含其他頁面的 key；不觸發 onRowSelectionChange）
     */
    clearSelection() {
        this._selectedKeyMap.clear();
        this._selectedRows = [];
        this._rerender();
        return this;
    }

    getData() {
        return this.data;
    }

    getSearchText() {
        return this._searchText;
    }

    getSearchDraft() {
        return this._searchDraft;
    }

    setSearchText(value) {
        this._searchDraft = String(value ?? '');
        if (this._isServerSide()) {
            this._clearSearchTimer();
            if (this._serverSearchChanged()) {
                this._applySearchDraft();
                this._loadServerData();
            } else {
                // 查詢字串實質未變：不重新查詢，也不回到第一頁
                this._searchText = this._searchDraft;
                this._rerender();
            }
            return this;
        }
        this._applySearchDraft();
        if (this.container) this.render();
        else this._renderToElement();
        return this;
    }

    clearSearch() {
        return this.setSearchText('');
    }

    /**
     * 重新執行目前的查詢。伺服器端模式會再呼叫一次 dataSource（取消前一個未完成的請求）；
     * 一般模式只重新渲染。回傳 Promise<boolean>：true 表示結果已套用到表格，
     * false 表示被較新的查詢取代、已中止、失敗（表格顯示錯誤與重試鈕）、已銷毀或未提供 dataSource。
     */
    reload() {
        if (this._destroyed) return Promise.resolve(false);
        if (!this._isServerSide()) {
            this._rerender();
            return Promise.resolve(true);
        }
        return this._loadServerData();
    }

    /**
     * 取得目前查詢條件：{ page（從 1 起算）, pageSize, sort: { key, direction } | null, search }
     */
    getQuery() {
        return {
            page: this._page + 1,
            pageSize: this._rowsPerPage,
            sort: this._getSortQuery(),
            search: String(this._searchText ?? '').trim(),
        };
    }

    /**
     * 設定欄位顯示狀態。key 為欄位名稱（Audit 格式的 key、標準格式的 name、Search 格式的 col_0…），
     * 也接受欄位索引數字。hideable: false 的欄位不受影響。預設不觸發 onColumnVisibilityChange，
     * 傳入 { emit: true } 才觸發。
     */
    setColumnVisible(key, visible, { emit = false } = {}) {
        const colIdx = this._findColumnIndex(key);
        if (colIdx < 0) return this;
        if (this._applyColumnVisibility(colIdx, visible !== false) && emit) this._emitColumnVisibility();
        return this;
    }

    /**
     * 取得所有欄位的顯示狀態：{ [欄位 key]: boolean }
     */
    getColumnVisibility() {
        const visibility = {};
        this.columns.forEach((col, colIdx) => {
            visibility[this._columnKey(col, colIdx)] = this._isColumnDisplayed(col, colIdx);
        });
        return visibility;
    }

    /**
     * 展開列明細。key 為 rowKey 值；未設定 rowKey 時為 dataIndex。
     */
    expandRow(key) {
        return this._setRowExpanded(key, true);
    }

    collapseRow(key) {
        return this._setRowExpanded(key, false);
    }

    toggleRow(key) {
        if (key === undefined || key === null) return this;
        return this._setRowExpanded(key, !this._expandedKeys.has(String(key)));
    }

    getExpandedKeys() {
        return [...this._expandedKeys.values()];
    }

    /**
     * 渲染到 container
     */
    render() {
        if (!this.container) return;
        const focusState = this._captureAsyncFocus();
        this._sortedCache = null;
        this.container.innerHTML = '';
        this._renderToElement();
        this.container.appendChild(this.element);
        this._afterAttach(focusState);
    }

    /**
     * 渲染到 this.element
     */
    _renderToElement() {
        this._cellComponents.splice(0).forEach(component => component.destroy?.());
        // 排序結果僅在本次渲染流程內共用（_renderToolbar / _bindEvents 重複取用時免重算），
        // finally 必定清除：流程外的呼叫（如事件中的 _fireSelectionChange）維持即時重算。
        // 進入即歸零：直接呼叫 _renderToElement（含回呼內重入）時不得沿用外層流程的快取。
        this._sortedCache = null;
        this._sortedCache = this._getSortedData();
        try {
            const sorted = this._sortedCache;
            const paginated = this._paginationEnabled ? this._getPaginatedData(sorted) : sorted;
            const visibleCols = this._getVisibleColumns();
            const isSelectable = this.options.selectableRows !== 'none' && this.options.selectableRows !== false;
            const keyed = this._hasRowKey();
            const isAnySelected = isSelectable && (keyed ? this._selectedKeyMap.size > 0 : this._selectedRows.length > 0);
            const serverSide = this._isServerSide();
            const expandable = this._getExpandable();

            // CSP 合規：模板不用 style 屬性，改用 class（樣式在 DataTable.css）
            const variantClass = THEMES[this.variant] ? this.variant : 'default';
            let html = `<div class="b4a-dt b4a-dt--${variantClass}${this._rootModifierClasses(serverSide, expandable)}">`;

            // 工具列
            html += this._renderToolbar(isAnySelected, sorted.length);
            if (serverSide) html += this._renderServerStatus();

            // 表格
            html += serverSide && this._loading ? '<div class="b4a-dt__scroll" aria-busy="true">' : '<div class="b4a-dt__scroll">';
            html += '<table class="b4a-dt__table">';

            // 表頭
            html += '<thead><tr>';
            if (expandable) html += this._renderExpandHeader();
            if (isSelectable) {
                const allSelected = keyed
                    ? this._getKeyedSelectAllState(paginated, sorted).all
                    : sorted.length > 0 && sorted.every(d => this._selectedRows.includes(d.dataIndex));
                html += '<th class="b4a-dt__th b4a-dt__th--select">';
                if (this.options.selectableRows !== 'single') {
                    html += `<input type="checkbox" class="b4a-dt__checkbox" data-action="select-all" ${allSelected ? 'checked' : ''}>`;
                }
                html += '</th>';
            }
            visibleCols.forEach(colIdx => {
                const col = this.columns[colIdx];
                const isSorted = this._sortCol === colIdx;
                const sortIcon = isSorted ? (this._sortDir === 'asc' ? ' ▲' : ' ▼') : '';
                const sortDisabled = col.options?.sort === false;
                html += `<th class="b4a-dt__th${sortDisabled ? '' : ' b4a-dt__th--sortable'}" data-action="sort" data-col="${colIdx}">`;
                html += `<div class="b4a-dt__th-inner">${escapeHtml(col.label || col.name || '')}${sortIcon}</div>`;
                html += '</th>';
            });
            html += '</tr></thead>';

            // 表身
            html += '<tbody>';
            const colspan = visibleCols.length + (isSelectable ? 1 : 0) + (expandable ? 1 : 0);
            if (paginated.length === 0) {
                if (serverSide && (this._loadError || this._loading)) {
                    html += this._renderServerPlaceholderRow(colspan);
                } else {
                    html += `<tr><td colspan="${colspan}" class="b4a-dt__td b4a-dt__td--empty">${escapeHtml(this._textLabels.body.noMatch)}</td></tr>`;
                }
            } else {
                // rowKey 模式的選取成員檢查用 Set，避免大量跨頁選取時逐列線性搜尋
                const selectedSet = keyed ? new Set(this._selectedRows) : null;
                paginated.forEach(({ row, dataIndex }, viewIndex) => {
                    const isEven = viewIndex % 2 === 1;
                    const isSelected = selectedSet ? selectedSet.has(dataIndex) : this._selectedRows.includes(dataIndex);
                    // 與舊行為一致：選取 > 偶數列 > 奇數列（hover 由 CSS :hover 蓋過）
                    const rowClass = isSelected ? ' b4a-dt__tr--selected' : (isEven ? ' b4a-dt__tr--even' : '');

                    html += `<tr class="b4a-dt__tr${rowClass}" data-row-index="${dataIndex}">`;

                    let detailId = null;
                    if (expandable) {
                        const expandCell = this._renderExpandCell(expandable, dataIndex);
                        detailId = expandCell.detailId;
                        html += expandCell.html;
                    }

                    if (isSelectable) {
                        html += '<td class="b4a-dt__td b4a-dt__td--select">';
                        html += `<input type="checkbox" class="b4a-dt__checkbox" data-action="select-row" data-index="${dataIndex}" ${isSelected ? 'checked' : ''}>`;
                        html += '</td>';
                    }

                    visibleCols.forEach(colIdx => {
                        const cellContent = this._renderCell(row, colIdx, dataIndex, viewIndex);
                        html += `<td class="b4a-dt__td" data-col="${colIdx}">${cellContent}</td>`;
                    });

                    html += '</tr>';
                    // 明細列內容於 innerHTML 之後以 DOM API 填入（字串一律 textContent）
                    if (detailId) {
                        html += `<tr class="b4a-dt__detail-row" id="${detailId}" data-detail-for="${dataIndex}"><td class="b4a-dt__td b4a-dt__detail-cell" colspan="${colspan}"></td></tr>`;
                    }
                });
            }
            html += '</tbody></table></div>';

            // 分頁（伺服器端模式依 dataSource 回報的 total）
            const paginationTotal = serverSide ? this._serverTotal : sorted.length;
            if (this._paginationEnabled && paginationTotal > 0) {
                html += this._renderPagination(paginationTotal);
            }

            html += '</div>';

            this.element.innerHTML = html;
            this._applyDynamicStyles(visibleCols);
            this._bindEvents(this.element);
            this._hydrateCellComponents(this.element);
            this._afterRenderFeatures(visibleCols, { isSelectable, keyed, serverSide, expandable });
            // Composite callers may mount B4A controls into cell hosts after every
            // sort/page re-render. The callback receives the stable table root.
            this.options.onRender?.(this.element, this);
        } finally {
            this._sortedCache = null;
        }
    }

    _hydrateCellComponents(root) {
        root.querySelectorAll('[data-b4a-link-cell]').forEach(host => {
            const link = new Link({
                text: host.dataset.linkText || '',
                href: host.dataset.linkHref || '',
                scope: host.dataset.linkScope === 'internal' ? Link.SCOPES.INTERNAL : Link.SCOPES.EXTERNAL,
            });
            const classes = String(host.dataset.linkClass || '').split(/\s+/).filter(Boolean);
            link.mount(host);
            if (classes.length) link.element.classList.add(...classes);
            this._cellComponents.push(link);
        });
        root.querySelectorAll('[data-b4a-badge-cell]').forEach(host => {
            const badge = new Badge({
                text: host.dataset.badgeText || '',
                variant: host.dataset.badgeVariant || Badge.VARIANTS.DEFAULT,
                type: Badge.TYPES.TEXT,
                size: Badge.SIZES.SMALL,
            });
            const classes = String(host.dataset.badgeClass || '').split(/\s+/).filter(Boolean);
            badge.render(host);
            if (classes.length) badge.element.classList.add(...classes);
            this._cellComponents.push(badge);
        });
    }

    /**
     * CSP 合規：HTML 剖析出的 style 屬性會被剝除，
     * 執行期才知道的動態樣式一律在渲染後以 CSSOM（el.style）指派。
     */
    _applyDynamicStyles(visibleCols) {
        // tableBodyHeight → 捲動區最大高度
        const bodyHeight = this.options.tableBodyHeight;
        if (bodyHeight) {
            const scroll = this.element.querySelector('.b4a-dt__scroll');
            if (scroll) {
                scroll.style.maxHeight = bodyHeight;
                scroll.style.overflowY = 'auto';
            }
        }

        // maxHeight（優先於 tableBodyHeight）/ stickyHeader：表身在元件內捲動，表頭由 CSS sticky 固定
        const maxHeight = this._resolveMaxHeight(bodyHeight);
        if (maxHeight) {
            const scroll = this.element.querySelector('.b4a-dt__scroll');
            if (scroll) {
                scroll.style.maxHeight = maxHeight;
                scroll.style.overflowY = 'auto';
            }
        }

        // setCellProps 自訂樣式（欄寬等）→ 套到該欄所有 th/td（與舊行為相同）
        visibleCols.forEach(colIdx => {
            const styleStr = this._getCellWidthStyle(this.columns[colIdx]);
            if (!styleStr) return;
            this.element.querySelectorAll(`[data-col="${colIdx}"]`).forEach(el => {
                el.style.cssText = styleStr;
            });
        });
    }

    // ── 內部方法 ──

    _getSearchableColumnIndices() {
        return this._getVisibleColumns().filter(colIdx =>
            this.columns[colIdx]?.options?.searchable !== false
        );
    }

    _getSearchCellText(row, colIdx, dataIndex) {
        const col = this.columns[colIdx];
        let rendered = row[colIdx];
        try {
            if (col.options?.customBodyRenderLite) {
                rendered = col.options.customBodyRenderLite(dataIndex, dataIndex);
            } else if (col.options?.customBodyRender) {
                rendered = col.options.customBodyRender(row[colIdx], {
                    rowData: row,
                    rowIndex: dataIndex,
                    columnIndex: colIdx,
                    dataIndex,
                });
            }
        } catch {
            // Searching must never make an otherwise renderable result list unusable.
            rendered = row[colIdx];
        }
        return renderedSearchText(rendered);
    }

    _getSearchIndex() {
        if (this._searchIndex) return this._searchIndex;
        const searchableColumns = this._getSearchableColumnIndices();
        this._searchIndex = this.data.map((row, dataIndex) => searchableColumns.map(colIdx =>
            normalizeSearchText(this._getSearchCellText(row, colIdx, dataIndex))
        ));
        return this._searchIndex;
    }

    _getFilteredData() {
        const indexed = this.data.map((row, dataIndex) => ({ row, dataIndex }));
        if (this.options.search !== true) return indexed;
        // 伺服器端模式由 dataSource 依 query.search 篩選，本地不再過濾
        if (this._isServerSide()) return indexed;
        const query = normalizeSearchText(this._searchText);
        if (!query) return indexed;
        const searchIndex = this._getSearchIndex();
        return indexed.filter(({ dataIndex }) =>
            searchIndex[dataIndex].some(cellText => cellText.includes(query))
        );
    }

    _applySearchDraft() {
        const changed = normalizeSearchText(this._searchDraft) !== normalizeSearchText(this._searchText);
        this._searchText = this._searchDraft;
        this._page = 0;

        // A submitted filter must never leave a previously selected, now-hidden
        // row available to a bulk action. Re-submitting the same effective query
        // preserves selection; draft input alone never touches it.
        // rowKey 模式的選取本來就跨頁/跨篩選保留（工具列顯示總選取數），不在此清除。
        if (changed && this._selectedRows.length > 0 && !this._hasRowKey()) {
            this._selectedRows = [];
            this._fireSelectionChange();
        }
        return changed;
    }

    _getSortedData() {
        if (this._sortedCache) return this._sortedCache;
        const indexed = this._getFilteredData();
        // 伺服器端模式的列已由 dataSource 排好序
        if (this._sortCol === null || !this._sortDir || this._isServerSide()) return indexed;

        const colIdx = this._sortCol;
        const dir = this._sortDir === 'asc' ? 1 : -1;
        return [...indexed].sort((a, b) => {
            const aVal = this._getCellSortValue(a.row[colIdx]);
            const bVal = this._getCellSortValue(b.row[colIdx]);
            if (typeof aVal === 'number' && typeof bVal === 'number') return (aVal - bVal) * dir;
            return String(aVal).localeCompare(String(bVal), 'zh-Hant') * dir;
        });
    }

    _getPaginatedData(sorted) {
        // 伺服器端模式：已載入的列就是目前這一頁
        if (this._isServerSide()) return sorted;
        const totalPages = Math.max(1, Math.ceil(sorted.length / this._rowsPerPage));
        this._page = Math.min(this._page, totalPages - 1);
        const start = this._page * this._rowsPerPage;
        return sorted.slice(start, start + this._rowsPerPage);
    }

    _getVisibleColumns() {
        return this.columns.reduce((acc, col, i) => {
            if (this._isColumnDisplayed(col, i)) acc.push(i);
            return acc;
        }, []);
    }

    /**
     * 欄位是否顯示：使用者/程式覆寫（columnToggle、setColumnVisible）優先，
     * 否則沿用欄位定義的 display / hidden / visible:false。
     */
    _isColumnDisplayed(col, colIdx) {
        if (this._columnVisibility.size > 0) {
            const override = this._columnVisibility.get(this._columnKey(col, colIdx));
            if (override !== undefined) return override;
        }
        return col.options?.display !== false && col.options?.display !== 'false';
    }

    _getCellSortValue(cell) {
        if (cell == null) return '';
        if (typeof cell === 'string' || typeof cell === 'number') return cell;
        return String(cell);
    }

    _renderCell(row, colIdx, dataIndex, rowIndex) {
        const col = this.columns[colIdx];
        if (col.options?.customBodyRenderLite) {
            return col.options.customBodyRenderLite(dataIndex, rowIndex);
        }
        if (col.options?.customBodyRender) {
            const result = col.options.customBodyRender(row[colIdx], {
                rowData: row,
                rowIndex,
                columnIndex: colIdx,
                dataIndex,
            });
            // raw() 標記 → 已知安全 HTML
            if (isRawHtml(result)) return result.__html;
            // 字串 → 一律 escape
            return result == null ? '' : escapeHtml(String(result));
        }
        const val = row[colIdx];
        if (val == null) return '';
        return escapeHtml(String(val));
    }

    _renderToolbar(isAnySelected, filteredCount = this.data.length) {
        const { customToolbar, customToolbarSelect } = this.options;
        const searchEnabled = this.options.search === true;
        const serverSide = this._isServerSide();
        const totalCount = this.data.length;
        // 伺服器端模式的搜尋送到伺服器，不是「篩選已載入結果」，改用對應的預設文字（呼叫端自訂的 placeholder 仍優先）
        const searchPlaceholder = serverSide
            ? this._label('search', 'placeholder', 'dataTable.serverSearchPlaceholder')
            : this._textLabels.search.placeholder;
        const searchButtonLabel = this._textLabels.search.buttonLabel;
        const searchResultCount = serverSide
            ? this._serverResultCountText()
            : String(this._textLabels.search.resultCount)
                .replaceAll('{count}', String(filteredCount))
                .replaceAll('{total}', String(totalCount));
        const columnToggleHtml = this._renderColumnToggleButton();
        const searchHtml = searchEnabled
            ? `<div class="b4a-dt__quick-search">
                <input class="b4a-dt__quick-search-input" type="search" data-action="quick-search" aria-label="${escapeHtml(searchPlaceholder)}" placeholder="${escapeHtml(searchPlaceholder)}" value="${escapeHtml(this._searchDraft)}">
                <button class="b4a-dt__quick-search-submit" type="button" data-action="quick-search-submit" aria-label="${escapeHtml(searchButtonLabel)}" title="${escapeHtml(searchButtonLabel)}"><span aria-hidden="true">🔍</span></button>
                <span class="b4a-dt__quick-search-count" aria-live="polite">${escapeHtml(searchResultCount)}</span>
            </div>`
            : '';

        if (isAnySelected && customToolbarSelect) {
            const sorted = this._getSortedData();
            const selectedRowsObj = {
                data: this._hasRowKey()
                    ? this._describeLoadedSelection(sorted)
                    : this._selectedRows.map(dataIndex => ({
                        index: sorted.findIndex(d => d.dataIndex === dataIndex),
                        dataIndex,
                    })),
                lookup: this._selectedRows.reduce((acc, i) => { acc[i] = true; return acc; }, {}),
            };
            // rowKey 模式：setter 收到的仍是 dataIndex，轉成 key 後取代選取
            const setSelection = this._hasRowKey()
                ? (rows) => this.setSelectedRows(rows)
                : (rows) => { this._selectedRows = rows; this.render(); };
            const toolbarContent = typeof customToolbarSelect === 'function'
                ? customToolbarSelect(selectedRowsObj, this.data, setSelection)
                : (customToolbarSelect || '');
            // rowKey 模式顯示跨頁的總選取數
            const selectedCount = this._hasRowKey() ? this._selectedKeyMap.size : this._selectedRows.length;

            return `<div class="b4a-dt__toolbar--select">
                <span class="b4a-dt__toolbar-text">${selectedCount} ${escapeHtml(this._textLabels.selectedRows.text)}已選擇</span>
                <div class="b4a-dt__toolbar-actions">${searchHtml}${toolbarContent}${columnToggleHtml}</div>
            </div>`;
        }

        const titleHtml = isRawHtml(this.title)
            ? this.title.__html
            : (typeof this.title === 'string' && this.title
                ? `<span class="b4a-dt__title">${escapeHtml(this.title)}</span>`
                : (this.title || ''));
        const toolbarHtml = customToolbar
            ? (typeof customToolbar === 'function' ? customToolbar() : (customToolbar || ''))
            : '';

        return `<div class="b4a-dt__toolbar">
            <div>${titleHtml}</div>
            <div class="b4a-dt__toolbar-actions">${searchHtml}${toolbarHtml}${columnToggleHtml}</div>
        </div>`;
    }

    _renderPagination(totalCount) {
        const totalPages = Math.max(1, Math.ceil(totalCount / this._rowsPerPage));
        const page = Math.min(this._page, totalPages - 1);
        const start = page * this._rowsPerPage + 1;
        const end = Math.min((page + 1) * this._rowsPerPage, totalCount);
        const options = this.options.rowsPerPageOptions || [10, 20, 100, 500, 1000];

        // 禁用外觀改由 CSS :disabled 呈現（同 btnStyle 舊邏輯）
        return `<div class="b4a-dt__pagination">
            <div class="b4a-dt__pagination-group">
                <span>${escapeHtml(this._textLabels.pagination.rowsPerPage)}</span>
                <select data-action="rows-per-page" class="b4a-dt__page-size">
                    ${options.map(opt => `<option value="${opt}" ${opt === this._rowsPerPage ? 'selected' : ''}>${opt}</option>`).join('')}
                </select>
            </div>
            <span>${start}-${end} ${escapeHtml(this._textLabels.pagination.displayRows)} ${totalCount}</span>
            <div class="b4a-dt__page-btns">
                <button class="b4a-dt__page-btn" data-action="page-first" ${page === 0 ? 'disabled' : ''} title="${Locale.t('dataTable.firstPage')}">⟨⟨</button>
                <button class="b4a-dt__page-btn" data-action="page-prev" ${page === 0 ? 'disabled' : ''} title="${Locale.t('dataTable.prevPage')}">⟨</button>
                <button class="b4a-dt__page-btn" data-action="page-next" ${page >= totalPages - 1 ? 'disabled' : ''} title="${Locale.t('dataTable.nextPage')}">⟩</button>
                <button class="b4a-dt__page-btn" data-action="page-last" ${page >= totalPages - 1 ? 'disabled' : ''} title="${Locale.t('dataTable.lastPage')}">⟩⟩</button>
            </div>
        </div>`;
    }

    _getCellWidthStyle(col) {
        const props = col.options?.setCellProps?.() || {};
        const style = props.style || {};
        return Object.entries(style).map(([k, v]) => {
            const key = k.replace(/[A-Z]/g, m => `-${m.toLowerCase()}`);
            return `${key}:${v}`;
        }).join(';');
    }

    _bindEvents(root) {
        if (!root) return;

        const quickSearch = root.querySelector('[data-action="quick-search"]');
        const quickSearchSubmit = root.querySelector('[data-action="quick-search-submit"]');
        const submitQuickSearch = focusTarget => {
            const input = this.element.querySelector('[data-action="quick-search"]');
            const selectionStart = input.selectionStart;
            const selectionEnd = input.selectionEnd;
            this._searchDraft = input.value;
            if (this._isServerSide()) {
                // 明確送出（Enter / 按鈕）一律立即查詢並取消尚未觸發的防抖
                this._clearSearchTimer();
                this._applySearchDraft();
                this._loadServerData();
            } else {
                this._applySearchDraft();
                if (this.container) this.render();
                else this._renderToElement();
            }

            const nextFocus = this.element.querySelector(
                focusTarget === 'input' ? '[data-action="quick-search"]' : '[data-action="quick-search-submit"]'
            );
            nextFocus?.focus?.({ preventScroll: true });
            if (focusTarget === 'input') {
                try {
                    nextFocus?.setSelectionRange?.(selectionStart, selectionEnd);
                } catch {
                    // Some input implementations do not expose a selection range.
                }
            }
        };
        quickSearch?.addEventListener('compositionstart', () => {
            this._quickSearchComposing = true;
        });
        quickSearch?.addEventListener('compositionend', event => {
            this._quickSearchComposing = false;
            this._searchDraft = event.currentTarget.value;
        });
        quickSearch?.addEventListener('input', event => {
            this._searchDraft = event.currentTarget.value;
        });
        quickSearch?.addEventListener('keydown', event => {
            if (event.key !== 'Enter' || event.isComposing || this._quickSearchComposing) return;
            event.preventDefault();
            submitQuickSearch('input');
        });
        quickSearchSubmit?.addEventListener('click', () => submitQuickSearch('button'));

        const serverSide = this._isServerSide();
        if (quickSearch && serverSide) {
            // 伺服器端模式：輸入即防抖查詢（組字期間不送出，組字結束後才排程）
            quickSearch.addEventListener('input', event => {
                if (event.isComposing || this._quickSearchComposing) return;
                this._scheduleServerSearch();
            });
            quickSearch.addEventListener('compositionend', () => {
                this._flushCompositionDeferredRender();
                this._scheduleServerSearch();
            });
        }

        // 排序
        root.querySelectorAll('[data-action="sort"]').forEach(th => {
            th.addEventListener('click', () => {
                const colIdx = parseInt(th.getAttribute('data-col'));
                const col = this.columns[colIdx];
                if (col.options?.sort === false) return;

                if (this._sortCol === colIdx) {
                    if (this._sortDir === 'asc') this._sortDir = 'desc';
                    else { this._sortCol = null; this._sortDir = null; }
                } else {
                    this._sortCol = colIdx;
                    this._sortDir = 'asc';
                }
                if (this._isServerSide()) {
                    this._loadServerData();
                    return;
                }
                if (this.container) this.render();
                else this._renderToElement();
            });
        });

        // 全選
        root.querySelector('[data-action="select-all"]')?.addEventListener('change', (e) => {
            if (this._hasRowKey()) {
                this._selectAllKeyed(e.target.checked);
                return;
            }
            const sorted = this._getSortedData();
            this._selectedRows = e.target.checked ? sorted.map(d => d.dataIndex) : [];
            this._afterSelectionChange();
        });

        // 單行選擇
        root.querySelectorAll('[data-action="select-row"]').forEach(cb => {
            cb.addEventListener('change', () => {
                const dataIndex = parseInt(cb.getAttribute('data-index'));
                if (this._hasRowKey()) {
                    this._toggleKeyedRow(dataIndex, cb.checked);
                    return;
                }
                if (this.options.selectableRows === 'single') {
                    this._selectedRows = cb.checked ? [dataIndex] : [];
                } else {
                    if (cb.checked) {
                        this._selectedRows.push(dataIndex);
                    } else {
                        this._selectedRows = this._selectedRows.filter(i => i !== dataIndex);
                    }
                }
                this._afterSelectionChange();
            });
        });

        // 每頁筆數
        root.querySelector('[data-action="rows-per-page"]')?.addEventListener('change', (e) => {
            this._rowsPerPage = parseInt(e.target.value);
            this._page = 0;
            if (this._isServerSide()) {
                this._loadServerData();
                return;
            }
            if (this.container) this.render();
            else this._renderToElement();
        });

        // 分頁按鈕（伺服器端模式：總頁數依 dataSource 回報的 total，換頁即重新查詢）
        const sorted = this._getSortedData();
        const totalPages = Math.max(1, Math.ceil((serverSide ? this._serverTotal : sorted.length) / this._rowsPerPage));
        const rerender = serverSide
            ? () => { this._loadServerData(); }
            : () => { if (this.container) this.render(); else this._renderToElement(); };
        root.querySelector('[data-action="page-first"]')?.addEventListener('click', () => { this._page = 0; rerender(); });
        root.querySelector('[data-action="page-prev"]')?.addEventListener('click', () => { if (this._page > 0) { this._page--; rerender(); } });
        root.querySelector('[data-action="page-next"]')?.addEventListener('click', () => { if (this._page < totalPages - 1) { this._page++; rerender(); } });
        root.querySelector('[data-action="page-last"]')?.addEventListener('click', () => { this._page = totalPages - 1; rerender(); });

        // 行 hover：改由 DataTable.css 的 .b4a-dt .b4a-dt__tr:hover 呈現
        //（特異性高於 --selected/--even，hover 蓋過選取/斑馬色，與舊 JS 行為一致）

        // ── 進階功能的事件（各自在選項啟用時才綁定） ──
        if (serverSide) {
            root.querySelector('[data-action="retry"]')?.addEventListener('click', () => { this.reload(); });
        }
        if (this._opt('columnToggle') === true) this._bindColumnToggle(root);
        const expandable = this._getExpandable();
        if (expandable) this._bindExpandable(root, expandable);
    }

    /**
     * 選取狀態變更後的更新：無 customToolbarSelect 時工具列與選取無關，
     * 只需局部同步選取相關 DOM，免整頁重建（onRender 不重發）；
     * customToolbarSelect 的工具列內容依賴 onRender 掛載（如選取動作按鈕），維持完整重繪。
     */
    _afterSelectionChange() {
        // customToolbar 為函式時可能讀取選取狀態，維持完整重繪以重新求值（與舊行為一致）
        if (!this.options.customToolbarSelect && typeof this.options.customToolbar !== 'function') {
            this._applySelectionUpdate();
            this._fireSelectionChange();
            return;
        }
        this._fireSelectionChange();
        if (this.container) this.render();
        else this._renderToElement();
    }

    /**
     * 局部同步選取相關 DOM（列 class、行 checkbox、表頭全選），結果與完整重繪一致
     */
    _applySelectionUpdate() {
        // :scope 鏈限定在本表自身的 tbody，避免命中儲存格內的巢狀表格列
        const ownRows = this.element.querySelectorAll(
            ':scope > .b4a-dt > .b4a-dt__scroll > .b4a-dt__table > tbody > tr[data-row-index]'
        );
        // rowKey 模式可能有大量跨頁選取：成員檢查改用 Set
        const selectedSet = this._hasRowKey() ? new Set(this._selectedRows) : null;
        ownRows.forEach((tr, viewIndex) => {
            const dataIndex = parseInt(tr.getAttribute('data-row-index'));
            const isSelected = selectedSet ? selectedSet.has(dataIndex) : this._selectedRows.includes(dataIndex);
            const isEven = viewIndex % 2 === 1;
            // 僅切換本元件的兩個修飾 class，保留外部程式加在列上的其他 class
            tr.classList.toggle('b4a-dt__tr--selected', isSelected);
            tr.classList.toggle('b4a-dt__tr--even', !isSelected && isEven);
            const cb = tr.querySelector(':scope > .b4a-dt__td--select > [data-action="select-row"]');
            if (cb) {
                cb.checked = isSelected;
                if (isSelected) cb.setAttribute('checked', '');
                else cb.removeAttribute('checked');
            }
        });

        const selectAll = this.element.querySelector(
            ':scope > .b4a-dt > .b4a-dt__scroll > .b4a-dt__table > thead [data-action="select-all"]'
        );
        if (selectAll && this._hasRowKey()) {
            // rowKey 模式：依 selectAllScope（目前頁 / 篩選後全部）判定，部分選取時呈現 indeterminate
            this._syncKeyedSelectAll(selectAll);
        } else if (selectAll) {
            // 與 _renderToElement 的 allSelected 判定等價：以快速篩選後的資料列判斷（成員相同，免排序）
            const visibleRows = this._getFilteredData();
            const allSelected = visibleRows.length > 0 && visibleRows.every(d => this._selectedRows.includes(d.dataIndex));
            selectAll.checked = allSelected;
            if (allSelected) selectAll.setAttribute('checked', '');
            else selectAll.removeAttribute('checked');
        }
    }

    _fireSelectionChange() {
        if (this.options.onRowSelectionChange) {
            const sorted = this._getSortedData();
            if (this._hasRowKey()) {
                // rowKey 模式：前三個參數只描述目前已載入的列，第四個參數是跨頁的完整 key 清單
                this.options.onRowSelectionChange([], this._describeLoadedSelection(sorted), [...this._selectedRows], this.getSelectedKeys());
                return;
            }
            const allSelected = this._selectedRows.map(dataIndex => ({
                index: sorted.findIndex(d => d.dataIndex === dataIndex),
                dataIndex,
            }));
            this.options.onRowSelectionChange([], allSelected, [...this._selectedRows]);
        }
    }

    /** rowKey 模式：已載入選取列的 { index（排序篩選後位置，不在其中為 -1）, dataIndex }，以 Map 查位置避免逐列線性搜尋 */
    _describeLoadedSelection(sorted) {
        const positions = new Map(sorted.map((d, position) => [d.dataIndex, position]));
        return this._selectedRows.map(dataIndex => ({
            index: positions.has(dataIndex) ? positions.get(dataIndex) : -1,
            dataIndex,
        }));
    }

    // ══════════════════════════════════════════════════════════════
    // 進階功能：共用
    // ══════════════════════════════════════════════════════════════

    /** 讀取進階選項：即時讀 this.options，未指定時用 ADVANCED_DEFAULTS */
    _opt(key) {
        const value = this.options[key];
        return value === undefined ? ADVANCED_DEFAULTS[key] : value;
    }

    _isServerSide() {
        return this._opt('serverSide') === true;
    }

    _hasRowKey() {
        const rowKey = this._opt('rowKey');
        return typeof rowKey === 'function' || (typeof rowKey === 'string' && rowKey !== '');
    }

    _getExpandable() {
        const expandable = this._opt('expandable');
        return expandable && typeof expandable.render === 'function' ? expandable : null;
    }

    _ensureUid() {
        if (!this._uid) this._uid = nextUid('b4a-dt');
        return this._uid;
    }

    /** 呼叫端 options.textLabels[group][key] 優先，否則取 Locale（渲染時求值，隨語系切換） */
    _label(group, key, localeKey) {
        const custom = this.options.textLabels?.[group]?.[key];
        return custom !== undefined && custom !== null ? String(custom) : Locale.t(localeKey);
    }

    _rootModifierClasses(serverSide, expandable) {
        let classes = '';
        if (this._opt('stickyHeader') === true) classes += ' b4a-dt--sticky-header';
        if (serverSide) classes += ' b4a-dt--server';
        if (serverSide && this._loading) classes += ' b4a-dt--loading';
        if (expandable?.expandOnRowClick === true) classes += ' b4a-dt--row-expand';
        return classes;
    }

    _resolveMaxHeight(bodyHeight) {
        const value = this._opt('maxHeight');
        if (typeof value === 'number' && Number.isFinite(value) && value > 0) return `${value}px`;
        if (typeof value === 'string' && value.trim()) return value.trim();
        if (this._opt('stickyHeader') === true && !bodyHeight) return DEFAULT_STICKY_MAX_HEIGHT;
        return '';
    }

    /** 以目前狀態重繪：container 模式走 render()（含掛回後的量測、選單定位與焦點還原） */
    _rerender() {
        if (this._isServerSide() && this._shouldDeferRenderForComposition()) {
            // 中文輸入法組字中重建輸入框會打斷組字：等 compositionend 再重繪
            this._renderDeferredByComposition = true;
            return;
        }
        if (this.container) {
            this.render();
            return;
        }
        const focusState = this._captureAsyncFocus();
        this._renderToElement();
        this._afterAttach(focusState);
    }

    _shouldDeferRenderForComposition() {
        if (!this._quickSearchComposing || typeof document === 'undefined') return false;
        const active = document.activeElement;
        return !!active && active.getAttribute?.('data-action') === 'quick-search' && !!this.element?.contains?.(active);
    }

    _flushCompositionDeferredRender() {
        if (!this._renderDeferredByComposition) return;
        this._renderDeferredByComposition = false;
        this._rerender();
    }

    /**
     * 伺服器端模式的重繪是非同步觸發的（回應抵達時使用者可能正在輸入或操作分頁），
     * 重繪前記下焦點所在的元件控制項，重繪後還原到同一個控制項（含輸入框游標位置）。
     */
    _captureAsyncFocus() {
        if (!this._isServerSide() || typeof document === 'undefined') return null;
        const active = document.activeElement;
        if (!active || !this.element?.contains?.(active)) {
            // 上次重繪時焦點所在的控制項暫時不存在（例如重新查詢中的「重試」鈕）：
            // 只有焦點已遺失（落在 body）時才沿用，不搶走使用者移到別處的焦點
            const pending = this._pendingFocus;
            this._pendingFocus = null;
            return pending && (!active || active === document.body) ? pending : null;
        }
        this._pendingFocus = null;
        const action = active.getAttribute?.('data-action');
        if (!FOCUS_RESTORABLE_ACTIONS.has(action)) return null;
        const index = active.getAttribute('data-index');
        let selectionStart = null;
        let selectionEnd = null;
        try {
            if (typeof active.selectionStart === 'number') {
                selectionStart = active.selectionStart;
                selectionEnd = active.selectionEnd;
            }
        } catch {
            // 部分 input 型別不支援選取範圍
        }
        return {
            action,
            index: index !== null && /^\d+$/.test(index) ? index : null,
            selectionStart,
            selectionEnd,
        };
    }

    _restoreAsyncFocus(state) {
        if (!state || this._destroyed) return;
        const find = action => this.element?.querySelector?.(state.index !== null
            ? `[data-action="${action}"][data-index="${state.index}"]`
            : `[data-action="${action}"]`);
        let target = find(state.action);
        if (target?.disabled && PAGE_FOCUS_FALLBACK[state.action]) {
            // 換到第一頁/最後一頁後原按鈕停用：焦點留在分頁區的另一側按鈕
            target = find(PAGE_FOCUS_FALLBACK[state.action]);
        }
        if (!target || target.disabled) {
            // 控制項暫時不存在（載入中）：保留一次，下一次非同步重繪時再還原
            this._pendingFocus = state.deferred ? null : { ...state, deferred: true };
            return;
        }
        if (target === document.activeElement) return;
        target.focus?.({ preventScroll: true });
        if (state.selectionStart !== null) {
            try {
                target.setSelectionRange?.(state.selectionStart, state.selectionEnd);
            } catch {
                // 部分 input 型別不支援選取範圍
            }
        }
    }

    /** element 掛回容器之後：量測固定欄寬、重新定位欄位選單、還原非同步重繪前的焦點 */
    _afterAttach(focusState) {
        if (this._stickyLayout) this._measureAndApplySticky();
        if (this._columnMenu) this._positionColumnMenu();
        if (focusState) this._restoreAsyncFocus(focusState);
    }

    /** 每次渲染後套用進階功能（全部依選項啟用，預設路徑不做任何 DOM 操作） */
    _afterRenderFeatures(visibleCols, { isSelectable, keyed, serverSide, expandable }) {
        if (expandable) this._fillDetailRows(expandable);
        if (keyed && isSelectable) {
            const selectAll = this.element.querySelector(
                ':scope > .b4a-dt > .b4a-dt__scroll > .b4a-dt__table > thead [data-action="select-all"]'
            );
            if (selectAll) this._syncKeyedSelectAll(selectAll);
        }
        if (serverSide) this._mountLoadingSpinner();
        this._applyStickyColumns(visibleCols, { isSelectable, expandable });
        if (this._columnMenu) this._syncColumnMenuAfterRender();
    }

    _ownTable() {
        return this.element?.querySelector?.(':scope > .b4a-dt > .b4a-dt__scroll > .b4a-dt__table') || null;
    }

    _ownTbody() {
        return this.element?.querySelector?.(':scope > .b4a-dt > .b4a-dt__scroll > .b4a-dt__table > tbody') || null;
    }

    // ══════════════════════════════════════════════════════════════
    // 伺服器端模式
    // ══════════════════════════════════════════════════════════════

    /** 首次查詢延到 microtask：建構子返回後才呼叫 dataSource / onQueryChange */
    _queueServerLoad() {
        if (this._destroyed) return;
        const token = {};
        this._queuedLoadToken = token;
        Promise.resolve().then(() => {
            if (this._queuedLoadToken !== token || this._destroyed) return;
            this._queuedLoadToken = null;
            this._loadServerData();
        });
    }

    /**
     * 依目前查詢條件呼叫 dataSource。新查詢會中止前一個請求（AbortSignal），
     * 並以遞增序號忽略任何晚到的過期回應（dataSource 未理會 signal 時也安全）。
     */
    _loadServerData() {
        if (this._destroyed) return Promise.resolve(false);
        this._queuedLoadToken = null;
        this._abortInFlight();
        const seq = ++this._requestSeq;
        const controller = typeof AbortController === 'function' ? new AbortController() : null;
        this._abortController = controller;
        const query = { ...this.getQuery(), signal: controller ? controller.signal : undefined };

        const onQueryChange = this._opt('onQueryChange');
        if (typeof onQueryChange === 'function') {
            try {
                onQueryChange(query);
            } catch (error) {
                console.error('[DataTable] onQueryChange 發生錯誤', error);
            }
            // 回呼內可能再次觸發查詢或銷毀元件：以最新狀態為準
            if (seq !== this._requestSeq || this._destroyed) return Promise.resolve(false);
        }

        const dataSource = this._opt('dataSource');
        if (typeof dataSource !== 'function') {
            // 受控模式：呼叫端依 onQueryChange 取資料後以 setData(rows, total) 推入
            this._rerender();
            return Promise.resolve(false);
        }

        this._loading = true;
        this._loadError = null;
        this._rerender();

        let pending;
        try {
            pending = Promise.resolve(dataSource(query));
        } catch (error) {
            pending = Promise.reject(error);
        }
        return pending.then(
            result => this._settleServerRequest(seq, result, null),
            error => this._settleServerRequest(seq, null, error ?? new Error('dataSource rejected')),
        );
    }

    _settleServerRequest(seq, result, error) {
        if (seq !== this._requestSeq || this._destroyed) return false;
        this._abortController = null;
        this._loading = false;

        if (error) {
            // 失敗時不保留上一次的列，避免被誤認為本次查詢結果；rowKey 選取不受影響
            this._loadError = error;
            this._assignData([]);
            this._searchIndex = null;
            this._afterServerRowsReplaced();
            this._rerender();
            return false;
        }

        const rows = Array.isArray(result) ? result : (Array.isArray(result?.rows) ? result.rows : []);
        const total = Array.isArray(result) ? rows.length : Number(result?.total);
        this._assignData(rows);
        this._searchIndex = null;
        this._serverTotal = Number.isFinite(total) && total >= 0
            ? Math.floor(total)
            : this._page * this._rowsPerPage + rows.length;
        this._serverLoaded = true;
        this._loadError = null;
        this._afterServerRowsReplaced();

        // 總筆數變少使目前頁超出範圍（例如刪除後重新載入）：改查最後一頁
        const lastPage = Math.max(0, Math.ceil(this._serverTotal / this._rowsPerPage) - 1);
        if (rows.length === 0 && this._page > lastPage) {
            this._page = lastPage;
            return this._loadServerData();
        }
        this._rerender();
        return true;
    }

    _afterServerRowsReplaced() {
        if (this._hasRowKey()) {
            this._syncSelectedRowsFromKeys();
            return;
        }
        // 未設定 rowKey 時 dataIndex 只對應目前這一頁：換頁或重新查詢後，選取與展開都失效
        this._expandedKeys.clear();
        if (this._selectedRows.length > 0) {
            this._selectedRows = [];
            this._fireSelectionChange();
        }
    }

    _abortInFlight() {
        if (!this._abortController) return;
        try {
            this._abortController.abort();
        } catch {
            // 中止失敗不影響後續：序號檢查仍會忽略過期回應
        }
        this._abortController = null;
    }

    /** 取消排程中與進行中的查詢，之後回來的回應一律忽略 */
    _cancelServerRequest() {
        this._queuedLoadToken = null;
        this._abortInFlight();
        this._requestSeq += 1;
        this._loading = false;
    }

    _clearSearchTimer() {
        if (this._searchTimer === null) return;
        clearTimeout(this._searchTimer);
        this._searchTimer = null;
    }

    _scheduleServerSearch() {
        if (this._destroyed) return;
        this._clearSearchTimer();
        const delay = Math.max(0, Number(this._opt('searchDebounce')) || 0);
        this._searchTimer = setTimeout(() => {
            this._searchTimer = null;
            if (this._destroyed) return;
            // 文字實質未變（例如打了又刪）不重新查詢，也不改變頁碼
            if (!this._serverSearchChanged()) return;
            this._applySearchDraft();
            this._loadServerData();
        }, delay);
    }

    /** 伺服器端比對的是實際送出的 query.search（去頭尾空白、保留大小寫），不是本地篩選用的正規化文字 */
    _serverSearchChanged() {
        return String(this._searchDraft ?? '').trim() !== String(this._searchText ?? '').trim();
    }

    _getSortQuery() {
        if (this._sortCol === null || this._sortCol === undefined || !this._sortDir) return null;
        const col = this.columns[this._sortCol];
        if (!col) return null;
        return { key: this._columnKey(col, this._sortCol), direction: this._sortDir === 'desc' ? 'desc' : 'asc' };
    }

    _renderServerStatus() {
        if (!this._loading) return '<div class="b4a-dt__status" role="status"></div>';
        const text = this._label('body', 'loading', 'dataTable.loading');
        return `<div class="b4a-dt__status b4a-dt__status--loading" role="status"><span class="b4a-dt__spinner-host" aria-hidden="true"></span><span class="b4a-dt__status-text">${escapeHtml(text)}</span></div>`;
    }

    _renderServerPlaceholderRow(colspan) {
        if (this._loadError) {
            const message = this._label('body', 'loadError', 'dataTable.loadError');
            const retry = this._label('body', 'retry', 'dataTable.retry');
            return `<tr><td colspan="${colspan}" class="b4a-dt__td b4a-dt__td--empty b4a-dt__td--error"><div class="b4a-dt__error" role="alert"><span class="b4a-dt__error-text">${escapeHtml(message)}</span><button type="button" class="b4a-dt__retry" data-action="retry">${escapeHtml(retry)}</button></div></td></tr>`;
        }
        // 載入中且尚無資料：以空白列取代「無查詢結果」避免誤導，進度由上方狀態列呈現
        return `<tr><td colspan="${colspan}" class="b4a-dt__td b4a-dt__td--empty b4a-dt__td--loading"></td></tr>`;
    }

    _serverResultCountText() {
        if (!this._serverLoaded) return '';
        return this._label('search', 'serverResultCount', 'dataTable.serverResultCount')
            .replaceAll('{total}', String(this._serverTotal));
    }

    /** 沿用 common/LoadingSpinner；納入 _cellComponents，下次重繪或 destroy 時一併停止動畫 */
    _mountLoadingSpinner() {
        const host = this.element.querySelector('.b4a-dt__spinner-host');
        if (!host) return;
        const spinner = new LoadingSpinner({ size: LoadingSpinner.SIZES.SMALL });
        spinner.mount(host);
        this._cellComponents.push(spinner);
    }

    // ══════════════════════════════════════════════════════════════
    // 資料與 rowKey
    // ══════════════════════════════════════════════════════════════

    /** 與 setData 既有邏輯相同的正規化；另保留原始列供 rowKey / expandable 取用 */
    _assignData(data) {
        if (data && data.length > 0 && !Array.isArray(data[0]) && typeof data[0] === 'object') {
            const keys = this.columns.map(c => c.name);
            this.data = data.map(row => keys.map(k => row[k] !== undefined ? row[k] : ''));
        } else {
            this.data = data || [];
        }
        this._sourceRows = Array.isArray(data) ? data : this.data;
        this._rebuildRowKeys();
    }

    _sourceRow(dataIndex) {
        const source = this._sourceRows?.[dataIndex];
        return source !== undefined ? source : this.data[dataIndex];
    }

    _rebuildRowKeys() {
        if (!this._hasRowKey()) {
            this._rowKeyIds = null;
            this._rowKeyValues = null;
            this._rowKeyIndex = null;
            return;
        }
        const rowKey = this._opt('rowKey');
        const colIdx = typeof rowKey === 'string' ? this.columns.findIndex(c => c.name === rowKey) : -1;
        const count = Array.isArray(this.data) ? this.data.length : 0;
        const ids = new Array(count);
        const values = new Array(count);
        const index = new Map();
        let missing = false;
        for (let dataIndex = 0; dataIndex < count; dataIndex++) {
            let value;
            try {
                value = this._readRowKey(rowKey, colIdx, dataIndex);
            } catch {
                value = undefined;
            }
            if (value === undefined || value === null || value === '') {
                // 取不到 key 的列給本次資料內唯一的替代 key（不會跨換頁或重新載入保留）
                missing = true;
                value = `\u0000row:${dataIndex}`;
            }
            const id = String(value);
            ids[dataIndex] = id;
            values[dataIndex] = value;
            if (!index.has(id)) index.set(id, dataIndex);
        }
        // 只在 key 真正有作用（可選取或可展開）時提醒；不可選取的表格帶 rowKey 不產生任何可見差異
        const keysMatter = (this.options.selectableRows !== 'none' && this.options.selectableRows !== false)
            || !!this._getExpandable();
        if (missing && keysMatter && !this._missingKeyWarned) {
            this._missingKeyWarned = true;
            console.warn('[DataTable] rowKey 無法從部分資料列取得 key，這些列的選取與展開狀態不會跨頁或跨重新載入保留。');
        }
        this._rowKeyIds = ids;
        this._rowKeyValues = values;
        this._rowKeyIndex = index;
    }

    _readRowKey(rowKey, colIdx, dataIndex) {
        const source = this._sourceRow(dataIndex);
        if (typeof rowKey === 'function') return rowKey(source, dataIndex);
        if (source && typeof source === 'object' && !Array.isArray(source)) return source[rowKey];
        return colIdx >= 0 ? this.data[dataIndex]?.[colIdx] : undefined;
    }

    // ══════════════════════════════════════════════════════════════
    // rowKey 跨頁選取
    // ══════════════════════════════════════════════════════════════

    /** 由 key 集合推導目前已載入列的 dataIndex 選取清單，並以最新的列物件更新 key → row 對照 */
    _syncSelectedRowsFromKeys() {
        const selected = [];
        if (this._selectedKeyMap.size > 0 && this._rowKeyIds) {
            this._rowKeyIds.forEach((id, dataIndex) => {
                const entry = this._selectedKeyMap.get(id);
                if (!entry) return;
                entry.key = this._rowKeyValues[dataIndex];
                entry.row = this._sourceRow(dataIndex);
                selected.push(dataIndex);
            });
        }
        this._selectedRows = selected;
    }

    _setKeyedSelection(dataIndices, selected) {
        if (!this._rowKeyIds) return;
        dataIndices.forEach(dataIndex => {
            const id = this._rowKeyIds[dataIndex];
            if (id === undefined) return;
            if (selected) {
                this._selectedKeyMap.set(id, { key: this._rowKeyValues[dataIndex], row: this._sourceRow(dataIndex) });
            } else {
                this._selectedKeyMap.delete(id);
            }
        });
        this._syncSelectedRowsFromKeys();
    }

    _toggleKeyedRow(dataIndex, checked) {
        if (this.options.selectableRows === 'single') this._selectedKeyMap.clear();
        this._setKeyedSelection([dataIndex], checked);
        this._afterSelectionChange();
    }

    _selectAllKeyed(checked) {
        const { rows } = this._getKeyedSelectAllState();
        this._setKeyedSelection(rows.map(d => d.dataIndex), checked);
        this._afterSelectionChange();
    }

    /**
     * rowKey 模式表頭全選的作用範圍：預設為目前頁（快速篩選後）；selectAllScope: 'filtered'
     * 為快速篩選後的全部列。伺服器端模式只有已載入的這一頁可選。
     */
    _getSelectAllScopeRows(paginated = null, sorted = null) {
        const all = sorted || this._getSortedData();
        if (this._isServerSide() || !this._paginationEnabled || this._opt('selectAllScope') === 'filtered') return all;
        return paginated || this._getPaginatedData(all);
    }

    _getKeyedSelectAllState(paginated = null, sorted = null) {
        const rows = this._getSelectAllScopeRows(paginated, sorted);
        let selectedCount = 0;
        rows.forEach(({ dataIndex }) => {
            if (this._selectedKeyMap.has(this._rowKeyIds?.[dataIndex])) selectedCount++;
        });
        return { rows, all: rows.length > 0 && selectedCount === rows.length, some: selectedCount > 0 };
    }

    _syncKeyedSelectAll(selectAll) {
        const state = this._getKeyedSelectAllState();
        selectAll.checked = state.all;
        if (state.all) selectAll.setAttribute('checked', '');
        else selectAll.removeAttribute('checked');
        selectAll.indeterminate = state.some && !state.all;
    }

    // ══════════════════════════════════════════════════════════════
    // 欄位顯示切換
    // ══════════════════════════════════════════════════════════════

    _columnKey(col, colIdx) {
        return col && col.name !== undefined && col.name !== null ? String(col.name) : String(colIdx);
    }

    _findColumnIndex(key) {
        if (key === undefined || key === null) return -1;
        const wanted = String(key);
        const byName = this.columns.findIndex((col, colIdx) => this._columnKey(col, colIdx) === wanted);
        if (byName >= 0) return byName;
        return Number.isInteger(key) && key >= 0 && key < this.columns.length ? key : -1;
    }

    _isColumnHideable(col) {
        const hideable = col?.options?.hideable ?? col?.hideable;
        return hideable !== false;
    }

    _applyColumnVisibility(colIdx, visible) {
        const col = this.columns[colIdx];
        if (!col || !this._isColumnHideable(col)) return false;
        if (this._isColumnDisplayed(col, colIdx) === visible) return false;
        this._columnVisibility.set(this._columnKey(col, colIdx), visible);
        // 快速篩選只比對顯示中的欄位：重建索引，並移除因此不再符合篩選的選取（與篩選清除選取的原則一致）
        this._searchIndex = null;
        this._pruneSelectionToFilter();
        this._rerender();
        return true;
    }

    _pruneSelectionToFilter() {
        if (this._hasRowKey() || this._isServerSide() || this.options.search !== true) return;
        if (this._selectedRows.length === 0 || !normalizeSearchText(this._searchText)) return;
        const visible = new Set(this._getFilteredData().map(d => d.dataIndex));
        const next = this._selectedRows.filter(dataIndex => visible.has(dataIndex));
        if (next.length === this._selectedRows.length) return;
        this._selectedRows = next;
        this._fireSelectionChange();
    }

    _emitColumnVisibility() {
        const callback = this._opt('onColumnVisibilityChange');
        if (typeof callback === 'function') callback(this.getColumnVisibility());
    }

    _renderColumnToggleButton() {
        if (this._opt('columnToggle') !== true) return '';
        const label = this._label('columns', 'button', 'dataTable.columnToggle');
        const open = !!this._columnMenu;
        const controls = open ? ` aria-controls="${this._columnMenu.id}"` : '';
        return `<button type="button" class="b4a-dt__column-toggle" data-action="column-toggle" aria-haspopup="menu" aria-expanded="${open ? 'true' : 'false'}"${controls}>${escapeHtml(label)}<span class="b4a-dt__column-toggle-caret" aria-hidden="true">▾</span></button>`;
    }

    _bindColumnToggle(root) {
        const trigger = root.querySelector('[data-action="column-toggle"]');
        if (!trigger) return;
        trigger.addEventListener('click', () => {
            if (this._columnMenu) this._closeColumnMenu(true);
            else this._openColumnMenu('first');
        });
        trigger.addEventListener('keydown', event => {
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                const edge = event.key === 'ArrowUp' ? 'last' : 'first';
                if (this._columnMenu) this._focusColumnMenuItem(edge);
                else this._openColumnMenu(edge);
            } else if (event.key === 'Escape' && this._columnMenu) {
                event.preventDefault();
                this._closeColumnMenu(true);
            }
        });
    }

    /**
     * 欄位選單開啟時掛到 document.body 並以 position: fixed 依觸發鈕定位（表格每次切換欄位都會整表重繪，
     * 選單留在元件外才能保持開啟與焦點）；document / window 監聽只在開啟期間存在。
     */
    _openColumnMenu(focusEdge = 'first') {
        if (this._columnMenu || this._destroyed || typeof document === 'undefined') return;
        const trigger = this.element?.querySelector?.('[data-action="column-toggle"]');
        if (!trigger) return;

        const menu = document.createElement('div');
        menu.className = 'b4a-dt__column-menu';
        menu.id = `${this._ensureUid()}-columns`;
        menu.setAttribute('role', 'menu');
        menu.setAttribute('aria-label', this._label('columns', 'menu', 'dataTable.columnMenuLabel'));
        this.columns.forEach((col, colIdx) => {
            const visible = this._isColumnDisplayed(col, colIdx);
            const locked = !this._isColumnHideable(col);
            // hideable: false 且隱藏中的欄位（通常是只供 render 取值的資料欄）不列入選單
            if (locked && !visible) return;
            const item = document.createElement('div');
            item.className = 'b4a-dt__column-menu-item';
            item.setAttribute('role', 'menuitemcheckbox');
            item.setAttribute('aria-checked', visible ? 'true' : 'false');
            item.tabIndex = -1;
            item.dataset.colIndex = String(colIdx);
            if (locked) item.setAttribute('aria-disabled', 'true');
            const check = document.createElement('span');
            check.className = 'b4a-dt__column-menu-check';
            check.setAttribute('aria-hidden', 'true');
            check.textContent = visible ? '✓' : '';
            const text = document.createElement('span');
            text.className = 'b4a-dt__column-menu-label';
            text.textContent = String(col.label || col.name || '');
            item.append(check, text);
            menu.appendChild(item);
        });

        const handlers = {
            keydown: event => this._onColumnMenuKeydown(event),
            click: event => this._onColumnMenuClick(event),
            outside: event => this._onColumnMenuOutsideClick(event),
            reposition: () => this._positionColumnMenu(),
        };
        menu.addEventListener('keydown', handlers.keydown);
        menu.addEventListener('click', handlers.click);
        document.body.appendChild(menu);
        document.addEventListener('click', handlers.outside);
        window.addEventListener('resize', handlers.reposition);
        window.addEventListener('scroll', handlers.reposition, true);

        this._columnMenu = menu;
        this._columnMenuHandlers = handlers;
        this._columnMenuTrigger = trigger;
        trigger.setAttribute('aria-expanded', 'true');
        trigger.setAttribute('aria-controls', menu.id);
        this._positionColumnMenu();
        this._focusColumnMenuItem(focusEdge);
    }

    _closeColumnMenu(restoreFocus = false) {
        const menu = this._columnMenu;
        if (!menu) return;
        const handlers = this._columnMenuHandlers;
        if (handlers) {
            menu.removeEventListener('keydown', handlers.keydown);
            menu.removeEventListener('click', handlers.click);
            document.removeEventListener('click', handlers.outside);
            window.removeEventListener('resize', handlers.reposition);
            window.removeEventListener('scroll', handlers.reposition, true);
        }
        menu.remove();
        this._columnMenu = null;
        this._columnMenuHandlers = null;
        this._columnMenuTrigger = null;
        const trigger = this.element?.querySelector?.('[data-action="column-toggle"]');
        if (!trigger) return;
        trigger.setAttribute('aria-expanded', 'false');
        trigger.removeAttribute('aria-controls');
        if (restoreFocus) trigger.focus?.();
    }

    _columnMenuItems() {
        return this._columnMenu ? [...this._columnMenu.querySelectorAll('[role="menuitemcheckbox"]')] : [];
    }

    _focusColumnMenuItem(edge) {
        const items = this._columnMenuItems();
        const target = edge === 'last' ? items[items.length - 1] : items[0];
        target?.focus?.();
    }

    _onColumnMenuKeydown(event) {
        const items = this._columnMenuItems();
        const current = items.indexOf(event.target?.closest?.('[role="menuitemcheckbox"]'));
        const move = delta => {
            if (items.length === 0) return;
            const next = current < 0
                ? (delta > 0 ? 0 : items.length - 1)
                : (current + delta + items.length) % items.length;
            items[next].focus();
        };
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                move(1);
                break;
            case 'ArrowUp':
                event.preventDefault();
                move(-1);
                break;
            case 'Home':
                event.preventDefault();
                items[0]?.focus();
                break;
            case 'End':
                event.preventDefault();
                items[items.length - 1]?.focus();
                break;
            case 'Enter':
            case ' ':
            case 'Spacebar':
                // 勾選型選單項目：切換後保持開啟，方便連續調整多個欄位
                event.preventDefault();
                if (current >= 0) this._toggleColumnMenuItem(items[current]);
                break;
            case 'Escape':
                event.preventDefault();
                // 避免外層（例如對話框）的 Escape 監聽同時關閉
                event.stopPropagation();
                this._closeColumnMenu(true);
                break;
            case 'Tab':
                // 焦點交回觸發鈕後不攔截預設行為，瀏覽器會從觸發鈕繼續 Tab 順序
                this._closeColumnMenu(true);
                break;
            default:
                break;
        }
    }

    _onColumnMenuClick(event) {
        const item = event.target?.closest?.('[role="menuitemcheckbox"]');
        if (!item || !this._columnMenu?.contains(item)) return;
        this._toggleColumnMenuItem(item);
    }

    _onColumnMenuOutsideClick(event) {
        const menu = this._columnMenu;
        const target = event.target;
        if (!menu || menu.contains(target)) return;
        // 觸發鈕自己的 click 會處理開關；開啟當下同步掛上的監聽對這次點擊也應 no-op
        const trigger = target?.closest?.('[data-action="column-toggle"]');
        if (trigger && this.element?.contains(trigger)) return;
        this._closeColumnMenu(false);
    }

    _toggleColumnMenuItem(item) {
        if (!item || item.getAttribute('aria-disabled') === 'true') return;
        const colIdx = Number(item.dataset.colIndex);
        const col = this.columns[colIdx];
        if (!col) return;
        const changed = this._applyColumnVisibility(colIdx, !this._isColumnDisplayed(col, colIdx));
        this._syncColumnMenuItems();
        item.focus?.();
        if (changed) this._emitColumnVisibility();
    }

    _syncColumnMenuItems() {
        this._columnMenuItems().forEach(item => {
            const colIdx = Number(item.dataset.colIndex);
            const col = this.columns[colIdx];
            const visible = !!col && this._isColumnDisplayed(col, colIdx);
            item.setAttribute('aria-checked', visible ? 'true' : 'false');
            const check = item.querySelector('.b4a-dt__column-menu-check');
            if (check) check.textContent = visible ? '✓' : '';
        });
    }

    /** 選單開啟中整表重繪：改指向新的觸發鈕、同步勾選狀態並重新定位 */
    _syncColumnMenuAfterRender() {
        const trigger = this.element?.querySelector?.('[data-action="column-toggle"]');
        if (!trigger) {
            this._closeColumnMenu(false);
            return;
        }
        this._columnMenuTrigger = trigger;
        this._syncColumnMenuItems();
        this._positionColumnMenu();
    }

    _positionColumnMenu() {
        const menu = this._columnMenu;
        const trigger = this._columnMenuTrigger;
        if (!menu || !trigger || trigger.isConnected === false) return;
        const margin = 4;
        const viewportTop = 8;
        const viewportBottom = Math.max(viewportTop, (window.innerHeight || 0) - 8);
        const viewportRight = Math.max(0, (window.innerWidth || 0) - 8);
        const anchor = trigger.getBoundingClientRect();

        menu.style.position = 'fixed';
        menu.style.zIndex = '10050';
        menu.style.maxHeight = '';
        const rect = menu.getBoundingClientRect();
        const width = rect.width || menu.scrollWidth || 0;
        const height = rect.height || menu.scrollHeight || 0;
        const spaceBelow = viewportBottom - anchor.bottom - margin;
        const spaceAbove = anchor.top - viewportTop - margin;
        const placeAbove = height > spaceBelow && spaceAbove > spaceBelow;
        const top = placeAbove ? Math.max(viewportTop, anchor.top - margin - height) : anchor.bottom + margin;
        // 右緣對齊觸發鈕（位於工具列右側），並夾在視窗內
        const left = Math.max(8, Math.min(anchor.right - width, viewportRight - width));
        menu.style.top = `${Math.round(top)}px`;
        menu.style.left = `${Math.round(left)}px`;
        menu.style.maxHeight = `${Math.max(120, Math.round(placeAbove ? spaceAbove : spaceBelow))}px`;
        menu.dataset.placement = placeAbove ? 'top' : 'bottom';
    }

    // ══════════════════════════════════════════════════════════════
    // 列展開明細
    // ══════════════════════════════════════════════════════════════

    _expandId(dataIndex) {
        return this._rowKeyIds ? this._rowKeyIds[dataIndex] : String(dataIndex);
    }

    _expandKeyValue(dataIndex) {
        return this._rowKeyValues ? this._rowKeyValues[dataIndex] : dataIndex;
    }

    _dataIndexForExpandId(id) {
        if (this._rowKeyIndex) {
            const dataIndex = this._rowKeyIndex.get(id);
            return dataIndex === undefined ? -1 : dataIndex;
        }
        if (!/^\d+$/.test(id)) return -1;
        const dataIndex = Number(id);
        return dataIndex < this.data.length ? dataIndex : -1;
    }

    _isRowExpandable(expandable, dataIndex) {
        if (typeof expandable.rowExpandable !== 'function') return true;
        try {
            return Boolean(expandable.rowExpandable(this._sourceRow(dataIndex), dataIndex));
        } catch {
            return false;
        }
    }

    _detailRowId(dataIndex) {
        return `${this._ensureUid()}-detail-${dataIndex}`;
    }

    _renderExpandHeader() {
        const label = this._label('expand', 'header', 'dataTable.expandColumn');
        return `<th class="b4a-dt__th b4a-dt__th--expand"><span class="b4a-dt__sr-only">${escapeHtml(label)}</span></th>`;
    }

    _renderExpandCell(expandable, dataIndex) {
        if (!this._isRowExpandable(expandable, dataIndex)) {
            return { html: '<td class="b4a-dt__td b4a-dt__td--expand"></td>', detailId: null };
        }
        const expanded = this._expandedKeys.has(this._expandId(dataIndex));
        const detailId = expanded ? this._detailRowId(dataIndex) : null;
        return {
            html: `<td class="b4a-dt__td b4a-dt__td--expand">${this._expandButtonHtml(dataIndex, detailId)}</td>`,
            detailId,
        };
    }

    _expandButtonHtml(dataIndex, detailId) {
        const label = escapeHtml(this._label('expand', 'toggle', 'dataTable.toggleRowDetails'));
        const expanded = detailId !== null;
        const controls = expanded ? ` aria-controls="${detailId}"` : '';
        return `<button type="button" class="b4a-dt__expand-btn" data-action="toggle-expand" data-index="${dataIndex}" aria-expanded="${expanded ? 'true' : 'false'}"${controls} aria-label="${label}" title="${label}"><span class="b4a-dt__expand-icon" aria-hidden="true">${expanded ? '▾' : '▸'}</span></button>`;
    }

    _bindExpandable(root, expandable) {
        const tbody = this._ownTbody();
        if (!tbody) return;
        // 只綁本表自己的列，避免命中儲存格內巢狀表格的展開鈕
        for (const tr of tbody.rows) {
            const button = tr.querySelector(':scope > .b4a-dt__td--expand > [data-action="toggle-expand"]');
            button?.addEventListener('click', () => {
                this._toggleExpandByIndex(parseInt(button.getAttribute('data-index'), 10));
            });
        }
        if (expandable.expandOnRowClick !== true) return;
        tbody.addEventListener('click', event => {
            const target = event.target;
            const tr = target?.closest?.('tr');
            if (!tr || tr.parentElement !== tbody || !tr.hasAttribute('data-row-index')) return;
            const interactive = target.closest(ROW_CLICK_IGNORE_SELECTOR);
            if (interactive && tr.contains(interactive)) return;
            // 拖曳選取文字時不切換
            const selection = typeof window !== 'undefined' ? window.getSelection?.() : null;
            if (selection && !selection.isCollapsed && tr.contains(selection.anchorNode)) return;
            this._toggleExpandByIndex(parseInt(tr.getAttribute('data-row-index'), 10));
        });
    }

    _toggleExpandByIndex(dataIndex) {
        if (!Number.isInteger(dataIndex) || dataIndex < 0 || dataIndex >= this.data.length) return;
        const id = this._expandId(dataIndex);
        this._setRowExpanded(this._expandKeyValue(dataIndex), !this._expandedKeys.has(id));
    }

    _setRowExpanded(key, expanded) {
        if (key === undefined || key === null) return this;
        const id = String(key);
        if (this._expandedKeys.has(id) === expanded) return this;
        const dataIndex = this._dataIndexForExpandId(id);
        // 未設定 rowKey 時 key 就是 dataIndex：不存在的索引之後也不會出現，直接忽略；
        // 設定 rowKey 時保留尚未載入的 key，該列載入後即呈現展開
        if (expanded && dataIndex < 0 && !this._rowKeyIndex) return this;
        const expandable = this._getExpandable();
        if (expanded && dataIndex >= 0 && expandable && !this._isRowExpandable(expandable, dataIndex)) return this;
        if (expanded) this._expandedKeys.set(id, dataIndex >= 0 ? this._expandKeyValue(dataIndex) : key);
        else this._expandedKeys.delete(id);
        if (dataIndex >= 0 && expandable) this._syncExpandedRowDom(dataIndex, expanded, expandable);
        return this;
    }

    /** 就地插入/移除明細列並更新展開鈕狀態（不整表重繪，焦點留在展開鈕上） */
    _syncExpandedRowDom(dataIndex, expanded, expandable) {
        const tbody = this._ownTbody();
        if (!tbody) return;
        const tr = [...tbody.rows].find(row => row.getAttribute('data-row-index') === String(dataIndex));
        if (!tr) return;
        const next = tr.nextElementSibling;
        const existing = next && next.classList.contains('b4a-dt__detail-row') ? next : null;
        const button = tr.querySelector(':scope > .b4a-dt__td--expand > [data-action="toggle-expand"]');
        const icon = button?.querySelector('.b4a-dt__expand-icon');
        if (expanded) {
            const detailId = this._detailRowId(dataIndex);
            if (!existing) {
                const detail = document.createElement('tr');
                detail.className = 'b4a-dt__detail-row';
                detail.id = detailId;
                detail.setAttribute('data-detail-for', String(dataIndex));
                const cell = document.createElement('td');
                cell.className = 'b4a-dt__td b4a-dt__detail-cell';
                cell.colSpan = tr.cells.length;
                detail.appendChild(cell);
                tr.parentNode.insertBefore(detail, tr.nextSibling);
                this._fillDetailCell(cell, expandable, dataIndex);
            }
            button?.setAttribute('aria-expanded', 'true');
            button?.setAttribute('aria-controls', detailId);
            if (icon) icon.textContent = '▾';
        } else {
            existing?.remove();
            button?.setAttribute('aria-expanded', 'false');
            button?.removeAttribute('aria-controls');
            if (icon) icon.textContent = '▸';
        }
    }

    _fillDetailRows(expandable) {
        const tbody = this._ownTbody();
        if (!tbody) return;
        for (const tr of tbody.rows) {
            if (!tr.classList.contains('b4a-dt__detail-row')) continue;
            this._fillDetailCell(tr.cells[0], expandable, parseInt(tr.getAttribute('data-detail-for'), 10));
        }
    }

    /** render 回傳 DOM 節點時直接插入；其他值（含字串）一律以 textContent 呈現，不解析 HTML */
    _fillDetailCell(cell, expandable, dataIndex) {
        if (!cell) return;
        let content = null;
        try {
            content = expandable.render(this._sourceRow(dataIndex), dataIndex);
        } catch (error) {
            console.error('[DataTable] expandable.render 發生錯誤', error);
        }
        cell.textContent = '';
        if (content === null || content === undefined || content === false) return;
        if (isDomNode(content)) {
            cell.appendChild(content);
            return;
        }
        cell.textContent = isRawHtml(content) ? content.__html : String(content);
    }

    // ══════════════════════════════════════════════════════════════
    // 固定欄（sticky columns）
    // ══════════════════════════════════════════════════════════════

    _columnSticky(colIdx) {
        const col = this.columns[colIdx];
        const sticky = col?.options?.sticky ?? col?.sticky;
        return sticky === 'left' || sticky === 'right' ? sticky : null;
    }

    _declaredColumnWidth(colIdx) {
        const col = this.columns[colIdx];
        let style = null;
        try {
            style = col?.options?.setCellProps?.()?.style || null;
        } catch {
            style = null;
        }
        return parsePixelLength(style?.width) || parsePixelLength(style?.minWidth) || parsePixelLength(col?.width);
    }

    /**
     * sticky 欄必須從左右兩端連續排列，不連續的宣告忽略；左側有固定欄時，前導的展開鈕欄與勾選欄一併固定。
     * 偏移量優先採用實際量測的欄寬，尚未排版量不到時用宣告寬度（px）；表格尺寸改變時以 ResizeObserver 重新量測。
     */
    _applyStickyColumns(visibleCols, { isSelectable, expandable }) {
        const left = [];
        for (const colIdx of visibleCols) {
            if (this._columnSticky(colIdx) !== 'left') break;
            left.push(colIdx);
        }
        const right = [];
        for (let i = visibleCols.length - 1; i >= left.length; i--) {
            if (this._columnSticky(visibleCols[i]) !== 'right') break;
            right.unshift(visibleCols[i]);
        }
        if (left.length === 0 && right.length === 0) {
            this._stickyLayout = null;
            this._disconnectStickyObserver();
            return;
        }
        const leading = [];
        if (expandable) leading.push(EXPAND_COLUMN_FALLBACK_WIDTH);
        if (isSelectable) leading.push(SELECT_COLUMN_FALLBACK_WIDTH);
        const leftCells = left.length === 0 ? [] : [
            ...leading.map((declared, pos) => ({ pos, declared })),
            ...left.map((colIdx, i) => ({ pos: leading.length + i, declared: this._declaredColumnWidth(colIdx) })),
        ];
        const firstRight = leading.length + visibleCols.length - right.length;
        const rightCells = right.map((colIdx, i) => ({ pos: firstRight + i, declared: this._declaredColumnWidth(colIdx) }));
        this._stickyLayout = { left: leftCells, right: rightCells };
        this._measureAndApplySticky();
        this._observeStickyTable();
    }

    _measureAndApplySticky() {
        const layout = this._stickyLayout;
        const table = this._ownTable();
        if (!layout || !table) return;
        const headRow = table.tHead?.rows?.[0] || null;
        const bodyRows = table.tBodies?.[0]
            ? [...table.tBodies[0].rows].filter(tr => tr.hasAttribute('data-row-index'))
            : [];
        const rows = headRow ? [headRow, ...bodyRows] : bodyRows;
        // 先讀完全部寬度再寫入樣式，避免版面反覆重算
        const widthOf = ({ pos, declared }) => {
            const measured = headRow?.cells?.[pos]?.getBoundingClientRect?.().width || 0;
            return measured > 0 ? measured : declared;
        };
        const leftOffsets = [];
        let offset = 0;
        layout.left.forEach(cell => {
            leftOffsets.push([cell.pos, offset]);
            offset += widthOf(cell);
        });
        const rightOffsets = [];
        offset = 0;
        for (let i = layout.right.length - 1; i >= 0; i--) {
            rightOffsets.push([layout.right[i].pos, offset]);
            offset += widthOf(layout.right[i]);
        }
        const leftEdge = layout.left.length ? layout.left[layout.left.length - 1].pos : -1;
        const rightEdge = layout.right.length ? layout.right[0].pos : -1;
        const px = value => `${Math.round(value * 100) / 100}px`;
        rows.forEach(tr => {
            leftOffsets.forEach(([pos, value]) => {
                const cell = tr.cells[pos];
                if (!cell) return;
                cell.classList.add('b4a-dt__cell--sticky-left');
                cell.classList.toggle('b4a-dt__cell--sticky-left-edge', pos === leftEdge);
                cell.style.left = px(value);
            });
            rightOffsets.forEach(([pos, value]) => {
                const cell = tr.cells[pos];
                if (!cell) return;
                cell.classList.add('b4a-dt__cell--sticky-right');
                cell.classList.toggle('b4a-dt__cell--sticky-right-edge', pos === rightEdge);
                cell.style.right = px(value);
            });
        });
    }

    _observeStickyTable() {
        if (this._destroyed || typeof ResizeObserver !== 'function') return;
        const table = this._ownTable();
        if (!table || this._observedTable === table) return;
        if (this._resizeObserver) {
            this._resizeObserver.disconnect();
        } else {
            this._resizeObserver = new ResizeObserver(() => {
                if (!this._destroyed && this._stickyLayout) this._measureAndApplySticky();
            });
        }
        this._resizeObserver.observe(table);
        this._observedTable = table;
    }

    _disconnectStickyObserver() {
        if (this._resizeObserver) this._resizeObserver.disconnect();
        this._resizeObserver = null;
        this._observedTable = null;
    }
}

export default DataTable;
