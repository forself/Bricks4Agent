/**
 * DataTable - 通用資料表格元件
 *
 * 支援排序、分頁、行選擇（checkbox）、自訂工具列、自訂渲染、主題
 *
 * 支援三種 columns 格式：
 *   1. 標準格式：[{name, label, options: {customBodyRender, display, setCellProps}}]
 *   2. Audit 格式：[{key, title, render, hidden, html, width, sortable}]（data 為物件陣列）
 *   3. Search 格式：[{title, visible, width}]（data 為 2D 陣列）
 *
 * 使用方式：
 *   // 方式 1：單參數物件
 *   const dt = new DataTable({
 *       container: document.querySelector('.table-area'),
 *       title: '搜尋結果',
 *       data: [[1, '名稱', '值'], ...],
 *       columns: [{ name: 'ID' }, { name: '名稱', options: { customBodyRender: (val) => `<a>${val}</a>` } }],
 *       options: { selectableRows: 'multiple' }
 *   });
 *
 *   // 方式 2：兩參數（container, config）
 *   const dt = new DataTable(container, { columns, data, pageSize: 20 });
 *
 *   // 方式 3：不帶 container，稍後 mount
 *   const dt = new DataTable({ columns, data });
 *   dt.mount(document.querySelector('.table-area'));
 *
 *   dt.setData(newData);
 *
 * @module DataTable
 */

import { escapeHtml, isRawHtml, raw } from '../../utils/security.js';

import Locale from '../../i18n/index.js';
// 主題定義（使用 CSS 變數，支援深色主題）
const THEMES = {
    default: {
        headerBg: 'var(--cl-light-green)', headerFontSize: '1.10rem',
        cellFontSize: '1.00rem', oddRowBg: 'var(--cl-bg-secondary)', evenRowBg: 'var(--cl-bg-tertiary)',
        hoverBg: 'var(--cl-bg-hover)', selectedBg: 'var(--cl-bg-active)', rowHeight: 'auto',
        headerHeight: 'auto', wordBreak: 'normal',
    },
    search: {
        headerBg: 'var(--cl-light-green)', headerFontSize: '.90rem',
        cellFontSize: 'inherit', oddRowBg: 'var(--cl-bg-secondary)', evenRowBg: 'var(--cl-bg-tertiary)',
        hoverBg: 'var(--cl-bg-hover)', selectedBg: 'var(--cl-bg-active)', rowHeight: '40px',
        headerHeight: '50px', wordBreak: 'break-word',
    },
};

const DEFAULT_TEXT_LABELS = {
    pagination: { rowsPerPage: Locale.t('dataTable.rowsPerPage'), displayRows: Locale.t('dataTable.displayRows') },
    body: { noMatch: Locale.t('dataTable.noMatch') },
    selectedRows: { text: Locale.t('dataTable.selectedUnit') },
};

