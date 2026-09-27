/**
 * ImportWizard — 內嵌（非對話框）的資料匯入精靈：上傳 → 欄位對應 → 預覽與驗證 → 確認匯入
 *
 * - 來源：檔案（原生 file input，可拖放）或貼上的文字；以 TextDecoder 依 encoding 解碼，移除 BOM。
 * - 解析：同目錄的 csv-parser.js（RFC 4180）；delimiter:'auto' 會在 , Tab ; 之間自動判斷。
 * - 所有值一律當文字處理，畫面上只用 textContent 呈現，不解讀任何標記。
 * - 驗證：必填 → 型別轉換（number/date/boolean）→ field.validate → field.transform。
 *
 * @example
 * const wizard = new ImportWizard({
 *     fields: [
 *         { key: 'name', label: '姓名', required: true, aliases: ['full name'] },
 *         { key: 'email', label: 'Email', required: true, validate: (v) => (v.includes('@') ? null : '格式錯誤') },
 *         { key: 'joined', label: '到職日', type: 'date' }
 *     ],
 *     onImport: async (rows) => api.importStaff(rows)
 * });
 * wizard.mount('#host');
 */
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';
import { nextUid } from '../../utils/uid.js';
import { Stepper } from '../Stepper/index.js';
import { Dropdown } from '../../form/Dropdown/index.js';
import { parseCsv, detectDelimiter, stripBom, DELIMITER_CANDIDATES } from './csv-parser.js';
import './locale.js';

const STEP_KEYS = Object.freeze(['upload', 'mapping', 'preview', 'confirm']);
const FIELD_TYPES = new Set(['text', 'number', 'date', 'boolean']);
const PREVIEW_ROWS = 100;
const MAX_LISTED_ERRORS = 200;
const TRUE_WORDS = new Set(['true', 't', 'yes', 'y', '1', 'on', '是', '真']);
const FALSE_WORDS = new Set(['false', 'f', 'no', 'n', '0', 'off', '否', '假']);
const HEADER_NOISE = /[\s_\-.:\/\\()[\]{}（）【】「」『』：、,，]+/g;
const VISUALLY_HIDDEN = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap;';
const FOCUSABLE = 'input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** 比對標題用的正規化：全形轉半形、轉小寫、去掉空白與常見標點。 */
function normalizeHeader(text) {
    return String(text ?? '').normalize('NFKC').toLowerCase().replace(HEADER_NOISE, '');
}

// 注意：模組層級函式的內容縮排 4 格，不可寫成「if (...) {」這種單行開區塊，
// 否則 metadata 的公開方法擷取（4 格縮排 + 名稱(...) {）會把 if 誤認成方法。

/** 數字：接受全形數字、千分位逗號與科學記號。 */
function coerceNumber(text) {
    let normalized = text.normalize('NFKC');
    if (/^[+-]?\d{1,3}(,\d{3})+(\.\d+)?$/.test(normalized)) normalized = normalized.replace(/,/g, '');
    const value = /^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/.test(normalized) ? Number(normalized) : Number.NaN;
    return Number.isFinite(value) ? { ok: true, value } : { ok: false, error: 'number' };
}

/** 日期：YYYY-MM-DD、YYYY/MM/DD 或 YYYY.MM.DD（月日可為一位數），輸出 YYYY-MM-DD。 */
function coerceDate(text) {
    const match = /^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})$/.exec(text.normalize('NFKC'));
    if (!match) return { ok: false, error: 'date' };
    const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])];
    const date = new Date(year, month - 1, day);
    const real = date.getFullYear() === year && date.getMonth() === month - 1 && date.getDate() === day;
    return real
        ? { ok: true, value: `${match[1]}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}` }
        : { ok: false, error: 'date' };
}

/** 是／否：true/t/yes/y/1/on/是/真 與 false/f/no/n/0/off/否/假（不分大小寫）。 */
function coerceBoolean(text) {
    const word = text.normalize('NFKC').toLowerCase();
    if (TRUE_WORDS.has(word)) return { ok: true, value: true };
    if (FALSE_WORDS.has(word)) return { ok: true, value: false };
    return { ok: false, error: 'boolean' };
}

/** 依欄位型別把文字轉成值；失敗時回傳錯誤代碼（對應 importWizard.errors.*）。 */
function coerceValue(text, type) {
    if (type === 'number') return coerceNumber(text);
    if (type === 'date') return coerceDate(text);
    if (type === 'boolean') return coerceBoolean(text);
    return { ok: true, value: text };
}

function readBytes(file) {
    if (typeof file.arrayBuffer === 'function') return file.arrayBuffer();
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => reject(reader.error);
        reader.readAsArrayBuffer(file);
    });
}

export class ImportWizard {
    static STEPS = STEP_KEYS;

