/**
 * ConnectionCard - 關聯人員/組織小卡片
 *
 * 類似 LinkedIn「你可能認識的人」卡片，展示頭像、名稱、副標題和標籤。
 * 支援 hover 浮起效果和點擊導航。
 * 樣式以元素層級 CSSOM（style.cssText / .style 指派）套用，
 * 不注入 <style>、不輸出 style="…" 屬性，相容嚴格 CSP（style-src 'self'）。
 *
 * @author MAGI System
 * @version 1.0.0
 *
 * @example
 * const card = new ConnectionCard({
 *     avatar: '/photos/member1.jpg',
 *     name: '張三',
 *     subtitle: '技術部 · 主管',
 *     tags: ['販毒', '勒索'],
 *     onClick: () => navigate('/profile/member/123')
 * });
 * card.mount('#card-container');
 */

import { escapeHtml } from '../../utils/security.js';
import { Avatar } from '../Avatar/index.js';

export class ConnectionCard {
    /**
     * @param {Object} options
     * @param {string} options.avatar - 頭像 URL
     * @param {string} options.name - 名稱
     * @param {string} options.subtitle - 副標題
     * @param {string[]} options.tags - 標籤陣列
     * @param {Function|null} options.onClick - 點擊回調
     */
    constructor(options = {}) {
        this.options = {
            avatar: '',
            name: '',
            subtitle: '',
            tags: [],
            onClick: null,
            ...options
        };

        this.element = null;
    }

    /**
     * 對 scope 內（含 scope 本身）所有卡片與 grid 容器套用元素層級樣式。
     * CSP 相容：取代原本的 <style> 注入；gridHTML() 產出的字串嵌入 DOM 後
     * 也可呼叫此方法完成樣式化。
     * @param {HTMLElement} scope
     */
    static applyStyles(scope) {
        if (!scope || !scope.querySelectorAll) return;

        const grids = [];
        const cards = [];
        if (scope.classList) {
            if (scope.classList.contains('social-connection-grid')) grids.push(scope);
            if (scope.classList.contains('social-connection-card')) cards.push(scope);
        }
        grids.push(...scope.querySelectorAll('.social-connection-grid'));
        cards.push(...scope.querySelectorAll('.social-connection-card'));

        grids.forEach((grid) => {
            grid.style.cssText = `
                display: flex;
                flex-wrap: wrap;
                gap: 16px;
            `;
        });
        cards.forEach((card) => ConnectionCard._styleCard(card));

        // 內嵌的頭像一併樣式化
        Avatar.applyStyles(scope);
    }

    /** 套用單一卡片節點的樣式與 hover 互動（以 data-cc-styled 去重） */
    static _styleCard(card) {
        if (card.dataset.ccStyled === '1') return;
        card.dataset.ccStyled = '1';

        const clickable = card.classList.contains('social-connection-card--clickable');
        card.style.cssText = `
            background: var(--cl-bg);
            border-radius: var(--cl-radius-xl);
            padding: 20px 16px;
            text-align: center;
            box-shadow: var(--cl-shadow-sm);
            transition: transform var(--cl-transition), box-shadow var(--cl-transition);
            width: 180px;
            flex-shrink: 0;
            ${clickable ? 'cursor: pointer;' : ''}
        `;

        // :hover 浮起效果 → 事件監聽取代 CSS 偽類
        card.addEventListener('mouseenter', () => {
            card.style.transform = 'translateY(-4px)';
            card.style.boxShadow = 'var(--cl-shadow-lg)';
        });
        card.addEventListener('mouseleave', () => {
            card.style.transform = 'none';
            card.style.boxShadow = 'var(--cl-shadow-sm)';
        });

        const avatarWrap = card.querySelector('.social-connection-card__avatar');
        if (avatarWrap) {
            avatarWrap.style.cssText = `
                display: flex;
                justify-content: center;
                margin-bottom: 12px;
            `;
        }

        const name = card.querySelector('.social-connection-card__name');
        if (name) {
            name.style.cssText = `
                font-size: var(--cl-font-size-lg);
                font-weight: 600;
                color: var(--cl-text);
                margin-bottom: 4px;
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            `;
        }

        const subtitle = card.querySelector('.social-connection-card__subtitle');
        if (subtitle) {
            subtitle.style.cssText = `
                font-size: var(--cl-font-size-sm);
                color: var(--cl-text-secondary);
                margin-bottom: 10px;
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            `;
        }

        const tagsWrap = card.querySelector('.social-connection-card__tags');
        if (tagsWrap) {
            tagsWrap.style.cssText = `
                display: flex;
                flex-wrap: wrap;
                gap: 4px;
                justify-content: center;
            `;
        }

        card.querySelectorAll('.social-connection-card__tag').forEach((tag) => {
            tag.style.cssText = `
                font-size: var(--cl-font-size-xs);
                padding: 2px 8px;
                border-radius: var(--cl-radius-lg);
                background: var(--cl-bg-hover);
                color: var(--cl-text-heading);
                white-space: nowrap;
            `;
        });
    }

    /**
     * 產生 HTML 字串
     * @returns {string}
     */
    toHTML() {
        const { avatar, name, subtitle, tags, onClick } = this.options;
        const clickClass = onClick ? ' social-connection-card--clickable' : '';
        const safeName = escapeHtml(name);
        const safeSubtitle = escapeHtml(subtitle);

        const avatarInstance = new Avatar({ src: avatar, alt: name, size: 'lg' });
        const avatarHTML = avatarInstance.toHTML();

        const tagsHTML = (tags || []).slice(0, 3).map(tag =>
            `<span class="social-connection-card__tag">${escapeHtml(tag)}</span>`
        ).join('');

        return `<div class="social-connection-card${clickClass}">
            <div class="social-connection-card__avatar">${avatarHTML}</div>
            <div class="social-connection-card__name" title="${safeName}">${safeName}</div>
            <div class="social-connection-card__subtitle" title="${safeSubtitle}">${safeSubtitle}</div>
            ${tagsHTML ? `<div class="social-connection-card__tags">${tagsHTML}</div>` : ''}
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
        this.element = target.querySelector('.social-connection-card');

        // CSP 相容：元素層級 CSSOM 樣式（含 hover 與內嵌頭像）
        ConnectionCard.applyStyles(target);

        if (this.options.onClick && this.element) {
            this.element.addEventListener('click', this.options.onClick);
        }
    }

    /**
     * 批次產生多張卡片的 HTML（Grid 佈局）
     * 嵌入 DOM 後請呼叫 ConnectionCard.applyStyles(container) 套樣式。
     * @param {Object[]} items - 卡片資料陣列
     * @returns {string}
     */
    static gridHTML(items) {
        const cards = items.map(item => {
            const card = new ConnectionCard(item);
            return card.toHTML();
        }).join('');

        return `<div class="social-connection-grid">${cards}</div>`;
    }

    update(options) {
        Object.assign(this.options, options);
        if (this.element && this.element.parentNode) {
            this.mount(this.element.parentNode);
        }
    }

    destroy() {
        if (this.element) {
            this.element.remove();
            this.element = null;
        }
    }
}

export default ConnectionCard;
