/**
 * PageDefinitionEditorPage - 頁面定義編輯器
 *
 * 視覺化編輯 PageDefinition，透過問卷互動建立頁面定義
 * 整合 PageGenerator 生成實際頁面程式碼
 *
 * @module PageDefinitionEditorPage
 */

import { BasePage } from '../../core/BasePage.js';

// 欄位類型定義（與 PageDefinition 對應）
const FieldTypes = {
    TEXT: 'text',
    TEXTAREA: 'textarea',
    NUMBER: 'number',
    EMAIL: 'email',
    PASSWORD: 'password',
    SELECT: 'select',
    RADIO: 'radio',
    CHECKBOX: 'checkbox',
    TOGGLE: 'toggle',
    DATE: 'date',
    TIME: 'time',
    DATETIME: 'datetime',
    RICHTEXT: 'richtext',
    CANVAS: 'canvas',
    COLOR: 'color',
    IMAGE: 'image',
    FILE: 'file',
    GEOLOCATION: 'geolocation',
    WEATHER: 'weather',
    HIDDEN: 'hidden'
};

// 頁面類型定義
const PageTypes = {
    FORM: 'form',
    LIST: 'list',
    DETAIL: 'detail',
    DASHBOARD: 'dashboard'
};

// 欄位類型對應的元件
const ComponentMapping = {
    [FieldTypes.DATE]: 'DatePicker',
    [FieldTypes.COLOR]: 'ColorPicker',
    [FieldTypes.IMAGE]: 'ImageViewer',
    [FieldTypes.RICHTEXT]: 'WebTextEditor',
    [FieldTypes.CANVAS]: 'DrawingBoard',
    [FieldTypes.GEOLOCATION]: 'GeolocationService',
    [FieldTypes.WEATHER]: 'WeatherService'
};

// 可用元件清單
const AvailableComponents = {
    ui: [
        { name: 'DatePicker', description: '日期選擇器', category: 'ui' },
        { name: 'ColorPicker', description: '顏色選擇器', category: 'ui' },
        { name: 'ImageViewer', description: '圖片檢視器', category: 'ui' },
        { name: 'ToastPanel', description: '提示訊息面板', category: 'ui' },
        { name: 'ModalPanel', description: '模態對話框', category: 'ui' }
    ],
    services: [
        { name: 'GeolocationService', description: '地理位置服務', category: 'service' },
        { name: 'WeatherService', description: '天氣服務', category: 'service' }
    ],
    advanced: [
        { name: 'WebTextEditor', description: '富文本編輯器', category: 'advanced', note: '需要 packages' },
        { name: 'DrawingBoard', description: '繪圖板', category: 'advanced', note: '需要 packages' }
    ]
};

export class PageDefinitionEditorPage extends BasePage {
    async onInit() {
        this._data = {
            // 當前步驟
            currentStep: 1,
            totalSteps: 5,

            // 頁面基本資訊
            pageInfo: {
                name: '',
                description: '',
                type: PageTypes.FORM
            },

            // 欄位定義
            fields: [],

            // 選擇的元件
            selectedComponents: ['ToastPanel', 'ModalPanel'],

            // API 設定
            apiConfig: {
                baseEndpoint: '',
                enableCrud: true
            },

            // 行為設定
            behaviors: {
                onInit: '',
                onSave: '',
                onDelete: ''
            },

            // 生成結果
            generatedCode: null,
            generating: false,
            error: null
        };
    }

    template() {
        const { currentStep, error } = this._data;

        return `
            <div class="page-definition-editor">
                <header class="page-header">
                    <h1>頁面定義編輯器</h1>
                    <p class="page-subtitle">透過問卷互動建立頁面定義，自動生成程式碼</p>
                </header>

                ${error ? `
                    <div class="alert alert-error">
                        <p>${this.esc(error)}</p>
                        <button type="button" class="btn-close" data-action="clear-error">&times;</button>
                    </div>
                ` : ''}

                ${this._renderProgressBar()}

                <div class="editor-content">
                    ${this._renderCurrentStep()}
                </div>

                ${this._renderNavigation()}
            </div>
        `;
    }

