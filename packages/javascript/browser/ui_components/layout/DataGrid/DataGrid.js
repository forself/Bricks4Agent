/**
 * DataGrid — 試算表式可編輯資料格，用於大量資料輸入。
 *
 * - ARIA grid 模式：role="grid"、aria-rowcount / aria-colcount、aria-rowindex / aria-colindex；
 *   同一時間只有一個作用中儲存格可經 Tab 抵達（roving tabindex）。
 * - 依欄位型別切換編輯器（text / number / select / date），checkbox 以空白鍵切換；
 *   逐格驗證、與基準資料比對的修改標記、TSV 剪貼簿貼上與複製、Shift+方向鍵矩形選取。
 * - virtual: true 時只渲染可視範圍的列；未開啟時一次渲染，之後只更新有變動的儲存格。
 *
 * 安全與樣式：儲存格內容一律以 textContent 寫入（format 的輸出也是純文字）；
 * 樣式只用 CSSOM 與 --cl-* token，不注入 <style>、不寫行內 style 屬性字串（CSP 合規）。
 *
 * 資料列保存在一般陣列，不放進 component-state，避免每次編輯都複製整份資料；
 * component-state 只保存生命週期、停用、作用中儲存格與編輯中等小型 UI 狀態。
 */
import Locale from '../../i18n/index.js';
import './locale.js';
import { createComponentState } from '../../utils/component-state.js';
import { setFieldError, clearFieldError, FIELD_ERROR_CONTRACT } from '../../utils/field-error.js';
import { nextUid } from '../../utils/uid.js';

const COLUMN_TYPES = Object.freeze(['text', 'number', 'select', 'date', 'checkbox']);
const DEFAULT_TRACK_MIN = Object.freeze({ text: 140, number: 100, select: 140, date: 150, checkbox: 72 });
const ROW_NUMBER_WIDTH = 52;
const OVERSCAN = 6;
const DEFAULT_VIRTUAL_HEIGHT = 400;
const FALLBACK_PAGE_ROWS = 10;
const WIDTH_PATTERN = /^\d+(?:\.\d+)?(?:px|rem|em|%|ch|fr|vw)$/;
const NUMBER_PATTERN = /^[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:e[+-]?\d+)?$/i;
const DATE_PATTERN = /^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})$/;
const COMPACT_DATE_PATTERN = /^(\d{4})(\d{2})(\d{2})$/;
const CURRENT_OPTION = '__current__';
const INVALID = Symbol('invalid');
const TRUE_WORDS = new Set(['true', '1', 'yes', 'y', 'on', 'v', 'x', '✓', '✔', '是', 'checked']);
const FALSE_WORDS = new Set(['', 'false', '0', 'no', 'n', 'off', '否', 'unchecked']);

const SR_ONLY_CSS = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap;';
const ROOT_CSS = 'position:relative;display:flex;flex-direction:column;gap:4px;box-sizing:border-box;width:100%;min-width:0;font-family:var(--cl-font-family);font-size:var(--cl-font-size-md);color:var(--cl-text);';
const FRAME_CSS = 'position:relative;box-sizing:border-box;width:100%;max-width:100%;overflow:auto;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);background:var(--cl-bg);';
const GRID_CSS = 'position:relative;box-sizing:border-box;width:100%;';
const HEAD_CSS = 'position:sticky;top:0;z-index:3;';
const HEADER_ROW_CSS = 'display:grid;box-sizing:border-box;background:var(--cl-bg-secondary);border-bottom:1px solid var(--cl-border);';
const HEADER_CELL_CSS = 'box-sizing:border-box;display:flex;align-items:center;gap:2px;min-width:0;padding:0 8px;font-size:var(--cl-font-size-sm);font-weight:600;color:var(--cl-text-secondary);background:var(--cl-bg-secondary);border-right:1px solid var(--cl-border-light);overflow:hidden;white-space:nowrap;';
const CORNER_CSS = 'position:sticky;left:0;z-index:1;';
const LABEL_CSS = 'overflow:hidden;text-overflow:ellipsis;';
const REQUIRED_MARK_CSS = 'color:var(--cl-danger);';
const BODY_CSS = 'position:relative;';
const ROW_CSS = 'display:grid;box-sizing:border-box;border-bottom:1px solid var(--cl-border-light);background:var(--cl-bg);';
const ROWNUM_CSS = 'position:sticky;left:0;z-index:2;box-sizing:border-box;display:flex;align-items:center;justify-content:flex-end;padding:0 8px;font-size:var(--cl-font-size-sm);color:var(--cl-text-muted);background:var(--cl-bg-secondary);border-right:1px solid var(--cl-border);font-variant-numeric:tabular-nums;';
const CELL_CSS = 'position:relative;box-sizing:border-box;display:flex;align-items:center;min-width:0;padding:0 8px;border-right:1px solid var(--cl-border-light);overflow:hidden;white-space:nowrap;cursor:cell;outline:none;';
const VALUE_CSS = 'flex:1 1 auto;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
const CHECK_CSS = 'display:inline-flex;align-items:center;justify-content:center;box-sizing:border-box;width:16px;height:16px;border:1px solid var(--cl-border-dark);border-radius:var(--cl-radius-xs);background:var(--cl-bg);color:var(--cl-text-inverse);font-size:var(--cl-font-size-xs);line-height:1;cursor:pointer;vertical-align:middle;';
const MARK_CSS = 'position:absolute;top:0;right:0;width:0;height:0;border-style:solid;border-width:0 8px 8px 0;border-color:transparent var(--cl-warning) transparent transparent;pointer-events:none;';
const EDITOR_CSS = 'position:absolute;top:0;left:0;right:0;bottom:0;z-index:1;box-sizing:border-box;width:100%;height:100%;margin:0;padding:0 6px;border:2px solid var(--cl-primary);border-radius:0;outline:none;font:inherit;color:var(--cl-text);background:var(--cl-bg);';
const EMPTY_CSS = 'padding:16px;text-align:center;color:var(--cl-text-muted);font-size:var(--cl-font-size-md);';
const STATUS_CSS = 'font-size:var(--cl-font-size-sm);line-height:1.4;color:var(--cl-danger);';

const INITIAL_CELL_STATE = Object.freeze({
    text: '', check: null, readonly: false, error: '', modified: false, selected: false,
    active: false, focused: false, disabled: false, bg: '', shadow: '', color: '', cursor: '',
});

const isEmpty = (value) => value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
const clamp = (value, min, max) => Math.min(Math.max(value, min), max);
const pad2 = (value) => String(value).padStart(2, '0');
const normalizeText = (value) => String(value ?? '').normalize('NFKC').trim().toLocaleLowerCase();
const inRect = (rect, row, col) => Boolean(rect) && row >= rect.top && row <= rect.bottom && col >= rect.left && col <= rect.right;

function focusWithoutScroll(element) {
    if (!element || typeof element.focus !== 'function') return;
    try {
        element.focus({ preventScroll: true });
    } catch (error) {
        element.focus();
    }
}

function safeJson(value) {
    try {
        return JSON.stringify(value);
    } catch (error) {
        return String(value);
    }
}

/** 數字解析：容許千分位逗號與全形數字；空值回傳 null，無法解析回傳 NaN。 */
function parseNumber(raw) {
    if (typeof raw === 'number') return Number.isFinite(raw) ? raw : NaN;
    if (raw === null || raw === undefined) return null;
    const text = String(raw).normalize('NFKC').trim().replace(/,/g, '');
    if (text === '') return null;
    return NUMBER_PATTERN.test(text) ? Number(text) : NaN;
}

/** 依小數位數四捨五入（以指數表示避免 1.005 之類的浮點誤差）。 */
function roundTo(value, precision) {
    if (precision === null || precision === undefined || !Number.isFinite(value)) return value;
    const shifted = Math.round(Number(`${value}e${precision}`));
    const rounded = Number(`${shifted}e-${precision}`);
    const result = Number.isFinite(rounded) ? rounded : Number(value.toFixed(precision));
    return Object.is(result, -0) ? 0 : result;
}

function clampToLimits(value, min, max) {
    if (typeof value !== 'number' || Number.isNaN(value)) return value;
    const lower = min === null ? value : Math.max(value, min);
    return max === null ? lower : Math.min(lower, max);
}

/** 日期解析：接受 YYYY-MM-DD、YYYY/M/D、YYYY.M.D、YYYYMMDD 與 Date；回傳 ISO 字串、null（空值）或 INVALID。 */
function parseDate(raw) {
    if (raw instanceof Date) return Number.isNaN(raw.getTime()) ? INVALID : `${raw.getFullYear()}-${pad2(raw.getMonth() + 1)}-${pad2(raw.getDate())}`;
    if (raw === null || raw === undefined) return null;
    const text = String(raw).normalize('NFKC').trim();
    if (text === '') return null;
    const match = DATE_PATTERN.exec(text) || COMPACT_DATE_PATTERN.exec(text);
    if (!match) return INVALID;
    const month = Number(match[2]);
    const day = Number(match[3]);
    const daysInMonth = new Date(Date.UTC(Number(match[1]), month, 0)).getUTCDate();
    return month >= 1 && month <= 12 && day >= 1 && day <= daysInMonth ? `${match[1]}-${pad2(month)}-${pad2(day)}` : INVALID;
}

/** 布林解析：true/false、1/0、yes/no、是/否、✓ 等；空值視為 false，無法辨識回傳 INVALID。 */
function parseBoolean(raw) {
    if (typeof raw === 'boolean') return raw;
    if (raw === null || raw === undefined) return false;
    if (typeof raw === 'number') return raw === 1 ? true : (raw === 0 ? false : INVALID);
    const text = String(raw).normalize('NFKC').trim().toLowerCase();
    return TRUE_WORDS.has(text) ? true : (FALSE_WORDS.has(text) ? false : INVALID);
}

/** 寬鬆相等：空值（null、undefined、空白字串）彼此相等，其餘以字串比較（1 與 '1' 相等）。 */
function sameValue(a, b) {
    const blankA = isEmpty(a);
    const blankB = isEmpty(b);
    if (blankA || blankB) return blankA && blankB;
    if (typeof a === 'number' && typeof b === 'number') return a === b || (Number.isNaN(a) && Number.isNaN(b));
    if (typeof a === 'object' || typeof b === 'object') return safeJson(a) === safeJson(b);
    return String(a) === String(b);
}

function normalizeOptions(options) {
    return (Array.isArray(options) ? options : [])
        .filter((option) => option !== null && option !== undefined)
        .map((option) => (typeof option === 'object'
            ? { value: option.value, label: option.label === undefined || option.label === null ? String(option.value ?? '') : String(option.label) }
            : { value: option, label: String(option) }));
}

function findOption(column, value) {
    const needle = String(value);
    return column.options.find((option) => String(option.value) === needle) || null;
}

/** 貼上時的選項比對：先比 value，再以不分大小寫比 label 與 value。 */
function matchOption(column, text) {
    const needle = normalizeText(text);
    return findOption(column, text)
        || column.options.find((option) => normalizeText(option.label) === needle || normalizeText(option.value) === needle)
        || null;
}

const isBlankCell = (column, value) => (column.type === 'checkbox' ? parseBoolean(value) === false : isEmpty(value));

