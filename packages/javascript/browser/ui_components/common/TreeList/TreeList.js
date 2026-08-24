/**
 * TreeList Component
 * 現代化極簡風格的導航樹狀列表
 * - 支援無限層級
 * - 葉節點與父節點皆可選取
 * - 展開箭頭獨立控制展開/收合
 * - 目前頁面高亮
 */

import { Icon } from '../Icon/index.js';

export class TreeList {
    /**
     * @param {Object} options
     * @param {Array} options.data - 樹狀資料 [{id, label, icon, children: []}]
     * @param {string} options.activeId - 初始選中的 ID
     * @param {Function} options.onSelect - 點擊節點回調 (node) => void
     * @param {string} options.width - 容器寬度 (預設 240px)
     * @param {string} options.theme - 主題: 'minimal' | 'classic' | 'modern' | 'dark'
     */
    constructor(options = {}) {
        this.options = {
            data: [],
            activeId: null,
            onSelect: null,
            width: '260px', // Slightly wider default for richer themes
            theme: 'modern', // Default to Modern as user complained Minimal was too simple
            ...options
        };

        this.data = this.options.data;
        this.activeId = this.options.activeId;
        this.expandedIds = new Set();
        this._icons = [];

        // Theme Configurations
        this.themes = {
            minimal: {
                bg: 'var(--cl-bg)',
                text: 'var(--cl-bg-code)',
                hover: 'var(--cl-bg-secondary)',
                activeBg: 'rgba(var(--cl-primary-rgb), 0.08)',
                activeText: 'var(--cl-primary)',
                font: '-apple-system, BlinkMacSystemFont, sans-serif',
                rowPadding: '6px 12px',
                borderRadius: 'var(--cl-radius-sm)',
                indent: 20,
                showGuides: false,
                arrowStyle: 'default'
            },
            classic: {
                bg: 'var(--cl-bg)',
                text: 'var(--cl-text)',
                hover: 'var(--cl-border-light)',
                activeBg: 'var(--cl-bg-info-light)',
                activeText: 'var(--cl-text-dark)',
                font: 'Segoe UI, Tahoma, Geneva, Verdana, sans-serif',
                rowPadding: '4px 8px',
                borderRadius: '0px',
                indent: 16,
                showGuides: true, // Show hierarchy lines
                arrowStyle: 'triangle'
            },
            modern: {
                bg: 'var(--cl-bg-tertiary)',
                text: 'var(--cl-text-heading)',
                hover: 'var(--cl-border-subtle)',
                activeBg: 'var(--cl-bg-info-light)',
                activeText: 'var(--cl-primary-dark)',
                font: 'Inter, -apple-system, Roboto, sans-serif',
                rowPadding: '10px 16px',
                borderRadius: '0 24px 24px 0', // Pill shape right
                indent: 24,
                showGuides: false,
                arrowStyle: 'chevron'
            },
            dark: {
                bg: 'var(--cl-bg-dark)',
                text: 'var(--cl-border-dark)',
                hover: 'var(--cl-bg-dark)',
                activeBg: 'var(--cl-text)',
                activeText: 'var(--cl-bg)',
                font: 'Consolas, "Courier New", monospace',
                rowPadding: '6px 12px',
                borderRadius: '0',
                indent: 20,
                showGuides: true,
                arrowStyle: 'carets'
            }
        };

        // 初始化：預設展開所有父節點以顯示 activeId
        if (this.activeId) {
            this._expandToId(this.data, this.activeId);
        }

        this.element = this._createElement();
    }

    _getTheme() {
        return this.themes[this.options.theme] || this.themes.minimal;
    }

    _createElement() {
        const theme = this._getTheme();
        const container = document.createElement('div');
        container.className = `tree-list theme-${this.options.theme}`;
        container.style.cssText = `
            width: ${this.options.width};
            background: ${theme.bg};
            display: flex;
            flex-direction: column;
            gap: ${this.options.theme === 'modern' ? '4px' : '0'};
            font-family: ${theme.font};
            font-size: var(--cl-font-size-lg);
            color: ${theme.text};
            user-select: none;
            height: 100%;
            overflow-y: auto;
            padding: ${this.options.theme === 'modern' ? '12px 12px 12px 0' : '8px 0'};
        `;

        // 渲染內容
        this._renderContent(container);

        return container;
    }

    _renderContent(container) {
        this._icons.forEach((icon) => icon.destroy());
        this._icons = [];
        container.innerHTML = '';
        this.data.forEach((node, index, arr) => {
            // Pass isLast for guide rendering
            container.appendChild(this._createNodeElement(node, 0, []));
        });
    }