    _renderProgressBar() {
        const { currentStep, totalSteps } = this._data;
        const steps = [
            '基本資訊',
            '欄位定義',
            '元件選擇',
            'API 設定',
            '預覽生成'
        ];

        return `
            <div class="progress-bar">
                ${steps.map((step, index) => `
                    <div class="progress-step ${index + 1 < currentStep ? 'completed' : ''} ${index + 1 === currentStep ? 'active' : ''}">
                        <div class="step-number">${index + 1}</div>
                        <div class="step-label">${step}</div>
                    </div>
                `).join('')}
            </div>
        `;
    }

    _renderCurrentStep() {
        const { currentStep } = this._data;

        switch (currentStep) {
            case 1: return this._renderStep1();
            case 2: return this._renderStep2();
            case 3: return this._renderStep3();
            case 4: return this._renderStep4();
            case 5: return this._renderStep5();
            default: return '';
        }
    }

    // 步驟 1: 基本資訊
    _renderStep1() {
        const { pageInfo } = this._data;

        return `
            <div class="step-content">
                <h2>步驟 1: 基本資訊</h2>
                <p class="step-description">設定頁面的基本資訊</p>

                <div class="form-section">
                    <div class="form-group">
                        <label for="pageName">頁面名稱 *</label>
                        <input type="text" id="pageName" name="name"
                               value="${this.escAttr(pageInfo.name)}"
                               placeholder="例如: ProductForm, OrderDetail"
                               pattern="^[A-Z][a-zA-Z0-9]*$"
                               required>
                        <small>PascalCase 格式，會自動加上 Page 後綴</small>
                    </div>

                    <div class="form-group">
                        <label for="pageDescription">頁面描述</label>
                        <input type="text" id="pageDescription" name="description"
                               value="${this.escAttr(pageInfo.description)}"
                               placeholder="例如: 商品編輯表單">
                    </div>

                    <div class="form-group">
                        <label>頁面類型 *</label>
                        <div class="type-selector">
                            ${Object.entries(PageTypes).map(([key, value]) => `
                                <label class="type-option ${pageInfo.type === value ? 'selected' : ''}"
                                       data-type="${value}">
                                    <input type="radio" name="pageType" value="${value}"
                                           ${pageInfo.type === value ? 'checked' : ''}>
                                    <span class="type-icon">${this._getPageTypeIcon(value)}</span>
                                    <span class="type-name">${this._getPageTypeName(value)}</span>
                                    <span class="type-desc">${this._getPageTypeDesc(value)}</span>
                                </label>
                            `).join('')}
                        </div>
                    </div>
                </div>
            </div>
        `;
    }

    // 步驟 2: 欄位定義
    _renderStep2() {
        const { fields } = this._data;

        return `
            <div class="step-content">
                <h2>步驟 2: 欄位定義</h2>
                <p class="step-description">定義頁面需要的欄位</p>

                <div class="fields-list">
                    ${fields.length === 0 ? `
                        <div class="empty-state">
                            <p>尚未定義任何欄位</p>
                            <p>點擊「新增欄位」開始定義</p>
                        </div>
                    ` : fields.map((field, index) => this._renderFieldItem(field, index)).join('')}
                </div>

                <button type="button" class="btn btn-secondary" data-action="add-field">
                    + 新增欄位
                </button>

                <div class="quick-templates">
                    <h4>快速模板</h4>
                    <div class="template-buttons">
                        <button type="button" class="btn btn-outline" data-action="template-basic">
                            基本表單
                        </button>
                        <button type="button" class="btn btn-outline" data-action="template-user">
                            使用者資料
                        </button>
                        <button type="button" class="btn btn-outline" data-action="template-content">
                            內容編輯
                        </button>
                    </div>
                </div>
            </div>
        `;
    }

