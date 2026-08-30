/**
 * DynamicListRenderer
 * 動態列表渲染器 - 從頁面定義 JSON 組合 SearchForm + DataTable + Pagination
 *
 * 組合現有元件：SearchForm、DataTable、Pagination
 */

import { escapeHtml, raw } from '../ui_components/utils/security.js';
import { BasicButton } from '../ui_components/common/BasicButton/BasicButton.js';
import { Link } from '../ui_components/common/Link/Link.js';
import { Badge } from '../ui_components/common/Badge/Badge.js';
import { StatGrid } from '../ui_components/common/StatGrid/StatGrid.js';
import { DrawerPanel } from '../ui_components/layout/Panel/DrawerPanel.js';
import {
    buildActionRequest,
    buildDownloadRequest,
    buildQueryPayload,
    formatTitleTemplate,
    formatRocDateTime,
    getQueryColumns,
    getQuerySearchFields,
    getUiActions,
    isDeclarativeListDefinition,
    isQueryDefinition,
    normalizeQueryDefinition,
    normalizeTableTextLabels,
    resolveFieldOptions,
    resolveLookupItems,
    resolveLookupLabel,
    resolveLookupSource,
    resolveRouteTemplate,
} from './QueryDefinitionAdapter.js';

export class DynamicListRenderer {
    /**
     * @param {Object} options
     * @param {Object} options.definition - 頁面定義 JSON
     * @param {Function} options.onSearch - 搜尋回調 (filters, page, pageSize) => void
     * @param {Function} options.onAction - 操作回調 (action, row) => void
     *                                      action: 'view' | 'edit' | 'delete'
     * @param {Function} options.onDownload - 下載回調 (request) => void|Promise<void>
     * @param {number} options.pageSize - 每頁筆數（預設 20）
     */
    constructor(options = {}) {
        this.options = {
            definition: null,
            onSearch: null,
            onAction: null,
            onDownload: null,
            confirmDownload: null,
            pageSize: 20,
            ...options
        };

        this._searchForm = null;
        this._dataTable = null;
        this._pagination = null;
        this._currentPage = 1;
        this._total = 0;
        this._rows = [];
        this._definition = null;
        this._isQuery = false;
        this._isDeclarativeList = false;
        this._querySearchFields = [];
        this._queryColumns = [];
        this._tableColumns = [];
        this._searchPanelOpen = true;
        this._searchPanelBody = null;
        this._searchPanelToggle = null;
        this._searchPanelLabels = null;
        this._tableClickHandler = null;
        this._modalElement = null;
        this._modalComponents = [];
        this._drawerPanel = null;
        this._statGrid = null;
        this._summaryContainer = null;
        this._controlComponents = [];
        this._renderedActionComponents = [];

        /** @type {Map<string, Object>} 模組快取 */
        this._modules = new Map();

        /** @type {Map<Object, Object>} lookup 欄位的標籤索引快取（依欄位定義物件） */
        this._lookupLabelCache = new Map();

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
        const rawDefinition = this.options.definition;
        const definition = isDeclarativeListDefinition(rawDefinition)
            ? normalizeQueryDefinition(rawDefinition)
            : rawDefinition;
        if (!definition?.fields && !definition?.columns) return;

        this._definition = definition;
        this._isQuery = isQueryDefinition(definition);
        this._isDeclarativeList = isDeclarativeListDefinition(definition);
        this._querySearchFields = this._isQuery ? getQuerySearchFields(definition) : [];
        this._queryColumns = this._isDeclarativeList ? getQueryColumns(definition) : [];

        this.element = document.createElement('div');
        this.element.className = this._isQuery ? 'dynamic-list dynamic-list--query' : 'dynamic-list';

        this._buildPageHeader(definition);
        this._buildSummary(definition);

        // 搜尋區
        const searchFields = this._buildSearchFields(definition.fields || []);
        if (searchFields.length > 0) {
            this._buildSearchForm(searchFields, definition);
        }

        // DataTable
        this._buildDataTable(definition.fields || [], definition);

        // 分頁
        if (!this._isDeclarativeList) {
            this._buildPagination(definition);
        }
    }

    _buildPageHeader(definition) {
        const title = definition?.page?.heading || definition?.page?.title || definition?.description || '';
        if (!title) return;

        const header = document.createElement('div');
        header.className = 'dynamic-list__header';
        header.style.cssText = 'margin-bottom:16px;';

        const heading = document.createElement('h2');
        heading.className = 'dynamic-list__title';
        heading.style.cssText = 'margin:0;font-size:var(--cl-font-size-2xl);font-weight:600;color:var(--cl-text);';
        heading.textContent = title;

        header.appendChild(heading);
        this.element.appendChild(header);
    }

    _buildSummary(definition) {
        if (!definition?.summary || !Array.isArray(definition.summary.stats)) return;
        const container = document.createElement('section');
        container.className = 'dynamic-list__summary';
        container.setAttribute('aria-label', definition.summary.label || '摘要');
        container.style.cssText = 'margin-bottom:16px;';
        this._summaryContainer = container;
        this.element.appendChild(container);
        this.setSummary(definition.fixtures?.summary || {});
    }

    _summaryStats(data = {}) {
        return (this._definition?.summary?.stats || []).map(stat => {
            const value = readNestedValue(data, stat.key);
            return {
                ...stat,
                value: formatSummaryValue(
                    value === undefined || value === null || value === '' ? (stat.fallback ?? '—') : value,
                    stat,
                ),
            };
        });
    }

    setSummary(data = {}) {
        if (!this._summaryContainer || !this._definition?.summary) return;
        this._statGrid?.destroy?.();
        this._summaryContainer.replaceChildren();
        this._statGrid = new StatGrid({
            stats: this._summaryStats(data),
            columns: this._definition.summary.columns || 4,
        });
        this._statGrid.mount(this._summaryContainer);
    }

    _buildSearchFields(fields) {
        return fields
            .filter(def => def.isSearchable)
            .map(def => {
                const resolvedOptions = resolveFieldOptions(def, this._definition, {});
                const field = {
                    key: def.fieldName,
                    label: def.label,
                    placeholder: def.placeholder || '',
                    required: def.isRequired === true || def.required === true,
                    requiredMessage: this._definition?.behaviors?.requiredMessage,
                    options: resolvedOptions,
                    defaultValue: def.defaultValue ?? null,
                    source: def,
                };

                // 依 fieldType 映射 SearchForm 的欄位類型
                switch (def.fieldType) {
                    case 'number':
                        field.type = 'number';
                        break;
                    case 'date':
                        field.type = 'date';
                        break;
                    case 'rocDate':
                        field.type = 'date';
                        field.format = 'taiwan';
                        field.min = def.min || null;
                        field.max = def.max || this._resolveMaxDate(def);
                        field.placeholder = field.placeholder || this._definition?.behaviors?.searchForm?.datePlaceholder || '';
                        break;
                    case 'select':
                    case 'radio':
                        field.type = 'select';
                        field.options = resolvedOptions;
                        break;
                    case 'multiselect':
                        field.type = 'multiselect';
                        field.options = resolvedOptions;
                        break;
                    case 'checkbox':
                    case 'toggle':
                        field.type = 'checkbox';
                        break;
                    default:
                        field.type = 'text';
                }

                return field;
            });
    }

