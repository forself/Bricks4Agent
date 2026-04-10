import { BasePage } from '../../core/BasePage.js';
import { SearchForm } from '../../../../../packages/javascript/browser/ui_components/form/SearchForm/SearchForm.js';
import { DataTable } from '../../../../../packages/javascript/browser/ui_components/layout/DataTable/DataTable.js';
import { raw, escapeAttr, escapeHtml } from '../../../../../packages/javascript/browser/ui_components/utils/security.js';
import { ensureCategoryOptions, getCategoryOptions, getStatusOptions, getCategoryLabel, getStatusLabel } from '../commerce.constants.js';

export class ProductListPage extends BasePage {
    async onInit() {
        this._products = [];
        this._filteredProducts = [];
        this._filters = {};
        this._selectedProduct = null;
        this._searchForm = null;
        this._dataTable = null;
        this._message = null;
        this._error = null;

        await ensureCategoryOptions(this.api);
        await this._loadProducts();
    }

    template() {
        return `
            <div class="product-list-page">
                <header class="page-header">
                    <h1>商品商城</h1>
                    <p class="page-subtitle">直接使用 SearchForm 與 DataTable 完成前台商品瀏覽與下單。</p>
                </header>

                <section class="card">
                    <div class="card-body">
                        <div data-feedback-host></div>
                        <div data-search-host></div>
                        <div data-table-host style="margin-top:16px;"></div>
                    </div>
                </section>

                <section class="card" style="margin-top:16px;">
                    <div class="card-body">
                        <div data-order-host></div>
                    </div>
                </section>
            </div>
        `;
    }

    events() {
        return {
            'click .js-buy-product': 'onBuyProductClick',
            'click #cancel-order': 'onCancelOrderClick',
            'submit #order-form': 'onSubmitOrder'
        };
    }

    async onMounted() {
        this._mountSearchForm();
        this._mountTable();
        this._renderFeedback();
        this._renderOrderForm();
        this._refreshTable();
    }

    async onDestroy() {
        this._searchForm?.destroy();
        this._dataTable?.destroy();
    }

    async _loadProducts() {
        this.showLoading();
        try {
            const products = await this.api.get('/shop/products');
            this._products = Array.isArray(products) ? products : [];
            this._filteredProducts = [...this._products];
        } catch (error) {
            this._error = error.message || '無法載入商品清單。';
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

        this._dataTable?.destroy();
        this._dataTable = new DataTable({
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
                {
                    key: 'statusLabel',
                    title: '狀態'
                },
                {
                    key: 'action',
                    title: '操作',
                    render: (_, row) => raw(
                        `<button class="btn btn-primary js-buy-product" data-product-id="${escapeAttr(row.id)}" aria-label="購買 ${escapeAttr(row.name)}">購買 ${escapeHtml(row.name)}</button>`
                    )
                }
            ],
            data: [],
            pageSize: 10,
            emptyText: '目前沒有符合條件的商品'
        });
        this._dataTable.mount(host);
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
        if (!this._dataTable) {
            return;
        }

        const rows = this._filteredProducts.map((product) => ({
            ...product,
            categoryName: getCategoryLabel(product.categoryId),
            statusLabel: getStatusLabel(product.status)
        }));
        this._dataTable.setData(rows);
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

    _renderOrderForm() {
        const host = this.$('[data-order-host]');
        if (!host) {
            return;
        }

        if (!this._selectedProduct) {
            host.innerHTML = `
                <div class="empty-state">
                    <h2>尚未選擇商品</h2>
                    <p>請在商品列表點選購買，建立會員訂單。</p>
                </div>
            `;
            return;
        }

        host.innerHTML = `
            <h2>建立訂單</h2>
            <p>商品：<strong>${this.esc(this._selectedProduct.name)}</strong>，價格 NT$ ${Number(this._selectedProduct.price).toLocaleString()}</p>
            <form id="order-form" class="login-form">
                <div class="form-group">
                    <label for="orderQuantity">數量</label>
                    <input id="orderQuantity" name="quantity" type="number" class="form-input" value="1" min="1" max="${this.escAttr(this._selectedProduct.stock)}" required>
                </div>
                <div class="form-group">
                    <label for="shippingAddress">收件地址</label>
                    <input id="shippingAddress" name="shippingAddress" type="text" class="form-input" required>
                </div>
                <div class="form-group">
                    <label for="orderNote">備註</label>
                    <textarea id="orderNote" name="note" class="form-input" rows="3"></textarea>
                </div>
                <div class="form-row form-row--between">
                    <button type="button" class="btn" id="cancel-order">取消</button>
                    <button type="submit" class="btn btn-primary">送出訂單</button>
                </div>
            </form>
        `;
    }

    onBuyProductClick(event, target) {
        const productId = Number(target.dataset.productId);
        this._selectedProduct = this._products.find((product) => product.id === productId) || null;
        this._message = null;
        this._error = null;
        this._renderFeedback();
        this._renderOrderForm();
    }

    onCancelOrderClick() {
        this._selectedProduct = null;
        this._renderOrderForm();
    }

    async onSubmitOrder(event) {
        event.preventDefault();

        if (!this._selectedProduct) {
            return;
        }

        const quantity = Number(this.$('#orderQuantity')?.value || 0);
        const shippingAddress = this.$('#shippingAddress')?.value?.trim() || '';
        const note = this.$('#orderNote')?.value?.trim() || '';

        this.showLoading();
        try {
            await this.api.post('/shop/orders', {
                productId: this._selectedProduct.id,
                quantity,
                shippingAddress,
                note
            });

            this._message = '訂單已建立';
            this._error = null;
            this._selectedProduct = null;
            await this._loadProducts();
            this._applyFilters();
        } catch (error) {
            this._error = error.message || '建立訂單失敗。';
        } finally {
            this.hideLoading();
            this._renderFeedback();
            this._renderOrderForm();
        }
    }
}

export default ProductListPage;
