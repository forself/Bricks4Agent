/**
 * Notification - 通知訊息元件
 *
 * 提供 Toast、Alert 等通知功能，支援多種類型和位置
 *
 * @author MAGI System
 * @version 1.0.0
 */
import Locale from '../../i18n/index.js';
import { nextUid } from '../../utils/uid.js';


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

        this.id = this.options.id || nextUid('notification');
        this.element = null;
        this._timeoutId = null;
        this._enterAnimation = null;
        this._exitAnimation = null;

        this._ensureContainer();
        this._create();
    }

    /** @private 依通知類型取得強調色（CSS 變數） */
    _getAccentColor() {
        const colors = {
            success: 'var(--cl-success)',
            error: 'var(--cl-danger)',
            warning: 'var(--cl-warning)',
            info: 'var(--cl-primary)'
        };
        return colors[this.options.type] || colors.info;
    }

    _ensureContainer() {
        const { position } = this.options;
        const containerId = `notification-container-${position}`;

        let container = document.getElementById(containerId);
        if (!container) {
            container = document.createElement('div');
            container.id = containerId;
            container.className = `notification-container ${position}`;

            const positionCss = {
                'top-right': 'top: 0; right: 0;',
                'top-left': 'top: 0; left: 0;',
                'top-center': 'top: 0; left: 50%; transform: translateX(-50%);',
                'bottom-right': 'bottom: 0; right: 0;',
                'bottom-left': 'bottom: 0; left: 0;',
                'bottom-center': 'bottom: 0; left: 50%; transform: translateX(-50%);'
            };
            container.style.cssText = 'position: fixed; z-index: 10000; display: flex; flex-direction: column; gap: 8px; pointer-events: none; max-width: 400px; width: 100%; padding: 16px; box-sizing: border-box; '
                + (positionCss[position] || positionCss['top-right']);

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
        const accent = this._getAccentColor();

        const item = document.createElement('div');
        item.id = this.id;
        item.className = `notification-item ${type}`;
        item.style.cssText = `pointer-events: auto; display: flex; align-items: flex-start; gap: 12px; padding: 14px 16px; background: var(--cl-bg); border-radius: var(--cl-radius-lg); box-shadow: var(--cl-shadow-md); transition: all var(--cl-transition-slow); border-left: 4px solid ${accent};`;

        // 圖示
        const icon = document.createElement('div');
        icon.className = `notification-icon ${type}`;
        icon.style.cssText = `flex-shrink: 0; width: 24px; height: 24px; display: flex; align-items: center; justify-content: center; font-size: var(--cl-font-size-2xl); color: ${accent};`;
        icon.textContent = this._getIcon();
        item.appendChild(icon);

        // 內容
        const content = document.createElement('div');
        content.className = 'notification-content';
        content.style.cssText = 'flex: 1; min-width: 0;';

        if (title) {
            const titleEl = document.createElement('div');
            titleEl.className = 'notification-title';
            titleEl.style.cssText = 'font-weight: 600; font-size: var(--cl-font-size-lg); color: var(--cl-text); margin-bottom: 4px;';
            titleEl.textContent = title;
            content.appendChild(titleEl);
        }

        if (message) {
            const messageEl = document.createElement('div');
            messageEl.className = 'notification-message';
            messageEl.style.cssText = 'font-size: var(--cl-font-size-md); color: var(--cl-text-secondary); line-height: 1.4; word-break: break-word;';
            messageEl.textContent = message;
            content.appendChild(messageEl);
        }

        item.appendChild(content);

        // 關閉按鈕
        if (closable) {
            const closeBtn = document.createElement('button');
            closeBtn.className = 'notification-close';
            closeBtn.type = 'button';
            closeBtn.style.cssText = 'flex-shrink: 0; width: 20px; height: 20px; border: none; background: transparent; cursor: pointer; padding: 0; display: flex; align-items: center; justify-content: center; color: var(--cl-text-placeholder); font-size: var(--cl-font-size-2xl); transition: color var(--cl-transition); border-radius: var(--cl-radius-sm);';
            closeBtn.innerHTML = '×';

            // :hover 效果（CSP 相容，改用事件）
            closeBtn.addEventListener('mouseenter', () => {
                closeBtn.style.color = 'var(--cl-text)';
                closeBtn.style.background = 'var(--cl-bg-secondary)';
            });
            closeBtn.addEventListener('mouseleave', () => {
                closeBtn.style.color = 'var(--cl-text-placeholder)';
                closeBtn.style.background = 'transparent';
            });

            closeBtn.addEventListener('click', () => this.close());
            item.appendChild(closeBtn);
        }

        this.element = item;
    }

    show() {
        if (!this.element || !this._container) return this;

        this._container.appendChild(this.element);
        Notification._notifications.push(this);

        // 進場動畫（Web Animations API，CSP 相容）
        if (typeof this.element.animate === 'function') {
            this._enterAnimation = this.element.animate(
                [
                    { opacity: 0, transform: 'translateX(100%)' },
                    { opacity: 1, transform: 'translateX(0)' }
                ],
                { duration: 300, easing: 'ease-out' }
            );
        }

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

        // 播放關閉動畫（Web Animations API，CSP 相容）
        this.element.classList.add('closing');
        if (typeof this.element.animate === 'function') {
            this._enterAnimation?.cancel();
            this._enterAnimation = null;
            this._exitAnimation = this.element.animate(
                [
                    { opacity: 1, transform: 'translateX(0)' },
                    { opacity: 0, transform: 'translateX(100%)' }
                ],
                { duration: 300, easing: 'ease-in', fill: 'forwards' }
            );
        }

        setTimeout(() => {
            this._exitAnimation?.cancel();
            this._exitAnimation = null;
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
