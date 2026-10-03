/**
 * TreeList Component
 * 現代化極簡風格的導航樹狀列表
 * - 支援無限層級
 * - 葉節點與父節點皆可選取
 * - 展開箭頭獨立控制展開/收合
 * - 目前頁面高亮
 * - WAI-ARIA tree 模式：role tree/treeitem/group、roving tabindex、方向鍵 / Home / End / Enter / Space
 * - (選用) checkable：勾選多選，預設上下連動並顯示半選 (mixed)，checkStrictly 可關閉連動
 * - (選用) loadChildren：展開時才非同步載入子節點，含載入中提示與失敗重試
 */

import { Icon } from '../Icon/index.js';
import Locale from '../../i18n/index.js';

export class TreeList {
    /**
     * @param {Object} options
     * @param {Array} options.data - 樹狀資料 [{id, label, icon, children: []}]
     * @param {string} options.activeId - 初始選中的 ID
     * @param {Function} options.onSelect - 點擊節點回調 (node) => void
     * @param {string} options.width - 容器寬度 (預設 260px)
     * @param {string} options.theme - 主題: 'minimal' | 'classic' | 'modern' | 'dark'
     * @param {string} options.ariaLabel - 樹的無障礙名稱 (寫入 role="tree" 的 aria-label)
     * @param {boolean} options.checkable - 每個節點顯示勾選框 (預設 false)
     * @param {boolean} options.checkStrictly - 勾選不做上下連動 (預設 false)
     * @param {Array} options.checkedKeys - 初始勾選的節點 id
     * @param {Function} options.onCheck - 使用者變更勾選時回調 (checkedKeys, { node, checked }) => void
     * @param {Function} options.loadChildren - 延遲載入子節點 async (node) => children[] (預設 null)
     */
    constructor(options = {}) {
        this.options = {
            data: [],
            activeId: null,
            onSelect: null,
            width: '260px', // Slightly wider default for richer themes
            theme: 'modern', // Default to Modern as user complained Minimal was too simple
            ariaLabel: '',
            checkable: false,
            checkStrictly: false,
            checkedKeys: [],
            onCheck: null,
            loadChildren: null,
            ...options
        };

        this.data = this.options.data;
        this.activeId = this.options.activeId;
        this.expandedIds = new Set();
        this._icons = [];
        this._rowIcons = new WeakMap();
        this._rowParts = new WeakMap();
        this._nodeMeta = new WeakMap();
        this._hoverRow = null;
        this._signature = null;
        this._destroyed = false;

        // Roving tabindex：_focusKey 是應持有 tabindex="0" 的節點 id,_tabStopRow 是它目前的 row。
        this._focusKey = this.activeId ?? undefined;
        this._tabStopRow = null;

        // 勾選狀態以節點 id 為鍵;尚未載入(或目前不在資料中)的 id 也會保留,載入後自動套用。
        this._checkedKeys = new Set();
        this._halfCheckedKeys = new Set();
        // 未載入的延遲節點被連動設定狀態時記下來,載入後子節點一律承接(覆蓋先前預設的鍵)。
        this._inheritOverride = new Map();

        // 延遲載入:_loadedChildren 是已載入子節點的快取,_loadStates 記錄進行中/失敗的載入。
        this._loadedChildren = new Map();
        this._loadStates = new Map();
        this._loadSeq = 0;

        this._onKeyDown = (event) => this._handleKeyDown(event);
        this._onFocusIn = (event) => this._handleFocusIn(event);

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

        this._resetCheckedKeys(this.options.checkedKeys);

        // 初始化：預設展開所有父節點以顯示 activeId
        if (this.activeId) {
            this._expandToId(this.data, this.activeId);
        }

        this.element = this._createElement();
        this._ensureTabStop();
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
        container.setAttribute('role', 'tree');
        if (this.options.ariaLabel) container.setAttribute('aria-label', String(this.options.ariaLabel));
        container.addEventListener('keydown', this._onKeyDown);
        container.addEventListener('focusin', this._onFocusIn);

        // 渲染內容
        this._renderContent(container);

        return container;
    }

