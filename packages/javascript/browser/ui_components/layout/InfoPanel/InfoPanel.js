/**
 * InfoPanel - 資訊面板容器組件
 * 用於展示統計數據、通知、卡片資訊等
 *
 * @author MAGI System
 * @version 1.0.0
 */
import Locale from '../../i18n/index.js';


export class InfoPanel {
    /**
     * 建立資訊面板容器
     * @param {Object} options - 配置選項
     * @param {string} options.containerId - 容器元素 ID
     * @param {Array} options.panels - 初始面板配置
     * @param {string} options.layout - 布局方式 ('grid', 'list', 'masonry')
     * @param {number} options.columns - 欄位數量（grid 模式）
     * @param {boolean} options.collapsible - 是否可收合
     * @param {boolean} options.sortable - 是否可排序
     * @param {Function} options.onPanelClick - 面板點擊回調
     * @param {Function} options.onPanelCollapse - 面板收合回調
     */
    constructor(options = {}) {
        this.containerId = options.containerId || 'info-panel-container';
        this.panels = options.panels || [];
        this.layout = options.layout || 'grid';
        this.columns = options.columns || 3;
        this.collapsible = options.collapsible !== undefined ? options.collapsible : true;
        this.sortable = options.sortable !== undefined ? options.sortable : false;
        this.onPanelClick = options.onPanelClick || null;
        this.onPanelCollapse = options.onPanelCollapse || null;

        this.panelMap = new Map();

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

        container.className = `info-panel-container info-panel-${this.layout}`;

        if (this.layout === 'grid') {
            container.style.gridTemplateColumns = `repeat(${this.columns}, 1fr)`;
        }

        // 添加初始面板
        this.panels.forEach(panel => this.addPanel(panel));
    }

    /**
     * 添加新面板
     * @param {Object} panel - 面板配置
     * @param {string} panel.id - 面板 ID
     * @param {string} panel.type - 面板類型 ('stat', 'card', 'notification', 'chart', 'custom')
     * @param {string} panel.title - 面板標題
     * @param {string} panel.icon - 圖示類名
     * @param {string} panel.color - 主題顏色
     * @param {Object} panel.data - 面板資料
     * @param {boolean} panel.collapsible - 是否可收合
     * @param {boolean} panel.collapsed - 初始是否收合
     */
    addPanel(panel) {
        if (!panel.id) {
            console.error('Panel must have an id');
            return;
        }

        if (this.panelMap.has(panel.id)) {
            console.warn(`Panel with id "${panel.id}" already exists`);
            return;
        }

        const container = document.getElementById(this.containerId);
        const panelElement = this.createPanelElement(panel);

        container.appendChild(panelElement);
        this.panelMap.set(panel.id, { element: panelElement, data: panel });
    }

    /**
     * 創建面板元素
     * @private
     */
    createPanelElement(panel) {
        const panelEl = document.createElement('div');
        panelEl.className = `info-panel info-panel-${panel.type || 'card'}`;
        panelEl.dataset.panelId = panel.id;

        if (panel.color) {
            panelEl.dataset.color = panel.color;
        }

        if (panel.collapsed) {
            panelEl.classList.add('collapsed');
        }

        // 面板頭部
        const header = document.createElement('div');
        header.className = 'info-panel-header';

        // 標題區域
        const titleArea = document.createElement('div');
        titleArea.className = 'info-panel-title-area';

        if (panel.icon) {
            const icon = document.createElement('i');
            icon.className = panel.icon;
            titleArea.appendChild(icon);
        }

        const title = document.createElement('h3');
        title.className = 'info-panel-title';
        title.textContent = panel.title || Locale.t('infoPanel.untitledPanel');
        titleArea.appendChild(title);

        header.appendChild(titleArea);

        // 操作按鈕區域
        const actions = document.createElement('div');
        actions.className = 'info-panel-actions';

        // 收合按鈕
        const isCollapsible = panel.collapsible !== undefined ? panel.collapsible : this.collapsible;
        if (isCollapsible) {
            const collapseBtn = document.createElement('button');
            collapseBtn.className = 'info-panel-collapse-btn';
            collapseBtn.innerHTML = '<i class="fas fa-chevron-up"></i>';
            collapseBtn.onclick = () => this.toggleCollapse(panel.id);
            actions.appendChild(collapseBtn);
        }

        header.appendChild(actions);
        panelEl.appendChild(header);

        // 面板內容
        const content = document.createElement('div');
        content.className = 'info-panel-content';

        // 根據類型渲染內容
        switch (panel.type) {
            case 'stat':
                content.appendChild(this.createStatContent(panel.data));
                break;
            case 'notification':
                content.appendChild(this.createNotificationContent(panel.data));
                break;
            case 'chart':
                content.appendChild(this.createChartContent(panel.data));
                break;
            case 'card':
            default:
                content.appendChild(this.createCardContent(panel.data));
                break;
        }

        panelEl.appendChild(content);

        // 點擊事件
        if (this.onPanelClick) {
            panelEl.onclick = (e) => {
                if (!e.target.closest('.info-panel-actions')) {
                    this.onPanelClick({ panelId: panel.id, panel });
                }
            };
        }

        return panelEl;
    }

