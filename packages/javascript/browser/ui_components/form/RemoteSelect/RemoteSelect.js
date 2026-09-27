/**
 * RemoteSelect — 選項來自非同步來源的可搜尋下拉選單（ARIA combobox）
 *
 * - 輸入後防抖查詢；以 AbortController + 請求序號丟棄過期回應
 * - 載入中（aria-busy + 可見指示）、查無資料、錯誤（可重試）三種狀態
 * - hasMore 時捲到清單底部，或以鍵盤選取「載入更多」項目載入下一頁
 * - 依查詢字串快取結果，存活到 destroy()
 * - 多選模式以標籤顯示已選項目，可用鍵盤移除
 * - 清單浮層沿用 Dropdown 的 fixed 定位；document/window 監聽只在展開期間掛載
 *
 * @example
 * const select = new RemoteSelect({
 *     fetchOptions: async (query, { page, pageSize, signal }) => {
 *         const response = await fetch(`/api/staff?q=${encodeURIComponent(query)}&page=${page}&size=${pageSize}`, { signal });
 *         const data = await response.json();
 *         return { items: data.rows.map((row) => ({ value: row.id, label: row.name })), hasMore: data.hasMore };
 *     },
 *     onChange: (value, item) => console.log(value, item)
 * }).mount('#host');
 */
import Locale from '../../i18n/index.js';
import './locale.js';
import { createComponentState } from '../../utils/component-state.js';
import { nextUid } from '../../utils/uid.js';
import { Icon } from '../../common/Icon/index.js';
import { setFieldError, clearFieldError, FIELD_ERROR_CONTRACT } from '../../utils/field-error.js';

/** 捲動到距清單底部多少 px 以內即載入下一頁 */
const SCROLL_LOAD_THRESHOLD = 24;

/** 視覺隱藏、但仍留給螢幕閱讀器朗讀的樣式 */
const SR_ONLY_CSS = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip-path:inset(50%);white-space:nowrap;';

const STATUS_ROW_CSS = 'display:flex;align-items:center;justify-content:space-between;gap:8px;padding:8px 12px;font-size:var(--cl-font-size-md);line-height:1.4;';

const ICON_BUTTON_CSS = 'display:inline-flex;align-items:center;justify-content:center;padding:4px;margin:0;border:none;background:transparent;border-radius:var(--cl-radius-sm);cursor:pointer;color:var(--cl-text-secondary);';

const isNil = (value) => value === null || value === undefined;
const keyOf = (value) => String(value);
const isItem = (item) => Boolean(item) && typeof item === 'object' && !isNil(item.value);
const labelOf = (item) => (isNil(item.label) ? String(item.value) : String(item.label));

function normalizeResponse(response) {
    if (Array.isArray(response)) return { items: response, hasMore: false };
    return {
        items: Array.isArray(response?.items) ? response.items : [],
        hasMore: Boolean(response?.hasMore)
    };
}

export class RemoteSelect {
    constructor(options = {}) {
        this.options = {
            fetchOptions: null,          // 必填：async (query, { page, pageSize, signal }) => ({ items, hasMore })
            debounce: 300,               // 停止輸入多少毫秒後才查詢
            minQueryLength: 1,           // 查詢字串（去除頭尾空白後）最少字元數；0 表示展開即以空字串查詢
            pageSize: 20,                // 每頁筆數，原樣傳給 fetchOptions
            multiple: false,             // 多選模式
            value: null,                 // 單選初始值
            values: [],                  // 多選初始值
            initialItems: [],            // 初始值的標籤來源 [{ value, label, description? }]
            resolveLabels: null,         // async (values) => items；已選值找不到標籤時呼叫
            placeholder: Locale.t('remoteSelect.placeholder'),
            clearable: true,             // 有選取值時顯示清除按鈕
            disabled: false,             // 停用
            width: '100%',               // 元件寬度（數字視為 px）
            maxSelected: null,           // 多選上限；null 表示不限
            cacheResults: true,          // 依查詢字串快取結果，存活到 destroy()
            ariaLabel: '',               // 沒有可見標籤時給輸入框與清單的無障礙名稱
            onChange: null,              // 單選 (value, item)；多選 (values, items)
            onError: null,               // (error) 查詢或標籤解析失敗
            ...options
        };

        if (typeof this.options.fetchOptions !== 'function') {
            console.warn('[RemoteSelect] options.fetchOptions 必須是函式：async (query, { page, pageSize, signal }) => ({ items, hasMore })');
        }

        this._id = nextUid('remote-select');
        this._itemsByKey = new Map();
        this._selected = [];
        this._selectedKeys = new Set();
        this._cache = new Map();
        this._results = null;
        this._optionEls = [];
        this._loadMoreEl = null;
        this._hoverIndex = -1;
        this._seq = 0;
        this._abort = null;
        this._inFlight = null;
        this._debounceTimer = null;
        this._failed = null;
        this._highlightAfterLoad = -1;
        this._pendingLabels = new Set();
        this._icons = [];
        this._spinning = false;
        this._focused = false;
        this._destroyed = false;
        this._globalListenersAttached = false;

        this._registerItems(this.options.initialItems);
        this._setSelection(this._toSelection(this._initialValues()));

        this.element = this._createElement();
        this._state = createComponentState({
            lifecycle: 'created',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            open: false,
            query: '',
            searching: false,
            status: 'idle',
            loadingMore: false,
            highlightIndex: -1
        }, {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            DESTROY: (s) => ({ ...s, lifecycle: 'destroyed', open: false }),
            SET_DISABLED: (s, p) => ({
                ...s,
                availability: p?.disabled ? 'disabled' : 'enabled',
                open: p?.disabled ? false : s.open,
                highlightIndex: p?.disabled ? -1 : s.highlightIndex
            }),
            OPEN: (s) => (s.availability === 'disabled' ? s : { ...s, open: true }),
            CLOSE: (s) => ({ ...s, open: false, highlightIndex: -1 }),
            INPUT: (s, p) => ({
                ...s,
                query: String(p?.query ?? ''),
                searching: true,
                open: s.availability !== 'disabled',
                highlightIndex: -1
            }),
            RESET_QUERY: (s) => ({ ...s, query: '', searching: false, status: 'idle', loadingMore: false, highlightIndex: -1 }),
            STATUS: (s, p) => ({ ...s, status: p?.status ?? s.status, loadingMore: Boolean(p?.loadingMore) }),
            HIGHLIGHT: (s, p) => ({ ...s, highlightIndex: Number.isInteger(p?.index) ? p.index : -1 })
        });

        this._bindEvents();
        this._renderTags();
        this._apply();
        this._resolveMissingLabels();
    }