export class DataTable {
    /**
     * @param {Object|HTMLElement} configOrContainer - 設定物件，或容器元素（兩參數模式）
     * @param {Object} [legacyConfig] - 兩參數模式的設定物件
     */
    constructor(configOrContainer = {}, legacyConfig) {
        let config;

        // 偵測兩參數呼叫：第一個參數是 HTMLElement
        if (configOrContainer instanceof HTMLElement || (configOrContainer && configOrContainer.nodeType === 1)) {
            config = { container: configOrContainer, ...(legacyConfig || {}) };
        } else {
            config = configOrContainer;
        }

        // 偵測並轉換簡化 columns 格式
        const { columns: rawCols, data: rawData } = this._normalizeColumnsAndData(config.columns || [], config.data || []);

        this.container = config.container || null;
        this.title = config.title || '';
        this.data = rawData;
        this.columns = rawCols;
        this.options = config.options || {};
        this.variant = config.variant || 'default';

        // 相容兩種呼叫方式：從 top-level config 合併到 options
        for (const key of ['selectableRows', 'customToolbar', 'customToolbarSelect', 'rowsPerPageOptions', 'sortOrder']) {
            if (config[key] !== undefined && this.options[key] === undefined) {
                this.options[key] = config[key];
            }
        }

        // 相容 pageSize / pageSizeOptions
        if (config.pageSize || config.pageSizeOptions) {
            if (!this.options.rowsPerPageOptions && config.pageSizeOptions) {
                this.options.rowsPerPageOptions = config.pageSizeOptions;
            }
        }

        // 支援 pagination: false 停用分頁
        if (config.pagination === false) {
            this._paginationEnabled = false;
        } else {
            this._paginationEnabled = true;
        }

        // 支援 emptyText
        if (config.emptyText) {
            if (!this.options.textLabels) this.options.textLabels = {};
            if (!this.options.textLabels.body) this.options.textLabels.body = {};
            this.options.textLabels.body.noMatch = config.emptyText;
        }

        // 支援 striped / hoverable（CL 版介面相容）
        if (config.striped !== undefined) this._striped = config.striped;
        if (config.hoverable !== undefined) this._hoverable = config.hoverable;

        this._theme = THEMES[this.variant] || THEMES.default;
        this._page = 0;
        const defaultPageSize = config.pageSize || (this.options.rowsPerPageOptions || [10, 20, 100, 500, 1000])[0] || 10;
        this._rowsPerPage = defaultPageSize;
        this._sortCol = null;
        this._sortDir = null;
        this._selectedRows = [];
        this._hoveredRow = null;

        // 合併 textLabels
        const tl = this.options.textLabels || {};
        this._textLabels = {
            pagination: { ...DEFAULT_TEXT_LABELS.pagination, ...(tl.pagination) },
            body: { ...DEFAULT_TEXT_LABELS.body, ...(tl.body) },
            selectedRows: { ...DEFAULT_TEXT_LABELS.selectedRows, ...(tl.selectedRows) },
        };

        // 初始排序
        if (this.options.sortOrder) {
            const colIdx = this.columns.findIndex(c => c.name === this.options.sortOrder.name);
            if (colIdx >= 0) {
                this._sortCol = colIdx;
                this._sortDir = this.options.sortOrder.direction || 'asc';
            }
        }

        // 建立 element（不帶 container 時也能使用）
        this.element = document.createElement('div');

        if (this.container) {
            this.render();
        } else if (this.data.length > 0 || this.columns.length > 0) {
            // 無 container 但有資料：渲染到 element
            this._renderToElement();
        }
    }

    /**
     * 正規化 columns 和 data 格式
     */
    _normalizeColumnsAndData(columns, data) {
        if (!columns || columns.length === 0) return { columns, data };

        const first = columns[0];

        // 已是標準格式（有 name 屬性）→ 不需要轉換
        if (first.name !== undefined) return { columns, data };

        // Audit 格式：有 key 屬性 → 物件陣列 data 需轉為 2D 陣列
        if (first.key !== undefined) {
            const keys = columns.map(c => c.key);
            const newCols = columns.map(col => {
                const opts = {};
                if (col.hidden) opts.display = false;
                if (col.width) opts.setCellProps = () => ({ style: { width: col.width } });
                if (col.sortable === false) opts.sort = false;
                if (col.render) {
                    const renderFn = col.render;
                    const colKeys = keys;
                    opts.customBodyRender = (value, tableMeta) => {
                        const rowObj = {};
                        colKeys.forEach((k, i) => { rowObj[k] = tableMeta.rowData[i]; });
                        return renderFn(value, rowObj);
                    };
                }
                if (col.html) {
                    if (!opts.customBodyRender) {
                        opts.customBodyRender = (value) => raw(value == null ? '' : String(value));
                    }
                }
                return {
                    name: col.key,
                    label: col.title || col.key,
                    options: Object.keys(opts).length > 0 ? opts : undefined
                };
            });
            const newData = Array.isArray(data) ? data.map(row => {
                if (Array.isArray(row)) return row;
                return keys.map(k => row[k] !== undefined ? row[k] : '');
            }) : [];
            return { columns: newCols, data: newData };
        }

        // Search 格式：有 title 但沒有 key 和 name
        if (first.title !== undefined) {
            const newCols = columns.map((col, i) => {
                const opts = {};
                if (col.visible === false) opts.display = false;
                if (col.hidden) opts.display = false;
                if (col.width) opts.setCellProps = () => ({ style: { width: col.width } });
                if (col.render) {
                    opts.customBodyRender = col.render;
                } else {
                    opts.customBodyRender = (value) => value == null ? '' : String(value);
                }
                return {
                    name: `col_${i}`,
                    label: col.title || '',
                    options: opts
                };
            });
            return { columns: newCols, data };
        }

        return { columns, data };
    }

