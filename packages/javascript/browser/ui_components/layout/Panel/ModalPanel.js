/**
 * ModalPanel
 * 彈出對話框 - 帶遮罩、居中顯示、Alert 類型
 */

import { BasePanel } from './BasePanel.js';
import { PanelManager } from './PanelManager.js';

import Locale from '../../i18n/index.js';
export class ModalPanel extends BasePanel {
    constructor(options = {}) {
        super({
            modal: true,
            closable: true,
            autoClose: true,
            showHeader: true,
            visibility: BasePanel.VISIBILITY.NONE,
            ...options
        });

        this._wrapWithBackdrop();
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
            background: rgba(0, 0, 0, 0.5);
            display: flex;
            align-items: center;
            justify-content: center;
            z-index: ${PanelManager.calculateZIndex(this)};
            opacity: 0;
            visibility: hidden;
            transition: all 0.3s ease;
        `;

        // 調整內部元素樣式
        this.element.style.cssText += `
            position: relative;
            max-width: 90vw;
            max-height: 90vh;
            overflow: auto;
            transform: scale(0.9);
            transition: transform 0.3s ease;
        `;

        this.backdrop.appendChild(this.element);

        // 點擊遮罩關閉
        if (this.options.autoClose) {
            this.backdrop.addEventListener('click', (e) => {
                if (e.target === this.backdrop) {
                    this.close();
                }
            });
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

        if (this.backdrop?.parentNode) {
            this.backdrop.remove();
        }

        PanelManager.unregister(this);
    }

    static confirm(options = {}) {
        const {
            title = Locale.t('modalPanel.confirmTitle'),
            message = '',
            confirmText = Locale.t('modalPanel.confirmText'),
            cancelText = Locale.t('modalPanel.cancelText'),
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
        msgEl.style.cssText = 'margin: 0 0 20px; color: var(--cl-text); font-size: 14px;';
        msgEl.textContent = message;

        const btnRow = document.createElement('div');
        btnRow.style.cssText = 'display: flex; justify-content: flex-end; gap: 10px;';

        const cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.style.cssText = 'padding: 8px 16px; border: 1px solid var(--cl-border); background: var(--cl-bg); border-radius: 6px; cursor: pointer; font-size: 14px;';
        cancelBtn.textContent = cancelText;

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.style.cssText = 'padding: 8px 16px; border: none; background: var(--cl-primary); color: var(--cl-text-inverse); border-radius: 6px; cursor: pointer; font-size: 14px;';
        confirmBtn.textContent = confirmText;

        btnRow.append(cancelBtn, confirmBtn);
        content.append(msgEl, btnRow);

        cancelBtn.addEventListener('click', () => {
            modal.close();
            onCancel();
        });

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
        msgEl.style.cssText = 'margin: 0 0 20px; color: var(--cl-text); font-size: 14px;';
        msgEl.textContent = message;

        const btnRow = document.createElement('div');
        btnRow.style.cssText = 'display: flex; justify-content: flex-end;';

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.style.cssText = 'padding: 8px 16px; border: none; background: var(--cl-primary); color: var(--cl-text-inverse); border-radius: 6px; cursor: pointer; font-size: 14px;';
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
        msgEl.style.cssText = 'margin: 0 0 12px; color: var(--cl-text); font-size: 14px;';
        msgEl.textContent = message;

        const input = document.createElement('input');
        input.type = 'text';
        input.setAttribute('placeholder', placeholder);
        input.style.cssText = 'width: 100%; padding: 8px 12px; border: 1px solid var(--cl-border); border-radius: 6px; margin-bottom: 20px; font-size: 14px; box-sizing: border-box; outline: none;';

        const btnRow = document.createElement('div');
        btnRow.style.cssText = 'display: flex; justify-content: flex-end; gap: 10px;';

        const cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.style.cssText = 'padding: 8px 16px; border: 1px solid var(--cl-border); background: var(--cl-bg); border-radius: 6px; cursor: pointer; font-size: 14px;';
        cancelBtn.textContent = cancelText;

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.style.cssText = 'padding: 8px 16px; border: none; background: var(--cl-primary); color: var(--cl-text-inverse); border-radius: 6px; cursor: pointer; font-size: 14px;';
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