    // ------------------------------------------------------------------
    // 公開 API
    // ------------------------------------------------------------------

    mount(container) {
        if (this._destroyed) return this;
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
            this._send('MOUNT');
        }
        return this;
    }

    /** 單選回傳值或 null；多選回傳值陣列（副本）。 */
    getValue() {
        if (this.options.multiple) return this._selected.map((entry) => entry.value);
        return this._selected.length ? this._selected[0].value : null;
    }

    /**
     * 設定選取值，不觸發 onChange。
     * 單選：setValue(value, item?)；多選：setValue(values, items?)。
     * item(s) 提供標籤；查不到標籤且有 resolveLabels 時會非同步解析。
     */
    setValue(value, item) {
        if (this._destroyed) return this;
        if (this.options.multiple) {
            const values = Array.isArray(value) ? value : (isNil(value) ? [] : [value]);
            this._registerItems(Array.isArray(item) ? item : (isItem(item) ? [item] : []));
            this._setSelection(this._toSelection(values));
        } else {
            const empty = isNil(value) || value === '';
            if (!empty && item && typeof item === 'object') {
                this._itemsByKey.set(keyOf(value), isNil(item.value) ? { ...item, value } : item);
            }
            this._setSelection(empty ? [] : this._toSelection([value]));
        }
        const s = this._state.snapshot();
        if (s.open || s.searching) this._closeList({ resetQuery: true });
        this._refreshSelectionUI();
        this._resolveMissingLabels();
        return this;
    }

    /** 已選項目陣列（單選也回傳陣列）；查不到的項目以 { value, label: String(value) } 代替。 */
    getSelectedItems() {
        return this._selected.map((entry) => this._itemsByKey.get(entry.key) || { value: entry.value, label: String(entry.value) });
    }

    setDisabled(disabled) {
        if (this._destroyed) return this;
        const flag = Boolean(disabled);
        if (flag) {
            const s = this._state.snapshot();
            if (s.open || s.searching || this._inFlight || this._debounceTimer) this._closeList({ resetQuery: true });
            // 停用的輸入框會失去焦點，但瀏覽器不一定送出 blur
            this._focused = false;
        }
        this.options.disabled = flag;
        this._send('SET_DISABLED', { disabled: flag });
        this._renderTags();
        return this;
    }

    /** 清除選取值與查詢字串，不觸發 onChange。 */
    clear() {
        if (this._destroyed) return this;
        this._setSelection([]);
        const s = this._state.snapshot();
        if (s.open || s.searching) this._closeList({ resetQuery: true });
        this._refreshSelectionUI();
        return this;
    }

    /**
     * 標示欄位錯誤；空訊息等同 clearError()。
     * display:false 只標示錯誤狀態、不顯示文字，給自行顯示錯誤文字的外層（FormField、SearchForm）使用。
     */
    setError(message, { display = true } = {}) {
        if (this._destroyed) return this;
        setFieldError(this, message, { target: this._input, visual: this._control, container: this.element, display });
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

    /** 清空快取並重新執行目前的查詢；回傳查詢完成的 Promise。 */
    refresh() {
        if (this._destroyed) return Promise.resolve();
        this._cache.clear();
        this._clearDebounce();
        const s = this._state.snapshot();
        const query = (s.searching ? s.query : '').trim();
        if (query.length < this._minQueryLength()) return Promise.resolve();
        return this._fetchPage(query, 1);
    }

    focus() {
        if (!this._destroyed) this._input.focus();
        return this;
    }

    /** 展開清單；需要時立即查詢目前的字串（不等防抖）。 */
    open() {
        if (this._destroyed || this._isDisabled()) return this;
        const s = this._state.snapshot();
        if (!s.open) {
            this._syncStaticLabels();
            this._send('OPEN');
        }
        const raw = s.searching ? s.query : '';
        const query = raw.trim();
        const hasCurrent = this._results && this._results.query === query && (s.status === 'ready' || s.status === 'empty');
        const inFlight = this._inFlight && this._inFlight.query === query;
        if (hasCurrent || inFlight || s.status === 'error') return this;
        this._requestQuery(raw, { immediate: true });
        return this;
    }

    /** 收合清單，保留查詢字串（同第一次 Escape）。 */
    close() {
        this._closeList({ resetQuery: false });
        return this;
    }

    snapshot() {
        return this._state.snapshot();
    }

    destroy() {
        if (this._destroyed) return;
        this._clearDebounce();
        this._cancelRequest();
        this._syncGlobalListeners(false);
        this._state.send('DESTROY');
        this._destroyed = true;
        clearFieldError(this);
        this._icons.forEach((icon) => icon.destroy());
        this._icons = [];
        this._cache.clear();
        this._pendingLabels.clear();
        this.element.remove();
    }

    // ------------------------------------------------------------------
    // DOM 建構
    // ------------------------------------------------------------------

    _createElement() {
        const { multiple, placeholder, width, ariaLabel } = this.options;
        const id = this._id;

        const root = document.createElement('div');
        root.className = `remote-select${multiple ? ' remote-select--multiple' : ''}`;
        root.style.cssText = 'position:relative;display:inline-block;max-width:100%;min-width:0;box-sizing:border-box;font-family:inherit;vertical-align:middle;';
        root.style.width = typeof width === 'number' ? `${width}px` : String(width || '100%');

        const control = document.createElement('div');
        control.className = 'remote-select__control';
        control.style.cssText = `display:flex;align-items:center;flex-wrap:${multiple ? 'wrap' : 'nowrap'};gap:4px;min-height:36px;box-sizing:border-box;padding:3px 6px 3px 10px;background:var(--cl-bg);border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);cursor:text;transition:border-color var(--cl-transition), box-shadow var(--cl-transition);`;

        let tags = null;
        if (multiple) {
            tags = document.createElement('div');
            tags.className = 'remote-select__tags';
            tags.setAttribute('role', 'list');
            tags.setAttribute('aria-label', Locale.t('remoteSelect.selectedItems'));
            tags.style.cssText = 'display:none;flex-wrap:wrap;gap:4px;min-width:0;max-width:100%;';
            control.appendChild(tags);
        }

        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'remote-select__input';
        input.id = `${id}-input`;
        input.autocomplete = 'off';
        input.spellcheck = false;
        input.placeholder = String(placeholder ?? '');
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-haspopup', 'listbox');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-controls', `${id}-listbox`);
        if (ariaLabel) input.setAttribute('aria-label', String(ariaLabel));
        input.style.cssText = `flex:${multiple ? '1 1 80px' : '1 1 auto'};width:0;min-width:${multiple ? '60px' : '0'};box-sizing:border-box;border:none;outline:none;background:transparent;padding:4px 0;margin:0;font-family:inherit;font-size:var(--cl-font-size-lg);color:var(--cl-text);`;
        control.appendChild(input);

        const icons = document.createElement('div');
        icons.className = 'remote-select__icons';
        icons.style.cssText = 'display:flex;align-items:center;gap:2px;flex:0 0 auto;margin-left:auto;';

        const spinner = document.createElement('span');
        spinner.className = 'remote-select__spinner';
        spinner.setAttribute('aria-hidden', 'true');
        spinner.style.cssText = 'display:none;align-items:center;padding:4px;';
        const spinnerIcon = new Icon({ name: 'refresh', size: 14, color: 'var(--cl-text-secondary)' }).mount(spinner);

        const clearButton = document.createElement('button');
        clearButton.type = 'button';
        clearButton.className = 'remote-select__clear';
        clearButton.setAttribute('aria-label', Locale.t('remoteSelect.clear'));
        clearButton.title = Locale.t('remoteSelect.clear');
        clearButton.style.cssText = `${ICON_BUTTON_CSS}display:none;`;
        const clearIcon = new Icon({ name: 'close', size: 14, color: 'var(--cl-text-secondary)' }).mount(clearButton);

        const toggleButton = document.createElement('button');
        toggleButton.type = 'button';
        toggleButton.className = 'remote-select__toggle';
        toggleButton.tabIndex = -1;
        toggleButton.setAttribute('aria-label', Locale.t('remoteSelect.toggle'));
        toggleButton.setAttribute('aria-expanded', 'false');
        toggleButton.setAttribute('aria-controls', `${id}-listbox`);
        toggleButton.style.cssText = ICON_BUTTON_CSS;
        const arrowIcon = new Icon({ name: 'chevron-down', size: 12, color: 'var(--cl-text-secondary)' }).mount(toggleButton);
        arrowIcon.element.style.transition = 'transform var(--cl-transition)';

        icons.append(spinner, clearButton, toggleButton);
        control.appendChild(icons);

        const panel = document.createElement('div');
        panel.className = 'remote-select__panel';
        panel.style.cssText = 'position:absolute;top:100%;left:0;right:0;margin-top:4px;z-index:1000;display:none;box-sizing:border-box;background:var(--cl-bg);border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);box-shadow:var(--cl-shadow-md);overflow:hidden;';

        const listbox = document.createElement('div');
        listbox.className = 'remote-select__listbox';
        listbox.id = `${id}-listbox`;
        listbox.setAttribute('role', 'listbox');
        listbox.setAttribute('aria-label', String(ariaLabel || placeholder || ''));
        listbox.setAttribute('aria-busy', 'false');
        if (multiple) listbox.setAttribute('aria-multiselectable', 'true');
        listbox.style.cssText = 'max-height:240px;overflow-y:auto;overscroll-behavior:contain;';

        const statusRow = document.createElement('div');
        statusRow.className = 'remote-select__status-row';
        statusRow.dataset.mode = 'hidden';
        statusRow.style.cssText = SR_ONLY_CSS;

        const status = document.createElement('div');
        status.className = 'remote-select__status';
        status.id = `${id}-status`;
        status.setAttribute('role', 'status');
        status.style.cssText = 'min-width:0;';

        const retryButton = document.createElement('button');
        retryButton.type = 'button';
        retryButton.className = 'remote-select__retry';
        retryButton.textContent = Locale.t('remoteSelect.retry');
        retryButton.style.cssText = 'display:none;flex:0 0 auto;padding:2px 10px;margin:0;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);background:var(--cl-bg);color:var(--cl-primary);font-family:inherit;font-size:var(--cl-font-size-sm);cursor:pointer;';

        statusRow.append(status, retryButton);
        panel.append(listbox, statusRow);
        root.append(control, panel);

        this._control = control;
        this._tagsWrap = tags;
        this._input = input;
        this._spinner = spinner;
        this._spinnerIcon = spinnerIcon;
        this._clearButton = clearButton;
        this._toggleButton = toggleButton;
        this._arrowIcon = arrowIcon;
        this._panel = panel;
        this._listbox = listbox;
        this._statusRow = statusRow;
        this._status = status;
        this._retryButton = retryButton;
        this._icons.push(spinnerIcon, clearIcon, arrowIcon);
        return root;
    }

    _bindEvents() {
        const input = this._input;
        input.addEventListener('input', () => this._handleInput());
        input.addEventListener('keydown', (event) => this._handleKeydown(event));
        input.addEventListener('focus', () => {
            this._focused = true;
            this._applyControlStyle();
            // 單選時輸入框顯示已選標籤；聚焦即全選，直接輸入就能取代成新的查詢字串
            if (!this.options.multiple && !this._state.snapshot().searching && input.value) input.select?.();
        });
        input.addEventListener('blur', () => {
            this._focused = false;
            this._applyControlStyle();
        });

        // 焦點離開整個元件：收合並放棄未確認的查詢字串，輸入框回到已選值
        this.element.addEventListener('focusout', (event) => {
            if (this._destroyed) return;
            const next = event.relatedTarget;
            if (next && this.element.contains(next)) return;
            const s = this._state.snapshot();
            if (s.open || s.searching) this._closeList({ resetQuery: true });
        });

        // 點在控制框的非輸入區（標籤、按鈕、留白）時保持焦點在輸入框
        this._control.addEventListener('mousedown', (event) => {
            if (event.target !== input) event.preventDefault();
        });
        this._control.addEventListener('click', (event) => {
            if (this._destroyed || this._isDisabled()) return;
            if (event.target?.closest?.('button')) return;
            input.focus();
            if (!this._state.snapshot().open) this.open();
        });
        this._toggleButton.addEventListener('click', () => {
            if (this._destroyed || this._isDisabled()) return;
            input.focus();
            if (this._state.snapshot().open) this._closeList({ resetQuery: false });
            else this.open();
        });
        this._clearButton.addEventListener('click', () => this._clearFromUI());
        this._retryButton.addEventListener('click', () => {
            this._retry();
            this.focus();
        });

        // 清單內按下滑鼠不搶走輸入框焦點
        this._panel.addEventListener('mousedown', (event) => event.preventDefault());
        this._listbox.addEventListener('click', (event) => {
            const index = this._indexFromEvent(event);
            if (index >= 0) this._activate(index, { keyboard: false });
        });
        this._listbox.addEventListener('mousemove', (event) => {
            const index = this._indexFromEvent(event);
            if (index === this._hoverIndex) return;
            this._hoverIndex = index;
            if (index >= 0 && this._isNavigable(index)) this._setHighlight(index, { scroll: false });
        });
        this._listbox.addEventListener('scroll', () => this._handleListScroll());

        this._handleDocumentMousedown = (event) => {
            if (!this.element.contains(event.target)) this._closeList({ resetQuery: true });
        };
        this._handleViewportChange = (event) => {
            // 清單本身捲動不必重新定位
            if (event?.type === 'scroll' && event.target instanceof Node && this._panel.contains(event.target)) return;
            this._positionPanel();
        };
    }

    // 全域監聽只在清單展開期間掛載；外部點擊收合，視窗縮放與捲動時重新定位
    _syncGlobalListeners(open) {
        const attach = Boolean(open) && !this._destroyed;
        if (attach === this._globalListenersAttached) return;
        this._globalListenersAttached = attach;
        if (attach) {
            document.addEventListener('mousedown', this._handleDocumentMousedown, true);
            window.addEventListener('resize', this._handleViewportChange);
            window.addEventListener('scroll', this._handleViewportChange, true);
        } else {
            document.removeEventListener('mousedown', this._handleDocumentMousedown, true);
            window.removeEventListener('resize', this._handleViewportChange);
            window.removeEventListener('scroll', this._handleViewportChange, true);
        }
    }

    // ------------------------------------------------------------------
    // 狀態與呈現
    // ------------------------------------------------------------------

    _send(event, payload = null) {
        const next = this._state.send(event, payload);
        this._apply(next);
        return next;
    }

    _setStatus(status, { loadingMore = false } = {}) {
        this._send('STATUS', { status, loadingMore });
    }

    _isDisabled() {
        return this._state ? this._state.snapshot().availability === 'disabled' : Boolean(this.options.disabled);
    }

    _apply(s = this._state.snapshot()) {
        if (this._destroyed) return;
        const disabled = s.availability === 'disabled';
        const open = s.open;
        const input = this._input;

        input.disabled = disabled;
        input.setAttribute('aria-expanded', open ? 'true' : 'false');
        this._toggleButton.setAttribute('aria-expanded', open ? 'true' : 'false');
        this._toggleButton.disabled = disabled;
        const activeId = open ? this._optionIdAt(s.highlightIndex) : null;
        if (activeId) input.setAttribute('aria-activedescendant', activeId);
        else input.removeAttribute('aria-activedescendant');

        if (!s.searching) {
            const text = this.options.multiple ? '' : this._selectedLabel();
            if (input.value !== text) input.value = text;
        }
        input.placeholder = this.options.multiple && this._selected.length ? '' : String(this.options.placeholder ?? '');

        this._applyControlStyle(s);
        const busy = s.status === 'loading' || s.loadingMore;
        this._listbox.setAttribute('aria-busy', busy ? 'true' : 'false');
        this._spinner.style.display = busy ? 'inline-flex' : 'none';
        if (busy !== this._spinning) {
            this._spinning = busy;
            this._spinnerIcon.setSpin(busy);
        }
        this._clearButton.style.display = this.options.clearable && !disabled && this._selected.length ? 'inline-flex' : 'none';
        this._arrowIcon.element.style.transform = open ? 'rotate(180deg)' : '';

        this._portalPanel(open);
        this._panel.style.display = open ? 'block' : 'none';
        this._syncLoadMore(s);
        this._renderStatus(s);
        this._syncGlobalListeners(open);
        if (open) this._positionPanel();
    }

    _applyControlStyle(s = this._state.snapshot()) {
        const disabled = s.availability === 'disabled';
        const style = this._control.style;
        style.background = disabled ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)';
        style.opacity = disabled ? '0.6' : '1';
        style.cursor = disabled ? 'not-allowed' : 'text';
        style.borderColor = !disabled && (s.open || this._focused) ? 'var(--cl-primary)' : 'var(--cl-border)';
        style.boxShadow = !disabled && this._focused ? '0 0 0 3px rgba(var(--cl-primary-rgb), 0.12)' : 'none';
    }

    _syncStaticLabels() {
        const clearLabel = Locale.t('remoteSelect.clear');
        this._clearButton.setAttribute('aria-label', clearLabel);
        this._clearButton.title = clearLabel;
        this._toggleButton.setAttribute('aria-label', Locale.t('remoteSelect.toggle'));
        this._tagsWrap?.setAttribute('aria-label', Locale.t('remoteSelect.selectedItems'));
    }

    _renderStatus(s) {
        const t = (key, params) => Locale.t(`remoteSelect.${key}`, params);
        const count = this._results ? this._results.items.length : 0;
        let message = '';
        let visible = true;
        let color = 'var(--cl-text-secondary)';
        switch (s.status) {
            case 'hint': {
                const min = this._minQueryLength();
                message = min > 1 ? t('minQuery', { count: min }) : t('typeToSearch');
                break;
            }
            case 'loading':
                message = t('loading');
                break;
            case 'empty':
                message = t('empty');
                break;
            case 'error':
                message = t('error');
                color = 'var(--cl-danger)';
                break;
            case 'ready':
                if (this._atMax()) {
                    message = t('maxReached', { max: this._maxSelected() });
                } else {
                    // 有結果時只給螢幕閱讀器筆數，不佔畫面
                    message = this._results?.hasMore ? t('resultCountMore', { count }) : t('resultCount', { count });
                    visible = false;
                }
                break;
            default:
                visible = false;
        }
        if (this._status.textContent !== message) this._status.textContent = message;
        this._status.style.color = color;
        this._statusRow.dataset.status = s.status;
        const mode = visible && message ? 'visible' : 'hidden';
        if (this._statusRow.dataset.mode !== mode) {
            this._statusRow.dataset.mode = mode;
            this._statusRow.style.cssText = mode === 'visible' ? STATUS_ROW_CSS : SR_ONLY_CSS;
        }
        const showRetry = s.status === 'error';
        this._retryButton.style.display = showRetry ? 'inline-flex' : 'none';
        if (showRetry) this._retryButton.textContent = t('retry');
    }

    _renderTags() {
        const wrap = this._tagsWrap;
        if (!wrap) return;
        const disabled = this._isDisabled();
        // 重繪前記下鍵盤焦點所在的標籤，重繪後還給同一個標籤的移除按鈕
        const active = document.activeElement;
        const focusedKey = active && wrap.contains(active) ? active.closest('.remote-select__tag')?.dataset.value : null;
        let refocus = null;
        const fragment = document.createDocumentFragment();
        for (const entry of this._selected) {
            const label = this._labelFor(entry);
            const tag = document.createElement('span');
            tag.className = 'remote-select__tag';
            tag.setAttribute('role', 'listitem');
            tag.dataset.value = entry.key;
            if (this._pendingLabels.has(entry.key)) tag.dataset.labelPending = 'true';
            tag.style.cssText = `display:inline-flex;align-items:center;gap:2px;max-width:100%;min-width:0;padding:1px ${disabled ? '8px' : '2px'} 1px 8px;background:var(--cl-primary-light);color:var(--cl-primary-dark);border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-sm);line-height:1.6;`;

            const text = document.createElement('span');
            text.className = 'remote-select__tag-label';
            text.textContent = label;
            text.style.cssText = 'overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:180px;';
            tag.appendChild(text);

            if (!disabled) {
                const remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'remote-select__tag-remove';
                remove.textContent = '×';
                remove.setAttribute('aria-label', Locale.t('remoteSelect.removeTag', { label }));
                remove.style.cssText = 'display:inline-flex;align-items:center;justify-content:center;padding:0 4px;margin:0;border:none;background:transparent;color:inherit;border-radius:var(--cl-radius-sm);font-family:inherit;font-size:var(--cl-font-size-md);line-height:1;cursor:pointer;';
                remove.addEventListener('click', () => {
                    this._removeKey(entry.key);
                    this.focus();
                });
                tag.appendChild(remove);
                if (entry.key === focusedKey) refocus = remove;
            }
            fragment.appendChild(tag);
        }
        wrap.replaceChildren(fragment);
        wrap.style.display = this._selected.length ? 'flex' : 'none';
        refocus?.focus();
    }

    _replaceResults(entry) {
        this._results = entry;
        this._hoverIndex = -1;
        // 換了一份清單，舊的高亮索引已無意義；呼叫端之後會 _apply
        if (this._state.snapshot().highlightIndex !== -1) this._state.send('HIGHLIGHT', { index: -1 });
        this._renderList();
    }

    _renderList() {
        const s = this._state.snapshot();
        const items = this._results ? this._results.items : [];
        const fragment = document.createDocumentFragment();
        this._optionEls = items.map((item, index) => {
            const el = this._createOption(item, index, s);
            fragment.appendChild(el);
            return el;
        });
        this._listbox.replaceChildren(fragment);
        this._syncLoadMore(s);
    }

    _appendOptions(start) {
        const s = this._state.snapshot();
        const items = this._results.items;
        const fragment = document.createDocumentFragment();
        for (let index = start; index < items.length; index += 1) {
            const el = this._createOption(items[index], index, s);
            this._optionEls.push(el);
            fragment.appendChild(el);
        }
        if (this._loadMoreEl && this._loadMoreEl.parentNode === this._listbox) {
            this._listbox.insertBefore(fragment, this._loadMoreEl);
        } else {
            this._listbox.appendChild(fragment);
        }
    }

    _createOption(item, index, s) {
        const el = document.createElement('div');
        el.className = 'remote-select__option';
        el.id = `${this._id}-option-${index}`;
        el.setAttribute('role', 'option');
        el.dataset.index = String(index);
        el.dataset.value = keyOf(item.value);
        el.style.cssText = 'display:flex;align-items:flex-start;gap:6px;padding:8px 12px;font-size:var(--cl-font-size-md);line-height:1.4;';

        const check = document.createElement('span');
        check.className = 'remote-select__check';
        check.setAttribute('aria-hidden', 'true');
        check.style.cssText = 'flex:0 0 14px;color:var(--cl-primary);';

        const body = document.createElement('span');
        body.className = 'remote-select__option-body';
        body.style.cssText = 'display:flex;flex-direction:column;gap:2px;min-width:0;';

        const label = document.createElement('span');
        label.className = 'remote-select__option-label';
        label.textContent = labelOf(item);
        label.style.cssText = 'overflow-wrap:anywhere;';
        body.appendChild(label);

        if (!isNil(item.description) && item.description !== '') {
            const description = document.createElement('span');
            description.className = 'remote-select__option-description';
            description.textContent = String(item.description);
            description.style.cssText = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);font-weight:400;overflow-wrap:anywhere;';
            body.appendChild(description);
        }

        el.append(check, body);
        this._paintOption(el, index, s);
        return el;
    }

    _paintOption(el, index, s) {
        const item = this._results?.items[index];
        if (!el || !item) return;
        const selected = this._selectedKeys.has(keyOf(item.value));
        const disabled = this._isOptionDisabled(item, selected);
        const highlighted = s.highlightIndex === index;
        el.setAttribute('aria-selected', selected ? 'true' : 'false');
        if (disabled) el.setAttribute('aria-disabled', 'true');
        else el.removeAttribute('aria-disabled');
        el.style.background = highlighted ? 'var(--cl-bg-hover)' : (selected ? 'var(--cl-primary-light)' : 'transparent');
        el.style.boxShadow = highlighted ? 'inset 3px 0 0 var(--cl-primary)' : 'none';
        el.style.color = disabled ? 'var(--cl-text-light)' : 'var(--cl-text)';
        el.style.cursor = disabled ? 'not-allowed' : 'pointer';
        el.style.fontWeight = selected ? '600' : '400';
        el.firstChild.textContent = selected ? '✓' : '';
    }

    _syncLoadMore(s) {
        const entry = this._results;
        if (!entry || !entry.items.length || !entry.hasMore) {
            this._loadMoreEl?.remove();
            return;
        }
        let el = this._loadMoreEl;
        if (!el) {
            el = document.createElement('div');
            el.className = 'remote-select__option remote-select__load-more';
            el.id = `${this._id}-load-more`;
            el.setAttribute('role', 'option');
            el.setAttribute('aria-selected', 'false');
            el.dataset.action = 'load-more';
            el.style.cssText = 'padding:8px 12px;text-align:center;font-size:var(--cl-font-size-md);line-height:1.4;color:var(--cl-primary);border-top:1px solid var(--cl-border-light);';
            this._loadMoreEl = el;
        }
        if (this._listbox.lastChild !== el) this._listbox.appendChild(el);
        const loading = s.loadingMore;
        const text = Locale.t(loading ? 'remoteSelect.loadingMore' : 'remoteSelect.loadMore');
        if (el.textContent !== text) el.textContent = text;
        if (loading) el.setAttribute('aria-disabled', 'true');
        else el.removeAttribute('aria-disabled');
        const highlighted = s.highlightIndex === entry.items.length;
        el.style.background = highlighted ? 'var(--cl-bg-hover)' : 'transparent';
        el.style.boxShadow = highlighted ? 'inset 3px 0 0 var(--cl-primary)' : 'none';
        el.style.cursor = loading ? 'progress' : 'pointer';
    }

    _loadMoreVisible() {
        return Boolean(this._loadMoreEl) && this._loadMoreEl.parentNode === this._listbox;
    }

    _optionIdAt(index) {
        return this._elementAt(index)?.id || null;
    }

    _elementAt(index) {
        if (index < 0) return null;
        if (index < this._optionEls.length) return this._optionEls[index];
        if (index === this._optionEls.length && this._loadMoreVisible()) return this._loadMoreEl;
        return null;
    }

    _indexFromEvent(event) {
        const option = event.target?.closest?.('[role="option"]');
        if (!option || !this._listbox.contains(option)) return -1;
        if (option === this._loadMoreEl) return this._results ? this._results.items.length : -1;
        const index = Number(option.dataset.index);
        return Number.isInteger(index) ? index : -1;
    }

    // ------------------------------------------------------------------
    // 鍵盤與高亮
    // ------------------------------------------------------------------

    _handleKeydown(event) {
        if (this._destroyed || this._isDisabled()) return;
        const s = this._state.snapshot();
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                if (!s.open) {
                    this.open();
                    if (!event.altKey) this._highlightEdge('first');
                } else {
                    this._moveHighlight(1);
                }
                break;
            case 'ArrowUp':
                event.preventDefault();
                if (!s.open) {
                    this.open();
                    this._highlightEdge('last');
                } else {
                    this._moveHighlight(-1);
                }
                break;
            case 'Home':
            case 'End':
                if (s.open && this._navCount()) {
                    event.preventDefault();
                    this._highlightEdge(event.key === 'Home' ? 'first' : 'last');
                }
                break;
            case 'Enter':
                if (!s.open) break;
                event.preventDefault();
                if (s.highlightIndex >= 0) this._activate(s.highlightIndex, { keyboard: true });
                else if (s.status === 'error') this._retry();
                break;
            case 'Escape':
                // 第一次收合清單、保留字串；第二次清除查詢字串。處理了就不再往外傳（避免同時關掉外層對話框）
                if (s.open) {
                    event.preventDefault();
                    event.stopPropagation();
                    this._closeList({ resetQuery: false });
                } else if (s.searching) {
                    event.preventDefault();
                    event.stopPropagation();
                    this._resetQuery();
                }
                break;
            case 'Backspace':
                if (this.options.multiple && this._input.value === '' && this._selected.length) {
                    event.preventDefault();
                    this._removeKey(this._selected[this._selected.length - 1].key);
                }
                break;
            default:
                break;
        }
    }

    _navCount() {
        if (!this._results) return 0;
        return this._results.items.length + (this._loadMoreVisible() ? 1 : 0);
    }

    _isNavigable(index) {
        const entry = this._results;
        if (!entry || index < 0) return false;
        if (index < entry.items.length) {
            const item = entry.items[index];
            return !this._isOptionDisabled(item, this._selectedKeys.has(keyOf(item.value)));
        }
        return index === entry.items.length && this._loadMoreVisible() && !this._state.snapshot().loadingMore;
    }

    _moveHighlight(delta) {
        const count = this._navCount();
        if (!count) return;
        let index = this._state.snapshot().highlightIndex;
        if (index < 0) index = delta > 0 ? -1 : count;
        for (let step = 0; step < count; step += 1) {
            index += delta;
            if (index < 0 || index >= count) return;
            if (this._isNavigable(index)) {
                this._setHighlight(index);
                return;
            }
        }
    }

    _highlightEdge(edge) {
        const count = this._navCount();
        if (edge === 'first') {
            for (let index = 0; index < count; index += 1) {
                if (this._isNavigable(index)) return this._setHighlight(index);
            }
        } else {
            for (let index = count - 1; index >= 0; index -= 1) {
                if (this._isNavigable(index)) return this._setHighlight(index);
            }
        }
        return undefined;
    }

    _setHighlight(index, { scroll = true } = {}) {
        const previous = this._state.snapshot().highlightIndex;
        if (previous === index) return;
        const next = this._send('HIGHLIGHT', { index });
        this._paintOption(this._optionEls[previous], previous, next);
        this._paintOption(this._optionEls[index], index, next);
        if (scroll) this._elementAt(index)?.scrollIntoView?.({ block: 'nearest' });
    }

    // ------------------------------------------------------------------
    // 查詢、分頁、快取
    // ------------------------------------------------------------------

    _minQueryLength() {
        return Math.max(0, Number(this.options.minQueryLength) || 0);
    }

    _clearDebounce() {
        if (this._debounceTimer) {
            clearTimeout(this._debounceTimer);
            this._debounceTimer = null;
        }
    }

    _handleInput() {
        if (this._destroyed || this._isDisabled()) return;
        const raw = this._input.value;
        this._send('INPUT', { query: raw });
        this._requestQuery(raw);
    }

    _requestQuery(raw, { immediate = false } = {}) {
        this._clearDebounce();
        const query = String(raw ?? '').trim();
        if (query.length < this._minQueryLength()) {
            this._cancelRequest();
            this._failed = null;
            this._replaceResults(null);
            this._setStatus('hint');
            return;
        }
        const cached = this.options.cacheResults ? this._cache.get(query) : null;
        if (cached) {
            this._cancelRequest();
            this._failed = null;
            this._replaceResults(cached);
            this._setStatus(cached.items.length ? 'ready' : 'empty');
            return;
        }
        const delay = Math.max(0, Number(this.options.debounce) || 0);
        if (immediate || delay === 0) {
            this._fetchPage(query, 1);
            return;
        }
        // 防抖期間：舊清單屬於舊字串，先清掉並顯示載入中；進行中的舊請求立即中止
        this._cancelRequest();
        this._failed = null;
        this._replaceResults(null);
        this._setStatus('loading');
        this._debounceTimer = setTimeout(() => {
            this._debounceTimer = null;
            this._fetchPage(query, 1);
        }, delay);
    }

    // 取消進行中的請求：序號加一讓晚到的回應被丟棄，並中止底層請求
    _cancelRequest() {
        this._seq += 1;
        this._inFlight = null;
        const controller = this._abort;
        this._abort = null;
        if (controller) {
            try {
                controller.abort();
            } catch {
                // 呼叫端的中止處理拋錯不影響元件
            }
        }
    }

    _fetchPage(query, page) {
        if (this._destroyed) return Promise.resolve();
        this._cancelRequest();
        const seq = this._seq;
        const controller = typeof AbortController === 'function' ? new AbortController() : null;
        this._abort = controller;
        this._inFlight = { query, page };
        this._failed = null;
        const append = page > 1 && this._results?.query === query;
        if (!append) this._replaceResults(null);
        this._setStatus(append ? 'ready' : 'loading', { loadingMore: append });

        const { fetchOptions, pageSize } = this.options;
        let request;
        try {
            request = typeof fetchOptions === 'function'
                ? Promise.resolve(fetchOptions(query, { page, pageSize, signal: controller ? controller.signal : undefined }))
                : Promise.resolve({ items: [], hasMore: false });
        } catch (error) {
            request = Promise.reject(error);
        }
        return request.then(
            (response) => {
                if (seq !== this._seq || this._destroyed) return;
                this._abort = null;
                this._inFlight = null;
                this._receive(query, page, append, response);
            },
            (error) => {
                if (seq !== this._seq || this._destroyed) return;
                this._abort = null;
                this._inFlight = null;
                this._fail(query, page, error);
            }
        );
    }

    _receive(query, page, append, response) {
        const { items, hasMore } = normalizeResponse(response);
        const entry = append ? this._results : { query, items: [], keys: new Set(), page: 0, hasMore: false };
        const start = entry.items.length;
        let touchesSelection = false;
        for (const item of items) {
            if (!isItem(item)) continue;
            const key = keyOf(item.value);
            this._itemsByKey.set(key, item);
            if (this._selectedKeys.has(key)) touchesSelection = true;
            if (entry.keys.has(key)) continue;
            entry.keys.add(key);
            entry.items.push(item);
        }
        entry.page = page;
        entry.hasMore = hasMore;
        if (this.options.cacheResults) this._cache.set(query, entry);

        if (append) this._appendOptions(start);
        else this._replaceResults(entry);
        if (touchesSelection) this._renderTags();
        this._setStatus(entry.items.length ? 'ready' : 'empty');

        // 以鍵盤選「載入更多」時，高亮移到第一筆新項目
        if (append && this._highlightAfterLoad >= 0) {
            const target = this._highlightAfterLoad;
            this._highlightAfterLoad = -1;
            if (target < entry.items.length && this._isNavigable(target)) this._setHighlight(target);
            else this._highlightEdge('last');
        }
    }

    _fail(query, page, error) {
        this._failed = { query, page };
        this._highlightAfterLoad = -1;
        this._setStatus('error');
        this._reportError(error);
    }

    _retry() {
        const failed = this._failed;
        if (!failed || this._destroyed || this._isDisabled()) return;
        this._fetchPage(failed.query, failed.page);
    }

    _loadMore({ keyboard = false } = {}) {
        const entry = this._results;
        if (!entry || !entry.hasMore || this._destroyed || this._isDisabled()) return;
        const s = this._state.snapshot();
        if (s.loadingMore || s.status === 'loading') return;
        this._highlightAfterLoad = keyboard ? entry.items.length : -1;
        this._fetchPage(entry.query, entry.page + 1);
    }

    _handleListScroll() {
        if (!this._results?.hasMore) return;
        const s = this._state.snapshot();
        // 載入失敗後不因捲動自動重試，改由「重試」或「載入更多」明確觸發
        if (!s.open || s.status === 'error') return;
        const list = this._listbox;
        if (list.scrollTop + list.clientHeight >= list.scrollHeight - SCROLL_LOAD_THRESHOLD) this._loadMore();
    }

    _reportError(error) {
        const handler = this.options.onError;
        if (typeof handler !== 'function') return;
        try {
            handler(error);
        } catch (callbackError) {
            console.error('[RemoteSelect] onError 執行失敗：', callbackError);
        }
    }

    // ------------------------------------------------------------------
    // 選取
    // ------------------------------------------------------------------

    _initialValues() {
        const { multiple, value, values } = this.options;
        if (!multiple) return isNil(value) || value === '' ? [] : [value];
        if (Array.isArray(values) && values.length) return values;
        if (Array.isArray(value)) return value;
        return isNil(value) ? [] : [value];
    }

    _toSelection(values) {
        const seen = new Set();
        const selection = [];
        for (const value of values) {
            if (isNil(value) || value === '') continue;
            const key = keyOf(value);
            if (seen.has(key)) continue;
            seen.add(key);
            selection.push({ key, value });
        }
        if (!this.options.multiple) return selection.slice(0, 1);
        const max = this._maxSelected();
        return max ? selection.slice(0, max) : selection;
    }

    _setSelection(selection) {
        this._selected = selection;
        this._selectedKeys = new Set(selection.map((entry) => entry.key));
    }

    _registerItems(items) {
        if (!Array.isArray(items)) return;
        for (const item of items) {
            if (isItem(item)) this._itemsByKey.set(keyOf(item.value), item);
        }
    }

    _maxSelected() {
        const max = Number(this.options.maxSelected);
        return this.options.multiple && Number.isFinite(max) && max > 0 ? Math.floor(max) : 0;
    }

    _atMax() {
        const max = this._maxSelected();
        return max > 0 && this._selected.length >= max;
    }

    _isOptionDisabled(item, selected) {
        return Boolean(item.disabled) || (!selected && this._atMax());
    }

    _labelFor(entry) {
        const item = this._itemsByKey.get(entry.key);
        if (item) return labelOf(item);
        if (this._pendingLabels.has(entry.key)) return Locale.t('remoteSelect.resolving');
        return String(entry.value);
    }

    _selectedLabel() {
        return this._selected.length ? this._labelFor(this._selected[0]) : '';
    }

    _activate(index, { keyboard = false } = {}) {
        const entry = this._results;
        if (!entry || this._isDisabled()) return;
        if (index === entry.items.length) {
            this._loadMore({ keyboard });
            return;
        }
        const item = entry.items[index];
        if (!item) return;
        const selected = this._selectedKeys.has(keyOf(item.value));
        if (this._isOptionDisabled(item, selected)) return;
        if (this.options.multiple) this._toggleItem(item, selected);
        else this._selectSingle(item);
    }

    _selectSingle(item) {
        const key = keyOf(item.value);
        const changed = !(this._selected.length === 1 && this._selected[0].key === key);
        this._itemsByKey.set(key, item);
        this._setSelection([{ key, value: item.value }]);
        this._closeList({ resetQuery: true });
        if (changed) this._emitChange();
    }

    _toggleItem(item, selected) {
        const key = keyOf(item.value);
        if (selected) {
            this._setSelection(this._selected.filter((entry) => entry.key !== key));
        } else {
            if (this._atMax()) return;
            this._itemsByKey.set(key, item);
            this._setSelection([...this._selected, { key, value: item.value }]);
        }
        this._refreshSelectionUI();
        this._emitChange();
    }

    _removeKey(key) {
        if (this._destroyed || this._isDisabled() || !this._selectedKeys.has(key)) return;
        this._setSelection(this._selected.filter((entry) => entry.key !== key));
        this._refreshSelectionUI();
        this._emitChange();
    }

    _clearFromUI() {
        if (this._destroyed || this._isDisabled() || !this._selected.length) return;
        this._setSelection([]);
        this._refreshSelectionUI();
        this._emitChange();
        this.focus();
    }

    _refreshSelectionUI() {
        if (this._destroyed) return;
        this._renderTags();
        const s = this._state.snapshot();
        this._optionEls.forEach((el, index) => this._paintOption(el, index, s));
        this._apply(s);
    }

    _emitChange() {
        const handler = this.options.onChange;
        if (typeof handler !== 'function') return;
        const items = this.getSelectedItems();
        if (this.options.multiple) handler(this.getValue(), items);
        else handler(this.getValue(), items[0] || null);
    }

    _closeList({ resetQuery = false } = {}) {
        if (this._destroyed) return;
        const hadPendingQuery = Boolean(this._debounceTimer);
        this._clearDebounce();
        const s = this._state.snapshot();
        if (s.open) this._send('CLOSE');
        if (resetQuery) {
            this._resetQuery();
        } else if (hadPendingQuery && s.status === 'loading' && !this._inFlight) {
            // 防抖中的查詢被取消，沒有請求在跑，不能停在載入中
            this._setStatus('idle');
        }
    }

    _resetQuery() {
        this._clearDebounce();
        this._cancelRequest();
        this._failed = null;
        this._highlightAfterLoad = -1;
        this._replaceResults(null);
        this._send('RESET_QUERY');
    }

    _resolveMissingLabels() {
        const resolver = this.options.resolveLabels;
        if (typeof resolver !== 'function' || this._destroyed) return;
        const missing = this._selected.filter((entry) => !this._itemsByKey.has(entry.key) && !this._pendingLabels.has(entry.key));
        if (!missing.length) return;
        missing.forEach((entry) => this._pendingLabels.add(entry.key));
        this._refreshSelectionUI();

        let request;
        try {
            request = Promise.resolve(resolver(missing.map((entry) => entry.value)));
        } catch (error) {
            request = Promise.reject(error);
        }
        const settle = () => {
            missing.forEach((entry) => this._pendingLabels.delete(entry.key));
            this._refreshSelectionUI();
        };
        request.then(
            (items) => {
                if (this._destroyed) return;
                this._registerItems(Array.isArray(items) ? items : []);
                settle();
            },
            (error) => {
                if (this._destroyed) return;
                if (typeof this.options.onError === 'function') this._reportError(error);
                else console.warn('[RemoteSelect] resolveLabels 失敗：', error);
                settle();
            }
        );
    }

    // ------------------------------------------------------------------
    // 浮層定位（沿用 Dropdown._portalMenu 的做法）
    // ------------------------------------------------------------------

    /**
     * 清單展開時留在元件內（維持 DOM 契約），改用 position:fixed 依控制框座標定位，
     * 不受上層容器 overflow/高度裁切；收合時還原成 absolute。
     */
    _portalPanel(open) {
        const panel = this._panel;
        if (open) {
            panel.dataset.floating = 'fixed';
            return;
        }
        if (!panel.dataset.floating) return;
        delete panel.dataset.floating;
        delete panel.dataset.placement;
        panel.style.position = 'absolute';
        panel.style.top = '100%';
        panel.style.left = '0';
        panel.style.right = '0';
        panel.style.bottom = 'auto';
        panel.style.width = '';
        panel.style.maxWidth = '';
        panel.style.marginTop = '4px';
        panel.style.zIndex = '1000';
    }

    _fixedContainingBlockOffset() {
        let ancestor = this.element.parentElement;
        while (ancestor && ancestor !== document.body && ancestor !== document.documentElement) {
            const style = window.getComputedStyle(ancestor);
            const createsBlock = (style.transform && style.transform !== 'none')
                || (style.perspective && style.perspective !== 'none')
                || (style.filter && style.filter !== 'none')
                || /transform|perspective|filter/.test(style.willChange || '')
                || /paint|layout|strict|content/.test(style.contain || '');
            if (createsBlock) {
                const rect = ancestor.getBoundingClientRect();
                return {
                    top: rect.top + (parseFloat(style.borderTopWidth) || 0),
                    left: rect.left + (parseFloat(style.borderLeftWidth) || 0)
                };
            }
            ancestor = ancestor.parentElement;
        }
        return { top: 0, left: 0 };
    }

    // 下方空間不足且上方較寬時翻到控制框上方；寬度至少 200px、不超出視窗
    _positionPanel() {
        if (this._destroyed) return;
        const panel = this._panel;
        const margin = 4;
        const viewportTop = 8;
        const viewportBottom = Math.max(viewportTop, window.innerHeight - 8);
        const viewportRight = Math.max(0, window.innerWidth - 8);
        const anchorRect = this._control.getBoundingClientRect();
        const width = Math.max(0, Math.min(Math.max(anchorRect.width, 200), viewportRight - 8));

        panel.style.position = 'fixed';
        panel.style.right = 'auto';
        panel.style.bottom = 'auto';
        panel.style.marginTop = '0';
        panel.style.zIndex = '10050';
        panel.style.width = `${Math.round(width)}px`;
        panel.style.maxWidth = `${Math.max(120, viewportRight - 8)}px`;

        const rect = panel.getBoundingClientRect();
        const height = rect.height || panel.scrollHeight || 0;
        const spaceBelow = viewportBottom - anchorRect.bottom - margin;
        const spaceAbove = anchorRect.top - viewportTop - margin;
        const placeAbove = height > spaceBelow && spaceAbove > spaceBelow;
        const top = placeAbove ? Math.max(viewportTop, anchorRect.top - margin - height) : anchorRect.bottom + margin;
        const left = Math.max(8, Math.min(anchorRect.left, viewportRight - width));
        const offset = this._fixedContainingBlockOffset();
        panel.style.top = `${Math.round(top - offset.top)}px`;
        panel.style.left = `${Math.round(left - offset.left)}px`;
        panel.dataset.placement = placeAbove ? 'top' : 'bottom';
    }
}

export default RemoteSelect;
