/**
 * Transfer — 雙清單選擇器（左：可選項目，右：已選項目）
 *
 * - 兩側清單皆為 role="listbox" aria-multiselectable="true"，以 roving tabindex 管理焦點：
 *   每側只有一個 Tab 停駐點（目前項目），方向鍵移動焦點。
 * - 「勾選」即 listbox 的選取狀態（aria-selected）；勾選後以中間按鈕、Enter 或雙擊移到另一側。
 * - 大量資料採「分段渲染」：每側先建立前 100 列，捲動接近底部或以鍵盤移到尚未建立的位置時，
 *   才再附加下一段；每列帶 aria-setsize / aria-posinset，讓輔助科技知道總數。
 * - 已選清單維持 value 的順序；新移入的項目附加在最後。
 *
 * @example
 * const transfer = new Transfer({
 *     items: [{ value: 'room-a', label: '會議室 A' }, { value: 'room-b', label: '會議室 B' }],
 *     value: ['room-b'],
 *     onChange: (values, { moved, direction }) => console.log(values, moved, direction)
 * });
 * transfer.mount('#host');
 */
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';
import { setFieldError, clearFieldError, FIELD_ERROR_CONTRACT } from '../../utils/field-error.js';
import { nextUid } from '../../utils/uid.js';
import './locale.js';

/** 每次附加的列數（分段渲染）。 */
const CHUNK_SIZE = 100;
/** PageUp / PageDown 一次移動的列數。 */
const PAGE_STEP = 10;
const SIDES = ['source', 'target'];
const NONSPACING_MARKS = /\p{Mn}/gu;
const VISUALLY_HIDDEN = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap;';

/** 搜尋用正規化：NFKD 拆出變音符號後移除，再轉小寫（不分大小寫、不分重音）。 */
function normalizeSearchText(text) {
    return String(text ?? '').normalize('NFKD').replace(NONSPACING_MARKS, '').toLowerCase();
}

function isDomNode(value) {
    return Boolean(value) && typeof value === 'object' && typeof value.nodeType === 'number';
}

export class Transfer {
    constructor(options = {}) {
        this.options = {
            items: [],              // [{ value, label, description?, disabled? }]
            value: [],              // 已選值（依序）
            titles: null,           // [可選清單標題, 已選清單標題]；null 用 Locale 預設
            searchable: true,       // 每側清單各自的搜尋框
            showSelectAll: true,    // 標題列的全選核取方塊
            maxSelected: null,      // 已選上限；null 不限
            sortable: false,        // 已選清單可用上移/下移按鈕與 Alt+↑/↓ 排序
            height: '280px',        // 清單高度（CSS 長度）
            renderItem: null,       // (item) => Node；預設顯示 label 與 description
            disabled: false,        // 停用
            onChange: null,         // (values, { moved, direction }) => void
            ...options
        };

        this._uid = nextUid('cl-transfer');
        this._destroyed = false;
        this._localeListening = false;
        this._records = [];
        this._byValue = new Map();
        this._indexItems(this.options.items);

        this._state = createComponentState({
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            value: this._normalizeValue(this.options.value),
            checked: { source: [], target: [] },
            query: { source: '', target: '' }
        }, {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            SET_DISABLED: (state, payload) => ({ ...state, availability: payload?.disabled ? 'disabled' : 'enabled' }),
            SET_CHECKED: (state, payload) => ({
                ...state,
                checked: { ...state.checked, [payload.side]: [...payload.values] }
            }),
            SET_QUERY: (state, payload) => ({
                ...state,
                query: { ...state.query, [payload.side]: String(payload?.query ?? '') }
            }),
            APPLY: (state, payload) => ({
                ...state,
                value: [...payload.value],
                checked: { source: [...payload.checked.source], target: [...payload.checked.target] }
            }),
            RESET: (state, payload) => ({
                ...state,
                value: [...payload.value],
                checked: { source: [], target: [] },
                query: { source: '', target: '' }
            })
        });

        this._onLocaleChange = () => {
            if (!this._destroyed) this._renderAll();
        };

        this.element = this._createElement();
        this._renderAll();
    }

    // ── 資料 ────────────────────────────────────────────────

    _indexItems(items) {
        const records = [];
        const byValue = new Map();
        (Array.isArray(items) ? items : []).forEach((raw) => {
            if (!raw || typeof raw !== 'object' || byValue.has(raw.value)) return;
            const item = { ...raw };
            const label = item.label === null || item.label === undefined ? String(item.value ?? '') : String(item.label);
            const description = item.description === null || item.description === undefined ? '' : String(item.description);
            const record = {
                item,
                value: item.value,
                label,
                description,
                disabled: Boolean(item.disabled),
                key: normalizeSearchText(description ? `${label}\n${description}` : label)
            };
            records.push(record);
            byValue.set(item.value, record);
        });
        this._records = records;
        this._byValue = byValue;
    }

    /** 去除重複與不存在於 items 的值，保留原順序。 */
    _normalizeValue(values) {
        const result = [];
        const seen = new Set();
        for (const value of Array.isArray(values) ? values : []) {
            if (seen.has(value) || !this._byValue.has(value)) continue;
            seen.add(value);
            result.push(value);
        }
        return result;
    }

    _isMovable(value) {
        const record = this._byValue.get(value);
        return Boolean(record) && !record.disabled;
    }

    _maxSelected() {
        const max = Number(this.options.maxSelected);
        return this.options.maxSelected !== null && this.options.maxSelected !== undefined && Number.isFinite(max) && max >= 0
            ? Math.floor(max)
            : null;
    }

    _snap() {
        return this._state.snapshot();
    }

