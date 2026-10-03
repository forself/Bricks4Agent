/**
 * NotificationCenter — 通知中心（複合元件）
 *
 * 鈴鐺按鈕 + 未讀徽章 + 浮出面板（訊息清單）。與 common/Notification（一次性 Toast）不同：
 * 這裡保存一串通知、追蹤已讀狀態，使用者主動開啟面板瀏覽。
 *
 * - 觸發鈕：aria-haspopup="dialog"、aria-expanded、aria-controls；無障礙名稱含未讀數
 *   （Locale notificationCenter.triggerLabel 的 {count}）；徽章超過 99 顯示「99+」。
 * - 面板：role="dialog"（非強制回應）＋標題；開啟時焦點移入面板；Escape、點面板外、
 *   Tab 離開元件都會關閉；Escape 與「焦點遺失」的外部點擊會把焦點還給觸發鈕。
 * - 浮出方式比照 form/Dropdown 的 _portalMenu：面板留在元件內（維持 DOM 契約），開啟時改
 *   position:fixed 依觸發鈕定位（有 transform/filter 的上層會補償座標），空間不足時翻到上方；
 *   document／window 監聽只在開啟期間掛載。
 * - 項目有 href 時是連結（經 sanitizeUrl()，不安全的網址退回按鈕），否則是按鈕；上下鍵在項目間移動。
 * - 未讀數變動以 polite live region 播報（依 announceDelay 節流）。
 * - CSP：createElement + textContent，樣式走 CSSOM；鈴鐺為 common/Icon（Canvas）。
 *
 * @example
 * const center = new NotificationCenter({
 *     items: [{ id: 'n1', title: '會議室預約已確認', message: 'A 室 週三 10:00', href: '#/rooms/a' }],
 *     onMarkRead: (ids) => api.markRead(ids)
 * }).mount('#header-actions');
 * center.add({ id: 'n2', title: '新的任務指派', variant: 'warning' });
 */
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';
import { sanitizeUrl } from '../../utils/security.js';
import { nextUid } from '../../utils/uid.js';
import { Icon } from '../Icon/index.js';
import './locale.js';

// Material Design「notifications」圖示 path（Apache-2.0，同 Icon.js 內建圖示來源），以 Canvas Path2D 繪製
const BELL_PATH = 'M12 22c1.1 0 2-.9 2-2h-4c0 1.1.89 2 2 2zm6-6v-5c0-3.07-1.64-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.63 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z';

const VARIANTS = Object.freeze(['info', 'success', 'warning', 'danger']);
const VARIANT_GLYPH = { info: 'ℹ︎', success: '✓', warning: '⚠︎', danger: '✕' };
const VARIANT_COLOR = { info: 'var(--cl-info)', success: 'var(--cl-success)', warning: 'var(--cl-warning-dark)', danger: 'var(--cl-danger)' };
const BADGE_CAP = 99;
const PANEL_MAX_HEIGHT = 480;
const PANEL_MIN_HEIGHT = 160;
const VIEWPORT_EDGE = 8;
const PANEL_GAP = 6;

const SR_ONLY_CSS = 'position:absolute;width:1px;height:1px;margin:-1px;padding:0;border:0;overflow:hidden;clip:rect(0 0 0 0);clip-path:inset(50%);white-space:nowrap;';
const ROOT_CSS = 'position:relative;display:inline-block;font-family:var(--cl-font-family);color:var(--cl-text);';
const TRIGGER_CSS = 'position:relative;display:inline-flex;align-items:center;justify-content:center;box-sizing:border-box;width:40px;height:40px;padding:0;border:1px solid transparent;border-radius:var(--cl-radius-round);background:transparent;color:var(--cl-text);cursor:pointer;';
const BADGE_CSS = 'position:absolute;top:0;right:0;display:none;align-items:center;justify-content:center;box-sizing:border-box;min-width:18px;height:18px;padding:0 5px;border-radius:var(--cl-radius-pill);background:var(--cl-danger);color:var(--cl-text-inverse);font-size:var(--cl-font-size-2xs);font-weight:700;line-height:1;pointer-events:none;';
const PANEL_CSS = 'position:absolute;top:100%;right:0;left:auto;z-index:1000;display:none;flex-direction:column;box-sizing:border-box;width:360px;max-width:calc(100vw - 16px);margin-top:6px;background:var(--cl-bg);color:var(--cl-text);border:1px solid var(--cl-border);border-radius:var(--cl-radius-lg);box-shadow:var(--cl-shadow-lg);overflow:hidden;text-align:left;';
const HEADER_CSS = 'flex:0 0 auto;display:flex;align-items:center;justify-content:space-between;gap:8px;padding:10px 12px;border-bottom:1px solid var(--cl-border-light);';
const HEADING_CSS = 'margin:0;font-size:var(--cl-font-size-lg);font-weight:600;color:var(--cl-text);';
const TEXT_BUTTON_CSS = 'padding:2px 6px;border:0;background:transparent;font:inherit;font-size:var(--cl-font-size-sm);border-radius:var(--cl-radius-sm);';
const LIST_HOST_CSS = 'flex:1 1 auto;min-height:0;overflow-y:auto;';
const LIST_CSS = 'list-style:none;margin:0;padding:0;';
const ITEM_CSS = 'margin:0;padding:0;border-bottom:1px solid var(--cl-border-light);';
const CONTROL_CSS = 'display:flex;align-items:flex-start;gap:10px;width:100%;box-sizing:border-box;margin:0;padding:10px 12px;border:0;background:transparent;color:inherit;font:inherit;text-align:left;text-decoration:none;cursor:pointer;';
const ICON_CSS = 'flex:0 0 auto;width:18px;text-align:center;font-weight:700;line-height:1.5;font-size:var(--cl-font-size-md);';
const BODY_CSS = 'flex:1 1 auto;min-width:0;display:flex;flex-direction:column;gap:2px;';
const TITLE_CSS = 'font-size:var(--cl-font-size-md);color:var(--cl-text);overflow-wrap:anywhere;';
const MESSAGE_CSS = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);white-space:pre-line;overflow-wrap:anywhere;';
const TIME_CSS = 'font-size:var(--cl-font-size-xs);color:var(--cl-text-muted);';
const DOT_CSS = 'flex:0 0 auto;width:8px;height:8px;margin-top:6px;border-radius:var(--cl-radius-round);background:var(--cl-primary);';
const EMPTY_CSS = 'margin:0;padding:24px 12px;text-align:center;color:var(--cl-text-muted);font-size:var(--cl-font-size-md);';
const FOOTER_CSS = 'flex:0 0 auto;display:none;justify-content:center;padding:8px 12px;border-top:1px solid var(--cl-border-light);';
const LOAD_MORE_CSS = 'padding:4px 12px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg);color:var(--cl-text);font:inherit;font-size:var(--cl-font-size-sm);cursor:pointer;';

