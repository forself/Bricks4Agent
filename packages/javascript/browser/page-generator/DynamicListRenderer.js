/**
 * DynamicListRenderer
 * 動態列表渲染器 - 從頁面定義 JSON 組合 SearchForm + DataTable + Pagination
 *
 * 組合現有元件：SearchForm、DataTable、Pagination
 */

import { escapeHtml, raw } from '../ui_components/utils/security.js';
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
    resolveLookupLabel,
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
        this._searchPanelOpen = true;
        this._searchPanelBody = null;
        this._tableClickHandler = null;
        this._modalElement = null;

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

        // 搜尋區
        const searchFields = this._buildSearchFields(definition.fields || []);
        if (searchFields.length > 0) {
            this._buildSearchForm(searchFields, definition);
        }

        // DataTable
        this._buildDataTable(definition.fields || [], definition);

        // 分頁
        if (!this._isQuery) {
            this._buildPagination(definition);
        }
    }

    _buildPageHeader(definition) {
        const title = definition?.page?.title || definition?.description || '';
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

        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'dynamic-list__search-toggle';
        toggle.style.cssText = 'background:transparent;border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-sm);color:var(--cl-primary);cursor:pointer;padding:6px 10px;font-size:var(--cl-font-size-md);';
        toggle.textContent = collapse.toggleLabel || '查詢條件收合';
        header.appendChild(toggle);

        const body = document.createElement('div');
        body.className = 'dynamic-list__search-panel-body';
        this._searchPanelOpen = collapse.initialOpen !== false;
        body.style.display = this._searchPanelOpen ? '' : 'none';
        this._searchPanelBody = body;

        toggle.addEventListener('click', () => {
            this._setSearchPanelOpen(!this._searchPanelOpen);
        });

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

        const columns = tableColumns.map(def => ({
            key: def.fieldName,
            title: def.label,
            width: def.width || undefined,
            hidden: def.hidden === true,
            sortable: def.sortable === false ? false : true,
            render: (value, row) => this._formatCellValue(def, value, row)
        }));

        // 操作列
        if ((!this._isQuery && this.options.onAction) || rowActions.length > 0) {
            columns.push({
                key: '_actions',
                title: table.actionColumnTitle || '操作',
                width: '140px',
                sortable: false,
                render: (_, row) => rowActions.length > 0 ? this._renderActionButtons(rowActions) : this._renderActions(row)
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

        this._dataTable = new DataTable({
            columns,
            data: initialRows,
            title: this._isDeclarativeList ? formatTitleTemplate(table, initialRows.length) : (table.title || ''),
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

    _renderActionButtons(actions) {
        return actions.map((action) => {
            const id = action.id || action.apiAction || action.key;
            const label = action.label || id;
            const actionClass = action.type === 'export' ? 'dynamic-list__download' : 'dynamic-list__action';
            return `<button type="button" class="${actionClass}" data-action-id="${escapeHtml(id)}">${escapeHtml(label)}</button>`;
        }).join('');
    }

    async _handleUiAction(actionId, button) {
        const action = this._findUiAction(actionId);
        if (!action) return null;
        const row = this._rowFromButton(button);

        if (action.type === 'export') {
            return this.downloadSelected({ actionId: action.id });
        }

        if (action.type === 'modal' || action.modal) {
            return this._openModal(action, row);
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

    _findUiAction(actionId) {
        const actions = getUiActions(this._definition);
        const direct = actions.find((action) => action.id === actionId || action.apiAction === actionId || action.key === actionId);
        if (direct) return direct;
        for (const column of this._queryColumns) {
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
        overlay.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.35);display:flex;align-items:center;justify-content:center;z-index:1000;';

        const panel = document.createElement('section');
        panel.className = 'dynamic-list__modal';
        panel.style.cssText = 'width:min(560px,92vw);max-height:86vh;overflow:auto;background:var(--cl-bg);border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);box-shadow:var(--cl-shadow-lg);padding:16px;';

        const title = document.createElement('h3');
        title.textContent = modal.title || action.label || '';
        title.style.cssText = 'margin:0 0 16px;font-size:var(--cl-font-size-xl);color:var(--cl-text);';
        panel.appendChild(title);

        const form = document.createElement('form');
        form.className = 'dynamic-list__modal-form';
        form.style.cssText = 'display:grid;gap:12px;';
        const values = {};
        for (const field of modal.fields || []) {
            const fieldName = field.name || field.fieldName;
            if (!fieldName) continue;
            const fieldType = field.type || field.fieldType || 'text';
            const initial = row?.[fieldName] ?? field.defaultValue ?? field.default ?? '';
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
        footer.style.cssText = 'display:flex;justify-content:flex-end;gap:8px;margin-top:8px;';
        const submit = document.createElement('button');
        submit.type = 'submit';
        submit.textContent = modal.submitText || '送出';
        submit.style.cssText = 'padding:8px 16px;border:1px solid var(--cl-primary);background:var(--cl-primary);color:var(--cl-text-inverse);border-radius:var(--cl-radius-sm);cursor:pointer;';
        const cancel = document.createElement('button');
        cancel.type = 'button';
        cancel.textContent = modal.cancelText || '取消';
        cancel.style.cssText = 'padding:8px 16px;border:1px solid var(--cl-border);background:var(--cl-bg);color:var(--cl-text);border-radius:var(--cl-radius-sm);cursor:pointer;';
        cancel.addEventListener('click', () => this._closeModal());
        footer.appendChild(submit);
        footer.appendChild(cancel);
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
            this.options.onAction?.(action.id, row, request);
            this._closeModal();
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
        const inputStyle = 'width:100%;box-sizing:border-box;padding:8px 10px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);font:inherit;';
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
        if (input.tagName !== 'TEXTAREA') input.type = modalInputType(fieldType);
        input.style.cssText = inputStyle;
        input.addEventListener('input', () => { values[fieldName] = input.value; });
        return input;
    }

    _closeModal() {
        this._modalElement?.remove();
        this._modalElement = null;
    }

    _renderDownloadButton() {
        const action = getUiActions(this._definition).find((item) => item.type === 'export');
        const label = action?.label || '匯出Excel';
        const id = action?.id || 'download';
        return `<button type="button" class="dynamic-list__download" data-action-id="${escapeHtml(id)}">${escapeHtml(label)}</button>`;
    }

    _formatCellValue(def, value, row) {
        const displayValue = this._resolveDisplayValue(def, value);
        if (def.action) {
            const action = {
                id: def.action.id || def.action.apiAction || `${def.fieldName}-action`,
                label: def.action.label || displayValue || def.label,
            };
            return raw(`<button type="button" class="dynamic-list__row-action" data-action-id="${escapeHtml(action.id)}">${escapeHtml(action.label)}</button>`);
        }
        if (def.link) {
            const href = resolveRouteTemplate(def.link.to || def.link.route || '', {
                row,
                columns: this._queryColumns,
                searchValues: this._searchForm?.getValues?.() || {},
            });
            if (isSafeRouteHref(href)) {
                const target = def.link.target ? ` target="${escapeHtml(def.link.target)}"` : '';
                const rel = def.link.target === '_blank' ? ' rel="noopener noreferrer"' : '';
                return raw(`<a href="${escapeHtml(href)}"${target}${rel}>${escapeHtml(displayValue)}</a>`);
            }
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
        if (value === null || value === undefined || value === '') return '—';

        switch (def.fieldType) {
            case 'checkbox':
            case 'toggle': {
                // CSP-safe：createElement + style.cssText（嚴格 style-src 會剝除 HTML style 屬性）
                const isTrue = value === true || value === 'true';
                const bgColor = isTrue ? 'var(--cl-success-light)' : 'var(--cl-bg-secondary)';
                const fgColor = isTrue ? 'var(--cl-success)' : 'var(--cl-grey)';
                const badge = document.createElement('span');
                badge.style.cssText = `padding:2px 6px;border-radius:3px;font-size:12px;background:${bgColor};color:${fgColor};`;
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
                swatch.style.cssText = 'display:inline-block;width:14px;height:14px;border-radius:3px;border:1px solid var(--cl-border);vertical-align:middle;';
                swatch.style.background = String(value);
                return swatch;
            }
            default:
                return displayValue;
        }
    }

    _resolveDisplayValue(def, value) {
        if (def.lookup || def.optionsSource || (Array.isArray(def.options) && def.options.length > 0)) {
            return resolveLookupLabel(def, value, this._definition);
        }
        if (value === null || value === undefined) return '';
        return String(value);
    }

    _renderActions(row) {
        const actions = [
            { name: 'view', text: '檢視', color: 'var(--cl-primary)' },
            { name: 'edit', text: '編輯', color: 'var(--cl-warning)' },
            { name: 'delete', text: '刪除', color: 'var(--cl-danger)' }
        ];
        return raw(`<div class="dynamic-list__row-actions">${
            actions.map(({ name, text }) => `<button type="button" class="dynamic-list__row-action" data-legacy-action="${escapeHtml(name)}">${escapeHtml(text)}</button>`).join('')
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
        if (request.selectionKey && request.selectionValues.length === 0) return null;

        const confirmed = await this._confirmDownload(request.confirmText);
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
        this._rows = Array.isArray(rows) ? rows : [];
        this._total = total ?? this._rows.length;
        if (this._isDeclarativeList && this._dataTable) {
            this._dataTable.title = formatTitleTemplate(this._definition?.table || {}, this._total);
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

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target && this.element) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._searchForm?.destroy?.();
        this._dataTable?.destroy?.();
        this._pagination?.destroy?.();
        this._closeModal();
        if (this._tableClickHandler) {
            this.element?.querySelector('.dynamic-list__table')?.removeEventListener('click', this._tableClickHandler);
            this._tableClickHandler = null;
        }
        if (this.element?.parentNode) {
            this.element.remove();
        }
        this._rows = [];
        this._searchPanelBody = null;
    }
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
    return href.startsWith('/') || href.startsWith('#') || /^https?:\/\//i.test(href);
}

export default DynamicListRenderer;