    constructor(options = {}) {
        this.options = {
            fields: [],                     // [{ key, label, required?, type?, aliases?, validate?, transform? }]
            accept: '.csv,.tsv,.txt',       // file input 的 accept，也用來檢查拖放的檔案
            maxFileSize: 5 * 1024 * 1024,   // 位元組上限（貼上的文字以 UTF-8 位元組數計）
            maxRows: 10000,                 // 資料列上限（不含標題列）
            encoding: 'utf-8',              // TextDecoder 編碼標籤，例如 'big5'
            delimiter: 'auto',              // 'auto' | ',' | '\t' | ';'（或其他單一字元）
            hasHeader: true,                // 第一列是否為標題
            allowPaste: true,               // 提供貼上文字的輸入區
            allowPartial: false,            // false：有任何無效列時不能確認匯入
            onImport: null,                 // async (validRows, { invalidRows, mapping, headers, rowNumbers }) => result
            onCancel: null,                 // () => void；提供時顯示「取消」按鈕
            ...options
        };

        this._uid = nextUid('cl-import-wizard');
        this._destroyed = false;
        this._localeListening = false;
        this._fields = this._normalizeFields(this.options.fields);
        this._data = null;
        this._validation = null;
        this._readToken = 0;
        this._importToken = 0;
        this._pasteDraft = '';
        this._children = [];
        this._mappingViews = new Map();

        this._state = createComponentState(this._initialState(), {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            GO_TO: (state, payload) => ({
                ...state,
                step: payload.step,
                mappingError: false,
                status: state.status === 'failed' ? 'ready' : state.status,
                importError: state.status === 'failed' ? '' : state.importError
            }),
            READING: (state) => ({
                ...state,
                status: 'reading',
                notice: null,
                warning: null,
                source: null,
                mapping: this._emptyMapping(),
                mappingError: false,
                result: null,
                importError: ''
            }),
            LOAD_FAILED: (state, payload) => ({
                ...state,
                status: 'idle',
                notice: payload.notice,
                warning: null,
                source: null,
                mapping: this._emptyMapping()
            }),
            LOADED: (state, payload) => ({
                ...state,
                status: 'ready',
                notice: null,
                warning: payload.warning ?? null,
                source: payload.source,
                mapping: payload.mapping
            }),
            SET_MAPPING: (state, payload) => ({ ...state, mapping: { ...state.mapping, [payload.key]: payload.column } }),
            MAPPING_BLOCKED: (state) => ({ ...state, mappingError: true }),
            MAPPING_RESOLVED: (state) => ({ ...state, mappingError: false }),
            IMPORTING: (state) => ({ ...state, status: 'importing', importError: '' }),
            IMPORT_DONE: (state, payload) => ({ ...state, status: 'done', result: { ...payload.result } }),
            IMPORT_FAILED: (state, payload) => ({ ...state, status: 'failed', importError: String(payload?.detail ?? '') }),
            RESET: (state) => ({ ...this._initialState(), lifecycle: state.lifecycle, visibility: state.visibility })
        });

        this._onLocaleChange = () => {
            if (!this._destroyed) this._render({ keepFocus: true });
        };

        this.element = this._createElement();
        this._render();
    }

    // ── 設定與狀態 ──────────────────────────────────────────

    _normalizeFields(fields) {
        const list = [];
        const seen = new Set();
        for (const raw of Array.isArray(fields) ? fields : []) {
            if (!raw || typeof raw !== 'object') continue;
            const key = raw.key === null || raw.key === undefined ? '' : String(raw.key);
            if (!key || seen.has(key)) continue;
            seen.add(key);
            list.push({
                key,
                label: raw.label === null || raw.label === undefined || raw.label === '' ? key : String(raw.label),
                required: Boolean(raw.required),
                type: FIELD_TYPES.has(raw.type) ? raw.type : 'text',
                aliases: Array.isArray(raw.aliases) ? raw.aliases.map((alias) => String(alias)) : [],
                validate: typeof raw.validate === 'function' ? raw.validate : null,
                transform: typeof raw.transform === 'function' ? raw.transform : null
            });
        }
        return list;
    }

    _emptyMapping() {
        return Object.fromEntries(this._fields.map((field) => [field.key, null]));
    }

    _initialState() {
        return {
            lifecycle: 'created',
            visibility: 'visible',
            step: 0,
            status: 'idle',
            notice: null,
            warning: null,
            source: null,
            mapping: this._emptyMapping(),
            mappingError: false,
            result: null,
            importError: ''
        };
    }

    _snap() {
        return this._state.snapshot();
    }

    _send(event, payload = null) {
        return this._state.send(event, payload);
    }

    _maxRows() {
        const value = Number(this.options.maxRows);
        return Number.isFinite(value) && value >= 1 ? Math.floor(value) : 10000;
    }

    _hasHeader() {
        return this.options.hasHeader !== false;
    }

    _stepDefs() {
        return STEP_KEYS.map((key) => ({ title: Locale.t(`importWizard.steps.${key}`) }));
    }

    _stepIndex(step) {
        if (typeof step === 'string') {
            const index = STEP_KEYS.indexOf(step);
            return index >= 0 ? index : null;
        }
        return Number.isInteger(step) && step >= 0 && step < STEP_KEYS.length ? step : null;
    }

    _missingRequired(state = this._snap()) {
        return this._fields.filter((field) => field.required && (state.mapping[field.key] === null || state.mapping[field.key] === undefined));
    }

    _canConfirm() {
        if (!this._data) return false;
        const { valid, invalid } = this._validate();
        return valid.length > 0 && (this.options.allowPartial || invalid.length === 0);
    }

    _canReach(index, state = this._snap()) {
        if (['reading', 'importing', 'done'].includes(state.status)) return false;
        if (index === 0) return true;
        if (!this._data) return false;
        if (index === 1) return true;
        if (this._missingRequired(state).length) return false;
        if (index === 2) return true;
        return this._canConfirm();
    }

    _formatSize(bytes) {
        const round = (value) => Math.round(value * 10) / 10;
        if (bytes < 1024) return Locale.t('importWizard.size.bytes', { value: bytes });
        if (bytes < 1024 * 1024) return Locale.t('importWizard.size.kilobytes', { value: round(bytes / 1024) });
        return Locale.t('importWizard.size.megabytes', { value: round(bytes / (1024 * 1024)) });
    }

    _message(notice) {
        return notice ? Locale.t(notice.key, notice.params) : '';
    }

    // ── 讀檔與解析 ──────────────────────────────────────────

    _acceptsFile(file) {
        const rules = String(this.options.accept || '').split(',').map((rule) => rule.trim().toLowerCase()).filter(Boolean);
        if (!rules.length) return true;
        const name = String(file.name || '').toLowerCase();
        const type = String(file.type || '').toLowerCase();
        return rules.some((rule) => {
            if (rule.startsWith('.')) return name.endsWith(rule);
            if (rule.endsWith('/*')) return type.startsWith(rule.slice(0, -1));
            return type === rule;
        });
    }

    _fail(notice) {
        this._data = null;
        this._validation = null;
        this._send('LOAD_FAILED', { notice });
        this._render({ keepFocus: true });
    }

