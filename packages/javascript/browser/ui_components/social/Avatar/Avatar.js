/**
 * Avatar - 圓形頭像元件
 *
 * 提供圓形頭像顯示，支援多種尺寸、圖片載入失敗回退顯示姓名首字、角標數字。
 * 樣式自注入，無外部 CSS 依賴。
 *
 * @author MAGI System
 * @version 1.0.0
 *
 * @example
 * const avatar = new Avatar({
 *     src: '/photos/user1.jpg',
 *     alt: '張三',
 *     size: 'lg',
 *     badge: 5,
 *     onClick: () => console.log('clicked')
 * });
 * avatar.mount(document.getElementById('container'));
 */

import { escapeHtml } from '../../utils/security.js';

export class Avatar {
    /** 尺寸對應 px */
    static SIZES = {
        xs: 24,
        sm: 32,
        md: 48,
        lg: 72,
        xl: 96
    };

    /** 回退色彩池（依名稱 hash 選色） */
    static COLORS = [
        'var(--cl-primary)', 'var(--cl-success)', 'var(--cl-warning)', 'var(--cl-purple)',
        'var(--cl-danger)', 'var(--cl-teal)', 'var(--cl-warning)', 'var(--cl-primary)'
    ];

    /**
     * @param {Object} options
     * @param {string} options.src - 圖片 URL
     * @param {string} options.alt - 替代文字（也用於產生回退首字）
     * @param {string} options.size - 尺寸：'xs'|'sm'|'md'|'lg'|'xl'
     * @param {number|null} options.badge - 角標數字，null 不顯示
     * @param {Function|null} options.onClick - 點擊回調
     */
    constructor(options = {}) {
        this.options = {
            src: '',
            alt: '',
            size: 'md',
            badge: null,
            onClick: null,
            ...options
        };

        this.element = null;
        this._injectStyles();
    }

    /** 注入全域樣式（僅一次） */
    _injectStyles() {
        if (document.getElementById('social-avatar-styles')) return;

        const style = document.createElement('style');
        style.id = 'social-avatar-styles';
        style.textContent = `
            .social-avatar {
                position: relative;
                display: inline-flex;
                align-items: center;
                justify-content: center;
                border-radius: var(--cl-radius-round);
                overflow: visible;
                flex-shrink: 0;
                user-select: none;
            }
            .social-avatar--clickable {
                cursor: pointer;
                transition: transform 0.15s ease, box-shadow 0.15s ease;
            }
            .social-avatar--clickable:hover {
                transform: scale(1.05);
                box-shadow: var(--cl-shadow-md);
            }
            .social-avatar__image {
                width: 100%;
                height: 100%;
                border-radius: var(--cl-radius-round);
                object-fit: cover;
            }
            .social-avatar__fallback {
                width: 100%;
                height: 100%;
                border-radius: var(--cl-radius-round);
                display: flex;
                align-items: center;
                justify-content: center;
                color: var(--cl-bg);
                font-weight: 600;
                line-height: 1;
            }
            .social-avatar__badge {
                position: absolute;
                top: -4px;
                right: -4px;
                min-width: 18px;
                height: 18px;
                padding: 0 5px;
                border-radius: var(--cl-radius-xl);
                background: var(--cl-danger);
                color: var(--cl-bg);
                font-size: var(--cl-font-size-xs);
                font-weight: 600;
                display: flex;
                align-items: center;
                justify-content: center;
                border: 2px solid var(--cl-bg);
                line-height: 1;
            }
        `;
        document.head.appendChild(style);
    }

    /** 根據名稱產生穩定的背景色 */
    _getColor(name) {
        if (!name) return Avatar.COLORS[0];
        let hash = 0;
        for (let i = 0; i < name.length; i++) {
            hash = name.charCodeAt(i) + ((hash << 5) - hash);
        }
        return Avatar.COLORS[Math.abs(hash) % Avatar.COLORS.length];
    }

    /** 取得名稱的首字（支援中英文） */
    _getInitial(name) {
        if (!name) return '?';
        return name.charAt(0).toUpperCase();
    }

    /**
     * 產生 HTML 字串（供外部直接嵌入 template）
     * @returns {string}
     */
    toHTML() {
        const { src, alt, size, badge, onClick } = this.options;
        const px = Avatar.SIZES[size] || Avatar.SIZES.md;
        const fontSize = Math.round(px * 0.4);
        const clickClass = onClick ? ' social-avatar--clickable' : '';
        const safeSrc = escapeHtml(src || '');
        const safeAlt = escapeHtml(alt || '');
        const initial = escapeHtml(this._getInitial(alt));
        const bgColor = this._getColor(alt);

        let inner;
        if (src) {
            inner = `<img class="social-avatar__image" src="${safeSrc}" alt="${safeAlt}" onerror="this.style.display='none';this.nextElementSibling.style.display='flex';">
                     <div class="social-avatar__fallback" style="display:none;background:${bgColor};font-size:${fontSize}px;">${initial}</div>`;
        } else {
            inner = `<div class="social-avatar__fallback" style="background:${bgColor};font-size:${fontSize}px;">${initial}</div>`;
        }

        const badgeHTML = (badge != null && badge > 0)
            ? `<span class="social-avatar__badge">${badge > 99 ? '99+' : badge}</span>`
            : '';

        return `<div class="social-avatar${clickClass}" style="width:${px}px;height:${px}px;" title="${safeAlt}">
            ${inner}
            ${badgeHTML}
        </div>`;
    }

    /**
     * 掛載到容器
     * @param {HTMLElement|string} container
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;

        if (!target) return;

        target.innerHTML = this.toHTML();
        this.element = target.querySelector('.social-avatar');

        if (this.options.onClick && this.element) {
            this.element.addEventListener('click', this.options.onClick);
        }
    }

    /** 更新配置並重新渲染 */
    update(options) {
        Object.assign(this.options, options);
        if (this.element && this.element.parentNode) {
            const parent = this.element.parentNode;
            this.mount(parent);
        }
    }

    /** 銷毀 */
    destroy() {
        if (this.element) {
            this.element.remove();
            this.element = null;
        }
    }
}

export default Avatar;