    _buildSearchForm(searchFields, definition) {
        const SearchForm = this._modules.get('SearchForm');
        if (!SearchForm) return;
        const searchFormText = definition?.behaviors?.searchForm || {};

        this._searchForm = new SearchForm({
            fields: searchFields,
            columns: Math.min(searchFields.length, 4),
            collapsible: this._isQuery ? false : searchFields.length > 4,
            visibleRows: 1,
            requiredMessage: definition?.behaviors?.requiredMessage,
            ...(searchFormText.searchText !== undefined ? { searchText: searchFormText.searchText } : {}),
            ...(searchFormText.resetText !== undefined ? { resetText: searchFormText.resetText } : {}),
            ...(searchFormText.requiredMark !== undefined ? { requiredMark: searchFormText.requiredMark } : {}),
            onChange: (_key, _value, values) => {
                if (this._isQuery) this._applyDependentSearchOptions(values);
            },
            onSearch: (values) => {
                this._currentPage = 1;
                this._handleSearch(values);
            },
            onReset: () => {
                this._currentPage = 1;
                if (!this._isQuery) this._fireSearch({});
            },
            onValidationError: () => {
                if (this._isQuery) this._setSearchPanelOpen(true);
            },
        });

        const searchWrap = document.createElement('div');
        searchWrap.className = 'dynamic-list__search';
        searchWrap.style.cssText = 'margin-bottom:16px;';
        const collapse = definition?.behaviors?.collapsibleSearch;
        if (this._isQuery && collapse) {
            this._mountCollapsibleSearch(searchWrap, collapse);
        } else {
            this._searchForm.mount(searchWrap);
        }
        if (this._isQuery) this._applyDependentSearchOptions(this._searchForm.getValues?.() || {});
        this.element.appendChild(searchWrap);
    }

    _mountCollapsibleSearch(searchWrap, collapse) {
        const panel = document.createElement('div');
        panel.className = 'dynamic-list__search-panel';

        const header = document.createElement('div');
        header.className = 'dynamic-list__search-panel-header';
        header.style.cssText = 'display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:8px;';

        const title = document.createElement('span');
        title.className = 'dynamic-list__search-panel-title';
        title.style.cssText = 'font-size:var(--cl-font-size-lg);font-weight:600;color:var(--cl-text);';
        title.textContent = collapse.panelTitle || '查詢條件';
        header.appendChild(title);

        const toggleComponent = new BasicButton({
            type: BasicButton.TYPES.CUSTOM,
            variant: 'plain',
            showIcon: false,
            customLabel: collapse.collapseLabel || collapse.toggleLabel || '查詢條件收合',
            onClick: () => this._setSearchPanelOpen(!this._searchPanelOpen),
        });
        this._controlComponents.push(toggleComponent);
        const toggle = toggleComponent.element;
        toggle.classList.add('dynamic-list__search-toggle');
        toggle.style.cssText = 'background:transparent;border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-sm);color:var(--cl-primary);cursor:pointer;padding:6px 10px;font-size:var(--cl-font-size-md);';
        toggleComponent.mount(header);

        const body = document.createElement('div');
        body.className = 'dynamic-list__search-panel-body';
        this._searchPanelOpen = collapse.initialOpen !== false;
        body.style.display = this._searchPanelOpen ? '' : 'none';
        this._searchPanelBody = body;
        this._searchPanelToggle = toggle;
        const collapseLabel = collapse.collapseLabel || collapse.toggleLabel || '查詢條件收合';
        const expandLabel = collapse.expandLabel
            || (collapseLabel.includes('收合') ? collapseLabel.replace('收合', '展開') : '查詢條件展開');
        this._searchPanelLabels = { collapse: collapseLabel, expand: expandLabel };
        this._syncSearchPanelToggle();

        this._searchForm.mount(body);
        panel.appendChild(header);
        panel.appendChild(body);
        searchWrap.appendChild(panel);
    }

    _applyDependentSearchOptions(values = {}) {
        if (!this._searchForm) return;
        for (const field of this._querySearchFields) {
            if (!field.dependsOn && !field.filter) continue;
            const component = this._searchForm._fieldComponents?.get(field.fieldName);
            if (!component?.setItems) continue;
            component.setItems(resolveFieldOptions(field, this._definition, values));
        }
    }

    _buildDataTable(fields, definition) {
        const DataTable = this._modules.get('DataTable');
        if (!DataTable) return;

        const table = definition?.table || {};
        const uiActions = getUiActions(definition);
        const toolbarActions = uiActions.filter((action) => action.placement === 'toolbar' && action.requiresSelection !== true);
        const selectionActions = uiActions.filter((action) => action.placement === 'toolbarSelect' || action.requiresSelection === true);
        const rowActions = uiActions.filter((action) => action.placement === 'row');
        const tableColumns = this._isDeclarativeList
            ? this._queryColumns
            : fields
                .filter(def => def.listOrder > 0)
                .sort((a, b) => a.listOrder - b.listOrder);
        this._tableColumns = tableColumns;

        const columns = tableColumns.map(def => ({
            key: def.fieldName,
            title: def.label,
            width: def.width || undefined,
            hidden: def.hidden === true,
            sortable: def.sortable === false ? false : true,
            render: (value, row) => this._formatCellValue(def, value, row)
        }));

        // 操作列
        if ((!this._isDeclarativeList && !this._isQuery && this.options.onAction) || rowActions.length > 0) {
            columns.push({
                key: '_actions',
                title: table.actionColumnTitle || '操作',
                width: table.actionColumnWidth || '140px',
                sortable: false,
                // DataTable escapes ordinary strings by design. Row actions are
                // renderer-owned, fully escaped markup, so opt in explicitly;
                // otherwise every row button is displayed as literal HTML text.
                render: (_, row) => raw(rowActions.length > 0
                    ? this._renderActionButtons(rowActions, row)
                    : this._renderActions(row)?.__html || '')
            });
        }

        const initialRows = this._getInitialRows(definition);
        this._rows = initialRows;
        this._total = initialRows.length;

        const dataTableOptions = {
            rowsPerPageOptions: table.rowsPerPageOptions,
            tableBodyHeight: table.tableBodyHeight,
            textLabels: normalizeTableTextLabels(table),
        };
        if (this._isDeclarativeList || selectionActions.length > 0) {
            dataTableOptions.selectableRows = table.selectableRows || (selectionActions.length > 0 ? 'multiple' : 'none');
        }
        const sortOrder = this._resolveSortOrder(tableColumns);
        if (sortOrder) dataTableOptions.sortOrder = sortOrder;
        if (toolbarActions.length > 0) {
            dataTableOptions.customToolbar = () => this._renderActionButtons(toolbarActions);
        }
        if (selectionActions.length > 0) {
            dataTableOptions.customToolbarSelect = () => this._renderActionButtons(selectionActions);
        }
        dataTableOptions.onRender = root => this._mountActionControls(root);

        const tableTitle = this._formatTableTitle(table, initialRows.length);

        this._dataTable = new DataTable({
            columns,
            data: initialRows,
            title: tableTitle,
            pagination: this._isDeclarativeList ? true : false, // 舊 list 用獨立 Pagination；query/adminList 對齊 DataTable labels/options
            pageSize: this.options.pageSize,
            striped: true,
            hoverable: true,
            emptyText: table.textLabels?.noMatch || '無資料',
            options: dataTableOptions,
        });

        const tableWrap = document.createElement('div');
        tableWrap.className = 'dynamic-list__table';
        if (uiActions.length > 0 || tableColumns.some((column) => column.action) || this.options.onAction) {
            this._tableClickHandler = (event) => {
                const actionButton = event.target.closest('[data-action-id]');
                if (actionButton) {
                    event.preventDefault();
                    this._handleUiAction(actionButton.dataset.actionId, actionButton);
                    return;
                }
                const legacyButton = event.target.closest('[data-legacy-action]');
                if (legacyButton && this.options.onAction) {
                    event.preventDefault();
                    this.options.onAction(legacyButton.dataset.legacyAction, this._rowFromButton(legacyButton));
                }
            };
            tableWrap.addEventListener('click', this._tableClickHandler);
        }
        this._dataTable.mount(tableWrap);
        this.element.appendChild(tableWrap);
    }

