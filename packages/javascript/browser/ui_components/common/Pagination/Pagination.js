/**
 * Pagination - 分頁器元件
 *
 * 提供頁碼導航、每頁數量選擇、快速跳頁功能
 *
 * @author MAGI System
 * @version 1.0.0
 */

import { escapeHtml } from '../../utils/security.js';
import Locale from '../../i18n/index.js';

export class Pagination {
    /**
     * @param {Object} options
     * @param {number} options.total - 總資料筆數
     * @param {number} options.page - 當前頁碼 (1-based)
     * @param {number} options.pageSize - 每頁筆數
     * @param {number[]} options.pageSizeOptions - 每頁筆數選項
     * @param {number} options.visiblePages - 顯示頁碼數量
     * @param {boolean} options.showTotal - 顯示總筆數
     * @param {boolean} options.showPageSize - 顯示每頁筆數選擇
     * @param {boolean} options.showJumper - 顯示跳頁輸入
     * @param {Function} options.onChange - 頁碼變更回調 (page, pageSize)
     */
    constructor(options = {}) {
        this.options = {
            total: 0,
            page: 1,
            pageSize: 20,
            pageSizeOptions: [10, 20, 50, 100],
            visiblePages: 5,
            showTotal: true,
            showPageSize: true,
            showJumper: true,
            onChange: null,
            ...options
        };

        this.element = null;
        this._injectStyles();
        this._create();
    }

    _injectStyles() {
        if (document.getElementById('pagination-styles')) return;

        const style = document.createElement('style');
        style.id = 'pagination-styles';
        style.textContent = `
            .pagination {
                display: flex;
                align-items: center;
                gap: 16px;
                font-size: 14px;
                color: var(--cl-text);
                flex-wrap: wrap;
            }
            .pagination-total {
                color: var(--cl-text-secondary);
            }
            .pagination-total strong {
                color: var(--cl-primary);
                font-weight: 600;
            }
            .pagination-pages {
                display: flex;
                align-items: center;
                gap: 4px;
            }
            .pagination-btn {
                min-width: 32px;
                height: 32px;
                padding: 0 8px;
                border: 1px solid var(--cl-border);
                background: var(--cl-bg);
                border-radius: 4px;
                cursor: pointer;
                display: flex;
                align-items: center;
                justify-content: center;
                transition: all 0.2s;
                font-size: 14px;
                color: var(--cl-text);
            }
            .pagination-btn:hover:not(:disabled):not(.active) {
                border-color: var(--cl-primary);
                color: var(--cl-primary);
            }
            .pagination-btn:disabled {
                cursor: not-allowed;
                opacity: 0.5;
                background: var(--cl-bg-secondary);
            }
            .pagination-btn.active {
                background: var(--cl-primary);
                border-color: var(--cl-primary);
                color: var(--cl-bg);
            }
            .pagination-ellipsis {
                min-width: 32px;
                height: 32px;
                display: flex;
                align-items: center;
                justify-content: center;
                color: var(--cl-text-placeholder);
            }
            .pagination-size {
                display: flex;
                align-items: center;
                gap: 8px;
            }
            .pagination-size select {
                height: 32px;
                padding: 0 24px 0 8px;
                border: 1px solid var(--cl-border);
                border-radius: 4px;
                background: var(--cl-bg);
                cursor: pointer;
                font-size: 14px;
                appearance: none;
                background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='12' height='12' viewBox='0 0 12 12'%3E%3Cpath fill='%23666' d='M3 4.5L6 7.5L9 4.5'/%3E%3C/svg%3E");
                background-repeat: no-repeat;
                background-position: right 8px center;
            }
            .pagination-size select:focus {
                outline: none;
                border-color: var(--cl-primary);
            }
            .pagination-jumper {
                display: flex;
                align-items: center;
                gap: 8px;
            }
            .pagination-jumper input {
                width: 50px;
                height: 32px;
                padding: 0 8px;
                border: 1px solid var(--cl-border);
                border-radius: 4px;
                text-align: center;
                font-size: 14px;
            }
            .pagination-jumper input:focus {
                outline: none;
                border-color: var(--cl-primary);
            }
            .pagination-jumper button {
                height: 32px;
                padding: 0 12px;
                border: 1px solid var(--cl-primary);
                background: var(--cl-primary);
                color: var(--cl-bg);
                border-radius: 4px;
                cursor: pointer;
                font-size: 13px;
                transition: all 0.2s;
            }
            .pagination-jumper button:hover {
                background: var(--cl-primary-dark);
            }
        `;
        document.head.appendChild(style);
    }

    get totalPages() {
        return Math.ceil(this.options.total / this.options.pageSize) || 1;
    }

    _create() {
        const container = document.createElement('div');
        container.className = 'pagination';

        this.element = container;
        this._render();
    }