const timeFormatterCache = new Map();

function defaultTimeFormatter(lang) {
    if (!timeFormatterCache.has(lang)) {
        const style = { dateStyle: 'medium', timeStyle: 'short' };
        let formatter;
        try {
            formatter = new Intl.DateTimeFormat(lang, style);
        } catch {
            formatter = new Intl.DateTimeFormat(undefined, style);
        }
        timeFormatterCache.set(lang, formatter);
    }
    return timeFormatterCache.get(lang);
}

function toDate(value) {
    if (value instanceof Date) return Number.isNaN(value.getTime()) ? null : value;
    if (typeof value === 'number' || (typeof value === 'string' && value.trim() !== '')) {
        const date = new Date(value);
        return Number.isNaN(date.getTime()) ? null : date;
    }
    return null;
}

function idKeyOf(id) {
    return id === null || id === undefined ? null : String(id);
}

function textOf(value) {
    return value === null || value === undefined ? '' : String(value);
}

function normalizeVariant(value) {
    return VARIANTS.includes(value) ? value : 'info';
}

export class NotificationCenter {
    static VARIANTS = VARIANTS;

    constructor(options = {}) {
        this.options = {
            items: [],               // 通知（新到舊）：[{ id, title, message?, time?, read?: false, variant?: 'info'|'success'|'warning'|'danger', href? }]
            maxItems: 100,           // 記憶體中最多保留幾則，超過時丟棄最舊的；0 或非有限數＝不限
            emptyText: null,         // 沒有通知時的文字；null 使用 Locale notificationCenter.empty
            markReadOnOpen: false,   // 開啟面板時把目前未讀全部標為已讀（觸發 onMarkRead）
            markReadOnClick: true,   // 點選項目時標為已讀（觸發 onMarkRead）
            onItemClick: null,       // (item, event) => void；item 為呼叫端原物件
            onMarkRead: null,        // (ids) => void；只在使用者操作造成已讀變化時呼叫
            onLoadMore: null,        // async () => items[] | { items, hasMore } | void；提供且 hasMore 時顯示「載入更多」
            hasMore: false,          // 是否還有更舊的通知
            ariaLabel: null,         // 觸發鈕基本名稱與面板標題；null 使用 Locale notificationCenter.label
            formatTime: null,        // (date: Date, item) => string；null 使用 Intl.DateTimeFormat（Locale 語言）
            announceDelay: 1000,     // 未讀數播報節流間隔（ms）
            ...options
        };

        this.element = null;
        this._destroyed = false;
        this._localeListening = false;
        this._globalListenersAttached = false;
        this._keySeq = 0;
        this._records = [];          // [{ key, idKey, item, read }]，新到舊
        this._controls = new Map();  // key → 項目控制元件（a 或 button）
        this._order = [];            // 目前面板內項目的 key（DOM 順序）
        this._listDirty = true;
        this._hoverControl = null;
        this._announceTimer = null;
        this._uid = nextUid('notification-center');

        this._state = createComponentState(
            { lifecycle: 'created', open: false, hasMore: !!this.options.hasMore, loading: false },
            {
                MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
                OPEN: (state) => (state.lifecycle === 'destroyed' ? state : { ...state, open: true }),
                CLOSE: (state) => ({ ...state, open: false }),
                SET_HAS_MORE: (state, payload) => ({ ...state, hasMore: !!payload?.hasMore }),
                LOAD_START: (state) => ({ ...state, loading: true }),
                LOAD_END: (state, payload) => ({
                    ...state,
                    loading: false,
                    hasMore: payload && typeof payload.hasMore === 'boolean' ? payload.hasMore : state.hasMore
                }),
                DESTROY: (state) => ({ ...state, lifecycle: 'destroyed', open: false, loading: false })
            }
        );

        this._onTriggerClick = () => this._toggle();
        this._onTriggerEnter = () => { this._trigger.style.background = 'var(--cl-bg-hover)'; };
        this._onTriggerLeave = () => { this._trigger.style.background = 'transparent'; };
        this._onKeyDown = (event) => this._handleKeyDown(event);
        this._onFocusOut = (event) => this._handleFocusOut(event);
        this._onPanelClick = (event) => this._handlePanelClick(event);
        this._onMouseOver = (event) => this._handleMouseOver(event);
        this._onMouseLeave = () => this._setHoverControl(null);
        this._onDocumentClick = (event) => this._handleDocumentClick(event);
        this._onViewportChange = (event) => this._handleViewportChange(event);
        this._onLocaleChange = () => this._handleLocaleChange();

        this._records = this._buildRecords(this.options.items, []);
        this._trim();
        this.element = this._createElement();
        this._updateTrigger();
        this._updateChrome();
        this._applyState();
        // 初始未讀數只作為比較基準，不播報
        this._announcedUnread = this.getUnreadCount();
    }