    /**
     * @param {Object} node 
     * @param {number} level 
     * @param {Array} guides - Array of booleans indicating vertical lines needed for parent levels
     */
    _createNodeElement(node, level, guides) {
        const theme = this._getTheme();
        const wrapper = document.createElement('div');
        wrapper.className = 'tree-node-wrapper';
        wrapper.style.position = 'relative';

        // 1. 節點本體 (Row)
        const row = document.createElement('div');
        row.className = 'tree-node-row';
        row.dataset.nodeId = String(node.id);
        const isActive = node.id === this.activeId;

        // Style adjustments based on theme
        if (isActive) {
            row.style.background = theme.activeBg;
            row.style.color = theme.activeText;
            if (this.options.theme === 'modern') {
                row.style.fontWeight = '600';
                // Add a left accent bar for modern theme active state
                row.style.borderLeft = '4px solid var(--cl-primary-dark)';
            }
            if (this.options.theme === 'classic') {
                row.style.outline = '1px dotted var(--cl-text)';
            }
        } else {
            row.style.background = 'transparent';
            row.style.color = theme.text;
            if (this.options.theme === 'modern') {
                row.style.borderLeft = '4px solid transparent';
            }
        }

        row.style.cssText += `
            display: flex;
            align-items: center;
            padding: ${theme.rowPadding};
            padding-left: ${12 + (level * theme.indent)}px;
            cursor: pointer;
            border-radius: ${theme.borderRadius};
            transition: background 0.1s ease, color 0.1s ease;
            position: relative;
        `;

        // Guide Lines (Classic / Dark)
        if (theme.showGuides && level > 0) {
            // This is a simplified guide line implementation. 
            // Real indentation guides usually require absolute positioning calculated from parent.
            // For now, we utilize the padding area.
        }

        // Hover 效果
        row.onmouseenter = () => {
            if (node.id !== this.activeId) {
                row.style.background = theme.hover;
            }
        };
        row.onmouseleave = () => {
            if (node.id !== this.activeId) {
                row.style.background = 'transparent';
            }
        };

        // 2. 展開箭頭 (只有當有子節點時顯示)
        const hasChildren = node.children && node.children.length > 0;
        const isExpanded = this.expandedIds.has(node.id);

        const arrow = document.createElement('div');
        arrow.className = 'tree-node-toggle';
        arrow.dataset.nodeId = String(node.id);
        arrow.style.cssText = `
            width: 20px;
            height: 20px;
            display: flex;
            align-items: center;
            justify-content: center;
            margin-right: 4px;
            opacity: ${hasChildren ? (isActive ? 1 : 0.7) : 0};
            transform: ${isExpanded ? 'rotate(90deg)' : 'rotate(0deg)'};
            transition: transform 0.2s;
            cursor: pointer;
        `;

        // Different arrows for themes
        const arrowIcons = {
            triangle: { name: 'triangle-right', size: 10 },
            carets: { name: 'caret-right', size: 10 },
            chevron: { name: 'chevron-right', size: 16 },
            default: { name: 'chevron-right', size: 16 }
        };
        this._mountIcon(arrow, { ...(arrowIcons[theme.arrowStyle] || arrowIcons.default), color: 'currentColor' });

        // 點擊箭頭單獨控制展開/收合
        if (hasChildren) {
            arrow.onclick = (e) => {
                e.stopPropagation();
                this._toggleExpand(node.id);
            };
        }

        row.appendChild(arrow);

        // 3. 圖示 (Icon)
        const icon = document.createElement('div');
        icon.style.cssText = `
            width: 18px;
            height: 18px;
            margin-right: 8px;
            display: flex;
            align-items: center;
            justify-content: center;
            flex-shrink: 0;
            color: ${isActive ? theme.activeText : 'inherit'};
            opacity: ${isActive ? 1 : 0.8};
        `;

        // 預設圖示邏輯：如果有自定義 icon 則顯示，否則視為資料夾或檔案
        if (node.icon) {
            // 判斷是否為 emoji 或 SVG 字串
            this._renderNodeIcon(icon, node.icon);
        } else {
            // Theme specific icons
            if (this.options.theme === 'classic' || this.options.theme === 'dark') {
                // Folder / File specific icons
                if (hasChildren) {
                    // Yellow Folder icon (same for both expanded and collapsed)
                    this._mountIcon(icon, { name: 'folder', size: 16, color: 'var(--cl-warning)' });
                } else {
                    this._mountIcon(icon, { name: 'file', size: 16, color: 'var(--cl-primary)' });
                }
            } else {
                // Minimal / Modern icons
                if (hasChildren) {
                    // Folder icon (same for both expanded and collapsed)
                    this._mountIcon(icon, { name: 'folder', size: 16, color: 'currentColor' });
                } else {
                    // File icon
                    this._mountIcon(icon, { name: 'file', size: 16, color: 'currentColor' });
                }
            }
        }
        row.appendChild(icon);

        // 4. 文字標籤
        const label = document.createElement('span');
        label.textContent = node.label;
        label.style.cssText = `
            flex: 1;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
        `;
        row.appendChild(label);

        // 整行永遠負責選取；父節點的展開/收合由箭頭獨立處理，避免互相搶事件。
        row.onclick = () => {
            this._handleSelect(node);
        };

        wrapper.appendChild(row);

        // 5. 子節點容器 (Children Container)
        if (hasChildren && isExpanded) {
            const childrenContainer = document.createElement('div');
            // Add connecting line for Classic style
            if (theme.showGuides) {
                childrenContainer.style.borderLeft = `1px solid ${theme.hover}`;
                childrenContainer.style.marginLeft = `${12 + (level * theme.indent) + 9}px`; // Align with arrow center

                // Reset indentation for children inside the guide container
                // We need to adjust padding for children because they are inside a new shifted container
                // To keep it simple, we won't strictly use the recursive level for padding if we use borderLeft container
                // actually, keeping the level 0 for children inside the bordered container is a common "nested div" approach.
                // But our _createNodeElement calculates padding based on level. 
                // Let's stick to the padding based approach for now to avoid complexity.
                // Revert logic: don't use the simple borderLeft on container for visual guides mixed with level padding.
                // It complicates the "indent" calculation.
                childrenContainer.style.borderLeft = 'none';
                childrenContainer.style.marginLeft = '0';
            }

            // 遞迴渲染子節點
            node.children.forEach(child => {
                childrenContainer.appendChild(this._createNodeElement(child, level + 1, []));
            });
            wrapper.appendChild(childrenContainer);
        }

        return wrapper;
    }

