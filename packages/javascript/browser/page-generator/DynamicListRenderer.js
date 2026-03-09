/**
 * DynamicListRenderer
 * 動態列表渲染器 - 從頁面定義 JSON 組合 SearchForm + DataTable + Pagination
 *
 * 組合現有元件：SearchForm、DataTable、Pagination
 */

export class DynamicListRenderer {
    /**
     * @param {Object} options
     * @param {Object} options.definition - 頁面定義 JSON
     * @param {Function} options.onSearch - 搜尋回調 (filters, page, pageSize) => void
     * @param {Function} options.onAction - 操作回調 (action, row) => void
     *                                      action: 'view' | 'edit' | 'delete'
     * @param {number} options.pageSize - 每頁筆數（預設 20）
     */
    constructor(options = {}) {
        this.options = {
            definition: null,
            onSearch: null,
            onAction: null,
            pageSize: 20,
            ...options
        };

        this._searchForm = null;
        this._dataTable = null;
        this._pagination = null;
        this._currentPage = 1;
        this._total = 0;

        /** @type {Map<string, Object>} 模組快取 */
        this._modules = new Map();

        this.element = null;
    }

    /**
     * 初始化（載入依賴元件後建構）
     */
    async init() {
        await this._loadModules();
        this._build();
        return this;
    }

    async _loadModules() {
        const [searchFormMod, dataTableMod, paginationMod] = await Promise.all([
            import('../ui_components/form/SearchForm/SearchForm.js'),
            import('../ui_components/layout/DataTable/DataTable.js'),
            import('../ui_components/common/Pagination/Pagination.js')
        ]);
        this._modules.set('SearchForm', searchFormMod.SearchForm);
        this._modules.set('DataTable', dataTableMod.DataTable);
        this._modules.set('Pagination', paginationMod.Pagination);
    }

    _build() {
        const { definition } = this.options;
        if (!definition?.fields) return;

        this.element = document.createElement('div');
        this.element.className = 'dynamic-list';

        // 搜尋區
        const searchFields = this._buildSearchFields(definition.fields);
        if (searchFields.length > 0) {
            this._buildSearchForm(searchFields);
        }

        // DataTable
        this._buildDataTable(definition.fields);

        // 分頁
        this._buildPagination();
    }

    _buildSearchFields(fields) {
        return fields
            .filter(def => def.isSearchable)
            .map(def => {
                const field = {
                    key: def.fieldName,
                    label: def.label,
                };

                // 依 fieldType 映射 SearchForm 的欄位類型
                switch (def.fieldType) {
                    case 'number':
                        field.type = 'NUMBER';
                        break;
                    case 'date':
                        field.type = 'DATE';
                        break;
                    case 'select':
                    case 'multiselect':
                    case 'radio':
                        field.type = 'SELECT';
                        if (def.optionsSource?.type === 'static') {
                            field.options = def.optionsSource.items;
                        }
                        break;
                    case 'checkbox':
                    case 'toggle':
                        field.type = 'CHECKBOX';
                        break;
                    default:
                        field.type = 'TEXT';
                }

                return field;
            });
    }

    _buildSearchForm(searchFields) {
        const SearchForm = this._modules.get('SearchForm');
        if (!SearchForm) return;

        this._searchForm = new SearchForm({
            fields: searchFields,
            columns: Math.min(searchFields.length, 4),
            collapsible: searchFields.length > 4,
            visibleRows: 1,
            onSearch: (values) => {
                this._currentPage = 1;
                this._fireSearch(values);
            },
            onReset: () => {
                this._currentPage = 1;
                this._fireSearch({});
            }
        });

        const searchWrap = document.createElement('div');
        searchWrap.className = 'dynamic-list__search';
        searchWrap.style.cssText = 'margin-bottom:16px;';
        this._searchForm.mount(searchWrap);
        this.element.appendChild(searchWrap);
    }

    _buildDataTable(fields) {
        const DataTable = this._modules.get('DataTable');
        if (!DataTable) return;

        // listOrder > 0 的欄位作為 columns
        const columns = fields
            .filter(def => def.listOrder > 0)
            .sort((a, b) => a.listOrder - b.listOrder)
            .map(def => ({
                key: def.fieldName,
                title: def.label,
                sortable: true,
                render: (value) => this._formatCellValue(def, value)
            }));

        // 操作列
        if (this.options.onAction) {
            columns.push({
                key: '_actions',
                title: '操作',
                width: '140px',
                sortable: false,
                render: (_, row) => this._renderActions(row)
            });
        }

        this._dataTable = new DataTable({
            columns,
            data: [],
            pagination: false, // 用獨立的 Pagination
            striped: true,
            hoverable: true,
            emptyText: '無資料',
        });

        const tableWrap = document.createElement('div');
        tableWrap.className = 'dynamic-list__table';
        this._dataTable.mount(tableWrap);
        this.element.appendChild(tableWrap);
    }