    // ── 資料 ──────────────────────────────────────────────

    _newKey() {
        this._keySeq += 1;
        return `n${this._keySeq}`;
    }

    /**
     * 正規化並去重（同 id 保留第一筆）；同 id 沿用舊紀錄的 key。
     * read：項目明確給 boolean 時採用，否則沿用舊紀錄，再否則為未讀。
     */
    _buildRecords(items, previous) {
        const list = Array.isArray(items) ? items : [items];
        const previousById = new Map();
        for (const record of previous) {
            if (record.idKey !== null && !previousById.has(record.idKey)) previousById.set(record.idKey, record);
        }
        const seen = new Set();
        const records = [];
        for (const item of list) {
            if (!item || typeof item !== 'object') continue;
            const idKey = idKeyOf(item.id);
            if (idKey !== null) {
                if (seen.has(idKey)) continue;
                seen.add(idKey);
            }
            const prior = idKey !== null ? previousById.get(idKey) : undefined;
            records.push({
                key: prior ? prior.key : this._newKey(),
                idKey,
                item,
                read: typeof item.read === 'boolean' ? item.read : (prior ? prior.read : false)
            });
        }
        return records;
    }

    _maxItems() {
        const max = Number(this.options.maxItems);
        return Number.isFinite(max) && max > 0 ? Math.floor(max) : Infinity;
    }

    _trim() {
        const max = this._maxItems();
        if (this._records.length > max) this._records.length = max;
    }

    _unreadCount() {
        let count = 0;
        for (const record of this._records) if (!record.read) count += 1;
        return count;
    }

    _afterDataChange() {
        this._updateTrigger();
        this._updateChrome();
        if (this._isOpen()) {
            this._renderList();
            this._positionPanel();
        } else {
            this._listDirty = true;
        }
        this._queueAnnouncement();
    }

    /**
     * 把指定紀錄標為已讀；回傳實際變動的原始 id。
     * 已讀只改外觀，所以就地更新既有項目節點而不重繪清單：點擊連結當下若重繪，
     * 被點的 <a> 會脫離文件，瀏覽器就不會導覽（HTML 規範：未連線的 a 無法導覽）。
     */
    _markRecordsRead(records, emit) {
        const changed = [];
        const touched = [];
        for (const record of records) {
            if (record.read) continue;
            record.read = true;
            touched.push(record);
            if (record.idKey !== null) changed.push(record.item.id);
        }
        if (touched.length > 0) {
            for (const record of touched) this._applyReadState(record);
            this._updateTrigger();
            this._updateChrome();
            this._queueAnnouncement();
        }
        if (emit && changed.length > 0 && typeof this.options.onMarkRead === 'function') {
            this.options.onMarkRead(changed);
        }
        return changed;
    }

    _applyReadState(record) {
        const control = this._controls.get(record.key);
        if (!control) return;
        if (control.parentElement) control.parentElement.dataset.read = String(record.read);
        if (control !== this._hoverControl) {
            control.style.background = record.read ? 'transparent' : 'var(--cl-primary-soft-subtle)';
        }
        const title = control.querySelector('.cl-notification-center__item-title');
        if (title) title.style.fontWeight = record.read ? '400' : '600';
        if (record.read) {
            control.querySelector('.cl-notification-center__item-dot')?.remove();
            control.querySelector('.cl-notification-center__item-unread')?.remove();
        }
    }

    // ── 建構 ──────────────────────────────────────────────

    _label() {
        return this.options.ariaLabel || Locale.t('notificationCenter.label');
    }

