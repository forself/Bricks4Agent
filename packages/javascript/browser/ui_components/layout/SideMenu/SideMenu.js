/**
 * SideMenu - 側邊選單元件
 *
 * 提供多層級選單、展開收合、圖示支援等功能
 *
 * @author MAGI System
 * @version 1.0.0
 */

import { escapeHtml } from '../../utils/security.js';

import Locale from '../../i18n/index.js';
export class SideMenu {
    /**
     * @param {Object} options
     * @param {Array} options.items - 選單項目 [{id, text, icon?, href?, children?, badge?, disabled?}]
     * @param {string} options.activeId - 當前選中項目 ID
     * @param {boolean} options.collapsed - 是否收合模式
     * @param {boolean} options.accordion - 手風琴模式（同層只展開一個）
     * @param {string} options.width - 選單寬度
     * @param {string} options.collapsedWidth - 收合時寬度
     * @param {Function} options.onSelect - 選擇回調 (item)
     * @param {Function} options.onToggle - 收合回調 (collapsed)
     */
    constructor(options = {}) {
        this.options = {
            items: [],
            activeId: null,
            collapsed: false,
            accordion: true,
            width: '240px',
            collapsedWidth: '64px',
            onSelect: null,
            onToggle: null,
            ...options
        };

        this._expandedIds = new Set();
        this.element = null;
        this._injectStyles();
        this._create();
    }

    _injectStyles() {
        if (document.getElementById('side-menu-styles')) return;

        const style = document.createElement('style');
        style.id = 'side-menu-styles';
        style.textContent = `
            .side-menu {
                display: flex;
                flex-direction: column;
                background: var(--cl-bg);
                border-right: 1px solid var(--cl-border-light);
                height: 100%;
                overflow: hidden;
                transition: width 0.3s ease;
            }
            .side-menu-header {
                display: flex;
                align-items: center;
                justify-content: space-between;
                padding: 16px;
                border-bottom: 1px solid var(--cl-border-light);
            }
            .side-menu-toggle {
                width: 32px;
                height: 32px;
                border: none;
                background: transparent;
                cursor: pointer;
                display: flex;
                align-items: center;
                justify-content: center;
                border-radius: 4px;
                color: var(--cl-text-secondary);
                transition: all 0.2s;
            }
            .side-menu-toggle:hover {
                background: var(--cl-bg-secondary);
                color: var(--cl-text);
            }
            .side-menu-content {
                flex: 1;
                overflow-y: auto;
                overflow-x: hidden;
                padding: 8px 0;
            }
            .side-menu-item {
                position: relative;
            }
            .side-menu-link {
                display: flex;
                align-items: center;
                gap: 12px;
                padding: 12px 16px;
                color: var(--cl-text);
                text-decoration: none;
                cursor: pointer;
                transition: all 0.2s;
                border-left: 3px solid transparent;
                white-space: nowrap;
            }
            .side-menu-link:hover {
                background: var(--cl-bg-secondary);
            }
            .side-menu-link.active {
                background: var(--cl-primary-light);
                border-left-color: var(--cl-primary);
                color: var(--cl-primary);
            }
            .side-menu-link.disabled {
                opacity: 0.5;
                cursor: not-allowed;
            }
            .side-menu-icon {
                width: 20px;
                height: 20px;
                display: flex;
                align-items: center;
                justify-content: center;
                flex-shrink: 0;
                font-size: 16px;
            }
            .side-menu-text {
                flex: 1;
                overflow: hidden;
                text-overflow: ellipsis;
                font-size: 14px;
            }
            .side-menu-badge {
                padding: 2px 8px;
                background: var(--cl-danger);
                color: var(--cl-bg);
                font-size: 11px;
                border-radius: 10px;
                flex-shrink: 0;
            }
            .side-menu-arrow {
                width: 20px;
                height: 20px;
                display: flex;
                align-items: center;
                justify-content: center;
                transition: transform 0.2s;
                flex-shrink: 0;
            }
            .side-menu-arrow.expanded {
                transform: rotate(90deg);
            }
            .side-menu-children {
                overflow: hidden;
                transition: max-height 0.3s ease;
            }
            .side-menu-children .side-menu-link {
                padding-left: 48px;
            }
            .side-menu-children .side-menu-children .side-menu-link {
                padding-left: 64px;
            }

            /* 收合模式 */
            .side-menu.collapsed .side-menu-text,
            .side-menu.collapsed .side-menu-badge,
            .side-menu.collapsed .side-menu-arrow {
                display: none;
            }
            .side-menu.collapsed .side-menu-link {
                justify-content: center;
                padding: 12px;
            }
            .side-menu.collapsed .side-menu-children {
                display: none;
            }

            /* Tooltip for collapsed mode */
            .side-menu.collapsed .side-menu-item:hover::after {
                content: attr(data-title);
                position: absolute;
                left: 100%;
                top: 50%;
                transform: translateY(-50%);
                background: var(--cl-text);
                color: var(--cl-bg);
                padding: 6px 12px;
                border-radius: 4px;
                font-size: 13px;
                white-space: nowrap;
                z-index: 1000;
                margin-left: 8px;
            }
        `;
        document.head.appendChild(style);
    }

    _create() {
        const { width, collapsed } = this.options;

        const container = document.createElement('nav');
        container.className = `side-menu${collapsed ? ' collapsed' : ''}`;
        container.style.width = collapsed ? this.options.collapsedWidth : width;

        // Header with toggle
        const header = document.createElement('div');
        header.className = 'side-menu-header';

        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'side-menu-toggle';
        toggle.innerHTML = collapsed ? '☰' : '◀';
        toggle.title = collapsed ? Locale.t('sideMenu.expand') : Locale.t('sideMenu.collapse');
        toggle.addEventListener('click', () => this.toggle());

        header.appendChild(toggle);
        container.appendChild(header);

        // Content
        const content = document.createElement('div');
        content.className = 'side-menu-content';
        this._renderItems(content, this.options.items);

        container.appendChild(content);
        this.element = container;
        this._contentEl = content;
        this._toggleEl = toggle;
    }

