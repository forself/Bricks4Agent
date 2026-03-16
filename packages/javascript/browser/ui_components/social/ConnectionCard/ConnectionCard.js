/**
 * ConnectionCard - 關聯人員/組織小卡片
 *
 * 類似 LinkedIn「你可能認識的人」卡片，展示頭像、名稱、副標題和標籤。
 * 支援 hover 浮起效果和點擊導航。
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
        this._injectStyles();
    }

    _injectStyles() {
        if (document.getElementById('social-connection-card-styles')) return;

        const style = document.createElement('style');
        style.id = 'social-connection-card-styles';
        style.textContent = `
            .social-connection-card {
                background: var(--cl-bg);
                border-radius: var(--cl-radius-xl);
                padding: 20px 16px;
                text-align: center;
                box-shadow: var(--cl-shadow-sm);
                transition: transform var(--cl-transition), box-shadow var(--cl-transition);
                width: 180px;
                flex-shrink: 0;
            }
            .social-connection-card:hover {
                transform: translateY(-4px);
                box-shadow: var(--cl-shadow-lg);
            }
            .social-connection-card--clickable {
                cursor: pointer;
            }
            .social-connection-card__avatar {
                display: flex;
                justify-content: center;
                margin-bottom: 12px;
            }
            .social-connection-card__name {
                font-size: var(--cl-font-size-lg);
                font-weight: 600;
                color: var(--cl-text);
                margin-bottom: 4px;
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }
            .social-connection-card__subtitle {
                font-size: var(--cl-font-size-sm);
                color: var(--cl-text-secondary);
                margin-bottom: 10px;
                white-space: nowrap;
                overflow: hidden;
                text-overflow: ellipsis;
            }
            .social-connection-card__tags {
                display: flex;
                flex-wrap: wrap;
                gap: 4px;
                justify-content: center;
            }
            .social-connection-card__tag {
                font-size: var(--cl-font-size-xs);
                padding: 2px 8px;
                border-radius: var(--cl-radius-lg);
                background: var(--cl-bg-hover);
                color: var(--cl-text-heading);
                white-space: nowrap;
            }

            /* Grid 容器（供頁面使用） */
            .social-connection-grid {
                display: flex;
                flex-wrap: wrap;
                gap: 16px;
            }
        `;
        document.head.appendChild(style);
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

        if (this.options.onClick && this.element) {
            this.element.addEventListener('click', this.options.onClick);
        }
    }

    /**
     * 批次產生多張卡片的 HTML（Grid 佈局）
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
