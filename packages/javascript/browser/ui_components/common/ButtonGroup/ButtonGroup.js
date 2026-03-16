/**
 * ButtonGroup Component
 * 按鈕群組容器 - 將相關按鈕組織在一起，支援分隔線
 *
 * @module ButtonGroup
 * @version 1.0.0
 *
 * 特色：
 * - 自動處理按鈕間距
 * - 支援群組間的分隔線
 * - 支援多種佈局方向 (horizontal, vertical)
 * - 支援不同主題
 *
 * @example 基本使用
 * ```javascript
 * import { ButtonGroup } from './index.js';
 * import { EditorButton } from '../EditorButton/index.js';
 *
 * const group = new ButtonGroup({
 *     theme: 'light',
 *     buttons: [
 *         new EditorButton({ type: 'bold' }),
 *         new EditorButton({ type: 'italic' }),
 *         new EditorButton({ type: 'underline' })
 *     ],
 *     showSeparator: true
 * });
 * group.mount('#toolbar');
 * ```
 *
 * @example 垂直佈局
 * ```javascript
 * const verticalGroup = new ButtonGroup({
 *     direction: 'vertical',
 *     theme: 'dark',
 *     buttons: [...]
 * });
 * ```
 */

export class ButtonGroup {
    /**
     * 建立按鈕群組
     * @param {Object} options - 配置選項
     * @param {Array} options.buttons - 按鈕陣列 (EditorButton 實例或配置)
     * @param {string} options.direction - 佈局方向 (horizontal, vertical)
     * @param {string} options.gap - 按鈕間距
     * @param {boolean} options.showSeparator - 是否在群組末端顯示分隔線
     * @param {string} options.separatorColor - 分隔線顏色
     * @param {string} options.theme - 主題 (light, dark, gradient)
     * @param {string} options.align - 對齊方式 (start, center, end)
     * @param {boolean} options.wrap - 是否允許換行
     */
    constructor(options = {}) {
        this.options = {
            buttons: [],
            direction: 'horizontal',
            gap: '4px',
            showSeparator: false,
            separatorColor: null,
            theme: 'light',
            align: 'start',
            wrap: false,
            ...options
        };

        this.buttons = [];
        this.element = this._createElement();
    }

    _getThemeStyles() {
        const themes = {
            light: {
                separatorColor: 'var(--cl-border-medium)',
                bg: 'transparent'
            },
            dark: {
                separatorColor: 'var(--cl-divider-inverse)',
                bg: 'transparent'
            },
            gradient: {
                separatorColor: 'var(--cl-divider-inverse)',
                bg: 'transparent'
            }
        };
        return themes[this.options.theme] || themes.light;
    }

    _createElement() {
        const { direction, gap, showSeparator, separatorColor, align, wrap } = this.options;
        const themeStyles = this._getThemeStyles();

        const group = document.createElement('div');
        group.className = 'button-group';

        const alignMap = { start: 'flex-start', center: 'center', end: 'flex-end' };

        group.style.cssText = `
            display: inline-flex;
            flex-direction: ${direction === 'vertical' ? 'column' : 'row'};
            align-items: ${alignMap[align] || 'flex-start'};
            gap: ${gap};
            ${wrap ? 'flex-wrap: wrap;' : ''}
        `;

        // 加入按鈕
        this.options.buttons.forEach(btn => {
            if (btn && btn.element) {
                group.appendChild(btn.element);
                this.buttons.push(btn);
            } else if (btn instanceof HTMLElement) {
                group.appendChild(btn);
            }
        });

        // 分隔線
        if (showSeparator) {
            const separator = document.createElement('div');
            separator.className = 'button-group__separator';
            const sepColor = separatorColor || themeStyles.separatorColor;

            if (direction === 'vertical') {
                separator.style.cssText = `
                    width: 100%;
                    height: 1px;
                    background: ${sepColor};
                    margin: 4px 0;
                `;
            } else {
                separator.style.cssText = `
                    width: 1px;
                    height: 20px;
                    background: ${sepColor};
                    margin: 0 4px;
                    align-self: center;
                `;
            }
            group.appendChild(separator);
        }

        return group;
    }

    /**
     * 新增按鈕
     * @param {EditorButton|HTMLElement} button - 按鈕
     */
    addButton(button) {
        if (button && button.element) {
            this.element.appendChild(button.element);
            this.buttons.push(button);
        } else if (button instanceof HTMLElement) {
            this.element.appendChild(button);
        }
        return this;
    }