function isValidWidth(width) {
    const text = String(width).trim();
    return /^\d+(?:\.\d+)?$/.test(text) || WIDTH_PATTERN.test(text);
}

function trackFor(column) {
    const width = column.width;
    const text = typeof width === 'string' ? width.trim() : '';
    if (typeof width === 'number' && Number.isFinite(width) && width > 0) return `${width}px`;
    if (/^\d+(?:\.\d+)?$/.test(text)) return `${text}px`;
    if (WIDTH_PATTERN.test(text)) return text;
    return `minmax(${DEFAULT_TRACK_MIN[column.type]}px, 1fr)`;
}

function trackMinPx(column) {
    const width = column.width;
    if (typeof width === 'number' && Number.isFinite(width) && width > 0) return width;
    const match = typeof width === 'string' ? /^(\d+(?:\.\d+)?)(?:px)?$/.exec(width.trim()) : null;
    return match ? Number(match[1]) : DEFAULT_TRACK_MIN[column.type];
}

function parsePx(value) {
    const match = /^(\d+(?:\.\d+)?)px$/.exec(String(value || '').trim());
    return match ? Number(match[1]) : 0;
}

function toCssLength(value) {
    if (typeof value === 'number' && Number.isFinite(value) && value > 0) return `${value}px`;
    return typeof value === 'string' ? value.trim() : '';
}

export class DataGrid {
    static COLUMN_TYPES = COLUMN_TYPES;

    constructor(options = {}) {
        this.options = {
            columns: [],                  // 欄位定義：[{ key, label, type, width, align, required, readonly, min, max, precision, maxLength, options, validate, format, compute }]
            rows: [],                     // 初始資料列（會複製，不改動呼叫端物件）
            rowKey: 'id',                 // 列鍵欄位名稱，或 (row) => key；缺少列鍵的列使用內部產生的鍵
            height: null,                 // CSS 長度（數字視為 px）；設定後在內部捲動並固定表頭
            virtual: false,               // true 時只渲染可視範圍的列（大量資料用；未設 height 時使用 400px）
            rowHeight: 34,                // 列高（px，20–200）；virtual 以此計算列位置
            disabled: false,              // 停用：不可編輯、貼上、清除，仍可瀏覽與複製
            showRowNumbers: false,        // 顯示列號欄（role="rowheader"）
            allowAddRowsOnPaste: false,   // 貼上超過最後一列時自動新增列；false 時截斷
            ariaLabel: '',                // 表格的無障礙名稱；空字串時使用語系預設
            onCellChange: null,           // ({ rowKey, key, value, oldValue, row }) => void，使用者變更儲存格時觸發
            onChange: null,               // (rows) => void，使用者操作造成資料變更後觸發一次
            onValidationChange: null,     // (errors) => void，錯誤清單內容改變時觸發
            onPaste: null,                // ({ startRowKey, startKey, rows, cols, appliedRows, appliedCols, addedRows, truncatedRows, data }) => void
            ...options
        };

        this._uid = nextUid('b4a-datagrid');
        this._columns = this._normalizeColumns(this.options.columns);
        this._colIndexByKey = new Map(this._columns.map((column, index) => [column.key, index]));
        this._computed = this._columns.filter((column) => column.compute);
        this._needsRowCopy = this._columns.some((column) => typeof column.readonly === 'function' || column.format);
        this._colOffset = this.options.showRowNumbers ? 1 : 0;
        const rowHeight = Number(this.options.rowHeight);
        this._rowHeight = Number.isFinite(rowHeight) ? clamp(Math.round(rowHeight), 20, 200) : 34;
        this._virtual = Boolean(this.options.virtual);
        this._heightCss = toCssLength(this.options.height) || (this._virtual ? `${DEFAULT_VIRTUAL_HEIGHT}px` : '');
        this._scrollsInternally = Boolean(this._heightCss);

        this._records = [];
        this._byKey = new Map();
        this._indexByKey = new Map();
        this._baseline = new Map();
        this._baselineOrder = [];
        this._errors = new Map();
        this._errorSignature = '';
        this._keySeq = 0;
        this._recordSeq = 0;

        this._rendered = new Map();
        this._edit = null;
        this._selectAll = false;
        this._focusWithin = false;
        this._dragging = false;
        this._destroyed = false;
        this._frameRequest = null;
        this._copyTimer = null;
        this._copyPending = false;
        this._resizeObserver = null;

        this._ui = createComponentState({
            lifecycle: 'created',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            active: null,
            anchor: null,
            editing: false,
        }, {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed', editing: false }),
            SET_DISABLED: (state, payload) => ({ ...state, availability: payload?.disabled ? 'disabled' : 'enabled' }),
            SET_ACTIVE: (state, payload) => ({ ...state, active: payload?.active ?? null, anchor: payload?.anchor ?? payload?.active ?? null }),
            SET_EDITING: (state, payload) => ({ ...state, editing: Boolean(payload?.editing) }),
        });

        this._bindHandlers();
        this._createDom();
        this._ingest(this.options.rows);
        this._captureBaseline();
        this._resetActive(null);
        this._render();
    }

    // ── 公開 API ────────────────────────────────────────────

