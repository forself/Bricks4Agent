import { BasePage } from '../../core/BasePage.js';
import { DataTable } from '../../../../../packages/javascript/browser/ui_components/layout/DataTable/DataTable.js';

export class OrderListPage extends BasePage {
    async onInit() {
        this._orders = [];
        this._table = null;
        this._error = null;

        await this._loadOrders();
    }

    template() {
        return `
            <div class="order-list-page">
                <header class="page-header">
                    <h1>我的訂單</h1>
                    <p class="page-subtitle">直接使用 DataTable 顯示會員訂單與持久化結果。</p>
                </header>

                <section class="card">
                    <div class="card-body">
                        <div data-feedback-host></div>
                        <div data-table-host></div>
                    </div>
                </section>
            </div>
        `;
    }

    async onMounted() {
        this._mountTable();
        this._renderFeedback();
        this._refreshTable();
    }

    async onDestroy() {
        this._table?.destroy();
    }

    async _loadOrders() {
        this.showLoading();
        try {
            const orders = await this.api.get('/shop/orders');
            this._orders = Array.isArray(orders) ? orders : [];
        } catch (error) {
            this._error = error.message || '無法載入訂單。';
        } finally {
            this.hideLoading();
        }
    }

    _mountTable() {
        const host = this.$('[data-table-host]');
        if (!host) {
            return;
        }

        this._table?.destroy();
        this._table = new DataTable({
            columns: [
                { key: 'itemsSummary', title: 'Items' },
                { key: 'orderNumber', title: '訂單編號' },
                {
                    key: 'totalAmount',
                    title: '總金額',
                    render: (value) => `NT$ ${Number(value).toLocaleString()}`
                },
                { key: 'status', title: '狀態' },
                { key: 'shippingAddress', title: '收件地址' },
                { key: 'note', title: '備註' },
                {
                    key: 'createdAt',
                    title: '建立時間',
                    render: (value) => new Date(value).toLocaleString('zh-TW')
                }
            ],
            data: [],
            pageSize: 10,
            emptyText: '目前沒有訂單'
        });
        this._table.mount(host);
    }

    _renderFeedback() {
        const host = this.$('[data-feedback-host]');
        if (!host) {
            return;
        }

        host.innerHTML = this._error
            ? `<div class="login-error">${this.esc(this._error)}</div>`
            : '';
    }

    _refreshTable() {
        const rows = this._orders.map((order) => ({
            ...order,
            itemsSummary: Array.isArray(order.items) && order.items.length > 0
                ? order.items.map((item) => `${item.productName} x${item.quantity}`).join(', ')
                : '-'
        }));

        this._table?.setData(rows);
    }
}

export default OrderListPage;
