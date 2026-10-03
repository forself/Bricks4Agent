/**
 * ModalPanel
 * 彈出對話框 - 帶遮罩、居中顯示、Alert 類型
 */

import { BasePanel } from './BasePanel.js';
import { PanelManager } from './PanelManager.js';

import Locale from '../../i18n/index.js';
import { nextUid } from '../../utils/uid.js';

// 同一個 Escape keydown 只關閉一層對話框（疊加時由最上層處理）
const handledEscapeEvents = new WeakSet();

const FOCUSABLE_SELECTOR = [
    'a[href]',
    'area[href]',
    'button:not([disabled])',
    'input:not([disabled]):not([type="hidden"])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    'iframe',
    '[contenteditable="true"]',
    '[tabindex]:not([tabindex="-1"])'
].join(', ');

export class ModalPanel extends BasePanel {
    /**
     * 全域預設值。`manageFocus` 預設 false 以維持既有行為；新專案建議在啟動時設為 true，
     * 讓所有對話框開啟時把焦點移入、Tab 限制在對話框內、關閉時還原焦點。
     */
    static defaults = {
        manageFocus: false
    };

    /**
     * @param {Object} options - BasePanel 選項，另外支援：
     * @param {boolean} [options.manageFocus] - 焦點管理；未指定時採用 ModalPanel.defaults.manageFocus
     * @param {string|Element} [options.initialFocus] - 開啟時要聚焦的元素（選擇器或元素），預設為內容區第一個可聚焦元素
     * @param {string} [options.ariaLabel] - 沒有標題時的對話框名稱
     */
    constructor(options = {}) {
        const autoClose = options.autoClose !== false;
        super({
            modal: true,
            closable: true,
            // ModalPanel owns outside-click handling through its backdrop.  Do
            // not also install BasePanel's document listener: a control such
            // as Dropdown may rerender/remove the clicked option before the
            // click bubbles to document, at which point element.contains()
            // becomes false and the modal is closed even though the click
            // originated inside it.
            autoClose: false,
            showHeader: true,
            // 預設 false，直接 new 的呼叫端仍可 close() 後再 open() 重複使用
            destroyOnClose: false,
            visibility: BasePanel.VISIBILITY.NONE,
            ...options,
            autoClose: false
        });

        this._modalEntered = false;
        this._reopenWarned = false;

        // Preserve the public ModalPanel option for the precise backdrop
        // listener below without enabling BasePanel's duplicate listener.
        this.options.autoClose = autoClose;

        this._wrapWithBackdrop();
    }

    _messageStyle(marginBottom = '20px') {
        return `margin: 0 0 ${marginBottom}; color: var(--cl-text); font-size: var(--cl-font-size-lg); font-family: var(--cl-font-family);`;
    }

    _buttonRowStyle(withGap = true) {
        return `display: flex; justify-content: flex-end;${withGap ? ' gap: 10px;' : ''}`;
    }

    _buttonStyle(variant = 'secondary') {
        const base = [
            'padding: 8px 16px',
            'border-radius: var(--cl-radius-md)',
            'cursor: pointer',
            'font-size: var(--cl-font-size-lg)',
            'font-family: var(--cl-font-family)',
            'transition: opacity var(--cl-transition-fast), background var(--cl-transition-fast), border-color var(--cl-transition-fast), color var(--cl-transition-fast)'
        ];

        if (variant === 'primary') {
            return `${base.join('; ')}; border: none; background: var(--cl-primary); color: var(--cl-text-inverse);`;
        }

        return `${base.join('; ')}; border: 1px solid var(--cl-border); background: var(--cl-bg); color: var(--cl-text);`;
    }

    _inputStyle() {
        return [
            'width: 100%',
            'padding: 8px 12px',
            'border: 1px solid var(--cl-border)',
            'border-radius: var(--cl-radius-md)',
            'margin-bottom: 20px',
            'font-size: var(--cl-font-size-lg)',
            'font-family: var(--cl-font-family)',
            'box-sizing: border-box',
            'background: var(--cl-bg)',
            'color: var(--cl-text)',
            'outline: none',
            'transition: border-color var(--cl-transition-fast)'
        ].join('; ') + ';';
    }

