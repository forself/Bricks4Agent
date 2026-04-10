import { BasePage } from '../../core/BasePage.js';
import { SearchForm } from '../../../../../packages/javascript/browser/ui_components/form/SearchForm/SearchForm.js';
import { DataTable } from '../../../../../packages/javascript/browser/ui_components/layout/DataTable/DataTable.js';
import { raw, escapeAttr, escapeHtml } from '../../../../../packages/javascript/browser/ui_components/utils/security.js';
import { ensureCategoryOptions, getCategoryOptions, getStatusOptions, getCategoryLabel, getStatusLabel } from '../commerce.constants.js';

export class AdminProductPage extends BasePage {
    async onInit() {
        this._products = [];
        this._filteredProducts = [];
        this._filters = {};
        this._searchForm = null;
        this._table = null;
        this._message = this.query?.flash === 'created'
            ? '商品已建立'
            : this.query?.flash === 'updated'
                ? '商品已更新'
                : null;
        this._error = null;

        await ensureCategoryOptions(this.api);
        await this._loadProducts();
    }

    template() {
        return `
            <div class="admin-product-page">
                <header class="page-header">
                    <h1>商品後台管理</h1>
                    <p class="page-subtitle">直接使用元件庫做清單搜尋，新增與編輯頁走 runtime definition。</p>
                </header>

                <section class="card">
                    <div class="card-body">
                        <div class="form-row form-row--between" style="margin-bottom:16px;">
                            <a href="#/admin/products/create" class="btn btn-primary">新增商品</a>
                        </div>
                        <div data-feedback-host></div>
                        <div data-search-host></div>
                        <div data-table-host style="margin-top:16px;"></div>
                    </div>
                </section>
            </div>
        `;
    }

    events() {
        return {
            'click .js-edit-product': 'onEditProductClick',
            'click .js-delete-product': 'onDeleteProductClick'
        };
    }

    async onMounted() {
        this._mountSearchForm();
        this._mountTable();
        this._renderFeedback();
        this._refreshTable();
    }

    async onDestroy() {
        this._searchForm?.destroy();
        this._table?.destroy();
    }

    async _loadProducts() {
        this.showLoading();
        try {
            const products = await this.api.get('/admin/products');
            this._products = Array.isArray(products) ? products : [];
            this._filteredProducts = [...this._products];
        } catch (error) {
            this._error = error.message || '無法載入後台商品。';
        } finally {
            this.hideLoading();
        }
    }

    _mountSearchForm() {
        const host = this.$('[data-search-host]');
        if (!host) {
            return;
        }

        this._searchForm?.destroy();
        this._searchForm = new SearchForm({
            fields: [
                { key: 'keyword', label: '關鍵字', type: SearchForm.FIELD_TYPES.TEXT, placeholder: '商品名稱或描述' },
                { key: 'categoryId', label: '分類', type: SearchForm.FIELD_TYPES.SELECT, options: getCategoryOptions() },
                { key: 'status', label: '狀態', type: SearchForm.FIELD_TYPES.SELECT, options: getStatusOptions() }
            ],
            columns: 3,
            showReset: true,
            onSearch: (values) => {
                this._filters = values;
                this._applyFilters();
            },
            onReset: () => {
                this._filters = {};
                this._applyFilters();
            }
        });
        this._searchForm.mount(host);
    }

    _mountTable() {
        const host = this.$('[data-table-host]');
        if (!host) {
            return;
        }

        this._table?.destroy();
        this._table = new DataTable({
            columns: [
                { key: 'id', title: 'ID', hidden: true },
                { key: 'name', title: '商品名稱' },
                { key: 'categoryName', title: '分類' },
                {
                    key: 'price',
                    title: '價格',
                    render: (value) => `NT$ ${Number(value).toLocaleString()}`
                },
                { key: 'stock', title: '庫存' },
                { key: 'statusLabel', title: '狀態' },
                {
                    key: 'actions',
                    title: '操作',
                    render: (_, row) => raw(`
                        <div style="display:flex;gap:8px;justify-content:center;">
                            <button class="btn js-edit-product" data-product-id="${escapeAttr(row.id)}">編輯</button>
                            <button class="btn js-delete-product" data-product-id="${escapeAttr(row.id)}" data-product-name="${escapeAttr(row.name)}">刪除</button>
                        </div>
                    `)
                }
            ],
            data: [],
            pageSize: 10,
            emptyText: '目前沒有商品資料'
        });
        this._table.mount(host);
    }

    _applyFilters() {
        const keyword = String(this._filters.keyword || '').trim().toLowerCase();
        const categoryId = this._filters.categoryId;
        const status = this._filters.status;

        this._filteredProducts = this._products.filter((product) => {
            if (keyword) {
                const haystack = `${product.name} ${product.description || ''}`.toLowerCase();
                if (!haystack.includes(keyword)) {
                    return false;
                }
            }

            if (categoryId && Number(product.categoryId) !== Number(categoryId)) {
                return false;
            }

            if (status && product.status !== status) {
                return false;
            }

            return true;
        });

        this._refreshTable();
    }

    _refreshTable() {
        const rows = this._filteredProducts.map((product) => ({
            ...product,
            categoryName: getCategoryLabel(product.categoryId),
            statusLabel: getStatusLabel(product.status)
        }));
        this._table?.setData(rows);
    }

    _renderFeedback() {
        const host = this.$('[data-feedback-host]');
        if (!host) {
            return;
        }

        if (this._error) {
            host.innerHTML = `<div class="login-error">${this.esc(this._error)}</div>`;
            return;
        }

        if (this._message) {
            host.innerHTML = `<div class="login-success">${this.esc(this._message)}</div>`;
            return;
        }

        host.innerHTML = '';
    }

    onEditProductClick(event, target) {
        const productId = target.dataset.productId;
        this.navigate(`/admin/products/${productId}/edit`);
    }

    async onDeleteProductClick(event, target) {
        const productId = target.dataset.productId;
        const productName = target.dataset.productName;
        const confirmed = window.confirm(`確定要刪除 ${productName} 嗎？`);

        if (!confirmed) {
            return;
        }

        this.showLoading();
        try {
            await this.api.delete(`/admin/products/${productId}`);
            this._message = '商品已刪除';
            this._error = null;
            await this._loadProducts();
            this._applyFilters();
        } catch (error) {
            this._error = error.message || '刪除商品失敗。';
        } finally {
            this.hideLoading();
            this._renderFeedback();
        }
    }
}

export default AdminProductPage;