    _renderItems(container, items, level = 0) {
        items.forEach(item => {
            const itemEl = document.createElement('div');
            itemEl.className = 'side-menu-item';
            itemEl.dataset.id = item.id;
            itemEl.dataset.title = item.text;

            const link = document.createElement('a');
            link.className = 'side-menu-link';
            if (item.href) link.href = item.href;
            if (item.disabled) link.classList.add('disabled');
            if (item.id === this.options.activeId) link.classList.add('active');

            // Icon
            if (item.icon) {
                const icon = document.createElement('span');
                icon.className = 'side-menu-icon';
                icon.textContent = item.icon;
                link.appendChild(icon);
            }

            // Text
            const text = document.createElement('span');
            text.className = 'side-menu-text';
            text.textContent = item.text;
            link.appendChild(text);

            // Badge
            if (item.badge !== undefined && item.badge !== null) {
                const badge = document.createElement('span');
                badge.className = 'side-menu-badge';
                badge.textContent = item.badge > 99 ? '99+' : item.badge;
                link.appendChild(badge);
            }

            // Arrow for parent items
            if (item.children && item.children.length > 0) {
                const arrow = document.createElement('span');
                arrow.className = 'side-menu-arrow';
                arrow.innerHTML = '›';
                if (this._expandedIds.has(item.id)) {
                    arrow.classList.add('expanded');
                }
                link.appendChild(arrow);
            }

            // Click handler
            if (!item.disabled) {
                link.addEventListener('click', (e) => {
                    if (item.children && item.children.length > 0) {
                        e.preventDefault();
                        this._toggleExpand(item.id);
                    } else if (!item.href) {
                        e.preventDefault();
                    }

                    if (!item.children || item.children.length === 0) {
                        this._setActive(item.id);
                        if (this.options.onSelect) {
                            this.options.onSelect(item);
                        }
                    }
                });
            }

            itemEl.appendChild(link);

            // Children
            if (item.children && item.children.length > 0) {
                const childrenEl = document.createElement('div');
                childrenEl.className = 'side-menu-children';
                childrenEl.style.maxHeight = this._expandedIds.has(item.id) ? '1000px' : '0';
                this._renderItems(childrenEl, item.children, level + 1);
                itemEl.appendChild(childrenEl);
            }

            container.appendChild(itemEl);
        });
    }

    _toggleExpand(id) {
        if (this._expandedIds.has(id)) {
            this._expandedIds.delete(id);
        } else {
            if (this.options.accordion) {
                // 收合同層其他項目
                this._expandedIds.clear();
            }
            this._expandedIds.add(id);
        }
        this._updateExpanded();
    }

    _updateExpanded() {
        const items = this.element.querySelectorAll('.side-menu-item');
        items.forEach(item => {
            const id = item.dataset.id;
            const arrow = item.querySelector(':scope > .side-menu-link > .side-menu-arrow');
            const children = item.querySelector(':scope > .side-menu-children');

            if (arrow && children) {
                if (this._expandedIds.has(id)) {
                    arrow.classList.add('expanded');
                    children.style.maxHeight = '1000px';
                } else {
                    arrow.classList.remove('expanded');
                    children.style.maxHeight = '0';
                }
            }
        });
    }

    _setActive(id) {
        // 移除所有 active
        this.element.querySelectorAll('.side-menu-link.active').forEach(el => {
            el.classList.remove('active');
        });

        // 設定新 active
        const item = this.element.querySelector(`.side-menu-item[data-id="${id}"]`);
        if (item) {
            item.querySelector(':scope > .side-menu-link')?.classList.add('active');
        }

        this.options.activeId = id;
    }

    toggle() {
        this.options.collapsed = !this.options.collapsed;
        this.element.classList.toggle('collapsed', this.options.collapsed);
        this.element.style.width = this.options.collapsed
            ? this.options.collapsedWidth
            : this.options.width;

        this._toggleEl.innerHTML = this.options.collapsed ? '☰' : '◀';
        this._toggleEl.title = this.options.collapsed ? Locale.t('sideMenu.expand') : Locale.t('sideMenu.collapse');

        if (this.options.onToggle) {
            this.options.onToggle(this.options.collapsed);
        }

        return this;
    }

    expand() {
        if (this.options.collapsed) {
            this.toggle();
        }
        return this;
    }

    collapse() {
        if (!this.options.collapsed) {
            this.toggle();
        }
        return this;
    }

    setActive(id) {
        this._setActive(id);
        return this;
    }

    setItems(items) {
        this.options.items = items;
        this._contentEl.innerHTML = '';
        this._renderItems(this._contentEl, items);
        return this;
    }

    updateBadge(id, badge) {
        const item = this.element.querySelector(`.side-menu-item[data-id="${id}"]`);
        if (item) {
            let badgeEl = item.querySelector('.side-menu-badge');
            if (badge === null || badge === undefined || badge === 0) {
                badgeEl?.remove();
            } else {
                if (!badgeEl) {
                    badgeEl = document.createElement('span');
                    badgeEl.className = 'side-menu-badge';
                    const link = item.querySelector('.side-menu-link');
                    const arrow = link.querySelector('.side-menu-arrow');
                    if (arrow) {
                        link.insertBefore(badgeEl, arrow);
                    } else {
                        link.appendChild(badgeEl);
                    }
                }
                badgeEl.textContent = badge > 99 ? '99+' : badge;
            }
        }
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

export default SideMenu;