    _wrapWithBackdrop() {
        // 建立遮罩
        this.backdrop = document.createElement('div');
        this.backdrop.className = 'modal-backdrop';
        this.backdrop.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            width: 100vw;
            height: 100vh;
            background: var(--cl-bg-overlay);
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: ${PanelManager.calculateZIndex(this)};
            opacity: 0;
            visibility: hidden;
            transition: opacity var(--cl-transition-slow), visibility var(--cl-transition-slow);
        `;

        // 調整內部元素樣式
        this.element.style.cssText += `
            position: relative;
            max-width: 90vw;
            max-height: 90vh;
            overflow: auto;
            transform: scale(0.9);
            transition: transform var(--cl-transition-slow);
        `;

        this.backdrop.appendChild(this.element);
        this._applyDialogSemantics();

        // 在 pointerdown 階段先處理真正的遮罩點擊。瀏覽器中的 click
        // 可能因焦點切換、DOM 更新或後續事件攔截而不送達遮罩；click
        // 仍保留作為鍵盤/合成事件的後備。兩者都只接受 exact backdrop，
        // 因此 panel、content 與其內部 Dropdown 不會誤關閉。
        if (this.options.autoClose) {
            this._handleBackdropPointerDown = (e) => {
                if (e.target === this.backdrop) this.close();
            };
            this._handleBackdropMouseDown = (e) => {
                if (e.target === this.backdrop) this.close();
            };
            this._handleBackdropClick = (e) => {
                if (e.target === this.backdrop) this.close();
            };
            // Capture phase is intentional.  A child control or browser focus
            // handler may stop the bubbling event even when hit-testing says
            // the backdrop itself was clicked.  Exact-target checking keeps
            // panel/content interactions unaffected.
            this.backdrop.addEventListener('pointerdown', this._handleBackdropPointerDown, true);
            this.backdrop.addEventListener('mousedown', this._handleBackdropMouseDown, true);
            this.backdrop.addEventListener('click', this._handleBackdropClick, true);
        }

        // ESC 關閉：疊加的對話框只由最上層處理；內部元件已處理（preventDefault）的 Escape 不再關閉對話框
        this._handleKeydown = (e) => {
            if (e.key !== 'Escape' || e.defaultPrevented || handledEscapeEvents.has(e)) return;
            if (this.options.visibility !== BasePanel.VISIBILITY.VISIBLE) return;
            const stack = PanelManager.modalStack;
            const index = stack.lastIndexOf(this.id);
            if (index >= 0 && index !== stack.length - 1) return;
            handledEscapeEvents.add(e);
            this.close();
        };
        document.addEventListener('keydown', this._handleKeydown);
    }

    /** 對話框語意：role="dialog"、aria-modal，並以標題（或 ariaLabel）命名。 */
    _applyDialogSemantics() {
        const panel = this.element;
        if (!panel) return;
        panel.setAttribute('role', 'dialog');
        panel.setAttribute('aria-modal', 'true');
        const titleEl = panel.querySelector('.panel__title');
        if (titleEl) {
            if (!titleEl.id) titleEl.id = nextUid('modal-title');
            panel.setAttribute('aria-labelledby', titleEl.id);
        } else if (this.options.ariaLabel) {
            panel.setAttribute('aria-label', String(this.options.ariaLabel));
        }
    }

    _shouldManageFocus() {
        const value = this.options.manageFocus;
        if (value === undefined || value === null) return ModalPanel.defaults.manageFocus === true;
        return value === true;
    }

    _isShown(el) {
        for (let node = el; node && node !== this.element?.parentNode; node = node.parentElement) {
            if (node.hidden || node.hasAttribute?.('inert')) return false;
            const style = window.getComputedStyle(node);
            if (style.display === 'none' || style.visibility === 'hidden') return false;
        }
        return true;
    }

    _focusableElements() {
        if (!this.element) return [];
        return [...this.element.querySelectorAll(FOCUSABLE_SELECTOR)]
            .filter((el) => el.getAttribute('aria-hidden') !== 'true' && this._isShown(el));
    }

    _focusInitial() {
        const option = this.options.initialFocus;
        let target = null;
        if (option instanceof Element) {
            target = this.element.contains(option) ? option : null;
        } else if (typeof option === 'string' && option) {
            target = this.element.querySelector(option);
        }
        if (!target) {
            const focusables = this._focusableElements();
            const content = this.element.querySelector('.panel__content');
            target = focusables.find((el) => content?.contains(el)) || focusables[0] || null;
        }
        if (!target) {
            if (!this.element.hasAttribute('tabindex')) this.element.setAttribute('tabindex', '-1');
            target = this.element;
        }
        target.focus({ preventScroll: true });
    }

    /**
     * Tab 循環：只在本對話框位於最上層、且焦點在對話框內或已遺失時介入；
     * 焦點在浮到 body 的子層（例如日期面板）時交給該層自行處理。
     */
    _trapTab(e) {
        if (e.key !== 'Tab' || e.defaultPrevented || !this.element) return;
        const stack = PanelManager.modalStack;
        if (stack.length && stack[stack.length - 1] !== this.id) return;
        const active = document.activeElement;
        const inside = this.element.contains(active);
        if (!inside && active && active !== document.body) return;

        const focusables = this._focusableElements();
        if (!focusables.length) {
            e.preventDefault();
            this.element.focus({ preventScroll: true });
            return;
        }
        const first = focusables[0];
        const last = focusables[focusables.length - 1];
        if (!inside || active === this.element) {
            e.preventDefault();
            (e.shiftKey ? last : first).focus({ preventScroll: true });
        } else if (e.shiftKey && active === first) {
            e.preventDefault();
            last.focus({ preventScroll: true });
        } else if (!e.shiftKey && active === last) {
            e.preventDefault();
            first.focus({ preventScroll: true });
        }
    }

    _activateFocusManagement() {
        if (this._focusTrapHandler || !this.element) return;
        const opener = document.activeElement;
        this._focusReturnTarget = opener && opener !== document.body && !this.element.contains(opener) ? opener : null;
        this._focusTrapHandler = (e) => this._trapTab(e);
        document.addEventListener('keydown', this._focusTrapHandler, true);
        if (!this.element.contains(document.activeElement)) this._focusInitial();
    }

    _deactivateFocusManagement({ restore = true } = {}) {
        if (!this._focusTrapHandler) return;
        document.removeEventListener('keydown', this._focusTrapHandler, true);
        this._focusTrapHandler = null;
        const target = this._focusReturnTarget;
        this._focusReturnTarget = null;
        if (!restore || !target?.isConnected || typeof target.focus !== 'function') return;
        // 只在焦點仍在對話框內或已遺失時還原，不搶走使用者已移往他處的焦點
        const active = document.activeElement;
        if (!active || active === document.body || this.element?.contains(active)) {
            target.focus({ preventScroll: true });
        }
    }

    _applyVisibility() {
        const { visibility } = this.options;

        if (!this.backdrop) {
            super._applyVisibility();
            return;
        }

        switch (visibility) {
            case BasePanel.VISIBILITY.VISIBLE:
                this.backdrop.style.display = 'flex';
                this.backdrop.style.visibility = 'visible';
                this.backdrop.style.opacity = '1';
                // Critical: Override BasePanel's display:none
                this.element.style.display = '';
                this.element.style.visibility = 'visible';
                this.element.style.transform = 'scale(1)';
                document.body.style.overflow = 'hidden';
                // Recalculate z-index on open to ensure it's on top
                this.backdrop.style.zIndex = PanelManager.calculateZIndex(this);
                break;
            case BasePanel.VISIBILITY.HIDDEN:
            case BasePanel.VISIBILITY.NONE:
                // Critical: Set display:none to prevent backdrop from blocking mouse events
                this.backdrop.style.display = 'none';
                this.backdrop.style.visibility = 'hidden';
                this.backdrop.style.opacity = '0';
                this.element.style.transform = 'scale(0.9)';
                document.body.style.overflow = '';
                break;
        }
    }

    _warnDestroyed(method) {
        if (this._reopenWarned) return;
        this._reopenWarned = true;
        console.warn(`[ModalPanel] ${method}() 已忽略:面板已銷毀 (destroyOnClose 會在 close() 後自動銷毀)`);
    }

    /**
     * 開啟 Modal
     */
    open() {
        // 已銷毀者不可再開:unregister 已跑過，再 enterModal 推進去的 id 永遠清不掉，
        // 會讓後續所有 Modal 的卷軸鎖與 z-index 計算長期失準。
        if (this._destroyed) {
            this._warnDestroyed('open');
            return this;
        }

        // 先註冊進入 Modal 狀態 (這會更新 Stack，影響 calculateZIndex 結果)
        PanelManager.enterModal(this);
        this._modalEntered = true;
        this.setVisibility(BasePanel.VISIBILITY.VISIBLE);
        if (this._shouldManageFocus()) this._activateFocusManagement();
        return this;
    }

    /**
     * 關閉 Modal
     */
    close() {
        // pointerdown 與 click 後備可能屬於同一次使用者操作；只允許
        // 第一次關閉觸發 onClose，避免重複銷毀或重複 render。
        if (this._destroyed || this.options.visibility !== BasePanel.VISIBILITY.VISIBLE) {
            return this;
        }

        // 先離開 Modal 狀態
        PanelManager.exitModal(this);
        this._modalEntered = false;
        super.close();
        this._deactivateFocusManagement({ restore: true });

        if (this.options.destroyOnClose && !this._destroyed) {
            // 延後到 microtask，讓 `modal.close(); onConfirm();` 的同步尾段先跑完；
            // onClose 回調可能否決關閉並重新 open()，此時不得把面板銷毀掉。
            queueMicrotask(() => {
                if (this._destroyed || this.options.visibility === BasePanel.VISIBILITY.VISIBLE) return;
                this.destroy();
            });
        }
        return this;
    }

    /**
     * 掛載（掛到 body）
     */
    mount(container = document.body) {
        if (this._destroyed) {
            this._warnDestroyed('mount');
            return this;
        }

        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.backdrop);
        return this;
    }

    /**
     * 銷毀
     */
    destroy() {
        if (this._destroyed) return;

        document.removeEventListener('keydown', this._handleKeydown);
        this._deactivateFocusManagement({ restore: true });

        if (this.backdrop && this._handleBackdropPointerDown) {
            this.backdrop.removeEventListener('pointerdown', this._handleBackdropPointerDown, true);
        }
        if (this.backdrop && this._handleBackdropMouseDown) {
            this.backdrop.removeEventListener('mousedown', this._handleBackdropMouseDown, true);
        }
        if (this.backdrop && this._handleBackdropClick) {
            this.backdrop.removeEventListener('click', this._handleBackdropClick, true);
        }
        this._handleBackdropPointerDown = null;
        this._handleBackdropMouseDown = null;
        this._handleBackdropClick = null;

        // BasePanel owns the delayed document-level outside-click listener.
        // Skipping its destroy routine leaves an invisible modal's listener
        // alive.  That stale listener can later interpret clicks inside a new
        // modal as outside clicks and invoke the old onClose callback, which in
        // turn destroys the new modal (for example, selecting a Dropdown row in
        // a reopened nested editor).  Unwind modal state and let the base class
        // remove every listener/child/manager registration first.
        // close() 已退出過就不重複退出
        if (this._modalEntered) {
            PanelManager.exitModal(this);
            this._modalEntered = false;
        }

        super.destroy();

        // 巢狀情境(confirm 的 onConfirm 再開 prompt)下，延後的銷毀不能搶走還開著的
        // Modal 的 body 卷軸鎖;要等 super.destroy() 連同子面板一起 unregister、把殘留的
        // modalStack id 清乾淨後再判斷，否則自己的殘留 id 會讓卷軸鎖永遠解不掉。
        if (PanelManager.modalStack.length === 0) {
            document.body.style.overflow = '';
        }

        if (this.backdrop?.parentNode) {
            this.backdrop.remove();
        }
        // 銷毀後 backdrop 為 null（既有公開契約，呼叫端以此判斷面板已拆除）。
        // mount() 已由 _destroyed 擋下，_applyVisibility() 對 null backdrop 會改走 BasePanel 路徑。
        this.backdrop = null;
    }

    static confirm(options = {}) {
        const {
            title = Locale.t('modalPanel.confirmTitle'),
            message = '',
            confirmText = Locale.t('modalPanel.confirmText'),
            cancelText = Locale.t('modalPanel.cancelText'),
            onConfirm = () => { },
            onCancel = () => { },
            onClose = null,
            ...rest
        } = options;

        let settled = false;
        const finishCancel = () => {
            if (settled) return;
            settled = true;
            onCancel();
            onClose?.();
        };

        const modal = new ModalPanel({
            title,
            closable: true,
            // Confirmation dialogs must remain open until the user explicitly
            // chooses an outcome.  In particular, a confirm created from a
            // click handler must not be closed again when that same click
            // reaches the document-level outside-click listener.
            autoClose: false,
            destroyOnClose: true,
            onClose: finishCancel,
            ...rest
        });

        const content = document.createElement('div');
        const msgEl = document.createElement('p');
        msgEl.style.cssText = modal._messageStyle();
        msgEl.textContent = message;

        const btnRow = document.createElement('div');
        btnRow.style.cssText = modal._buttonRowStyle();

        const cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.style.cssText = modal._buttonStyle('secondary');
        cancelBtn.textContent = cancelText;

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.style.cssText = modal._buttonStyle('primary');
        confirmBtn.textContent = confirmText;

        btnRow.append(cancelBtn, confirmBtn);
        content.append(msgEl, btnRow);

        cancelBtn.addEventListener('click', () => {
            modal.close();
        });

        confirmBtn.addEventListener('click', () => {
            if (settled) return;
            settled = true;
            modal.close();
            onConfirm();
        });

        modal.setContent(content);
        modal.mount();
        modal.open();

        return modal;
    }

    /**
     * 快速建立提示對話框
     */
    static alert(options = {}) {
        const {
            title = Locale.t('modalPanel.alertTitle'),
            message = '',
            confirmText = Locale.t('modalPanel.okText'),
            onConfirm = () => { },
            ...rest
        } = options;

        const modal = new ModalPanel({
            title,
            closable: true,
            destroyOnClose: true,
            ...rest
        });

        const content = document.createElement('div');
        const msgEl = document.createElement('p');
        msgEl.style.cssText = modal._messageStyle();
        msgEl.textContent = message;

        const btnRow = document.createElement('div');
        btnRow.style.cssText = modal._buttonRowStyle(false);

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.style.cssText = modal._buttonStyle('primary');
        confirmBtn.textContent = confirmText;

        btnRow.append(confirmBtn);
        content.append(msgEl, btnRow);

        confirmBtn.addEventListener('click', () => {
            modal.close();
            onConfirm();
        });

        modal.setContent(content);
        modal.mount();
        modal.open();

        return modal;
    }

    /**
     * 快速建立輸入對話框
     */
    static prompt(options = {}) {
        const {
            title = Locale.t('modalPanel.promptTitle'),
            message = '',
            placeholder = '',
            confirmText = Locale.t('modalPanel.confirmText'),
            cancelText = Locale.t('modalPanel.cancelText'),
            validate = () => true, // 驗證函式 (value) => boolean
            onConfirm = () => { },
            onCancel = () => { },
            ...rest
        } = options;

        const modal = new ModalPanel({
            title,
            closable: true,
            destroyOnClose: true,
            ...rest
        });

        const content = document.createElement('div');
        const msgEl = document.createElement('p');
        msgEl.style.cssText = modal._messageStyle('12px');
        msgEl.textContent = message;

        const input = document.createElement('input');
        input.type = 'text';
        input.setAttribute('placeholder', placeholder);
        input.style.cssText = modal._inputStyle();

        const btnRow = document.createElement('div');
        btnRow.style.cssText = modal._buttonRowStyle();

        const cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.style.cssText = modal._buttonStyle('secondary');
        cancelBtn.textContent = cancelText;

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.style.cssText = modal._buttonStyle('primary');
        confirmBtn.textContent = confirmText;

        btnRow.append(cancelBtn, confirmBtn);
        content.append(msgEl, input, btnRow);

        // 輸入驗證樣式
        input.addEventListener('input', () => {
            const isValid = validate(input.value);
            confirmBtn.disabled = !isValid;
            confirmBtn.style.opacity = isValid ? '1' : '0.5';
            confirmBtn.style.cursor = isValid ? 'pointer' : 'not-allowed';
            input.style.borderColor = isValid ? 'var(--cl-border)' : 'var(--cl-danger)';
        });

        // 初始驗證
        input.dispatchEvent(new Event('input'));

        cancelBtn.addEventListener('click', () => {
            modal.close();
            onCancel();
        });

        confirmBtn.addEventListener('click', () => {
            if (validate(input.value)) {
                modal.close();
                onConfirm(input.value);
            }
        });

        // Enter 提交
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && validate(input.value)) {
                modal.close();
                onConfirm(input.value);
            }
        });

        modal.setContent(content);
        modal.mount();
        modal.open();

        // 自動聚焦
        setTimeout(() => input.focus(), 100);

        return modal;
    }
}

export default ModalPanel;