    _createElement() {
        const root = document.createElement('div');
        root.className = 'cl-notification-center';
        root.style.cssText = ROOT_CSS;

        const trigger = document.createElement('button');
        trigger.type = 'button';
        trigger.className = 'cl-notification-center__trigger';
        trigger.setAttribute('aria-haspopup', 'dialog');
        trigger.setAttribute('aria-expanded', 'false');
        trigger.setAttribute('aria-controls', `${this._uid}-panel`);
        trigger.style.cssText = TRIGGER_CSS;
        const bell = document.createElement('span');
        bell.className = 'cl-notification-center__bell';
        bell.setAttribute('aria-hidden', 'true');
        bell.style.cssText = 'display:inline-flex;';
        this._bellIcon = new Icon({ name: 'notifications', pathData: BELL_PATH, size: 20, color: 'currentColor' });
        this._bellIcon.mount(bell);
        const badge = document.createElement('span');
        badge.className = 'cl-notification-center__badge';
        badge.setAttribute('aria-hidden', 'true');
        badge.style.cssText = BADGE_CSS;
        trigger.append(bell, badge);
        trigger.addEventListener('click', this._onTriggerClick);
        trigger.addEventListener('mouseenter', this._onTriggerEnter);
        trigger.addEventListener('mouseleave', this._onTriggerLeave);
        root.appendChild(trigger);
        this._trigger = trigger;
        this._badge = badge;

        const panel = document.createElement('div');
        panel.className = 'cl-notification-center__panel';
        panel.id = `${this._uid}-panel`;
        panel.setAttribute('role', 'dialog');
        panel.setAttribute('aria-modal', 'false');
        panel.setAttribute('aria-labelledby', `${this._uid}-heading`);
        panel.tabIndex = -1;
        panel.hidden = true;
        panel.style.cssText = PANEL_CSS;
        panel.style.maxHeight = `${PANEL_MAX_HEIGHT}px`;

        const header = document.createElement('div');
        header.className = 'cl-notification-center__header';
        header.style.cssText = HEADER_CSS;
        const heading = document.createElement('h2');
        heading.className = 'cl-notification-center__heading';
        heading.id = `${this._uid}-heading`;
        heading.style.cssText = HEADING_CSS;
        const markAll = document.createElement('button');
        markAll.type = 'button';
        markAll.className = 'cl-notification-center__mark-all';
        markAll.style.cssText = TEXT_BUTTON_CSS;
        header.append(heading, markAll);

        const listHost = document.createElement('div');
        listHost.className = 'cl-notification-center__list-host';
        listHost.style.cssText = LIST_HOST_CSS;
        listHost.addEventListener('mouseover', this._onMouseOver);
        listHost.addEventListener('mouseleave', this._onMouseLeave);

        const footer = document.createElement('div');
        footer.className = 'cl-notification-center__footer';
        footer.style.cssText = FOOTER_CSS;
        const loadMore = document.createElement('button');
        loadMore.type = 'button';
        loadMore.className = 'cl-notification-center__load-more';
        loadMore.style.cssText = LOAD_MORE_CSS;
        footer.appendChild(loadMore);

        panel.append(header, listHost, footer);
        panel.addEventListener('click', this._onPanelClick);
        root.appendChild(panel);
        this._panel = panel;
        this._heading = heading;
        this._markAllButton = markAll;
        this._listHost = listHost;
        this._footer = footer;
        this._loadMoreButton = loadMore;

        const live = document.createElement('div');
        live.className = 'cl-notification-center__live';
        live.setAttribute('role', 'status');
        live.setAttribute('aria-live', 'polite');
        live.setAttribute('aria-atomic', 'true');
        live.style.cssText = SR_ONLY_CSS;
        root.appendChild(live);
        this._live = live;

        root.addEventListener('keydown', this._onKeyDown);
        root.addEventListener('focusout', this._onFocusOut);
        return root;
    }

    // ── 狀態同步 ──────────────────────────────────────────

    _isOpen() {
        const state = this._state.snapshot();
        return state.open && state.lifecycle !== 'destroyed';
    }

    _applyState() {
        if (!this.element) return;
        const state = this._state.snapshot();
        const open = state.open && state.lifecycle !== 'destroyed';
        this._trigger.setAttribute('aria-expanded', String(open));
        this.element.classList.toggle('cl-notification-center--open', open);
        this._panel.hidden = !open;
        this._panel.style.display = open ? 'flex' : 'none';
        this._syncGlobalListeners(open);
        this._updateChrome(state);
        if (open) {
            if (this._listDirty) this._renderList();
            this._positionPanel();
        } else {
            this._resetPanelPosition();
        }
    }

    // document／window 監聽只在面板開啟期間掛載；外部點擊用 capture，避免被 stopPropagation 吃掉
    _syncGlobalListeners(open) {
        if (open === this._globalListenersAttached) return;
        this._globalListenersAttached = open;
        if (open) {
            document.addEventListener('click', this._onDocumentClick, true);
            window.addEventListener('resize', this._onViewportChange);
            window.addEventListener('scroll', this._onViewportChange, true);
        } else {
            document.removeEventListener('click', this._onDocumentClick, true);
            window.removeEventListener('resize', this._onViewportChange);
            window.removeEventListener('scroll', this._onViewportChange, true);
        }
    }

    _updateTrigger() {
        if (!this._trigger) return;
        const count = this._unreadCount();
        this._badge.textContent = count > BADGE_CAP ? `${BADGE_CAP}+` : String(count);
        this._badge.style.display = count > 0 ? 'inline-flex' : 'none';
        const label = this._label();
        this._trigger.setAttribute('aria-label', count > 0
            ? Locale.t('notificationCenter.triggerLabel', { label, count })
            : label);
        this._trigger.dataset.unread = String(count);
    }