    async _loadFile(file) {
        if (!file || this._destroyed) return;
        const { status } = this._snap();
        if (status === 'reading' || status === 'importing') return;
        const token = ++this._readToken;
        if (!this._acceptsFile(file)) {
            this._fail({ key: 'importWizard.errors.fileType', params: { name: String(file.name || '') } });
            return;
        }
        const max = Number(this.options.maxFileSize);
        if (Number.isFinite(max) && max > 0 && file.size > max) {
            this._fail({ key: 'importWizard.errors.fileSize', params: { size: this._formatSize(file.size), max: this._formatSize(max) } });
            return;
        }

        this._data = null;
        this._validation = null;
        this._send('READING');
        this._render({ keepFocus: true });
        this._announce(Locale.t('importWizard.upload.reading'));

        let buffer;
        try {
            buffer = await readBytes(file);
        } catch {
            if (!this._destroyed && token === this._readToken) this._fail({ key: 'importWizard.errors.readFailed' });
            return;
        }
        if (this._destroyed || token !== this._readToken) return;

        const encoding = String(this.options.encoding || 'utf-8');
        let text;
        try {
            text = new TextDecoder(encoding).decode(buffer);
        } catch {
            this._fail({ key: 'importWizard.errors.encoding', params: { encoding } });
            return;
        }
        this._loadText(text, { name: String(file.name || ''), decoded: true });
    }

    _loadPasted() {
        const { status } = this._snap();
        if (this._destroyed || status === 'reading' || status === 'importing') return;
        const text = this._pasteDraft;
        if (!text.trim()) {
            this._fail({ key: 'importWizard.errors.pasteEmpty' });
            return;
        }
        const max = Number(this.options.maxFileSize);
        const bytes = new TextEncoder().encode(text).length;
        if (Number.isFinite(max) && max > 0 && bytes > max) {
            this._fail({ key: 'importWizard.errors.fileSize', params: { size: this._formatSize(bytes), max: this._formatSize(max) } });
            return;
        }
        this._readToken += 1;
        this._loadText(text, { name: '', pasted: true, decoded: false });
    }

    _resolveDelimiter(text, name) {
        const option = this.options.delimiter;
        if (option === 'auto' || option === null || option === undefined) {
            return detectDelimiter(text, /\.tsv$/i.test(name) ? ['\t', ',', ';'] : DELIMITER_CANDIDATES);
        }
        return typeof option === 'string' && option.length === 1 ? option : ',';
    }

    _loadText(text, { name = '', pasted = false, decoded = false } = {}) {
        const clean = stripBom(text);
        if (!clean.trim()) {
            this._fail({ key: 'importWizard.errors.empty' });
            return;
        }
        const hasHeader = this._hasHeader();
        const maxRows = this._maxRows();
        let parsed;
        try {
            parsed = parseCsv(clean, {
                delimiter: this._resolveDelimiter(clean, name),
                skipEmptyLines: 'greedy',
                maxRecords: maxRows + (hasHeader ? 1 : 0) + 1
            });
        } catch {
            this._fail({ key: 'importWizard.errors.readFailed' });
            return;
        }
        if (!parsed.rows.length) {
            this._fail({ key: 'importWizard.errors.empty' });
            return;
        }
        const headerError = hasHeader ? parsed.errors.find((error) => error.record === 0) : null;
        if (headerError) {
            this._fail({ key: 'importWizard.errors.headerInvalid', params: { line: headerError.line } });
            return;
        }
        const rows = hasHeader ? parsed.rows.slice(1) : parsed.rows;
        if (!rows.length) {
            this._fail({ key: 'importWizard.errors.noDataRows' });
            return;
        }
        if (rows.length > maxRows) {
            this._fail({ key: 'importWizard.errors.tooManyRows', params: { max: maxRows } });
            return;
        }

        const columnCount = hasHeader ? parsed.rows[0].length : rows[0].length;
        const headers = hasHeader ? parsed.rows[0].map((header) => header.trim()) : new Array(columnCount).fill('');
        const rowErrors = new Map();
        parsed.errors.forEach((error) => {
            const index = hasHeader ? error.record - 1 : error.record;
            if (index >= 0) rowErrors.set(index, error);
        });
        this._data = {
            name,
            pasted,
            headers,
            rows,
            lines: hasHeader ? parsed.lines.slice(1) : parsed.lines,
            rowErrors,
            columnCount
        };
        this._validation = null;

        const encoding = String(this.options.encoding || 'utf-8');
        this._send('LOADED', {
            source: { name, pasted, rows: rows.length, columns: columnCount },
            mapping: this._autoMap(headers, hasHeader),
            warning: decoded && clean.includes('�') ? { key: 'importWizard.errors.decodeWarning', params: { encoding } } : null
        });
        this._render({ keepFocus: true });
        this._announce(this._loadedText(this._snap().source));
    }

    _loadedText(source) {
        const name = source.pasted ? Locale.t('importWizard.upload.pastedName') : source.name;
        return Locale.t('importWizard.upload.loaded', { name, rows: source.rows, columns: source.columns });
    }

    /** 以正規化後的標題比對欄位的 key、label 與 aliases；沒有標題列時依位置對應。 */
    _autoMap(headers, hasHeader) {
        const mapping = this._emptyMapping();
        if (!hasHeader) {
            this._fields.forEach((field, index) => {
                mapping[field.key] = index < headers.length ? index : null;
            });
            return mapping;
        }
        const normalized = headers.map(normalizeHeader);
        const used = new Set();
        for (const field of this._fields) {
            const candidates = new Set([field.key, field.label, ...field.aliases].map(normalizeHeader).filter(Boolean));
            const column = normalized.findIndex((header, index) => header && !used.has(index) && candidates.has(header));
            if (column >= 0) {
                mapping[field.key] = column;
                used.add(column);
            }
        }
        return mapping;
    }

    _columnLabel(index) {
        const headers = this._data?.headers ?? [];
        const header = headers[index];
        if (!header) return Locale.t('importWizard.mapping.column', { index: index + 1 });
        const duplicates = headers.filter((item) => item === header).length;
        return duplicates > 1 ? Locale.t('importWizard.mapping.columnWithIndex', { header, index: index + 1 }) : header;
    }

    // ── 驗證 ────────────────────────────────────────────────

