/**
 * Notification - 通知訊息元件
 *
 * 提供 Toast、Alert 等通知功能，支援多種類型和位置
 *
 * @author MAGI System
 * @version 1.0.0
 */
import Locale from '../../i18n/index.js';


export class Notification {
    static TYPES = {
        SUCCESS: 'success',
        ERROR: 'error',
        WARNING: 'warning',
        INFO: 'info'
    };

    static POSITIONS = {
        TOP_RIGHT: 'top-right',
        TOP_LEFT: 'top-left',
        TOP_CENTER: 'top-center',
        BOTTOM_RIGHT: 'bottom-right',
        BOTTOM_LEFT: 'bottom-left',
        BOTTOM_CENTER: 'bottom-center'
    };

    static _container = null;
    static _notifications = [];

    /**
     * @param {Object} options
     * @param {string} options.type - 通知類型
     * @param {string} options.title - 標題
     * @param {string} options.message - 訊息內容
     * @param {number} options.duration - 顯示時間 (ms)，0 為不自動關閉
     * @param {string} options.position - 顯示位置
     * @param {boolean} options.closable - 是否可手動關閉
     * @param {Function} options.onClose - 關閉回調
     * @param {string} options.icon - 自訂圖示
     */
    constructor(options = {}) {
        this.options = {
            type: Notification.TYPES.INFO,
            title: '',
            message: '',
            duration: 4000,
            position: Notification.POSITIONS.TOP_RIGHT,
            closable: true,
            onClose: null,
            icon: null,
            ...options
        };

        this.id = `notification-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
        this.element = null;
        this._timeoutId = null;

        this._injectStyles();
        this._ensureContainer();
        this._create();
    }

    _injectStyles() {
        if (document.getElementById('notification-styles')) return;

        const style = document.createElement('style');
        style.id = 'notification-styles';
        style.textContent = `
            .notification-container {
                position: fixed;
                z-index: 10000;
                display: flex;
                flex-direction: column;
                gap: 8px;
                pointer-events: none;
                max-width: 400px;
                width: 100%;
                padding: 16px;
                box-sizing: border-box;
            }
            .notification-container.top-right { top: 0; right: 0; }
            .notification-container.top-left { top: 0; left: 0; }
            .notification-container.top-center { top: 0; left: 50%; transform: translateX(-50%); }
            .notification-container.bottom-right { bottom: 0; right: 0; }
            .notification-container.bottom-left { bottom: 0; left: 0; }
            .notification-container.bottom-center { bottom: 0; left: 50%; transform: translateX(-50%); }

            .notification-item {
                pointer-events: auto;
                display: flex;
                align-items: flex-start;
                gap: 12px;
                padding: 14px 16px;
                background: var(--cl-bg);
                border-radius: 8px;
                box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
                animation: notification-slide-in 0.3s ease-out;
                transition: all 0.3s ease;
                border-left: 4px solid;
            }
            .notification-item.closing {
                animation: notification-slide-out 0.3s ease-in forwards;
            }
            .notification-item.success { border-left-color: var(--cl-success); }
            .notification-item.error { border-left-color: var(--cl-danger); }
            .notification-item.warning { border-left-color: var(--cl-warning); }
            .notification-item.info { border-left-color: var(--cl-primary); }

            .notification-icon {
                flex-shrink: 0;
                width: 24px;
                height: 24px;
                display: flex;
                align-items: center;
                justify-content: center;
                font-size: 18px;
            }
            .notification-icon.success { color: var(--cl-success); }
            .notification-icon.error { color: var(--cl-danger); }
            .notification-icon.warning { color: var(--cl-warning); }
            .notification-icon.info { color: var(--cl-primary); }

            .notification-content {
                flex: 1;
                min-width: 0;
            }
            .notification-title {
                font-weight: 600;
                font-size: 14px;
                color: var(--cl-text);
                margin-bottom: 4px;
            }
            .notification-message {
                font-size: 13px;
                color: var(--cl-text-secondary);
                line-height: 1.4;
                word-break: break-word;
            }
            .notification-close {
                flex-shrink: 0;
                width: 20px;
                height: 20px;
                border: none;
                background: transparent;
                cursor: pointer;
                padding: 0;
                display: flex;
                align-items: center;
                justify-content: center;
                color: var(--cl-text-placeholder);
                font-size: 18px;
                transition: color 0.2s;
                border-radius: 4px;
            }
            .notification-close:hover {
                color: var(--cl-text);
                background: var(--cl-bg-secondary);
            }

            @keyframes notification-slide-in {
                from {
                    opacity: 0;
                    transform: translateX(100%);
                }
                to {
                    opacity: 1;
                    transform: translateX(0);
                }
            }
            @keyframes notification-slide-out {
                from {
                    opacity: 1;
                    transform: translateX(0);
                }
                to {
                    opacity: 0;
                    transform: translateX(100%);
                }
            }
        `;
        document.head.appendChild(style);
    }

    _ensureContainer() {
        const { position } = this.options;
        const containerId = `notification-container-${position}`;

        let container = document.getElementById(containerId);
        if (!container) {
            container = document.createElement('div');
            container.id = containerId;
            container.className = `notification-container ${position}`;
            document.body.appendChild(container);
        }

        this._container = container;
    }

    _getIcon() {
        if (this.options.icon) return this.options.icon;

        const icons = {
            success: '✓',
            error: '✕',
            warning: '⚠',
            info: 'ℹ'
        };
        return icons[this.options.type] || icons.info;
    }

    _create() {
        const { type, title, message, closable } = this.options;

        const item = document.createElement('div');
        item.id = this.id;
        item.className = `notification-item ${type}`;

        // 圖示
        const icon = document.createElement('div');
        icon.className = `notification-icon ${type}`;
        icon.textContent = this._getIcon();
        item.appendChild(icon);

        // 內容
        const content = document.createElement('div');
        content.className = 'notification-content';

        if (title) {
            const titleEl = document.createElement('div');
            titleEl.className = 'notification-title';
            titleEl.textContent = title;
            content.appendChild(titleEl);
        }

        if (message) {
            const messageEl = document.createElement('div');
            messageEl.className = 'notification-message';
            messageEl.textContent = message;
            content.appendChild(messageEl);
        }

        item.appendChild(content);

        // 關閉按鈕
        if (closable) {
            const closeBtn = document.createElement('button');
            closeBtn.className = 'notification-close';
            closeBtn.type = 'button';
            closeBtn.innerHTML = '×';
            closeBtn.addEventListener('click', () => this.close());
            item.appendChild(closeBtn);
        }

        this.element = item;
    }

    show() {
        if (!this.element || !this._container) return this;

        this._container.appendChild(this.element);
        Notification._notifications.push(this);

        // 自動關閉
        if (this.options.duration > 0) {
            this._timeoutId = setTimeout(() => this.close(), this.options.duration);
        }

        return this;
    }

    close() {
        if (!this.element) return;

        // 清除計時器
        if (this._timeoutId) {
            clearTimeout(this._timeoutId);
            this._timeoutId = null;
        }

        // 播放關閉動畫
        this.element.classList.add('closing');

        setTimeout(() => {
            this.element?.remove();
            this.element = null;

            // 從列表移除
            const index = Notification._notifications.indexOf(this);
            if (index > -1) {
                Notification._notifications.splice(index, 1);
            }

            // 回調
            if (this.options.onClose) {
                this.options.onClose();
            }
        }, 300);
    }

    /**
     * 靜態方法：顯示成功通知
     */
    static success(message, options = {}) {
        return new Notification({
            type: Notification.TYPES.SUCCESS,
            title: Locale.t('notification.success'),
            message,
            ...options
        }).show();
    }

    /**
     * 靜態方法：顯示錯誤通知
     */
    static error(message, options = {}) {
        return new Notification({
            type: Notification.TYPES.ERROR,
            title: Locale.t('notification.error'),
            message,
            duration: 0, // 錯誤預設不自動關閉
            ...options
        }).show();
    }

    /**
     * 靜態方法：顯示警告通知
     */
    static warning(message, options = {}) {
        return new Notification({
            type: Notification.TYPES.WARNING,
            title: Locale.t('notification.warning'),
            message,
            ...options
        }).show();
    }

    /**
     * 靜態方法：顯示資訊通知
     */
    static info(message, options = {}) {
        return new Notification({
            type: Notification.TYPES.INFO,
            title: Locale.t('notification.info'),
            message,
            ...options
        }).show();
    }

    /**
     * 靜態方法：關閉所有通知
     */
    static closeAll() {
        [...Notification._notifications].forEach(n => n.close());
    }
}

export default Notification;