    /** 面板標題、「全部標為已讀」、「載入更多」等非清單部分 */
    _updateChrome(state = this._state.snapshot()) {
        if (!this._panel) return;
        this._heading.textContent = this._label();

        const hasUnread = this._unreadCount() > 0;
        this._markAllButton.textContent = Locale.t('notificationCenter.markAllRead');
        this._markAllButton.setAttribute('aria-disabled', String(!hasUnread));
        this._markAllButton.style.color = hasUnread ? 'var(--cl-primary)' : 'var(--cl-text-muted)';
        this._markAllButton.style.cursor = hasUnread ? 'pointer' : 'default';

        const canLoad = typeof this.options.onLoadMore === 'function'
            && state.hasMore
            && this._records.length < this._maxItems();
        this._footer.style.display = canLoad ? 'flex' : 'none';
        this._loadMoreButton.textContent = state.loading
            ? Locale.t('notificationCenter.loading')
            : Locale.t('notificationCenter.loadMore');
        this._loadMoreButton.setAttribute('aria-disabled', String(!!state.loading));
        this._listHost.setAttribute('aria-busy', String(!!state.loading));
    }

    // ── 清單渲染 ──────────────────────────────────────────

    _renderList() {
        if (!this._listHost) return;
        this._listDirty = false;
        const focus = this._captureItemFocus();
        this._controls = new Map();
        this._order = [];
        this._hoverControl = null;

        if (this._records.length === 0) {
            const empty = document.createElement('p');
            empty.className = 'cl-notification-center__empty';
            empty.style.cssText = EMPTY_CSS;
            empty.textContent = this.options.emptyText ?? Locale.t('notificationCenter.empty');
            this._listHost.replaceChildren(empty);
        } else {
            const list = document.createElement('ul');
            list.className = 'cl-notification-center__list';
            list.setAttribute('aria-labelledby', this._heading.id);
            list.style.cssText = LIST_CSS;
            const formatTime = this._resolveFormatter();
            for (const record of this._records) list.appendChild(this._createItem(record, formatTime));
            this._listHost.replaceChildren(list);
        }
        this._restoreItemFocus(focus);
    }

    _resolveFormatter() {
        if (typeof this.options.formatTime === 'function') return this.options.formatTime;
        const formatter = defaultTimeFormatter(Locale.getLang());
        return (date) => formatter.format(date);
    }

    _createItem(record, formatTime) {
        const { item, read, key } = record;
        const variant = normalizeVariant(item.variant);

        const entry = document.createElement('li');
        entry.className = `cl-notification-center__item cl-notification-center__item--${variant}`;
        entry.dataset.key = key;
        entry.dataset.read = String(read);
        entry.dataset.variant = variant;
        if (record.idKey !== null) entry.dataset.id = record.idKey;
        entry.style.cssText = ITEM_CSS;

        const href = sanitizeUrl(item.href);
        let control;
        if (href) {
            control = document.createElement('a');
            control.href = href;
        } else {
            control = document.createElement('button');
            control.type = 'button';
        }
        control.className = 'cl-notification-center__item-control';
        control.dataset.key = key;
        control.style.cssText = CONTROL_CSS;
        control.style.background = read ? 'transparent' : 'var(--cl-primary-soft-subtle)';

        const icon = document.createElement('span');
        icon.className = 'cl-notification-center__item-icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.style.cssText = ICON_CSS;
        icon.style.color = VARIANT_COLOR[variant];
        icon.textContent = VARIANT_GLYPH[variant];

        const body = document.createElement('span');
        body.className = 'cl-notification-center__item-body';
        body.style.cssText = BODY_CSS;
        if (variant !== 'info') {
            const variantLabel = document.createElement('span');
            variantLabel.className = 'cl-notification-center__item-variant';
            variantLabel.style.cssText = SR_ONLY_CSS;
            variantLabel.textContent = Locale.t(`notificationCenter.variant.${variant}`);
            body.appendChild(variantLabel);
        }
        const title = document.createElement('span');
        title.className = 'cl-notification-center__item-title';
        title.style.cssText = TITLE_CSS;
        title.style.fontWeight = read ? '400' : '600';
        title.textContent = textOf(item.title);
        body.appendChild(title);
        const messageText = textOf(item.message);
        if (messageText) {
            const message = document.createElement('span');
            message.className = 'cl-notification-center__item-message';
            message.style.cssText = MESSAGE_CSS;
            message.textContent = messageText;
            body.appendChild(message);
        }
        const timeElement = this._createTime(item, formatTime);
        if (timeElement) body.appendChild(timeElement);

        control.append(icon, body);
        if (!read) {
            const dot = document.createElement('span');
            dot.className = 'cl-notification-center__item-dot';
            dot.setAttribute('aria-hidden', 'true');
            dot.style.cssText = DOT_CSS;
            const unread = document.createElement('span');
            unread.className = 'cl-notification-center__item-unread';
            unread.style.cssText = SR_ONLY_CSS;
            unread.textContent = Locale.t('notificationCenter.unread');
            control.append(dot, unread);
        }

        entry.appendChild(control);
        this._controls.set(key, control);
        this._order.push(key);
        return entry;
    }