    _send(event, payload = null) {
        return this._state.send(event, payload);
    }

    _title(side) {
        const titles = Array.isArray(this.options.titles) ? this.options.titles : [];
        const custom = titles[side === 'source' ? 0 : 1];
        if (custom !== null && custom !== undefined && String(custom) !== '') return String(custom);
        return Locale.t(side === 'source' ? 'transfer.sourceTitle' : 'transfer.targetTitle');
    }

    // ── DOM 建立 ────────────────────────────────────────────

    _createElement() {
        const root = document.createElement('div');
        root.className = 'cl-transfer';
        root.style.cssText = 'position:relative;display:flex;flex-direction:column;gap:6px;width:100%;max-width:100%;min-width:0;box-sizing:border-box;font-family:var(--cl-font-family);color:var(--cl-text);';

        const body = document.createElement('div');
        body.className = 'cl-transfer__body';
        body.style.cssText = 'display:flex;flex-wrap:wrap;align-items:stretch;gap:8px;min-width:0;';

        const instructions = document.createElement('div');
        instructions.className = 'cl-transfer__instructions';
        instructions.id = `${this._uid}-instructions`;
        instructions.style.cssText = VISUALLY_HIDDEN;
        this._instructions = instructions;

        this._views = {
            source: this._createPanel('source'),
            target: this._createPanel('target')
        };

        const operations = document.createElement('div');
        operations.className = 'cl-transfer__operations';
        operations.style.cssText = 'display:flex;flex-direction:column;justify-content:center;align-items:center;gap:8px;flex:0 0 auto;';
        this._toTargetButton = this._createButton('cl-transfer__button cl-transfer__button--to-target', '→', () => this._move('right', { via: 'button' }));
        this._toSourceButton = this._createButton('cl-transfer__button cl-transfer__button--to-source', '←', () => this._move('left', { via: 'button' }));
        operations.append(this._toTargetButton, this._toSourceButton);

        body.append(this._views.source.panel, operations, this._views.target.panel);

        const live = document.createElement('div');
        live.className = 'cl-transfer__live';
        live.setAttribute('role', 'status');
        live.setAttribute('aria-live', 'polite');
        live.setAttribute('aria-atomic', 'true');
        live.style.cssText = VISUALLY_HIDDEN;
        this._live = live;

        root.append(body, instructions, live);
        return root;
    }

    _createPanel(side) {
        const view = {
            side,
            visible: [],
            positions: new Map(),
            rows: [],
            rendered: 0,
            active: 0,
            preferredValue: undefined,
            anchorValue: undefined,
            tabRow: null,
            hoverRow: null
        };

        const panel = document.createElement('div');
        panel.className = `cl-transfer__panel cl-transfer__panel--${side}`;
        panel.style.cssText = 'display:flex;flex-direction:column;flex:1 1 220px;min-width:0;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg);overflow:hidden;';

        const header = document.createElement('div');
        header.className = 'cl-transfer__header';
        header.style.cssText = 'display:flex;align-items:center;gap:8px;padding:8px 10px;border-bottom:1px solid var(--cl-border-light);background:var(--cl-bg-secondary);min-width:0;';

        if (this.options.showSelectAll) {
            const selectAll = document.createElement('input');
            selectAll.type = 'checkbox';
            selectAll.className = 'cl-transfer__select-all';
            selectAll.style.cssText = 'margin:0;width:16px;height:16px;flex:0 0 auto;accent-color:var(--cl-primary);cursor:pointer;';
            selectAll.addEventListener('change', () => this._toggleAllVisible(side));
            header.appendChild(selectAll);
            view.selectAll = selectAll;
        }

        const title = document.createElement('span');
        title.className = 'cl-transfer__title';
        title.id = `${this._uid}-${side}-title`;
        title.style.cssText = 'flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-size:var(--cl-font-size-md);font-weight:600;';
        header.appendChild(title);
        view.title = title;

        if (side === 'target' && this._maxSelected() !== null) {
            const maxHint = document.createElement('span');
            maxHint.className = 'cl-transfer__max';
            maxHint.style.cssText = 'flex:0 0 auto;font-size:var(--cl-font-size-xs);color:var(--cl-text-muted);';
            header.appendChild(maxHint);
            view.maxHint = maxHint;
        }

        const count = document.createElement('span');
        count.className = 'cl-transfer__count';
        count.style.cssText = 'flex:0 0 auto;font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);font-variant-numeric:tabular-nums;';
        header.appendChild(count);
        view.count = count;
        panel.appendChild(header);

        const listbox = document.createElement('div');
        listbox.className = 'cl-transfer__list';
        listbox.id = `${this._uid}-${side}-list`;
        listbox.setAttribute('role', 'listbox');
        listbox.setAttribute('aria-multiselectable', 'true');
        listbox.setAttribute('aria-labelledby', title.id);
        listbox.setAttribute('aria-describedby', this._instructions.id);
        listbox.tabIndex = -1;
        // height 是基準高度；另一側較高（例如有排序列）而被拉伸時，清單會長滿剩餘空間
        listbox.style.cssText = 'position:relative;flex:1 1 auto;overflow-y:auto;overflow-x:hidden;padding:4px 0;box-sizing:border-box;outline-offset:-2px;';
        const height = this.options.height;
        listbox.style.height = typeof height === 'number' ? `${height}px` : String(height || '280px');
        listbox.addEventListener('click', (event) => this._onListClick(side, event));
        listbox.addEventListener('dblclick', (event) => this._onListDoubleClick(side, event));
        listbox.addEventListener('keydown', (event) => this._onListKeydown(side, event));
        listbox.addEventListener('focusin', (event) => this._onListFocusIn(side, event));
        listbox.addEventListener('focusout', (event) => this._onListFocusOut(side, event));
        listbox.addEventListener('scroll', () => this._onListScroll(side));
        listbox.addEventListener('mouseover', (event) => this._onListHover(side, event, true));
        listbox.addEventListener('mouseout', (event) => this._onListHover(side, event, false));
        view.listbox = listbox;

        if (this.options.searchable) {
            const searchWrap = document.createElement('div');
            searchWrap.className = 'cl-transfer__search-wrap';
            searchWrap.style.cssText = 'padding:8px 10px 4px;';
            const search = document.createElement('input');
            search.type = 'search';
            search.className = 'cl-transfer__search';
            search.setAttribute('aria-controls', listbox.id);
            search.autocomplete = 'off';
            search.style.cssText = 'width:100%;box-sizing:border-box;height:30px;padding:0 8px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);background:var(--cl-bg);color:var(--cl-text);font-size:var(--cl-font-size-md);font-family:inherit;';
            search.addEventListener('input', () => this._onSearchInput(side));
            search.addEventListener('keydown', (event) => this._onSearchKeydown(side, event));
            searchWrap.appendChild(search);
            panel.appendChild(searchWrap);
            view.search = search;
        }

        const listWrap = document.createElement('div');
        listWrap.className = 'cl-transfer__list-wrap';
        listWrap.style.cssText = 'position:relative;display:flex;flex-direction:column;flex:1 1 auto;min-height:0;';
        const empty = document.createElement('div');
        empty.className = 'cl-transfer__empty';
        empty.style.cssText = 'position:absolute;left:0;right:0;top:0;padding:24px 12px;text-align:center;font-size:var(--cl-font-size-md);color:var(--cl-text-placeholder);pointer-events:none;';
        listWrap.append(listbox, empty);
        panel.appendChild(listWrap);
        view.empty = empty;

        if (side === 'target' && this.options.sortable) {
            const sortBar = document.createElement('div');
            sortBar.className = 'cl-transfer__sort';
            sortBar.style.cssText = 'display:flex;justify-content:flex-end;gap:6px;padding:6px 10px;border-top:1px solid var(--cl-border-light);';
            this._moveUpButton = this._createButton('cl-transfer__button cl-transfer__button--up', '↑', () => this._sort('up', { via: 'button' }));
            this._moveDownButton = this._createButton('cl-transfer__button cl-transfer__button--down', '↓', () => this._sort('down', { via: 'button' }));
            sortBar.append(this._moveUpButton, this._moveDownButton);
            panel.appendChild(sortBar);
        }

        view.panel = panel;
        return view;
    }

