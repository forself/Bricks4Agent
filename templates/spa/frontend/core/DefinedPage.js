/**
 * DefinedPage - 宣告式頁面基底類別
 *
 * 透過靜態 definition 物件描述頁面結構（搜尋欄位、表格欄位、API 端點、權限），
 * 自動處理所有樣板邏輯（template 產生、元件掛載、事件綁定、API 呼叫、Session 持久化）。
 *
 * 子類別只需覆寫 transformRow() / renderCell() 提供獨特邏輯。
 *
 * @module DefinedPage
 *
 * @example
 * class MyAuditPage extends DefinedPage {
 *     static definition = {
 *         title: '審核查詢',
 *         api: { search: 'Module/AuditSearch' },
 *         search: { fields: [{ key: 'Name', label: '名稱', type: 'TEXT' }] },
 *         table: { columns: [{ key: 'name', title: '名稱' }] },
 *     };
 *     transformRow(raw) { return { name: raw.Name }; }
 * }
 */

import { BasePage } from './BasePage.js';

export class DefinedPage extends BasePage {

    // ════════════════════════════════════════════════════════
    //  靜態屬性：子類別透過這些屬性注入外部依賴
    // ════════════════════════════════════════════════════════

    /** @type {typeof import('@cl/layout/DataTable/DataTable.js').DataTable} */
    static DataTable = null;

    /** @type {typeof import('@cl/common/Notification/Notification.js').Notification} */
    static Notification = null;

    /** @type {typeof import('../../components/vanilla/FormManager.js').FormManager} */
    static FormManager = null;

    /** @type {typeof import('../../components/uicomponent/SearchDatePick.js').default} */
    static SearchDatePick = null;

    /**
     * API 工具物件，子類別應在應用程式啟動時設定
     * 需提供：apiPost(url, data), apiCache(key), setSessionData(k,v),
     *          getSessionData(k), oneCheckGoback(), loadData(), toast(msg), transDate(d)
     * @type {Object}
     */
    static apiUtils = null;

    /**
     * 角色檢查物件，子類別應在應用程式啟動時設定
     * 需提供：GetRole() → Promise<Object>
     * @type {Object}
     */
    static authHeader = null;

    // ════════════════════════════════════════════════════════
    //  子類別覆寫點（獨特邏輯）
    // ════════════════════════════════════════════════════════

    /**
     * 將 API 原始資料轉換為表格顯示資料（每一列呼叫一次）
     * @param {Object} rawItem - API 回傳的原始資料項
     * @returns {Object} 轉換後的列資料（key 對應 table.columns 的 key）
     */
    transformRow(rawItem) { return rawItem; }

    /**
     * 自訂欄位渲染。回傳 HTML 字串覆寫預設行為，回傳 null 使用預設 esc()
     * @param {string} key - 欄位 key
     * @param {*} value - 欄位值
     * @param {Object} row - 該列完整資料
     * @returns {string|null}
     */
    renderCell(key, value, row) { return null; }

    /**
     * 搜尋完成後的額外處理（可選）
     * @param {Array} data - 搜尋結果
     */
    onSearchComplete(data) {}

    /**
     * 額外 HTML 插在表格下方（可選）
     * @returns {string}
     */
    getExtraTemplate() { return ''; }

    /**
     * 額外事件綁定（可選）。會與自動綁定的事件合併
     * @returns {Object}
     */
    getExtraEvents() { return {}; }

    // ════════════════════════════════════════════════════════
    //  自動處理（通常不需覆寫）
    // ════════════════════════════════════════════════════════

    constructor(options = {}) {
        super(options);
        this.autoUpdate = false;

        const def = this.constructor.definition;
        if (!def) throw new Error(`${this.constructor.name} 必須定義 static definition`);

        this._def = def;
        this._stateKey = def.stateKey || this.constructor.name + 'State';

        this._data = {
            resultData: [],
            vars: {},
            varsAll: {},
            role: {},
            loading: false,
            isOpen: false,
        };

        this._table = null;
        this._searchFm = null;
        this._datePickers = [];

        // 取得依賴（靜態屬性或建構式選項）
        this._DataTable = this.constructor.DataTable || options.DataTable;
        this._Notification = this.constructor.Notification || options.Notification;
        this._FormManager = this.constructor.FormManager || options.FormManager;
        this._SearchDatePick = this.constructor.SearchDatePick || options.SearchDatePick;
        this._utils = this.constructor.apiUtils || options.apiUtils;
        this._authHeader = this.constructor.authHeader || options.authHeader;
    }

    // ──── onInit ────────────────────────────────────────────

