/**
 * TabContainer - 頁籤容器組件
 * 提供多頁籤介面，支援動態添加、移除、切換頁籤
 *
 * @author MAGI System
 * @version 1.0.0
 */

export class TabContainer {
    /**
     * 建立頁籤容器
     * @param {Object} options - 配置選項
     * @param {string} options.containerId - 容器元素 ID
     * @param {Array} options.tabs - 初始頁籤配置
     * @param {string} options.position - 頁籤位置 ('top', 'bottom', 'left', 'right')
     * @param {boolean} options.closable - 頁籤是否可關閉
     * @param {boolean} options.animated - 是否啟用動畫
     * @param {Function} options.onTabChange - 頁籤切換回調
     * @param {Function} options.onTabClose - 頁籤關閉回調
     */
    constructor(options = {}) {
        this.containerId = options.containerId || 'tab-container';
        this.tabs = options.tabs || [];
        this.position = options.position || 'top';
        this.closable = options.closable !== undefined ? options.closable : true;
        this.animated = options.animated !== undefined ? options.animated : true;
        this.onTabChange = options.onTabChange || null;
        this.onTabClose = options.onTabClose || null;

        this.activeTabId = null;
        this.tabMap = new Map();

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

        // 清空容器
        container.innerHTML = '';
        container.className = `tab-container tab-container-${this.position}`;

        // 建立頁籤導航欄
        this.tabNav = document.createElement('div');
        this.tabNav.className = 'tab-nav';

        this.tabList = document.createElement('ul');
        this.tabList.className = 'tab-list';
        this.tabNav.appendChild(this.tabList);

        // 建立內容區域
        this.tabContent = document.createElement('div');
        this.tabContent.className = 'tab-content';

        // 根據位置調整順序
        if (this.position === 'top' || this.position === 'left') {
            container.appendChild(this.tabNav);
            container.appendChild(this.tabContent);
        } else {
            container.appendChild(this.tabContent);
            container.appendChild(this.tabNav);
        }

        // 添加初始頁籤
        this.tabs.forEach(tab => this.addTab(tab));

        // 激活第一個頁籤
        if (this.tabs.length > 0) {
            this.activateTab(this.tabs[0].id);
        }
    }

    /**
     * 添加新頁籤
     * @param {Object} tab - 頁籤配置
     * @param {string} tab.id - 頁籤 ID
     * @param {string} tab.title - 頁籤標題
     * @param {string} tab.content - 頁籤內容（HTML 字串或 DOM 元素）
     * @param {string} tab.icon - 圖示類名
     * @param {number} tab.badge - 徽章數字
     * @param {boolean} tab.closable - 是否可關閉（覆蓋全域設定）
     */
    addTab(tab) {
        if (!tab.id || !tab.title) {
            console.error('Tab must have id and title');
            return;
        }

        if (this.tabMap.has(tab.id)) {
            console.warn(`Tab with id "${tab.id}" already exists`);
            return;
        }

        // 建立頁籤按鈕
        const tabItem = document.createElement('li');
        tabItem.className = 'tab-item';
        tabItem.dataset.tabId = tab.id;

        const tabButton = document.createElement('button');
        tabButton.className = 'tab-button';
        tabButton.type = 'button';

        // 圖示
        if (tab.icon) {
            const icon = document.createElement('i');
            icon.className = tab.icon;
            tabButton.appendChild(icon);
        }

        // 標題
        const title = document.createElement('span');
        title.className = 'tab-title';
        title.textContent = tab.title;
        tabButton.appendChild(title);

        // 徽章
        if (tab.badge !== undefined && tab.badge > 0) {
            const badge = document.createElement('span');
            badge.className = 'tab-badge';
            badge.textContent = tab.badge > 99 ? '99+' : tab.badge;
            tabButton.appendChild(badge);
        }

        // 關閉按鈕
        const isClosable = tab.closable !== undefined ? tab.closable : this.closable;
        if (isClosable) {
            const closeBtn = document.createElement('button');
            closeBtn.className = 'tab-close';
            closeBtn.type = 'button';
            closeBtn.innerHTML = '&times;';
            closeBtn.onclick = (e) => {
                e.stopPropagation();
                this.closeTab(tab.id);
            };
            tabButton.appendChild(closeBtn);
        }

        // 點擊切換頁籤
        tabButton.onclick = () => this.activateTab(tab.id);

        tabItem.appendChild(tabButton);
        this.tabList.appendChild(tabItem);

        // 建立內容面板
        const panel = document.createElement('div');
        panel.className = 'tab-panel';
        panel.dataset.tabId = tab.id;
        panel.style.display = 'none';

        if (typeof tab.content === 'string') {
            panel.innerHTML = tab.content;
        } else if (tab.content instanceof HTMLElement) {
            panel.appendChild(tab.content);
        }

        this.tabContent.appendChild(panel);

        // 儲存引用
        this.tabMap.set(tab.id, {
            tabItem,
            tabButton,
            panel,
            data: tab
        });
    }

