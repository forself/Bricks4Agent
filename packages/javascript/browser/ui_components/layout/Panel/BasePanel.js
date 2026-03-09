/**
 * BasePanel
 * 基礎容器類別 - 所有 Panel 的父類
 */

import { PanelManager } from './PanelManager.js';

let panelIdCounter = 0;

export class BasePanel {
    static VISIBILITY = {
        VISIBLE: 'visible',   // 正常顯示
        HIDDEN: 'hidden',     // 隱藏但佔位
        NONE: 'none'          // 隱藏不佔位
    };

    /**
     * @param {Object} options
     * @param {string} options.title - 標題
     * @param {boolean} options.showHeader - 顯示標題列
     * @param {boolean} options.collapsible - 可收折
     * @param {boolean} options.defaultExpanded - 預設展開
     * @param {string} options.visibility - 可見狀態
     * @param {boolean} options.disabled - 禁用
     * @param {boolean} options.focusMode - 聚焦模式
     * @param {string} options.focusScope - 聚焦範圍 'siblings' | 'all' | 'parent'
     * @param {boolean} options.modal - Modal 模式
     * @param {string} options.modalScope - Modal 範圍 'global' | 'parent'
     * @param {boolean} options.inheritDisabled - 繼承父容器禁用狀態
     * @param {boolean} options.autoClose - 點擊外部關閉
     * @param {boolean} options.closable - 顯示關閉按鈕
     * @param {BasePanel} options.parent - 父容器
     * @param {Function} options.onClose - 關閉回調
     * @param {Function} options.onExpand - 展開回調
     * @param {Function} options.onCollapse - 收折回調
     * @param {Function} options.onFocus - 聚焦回調
     */
    constructor(options = {}) {
        this.id = `panel-${++panelIdCounter}`;

        this.options = {
            title: '',
            showHeader: true,
            collapsible: false,
            defaultExpanded: true,
            visibility: BasePanel.VISIBILITY.VISIBLE,
            disabled: false,
            focusMode: false,
            focusScope: 'siblings',
            modal: false,
            modalScope: 'global',
            inheritDisabled: true,
            autoClose: false,
            closable: false,
            parent: null,
            onClose: null,
            onExpand: null,
            onCollapse: null,
            onFocus: null,
            onBlur: null,
            ...options
        };

        this.expanded = this.options.defaultExpanded;
        this.isFocused = false;

        this.element = this._createElement();
        this._bindEvents();

        // 註冊到管理器
        PanelManager.register(this, this.options.parent);

        // 繼承父容器禁用狀態
        if (this.options.inheritDisabled && this.options.parent?.options.disabled) {
            this.setDisabled(true);
        }

        // 應用初始可見狀態
        this._applyVisibility();
    }

