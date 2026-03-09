/**
 * HomePage - 首頁
 *
 * @module HomePage
 */

import { BasePage } from '../core/BasePage.js';

export class HomePage extends BasePage {
    async onInit() {
        this._data = {
            title: '歡迎使用 SPA 應用程式',
            stats: {
                users: 0,
                orders: 0,
                revenue: 0
            },
            recentActivities: []
        };

        // 載入資料
        await this._loadDashboardData();
    }

    async _loadDashboardData() {
        try {
            // 模擬 API 呼叫
            // const data = await this.api.get('/dashboard');
            // this.data.stats = data.stats;
            // this.data.recentActivities = data.activities;

            // 模擬資料
            this._data.stats = {
                users: 1234,
                orders: 567,
                revenue: 89012
            };

            this._data.recentActivities = [
                { id: 1, action: '新使用者註冊', user: 'Alice', time: '5 分鐘前' },
                { id: 2, action: '訂單完成', user: 'Bob', time: '12 分鐘前' },
                { id: 3, action: '商品上架', user: 'Charlie', time: '30 分鐘前' }
            ];

        } catch (error) {
            console.error('[HomePage] 載入資料失敗:', error);
        }
    }

    template() {
        const { title, stats, recentActivities } = this._data;

        return `
            <div class="home-page">
                <header class="page-header">
                    <h1>${this.esc(title)}</h1>
                    <p class="page-subtitle">管理您的應用程式</p>
                </header>

                <!-- 統計卡片 -->
                <section class="stats-grid">
                    <div class="stat-card">
                        <div class="stat-icon stat-icon--users">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                                <circle cx="9" cy="7" r="4"/>
                                <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
                                <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
                            </svg>
                        </div>
                        <div class="stat-content">
                            <div class="stat-value">${stats.users.toLocaleString()}</div>
                            <div class="stat-label">使用者</div>
                        </div>
                    </div>

                    <div class="stat-card">
                        <div class="stat-icon stat-icon--orders">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M6 2L3 6v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6l-3-4z"/>
                                <line x1="3" y1="6" x2="21" y2="6"/>
                                <path d="M16 10a4 4 0 0 1-8 0"/>
                            </svg>
                        </div>
                        <div class="stat-content">
                            <div class="stat-value">${stats.orders.toLocaleString()}</div>
                            <div class="stat-label">訂單</div>
                        </div>
                    </div>

                    <div class="stat-card">
                        <div class="stat-icon stat-icon--revenue">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <line x1="12" y1="1" x2="12" y2="23"/>
                                <path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>
                            </svg>
                        </div>
                        <div class="stat-content">
                            <div class="stat-value">$${stats.revenue.toLocaleString()}</div>
                            <div class="stat-label">營收</div>
                        </div>
                    </div>
                </section>

                <!-- 快速操作 -->
                <section class="quick-actions">
                    <h2>快速操作</h2>
                    <div class="action-buttons">
                        <a href="#/users/create" class="action-btn action-btn--primary">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                                <circle cx="8.5" cy="7" r="4"/>
                                <line x1="20" y1="8" x2="20" y2="14"/>
                                <line x1="23" y1="11" x2="17" y2="11"/>
                            </svg>
                            新增使用者
                        </a>
                        <a href="#/users" class="action-btn">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/>
                                <circle cx="9" cy="7" r="4"/>
                                <path d="M23 21v-2a4 4 0 0 0-3-3.87"/>
                                <path d="M16 3.13a4 4 0 0 1 0 7.75"/>
                            </svg>
                            管理使用者
                        </a>
                        <a href="#/settings" class="action-btn">
                            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                                <circle cx="12" cy="12" r="3"/>
                                <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/>
                            </svg>
                            系統設定
                        </a>
                    </div>
                </section>

                <!-- 最近活動 -->
                <section class="recent-activities">
                    <h2>最近活動</h2>
                    <div class="activity-list">
                        ${recentActivities.map(activity => `
                            <div class="activity-item">
                                <div class="activity-avatar">${this.esc(activity.user?.[0] || '?')}</div>
                                <div class="activity-content">
                                    <div class="activity-text">
                                        <strong>${this.esc(activity.user)}</strong> ${this.esc(activity.action)}
                                    </div>
                                    <div class="activity-time">${this.esc(activity.time)}</div>
                                </div>
                            </div>
                        `).join('')}
                    </div>
                </section>
            </div>
        `;
    }

    events() {
        return {
            'click .action-btn': 'onActionClick'
        };
    }

    onActionClick(event, target) {
        // 可以加入追蹤或其他邏輯
        console.log('[HomePage] 操作按鈕點擊:', target.textContent.trim());
    }
}

export default HomePage;