    _buildPagination() {
        const Pagination = this._modules.get('Pagination');
        if (!Pagination) return;

        this._pagination = new Pagination({
            total: 0,
            page: 1,
            pageSize: this.options.pageSize,
            showTotal: true,
            showPageSize: true,
            onChange: (page, pageSize) => {
                this._currentPage = page;
                this.options.pageSize = pageSize;
                const filters = this._searchForm?.getValues?.() || {};
                this._fireSearch(filters);
            }
        });

        const pageWrap = document.createElement('div');
        pageWrap.className = 'dynamic-list__pagination';
        pageWrap.style.cssText = 'margin-top:16px;display:flex;justify-content:flex-end;';
        this._pagination.mount(pageWrap);
        this.element.appendChild(pageWrap);
    }

    _formatCellValue(def, value) {
        if (value === null || value === undefined || value === '') return '—';

        switch (def.fieldType) {
            case 'checkbox':
            case 'toggle': {
                const isTrue = value === true || value === 'true';
                const bgColor = isTrue ? 'var(--cl-success-light)' : 'var(--cl-bg-secondary)';
                const fgColor = isTrue ? 'var(--cl-success)' : 'var(--cl-grey)';
                const text = isTrue ? '是' : '否';
                return `<span style="padding:2px 6px;border-radius:3px;font-size:12px;background:${bgColor};color:${fgColor};">${text}</span>`;
            }
            case 'date':
                try {
                    const d = new Date(value);
                    if (!isNaN(d.getTime())) return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')}`;
                } catch { /* fallthrough */ }
                return String(value);
            case 'select':
            case 'radio':
                if (def.optionsSource?.type === 'static') {
                    const item = def.optionsSource.items.find(i => i.value === value);
                    if (item) return item.label;
                }
                return String(value);
            case 'color':
                return `<span style="display:inline-block;width:14px;height:14px;border-radius:3px;background:${value};border: 1px solid var(--cl-border);vertical-align:middle;"></span>`;
            default:
                return String(value);
        }
    }

    _renderActions(row) {
        const container = document.createElement('div');
        container.style.cssText = 'display:flex;gap:4px;';

        const actions = [
            { name: 'view', text: '檢視', color: 'var(--cl-primary)' },
            { name: 'edit', text: '編輯', color: 'var(--cl-warning)' },
            { name: 'delete', text: '刪除', color: 'var(--cl-danger)' }
        ];

        actions.forEach(({ name, text, color }) => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.textContent = text;
            btn.style.cssText = `
                padding:3px 8px;border:none;background:${color}11;color:${color};
                border-radius:4px;cursor:pointer;font-size:12px;transition:background 0.15s;
            `;
            btn.addEventListener('mouseenter', () => { btn.style.background = `${color}22`; });
            btn.addEventListener('mouseleave', () => { btn.style.background = `${color}11`; });
            btn.addEventListener('click', (e) => {
                e.stopPropagation();
                this.options.onAction(name, row);
            });
            container.appendChild(btn);
        });

        return container;
    }

    _fireSearch(filters) {
        if (this.options.onSearch) {
            this.options.onSearch(filters, this._currentPage, this.options.pageSize);
        }
    }

    // ─── 公開 API ───

    /**
     * 設定列表資料
     * @param {Array} rows - 資料陣列
     * @param {number} total - 總筆數
     */
    setData(rows, total) {
        this._total = total;
        if (this._dataTable?.setData) {
            this._dataTable.setData(rows);
        }
        if (this._pagination) {
            this._pagination.options.total = total;
            this._pagination.options.page = this._currentPage;
            // 觸發重新渲染分頁
            if (this._pagination.element?.parentNode) {
                const parent = this._pagination.element.parentNode;
                this._pagination.destroy();

                const Pagination = this._modules.get('Pagination');
                this._pagination = new Pagination({
                    total,
                    page: this._currentPage,
                    pageSize: this.options.pageSize,
                    showTotal: true,
                    showPageSize: true,
                    onChange: (page, pageSize) => {
                        this._currentPage = page;
                        this.options.pageSize = pageSize;
                        const filters = this._searchForm?.getValues?.() || {};
                        this._fireSearch(filters);
                    }
                });
                this._pagination.mount(parent);
            }
        }
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target && this.element) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._searchForm?.destroy?.();
        this._dataTable?.destroy?.();
        this._pagination?.destroy?.();
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default DynamicListRenderer;
