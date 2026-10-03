/**
 * Popover — 依附在錨點元素旁的互動式浮動面板
 *
 * 與 Tooltip 不同：面板可以放按鈕、連結、表單等可互動內容，並負責焦點管理。
 * - 觸發方式：click / hover / focus / manual
 * - 開啟時移到 document.body，以 fixed 座標定位；空間不足時自動翻面，並平移留在視窗內
 * - 錨點帶 aria-haspopup / aria-expanded / aria-controls，destroy() 時還原成原本的值
 * - document/window 監聽與 ResizeObserver 只在開啟期間掛載
 *
 * @example
 * const popover = new Popover({
 *     anchor: document.querySelector('#filter-button'),
 *     title: '篩選條件',
 *     content: () => buildFilterForm(),
 *     onClose: (reason) => console.log('closed by', reason)
 * });
 */
import Locale from '../../i18n/index.js';
import './locale.js';
import { nextUid } from '../../utils/uid.js';

const PLACEMENTS = new Set([
    'top', 'top-start', 'top-end',
    'bottom', 'bottom-start', 'bottom-end',
    'left', 'left-start', 'left-end',
    'right', 'right-start', 'right-end'
]);
const TRIGGERS = new Set(['click', 'hover', 'focus', 'manual']);
/** aria-haspopup 只接受這些值；其他 role 不宣告 haspopup，只以 aria-expanded + aria-controls 表示展開關係 */
const HASPOPUP_ROLES = new Set(['dialog', 'menu', 'listbox', 'tree', 'grid']);
const ANCHOR_ARIA = ['aria-haspopup', 'aria-expanded', 'aria-controls'];
const FOCUSABLE_SELECTOR = [
    'a[href]', 'area[href]', 'button:not([disabled])', 'input:not([disabled]):not([type="hidden"])',
    'select:not([disabled])', 'textarea:not([disabled])', 'iframe', 'audio[controls]', 'video[controls]',
    '[contenteditable]:not([contenteditable="false"])', '[tabindex]'
].join(',');
const VIEWPORT_MARGIN = 8;
const DEFAULT_MAX_WIDTH = 360;
const OPPOSITE = { top: 'bottom', bottom: 'top', left: 'right', right: 'left' };

const clamp = (value, min, max) => Math.min(Math.max(value, min), Math.max(min, max));
const toDelay = (value, fallback) => (Number.isFinite(Number(value)) && Number(value) >= 0 ? Number(value) : fallback);

export class Popover {
    constructor(options = {}) {
        this.options = {
            anchor: null,                           // 必填：錨點元素
            content: null,                          // Node、純文字字串，或回傳 Node/字串的函式（每次開啟時呼叫）
            title: '',                              // 標題；有標題時以 aria-labelledby 作為面板名稱
            trigger: 'click',                       // 'click' | 'hover' | 'focus' | 'manual'
            placement: 'bottom-start',              // top / bottom / left / right，可加 -start 或 -end
            offset: 8,                              // 與錨點的距離（px）
            closeOnOutsideClick: true,              // 點擊面板與錨點以外的區域時關閉
            closeOnEscape: true,                    // 按 Escape 關閉
            autoFocus: true,                        // 以點擊或鍵盤開啟時，焦點移到面板內第一個可聚焦元素
            trapFocus: false,                       // Tab 焦點限制在面板內
            returnFocus: true,                      // 關閉時若焦點在面板內，送回錨點
            hoverDelay: { open: 150, close: 200 },  // hover 觸發的開啟 / 關閉延遲（ms）
            role: 'dialog',                         // 面板的 ARIA role
            ariaLabel: '',                          // 沒有 title 時的無障礙名稱
            width: null,                            // 面板寬度（數字視為 px）；null 依內容，最寬 360px
            closeButton: false,                     // 在標題列顯示關閉按鈕
            onOpen: null,                           // () 開啟後
            onClose: null,                          // (reason) 關閉後
            ...options
        };

        this._id = nextUid('popover');
        this._titleId = `${this._id}-title`;
        this.anchor = this.options.anchor instanceof Element ? this.options.anchor : null;
        if (!this.anchor) console.warn('[Popover] options.anchor 必須是 DOM 元素。');
        this._trigger = TRIGGERS.has(this.options.trigger) ? this.options.trigger : 'click';
        this._open = false;
        this._destroyed = false;
        this._home = null;
        this._openTimer = null;
        this._closeTimer = null;
        this._raf = null;
        this._resizeObserver = null;
        this._attached = null;
        this._hoverAnchor = false;
        this._hoverPanel = false;
        this._suppressFocusOpen = false;
        this._savedAria = null;
        this._closeButton = null;

        this._bindHandlers();
        this.element = this._createElement();
        if (typeof this.options.content !== 'function') this._renderContent();
        this._applyAnchorAria();
        this._bindTrigger();
    }

