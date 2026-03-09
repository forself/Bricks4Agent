/**
 * FunctionMenu - 功能選單容器組件
 * 用於展示功能按鈕、操作選單、快捷功能等
 *
 * @author MAGI System
 * @version 1.0.0
 */

export class FunctionMenu {
    /**
     * 建立功能選單容器
     * @param {Object} options - 配置選項
     * @param {string} options.containerId - 容器元素 ID
     * @param {Array} options.items - 初始選單項目配置
     * @param {string} options.layout - 布局方式 ('horizontal', 'vertical', 'grid')
     * @param {number} options.columns - 欄位數量（grid 模式）
     * @param {string} options.size - 按鈕尺寸 ('small', 'medium', 'large')
     * @param {boolean} options.showLabels - 是否顯示標籤
     * @param {boolean} options.showBadges - 是否顯示徽章
     * @param {Function} options.onItemClick - 項目點擊回調
     */
    constructor(options = {}) {
        this.containerId = options.containerId || 'function-menu';
        this.items = options.items || [];
        this.layout = options.layout || 'horizontal';
        this.columns = options.columns || 4;
        this.size = options.size || 'medium';
        this.showLabels = options.showLabels !== undefined ? options.showLabels : true;
        this.showBadges = options.showBadges !== undefined ? options.showBadges : true;
        this.onItemClick = options.onItemClick || null;

        this.itemMap = new Map();

        this.init();
    }

    /**
     * 初始化容器
     */
    init() {
        const container = document.getElementById(this.containerId);
        if (!container) {
            console.error(`Container with id "${this.containerId}" not found`);
            return;
        }

        container.className = `function-menu function-menu-${this.layout} function-menu-${this.size}`;

        if (this.layout === 'grid') {
            container.style.gridTemplateColumns = `repeat(${this.columns}, 1fr)`;
        }

        // 添加初始項目
        this.items.forEach(item => this.addItem(item));
    }

    /**
     * 添加選單項目
     * @param {Object} item - 選單項目配置
     * @param {string} item.id - 項目 ID
     * @param {string} item.label - 項目標籤
     * @param {string} item.icon - 圖示類名
     * @param {string} item.color - 顏色主題
     * @param {number} item.badge - 徽章數字
     * @param {boolean} item.disabled - 是否禁用
     * @param {string} item.tooltip - 提示文字
     * @param {Function} item.onClick - 點擊回調
     */
    addItem(item) {
        if (!item.id) {
            console.error('Menu item must have an id');
            return;
        }

        if (this.itemMap.has(item.id)) {
            console.warn(`Menu item with id "${item.id}" already exists`);
            return;
        }

        const container = document.getElementById(this.containerId);
        const itemElement = this.createItemElement(item);

        container.appendChild(itemElement);
        this.itemMap.set(item.id, { element: itemElement, data: item });
    }

    /**
     * 創建選單項目元素
     * @private
     */
    createItemElement(item) {
        const itemEl = document.createElement('div');
        itemEl.className = 'menu-item';
        itemEl.dataset.itemId = item.id;

        if (item.disabled) {
            itemEl.classList.add('disabled');
        }

        if (item.color) {
            itemEl.dataset.color = item.color;
        }

        if (item.tooltip) {
            itemEl.title = item.tooltip;
        }

        // 按鈕元素
        const button = document.createElement('button');
        button.className = 'menu-item-button';
        button.type = 'button';

        if (item.disabled) {
            button.disabled = true;
        }

        // 圖示
        if (item.icon) {
            const iconWrapper = document.createElement('div');
            iconWrapper.className = 'menu-item-icon';

            const icon = document.createElement('i');
            icon.className = item.icon;
            iconWrapper.appendChild(icon);

            // 徽章
            if (item.badge !== undefined && item.badge > 0 && this.showBadges) {
                const badge = document.createElement('span');
                badge.className = 'menu-item-badge';
                badge.textContent = item.badge > 99 ? '99+' : item.badge;
                iconWrapper.appendChild(badge);
            }

            button.appendChild(iconWrapper);
        }

        // 標籤
        if (this.showLabels && item.label) {
            const label = document.createElement('span');
            label.className = 'menu-item-label';
            label.textContent = item.label;
            button.appendChild(label);
        }

        // 點擊事件
        button.onclick = () => {
            if (!item.disabled) {
                if (item.onClick) {
                    item.onClick({ itemId: item.id, item });
                }
                if (this.onItemClick) {
                    this.onItemClick({ itemId: item.id, item });
                }
            }
        };

        itemEl.appendChild(button);

        return itemEl;
    }

