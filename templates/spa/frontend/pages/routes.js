/**
 * 路由配置表
 *
 * 定義應用程式的所有路由
 *
 * @module routes
 * @version 1.0.0
 */

// 頁面元件
import { HomePage } from './HomePage.js';
import { UsersPage } from './users/UsersPage.js';
import { UserListPage } from './users/UserListPage.js';
import { UserDetailPage } from './users/UserDetailPage.js';
import { UserCreatePage } from './users/UserCreatePage.js';
import { SettingsPage } from './SettingsPage.js';
import { LoginPage } from './LoginPage.js';

/**
 * 路由表
 *
 * 路由配置項：
 * - path: 路由路徑 (支援動態參數 :param)
 * - component: 頁面元件類別
 * - exact: 是否精確匹配 (預設 false)
 * - meta: 路由元資訊 (標題、權限等)
 * - children: 子路由 (巢狀路由)
 * - beforeEnter: 路由前置守衛
 */
export const routes = [
    {
        path: '/',
        component: HomePage,
        exact: true,
        meta: {
            title: '首頁',
            requiresAuth: false
        }
    },
    {
        path: '/login',
        component: LoginPage,
        meta: {
            title: '登入',
            requiresAuth: false,
            hideLayout: true
        }
    },
    {
        path: '/users',
        component: UsersPage,
        meta: {
            title: '使用者管理',
            requiresAuth: true,
            permissions: ['user:read']
        },
        children: [
            {
                path: '/',
                component: UserListPage,
                exact: true,
                meta: { title: '使用者列表' }
            },
            {
                path: '/create',
                component: UserCreatePage,
                meta: {
                    title: '新增使用者',
                    permissions: ['user:create']
                }
            },
            {
                path: '/:id',
                component: UserDetailPage,
                exact: true,
                meta: { title: '使用者詳情' }
            }
        ]
    },
    {
        path: '/settings',
        component: SettingsPage,
        meta: {
            title: '設定',
            requiresAuth: true
        }
    }
];

/**
 * 路由守衛示例
 *
 * @example
 * // 在 App.js 中設定全域守衛
 * router.beforeEach((to, from, next) => {
 *     const store = router.store;
 *     const isAuthenticated = store.get('isAuthenticated');
 *
 *     if (to.meta.requiresAuth && !isAuthenticated) {
 *         next('/login');
 *         return;
 *     }
 *
 *     next();
 * });
 */

export default routes;
