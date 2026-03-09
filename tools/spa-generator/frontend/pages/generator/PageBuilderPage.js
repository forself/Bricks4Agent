/**
 * PageBuilderPage - 頁面建構器
 *
 * 三欄式佈局的頁面定義建構工具，支援：
 * - 左欄：JSON 編輯區（手動輸入、範例模板、檔案匯入）
 * - 中欄：即時預覽區（表單/明細/列表三模式，debounce 300ms）
 * - 右欄：輸出/操作區（驗證、生成靜態 .js、下載、複製）
 *
 * @module PageBuilderPage
 */

import { BasePage } from '../../core/BasePage.js';

/**
 * 員工管理範例定義
 */
const SAMPLE_EMPLOYEE = {
    page: { pageName: '員工管理', entity: 'employee', view: 'adminList' },
    fields: [
        { fieldName: 'id', label: '編號', fieldType: 'number', formRow: 0, formCol: null, listOrder: 1, isRequired: false, isReadonly: true, isSearchable: false },
        { fieldName: 'name', label: '姓名', fieldType: 'text', formRow: 1, formCol: 6, listOrder: 2, isRequired: true, isReadonly: false, isSearchable: true, validation: { maxLength: 50 } },
        { fieldName: 'email', label: '電子郵件', fieldType: 'email', formRow: 1, formCol: 6, listOrder: 3, isRequired: true, isReadonly: false, isSearchable: true },
        { fieldName: 'department', label: '部門', fieldType: 'select', formRow: 2, formCol: 6, listOrder: 4, isRequired: true, isReadonly: false, isSearchable: true, optionsSource: { type: 'static', items: [{ value: 'hr', label: '人力資源部' }, { value: 'it', label: '資訊部' }, { value: 'sales', label: '業務部' }] }, triggers: [{ on: 'change', target: 'team', action: 'reloadOptions' }, { on: 'change', target: 'team', action: 'clear' }] },
        { fieldName: 'hireDate', label: '到職日', fieldType: 'date', formRow: 3, formCol: 6, listOrder: 5, isRequired: true, isReadonly: false, isSearchable: true },
        { fieldName: 'isActive', label: '在職', fieldType: 'toggle', formRow: 3, formCol: 6, listOrder: 6, isRequired: false, isReadonly: false, isSearchable: true, defaultValue: 'true' }
    ]
};

/**
 * 訂單管理範例定義
 */
const SAMPLE_ORDER = {
    page: { pageName: '訂單管理', entity: 'order', view: 'salesList' },
    fields: [
        { fieldName: 'orderId', label: '訂單編號', fieldType: 'text', formRow: 0, formCol: null, listOrder: 1, isRequired: false, isReadonly: true, isSearchable: true },
        { fieldName: 'customerName', label: '客戶名稱', fieldType: 'text', formRow: 1, formCol: 6, listOrder: 2, isRequired: true, isReadonly: false, isSearchable: true, validation: { maxLength: 100 } },
        { fieldName: 'orderDate', label: '訂單日期', fieldType: 'date', formRow: 1, formCol: 6, listOrder: 3, isRequired: true, isReadonly: false, isSearchable: true },
        { fieldName: 'totalAmount', label: '總金額', fieldType: 'number', formRow: 2, formCol: 6, listOrder: 4, isRequired: true, isReadonly: false, isSearchable: false, validation: { min: 0 } },
        { fieldName: 'status', label: '狀態', fieldType: 'select', formRow: 2, formCol: 6, listOrder: 5, isRequired: true, isReadonly: false, isSearchable: true, optionsSource: { type: 'static', items: [{ value: 'pending', label: '待處理' }, { value: 'processing', label: '處理中' }, { value: 'shipped', label: '已出貨' }, { value: 'completed', label: '已完成' }, { value: 'cancelled', label: '已取消' }] } },
        { fieldName: 'notes', label: '備註', fieldType: 'textarea', formRow: 3, formCol: 12, listOrder: null, isRequired: false, isReadonly: false, isSearchable: false }
    ]
};

