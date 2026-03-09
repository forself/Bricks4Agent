/**
 * SPA Router - 前端路由系統
 *
 * 功能：
 * - Hash/History 模式路由
 * - 巢狀路由支援
 * - 路由守衛 (beforeEnter, afterEnter)
 * - 動態路由參數
 * - 懶載入頁面
 *
 * @module Router
 * @version 1.0.0
 *
 * @example
 * const router = new Router({
 *     routes: [
 *         { path: '/', component: HomePage },
 *         { path: '/users', component: UsersPage, children: [
 *             { path: ':id', component: UserDetailPage }
 *         ]}
 *     ]
 * });
 */

export class Router {
    /**
     * @param {Object} options - 路由配置
     * @param {Array} options.routes - 路由表
     * @param {Object} options.store - 狀態管理
     * @param {Object} options.api - API 服務
     * @param {Object} options.layout - 佈局元件
     * @param {string} options.mode - 路由模式 ('hash' | 'history')
     */
    constructor(options = {}) {
        this.routes = options.routes || [];
        this.store = options.store;
        this.api = options.api;
        this.layout = options.layout;
        this.mode = options.mode || 'hash';

        this.currentRoute = null;
        this.currentPage = null;
        this.params = {};
        this.query = {};

        this._listeners = [];
        this._beforeHooks = [];
        this._afterHooks = [];
    }

    /**
     * 啟動路由
     */
    start() {
        // 監聽路由變化
        if (this.mode === 'history') {
            window.addEventListener('popstate', () => this._handleRouteChange());
        } else {
            window.addEventListener('hashchange', () => this._handleRouteChange());
        }

        // 處理初始路由
        this._handleRouteChange();
    }

    /**
     * 導航到指定路徑
     * @param {string} path - 目標路徑
     * @param {Object} options - 導航選項
     */
    navigate(path, options = {}) {
        const { replace = false, query = {} } = options;

        // 構建完整路徑
        let fullPath = path;
        const queryString = this._buildQueryString(query);
        if (queryString) {
            fullPath += '?' + queryString;
        }

        if (this.mode === 'history') {
            if (replace) {
                history.replaceState(null, '', fullPath);
            } else {
                history.pushState(null, '', fullPath);
            }
            this._handleRouteChange();
        } else {
            if (replace) {
                location.replace('#' + fullPath);
            } else {
                location.hash = fullPath;
            }
        }
    }

    /**
     * 返回上一頁
     */
    back() {
        history.back();
    }

    /**
     * 前進下一頁
     */
    forward() {
        history.forward();
    }

    /**
     * 新增全域前置守衛
     * @param {Function} hook - 守衛函數 (to, from, next)
     */
    beforeEach(hook) {
        this._beforeHooks.push(hook);
    }

    /**
     * 新增全域後置守衛
     * @param {Function} hook - 守衛函數 (to, from)
     */
    afterEach(hook) {
        this._afterHooks.push(hook);
    }

    /**
     * 訂閱路由變化
     * @param {Function} callback - 回調函數
     * @returns {Function} 取消訂閱函數
     */
    subscribe(callback) {
        this._listeners.push(callback);
        return () => {
            this._listeners = this._listeners.filter(l => l !== callback);
        };
    }

    /**
     * 取得當前路由資訊
     */
    getCurrentRoute() {
        return {
            path: this.currentRoute?.path,
            params: { ...this.params },
            query: { ...this.query },
            meta: this.currentRoute?.meta || {}
        };
    }

    /**
     * 處理路由變化
     */
    async _handleRouteChange() {
        const path = this._getCurrentPath();
        const { pathname, query } = this._parsePath(path);

        this.query = query;

        // 匹配路由
        const matchResult = this._matchRoute(pathname, this.routes);

        if (!matchResult) {
            // 404 處理
            this._handle404(pathname);
            return;
        }

        const { route, params, parentRoutes } = matchResult;
        this.params = params;

        // 執行前置守衛
        const from = this.currentRoute;
        const to = { path: pathname, params, query, meta: route.meta || {} };

        const canProceed = await this._runBeforeHooks(to, from);
        if (!canProceed) return;

        // 執行路由級別的前置守衛
        if (route.beforeEnter) {
            const result = await route.beforeEnter(to, from);
            if (result === false) return;
            if (typeof result === 'string') {
                this.navigate(result);
                return;
            }
        }

        // 載入並渲染頁面
        await this._loadPage(route, parentRoutes);

        // 更新當前路由
        this.currentRoute = route;

        // 執行後置守衛
        this._runAfterHooks(to, from);

        // 通知訂閱者
        this._notifyListeners();
    }

    /**
     * 取得當前路徑
     */
    _getCurrentPath() {
        if (this.mode === 'history') {
            return location.pathname + location.search;
        } else {
            return location.hash.slice(1) || '/';
        }
    }

    /**
     * 解析路徑
     */
    _parsePath(path) {
        const [pathname, queryString] = path.split('?');
        const query = {};

        if (queryString) {
            const params = new URLSearchParams(queryString);
            params.forEach((value, key) => {
                query[key] = value;
            });
        }

        return { pathname: pathname || '/', query };
    }

