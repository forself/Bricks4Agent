/**
 * IssueList — 驗證／衝突問題清單（複合元件）
 *
 * 顯示一組 error / warning / info 問題，使用者可依嚴重度篩選，並點選某列跳回問題來源：
 * onSelect 收到呼叫端原樣傳入的 issue 物件（含不透明的 source 定位資訊）。
 *
 * - 每列是 role="button" 的列：Enter／Space 觸發 onSelect；上下鍵與 Home／End 在列之間移動
 *   （roving tabindex，整份清單只佔一個 Tab 停駐點）；dismissible 時 Delete／Backspace 等同按「忽略」。
 * - 嚴重度同時以圖示、文字標籤與顏色呈現，不單靠顏色。
 * - 資料變動（setIssues／addIssue／removeIssue／clear／忽略）後，以 polite live region 播報摘要；
 *   依 announceDelay 節流，只播報與上次不同的摘要，初始資料不播報。
 * - CSP：全部以 createElement + textContent 建構，樣式走 CSSOM，無 innerHTML。
 *
 * @example
 * const list = new IssueList({
 *     issues: [
 *         { id: 'r1', severity: 'error', title: '會議室時段重疊', message: 'A 室 10:00–11:00 已被預約', source: { field: 'room' } },
 *         { id: 'r2', severity: 'warning', title: '參與人數超過建議上限' }
 *     ],
 *     onSelect: (issue) => focusField(issue.source?.field)
 * }).mount('#issues');
 */
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';
import { nextUid } from '../../utils/uid.js';
import './locale.js';

const SEVERITIES = Object.freeze(['error', 'warning', 'info']);

// 文字圖示（非 SVG）；U+FE0E 要求以文字而非彩色 emoji 呈現，才會套用 CSS 顏色
const SEVERITY_GLYPH = { error: '✕', warning: '⚠︎', info: 'ℹ︎' };
const SEVERITY_COLOR = { error: 'var(--cl-danger)', warning: 'var(--cl-warning-dark)', info: 'var(--cl-info)' };
const SEVERITY_SOFT = { error: 'var(--cl-danger-light)', warning: 'var(--cl-warning-light)', info: 'var(--cl-info-light)' };

const SR_ONLY_CSS = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap;';
const ROOT_CSS = 'position:relative;display:flex;flex-direction:column;gap:8px;min-width:0;font-family:var(--cl-font-family);color:var(--cl-text);';
const SUMMARY_CSS = 'display:flex;flex-wrap:wrap;align-items:center;gap:6px;';
const COUNT_CSS = 'display:inline-flex;align-items:center;gap:4px;padding:2px 10px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-pill);background:var(--cl-bg);color:var(--cl-text);font:inherit;font-size:var(--cl-font-size-sm);line-height:1.6;cursor:pointer;';
const BODY_CSS = 'display:flex;flex-direction:column;gap:8px;min-width:0;';
const LIST_CSS = 'list-style:none;margin:0;padding:0;display:flex;flex-direction:column;gap:4px;';
const GROUP_CSS = 'display:flex;flex-direction:column;gap:4px;';
const GROUP_HEADING_CSS = 'margin:4px 0 0;font-size:var(--cl-font-size-sm);font-weight:600;color:var(--cl-text-secondary);';
const ITEM_CSS = 'display:flex;align-items:stretch;gap:4px;margin:0;padding:0;border:1px solid var(--cl-border-light);border-left:3px solid var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg);';
const ROW_CSS = 'flex:1 1 auto;min-width:0;display:flex;align-items:flex-start;gap:8px;padding:8px 10px;cursor:pointer;border-radius:var(--cl-radius-md);background:transparent;';
const ICON_CSS = 'flex:0 0 auto;width:16px;text-align:center;font-weight:700;line-height:1.5;font-size:var(--cl-font-size-md);';
const BADGE_CSS = 'flex:0 0 auto;padding:0 6px;border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-xs);line-height:1.8;color:var(--cl-text);white-space:nowrap;';
const TEXT_CSS = 'flex:1 1 auto;min-width:0;display:flex;flex-direction:column;gap:2px;';
const TITLE_CSS = 'font-size:var(--cl-font-size-md);font-weight:600;color:var(--cl-text);overflow-wrap:anywhere;';
const MESSAGE_CSS = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);white-space:pre-line;overflow-wrap:anywhere;';
const META_CSS = 'flex:0 0 auto;margin-left:auto;font-size:var(--cl-font-size-xs);color:var(--cl-text-muted);white-space:nowrap;';
const DISMISS_CSS = 'flex:0 0 auto;width:32px;padding:0;border:0;background:transparent;color:var(--cl-text-muted);font:inherit;font-size:var(--cl-font-size-xl);line-height:1;cursor:pointer;border-radius:var(--cl-radius-sm);';
const EMPTY_CSS = 'margin:0;padding:12px;text-align:center;color:var(--cl-text-muted);font-size:var(--cl-font-size-md);';