    _buildPagination(definition = this._definition) {
        const Pagination = this._modules.get('Pagination');
        if (!Pagination) return;
        const rowsPerPageOptions = definition?.table?.rowsPerPageOptions || [10, 20, 50, 100];

        this._pagination = new Pagination({
            total: 0,
            page: 1,
            pageSize: this.options.pageSize,
            pageSizeOptions: rowsPerPageOptions,
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

    _handleSearch(values) {
        if (!this._isQuery) {
            this._fireSearch(values);
            return;
        }

        const result = buildQueryPayload(this._querySearchFields, values, {
            requiredMessage: this._definition?.behaviors?.requiredMessage,
        });
        if (result.errors.length > 0) {
            this._setSearchPanelOpen(true);
            return;
        }
        this._fireSearch(result.payload);
    }

    _setSearchPanelOpen(open) {
        this._searchPanelOpen = !!open;
        if (this._searchPanelBody) {
            this._searchPanelBody.style.display = this._searchPanelOpen ? '' : 'none';
        }
        this._syncSearchPanelToggle();
    }

    _syncSearchPanelToggle() {
        if (!this._searchPanelToggle) return;
        const labels = this._searchPanelLabels || {};
        const label = this._searchPanelOpen
            ? (labels.collapse || '查詢條件收合')
            : (labels.expand || '查詢條件展開');
        const labelElement = this._searchPanelToggle.querySelector('.basic-btn__label');
        if (labelElement) labelElement.textContent = label;
        else this._searchPanelToggle.textContent = label;
        this._searchPanelToggle.setAttribute('title', label);
        this._searchPanelToggle.setAttribute('aria-label', label);
        this._searchPanelToggle.setAttribute('aria-expanded', String(this._searchPanelOpen));
    }

    _resolveMaxDate(def) {
        if (def.maxYearOffset === null || def.maxYearOffset === undefined) return null;
        const offset = Number(def.maxYearOffset || 0);
        const year = new Date().getFullYear() + offset;
        return new Date(year, 11, 31);
    }

    _getInitialRows(definition) {
        const sampleRow = definition?.fixtures?.sampleRow;
        if (!this._isDeclarativeList || !sampleRow) return [];
        return [sampleRow];
    }

    _resolveSortOrder(columns) {
        const explicit = columns.find((column) => column.sortOrder);
        if (!explicit) return null;
        return {
            name: explicit.fieldName,
            direction: explicit.sortOrder,
        };
    }

    _renderActionButtons(actions, row = null) {
        const hosts = actions.filter(action => !matchesCondition(action.hiddenWhen, row)).map((action) => {
            const id = action.id || action.apiAction || action.key;
            const label = action.label || id;
            return `<span class="dynamic-list__action-host" data-dynamic-action-host="${escapeHtml(id)}" data-action-label="${escapeHtml(label)}"></span>`;
        }).join('');
        return row
            ? `<span class="dynamic-list__row-actions">${hosts}</span>`
            : hosts;
    }

    _mountActionControls(root) {
        this._renderedActionComponents.splice(0).forEach(component => component.destroy?.());
        if (!root) return;

        // 同步渲染期間 definition 與表單值不會變動，整批 host 共用一次計算結果
        const uiActions = getUiActions(this._definition);
        const searchValues = this._searchForm?.getValues?.() || {};

        root.querySelectorAll('[data-dynamic-action-host]').forEach(host => {
            const actionId = host.dataset.dynamicActionHost || '';
            const action = this._findUiAction(actionId, uiActions);
            if (!action) return;
            const label = action.label || host.dataset.actionLabel || actionId;
            const row = this._rowFromButton(host);
            const isRowAction = Boolean(host.closest('tbody tr'));
            if ((action.type === 'link' || action.type === 'create') && action.route) {
                const href = toRuntimeRoute(resolveRouteTemplate(action.route, {
                    row,
                    columns: this._queryColumns,
                    searchValues,
                }));
                if (!isSafeRouteHref(href)) return;
                const link = new Link({
                    text: label,
                    href,
                    scope: action.target === '_blank' ? Link.SCOPES.EXTERNAL : Link.SCOPES.INTERNAL,
                });
                link.element.dataset.actionId = actionId;
                link.mount(host);
                link.element.classList.add(isRowAction ? 'dynamic-list__row-action' : 'dynamic-list__action');
                link.element.dataset.actionId = actionId;
                this._renderedActionComponents.push(link);
                return;
            }
            const button = new BasicButton({
                type: BasicButton.TYPES.CUSTOM,
                variant: isRowAction && action.icon ? 'icon' : 'plain',
                showIcon: action.appearance === 'legacy-icon' || (isRowAction && Boolean(action.icon)),
                icon: action.icon || action.type || actionId,
                customLabel: label,
                disabled: host.dataset.actionDisabled === 'true',
                onClick: event => {
                    event?.stopPropagation?.();
                    void this._handleUiAction(actionId, button.element);
                },
            });
            button.element.classList.add(action.type === 'export'
                ? 'dynamic-list__download'
                : (isRowAction ? 'dynamic-list__row-action' : 'dynamic-list__action'));
            if (isRowAction && action.icon) button.element.classList.add('dynamic-list__row-action--icon');
            if (action.appearance === 'legacy-icon') button.element.classList.add('dynamic-list__action--legacy-icon');
            button.element.dataset.actionId = actionId;
            button.element.title = label;
            button.mount(host);
            this._renderedActionComponents.push(button);
        });

        root.querySelectorAll('[data-legacy-action-host]').forEach(host => {
            const action = host.dataset.legacyActionHost || '';
            const label = host.dataset.actionLabel || action;
            const button = new BasicButton({
                type: BasicButton.TYPES.CUSTOM,
                variant: 'plain',
                showIcon: false,
                customLabel: label,
                onClick: event => {
                    event?.stopPropagation?.();
                    this.options.onAction?.(action, this._rowFromButton(button.element));
                },
            });
            button.element.classList.add('dynamic-list__row-action');
            button.element.dataset.legacyAction = action;
            button.mount(host);
            this._renderedActionComponents.push(button);
        });

        root.querySelectorAll('[data-field-link-host]').forEach(host => {
            const rowIndex = Number(host.closest('tr')?.dataset?.rowIndex);
            const columnIndex = Number(host.closest('td')?.dataset?.col);
            const row = Number.isInteger(rowIndex) ? this._rows[rowIndex] : null;
            const field = Number.isInteger(columnIndex) ? this._tableColumns[columnIndex] : null;
            if (!row || !field?.link) return;
            const href = toRuntimeRoute(resolveRouteTemplate(field.link.to || field.link.route || '', {
                row,
                columns: this._queryColumns,
                searchValues,
            }));
            if (!isSafeRouteHref(href)) return;
            const link = new Link({
                text: host.dataset.linkLabel || '',
                href,
                scope: field.link.target === '_blank' ? Link.SCOPES.EXTERNAL : Link.SCOPES.INTERNAL,
            });
            link.mount(host);
            this._renderedActionComponents.push(link);
        });
    }

    _formatTableTitle(table = {}, count = 0) {
        const titleText = this._isDeclarativeList ? formatTitleTemplate(table, count) : (table.title || '');
        return table.titleNote
            ? raw(`<span class="dynamic-list__table-title">${escapeHtml(titleText)}</span><span class="dynamic-list__table-note">${escapeHtml(table.titleNote)}</span>`)
            : titleText;
    }

    async _handleUiAction(actionId, button) {
        const action = this._findUiAction(actionId);
        if (!action) return null;
        const row = this._rowFromButton(button);

        if (action.type === 'drawer' || action.drawer) {
            const request = buildActionRequest(this._definition, action, {
                row,
                rows: this._rows,
                selectedIndices: this._dataTable?.getSelectedRows?.() || [],
                searchValues: this._searchForm?.getValues?.() || {},
            });
            const result = typeof this.options.onAction === 'function'
                ? await this.options.onAction(action.id || actionId, row, request)
                : request;
            if (result !== null && result !== undefined && result !== false) {
                return this._openDrawer(action, result);
            }
            return result;
        }

        if (action.type === 'modal' || action.modal) {
            return this._openModal(action, row);
        }

        if (action.type === 'export') {
            return this.downloadSelected({ actionId: action.id, row });
        }

        if ((action.type === 'link' || action.type === 'create') && action.route) {
            const href = toRuntimeRoute(resolveRouteTemplate(action.route, {
                row,
                columns: this._queryColumns,
                searchValues: this._searchForm?.getValues?.() || {},
            }));
            if (!isSafeRouteHref(href)) return null;
            if (action.target === '_blank') {
                window.open(href, '_blank', 'noopener,noreferrer');
            } else {
                window.location.hash = href.startsWith('#') ? href.slice(1) : href;
            }
            return { id: action.id || actionId, type: 'link', route: href };
        }

        const request = buildActionRequest(this._definition, action, {
            row,
            rows: this._rows,
            selectedIndices: this._dataTable?.getSelectedRows?.() || [],
            searchValues: this._searchForm?.getValues?.() || {},
        });
        if (typeof this.options.onAction === 'function') {
            return this.options.onAction(action.id || actionId, row, request);
        }
        return request;
    }

    _openDrawer(action, data) {
        this._closeDrawer();
        const config = action.drawer || {};
        const drawer = new DrawerPanel({
            title: config.title || action.label || '明細',
            position: config.placement || 'right',
            width: `${Number(config.width) || 720}px`,
            autoClose: true,
            closable: true,
        });
        const content = document.createElement('div');
        content.className = 'dynamic-list__drawer-content';
        content.style.cssText = 'display:grid;gap:14px;min-width:0;';
        for (const column of config.columns || []) {
            const item = document.createElement('section');
            item.className = 'dynamic-list__drawer-item';
            const title = document.createElement('h4');
            title.textContent = column.title || column.label || column.key || '';
            title.style.cssText = 'margin:0 0 6px;font-size:var(--cl-font-size-md);color:var(--cl-text-secondary);';
            const value = document.createElement(column.multiline === false ? 'div' : 'pre');
            value.textContent = formatDrawerValue(readNestedValue(data, column.key));
            value.style.cssText = column.multiline === false
                ? 'margin:0;color:var(--cl-text);overflow-wrap:anywhere;'
                : 'margin:0;padding:10px;white-space:pre-wrap;overflow-wrap:anywhere;background:var(--cl-bg-secondary);border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-sm);font:inherit;color:var(--cl-text);';
            item.append(title, value);
            content.appendChild(item);
        }
        drawer.setContent(content);
        drawer.mount();
        drawer.open();
        this._drawerPanel = drawer;
        return drawer;
    }

    _closeDrawer() {
        this._drawerPanel?.destroy?.();
        this._drawerPanel = null;
    }

    // actions 可由呼叫端注入：批次掛載時整批 host 共用一次 getUiActions() 結果
    _findUiAction(actionId, actions = getUiActions(this._definition)) {
        // A page may intentionally expose multiple UI actions backed by one
        // API contract (for example publish vs transfer with distinct
        // payload flags). Prefer the exact UI id so label/title/visibility do
        // not get borrowed from the first action sharing the apiAction id.
        const direct = actions.find((action) => action.id === actionId || action.key === actionId)
            || actions.find((action) => action.apiAction === actionId);
        if (direct) return direct;
        for (const column of this._tableColumns.length ? this._tableColumns : this._queryColumns) {
            const action = column.action;
            if (!action) continue;
            const id = action.id || action.apiAction || `${column.fieldName}-action`;
            if (id === actionId) {
                return {
                    ...action,
                    id,
                    placement: 'row',
                    label: action.label || column.label,
                };
            }
        }
        return null;
    }

    _rowFromButton(button) {
        const rowIndex = Number(button?.closest?.('tr')?.dataset?.rowIndex);
        if (!Number.isInteger(rowIndex) || rowIndex < 0) return null;
        return this._rows[rowIndex] || null;
    }

    _openModal(action, row = null) {
        const modalId = action.modal || action.modalId;
        const modal = (this._definition?.modals || []).find((item) => item.id === modalId);
        if (!modal) return null;
        this._closeModal();

        const overlay = document.createElement('div');
        overlay.className = 'dynamic-list__modal-overlay';
        overlay.style.cssText = 'position:fixed;inset:0;background:var(--cl-bg-overlay-medium);display:flex;align-items:center;justify-content:center;z-index:1000;';
        overlay.addEventListener('click', event => {
            if (event.target === overlay) this._closeModal();
        });

        const panel = document.createElement('section');
        panel.className = 'dynamic-list__modal';
        panel.style.cssText = 'width:min(520px,92vw);max-height:86vh;overflow:auto;background:var(--cl-bg);border:1px solid var(--cl-border);border-radius:10px;box-shadow:var(--cl-shadow-lg);padding:0;';

        const title = document.createElement('h3');
        title.style.cssText = 'margin:0;padding:20px;font-size:var(--cl-font-size-xl);font-weight:700;color:var(--cl-text-inverse);background:var(--cl-primary);text-align:left;display:flex;align-items:center;gap:8px;';
        if (action.appearance === 'legacy-icon' && action.icon) {
            const titleIcon = document.createElement('span');
            titleIcon.setAttribute('aria-hidden', 'true');
            titleIcon.textContent = legacyActionGlyph(action.icon);
            titleIcon.style.cssText = 'display:inline-block;margin-right:8px;';
            title.appendChild(titleIcon);
        }
        title.appendChild(document.createTextNode(modal.title || action.label || ''));
        const titleCloseComponent = new BasicButton({
            type: BasicButton.TYPES.CUSTOM,
            variant: 'plain',
            showIcon: false,
            customLabel: '×',
            ariaLabel: '關閉',
            onClick: () => this._closeModal(),
        });
        const titleClose = titleCloseComponent.element;
        titleClose.setAttribute('aria-label', '關閉');
        titleClose.style.cssText = 'margin-left:auto;border:0;background:transparent;color:var(--cl-text-inverse);font:inherit;line-height:1;cursor:pointer;';
        titleCloseComponent.mount(title);
        this._modalComponents.push(titleCloseComponent);
        panel.appendChild(title);

        const form = document.createElement('form');
        form.className = 'dynamic-list__modal-form';
        form.style.cssText = 'display:grid;gap:12px;min-height:420px;box-sizing:border-box;padding:24px 20px 16px;';
        const descriptions = Array.isArray(modal.description) ? modal.description : (modal.description ? [modal.description] : []);
        for (const description of descriptions) {
            const paragraph = document.createElement('p');
            paragraph.className = 'dynamic-list__modal-description';
            paragraph.textContent = description;
            paragraph.style.cssText = 'margin:0;color:var(--cl-text);font-size:var(--cl-font-size-md);';
            form.appendChild(paragraph);
        }
        const values = {};
        for (const field of modal.fields || []) {
            const fieldName = field.name || field.fieldName;
            if (!fieldName) continue;
            const fieldType = field.type || field.fieldType || 'text';
            const configuredInitial = field.defaultValue ?? field.default ?? '';
            const initial = row?.[fieldName] ?? (configuredInitial === '$todayRoc'
                ? formatRocDateTime(new Date()).split(' ')[0]
                : configuredInitial);
            values[fieldName] = initial;
            const wrap = document.createElement('label');
            wrap.style.cssText = fieldType === 'hidden' ? 'display:none;' : 'display:grid;gap:4px;font-size:var(--cl-font-size-md);color:var(--cl-text);';
            const label = document.createElement('span');
            label.textContent = `${field.label || fieldName}${field.required ? (modal.requiredMark || '*') : ''}`;
            const input = this._createModalFieldControl(field, fieldName, initial, values);
            wrap.appendChild(label);
            wrap.appendChild(input);
            form.appendChild(wrap);
        }

        const footer = document.createElement('div');
        footer.style.cssText = 'display:flex;justify-content:flex-end;align-self:end;gap:8px;margin-top:auto;padding-top:8px;';
        const submitComponent = new BasicButton({
            type: BasicButton.TYPES.SAVE,
            variant: 'primary',
            showIcon: false,
            customLabel: modal.submitText || '送出',
        });
        const submit = submitComponent.element;
        submit.type = 'submit';
        submit.style.cssText = 'padding:8px 16px;border:1px solid var(--cl-success);background:var(--cl-success);color:var(--cl-text-inverse);border-radius:var(--cl-radius-sm);cursor:pointer;';
        submitComponent.mount(footer);
        const cancelComponent = new BasicButton({
            type: BasicButton.TYPES.CANCEL,
            variant: 'danger',
            showIcon: false,
            customLabel: modal.cancelText || '取消',
            onClick: () => this._closeModal(),
        });
        const cancel = cancelComponent.element;
        cancel.style.cssText = 'padding:8px 16px;border:1px solid var(--cl-danger);background:var(--cl-danger);color:var(--cl-text-inverse);border-radius:var(--cl-radius-sm);cursor:pointer;';
        cancelComponent.mount(footer);
        this._modalComponents.push(submitComponent, cancelComponent);
        form.appendChild(footer);

        form.addEventListener('submit', (event) => {
            event.preventDefault();
            const submitAction = modal.submitAction || action.submitAction || action.apiAction;
            const request = submitAction
                ? buildActionRequest(this._definition, { ...action, apiAction: submitAction }, {
                    row,
                    rows: this._rows,
                    selectedIndices: this._dataTable?.getSelectedRows?.() || [],
                    searchValues: values,
                })
                : null;
            const result = this.options.onAction?.(action.id, row, request);
            Promise.resolve(result)
                .then(value => {
                    // Upload validation/cancel returns null. Keep the legacy
                    // import window open so the user can correct the file.
                    if (value !== null && value !== false) this._closeModal();
                })
                .catch(() => { /* failure message is rendered by the page runtime */ });
        });

        panel.appendChild(form);
        overlay.appendChild(panel);
        document.body.appendChild(overlay);
        this._modalElement = overlay;
        return overlay;
    }

    _createModalFieldControl(field, fieldName, initial, values) {
        const fieldType = field.type || field.fieldType || 'text';
        const options = resolveFieldOptions(field, this._definition);
        const requestedWidth = String(field.componentOptions?.width || '100%');
        const inputWidth = /^\d+(?:\.\d+)?(?:px|%)$/.test(requestedWidth) ? requestedWidth : '100%';
        const inputStyle = `width:${inputWidth};box-sizing:border-box;padding:8px 10px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);font:inherit;`;
        const setScalarValue = (value) => { values[fieldName] = value; };

        if (fieldType === 'select' || fieldType === 'multiselect') {
            const select = document.createElement('select');
            select.name = fieldName;
            select.multiple = fieldType === 'multiselect';
            select.required = field.required === true;
            select.disabled = field.readOnly === true || field.isReadonly === true;
            select.style.cssText = inputStyle;
            const initialValues = Array.isArray(initial) ? initial.map(String) : String(initial ?? '').split(',').filter(Boolean);
            if (!select.multiple && !field.required) {
                const empty = document.createElement('option');
                empty.value = '';
                empty.textContent = field.placeholder || '';
                select.appendChild(empty);
            }
            for (const option of options) {
                const item = document.createElement('option');
                item.value = String(option.value ?? '');
                item.textContent = option.label == null ? String(option.value ?? '') : String(option.label);
                item.selected = initialValues.includes(item.value);
                select.appendChild(item);
            }
            select.addEventListener('change', () => {
                values[fieldName] = select.multiple
                    ? Array.from(select.selectedOptions).map((option) => option.value)
                    : select.value;
            });
            values[fieldName] = select.multiple ? initialValues : (initialValues[0] || '');
            return select;
        }

        if (fieldType === 'radio') {
            const group = document.createElement('div');
            group.style.cssText = 'display:flex;flex-wrap:wrap;gap:8px 16px;';
            for (const option of options) {
                const optionLabel = document.createElement('label');
                optionLabel.style.cssText = 'display:inline-flex;align-items:center;gap:4px;';
                const radio = document.createElement('input');
                radio.type = 'radio';
                radio.name = fieldName;
                radio.value = String(option.value ?? '');
                radio.checked = String(initial ?? '') === radio.value;
                radio.required = field.required === true;
                radio.disabled = field.readOnly === true || field.isReadonly === true;
                radio.addEventListener('change', () => {
                    if (radio.checked) setScalarValue(radio.value);
                });
                const text = document.createElement('span');
                text.textContent = option.label == null ? String(option.value ?? '') : String(option.label);
                optionLabel.appendChild(radio);
                optionLabel.appendChild(text);
                group.appendChild(optionLabel);
            }
            return group;
        }

        if (fieldType === 'checkbox' || fieldType === 'toggle') {
            const checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.name = fieldName;
            checkbox.checked = initial === true || initial === 'true' || initial === 1 || initial === '1';
            checkbox.disabled = field.readOnly === true || field.isReadonly === true;
            checkbox.addEventListener('change', () => { values[fieldName] = checkbox.checked; });
            values[fieldName] = checkbox.checked;
            return checkbox;
        }

        const input = document.createElement(fieldType === 'textarea' ? 'textarea' : 'input');
        input.name = fieldName;
        input.value = initial == null ? '' : String(initial);
        input.placeholder = field.placeholder || '';
        input.readOnly = field.readOnly === true || field.isReadonly === true;
        input.required = field.required === true;
        if (field.accept && input.tagName !== 'TEXTAREA') input.accept = field.accept;
        if (input.tagName !== 'TEXTAREA') input.type = modalInputType(fieldType);
        if (input.tagName === 'TEXTAREA') input.rows = Number(field.componentOptions?.rows) || 3;
        input.style.cssText = inputStyle;
        input.addEventListener('input', () => {
            values[fieldName] = fieldType === 'file' ? (input.files?.[0] || null) : input.value;
        });
        return input;
    }

    _closeModal() {
        this._modalComponents.splice(0).forEach(component => component.destroy?.());
        this._modalElement?.remove();
        this._modalElement = null;
    }

    _renderDownloadButton() {
        const action = getUiActions(this._definition).find((item) => item.type === 'export');
        const label = action?.label || '匯出Excel';
        const id = action?.id || 'download';
        return `<span class="dynamic-list__action-host" data-dynamic-action-host="${escapeHtml(id)}" data-action-label="${escapeHtml(label)}"></span>`;
    }

    _formatCellValue(def, value, row) {
        const displayValue = this._resolveDisplayValue(def, value);
        if (def.action) {
            const action = {
                id: def.action.id || def.action.apiAction || `${def.fieldName}-action`,
                label: def.action.label || displayValue || def.label,
            };
            const disabled = matchesCondition(def.action.disabledWhen, row);
            return raw(`<span class="dynamic-list__action-host" data-dynamic-action-host="${escapeHtml(action.id)}" data-action-label="${escapeHtml(action.label)}" data-action-icon="${def.action.icon ? 'true' : 'false'}" data-action-disabled="${disabled ? 'true' : 'false'}"></span>`);
        }
        if (def.link) {
            const href = toRuntimeRoute(resolveRouteTemplate(def.link.to || def.link.route || '', {
                row,
                columns: this._queryColumns,
                searchValues: this._searchForm?.getValues?.() || {},
            }));
            if (isSafeRouteHref(href)) {
                return raw(`<span data-field-link-host="" data-link-label="${escapeHtml(displayValue)}"></span>`);
            }
        }
        if (def.format === 'badge') {
            const normalized = String(value ?? '').toLowerCase();
            const variant = normalized === 'failed' || normalized === '失敗備份'
                ? Badge.VARIANTS.DANGER
                : (normalized === 'processing' || normalized === '處理中'
                    ? Badge.VARIANTS.INFO
                    : (normalized === 'pending' || normalized === '待送'
                        ? Badge.VARIANTS.WARNING
                        : Badge.VARIANTS.DEFAULT));
            const badge = new Badge({ text: displayValue, variant });
            // DataTable deliberately stringifies ordinary return values.  A DOM
            // element would therefore appear as "[object HTMLSpanElement]".
            // Badge owns this markup and escapes its text through textContent,
            // so serialize the component output and mark only that controlled
            // markup as renderer-owned HTML.
            return raw(badge.element.outerHTML);
        }
        if (def.format === 'raw') {
            return value === null || value === undefined ? '' : String(value);
        }
        if (def.format === 'rocDate') {
            if (def.rocDateSource === 'westernDate' || def.dateSource === 'westernDate') {
                return formatRocDateTime(value);
            }
            return value === null || value === undefined ? '' : String(value);
        }
        if (def.format === 'datetime' || def.format === 'datetime-short') {
            return formatSummaryValue(value, { format: 'datetime-short' });
        }
        if (value === null || value === undefined || value === '') return displayValue;

        switch (def.fieldType) {
            case 'checkbox':
            case 'toggle': {
                // CSP-safe：createElement + style.cssText（嚴格 style-src 會剝除 HTML style 屬性）
                const isTrue = value === true || value === 'true';
                const bgColor = isTrue ? 'var(--cl-success-light)' : 'var(--cl-bg-secondary)';
                const fgColor = isTrue ? 'var(--cl-success)' : 'var(--cl-grey)';
                const badge = document.createElement('span');
                badge.style.cssText = `padding:2px 6px;border-radius:var(--cl-radius-xs);font-size:var(--cl-font-size-sm);background:${bgColor};color:${fgColor};`;
                badge.textContent = isTrue ? '是' : '否';
                return badge;
            }
            case 'date':
                try {
                    const d = new Date(value);
                    if (!isNaN(d.getTime())) return `${d.getFullYear()}/${String(d.getMonth() + 1).padStart(2, '0')}/${String(d.getDate()).padStart(2, '0')}`;
                } catch { /* fallthrough */ }
                return String(value);
            case 'rocDate':
                return String(value);
            case 'select':
            case 'radio':
                return displayValue;
            case 'color': {
                // CSP-safe：createElement + style.cssText（嚴格 style-src 會剝除 HTML style 屬性）
                const swatch = document.createElement('span');
                swatch.style.cssText = 'display:inline-block;width:14px;height:14px;border-radius:var(--cl-radius-xs);border:1px solid var(--cl-border);vertical-align:middle;';
                const color = String(value).trim();
                swatch.style.background = /^#[0-9a-f]{3,8}$/i.test(color) ? color : 'transparent';
                return raw(swatch.outerHTML);
            }
            default:
                return displayValue;
        }
    }

    _resolveDisplayValue(def, value) {
        if (def.lookup || def.optionsSource || (Array.isArray(def.options) && def.options.length > 0)) {
            const resolved = this._resolveLookupDisplay(def, value);
            return resolved === '' ? (def.fallback ?? def.lookup?.fallback ?? '') : resolved;
        }
        if (value === null || value === undefined || value === '') return def.fallback ?? '';
        return String(value);
    }

    _resolveLookupDisplay(def, value) {
        let index = this._lookupLabelCache.get(def);
        if (!index || isLookupIndexStale(index, def, this._definition)) {
            index = buildLookupLabelIndex(def, this._definition);
            this._lookupLabelCache.set(def, index);
        }
        // value !== value（NaN）走 === 語義的原路徑；Map 以 SameValueZero 比對會多命中 NaN
        if (!index.labels || value !== value) return resolveLookupLabel(def, value, this._definition);
        if (index.labels.has(value)) return index.labels.get(value);
        if (index.sparse && value === undefined) return resolveLookupLabel(def, value, this._definition);
        return index.fallback ?? (value == null ? '' : String(value));
    }

    _renderActions(row) {
        const actions = [
            { name: 'view', text: '檢視', color: 'var(--cl-primary)' },
            { name: 'edit', text: '編輯', color: 'var(--cl-warning)' },
            { name: 'delete', text: '刪除', color: 'var(--cl-danger)' }
        ];
        return raw(`<div class="dynamic-list__row-actions">${
            actions.map(({ name, text }) => `<span class="dynamic-list__action-host" data-legacy-action-host="${escapeHtml(name)}" data-action-label="${escapeHtml(text)}"></span>`).join('')
        }</div>`);
    }

    _fireSearch(filters) {
        if (this.options.onSearch) {
            this.options.onSearch(filters, this._currentPage, this.options.pageSize);
        }
    }

    buildSearchPayload(values = this._searchForm?.getValues?.() || {}, options = {}) {
        const result = buildQueryPayload(this._querySearchFields, values, {
            requiredMessage: this._definition?.behaviors?.requiredMessage,
        });
        if (result.errors.length > 0 && options.throwOnError !== false) {
            const error = new Error(result.errors.map((item) => item.message).join('\n'));
            error.validationErrors = result.errors;
            throw error;
        }
        return result;
    }

    buildDownloadRequest(options = {}) {
        if (!this._isDeclarativeList) return null;
        const selected = this._dataTable?.getSelectedRows?.() || [];
        return buildDownloadRequest(this._definition, this._rows, selected, options);
    }

    async downloadSelected(options = {}) {
        const request = this.buildDownloadRequest(options);
        if (!request) return null;
        if (request.action?.requiresSelection !== false
            && request.selectionKey && request.selectionValues.length === 0) return null;
        if (options.outputExtension && /^\.[A-Za-z0-9]+$/.test(options.outputExtension)) {
            request.fileName = String(request.fileName || 'TIM_download')
                .replace(/\.[^.]+$/, options.outputExtension);
        }

        const confirmed = options.skipConfirm === true
            ? true
            : await this._confirmDownload(request.confirmText);
        if (!confirmed) return null;

        if (typeof this.options.onDownload === 'function') {
            return this.options.onDownload(request);
        }
        return this._postDownload(request, options);
    }

    async _confirmDownload(message) {
        if (!message) return true;
        if (typeof this.options.confirmDownload === 'function') {
            return this.options.confirmDownload(message);
        }
        if (typeof window !== 'undefined' && typeof window.confirm === 'function') {
            return window.confirm(message);
        }
        return true;
    }

    async _postDownload(request, options = {}) {
        const fetchImpl = options.fetch || globalThis.fetch;
        if (typeof fetchImpl !== 'function') return request;

        const response = await fetchImpl(request.legacyPath, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(request.payload),
        });
        const blob = await response.blob();
        if (typeof document === 'undefined' || typeof URL === 'undefined' || typeof URL.createObjectURL !== 'function') {
            return { request, response, blob };
        }

        const url = URL.createObjectURL(blob);
        try {
            const link = document.createElement('a');
            link.href = url;
            link.download = request.fileName;
            link.click();
        } finally {
            URL.revokeObjectURL(url);
        }
        return { request, response, blob };
    }

    // ─── 公開 API ───

    /**
     * 設定列表資料
     * @param {Array} rows - 資料陣列
     * @param {number} total - 總筆數
     */
    setData(rows, total) {
        // 資料更新時重新推導 lookup 標籤，容忍外部在執行期就地修改欄定義的 options
        this._lookupLabelCache.clear();
        this._rows = Array.isArray(rows) ? rows.map(row => this._normalizeRow(row)) : [];
        this._total = total ?? this._rows.length;
        if (this._isDeclarativeList && this._dataTable) {
            this._dataTable.title = this._formatTableTitle(this._definition?.table || {}, this._total);
        }
        if (this._dataTable?.setData) {
            this._dataTable.setData(this._rows);
        }
        if (this._pagination) {
            this._pagination.options.total = this._total;
            this._pagination.options.page = this._currentPage;
            // 觸發重新渲染分頁
            if (this._pagination.element?.parentNode) {
                const parent = this._pagination.element.parentNode;
                this._pagination.destroy();

                const Pagination = this._modules.get('Pagination');
                this._pagination = new Pagination({
                    total: this._total,
                    page: this._currentPage,
                    pageSize: this.options.pageSize,
                    pageSizeOptions: this._definition?.table?.rowsPerPageOptions || [10, 20, 50, 100],
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

    setSelectedRowsByKey(key, values = []) {
        if (!key || !this._dataTable?.setSelectedRows) return;
        const wanted = new Set((values || []).map(value => String(value)));
        const indices = this._rows.reduce((result, row, index) => {
            if (wanted.has(String(readNestedValue(row, key) ?? ''))) result.push(index);
            return result;
        }, []);
        this._dataTable.setSelectedRows(indices);
    }

    _normalizeRow(row) {
        if (!row || Array.isArray(row) || typeof row !== 'object') return row;
        const normalized = { ...row };
        for (const column of this._queryColumns) {
            if (!column.sourceKey || normalized[column.fieldName] !== undefined) continue;
            normalized[column.fieldName] = normalized[column.sourceKey];
        }
        return normalized;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target && this.element) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._renderedActionComponents.splice(0).forEach(component => component.destroy?.());
        this._controlComponents.splice(0).forEach(component => component.destroy?.());
        this._searchForm?.destroy?.();
        this._dataTable?.destroy?.();
        this._pagination?.destroy?.();
        this._statGrid?.destroy?.();
        this._statGrid = null;
        this._summaryContainer = null;
        this._closeModal();
        this._closeDrawer();
        this._lookupLabelCache.clear();
        if (this._tableClickHandler) {
            this.element?.querySelector('.dynamic-list__table')?.removeEventListener('click', this._tableClickHandler);
            this._tableClickHandler = null;
        }
        if (this.element?.parentNode) {
            this.element.remove();
        }
        this._rows = [];
        this._searchPanelBody = null;
        this._searchPanelToggle = null;
        this._searchPanelLabels = null;
    }
}

function readNestedValue(source, path) {
    if (!path) return source;
    return String(path).split('.').reduce((value, key) => value == null ? undefined : value[key], source);
}

function formatDrawerValue(value) {
    if (value === undefined || value === null) return '';
    if (typeof value === 'string') return value;
    try { return JSON.stringify(value, null, 2); }
    catch { return String(value); }
}

function formatSummaryValue(value, stat = {}) {
    if (stat.format === 'boolean') {
        return value === true || String(value).toLowerCase() === 'true'
            ? (stat.trueLabel || '是')
            : (stat.falseLabel || '否');
    }
    if (stat.format === 'datetime-short' && typeof value === 'string') {
        const match = /^(\d{4}-\d{2}-\d{2})T(\d{2}:\d{2})/.exec(value);
        if (match) return `${match[1]} ${match[2]}`;
    }
    return value;
}

function modalInputType(fieldType) {
    if (fieldType === 'hidden') return 'hidden';
    if (fieldType === 'password') return 'password';
    if (fieldType === 'email') return 'email';
    if (fieldType === 'tel') return 'tel';
    if (fieldType === 'url') return 'url';
    if (fieldType === 'number' || fieldType === 'integer' || fieldType === 'decimal') return 'number';
    if (fieldType === 'date') return 'date';
    if (fieldType === 'dateTime' || fieldType === 'datetime') return 'datetime-local';
    if (fieldType === 'time') return 'time';
    if (fieldType === 'file' || fieldType === 'upload') return 'file';
    return 'text';
}

function isSafeRouteHref(href) {
    if (typeof href !== 'string' || href.length === 0) return false;
    // 相對路徑必須為單一前導斜線;擋掉 //host 與反斜線變體(/\、\/、\\),
    // 瀏覽器會把 \ 正規化為 /,只擋 // 會漏 /\evil.com 造成開放重定向。
    return (href.startsWith('/') && !/^[/\\]{2}/.test(href))
        || href.startsWith('#')
        || /^https?:\/\//i.test(href);
}

function toRuntimeRoute(href) {
    if (typeof href !== 'string') return '';
    if (href.startsWith('/') && !/^[/\\]{2}/.test(href)) return `#${href}`;
    return href;
}

/**
 * 把 lookup 欄（含 endpoint/fixture 來源）的選項展開成 Map。為與 resolveLookupLabel 的
 * find/!match 語義一致：非物件項目整欄退回原路徑（labels=null）、同一 value 只收第一筆、
 * 有 undefined key 時標記 sparse。shared 記下當時的 fixture 陣列供過期判斷。
 */
function buildLookupLabelIndex(def, definition) {
    const { items, shared, endpoint, valueField, labelField, fallback } = resolveLookupSource(def, definition);
    let labels = new Map();
    let sparse = false;
    for (const item of items) {
        if (item === null || item === undefined) continue;
        if (typeof item !== 'object') {
            labels = null;
            break;
        }
        const key = item[valueField];
        if (key === undefined) {
            sparse = true;
            continue;
        }
        if (labels.has(key)) continue;
        labels.set(key, item[labelField] ?? fallback ?? String(key));
    }
    return {
        labels, sparse, fallback, endpoint, shared, length: items.length,
        options: def?.options,
        optionsLength: Array.isArray(def?.options) ? def.options.length : -1,
    };
}

function isLookupIndexStale(index, def, definition) {
    // 內嵌／source.items 來源沒有 endpoint 可比對，改盯欄位自身 options 的 identity + 長度
    const options = def?.options;
    if (options !== index.options) return true;
    if ((Array.isArray(options) ? options.length : -1) !== index.optionsLength) return true;
    if (!index.endpoint) return false;
    const current = resolveLookupItems(definition, index.endpoint);
    return current !== index.shared || (current !== null && current.length !== index.length);
}

function matchesCondition(condition, row) {
    if (!condition || !row) return false;
    const field = condition.field || condition.key;
    if (!field) return false;
    if (Object.prototype.hasOwnProperty.call(condition, 'equals')) {
        return String(row[field] ?? '') === String(condition.equals ?? '');
    }
    if (Array.isArray(condition.in)) return condition.in.map(String).includes(String(row[field] ?? ''));
    return false;
}

function legacyActionIcon(icon) {
    const glyph = legacyActionGlyph(icon);
    return `<span class="dynamic-list__action-icon" aria-hidden="true">${escapeHtml(glyph)}</span>`;
}

function legacyActionGlyph(icon) {
    const glyphs = {
        add: '＋', download: '⇩', upload: '⇧', excel: '▦', word: 'W', trash: '▰',
        link: '↗', export: '⇩', import: '⇧', api: '●', magic: '✦',
    };
    return glyphs[String(icon || '').toLowerCase()] || '●';
}

export default DynamicListRenderer;