    /**
     * 匹配路由
     */
    _matchRoute(pathname, routes, parentRoutes = []) {
        for (const route of routes) {
            const { matched, params } = this._matchPath(pathname, route.path, route.exact);

            if (matched) {
                // 檢查是否有子路由需要匹配
                if (route.children && route.children.length > 0) {
                    // 計算子路由的路徑
                    const childPath = pathname.slice(route.path.length) || '/';
                    const childMatch = this._matchRoute(
                        childPath,
                        route.children,
                        [...parentRoutes, route]
                    );

                    if (childMatch) {
                        return {
                            route: childMatch.route,
                            params: { ...params, ...childMatch.params },
                            parentRoutes: childMatch.parentRoutes
                        };
                    }
                }

                return { route, params, parentRoutes };
            }
        }

        return null;
    }

    /**
     * 匹配路徑
     */
    _matchPath(pathname, routePath, exact = false) {
        // 處理動態參數 :param
        const paramNames = [];
        const regexStr = routePath
            .replace(/:([^/]+)/g, (_, name) => {
                paramNames.push(name);
                return '([^/]+)';
            })
            .replace(/\//g, '\\/');

        const regex = exact
            ? new RegExp(`^${regexStr}$`)
            : new RegExp(`^${regexStr}(?:/|$)`);

        const match = pathname.match(regex);

        if (!match) {
            return { matched: false, params: {} };
        }

        const params = {};
        paramNames.forEach((name, index) => {
            params[name] = match[index + 1];
        });

        return { matched: true, params };
    }

    /**
     * 載入頁面
     */
    async _loadPage(route, parentRoutes) {
        try {
            this.store?.set('loading', true);

            // 銷毀當前頁面
            if (this.currentPage && this.currentPage.destroy) {
                this.currentPage.destroy();
            }

            // 取得頁面元件
            let PageComponent = route.component;

            // 支援懶載入
            if (typeof PageComponent === 'function' && !PageComponent.prototype) {
                const module = await PageComponent();
                PageComponent = module.default || module;
            }

            // 建立頁面實例
            const page = new PageComponent({
                router: this,
                store: this.store,
                api: this.api,
                params: this.params,
                query: this.query,
                meta: route.meta || {}
            });

            // 渲染到內容區
            const contentArea = this.layout.getContentArea();
            if (contentArea) {
                contentArea.innerHTML = '';

                // 處理巢狀路由
                if (parentRoutes.length > 0) {
                    await this._renderNestedRoutes(parentRoutes, page, contentArea);
                } else {
                    page.mount(contentArea);
                }
            }

            this.currentPage = page;

            // 更新頁面標題
            if (route.meta?.title) {
                document.title = route.meta.title;
            }

        } catch (error) {
            console.error('[Router] 載入頁面失敗:', error);
            this._handleError(error);
        } finally {
            this.store?.set('loading', false);
        }
    }

    /**
     * 渲染巢狀路由
     */
    async _renderNestedRoutes(parentRoutes, childPage, container) {
        let currentContainer = container;

        for (const parentRoute of parentRoutes) {
            let ParentComponent = parentRoute.component;

            if (typeof ParentComponent === 'function' && !ParentComponent.prototype) {
                const module = await ParentComponent();
                ParentComponent = module.default || module;
            }

            const parentPage = new ParentComponent({
                router: this,
                store: this.store,
                api: this.api,
                params: this.params,
                query: this.query,
                meta: parentRoute.meta || {}
            });

            parentPage.mount(currentContainer);

            // 取得子路由出口
            const outlet = parentPage.getRouterOutlet?.() ||
                currentContainer.querySelector('[data-router-outlet]');

            if (outlet) {
                currentContainer = outlet;
            }
        }

        // 渲染最終的子頁面
        childPage.mount(currentContainer);
    }

    /**
     * 執行前置守衛
     */
    async _runBeforeHooks(to, from) {
        for (const hook of this._beforeHooks) {
            let nextCalled = false;
            let nextValue = true;

            const next = (value) => {
                nextCalled = true;
                nextValue = value;
            };

            await hook(to, from, next);

            if (!nextCalled) continue;

            if (nextValue === false) return false;
            if (typeof nextValue === 'string') {
                this.navigate(nextValue);
                return false;
            }
        }

        return true;
    }

    /**
     * 執行後置守衛
     */
    _runAfterHooks(to, from) {
        this._afterHooks.forEach(hook => hook(to, from));
    }

    /**
     * 處理 404
     */
    _handle404(pathname) {
        console.warn('[Router] 找不到路由:', pathname);

        const contentArea = this.layout?.getContentArea();
        if (contentArea) {
            contentArea.innerHTML = `
                <div class="page-404">
                    <h1>404</h1>
                    <p>找不到頁面: ${pathname}</p>
                    <button onclick="location.hash='/'">返回首頁</button>
                </div>
            `;
        }
    }

    /**
     * 處理錯誤
     */
    _handleError(error) {
        const contentArea = this.layout?.getContentArea();
        if (contentArea) {
            contentArea.innerHTML = `
                <div class="page-error">
                    <h1>載入錯誤</h1>
                    <p>${error.message}</p>
                    <button onclick="location.reload()">重新載入</button>
                </div>
            `;
        }
    }

    /**
     * 建構 Query String
     */
    _buildQueryString(query) {
        const params = new URLSearchParams();
        Object.entries(query).forEach(([key, value]) => {
            if (value !== undefined && value !== null) {
                params.append(key, value);
            }
        });
        return params.toString();
    }

    /**
     * 通知訂閱者
     */
    _notifyListeners() {
        const routeInfo = this.getCurrentRoute();
        this._listeners.forEach(callback => callback(routeInfo));
    }
}

export default Router;