function normalizeSeverity(value) {
    return SEVERITIES.includes(value) ? value : 'info';
}

/** null／空陣列／無有效值／三種全選 → null（不篩選）；否則依 error→warning→info 排序去重 */
function normalizeFilter(filter) {
    if (filter === null || filter === undefined) return null;
    const list = Array.isArray(filter) ? filter : [filter];
    const picked = SEVERITIES.filter((severity) => list.includes(severity));
    return picked.length > 0 && picked.length < SEVERITIES.length ? picked : null;
}

function sameId(left, right) {
    if (left === null || left === undefined || right === null || right === undefined) return false;
    return String(left) === String(right);
}

function textOf(value) {
    return value === null || value === undefined ? '' : String(value);
}

export class IssueList {
    static SEVERITIES = SEVERITIES;

    constructor(options = {}) {
        this.options = {
            issues: [],              // 問題陣列：[{ id, severity: 'error'|'warning'|'info', title, message?, source?, meta? }]
            sort: 'severity',        // 'severity'：依 error→warning→info 穩定排序；'none'：維持輸入順序
            groupBy: null,           // null：單一清單；'severity'：依嚴重度分組並顯示組標題
            showSummary: true,       // 顯示各嚴重度計數；點擊計數即篩選
            filter: null,            // null：顯示全部；['error', ...]：只顯示指定嚴重度
            dismissible: false,      // 每列顯示「忽略」按鈕（Delete／Backspace 鍵亦可）
            emptyText: null,         // 沒有問題時的文字；null 使用 Locale issueList.empty
            maxHeight: null,         // 清單最大高度（數字為 px，或 CSS 長度字串）；超過時捲動
            ariaLabel: null,         // 清單的無障礙名稱；null 使用 Locale issueList.label
            announceDelay: 1000,     // live region 播報節流間隔（ms）
            onSelect: null,          // (issue) => void，使用者點選或按 Enter／Space
            onDismiss: null,         // (issue) => void，使用者忽略某列（該列已從清單移除）
            ...options
        };

        this.element = null;
        this._destroyed = false;
        this._localeListening = false;
        this._keySeq = 0;
        this._records = this._normalizeIssues(this.options.issues);
        this._entries = new Map();   // key → { li, row, dismiss, record, index }
        this._order = [];            // 可見列的 key（DOM 順序）
        this._activeKey = null;
        this._hoverRow = null;
        this._spaceKey = null;
        this._announceTimer = null;
        this._uid = nextUid('issue-list');

        this._state = createComponentState(
            { lifecycle: 'created', filter: normalizeFilter(this.options.filter) },
            {
                MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
                SET_FILTER: (state, payload) => ({ ...state, filter: normalizeFilter(payload?.filter) }),
                DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' })
            }
        );

        this._onKeyDown = (event) => this._handleKeyDown(event);
        this._onKeyUp = (event) => this._handleKeyUp(event);
        this._onBodyClick = (event) => this._handleBodyClick(event);
        this._onSummaryClick = (event) => this._handleSummaryClick(event);
        this._onFocusIn = (event) => this._handleFocusIn(event);
        this._onMouseOver = (event) => this._handleMouseOver(event);
        this._onMouseLeave = () => this._setHoverRow(null);
        this._onLocaleChange = () => this._handleLocaleChange();

        this.element = this._createElement();
        this._render();
        // 初始資料只作為比較基準，不播報
        this._announcedSummary = this._summaryText();
    }

