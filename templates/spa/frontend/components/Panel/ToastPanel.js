/**
 * ToastPanel
 * 通知訊息 - 自動消失的提示
 */

import { BasePanel } from './BasePanel.js';

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
            border-radius: 8px;
            background: white;
            box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
            min-width: 250px;
            max-width: 400px;
            opacity: 0;
            transform: translateX(100%);
            transition: all 0.3s ease;
            margin-bottom: 10px;
        `;

        // 圖示
        const icon = document.createElement('span');
        icon.className = 'toast__icon';
        icon.innerHTML = this._getIcon();
        icon.style.cssText = `display: flex; flex-shrink: 0;`;
        toast.appendChild(icon);

        // 內容
        const content = document.createElement('div');
        content.className = 'toast__content';
        content.style.cssText = `flex: 1; font-size: 14px; color: #333;`;
        toast.appendChild(content);
        this.content = content;

        // 關閉按鈕
        if (this.options.closable) {
            const closeBtn = document.createElement('button');
            closeBtn.type = 'button';
            closeBtn.innerHTML = `<svg width="14" height="14" viewBox="0 0 14 14" fill="none">
                <path d="M3 3L11 11M3 11L11 3" stroke="#999" stroke-width="2" stroke-linecap="round"/>
            </svg>`;
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

    _getIcon() {
        const { type } = this.options;
        const colors = {
            info: '#2196F3',
            success: '#4CAF50',
            warning: '#FF9800',
            error: '#F44336'
        };
        const color = colors[type] || colors.info;

        const icons = {
            info: `<svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                <circle cx="10" cy="10" r="8" stroke="${color}" stroke-width="2"/>
                <path d="M10 9V14M10 6V7" stroke="${color}" stroke-width="2" stroke-linecap="round"/>
            </svg>`,
            success: `<svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                <circle cx="10" cy="10" r="8" stroke="${color}" stroke-width="2"/>
                <path d="M6 10L9 13L14 7" stroke="${color}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>`,
            warning: `<svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                <path d="M10 2L18 17H2L10 2Z" stroke="${color}" stroke-width="2" stroke-linejoin="round"/>
                <path d="M10 8V11M10 14V15" stroke="${color}" stroke-width="2" stroke-linecap="round"/>
            </svg>`,
            error: `<svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                <circle cx="10" cy="10" r="8" stroke="${color}" stroke-width="2"/>
                <path d="M7 7L13 13M7 13L13 7" stroke="${color}" stroke-width="2" stroke-linecap="round"/>
            </svg>`
        };

        return icons[type] || icons.info;
    }

    _applyToastStyle() {
        const { type } = this.options;
        const bgColors = {
            info: 'rgba(33, 150, 243, 0.1)',
            success: 'rgba(76, 175, 80, 0.1)',
            warning: 'rgba(255, 152, 0, 0.1)',
            error: 'rgba(244, 67, 54, 0.1)'
        };
        this.element.style.background = bgColors[type] || 'white';
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
        }

        this.element.style.opacity = '0';
        this.element.style.transform = 'translateX(100%)';

        setTimeout(() => {
            if (this.element?.parentNode) {
                this.element.remove();
            }
        }, 300);

        return this;
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