    // ------------------------------------------------------------------
    // 公開 API
    // ------------------------------------------------------------------

    /**
     * 選用：指定面板關閉期間的存放容器。
     * 沒有呼叫 mount() 時，面板關閉期間不在 DOM 中；開啟時一律移到 document.body。
     */
    mount(container) {
        if (this._destroyed) return this;
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target instanceof Element) {
            this._home = target;
            if (!this._open) target.appendChild(this.element);
        }
        return this;
    }

    /** 開啟面板。程式呼叫預設不移動焦點；傳 { focus: true } 時比照鍵盤開啟套用 autoFocus。 */
    open({ focus = false } = {}) {
        return this._show(focus ? 'keyboard' : 'api');
    }

    /** 關閉面板；reason 會傳給 onClose。 */
    close(reason = 'api') {
        if (this._destroyed || !this._open) return this;
        this._hide(reason);
        return this;
    }

    toggle() {
        return this._open ? this.close('api') : this.open();
    }

    isOpen() {
        return this._open;
    }

    /** 更換內容：Node、純文字字串，或回傳 Node/字串的函式（關閉期間傳入函式時，延到下次開啟才呼叫）。 */
    setContent(content) {
        if (this._destroyed) return this;
        this.options.content = content;
        if (typeof content !== 'function' || this._open) this._renderContent();
        if (this._open) this.updatePosition();
        return this;
    }

    /** 依錨點與視窗重新計算位置（開啟期間捲動、縮放與尺寸變化時會自動呼叫）。 */
    updatePosition() {
        if (this._destroyed || !this._open || !this.anchor) return this;
        const panel = this.element;
        const viewportWidth = window.innerWidth || document.documentElement.clientWidth || 0;
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight || 0;
        const room = Math.max(0, viewportWidth - VIEWPORT_MARGIN * 2);
        const { width } = this.options;
        if (width !== null && width !== undefined && width !== '') {
            panel.style.width = typeof width === 'number' ? `${width}px` : String(width);
            panel.style.maxWidth = `${room}px`;
        } else {
            panel.style.width = 'max-content';
            panel.style.maxWidth = `${Math.min(DEFAULT_MAX_WIDTH, room)}px`;
        }

        const anchorRect = this.anchor.getBoundingClientRect();
        const panelRect = panel.getBoundingClientRect();
        const panelWidth = panelRect.width || 0;
        const panelHeight = panelRect.height || 0;
        const offset = Number(this.options.offset) || 0;
        const [preferredSide, align = 'center'] = this._placement().split('-');
        const space = {
            top: anchorRect.top - offset - VIEWPORT_MARGIN,
            bottom: viewportHeight - anchorRect.bottom - offset - VIEWPORT_MARGIN,
            left: anchorRect.left - offset - VIEWPORT_MARGIN,
            right: viewportWidth - anchorRect.right - offset - VIEWPORT_MARGIN
        };

        // 翻面：偏好的一側放不下、而對側空間較大時改放對側
        let side = preferredSide;
        const needed = side === 'top' || side === 'bottom' ? panelHeight : panelWidth;
        if (space[side] < needed && space[OPPOSITE[side]] > space[side]) side = OPPOSITE[side];

        let left;
        let top;
        if (side === 'top' || side === 'bottom') {
            top = side === 'bottom' ? anchorRect.bottom + offset : anchorRect.top - offset - panelHeight;
            if (align === 'start') left = anchorRect.left;
            else if (align === 'end') left = anchorRect.right - panelWidth;
            else left = anchorRect.left + (anchorRect.width - panelWidth) / 2;
        } else {
            left = side === 'right' ? anchorRect.right + offset : anchorRect.left - offset - panelWidth;
            if (align === 'start') top = anchorRect.top;
            else if (align === 'end') top = anchorRect.bottom - panelHeight;
            else top = anchorRect.top + (anchorRect.height - panelHeight) / 2;
        }

        // 平移：留在視窗邊界內
        left = clamp(left, VIEWPORT_MARGIN, viewportWidth - VIEWPORT_MARGIN - panelWidth);
        top = clamp(top, VIEWPORT_MARGIN, viewportHeight - VIEWPORT_MARGIN - panelHeight);
        panel.style.left = `${Math.round(left)}px`;
        panel.style.top = `${Math.round(top)}px`;
        panel.dataset.placement = align === 'center' ? side : `${side}-${align}`;
        return this;
    }

    /** 移除面板、全域監聽與計時器，並還原錨點原本的 ARIA 屬性。不觸發 onClose。 */
    destroy() {
        if (this._destroyed) return;
        const focusInside = this._open && this._containsFocus();
        this._clearTimers();
        this._detachGlobal();
        this._open = false;
        this._unbindTrigger();
        this._restoreAnchorAria();
        this.element.remove();
        this._destroyed = true;
        this._home = null;
        if (focusInside && this.options.returnFocus) this._focusAnchor();
    }

    // ------------------------------------------------------------------
    // DOM 建構
    // ------------------------------------------------------------------

    _createElement() {
        const { role, title, ariaLabel, closeButton, trapFocus } = this.options;
        const panel = document.createElement('div');
        panel.className = 'popover';
        panel.id = this._id;
        panel.setAttribute('role', String(role || 'dialog'));
        // 可程式聚焦：沒有可聚焦內容時 autoFocus 落在面板本身；點擊面板空白處時焦點也留在面板內
        panel.tabIndex = -1;
        panel.style.cssText = 'position:fixed;top:0;left:0;z-index:10060;display:none;box-sizing:border-box;min-width:120px;background:var(--cl-bg);color:var(--cl-text);border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);box-shadow:var(--cl-shadow-lg);font-family:var(--cl-font-family);font-size:var(--cl-font-size-md);line-height:1.5;';
        if (trapFocus && panel.getAttribute('role') === 'dialog') panel.setAttribute('aria-modal', 'true');

        if (title || closeButton) {
            const header = document.createElement('div');
            header.className = 'popover__header';
            header.style.cssText = 'display:flex;align-items:center;gap:8px;padding:8px 8px 8px 12px;border-bottom:1px solid var(--cl-border-light);';
            if (title) {
                const heading = document.createElement('div');
                heading.className = 'popover__title';
                heading.id = this._titleId;
                heading.textContent = String(title);
                heading.style.cssText = 'flex:1;min-width:0;font-weight:600;color:var(--cl-text-heading);overflow-wrap:anywhere;';
                header.appendChild(heading);
                panel.setAttribute('aria-labelledby', heading.id);
            }
            if (closeButton) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'popover__close';
                button.textContent = '×';
                button.style.cssText = 'display:inline-flex;align-items:center;justify-content:center;margin-left:auto;padding:2px 8px;border:none;background:transparent;color:var(--cl-text-secondary);border-radius:var(--cl-radius-sm);font-family:inherit;font-size:var(--cl-font-size-lg);line-height:1;cursor:pointer;';
                button.addEventListener('click', () => this.close('close-button'));
                header.appendChild(button);
                this._closeButton = button;
            }
            panel.appendChild(header);
        }
        if (!title && ariaLabel) panel.setAttribute('aria-label', String(ariaLabel));

        const body = document.createElement('div');
        body.className = 'popover__body';
        body.style.cssText = 'padding:12px;overflow-wrap:anywhere;';
        panel.appendChild(body);
        this._body = body;

        panel.addEventListener('keydown', this._handlePanelKeydown);
        panel.addEventListener('focusout', this._handleFocusLeave);
        panel.addEventListener('mouseenter', this._handlePanelEnter);
        panel.addEventListener('mouseleave', this._handlePanelLeave);
        this._syncLabels();
        return panel;
    }

    _syncLabels() {
        if (!this._closeButton) return;
        const label = Locale.t('popover.close');
        this._closeButton.setAttribute('aria-label', label);
        this._closeButton.title = label;
    }

    _renderContent() {
        let value = this.options.content;
        if (typeof value === 'function') {
            try {
                value = value(this);
            } catch (error) {
                console.error('[Popover] content 函式執行失敗：', error);
                value = null;
            }
        }
        const body = this._body;
        if (value instanceof Node) {
            if (body.childNodes.length !== 1 || body.firstChild !== value) body.replaceChildren(value);
            return;
        }
        body.replaceChildren();
        // 字串一律當純文字，不解析 HTML
        if (value !== null && value !== undefined && value !== false) body.textContent = String(value);
    }

    _placement() {
        const placement = String(this.options.placement || '');
        return PLACEMENTS.has(placement) ? placement : 'bottom-start';
    }

    _hoverDelay() {
        const delay = this.options.hoverDelay;
        if (typeof delay === 'number') return { open: toDelay(delay, 150), close: toDelay(delay, 200) };
        return { open: toDelay(delay?.open, 150), close: toDelay(delay?.close, 200) };
    }

    // ------------------------------------------------------------------
    // 錨點 ARIA 與觸發監聽
    // ------------------------------------------------------------------

    _applyAnchorAria() {
        const anchor = this.anchor;
        if (!anchor) return;
        this._savedAria = Object.fromEntries(ANCHOR_ARIA.map((name) => [name, anchor.getAttribute(name)]));
        const role = String(this.options.role || 'dialog');
        if (HASPOPUP_ROLES.has(role)) anchor.setAttribute('aria-haspopup', role);
        anchor.setAttribute('aria-expanded', 'false');
        anchor.setAttribute('aria-controls', this._id);
    }

    _restoreAnchorAria() {
        const anchor = this.anchor;
        const saved = this._savedAria;
        if (!anchor || !saved) return;
        for (const name of ANCHOR_ARIA) {
            if (saved[name] === null) anchor.removeAttribute(name);
            else anchor.setAttribute(name, saved[name]);
        }
        this._savedAria = null;
    }

    _triggerListeners() {
        switch (this._trigger) {
            case 'click':
                return [['click', this._handleAnchorClick]];
            case 'hover':
                // hover 也回應鍵盤焦點，鍵盤使用者才看得到內容
                return [
                    ['mouseenter', this._handleAnchorEnter],
                    ['mouseleave', this._handleAnchorLeave],
                    ['focusin', this._handleAnchorFocusIn],
                    ['focusout', this._handleFocusLeave]
                ];
            case 'focus':
                return [['focusin', this._handleAnchorFocusIn], ['focusout', this._handleFocusLeave]];
            default:
                return [];
        }
    }

    _bindTrigger() {
        if (!this.anchor) return;
        for (const [type, handler] of this._triggerListeners()) this.anchor.addEventListener(type, handler);
    }

    _unbindTrigger() {
        if (!this.anchor) return;
        for (const [type, handler] of this._triggerListeners()) this.anchor.removeEventListener(type, handler);
    }

    _bindHandlers() {
        this._handleAnchorClick = (event) => {
            if (this._destroyed) return;
            if (this._open) this.close('trigger');
            // detail === 0：由鍵盤（Enter / Space）觸發的 click
            else this._show(event?.detail === 0 ? 'keyboard' : 'click');
        };
        this._handleAnchorEnter = () => {
            this._hoverAnchor = true;
            this._cancelClose();
            this._scheduleOpen();
        };
        this._handleAnchorLeave = () => {
            this._hoverAnchor = false;
            this._cancelOpen();
            this._scheduleClose('hover');
        };
        this._handlePanelEnter = () => {
            if (this._trigger !== 'hover') return;
            this._hoverPanel = true;
            this._cancelClose();
        };
        this._handlePanelLeave = () => {
            if (this._trigger !== 'hover') return;
            this._hoverPanel = false;
            this._scheduleClose('hover');
        };
        this._handleAnchorFocusIn = () => {
            if (this._destroyed || this._suppressFocusOpen) return;
            this._cancelClose();
            this._show('focus');
        };
        this._handleFocusLeave = (event) => {
            if (!this._open || (this._trigger !== 'focus' && this._trigger !== 'hover')) return;
            if (this._isInternalTarget(event.relatedTarget)) return;
            if (this._trigger === 'focus') this.close('blur');
            else this._scheduleClose('blur');
        };
        this._handlePanelKeydown = (event) => this._handleKeydown(event, false);
        this._handleAnchorKeydown = (event) => this._handleKeydown(event, true);
        // 焦點不在面板或錨點時（例如 hover 開啟）也能用 Escape 關閉
        this._handleDocumentKeydown = (event) => {
            if (event.key !== 'Escape' || event.defaultPrevented || !this._open) return;
            this.close('escape');
        };
        this._handleDocumentPointerDown = (event) => {
            if (!this._open || this._isInternalTarget(event.target)) return;
            this.close('outside');
        };
        this._handleViewportChange = () => this._schedulePosition();
    }

    // ------------------------------------------------------------------
    // 開關、焦點與鍵盤
    // ------------------------------------------------------------------

    _show(source) {
        if (this._destroyed || this._open || !this.anchor) return this;
        this._clearTimers();
        this._open = true;
        if (typeof this.options.content === 'function') this._renderContent();
        this._syncLabels();
        const panel = this.element;
        panel.style.display = 'block';
        panel.dataset.portal = 'body';
        document.body.appendChild(panel);
        this.anchor.setAttribute('aria-expanded', 'true');
        this._attachGlobal();
        this.updatePosition();
        if (this.options.autoFocus && (source === 'click' || source === 'keyboard')) this._focusInitial();
        this._emit('onOpen');
        return this;
    }

    _hide(reason) {
        const focusInside = this._containsFocus();
        this._clearTimers();
        this._detachGlobal();
        this._open = false;
        const panel = this.element;
        panel.style.display = 'none';
        delete panel.dataset.portal;
        delete panel.dataset.placement;
        if (this._home) this._home.appendChild(panel);
        else panel.remove();
        this.anchor?.setAttribute('aria-expanded', 'false');
        // 點到外面或焦點已自行離開時，不把焦點搶回來
        if (focusInside && this.options.returnFocus && reason !== 'outside' && reason !== 'blur') this._focusAnchor();
        this._emit('onClose', reason);
    }

    _emit(name, ...args) {
        const handler = this.options[name];
        if (typeof handler === 'function') handler(...args);
    }

    /**
     * 面板、錨點，以及其他浮在 body 上的浮層（data-portal="body"：面板內 DatePicker 的月曆、
     * TimePicker 的面板、巢狀 Popover）都算「內部」，點擊或聚焦它們不會關閉本面板。
     */
    _isInternalTarget(target) {
        if (!(target instanceof Node)) return false;
        if (this.element.contains(target) || this.anchor?.contains(target)) return true;
        const element = target instanceof Element ? target : target.parentElement;
        const layer = element?.closest('[data-portal="body"]');
        return Boolean(layer) && layer !== this.element;
    }

    _containsFocus() {
        const active = document.activeElement;
        return Boolean(active) && this.element.contains(active);
    }

    _focusAnchor() {
        if (!this.anchor || typeof this.anchor.focus !== 'function') return;
        // 送回焦點不應再次觸發 focus / hover 的開啟
        this._suppressFocusOpen = true;
        try {
            this.anchor.focus();
        } finally {
            this._suppressFocusOpen = false;
        }
    }

    _focusables() {
        return [...this.element.querySelectorAll(FOCUSABLE_SELECTOR)]
            .filter((el) => el.tabIndex >= 0 && !el.closest('[hidden], [inert]'));
    }

    _focusInitial() {
        const focusables = this._focusables();
        const target = focusables.find((el) => this._body.contains(el)) || focusables[0] || this.element;
        target.focus?.();
    }

    /** 文件順序中錨點之後的第一個可聚焦元素（面板本身除外） */
    _nextFocusableAfterAnchor() {
        const anchor = this.anchor;
        if (!anchor) return null;
        for (const el of document.querySelectorAll(FOCUSABLE_SELECTOR)) {
            if (el === anchor || anchor.contains(el) || this.element.contains(el)) continue;
            if (el.tabIndex < 0 || el.closest('[hidden], [inert]')) continue;
            if (anchor.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING) return el;
        }
        return null;
    }

    _handleKeydown(event, fromAnchor) {
        if (!this._open || this._destroyed) return;
        if (event.key === 'Escape') {
            if (!this.options.closeOnEscape || event.defaultPrevented) return;
            // 在面板或錨點上處理掉，不再傳到外層（例如同時關掉外層對話框）
            event.preventDefault();
            event.stopPropagation();
            this.close('escape');
            return;
        }
        if (event.key !== 'Tab') return;

        if (fromAnchor) {
            // 面板被移到 body 末端；Tab 從錨點直接進入面板，維持「面板緊接在錨點之後」的順序
            if (event.shiftKey) return;
            const first = this._focusables()[0];
            if (!first) return;
            event.preventDefault();
            first.focus();
            return;
        }

        const focusables = this._focusables();
        const active = document.activeElement;
        if (this.options.trapFocus) {
            event.preventDefault();
            if (!focusables.length) {
                this.element.focus();
                return;
            }
            const index = focusables.indexOf(active);
            let next;
            if (event.shiftKey) next = index <= 0 ? focusables[focusables.length - 1] : focusables[index - 1];
            else next = index === -1 || index === focusables.length - 1 ? focusables[0] : focusables[index + 1];
            next.focus();
            return;
        }

        const first = focusables[0];
        const last = focusables[focusables.length - 1];
        if (event.shiftKey) {
            if (!first || active === first || active === this.element) {
                event.preventDefault();
                this._focusAnchor();
            }
            return;
        }
        if (!last || active === last) {
            const next = this._nextFocusableAfterAnchor();
            if (!next) return;
            event.preventDefault();
            if (this._trigger !== 'manual') this.close('blur');
            next.focus();
        }
    }

    // ------------------------------------------------------------------
    // 計時器與全域監聽（只在開啟期間）
    // ------------------------------------------------------------------

    _scheduleOpen() {
        if (this._destroyed || this._open || this._openTimer) return;
        this._openTimer = setTimeout(() => {
            this._openTimer = null;
            this._show('hover');
        }, this._hoverDelay().open);
    }

    _scheduleClose(reason) {
        if (this._destroyed || !this._open || this._closeTimer) return;
        this._closeTimer = setTimeout(() => {
            this._closeTimer = null;
            // 指標仍在錨點或面板上、或正在操作面板內容時保持開啟
            if (this._hoverAnchor || this._hoverPanel || this._containsFocus()) return;
            this.close(reason);
        }, this._hoverDelay().close);
    }

    _cancelOpen() {
        if (this._openTimer) {
            clearTimeout(this._openTimer);
            this._openTimer = null;
        }
    }

    _cancelClose() {
        if (this._closeTimer) {
            clearTimeout(this._closeTimer);
            this._closeTimer = null;
        }
    }

    _clearTimers() {
        this._cancelOpen();
        this._cancelClose();
    }

    _schedulePosition() {
        if (!this._open || this._raf !== null) return;
        const raf = globalThis.requestAnimationFrame;
        if (typeof raf !== 'function') {
            this.updatePosition();
            return;
        }
        this._raf = raf(() => {
            this._raf = null;
            this.updatePosition();
        });
    }

    _attachGlobal() {
        if (this._attached) return;
        const attached = {
            outside: Boolean(this.options.closeOnOutsideClick),
            escape: Boolean(this.options.closeOnEscape)
        };
        this._attached = attached;
        if (attached.outside) document.addEventListener('mousedown', this._handleDocumentPointerDown, true);
        if (attached.escape) document.addEventListener('keydown', this._handleDocumentKeydown);
        window.addEventListener('resize', this._handleViewportChange);
        window.addEventListener('scroll', this._handleViewportChange, true);
        this.anchor?.addEventListener('keydown', this._handleAnchorKeydown);
        if (typeof ResizeObserver === 'function') {
            this._resizeObserver = new ResizeObserver(this._handleViewportChange);
            if (this.anchor) this._resizeObserver.observe(this.anchor);
            this._resizeObserver.observe(this.element);
        }
    }

    _detachGlobal() {
        const attached = this._attached;
        if (!attached) return;
        this._attached = null;
        if (attached.outside) document.removeEventListener('mousedown', this._handleDocumentPointerDown, true);
        if (attached.escape) document.removeEventListener('keydown', this._handleDocumentKeydown);
        window.removeEventListener('resize', this._handleViewportChange);
        window.removeEventListener('scroll', this._handleViewportChange, true);
        this.anchor?.removeEventListener('keydown', this._handleAnchorKeydown);
        this._resizeObserver?.disconnect();
        this._resizeObserver = null;
        if (this._raf !== null) {
            globalThis.cancelAnimationFrame?.(this._raf);
            this._raf = null;
        }
    }
}

export default Popover;