    /**
     * 更新資料並重新渲染
     */
    setData(data) {
        if (data && data.length > 0 && !Array.isArray(data[0]) && typeof data[0] === 'object') {
            const keys = this.columns.map(c => c.name);
            this.data = data.map(row => keys.map(k => row[k] !== undefined ? row[k] : ''));
        } else {
            this.data = data || [];
        }
        this._page = 0;
        this._selectedRows = [];
        if (this.container) {
            this.render();
        } else {
            this._renderToElement();
        }
        return this;
    }

    /**
     * 掛載到容器（CL mount 慣例）
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) {
            this.container = target;
            this.render();
        }
        return this;
    }

    /**
     * 銷毀表格
     */
    destroy() {
        if (this.container) {
            this.container.innerHTML = '';
        }
        if (this.element) {
            this.element.innerHTML = '';
        }
    }

    getSelectedRows() {
        return [...this._selectedRows];
    }

    setSelectedRows(indices) {
        this._selectedRows = indices || [];
        this.render();
    }

    getData() {
        return this.data;
    }

    /**
     * 渲染到 container
     */
    render() {
        if (!this.container) return;
        this.container.innerHTML = '';
        this._renderToElement();
        this.container.appendChild(this.element);
    }

    /**
     * 渲染到 this.element
     */
    _renderToElement() {
        const sorted = this._getSortedData();
        const paginated = this._paginationEnabled ? this._getPaginatedData(sorted) : sorted;
        const visibleCols = this._getVisibleColumns();
        const isSelectable = this.options.selectableRows !== 'none' && this.options.selectableRows !== false;
        const isAnySelected = isSelectable && this._selectedRows.length > 0;

        let html = '<div style="box-shadow:none;background:var(--cl-bg);">';

        // 工具列
        html += this._renderToolbar(isAnySelected);

        // 表格
        const bodyHeight = this.options.tableBodyHeight;
        html += `<div style="overflow-x:auto;${bodyHeight ? `max-height:${bodyHeight};overflow-y:auto;` : ''}">`;
        html += '<table style="width:100%;border-collapse:separate;border-spacing:0;table-layout:auto;">';

        // 表頭
        html += '<thead><tr>';
        if (isSelectable) {
            const allSelected = sorted.length > 0 && sorted.every(d => this._selectedRows.includes(d.dataIndex));
            html += `<th style="${this._headerStyle()};width:48px;padding:4px;">`;
            if (this.options.selectableRows !== 'single') {
                html += `<input type="checkbox" data-action="select-all" ${allSelected ? 'checked' : ''} style="cursor:pointer;">`;
            }
            html += '</th>';
        }
        visibleCols.forEach(colIdx => {
            const col = this.columns[colIdx];
            const isSorted = this._sortCol === colIdx;
            const sortIcon = isSorted ? (this._sortDir === 'asc' ? ' ▲' : ' ▼') : '';
            const sortDisabled = col.options?.sort === false;
            const widthStyle = this._getCellWidthStyle(col);
            html += `<th style="${this._headerStyle()};${sortDisabled ? '' : 'cursor:pointer;'}${widthStyle}" data-action="sort" data-col="${colIdx}">`;
            html += `<div style="display:flex;align-items:center;justify-content:center;">${escapeHtml(col.label || col.name || '')}${sortIcon}</div>`;
            html += '</th>';
        });
        html += '</tr></thead>';

        // 表身
        html += '<tbody>';
        if (paginated.length === 0) {
            const colspan = visibleCols.length + (isSelectable ? 1 : 0);
            html += `<tr><td colspan="${colspan}" style="${this._cellStyle()};padding:24px;color:var(--cl-text-secondary);">${escapeHtml(this._textLabels.body.noMatch)}</td></tr>`;
        } else {
            paginated.forEach(({ row, dataIndex }, viewIndex) => {
                const isEven = viewIndex % 2 === 1;
                const isSelected = this._selectedRows.includes(dataIndex);
                const bgColor = isSelected ? this._theme.selectedBg : (isEven ? this._theme.evenRowBg : this._theme.oddRowBg);

                html += `<tr style="background-color:${bgColor};height:${this._theme.rowHeight};transition:background-color 0.15s;" data-row-index="${dataIndex}">`;

                if (isSelectable) {
                    html += `<td style="${this._cellStyle()};width:48px;padding:4px;">`;
                    html += `<input type="checkbox" data-action="select-row" data-index="${dataIndex}" ${isSelected ? 'checked' : ''} style="cursor:pointer;">`;
                    html += '</td>';
                }

                visibleCols.forEach(colIdx => {
                    const col = this.columns[colIdx];
                    const widthStyle = this._getCellWidthStyle(col);
                    const cellContent = this._renderCell(row, colIdx, dataIndex, viewIndex);
                    html += `<td style="${this._cellStyle()};${widthStyle}">${cellContent}</td>`;
                });

                html += '</tr>';
            });
        }
        html += '</tbody></table></div>';

        // 分頁
        if (this._paginationEnabled && sorted.length > 0) {
            html += this._renderPagination(sorted.length);
        }

        html += '</div>';

        this.element.innerHTML = html;
        this._bindEvents(this.element);
    }