    async onInit() {
        const def = this._def;
        const utils = this._utils;

        try {
            // 1. 載入角色和全域資料
            const promises = [];
            if (this._authHeader?.GetRole) promises.push(this._authHeader.GetRole());
            else promises.push(Promise.resolve({}));
            if (utils?.loadData) promises.push(utils.loadData());
            else promises.push(Promise.resolve({}));

            const [role, codeData] = await Promise.all(promises);
            this._data.role = role || {};
            this._data.varsAll = codeData || {};

            // 2. 權限檢查
            if (def.checkRole && !def.checkRole(this._data.role)) {
                this._Notification?.error?.('您無權限瀏覽此頁面');
                this.navigate('/');
                return;
            }

            // 3. 載入頁面專用資料
            if (def.loadVars) {
                try {
                    this._data.vars = await def.loadVars(utils, this._data.varsAll) || {};
                } catch (e) {
                    console.error(`[${this.constructor.name}] 載入頁面資料失敗:`, e?.message);
                }
            }

            // 4. 讀取 session 或初始搜尋
            if (utils?.oneCheckGoback?.() && utils?.getSessionData?.(this._stateKey)) {
                try {
                    const state = JSON.parse(utils.getSessionData(this._stateKey));
                    this._data.resultData = state?.data || [];
                } catch (e) {
                    console.error(`[${this.constructor.name}] 解析查詢狀態失敗:`, e?.message);
                }
            } else if (def.api?.search) {
                try {
                    const response = await utils?.apiPost?.(def.api.search, {});
                    this._data.resultData = response || [];
                } catch (e) {
                    console.error(`[${this.constructor.name}] 初始查詢失敗:`, e?.message);
                    this._Notification?.error?.('載入資料失敗，請稍後再試');
                }
            }
        } catch (error) {
            console.error(`[${this.constructor.name}] 初始化失敗:`, error?.message);
        }
    }

    // ──── template ─────────────────────────────────────────

    template() {
        const def = this._def;
        const d = this._data;

        return `<div>
            <form id="search-form">
                <div class="card">
                    <div class="card-title bg-light border-bottom p-3 mb-0">
                        <i class="fas ${this.escAttr(def.icon || 'fa-search')}"></i> ${this.esc(def.title || '')}
                    </div>
                    <div class="card-body">
                        <strong style="font-size:20px;">查詢條件</strong>
                        <div class="card border-gray" style="background-color:#9dd3a8;">
                            <div class="card-title">
                                <button type="button" class="btn btn-gradient cyan pull-right btn-font"
                                    style="font-weight:bold;background-color:#54c7c7;margin-top:4px;margin-right:10px;"
                                    data-action="toggle-collapse">
                                    <i class="mdi mdi-elevator"></i> 查詢條件收合
                                </button>
                            </div>
                            <div id="collapse-area" style="display:${d.isOpen ? 'block' : 'none'};">
                                <div class="card-body" style="background-color:white;">
                                    ${this._buildSearchFieldsHTML()}
                                    <div class="search-actions">
                                        <button type="submit" class="btn btn-gradient green btn-font">
                                            <i class="fas fa-search"></i> <b>查詢</b>
                                        </button>&nbsp;&nbsp;
                                        <button type="button" class="btn btn-gradient red btn-font" data-action="reset-form">
                                            <i class="fas fa-eraser"></i> <b>清除欄位值</b>
                                        </button>
                                    </div>
                                </div>
                            </div>
                        </div>
                    </div>
                </div>
            </form>
            <div class="card position-sticky"><div class="card-body">
                <div id="result-title" class="pager" style="margin-bottom:10px;">
                    ${this.esc(def.resultLabel || '查詢結果')}
                    <span class="count">共${d.resultData.length}筆</span>
                </div>
                <div id="table-container"></div>
                <div id="loading-area" style="display:none;text-align:center;padding:50px;">
                    <i class="fas fa-spinner fa-spin fa-3x"></i> 載入中...
                </div>
            </div></div>
            ${this.getExtraTemplate()}
        </div>`;
    }

    // ──── onMounted ────────────────────────────────────────

    async onMounted() {
        // 掛載 FormManager
        const formEl = this.$('#search-form');
        if (formEl && this._FormManager) {
            this._searchFm = new this._FormManager(formEl);
            this._searchFm.onSubmit(v => this._onSearch(v));
        }

        // 渲染表格
        this._renderTable();

        // 掛載日期選擇器
        this._mountDatePickers();
    }

    // ──── events ───────────────────────────────────────────

    events() {
        const baseEvents = {
            'click [data-action="toggle-collapse"]': '_toggleCollapse',
            'click [data-action="reset-form"]': '_resetForm',
        };

        // 從 definition.search.fields 收集 onChange 事件
        const fields = this._def.search?.fields || [];
        for (const field of fields) {
            if (field.onChange) {
                baseEvents[`change [data-action="${field.onChange}"]`] = field.onChange;
            }
        }

        // 合併子類別額外事件
        const extra = this.getExtraEvents();
        return { ...baseEvents, ...extra };
    }