    /** 狀態快照（生命週期、停用、作用中儲存格、編輯中）。 */
    snapshot() {
        return this._ui.snapshot();
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) {
            console.warn('[DataGrid] mount target not found:', container);
            return this;
        }
        target.appendChild(this.element);
        if (!this._destroyed) {
            this._ui.send('MOUNT');
            if (this._virtual) this._renderWindow();
        }
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        if (this._edit) this._teardownEditor(false);
        this._destroyed = true;
        this._stopDrag();
        this._cancelScheduled();
        if (this._copyTimer) {
            clearTimeout(this._copyTimer);
            this._copyTimer = null;
        }
        if (this._resizeObserver) {
            this._resizeObserver.disconnect();
            this._resizeObserver = null;
        }
        this._gridEvents.forEach(([type, handler]) => this._grid.removeEventListener(type, handler));
        if (this._virtual) this._frame.removeEventListener('scroll', this._onScroll);
        clearFieldError(this);
        this._ui.send('DESTROY');
        this._rendered.clear();
        this.element?.remove();
    }

    /** 目前所有資料列（乾淨的淺層複本，不含內部欄位）。 */
    getRows() {
        return this._records.map((record) => ({ ...record.data }));
    }

    /** 取代全部資料列並以其作為新的基準（清除修改標記與驗證錯誤）。 */
    setRows(rows) {
        if (this._edit) this._teardownEditor(false);
        const hadFocus = this._focusWithin && !this._destroyed;
        const previous = this._activePosition();
        this._ingest(rows);
        this._captureBaseline();
        this._errors = new Map();
        this._selectAll = false;
        this._resetActive(previous);
        this._render();
        if (hadFocus) this._focusActive();
        this._emitValidation();
        return this;
    }

    getRow(key) {
        const record = this._byKey.get(String(key));
        return record ? { ...record.data } : null;
    }

    /** 以 patch 合併更新一列（程式呼叫，不觸發 onCellChange / onChange）；找不到該列時回傳 false。 */
    updateRow(key, patch) {
        const record = this._byKey.get(String(key));
        if (!record || !patch || typeof patch !== 'object') return false;
        this._applyPatches([{ record, patch: { ...patch } }], { user: false });
        return true;
    }

    /** 新增一列（預設加在最後）；回傳該列的列鍵（缺少列鍵時為內部產生的鍵）。 */
    addRow(row = {}, { index } = {}) {
        const record = this._makeRecord(row);
        this._recompute(record);
        const at = Number.isInteger(index) ? clamp(index, 0, this._records.length) : this._records.length;
        this._insertRecords([record], at);
        return record.key;
    }

    /** 依列鍵移除一或多列；回傳實際移除的列數。 */
    removeRows(keys) {
        const list = Array.isArray(keys) ? keys : [keys];
        const targets = new Set();
        list.forEach((key) => {
            const record = key === null || key === undefined ? null : this._byKey.get(String(key));
            if (record) targets.add(record);
        });
        if (!targets.size) return 0;
        if (this._edit && targets.has(this._edit.record)) this._teardownEditor(false);
        const hadFocus = this._focusWithin;
        const before = this._syncContext();
        let firstIndex = this._records.length;
        targets.forEach((record) => {
            firstIndex = Math.min(firstIndex, this._indexByKey.get(record.keyStr));
            this._byKey.delete(record.keyStr);
            this._errors.delete(record.keyStr);
            const entry = this._rendered.get(record.keyStr);
            if (entry) {
                entry.el.remove();
                this._rendered.delete(record.keyStr);
            }
        });
        this._records = this._records.filter((record) => !targets.has(record));
        this._reindex();
        this._selectAll = false;
        const state = this._ui.snapshot();
        if (!state.active || !this._byKey.has(state.active.key)) {
            this._resetActive(before.activeIndex >= 0 ? { rowIndex: before.activeIndex, colIndex: before.activeCol } : null);
        } else if (!state.anchor || !this._byKey.has(state.anchor.key)) {
            this._ui.send('SET_ACTIVE', { active: state.active, anchor: state.active });
        }
        this._updateCounts();
        if (this._virtual) this._renderWindow();
        else this._syncMetaFrom(firstIndex);
        this._updateEmpty();
        if (before.multi) this._syncAllRendered();
        else this._syncActiveCell();
        if (hadFocus && !this._destroyed) this._focusActive();
        this._updateStatus();
        this._emitValidation();
        return targets.size;
    }

    getValue() {
        return this.getRows();
    }

    /** 同 setRows()；不觸發 onChange。 */
    setValue(rows) {
        return this.setRows(rows);
    }

    /** 清空所有資料列（同 setRows([])）。 */
    clear() {
        return this.setRows([]);
    }

    setDisabled(disabled) {
        const next = Boolean(disabled);
        if (next && this._edit) this._teardownEditor(this._focusWithin);
        this.options.disabled = next;
        this._ui.send('SET_DISABLED', { disabled: next });
        this._applyDisabled();
        this._syncAllRendered();
        return this;
    }

    /** 驗證所有儲存格；全部通過時回傳 true。 */
    validate() {
        this._records.forEach((record) => {
            const rowCopy = { ...record.data };
            this._columns.forEach((column) => {
                record.touched.add(column.key);
                this._setError(record, column, this._checkCell(record, column, rowCopy));
            });
        });
        this._syncAllRendered();
        this._updateStatus();
        this._emitValidation();
        return this._errors.size === 0;
    }

    /** 目前的驗證錯誤：[{ rowKey, key, message }]，依列與欄的順序排列。 */
    getErrors() {
        const list = [];
        if (!this._errors.size) return list;
        this._records.forEach((record) => {
            const messages = this._errors.get(record.keyStr);
            if (!messages) return;
            this._columns.forEach((column) => {
                const message = messages.get(column.key);
                if (message) list.push({ rowKey: record.key, key: column.key, message });
            });
        });
        return list;
    }

    /** 與基準資料相比是否有新增、修改或移除。 */
    isDirty() {
        if (this._records.length !== this._baselineOrder.length) return true;
        return this._records.some((record) => {
            const base = this._baseline.get(record.keyStr);
            return !base || Object.keys(this._diffRow(base.data, record.data, true)).length > 0;
        });
    }

    /** 相對基準的變更：{ added: [row], updated: [{ rowKey, changes: { [key]: { from, to } } }], removed: [row] }。 */
    getChanges() {
        const added = [];
        const updated = [];
        const current = new Set();
        this._records.forEach((record) => {
            current.add(record.keyStr);
            const base = this._baseline.get(record.keyStr);
            if (!base) {
                added.push({ ...record.data });
                return;
            }
            const changes = this._diffRow(base.data, record.data, false);
            if (Object.keys(changes).length) updated.push({ rowKey: record.key, changes });
        });
        const removed = this._baselineOrder
            .filter((keyStr) => !current.has(keyStr))
            .map((keyStr) => ({ ...this._baseline.get(keyStr).data }));
        return { added, updated, removed };
    }

    /** 以目前資料作為新的基準（清除修改標記）；編輯中的內容會先提交。 */
    acceptChanges() {
        if (this._edit) this._commitEdit({ refocus: this._focusWithin });
        this._captureBaseline();
        this._syncAllRendered();
        return this;
    }

    /** 還原為基準資料（丟棄新增、修改與移除），並清除驗證錯誤。 */
    revertChanges() {
        if (this._edit) this._teardownEditor(false);
        const hadFocus = this._focusWithin && !this._destroyed;
        const previous = this._activePosition();
        this._records = this._baselineOrder.map((keyStr) => {
            const base = this._baseline.get(keyStr);
            const record = this._byKey.get(keyStr) || { uid: ++this._recordSeq, key: base.key, keyStr, data: {}, touched: new Set() };
            record.data = { ...base.data };
            record.touched = new Set();
            return record;
        });
        this._byKey = new Map(this._records.map((record) => [record.keyStr, record]));
        this._reindex();
        this._errors = new Map();
        this._selectAll = false;
        this._resetActive(previous);
        this._render();
        if (hadFocus) this._focusActive();
        this._emitValidation();
        return this;
    }

    /** 將指定儲存格設為作用中並取得焦點；找不到時回傳 false。 */
    focusCell(rowKey, key) {
        const record = this._byKey.get(String(rowKey));
        const colIndex = key === undefined ? 0 : this._colIndexByKey.get(String(key));
        if (!record || colIndex === undefined || this._destroyed) return false;
        this._setActive(this._indexByKey.get(record.keyStr), colIndex, { focus: true });
        return true;
    }

    /** 目前作用中的儲存格：{ rowKey, key } 或 null。 */
    getActiveCell() {
        const position = this._activePosition();
        if (!position) return null;
        return { rowKey: this._records[position.rowIndex].key, key: this._columns[position.colIndex].key };
    }

    /**
     * 標示整個表格的欄位錯誤（訊息顯示在表格下方）；空訊息等同 clearError()。
     * display:false 只標示錯誤狀態、不顯示文字，給自行顯示錯誤文字的外層使用。
     */
    setError(message, { display = true } = {}) {
        if (this._destroyed) return this;
        setFieldError(this, message, { target: this._grid, visual: this._frame, container: this.element, display });
        return this;
    }

    /** 清除 setError 的標示與文字。 */
    clearError() {
        clearFieldError(this);
        return this;
    }

    get [FIELD_ERROR_CONTRACT]() {
        return true;
    }

    /** 解析試算表複製的 TSV：支援引號欄位（內含 tab、換行、雙引號）與結尾換行。 */
    static parseTSV(text) {
        const source = String(text ?? '').replace(/\r\n?/g, '\n');
        const rows = [];
        if (source === '') return rows;
        const length = source.length;
        let row = [];
        let index = 0;
        while (index <= length) {
            const closing = source[index] === '"' ? DataGrid._closingQuote(source, index + 1) : -1;
            if (closing >= 0) {
                row.push(source.slice(index + 1, closing).replace(/""/g, '"'));
                index = closing + 1;
            } else {
                const end = DataGrid._delimiterAt(source, index);
                row.push(source.slice(index, end));
                index = end;
            }
            if (index >= length) {
                rows.push(row);
                break;
            }
            const delimiter = source[index];
            index += 1;
            if (delimiter === '\n') {
                rows.push(row);
                row = [];
                if (index >= length) break;
            }
        }
        return rows;
    }

    /** 將二維陣列轉成 TSV；含 tab、換行或雙引號的值以雙引號包住。 */
    static toTSV(matrix) {
        return (Array.isArray(matrix) ? matrix : []).map((row) => (Array.isArray(row) ? row : [row]).map((cell) => {
            const text = cell === null || cell === undefined ? '' : String(cell);
            return /[\t\n\r"]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
        }).join('\t')).join('\n');
    }

    // ── TSV 內部輔助 ────────────────────────────────────────

    /** 找出引號欄位的結束引號：後面必須是 tab、換行或結尾；否則視為一般欄位（回傳 -1）。 */
    static _closingQuote(source, start) {
        for (let index = start; index < source.length; index += 1) {
            if (source[index] !== '"') continue;
            if (source[index + 1] === '"') {
                index += 1;
                continue;
            }
            const next = source[index + 1];
            return next === undefined || next === '\t' || next === '\n' ? index : -1;
        }
        return -1;
    }

    static _delimiterAt(source, start) {
        for (let index = start; index < source.length; index += 1) {
            if (source[index] === '\t' || source[index] === '\n') return index;
        }
        return source.length;
    }

    // ── 建構 ────────────────────────────────────────────────

    _normalizeColumns(columns) {
        const list = [];
        const seen = new Set();
        (Array.isArray(columns) ? columns : []).forEach((definition) => {
            if (!definition || typeof definition !== 'object') return;
            const key = definition.key === undefined || definition.key === null ? '' : String(definition.key);
            if (!key) return;
            if (seen.has(key)) {
                console.warn(`[DataGrid] duplicate column key ignored: ${key}`);
                return;
            }
            seen.add(key);
            const type = COLUMN_TYPES.includes(definition.type) ? definition.type : 'text';
            const defaultAlign = type === 'number' ? 'right' : (type === 'checkbox' ? 'center' : 'left');
            if (typeof definition.width === 'string' && definition.width.trim() && !isValidWidth(definition.width)) {
                console.warn(`[DataGrid] unsupported column width ignored: ${definition.width}`);
            }
            list.push({
                key,
                label: definition.label === undefined || definition.label === null ? key : String(definition.label),
                type,
                width: definition.width,
                align: ['left', 'center', 'right'].includes(definition.align) ? definition.align : defaultAlign,
                required: Boolean(definition.required),
                readonly: typeof definition.readonly === 'function' ? definition.readonly : Boolean(definition.readonly),
                min: this._normalizeLimit(type, definition.min),
                max: this._normalizeLimit(type, definition.max),
                precision: Number.isInteger(definition.precision) && definition.precision >= 0 && definition.precision <= 10 ? definition.precision : null,
                maxLength: Number.isInteger(definition.maxLength) && definition.maxLength >= 0 ? definition.maxLength : null,
                options: normalizeOptions(definition.options),
                validate: typeof definition.validate === 'function' ? definition.validate : null,
                format: typeof definition.format === 'function' ? definition.format : null,
                compute: typeof definition.compute === 'function' ? definition.compute : null,
            });
        });
        return list;
    }

    _normalizeLimit(type, value) {
        if (value === undefined || value === null || value === '') return null;
        if (type === 'date') {
            const iso = parseDate(value);
            return typeof iso === 'string' ? iso : null;
        }
        const number = Number(value);
        return Number.isFinite(number) ? number : null;
    }

    _bindHandlers() {
        this._onKeyDown = (event) => this._handleKeyDown(event);
        this._onMouseDown = (event) => this._handleMouseDown(event);
        this._onMouseOver = (event) => this._handleMouseOver(event);
        this._onDocumentMouseUp = () => this._stopDrag();
        this._onDblClick = (event) => this._handleDblClick(event);
        this._onBoxClick = (event) => this._handleBoxClick(event);
        this._onFocusIn = (event) => this._handleFocusIn(event);
        this._onFocusOut = (event) => this._handleFocusOut(event);
        this._onCopy = (event) => this._handleCopy(event, false);
        this._onCut = (event) => this._handleCopy(event, true);
        this._onPaste = (event) => this._handlePaste(event);
        this._onScroll = () => this._scheduleWindow();
        this._onEditorBlur = () => {
            if (this._edit) this._commitEdit({ refocus: false });
        };
    }

    _createDom() {
        const root = document.createElement('div');
        root.className = 'b4a-datagrid';
        root.style.cssText = ROOT_CSS;

        const frame = document.createElement('div');
        frame.className = 'b4a-datagrid__frame';
        frame.style.cssText = FRAME_CSS;
        if (this._heightCss) frame.style.setProperty('height', this._heightCss);

        const tracks = this._columns.map(trackFor);
        if (this.options.showRowNumbers) tracks.unshift(`${ROW_NUMBER_WIDTH}px`);
        const template = tracks.join(' ') || '1fr';
        const minWidth = this._columns.reduce((sum, column) => sum + trackMinPx(column), this.options.showRowNumbers ? ROW_NUMBER_WIDTH : 0);

        const grid = document.createElement('div');
        grid.className = 'b4a-datagrid__grid';
        grid.id = this._uid;
        grid.setAttribute('role', 'grid');
        grid.setAttribute('aria-multiselectable', 'true');
        grid.setAttribute('aria-label', this.options.ariaLabel ? String(this.options.ariaLabel) : Locale.t('dataGrid.gridLabel'));
        grid.setAttribute('aria-colcount', String(this._columns.length + this._colOffset));
        grid.style.cssText = GRID_CSS;
        grid.style.minWidth = `${minWidth}px`;

        const head = document.createElement('div');
        head.className = 'b4a-datagrid__head';
        head.setAttribute('role', 'rowgroup');
        head.style.cssText = HEAD_CSS;
        this._headRow = this._buildHeader(template);
        head.appendChild(this._headRow);

        const body = document.createElement('div');
        body.className = 'b4a-datagrid__body';
        body.setAttribute('role', 'rowgroup');
        body.style.cssText = BODY_CSS;

        grid.append(head, body);

        const empty = document.createElement('div');
        empty.className = 'b4a-datagrid__empty';
        empty.style.cssText = EMPTY_CSS;
        empty.textContent = Locale.t('dataGrid.empty');

        frame.append(grid, empty);

        // 作用中儲存格的驗證訊息（視覺用；輔助技術經由儲存格的 aria-describedby 取得同一訊息）
        const status = document.createElement('div');
        status.className = 'b4a-datagrid__status';
        status.setAttribute('aria-hidden', 'true');
        status.style.cssText = STATUS_CSS;
        status.style.display = 'none';

        const live = document.createElement('div');
        live.className = 'b4a-datagrid__live';
        live.setAttribute('role', 'status');
        live.setAttribute('aria-live', 'polite');
        live.setAttribute('aria-atomic', 'true');
        live.style.cssText = SR_ONLY_CSS;

        root.append(frame, status, live);

        this.element = root;
        this._frame = frame;
        this._grid = grid;
        this._body = body;
        this._emptyEl = empty;
        this._statusEl = status;
        this._liveEl = live;
        this._buildTemplates(template);
        this._applyDisabled();

        this._gridEvents = [
            ['keydown', this._onKeyDown],
            ['mousedown', this._onMouseDown],
            ['mouseover', this._onMouseOver],
            ['dblclick', this._onDblClick],
            ['click', this._onBoxClick],
            ['focusin', this._onFocusIn],
            ['focusout', this._onFocusOut],
            ['copy', this._onCopy],
            ['cut', this._onCut],
            ['paste', this._onPaste],
        ];
        this._gridEvents.forEach(([type, handler]) => grid.addEventListener(type, handler));
        if (this._virtual) {
            frame.addEventListener('scroll', this._onScroll, { passive: true });
            if (typeof ResizeObserver === 'function') {
                this._resizeObserver = new ResizeObserver(this._onScroll);
                this._resizeObserver.observe(frame);
            }
        }
    }

    _buildHeader(template) {
        const row = document.createElement('div');
        row.className = 'b4a-datagrid__header-row';
        row.setAttribute('role', 'row');
        row.setAttribute('aria-rowindex', '1');
        row.style.cssText = HEADER_ROW_CSS;
        row.style.setProperty('grid-template-columns', template);
        row.style.height = `${this._rowHeight}px`;

        if (this.options.showRowNumbers) {
            const corner = document.createElement('div');
            corner.className = 'b4a-datagrid__corner';
            corner.setAttribute('role', 'columnheader');
            corner.setAttribute('aria-colindex', '1');
            corner.style.cssText = HEADER_CELL_CSS + CORNER_CSS;
            const label = document.createElement('span');
            label.style.cssText = SR_ONLY_CSS;
            label.textContent = Locale.t('dataGrid.rowNumber');
            corner.appendChild(label);
            row.appendChild(corner);
        }

        this._columns.forEach((column, index) => {
            const cell = document.createElement('div');
            cell.className = 'b4a-datagrid__column-header';
            cell.setAttribute('role', 'columnheader');
            cell.setAttribute('aria-colindex', String(index + 1 + this._colOffset));
            cell.dataset.colKey = column.key;
            cell.style.cssText = HEADER_CELL_CSS;
            if (column.align === 'right') cell.style.justifyContent = 'flex-end';
            else if (column.align === 'center') cell.style.justifyContent = 'center';
            const label = document.createElement('span');
            label.className = 'b4a-datagrid__column-label';
            label.style.cssText = LABEL_CSS;
            label.textContent = column.label;
            cell.appendChild(label);
            if (column.required) {
                const mark = document.createElement('span');
                mark.setAttribute('aria-hidden', 'true');
                mark.style.cssText = REQUIRED_MARK_CSS;
                mark.textContent = '*';
                const hint = document.createElement('span');
                hint.style.cssText = SR_ONLY_CSS;
                hint.textContent = Locale.t('dataGrid.requiredHint');
                cell.append(mark, hint);
            }
            row.appendChild(cell);
        });
        return row;
    }

    /** 列、列號與每一欄儲存格的樣板；渲染時 cloneNode，比逐一設定樣式快。 */
    _buildTemplates(template) {
        const row = document.createElement('div');
        row.className = 'b4a-datagrid__row';
        row.setAttribute('role', 'row');
        row.style.cssText = ROW_CSS;
        row.style.setProperty('grid-template-columns', template);
        row.style.height = `${this._rowHeight}px`;
        if (this._virtual) {
            row.style.position = 'absolute';
            row.style.left = '0';
            row.style.right = '0';
        }
        this._rowTemplate = row;

        const number = document.createElement('div');
        number.className = 'b4a-datagrid__rownum';
        number.setAttribute('role', 'rowheader');
        number.setAttribute('aria-colindex', '1');
        number.style.cssText = ROWNUM_CSS;
        this._rowNumTemplate = number;

        this._cellTemplates = this._columns.map((column, index) => {
            const cell = document.createElement('div');
            cell.className = `b4a-datagrid__cell b4a-datagrid__cell--${column.type}`;
            cell.setAttribute('role', 'gridcell');
            cell.setAttribute('aria-colindex', String(index + 1 + this._colOffset));
            cell.dataset.colKey = column.key;
            cell.tabIndex = -1;
            cell.style.cssText = CELL_CSS;
            const value = document.createElement('span');
            value.className = 'b4a-datagrid__value';
            value.style.cssText = VALUE_CSS;
            value.style.textAlign = column.align;
            cell.appendChild(value);
            return cell;
        });
    }

    _applyDisabled() {
        const disabled = this._isDisabled();
        if (disabled) this._grid.setAttribute('aria-disabled', 'true');
        else this._grid.removeAttribute('aria-disabled');
        this._frame.style.opacity = disabled ? '0.7' : '';
    }

    // ── 資料列 ──────────────────────────────────────────────

    _columnByKey(key) {
        const index = this._colIndexByKey.get(key);
        return index === undefined ? null : this._columns[index];
    }

    _resolveKey(data) {
        const { rowKey } = this.options;
        let key;
        if (typeof rowKey === 'function') {
            try {
                key = rowKey({ ...data });
            } catch (error) {
                key = undefined;
            }
        } else if (rowKey !== null && rowKey !== undefined && rowKey !== '') {
            key = data[String(rowKey)];
        }
        return key === null || key === undefined || key === '' ? null : key;
    }

    _generateKey() {
        let key = '';
        do {
            this._keySeq += 1;
            key = `__dg-row-${this._keySeq}`;
        } while (this._byKey.has(key) || this._baseline.has(key));
        return key;
    }

    _makeRecord(row) {
        const data = row && typeof row === 'object' ? { ...row } : {};
        let key = this._resolveKey(data);
        if (key !== null && this._byKey.has(String(key))) {
            console.warn(`[DataGrid] duplicate row key "${String(key)}"; a generated key is used`);
            key = null;
        }
        if (key === null) key = this._generateKey();
        this._recordSeq += 1;
        return { uid: this._recordSeq, key, keyStr: String(key), data, touched: new Set() };
    }

    _ingest(rows) {
        this._records = [];
        this._byKey = new Map();
        (Array.isArray(rows) ? rows : []).forEach((row) => {
            if (!row || typeof row !== 'object') return;
            const record = this._makeRecord(row);
            this._recompute(record);
            this._records.push(record);
            this._byKey.set(record.keyStr, record);
        });
        this._reindex();
    }

    _reindex() {
        this._indexByKey = new Map(this._records.map((record, index) => [record.keyStr, index]));
    }

    _captureBaseline() {
        this._baseline = new Map(this._records.map((record) => [record.keyStr, { key: record.key, data: { ...record.data } }]));
        this._baselineOrder = this._records.map((record) => record.keyStr);
    }

    _insertRecords(records, at) {
        if (!records.length) return;
        const wasEmpty = !this._activePosition();
        this._records.splice(at, 0, ...records);
        records.forEach((record) => this._byKey.set(record.keyStr, record));
        this._reindex();
        if (wasEmpty) this._resetActive(null);
        if (this._destroyed) return;
        this._updateCounts();
        if (this._virtual) {
            this._renderWindow();
        } else {
            const context = this._syncContext();
            const next = this._records[at + records.length];
            const reference = next ? this._rendered.get(next.keyStr)?.el || null : null;
            const fragment = document.createDocumentFragment();
            records.forEach((record, offset) => {
                const entry = this._createRow(record, at + offset, context);
                this._rendered.set(record.keyStr, entry);
                fragment.appendChild(entry.el);
            });
            this._body.insertBefore(fragment, reference);
            this._syncMetaFrom(at + records.length);
        }
        this._updateEmpty();
        if (wasEmpty && this._focusWithin) this._focusActive();
        this._updateStatus();
    }

    _recompute(record) {
        this._computed.forEach((column) => {
            let value = null;
            try {
                value = column.compute({ ...record.data });
            } catch (error) {
                console.warn(`[DataGrid] compute failed for column "${column.key}":`, error);
                value = null;
            }
            record.data[column.key] = value === undefined ? null : value;
        });
    }

    _rowCopy(record) {
        return this._needsRowCopy ? { ...record.data } : null;
    }

    _isReadonly(record, column, rowCopy = null) {
        if (column.compute) return true;
        if (typeof column.readonly !== 'function') return column.readonly;
        try {
            return Boolean(column.readonly(rowCopy || { ...record.data }));
        } catch (error) {
            return true;
        }
    }

    _equal(column, a, b) {
        if (column && column.type === 'checkbox') {
            const left = parseBoolean(a);
            const right = parseBoolean(b);
            if (left !== INVALID && right !== INVALID) return left === right;
        }
        return sameValue(a, b);
    }

    _isModified(record, column) {
        const base = this._baseline.get(record.keyStr);
        const value = record.data[column.key];
        if (!base) return !isBlankCell(column, value);
        return !this._equal(column, base.data[column.key], value);
    }

    _diffRow(baseData, data, stopAtFirst) {
        const changes = {};
        const fields = new Set([...Object.keys(baseData), ...Object.keys(data)]);
        for (const field of fields) {
            const column = this._columnByKey(field);
            const equal = column ? this._equal(column, baseData[field], data[field]) : sameValue(baseData[field], data[field]);
            if (equal) continue;
            changes[field] = { from: baseData[field], to: data[field] };
            if (stopAtFirst) break;
        }
        return changes;
    }

    /**
     * 所有資料寫入的共同路徑：套用 patch、重算計算欄、重新驗證、只更新該列有變動的儲存格，
     * 最後依來源觸發回呼（user=true 才觸發 onCellChange / onChange）。
     */
    _applyPatches(items, { user = false, forceChange = false } = {}) {
        const changed = [];
        const touched = [];
        const seen = new Set();
        items.forEach(({ record, patch }) => {
            if (!record || !patch || !this._byKey.has(record.keyStr)) return;
            if (!seen.has(record)) {
                seen.add(record);
                touched.push(record);
            }
            Object.keys(patch).forEach((field) => {
                const column = this._columnByKey(field);
                const value = patch[field];
                const oldValue = record.data[field];
                if (column) record.touched.add(field);
                if (column ? this._equal(column, oldValue, value) : sameValue(oldValue, value)) return;
                record.data[field] = value;
                changed.push({ record, field, column, value, oldValue });
            });
        });
        touched.forEach((record) => {
            this._recompute(record);
            this._validateRecord(record);
            this._syncRecord(record);
        });
        this._updateStatus();
        if (user) {
            changed.forEach(({ record, field, column, value, oldValue }) => {
                if (column) this._emit('onCellChange', { rowKey: record.key, key: field, value, oldValue, row: { ...record.data } });
            });
            if (changed.length || forceChange) this._emit('onChange', this.getRows());
        }
        this._emitValidation();
        return changed;
    }

    _patchFor(patches, record) {
        let patch = patches.get(record);
        if (!patch) {
            patch = {};
            patches.set(record, patch);
        }
        return patch;
    }

    _patchList(patches) {
        return [...patches].map(([record, patch]) => ({ record, patch }));
    }

    _eachCell(rect, callback) {
        for (let row = rect.top; row <= rect.bottom; row += 1) {
            const record = this._records[row];
            if (!record) continue;
            const rowCopy = this._rowCopy(record);
            for (let col = rect.left; col <= rect.right; col += 1) callback(record, this._columns[col], rowCopy);
        }
    }

    _emit(name, payload) {
        const handler = this.options[name];
        if (typeof handler !== 'function') return;
        try {
            handler(payload);
        } catch (error) {
            console.error(`[DataGrid] ${name} failed:`, error);
        }
    }

    // ── 驗證 ────────────────────────────────────────────────

    _validateRecord(record) {
        if (!record.touched.size) return;
        const rowCopy = { ...record.data };
        this._columns.forEach((column) => {
            if (!record.touched.has(column.key) && !column.compute) return;
            this._setError(record, column, this._checkCell(record, column, rowCopy));
        });
    }

    /** 驗證順序：必填 → 型別 → 範圍 → 長度 → 自訂 validate；回傳訊息或空字串。 */
    _checkCell(record, column, rowCopy) {
        const value = record.data[column.key];
        const t = (key, params) => Locale.t(`dataGrid.errors.${key}`, params);
        if (column.required && isBlankCell(column, value)) return t('required');
        if (!isEmpty(value)) {
            const message = this._typeError(column, value, t);
            if (message) return message;
        }
        if (!column.validate) return '';
        try {
            const result = column.validate(value, rowCopy);
            return result ? String(result) : '';
        } catch (error) {
            console.warn(`[DataGrid] validate failed for column "${column.key}":`, error);
            return '';
        }
    }

    _typeError(column, value, t) {
        if (column.type === 'number') {
            const number = parseNumber(value);
            if (typeof number !== 'number' || Number.isNaN(number)) return t('number');
            if (column.min !== null && number < column.min) return t('min', { min: column.min });
            if (column.max !== null && number > column.max) return t('max', { max: column.max });
            return '';
        }
        if (column.type === 'date') {
            const iso = parseDate(value);
            if (typeof iso !== 'string') return t('date');
            if (column.min !== null && iso < column.min) return t('dateMin', { min: column.min });
            if (column.max !== null && iso > column.max) return t('dateMax', { max: column.max });
            return '';
        }
        if (column.type === 'select') return column.options.length && !findOption(column, value) ? t('option') : '';
        if (column.type === 'checkbox') return parseBoolean(value) === INVALID ? t('boolean') : '';
        return column.maxLength !== null && String(value).length > column.maxLength ? t('maxLength', { maxLength: column.maxLength }) : '';
    }

    _setError(record, column, message) {
        let messages = this._errors.get(record.keyStr);
        if (message) {
            if (!messages) {
                messages = new Map();
                this._errors.set(record.keyStr, messages);
            }
            messages.set(column.key, message);
        } else if (messages) {
            messages.delete(column.key);
            if (!messages.size) this._errors.delete(record.keyStr);
        }
    }

    _emitValidation() {
        const parts = [];
        this._errors.forEach((messages, keyStr) => {
            messages.forEach((message, key) => parts.push(`${keyStr}\u0001${key}\u0001${message}`));
        });
        const signature = parts.sort().join('\u0002');
        if (signature === this._errorSignature) return;
        this._errorSignature = signature;
        if (!this._destroyed) this._emit('onValidationChange', this.getErrors());
    }

    // ── 渲染 ────────────────────────────────────────────────

    _render() {
        if (this._destroyed) return;
        this._updateCounts();
        this._rendered.clear();
        this._body.replaceChildren();
        if (this._virtual) {
            this._renderWindow();
        } else {
            const context = this._syncContext();
            const fragment = document.createDocumentFragment();
            this._records.forEach((record, index) => {
                const entry = this._createRow(record, index, context);
                this._rendered.set(record.keyStr, entry);
                fragment.appendChild(entry.el);
            });
            this._body.appendChild(fragment);
        }
        this._updateEmpty();
        this._updateStatus();
    }

    _viewportHeight() {
        return this._frame.clientHeight || parsePx(this._heightCss) || DEFAULT_VIRTUAL_HEIGHT;
    }

    _headerHeight() {
        return this._headRow.offsetHeight || this._rowHeight;
    }

    _windowRange() {
        const total = this._records.length;
        if (!total) return [0, -1];
        const rowHeight = this._rowHeight;
        const visible = Math.max(rowHeight, this._viewportHeight() - this._headerHeight());
        const scrollTop = Math.max(0, this._frame.scrollTop || 0);
        const first = clamp(Math.floor(scrollTop / rowHeight) - OVERSCAN, 0, total - 1);
        const last = clamp(Math.ceil((scrollTop + visible) / rowHeight) + OVERSCAN, first, total - 1);
        return [first, last];
    }

    /** virtual 模式：只保留可視範圍（含緩衝）的列，並固定保留作用中列，避免焦點與編輯器被移除。 */
    _renderWindow() {
        if (this._destroyed || !this._virtual) return;
        this._body.style.height = `${this._records.length * this._rowHeight}px`;
        const [first, last] = this._windowRange();
        const wanted = [];
        for (let index = first; index <= last; index += 1) wanted.push(index);
        const active = this._activePosition();
        if (active && (active.rowIndex < first || active.rowIndex > last)) {
            wanted.push(active.rowIndex);
            wanted.sort((a, b) => a - b);
        }
        const keep = new Set(wanted.map((index) => this._records[index].keyStr));
        this._rendered.forEach((entry, keyStr) => {
            if (keep.has(keyStr)) return;
            entry.el.remove();
            this._rendered.delete(keyStr);
        });
        const context = this._syncContext();
        let previous = null;
        wanted.forEach((index) => {
            const record = this._records[index];
            let entry = this._rendered.get(record.keyStr);
            if (entry) {
                this._syncRowMeta(entry, index);
            } else {
                entry = this._createRow(record, index, context);
                this._rendered.set(record.keyStr, entry);
            }
            const expected = previous ? previous.nextSibling : this._body.firstChild;
            if (entry.el !== expected) this._body.insertBefore(entry.el, expected);
            previous = entry.el;
        });
    }

    _scheduleWindow() {
        if (this._frameRequest || this._destroyed || !this._virtual) return;
        const run = () => {
            this._frameRequest = null;
            this._renderWindow();
        };
        this._frameRequest = typeof requestAnimationFrame === 'function'
            ? { raf: requestAnimationFrame(run) }
            : { timer: setTimeout(run, 16) };
    }

    _cancelScheduled() {
        const request = this._frameRequest;
        if (!request) return;
        if (request.raf !== undefined && typeof cancelAnimationFrame === 'function') cancelAnimationFrame(request.raf);
        if (request.timer !== undefined) clearTimeout(request.timer);
        this._frameRequest = null;
    }

    _createRow(record, index, context) {
        const row = this._rowTemplate.cloneNode(false);
        row.setAttribute('aria-rowindex', String(index + 2));
        row.dataset.rowKey = record.keyStr;
        if (this._virtual) row.style.top = `${index * this._rowHeight}px`;
        const entry = { el: row, record, index, rowNum: null, cells: [], parts: [], added: false };
        if (this.options.showRowNumbers) {
            const number = this._rowNumTemplate.cloneNode(false);
            number.textContent = String(index + 1);
            row.appendChild(number);
            entry.rowNum = number;
        }
        const rowCopy = this._rowCopy(record);
        this._columns.forEach((column, col) => {
            const cell = this._cellTemplates[col].cloneNode(true);
            row.appendChild(cell);
            entry.cells.push(cell);
            entry.parts.push({ value: cell.firstChild, box: null, mark: null, sr: null, err: null, state: INITIAL_CELL_STATE });
            this._syncCell(entry, col, index, context, rowCopy);
        });
        this._syncRowState(entry);
        return entry;
    }

    _syncRowMeta(entry, index) {
        if (!entry || entry.index === index) return;
        entry.index = index;
        entry.el.setAttribute('aria-rowindex', String(index + 2));
        if (this._virtual) entry.el.style.top = `${index * this._rowHeight}px`;
        if (entry.rowNum) entry.rowNum.textContent = String(index + 1);
    }

    _syncMetaFrom(start) {
        for (let index = Math.max(0, start); index < this._records.length; index += 1) {
            this._syncRowMeta(this._rendered.get(this._records[index].keyStr), index);
        }
    }

    _syncRowState(entry) {
        const added = !this._baseline.has(entry.record.keyStr);
        if (entry.added === added) return;
        entry.added = added;
        if (added) entry.el.dataset.rowState = 'added';
        else delete entry.el.dataset.rowState;
    }

    _syncRecord(record) {
        const entry = this._rendered.get(record.keyStr);
        if (!entry) return;
        this._syncColumns(entry, this._indexByKey.get(record.keyStr), 0, this._columns.length - 1, this._syncContext());
        this._syncRowState(entry);
    }

    _syncColumns(entry, index, from, to, context) {
        const rowCopy = this._rowCopy(entry.record);
        for (let col = from; col <= to; col += 1) this._syncCell(entry, col, index, context, rowCopy);
    }

    _syncAllRendered() {
        if (this._destroyed) return;
        const context = this._syncContext();
        this._rendered.forEach((entry) => {
            this._syncColumns(entry, this._indexByKey.get(entry.record.keyStr), 0, this._columns.length - 1, context);
            this._syncRowState(entry);
        });
    }

    _syncAt(rowIndex, colIndex, context) {
        const record = this._records[rowIndex];
        const entry = record ? this._rendered.get(record.keyStr) : null;
        if (entry && colIndex >= 0) this._syncCell(entry, colIndex, rowIndex, context, this._rowCopy(record));
    }

    _syncRect(rect, context) {
        if (this._rendered.size < rect.bottom - rect.top + 1) {
            this._rendered.forEach((entry) => {
                const index = this._indexByKey.get(entry.record.keyStr);
                if (index >= rect.top && index <= rect.bottom) this._syncColumns(entry, index, rect.left, rect.right, context);
            });
            return;
        }
        for (let index = rect.top; index <= rect.bottom; index += 1) {
            const entry = this._rendered.get(this._records[index].keyStr);
            if (entry) this._syncColumns(entry, index, rect.left, rect.right, context);
        }
    }

    _syncActiveCell() {
        const position = this._activePosition();
        if (position) this._syncAt(position.rowIndex, position.colIndex, this._syncContext());
    }

    _syncSelectionChange(before, after) {
        if (before.activeIndex >= 0) this._syncAt(before.activeIndex, before.activeCol, after);
        if (after.activeIndex >= 0) this._syncAt(after.activeIndex, after.activeCol, after);
        if (before.multi) this._syncRect(before.rect, after);
        if (after.multi) this._syncRect(after.rect, after);
    }

    _displayText(column, value, rowCopy) {
        if (column.format) {
            try {
                const output = column.format(value, rowCopy || {});
                return output === null || output === undefined ? '' : String(output);
            } catch (error) {
                return value === null || value === undefined ? '' : String(value);
            }
        }
        if (value === null || value === undefined) return '';
        if (column.type === 'number' && column.precision !== null) {
            const number = parseNumber(value);
            return typeof number === 'number' && Number.isFinite(number) ? number.toFixed(column.precision) : String(value);
        }
        if (column.type === 'select') {
            const option = findOption(column, value);
            return option ? option.label : String(value);
        }
        if (value instanceof Date) {
            const iso = parseDate(value);
            return typeof iso === 'string' ? iso : '';
        }
        return String(value);
    }

    /** 計算單一儲存格應有的狀態，只把與上次不同的部分寫回 DOM。 */
    _syncCell(entry, col, rowIndex, context, rowCopy) {
        const record = entry.record;
        const column = this._columns[col];
        const value = record.data[column.key];
        const readonly = this._isReadonly(record, column, rowCopy);
        const error = this._errors.get(record.keyStr)?.get(column.key) || '';
        const active = context.activeIndex === rowIndex && context.activeCol === col;
        const selected = Boolean(context.multi && inRect(context.rect, rowIndex, col));
        const next = {
            text: '',
            check: null,
            readonly,
            error,
            modified: !column.compute && this._isModified(record, column),
            selected,
            active,
            focused: active && context.focused,
            disabled: context.disabled,
            bg: selected ? 'var(--cl-bg-active)' : (error ? 'var(--cl-bg-danger-light)' : (readonly ? 'var(--cl-bg-secondary)' : '')),
            shadow: error ? 'inset 0 0 0 2px var(--cl-danger)' : '',
            color: readonly ? 'var(--cl-text-secondary)' : '',
            cursor: readonly || context.disabled ? 'default' : '',
        };
        if (column.type === 'checkbox' && !column.format) {
            const checked = parseBoolean(value);
            if (checked === INVALID) next.text = String(value);
            else next.check = checked;
        } else {
            next.text = this._displayText(column, value, rowCopy);
        }
        this._applyCellState(entry.cells[col], entry.parts[col], next, column, `${this._uid}-err-${record.uid}-${col}`);
    }

    _applyCellState(cell, parts, next, column, errorId) {
        const prev = parts.state;
        if (prev.text !== next.text || prev.check !== next.check) {
            if (next.check === null) {
                if (parts.box) {
                    parts.box.remove();
                    parts.box = null;
                }
                parts.value.textContent = next.text;
            } else {
                if (!parts.box) {
                    parts.value.textContent = '';
                    parts.box = this._createCheckBox(column);
                    parts.value.appendChild(parts.box);
                }
                parts.box.setAttribute('aria-checked', next.check ? 'true' : 'false');
                parts.box.textContent = next.check ? '✓' : '';
                parts.box.style.background = next.check ? 'var(--cl-primary)' : 'var(--cl-bg)';
                parts.box.style.borderColor = next.check ? 'var(--cl-primary)' : 'var(--cl-border-dark)';
            }
        }
        if (prev.readonly !== next.readonly) {
            if (next.readonly) cell.setAttribute('aria-readonly', 'true');
            else cell.removeAttribute('aria-readonly');
        }
        if (parts.box) {
            const readonly = next.readonly || next.disabled ? 'true' : 'false';
            if (parts.box.getAttribute('aria-readonly') !== readonly) parts.box.setAttribute('aria-readonly', readonly);
        }
        if (prev.error !== next.error) {
            if (next.error) {
                if (!parts.err) {
                    parts.err = document.createElement('span');
                    parts.err.className = 'b4a-datagrid__error-text';
                    parts.err.id = errorId;
                    parts.err.hidden = true;
                    cell.appendChild(parts.err);
                }
                parts.err.textContent = next.error;
                cell.setAttribute('aria-invalid', 'true');
                cell.setAttribute('aria-describedby', parts.err.id);
            } else {
                cell.removeAttribute('aria-invalid');
                cell.removeAttribute('aria-describedby');
                if (parts.err) {
                    parts.err.remove();
                    parts.err = null;
                }
            }
        }
        if (prev.modified !== next.modified) {
            if (next.modified) {
                parts.mark = document.createElement('span');
                parts.mark.className = 'b4a-datagrid__mark';
                parts.mark.setAttribute('aria-hidden', 'true');
                parts.mark.style.cssText = MARK_CSS;
                parts.sr = document.createElement('span');
                parts.sr.className = 'b4a-datagrid__modified';
                parts.sr.style.cssText = SR_ONLY_CSS;
                parts.sr.textContent = Locale.t('dataGrid.modified');
                cell.append(parts.mark, parts.sr);
                cell.dataset.modified = 'true';
            } else {
                parts.mark?.remove();
                parts.sr?.remove();
                parts.mark = null;
                parts.sr = null;
                delete cell.dataset.modified;
            }
        }
        if (prev.selected !== next.selected) {
            if (next.selected) cell.setAttribute('aria-selected', 'true');
            else cell.removeAttribute('aria-selected');
        }
        if (prev.active !== next.active) cell.tabIndex = next.active ? 0 : -1;
        if (prev.focused !== next.focused || (next.focused && Boolean(prev.error) !== Boolean(next.error))) {
            // 無效儲存格取得焦點時外框改用 danger 色，焦點與錯誤兩個狀態都看得到
            cell.style.outline = next.focused ? `2px solid ${next.error ? 'var(--cl-danger)' : 'var(--cl-primary)'}` : 'none';
            cell.style.outlineOffset = next.focused ? '-2px' : '';
        }
        if (prev.bg !== next.bg) cell.style.background = next.bg;
        if (prev.shadow !== next.shadow) cell.style.boxShadow = next.shadow;
        if (prev.color !== next.color) cell.style.color = next.color;
        if (prev.cursor !== next.cursor) cell.style.cursor = next.cursor || 'cell';
        parts.state = next;
    }

    _createCheckBox(column) {
        const box = document.createElement('span');
        box.className = 'b4a-datagrid__check';
        box.setAttribute('role', 'checkbox');
        box.setAttribute('aria-label', column.label);
        box.style.cssText = CHECK_CSS;
        return box;
    }

    _updateCounts() {
        this._grid.setAttribute('aria-rowcount', String(this._records.length + 1));
        if (this._virtual) this._body.style.height = `${this._records.length * this._rowHeight}px`;
    }

    _updateEmpty() {
        const empty = !this._records.length;
        this._emptyEl.style.display = empty ? '' : 'none';
        if (empty) this._grid.tabIndex = 0;
        else this._grid.removeAttribute('tabindex');
    }

    _updateStatus() {
        if (this._destroyed) return;
        const position = this._activePosition();
        let text = '';
        if (position) {
            const record = this._records[position.rowIndex];
            const column = this._columns[position.colIndex];
            const message = this._errors.get(record.keyStr)?.get(column.key);
            if (message) text = Locale.t('dataGrid.cellMessage', { row: position.rowIndex + 1, column: column.label, message });
        }
        if (this._statusEl.textContent !== text) this._statusEl.textContent = text;
        this._statusEl.style.display = text ? '' : 'none';
    }

    _announce(text) {
        if (this._liveEl && !this._destroyed) this._liveEl.textContent = text;
    }

    // ── 作用中儲存格與選取 ──────────────────────────────────

    _isDisabled() {
        return this._ui.snapshot().availability === 'disabled';
    }

    _position(ref) {
        if (!ref || !this._columns.length) return null;
        const rowIndex = this._indexByKey.get(ref.key);
        if (rowIndex === undefined) return null;
        return { rowIndex, colIndex: clamp(ref.col, 0, this._columns.length - 1) };
    }

    _activePosition() {
        return this._position(this._ui.snapshot().active);
    }

    _rangeRect() {
        if (!this._records.length || !this._columns.length) return null;
        if (this._selectAll) return { top: 0, left: 0, bottom: this._records.length - 1, right: this._columns.length - 1 };
        const state = this._ui.snapshot();
        const active = this._position(state.active);
        if (!active) return null;
        const anchor = this._position(state.anchor) || active;
        return {
            top: Math.min(anchor.rowIndex, active.rowIndex),
            bottom: Math.max(anchor.rowIndex, active.rowIndex),
            left: Math.min(anchor.colIndex, active.colIndex),
            right: Math.max(anchor.colIndex, active.colIndex),
        };
    }

    _syncContext() {
        const active = this._activePosition();
        const rect = this._rangeRect();
        return {
            activeIndex: active ? active.rowIndex : -1,
            activeCol: active ? active.colIndex : -1,
            rect,
            multi: Boolean(rect && (rect.top !== rect.bottom || rect.left !== rect.right)),
            focused: this._focusWithin,
            disabled: this._isDisabled(),
        };
    }

    _resetActive(previous) {
        if (!this._records.length || !this._columns.length) {
            this._ui.send('SET_ACTIVE', { active: null, anchor: null });
            return;
        }
        const row = previous ? Math.min(previous.rowIndex, this._records.length - 1) : 0;
        const col = previous ? Math.min(previous.colIndex, this._columns.length - 1) : 0;
        const active = { key: this._records[row].keyStr, col };
        this._ui.send('SET_ACTIVE', { active, anchor: active });
    }

    _setActive(rowIndex, colIndex, { extend = false, focus = true } = {}) {
        if (this._destroyed || !this._records.length || !this._columns.length) return;
        const row = clamp(rowIndex, 0, this._records.length - 1);
        const col = clamp(colIndex, 0, this._columns.length - 1);
        const before = this._syncContext();
        const state = this._ui.snapshot();
        const active = { key: this._records[row].keyStr, col };
        const anchor = extend ? (state.anchor || state.active || active) : active;
        this._selectAll = false;
        this._ui.send('SET_ACTIVE', { active, anchor });
        if (focus || this._virtual) this._revealRow(row);
        if (this._virtual) this._renderWindow();
        this._syncSelectionChange(before, this._syncContext());
        if (focus) this._focusActive();
        this._updateStatus();
    }

    /** 選取範圍設為 active 與 anchor 圍成的矩形（焦點留在 active）。 */
    _setSelection(active, anchor) {
        const before = this._syncContext();
        this._selectAll = false;
        this._ui.send('SET_ACTIVE', {
            active: { key: this._records[active.rowIndex].keyStr, col: active.colIndex },
            anchor: { key: this._records[anchor.rowIndex].keyStr, col: anchor.colIndex },
        });
        this._syncSelectionChange(before, this._syncContext());
        this._updateStatus();
    }

    _selectAllCells() {
        if (!this._records.length || !this._columns.length) return;
        this._selectAll = true;
        this._syncAllRendered();
        this._announce(Locale.t('dataGrid.selectedAll', { rows: this._records.length, cols: this._columns.length }));
    }

    _focusActive() {
        if (this._destroyed) return;
        const position = this._activePosition();
        if (!position) {
            if (!this._records.length) focusWithoutScroll(this._grid);
            return;
        }
        const entry = this._rendered.get(this._records[position.rowIndex].keyStr);
        const cell = entry ? entry.cells[position.colIndex] : null;
        if (!cell) return;
        if (!this._scrollsInternally) {
            cell.focus();
            return;
        }
        focusWithoutScroll(cell);
        this._revealColumn(cell);
    }

    /** 捲動內部容器讓整列落在固定表頭下方的可視區。 */
    _revealRow(rowIndex) {
        if (!this._scrollsInternally) return;
        const frame = this._frame;
        const rowHeight = this._rowHeight;
        const visible = Math.max(rowHeight, this._viewportHeight() - this._headerHeight());
        const top = rowIndex * rowHeight;
        const scrollTop = frame.scrollTop || 0;
        if (top < scrollTop) frame.scrollTop = top;
        else if (top + rowHeight > scrollTop + visible) frame.scrollTop = top + rowHeight - visible;
    }

    _revealColumn(cell) {
        const frame = this._frame;
        const width = cell.offsetWidth;
        if (!width) return;
        const left = cell.offsetLeft;
        const sticky = this.options.showRowNumbers ? ROW_NUMBER_WIDTH : 0;
        if (left < frame.scrollLeft + sticky) frame.scrollLeft = Math.max(0, left - sticky);
        else if (left + width > frame.scrollLeft + frame.clientWidth) frame.scrollLeft = left + width - frame.clientWidth;
    }

    _pageSize() {
        const visible = this._scrollsInternally ? this._viewportHeight() - this._headerHeight() : 0;
        const rows = Math.floor(visible / this._rowHeight);
        if (rows > 1) return rows - 1;
        return rows === 1 ? 1 : FALLBACK_PAGE_ROWS;
    }

    /** Tab / Shift+Tab：逐格移動並在列尾換列；已在第一格或最後一格時回傳 false，讓焦點離開表格。 */
    _tabMove(backward) {
        const position = this._activePosition();
        if (!position) return false;
        const lastRow = this._records.length - 1;
        const lastCol = this._columns.length - 1;
        let { rowIndex, colIndex } = position;
        if (backward) {
            if (colIndex > 0) colIndex -= 1;
            else if (rowIndex > 0) [rowIndex, colIndex] = [rowIndex - 1, lastCol];
            else return false;
        } else if (colIndex < lastCol) {
            colIndex += 1;
        } else if (rowIndex < lastRow) {
            [rowIndex, colIndex] = [rowIndex + 1, 0];
        } else {
            return false;
        }
        this._setActive(rowIndex, colIndex, { focus: true });
        return true;
    }

    _moveBy(rows, cols) {
        const position = this._activePosition();
        if (position) this._setActive(position.rowIndex + rows, position.colIndex + cols, { focus: true });
    }

    // ── 鍵盤 ────────────────────────────────────────────────

    _handleKeyDown(event) {
        if (this._destroyed) return;
        if (this._edit) {
            if (event.target === this._edit.input) this._handleEditorKey(event);
            return;
        }
        this._handleGridKey(event);
    }

    _handleGridKey(event) {
        if (event.isComposing || typeof event.key !== 'string') return;
        const position = this._activePosition();
        if (!position) return;
        const ctrl = event.ctrlKey || event.metaKey;
        const { rowIndex, colIndex } = position;
        const lastRow = this._records.length - 1;
        const lastCol = this._columns.length - 1;
        const go = (row, col) => {
            event.preventDefault();
            this._setActive(row, col, { extend: event.shiftKey, focus: true });
        };
        switch (event.key) {
            case 'ArrowUp': go(rowIndex - 1, colIndex); return;
            case 'ArrowDown': go(rowIndex + 1, colIndex); return;
            case 'ArrowLeft': go(rowIndex, colIndex - 1); return;
            case 'ArrowRight': go(rowIndex, colIndex + 1); return;
            case 'Home': go(ctrl ? 0 : rowIndex, 0); return;
            case 'End': go(ctrl ? lastRow : rowIndex, lastCol); return;
            case 'PageUp': go(rowIndex - this._pageSize(), colIndex); return;
            case 'PageDown': go(rowIndex + this._pageSize(), colIndex); return;
            case 'Tab':
                if (!ctrl && !event.altKey && this._tabMove(event.shiftKey)) event.preventDefault();
                return;
            case 'Enter':
            case 'F2':
                if (!ctrl && !event.altKey && this._startEdit()) event.preventDefault();
                return;
            case 'Escape':
                if (this._syncContext().multi) {
                    event.preventDefault();
                    this._setActive(rowIndex, colIndex, { focus: false });
                }
                return;
            case 'Delete':
            case 'Backspace':
                event.preventDefault();
                this._clearSelection();
                return;
            default:
                break;
        }
        if (ctrl && !event.altKey) {
            const letter = event.key.length === 1 ? event.key.toLowerCase() : '';
            if (letter === 'a') {
                event.preventDefault();
                this._selectAllCells();
            } else if (letter === 'c' || letter === 'x') {
                this._armCopyFallback(letter === 'x');
            }
            return;
        }
        const column = this._columns[colIndex];
        if (event.key === ' ' && column.type === 'checkbox') {
            event.preventDefault();
            this._toggleCheckboxes();
            return;
        }
        if (event.key === 'Process' || event.keyCode === 229) {
            this._startEdit({ text: '' });
            return;
        }
        // 可列印字元：開啟編輯器並以該字元取代原值（date 欄無法填入部分文字，只清空並開啟）
        if (event.key.length === 1 && this._startEdit({ text: event.key }) && column.type !== 'date') event.preventDefault();
    }

    _handleEditorKey(event) {
        if (event.isComposing || event.keyCode === 229) return;
        if (event.key === 'Enter') {
            if (event.altKey || event.ctrlKey || event.metaKey) return;
            event.preventDefault();
            this._commitEdit({ refocus: true });
            if (!this._destroyed) this._moveBy(event.shiftKey ? -1 : 1, 0);
        } else if (event.key === 'Tab') {
            this._commitEdit({ refocus: true });
            if (!this._destroyed && this._tabMove(event.shiftKey)) event.preventDefault();
        } else if (event.key === 'Escape') {
            event.preventDefault();
            event.stopPropagation();
            this._teardownEditor(true);
        }
    }

    // ── 編輯 ────────────────────────────────────────────────

    /** 在作用中儲存格開啟編輯器；唯讀、計算欄、checkbox 或停用時回傳 false。 */
    _startEdit({ text = null } = {}) {
        if (this._edit || this._destroyed || this._isDisabled()) return false;
        const position = this._activePosition();
        if (!position) return false;
        const record = this._records[position.rowIndex];
        const column = this._columns[position.colIndex];
        if (column.type === 'checkbox' || this._isReadonly(record, column)) return false;
        const entry = this._rendered.get(record.keyStr);
        if (!entry) return false;
        const cell = entry.cells[position.colIndex];
        const parts = entry.parts[position.colIndex];
        const input = this._createEditor(column, record.data[column.key], position.rowIndex, text);
        parts.value.style.visibility = 'hidden';
        cell.appendChild(input);
        this._edit = { record, column, input, cell, parts };
        this._ui.send('SET_EDITING', { editing: true });
        input.addEventListener('blur', this._onEditorBlur);
        focusWithoutScroll(input);
        if (input.type === 'text') {
            const end = input.value.length;
            try {
                input.setSelectionRange(end, end);
            } catch (error) {
                // 部分輸入型別不支援游標位置
            }
        }
        return true;
    }

    _createEditor(column, value, rowIndex, text) {
        let input;
        if (column.type === 'select') {
            input = document.createElement('select');
            const blank = document.createElement('option');
            blank.value = '';
            blank.textContent = Locale.t('dataGrid.emptyOption');
            input.appendChild(blank);
            column.options.forEach((option, index) => {
                const element = document.createElement('option');
                element.value = String(index);
                element.textContent = option.label;
                input.appendChild(element);
            });
            let selected = '';
            if (!isEmpty(value)) {
                const index = column.options.findIndex((option) => String(option.value) === String(value));
                if (index >= 0) {
                    selected = String(index);
                } else {
                    // 目前值不在選項中（例如貼上的無效值）：保留為一個選項，未變更時提交不會改動資料
                    const current = document.createElement('option');
                    current.value = CURRENT_OPTION;
                    current.textContent = String(value);
                    input.appendChild(current);
                    selected = CURRENT_OPTION;
                }
            }
            if (typeof text === 'string' && text.trim()) {
                const needle = normalizeText(text);
                const index = column.options.findIndex((option) => normalizeText(option.label).startsWith(needle));
                if (index >= 0) selected = String(index);
            }
            input.value = selected;
        } else {
            input = document.createElement('input');
            if (column.type === 'date') {
                input.type = 'date';
                if (column.min !== null) input.min = column.min;
                if (column.max !== null) input.max = column.max;
                const iso = text === null ? parseDate(value) : null;
                input.value = typeof iso === 'string' ? iso : '';
            } else {
                input.type = 'text';
                input.autocomplete = 'off';
                input.spellcheck = false;
                if (column.type === 'number') input.setAttribute('inputmode', 'decimal');
                if (column.maxLength !== null) input.maxLength = column.maxLength;
                input.value = text === null ? this._editText(column, value) : text;
            }
        }
        input.className = 'b4a-datagrid__editor';
        input.setAttribute('aria-label', Locale.t('dataGrid.editorLabel', { column: column.label, row: rowIndex + 1 }));
        input.style.cssText = EDITOR_CSS;
        return input;
    }

    _editText(column, value) {
        if (value === null || value === undefined) return '';
        if (column.type === 'number' && column.precision !== null) {
            const number = parseNumber(value);
            if (typeof number === 'number' && Number.isFinite(number)) return number.toFixed(column.precision);
        }
        return String(value);
    }

    /** 編輯器的原始輸入轉成儲存值：number 解析後依 precision 四捨五入並夾在 min/max 內，無法解析時保留原文（標示為錯誤）。 */
    _editorValue(edit) {
        const { column, input } = edit;
        const raw = input.value;
        if (column.type === 'select') {
            if (raw === '') return null;
            if (raw === CURRENT_OPTION) return edit.record.data[column.key];
            const option = column.options[Number(raw)];
            return option ? option.value : null;
        }
        if (column.type === 'number') {
            const number = parseNumber(raw);
            if (number === null) return null;
            if (Number.isNaN(number)) return raw;
            return clampToLimits(roundTo(number, column.precision), column.min, column.max);
        }
        if (column.type === 'date') return raw === '' ? null : raw;
        return raw;
    }

    _commitEdit({ refocus = true } = {}) {
        const edit = this._edit;
        if (!edit) return;
        const value = this._editorValue(edit);
        this._teardownEditor(refocus);
        if (this._destroyed || !this._byKey.has(edit.record.keyStr)) return;
        this._applyPatches([{ record: edit.record, patch: { [edit.column.key]: value } }], { user: true });
    }

    _teardownEditor(refocus) {
        const edit = this._edit;
        if (!edit) return;
        this._edit = null;
        this._ui.send('SET_EDITING', { editing: false });
        edit.input.removeEventListener('blur', this._onEditorBlur);
        edit.parts.value.style.visibility = '';
        if (refocus && edit.cell.isConnected) focusWithoutScroll(edit.cell);
        edit.input.remove();
    }

    // ── 滑鼠 ────────────────────────────────────────────────

    _hitTest(target) {
        const cell = target && typeof target.closest === 'function' ? target.closest('[role="gridcell"]') : null;
        if (!cell || !this._body.contains(cell)) return null;
        const rowIndex = this._indexByKey.get(cell.parentElement?.dataset.rowKey);
        const colIndex = this._colIndexByKey.get(cell.dataset.colKey);
        return rowIndex === undefined || colIndex === undefined ? null : { rowIndex, colIndex, cell };
    }

    _handleMouseDown(event) {
        if (this._destroyed || event.button !== 0) return;
        if (this._edit && this._edit.input.contains(event.target)) return;
        let hit = this._hitTest(event.target);
        if (!hit) return;
        if (this._edit) {
            this._commitEdit({ refocus: false });
            hit = this._hitTest(hit.cell);
            if (!hit || this._destroyed) return;
        }
        event.preventDefault();
        this._setActive(hit.rowIndex, hit.colIndex, { extend: event.shiftKey, focus: true });
        if (!event.shiftKey) this._startDrag();
    }

    _handleMouseOver(event) {
        if (!this._dragging || this._edit) return;
        if (event.buttons === 0) {
            this._stopDrag();
            return;
        }
        const hit = this._hitTest(event.target);
        if (!hit) return;
        const position = this._activePosition();
        if (position && position.rowIndex === hit.rowIndex && position.colIndex === hit.colIndex) return;
        this._setActive(hit.rowIndex, hit.colIndex, { extend: true, focus: true });
    }

    _startDrag() {
        if (this._dragging) return;
        this._dragging = true;
        document.addEventListener('mouseup', this._onDocumentMouseUp);
    }

    _stopDrag() {
        if (!this._dragging) return;
        this._dragging = false;
        document.removeEventListener('mouseup', this._onDocumentMouseUp);
    }

    _handleDblClick(event) {
        if (this._destroyed || this._edit) return;
        const hit = this._hitTest(event.target);
        if (!hit) return;
        this._setActive(hit.rowIndex, hit.colIndex, { focus: true });
        this._startEdit();
    }

    _handleBoxClick(event) {
        if (this._destroyed || this._edit) return;
        const box = event.target && typeof event.target.closest === 'function' ? event.target.closest('.b4a-datagrid__check') : null;
        const hit = box ? this._hitTest(box) : null;
        if (hit) this._toggleAt(hit.rowIndex, hit.colIndex);
    }

    // ── 焦點 ────────────────────────────────────────────────

    _handleFocusIn(event) {
        if (this._destroyed) return;
        const wasFocused = this._focusWithin;
        this._focusWithin = true;
        if (this._edit) return;
        const hit = this._hitTest(event.target);
        const position = this._activePosition();
        if (hit && (!position || hit.rowIndex !== position.rowIndex || hit.colIndex !== position.colIndex)) {
            // 焦點經由其他途徑（例如程式或輔助技術）落在非作用中儲存格：改以該格為作用中
            this._setActive(hit.rowIndex, hit.colIndex, { focus: false });
            return;
        }
        if (!wasFocused) this._syncActiveCell();
    }

    _handleFocusOut(event) {
        if (this._destroyed) return;
        const next = event.relatedTarget;
        if (next && this._grid.contains(next)) return;
        this._focusWithin = false;
        this._syncActiveCell();
    }

    // ── checkbox 與清除 ─────────────────────────────────────

    _toggleAt(rowIndex, colIndex) {
        if (this._isDisabled()) return;
        const record = this._records[rowIndex];
        const column = this._columns[colIndex];
        if (!record || !column || column.type !== 'checkbox' || this._isReadonly(record, column)) return;
        this._applyPatches([{ record, patch: { [column.key]: parseBoolean(record.data[column.key]) !== true } }], { user: true });
    }

    /** 空白鍵：切換作用中 checkbox；選取多格時，範圍內可編輯的 checkbox 一律設為作用中格切換後的值。 */
    _toggleCheckboxes() {
        if (this._isDisabled()) return;
        const context = this._syncContext();
        if (context.activeIndex < 0) return;
        if (!context.multi) {
            this._toggleAt(context.activeIndex, context.activeCol);
            return;
        }
        const record = this._records[context.activeIndex];
        const column = this._columns[context.activeCol];
        if (this._isReadonly(record, column)) return;
        const next = parseBoolean(record.data[column.key]) !== true;
        const patches = new Map();
        this._eachCell(context.rect, (row, col, rowCopy) => {
            if (col.type === 'checkbox' && !this._isReadonly(row, col, rowCopy)) this._patchFor(patches, row)[col.key] = next;
        });
        this._applyPatches(this._patchList(patches), { user: true });
    }

    /** Delete / Backspace / 剪下：清除選取範圍內可編輯儲存格（checkbox 設為 false，其餘設為 null）。 */
    _clearSelection() {
        if (this._isDisabled()) return;
        const rect = this._rangeRect();
        if (!rect) return;
        const patches = new Map();
        this._eachCell(rect, (record, column, rowCopy) => {
            if (!this._isReadonly(record, column, rowCopy)) this._patchFor(patches, record)[column.key] = column.type === 'checkbox' ? false : null;
        });
        const changed = this._applyPatches(this._patchList(patches), { user: true });
        this._announce(Locale.t('dataGrid.cleared', { count: changed.length }));
    }

    // ── 剪貼簿 ──────────────────────────────────────────────

    _copyText(record, column) {
        const value = record.data[column.key];
        if (value === null || value === undefined) return '';
        if (column.type === 'checkbox') {
            const checked = parseBoolean(value);
            if (checked === INVALID) return String(value);
            return checked ? 'TRUE' : 'FALSE';
        }
        if (column.type === 'select') {
            const option = findOption(column, value);
            return option ? option.label : String(value);
        }
        if (column.type === 'number' && column.precision !== null) {
            const number = parseNumber(value);
            if (typeof number === 'number' && Number.isFinite(number)) return number.toFixed(column.precision);
        }
        if (value instanceof Date) {
            const iso = parseDate(value);
            return typeof iso === 'string' ? iso : '';
        }
        return String(value);
    }

    /** 選取範圍轉成 TSV（select 取顯示文字、checkbox 為 TRUE/FALSE、number 依 precision；不套用 format）。 */
    _selectionText() {
        const rect = this._rangeRect();
        if (!rect) return null;
        const matrix = [];
        this._eachCell(rect, (record, column) => {
            const rowIndex = this._indexByKey.get(record.keyStr) - rect.top;
            if (!matrix[rowIndex]) matrix[rowIndex] = [];
            matrix[rowIndex].push(this._copyText(record, column));
        });
        this._lastCopySize = { rows: rect.bottom - rect.top + 1, cols: rect.right - rect.left + 1 };
        return DataGrid.toTSV(matrix);
    }

    _handleCopy(event, cut) {
        if (this._destroyed || this._edit) return;
        const text = this._selectionText();
        if (text === null) return;
        this._copyPending = false;
        if (event.clipboardData && typeof event.clipboardData.setData === 'function') {
            event.clipboardData.setData('text/plain', text);
            event.preventDefault();
        } else {
            this._writeClipboard(text);
        }
        this._announce(Locale.t('dataGrid.copied', this._lastCopySize));
        if (cut) this._clearSelection();
    }

    /** 沒有選取文字時部分瀏覽器不觸發 copy 事件：Ctrl+C 後若事件沒來，改用 Clipboard API。 */
    _armCopyFallback(cut) {
        this._copyPending = true;
        if (this._copyTimer) clearTimeout(this._copyTimer);
        this._copyTimer = setTimeout(() => {
            this._copyTimer = null;
            if (!this._copyPending || this._destroyed || this._edit) return;
            this._copyPending = false;
            const text = this._selectionText();
            if (text === null) return;
            this._writeClipboard(text);
            this._announce(Locale.t('dataGrid.copied', this._lastCopySize));
            if (cut) this._clearSelection();
        }, 0);
    }

    _writeClipboard(text) {
        const clipboard = typeof navigator !== 'undefined' ? navigator.clipboard : null;
        if (!clipboard || typeof clipboard.writeText !== 'function') return;
        try {
            const pending = clipboard.writeText(text);
            if (pending && typeof pending.catch === 'function') pending.catch(() => {});
        } catch (error) {
            // 權限不足或非安全環境時略過
        }
    }

    _handlePaste(event) {
        if (this._destroyed || this._edit) return;
        const data = event.clipboardData;
        const text = data && typeof data.getData === 'function' ? data.getData('text/plain') : '';
        event.preventDefault();
        if (this._isDisabled()) return;
        this._pasteText(text);
    }

    /** 貼上時的型別轉換：無法轉換的值保留原文（之後被標示為錯誤），不會被丟棄。 */
    _coercePaste(column, raw) {
        const text = raw === null || raw === undefined ? '' : String(raw);
        if (column.type === 'number') {
            const number = parseNumber(text);
            if (number === null) return null;
            return Number.isNaN(number) ? text : roundTo(number, column.precision);
        }
        if (column.type === 'date') {
            const iso = parseDate(text);
            if (iso === null) return null;
            return iso === INVALID ? text : iso;
        }
        if (column.type === 'select') {
            if (text.trim() === '') return null;
            const option = matchOption(column, text);
            return option ? option.value : text;
        }
        if (column.type === 'checkbox') {
            const checked = parseBoolean(text);
            return checked === INVALID ? text : checked;
        }
        return text;
    }

    _pasteText(text) {
        const data = DataGrid.parseTSV(text);
        const colCount = this._columns.length;
        if (!data.length || !colCount) return;
        const allowAdd = Boolean(this.options.allowAddRowsOnPaste);
        let rect = this._rangeRect();
        if (!rect) {
            if (!allowAdd) return;
            rect = { top: 0, left: 0, bottom: 0, right: 0 };
        }
        const width = data.reduce((max, row) => Math.max(max, row.length), 0);
        const fill = data.length === 1 && width === 1 && (rect.top !== rect.bottom || rect.left !== rect.right);
        const existing = this._records.length;
        const created = [];
        const patches = new Map();
        const recordAt = (index) => (index < existing ? this._records[index] : created[index - existing]);
        const put = (record, column, raw) => {
            if (!this._isReadonly(record, column)) this._patchFor(patches, record)[column.key] = this._coercePaste(column, raw);
        };
        let appliedRows = 0;
        let appliedCols = 0;
        let truncatedRows = 0;
        if (fill) {
            this._eachCell(rect, (record, column) => put(record, column, data[0][0]));
            appliedRows = rect.bottom - rect.top + 1;
            appliedCols = rect.right - rect.left + 1;
        } else {
            appliedCols = Math.min(width, colCount - rect.left);
            for (let offset = 0; offset < data.length; offset += 1) {
                const rowIndex = rect.top + offset;
                if (rowIndex >= existing + created.length) {
                    if (!allowAdd) {
                        truncatedRows = data.length - offset;
                        break;
                    }
                    const record = this._makeRecord({});
                    this._recompute(record);
                    created.push(record);
                }
                const record = recordAt(rowIndex);
                appliedRows += 1;
                data[offset].forEach((raw, index) => {
                    const colIndex = rect.left + index;
                    if (colIndex < colCount) put(record, this._columns[colIndex], raw);
                });
            }
        }
        if (created.length) this._insertRecords(created, this._records.length);
        this._applyPatches(this._patchList(patches), { user: true, forceChange: created.length > 0 });
        if (this._destroyed) return;
        if (appliedRows && appliedCols) {
            const bottom = rect.top + appliedRows - 1;
            const right = rect.left + appliedCols - 1;
            this._setSelection({ rowIndex: rect.top, colIndex: rect.left }, { rowIndex: bottom, colIndex: right });
        }
        const startRecord = this._records[rect.top];
        this._emit('onPaste', {
            startRowKey: startRecord ? startRecord.key : null,
            startKey: this._columns[rect.left].key,
            rows: data.length,
            cols: width,
            appliedRows,
            appliedCols,
            addedRows: created.length,
            truncatedRows,
            data,
        });
        let message = Locale.t('dataGrid.pasted', { rows: appliedRows, cols: appliedCols });
        if (created.length) message += Locale.t('dataGrid.pasteAdded', { count: created.length });
        if (truncatedRows) message += Locale.t('dataGrid.pasteTruncated', { count: truncatedRows });
        this._announce(message);
    }
}

export default DataGrid;