    _createButton(className, text, onActivate) {
        const button = document.createElement('button');
        button.type = 'button';
        button.className = className;
        button.textContent = text;
        button.style.cssText = 'min-width:36px;height:32px;padding:0 10px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg);color:var(--cl-text);font-size:var(--cl-font-size-lg);font-family:inherit;line-height:1;cursor:pointer;';
        button.addEventListener('click', () => {
            if (!button.disabled) onActivate();
        });
        return button;
    }

    // ── 渲染 ────────────────────────────────────────────────

    _renderAll() {
        const state = this._snap();
        this._applyTexts(state);
        this._applyAvailability(state);
        SIDES.forEach((side) => this._renderList(side, state));
        this._updateButtons(state);
    }

    _applyTexts(state = this._snap()) {
        const sourceTitle = this._title('source');
        const targetTitle = this._title('target');
        this._instructions.textContent = Locale.t('transfer.instructions');
        SIDES.forEach((side) => {
            const view = this._views[side];
            const title = side === 'source' ? sourceTitle : targetTitle;
            view.title.textContent = title;
            if (view.search) {
                view.search.placeholder = Locale.t('transfer.searchPlaceholder');
                view.search.setAttribute('aria-label', Locale.t('transfer.searchLabel', { title }));
            }
            if (view.selectAll) view.selectAll.setAttribute('aria-label', Locale.t('transfer.selectAll', { title }));
            if (view.maxHint) view.maxHint.textContent = Locale.t('transfer.maxHint', { max: this._maxSelected() });
            this._updateHeader(side, state);
            this._updateEmpty(side, state);
        });
        this._labelButton(this._toTargetButton, Locale.t('transfer.moveToTarget', { title: targetTitle }));
        this._labelButton(this._toSourceButton, Locale.t('transfer.moveToSource', { title: sourceTitle }));
        if (this._moveUpButton) this._labelButton(this._moveUpButton, Locale.t('transfer.moveUp'));
        if (this._moveDownButton) this._labelButton(this._moveDownButton, Locale.t('transfer.moveDown'));
    }

    _labelButton(button, label) {
        button.setAttribute('aria-label', label);
        button.title = label;
    }