    // ──── 搜尋 ────────────────────────────────────────────

    async _onSearch(values) {
        if (this._data.loading) return;
        this._data.loading = true;
        this._showLoading(true);

        try {
            const response = await this._utils?.apiPost?.(this._def.api.search, values || {});
            const data = response || [];
            this._data.resultData = data;

            // Session 持久化
            this._utils?.setSessionData?.(
                this._stateKey,
                JSON.stringify({ query: values, data })
            );

            this._utils?.toast?.('查詢完成，共' + data.length + '筆');
            this._renderTable();
            this._updateResultCount();
            this.onSearchComplete(data);

        } catch (error) {
            console.error(`[${this.constructor.name}] 查詢失敗:`, error?.message);
            this._Notification?.error?.('查詢失敗，請稍後再試');
        } finally {
            this._data.loading = false;
            this._showLoading(false);
        }
    }

    // ──── 表格渲染 ─────────────────────────────────────────

    _renderTable() {
        const container = this.$('#table-container');
        if (!container || !this._DataTable) return;

        const def = this._def;
        const tableData = (this._data.resultData || []).map(item => this.transformRow(item));

        // 建立 columns，整合 renderCell 覆寫
        const columns = (def.table?.columns || []).map(col => {
            const colDef = { ...col };
            // 如果 column 定義中已有 render，保留（子類別可在 definition 直接指定）
            if (!colDef.render) {
                colDef.render = (val, row) => {
                    const custom = this.renderCell(col.key, val, row);
                    return custom !== null ? custom : this.esc(val ?? '');
                };
            }
            return colDef;
        });

        this._table = new this._DataTable(container, {
            columns,
            data: tableData,
            pageSize: def.table?.pageSize || 20,
            pageSizeOptions: def.table?.pageSizeOptions,
        });
    }

    // ──── 搜尋欄位 HTML 產生 ───────────────────────────────

    _buildSearchFieldsHTML() {
        const fields = this._def.search?.fields || [];
        if (fields.length === 0) return '';

        let html = '<div class="search-cards"><div class="card border-gray search-card"><div class="card-body">';

        // 依 row 分組（如果欄位有 row 屬性，否則自動排列）
        let currentRow = null;
        for (const field of fields) {
            const fieldRow = field.row !== undefined ? field.row : null;

            // 需要新 row 時
            if (currentRow === null || (fieldRow !== null && fieldRow !== currentRow)) {
                if (currentRow !== null) html += '</div>'; // 關閉前一個 row
                html += '<div class="row">';
                currentRow = fieldRow !== null ? fieldRow : (currentRow === null ? 0 : currentRow + 1);
            }

            html += this._buildFieldHTML(field);
        }

        if (currentRow !== null) html += '</div>'; // 關閉最後一個 row
        html += '</div></div></div>';

        return html;
    }

    _buildFieldHTML(field) {
        const colWidth = field.colWidth || 'col-md-3';
        let inner = '';

        switch (field.type) {
            case 'TEXT':
                inner = `<label class="font-label">${this.esc(field.label)}</label>
                    <input type="text" name="${this.escAttr(field.key)}" class="form-control"
                        ${field.placeholder ? `placeholder="${this.escAttr(field.placeholder)}"` : ''}
                        ${field.width ? `style="width:${this.escAttr(field.width)}"` : ''} />`;
                break;

            case 'SELECT': {
                const opts = this._resolveOptions(field);
                const actionAttr = field.onChange
                    ? `data-action="${this.escAttr(field.onChange)}"`
                    : '';
                const idAttr = field.dependsOn
                    ? `id="select-${this.escAttr(field.key)}"`
                    : '';
                inner = `<label class="font-label">${this.esc(field.label)}</label>
                    <select name="${this.escAttr(field.key)}" class="form-control" ${actionAttr} ${idAttr}>
                        <option value="">請點選或關鍵字搜尋</option>
                        ${opts.map(o => `<option value="${this.escAttr(o.value ?? o.ADCode ?? '')}">${this.esc(o.label ?? o.Name ?? '')}</option>`).join('')}
                    </select>`;
                break;
            }

            case 'ROC_DATE':
                inner = `<span class="sdp-mount"
                    data-sdp-name="${this.escAttr(field.key)}"
                    data-sdp-label="${this.escAttr(field.label)}"
                    ${field.required ? 'data-sdp-required="true"' : ''}
                    ${field.value ? `data-sdp-value="${this.escAttr(field.value)}"` : ''}></span>
                    <input type="hidden" name="${this.escAttr(field.key)}" />`;
                break;

            case 'RADIO': {
                const allOpts = [
                    ...(field.options || []),
                    ...(field.conditionalOptions ? field.conditionalOptions(this._data.role) : []),
                ];
                const hint = field.hint
                    ? `<small style="display:inline;"> (${this.esc(field.hint)})</small>`
                    : '';
                inner = `<label class="font-label">${this.esc(field.label)}${hint}</label><br/>
                    ${allOpts.map(o =>
                        `<label><input type="radio" name="${this.escAttr(field.key)}" value="${this.escAttr(o.value)}" style="font-size:15px;" /> ${this.esc(o.label)}</label>`
                    ).join(' ')}`;
                break;
            }

            case 'CHECKBOX':
                inner = `<label class="font-label">
                    <input type="checkbox" name="${this.escAttr(field.key)}" value="1" />
                    ${this.esc(field.label)}
                </label>`;
                break;

            case 'DATE_RANGE':
                inner = `<span class="sdp-mount"
                    data-sdp-name="${this.escAttr(field.key + 'S')}"
                    data-sdp-label="${this.escAttr(field.label + '起')}"></span>
                    <input type="hidden" name="${this.escAttr(field.key + 'S')}" />
                    <div class="date-separator">～</div>
                    <span class="sdp-mount"
                    data-sdp-name="${this.escAttr(field.key + 'E')}"
                    data-sdp-label="${this.escAttr(field.label + '迄')}"></span>
                    <input type="hidden" name="${this.escAttr(field.key + 'E')}" />`;
                break;

            default:
                inner = `<label class="font-label">${this.esc(field.label)}</label>
                    <input type="text" name="${this.escAttr(field.key)}" class="form-control" />`;
        }

        return `<div class="${this.escAttr(colWidth)}"${field.style ? ` style="${this.escAttr(field.style)}"` : ''}>${inner}</div>`;
    }