    _renderFieldItem(field, index) {
        const typeLabel = this._getFieldTypeLabel(field.type);
        const componentBadge = ComponentMapping[field.type]
            ? `<span class="component-badge">${ComponentMapping[field.type]}</span>`
            : '';

        return `
            <div class="field-item" data-index="${index}">
                <div class="field-header">
                    <span class="field-name">${this.esc(field.name)}</span>
                    <span class="field-type">${typeLabel}</span>
                    ${componentBadge}
                    ${field.required ? '<span class="required-badge">必填</span>' : ''}
                </div>
                <div class="field-label">${this.esc(field.label || field.name)}</div>
                <div class="field-actions">
                    <button type="button" class="btn-icon" data-action="edit-field" data-index="${index}">
                        ✏️
                    </button>
                    <button type="button" class="btn-icon" data-action="delete-field" data-index="${index}">
                        🗑️
                    </button>
                    ${index > 0 ? `
                        <button type="button" class="btn-icon" data-action="move-field-up" data-index="${index}">
                            ⬆️
                        </button>
                    ` : ''}
                    ${index < this._data.fields.length - 1 ? `
                        <button type="button" class="btn-icon" data-action="move-field-down" data-index="${index}">
                            ⬇️
                        </button>
                    ` : ''}
                </div>
            </div>
        `;
    }

    // 步驟 3: 元件選擇
    _renderStep3() {
        const { selectedComponents, fields } = this._data;

        // 自動推斷的元件
        const inferredComponents = new Set();
        for (const field of fields) {
            const comp = ComponentMapping[field.type];
            if (comp) inferredComponents.add(comp);
        }

        return `
            <div class="step-content">
                <h2>步驟 3: 元件選擇</h2>
                <p class="step-description">選擇頁面需要的元件</p>

                ${inferredComponents.size > 0 ? `
                    <div class="inferred-components">
                        <h4>根據欄位自動推斷的元件</h4>
                        <div class="component-tags">
                            ${[...inferredComponents].map(comp => `
                                <span class="component-tag inferred">${comp} (自動)</span>
                            `).join('')}
                        </div>
                    </div>
                ` : ''}

                <div class="component-categories">
                    ${this._renderComponentCategory('UI 元件', AvailableComponents.ui, selectedComponents)}
                    ${this._renderComponentCategory('服務', AvailableComponents.services, selectedComponents)}
                    ${this._renderComponentCategory('進階元件', AvailableComponents.advanced, selectedComponents)}
                </div>
            </div>
        `;
    }

    _renderComponentCategory(title, components, selectedComponents) {
        return `
            <div class="component-category">
                <h4>${title}</h4>
                <div class="component-list">
                    ${components.map(comp => `
                        <label class="component-item ${selectedComponents.includes(comp.name) ? 'selected' : ''}">
                            <input type="checkbox" name="component" value="${comp.name}"
                                   ${selectedComponents.includes(comp.name) ? 'checked' : ''}>
                            <span class="component-name">${comp.name}</span>
                            <span class="component-desc">${comp.description}</span>
                            ${comp.note ? `<span class="component-note">${comp.note}</span>` : ''}
                        </label>
                    `).join('')}
                </div>
            </div>
        `;
    }

    // 步驟 4: API 設定
    _renderStep4() {
        const { apiConfig, behaviors } = this._data;

        return `
            <div class="step-content">
                <h2>步驟 4: API 設定</h2>
                <p class="step-description">設定 API 端點和頁面行為</p>

                <div class="form-section">
                    <h3>API 端點</h3>
                    <div class="form-group">
                        <label for="baseEndpoint">基礎端點</label>
                        <input type="text" id="baseEndpoint" name="baseEndpoint"
                               value="${this.escAttr(apiConfig.baseEndpoint)}"
                               placeholder="/api/items">
                        <small>設定後會自動產生 CRUD 端點</small>
                    </div>

                    <div class="form-group">
                        <label class="checkbox-label">
                            <input type="checkbox" id="enableCrud" name="enableCrud"
                                   ${apiConfig.enableCrud ? 'checked' : ''}>
                            啟用完整 CRUD 操作
                        </label>
                    </div>
                </div>

                <div class="form-section">
                    <h3>頁面行為</h3>
                    <div class="form-group">
                        <label for="onInit">初始化行為</label>
                        <input type="text" id="onInit" name="onInit"
                               value="${this.escAttr(behaviors.onInit)}"
                               placeholder="loadInitialData">
                        <small>頁面載入時執行的方法名稱</small>
                    </div>

                    <div class="form-group">
                        <label for="onSave">儲存後行為</label>
                        <input type="text" id="onSave" name="onSave"
                               value="${this.escAttr(behaviors.onSave)}"
                               placeholder="navigateToList">
                        <small>儲存成功後執行的方法名稱</small>
                    </div>

                    <div class="form-group">
                        <label for="onDelete">刪除後行為</label>
                        <input type="text" id="onDelete" name="onDelete"
                               value="${this.escAttr(behaviors.onDelete)}"
                               placeholder="navigateBack">
                        <small>刪除成功後執行的方法名稱</small>
                    </div>
                </div>
            </div>
        `;
    }

