/**
 * ToastPanel
 * 通知訊息 - 自動消失的提示
 */

import { BasePanel } from './BasePanel.js';
import { Icon } from '../../common/Icon/index.js';

export class ToastPanel extends BasePanel {
    static POSITIONS = {
        TOP: 'top',
        TOP_LEFT: 'top-left',
        TOP_RIGHT: 'top-right',
        BOTTOM: 'bottom',
        BOTTOM_LEFT: 'bottom-left',
        BOTTOM_RIGHT: 'bottom-right'
    };

    static TYPES = {
        INFO: 'info',
        SUCCESS: 'success',
        WARNING: 'warning',
        ERROR: 'error'
    };

    static container = null;

    constructor(options = {}) {
        super({
            showHeader: false,
            visibility: BasePanel.VISIBILITY.NONE,
            position: ToastPanel.POSITIONS.TOP_RIGHT,
            type: ToastPanel.TYPES.INFO,
            timeout: 3000,
            ...options
        });

        this._applyToastStyle();
    }

    _createElement() {
        const { type } = this.options;

        const toast = document.createElement('div');
        toast.id = this.id;
        toast.className = `toast toast--${type}`;
        toast.style.cssText = `
            display: flex;
            align-items: center;
            gap: 10px;
            padding: 12px 16px;
            border-radius: var(--cl-radius-lg);
            background: var(--cl-bg);
            box-shadow: var(--cl-shadow-md);
            min-width: 250px;
            max-width: 400px;
            opacity: 0;
            transform: translateX(100%);
            transition: all var(--cl-transition-slow);
            margin-bottom: 10px;
        `;

        // 圖示
        const icon = document.createElement('span');
        icon.className = 'toast__icon';
        icon.style.cssText = `display: flex; flex-shrink: 0;`;
        this._mountTypeIcon(icon);
        toast.appendChild(icon);

        // 內容
        const content = document.createElement('div');
        content.className = 'toast__content';
        content.style.cssText = `flex: 1; font-size: var(--cl-font-size-lg); color: var(--cl-text);`;
        toast.appendChild(content);
        this.content = content;

        // 關閉按鈕
        if (this.options.closable) {
            const closeBtn = document.createElement('button');
            closeBtn.type = 'button';
            this._closeIcon = new Icon({
                name: 'close',
                size: 14,
                color: 'var(--cl-text-placeholder)'
            }).mount(closeBtn);
            closeBtn.style.cssText = `
                display: flex;
                border: none;
                background: none;
                cursor: pointer;
                padding: 2px;
                opacity: 0.6;
            `;
            closeBtn.addEventListener('click', () => this.close());
            toast.appendChild(closeBtn);
        }

        return toast;
    }

    _mountTypeIcon(container) {
        const { type } = this.options;
        const colors = {
            info: 'var(--cl-primary)',
            success: 'var(--cl-success)',
            warning: 'var(--cl-warning)',
            error: 'var(--cl-danger)'
        };
        const color = colors[type] || colors.info;

        const iconNames = {
            info: 'info',
            success: 'check',
            warning: 'warning',
            error: 'error'
        };

        this._toastIcon?.destroy();
        this._toastIcon = new Icon({
            name: iconNames[type] || iconNames.info,
            size: 20,
            color
        }).mount(container);
    }

    _applyToastStyle() {
        const { type } = this.options;
        const bgColors = {
            info: 'rgba(var(--cl-primary-rgb), 0.1)',
            success: 'rgba(var(--cl-success-rgb), 0.1)',
            warning: 'rgba(var(--cl-warning-rgb), 0.1)',
            error: 'rgba(var(--cl-danger-rgb), 0.1)'
        };
        this.element.style.background = bgColors[type] || 'var(--cl-bg)';
    }

    _getContainer() {
        const { position } = this.options;
        const containerId = `toast-container-${position}`;

        let container = document.getElementById(containerId);
        if (!container) {
            container = document.createElement('div');
            container.id = containerId;

            const positionStyles = {
                'top': 'top: 20px; left: 50%; transform: translateX(-50%);',
                'top-left': 'top: 20px; left: 20px;',
                'top-right': 'top: 20px; right: 20px;',
                'bottom': 'bottom: 20px; left: 50%; transform: translateX(-50%);',
                'bottom-left': 'bottom: 20px; left: 20px;',
                'bottom-right': 'bottom: 20px; right: 20px;'
            };

            container.style.cssText = `
                position: fixed;
                ${positionStyles[position] || positionStyles['top-right']}
                z-index: 10000;
                display: flex;
                flex-direction: column;
            `;

            document.body.appendChild(container);
        }

        return container;
    }

    show() {
        const container = this._getContainer();
        container.appendChild(this.element);

        // 觸發動畫
        requestAnimationFrame(() => {
            this.element.style.opacity = '1';
            this.element.style.transform = 'translateX(0)';
        });

        // 自動消失
        if (this.options.timeout > 0) {
            this.timeoutId = setTimeout(() => this.close(), this.options.timeout);
        }

        return this;
    }

    close() {
        if (this.timeoutId) {
            clearTimeout(this.timeoutId);
            this.timeoutId = null;
        }

        if (this._closeTransitionId) clearTimeout(this._closeTransitionId);

        this.element.style.opacity = '0';
        this.element.style.transform = 'translateX(100%)';

        this._closeTransitionId = setTimeout(() => {
            this._closeTransitionId = null;
            this.destroy();
        }, 300);

        return this;
    }

    destroy() {
        if (this.timeoutId) clearTimeout(this.timeoutId);
        if (this._closeTransitionId) clearTimeout(this._closeTransitionId);
        this.timeoutId = null;
        this._closeTransitionId = null;
        this._toastIcon?.destroy();
        this._toastIcon = null;
        super.destroy();
    }

    // === 靜態方法 ===

    static show(message, options = {}) {
        const toast = new ToastPanel(options);
        toast.setContent(message);
        toast.show();
        return toast;
    }

    static info(message, options = {}) {
        return ToastPanel.show(message, { type: 'info', ...options });
    }

    static success(message, options = {}) {
        return ToastPanel.show(message, { type: 'success', ...options });
    }

    static warning(message, options = {}) {
        return ToastPanel.show(message, { type: 'warning', ...options });
    }

    static error(message, options = {}) {
        return ToastPanel.show(message, { type: 'error', ...options });
    }
}

export default ToastPanel;