    /**
     * 移除按鈕
     * @param {number} index - 索引
     */
    removeButton(index) {
        if (this.buttons[index]) {
            const btn = this.buttons[index];
            if (btn.element && btn.element.parentNode) {
                btn.element.parentNode.removeChild(btn.element);
            }
            this.buttons.splice(index, 1);
        }
        return this;
    }

    /**
     * 清空所有按鈕
     */
    clear() {
        this.buttons.forEach(btn => {
            if (btn.destroy) btn.destroy();
        });
        this.buttons = [];
        this.element.innerHTML = '';
        return this;
    }

    /**
     * 掛載到容器
     */
    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
        }
        return this;
    }

    /**
     * 銷毀元件
     */
    destroy() {
        this.clear();
        if (this.element && this.element.parentNode) {
            this.element.parentNode.removeChild(this.element);
        }
    }
}

/**
 * EditorToolbar Component
 * 編輯器工具列 - 組合多個 ButtonGroup
 */
export class EditorToolbar {
    /**
     * 建立編輯器工具列
     * @param {Object} options - 配置選項
     * @param {Array} options.groups - ButtonGroup 實例陣列
     * @param {string} options.theme - 主題 (light, dark, gradient)
     * @param {string} options.position - 位置 (top, bottom)
     * @param {boolean} options.sticky - 是否黏著
     * @param {string} options.background - 背景色
     * @param {string} options.padding - 內邊距
     */
    constructor(options = {}) {
        this.options = {
            groups: [],
            theme: 'light',
            position: 'top',
            sticky: false,
            background: null,
            padding: '8px 12px',
            ...options
        };

        this.groups = [];
        this.element = this._createElement();
    }

    _getThemeStyles() {
        const themes = {
            light: {
                bg: 'var(--cl-bg)',
                border: '1px solid var(--cl-border-medium)',
                shadow: 'var(--cl-shadow-sm)'
            },
            dark: {
                bg: 'var(--cl-bg-dark)',
                border: '1px solid var(--cl-border)',
                shadow: 'var(--cl-shadow-sm)'
            },
            gradient: {
                bg: 'linear-gradient(135deg, var(--cl-gradient-start) 0%, var(--cl-gradient-end) 100%)',
                border: 'none',
                shadow: 'var(--cl-shadow-md)'
            }
        };
        return themes[this.options.theme] || themes.light;
    }

    _createElement() {
        const { groups, position, sticky, background, padding } = this.options;
        const themeStyles = this._getThemeStyles();

        const toolbar = document.createElement('div');
        toolbar.className = 'editor-toolbar';

        toolbar.style.cssText = `
            display: flex;
            align-items: center;
            gap: 8px;
            padding: ${padding};
            background: ${background || themeStyles.bg};
            border: ${themeStyles.border};
            ${position === 'top' ? 'border-radius: var(--cl-radius-lg) var(--cl-radius-lg) 0 0;' : 'border-radius: 0 0 var(--cl-radius-lg) var(--cl-radius-lg);'}
            box-shadow: ${themeStyles.shadow};
            flex-wrap: wrap;
            ${sticky ? 'position: sticky; top: 0; z-index: 100;' : ''}
        `;

        // 加入群組
        groups.forEach((group, index) => {
            if (group && group.element) {
                toolbar.appendChild(group.element);
                this.groups.push(group);
            }

            // 在群組間加入分隔線 (除了最後一個)
            if (index < groups.length - 1) {
                const separator = document.createElement('div');
                separator.className = 'editor-toolbar__separator';
                separator.style.cssText = `
                    width: 1px;
                    height: 24px;
                    background: ${this.options.theme === 'light' ? 'var(--cl-border-medium)' : 'var(--cl-divider-inverse)'};
                    margin: 0 4px;
                `;
                toolbar.appendChild(separator);
            }
        });

        return toolbar;
    }

    /**
     * 新增群組
     */
    addGroup(group) {
        if (group && group.element) {
            // 加入分隔線
            if (this.groups.length > 0) {
                const separator = document.createElement('div');
                separator.className = 'editor-toolbar__separator';
                separator.style.cssText = `
                    width: 1px;
                    height: 24px;
                    background: ${this.options.theme === 'light' ? 'var(--cl-border-medium)' : 'var(--cl-divider-inverse)'};
                    margin: 0 4px;
                `;
                this.element.appendChild(separator);
            }
            this.element.appendChild(group.element);
            this.groups.push(group);
        }
        return this;
    }

    /**
     * 掛載到容器
     */
    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
        }
        return this;
    }

    /**
     * 銷毀
     */
    destroy() {
        this.groups.forEach(g => g.destroy?.());
        this.groups = [];
        if (this.element && this.element.parentNode) {
            this.element.parentNode.removeChild(this.element);
        }
    }
}

export default ButtonGroup;