    // 步驟 5: 預覽與生成
    _renderStep5() {
        const { generatedCode, generating } = this._data;
        const definition = this._buildDefinition();

        return `
            <div class="step-content">
                <h2>步驟 5: 預覽與生成</h2>
                <p class="step-description">確認定義並生成程式碼</p>

                <div class="preview-panels">
                    <div class="preview-panel">
                        <h3>頁面定義 (JSON)</h3>
                        <pre class="code-block">${this.esc(JSON.stringify(definition, null, 2))}</pre>
                    </div>

                    ${generatedCode ? `
                        <div class="preview-panel">
                            <h3>生成的程式碼</h3>
                            <div class="code-actions">
                                <button type="button" class="btn btn-sm" data-action="copy-code">
                                    複製程式碼
                                </button>
                                <button type="button" class="btn btn-sm" data-action="download-code">
                                    下載檔案
                                </button>
                            </div>
                            <pre class="code-block generated-code">${this.esc(generatedCode)}</pre>
                        </div>
                    ` : ''}
                </div>

                <div class="generate-actions">
                    <button type="button" class="btn btn-primary btn-lg" data-action="generate"
                            ${generating ? 'disabled' : ''}>
                        ${generating ? '生成中...' : '生成程式碼'}
                    </button>
                </div>
            </div>
        `;
    }

    _renderNavigation() {
        const { currentStep, totalSteps, generating } = this._data;

        return `
            <div class="editor-navigation">
                ${currentStep > 1 ? `
                    <button type="button" class="btn btn-secondary" data-action="prev-step">
                        ← 上一步
                    </button>
                ` : '<div></div>'}

                ${currentStep < totalSteps ? `
                    <button type="button" class="btn btn-primary" data-action="next-step">
                        下一步 →
                    </button>
                ` : ''}
            </div>
        `;
    }

    // 輔助方法
    _getPageTypeIcon(type) {
        const icons = {
            [PageTypes.FORM]: '📝',
            [PageTypes.LIST]: '📋',
            [PageTypes.DETAIL]: '📄',
            [PageTypes.DASHBOARD]: '📊'
        };
        return icons[type] || '📄';
    }

    _getPageTypeName(type) {
        const names = {
            [PageTypes.FORM]: '表單頁',
            [PageTypes.LIST]: '列表頁',
            [PageTypes.DETAIL]: '詳情頁',
            [PageTypes.DASHBOARD]: '儀表板'
        };
        return names[type] || type;
    }

    _getPageTypeDesc(type) {
        const descs = {
            [PageTypes.FORM]: '用於新增或編輯資料',
            [PageTypes.LIST]: '用於顯示資料列表',
            [PageTypes.DETAIL]: '用於顯示單筆資料詳情',
            [PageTypes.DASHBOARD]: '用於顯示統計和圖表'
        };
        return descs[type] || '';
    }

    _getFieldTypeLabel(type) {
        const labels = {
            [FieldTypes.TEXT]: '文字',
            [FieldTypes.TEXTAREA]: '多行文字',
            [FieldTypes.NUMBER]: '數字',
            [FieldTypes.EMAIL]: '電子郵件',
            [FieldTypes.PASSWORD]: '密碼',
            [FieldTypes.SELECT]: '下拉選單',
            [FieldTypes.RADIO]: '單選',
            [FieldTypes.CHECKBOX]: '核取方塊',
            [FieldTypes.TOGGLE]: '開關',
            [FieldTypes.DATE]: '日期',
            [FieldTypes.TIME]: '時間',
            [FieldTypes.DATETIME]: '日期時間',
            [FieldTypes.RICHTEXT]: '富文本',
            [FieldTypes.CANVAS]: '畫布',
            [FieldTypes.COLOR]: '顏色',
            [FieldTypes.IMAGE]: '圖片',
            [FieldTypes.FILE]: '檔案',
            [FieldTypes.GEOLOCATION]: '位置',
            [FieldTypes.WEATHER]: '天氣',
            [FieldTypes.HIDDEN]: '隱藏'
        };
        return labels[type] || type;
    }