    _validate() {
        if (this._validation) return this._validation;
        const result = { valid: [], rowNumbers: [], invalid: [], byIndex: new Map() };
        if (!this._data) return result;
        const { mapping } = this._snap();
        const { rows, lines, rowErrors, columnCount } = this._data;

        rows.forEach((row, index) => {
            const rowNumber = lines[index];
            const errors = [];
            const raw = {};
            for (const field of this._fields) {
                const column = mapping[field.key];
                raw[field.key] = column === null || column === undefined ? '' : String(row[column] ?? '').trim();
            }

            const parseError = rowErrors.get(index);
            if (parseError) errors.push({ field: null, message: Locale.t(`importWizard.errors.${parseError.code}`) });
            if (row.length !== columnCount) {
                errors.push({ field: null, message: Locale.t('importWizard.errors.columnCount', { expected: columnCount, actual: row.length }) });
            }
            if (errors.length) {
                result.invalid.push({ row: rowNumber, values: raw, errors });
                result.byIndex.set(index, { rowLevel: true, fields: new Map() });
                return;
            }

            const typed = {};
            const failed = new Map();
            for (const field of this._fields) {
                const text = raw[field.key];
                typed[field.key] = null;
                if (text === '') {
                    if (field.required) failed.set(field.key, Locale.t('importWizard.errors.required'));
                    continue;
                }
                const coerced = coerceValue(text, field.type);
                if (coerced.ok) typed[field.key] = coerced.value;
                else failed.set(field.key, Locale.t(`importWizard.errors.${coerced.error}`));
            }
            for (const field of this._fields) {
                if (!field.validate || failed.has(field.key) || typed[field.key] === null) continue;
                try {
                    const message = field.validate(typed[field.key], { ...typed });
                    if (message) failed.set(field.key, String(message));
                } catch {
                    failed.set(field.key, Locale.t('importWizard.errors.validateFailed'));
                }
            }

            const output = {};
            if (!failed.size) {
                for (const field of this._fields) {
                    const value = typed[field.key];
                    if (!field.transform || value === null) {
                        output[field.key] = value;
                        continue;
                    }
                    try {
                        output[field.key] = field.transform(value);
                    } catch {
                        failed.set(field.key, Locale.t('importWizard.errors.transformFailed'));
                    }
                }
            }

            if (failed.size) {
                this._fields.forEach((field) => {
                    if (failed.has(field.key)) errors.push({ field: field.key, message: failed.get(field.key) });
                });
                result.invalid.push({ row: rowNumber, values: raw, errors });
                result.byIndex.set(index, { rowLevel: false, fields: failed });
            } else {
                result.valid.push(output);
                result.rowNumbers.push(rowNumber);
            }
        });

        this._validation = result;
        return result;
    }

    // ── DOM ────────────────────────────────────────────────

    _createElement() {
        const root = document.createElement('div');
        root.className = 'cl-import-wizard';
        root.setAttribute('role', 'group');
        root.style.cssText = 'position:relative;display:flex;flex-direction:column;gap:16px;width:100%;max-width:100%;min-width:0;box-sizing:border-box;padding:16px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-lg);background:var(--cl-bg);color:var(--cl-text);font-family:var(--cl-font-family);font-size:var(--cl-font-size-md);';

        const progress = document.createElement('div');
        progress.className = 'cl-import-wizard__progress';
        this._stepper = new Stepper({ steps: this._stepDefs(), current: 0, size: 'small' });
        this._stepper.mount(progress);

        const panel = document.createElement('div');
        panel.className = 'cl-import-wizard__panel';
        panel.style.cssText = 'display:flex;flex-direction:column;gap:12px;min-width:0;';
        this._panel = panel;

        const footer = document.createElement('div');
        footer.className = 'cl-import-wizard__footer';
        footer.style.cssText = 'display:flex;flex-wrap:wrap;justify-content:flex-end;align-items:center;gap:8px;padding-top:12px;border-top:1px solid var(--cl-border-light);';
        this._footer = footer;

        const live = document.createElement('div');
        live.className = 'cl-import-wizard__live';
        live.setAttribute('role', 'status');
        live.setAttribute('aria-live', 'polite');
        live.setAttribute('aria-atomic', 'true');
        live.style.cssText = VISUALLY_HIDDEN;
        this._live = live;

        root.append(progress, panel, footer, live);
        return root;
    }

    _announce(message) {
        this._live.textContent = '';
        this._live.textContent = message;
    }

    _destroyChildren() {
        this._children.forEach((child) => child.destroy?.());
        this._children = [];
        this._mappingViews.clear();
    }

    _render({ focusKey = null, keepFocus = false } = {}) {
        let restore = focusKey;
        if (!restore && keepFocus) {
            const active = document.activeElement;
            if (active && this.element.contains(active)) restore = active.closest('[data-focus-key]')?.dataset.focusKey ?? null;
        }

        const state = this._snap();
        this._destroyChildren();
        this.element.style.display = state.visibility === 'hidden' ? 'none' : 'flex';
        this.element.setAttribute('aria-label', Locale.t('importWizard.label'));
        this.element.dataset.step = STEP_KEYS[state.step];
        this.element.dataset.status = state.status;
        this._stepper.options.complete = state.status === 'done';
        this._stepper.setSteps(this._stepDefs());
        this._stepper.goTo(state.step);

        const builders = [this._buildUpload, this._buildMapping, this._buildPreview, this._buildConfirm];
        const { content, buttons } = builders[state.step].call(this, state);
        this._panel.replaceChildren(...content);
        const busy = state.status === 'reading' || state.status === 'importing';
        this._panel.setAttribute('aria-busy', busy ? 'true' : 'false');
        this._footer.replaceChildren(...buttons);

        if (restore) this._focusKey(restore);
    }

    _focusKey(key) {
        const element = [...this.element.querySelectorAll('[data-focus-key]')].find((item) => item.dataset.focusKey === key);
        if (!element) return;
        const target = element.matches(FOCUSABLE) || element.getAttribute('tabindex') === '-1' ? element : element.querySelector(FOCUSABLE);
        target?.focus();
    }

    _heading(text) {
        const heading = document.createElement('div');
        heading.className = 'cl-import-wizard__heading';
        heading.setAttribute('role', 'heading');
        heading.setAttribute('aria-level', '3');
        heading.tabIndex = -1;
        heading.dataset.focusKey = 'heading';
        heading.textContent = text;
        heading.style.cssText = 'margin:0;font-size:var(--cl-font-size-lg);font-weight:600;color:var(--cl-text-heading);';
        return heading;
    }

    _paragraph(text, className, tone = 'default') {
        const paragraph = document.createElement('p');
        paragraph.className = className;
        paragraph.textContent = text;
        const color = { default: 'var(--cl-text)', muted: 'var(--cl-text-muted)', danger: 'var(--cl-danger)', warning: 'var(--cl-warning-dark)', success: 'var(--cl-success-dark)' }[tone];
        paragraph.style.cssText = `margin:0;font-size:var(--cl-font-size-md);line-height:1.5;color:${color};overflow-wrap:anywhere;`;
        return paragraph;
    }