    _renderContent(container) {
        const hadFocus = this._hasFocusWithin();
        this._icons.forEach((icon) => icon.destroy());
        this._icons = [];
        this._rowIcons = new WeakMap();
        this._rowParts = new WeakMap();
        this._hoverRow = null;
        this._tabStopRow = null;
        this._normalizeCheckState();
        container.innerHTML = '';
        this.data.forEach((node, index, arr) => {
            // Pass isLast for guide rendering
            container.appendChild(this._createNodeElement(node, 0, [], index + 1, arr.length));
        });
        this._signature = this._visibleSignature();
        // 建構期間 this.element 尚未指定,由 constructor 在建立後補做
        if (this.element) this._ensureTabStop(hadFocus);
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
     * @param {number} posinset - 在同層中的位置 (1-based,aria-posinset)
     * @param {number} setsize - 同層節點數 (aria-setsize)
     */
    _createNodeElement(node, level, guides, posinset = 1, setsize = 1) {
        const wrapper = document.createElement('div');
        wrapper.className = 'tree-node-wrapper';
        wrapper.style.position = 'relative';

        // 已展開但尚未載入的延遲節點先發出載入,row 才能一併反映 aria-busy
        this._maybeStartLoad(node);
        wrapper.appendChild(this._createRowElement(node, level, posinset, setsize));

        // 5. 子節點容器 (Children Container)
        const childrenContainer = this._createChildrenElement(node, level);
        if (childrenContainer) wrapper.appendChild(childrenContainer);

        this._nodeMeta.set(wrapper, { node, level, posinset, setsize });
        return wrapper;
    }

    _createRowElement(node, level, posinset = 1, setsize = 1) {
        const iconStart = this._icons.length;
        const theme = this._getTheme();

        // 1. 節點本體 (Row)
        const row = document.createElement('div');
        row.className = 'tree-node-row';
        row.dataset.nodeId = String(node.id);
        const isActive = node.id === this.activeId;

        // Style adjustments based on theme
        row.style.cssText = this._rowStyleText(level, isActive);

        // WAI-ARIA tree 模式:row 即 treeitem,層級與位置以 aria-level / setsize / posinset 明示
        row.setAttribute('role', 'treeitem');
        row.setAttribute('aria-level', String(level + 1));
        row.setAttribute('aria-setsize', String(setsize));
        row.setAttribute('aria-posinset', String(posinset));
        row.setAttribute('aria-selected', isActive ? 'true' : 'false');
        const isTabStop = this._focusKey !== undefined && node.id === this._focusKey;
        row.setAttribute('tabindex', isTabStop ? '0' : '-1');
        if (isTabStop) {
            if (this._tabStopRow && this._tabStopRow !== row) this._tabStopRow.setAttribute('tabindex', '-1');
            this._tabStopRow = row;
        }

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

        // 2. 展開箭頭 (只有當有子節點時顯示;延遲節點在載入前也視為有子節點)
        const hasChildren = this._hasChildren(node);
        const isExpanded = this.expandedIds.has(node.id);

        const arrow = document.createElement('div');
        arrow.className = 'tree-node-toggle';
        arrow.dataset.nodeId = String(node.id);
        arrow.setAttribute('aria-hidden', 'true');
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

        // 2b. 勾選框 (checkable)
        const checkbox = this.options.checkable ? this._createCheckbox(row, node) : null;
        if (checkbox) row.appendChild(checkbox);

        // 3. 圖示 (Icon)
        const icon = document.createElement('div');
        icon.style.cssText = this._nodeIconStyleText(isActive);
        // 裝飾用;treeitem 的名稱只取文字標籤
        icon.setAttribute('aria-hidden', 'true');

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

        this._applyRowAria(row, node);
        this._rowIcons.set(row, this._icons.slice(iconStart));
        this._rowParts.set(row, {
            arrow,
            checkbox,
            iconBox: icon,
            checkState: checkbox ? this._checkStateOf(node) : null
        });
        this._nodeMeta.set(row, { node, level, posinset, setsize });
        return row;
    }

    /** aria-expanded 只放在可展開的節點;aria-busy 只在子節點載入中時出現。 */
    _applyRowAria(row, node) {
        if (this._hasChildren(node)) {
            row.setAttribute('aria-expanded', this.expandedIds.has(node.id) ? 'true' : 'false');
        } else {
            this._removeAttr(row, 'aria-expanded');
        }
        if (this._lazyStatus(node) === 'loading') {
            row.setAttribute('aria-busy', 'true');
        } else {
            this._removeAttr(row, 'aria-busy');
        }
    }

    _removeAttr(element, name) {
        if (element.getAttribute(name) !== null) element.removeAttribute(name);
    }

    /**
     * 勾選框只負責視覺與滑鼠操作;勾選語意由 treeitem 的 aria-checked 提供(避免輔助科技重複朗讀),
     * 並以 tabindex=-1 + 阻止 mousedown 預設行為讓焦點留在 treeitem 上。
     */
    _createCheckbox(row, node) {
        const disabled = this._isCheckDisabled(node);
        const checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.className = 'tree-node-checkbox';
        checkbox.dataset.nodeId = String(node.id);
        checkbox.tabIndex = -1;
        checkbox.disabled = disabled;
        checkbox.setAttribute('aria-hidden', 'true');
        checkbox.style.cssText = `
            width: 16px;
            height: 16px;
            margin: 0 8px 0 0;
            flex-shrink: 0;
            accent-color: var(--cl-primary);
            cursor: ${disabled ? 'not-allowed' : 'pointer'};
        `;
        this._applyCheckState(row, checkbox, this._checkStateOf(node));

        checkbox.onmousedown = (event) => event.preventDefault();
        checkbox.onclick = (event) => {
            event.stopPropagation();
            this._focusRow(row);
            this._toggleCheckFromUser(node);
            // 原生 click 已先切換 checked;不論這次變更是否被接受,都以模型狀態為準重新套用
            const state = this._checkStateOf(node);
            this._applyCheckState(row, checkbox, state);
            const parts = this._rowParts.get(row);
            if (parts) parts.checkState = state;
        };
        return checkbox;
    }

    _applyCheckState(row, checkbox, state) {
        row.setAttribute('aria-checked', state);
        if (checkbox) {
            checkbox.checked = state === 'true';
            checkbox.indeterminate = state === 'mixed';
        }
    }

    _createChildrenElement(node, level) {
        const hasChildren = this._hasChildren(node);
        if (!hasChildren || !this.expandedIds.has(node.id)) return null;

        const theme = this._getTheme();
        const childrenContainer = document.createElement('div');
        childrenContainer.setAttribute('role', 'group');
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

        // 延遲節點:載入中 / 載入失敗時以狀態列取代子節點
        const status = this._lazyStatus(node);
        if (status) {
            childrenContainer.appendChild(this._createStatusRow(node, level, status === 'error' ? 'error' : 'loading'));
            return childrenContainer;
        }

        // 遞迴渲染子節點
        this._childrenOf(node).forEach((child, index, arr) => {
            childrenContainer.appendChild(this._createNodeElement(child, level + 1, [], index + 1, arr.length));
        });
        return childrenContainer;
    }

    /** 延遲載入的「載入中」或「載入失敗 + 重試」列;不是 treeitem,方向鍵不會停在這裡。 */
    _createStatusRow(node, level, kind) {
        const theme = this._getTheme();
        const isError = kind === 'error';
        const row = document.createElement('div');
        row.className = isError ? 'tree-node-error' : 'tree-node-loading';
        if (isError) row.setAttribute('role', 'alert');
        row.style.cssText = `
            display: flex;
            align-items: center;
            gap: 8px;
            padding: ${theme.rowPadding};
            padding-left: ${12 + ((level + 1) * theme.indent) + 24}px;
            font-size: var(--cl-font-size-md);
            color: ${isError ? 'var(--cl-danger)' : 'inherit'};
        `;

        const text = document.createElement('span');
        text.textContent = Locale.t(isError ? 'treeList.loadError' : 'treeList.loading');
        row.appendChild(text);

        if (isError) {
            const retry = document.createElement('button');
            retry.type = 'button';
            retry.className = 'tree-node-retry';
            retry.textContent = Locale.t('treeList.retry');
            retry.setAttribute('aria-label', Locale.t('treeList.retryLabel', { label: String(node.label ?? '') }));
            retry.style.cssText = `
                padding: 2px 10px;
                border: 1px solid var(--cl-border);
                border-radius: var(--cl-radius-sm);
                background: var(--cl-bg);
                color: var(--cl-text);
                font-family: inherit;
                font-size: var(--cl-font-size-sm);
                cursor: pointer;
            `;
            retry.onclick = (event) => {
                event.stopPropagation();
                this._retryLoad(node.id);
            };
            row.appendChild(retry);
        }
        return row;
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
            const hasChildren = this._hasChildren(meta.node);
            const parts = this._rowParts.get(row);
            const arrow = parts?.arrow;
            const iconBox = parts?.iconBox;
            this._applyRowStyle(row, this._rowStyleText(meta.level, isActive));
            row.setAttribute('aria-selected', isActive ? 'true' : 'false');
            if (arrow) arrow.style.cssText = this._toggleStyleText(hasChildren, isActive, this.expandedIds.has(id));
            if (iconBox) iconBox.style.cssText = this._nodeIconStyleText(isActive);
            // Icon 以 canvas 繪製,currentColor 不會隨父層色彩自動重繪,必須手動重畫。
            (this._rowIcons.get(row) || []).forEach((icon) => icon.redraw());
        });
    }

    _updateExpansion(id) {
        if (!this.element) return;
        const hadFocus = this._hasFocusWithin();
        const wrappers = [...this.element.querySelectorAll('.tree-node-wrapper')]
            .filter((wrapper) => this._nodeMeta.get(wrapper)?.node.id === id);

        wrappers.forEach((wrapper) => {
            // 外層同 id 子樹已重建時,內層 wrapper 已脫離,跳過以免產生無主的 Icon
            if (!this.element.contains(wrapper)) return;
            const meta = this._nodeMeta.get(wrapper);
            const row = wrapper.children[0];
            const childrenContainer = wrapper.children[1];
            const node = meta.node;
            this._maybeStartLoad(node);
            const hasChildren = this._hasChildren(node);
            const arrow = this._rowParts.get(row)?.arrow;
            if (arrow) {
                arrow.style.cssText = this._toggleStyleText(
                    hasChildren,
                    node.id === this.activeId,
                    this.expandedIds.has(node.id)
                );
            }
            if (childrenContainer) {
                // 收合時若鍵盤停駐點在被收起的子樹內,改停在收合的節點上
                if (this._tabStopRow && childrenContainer.contains(this._tabStopRow)) this._focusKey = node.id;
                this._releaseSubtree(childrenContainer);
                childrenContainer.remove();
            }
            const next = this._createChildrenElement(node, meta.level);
            if (next) wrapper.appendChild(next);
            if (row) this._applyRowAria(row, node);
        });
        this._signature = this._visibleSignature();
        this._ensureTabStop(hadFocus);
    }

    _toggleExpand(id) {
        if (this.expandedIds.has(id)) {
            this.expandedIds.delete(id);
        } else {
            this.expandedIds.add(id);
            // 重新展開先前載入失敗的延遲節點 = 再試一次
            if (this._loadStates.get(id)?.status === 'error') this._loadStates.delete(id);
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
        if (this._destroyed) return;
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
        const checkable = this.options.checkable;
        const lazy = typeof this.options.loadChildren === 'function';
        const walk = (nodes, level) => {
            (Array.isArray(nodes) ? nodes : []).forEach((node) => {
                const children = this._childList(node);
                parts.push(level, node.id, node.label, node.icon ?? '', children.length);
                if (checkable) parts.push(this._isCheckDisabled(node) ? 1 : 0);
                if (lazy) parts.push(this._hasChildren(node) ? 1 : 0);
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
            const children = this._childrenOf(node);
            if (children) {
                const found = this._expandToId(children, targetId);
                if (found) {
                    this.expandedIds.add(node.id);
                    return true;
                }
            }
        }
        return false;
    }

    // ── 資料走訪 ───────────────────────────────────────────────

    /** 節點目前的子節點:延遲節點載入後取快取,其餘維持原本讀 node.children 的行為。 */
    _childrenOf(node) {
        if (node && this._loadedChildren.has(node.id) && this._isLazyNode(node)) {
            return this._loadedChildren.get(node.id);
        }
        return node?.children;
    }

    _childList(node) {
        const children = this._childrenOf(node);
        return Array.isArray(children) ? children : [];
    }

    _rootList() {
        return Array.isArray(this.data) ? this.data : [];
    }

    _hasChildren(node) {
        const children = this._childrenOf(node);
        if (children && children.length > 0) return true;
        return this._isLazyNode(node) && !this._loadedChildren.has(node.id);
    }

    /** 以前序走訪所有「已載入」節點;visitor 回傳 false 可提前結束。重複參照與循環只走一次。 */
    _walk(visitor) {
        const visited = new Set();
        const roots = this._rootList();
        const stack = [];
        for (let i = roots.length - 1; i >= 0; i -= 1) stack.push(roots[i]);
        while (stack.length) {
            const node = stack.pop();
            if (!node || typeof node !== 'object' || visited.has(node)) continue;
            visited.add(node);
            if (visitor(node) === false) return;
            const children = this._childList(node);
            for (let i = children.length - 1; i >= 0; i -= 1) stack.push(children[i]);
        }
    }

    _findNode(key) {
        let found = null;
        this._walk((node) => {
            if (node.id !== key) return true;
            found = node;
            return false;
        });
        return found;
    }

    // ── 勾選 ─────────────────────────────────────────────────

    _isCheckDisabled(node) {
        return !!(node && (node.disabled || node.disableCheckbox));
    }

    _checkStateOf(node) {
        if (this._checkedKeys.has(node.id)) return 'true';
        return this._halfCheckedKeys.has(node.id) ? 'mixed' : 'false';
    }

    /** 接受陣列或 Set;其他值(null、undefined…)視為空集合。 */
    _checkedKeysInput(keys) {
        if (Array.isArray(keys)) return keys;
        return keys instanceof Set ? [...keys] : [];
    }

    _resetCheckedKeys(keys) {
        const list = this._checkedKeysInput(keys);
        this._checkedKeys = new Set(list.filter((key) => key !== undefined && key !== null));
        this._halfCheckedKeys = new Set();
        this._inheritOverride.clear();
        this._normalizeCheckState();
    }

    /** 由目前的勾選集合推導整棵(已載入)樹:勾選的父節點帶動子孫,再由下而上推導父節點與半選。 */
    _normalizeCheckState() {
        if (!this.options.checkable && this._checkedKeys.size === 0) return;
        if (this.options.checkStrictly) {
            this._halfCheckedKeys = new Set();
            return;
        }
        this._fillDown(this._rootList(), false);
        this._deriveCheckState();
    }

    /**
     * 由上而下:已勾選(且未停用)節點的子孫一律勾選。停用節點本身不變,也不再往下帶動。
     * @param {Array} roots
     * @param {boolean} forced - roots 是否由已勾選的父節點帶動
     */
    _fillDown(roots, forced) {
        const visited = new Set();
        const stack = [];
        for (let i = roots.length - 1; i >= 0; i -= 1) stack.push([roots[i], forced]);
        while (stack.length) {
            const [node, parentChecked] = stack.pop();
            if (!node || typeof node !== 'object' || visited.has(node)) continue;
            visited.add(node);
            const disabled = this._isCheckDisabled(node);
            if (parentChecked && !disabled) this._checkedKeys.add(node.id);
            const pushDown = !disabled && this._checkedKeys.has(node.id);
            const children = this._childList(node);
            for (let i = children.length - 1; i >= 0; i -= 1) stack.push([children[i], pushDown]);
        }
    }

    /** 把 root 與其子孫設為同一狀態(遇到停用節點即略過該子樹);未載入的延遲節點記下承接值。 */
    _cascadeDown(root, checked) {
        const visited = new Set();
        const stack = [root];
        while (stack.length) {
            const node = stack.pop();
            if (!node || typeof node !== 'object' || visited.has(node)) continue;
            visited.add(node);
            if (node !== root && this._isCheckDisabled(node)) continue;
            if (checked) this._checkedKeys.add(node.id);
            else this._checkedKeys.delete(node.id);
            if (this._isLazyNode(node) && !this._loadedChildren.has(node.id)) {
                this._inheritOverride.set(node.id, checked);
            }
            const children = this._childList(node);
            for (let i = children.length - 1; i >= 0; i -= 1) stack.push(children[i]);
        }
    }

    /**
     * 由下而上推導:未停用的父節點在「所有未停用的子節點皆勾選」時勾選,部分勾選或半選時為半選。
     * 停用節點不參與推導,也不被推導;沒有可推導子節點的節點保留自身狀態。
     */
    _deriveCheckState() {
        this._halfCheckedKeys = new Set();
        if (this.options.checkStrictly) return;
        const visited = new Set();
        const roots = this._rootList();
        const stack = [];
        for (let i = roots.length - 1; i >= 0; i -= 1) stack.push({ node: roots[i], entered: false });
        while (stack.length) {
            const frame = stack[stack.length - 1];
            const { node } = frame;
            if (!frame.entered) {
                frame.entered = true;
                if (!node || typeof node !== 'object' || visited.has(node)) {
                    stack.pop();
                    continue;
                }
                visited.add(node);
                const children = this._childList(node);
                for (let i = children.length - 1; i >= 0; i -= 1) stack.push({ node: children[i], entered: false });
                continue;
            }
            stack.pop();
            if (this._isCheckDisabled(node)) continue;
            let total = 0;
            let checkedCount = 0;
            let partial = false;
            this._childList(node).forEach((child) => {
                if (!child || typeof child !== 'object' || this._isCheckDisabled(child)) return;
                total += 1;
                if (this._checkedKeys.has(child.id)) {
                    checkedCount += 1;
                    partial = true;
                } else if (this._halfCheckedKeys.has(child.id)) {
                    partial = true;
                }
            });
            if (total === 0) continue;
            if (checkedCount === total) {
                this._checkedKeys.add(node.id);
            } else {
                this._checkedKeys.delete(node.id);
                if (partial) this._halfCheckedKeys.add(node.id);
            }
        }
    }

    _setNodeChecked(node, checked) {
        if (this.options.checkStrictly) {
            if (checked) this._checkedKeys.add(node.id);
            else this._checkedKeys.delete(node.id);
            return;
        }
        this._cascadeDown(node, checked);
        this._deriveCheckState();
    }

    /** 使用者點擊勾選框或按空白鍵:半選或未勾選 → 勾選,勾選 → 取消;停用節點不變。 */
    _toggleCheckFromUser(node) {
        if (this._destroyed || !this.options.checkable || !node || this._isCheckDisabled(node)) return false;
        const checked = !this._checkedKeys.has(node.id);
        this._setNodeChecked(node, checked);
        this._syncCheckDom();
        if (typeof this.options.onCheck === 'function') {
            this.options.onCheck(this.getCheckedKeys(), { node, checked });
        }
        return true;
    }

    /** 延遲載入完成後讓新子節點承接父節點狀態(僅連動模式),再重新推導祖先。 */
    _applyLoadedCheckState(node) {
        const override = this._inheritOverride.get(node.id);
        this._inheritOverride.delete(node.id);
        if (this.options.checkStrictly) return;
        if (!this.options.checkable && this._checkedKeys.size === 0) return;
        const children = this._childList(node);
        const parentDisabled = this._isCheckDisabled(node);
        if (!parentDisabled && this._checkedKeys.has(node.id)) {
            this._fillDown(children, true);
        } else if (!parentDisabled && override === false) {
            children.forEach((child) => {
                if (child && typeof child === 'object' && !this._isCheckDisabled(child)) this._cascadeDown(child, false);
            });
        } else {
            this._fillDown(children, false);
        }
        this._deriveCheckState();
    }

    /** 只更新狀態有變的可見列(aria-checked 與勾選框),不重建 DOM。 */
    _syncCheckDom() {
        if (!this.options.checkable || !this.element || this._destroyed) return;
        this._rowsWithin(this.element).forEach((row) => {
            const meta = this._nodeMeta.get(row);
            const parts = this._rowParts.get(row);
            if (!meta || !parts?.checkbox) return;
            const state = this._checkStateOf(meta.node);
            if (parts.checkState === state) return;
            this._applyCheckState(row, parts.checkbox, state);
            parts.checkState = state;
        });
    }

    // ── 延遲載入 ───────────────────────────────────────────────

    /** children 為 undefined 且 isLeaf !== true,或 hasChildren: true 且尚無子節點,即為延遲節點。 */
    _isLazyNode(node) {
        if (typeof this.options.loadChildren !== 'function' || !node || typeof node !== 'object') return false;
        if (node.hasChildren === true) return !(Array.isArray(node.children) && node.children.length > 0);
        return node.children === undefined && node.isLeaf !== true;
    }

    /** null = 不是尚未載入的延遲節點;否則為 'idle' | 'loading' | 'error'。 */
    _lazyStatus(node) {
        if (!this._isLazyNode(node) || this._loadedChildren.has(node.id)) return null;
        return this._loadStates.get(node.id)?.status || 'idle';
    }

    _maybeStartLoad(node) {
        if (this._destroyed || !node || !this.expandedIds.has(node.id)) return;
        if (this._lazyStatus(node) === 'idle') this._beginLoad(node);
    }

    /** 每次載入帶一個遞增 token;完成時 token 不符(已重載、setData、destroy)就丟棄結果。 */
    _beginLoad(node) {
        const token = ++this._loadSeq;
        const entry = { status: 'loading', token, promise: null, error: null };
        this._loadStates.set(node.id, entry);
        let result;
        try {
            result = this.options.loadChildren(node);
        } catch (error) {
            result = Promise.reject(error);
        }
        entry.promise = Promise.resolve(result).then(
            (children) => this._finishLoad(node, token, children, null),
            (error) => this._finishLoad(node, token, null, error ?? new Error('loadChildren rejected'))
        );
        return entry.promise;
    }

    _finishLoad(node, token, children, error) {
        const key = node.id;
        const entry = this._loadStates.get(key);
        if (this._destroyed || !entry || entry.token !== token) return false;
        let failure = error;
        if (!failure && !Array.isArray(children)) {
            failure = new TypeError('[TreeList] loadChildren must resolve to an array of nodes.');
            console.warn(failure.message);
        }
        if (failure) {
            entry.status = 'error';
            entry.error = failure;
            this._refreshNode(key);
            return false;
        }
        this._loadStates.delete(key);
        this._loadedChildren.set(key, children);
        this._applyLoadedCheckState(node);
        this._refreshNode(key);
        this._syncCheckDom();
        return true;
    }

    _retryLoad(key) {
        if (this._destroyed || this._loadStates.get(key)?.status !== 'error') return;
        this._loadStates.delete(key);
        // 重試按鈕會隨重建消失,焦點回到該節點
        this._focusKey = key;
        this._refreshNode(key);
    }

    /** 丟棄 root 與其已載入子孫的快取及進行中的載入(進行中的結果回來後會因 token 不符被忽略)。 */
    _forgetLoaded(root) {
        const keys = [];
        const visited = new Set();
        const stack = [root];
        while (stack.length) {
            const node = stack.pop();
            if (!node || typeof node !== 'object' || visited.has(node)) continue;
            visited.add(node);
            keys.push(node.id);
            this._childList(node).forEach((child) => stack.push(child));
        }
        keys.forEach((key) => {
            this._loadedChildren.delete(key);
            this._loadStates.delete(key);
        });
    }

    /** 以同一個 wrapper 重建節點的 row 與子樹(延遲載入狀態改變時用),保留鍵盤焦點。 */
    _refreshNode(key) {
        if (!this.element || this._destroyed) return;
        const hadFocus = this._hasFocusWithin();
        const wrappers = [...this.element.querySelectorAll('.tree-node-wrapper')]
            .filter((wrapper) => this._nodeMeta.get(wrapper)?.node.id === key);
        wrappers.forEach((wrapper) => {
            if (!this.element.contains(wrapper)) return;
            const meta = this._nodeMeta.get(wrapper);
            // 停駐點若在被重建的子樹內(例如焦點在已載入的子節點上時重新載入),改停在此節點
            if (this._tabStopRow && wrapper.contains(this._tabStopRow)) this._focusKey = key;
            this._releaseSubtree(wrapper);
            this._maybeStartLoad(meta.node);
            const row = this._createRowElement(meta.node, meta.level, meta.posinset, meta.setsize);
            const children = this._createChildrenElement(meta.node, meta.level);
            if (children) wrapper.replaceChildren(row, children);
            else wrapper.replaceChildren(row);
        });
        this._signature = this._visibleSignature();
        this._ensureTabStop(hadFocus);
    }

    // ── 鍵盤與焦點 ─────────────────────────────────────────────

    _hasFocusWithin() {
        const element = this.element;
        const active = element?.ownerDocument?.activeElement;
        return !!(active && active !== element && element.contains(active));
    }

    _visibleRows() {
        return this.element ? this._rowsWithin(this.element) : [];
    }

    _rowForKey(key) {
        if (key === undefined || key === null) return null;
        return this._visibleRows().find((row) => this._nodeMeta.get(row)?.node.id === key) || null;
    }

    _rowFromTarget(target) {
        for (let current = target; current && current !== this.element; current = current.parentNode) {
            if (current.classList?.contains?.('tree-node-row')) return current;
        }
        return null;
    }

    _setTabStop(row) {
        if (this._tabStopRow && this._tabStopRow !== row) this._tabStopRow.setAttribute('tabindex', '-1');
        row.setAttribute('tabindex', '0');
        this._tabStopRow = row;
        this._focusKey = this._nodeMeta.get(row)?.node.id;
    }

    /** 確保恰好一列 tabindex="0":優先 _focusKey,其次目前選取的節點,最後是第一列。 */
    _ensureTabStop(restoreFocus = false) {
        const element = this.element;
        if (!element || this._destroyed) return;
        let row = this._tabStopRow;
        const valid = row && element.contains(row) && this._nodeMeta.get(row)?.node.id === this._focusKey;
        if (!valid) {
            row = this._rowForKey(this._focusKey)
                || this._rowForKey(this.activeId)
                || element.querySelector('.tree-node-row');
            if (row) this._setTabStop(row);
            else this._tabStopRow = null;
        }
        if (restoreFocus && row && !this._hasFocusWithin()) row.focus?.();
    }

    _focusRow(row) {
        if (!row) return;
        this._setTabStop(row);
        row.focus?.();
    }

    _firstChildRow(row) {
        const group = row.parentNode?.children?.[1];
        return group?.querySelector?.('.tree-node-row') ?? null;
    }

    _parentRow(row) {
        const group = row.parentNode?.parentNode;
        if (!group || group === this.element) return null;
        const parentWrapper = group.parentNode;
        if (!parentWrapper || !this._nodeMeta.has(parentWrapper)) return null;
        return parentWrapper.children[0] ?? null;
    }

    _handleFocusIn(event) {
        const row = this._rowFromTarget(event.target);
        if (row && this._nodeMeta.has(row)) this._setTabStop(row);
    }

    _handleKeyDown(event) {
        if (this._destroyed || event.defaultPrevented) return;
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        const row = this._rowFromTarget(event.target);
        const meta = row ? this._nodeMeta.get(row) : null;
        if (!meta) return;
        const { node } = meta;
        const expandable = this._hasChildren(node);
        const expanded = expandable && this.expandedIds.has(node.id);

        switch (event.key) {
            case 'ArrowDown':
            case 'ArrowUp': {
                const rows = this._visibleRows();
                this._focusRow(rows[rows.indexOf(row) + (event.key === 'ArrowDown' ? 1 : -1)]);
                break;
            }
            case 'Home':
                this._focusRow(this.element.querySelector('.tree-node-row'));
                break;
            case 'End': {
                const rows = this._visibleRows();
                this._focusRow(rows[rows.length - 1]);
                break;
            }
            case 'ArrowRight':
                if (expanded) this._focusRow(this._firstChildRow(row));
                else if (expandable) this._toggleExpand(node.id);
                break;
            case 'ArrowLeft':
                if (expanded) this._toggleExpand(node.id);
                else this._focusRow(this._parentRow(row));
                break;
            case 'Enter':
                this._handleSelect(node);
                break;
            case ' ':
            case 'Spacebar':
                if (this.options.checkable) this._toggleCheckFromUser(node);
                else this._handleSelect(node);
                break;
            default:
                return;
        }
        event.preventDefault();
    }

    // Public API

    /**
     * 更新資料
     */
    setData(data) {
        if (this._destroyed) return this;
        this.data = Array.isArray(data) ? data : [];
        this.options.data = this.data;
        // 新資料 = 新的延遲載入狀態;仍展開的延遲節點會在重繪時重新載入
        this._loadedChildren.clear();
        this._loadStates.clear();
        this._renderContent(this.element);
        return this;
    }

    /**
     * 設定選中項目
     */
    setActive(id) {
        if (this._destroyed) return this;
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
        // 焦點不在樹內時,Tab 進入樹應落在選取的節點上(該節點可見時)
        if (!this._hasFocusWithin()) {
            const activeRow = this._rowForKey(id);
            if (activeRow) this._setTabStop(activeRow);
        }
        this._ensureTabStop();
        return this;
    }

    setActiveId(id) {
        return this.setActive(id);
    }

    /**
     * 取得勾選的節點 id(已載入的依樹狀順序,之後是尚未載入但被設定為勾選的 id)
     * @param {Object} [options]
     * @param {boolean} [options.leafOnly=false] - 只回傳沒有已載入子節點的節點
     * @param {boolean} [options.includeIndeterminate=false] - 一併回傳半選 (mixed) 的節點
     * @returns {Array}
     */
    getCheckedKeys({ leafOnly = false, includeIndeterminate = false } = {}) {
        const result = [];
        const seen = new Set();
        this._walk((node) => {
            const key = node.id;
            if (seen.has(key)) return true;
            seen.add(key);
            const checked = this._checkedKeys.has(key);
            const half = includeIndeterminate && this._halfCheckedKeys.has(key);
            if (!checked && !half) return true;
            if (leafOnly && this._childList(node).length > 0) return true;
            result.push(key);
            return true;
        });
        this._checkedKeys.forEach((key) => {
            if (!seen.has(key)) result.push(key);
        });
        return result;
    }

    /**
     * 以程式設定勾選(不觸發 onCheck)。連動模式下會帶動子孫並推導父節點;停用節點照給定值設定。
     */
    setCheckedKeys(keys) {
        if (this._destroyed) return this;
        this._resetCheckedKeys(keys);
        this.options.checkedKeys = [...this._checkedKeysInput(keys)];
        this._syncCheckDom();
        return this;
    }

    /** 勾選所有已載入且未停用的節點(不觸發 onCheck,停用節點維持原狀)。 */
    checkAll() {
        if (this._destroyed) return this;
        this._walk((node) => {
            if (!this._isCheckDisabled(node)) this._checkedKeys.add(node.id);
        });
        this._inheritOverride.clear();
        this._deriveCheckState();
        this._syncCheckDom();
        return this;
    }

    /** 取消所有未停用節點的勾選,含尚未載入的 id(不觸發 onCheck,停用節點維持原狀)。 */
    uncheckAll() {
        if (this._destroyed) return this;
        const kept = new Set();
        this._walk((node) => {
            if (this._isCheckDisabled(node) && this._checkedKeys.has(node.id)) kept.add(node.id);
        });
        this._checkedKeys = kept;
        this._inheritOverride.clear();
        this._deriveCheckState();
        this._syncCheckDom();
        return this;
    }

    /**
     * 清除延遲節點(與其子孫)的載入快取;節點展開中則立即重新載入。
     * @returns {Promise<boolean>} 重新載入成功為 true;未展開、找不到、非延遲節點、失敗或被取代為 false
     */
    reloadNode(key) {
        if (this._destroyed || typeof this.options.loadChildren !== 'function') return Promise.resolve(false);
        const node = this._findNode(key);
        if (!node || !this._isLazyNode(node)) return Promise.resolve(false);
        this._forgetLoaded(node);
        this._refreshNode(key);
        const state = this._loadStates.get(key);
        return state?.status === 'loading' && state.promise ? state.promise : Promise.resolve(false);
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
        this._destroyed = true;
        this._icons.forEach((icon) => icon.destroy());
        this._icons = [];
        this._rowIcons = new WeakMap();
        this._rowParts = new WeakMap();
        this._hoverRow = null;
        this._tabStopRow = null;
        // 進行中的載入完成後會因 _destroyed / token 不符而被丟棄
        this._loadStates.clear();
        this._loadedChildren.clear();
        this._inheritOverride.clear();
        if (this.element) {
            this.element.removeEventListener('keydown', this._onKeyDown);
            this.element.removeEventListener('focusin', this._onFocusIn);
        }
        this.element?.remove();
    }
}

export default TreeList;
