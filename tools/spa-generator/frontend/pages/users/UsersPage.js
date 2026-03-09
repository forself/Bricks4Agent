/**
 * UsersPage - 使用者管理頁面 (巢狀路由容器)
 *
 * 這是一個巢狀頁面範例，包含子路由出口
 *
 * @module UsersPage
 */

import { NestedPage } from '../../core/NestedPage.js';

export class UsersPage extends NestedPage {
    /**
     * 定義子導航
     */
    getSubNav() {
        return [
            { path: '/users', label: '使用者列表', icon: 'list', exact: true },
            { path: '/users/create', label: '新增使用者', icon: 'plus' }
        ];
    }

    template() {
        return `
            <div class="users-page">
                <header class="page-header">
                    <h1>使用者管理</h1>
                    <p class="page-subtitle">管理系統使用者帳號</p>
                </header>

                <!-- 子導航 -->
                ${this.renderSubNav()}

                <!-- 子路由出口 -->
                <div class="nested-content">
                    ${this.renderOutlet()}
                </div>
            </div>
        `;
    }
}

export default UsersPage;
