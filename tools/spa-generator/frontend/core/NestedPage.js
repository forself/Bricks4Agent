/**
 * NestedPage - 巢狀頁面基礎類別
 *
 * 用於包含子路由的頁面，提供：
 * - 子路由出口 (router-outlet)
 * - 子導航管理
 * - 父子頁面資料傳遞
 *
 * @module NestedPage
 * @version 1.0.0
 *
 * @example
 * // 使用者管理頁面（包含子路由）
 * class UsersPage extends NestedPage {
 *     // 定義子導航
 *     getSubNav() {
 *         return [
 *             { path: '/users', label: '使用者列表', exact: true },
 *             { path: '/users/create', label: '新增使用者' },
 *         ];
 *     }
 *
 *     template() {
 *         return `
 *             <div class="users-page">
 *                 <h1>使用者管理</h1>
 *                 ${this.renderSubNav()}
 *                 ${this.renderOutlet()}
 *             </div>
 *         `;
 *     }
 * }
 *
 * // 路由配置
 * {
 *     path: '/users',
 *     component: UsersPage,
 *     children: [
 *         { path: '/', component: UserListPage, exact: true },
 *         { path: '/create', component: UserCreatePage },
 *         { path: '/:id', component: UserDetailPage },
 *         { path: '/:id/edit', component: UserEditPage }
 *     ]
 * }
 */

import { BasePage } from './BasePage.js';

export class NestedPage extends BasePage {
    constructor(options = {}) {
        super(options);
        this._routerOutlet = null;
        this._childPage = null;
    }

    /**
     * 取得子導航配置
     * 子類別覆寫此方法
     * @returns {Array} 子導航項目
     */
    getSubNav() {
        return [];
        // 範例:
        // return [
        //     { path: '/users', label: '列表', icon: 'list', exact: true },
        //     { path: '/users/create', label: '新增', icon: 'plus' },
        // ];
    }

    /**
     * 渲染子導航
     * @returns {string} HTML 字串
     */
    renderSubNav() {
        const items = this.getSubNav();
        if (items.length === 0) return '';

        const currentPath = location.hash.slice(1) || '/';

        const navItems = items.map(item => {
            const isActive = item.exact
                ? currentPath === item.path
                : currentPath.startsWith(item.path);

            const icon = item.icon ? `<span class="subnav-icon">${this._getIcon(item.icon)}</span>` : '';

            return `
                <a href="#${item.path}"
                   class="subnav-item ${isActive ? 'active' : ''}"
                   data-path="${item.path}">
                    ${icon}
                    <span class="subnav-label">${item.label}</span>
                </a>
            `;
        }).join('');

        return `
            <nav class="subnav">
                ${navItems}
            </nav>
        `;
    }

    /**
     * 渲染路由出口
     * @returns {string} HTML 字串
     */
    renderOutlet() {
        return `<div class="router-outlet" data-router-outlet></div>`;
    }

    /**
     * 取得路由出口元素
     * @returns {HTMLElement|null}
     */
    getRouterOutlet() {
        if (!this._routerOutlet) {
            this._routerOutlet = this.element?.querySelector('[data-router-outlet]');
        }
        return this._routerOutlet;
    }

    /**
     * 設定子頁面
     * @param {BasePage} page - 子頁面實例
     */
    setChildPage(page) {
        this._childPage = page;
    }

    /**
     * 取得子頁面
     * @returns {BasePage|null}
     */
    getChildPage() {
        return this._childPage;
    }

    /**
     * 傳遞資料給子頁面
     * @param {Object} data - 要傳遞的資料
     */
    passToChild(data) {
        if (this._childPage) {
            Object.assign(this._childPage._data, data);
        }
    }

    /**
     * 掛載後更新子導航狀態
     */
    async onMounted() {
        await super.onMounted();
        this._updateSubNavState();

        // 監聽路由變化
        window.addEventListener('hashchange', () => this._updateSubNavState());
    }

    /**
     * 更新子導航狀態
     */
    _updateSubNavState() {
        const currentPath = location.hash.slice(1) || '/';
        const items = this.element?.querySelectorAll('.subnav-item');

        items?.forEach(item => {
            const itemPath = item.dataset.path;
            const isExact = item.dataset.exact === 'true';
            const isActive = isExact
                ? currentPath === itemPath
                : currentPath.startsWith(itemPath);

            item.classList.toggle('active', isActive);
        });
    }

    /**
     * 取得圖示 SVG
     */
    _getIcon(name) {
        const icons = {
            list: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2"><line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/></svg>',
            plus: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>',
            edit: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2"><path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/></svg>',
            user: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/></svg>',
            settings: '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>'
        };
        return icons[name] || '';
    }

    /**
     * 銷毀時清理子頁面
     */
    async onDestroy() {
        if (this._childPage) {
            await this._childPage.destroy();
            this._childPage = null;
        }
        await super.onDestroy();
    }
}

export default NestedPage;