export class PageBuilderPage extends BasePage {
    async onInit() {
        this._data = {
            // JSON 編輯器的原始文字
            jsonText: JSON.stringify(SAMPLE_EMPLOYEE, null, 2),
            // JSON 是否有效
            jsonValid: true,
            // JSON 解析錯誤訊息
            jsonError: '',
            // 解析後的定義物件
            parsedDefinition: SAMPLE_EMPLOYEE,

            // 預覽模式：form / detail / list
            previewMode: 'form',
            // 預覽錯誤訊息
            previewError: '',

            // 生成的程式碼
            generatedCode: '',
            // 驗證結果
            validationResults: null,
            // 是否正在處理
            processing: false
        };

        // debounce 計時器
        this._debounceTimer = null;
        // DynamicPageRenderer 模組參照
        this._rendererClass = null;
        this._previewRenderer = null;
    }

    template() {
        const {
            jsonText, jsonValid, jsonError,
            previewMode, previewError,
            generatedCode, validationResults, processing
        } = this._data;

        return `
            <div class="page-builder">
                <!-- 左欄：JSON 編輯區 -->
                <div class="page-builder__editor">
                    <div class="page-builder__panel-header">
                        <h3>JSON 編輯區</h3>
                        <span class="page-builder__json-indicator ${jsonValid ? 'page-builder__json-indicator--valid' : 'page-builder__json-indicator--invalid'}"
                              title="${jsonValid ? 'JSON 格式正確' : this.escAttr(jsonError)}"></span>
                    </div>

                    <div class="page-builder__editor-toolbar">
                        <div class="page-builder__sample-buttons">
                            <button type="button" class="btn btn-sm btn-outline" data-action="load-employee">
                                員工範例
                            </button>
                            <button type="button" class="btn btn-sm btn-outline" data-action="load-order">
                                訂單範例
                            </button>
                        </div>
                        <div class="page-builder__editor-actions">
                            <button type="button" class="btn btn-sm btn-outline" data-action="import-file">
                                從檔案匯入
                            </button>
                            <button type="button" class="btn btn-sm btn-outline" data-action="format-json">
                                格式化
                            </button>
                        </div>
                        <input type="file" id="jsonFileInput" accept=".json" style="display:none">
                    </div>

                    <textarea
                        id="jsonEditor"
                        class="page-builder__json-textarea"
                        spellcheck="false"
                        placeholder="在此貼上或輸入 JSON 定義..."
                    >${this.esc(jsonText)}</textarea>

                    ${!jsonValid ? `
                        <div class="page-builder__json-error">
                            ${this.esc(jsonError)}
                        </div>
                    ` : ''}
                </div>

                <!-- 中欄：即時預覽區 -->
                <div class="page-builder__preview">
                    <div class="page-builder__panel-header">
                        <h3>即時預覽</h3>
                    </div>

                    <div class="page-builder__preview-tabs">
                        <button type="button"
                                class="page-builder__tab ${previewMode === 'form' ? 'page-builder__tab--active' : ''}"
                                data-action="set-mode" data-mode="form">
                            表單
                        </button>
                        <button type="button"
                                class="page-builder__tab ${previewMode === 'detail' ? 'page-builder__tab--active' : ''}"
                                data-action="set-mode" data-mode="detail">
                            明細
                        </button>
                        <button type="button"
                                class="page-builder__tab ${previewMode === 'list' ? 'page-builder__tab--active' : ''}"
                                data-action="set-mode" data-mode="list">
                            列表
                        </button>
                    </div>

                    <div id="previewContainer" class="page-builder__preview-container">
                        ${previewError ? `
                            <div class="page-builder__preview-error">
                                <p>預覽渲染失敗</p>
                                <pre>${this.esc(previewError)}</pre>
                            </div>
                        ` : `
                            <div class="page-builder__preview-placeholder">
                                載入預覽中...
                            </div>
                        `}
                    </div>
                </div>

                <!-- 右欄：輸出/操作區 -->
                <div class="page-builder__output">
                    <div class="page-builder__panel-header">
                        <h3>輸出 / 操作</h3>
                    </div>

                    <div class="page-builder__action-buttons">
                        <button type="button"
                                class="btn btn-primary"
                                data-action="validate"
                                ${processing ? 'disabled' : ''}>
                            ${processing ? '處理中...' : '驗證'}
                        </button>
                        <button type="button"
                                class="btn btn-primary"
                                data-action="generate"
                                ${processing || !jsonValid ? 'disabled' : ''}>
                            生成靜態 .js
                        </button>
                        <button type="button"
                                class="btn btn-secondary"
                                data-action="download-json"
                                ${!jsonValid ? 'disabled' : ''}>
                            下載定義 JSON
                        </button>
                        <button type="button"
                                class="btn btn-secondary"
                                data-action="copy-code"
                                ${!generatedCode ? 'disabled' : ''}>
                            複製程式碼
                        </button>
                    </div>

                    <!-- 驗證結果區 -->
                    ${validationResults !== null ? `
                        <div class="page-builder__validation-results">
                            <h4>驗證結果</h4>
                            ${validationResults.valid ? `
                                <div class="page-builder__validation-success">
                                    定義驗證通過，共 ${validationResults.fieldCount || 0} 個欄位。
                                </div>
                            ` : `
                                <div class="page-builder__validation-errors">
                                    <p>驗證失敗：</p>
                                    <ul>
                                        ${(validationResults.errors || []).map(err => `
                                            <li>${this.esc(err)}</li>
                                        `).join('')}
                                    </ul>
                                </div>
                            `}
                        </div>
                    ` : ''}

                    <!-- 生成的程式碼區 -->
                    ${generatedCode ? `
                        <div class="page-builder__code-output">
                            <h4>生成的程式碼</h4>
                            <textarea
                                id="generatedCodeArea"
                                class="page-builder__code-textarea"
                                readonly
                            >${this.esc(generatedCode)}</textarea>
                        </div>
                    ` : ''}
                </div>
            </div>
        `;
    }

