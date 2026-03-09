/**
 * SPA Layout - 應用程式佈局
 *
 * 功能：
 * - Header / Sidebar / Content / Footer 佈局
 * - 響應式側邊欄
 * - 主題切換
 * - 載入狀態顯示
 *
 * @module Layout
 * @version 1.0.0
 */

export class Layout {
    /**
     * @param {Object} options - 配置選項
     * @param {Object} options.store - 狀態管理
     * @param {Object} options.api - API 服務
     */
    constructor(options = {}) {
        this.store = options.store;
        this.api = options.api;
        this.element = null;
        this._contentArea = null;
        this._loadingOverlay = null;
    }

    /**
     * 掛載佈局
     * @param {HTMLElement} container - 容器元素
     */
    mount(container) {
        this.element = document.createElement('div');
        this.element.className = 'app-layout';
        this.element.innerHTML = this._render();

        container.appendChild(this.element);

        // 取得關鍵元素
        this._contentArea = this.element.querySelector('.layout-content');
        this._loadingOverlay = this.element.querySelector('.layout-loading');

        // 綁定事件
        this._bindEvents();

        // 設定初始主題
        const theme = this.store?.get('theme') || 'light';
        document.documentElement.setAttribute('data-theme', theme);
    }

    /**
     * 渲染佈局 HTML
     */
    _render() {
        return `
            <!-- 側邊欄 -->
            <aside class="layout-sidebar" data-collapsed="false">
                <div class="sidebar-header">
                    <div class="sidebar-logo">
                        <span class="logo-icon">S</span>
                        <span class="logo-text">SPA App</span>
                    </div>
                    <button class="sidebar-toggle" aria-label="收合側邊欄">
                        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M15 18l-6-6 6-6"/>
                        </svg>
                    </button>
                </div>

                <nav class="sidebar-nav">
                    <a href="#/" class="nav-item" data-path="/">
                        <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>
                            <polyline points="9 22 9 12 15 12 15 22"/>
                        </svg>
                        <span class="nav-text">首頁</span>
                    </a>
                    <a href="#/users" class="nav-item" data-path="/users">
                        <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                            <circle cx="9" cy="7" r="4"/>
                            <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
                            <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
                        </svg>
                        <span class="nav-text">使用者管理</span>
                    </a>
                    <a href="#/settings" class="nav-item" data-path="/settings">
                        <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <circle cx="12" cy="12" r="3"/>
                            <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/>
                        </svg>
                        <span class="nav-text">設定</span>
                    </a>
                </nav>

                <div class="sidebar-footer">
                    <button class="theme-toggle" aria-label="切換主題">
                        <svg class="theme-icon-light" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <circle cx="12" cy="12" r="5"/>
                            <line x1="12" y1="1" x2="12" y2="3"/>
                            <line x1="12" y1="21" x2="12" y2="23"/>
                            <line x1="4.22" y1="4.22" x2="5.64" y2="5.64"/>
                            <line x1="18.36" y1="18.36" x2="19.78" y2="19.78"/>
                            <line x1="1" y1="12" x2="3" y2="12"/>
                            <line x1="21" y1="12" x2="23" y2="12"/>
                            <line x1="4.22" y1="19.78" x2="5.64" y2="18.36"/>
                            <line x1="18.36" y1="5.64" x2="19.78" y2="4.22"/>
                        </svg>
                        <svg class="theme-icon-dark" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/>
                        </svg>
                    </button>
                </div>
            </aside>

            <!-- 主內容區 -->
            <main class="layout-main">
                <!-- 頂部導航 -->
                <header class="layout-header">
                    <button class="mobile-menu-toggle" aria-label="開啟選單">
                        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <line x1="3" y1="12" x2="21" y2="12"/>
                            <line x1="3" y1="6" x2="21" y2="6"/>
                            <line x1="3" y1="18" x2="21" y2="18"/>
                        </svg>
                    </button>

                    <div class="header-breadcrumb">
                        <span class="breadcrumb-item">首頁</span>
                    </div>

                    <div class="header-actions">
                        <button class="header-action" aria-label="通知">
                            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/>
                                <path d="M13.73 21a2 2 0 0 1-3.46 0"/>
                            </svg>
                        </button>
                        <div class="header-user">
                            <div class="user-avatar">U</div>
                        </div>
                    </div>
                </header>

                <!-- 頁面內容 -->
                <div class="layout-content">
                    <!-- 路由內容將被注入這裡 -->
                </div>
            </main>

            <!-- 載入中覆蓋層 -->
            <div class="layout-loading" style="display: none;">
                <div class="loading-spinner"></div>
            </div>

            <!-- 行動版側邊欄遮罩 -->
            <div class="sidebar-overlay"></div>
        `;
    }