    _createTime(item, formatTime) {
        if (item.time === null || item.time === undefined || item.time === '') return null;
        const time = document.createElement('time');
        time.className = 'cl-notification-center__item-time';
        time.style.cssText = TIME_CSS;
        const date = toDate(item.time);
        if (!date) {
            time.textContent = String(item.time);
            return time;
        }
        time.setAttribute('datetime', date.toISOString());
        time.textContent = textOf(formatTime(date, item));
        return time;
    }

    _captureItemFocus() {
        const active = this.element?.ownerDocument?.activeElement;
        if (!active || !this._listHost || !this._listHost.contains(active)) return null;
        const key = active.dataset?.key;
        return { key: key || null, index: key ? this._order.indexOf(key) : 0 };
    }

    _restoreItemFocus(focus) {
        if (!focus || !this._isOpen()) return;
        const control = focus.key ? this._controls.get(focus.key) : null;
        if (control) {
            control.focus();
            return;
        }
        if (!this._focusItemAt(Math.max(0, focus.index))) this._panel.focus();
    }

    _focusItemAt(index) {
        if (this._order.length === 0) return false;
        const clamped = Math.max(0, Math.min(index, this._order.length - 1));
        this._controls.get(this._order[clamped])?.focus();
        return true;
    }

    // ── 浮出定位（比照 form/Dropdown 的 _portalMenu／_positionMenu） ──

    _fixedContainingBlockOffset() {
        let ancestor = this.element?.parentElement;
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

    _positionPanel() {
        const panel = this._panel;
        if (!panel || !this._isOpen()) return;
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0;
        const viewportWidth = window.innerWidth || document.documentElement.clientWidth || 0;
        const viewportBottom = Math.max(VIEWPORT_EDGE, viewportHeight - VIEWPORT_EDGE);
        const viewportRight = Math.max(0, viewportWidth - VIEWPORT_EDGE);
        const anchor = this._trigger.getBoundingClientRect();

        panel.dataset.floating = 'fixed';
        panel.style.position = 'fixed';
        panel.style.right = 'auto';
        panel.style.bottom = 'auto';
        panel.style.marginTop = '0';
        panel.style.zIndex = '10050';
        panel.style.maxHeight = `${PANEL_MAX_HEIGHT}px`;

        const rect = panel.getBoundingClientRect();
        const naturalHeight = rect.height || panel.scrollHeight || 0;
        const width = rect.width || panel.offsetWidth || 0;
        const spaceBelow = viewportBottom - anchor.bottom - PANEL_GAP;
        const spaceAbove = anchor.top - VIEWPORT_EDGE - PANEL_GAP;
        const placeAbove = naturalHeight > spaceBelow && spaceAbove > spaceBelow;
        const maxHeight = Math.min(PANEL_MAX_HEIGHT, Math.max(PANEL_MIN_HEIGHT, Math.floor(placeAbove ? spaceAbove : spaceBelow)));
        panel.style.maxHeight = `${maxHeight}px`;
        const height = Math.min(naturalHeight, maxHeight);
        const top = placeAbove ? Math.max(VIEWPORT_EDGE, anchor.top - PANEL_GAP - height) : anchor.bottom + PANEL_GAP;
        // 面板右緣對齊觸發鈕右緣（鈴鐺多在頁首右側），再夾在視窗內
        const left = Math.max(VIEWPORT_EDGE, Math.min(anchor.right - width, viewportRight - width));
        const offset = this._fixedContainingBlockOffset();
        panel.style.top = `${Math.round(top - offset.top)}px`;
        panel.style.left = `${Math.round(left - offset.left)}px`;
        panel.dataset.placement = placeAbove ? 'top' : 'bottom';
    }

    _resetPanelPosition() {
        const panel = this._panel;
        if (!panel) return;
        delete panel.dataset.floating;
        delete panel.dataset.placement;
        panel.style.position = 'absolute';
        panel.style.top = '100%';
        panel.style.right = '0';
        panel.style.left = 'auto';
        panel.style.bottom = 'auto';
        panel.style.marginTop = '6px';
        panel.style.zIndex = '1000';
        panel.style.maxHeight = `${PANEL_MAX_HEIGHT}px`;
    }

    // ── 開關與焦點 ────────────────────────────────────────

    _toggle() {
        if (this._isOpen()) this._close({ restoreFocus: true });
        else this.open();
    }

    /**
     * restoreFocus：true 一律還給觸發鈕；false 不動；'auto' 只在焦點原本在元件內或已遺失
     * （落在 body）時還給觸發鈕，不搶走使用者剛點到的其他控制項。
     */
    _close({ restoreFocus = 'auto' } = {}) {
        if (!this._isOpen()) return this;
        const doc = this.element.ownerDocument;
        const active = doc.activeElement;
        const focusInside = !!active && this.element.contains(active) && active !== this._trigger;
        const focusLost = !active || active === doc.body || active === doc.documentElement;
        this._state.send('CLOSE');
        this._applyState();
        this._setHoverControl(null);
        if (restoreFocus === true || (restoreFocus === 'auto' && (focusInside || focusLost))) {
            this._trigger.focus({ preventScroll: true });
        }
        return this;
    }

    _handleDocumentClick(event) {
        if (!this._isOpen()) return;
        if (event.target instanceof Node && this.element.contains(event.target)) return;
        this._close({ restoreFocus: 'auto' });
    }

    _handleViewportChange(event) {
        if (!this._isOpen()) return;
        // 面板自己的清單捲動不影響定位
        if (event && event.type === 'scroll' && event.target instanceof Node && this._panel.contains(event.target)) return;
        this._positionPanel();
    }

    _handleFocusOut(event) {
        if (!this._isOpen()) return;
        const next = event.relatedTarget;
        // relatedTarget 為 null（點到不可聚焦處）交給外部點擊處理
        if (next instanceof Node && !this.element.contains(next)) this._close({ restoreFocus: false });
    }

    _handleKeyDown(event) {
        if (!this._isOpen()) return;
        if (event.key === 'Escape' || event.key === 'Esc') {
            event.preventDefault();
            // 只消化這一層：避免外層對話框同時被 Escape 關閉
            event.stopPropagation();
            this._close({ restoreFocus: true });
            return;
        }
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        const target = event.target;
        const onItem = target instanceof Element && target.classList.contains('cl-notification-center__item-control');
        if (!onItem && target !== this._panel) return;
        const index = onItem ? this._order.indexOf(target.dataset.key) : -1;
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault();
                this._focusItemAt(index + 1);
                break;
            case 'ArrowUp':
                event.preventDefault();
                this._focusItemAt(index <= 0 ? 0 : index - 1);
                break;
            case 'Home':
                event.preventDefault();
                this._focusItemAt(0);
                break;
            case 'End':
                event.preventDefault();
                this._focusItemAt(this._order.length - 1);
                break;
            default:
                break;
        }
    }