    /**
     * 創建統計面板內容
     * @private
     */
    createStatContent(data) {
        const content = document.createElement('div');
        content.className = 'stat-content';

        const value = document.createElement('div');
        value.className = 'stat-value';
        value.textContent = data.value || '0';
        content.appendChild(value);

        if (data.label) {
            const label = document.createElement('div');
            label.className = 'stat-label';
            label.textContent = data.label;
            content.appendChild(label);
        }

        if (data.change !== undefined) {
            const change = document.createElement('div');
            change.className = `stat-change ${data.change >= 0 ? 'positive' : 'negative'}`;
            const icon = data.change >= 0 ? '↑' : '↓';
            change.innerHTML = `${icon} ${Math.abs(data.change)}%`;
            content.appendChild(change);
        }

        return content;
    }

    /**
     * 創建通知面板內容
     * @private
     */
    createNotificationContent(data) {
        const content = document.createElement('div');
        content.className = 'notification-content';

        if (Array.isArray(data.items)) {
            data.items.forEach(item => {
                const notifItem = document.createElement('div');
                notifItem.className = 'notification-item';

                if (item.time) {
                    const time = document.createElement('span');
                    time.className = 'notification-time';
                    time.textContent = item.time;
                    notifItem.appendChild(time);
                }

                const message = document.createElement('p');
                message.className = 'notification-message';
                message.textContent = item.message || '';
                notifItem.appendChild(message);

                content.appendChild(notifItem);
            });
        }

        return content;
    }

    /**
     * 創建圖表面板內容
     * @private
     */
    createChartContent(data) {
        const content = document.createElement('div');
        content.className = 'chart-content';

        const placeholder = document.createElement('div');
        placeholder.className = 'chart-placeholder';
        placeholder.textContent = data.chartType || Locale.t('infoPanel.chartPlaceholder');
        placeholder.style.height = data.height || '200px';

        content.appendChild(placeholder);
        return content;
    }

    /**
     * 創建卡片面板內容
     * @private
     */
    createCardContent(data) {
        const content = document.createElement('div');
        content.className = 'card-content';

        if (typeof data === 'string') {
            content.innerHTML = data;
        } else if (data instanceof HTMLElement) {
            content.appendChild(data);
        } else if (data.html) {
            content.innerHTML = data.html;
        } else if (data.text) {
            const text = document.createElement('p');
            text.textContent = data.text;
            content.appendChild(text);
        }

        return content;
    }

    /**
     * 切換面板收合狀態
     * @param {string} panelId - 面板 ID
     */
    toggleCollapse(panelId) {
        if (!this.panelMap.has(panelId)) {
            console.error(`Panel with id "${panelId}" not found`);
            return;
        }

        const { element, data } = this.panelMap.get(panelId);
        const isCollapsed = element.classList.toggle('collapsed');

        // 更新收合按鈕圖示
        const collapseBtn = element.querySelector('.info-panel-collapse-btn i');
        if (collapseBtn) {
            collapseBtn.className = isCollapsed ? 'fas fa-chevron-down' : 'fas fa-chevron-up';
        }

        // 觸發回調
        if (this.onPanelCollapse) {
            this.onPanelCollapse({ panelId, collapsed: isCollapsed, panel: data });
        }
    }

    /**
     * 移除面板
     * @param {string} panelId - 面板 ID
     */
    removePanel(panelId) {
        if (!this.panelMap.has(panelId)) {
            console.error(`Panel with id "${panelId}" not found`);
            return;
        }

        const { element } = this.panelMap.get(panelId);
        element.remove();
        this.panelMap.delete(panelId);
    }

    /**
     * 更新面板資料
     * @param {string} panelId - 面板 ID
     * @param {Object} newData - 新資料
     */
    updatePanel(panelId, newData) {
        if (!this.panelMap.has(panelId)) {
            console.error(`Panel with id "${panelId}" not found`);
            return;
        }

        const { element, data } = this.panelMap.get(panelId);

        // 更新資料
        Object.assign(data, newData);

        // 重新渲染內容
        const content = element.querySelector('.info-panel-content');
        content.innerHTML = '';

        switch (data.type) {
            case 'stat':
                content.appendChild(this.createStatContent(data.data));
                break;
            case 'notification':
                content.appendChild(this.createNotificationContent(data.data));
                break;
            case 'chart':
                content.appendChild(this.createChartContent(data.data));
                break;
            case 'card':
            default:
                content.appendChild(this.createCardContent(data.data));
                break;
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
        container.className = `info-panel-container info-panel-${layout}`;

        if (layout === 'grid') {
            container.style.gridTemplateColumns = `repeat(${this.columns}, 1fr)`;
        } else {
            container.style.gridTemplateColumns = '';
        }
    }

    /**
     * 取得面板數量
     * @returns {number}
     */
    getPanelCount() {
        return this.panelMap.size;
    }

    /**
     * 清空所有面板
     */
    clearAllPanels() {
        const panelIds = Array.from(this.panelMap.keys());
        panelIds.forEach(id => this.removePanel(id));
    }

    /**
     * 銷毀容器
     */
    destroy() {
        this.clearAllPanels();
        const container = document.getElementById(this.containerId);
        if (container) {
            container.innerHTML = '';
        }
    }
}

// 導出供外部使用
export default InfoPanel;
if (typeof module !== 'undefined' && module.exports) {
    module.exports = InfoPanel;
}