    events() {
        return {
            'input #jsonEditor': 'onJsonInput',
            'click [data-action="load-employee"]': 'onLoadEmployee',
            'click [data-action="load-order"]': 'onLoadOrder',
            'click [data-action="import-file"]': 'onImportFile',
            'change #jsonFileInput': 'onFileSelected',
            'click [data-action="format-json"]': 'onFormatJson',
            'click [data-action="set-mode"]': 'onSetMode',
            'click [data-action="validate"]': 'onValidate',
            'click [data-action="generate"]': 'onGenerate',
            'click [data-action="download-json"]': 'onDownloadJson',
            'click [data-action="copy-code"]': 'onCopyCode'
        };
    }

    async onMounted() {
        // 初次掛載後執行預覽渲染
        await this._renderPreview();
    }

    /**
     * 頁面更新後重新渲染預覽
     */
    async onUpdated() {
        // 每次 DOM 更新後，重新同步 textarea 的值（因 BasePage 會重建 DOM）
        const editor = this.$('#jsonEditor');
        if (editor && editor.value !== this._data.jsonText) {
            editor.value = this._data.jsonText;
        }
    }

    // ─── JSON 編輯區事件 ───

    /**
     * JSON 輸入變更，加上 debounce 300ms
     */
    onJsonInput(event) {
        const text = event.target.value;
        this._data.jsonText = text;
        this._parseAndValidateJson(text);

        // debounce 300ms 後重新渲染預覽
        if (this._debounceTimer) {
            clearTimeout(this._debounceTimer);
        }
        this._debounceTimer = setTimeout(() => {
            this._renderPreview();
        }, 300);
    }

    /**
     * 載入員工範例
     */
    onLoadEmployee() {
        this._loadSample(SAMPLE_EMPLOYEE);
    }

    /**
     * 載入訂單範例
     */
    onLoadOrder() {
        this._loadSample(SAMPLE_ORDER);
    }

