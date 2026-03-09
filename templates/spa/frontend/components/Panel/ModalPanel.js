/**
 * ModalPanel
 * 彈出對話框 - 帶遮罩、居中顯示、Alert 類型
 */

import { BasePanel } from './BasePanel.js';
import { PanelManager } from './PanelManager.js';

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
            title = '確認',
            message = '',
            confirmText = '確認',
            cancelText = '取消',
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
        content.innerHTML = `
            <p style="margin: 0 0 20px; color: #333; font-size: 14px;">${message}</p>
            <div style="display: flex; justify-content: flex-end; gap: 10px;">
                <button type="button" class="modal-cancel" style="
                    padding: 8px 16px;
                    border: 1px solid #ddd;
                    background: white;
                    border-radius: 6px;
                    cursor: pointer;
                    font-size: 14px;
                ">${cancelText}</button>
                <button type="button" class="modal-confirm" style="
                    padding: 8px 16px;
                    border: none;
                    background: #2196F3;
                    color: white;
                    border-radius: 6px;
                    cursor: pointer;
                    font-size: 14px;
                ">${confirmText}</button>
            </div>
        `;

        content.querySelector('.modal-cancel').addEventListener('click', () => {
            modal.close();
            onCancel();
        });

        content.querySelector('.modal-confirm').addEventListener('click', () => {
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
            title = '提示',
            message = '',
            confirmText = '確定',
            onConfirm = () => { },
            ...rest
        } = options;

        const modal = new ModalPanel({
            title,
            closable: true,
            ...rest
        });

        const content = document.createElement('div');
        content.innerHTML = `
            <p style="margin: 0 0 20px; color: #333; font-size: 14px;">${message}</p>
            <div style="display: flex; justify-content: flex-end;">
                <button type="button" class="modal-confirm" style="
                    padding: 8px 16px;
                    border: none;
                    background: #2196F3;
                    color: white;
                    border-radius: 6px;
                    cursor: pointer;
                    font-size: 14px;
                ">${confirmText}</button>
            </div>
        `;

        content.querySelector('.modal-confirm').addEventListener('click', () => {
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
            title = '輸入',
            message = '',
            placeholder = '',
            confirmText = '確認',
            cancelText = '取消',
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
        content.innerHTML = `
            <p style="margin: 0 0 12px; color: #333; font-size: 14px;">${message}</p>
            <input type="text" class="modal-input" placeholder="${placeholder}" style="
                width: 100%;
                padding: 8px 12px;
                border: 1px solid #ddd;
                border-radius: 6px;
                margin-bottom: 20px;
                font-size: 14px;
                box-sizing: border-box; 
                outline: none;
            ">
            <div style="display: flex; justify-content: flex-end; gap: 10px;">
                <button type="button" class="modal-cancel" style="
                    padding: 8px 16px;
                    border: 1px solid #ddd;
                    background: white;
                    border-radius: 6px;
                    cursor: pointer;
                    font-size: 14px;
                ">${cancelText}</button>
                <button type="button" class="modal-confirm" style="
                    padding: 8px 16px;
                    border: none;
                    background: #2196F3;
                    color: white;
                    border-radius: 6px;
                    cursor: pointer;
                    font-size: 14px;
                ">${confirmText}</button>
            </div>
        `;

        const input = content.querySelector('.modal-input');
        const confirmBtn = content.querySelector('.modal-confirm');

        // 輸入驗證樣式
        input.addEventListener('input', () => {
            const isValid = validate(input.value);
            confirmBtn.disabled = !isValid;
            confirmBtn.style.opacity = isValid ? '1' : '0.5';
            confirmBtn.style.cursor = isValid ? 'pointer' : 'not-allowed';
            input.style.borderColor = isValid ? '#ddd' : '#F44336';
        });

        // 初始驗證
        input.dispatchEvent(new Event('input'));

        content.querySelector('.modal-cancel').addEventListener('click', () => {
            console.log('[ModalPanel.prompt] Cancel clicked');
            modal.close();
            onCancel();
        });

        confirmBtn.addEventListener('click', () => {
            console.log('[ModalPanel.prompt] Confirm clicked, value:', input.value, 'valid:', validate(input.value));
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