    _render() {
        const { total, page, pageSize, showTotal, showPageSize, showJumper, pageSizeOptions, visiblePages } = this.options;
        const totalPages = this.totalPages;

        this.element.innerHTML = '';

        // 總筆數
        if (showTotal) {
            const totalEl = document.createElement('div');
            totalEl.className = 'pagination-total';
            totalEl.innerHTML = `${Locale.t('pagination.totalPrefix', null) || ''}<strong>${total}</strong> ${Locale.t('pagination.totalSuffix', null) || ''}`;
            this.element.appendChild(totalEl);
        }

        // 頁碼區
        const pagesEl = document.createElement('div');
        pagesEl.className = 'pagination-pages';

        // 上一頁
        const prevBtn = this._createButton('‹', page > 1, () => this.goTo(page - 1));
        prevBtn.title = Locale.t('pagination.prev');
        pagesEl.appendChild(prevBtn);

        // 頁碼
        const pageNumbers = this._getPageNumbers(page, totalPages, visiblePages);
        pageNumbers.forEach((num, index) => {
            if (num === '...') {
                const ellipsis = document.createElement('span');
                ellipsis.className = 'pagination-ellipsis';
                ellipsis.textContent = '...';
                pagesEl.appendChild(ellipsis);
            } else {
                const btn = this._createButton(num, true, () => this.goTo(num));
                if (num === page) btn.classList.add('active');
                pagesEl.appendChild(btn);
            }
        });

        // 下一頁
        const nextBtn = this._createButton('›', page < totalPages, () => this.goTo(page + 1));
        nextBtn.title = Locale.t('pagination.next');
        pagesEl.appendChild(nextBtn);

        this.element.appendChild(pagesEl);

        // 每頁筆數
        if (showPageSize) {
            const sizeEl = document.createElement('div');
            sizeEl.className = 'pagination-size';

            const select = document.createElement('select');
            pageSizeOptions.forEach(size => {
                const option = document.createElement('option');
                option.value = size;
                option.textContent = `${size}${Locale.t('pagination.perPage')}`;
                if (size === pageSize) option.selected = true;
                select.appendChild(option);
            });
            select.addEventListener('change', (e) => {
                const newSize = parseInt(e.target.value, 10);
                this.setPageSize(newSize);
            });

            sizeEl.appendChild(select);
            this.element.appendChild(sizeEl);
        }

        // 跳頁
        if (showJumper && totalPages > 1) {
            const jumperEl = document.createElement('div');
            jumperEl.className = 'pagination-jumper';

            const label = document.createElement('span');
            label.textContent = Locale.t('pagination.goTo');
            jumperEl.appendChild(label);

            const input = document.createElement('input');
            input.type = 'number';
            input.min = 1;
            input.max = totalPages;
            input.value = page;
            jumperEl.appendChild(input);

            const label2 = document.createElement('span');
            label2.textContent = Locale.t('pagination.page');
            jumperEl.appendChild(label2);

            const btn = document.createElement('button');
            btn.type = 'button';
            btn.textContent = Locale.t('pagination.jump');
            btn.addEventListener('click', () => {
                const targetPage = parseInt(input.value, 10);
                if (targetPage >= 1 && targetPage <= totalPages) {
                    this.goTo(targetPage);
                }
            });
            jumperEl.appendChild(btn);

            // Enter 鍵跳轉
            input.addEventListener('keydown', (e) => {
                if (e.key === 'Enter') {
                    btn.click();
                }
            });

            this.element.appendChild(jumperEl);
        }
    }

    _createButton(text, enabled, onClick) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'pagination-btn';
        btn.textContent = text;
        btn.disabled = !enabled;
        if (enabled) {
            btn.addEventListener('click', onClick);
        }
        return btn;
    }

    _getPageNumbers(current, total, visible) {
        if (total <= visible) {
            return Array.from({ length: total }, (_, i) => i + 1);
        }

        const half = Math.floor(visible / 2);
        let start = Math.max(1, current - half);
        let end = Math.min(total, start + visible - 1);

        if (end - start < visible - 1) {
            start = Math.max(1, end - visible + 1);
        }

        const pages = [];

        if (start > 1) {
            pages.push(1);
            if (start > 2) pages.push('...');
        }

        for (let i = start; i <= end; i++) {
            pages.push(i);
        }

        if (end < total) {
            if (end < total - 1) pages.push('...');
            pages.push(total);
        }

        return pages;
    }

    goTo(page) {
        const totalPages = this.totalPages;
        const newPage = Math.max(1, Math.min(page, totalPages));

        if (newPage !== this.options.page) {
            this.options.page = newPage;
            this._render();

            if (this.options.onChange) {
                this.options.onChange(newPage, this.options.pageSize);
            }
        }

        return this;
    }

    setPageSize(pageSize) {
        if (pageSize !== this.options.pageSize) {
            this.options.pageSize = pageSize;
            // 重新計算當前頁，避免超出範圍
            const totalPages = this.totalPages;
            if (this.options.page > totalPages) {
                this.options.page = totalPages;
            }
            this._render();

            if (this.options.onChange) {
                this.options.onChange(this.options.page, pageSize);
            }
        }

        return this;
    }

    setTotal(total) {
        this.options.total = total;
        // 重新計算當前頁
        const totalPages = this.totalPages;
        if (this.options.page > totalPages) {
            this.options.page = Math.max(1, totalPages);
        }
        this._render();

        return this;
    }

    getState() {
        return {
            page: this.options.page,
            pageSize: this.options.pageSize,
            total: this.options.total,
            totalPages: this.totalPages
        };
    }

    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this.element?.remove();
        this.element = null;
    }
}

export default Pagination;