    _handlePanelClick(event) {
        const target = event.target instanceof Element ? event.target : null;
        if (!target) return;
        if (target.closest('.cl-notification-center__mark-all')) {
            this._markRecordsRead(this._records, true);
            return;
        }
        if (target.closest('.cl-notification-center__load-more')) {
            this._loadMore();
            return;
        }
        const control = target.closest('.cl-notification-center__item-control');
        if (control && this._panel.contains(control)) this._activateItem(control.dataset.key, event);
    }

    _activateItem(key, event) {
        const record = this._records.find((entry) => entry.key === key);
        if (!record) return;
        if (this.options.markReadOnClick && !record.read) this._markRecordsRead([record], true);
        // 以修飾鍵或中鍵開新分頁時保持面板開啟，方便連續處理
        const modified = !!event && (event.ctrlKey || event.metaKey || event.shiftKey || event.button === 1);
        if (!modified) this._close({ restoreFocus: 'auto' });
        if (typeof this.options.onItemClick === 'function') this.options.onItemClick(record.item, event);
    }

    _handleMouseOver(event) {
        const target = event.target instanceof Element ? event.target : null;
        const control = target ? target.closest('.cl-notification-center__item-control') : null;
        this._setHoverControl(control && this._listHost.contains(control) ? control : null);
    }

    _setHoverControl(control) {
        if (control === this._hoverControl) return;
        const previous = this._hoverControl;
        if (previous) {
            const record = this._records.find((entry) => entry.key === previous.dataset.key);
            previous.style.background = record && !record.read ? 'var(--cl-primary-soft-subtle)' : 'transparent';
        }
        this._hoverControl = control;
        if (control) control.style.background = 'var(--cl-bg-hover)';
    }

    // ── 載入更多 ──────────────────────────────────────────

    async _loadMore() {
        const state = this._state.snapshot();
        if (this._destroyed || state.loading || !state.hasMore || typeof this.options.onLoadMore !== 'function') return;
        const hadFocus = this.element.ownerDocument.activeElement === this._loadMoreButton;
        const before = this._records.length;
        this._state.send('LOAD_START');
        this._updateChrome();

        let result;
        let failed = false;
        try {
            result = await this.options.onLoadMore();
        } catch (error) {
            failed = true;
            console.error('[NotificationCenter] onLoadMore failed:', error);
        }
        if (this._destroyed) return;

        let hasMore;
        if (!failed && result && typeof result === 'object') {
            const items = Array.isArray(result) ? result : (Array.isArray(result.items) ? result.items : null);
            if (!Array.isArray(result) && typeof result.hasMore === 'boolean') hasMore = result.hasMore;
            if (items) this._appendOlder(items);
        }
        this._state.send('LOAD_END', hasMore === undefined ? {} : { hasMore });
        this._updateTrigger();
        this._updateChrome();
        if (this._isOpen()) {
            this._renderList();
            this._positionPanel();
            // 「載入更多」消失時，焦點移到第一則新載入的通知
            if (hadFocus && this._footer.style.display === 'none' && !this._focusItemAt(before)) this._panel.focus();
        } else {
            this._listDirty = true;
        }
        if (failed) this._live.textContent = Locale.t('notificationCenter.loadFailed');
        else this._queueAnnouncement();
    }

    /** 較舊的通知接在清單尾端；已存在的 id 不重複加入 */
    _appendOlder(items) {
        const existing = new Set(this._records.map((record) => record.idKey).filter((idKey) => idKey !== null));
        const incoming = this._buildRecords(items, []).filter((record) => record.idKey === null || !existing.has(record.idKey));
        this._records = [...this._records, ...incoming];
        this._trim();
    }