    /**
     * 綁定事件
     */
    _bindEvents() {
        // 側邊欄收合
        const sidebarToggle = this.element.querySelector('.sidebar-toggle');
        sidebarToggle?.addEventListener('click', () => this._toggleSidebar());

        // 行動版選單
        const mobileToggle = this.element.querySelector('.mobile-menu-toggle');
        mobileToggle?.addEventListener('click', () => this._toggleMobileSidebar());

        // 側邊欄遮罩點擊
        const overlay = this.element.querySelector('.sidebar-overlay');
        overlay?.addEventListener('click', () => this._closeMobileSidebar());

        // 主題切換
        const themeToggle = this.element.querySelector('.theme-toggle');
        themeToggle?.addEventListener('click', () => this._toggleTheme());

        // 導航項目點擊
        const navItems = this.element.querySelectorAll('.nav-item');
        navItems.forEach(item => {
            item.addEventListener('click', () => {
                this._setActiveNav(item.dataset.path);
                this._closeMobileSidebar();
            });
        });

        // 監聽路由變化更新導航狀態
        window.addEventListener('hashchange', () => {
            const path = location.hash.slice(1) || '/';
            this._setActiveNav(path);
        });

        // 初始化導航狀態
        const initialPath = location.hash.slice(1) || '/';
        this._setActiveNav(initialPath);
    }

    /**
     * 切換側邊欄
     */
    _toggleSidebar() {
        const sidebar = this.element.querySelector('.layout-sidebar');
        const isCollapsed = sidebar.dataset.collapsed === 'true';
        sidebar.dataset.collapsed = (!isCollapsed).toString();
        this.store?.set('sidebarCollapsed', !isCollapsed);
    }

    /**
     * 切換行動版側邊欄
     */
    _toggleMobileSidebar() {
        const sidebar = this.element.querySelector('.layout-sidebar');
        sidebar.classList.toggle('mobile-open');
        this.element.querySelector('.sidebar-overlay').classList.toggle('active');
    }

    /**
     * 關閉行動版側邊欄
     */
    _closeMobileSidebar() {
        const sidebar = this.element.querySelector('.layout-sidebar');
        sidebar.classList.remove('mobile-open');
        this.element.querySelector('.sidebar-overlay').classList.remove('active');
    }

    /**
     * 切換主題
     */
    _toggleTheme() {
        const currentTheme = this.store?.get('theme') || 'light';
        const newTheme = currentTheme === 'light' ? 'dark' : 'light';
        this.store?.set('theme', newTheme);
    }

    /**
     * 設定當前活動導航
     */
    _setActiveNav(path) {
        const navItems = this.element.querySelectorAll('.nav-item');
        navItems.forEach(item => {
            const itemPath = item.dataset.path;
            const isActive = path === itemPath || (itemPath !== '/' && path.startsWith(itemPath));
            item.classList.toggle('active', isActive);
        });

        // 更新麵包屑
        this._updateBreadcrumb(path);
    }

    /**
     * 更新麵包屑
     */
    _updateBreadcrumb(path) {
        const breadcrumb = this.element.querySelector('.header-breadcrumb');
        if (!breadcrumb) return;

        const pathMap = {
            '/': '首頁',
            '/users': '使用者管理',
            '/settings': '設定'
        };

        const segments = path.split('/').filter(s => s);
        let html = `<a href="#/" class="breadcrumb-item">首頁</a>`;

        let currentPath = '';
        segments.forEach((segment, index) => {
            currentPath += '/' + segment;
            const name = pathMap[currentPath] || segment;
            const isLast = index === segments.length - 1;

            if (isLast) {
                html += `<span class="breadcrumb-separator">/</span><span class="breadcrumb-item current">${name}</span>`;
            } else {
                html += `<span class="breadcrumb-separator">/</span><a href="#${currentPath}" class="breadcrumb-item">${name}</a>`;
            }
        });

        breadcrumb.innerHTML = html;
    }

    /**
     * 取得內容區
     */
    getContentArea() {
        return this._contentArea;
    }

    /**
     * 設定載入狀態
     */
    setLoading(loading) {
        if (this._loadingOverlay) {
            this._loadingOverlay.style.display = loading ? 'flex' : 'none';
        }
    }

    /**
     * 更新使用者資訊
     */
    updateUser(user) {
        const avatar = this.element.querySelector('.user-avatar');
        if (avatar && user) {
            avatar.textContent = user.name?.[0] || 'U';
        }
    }

    /**
     * 銷毀佈局
     */
    destroy() {
        if (this.element && this.element.parentNode) {
            this.element.parentNode.removeChild(this.element);
        }
    }
}

export default Layout;
