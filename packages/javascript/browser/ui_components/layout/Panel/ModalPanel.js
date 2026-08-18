/**
 * ModalPanel
 * 彈出對話框 - 帶遮罩、居中顯示、Alert 類型
 */

import { BasePanel } from './BasePanel.js';
import { PanelManager } from './PanelManager.js';

import Locale from '../../i18n/index.js';
export class ModalPanel extends BasePanel {
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
            visibility: BasePanel.VISIBILITY.NONE,
            ...options,
            autoClose: false
        });

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

        // ESC 關閉
        this._handleKeydown = (e) => {
            if (e.key === 'Escape' && this.options.visibility === BasePanel.VISIBILITY.VISIBLE) {
                this.close();
            }
        };
        document.addEventListener('keydown', this._handleKeydown);
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

    /**
     * 開啟 Modal
     */
    open() {
        // 先註冊進入 Modal 狀態 (這會更新 Stack，影響 calculateZIndex 結果)
        PanelManager.enterModal(this);
        this.setVisibility(BasePanel.VISIBILITY.VISIBLE);
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
        super.close();
        return this;
    }

    /**
     * 掛載（掛到 body）
     */
    mount(container = document.body) {
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
        document.removeEventListener('keydown', this._handleKeydown);
        document.body.style.overflow = '';

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
        PanelManager.exitModal(this);
        super.destroy();

        if (this.backdrop?.parentNode) {
            this.backdrop.remove();
        }
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