    /**
     * 激活指定頁籤
     * @param {string} tabId - 頁籤 ID
     */
    activateTab(tabId) {
        if (!this.tabMap.has(tabId)) {
            console.error(`Tab with id "${tabId}" not found`);
            return;
        }

        const previousTabId = this.activeTabId;

        // 取消所有頁籤的激活狀態
        this.tabMap.forEach((tab, id) => {
            tab.tabItem.classList.remove('active');
            tab.panel.style.display = 'none';
        });

        // 激活指定頁籤
        const activeTab = this.tabMap.get(tabId);
        activeTab.tabItem.classList.add('active');

        if (this.animated) {
            activeTab.panel.style.animation = 'tabFadeIn 0.3s ease';
        }
        activeTab.panel.style.display = 'block';

        this.activeTabId = tabId;

        // 觸發回調
        if (this.onTabChange) {
            this.onTabChange({
                tabId,
                previousTabId,
                tab: activeTab.data
            });
        }
    }

    /**
     * 關閉指定頁籤
     * @param {string} tabId - 頁籤 ID
     */
    closeTab(tabId) {
        if (!this.tabMap.has(tabId)) {
            console.error(`Tab with id "${tabId}" not found`);
            return;
        }

        // 觸發關閉回調
        if (this.onTabClose) {
            const shouldClose = this.onTabClose({
                tabId,
                tab: this.tabMap.get(tabId).data
            });
            if (shouldClose === false) {
                return; // 取消關閉
            }
        }

        const tab = this.tabMap.get(tabId);

        // 移除 DOM 元素
        tab.tabItem.remove();
        tab.panel.remove();

        // 從 Map 中移除
        this.tabMap.delete(tabId);

        // 如果關閉的是激活頁籤，激活另一個頁籤
        if (this.activeTabId === tabId) {
            const remainingTabs = Array.from(this.tabMap.keys());
            if (remainingTabs.length > 0) {
                this.activateTab(remainingTabs[0]);
            } else {
                this.activeTabId = null;
            }
        }
    }

    /**
     * 更新頁籤徽章
     * @param {string} tabId - 頁籤 ID
     * @param {number} badge - 徽章數字
     */
    updateBadge(tabId, badge) {
        if (!this.tabMap.has(tabId)) {
            console.error(`Tab with id "${tabId}" not found`);
            return;
        }

        const tab = this.tabMap.get(tabId);
        let badgeEl = tab.tabButton.querySelector('.tab-badge');

        if (badge === 0 || badge === null || badge === undefined) {
            // 移除徽章
            if (badgeEl) {
                badgeEl.remove();
            }
        } else {
            // 更新或建立徽章
            if (!badgeEl) {
                badgeEl = document.createElement('span');
                badgeEl.className = 'tab-badge';
                tab.tabButton.appendChild(badgeEl);
            }
            badgeEl.textContent = badge > 99 ? '99+' : badge;
        }
    }

    /**
     * 更新頁籤標題
     * @param {string} tabId - 頁籤 ID
     * @param {string} title - 新標題
     */
    updateTitle(tabId, title) {
        if (!this.tabMap.has(tabId)) {
            console.error(`Tab with id "${tabId}" not found`);
            return;
        }

        const tab = this.tabMap.get(tabId);
        const titleEl = tab.tabButton.querySelector('.tab-title');
        if (titleEl) {
            titleEl.textContent = title;
        }
    }

    /**
     * 更新頁籤內容
     * @param {string} tabId - 頁籤 ID
     * @param {string|HTMLElement} content - 新內容
     */
    updateContent(tabId, content) {
        if (!this.tabMap.has(tabId)) {
            console.error(`Tab with id "${tabId}" not found`);
            return;
        }

        const tab = this.tabMap.get(tabId);
        tab.panel.innerHTML = '';

        if (typeof content === 'string') {
            tab.panel.innerHTML = content;
        } else if (content instanceof HTMLElement) {
            tab.panel.appendChild(content);
        }
    }

    /**
     * 取得當前激活的頁籤 ID
     * @returns {string|null}
     */
    getActiveTabId() {
        return this.activeTabId;
    }

    /**
     * 取得所有頁籤 ID
     * @returns {Array<string>}
     */
    getAllTabIds() {
        return Array.from(this.tabMap.keys());
    }

    /**
     * 取得頁籤數量
     * @returns {number}
     */
    getTabCount() {
        return this.tabMap.size;
    }

    /**
     * 移除所有頁籤
     */
    removeAllTabs() {
        const tabIds = Array.from(this.tabMap.keys());
        tabIds.forEach(id => this.closeTab(id));
    }

    /**
     * 銷毀容器
     */
    destroy() {
        this.removeAllTabs();
        const container = document.getElementById(this.containerId);
        if (container) {
            container.innerHTML = '';
        }
    }
}

// 導出供外部使用
export default TabContainer;

if (typeof module !== 'undefined' && module.exports) {
    module.exports = TabContainer;
}