    /**
     * 解析欄位 options（支援陣列、函式、字串來源）
     */
    _resolveOptions(field) {
        if (!field.options) return [];
        if (typeof field.options === 'function') {
            try {
                return field.options(this._data.vars, this._data.varsAll) || [];
            } catch (e) {
                console.error(`[${this.constructor.name}] 解析 ${field.key} 選項失敗:`, e?.message);
                return [];
            }
        }
        return field.options;
    }

    // ──── 日期選擇器掛載 ──────────────────────────────────

    _mountDatePickers() {
        // 清理舊的
        if (this._datePickers) {
            this._datePickers.forEach(dp => dp.destroy?.());
        }
        this._datePickers = [];

        if (!this._SearchDatePick) return;

        const mounts = this.element?.querySelectorAll('.sdp-mount');
        if (!mounts) return;

        mounts.forEach(el => {
            const dp = new this._SearchDatePick(el, {
                nameEn: el.dataset.sdpName,
                nameCh: el.dataset.sdpLabel,
                require: el.dataset.sdpRequired === 'true',
                value: el.dataset.sdpValue || undefined,
                form: this._searchFm,
            });
            dp.mount();
            this._datePickers.push(dp);
        });
    }

    // ──── 通用 UI 方法 ────────────────────────────────────

    _showLoading(show) {
        const loading = this.$('#loading-area');
        const table = this.$('#table-container');
        if (loading) loading.style.display = show ? 'block' : 'none';
        if (table) table.style.display = show ? 'none' : 'block';
    }

    _updateResultCount() {
        const el = this.$('#result-title');
        if (el) {
            el.innerHTML = `${this.esc(this._def.resultLabel || '查詢結果')} <span class="count">共${this._data.resultData.length}筆</span>`;
        }
    }

    _toggleCollapse() {
        this._data.isOpen = !this._data.isOpen;
        const el = this.$('#collapse-area');
        if (el) el.style.display = this._data.isOpen ? 'block' : 'none';
    }

    _resetForm() {
        if (this._searchFm) this._searchFm.resetFields();
    }

    /**
     * 更新聯動的下拉選單（通用方法）
     * @param {string} selector - 目標 select 的 CSS selector
     * @param {string} parentCode - 上層選擇的值
     * @param {string} [filterField='TIMCode'] - 用來過濾的欄位名
     */
    _updateBranchSelect(selector, parentCode, filterField = 'TIMCode') {
        const selectEl = this.$(selector);
        if (!selectEl) return;
        const filtered = parentCode
            ? (this._data.varsAll.allunits || []).filter(u => u[filterField] === parentCode)
            : [];
        selectEl.innerHTML = '<option value="">請點選或關鍵字搜尋</option>'
            + filtered.map(i =>
                `<option value="${this.escAttr(i.ADCode)}">${this.esc(i.Name)}</option>`
            ).join('');
    }

    // ──── 銷毀 ────────────────────────────────────────────

    async onDestroy() {
        if (this._datePickers) {
            this._datePickers.forEach(dp => dp.destroy?.());
            this._datePickers = [];
        }
        this._table = null;
        this._searchFm = null;
    }
}

export default DefinedPage;