    /**
     * 開啟檔案選擇器
     */
    onImportFile() {
        const fileInput = this.$('#jsonFileInput');
        if (fileInput) {
            fileInput.click();
        }
    }

    /**
     * 檔案選取完成，讀取 JSON 內容
     */
    onFileSelected(event) {
        const file = event.target.files[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = (e) => {
            const text = e.target.result;
            this._data.jsonText = text;
            this._parseAndValidateJson(text);
            this._scheduleUpdate();
            // 延遲渲染預覽，確保 DOM 已更新
            setTimeout(() => this._renderPreview(), 50);
        };
        reader.onerror = () => {
            this.showMessage('檔案讀取失敗', 'error');
        };
        reader.readAsText(file);
    }

    /**
     * 格式化 JSON（美化輸出）
     */
    onFormatJson() {
        if (!this._data.jsonValid || !this._data.parsedDefinition) {
            this.showMessage('JSON 格式不正確，無法格式化', 'error');
            return;
        }
        const formatted = JSON.stringify(this._data.parsedDefinition, null, 2);
        this._data.jsonText = formatted;
        this._scheduleUpdate();
    }

    // ─── 預覽區事件 ───

    /**
     * 切換預覽模式
     */
    onSetMode(event, target) {
        const btn = target || event.target.closest('[data-action="set-mode"]');
        if (!btn) return;

        const mode = btn.dataset.mode;
        if (mode && mode !== this._data.previewMode) {
            this._data.previewMode = mode;
            this._scheduleUpdate();
            setTimeout(() => this._renderPreview(), 50);
        }
    }

    // ─── 輸出區事件 ───

    /**
     * 驗證定義 JSON
     */
    async onValidate() {
        if (!this._data.jsonValid) {
            this._data.validationResults = {
                valid: false,
                errors: ['JSON 格式不正確，請先修正語法錯誤。']
            };
            this._scheduleUpdate();
            return;
        }

        this._data.processing = true;
        this._scheduleUpdate();

        try {
            const response = await this.api.post('/page-builder/validate', {
                definition: this._data.parsedDefinition
            });

            this._data.validationResults = response.data || response;
        } catch (error) {
            // 後端不可用時，進行前端基本驗證
            console.warn('[PageBuilderPage] 後端驗證 API 不可用，使用前端驗證');
            this._data.validationResults = this._validateLocally(this._data.parsedDefinition);
        } finally {
            this._data.processing = false;
            this._scheduleUpdate();
        }
    }

    /**
     * 生成靜態 .js 檔案
     */
    async onGenerate() {
        if (!this._data.jsonValid || !this._data.parsedDefinition) {
            this.showMessage('JSON 無效，無法生成', 'error');
            return;
        }

        this._data.processing = true;
        this._scheduleUpdate();

        try {
            const response = await this.api.post('/page-builder/generate', {
                definition: this._data.parsedDefinition
            });

            this._data.generatedCode = response.data?.code || response.code || '';
            this.showMessage('程式碼生成成功', 'success');
        } catch (error) {
            // 後端不可用時，使用前端模擬生成
            console.warn('[PageBuilderPage] 後端生成 API 不可用，使用前端模擬生成');
            this._data.generatedCode = this._generateCodeLocally(this._data.parsedDefinition);
            this.showMessage('程式碼生成成功（前端模擬）', 'success');
        } finally {
            this._data.processing = false;
            this._scheduleUpdate();
        }
    }

    /**
     * 下載定義 JSON 檔案
     */
    onDownloadJson() {
        if (!this._data.jsonValid || !this._data.parsedDefinition) {
            this.showMessage('JSON 無效，無法下載', 'error');
            return;
        }

        const def = this._data.parsedDefinition;
        const fileName = (def.page?.entity || def.page?.pageName || 'definition') + '.json';
        const json = JSON.stringify(def, null, 2);
        const blob = new Blob([json], { type: 'application/json' });
        const url = URL.createObjectURL(blob);

        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.click();
        URL.revokeObjectURL(url);

        this.showMessage(`已下載 ${fileName}`, 'success');
    }

    /**
     * 複製生成的程式碼到剪貼簿
     */
    async onCopyCode() {
        const { generatedCode } = this._data;
        if (!generatedCode) return;

        try {
            await navigator.clipboard.writeText(generatedCode);
            this.showMessage('已複製到剪貼簿', 'success');
        } catch (error) {
            this.showMessage('複製失敗', 'error');
        }
    }

    // ─── 內部方法 ───

    /**
     * 載入範例定義
     * @param {Object} sample - 範例定義物件
     */
    _loadSample(sample) {
        const text = JSON.stringify(sample, null, 2);
        this._data.jsonText = text;
        this._data.parsedDefinition = sample;
        this._data.jsonValid = true;
        this._data.jsonError = '';
        this._data.generatedCode = '';
        this._data.validationResults = null;
        this._scheduleUpdate();
        setTimeout(() => this._renderPreview(), 50);
    }

    /**
     * 解析並驗證 JSON 文字
     * @param {string} text - JSON 原始文字
     */
    _parseAndValidateJson(text) {
        if (!text.trim()) {
            this._data.jsonValid = false;
            this._data.jsonError = 'JSON 內容不可為空';
            this._data.parsedDefinition = null;
            return;
        }

        try {
            const parsed = JSON.parse(text);
            this._data.parsedDefinition = parsed;
            this._data.jsonValid = true;
            this._data.jsonError = '';
        } catch (e) {
            this._data.jsonValid = false;
            this._data.jsonError = e.message;
            this._data.parsedDefinition = null;
        }
    }

    /**
     * 渲染即時預覽
     * 動態載入 DynamicPageRenderer 並渲染到預覽容器
     */
    async _renderPreview() {
        const container = this.$('#previewContainer');
        if (!container) return;

        const { parsedDefinition, previewMode } = this._data;

        if (!parsedDefinition) {
            container.innerHTML = `
                <div class="page-builder__preview-placeholder">
                    請輸入有效的 JSON 定義
                </div>
            `;
            return;
        }

        try {
            // 動態載入 DynamicPageRenderer（避免循環依賴）
            if (!this._rendererClass) {
                try {
                    const module = await import('../../../../../packages/javascript/browser/page-generator/DynamicPageRenderer.js');
                    this._rendererClass = module.DynamicPageRenderer || module.default;
                } catch (importErr) {
                    console.warn('[PageBuilderPage] 無法載入 DynamicPageRenderer:', importErr);
                    // 降級為簡易 HTML 預覽
                    this._renderSimplePreview(container, parsedDefinition, previewMode);
                    return;
                }
            }

            // 使用 DynamicPageRenderer 渲染
            this._previewRenderer?.destroy?.();
            container.innerHTML = '';
            const renderer = new this._rendererClass({
                definition: parsedDefinition,
                mode: previewMode,
                container: container
            });
            await renderer.init();
            renderer.mount(container);
            this._previewRenderer = renderer;

            this._data.previewError = '';
        } catch (error) {
            console.error('[PageBuilderPage] 預覽渲染錯誤:', error);
            this._data.previewError = error.message || '未知渲染錯誤';
            container.innerHTML = `
                <div class="page-builder__preview-error">
                    <p>預覽渲染失敗</p>
                    <pre>${this.esc(error.message || '未知錯誤')}</pre>
                </div>
            `;
        }
    }

    /**
     * 降級的簡易 HTML 預覽（當 DynamicPageRenderer 不可用時）
     * @param {HTMLElement} container - 預覽容器
     * @param {Object} definition - 頁面定義
     * @param {string} mode - 預覽模式
     */
    _renderSimplePreview(container, definition, mode) {
        const fields = definition.fields || [];
        const pageName = definition.page?.pageName || '未命名頁面';

        let html = `<div class="page-builder__simple-preview">`;
        html += `<h4>${this.esc(pageName)} - ${mode === 'form' ? '表單' : mode === 'detail' ? '明細' : '列表'}模式</h4>`;

        if (mode === 'form') {
            html += '<div class="preview-form">';
            for (const field of fields) {
                if (field.isReadonly && field.formRow === 0) continue;
                const colClass = field.formCol ? `col-${field.formCol}` : 'col-12';
                html += `
                    <div class="preview-form-group ${colClass}">
                        <label>${this.esc(field.label || field.fieldName)}${field.isRequired ? ' *' : ''}</label>
                        ${this._renderPreviewInput(field)}
                    </div>
                `;
            }
            html += '</div>';
        } else if (mode === 'detail') {
            html += '<div class="preview-detail">';
            for (const field of fields) {
                html += `
                    <div class="preview-detail-row">
                        <span class="preview-detail-label">${this.esc(field.label || field.fieldName)}</span>
                        <span class="preview-detail-value">（範例值）</span>
                    </div>
                `;
            }
            html += '</div>';
        } else if (mode === 'list') {
            const listFields = fields
                .filter(f => f.listOrder != null)
                .sort((a, b) => a.listOrder - b.listOrder);

            html += '<table class="preview-table"><thead><tr>';
            for (const field of listFields) {
                html += `<th>${this.esc(field.label || field.fieldName)}</th>`;
            }
            html += '</tr></thead><tbody>';
            // 產生 3 行範例資料
            for (let row = 1; row <= 3; row++) {
                html += '<tr>';
                for (const field of listFields) {
                    html += `<td>${this.esc(this._getSampleValue(field, row))}</td>`;
                }
                html += '</tr>';
            }
            html += '</tbody></table>';
        }

        html += '</div>';
        container.innerHTML = html;
    }

    /**
     * 渲染預覽用的輸入元素
     * @param {Object} field - 欄位定義
     * @returns {string} HTML 字串
     */
    _renderPreviewInput(field) {
        const type = field.fieldType || 'text';

        switch (type) {
            case 'select':
                const items = field.optionsSource?.items || [];
                return `<select disabled>
                    <option value="">請選擇</option>
                    ${items.map(item => `<option value="${this.escAttr(item.value)}">${this.esc(item.label)}</option>`).join('')}
                </select>`;

            case 'textarea':
                return `<textarea disabled placeholder="${this.escAttr(field.label || '')}"></textarea>`;

            case 'toggle':
                return `<label class="preview-toggle"><input type="checkbox" disabled ${field.defaultValue === 'true' ? 'checked' : ''}><span></span></label>`;

            case 'date':
                return `<input type="date" disabled>`;

            default:
                return `<input type="${this.escAttr(type)}" disabled placeholder="${this.escAttr(field.label || '')}">`;
        }
    }

    /**
     * 產生範例值（供列表預覽使用）
     * @param {Object} field - 欄位定義
     * @param {number} row - 行號
     * @returns {string} 範例值
     */
    _getSampleValue(field, row) {
        const type = field.fieldType || 'text';

        switch (type) {
            case 'number':
                return String(row * 100 + row);
            case 'email':
                return `user${row}@example.com`;
            case 'date':
                return `2024-0${row}-15`;
            case 'toggle':
                return row % 2 === 0 ? '否' : '是';
            case 'select':
                const items = field.optionsSource?.items || [];
                return items[row % items.length]?.label || '—';
            default:
                return `${field.label || field.fieldName}${row}`;
        }
    }

    /**
     * 前端本地驗證
     * @param {Object} definition - 頁面定義
     * @returns {Object} 驗證結果
     */
    _validateLocally(definition) {
        const errors = [];

        if (!definition.page) {
            errors.push('缺少 page 設定區塊');
        } else {
            if (!definition.page.pageName) errors.push('page.pageName 為必填');
            if (!definition.page.entity) errors.push('page.entity 為必填');
        }

        if (!definition.fields || !Array.isArray(definition.fields)) {
            errors.push('缺少 fields 陣列或格式不正確');
        } else if (definition.fields.length === 0) {
            errors.push('fields 陣列不可為空');
        } else {
            for (let i = 0; i < definition.fields.length; i++) {
                const f = definition.fields[i];
                if (!f.fieldName) errors.push(`fields[${i}] 缺少 fieldName`);
                if (!f.fieldType) errors.push(`fields[${i}] 缺少 fieldType`);
            }
        }

        return {
            valid: errors.length === 0,
            errors,
            fieldCount: definition.fields?.length || 0
        };
    }

    /**
     * 前端模擬程式碼生成
     * @param {Object} definition - 頁面定義
     * @returns {string} 生成的 JavaScript 程式碼
     */
    _generateCodeLocally(definition) {
        const pageName = definition.page?.pageName || '未命名';
        const entity = definition.page?.entity || 'item';
        const fields = definition.fields || [];
        const className = entity.charAt(0).toUpperCase() + entity.slice(1) + 'Page';

        const fieldInits = fields.map(f => {
            const defaultVal = f.defaultValue != null ? JSON.stringify(f.defaultValue) : "''";
            return `            ${f.fieldName}: ${defaultVal}`;
        }).join(',\n');

        const formFields = fields
            .filter(f => !(f.isReadonly && f.formRow === 0))
            .map(f => {
                const required = f.isRequired ? 'required' : '';
                return `                    <div class="form-group">
                        <label for="${f.fieldName}">${f.label || f.fieldName}${f.isRequired ? ' *' : ''}</label>
                        <input type="${f.fieldType === 'email' ? 'email' : f.fieldType === 'number' ? 'number' : 'text'}"
                               id="${f.fieldName}" name="${f.fieldName}"
                               value="\${this.escAttr(form.${f.fieldName})}"
                               ${required}>
                    </div>`;
            }).join('\n');

        return `/**
 * ${className} - ${pageName}
 *
 * 由 PageBuilder 自動生成
 * 生成時間: ${new Date().toISOString()}
 *
 * @module ${className}
 */

import { BasePage } from '../core/BasePage.js';

export class ${className} extends BasePage {
    async onInit() {
        this._data = {
            form: {
${fieldInits}
            },
            loading: false,
            submitting: false,
            error: null
        };
    }

    template() {
        const { form, loading, submitting, error } = this._data;

        return \`
            <div class="${entity}-page">
                <header class="page-header">
                    <h1>${pageName}</h1>
                </header>

                \${error ? \`
                    <div class="alert alert-error">
                        <p>\${this.esc(error)}</p>
                    </div>
                \` : ''}

                <form id="main-form" class="form-container">
${formFields}

                    <div class="form-actions">
                        <button type="submit" class="btn btn-primary" \${submitting ? 'disabled' : ''}>
                            \${submitting ? '處理中...' : '儲存'}
                        </button>
                    </div>
                </form>
            </div>
        \`;
    }

    events() {
        return {
            'submit #main-form': 'onSubmit',
            'input .form-group input': 'onInput'
        };
    }

    onInput(event) {
        const { name, value } = event.target;
        this._data.form[name] = value;
    }

    async onSubmit(event) {
        event.preventDefault();
        this._data.submitting = true;
        this._scheduleUpdate();

        try {
            // TODO: 實作 API 呼叫
            await this.api.post('/api/${entity}', this._data.form);
            this.showMessage('儲存成功', 'success');
        } catch (error) {
            this._data.error = error.message || '儲存失敗';
        } finally {
            this._data.submitting = false;
            this._scheduleUpdate();
        }
    }
}

export default ${className};
`;
    }

    /**
     * 銷毀時清理 debounce 計時器
     */
    async onDestroy() {
        if (this._debounceTimer) {
            clearTimeout(this._debounceTimer);
            this._debounceTimer = null;
        }

        this._previewRenderer?.destroy?.();
        this._previewRenderer = null;
    }
}

export default PageBuilderPage;