    // ── 內部方法 ──

    _getSortedData() {
        const indexed = this.data.map((row, i) => ({ row, dataIndex: i }));
        if (this._sortCol === null || !this._sortDir) return indexed;

        const colIdx = this._sortCol;
        const dir = this._sortDir === 'asc' ? 1 : -1;
        return [...indexed].sort((a, b) => {
            const aVal = this._getCellSortValue(a.row[colIdx]);
            const bVal = this._getCellSortValue(b.row[colIdx]);
            if (typeof aVal === 'number' && typeof bVal === 'number') return (aVal - bVal) * dir;
            return String(aVal).localeCompare(String(bVal), 'zh-Hant') * dir;
        });
    }

    _getPaginatedData(sorted) {
        const totalPages = Math.max(1, Math.ceil(sorted.length / this._rowsPerPage));
        this._page = Math.min(this._page, totalPages - 1);
        const start = this._page * this._rowsPerPage;
        return sorted.slice(start, start + this._rowsPerPage);
    }

    _getVisibleColumns() {
        return this.columns.reduce((acc, col, i) => {
            if (col.options?.display !== false && col.options?.display !== 'false') acc.push(i);
            return acc;
        }, []);
    }

    _getCellSortValue(cell) {
        if (cell == null) return '';
        if (typeof cell === 'string' || typeof cell === 'number') return cell;
        return String(cell);
    }

    _renderCell(row, colIdx, dataIndex, rowIndex) {
        const col = this.columns[colIdx];
        if (col.options?.customBodyRenderLite) {
            return col.options.customBodyRenderLite(dataIndex, rowIndex);
        }
        if (col.options?.customBodyRender) {
            const result = col.options.customBodyRender(row[colIdx], {
                rowData: row,
                rowIndex,
                columnIndex: colIdx,
                dataIndex,
            });
            // raw() 標記 → 已知安全 HTML
            if (isRawHtml(result)) return result.__html;
            // 字串 → 一律 escape
            return result == null ? '' : escapeHtml(String(result));
        }
        const val = row[colIdx];
        if (val == null) return '';
        return escapeHtml(String(val));
    }

