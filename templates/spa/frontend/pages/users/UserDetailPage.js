/**
 * UserDetailPage - 使用者詳情頁面
 *
 * @module UserDetailPage
 */

import { BasePage } from '../../core/BasePage.js';

export class UserDetailPage extends BasePage {
    async onInit() {
        this._data = {
            user: null,
            loading: true,
            editing: false
        };

        await this._loadUser();
    }

    async _loadUser() {
        const userId = this.params.id;

        try {
            this._data.loading = true;

            // 模擬 API 呼叫
            // const user = await this.api.get(`/users/${userId}`);

            await new Promise(resolve => setTimeout(resolve, 200));

            // 模擬資料
            this._data.user = {
                id: parseInt(userId),
                name: 'Alice Chen',
                email: 'alice@example.com',
                role: '管理員',
                status: 'active',
                phone: '0912-345-678',
                department: '資訊部',
                createdAt: '2026-01-20',
                lastLogin: '2026-01-25 14:30'
            };

        } catch (error) {
            console.error('[UserDetailPage] 載入使用者失敗:', error);
            this.showMessage('載入失敗', 'error');
        } finally {
            this._data.loading = false;
        }
    }

    template() {
        const { user, loading, editing } = this._data;

        if (loading) {
            return `
                <div class="user-detail-page">
                    <div class="loading-state">
                        <div class="loading-spinner"></div>
                        <p>載入中...</p>
                    </div>
                </div>
            `;
        }

        if (!user) {
            return `
                <div class="user-detail-page">
                    <div class="empty-state">
                        <h2>找不到使用者</h2>
                        <a href="#/users" class="btn">返回列表</a>
                    </div>
                </div>
            `;
        }

        return `
            <div class="user-detail-page">
                <!-- 返回按鈕 -->
                <div class="page-back">
                    <a href="#/users" class="back-link">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <line x1="19" y1="12" x2="5" y2="12"/>
                            <polyline points="12 19 5 12 12 5"/>
                        </svg>
                        返回列表
                    </a>
                </div>

                <!-- 使用者資訊卡 -->
                <div class="detail-card">
                    <div class="detail-header">
                        <div class="user-avatar-large">${this.esc(user.name?.[0] || '?')}</div>
                        <div class="user-info">
                            <h2>${this.esc(user.name)}</h2>
                            <p class="user-email">${this.esc(user.email)}</p>
                            <div class="user-badges">
                                <span class="badge badge-role">${this.esc(user.role)}</span>
                                <span class="badge badge-status badge-status--${this.escAttr(user.status)}">
                                    ${user.status === 'active' ? '啟用' : '停用'}
                                </span>
                            </div>
                        </div>
                        <div class="detail-actions">
                            <button class="btn btn-secondary" id="btn-edit">
                                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                    <path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/>
                                    <path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>
                                </svg>
                                編輯
                            </button>
                        </div>
                    </div>

                    <div class="detail-body">
                        <div class="detail-section">
                            <h3>基本資訊</h3>
                            <div class="detail-grid">
                                <div class="detail-item">
                                    <label>電話</label>
                                    <span>${this.esc(user.phone || '-')}</span>
                                </div>
                                <div class="detail-item">
                                    <label>部門</label>
                                    <span>${this.esc(user.department || '-')}</span>
                                </div>
                                <div class="detail-item">
                                    <label>建立時間</label>
                                    <span>${this.esc(user.createdAt)}</span>
                                </div>
                                <div class="detail-item">
                                    <label>最後登入</label>
                                    <span>${this.esc(user.lastLogin || '-')}</span>
                                </div>
                            </div>
                        </div>

                        <div class="detail-section">
                            <h3>操作</h3>
                            <div class="action-buttons">
                                <button class="btn btn-outline" id="btn-reset-password">
                                    重設密碼
                                </button>
                                <button class="btn btn-outline ${user.status === 'active' ? 'btn-warning' : 'btn-success'}"
                                        id="btn-toggle-status">
                                    ${user.status === 'active' ? '停用帳號' : '啟用帳號'}
                                </button>
                                <button class="btn btn-danger" id="btn-delete">
                                    刪除帳號
                                </button>
                            </div>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    events() {
        return {
            'click #btn-edit': 'onEdit',
            'click #btn-reset-password': 'onResetPassword',
            'click #btn-toggle-status': 'onToggleStatus',
            'click #btn-delete': 'onDelete'
        };
    }

    onEdit() {
        this.navigate(`/users/${this.params.id}/edit`);
    }

    async onResetPassword() {
        if (!confirm('確定要重設此使用者的密碼嗎？')) return;

        try {
            // await this.api.post(`/users/${this.params.id}/reset-password`);
            this.showMessage('密碼重設郵件已發送', 'success');
        } catch (error) {
            this.showMessage('操作失敗', 'error');
        }
    }

    async onToggleStatus() {
        const user = this._data.user;
        const newStatus = user.status === 'active' ? 'inactive' : 'active';
        const action = newStatus === 'active' ? '啟用' : '停用';

        if (!confirm(`確定要${action}此帳號嗎？`)) return;

        try {
            // await this.api.patch(`/users/${this.params.id}`, { status: newStatus });
            this._data.user.status = newStatus;
            this.showMessage(`帳號已${action}`, 'success');
        } catch (error) {
            this.showMessage('操作失敗', 'error');
        }
    }

    async onDelete() {
        if (!confirm('確定要刪除此使用者嗎？此操作無法復原。')) return;

        try {
            // await this.api.delete(`/users/${this.params.id}`);
            this.showMessage('使用者已刪除', 'success');
            this.navigate('/users');
        } catch (error) {
            this.showMessage('刪除失敗', 'error');
        }
    }
}

export default UserDetailPage;
