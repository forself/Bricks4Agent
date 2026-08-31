/**
 * UserListPage - 使用者列表頁面
 *
 * @module UserListPage
 */

import { BasePage } from '../../core/BasePage.js';

export class UserListPage extends BasePage {
    async onInit() {
        this._data = {
            users: [],
            loading: true,
            searchQuery: '',
            currentPage: 1,
            totalPages: 1,
            pageSize: 10
        };

        await this._loadUsers();
    }

    async _loadUsers() {
        try {
            this._data.loading = true;

            // 模擬 API 呼叫
            // const result = await this.api.get('/users', {
            //     params: {
            //         page: this._data.currentPage,
            //         pageSize: this._data.pageSize,
            //         search: this._data.searchQuery
            //     }
            // });

            // 模擬資料
            await new Promise(resolve => setTimeout(resolve, 300));

            this._data.users = [
                { id: 1, name: 'Alice Chen', email: 'alice@example.com', role: '管理員', status: 'active', createdAt: '2026-01-20' },
                { id: 2, name: 'Bob Wang', email: 'bob@example.com', role: '使用者', status: 'active', createdAt: '2026-01-19' },
                { id: 3, name: 'Charlie Lin', email: 'charlie@example.com', role: '使用者', status: 'inactive', createdAt: '2026-01-18' },
                { id: 4, name: 'Diana Lee', email: 'diana@example.com', role: '編輯', status: 'active', createdAt: '2026-01-17' },
                { id: 5, name: 'Eve Wu', email: 'eve@example.com', role: '使用者', status: 'active', createdAt: '2026-01-16' }
            ];

            this._data.totalPages = 3;

        } catch (error) {
            console.error('[UserListPage] 載入使用者失敗:', error);
            this.showMessage('載入失敗', 'error');
        } finally {
            this._data.loading = false;
        }
    }

    template() {
        const { users, loading, searchQuery, currentPage, totalPages } = this._data;

        if (loading) {
            return `
                <div class="user-list-page">
                    <div class="loading-state">
                        <div class="loading-spinner"></div>
                        <p>載入中...</p>
                    </div>
                </div>
            `;
        }

        return `
            <div class="user-list-page">
                <!-- 搜尋列 -->
                <div class="list-toolbar">
                    <div class="search-box">
                        <canvas class="search-icon" data-canvas-icon="search" data-canvas-icon-size="24" width="24" height="24" aria-hidden="true"></canvas>
                        <input type="text"
                               class="search-input"
                               placeholder="搜尋使用者..."
                               value="${this.escAttr(searchQuery)}"
                               id="user-search">
                    </div>
                    <a href="#/users/create" class="btn btn-primary">
                        <canvas data-canvas-icon="plus" data-canvas-icon-size="24" width="24" height="24" aria-hidden="true"></canvas>
                        新增使用者
                    </a>
                </div>

                <!-- 使用者表格 -->
                <div class="table-container">
                    <table class="data-table">
                        <thead>
                            <tr>
                                <th>姓名</th>
                                <th>Email</th>
                                <th>角色</th>
                                <th>狀態</th>
                                <th>建立日期</th>
                                <th>操作</th>
                            </tr>
                        </thead>
                        <tbody>
                            ${users.map(user => `
                                <tr data-user-id="${this.escAttr(user.id)}">
                                    <td>
                                        <div class="user-cell">
                                            <div class="user-avatar">${this.esc(user.name?.[0] || '?')}</div>
                                            <span>${this.esc(user.name)}</span>
                                        </div>
                                    </td>
                                    <td>${this.esc(user.email)}</td>
                                    <td><span class="badge badge-role">${this.esc(user.role)}</span></td>
                                    <td>
                                        <span class="badge badge-status badge-status--${this.escAttr(user.status)}">
                                            ${user.status === 'active' ? '啟用' : '停用'}
                                        </span>
                                    </td>
                                    <td>${this.esc(user.createdAt)}</td>
                                    <td>
                                        <div class="action-cell">
                                            <a href="#/users/${this.escAttr(user.id)}" class="action-link" title="檢視">
                                                <canvas data-canvas-icon="eye" data-canvas-icon-size="24" width="24" height="24" aria-hidden="true"></canvas>
                                            </a>
                                            <button class="action-link btn-delete" data-id="${this.escAttr(user.id)}" title="刪除">
                                                <canvas data-canvas-icon="trash" data-canvas-icon-size="24" width="24" height="24" aria-hidden="true"></canvas>
                                            </button>
                                        </div>
                                    </td>
                                </tr>
                            `).join('')}
                        </tbody>
                    </table>
                </div>

                <!-- 分頁 -->
                ${totalPages > 1 ? `
                    <div class="pagination">
                        <button class="page-btn" ${currentPage === 1 ? 'disabled' : ''} data-page="${currentPage - 1}">
                            上一頁
                        </button>
                        <span class="page-info">第 ${currentPage} / ${totalPages} 頁</span>
                        <button class="page-btn" ${currentPage === totalPages ? 'disabled' : ''} data-page="${currentPage + 1}">
                            下一頁
                        </button>
                    </div>
                ` : ''}
            </div>
        `;
    }

    events() {
        return {
            'input #user-search': 'onSearch',
            'click .btn-delete': 'onDelete',
            'click .page-btn': 'onPageChange'
        };
    }

    onSearch(event) {
        const query = event.target.value;
        this._data.searchQuery = query;

        // 防抖搜尋
        clearTimeout(this._searchTimer);
        this._searchTimer = setTimeout(() => {
            this._data.currentPage = 1;
            this._loadUsers();
        }, 300);
    }

    async onDelete(event, target) {
        const userId = target.dataset.id;
        const user = this._data.users.find(u => u.id === parseInt(userId));

        if (!confirm(`確定要刪除使用者「${user?.name}」嗎？`)) {
            return;
        }

        try {
            // await this.api.delete(`/users/${userId}`);
            this._data.users = this._data.users.filter(u => u.id !== parseInt(userId));
            this.showMessage('使用者已刪除', 'success');
        } catch (error) {
            this.showMessage('刪除失敗', 'error');
        }
    }

    onPageChange(event, target) {
        if (target.disabled) return;

        const page = parseInt(target.dataset.page);
        this._data.currentPage = page;
        this._loadUsers();
    }
}

export default UserListPage;