    _buildDefinition() {
        const { pageInfo, fields, selectedComponents, apiConfig, behaviors } = this._data;

        // 收集自動推斷的元件
        const inferredComponents = new Set();
        for (const field of fields) {
            const comp = ComponentMapping[field.type];
            if (comp) inferredComponents.add(comp);
        }

        // 合併選擇的元件
        const allComponents = [...new Set([...selectedComponents, ...inferredComponents])];

        const definition = {
            name: pageInfo.name.endsWith('Page') ? pageInfo.name : `${pageInfo.name}Page`,
            type: pageInfo.type,
            description: pageInfo.description || pageInfo.name,
            components: allComponents,
            fields: fields,
            styles: {
                layout: 'single',
                theme: 'default'
            }
        };

        // API 設定
        if (apiConfig.baseEndpoint) {
            definition.api = {
                get: apiConfig.baseEndpoint,
                create: apiConfig.baseEndpoint,
                update: apiConfig.baseEndpoint,
                delete: apiConfig.baseEndpoint
            };

            if (!apiConfig.enableCrud) {
                delete definition.api.delete;
            }
        }

        // 行為設定
        const behaviorConfig = {};
        if (behaviors.onInit) behaviorConfig.onInit = behaviors.onInit;
        if (behaviors.onSave) behaviorConfig.onSave = behaviors.onSave;
        if (behaviors.onDelete) behaviorConfig.onDelete = behaviors.onDelete;

        if (Object.keys(behaviorConfig).length > 0) {
            definition.behaviors = behaviorConfig;
        }

        return definition;
    }

    events() {
        return {
            'input .form-group input': 'onInput',
            'change input[type="radio"]': 'onRadioChange',
            'change input[type="checkbox"]': 'onCheckboxChange',
            'click [data-action="prev-step"]': 'onPrevStep',
            'click [data-action="next-step"]': 'onNextStep',
            'click [data-action="add-field"]': 'onAddField',
            'click [data-action="edit-field"]': 'onEditField',
            'click [data-action="delete-field"]': 'onDeleteField',
            'click [data-action="move-field-up"]': 'onMoveFieldUp',
            'click [data-action="move-field-down"]': 'onMoveFieldDown',
            'click [data-action="template-basic"]': 'onTemplateBasic',
            'click [data-action="template-user"]': 'onTemplateUser',
            'click [data-action="template-content"]': 'onTemplateContent',
            'click [data-action="generate"]': 'onGenerate',
            'click [data-action="copy-code"]': 'onCopyCode',
            'click [data-action="download-code"]': 'onDownloadCode',
            'click [data-action="clear-error"]': 'onClearError',
            'click .type-option': 'onTypeSelect'
        };
    }

    onInput(event) {
        const { name, value } = event.target;
        const { currentStep } = this._data;

        if (currentStep === 1) {
            this._data.pageInfo[name] = value;
        } else if (currentStep === 4) {
            if (name === 'baseEndpoint') {
                this._data.apiConfig.baseEndpoint = value;
            } else {
                this._data.behaviors[name] = value;
            }
        }
    }

    onRadioChange(event) {
        if (event.target.name === 'pageType') {
            this._data.pageInfo.type = event.target.value;
            this._scheduleUpdate();
        }
    }

    onCheckboxChange(event) {
        if (event.target.name === 'component') {
            const { selectedComponents } = this._data;
            const value = event.target.value;

            if (event.target.checked) {
                if (!selectedComponents.includes(value)) {
                    selectedComponents.push(value);
                }
            } else {
                const index = selectedComponents.indexOf(value);
                if (index > -1) {
                    selectedComponents.splice(index, 1);
                }
            }
            this._scheduleUpdate();
        } else if (event.target.name === 'enableCrud') {
            this._data.apiConfig.enableCrud = event.target.checked;
        }
    }

