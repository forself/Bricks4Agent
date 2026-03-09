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
import { SettingsPage } from './SettingsPage.js';
import { LoginPage } from './LoginPage.js';

// 生成器頁面
import { ProjectCreatePage } from './generator/ProjectCreatePage.js';
import { PageGeneratorPage } from './generator/PageGeneratorPage.js';
import { PageDefinitionEditorPage } from './generator/PageDefinitionEditorPage.js';
import { PageBuilderPage } from './generator/PageBuilderPage.js';
import { ApiGeneratorPage } from './generator/ApiGeneratorPage.js';
import { FeatureGeneratorPage } from './generator/FeatureGeneratorPage.js';

// 社群風格頁面（使用 mock 資料）
import { MemberProfilePage } from '../../../../templates/spa/frontend/pages/social/MemberProfilePage.js';
import { ActivityFeedPage } from '../../../../templates/spa/frontend/pages/social/ActivityFeedPage.js';
import { ActivityDetailPage } from '../../../../templates/spa/frontend/pages/social/ActivityDetailPage.js';
import { NetworkGraphPage } from '../../../../templates/spa/frontend/pages/social/NetworkGraphPage.js';

// 員警社群頁面
import { OfficerProfileEditPage } from '../../../../templates/spa/frontend/pages/social/OfficerProfileEditPage.js';
import { FriendsPage } from '../../../../templates/spa/frontend/pages/social/FriendsPage.js';
import { GroupsPage } from '../../../../templates/spa/frontend/pages/social/GroupsPage.js';
import { ClubsPage } from '../../../../templates/spa/frontend/pages/social/ClubsPage.js';
import { GroupDetailPage } from '../../../../templates/spa/frontend/pages/social/GroupDetailPage.js';

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
        path: '/project',
        component: ProjectCreatePage,
        meta: {
            title: '建立專案',
            requiresAuth: false
        }
    },
    {
        path: '/page',
        component: PageGeneratorPage,
        meta: {
            title: '生成頁面 (簡易)',
            requiresAuth: false
        }
    },
    {
        path: '/page-editor',
        component: PageDefinitionEditorPage,
        meta: {
            title: '頁面定義編輯器',
            requiresAuth: false
        }
    },
    {
        path: '/page-builder',
        component: PageBuilderPage,
        meta: {
            title: '頁面建構器',
            requiresAuth: false
        }
    },
    {
        path: '/api',
        component: ApiGeneratorPage,
        meta: {
            title: '生成 API',
            requiresAuth: false
        }
    },
    {
        path: '/feature',
        component: FeatureGeneratorPage,
        meta: {
            title: '完整功能',
            requiresAuth: false
        }
    },
    {
        path: '/settings',
        component: SettingsPage,
        meta: {
            title: '設定',
            requiresAuth: false
        }
    },
    // ── 社群風格頁面 ──
    },
    },
    },
    },
    },
    // ── 員警社群頁面 ──
    },
    },
    },
    },
    },
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