    _renderToolbar(isAnySelected) {
        const { customToolbar, customToolbarSelect } = this.options;

        if (isAnySelected && customToolbarSelect) {
            const sorted = this._getSortedData();
            const selectedRowsObj = {
                data: this._selectedRows.map(dataIndex => ({
                    index: sorted.findIndex(d => d.dataIndex === dataIndex),
                    dataIndex,
                })),
                lookup: this._selectedRows.reduce((acc, i) => { acc[i] = true; return acc; }, {}),
            };
            const toolbarContent = typeof customToolbarSelect === 'function'
                ? customToolbarSelect(selectedRowsObj, this.data, (rows) => { this._selectedRows = rows; this.render(); })
                : (customToolbarSelect || '');

            return `<div style="display:flex;align-items:center;justify-content:space-between;padding:8px 16px;background-color:var(--cl-bg-active);min-height:48px;">
                <span style="font-weight:500;">${this._selectedRows.length} ${escapeHtml(this._textLabels.selectedRows.text)}已選擇</span>
                <div>${toolbarContent}</div>
            </div>`;
        }

        const titleHtml = isRawHtml(this.title)
            ? this.title.__html
            : (typeof this.title === 'string' && this.title
                ? `<span style="font-weight:bold;">${escapeHtml(this.title)}</span>`
                : (this.title || ''));
        const toolbarHtml = customToolbar
            ? (typeof customToolbar === 'function' ? customToolbar() : (customToolbar || ''))
            : '';

        return `<div style="display:flex;align-items:center;justify-content:space-between;padding:4px 0;background:transparent;">
            <div>${titleHtml}</div>
            <div>${toolbarHtml}</div>
        </div>`;
    }

    _renderPagination(totalCount) {
        const totalPages = Math.max(1, Math.ceil(totalCount / this._rowsPerPage));
        const page = Math.min(this._page, totalPages - 1);
        const start = page * this._rowsPerPage + 1;
        const end = Math.min((page + 1) * this._rowsPerPage, totalCount);
        const options = this.options.rowsPerPageOptions || [10, 20, 100, 500, 1000];

        const btnStyle = (disabled) =>
            `padding:4px 8px;border:1px solid var(--cl-border-dark);border-radius:var(--cl-radius-sm);background:${disabled ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)'};cursor:${disabled ? 'default' : 'pointer'};color:${disabled ? 'var(--cl-text-placeholder)' : 'var(--cl-text)'};min-width:28px;font-size:0.8rem;line-height:1.5;`;

        return `<div style="display:flex;align-items:center;justify-content:flex-end;padding:12px 0;gap:16px;font-size:0.875rem;flex-wrap:wrap;">
            <div style="display:flex;align-items:center;gap:4px;">
                <span>${escapeHtml(this._textLabels.pagination.rowsPerPage)}</span>
                <select data-action="rows-per-page" style="padding:4px 8px;border-radius:var(--cl-radius-sm);border:1px solid var(--cl-border-dark);background:var(--cl-bg);color:var(--cl-text);">
                    ${options.map(opt => `<option value="${opt}" ${opt === this._rowsPerPage ? 'selected' : ''}>${opt}</option>`).join('')}
                </select>
            </div>
            <span>${start}-${end} ${escapeHtml(this._textLabels.pagination.displayRows)} ${totalCount}</span>
            <div style="display:flex;gap:2px;">
                <button data-action="page-first" ${page === 0 ? 'disabled' : ''} style="${btnStyle(page === 0)}" title="${Locale.t('dataTable.firstPage')}">⟨⟨</button>
                <button data-action="page-prev" ${page === 0 ? 'disabled' : ''} style="${btnStyle(page === 0)}" title="${Locale.t('dataTable.prevPage')}">⟨</button>
                <button data-action="page-next" ${page >= totalPages - 1 ? 'disabled' : ''} style="${btnStyle(page >= totalPages - 1)}" title="${Locale.t('dataTable.nextPage')}">⟩</button>
                <button data-action="page-last" ${page >= totalPages - 1 ? 'disabled' : ''} style="${btnStyle(page >= totalPages - 1)}" title="${Locale.t('dataTable.lastPage')}">⟩⟩</button>
            </div>
        </div>`;
    }

    _headerStyle() {
        const t = this._theme;
        return `border:3px solid var(--cl-bg);text-align:center;padding:0px;background-color:${t.headerBg};color:var(--cl-text-inverse);font-weight:bold;font-size:${t.headerFontSize};user-select:none;height:${t.headerHeight}`;
    }

    _cellStyle() {
        const t = this._theme;
        return `border:3px solid var(--cl-bg);text-align:center;padding:0px;font-size:${t.cellFontSize};word-break:${t.wordBreak}`;
    }

