/**
 * Avatar - 圓形頭像元件
 *
 * 提供圓形頭像顯示，支援多種尺寸、圖片載入失敗回退顯示姓名首字、角標數字。
 * 樣式以元素層級 CSSOM（style.cssText / .style 指派）套用，
 * 不注入 <style>、不輸出 style="…" 屬性，相容嚴格 CSP（style-src 'self'）。
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
    }

    /**
     * 對 scope 內（含 scope 本身）所有 .social-avatar 節點套用元素層級樣式。
     * CSP 相容：取代原本的 <style> 注入與模板 style 屬性；
     * 供 toHTML() 字串被嵌入 DOM 後的後置樣式化（父元件也會呼叫此方法）。
     * @param {HTMLElement} scope
     */
    static applyStyles(scope) {
        if (!scope || !scope.querySelectorAll) return;
        const roots = [];
        if (scope.classList && scope.classList.contains('social-avatar')) roots.push(scope);
        roots.push(...scope.querySelectorAll('.social-avatar'));
        roots.forEach((root) => Avatar._styleRoot(root));
    }

    /** 套用單一頭像節點的樣式與互動（以 data-avatar-styled 去重） */
    static _styleRoot(root) {
        if (root.dataset.avatarStyled === '1') return;
        root.dataset.avatarStyled = '1';

        const px = parseInt(root.dataset.avatarSize, 10) || Avatar.SIZES.md;
        const clickable = root.classList.contains('social-avatar--clickable');

        root.style.cssText = `
            position: relative;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: ${px}px;
            height: ${px}px;
            border-radius: var(--cl-radius-round);
            overflow: visible;
            flex-shrink: 0;
            user-select: none;
            ${clickable ? 'cursor: pointer; transition: transform 0.15s ease, box-shadow 0.15s ease;' : ''}
        `;

        // :hover（僅 clickable）→ 事件監聽取代 CSS 偽類
        if (clickable) {
            root.addEventListener('mouseenter', () => {
                root.style.transform = 'scale(1.05)';
                root.style.boxShadow = 'var(--cl-shadow-md)';
            });
            root.addEventListener('mouseleave', () => {
                root.style.transform = 'none';
                root.style.boxShadow = 'none';
            });
        }

        const img = root.querySelector('.social-avatar__image');
        const fallback = root.querySelector('.social-avatar__fallback');

        if (img) {
            img.style.cssText = `
                width: 100%;
                height: 100%;
                border-radius: var(--cl-radius-round);
                object-fit: cover;
            `;
            // CSP 相容：JS 綁定取代 inline onerror，載圖失敗切換首字回退
            img.addEventListener('error', () => {
                img.style.display = 'none';
                if (fallback) fallback.style.display = 'flex';
            });
        }

        if (fallback) {
            // 背景色僅接受 var(--cl-*) 形式（來自 COLORS 池），其餘回退預設
            const rawBg = fallback.dataset.avatarBg || '';
            const bg = /^var\(--[A-Za-z0-9-]+\)$/.test(rawBg) ? rawBg : Avatar.COLORS[0];
            const fontSize = parseInt(fallback.dataset.avatarFontSize, 10) || Math.round(px * 0.4);
            fallback.style.cssText = `
                width: 100%;
                height: 100%;
                border-radius: var(--cl-radius-round);
                display: ${fallback.dataset.avatarHidden === '1' ? 'none' : 'flex'};
                align-items: center;
                justify-content: center;
                color: var(--cl-bg);
                font-weight: 600;
                font-size: ${fontSize}px;
                line-height: 1;
            `;
            fallback.style.background = bg;
        }

        const badgeEl = root.querySelector('.social-avatar__badge');
        if (badgeEl) {
            badgeEl.style.cssText = `
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
            `;
        }
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
     * 實例層樣式參數以 data-* 攜帶，嵌入後呼叫 Avatar.applyStyles(container) 套樣式。
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
        const bgColor = escapeHtml(this._getColor(alt));

        let inner;
        if (src) {
            inner = `<img class="social-avatar__image" src="${safeSrc}" alt="${safeAlt}">
                     <div class="social-avatar__fallback" data-avatar-hidden="1" data-avatar-bg="${bgColor}" data-avatar-font-size="${fontSize}">${initial}</div>`;
        } else {
            inner = `<div class="social-avatar__fallback" data-avatar-bg="${bgColor}" data-avatar-font-size="${fontSize}">${initial}</div>`;
        }

        const badgeHTML = (badge != null && badge > 0)
            ? `<span class="social-avatar__badge">${badge > 99 ? '99+' : badge}</span>`
            : '';

        return `<div class="social-avatar${clickClass}" data-avatar-size="${px}" title="${safeAlt}">
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

        // CSP 相容：元素層級 CSSOM 樣式（含 hover 與圖片載入失敗回退綁定）
        Avatar.applyStyles(target);

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
