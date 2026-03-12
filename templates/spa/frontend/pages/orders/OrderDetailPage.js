/**
 * OrderDetail - OrderDetail 頁面
 *
 * @module OrderDetailPage
 */

import { BasePage } from '../../core/BasePage.js';

export class OrderDetailPage extends BasePage {
    async onInit() {
        this._data = {
            item: null,
            loading: true,
            error: null
        };

        await this._loadItem();
    }

    async _loadItem() {
        const id = this.params.id;

        try {
            this._data.loading = true;

            // TODO: 替換為實際 API 呼叫
            // const item = await this.api.get(`/orders/${id}`);
            // this._data.item = item;

            // 模擬資料
            await new Promise(resolve => setTimeout(resolve, 200));
            this._data.item = {
                id: parseInt(id),
                name: `項目 ${id}`,
                description: '這是項目描述',
                createdAt: '2026-01-25'
            };

        } catch (error) {
            console.error('[OrderDetailPage] 載入失敗:', error);
            this._data.error = error.message;
        } finally {
            this._data.loading = false;
        }
    }

    template() {
        const { item, loading, error } = this._data;

        if (loading) {
            return `
                <div class="order-detail-page">
                    <div class="loading-state">
                        <div class="loading-spinner"></div>
                        <p>載入中...</p>
                    </div>
                </div>
            `;
        }

        if (error || !item) {
            return `
                <div class="order-detail-page">
                    <div class="error-state">
                        <h2>找不到資料</h2>
                        <a href="#/orders/order-detail" class="btn">返回列表</a>
                    </div>
                </div>
            `;
        }

        return `
            <div class="order-detail-page">
                <div class="page-back">
                    <a href="#/orders/order-detail" class="back-link">
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                            <line x1="19" y1="12" x2="5" y2="12"/>
                            <polyline points="12 19 5 12 12 5"/>
                        </svg>
                        返回列表
                    </a>
                </div>

                <div class="detail-card">
                    <div class="detail-header">
                        <h2>${this.esc(item.name)}</h2>
                    </div>

                    <div class="detail-body">
                        <div class="detail-section">
                            <h3>基本資訊</h3>
                            <div class="detail-grid">
                                <div class="detail-item">
                                    <label>ID</label>
                                    <span>${this.esc(item.id)}</span>
                                </div>
                                <div class="detail-item">
                                    <label>描述</label>
                                    <span>${this.esc(item.description || '-')}</span>
                                </div>
                                <div class="detail-item">
                                    <label>建立時間</label>
                                    <span>${this.esc(item.createdAt)}</span>
                                </div>
                            </div>
                        </div>

                        <div class="detail-actions">
                            <button class="btn btn-primary" id="btn-edit">編輯</button>
                            <button class="btn btn-danger" id="btn-delete">刪除</button>
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    events() {
        return {
            'click #btn-edit': 'onEdit',
            'click #btn-delete': 'onDelete'
        };
    }

    onEdit() {
        this.navigate(`/orders/order-detail/${this.params.id}/edit`);
    }

    async onDelete() {
        if (!confirm('確定要刪除嗎？此操作無法復原。')) {
            return;
        }

        try {
            // await this.api.delete(`/orders/${this.params.id}`);
            this.showMessage('已刪除', 'success');
            this.navigate('/orders/order-detail');
        } catch (error) {
            this.showMessage('刪除失敗', 'error');
        }
    }
}

export default OrderDetailPage;
