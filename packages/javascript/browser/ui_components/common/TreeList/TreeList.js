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
        this._rowIcons = new WeakMap();
        this._nodeMeta = new WeakMap();
        this._hoverRow = null;
        this._signature = null;

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
        this._rowIcons = new WeakMap();
        this._hoverRow = null;
        container.innerHTML = '';
        this.data.forEach((node, index, arr) => {
            // Pass isLast for guide rendering
            container.appendChild(this._createNodeElement(node, 0, []));
        });
        this._signature = this._visibleSignature();
    }

    /**
     * 選取態會就地改寫既有 row 的 cssText,因此樣式字串必須與整棵重建時的宣告順序逐字相同,
     * 否則 style 屬性序列化結果會與重建版本不一致。
     */
    _rowStyleText(level, isActive) {
        const theme = this._getTheme();
        const head = isActive
            ? [`background: ${theme.activeBg};`, `color: ${theme.activeText};`]
            : ['background: transparent;', `color: ${theme.text};`];

        if (isActive) {
            if (this.options.theme === 'modern') {
                head.push('font-weight: 600;');
                // Add a left accent bar for modern theme active state
                head.push('border-left: 4px solid var(--cl-primary-dark);');
            }
            if (this.options.theme === 'classic') {
                head.push('outline: 1px dotted var(--cl-text);');
            }
        } else if (this.options.theme === 'modern') {
            head.push('border-left: 4px solid transparent;');
        }

        return head.join(' ') + `
            display: flex;
            align-items: center;
            padding: ${theme.rowPadding};
            padding-left: ${12 + (level * theme.indent)}px;
            cursor: pointer;
            border-radius: ${theme.borderRadius};
            transition: background 0.1s ease, color 0.1s ease;
            position: relative;
        `;
    }

    _toggleStyleText(hasChildren, isActive, isExpanded) {
        return `
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
    }

    _nodeIconStyleText(isActive) {
        const theme = this._getTheme();
        return `
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
    }

    /**
     * @param {Object} node
     * @param {number} level
     * @param {Array} guides - Array of booleans indicating vertical lines needed for parent levels
     */
    _createNodeElement(node, level, guides) {
        const wrapper = document.createElement('div');
        wrapper.className = 'tree-node-wrapper';
        wrapper.style.position = 'relative';

        wrapper.appendChild(this._createRowElement(node, level));

        // 5. 子節點容器 (Children Container)
        const childrenContainer = this._createChildrenElement(node, level);
        if (childrenContainer) wrapper.appendChild(childrenContainer);

        this._nodeMeta.set(wrapper, { node, level });
        return wrapper;
    }

    _createRowElement(node, level) {
        const iconStart = this._icons.length;
        const theme = this._getTheme();

        // 1. 節點本體 (Row)
        const row = document.createElement('div');
        row.className = 'tree-node-row';
        row.dataset.nodeId = String(node.id);
        const isActive = node.id === this.activeId;

        // Style adjustments based on theme
        row.style.cssText = this._rowStyleText(level, isActive);

        // Guide Lines (Classic / Dark)
        if (theme.showGuides && level > 0) {
            // This is a simplified guide line implementation.
            // Real indentation guides usually require absolute positioning calculated from parent.
            // For now, we utilize the padding area.
        }

        // Hover 效果
        row.onmouseenter = () => {
            if (node.id !== this.activeId) {
                this._hoverRow = row;
                row.style.background = theme.hover;
            }
        };
        row.onmouseleave = () => {
            if (this._hoverRow === row) this._hoverRow = null;
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
        arrow.style.cssText = this._toggleStyleText(hasChildren, isActive, isExpanded);

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
        icon.style.cssText = this._nodeIconStyleText(isActive);

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

        this._rowIcons.set(row, this._icons.slice(iconStart));
        this._nodeMeta.set(row, { node, level });
        return row;
    }

    _createChildrenElement(node, level) {
        const hasChildren = node.children && node.children.length > 0;
        if (!hasChildren || !this.expandedIds.has(node.id)) return null;

        const theme = this._getTheme();
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
        return childrenContainer;
    }

    _rowsWithin(element) {
        const rows = [...element.querySelectorAll('.tree-node-row')];
        if (element.classList?.contains?.('tree-node-row')) rows.unshift(element);
        return rows;
    }

    /** 移除子樹前先回收其 Icon,避免脫離 DOM 的實例殘留在 _icons 中造成洩漏。 */
    _releaseSubtree(element) {
        const released = new Set();
        this._rowsWithin(element).forEach((row) => {
            if (this._hoverRow === row) this._hoverRow = null;
            (this._rowIcons.get(row) || []).forEach((icon) => {
                released.add(icon);
                icon.destroy();
            });
            this._rowIcons.delete(row);
        });
        if (released.size) this._icons = this._icons.filter((icon) => !released.has(icon));
    }

    /**
     * row 上掛著 background/color 轉場,轉場期間 getComputedStyle 仍回傳舊色,
     * canvas icon 會取到過期顏色;先關掉轉場強制套用新值,再還原原本的宣告字串
     * (整棵重建時 row 是全新元素、本來就不會跑轉場,如此兩條路徑結果一致)。
     */
    _applyRowStyle(row, styleText) {
        row.style.cssText = `${styleText} transition: none;`;
        if (typeof getComputedStyle === 'function') void getComputedStyle(row).color;
        row.style.cssText = styleText;
    }

    /** 整棵重建會一併抹掉 hover 留下的 inline 底色,定點更新也要跟上,否則會殘留在無關的列上。 */
    _clearHoverResidue() {
        const row = this._hoverRow;
        this._hoverRow = null;
        if (!row || !this.element?.contains(row)) return;
        const meta = this._nodeMeta.get(row);
        if (!meta) return;
        this._applyRowStyle(row, this._rowStyleText(meta.level, meta.node.id === this.activeId));
    }

    _updateSelection(previousId) {
        if (!this.element) return;
        if (previousId === this.activeId) return;
        this._clearHoverResidue();
        this._rowsWithin(this.element).forEach((row) => {
            const meta = this._nodeMeta.get(row);
            if (!meta) return;
            const id = meta.node.id;
            if (id !== previousId && id !== this.activeId) return;

            const isActive = id === this.activeId;
            const hasChildren = meta.node.children && meta.node.children.length > 0;
            const arrow = row.children[0];
            const iconBox = row.children[1];
            this._applyRowStyle(row, this._rowStyleText(meta.level, isActive));
            if (arrow) arrow.style.cssText = this._toggleStyleText(hasChildren, isActive, this.expandedIds.has(id));
            if (iconBox) iconBox.style.cssText = this._nodeIconStyleText(isActive);
            // Icon 以 canvas 繪製,currentColor 不會隨父層色彩自動重繪,必須手動重畫。
            (this._rowIcons.get(row) || []).forEach((icon) => icon.redraw());
        });
    }

    _updateExpansion(id) {
        if (!this.element) return;
        const wrappers = [...this.element.querySelectorAll('.tree-node-wrapper')]
            .filter((wrapper) => this._nodeMeta.get(wrapper)?.node.id === id);

        wrappers.forEach((wrapper) => {
            // 外層同 id 子樹已重建時,內層 wrapper 已脫離,跳過以免產生無主的 Icon
            if (!this.element.contains(wrapper)) return;
            const meta = this._nodeMeta.get(wrapper);
            const row = wrapper.children[0];
            const childrenContainer = wrapper.children[1];
            const node = meta.node;
            const hasChildren = node.children && node.children.length > 0;
            const arrow = row?.children?.[0];
            if (arrow) {
                arrow.style.cssText = this._toggleStyleText(
                    hasChildren,
                    node.id === this.activeId,
                    this.expandedIds.has(node.id)
                );
            }
            if (childrenContainer) {
                this._releaseSubtree(childrenContainer);
                childrenContainer.remove();
            }
            const next = this._createChildrenElement(node, meta.level);
            if (next) wrapper.appendChild(next);
        });
        this._signature = this._visibleSignature();
    }

    _toggleExpand(id) {
        if (this.expandedIds.has(id)) {
            this.expandedIds.delete(id);
        } else {
            this.expandedIds.add(id);
        }
        // 只重建該節點的子樹,其餘節點原封不動
        this._updateExpansion(id);
    }

    _handleSelect(node) {
        const previousId = this.activeId;
        this.activeId = node.id;
        this._updateSelection(previousId); // 更新高亮狀態
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
     * 目前可見節點的簽章。消費端「就地改 data 再重新指定同一個 activeId 以刷新」是既有用法,
     * 用簽章比對偵測資料變動,才不必為此把每次選取都退回整棵重建。
     */
    _visibleSignature() {
        const parts = [];
        const walk = (nodes, level) => {
            (Array.isArray(nodes) ? nodes : []).forEach((node) => {
                const children = Array.isArray(node.children) ? node.children : [];
                parts.push(level, node.id, node.label, node.icon ?? '', children.length);
                if (children.length && this.expandedIds.has(node.id)) walk(children, level + 1);
            });
        };
        walk(this.data, 0);
        return parts.join('\u001f');
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
        const previousId = this.activeId;
        this.activeId = id;
        this.options.activeId = id;
        const expandedBefore = this.expandedIds.size;
        this._expandToId(this.data, id);
        if (this.expandedIds.size !== expandedBefore || this._visibleSignature() !== this._signature) {
            this._renderContent(this.element);
        } else {
            this._updateSelection(previousId);
        }
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
        this._rowIcons = new WeakMap();
        this._hoverRow = null;
        this.element?.remove();
    }
}

export default TreeList;