    _createElement() {
        const { title, showHeader, closable, collapsible } = this.options;

        // 外層容器
        const container = document.createElement('div');
        container.id = this.id;
        container.className = 'panel';
        container.dataset.panelId = this.id;
        container.style.cssText = `
            position: relative;
            border: 1px solid var(--cl-border-light);
            border-radius: 8px;
            background: var(--cl-bg);
            overflow: hidden;
            transition: all 0.3s ease;
        `;

        // 標題列
        if (showHeader) {
            const header = document.createElement('div');
            header.className = 'panel__header';
            header.style.cssText = `
                display: flex;
                align-items: center;
                justify-content: space-between;
                padding: 12px 16px;
                background: var(--cl-bg-tertiary);
                border-bottom: 1px solid var(--cl-border-light);
                user-select: none;
            `;

            // 左側：標題 + 收折按鈕
            const headerLeft = document.createElement('div');
            headerLeft.className = 'panel__header-left';
            headerLeft.style.cssText = `display: flex; align-items: center; gap: 8px;`;

            if (collapsible) {
                const toggleBtn = document.createElement('button');
                toggleBtn.className = 'panel__toggle';
                toggleBtn.type = 'button';
                toggleBtn.innerHTML = this._getToggleIcon();
                toggleBtn.style.cssText = `
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    width: 24px;
                    height: 24px;
                    border: none;
                    background: none;
                    cursor: pointer;
                    border-radius: 4px;
                    transition: all 0.2s;
                `;
                toggleBtn.addEventListener('click', () => this.toggle());
                headerLeft.appendChild(toggleBtn);
                this.toggleBtn = toggleBtn;
            }

            if (title) {
                const titleEl = document.createElement('span');
                titleEl.className = 'panel__title';
                titleEl.textContent = title;
                titleEl.style.cssText = `font-weight: 600; font-size: 14px; color: var(--cl-text);`;
                headerLeft.appendChild(titleEl);
            }

            header.appendChild(headerLeft);

            // 右側：關閉按鈕
            if (closable) {
                const closeBtn = document.createElement('button');
                closeBtn.className = 'panel__close';
                closeBtn.type = 'button';
                closeBtn.innerHTML = `<svg width="16" height="16" viewBox="0 0 16 16" fill="none">
                    <path d="M4 4L12 12M4 12L12 4" stroke="var(--cl-text-secondary)" stroke-width="2" stroke-linecap="round"/>
                </svg>`;
                closeBtn.style.cssText = `
                    display: flex;
                    align-items: center;
                    justify-content: center;
                    width: 28px;
                    height: 28px;
                    border: none;
                    background: none;
                    cursor: pointer;
                    border-radius: 4px;
                    transition: all 0.2s;
                `;
                closeBtn.addEventListener('mouseenter', () => closeBtn.style.background = 'var(--cl-border-light)');
                closeBtn.addEventListener('mouseleave', () => closeBtn.style.background = 'none');
                closeBtn.addEventListener('click', () => this.close());
                header.appendChild(closeBtn);
            }

            container.appendChild(header);
            this.header = header;
        }

        // 內容區
        const content = document.createElement('div');
        content.className = 'panel__content';
        content.style.cssText = `
            padding: 16px;
            overflow: hidden;
            transition: all 0.3s ease;
        `;
        container.appendChild(content);
        this.content = content;

        // 禁用遮罩
        const overlay = document.createElement('div');
        overlay.className = 'panel__overlay';
        overlay.style.cssText = `
            position: absolute;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(255, 255, 255, 0.7);
            z-index: 10;
            display: none;
            cursor: not-allowed;
        `;
        container.appendChild(overlay);
        this.overlay = overlay;

        return container;
    }

    _getToggleIcon() {
        const rotation = this.expanded ? 'rotate(90deg)' : 'rotate(0deg)';
        return `<svg width="16" height="16" viewBox="0 0 16 16" fill="none" style="transform: ${rotation}; transition: transform 0.2s;">
            <path d="M6 4L10 8L6 12" stroke="var(--cl-text-secondary)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>`;
    }

    _bindEvents() {
        // Focus 模式
        if (this.options.focusMode) {
            this.element.addEventListener('click', (e) => {
                if (!this.isFocused) {
                    this.focus();
                }
            });
        }

        // 點擊外部關閉 - 延遲綁定，避免同一次點擊觸發關閉
        if (this.options.autoClose) {
            // 使用 setTimeout 確保在下一個事件循環才綁定，避免當前點擊事件觸發關閉
            setTimeout(() => {
                this._handleOutsideClick = (e) => {
                    if (!this.element.contains(e.target) &&
                        this.options.visibility === BasePanel.VISIBILITY.VISIBLE) {
                        this.close();
                    }
                };
                document.addEventListener('click', this._handleOutsideClick);
            }, 0);
        }
    }

    _applyVisibility() {
        const { visibility } = this.options;

        switch (visibility) {
            case BasePanel.VISIBILITY.VISIBLE:
                this.element.style.display = '';
                this.element.style.visibility = 'visible';
                break;
            case BasePanel.VISIBILITY.HIDDEN:
                this.element.style.display = '';
                this.element.style.visibility = 'hidden';
                break;
            case BasePanel.VISIBILITY.NONE:
                this.element.style.display = 'none';
                break;
        }
    }