    _toggleExpand(id) {
        if (this.expandedIds.has(id)) {
            this.expandedIds.delete(id);
        } else {
            this.expandedIds.add(id);
        }
        // 重繪 (簡單起見。優化可做局部 DOM 更新)
        this._renderContent(this.element);
    }

    _handleSelect(node) {
        this.activeId = node.id;
        this._renderContent(this.element); // 更新高亮狀態
        if (this.options.onSelect) {
            this.options.onSelect(node);
        }
    }

    // New API
    setTheme(themeName) {
        if (this.themes[themeName]) {
            this.options.theme = themeName;

            // Update container style
            const theme = this._getTheme();
            this.element.className = `tree-list theme-${themeName}`;
            this.element.style.background = theme.bg;
            this.element.style.fontFamily = theme.font;
            this.element.style.color = theme.text;
            this.element.style.gap = themeName === 'modern' ? '4px' : '0';
            this.element.style.padding = themeName === 'modern' ? '12px 12px 12px 0' : '8px 0';

            this._renderContent(this.element);
        }
    }

    /**
     * 遞迴 helper: 尋找並展開包含 targetId 的路徑
     */
    _expandToId(nodes, targetId) {
        for (const node of nodes) {
            if (node.id === targetId) return true;
            if (node.children) {
                const found = this._expandToId(node.children, targetId);
                if (found) {
                    this.expandedIds.add(node.id);
                    return true;
                }
            }
        }
        return false;
    }

    // Public API

    /**
     * 更新資料
     */
    setData(data) {
        this.data = Array.isArray(data) ? data : [];
        this.options.data = this.data;
        this._renderContent(this.element);
        return this;
    }

    /**
     * 設定選中項目
     */
    setActive(id) {
        this.activeId = id;
        this.options.activeId = id;
        this._expandToId(this.data, id);
        this._renderContent(this.element);
        return this;
    }

    setActiveId(id) {
        return this.setActive(id);
    }

    _mountIcon(container, options) {
        const icon = new Icon(options);
        this._icons.push(icon);
        icon.mount(container);
        return icon;
    }

    _renderNodeIcon(container, value) {
        if (typeof value === 'string') {
            const candidate = value.trim();
            if (Icon.has(candidate)) {
                this._mountIcon(container, { name: candidate, size: 16, color: 'currentColor' });
                return;
            }

            const characters = Array.from(candidate);
            const isShortEmoji = characters.length > 0
                && characters.length <= 2
                && characters.some((character) => /\p{Extended_Pictographic}/u.test(character));
            if (isShortEmoji) {
                container.textContent = candidate;
                return;
            }

            const rejectedKind = /<\s*\/?[a-z][^>]*>/i.test(candidate) ? 'markup/SVG' : 'unknown';
            console.warn(`[TreeList] Rejected ${rejectedKind} node.icon; use an Icon registry name or an emoji of at most two characters.`);
        } else {
            console.warn('[TreeList] Rejected non-string node.icon; use an Icon registry name or an emoji of at most two characters.');
        }

        this._mountIcon(container, { name: 'help', size: 16, color: 'currentColor' });
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

    destroy() {
        this._icons.forEach((icon) => icon.destroy());
        this._icons = [];
        this.element?.remove();
    }
}

export default TreeList;