    /**
     * 移除選單項目
     * @param {string} itemId - 項目 ID
     */
    removeItem(itemId) {
        if (!this.itemMap.has(itemId)) {
            console.error(`Menu item with id "${itemId}" not found`);
            return;
        }

        const { element } = this.itemMap.get(itemId);
        element.remove();
        this.itemMap.delete(itemId);
    }

    /**
     * 更新項目徽章
     * @param {string} itemId - 項目 ID
     * @param {number} badge - 徽章數字
     */
    updateBadge(itemId, badge) {
        if (!this.itemMap.has(itemId)) {
            console.error(`Menu item with id "${itemId}" not found`);
            return;
        }

        const { element, data } = this.itemMap.get(itemId);
        data.badge = badge;

        let badgeEl = element.querySelector('.menu-item-badge');
        const iconWrapper = element.querySelector('.menu-item-icon');

        if (badge === 0 || badge === null || badge === undefined) {
            // 移除徽章
            if (badgeEl) {
                badgeEl.remove();
            }
        } else {
            // 更新或建立徽章
            if (!badgeEl && iconWrapper) {
                badgeEl = document.createElement('span');
                badgeEl.className = 'menu-item-badge';
                iconWrapper.appendChild(badgeEl);
            }
            if (badgeEl) {
                badgeEl.textContent = badge > 99 ? '99+' : badge;
            }
        }
    }

    /**
     * 啟用項目
     * @param {string} itemId - 項目 ID
     */
    enableItem(itemId) {
        if (!this.itemMap.has(itemId)) {
            console.error(`Menu item with id "${itemId}" not found`);
            return;
        }

        const { element, data } = this.itemMap.get(itemId);
        data.disabled = false;

        element.classList.remove('disabled');
        const button = element.querySelector('button');
        if (button) {
            button.disabled = false;
        }
    }

    /**
     * 禁用項目
     * @param {string} itemId - 項目 ID
     */
    disableItem(itemId) {
        if (!this.itemMap.has(itemId)) {
            console.error(`Menu item with id "${itemId}" not found`);
            return;
        }

        const { element, data } = this.itemMap.get(itemId);
        data.disabled = true;

        element.classList.add('disabled');
        const button = element.querySelector('button');
        if (button) {
            button.disabled = true;
        }
    }

    /**
     * 更新項目標籤
     * @param {string} itemId - 項目 ID
     * @param {string} label - 新標籤
     */
    updateLabel(itemId, label) {
        if (!this.itemMap.has(itemId)) {
            console.error(`Menu item with id "${itemId}" not found`);
            return;
        }

        const { element, data } = this.itemMap.get(itemId);
        data.label = label;

        const labelEl = element.querySelector('.menu-item-label');
        if (labelEl) {
            labelEl.textContent = label;
        }
    }

    /**
     * 改變布局
     * @param {string} layout - 布局方式
     * @param {number} columns - 欄位數量（grid 模式）
     */
    setLayout(layout, columns) {
        this.layout = layout;
        this.columns = columns || this.columns;

        const container = document.getElementById(this.containerId);
        container.className = `function-menu function-menu-${layout} function-menu-${this.size}`;

        if (layout === 'grid') {
            container.style.gridTemplateColumns = `repeat(${this.columns}, 1fr)`;
        } else {
            container.style.gridTemplateColumns = '';
        }
    }

    /**
     * 改變尺寸
     * @param {string} size - 尺寸大小
     */
    setSize(size) {
        this.size = size;

        const container = document.getElementById(this.containerId);
        container.className = `function-menu function-menu-${this.layout} function-menu-${size}`;

        if (this.layout === 'grid') {
            container.style.gridTemplateColumns = `repeat(${this.columns}, 1fr)`;
        }
    }

    /**
     * 取得項目數量
     * @returns {number}
     */
    getItemCount() {
        return this.itemMap.size;
    }

    /**
     * 清空所有項目
     */
    clearAllItems() {
        const itemIds = Array.from(this.itemMap.keys());
        itemIds.forEach(id => this.removeItem(id));
    }

    /**
     * 銷毀容器
     */
    destroy() {
        this.clearAllItems();
        const container = document.getElementById(this.containerId);
        if (container) {
            container.innerHTML = '';
        }
    }
}

// 導出供外部使用
export default FunctionMenu;

if (typeof module !== 'undefined' && module.exports) {
    module.exports = FunctionMenu;
}
