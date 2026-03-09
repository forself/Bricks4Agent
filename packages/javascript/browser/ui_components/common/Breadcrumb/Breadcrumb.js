/**
 * Breadcrumb - 麵包屑導航元件
 *
 * 提供路徑導航功能，支援自訂分隔符號和圖示
 *
 * @author MAGI System
 * @version 1.0.0
 */

import { escapeHtml, sanitizeUrl } from '../../utils/security.js';

import Locale from '../../i18n/index.js';
export class Breadcrumb {
    /**
     * @param {Object} options
     * @param {Array} options.items - 導航項目 [{text, href?, icon?, active?}]
     * @param {string} options.separator - 分隔符號
     * @param {string} options.homeIcon - 首頁圖示
     * @param {boolean} options.showHome - 是否顯示首頁
     * @param {string} options.homeHref - 首頁連結
     * @param {Function} options.onNavigate - 導航回調 (item, index)
     */
    constructor(options = {}) {
        this.options = {
            items: [],
            separator: '/',
            homeIcon: '🏠',
            showHome: true,
            homeHref: '#/',
            onNavigate: null,
            ...options
        };

        this.element = null;
        this._injectStyles();
        this._create();
    }

    _injectStyles() {
        if (document.getElementById('breadcrumb-styles')) return;

        const style = document.createElement('style');
        style.id = 'breadcrumb-styles';
        style.textContent = `
            .breadcrumb {
                display: flex;
                align-items: center;
                flex-wrap: wrap;
                gap: 8px;
                font-size: 14px;
                padding: 8px 0;
            }
            .breadcrumb-item {
                display: flex;
                align-items: center;
                gap: 4px;
            }
            .breadcrumb-link {
                color: var(--cl-text-secondary);
                text-decoration: none;
                transition: color 0.2s;
                cursor: pointer;
                display: flex;
                align-items: center;
                gap: 4px;
            }
            .breadcrumb-link:hover {
                color: var(--cl-primary);
            }
            .breadcrumb-text {
                color: var(--cl-text);
                font-weight: 500;
            }
            .breadcrumb-separator {
                color: var(--cl-text-placeholder);
                margin: 0 4px;
                user-select: none;
            }
            .breadcrumb-icon {
                font-size: 14px;
            }
        `;
        document.head.appendChild(style);
    }

    _create() {
        const container = document.createElement('nav');
        container.className = 'breadcrumb';
        container.setAttribute('aria-label', 'breadcrumb');

        this.element = container;
        this._render();
    }

    _render() {
        const { items, separator, showHome, homeIcon, homeHref } = this.options;
        this.element.innerHTML = '';

        // 構建完整項目列表
        const allItems = [];

        if (showHome) {
            allItems.push({
                text: Locale.t('breadcrumb.home'),
                icon: homeIcon,
                href: homeHref,
                isHome: true
            });
        }

        allItems.push(...items);

        allItems.forEach((item, index) => {
            const isLast = index === allItems.length - 1;
            const itemEl = document.createElement('span');
            itemEl.className = 'breadcrumb-item';

            if (isLast && !item.href) {
                // 當前頁面（最後一項且無連結）
                const text = document.createElement('span');
                text.className = 'breadcrumb-text';
                if (item.icon) {
                    text.innerHTML = `<span class="breadcrumb-icon">${escapeHtml(item.icon)}</span> `;
                }
                text.appendChild(document.createTextNode(item.text));
                itemEl.appendChild(text);
            } else {
                // 可點擊的連結
                const link = document.createElement('a');
                link.className = 'breadcrumb-link';
                link.href = sanitizeUrl(item.href) || '#';

                if (item.icon) {
                    const icon = document.createElement('span');
                    icon.className = 'breadcrumb-icon';
                    icon.textContent = item.icon;
                    link.appendChild(icon);
                }

                link.appendChild(document.createTextNode(item.text));

                link.addEventListener('click', (e) => {
                    if (!item.href || link.href === '#') {
                        e.preventDefault();
                    }

                    if (this.options.onNavigate) {
                        this.options.onNavigate(item, index);
                    }
                });

                itemEl.appendChild(link);
            }

            this.element.appendChild(itemEl);

            // 分隔符
            if (!isLast) {
                const sep = document.createElement('span');
                sep.className = 'breadcrumb-separator';
                sep.textContent = separator;
                sep.setAttribute('aria-hidden', 'true');
                this.element.appendChild(sep);
            }
        });
    }

    setItems(items) {
        this.options.items = items;
        this._render();
        return this;
    }

    push(item) {
        this.options.items.push(item);
        this._render();
        return this;
    }

    pop() {
        this.options.items.pop();
        this._render();
        return this;
    }

    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this.element?.remove();
        this.element = null;
    }
}

export default Breadcrumb;