    // ── 建構 ──────────────────────────────────────────────

    /**
     * 正規化輸入；同 id 的問題沿用舊的內部 key，讓 setIssues 重新驗證後
     * 焦點與 roving 位置仍停在同一個問題上。
     */
    _normalizeIssues(issues, previous = []) {
        const list = Array.isArray(issues) ? issues : [];
        const keyById = new Map();
        for (const record of previous) {
            const id = record.issue.id;
            if (id !== null && id !== undefined && !keyById.has(String(id))) keyById.set(String(id), record.key);
        }
        const used = new Set();
        const records = [];
        for (const issue of list) {
            if (!issue || typeof issue !== 'object') continue;
            const id = issue.id;
            const reusable = id !== null && id !== undefined ? keyById.get(String(id)) : undefined;
            const record = reusable && !used.has(reusable)
                ? { key: reusable, issue, severity: normalizeSeverity(issue.severity) }
                : this._createRecord(issue);
            used.add(record.key);
            records.push(record);
        }
        return records;
    }

    _createRecord(issue) {
        this._keySeq += 1;
        return { key: `k${this._keySeq}`, issue, severity: normalizeSeverity(issue.severity) };
    }

    _createElement() {
        const root = document.createElement('div');
        root.className = 'cl-issue-list';
        root.style.cssText = ROOT_CSS;

        this._summary = null;
        this._summaryButtons = null;
        if (this.options.showSummary) {
            this._summary = this._createSummary();
            root.appendChild(this._summary);
        }

        const body = document.createElement('div');
        body.className = 'cl-issue-list__body';
        body.id = `${this._uid}-body`;
        body.setAttribute('role', 'group');
        body.tabIndex = -1;
        body.style.cssText = BODY_CSS;
        this._applyMaxHeight(body);
        body.addEventListener('keydown', this._onKeyDown);
        body.addEventListener('keyup', this._onKeyUp);
        body.addEventListener('click', this._onBodyClick);
        body.addEventListener('focusin', this._onFocusIn);
        body.addEventListener('mouseover', this._onMouseOver);
        body.addEventListener('mouseleave', this._onMouseLeave);
        root.appendChild(body);
        this._body = body;

        const live = document.createElement('div');
        live.className = 'cl-issue-list__live';
        live.setAttribute('role', 'status');
        live.setAttribute('aria-live', 'polite');
        live.setAttribute('aria-atomic', 'true');
        live.style.cssText = SR_ONLY_CSS;
        root.appendChild(live);
        this._live = live;

        return root;
    }

    _applyMaxHeight(body) {
        const { maxHeight } = this.options;
        if (maxHeight === null || maxHeight === undefined || maxHeight === '') return;
        // 個別屬性指派（非字串拼接 cssText），無效值由 CSSOM 自行忽略
        body.style.maxHeight = typeof maxHeight === 'number' ? `${maxHeight}px` : String(maxHeight);
        body.style.overflowY = 'auto';
    }