    // === 公開方法 ===

    /**
     * 展開
     */
    expand() {
        if (!this.options.collapsible || this.expanded) return;

        this.expanded = true;
        this.content.style.display = '';
        if (this.toggleBtn) {
            this.toggleBtn.innerHTML = this._getToggleIcon();
        }

        if (this.options.onExpand) {
            this.options.onExpand(this);
        }
    }

    /**
     * 收折
     */
    collapse() {
        if (!this.options.collapsible || !this.expanded) return;

        this.expanded = false;
        this.content.style.display = 'none';
        if (this.toggleBtn) {
            this.toggleBtn.innerHTML = this._getToggleIcon();
        }

        if (this.options.onCollapse) {
            this.options.onCollapse(this);
        }
    }

    /**
     * 切換展開/收折
     */
    toggle() {
        this.expanded ? this.collapse() : this.expand();
    }

    /**
     * 顯示
     */
    show() {
        this.setVisibility(BasePanel.VISIBILITY.VISIBLE);
    }

    /**
     * 隱藏（佔位）
     */
    hide() {
        this.setVisibility(BasePanel.VISIBILITY.HIDDEN);
    }

    /**
     * 隱藏（不佔位）
     */
    remove() {
        this.setVisibility(BasePanel.VISIBILITY.NONE);
    }

    /**
     * 設定可見狀態
     */
    setVisibility(visibility) {
        this.options.visibility = visibility;
        this._applyVisibility();
    }

    /**
     * 設定禁用狀態
     */
    setDisabled(disabled) {
        this.options.disabled = disabled;
        this.overlay.style.display = disabled ? 'block' : 'none';

        // 傳遞給子容器
        if (this.options.inheritDisabled) {
            PanelManager.getChildren(this).forEach(child => {
                if (child.options.inheritDisabled) {
                    child.setDisabled(disabled);
                }
            });
        }
    }

    /**
     * 聚焦
     */
    focus() {
        if (this.isFocused) return;

        this.isFocused = true;
        this.element.style.boxShadow = '0 0 0 3px rgba(var(--cl-primary-rgb), 0.3)';
        PanelManager.enterFocus(this);

        if (this.options.onFocus) {
            this.options.onFocus(this);
        }
    }

    /**
     * 失焦
     */
    blur() {
        if (!this.isFocused) return;

        this.isFocused = false;
        this.element.style.boxShadow = '';
        PanelManager.exitFocus(this);

        if (this.options.onBlur) {
            this.options.onBlur(this);
        }
    }

    /**
     * 關閉
     */
    close() {
        if (this.isFocused) this.blur();
        this.setVisibility(BasePanel.VISIBILITY.NONE);

        if (this.options.onClose) {
            this.options.onClose(this);
        }
    }

    /**
     * 設定內容
     */
    setContent(content) {
        if (typeof content === 'string') {
            this.content.innerHTML = content;
        } else if (content instanceof HTMLElement) {
            this.content.innerHTML = '';
            this.content.appendChild(content);
        }
        return this;
    }

    /**
     * 追加內容
     */
    appendContent(content) {
        if (typeof content === 'string') {
            this.content.insertAdjacentHTML('beforeend', content);
        } else if (content instanceof HTMLElement) {
            this.content.appendChild(content);
        }
        return this;
    }

    /**
     * 追加子 Panel
     */
    appendPanel(panel) {
        // 重新註冊父子關係
        PanelManager.unregister(panel);
        PanelManager.register(panel, this);
        this.content.appendChild(panel.element);
        return this;
    }

    /**
     * 掛載
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    /**
     * 銷毀
     */
    destroy() {
        // 移除事件
        if (this._handleOutsideClick) {
            document.removeEventListener('click', this._handleOutsideClick);
        }

        // 銷毀子容器
        PanelManager.getChildren(this).forEach(child => child.destroy());

        // 從管理器移除
        PanelManager.unregister(this);

        // 從 DOM 移除
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default BasePanel;