    _alert(text, className) {
        const alert = this._paragraph(text, className, 'danger');
        alert.setAttribute('role', 'alert');
        return alert;
    }

    /**
     * busy：以 aria-disabled 表示「進行中、暫不可按」，按鈕仍可聚焦，焦點不會在忙碌期間遺失。
     */
    _button(label, { key, primary = false, disabled = false, busy = false, onActivate, describedBy = null }) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = `cl-import-wizard__button cl-import-wizard__button--${key}`;
        button.dataset.focusKey = key;
        button.textContent = label;
        button.style.cssText = 'height:34px;padding:0 16px;box-sizing:border-box;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg);color:var(--cl-text);font-size:var(--cl-font-size-md);font-family:inherit;cursor:pointer;';
        if (primary) {
            button.style.background = 'var(--cl-primary)';
            button.style.borderColor = 'var(--cl-primary)';
            button.style.color = 'var(--cl-text-inverse)';
        }
        button.disabled = Boolean(disabled);
        if (busy) button.setAttribute('aria-disabled', 'true');
        if (button.disabled || busy) {
            button.style.opacity = '0.5';
            button.style.cursor = 'not-allowed';
        }
        if (describedBy) button.setAttribute('aria-describedby', describedBy);
        button.addEventListener('click', () => {
            if (!button.disabled && button.getAttribute('aria-disabled') !== 'true') onActivate();
        });
        return button;
    }

    _cancelButton(state) {
        if (typeof this.options.onCancel !== 'function' || state.status === 'done') return [];
        const button = this._button(Locale.t('importWizard.buttons.cancel'), {
            key: 'cancel',
            disabled: state.status === 'importing',
            onActivate: () => this._cancel()
        });
        button.style.marginRight = 'auto';
        return [button];
    }

    _backButton(state, step) {
        return this._button(Locale.t('importWizard.buttons.back'), {
            key: 'back',
            disabled: state.status === 'importing',
            onActivate: () => this._navigate(step)
        });
    }

    // ── 各步驟 ──────────────────────────────────────────────

    _buildUpload(state) {
        const busy = state.status === 'reading';
        const content = [this._heading(Locale.t('importWizard.upload.heading'))];

        const dropzone = document.createElement('div');
        dropzone.className = 'cl-import-wizard__dropzone';
        dropzone.style.cssText = 'display:flex;flex-direction:column;align-items:flex-start;gap:8px;padding:16px;border:2px dashed var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg-secondary);min-width:0;';
        const fileId = `${this._uid}-file`;
        const fileLabel = document.createElement('label');
        fileLabel.className = 'cl-import-wizard__file-label';
        fileLabel.htmlFor = fileId;
        fileLabel.textContent = Locale.t('importWizard.upload.fileLabel');
        fileLabel.style.cssText = 'font-weight:600;font-size:var(--cl-font-size-md);';
        const fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.id = fileId;
        fileInput.className = 'cl-import-wizard__file';
        fileInput.accept = String(this.options.accept || '');
        fileInput.dataset.focusKey = 'file';
        fileInput.style.cssText = 'max-width:100%;font-size:var(--cl-font-size-md);font-family:inherit;color:var(--cl-text);';
        fileInput.addEventListener('change', () => this._loadFile(fileInput.files?.[0]));
        dropzone.append(
            this._paragraph(Locale.t('importWizard.upload.dropHint'), 'cl-import-wizard__drop-hint'),
            fileLabel,
            fileInput,
            this._paragraph(Locale.t('importWizard.upload.acceptHint', {
                accept: String(this.options.accept || '*'),
                size: this._formatSize(Number(this.options.maxFileSize) || 0)
            }), 'cl-import-wizard__accept-hint', 'muted')
        );
        const highlight = (on) => {
            dropzone.style.borderColor = on ? 'var(--cl-primary)' : 'var(--cl-border)';
            dropzone.style.background = on ? 'var(--cl-primary-light)' : 'var(--cl-bg-secondary)';
        };
        dropzone.addEventListener('dragenter', (event) => {
            event.preventDefault();
            if (!busy) highlight(true);
        });
        dropzone.addEventListener('dragover', (event) => {
            event.preventDefault();
            if (!busy) highlight(true);
        });
        dropzone.addEventListener('dragleave', (event) => {
            if (!dropzone.contains(event.relatedTarget)) highlight(false);
        });
        dropzone.addEventListener('drop', (event) => {
            event.preventDefault();
            highlight(false);
            if (busy) return;
            const file = event.dataTransfer?.files?.[0];
            if (file) this._loadFile(file);
        });
        content.push(dropzone);

        if (this.options.allowPaste) {
            const pasteWrap = document.createElement('div');
            pasteWrap.className = 'cl-import-wizard__paste';
            pasteWrap.style.cssText = 'display:flex;flex-direction:column;align-items:flex-start;gap:6px;min-width:0;';
            const pasteId = `${this._uid}-paste`;
            const pasteLabel = document.createElement('label');
            pasteLabel.htmlFor = pasteId;
            pasteLabel.textContent = Locale.t(this._hasHeader() ? 'importWizard.upload.pasteLabel' : 'importWizard.upload.pasteLabelNoHeader');
            pasteLabel.style.cssText = 'font-weight:600;font-size:var(--cl-font-size-md);';
            const textarea = document.createElement('textarea');
            textarea.id = pasteId;
            textarea.className = 'cl-import-wizard__paste-input';
            textarea.rows = 5;
            textarea.placeholder = Locale.t('importWizard.upload.pastePlaceholder');
            textarea.value = this._pasteDraft;
            textarea.spellcheck = false;
            textarea.dataset.focusKey = 'paste';
            textarea.style.cssText = 'width:100%;box-sizing:border-box;padding:8px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg);color:var(--cl-text);font-family:var(--cl-font-family-mono);font-size:var(--cl-font-size-sm);resize:vertical;';
            textarea.addEventListener('input', () => {
                this._pasteDraft = textarea.value;
            });
            const usePaste = this._button(Locale.t('importWizard.upload.usePaste'), {
                key: 'use-paste',
                busy,
                onActivate: () => this._loadPasted()
            });
            pasteWrap.append(pasteLabel, textarea, usePaste);
            content.push(pasteWrap);
        }

        if (busy) content.push(this._paragraph(Locale.t('importWizard.upload.reading'), 'cl-import-wizard__reading', 'muted'));
        if (state.notice) content.push(this._alert(this._message(state.notice), 'cl-import-wizard__error'));
        if (state.source && state.status === 'ready') {
            content.push(this._paragraph(this._loadedText(state.source), 'cl-import-wizard__loaded', 'success'));
            if (state.warning) content.push(this._paragraph(this._message(state.warning), 'cl-import-wizard__warning', 'warning'));
        }

        return {
            content,
            buttons: [
                ...this._cancelButton(state),
                this._button(Locale.t('importWizard.buttons.next'), {
                    key: 'next',
                    primary: true,
                    disabled: !(state.status === 'ready' && this._data),
                    onActivate: () => this._navigate(1)
                })
            ]
        };
    }

    _buildMapping(state) {
        const content = [
            this._heading(Locale.t('importWizard.mapping.heading')),
            this._paragraph(Locale.t('importWizard.mapping.hint'), 'cl-import-wizard__hint', 'muted')
        ];
        const columns = (this._data?.headers ?? []).map((_, index) => ({ value: index, label: this._columnLabel(index) }));
        const missing = new Set(state.mappingError ? this._missingRequired(state).map((field) => field.key) : []);

        const list = document.createElement('div');
        list.className = 'cl-import-wizard__mapping';
        list.style.cssText = 'display:flex;flex-direction:column;gap:10px;min-width:0;';
        this._fields.forEach((field, index) => {
            const row = document.createElement('div');
            row.className = 'cl-import-wizard__mapping-row';
            row.dataset.field = field.key;
            row.style.cssText = 'display:flex;flex-wrap:wrap;align-items:flex-start;gap:8px 12px;min-width:0;';

            const label = document.createElement('span');
            label.className = 'cl-import-wizard__field-label';
            label.style.cssText = 'flex:0 0 160px;max-width:100%;padding-top:6px;font-weight:600;overflow-wrap:anywhere;';
            label.textContent = field.label;
            if (field.required) {
                const mark = document.createElement('span');
                mark.setAttribute('aria-hidden', 'true');
                mark.textContent = ` ${Locale.t('importWizard.mapping.requiredMark')}`;
                mark.style.color = 'var(--cl-danger)';
                label.appendChild(mark);
            }

            const host = document.createElement('div');
            host.className = 'cl-import-wizard__mapping-control';
            host.style.cssText = 'flex:1 1 220px;min-width:0;';
            const dropdown = new Dropdown({
                variant: 'searchable',
                size: 'small',
                items: columns,
                value: state.mapping[field.key],
                placeholder: Locale.t('importWizard.mapping.notMapped'),
                onChange: (value) => this._setMapping(field.key, value)
            });
            dropdown.mount(host);
            if (dropdown.input) {
                dropdown.input.setAttribute('aria-label', Locale.t('importWizard.mapping.fieldLabel', { field: field.label }));
                dropdown.input.dataset.focusKey = `map-${index}`;
                if (field.required) dropdown.input.setAttribute('aria-required', 'true');
            }
            if (missing.has(field.key)) dropdown.setError(Locale.t('importWizard.mapping.required'));

            const sample = document.createElement('span');
            sample.className = 'cl-import-wizard__sample';
            sample.style.cssText = 'flex:1 1 160px;min-width:0;padding-top:6px;font-size:var(--cl-font-size-sm);color:var(--cl-text-muted);overflow-wrap:anywhere;';

            row.append(label, host, sample);
            list.appendChild(row);
            this._children.push(dropdown);
            this._mappingViews.set(field.key, { dropdown, sample });
            this._updateSample(field.key, state.mapping[field.key]);
        });
        content.push(list);

        if (state.mappingError && missing.size) content.push(this._alert(this._missingText(state), 'cl-import-wizard__mapping-error'));

        return {
            content,
            buttons: [
                ...this._cancelButton(state),
                this._backButton(state, 0),
                this._button(Locale.t('importWizard.buttons.next'), {
                    key: 'next',
                    primary: true,
                    onActivate: () => this._submitMapping()
                })
            ]
        };
    }

    _missingText(state = this._snap()) {
        const labels = this._missingRequired(state).map((field) => field.label);
        return Locale.t('importWizard.mapping.missingRequired', { fields: labels.join(Locale.t('importWizard.mapping.fieldSeparator')) });
    }

    _updateSample(key, column) {
        const view = this._mappingViews.get(key);
        if (!view) return;
        const value = column === null || column === undefined ? '' : String(this._data?.rows?.[0]?.[column] ?? '').trim();
        view.sample.textContent = value ? Locale.t('importWizard.mapping.sample', { value }) : '';
    }

    _buildPreview(state) {
        const { valid, invalid, byIndex } = this._validate();
        const rows = this._data?.rows ?? [];
        const content = [
            this._heading(Locale.t('importWizard.preview.heading')),
            this._paragraph(Locale.t('importWizard.preview.summary', { total: rows.length, valid: valid.length, invalid: invalid.length }), 'cl-import-wizard__summary')
        ];

        let blockedText = '';
        if (!valid.length) blockedText = Locale.t('importWizard.preview.nothingToImport');
        else if (invalid.length && !this.options.allowPartial) blockedText = Locale.t('importWizard.preview.blocked', { count: invalid.length });
        const blockedId = `${this._uid}-blocked`;
        if (blockedText) {
            const blocked = this._paragraph(blockedText, 'cl-import-wizard__blocked', 'danger');
            blocked.id = blockedId;
            content.push(blocked);
        }

        const errorsSection = document.createElement('div');
        errorsSection.className = 'cl-import-wizard__errors';
        errorsSection.style.cssText = 'display:flex;flex-direction:column;gap:6px;min-width:0;';
        if (invalid.length) {
            const title = document.createElement('div');
            title.setAttribute('role', 'heading');
            title.setAttribute('aria-level', '4');
            title.textContent = Locale.t('importWizard.preview.errorsHeading');
            title.style.cssText = 'font-weight:600;font-size:var(--cl-font-size-md);';
            const list = document.createElement('ul');
            list.className = 'cl-import-wizard__error-list';
            list.style.cssText = 'margin:0;padding-left:20px;max-height:180px;overflow:auto;font-size:var(--cl-font-size-sm);color:var(--cl-danger);';
            const labels = new Map(this._fields.map((field) => [field.key, field.label]));
            let total = 0;
            for (const entry of invalid) {
                for (const error of entry.errors) {
                    total += 1;
                    if (total > MAX_LISTED_ERRORS) continue;
                    const item = document.createElement('li');
                    item.textContent = Locale.t('importWizard.preview.errorItem', {
                        row: entry.row,
                        field: error.field === null ? Locale.t('importWizard.preview.rowLevel') : labels.get(error.field),
                        message: error.message
                    });
                    list.appendChild(item);
                }
            }
            errorsSection.append(title, list);
            if (total > MAX_LISTED_ERRORS) {
                errorsSection.appendChild(this._paragraph(Locale.t('importWizard.preview.moreErrors', { count: total - MAX_LISTED_ERRORS }), 'cl-import-wizard__more-errors', 'muted'));
            }
        } else {
            errorsSection.appendChild(this._paragraph(Locale.t('importWizard.preview.noErrors'), 'cl-import-wizard__no-errors', 'success'));
        }
        content.push(errorsSection);
        content.push(this._previewTable(state, rows, byIndex));

        return {
            content,
            buttons: [
                ...this._cancelButton(state),
                this._backButton(state, 1),
                this._button(Locale.t('importWizard.buttons.next'), {
                    key: 'next',
                    primary: true,
                    disabled: Boolean(blockedText),
                    describedBy: blockedText ? blockedId : null,
                    onActivate: () => this._navigate(3)
                })
            ]
        };
    }

    _previewTable(state, rows, byIndex) {
        const count = Math.min(PREVIEW_ROWS, rows.length);
        const caption = Locale.t('importWizard.preview.caption', { count });
        const wrap = document.createElement('div');
        wrap.className = 'cl-import-wizard__table-wrap';
        wrap.tabIndex = 0;
        wrap.setAttribute('role', 'region');
        wrap.setAttribute('aria-label', caption);
        wrap.style.cssText = 'max-height:320px;overflow:auto;border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-md);';

        const table = document.createElement('table');
        table.className = 'cl-import-wizard__table';
        table.style.cssText = 'width:100%;border-collapse:collapse;font-size:var(--cl-font-size-sm);';
        const captionEl = document.createElement('caption');
        captionEl.textContent = caption;
        captionEl.style.cssText = 'caption-side:top;text-align:left;padding:6px 8px;color:var(--cl-text-muted);';
        table.appendChild(captionEl);

        const cellStyle = 'padding:6px 8px;border-bottom:1px solid var(--cl-border-light);text-align:left;vertical-align:top;max-width:280px;overflow-wrap:anywhere;';
        const thead = document.createElement('thead');
        const headRow = document.createElement('tr');
        [Locale.t('importWizard.preview.rowHeader'), ...this._fields.map((field) => field.label)].forEach((text) => {
            const th = document.createElement('th');
            th.scope = 'col';
            th.textContent = text;
            th.style.cssText = `${cellStyle}position:sticky;top:0;background:var(--cl-bg-secondary);font-weight:600;`;
            headRow.appendChild(th);
        });
        thead.appendChild(headRow);
        table.appendChild(thead);

        const tbody = document.createElement('tbody');
        const { mapping } = state;
        const fragment = document.createDocumentFragment();
        for (let index = 0; index < count; index += 1) {
            const row = rows[index];
            const status = byIndex.get(index);
            const tr = document.createElement('tr');
            if (status) tr.dataset.invalid = 'true';
            const th = document.createElement('th');
            th.scope = 'row';
            th.textContent = String(this._data.lines[index]);
            th.style.cssText = `${cellStyle}font-weight:400;color:${status?.rowLevel ? 'var(--cl-danger)' : 'var(--cl-text-muted)'};`;
            tr.appendChild(th);
            for (const field of this._fields) {
                const td = document.createElement('td');
                const column = mapping[field.key];
                td.textContent = column === null || column === undefined ? '' : String(row[column] ?? '').trim();
                td.style.cssText = cellStyle;
                const message = status?.fields.get(field.key);
                if (message) {
                    td.dataset.invalid = 'true';
                    td.title = message;
                    td.style.color = 'var(--cl-danger)';
                    td.style.fontWeight = '600';
                    td.style.background = 'var(--cl-bg-danger-light)';
                }
                tr.appendChild(td);
            }
            fragment.appendChild(tr);
        }
        tbody.appendChild(fragment);
        table.appendChild(tbody);
        wrap.appendChild(table);
        return wrap;
    }

    _buildConfirm(state) {
        const content = [this._heading(Locale.t('importWizard.confirm.heading'))];
        if (state.status === 'done') {
            content.push(this._paragraph(Locale.t('importWizard.confirm.success', state.result), 'cl-import-wizard__result', 'success'));
            return {
                content,
                buttons: [this._button(Locale.t('importWizard.confirm.startOver'), {
                    key: 'restart',
                    primary: true,
                    onActivate: () => this.reset()
                })]
            };
        }

        const { valid, invalid } = this._validate();
        content.push(this._paragraph(Locale.t('importWizard.confirm.summary', { valid: valid.length }), 'cl-import-wizard__summary'));
        if (invalid.length) content.push(this._paragraph(Locale.t('importWizard.confirm.skipped', { invalid: invalid.length }), 'cl-import-wizard__skipped', 'warning'));
        const importing = state.status === 'importing';
        if (importing) content.push(this._paragraph(Locale.t('importWizard.confirm.busy'), 'cl-import-wizard__busy', 'muted'));
        if (state.status === 'failed') {
            content.push(this._alert(state.importError
                ? Locale.t('importWizard.confirm.failureDetail', { detail: state.importError })
                : Locale.t('importWizard.confirm.failure'), 'cl-import-wizard__failure'));
        }
        const failed = state.status === 'failed';
        return {
            content,
            buttons: [
                ...this._cancelButton(state),
                this._backButton(state, 2),
                this._button(importing
                    ? Locale.t('importWizard.confirm.busy')
                    : Locale.t(failed ? 'importWizard.confirm.retry' : 'importWizard.confirm.start'), {
                    key: failed ? 'retry' : 'import',
                    primary: true,
                    busy: importing,
                    onActivate: () => this._runImport()
                })
            ]
        };
    }

    // ── 動作 ────────────────────────────────────────────────

    _navigate(step) {
        const state = this._snap();
        if (!this._canReach(step, state)) return false;
        if (step !== state.step) this._send('GO_TO', { step });
        this._render({ focusKey: 'heading' });
        this._announce(Locale.t('importWizard.stepStatus', {
            current: step + 1,
            total: STEP_KEYS.length,
            title: Locale.t(`importWizard.steps.${STEP_KEYS[step]}`)
        }));
        return true;
    }

    _setMapping(key, value) {
        const column = value === null || value === undefined || value === '' ? null : Number(value);
        const nextColumn = Number.isInteger(column) && column >= 0 ? column : null;
        this._send('SET_MAPPING', { key, column: nextColumn });
        this._validation = null;
        this._updateSample(key, nextColumn);
        const view = this._mappingViews.get(key);
        if (view && nextColumn !== null) view.dropdown.clearError();
        const state = this._snap();
        if (state.mappingError) {
            const alert = this._panel.querySelector('.cl-import-wizard__mapping-error');
            if (!this._missingRequired(state).length) {
                this._send('MAPPING_RESOLVED');
                alert?.remove();
            } else if (alert) {
                alert.textContent = this._missingText(state);
            }
        }
    }

    _submitMapping() {
        const missing = this._missingRequired();
        if (!missing.length) {
            this._navigate(2);
            return;
        }
        this._send('MAPPING_BLOCKED');
        this._render({ focusKey: `map-${this._fields.indexOf(missing[0])}` });
    }

    _cancel() {
        this.reset();
        if (typeof this.options.onCancel === 'function') this.options.onCancel();
    }

    async _runImport() {
        const state = this._snap();
        if (this._destroyed || state.step !== 3 || state.status === 'importing' || state.status === 'done' || !this._canConfirm()) return;
        const token = ++this._importToken;
        const { valid, rowNumbers, invalid } = this._validate();
        this._send('IMPORTING');
        this._render({ keepFocus: true });
        this._announce(Locale.t('importWizard.confirm.busy'));

        let result;
        try {
            result = typeof this.options.onImport === 'function'
                ? await this.options.onImport(valid.map((row) => ({ ...row })), {
                    invalidRows: invalid.map((entry) => ({ row: entry.row, values: { ...entry.values }, errors: entry.errors.map((error) => ({ ...error })) })),
                    mapping: this.getMapping(),
                    headers: [...(this._data?.headers ?? [])],
                    rowNumbers: [...rowNumbers]
                })
                : undefined;
        } catch (error) {
            if (this._destroyed || token !== this._importToken) return;
            const detail = error && typeof error.message === 'string' ? error.message : '';
            this._send('IMPORT_FAILED', { detail });
            this._render({ focusKey: 'retry' });
            this._announce(detail ? Locale.t('importWizard.confirm.failureDetail', { detail }) : Locale.t('importWizard.confirm.failure'));
            return;
        }
        if (this._destroyed || token !== this._importToken) return;
        const imported = Number.isFinite(result?.imported) ? result.imported : valid.length;
        const failed = Number.isFinite(result?.failed) ? result.failed : invalid.length;
        this._send('IMPORT_DONE', { result: { imported, failed } });
        this._render({ focusKey: 'restart' });
        this._announce(Locale.t('importWizard.confirm.success', { imported, failed }));
    }

    // ── 公開 API ────────────────────────────────────────────

    snapshot() {
        return this._state.snapshot();
    }

    /**
     * 切換到指定步驟（0–3 或 'upload'|'mapping'|'preview'|'confirm'），只能前往目前可到達的步驟：
     * 需先載入資料才能到對應；必填欄位都對應後才能到預覽；可確認匯入時才能到確認。
     * 讀檔中、匯入中或匯入完成後一律回傳 false（完成後請用 reset()）。
     * @returns {boolean} 是否已位於該步驟
     */
    goTo(step) {
        if (this._destroyed) return false;
        const index = this._stepIndex(step);
        if (index === null) return false;
        const state = this._snap();
        if (index === state.step) return true;
        if (!this._canReach(index, state)) return false;
        const hadFocus = this.element.contains(document.activeElement);
        this._send('GO_TO', { step: index });
        this._render({ focusKey: hadFocus ? 'heading' : null });
        return true;
    }

    /** 清除來源資料、對應與結果，回到第一步；進行中的讀檔或匯入結果會被忽略。 */
    reset() {
        if (this._destroyed) return this;
        const hadFocus = this.element.contains(document.activeElement);
        this._readToken += 1;
        this._importToken += 1;
        this._data = null;
        this._validation = null;
        this._pasteDraft = '';
        this._send('RESET');
        this._render({ focusKey: hadFocus ? 'heading' : null });
        return this;
    }

    /** 目前的欄位對應：{ [fieldKey]: 來源欄位索引（0 起算）| null }。 */
    getMapping() {
        return { ...this._snap().mapping };
    }

    /**
     * 依目前對應驗證後的資料：
     * valid 為轉換後的列物件；invalid 為 { row（來源行號）, values（原始文字）, errors: [{ field, message }] }。
     */
    getRows() {
        const { valid, invalid } = this._validate();
        return {
            valid: valid.map((row) => ({ ...row })),
            invalid: invalid.map((entry) => ({ row: entry.row, values: { ...entry.values }, errors: entry.errors.map((error) => ({ ...error })) }))
        };
    }

    show() {
        if (this._destroyed) return this;
        this._send('SHOW');
        this.element.style.display = 'flex';
        return this;
    }

    hide() {
        if (this._destroyed) return this;
        this._send('HIDE');
        this.element.style.display = 'none';
        return this;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target || this._destroyed) return this;
        target.appendChild(this.element);
        this._send('MOUNT');
        if (!this._localeListening) {
            window.addEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = true;
        }
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        this._readToken += 1;
        this._importToken += 1;
        this._send('DESTROY');
        this._destroyChildren();
        this._stepper?.destroy();
        if (this._localeListening) {
            window.removeEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = false;
        }
        this._data = null;
        this._validation = null;
        this.element?.remove();
    }
}

export default ImportWizard;
