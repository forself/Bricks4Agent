/**
 * SPA Application - 主應用程式入口
 *
 * 功能：
 * - 初始化路由系統
 * - 初始化狀態管理
 * - 初始化 API 服務
 * - 載入佈局與頁面
 *
 * @module App
 * @version 1.0.0
 */

import { Router } from './Router.js';
import { Store } from './Store.js';
import { ApiService } from './ApiService.js';
import { Layout } from './Layout.js';

// 路由配置
import { routes } from '../pages/routes.js';

class App {
    constructor() {
        this.router = null;
        this.store = null;
        this.api = null;
        this.layout = null;
        this.rootElement = document.getElementById('app');
    }

    /**
     * 初始化應用程式
     */
    async init() {
        try {
            console.log('[App] 初始化應用程式...');

            // 1. 初始化 API 服務
            this.api = new ApiService({
                baseUrl: '/api',
                timeout: 30000
            });

            // 2. 初始化狀態管理
            this.store = new Store({
                // 初始狀態
                user: null,
                isAuthenticated: false,
                theme: localStorage.getItem('theme') || 'light',
                sidebarCollapsed: false,
                notifications: [],
                loading: false
            });

            // 3. 初始化佈局
            this.layout = new Layout({
                store: this.store,
                api: this.api
            });

            // 4. 初始化路由器
            this.router = new Router({
                routes: routes,
                store: this.store,
                api: this.api,
                layout: this.layout
            });

            // 5. 渲染佈局
            this.rootElement.innerHTML = '';
            this.layout.mount(this.rootElement);

            // 6. 啟動路由
            this.router.start();

            // 7. 綁定全域事件
            this._bindGlobalEvents();

            console.log('[App] 應用程式初始化完成');

        } catch (error) {
            console.error('[App] 初始化失敗:', error);
            this._showError('應用程式載入失敗，請重新整理頁面。');
        }
    }

    /**
     * 綁定全域事件
     */
    _bindGlobalEvents() {
        // 主題切換
        this.store.subscribe('theme', (theme) => {
            document.documentElement.setAttribute('data-theme', theme);
            localStorage.setItem('theme', theme);
        });

        // 載入狀態
        this.store.subscribe('loading', (loading) => {
            this.layout.setLoading(loading);
        });

        // 未授權處理
        window.addEventListener('unauthorized', () => {
            this.store.set('user', null);
            this.store.set('isAuthenticated', false);
            this.router.navigate('/login');
        });

        // 錯誤處理
        window.addEventListener('unhandledrejection', (event) => {
            console.error('[App] 未處理的 Promise 錯誤:', event.reason);
        });
    }

    /**
     * 顯示錯誤訊息
     */
    _showError(message) {
        this.rootElement.innerHTML = `
            <div class="app-error">
                <div class="error-icon">!</div>
                <h2>發生錯誤</h2>
                <p>${message}</p>
                <button onclick="location.reload()">重新載入</button>
            </div>
        `;
    }

    /**
     * 取得 API 服務
     */
    getApi() {
        return this.api;
    }

    /**
     * 取得狀態管理
     */
    getStore() {
        return this.store;
    }

    /**
     * 取得路由器
     */
    getRouter() {
        return this.router;
    }
}

// 建立全域應用實例
const app = new App();

// DOM 載入完成後初始化
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => app.init());
} else {
    app.init();
}

// 匯出給其他模組使用
export { app };
export default App;