    _createSummary() {
        const bar = document.createElement('div');
        bar.className = 'cl-issue-list__summary';
        bar.setAttribute('role', 'group');
        bar.style.cssText = SUMMARY_CSS;
        bar.addEventListener('click', this._onSummaryClick);

        this._summaryButtons = {};
        for (const key of ['all', ...SEVERITIES]) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = `cl-issue-list__count cl-issue-list__count--${key}`;
            button.dataset.severity = key;
            button.style.cssText = COUNT_CSS;

            if (key !== 'all') {
                const glyph = document.createElement('span');
                glyph.className = 'cl-issue-list__count-icon';
                glyph.setAttribute('aria-hidden', 'true');
                glyph.textContent = SEVERITY_GLYPH[key];
                glyph.style.cssText = `color:${SEVERITY_COLOR[key]};font-weight:700;`;
                button.appendChild(glyph);
            }
            const label = document.createElement('span');
            label.className = 'cl-issue-list__count-label';
            const value = document.createElement('span');
            value.className = 'cl-issue-list__count-value';
            value.style.cssText = 'font-weight:600;';
            button.append(label, value);
            bar.appendChild(button);
            this._summaryButtons[key] = { button, label, value };
        }
        return bar;
    }

    // ── 渲染 ──────────────────────────────────────────────

    _render({ keepFocus = true } = {}) {
        if (this._destroyed || !this.element) return;
        const focus = keepFocus ? this._captureFocus() : null;
        const filter = this._state.snapshot().filter;
        this._updateSummary(filter);
        this._renderBody(filter);
        this._restoreFocus(focus);
    }

    _counts() {
        const counts = { total: this._records.length, error: 0, warning: 0, info: 0 };
        for (const record of this._records) counts[record.severity] += 1;
        return counts;
    }

    _updateSummary(filter) {
        if (!this._summaryButtons) return;
        const counts = this._counts();
        this._summary.setAttribute('aria-label', Locale.t('issueList.summaryLabel'));
        for (const [key, parts] of Object.entries(this._summaryButtons)) {
            const label = key === 'all' ? Locale.t('issueList.all') : Locale.t(`issueList.severity.${key}`);
            const count = key === 'all' ? counts.total : counts[key];
            parts.label.textContent = label;
            parts.value.textContent = String(count);
            // 相鄰 span 的文字會被接成「錯誤2」，以明確名稱（含可見文字）取代
            parts.button.setAttribute('aria-label', Locale.t('issueList.countLabel', { label, count }));
            const pressed = key === 'all' ? filter === null : (filter !== null && filter.includes(key));
            parts.button.setAttribute('aria-pressed', String(pressed));
            parts.button.style.background = pressed ? 'var(--cl-primary-light)' : 'var(--cl-bg)';
            parts.button.style.borderColor = pressed ? 'var(--cl-primary)' : 'var(--cl-border)';
            parts.button.style.fontWeight = pressed ? '600' : '400';
        }
    }

    _visibleRecords(filter) {
        const records = filter ? this._records.filter((record) => filter.includes(record.severity)) : this._records;
        if (this.options.sort !== 'severity') return records.slice();
        // 只有三個等級：分桶即為 O(n) 的穩定排序
        const buckets = { error: [], warning: [], info: [] };
        for (const record of records) buckets[record.severity].push(record);
        return [...buckets.error, ...buckets.warning, ...buckets.info];
    }

    _renderBody(filter) {
        const body = this._body;
        const visible = this._visibleRecords(filter);
        const fragment = document.createDocumentFragment();
        this._entries = new Map();
        this._order = [];
        this._hoverRow = null;
        body.setAttribute('aria-label', this.options.ariaLabel || Locale.t('issueList.label'));

        if (visible.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'cl-issue-list__empty';
            empty.style.cssText = EMPTY_CSS;
            empty.textContent = this._records.length === 0
                ? (this.options.emptyText ?? Locale.t('issueList.empty'))
                : Locale.t('issueList.filteredEmpty');
            fragment.appendChild(empty);
        } else if (this.options.groupBy === 'severity') {
            const buckets = { error: [], warning: [], info: [] };
            for (const record of visible) buckets[record.severity].push(record);
            for (const severity of SEVERITIES) {
                if (buckets[severity].length === 0) continue;
                const group = document.createElement('div');
                group.className = `cl-issue-list__group cl-issue-list__group--${severity}`;
                group.dataset.severity = severity;
                group.style.cssText = GROUP_CSS;
                const heading = document.createElement('div');
                heading.className = 'cl-issue-list__group-heading';
                heading.id = `${this._uid}-group-${severity}`;
                heading.style.cssText = GROUP_HEADING_CSS;
                heading.textContent = Locale.t('issueList.groupHeading', {
                    label: Locale.t(`issueList.severity.${severity}`),
                    count: buckets[severity].length
                });
                const list = this._createList(buckets[severity]);
                list.setAttribute('aria-labelledby', heading.id);
                group.append(heading, list);
                fragment.appendChild(group);
            }
        } else {
            fragment.appendChild(this._createList(visible));
        }

        body.replaceChildren(fragment);

        // roving tabindex：保留原本的作用列，否則回到第一列
        const activeKey = this._entries.has(this._activeKey) ? this._activeKey : (this._order[0] ?? null);
        this._activeKey = null;
        if (activeKey) this._setActive(activeKey);
    }

    _createList(records) {
        const list = document.createElement('ul');
        list.className = 'cl-issue-list__items';
        list.style.cssText = LIST_CSS;
        for (const record of records) {
            const entry = this._createRow(record, this._order.length);
            this._entries.set(record.key, entry);
            this._order.push(record.key);
            list.appendChild(entry.li);
        }
        return list;
    }

    _createRow(record, index) {
        const { issue, severity, key } = record;
        const titleText = textOf(issue.title);

        const li = document.createElement('li');
        li.className = `cl-issue-list__item cl-issue-list__item--${severity}`;
        li.dataset.issueKey = key;
        li.dataset.severity = severity;
        li.style.cssText = ITEM_CSS;
        li.style.borderLeftColor = SEVERITY_COLOR[severity];

        const row = document.createElement('div');
        row.className = 'cl-issue-list__row';
        row.setAttribute('role', 'button');
        row.tabIndex = -1;
        row.style.cssText = ROW_CSS;

        const icon = document.createElement('span');
        icon.className = 'cl-issue-list__icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.style.cssText = ICON_CSS;
        icon.style.color = SEVERITY_COLOR[severity];
        icon.textContent = SEVERITY_GLYPH[severity];

        const badge = document.createElement('span');
        badge.className = 'cl-issue-list__severity';
        badge.style.cssText = BADGE_CSS;
        badge.style.background = SEVERITY_SOFT[severity];
        badge.textContent = Locale.t(`issueList.severity.${severity}`);

        const text = document.createElement('span');
        text.className = 'cl-issue-list__text';
        text.style.cssText = TEXT_CSS;
        const title = document.createElement('span');
        title.className = 'cl-issue-list__title';
        title.style.cssText = TITLE_CSS;
        title.textContent = titleText;
        text.appendChild(title);
        const messageText = textOf(issue.message);
        if (messageText) {
            const message = document.createElement('span');
            message.className = 'cl-issue-list__message';
            message.style.cssText = MESSAGE_CSS;
            message.textContent = messageText;
            text.appendChild(message);
        }

        row.append(icon, badge, text);
        const metaText = textOf(issue.meta);
        if (metaText) {
            const meta = document.createElement('span');
            meta.className = 'cl-issue-list__meta';
            meta.style.cssText = META_CSS;
            meta.textContent = metaText;
            row.appendChild(meta);
        }
        li.appendChild(row);

        let dismiss = null;
        if (this.options.dismissible) {
            dismiss = document.createElement('button');
            dismiss.type = 'button';
            dismiss.className = 'cl-issue-list__dismiss';
            dismiss.tabIndex = -1;
            const label = Locale.t('issueList.dismiss', { title: titleText });
            dismiss.setAttribute('aria-label', label);
            dismiss.title = label;
            dismiss.style.cssText = DISMISS_CSS;
            dismiss.textContent = '×';
            li.appendChild(dismiss);
        }

        return { li, row, dismiss, record, index };
    }

    // ── 焦點與互動 ────────────────────────────────────────

    _setActive(key) {
        if (key === this._activeKey) return;
        const previous = this._entries.get(this._activeKey);
        if (previous) {
            previous.row.tabIndex = -1;
            if (previous.dismiss) previous.dismiss.tabIndex = -1;
        }
        const next = this._entries.get(key);
        this._activeKey = next ? key : null;
        if (next) {
            next.row.tabIndex = 0;
            if (next.dismiss) next.dismiss.tabIndex = 0;
        }
    }

    _focusRowAt(index) {
        if (this._order.length === 0) return false;
        const clamped = Math.max(0, Math.min(index, this._order.length - 1));
        const key = this._order[clamped];
        this._setActive(key);
        this._entries.get(key).row.focus();
        return true;
    }

    _focusFallback() {
        const target = this._summaryButtons?.all?.button || this._body;
        target?.focus();
    }

    _captureFocus() {
        const active = this.element?.ownerDocument?.activeElement;
        if (!active || !this._body || !this._body.contains(active)) return null;
        const li = active.closest ? active.closest('.cl-issue-list__item') : null;
        const entry = li ? this._entries.get(li.dataset.issueKey) : null;
        if (!entry) return { key: null, part: 'row', index: 0 };
        return { key: entry.record.key, part: active === entry.dismiss ? 'dismiss' : 'row', index: entry.index };
    }

    _restoreFocus(focus) {
        if (!focus) return;
        const entry = focus.key ? this._entries.get(focus.key) : null;
        if (entry) {
            this._setActive(entry.record.key);
            (focus.part === 'dismiss' && entry.dismiss ? entry.dismiss : entry.row).focus();
            return;
        }
        if (!this._focusRowAt(focus.index)) this._focusFallback();
    }

    _entryFromEvent(event, selector) {
        const target = event.target;
        const element = target && target.closest ? target.closest(selector) : null;
        if (!element || !this._body.contains(element)) return null;
        const li = element.closest('.cl-issue-list__item');
        return li ? this._entries.get(li.dataset.issueKey) || null : null;
    }

    _handleKeyDown(event) {
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        const entry = this._entryFromEvent(event, '.cl-issue-list__row');
        if (!entry || event.target !== entry.row) return;
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                this._focusRowAt(entry.index + 1);
                break;
            case 'ArrowUp':
                event.preventDefault();
                this._focusRowAt(entry.index - 1);
                break;
            case 'Home':
                event.preventDefault();
                this._focusRowAt(0);
                break;
            case 'End':
                event.preventDefault();
                this._focusRowAt(this._order.length - 1);
                break;
            case 'Enter':
                event.preventDefault();
                if (!event.repeat) this._select(entry.record.key);
                break;
            case ' ':
            case 'Spacebar':
                // 與原生按鈕一致：keydown 只擋捲動，keyup 才觸發
                event.preventDefault();
                this._spaceKey = entry.record.key;
                break;
            case 'Delete':
            case 'Backspace':
                if (this.options.dismissible) {
                    event.preventDefault();
                    this._dismiss(entry.record.key);
                }
                break;
            default:
                break;
        }
    }

    _handleKeyUp(event) {
        if (event.key !== ' ' && event.key !== 'Spacebar') return;
        const entry = this._entryFromEvent(event, '.cl-issue-list__row');
        const armed = this._spaceKey;
        this._spaceKey = null;
        if (!entry || armed !== entry.record.key) return;
        event.preventDefault();
        this._select(entry.record.key);
    }

    _handleBodyClick(event) {
        const dismissEntry = this._entryFromEvent(event, '.cl-issue-list__dismiss');
        if (dismissEntry) {
            this._dismiss(dismissEntry.record.key);
            return;
        }
        const entry = this._entryFromEvent(event, '.cl-issue-list__row');
        if (entry) this._select(entry.record.key);
    }

    _handleSummaryClick(event) {
        const button = event.target && event.target.closest ? event.target.closest('.cl-issue-list__count') : null;
        if (!button || !this._summary || !this._summary.contains(button)) return;
        const severity = button.dataset.severity;
        if (severity === 'all') {
            this.setFilter(null);
            return;
        }
        const current = this._state.snapshot().filter;
        const onlyThis = current !== null && current.length === 1 && current[0] === severity;
        this.setFilter(onlyThis ? null : [severity]);
    }

    _handleFocusIn(event) {
        const entry = this._entryFromEvent(event, '.cl-issue-list__item');
        if (entry) this._setActive(entry.record.key);
    }

    _handleMouseOver(event) {
        const entry = this._entryFromEvent(event, '.cl-issue-list__row');
        this._setHoverRow(entry ? entry.row : null);
    }

    _setHoverRow(row) {
        if (row === this._hoverRow) return;
        if (this._hoverRow) this._hoverRow.style.background = 'transparent';
        this._hoverRow = row;
        if (row) row.style.background = 'var(--cl-bg-hover)';
    }

    _select(key) {
        const entry = this._entries.get(key);
        if (!entry) return;
        this._setActive(key);
        if (typeof this.options.onSelect === 'function') this.options.onSelect(entry.record.issue);
    }

    _dismiss(key) {
        const index = this._records.findIndex((record) => record.key === key);
        if (index < 0) return;
        const [record] = this._records.splice(index, 1);
        this._render();
        this._queueAnnouncement();
        if (typeof this.options.onDismiss === 'function') this.options.onDismiss(record.issue);
    }

    _handleLocaleChange() {
        if (this._destroyed) return;
        this._render();
        // 語言切換不是摘要變動：沒有待播報內容時，以新語言重設比較基準
        if (this._announceTimer === null) this._announcedSummary = this._summaryText();
    }

    // ── live region ─────────────────────────────────────

    _summaryText() {
        const counts = this._counts();
        return counts.total === 0
            ? Locale.t('issueList.announceEmpty')
            : Locale.t('issueList.announceSummary', counts);
    }

    _queueAnnouncement() {
        if (this._destroyed || this._announceTimer !== null) return;
        const delay = Math.max(0, Number(this.options.announceDelay) || 0);
        this._announceTimer = setTimeout(() => this._flushAnnouncement(), delay);
    }

    _flushAnnouncement() {
        this._announceTimer = null;
        if (this._destroyed || !this._live) return;
        const text = this._summaryText();
        if (text === this._announcedSummary) return;
        this._announcedSummary = text;
        this._live.textContent = text;
    }

    // ── 公開 API ─────────────────────────────────────────

    /** 取狀態機快照（lifecycle、filter） */
    snapshot() {
        return this._state.snapshot();
    }

    /** 以新陣列取代全部問題；摘要變動會節流播報 */
    setIssues(issues) {
        if (this._destroyed) return this;
        this._records = this._normalizeIssues(issues, this._records);
        this._render();
        this._queueAnnouncement();
        return this;
    }

    /** 新增一筆；id 已存在時就地取代該筆 */
    addIssue(issue) {
        if (this._destroyed || !issue || typeof issue !== 'object') return this;
        const existing = this._records.find((record) => sameId(record.issue.id, issue.id));
        if (existing) {
            existing.issue = issue;
            existing.severity = normalizeSeverity(issue.severity);
        } else {
            this._records.push(this._createRecord(issue));
        }
        this._render();
        this._queueAnnouncement();
        return this;
    }

    /** 移除指定 id 的問題（不觸發 onDismiss） */
    removeIssue(id) {
        if (this._destroyed) return this;
        const before = this._records.length;
        this._records = this._records.filter((record) => !sameId(record.issue.id, id));
        if (this._records.length !== before) {
            this._render();
            this._queueAnnouncement();
        }
        return this;
    }

    /** 清空全部問題 */
    clear() {
        return this.setIssues([]);
    }

    /** 目前的問題（輸入順序；回傳新陣列，元素為呼叫端原物件） */
    getIssues() {
        return this._records.map((record) => record.issue);
    }

    /** 設定篩選：null 或空陣列 = 全部 */
    setFilter(severities) {
        if (this._destroyed) return this;
        this._state.send('SET_FILTER', { filter: severities });
        this._render();
        return this;
    }

    /** 目前的篩選（null = 全部） */
    getFilter() {
        return this._state.snapshot().filter;
    }

    /** 聚焦第一個可見列；沒有列時不動作 */
    focusFirst() {
        if (!this._destroyed) this._focusRowAt(0);
        return this;
    }

    mount(container) {
        if (this._destroyed) return this;
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) {
            console.warn('[IssueList] mount target not found:', container);
            return this;
        }
        target.appendChild(this.element);
        if (!this._localeListening && typeof window !== 'undefined') {
            window.addEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = true;
        }
        this._state.send('MOUNT');
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        this._destroyed = true;
        this._state.send('DESTROY');
        if (this._announceTimer !== null) {
            clearTimeout(this._announceTimer);
            this._announceTimer = null;
        }
        if (this._localeListening) {
            window.removeEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = false;
        }
        if (this._body) {
            this._body.removeEventListener('keydown', this._onKeyDown);
            this._body.removeEventListener('keyup', this._onKeyUp);
            this._body.removeEventListener('click', this._onBodyClick);
            this._body.removeEventListener('focusin', this._onFocusIn);
            this._body.removeEventListener('mouseover', this._onMouseOver);
            this._body.removeEventListener('mouseleave', this._onMouseLeave);
        }
        this._summary?.removeEventListener('click', this._onSummaryClick);
        this.element?.remove();
        this._entries = new Map();
        this._order = [];
        this._records = [];
        this._hoverRow = null;
        this._activeKey = null;
    }
}

export default IssueList;
