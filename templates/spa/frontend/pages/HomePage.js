import { BasePage } from '../core/BasePage.js';

export class HomePage extends BasePage {
    async onInit() {
        this._data = {
            user: this.store?.get('user') || null,
            stats: null
        };

        if (this._data.user?.role === 'admin') {
            await this._loadAdminStats();
        }
    }

    async _loadAdminStats() {
        try {
            this._data.stats = await this.api.get('/dashboard');
        } catch (error) {
            console.warn('[HomePage] failed to load dashboard stats', error);
        }
    }

    template() {
        const user = this._data.user;
        const stats = this._data.stats?.stats || null;

        return `
            <div class="home-page">
                <header class="page-header">
                    <h1>會員商務網站驗證</h1>
                    <p class="page-subtitle">
                        ${user ? `目前登入：${this.esc(user.name)}（${this.esc(user.role)}）` : '請先註冊或登入後繼續驗證流程。'}
                    </p>
                </header>

                <section class="card">
                    <div class="card-body">
                        <h2>驗證目標</h2>
                        <ul>
                            <li>前台會員註冊、登入與商品購買</li>
                            <li>訂單資料持久化與會員查詢</li>
                            <li>後台商品管理與編輯流程</li>
                            <li>前端直接使用元件庫與 runtime/generator 疊代</li>
                        </ul>
                    </div>
                </section>

                <section class="card" style="margin-top:16px;">
                    <div class="card-body">
                        <h2>快速入口</h2>
                        <div class="action-buttons">
                            <a href="#/register" class="action-btn action-btn--primary">開始註冊</a>
                            <a href="#/login" class="action-btn">前往登入</a>
                            <a href="#/products" class="action-btn">前往商品商城</a>
                            <a href="#/orders" class="action-btn">查看我的訂單</a>
                            <a href="#/admin/products" class="action-btn">進入後台商品管理</a>
                        </div>
                    </div>
                </section>

                ${stats ? `
                    <section class="card" style="margin-top:16px;">
                        <div class="card-body">
                            <h2>後台摘要</h2>
                            <div class="stats-grid">
                                <div class="stat-card"><div class="stat-value">${this.esc(stats.users ?? 0)}</div><div class="stat-label">會員數</div></div>
                                <div class="stat-card"><div class="stat-value">${this.esc(stats.products ?? 0)}</div><div class="stat-label">商品數</div></div>
                                <div class="stat-card"><div class="stat-value">${this.esc(stats.orders ?? 0)}</div><div class="stat-label">訂單數</div></div>
                                <div class="stat-card"><div class="stat-value">${this.esc(stats.revenue ?? 0)}</div><div class="stat-label">營收總額</div></div>
                            </div>
                        </div>
                    </section>
                ` : ''}
            </div>
        `;
    }
}

export default HomePage;