    _getCellWidthStyle(col) {
        const props = col.options?.setCellProps?.() || {};
        const style = props.style || {};
        return Object.entries(style).map(([k, v]) => {
            const key = k.replace(/[A-Z]/g, m => `-${m.toLowerCase()}`);
            return `${key}:${v}`;
        }).join(';');
    }

    _bindEvents(root) {
        if (!root) return;

        // 排序
        root.querySelectorAll('[data-action="sort"]').forEach(th => {
            th.addEventListener('click', () => {
                const colIdx = parseInt(th.getAttribute('data-col'));
                const col = this.columns[colIdx];
                if (col.options?.sort === false) return;

                if (this._sortCol === colIdx) {
                    if (this._sortDir === 'asc') this._sortDir = 'desc';
                    else { this._sortCol = null; this._sortDir = null; }
                } else {
                    this._sortCol = colIdx;
                    this._sortDir = 'asc';
                }
                if (this.container) this.render();
                else this._renderToElement();
            });
        });

        // 全選
        root.querySelector('[data-action="select-all"]')?.addEventListener('change', (e) => {
            const sorted = this._getSortedData();
            this._selectedRows = e.target.checked ? sorted.map(d => d.dataIndex) : [];
            this._fireSelectionChange();
            if (this.container) this.render();
            else this._renderToElement();
        });

        // 單行選擇
        root.querySelectorAll('[data-action="select-row"]').forEach(cb => {
            cb.addEventListener('change', () => {
                const dataIndex = parseInt(cb.getAttribute('data-index'));
                if (this.options.selectableRows === 'single') {
                    this._selectedRows = cb.checked ? [dataIndex] : [];
                } else {
                    if (cb.checked) {
                        this._selectedRows.push(dataIndex);
                    } else {
                        this._selectedRows = this._selectedRows.filter(i => i !== dataIndex);
                    }
                }
                this._fireSelectionChange();
                if (this.container) this.render();
                else this._renderToElement();
            });
        });

        // 每頁筆數
        root.querySelector('[data-action="rows-per-page"]')?.addEventListener('change', (e) => {
            this._rowsPerPage = parseInt(e.target.value);
            this._page = 0;
            if (this.container) this.render();
            else this._renderToElement();
        });

        // 分頁按鈕
        const sorted = this._getSortedData();
        const totalPages = Math.max(1, Math.ceil(sorted.length / this._rowsPerPage));
        const rerender = () => { if (this.container) this.render(); else this._renderToElement(); };
        root.querySelector('[data-action="page-first"]')?.addEventListener('click', () => { this._page = 0; rerender(); });
        root.querySelector('[data-action="page-prev"]')?.addEventListener('click', () => { if (this._page > 0) { this._page--; rerender(); } });
        root.querySelector('[data-action="page-next"]')?.addEventListener('click', () => { if (this._page < totalPages - 1) { this._page++; rerender(); } });
        root.querySelector('[data-action="page-last"]')?.addEventListener('click', () => { this._page = totalPages - 1; rerender(); });

        // 行 hover
        root.querySelectorAll('tbody tr[data-row-index]').forEach(tr => {
            tr.addEventListener('mouseenter', () => {
                tr.style.backgroundColor = this._theme.hoverBg;
            });
            tr.addEventListener('mouseleave', () => {
                const dataIndex = parseInt(tr.getAttribute('data-row-index'));
                const viewIndex = Array.from(tr.parentElement.children).indexOf(tr);
                const isEven = viewIndex % 2 === 1;
                const isSelected = this._selectedRows.includes(dataIndex);
                tr.style.backgroundColor = isSelected ? this._theme.selectedBg : (isEven ? this._theme.evenRowBg : this._theme.oddRowBg);
            });
        });
    }

    _fireSelectionChange() {
        if (this.options.onRowSelectionChange) {
            const sorted = this._getSortedData();
            const allSelected = this._selectedRows.map(dataIndex => ({
                index: sorted.findIndex(d => d.dataIndex === dataIndex),
                dataIndex,
            }));
            this.options.onRowSelectionChange([], allSelected, [...this._selectedRows]);
        }
    }
}

export default DataTable;