    onTypeSelect(event) {
        const typeOption = event.target.closest('.type-option');
        if (typeOption) {
            const type = typeOption.dataset.type;
            this._data.pageInfo.type = type;
            this._scheduleUpdate();
        }
    }

    onPrevStep() {
        if (this._data.currentStep > 1) {
            this._data.currentStep--;
            this._scheduleUpdate();
        }
    }

    onNextStep() {
        // 驗證當前步驟
        if (!this._validateCurrentStep()) {
            return;
        }

        if (this._data.currentStep < this._data.totalSteps) {
            this._data.currentStep++;
            this._scheduleUpdate();
        }
    }

    _validateCurrentStep() {
        const { currentStep, pageInfo, fields } = this._data;

        switch (currentStep) {
            case 1:
                if (!pageInfo.name) {
                    this._data.error = '請輸入頁面名稱';
                    this._scheduleUpdate();
                    return false;
                }
                if (!/^[A-Z][a-zA-Z0-9]*$/.test(pageInfo.name)) {
                    this._data.error = '頁面名稱必須是 PascalCase 格式';
                    this._scheduleUpdate();
                    return false;
                }
                break;

            case 2:
                if (fields.length === 0) {
                    this._data.error = '請至少定義一個欄位';
                    this._scheduleUpdate();
                    return false;
                }
                break;
        }

        this._data.error = null;
        return true;
    }

    onAddField() {
        const fieldName = prompt('請輸入欄位名稱 (英文):');
        if (!fieldName) return;

        const fieldLabel = prompt('請輸入欄位標籤 (中文):') || fieldName;

        // 顯示類型選擇
        const typeOptions = Object.entries(FieldTypes)
            .map(([key, value], index) => `${index + 1}. ${this._getFieldTypeLabel(value)}`)
            .join('\n');

        const typeIndex = parseInt(prompt(`請選擇欄位類型:\n${typeOptions}`)) - 1;
        const typeValue = Object.values(FieldTypes)[typeIndex];

        if (!typeValue) {
            this.showMessage('無效的類型選擇', 'error');
            return;
        }

        const required = confirm('此欄位是否必填?');

        this._data.fields.push({
            name: fieldName,
            type: typeValue,
            label: fieldLabel,
            required
        });

        this._scheduleUpdate();
    }

    onEditField(event) {
        const index = parseInt(event.target.dataset.index);
        const field = this._data.fields[index];

        const newName = prompt('欄位名稱:', field.name);
        if (newName) field.name = newName;

        const newLabel = prompt('欄位標籤:', field.label);
        if (newLabel) field.label = newLabel;

        field.required = confirm('此欄位是否必填?');

        this._scheduleUpdate();
    }

    onDeleteField(event) {
        const index = parseInt(event.target.dataset.index);
        if (confirm('確定要刪除此欄位嗎?')) {
            this._data.fields.splice(index, 1);
            this._scheduleUpdate();
        }
    }

    onMoveFieldUp(event) {
        const index = parseInt(event.target.dataset.index);
        if (index > 0) {
            const fields = this._data.fields;
            [fields[index - 1], fields[index]] = [fields[index], fields[index - 1]];
            this._scheduleUpdate();
        }
    }

    onMoveFieldDown(event) {
        const index = parseInt(event.target.dataset.index);
        const fields = this._data.fields;
        if (index < fields.length - 1) {
            [fields[index], fields[index + 1]] = [fields[index + 1], fields[index]];
            this._scheduleUpdate();
        }
    }

    onTemplateBasic() {
        this._data.fields = [
            { name: 'title', type: FieldTypes.TEXT, label: '標題', required: true },
            { name: 'description', type: FieldTypes.TEXTAREA, label: '描述', required: false }
        ];
        this._scheduleUpdate();
    }

    onTemplateUser() {
        this._data.fields = [
            { name: 'name', type: FieldTypes.TEXT, label: '姓名', required: true },
            { name: 'email', type: FieldTypes.EMAIL, label: '電子郵件', required: true },
            { name: 'phone', type: FieldTypes.TEXT, label: '電話', required: false },
            { name: 'isActive', type: FieldTypes.TOGGLE, label: '啟用', required: false, default: true }
        ];
        this._scheduleUpdate();
    }

