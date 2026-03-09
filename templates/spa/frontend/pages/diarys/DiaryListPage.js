/**
 * DiaryList - DiaryList 頁面
 *
 * @module DiaryListPage
 */

import { BasePage } from '../../core/BasePage.js';

export class DiaryListPage extends BasePage {
    async onInit() {
        this._data = {
            items: [],
            loading: true,
            error: null
        };

        await this._loadData();
    }

    async _loadData() {
        try {
            this._data.loading = true;

            // TODO: 替換為實際 API 呼叫
            // const items = await this.api.get('/diarys');
            // this._data.items = items;

            // 模擬資料
            await new Promise(resolve => setTimeout(resolve, 300));
            this._data.items = [
                { id: 1, name: '範例項目 1' },
                { id: 2, name: '範例項目 2' },
                { id: 3, name: '範例項目 3' }
            ];

        } catch (error) {
            console.error('[DiaryListPage] 載入資料失敗:', error);
            this._data.error = error.message;
            this.showMessage('載入失敗', 'error');
        } finally {
            this._data.loading = false;
        }
    }

    template() {
        const { items, loading, error } = this._data;

        if (loading) {
            return `
                <div class="diary-list-page">
                    <div class="loading-state">
                        <div class="loading-spinner"></div>
                        <p>載入中...</p>
                    </div>
                </div>
            `;
        }

        if (error) {
            return `
                <div class="diary-list-page">
                    <div class="error-state">
                        <h2>載入失敗</h2>
                        <p>${this.esc(error)}</p>
                        <button class="btn btn-primary" id="btn-retry">重試</button>
                    </div>
                </div>
            `;
        }

        return `
            <div class="diary-list-page">
                <header class="page-header">
                    <h1>DiaryList</h1>
                    <p class="page-subtitle">DiaryList 頁面</p>
                </header>

                <div class="page-content">
                    <div class="list-container">
                        ${items.length === 0 ? `
                            <div class="empty-state">
                                <p>目前沒有資料</p>
                            </div>
                        ` : `
                            <ul class="item-list">
                                ${items.map(item => `
                                    <li class="item" data-id="${this.escAttr(item.id)}">
                                        <span class="item-name">${this.esc(item.name)}</span>
                                        <div class="item-actions">
                                            <button class="btn btn-sm btn-edit" data-id="${this.escAttr(item.id)}">
                                                編輯
                                            </button>
                                            <button class="btn btn-sm btn-danger btn-delete" data-id="${this.escAttr(item.id)}">
                                                刪除
                                            </button>
                                        </div>
                                    </li>
                                `).join('')}
                            </ul>
                        `}
                    </div>
                </div>
            </div>
        `;
    }

    events() {
        return {
            'click #btn-retry': 'onRetry',
            'click .btn-edit': 'onEdit',
            'click .btn-delete': 'onDelete'
        };
    }

    onRetry() {
        this._loadData();
    }

    onEdit(event, target) {
        const id = target.dataset.id;
        this.navigate(`/diarys/diary-list/${id}/edit`);
    }

    async onDelete(event, target) {
        const id = target.dataset.id;
        const item = this._data.items.find(i => i.id === parseInt(id));

        if (!confirm(`確定要刪除「${item?.name}」嗎？`)) {
            return;
        }

        try {
            // await this.api.delete(`/diarys/${id}`);
            this._data.items = this._data.items.filter(i => i.id !== parseInt(id));
            this.showMessage('已刪除', 'success');
        } catch (error) {
            this.showMessage('刪除失敗', 'error');
        }
    }
}

export default DiaryListPage;