    _applyAvailability(state) {
        const disabled = state.availability === 'disabled';
        this.element.style.display = state.visibility === 'hidden' ? 'none' : 'flex';
        if (disabled) this.element.setAttribute('aria-disabled', 'true');
        else this.element.removeAttribute('aria-disabled');
        SIDES.forEach((side) => {
            const view = this._views[side];
            if (view.search) view.search.disabled = disabled;
            if (disabled) view.listbox.setAttribute('aria-disabled', 'true');
            else view.listbox.removeAttribute('aria-disabled');
            view.panel.style.background = disabled ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)';
        });
    }

    _computeVisible(side, state) {
        const query = normalizeSearchText(state.query[side]).trim();
        const matches = (record) => !query || record.key.includes(query);
        if (side === 'source') {
            const selected = new Set(state.value);
            return this._records.filter((record) => !selected.has(record.value) && matches(record));
        }
        const result = [];
        for (const value of state.value) {
            const record = this._byValue.get(value);
            if (record && matches(record)) result.push(record);
        }
        return result;
    }

    _renderList(side, state = this._snap()) {
        const view = this._views[side];
        const activeValue = view.preferredValue !== undefined ? view.preferredValue : view.visible[view.active]?.value;
        view.preferredValue = undefined;
        const hadFocus = view.listbox.contains(document.activeElement);

        view.visible = this._computeVisible(side, state);
        view.positions = new Map(view.visible.map((record, index) => [record.value, index]));
        if (activeValue !== undefined && view.positions.has(activeValue)) {
            view.active = view.positions.get(activeValue);
        } else {
            view.active = Math.max(0, Math.min(view.active, view.visible.length - 1));
        }
        if (view.anchorValue !== undefined && !view.positions.has(view.anchorValue)) view.anchorValue = undefined;

        view.hoverRow = null;
        view.tabRow = null;
        view.rows = [];
        view.rendered = 0;
        view.listbox.replaceChildren();
        this._ensureRendered(side, Math.max(CHUNK_SIZE, view.active + 1), state);
        this._syncRoving(side, state);
        this._updateHeader(side, state);
        this._updateEmpty(side, state);

        if (hadFocus) {
            if (view.visible.length && state.availability !== 'disabled') this._setActive(side, view.active, { focus: true });
            else view.listbox.focus();
        }
    }

    /** 確保前 count 列已建立（以 CHUNK_SIZE 為單位附加）。 */
    _ensureRendered(side, count, state = this._snap()) {
        const view = this._views[side];
        const needed = Math.min(view.visible.length, count);
        if (view.rendered >= needed) return;
        const end = Math.min(view.visible.length, Math.max(needed, view.rendered + CHUNK_SIZE));
        const checked = new Set(state.checked[side]);
        const disabledAll = state.availability === 'disabled';
        const fragment = document.createDocumentFragment();
        for (let index = view.rendered; index < end; index += 1) {
            const row = this._createRow(view, view.visible[index], index, checked.has(view.visible[index].value), disabledAll);
            view.rows[index] = row;
            fragment.appendChild(row);
        }
        view.listbox.appendChild(fragment);
        view.rendered = end;
        view.listbox.dataset.rendered = String(end);
    }

    _createRow(view, record, index, checked, disabledAll) {
        const row = document.createElement('div');
        row.className = 'cl-transfer__item';
        row.id = `${this._uid}-${view.side}-option-${index}`;
        row.setAttribute('role', 'option');
        row.setAttribute('aria-posinset', String(index + 1));
        row.setAttribute('aria-setsize', String(view.visible.length));
        row.dataset.index = String(index);
        row.dataset.value = String(record.value);
        row.tabIndex = -1;
        row.style.cssText = 'display:flex;align-items:flex-start;gap:8px;padding:6px 10px;font-size:var(--cl-font-size-md);line-height:1.4;outline-offset:-2px;user-select:none;';

        const box = document.createElement('span');
        box.className = 'cl-transfer__check';
        box.setAttribute('aria-hidden', 'true');
        box.style.cssText = 'flex:0 0 auto;display:inline-flex;align-items:center;justify-content:center;width:16px;height:16px;margin-top:2px;box-sizing:border-box;border:1px solid var(--cl-border-dark);border-radius:var(--cl-radius-sm);color:var(--cl-text-inverse);font-size:var(--cl-font-size-xs);line-height:1;';

        const content = document.createElement('span');
        content.className = 'cl-transfer__content';
        content.style.cssText = 'flex:1;min-width:0;display:flex;flex-direction:column;overflow-wrap:anywhere;';
        this._fillContent(content, record, row);

        row.append(box, content);
        this._paintRow(row, record, checked, disabledAll);
        return row;
    }

    _fillContent(content, record, row) {
        const { renderItem } = this.options;
        if (typeof renderItem === 'function') {
            try {
                const node = renderItem(record.item);
                if (isDomNode(node)) {
                    content.appendChild(node);
                    row.setAttribute('aria-label', record.label);
                    return;
                }
                if (node !== null && node !== undefined && typeof node !== 'object') {
                    content.textContent = String(node);
                    return;
                }
            } catch (error) {
                console.error('[Transfer] renderItem failed; falling back to the label.', error);
                content.replaceChildren();
            }
        }
        const label = document.createElement('span');
        label.className = 'cl-transfer__label';
        label.textContent = record.label;
        content.appendChild(label);
        if (record.description) {
            const description = document.createElement('span');
            description.className = 'cl-transfer__description';
            description.textContent = record.description;
            description.style.cssText = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);';
            content.appendChild(description);
        }
    }

    _paintRow(row, record, checked, disabledAll) {
        const disabled = record.disabled || disabledAll;
        row.setAttribute('aria-selected', checked ? 'true' : 'false');
        if (disabled) row.setAttribute('aria-disabled', 'true');
        else row.removeAttribute('aria-disabled');
        row.classList.toggle('cl-transfer__item--checked', checked);
        row.classList.toggle('cl-transfer__item--disabled', disabled);
        const hovered = row.dataset.hover === 'true' && !disabled;
        row.style.background = checked ? 'var(--cl-primary-light)' : (hovered ? 'var(--cl-bg-hover)' : 'transparent');
        row.style.color = disabled ? 'var(--cl-text-muted)' : 'var(--cl-text)';
        row.style.cursor = disabled ? 'not-allowed' : 'pointer';
        row.style.opacity = disabled ? '0.6' : '1';
        const box = row.firstChild;
        box.textContent = checked ? '✓' : '';
        box.style.background = checked ? 'var(--cl-primary)' : 'var(--cl-bg)';
        box.style.borderColor = checked ? 'var(--cl-primary)' : 'var(--cl-border-dark)';
    }

    _repaintValues(side, values, state = this._snap()) {
        const view = this._views[side];
        const checked = new Set(state.checked[side]);
        const disabledAll = state.availability === 'disabled';
        for (const value of values) {
            const index = view.positions.get(value);
            if (index === undefined || index >= view.rendered) continue;
            this._paintRow(view.rows[index], view.visible[index], checked.has(value), disabledAll);
        }
    }

    _syncRoving(side, state = this._snap()) {
        const view = this._views[side];
        if (view.tabRow) view.tabRow.tabIndex = -1;
        view.tabRow = null;
        if (!view.visible.length || state.availability === 'disabled') return;
        const row = view.rows[view.active];
        if (row) {
            row.tabIndex = 0;
            view.tabRow = row;
        }
    }

    _updateHeader(side, state = this._snap()) {
        const view = this._views[side];
        const checked = state.checked[side];
        const total = side === 'source' ? this._records.length - state.value.length : state.value.length;
        view.count.textContent = Locale.t('transfer.count', { checked: checked.length, total });
        view.count.title = Locale.t('transfer.countLabel', { title: this._title(side), checked: checked.length, total });
        if (view.selectAll) {
            const checkedSet = new Set(checked);
            let enabled = 0;
            let enabledChecked = 0;
            for (const record of view.visible) {
                if (record.disabled) continue;
                enabled += 1;
                if (checkedSet.has(record.value)) enabledChecked += 1;
            }
            view.selectAll.checked = enabled > 0 && enabledChecked === enabled;
            view.selectAll.indeterminate = enabledChecked > 0 && enabledChecked < enabled;
            view.selectAll.disabled = state.availability === 'disabled' || enabled === 0;
            view.selectAll.style.cursor = view.selectAll.disabled ? 'not-allowed' : 'pointer';
        }
    }

    _updateEmpty(side, state = this._snap()) {
        const view = this._views[side];
        const isEmpty = view.visible.length === 0;
        view.empty.style.display = isEmpty ? 'block' : 'none';
        view.empty.textContent = !isEmpty
            ? ''
            : (normalizeSearchText(state.query[side]).trim() ? Locale.t('transfer.noMatch') : Locale.t('transfer.empty'));
    }

    _setButtonEnabled(button, enabled) {
        if (!button) return;
        button.disabled = !enabled;
        button.style.opacity = enabled ? '1' : '0.5';
        button.style.cursor = enabled ? 'pointer' : 'not-allowed';
        button.style.background = enabled ? 'var(--cl-bg)' : 'var(--cl-bg-secondary)';
    }

    _updateButtons(state = this._snap()) {
        const disabled = state.availability === 'disabled';
        const max = this._maxSelected();
        const capacity = max === null ? Infinity : max - state.value.length;
        const movableSource = state.checked.source.some((value) => this._isMovable(value));
        const movableTarget = state.checked.target.some((value) => this._isMovable(value));
        this._setButtonEnabled(this._toTargetButton, !disabled && movableSource && capacity > 0);
        this._setButtonEnabled(this._toSourceButton, !disabled && movableTarget);
        if (this.options.sortable) {
            const { canUp, canDown } = this._sortAvailability(state, new Set(state.checked.target));
            this._setButtonEnabled(this._moveUpButton, !disabled && canUp);
            this._setButtonEnabled(this._moveDownButton, !disabled && canDown);
        }
    }

    _announce(message) {
        if (!this._live) return;
        this._live.textContent = '';
        this._live.textContent = message;
    }

    // ── 焦點與勾選 ──────────────────────────────────────────

    _setActive(side, index, { focus = false } = {}) {
        const view = this._views[side];
        if (!view.visible.length) return;
        const next = Math.max(0, Math.min(index, view.visible.length - 1));
        this._ensureRendered(side, next + 1);
        view.active = next;
        this._syncRoving(side);
        const row = view.rows[next];
        if (focus && row) {
            row.focus();
            row.scrollIntoView?.({ block: 'nearest' });
        }
    }

    _setChecked(side, nextValues, changedValues) {
        const state = this._state.send('SET_CHECKED', { side, values: nextValues });
        this._repaintValues(side, changedValues, state);
        this._updateHeader(side, state);
        this._updateButtons(state);
    }

    _toggleAt(side, index) {
        const view = this._views[side];
        const record = view.visible[index];
        if (!record || record.disabled) return;
        const checked = this._snap().checked[side];
        const isChecked = checked.includes(record.value);
        const next = isChecked ? checked.filter((value) => value !== record.value) : [...checked, record.value];
        view.anchorValue = record.value;
        this._setChecked(side, next, [record.value]);
    }

    _checkRange(side, from, to) {
        const view = this._views[side];
        const start = Math.max(0, Math.min(from, to));
        const end = Math.min(view.visible.length - 1, Math.max(from, to));
        const checked = this._snap().checked[side];
        const checkedSet = new Set(checked);
        const added = [];
        for (let index = start; index <= end; index += 1) {
            const record = view.visible[index];
            if (record && !record.disabled && !checkedSet.has(record.value)) {
                checkedSet.add(record.value);
                added.push(record.value);
            }
        }
        if (added.length) this._setChecked(side, [...checked, ...added], added);
    }

    _toggleAllVisible(side) {
        const state = this._snap();
        if (state.availability === 'disabled') return;
        const view = this._views[side];
        const enabled = view.visible.filter((record) => !record.disabled).map((record) => record.value);
        if (!enabled.length) {
            this._updateHeader(side, state);
            return;
        }
        const checkedSet = new Set(state.checked[side]);
        const allChecked = enabled.every((value) => checkedSet.has(value));
        if (allChecked) {
            const remove = new Set(enabled);
            this._setChecked(side, state.checked[side].filter((value) => !remove.has(value)), enabled);
        } else {
            const added = enabled.filter((value) => !checkedSet.has(value));
            this._setChecked(side, [...state.checked[side], ...added], added);
        }
    }

    _anchorIndex(side) {
        const view = this._views[side];
        return view.anchorValue !== undefined && view.positions.has(view.anchorValue)
            ? view.positions.get(view.anchorValue)
            : null;
    }

    // ── 事件 ────────────────────────────────────────────────

    _rowFromEvent(side, event) {
        const row = event.target?.closest?.('.cl-transfer__item');
        return row && this._views[side].listbox.contains(row) ? row : null;
    }

    _onListClick(side, event) {
        if (this._snap().availability === 'disabled') return;
        const row = this._rowFromEvent(side, event);
        if (!row) return;
        const index = Number(row.dataset.index);
        this._setActive(side, index, { focus: true });
        const record = this._views[side].visible[index];
        if (!record || record.disabled) return;
        if (event.shiftKey) {
            const anchor = this._anchorIndex(side);
            if (anchor === null) this._toggleAt(side, index);
            else this._checkRange(side, anchor, index);
        } else {
            this._toggleAt(side, index);
        }
    }

    _onListDoubleClick(side, event) {
        if (this._snap().availability === 'disabled') return;
        const row = this._rowFromEvent(side, event);
        if (!row) return;
        const record = this._views[side].visible[Number(row.dataset.index)];
        if (!record || record.disabled) return;
        // 雙擊前的兩次 click 已把勾選狀態切換兩次（等於沒變），這裡只移動被雙擊的項目。
        this._move(side === 'source' ? 'right' : 'left', { values: [record.value], via: 'pointer' });
    }

    _onListKeydown(side, event) {
        const state = this._snap();
        if (state.availability === 'disabled') return;
        const view = this._views[side];
        const count = view.visible.length;
        if (!count) return;
        const key = event.key;

        if (event.altKey && (key === 'ArrowUp' || key === 'ArrowDown')) {
            if (side === 'target' && this.options.sortable) {
                event.preventDefault();
                this._sort(key === 'ArrowUp' ? 'up' : 'down', { focusedValue: view.visible[view.active]?.value, via: 'keyboard' });
            }
            return;
        }

        const navigation = {
            ArrowDown: view.active + 1,
            ArrowUp: view.active - 1,
            Home: 0,
            End: count - 1,
            PageDown: view.active + PAGE_STEP,
            PageUp: view.active - PAGE_STEP
        };
        if (Object.prototype.hasOwnProperty.call(navigation, key)) {
            event.preventDefault();
            const next = Math.max(0, Math.min(navigation[key], count - 1));
            if (event.shiftKey) {
                let anchor = this._anchorIndex(side);
                if (anchor === null) {
                    anchor = view.active;
                    view.anchorValue = view.visible[view.active]?.value;
                }
                this._setActive(side, next, { focus: true });
                this._checkRange(side, anchor, next);
            } else {
                this._setActive(side, next, { focus: true });
            }
            return;
        }

        if (key === ' ' || key === 'Spacebar') {
            event.preventDefault();
            const anchor = this._anchorIndex(side);
            if (event.shiftKey && anchor !== null) this._checkRange(side, anchor, view.active);
            else this._toggleAt(side, view.active);
            return;
        }

        if (key === 'Enter') {
            event.preventDefault();
            this._move(side === 'source' ? 'right' : 'left', { focusedValue: view.visible[view.active]?.value, via: 'keyboard' });
            return;
        }

        if ((key === 'a' || key === 'A') && (event.ctrlKey || event.metaKey) && !event.altKey) {
            event.preventDefault();
            this._toggleAllVisible(side);
        }
    }

    _onListFocusIn(side, event) {
        const view = this._views[side];
        if (event.target === view.listbox) {
            if (view.visible.length && this._snap().availability !== 'disabled') this._setActive(side, view.active, { focus: true });
            return;
        }
        const row = this._rowFromEvent(side, event);
        if (!row) return;
        const index = Number(row.dataset.index);
        if (index !== view.active) {
            view.active = index;
            this._syncRoving(side);
        }
        row.style.outline = '2px solid var(--cl-primary)';
    }

    _onListFocusOut(side, event) {
        const row = this._rowFromEvent(side, event);
        if (row) row.style.outline = '';
    }

    _onListScroll(side) {
        const view = this._views[side];
        if (view.rendered >= view.visible.length) return;
        const listbox = view.listbox;
        if (listbox.scrollTop + listbox.clientHeight >= listbox.scrollHeight - 48) {
            this._ensureRendered(side, view.rendered + CHUNK_SIZE);
            this._syncRoving(side);
        }
    }

    _onListHover(side, event, entering) {
        const row = this._rowFromEvent(side, event);
        if (!row) return;
        if (!entering && row.contains(event.relatedTarget)) return;
        if (entering) row.dataset.hover = 'true';
        else delete row.dataset.hover;
        // 勾選與停用列的底色由 _paintRow 決定，懸停只影響一般列
        if (row.getAttribute('aria-selected') === 'true' || row.hasAttribute('aria-disabled')) return;
        row.style.background = entering ? 'var(--cl-bg-hover)' : 'transparent';
    }

    _onSearchInput(side) {
        const view = this._views[side];
        const state = this._state.send('SET_QUERY', { side, query: view.search.value });
        view.active = 0;
        this._renderList(side, state);
        this._updateButtons(state);
    }

    _onSearchKeydown(side, event) {
        const view = this._views[side];
        if (event.key === 'ArrowDown') {
            if (view.visible.length && this._snap().availability !== 'disabled') {
                event.preventDefault();
                this._setActive(side, view.active, { focus: true });
            }
        } else if (event.key === 'Escape') {
            if (view.search.value) {
                event.preventDefault();
                event.stopPropagation();
                view.search.value = '';
                this._onSearchInput(side);
            }
        } else if (event.key === 'Enter') {
            // 搜尋框在表單內時，Enter 不應送出表單
            event.preventDefault();
        }
    }

    // ── 移動與排序 ──────────────────────────────────────────

    /**
     * @param {'right'|'left'} direction - right：移到已選；left：移回可選
     * @param {object} [options]
     * @param {Array} [options.values] - 指定要移動的值（雙擊）；未指定時移動勾選的項目
     * @param {*} [options.focusedValue] - 沒有勾選時改移動這個（鍵盤 Enter 的焦點項目）
     * @param {'button'|'keyboard'|'pointer'} [options.via]
     */
    _move(direction, { values = null, focusedValue, via = 'button' } = {}) {
        const state = this._snap();
        if (state.availability === 'disabled') return false;
        const fromSide = direction === 'right' ? 'source' : 'target';
        const inTarget = new Set(state.value);
        const onFromSide = (value) => (fromSide === 'target' ? inTarget.has(value) : !inTarget.has(value));

        let candidates = (values ?? state.checked[fromSide]).filter((value) => this._isMovable(value) && onFromSide(value));
        if (!candidates.length && !values && focusedValue !== undefined && this._isMovable(focusedValue) && onFromSide(focusedValue)) {
            candidates = [focusedValue];
        }
        if (!candidates.length) return false;

        const candidateSet = new Set(candidates);
        let moving;
        let nextValue;
        let limited = false;
        const max = this._maxSelected();
        if (direction === 'right') {
            moving = this._records.filter((record) => candidateSet.has(record.value)).map((record) => record.value);
            if (max !== null) {
                const capacity = Math.max(0, max - state.value.length);
                if (moving.length > capacity) {
                    moving = moving.slice(0, capacity);
                    limited = true;
                }
            }
            if (!moving.length) {
                this._announce(Locale.t('transfer.maxReached', { max }));
                return false;
            }
            nextValue = [...state.value, ...moving];
        } else {
            moving = state.value.filter((value) => candidateSet.has(value));
            nextValue = state.value.filter((value) => !candidateSet.has(value));
        }

        const movedSet = new Set(moving);
        // 來源側的焦點移到原焦點之後第一個沒被移走的項目（沒有時取前一個）
        const origin = this._views[fromSide];
        const remaining = (index) => origin.visible[index] && !movedSet.has(origin.visible[index].value);
        let nextIndex = origin.active;
        while (nextIndex < origin.visible.length && !remaining(nextIndex)) nextIndex += 1;
        if (nextIndex >= origin.visible.length) {
            nextIndex = origin.active - 1;
            while (nextIndex >= 0 && !remaining(nextIndex)) nextIndex -= 1;
        }
        origin.preferredValue = nextIndex >= 0 ? origin.visible[nextIndex].value : undefined;

        const nextState = this._state.send('APPLY', {
            value: nextValue,
            checked: {
                source: state.checked.source.filter((value) => !movedSet.has(value)),
                target: state.checked.target.filter((value) => !movedSet.has(value))
            }
        });

        const trigger = document.activeElement;
        SIDES.forEach((side) => this._renderList(side, nextState));
        this._updateButtons(nextState);

        if (via === 'button' && trigger && trigger.tagName === 'BUTTON' && trigger.disabled && this.element.contains(trigger)) {
            const origin = this._views[fromSide];
            const destination = this._views[fromSide === 'source' ? 'target' : 'source'];
            const next = origin.visible.length ? fromSide : destination.side;
            this._setActive(next, this._views[next].active, { focus: true });
        }

        const toSide = fromSide === 'source' ? 'target' : 'source';
        const message = Locale.t(direction === 'right' ? 'transfer.movedToTarget' : 'transfer.movedToSource', {
            count: moving.length,
            title: this._title(toSide)
        });
        this._announce(limited ? `${message} ${Locale.t('transfer.maxReached', { max })}` : message);

        if (typeof this.options.onChange === 'function') {
            this.options.onChange([...nextValue], { moved: [...moving], direction });
        }
        return true;
    }

    /** 以目前顯示（可能已篩選）的已選清單計算能否上移/下移。 */
    _sortAvailability(state, moving) {
        const visible = this._views.target.visible;
        let canUp = false;
        let canDown = false;
        for (let index = 0; index < visible.length; index += 1) {
            if (!moving.has(visible[index].value)) continue;
            if (index > 0 && !moving.has(visible[index - 1].value)) canUp = true;
            if (index < visible.length - 1 && !moving.has(visible[index + 1].value)) canDown = true;
            if (canUp && canDown) break;
        }
        return { canUp, canDown };
    }

    _sort(direction, { focusedValue, via = 'button' } = {}) {
        const state = this._snap();
        if (state.availability === 'disabled' || !this.options.sortable) return false;
        let moving = new Set(state.checked.target);
        if (!moving.size && via === 'keyboard' && focusedValue !== undefined) moving = new Set([focusedValue]);
        if (!moving.size) return false;

        const view = this._views.target;
        const order = view.visible.map((record) => record.value);
        const moved = new Set();
        if (direction === 'up') {
            for (let index = 1; index < order.length; index += 1) {
                if (moving.has(order[index]) && !moving.has(order[index - 1])) {
                    [order[index - 1], order[index]] = [order[index], order[index - 1]];
                    moved.add(order[index - 1]);
                }
            }
        } else {
            for (let index = order.length - 2; index >= 0; index -= 1) {
                if (moving.has(order[index]) && !moving.has(order[index + 1])) {
                    [order[index], order[index + 1]] = [order[index + 1], order[index]];
                    moved.add(order[index + 1]);
                }
            }
        }
        if (!moved.size) return false;

        // 只在「目前顯示的項目」所占的位置之間重新排列，被搜尋隱藏的項目維持原位
        const visibleSet = new Set(order);
        const nextValue = [...state.value];
        let cursor = 0;
        state.value.forEach((value, slot) => {
            if (visibleSet.has(value)) {
                nextValue[slot] = order[cursor];
                cursor += 1;
            }
        });

        const trigger = document.activeElement;
        const nextState = this._state.send('APPLY', { value: nextValue, checked: state.checked });
        this._renderList('target', nextState);
        this._updateButtons(nextState);
        if (via === 'button' && trigger && trigger.tagName === 'BUTTON' && trigger.disabled && this.element.contains(trigger)) {
            const other = trigger === this._moveUpButton ? this._moveDownButton : this._moveUpButton;
            if (other && !other.disabled) other.focus();
            else this._setActive('target', view.active, { focus: true });
        }
        const movedValues = nextValue.filter((value) => moved.has(value));
        this._announce(Locale.t(direction === 'up' ? 'transfer.movedUp' : 'transfer.movedDown', { count: movedValues.length }));
        if (typeof this.options.onChange === 'function') {
            this.options.onChange([...nextValue], { moved: movedValues, direction });
        }
        return true;
    }

    // ── 公開 API ────────────────────────────────────────────

    snapshot() {
        return this._state.snapshot();
    }

    getValue() {
        return [...this._snap().value];
    }

    /**
     * 設定已選值（依序）。不在 items 中的值與重複值會被略過。
     * 預設不觸發 onChange；傳 { emit: true } 時以 { moved: [], direction: null } 觸發。
     * maxSelected 只限制使用者操作，不截斷程式設定的值。
     */
    setValue(values, { emit = false } = {}) {
        if (this._destroyed) return this;
        const value = this._normalizeValue(values);
        const state = this._snap();
        const inTarget = new Set(value);
        const nextState = this._state.send('APPLY', {
            value,
            checked: {
                source: state.checked.source.filter((item) => !inTarget.has(item)),
                target: state.checked.target.filter((item) => inTarget.has(item))
            }
        });
        SIDES.forEach((side) => this._renderList(side, nextState));
        this._updateButtons(nextState);
        if (emit && typeof this.options.onChange === 'function') {
            this.options.onChange([...value], { moved: [], direction: null });
        }
        return this;
    }

    /** 替換 items；已選值中不存在的項目會被移除。 */
    setItems(items) {
        if (this._destroyed) return this;
        this.options.items = Array.isArray(items) ? items : [];
        this._indexItems(this.options.items);
        const state = this._snap();
        const value = this._normalizeValue(state.value);
        const inTarget = new Set(value);
        this._state.send('APPLY', {
            value,
            checked: {
                source: state.checked.source.filter((item) => this._isMovable(item) && !inTarget.has(item)),
                target: state.checked.target.filter((item) => this._isMovable(item) && inTarget.has(item))
            }
        });
        this._renderAll();
        return this;
    }

    /** 清空已選值、勾選與搜尋字；不觸發 onChange。 */
    clear() {
        if (this._destroyed) return this;
        this._state.send('RESET', { value: [] });
        SIDES.forEach((side) => {
            const view = this._views[side];
            if (view.search) view.search.value = '';
            view.active = 0;
            view.anchorValue = undefined;
        });
        this._renderAll();
        return this;
    }

    setDisabled(disabled) {
        if (this._destroyed) return this;
        this._state.send('SET_DISABLED', { disabled: Boolean(disabled) });
        this.options.disabled = Boolean(disabled);
        this._renderAll();
        return this;
    }

    /**
     * 標示欄位錯誤；空訊息等同 clearError()。
     * display:false 只標示錯誤狀態、不顯示文字，給自行顯示錯誤文字的外層（FormField、SearchForm）使用。
     */
    setError(message, { display = true } = {}) {
        if (this._destroyed) return this;
        setFieldError(this, message, {
            target: [this._views.source.listbox, this._views.target.listbox],
            visual: [this._views.source.panel, this._views.target.panel],
            container: this.element,
            display
        });
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

    show() {
        if (this._destroyed) return this;
        this._state.send('SHOW');
        this._applyAvailability(this._snap());
        return this;
    }

    hide() {
        if (this._destroyed) return this;
        this._state.send('HIDE');
        this._applyAvailability(this._snap());
        return this;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target || this._destroyed) return this;
        target.appendChild(this.element);
        this._state.send('MOUNT');
        if (!this._localeListening) {
            window.addEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = true;
        }
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        this._state.send('DESTROY');
        clearFieldError(this);
        if (this._localeListening) {
            window.removeEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = false;
        }
        this.element?.remove();
        SIDES.forEach((side) => {
            const view = this._views[side];
            view.rows = [];
            view.visible = [];
            view.positions = new Map();
            view.tabRow = null;
            view.hoverRow = null;
        });
    }
}

export default Transfer;