    onTemplateContent() {
        this._data.fields = [
            { name: 'title', type: FieldTypes.TEXT, label: '標題', required: true },
            { name: 'content', type: FieldTypes.RICHTEXT, label: '內容', required: true },
            { name: 'coverImage', type: FieldTypes.IMAGE, label: '封面圖片', required: false },
            { name: 'publishDate', type: FieldTypes.DATE, label: '發布日期', required: false }
        ];
        this._scheduleUpdate();
    }

    async onGenerate() {
        this._data.generating = true;
        this._data.error = null;
        this._scheduleUpdate();

        try {
            const definition = this._buildDefinition();

            // 呼叫後端生成 API
            const response = await this.api.post('/generator/page-definition', {
                definition
            });

            this._data.generatedCode = response.data?.code || response.code;
            this.showMessage('程式碼生成成功!', 'success');

        } catch (error) {
            // 如果後端 API 不存在，使用前端模擬生成
            console.warn('後端 API 不可用，使用前端模擬生成');
            this._data.generatedCode = this._generateCodeFrontend();
            this.showMessage('程式碼生成成功 (前端模擬)!', 'success');
        } finally {
            this._data.generating = false;
            this._scheduleUpdate();
        }
    }

    _generateCodeFrontend() {
        const definition = this._buildDefinition();

        // 簡化的前端生成邏輯
        let code = `/**
 * ${definition.name} - ${definition.description}
 *
 * 頁面類型: ${definition.type}
 * 生成時間: ${new Date().toISOString()}
 *
 * @module ${definition.name}
 */

import { BasePage } from '../core/BasePage.js';
`;

        // 添加元件 imports
        for (const comp of definition.components) {
            code += `// import { ${comp} } from '../components/...';\n`;
        }

        code += `
export class ${definition.name} extends BasePage {
    async onInit() {
        this._data = {
            form: {
${definition.fields.map(f => `                ${f.name}: ${f.default !== undefined ? JSON.stringify(f.default) : "''"}`).join(',\n')}
            },
            loading: false,
            submitting: false,
            error: null
        };
    }

    template() {
        const { form, loading, submitting, error } = this._data;

        return \`
            <div class="${definition.name.toLowerCase().replace(/page$/, '-page')}">
                <header class="page-header">
                    <h1>${definition.description}</h1>
                </header>

                \${error ? \`
                    <div class="alert alert-error">
                        <p>\${this.esc(error)}</p>
                    </div>
                \` : ''}

                <form id="main-form" class="form-container">
${definition.fields.map(f => `                    <!-- ${f.label} -->
                    <div class="form-group">
                        <label for="${f.name}">${f.label}${f.required ? ' *' : ''}</label>
                        <input type="${f.type === 'email' ? 'email' : f.type === 'number' ? 'number' : 'text'}"
                               id="${f.name}" name="${f.name}"
                               value="\${this.escAttr(form.${f.name})}"
                               ${f.required ? 'required' : ''}>
                    </div>`).join('\n')}

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
        // TODO: 實作儲存邏輯
        this.showMessage('儲存成功!', 'success');
    }
}

export default ${definition.name};
`;

        return code;
    }

    onCopyCode() {
        const { generatedCode } = this._data;
        if (generatedCode) {
            navigator.clipboard.writeText(generatedCode).then(() => {
                this.showMessage('已複製到剪貼簿', 'success');
            }).catch(() => {
                this.showMessage('複製失敗', 'error');
            });
        }
    }

    onDownloadCode() {
        const { generatedCode, pageInfo } = this._data;
        if (generatedCode) {
            const fileName = `${pageInfo.name}Page.js`;
            const blob = new Blob([generatedCode], { type: 'text/javascript' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            a.click();
            URL.revokeObjectURL(url);
            this.showMessage(`已下載 ${fileName}`, 'success');
        }
    }

    onClearError() {
        this._data.error = null;
        this._scheduleUpdate();
    }
}

export default PageDefinitionEditorPage;