    // ── live region ─────────────────────────────────────

    _queueAnnouncement() {
        if (this._destroyed || this._announceTimer !== null) return;
        const delay = Math.max(0, Number(this.options.announceDelay) || 0);
        this._announceTimer = setTimeout(() => this._flushAnnouncement(), delay);
    }

    _flushAnnouncement() {
        this._announceTimer = null;
        if (this._destroyed || !this._live) return;
        const count = this._unreadCount();
        if (count === this._announcedUnread) return;
        this._announcedUnread = count;
        this._live.textContent = count > 0
            ? Locale.t('notificationCenter.unreadAnnouncement', { count })
            : Locale.t('notificationCenter.noUnread');
    }

    _handleLocaleChange() {
        if (this._destroyed) return;
        this._updateTrigger();
        this._updateChrome();
        if (this._isOpen()) this._renderList();
        else this._listDirty = true;
    }

    // ── 公開 API ─────────────────────────────────────────

    /** 取狀態機快照（lifecycle、open、hasMore、loading） */
    snapshot() {
        return this._state.snapshot();
    }

    /** 以新陣列（新到舊）取代全部通知 */
    setItems(items) {
        if (this._destroyed) return this;
        this._records = this._buildRecords(Array.isArray(items) ? items : [], this._records);
        this._trim();
        this._afterDataChange();
        return this;
    }

    /** 新增一則或多則（陣列視為新到舊）放在最上方；同 id 取代舊的並移到最上方 */
    add(itemOrItems) {
        if (this._destroyed) return this;
        const incoming = this._buildRecords(Array.isArray(itemOrItems) ? itemOrItems : [itemOrItems], this._records);
        if (incoming.length === 0) return this;
        const replaced = new Set(incoming.map((record) => record.idKey).filter((idKey) => idKey !== null));
        this._records = [...incoming, ...this._records.filter((record) => record.idKey === null || !replaced.has(record.idKey))];
        this._trim();
        this._afterDataChange();
        return this;
    }

    /** 標為已讀；預設不觸發 onMarkRead，需要時傳 { emit: true } */
    markRead(ids, { emit = false } = {}) {
        if (this._destroyed) return this;
        const wanted = new Set((Array.isArray(ids) ? ids : [ids]).map(idKeyOf).filter((idKey) => idKey !== null));
        this._markRecordsRead(this._records.filter((record) => record.idKey !== null && wanted.has(record.idKey)), emit);
        return this;
    }

    /** 全部標為已讀；預設不觸發 onMarkRead，需要時傳 { emit: true } */
    markAllRead({ emit = false } = {}) {
        if (this._destroyed) return this;
        this._markRecordsRead(this._records, emit);
        return this;
    }

    /** 移除指定 id 的通知 */
    remove(id) {
        if (this._destroyed) return this;
        const idKey = idKeyOf(id);
        const before = this._records.length;
        this._records = this._records.filter((record) => record.idKey === null || record.idKey !== idKey);
        if (this._records.length !== before) this._afterDataChange();
        return this;
    }

    /** 目前的通知（新到舊）；回傳淺拷貝並帶上目前的 read 狀態 */
    getItems() {
        return this._records.map((record) => ({ ...record.item, read: record.read }));
    }

    getUnreadCount() {
        return this._unreadCount();
    }

    /** 設定是否還有更舊的通知可載入 */
    setHasMore(hasMore) {
        if (this._destroyed) return this;
        this._state.send('SET_HAS_MORE', { hasMore });
        this._updateChrome();
        if (this._isOpen()) this._positionPanel();
        return this;
    }

    isOpen() {
        return this._isOpen();
    }

    /** 開啟面板並把焦點移入 */
    open() {
        if (this._destroyed || this._isOpen()) return this;
        this._state.send('OPEN');
        this._applyState();
        if (this.options.markReadOnOpen) this._markRecordsRead(this._records, true);
        this._panel.focus({ preventScroll: true });
        return this;
    }

    /** 關閉面板；焦點原本在面板內時還給觸發鈕 */
    close() {
        return this._close({ restoreFocus: 'auto' });
    }

    mount(container) {
        if (this._destroyed) return this;
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) {
            console.warn('[NotificationCenter] mount target not found:', container);
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
        this._syncGlobalListeners(false);
        if (this._announceTimer !== null) {
            clearTimeout(this._announceTimer);
            this._announceTimer = null;
        }
        if (this._localeListening) {
            window.removeEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = false;
        }
        this._trigger?.removeEventListener('click', this._onTriggerClick);
        this._trigger?.removeEventListener('mouseenter', this._onTriggerEnter);
        this._trigger?.removeEventListener('mouseleave', this._onTriggerLeave);
        this._panel?.removeEventListener('click', this._onPanelClick);
        this._listHost?.removeEventListener('mouseover', this._onMouseOver);
        this._listHost?.removeEventListener('mouseleave', this._onMouseLeave);
        this.element?.removeEventListener('keydown', this._onKeyDown);
        this.element?.removeEventListener('focusout', this._onFocusOut);
        this._bellIcon?.destroy();
        this._bellIcon = null;
        this.element?.remove();
        this._records = [];
        this._controls = new Map();
        this._order = [];
        this._hoverControl = null;
    }
}

export default NotificationCenter;
