/**
 * PageGenerator - 頁面生成器核心
 *
 * 根據 PageDefinition 生成實際的頁面程式碼
 * 支援多種輸出模式：
 * - 生成 JavaScript 類別程式碼 (字串)
 * - 生成可直接使用的頁面類別 (運行時)
 *
 * @module PageGenerator
 */

import {
    validateDefinition,
    inferComponents,
    FieldTypes,
    PageTypes,
    ComponentMapping,
    AvailableComponents
} from './PageDefinition.js';

// ============================================================
// 元件路徑映射
// ============================================================

/**
 * 元件的 import 路徑映射
 * @type {Object<string, { spa: string, packages: string }>}
 */
export const ComponentPaths = {
    // UI 元件 - SPA 範本
    DatePicker: {
        spa: '../components/DatePicker/DatePicker.js',
        packages: null
    },
    ColorPicker: {
        spa: '../components/ColorPicker/ColorPicker.js',
        packages: null
    },
    ImageViewer: {
        spa: '../components/ImageViewer/ImageViewer.js',
        packages: null
    },
    ToastPanel: {
        spa: '../components/Panel/ToastPanel.js',
        packages: null
    },
    ModalPanel: {
        spa: '../components/Panel/ModalPanel.js',
        packages: null
    },

    // 服務元件 - SPA 範本
    GeolocationService: {
        spa: '../components/services/GeolocationService.js',
        packages: null
    },
    WeatherService: {
        spa: '../components/services/WeatherService.js',
        packages: null
    },

    // 進階元件 - Packages (路徑對應 packages/javascript/browser/ui_components/)
    WebTextEditor: {
        spa: null,
        packages: '@component-library/ui_components/editor/WebTextEditor/WebTextEditor.js'
    },
    DrawingBoard: {
        spa: null,
        packages: '@component-library/ui_components/viz/DrawingBoard/DrawingBoard.js'
    },
    WebPainter: {
        spa: null,
        packages: '@component-library/ui_components/viz/WebPainter/WebPainter.js'
    },
    BasicButton: {
        spa: null,
        packages: '@component-library/ui_components/common/BasicButton/BasicButton.js'
    },
    EditorButton: {
        spa: null,
        packages: '@component-library/ui_components/common/EditorButton/EditorButton.js'
    },
    ButtonGroup: {
        spa: null,
        packages: '@component-library/ui_components/common/ButtonGroup/ButtonGroup.js'
    },

    // 複合輸入元件 - Packages (路徑對應 packages/javascript/browser/ui_components/input/)
    DateTimeInput: {
        spa: null,
        packages: '@component-library/ui_components/input/DateTimeInput/DateTimeInput.js'
    },
    AddressInput: {
        spa: null,
        packages: '@component-library/ui_components/input/AddressInput/AddressInput.js'
    },
    AddressListInput: {
        spa: null,
        packages: '@component-library/ui_components/input/AddressListInput/AddressListInput.js'
    },
    ChainedInput: {
        spa: null,
        packages: '@component-library/ui_components/input/ChainedInput/ChainedInput.js'
    },
    ListInput: {
        spa: null,
        packages: '@component-library/ui_components/input/ListInput/ListInput.js'
    },
    PersonInfoList: {
        spa: null,
        packages: '@component-library/ui_components/input/PersonInfoList/PersonInfoList.js'
    },
    PhoneListInput: {
        spa: null,
        packages: '@component-library/ui_components/input/PhoneListInput/PhoneListInput.js'
    },
    SocialMediaList: {
        spa: null,
        packages: '@component-library/ui_components/input/SocialMediaList/SocialMediaList.js'
    },
    OrganizationInput: {
        spa: null,
        packages: '@component-library/ui_components/input/OrganizationInput/OrganizationInput.js'
    },
    StudentInput: {
        spa: null,
        packages: '@component-library/ui_components/input/StudentInput/StudentInput.js'
    }
};

// ============================================================
// 欄位渲染器
// ============================================================

/**
 * 欄位 HTML 模板生成器
 * @type {Object<string, Function>}
 */
export const FieldRenderers = {
    // 基本類型
    [FieldTypes.TEXT]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <input type="text" id="${field.name}" name="${field.name}"
                   value="\${this.escAttr(this._data.form.${field.name})}"
                   ${field.required ? 'required' : ''}
                   ${field.validation?.maxLength ? `maxlength="${field.validation.maxLength}"` : ''}>
            ${field.description ? `<small>${field.description}</small>` : ''}
        </div>`,

    [FieldTypes.TEXTAREA]: (field) => `
        <div class="form-group full-width">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <textarea id="${field.name}" name="${field.name}"
                      rows="${field.config?.rows || 5}"
                      ${field.required ? 'required' : ''}>\${this.esc(this._data.form.${field.name})}</textarea>
            ${field.description ? `<small>${field.description}</small>` : ''}
        </div>`,

    [FieldTypes.NUMBER]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <input type="number" id="${field.name}" name="${field.name}"
                   value="\${this.escAttr(this._data.form.${field.name})}"
                   ${field.validation?.min !== undefined ? `min="${field.validation.min}"` : ''}
                   ${field.validation?.max !== undefined ? `max="${field.validation.max}"` : ''}
                   ${field.required ? 'required' : ''}>
        </div>`,

    [FieldTypes.EMAIL]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <input type="email" id="${field.name}" name="${field.name}"
                   value="\${this.escAttr(this._data.form.${field.name})}"
                   ${field.required ? 'required' : ''}>
        </div>`,

    [FieldTypes.PASSWORD]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <input type="password" id="${field.name}" name="${field.name}"
                   ${field.required ? 'required' : ''}>
        </div>`,

    [FieldTypes.SELECT]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <select id="${field.name}" name="${field.name}" ${field.required ? 'required' : ''}>
                <option value="">請選擇</option>
                ${(field.options || []).map(opt => `
                    <option value="${opt.value}" \${this._data.form.${field.name} === '${opt.value}' ? 'selected' : ''}>${opt.label}</option>
                `).join('')}
            </select>
        </div>`,

    [FieldTypes.RADIO]: (field) => `
        <div class="form-group">
            <label>${field.label || field.name}${field.required ? ' *' : ''}</label>
            <div class="radio-group">
                ${(field.options || []).map(opt => `
                    <label class="radio-label">
                        <input type="radio" name="${field.name}" value="${opt.value}"
                               \${this._data.form.${field.name} === '${opt.value}' ? 'checked' : ''}>
                        ${opt.label}
                    </label>
                `).join('')}
            </div>
        </div>`,

    [FieldTypes.CHECKBOX]: (field) => `
        <div class="form-group">
            <label class="checkbox-label">
                <input type="checkbox" id="${field.name}" name="${field.name}"
                       \${this._data.form.${field.name} ? 'checked' : ''}>
                ${field.label || field.name}
            </label>
        </div>`,

    [FieldTypes.TOGGLE]: (field) => `
        <div class="form-group">
            <label class="toggle-label">
                <span>${field.label || field.name}</span>
                <input type="checkbox" id="${field.name}" name="${field.name}"
                       class="toggle-input"
                       \${this._data.form.${field.name} ? 'checked' : ''}>
                <span class="toggle-switch"></span>
            </label>
        </div>`,

    [FieldTypes.DATE]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <div id="${field.name}-picker"></div>
        </div>`,

    [FieldTypes.TIME]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <input type="time" id="${field.name}" name="${field.name}"
                   value="\${this.escAttr(this._data.form.${field.name})}"
                   ${field.required ? 'required' : ''}>
        </div>`,

    [FieldTypes.DATETIME]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <input type="datetime-local" id="${field.name}" name="${field.name}"
                   value="\${this.escAttr(this._data.form.${field.name})}"
                   ${field.required ? 'required' : ''}>
        </div>`,

    [FieldTypes.COLOR]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}</label>
            <div id="${field.name}-picker"></div>
        </div>`,

    [FieldTypes.RICHTEXT]: (field) => `
        <div class="form-group full-width">
            <label for="${field.name}">${field.label || field.name}${field.required ? ' *' : ''}</label>
            <div id="${field.name}-editor"></div>
        </div>`,

    [FieldTypes.CANVAS]: (field) => `
        <div class="form-group full-width">
            <label for="${field.name}">${field.label || field.name}</label>
            <div id="${field.name}-canvas"></div>
        </div>`,

    [FieldTypes.IMAGE]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}</label>
            <div id="${field.name}-viewer"></div>
            <input type="file" id="${field.name}" name="${field.name}"
                   accept="image/*" style="display:none;">
            <button type="button" class="btn btn-secondary" data-action="select-image" data-field="${field.name}">
                選擇圖片
            </button>
        </div>`,

    [FieldTypes.FILE]: (field) => `
        <div class="form-group">
            <label for="${field.name}">${field.label || field.name}</label>
            <input type="file" id="${field.name}" name="${field.name}"
                   ${field.config?.accept ? `accept="${field.config.accept}"` : ''}
                   ${field.config?.multiple ? 'multiple' : ''}>
        </div>`,

    [FieldTypes.GEOLOCATION]: (field) => `
        <div class="form-group full-width">
            <label>${field.label || '位置資訊'}</label>
            <div class="location-display">
                <span id="${field.name}-display">\${this._data.${field.name}?.address?.shortName || '尚未定位'}</span>
                <button type="button" class="btn btn-secondary btn-sm" data-action="get-location" data-field="${field.name}">
                    取得位置
                </button>
            </div>
        </div>`,

    [FieldTypes.WEATHER]: (field) => `
        <div class="form-group full-width">
            <label>${field.label || '天氣資訊'}</label>
            <div class="weather-display" id="${field.name}-display">
                \${this._data.${field.name} ? \`
                    <span class="weather-icon">\${this._data.${field.name}.icon}</span>
                    <span class="weather-temp">\${this._data.${field.name}.temperature}\${this._data.${field.name}.unit}</span>
                    <span class="weather-desc">\${this._data.${field.name}.description}</span>
                \` : '需要先取得位置'}
            </div>
        </div>`,

    [FieldTypes.HIDDEN]: (field) => `
        <input type="hidden" id="${field.name}" name="${field.name}"
               value="\${this.escAttr(this._data.form.${field.name})}">`
};

// ============================================================
// PageGenerator 類別
// ============================================================

export class PageGenerator {
    /**
     * @param {Object} options
     * @param {string} options.baseImportPath - 基礎 import 路徑
     * @param {boolean} options.usePackages - 是否使用 packages 元件
     */
    constructor(options = {}) {
        this.baseImportPath = options.baseImportPath || '../core/BasePage.js';
        this.usePackages = options.usePackages || false;
    }

    /**
     * 從頁面定義生成程式碼
     * @param {PageDefinition} definition - 頁面定義
     * @returns {{ code: string, errors: string[] }}
     */
    generate(definition) {
        // 驗證定義
        const validation = validateDefinition(definition);
        if (!validation.valid) {
            return {
                code: null,
                errors: validation.errors
            };
        }

        // 推斷需要的元件
        const inferredComponents = inferComponents(definition.fields || []);
        const allComponents = [
            ...new Set([
                ...(definition.components || []).map(c => typeof c === 'string' ? c : c.name),
                ...inferredComponents
            ])
        ];

        // 生成程式碼
        const code = this._generateCode(definition, allComponents);

        return {
            code,
            errors: []
        };
    }

    /**
     * 生成完整的頁面程式碼
     * @private
     */
    _generateCode(definition, components) {
        const imports = this._generateImports(components);
        const className = definition.name;
        const pageType = definition.type;

        let code = `/**
 * ${className} - ${definition.description || '自動生成的頁面'}
 *
 * 頁面類型: ${pageType}
 * 生成時間: ${new Date().toISOString()}
 *
 * @module ${className}
 */

${imports}

export class ${className} extends BasePage {
`;

        // 生成 onInit
        code += this._generateOnInit(definition);

        // 生成 template
        code += this._generateTemplate(definition);

        // 生成 events
        code += this._generateEvents(definition);

        // 生成元件初始化 (onMounted)
        if (this._needsComponentInit(definition)) {
            code += this._generateOnMounted(definition);
        }

        // 生成事件處理器
        code += this._generateEventHandlers(definition);

        // 生成服務方法
        code += this._generateServiceMethods(definition);

        // 生成 API 方法
        if (definition.api) {
            code += this._generateApiMethods(definition);
        }

        // 生成自訂行為 stub 方法
        code += this._generateBehaviorStubs(definition);

        code += `}

export default ${className};
`;

        return code;
    }

    /**
     * 生成 import 語句
     * @private
     */
    _generateImports(components) {
        const imports = [`import { BasePage } from '${this.baseImportPath}';`];

        for (const comp of components) {
            const paths = ComponentPaths[comp];
            if (!paths) {
                imports.push(`// TODO: 未知元件 ${comp}，請手動添加 import`);
                continue;
            }

            const path = this.usePackages && paths.packages
                ? paths.packages
                : paths.spa;

            if (path) {
                imports.push(`import { ${comp} } from '${path}';`);
            } else {
                imports.push(`// TODO: 元件 ${comp} 在當前模式下不可用`);
            }
        }

        return imports.join('\n');
    }

    /**
     * 生成 onInit 方法
     * @private
     */
    _generateOnInit(definition) {
        const formDefaults = {};

        for (const field of definition.fields || []) {
            if (field.default !== undefined) {
                formDefaults[field.name] = field.default;
            } else {
                // 根據類型設定預設值
                switch (field.type) {
                    case FieldTypes.CHECKBOX:
                    case FieldTypes.TOGGLE:
                        formDefaults[field.name] = false;
                        break;
                    case FieldTypes.NUMBER:
                        formDefaults[field.name] = 0;
                        break;
                    default:
                        formDefaults[field.name] = '';
                }
            }
        }

        let code = `
    async onInit() {
        this._data = {
            form: ${JSON.stringify(formDefaults, null, 16).replace(/\n/g, '\n        ')},
            loading: false,
            submitting: false,
            error: null
        };
`;

        // 初始化服務
        for (const field of definition.fields || []) {
            if (field.type === FieldTypes.GEOLOCATION) {
                code += `
        // 初始化地理位置服務
        this._geoService = new GeolocationService();
`;
            }
            if (field.type === FieldTypes.WEATHER) {
                code += `
        // 初始化天氣服務
        this._weatherService = new WeatherService();
`;
            }
        }

        // 載入初始資料
        if (definition.behaviors?.onInit) {
            code += `
        // 執行初始化行為
        await this._${definition.behaviors.onInit}();
`;
        }

        code += `    }
`;

        return code;
    }

    /**
     * 生成 template 方法
     * @private
     */
    _generateTemplate(definition) {
        const layout = definition.styles?.layout || 'single';

        let code = `
    template() {
        const { form, loading, submitting, error } = this._data;

        return \`
            <div class="${this._toKebabCase(definition.name)}">
                <header class="page-header">
                    <h1>${definition.description || definition.name.replace(/Page$/, '')}</h1>
                </header>

                \${error ? \`
                    <div class="alert alert-error">
                        <p>\${this.esc(error)}</p>
                    </div>
                \` : ''}

                <form id="main-form" class="form-container">
`;

        // 根據佈局生成欄位
        if (layout === 'two-column') {
            code += `                    <div class="form-grid">\n`;
        }

        for (const field of definition.fields || []) {
            const renderer = FieldRenderers[field.type];
            if (renderer) {
                code += renderer(field).split('\n').map(line => '                    ' + line).join('\n') + '\n';
            } else {
                code += `                    <!-- TODO: 未知欄位類型 ${field.type} -->\n`;
            }
        }

        if (layout === 'two-column') {
            code += `                    </div>\n`;
        }

        code += `
                    <div class="form-actions">
                        <button type="submit" class="btn btn-primary" \${submitting ? 'disabled' : ''}>
                            \${submitting ? '處理中...' : '${definition.type === PageTypes.FORM ? '儲存' : '送出'}'}
                        </button>
                    </div>
                </form>
            </div>
        \`;
    }
`;

        return code;
    }

    /**
     * 生成 events 方法
     * @private
     */
    _generateEvents(definition) {
        const events = {
            'submit #main-form': 'onSubmit',
            'input .form-group input': 'onInput',
            'input .form-group textarea': 'onInput',
            'change .form-group select': 'onInput'
        };

        // 添加欄位特定事件
        for (const field of definition.fields || []) {
            if (field.type === FieldTypes.GEOLOCATION) {
                events[`click [data-action="get-location"][data-field="${field.name}"]`] = `onGetLocation_${field.name}`;
            }
            if (field.type === FieldTypes.IMAGE) {
                events[`click [data-action="select-image"][data-field="${field.name}"]`] = `onSelectImage_${field.name}`;
                events[`change #${field.name}`] = `onImageSelected_${field.name}`;
            }
        }

        // 添加自訂觸發器
        if (definition.behaviors?.fieldTriggers) {
            for (const [fieldName, trigger] of Object.entries(definition.behaviors.fieldTriggers)) {
                events[`change #${fieldName}`] = `onFieldChange_${fieldName}`;
            }
        }

        return `
    events() {
        return ${JSON.stringify(events, null, 12).replace(/\n/g, '\n        ')};
    }
`;
    }

    /**
     * 判斷是否需要元件初始化
     * @private
     */
    _needsComponentInit(definition) {
        const componentFields = [
            FieldTypes.DATE,
            FieldTypes.COLOR,
            FieldTypes.RICHTEXT,
            FieldTypes.CANVAS,
            FieldTypes.IMAGE
        ];

        return (definition.fields || []).some(f => componentFields.includes(f.type));
    }

    /**
     * 生成 onMounted 方法
     * @private
     */
    _generateOnMounted(definition) {
        let code = `
    async onMounted() {
`;

        for (const field of definition.fields || []) {
            switch (field.type) {
                case FieldTypes.DATE:
                    code += `
        // 初始化日期選擇器: ${field.name}
        this._${field.name}Picker = new DatePicker(this.$('#${field.name}-picker'), {
            value: this._data.form.${field.name},
            onChange: (date) => {
                this._data.form.${field.name} = date;
            }
        });
`;
                    break;

                case FieldTypes.COLOR:
                    code += `
        // 初始化顏色選擇器: ${field.name}
        this._${field.name}Picker = new ColorPicker(this.$('#${field.name}-picker'), {
            value: this._data.form.${field.name},
            onChange: (color) => {
                this._data.form.${field.name} = color;
            }
        });
`;
                    break;

                case FieldTypes.RICHTEXT:
                    code += `
        // 初始化富文本編輯器: ${field.name}
        this._${field.name}Editor = new WebTextEditor({
            container: this.$('#${field.name}-editor'),
            content: this._data.form.${field.name},
            onChange: (content) => {
                this._data.form.${field.name} = content;
            }
        });
`;
                    break;

                case FieldTypes.CANVAS:
                    code += `
        // 初始化畫布: ${field.name}
        this._${field.name}Canvas = new DrawingBoard(this.$('#${field.name}-canvas'), {
            width: ${field.config?.width || 600},
            height: ${field.config?.height || 400},
            onChange: (data) => {
                this._data.form.${field.name} = data;
            }
        });
`;
                    break;

                case FieldTypes.IMAGE:
                    code += `
        // 初始化圖片檢視器: ${field.name}
        this._${field.name}Viewer = new ImageViewer(this.$('#${field.name}-viewer'), {
            src: this._data.form.${field.name}
        });
`;
                    break;
            }
        }

        code += `    }
`;

        return code;
    }

    /**
     * 生成事件處理器
     * @private
     */
    _generateEventHandlers(definition) {
        let code = `
    onInput(event) {
        const { name, value, type, checked } = event.target;
        this._data.form[name] = type === 'checkbox' ? checked : value;
    }

    async onSubmit(event) {
        event.preventDefault();

        // 驗證
        if (!this._validate()) {
            return;
        }

        this._data.submitting = true;
        this._data.error = null;
        this._scheduleUpdate();

        try {
            await this._save();
            this.showMessage('儲存成功!', 'success');
${definition.behaviors?.onSave ? `
            // 執行儲存後行為
            await this._${definition.behaviors.onSave}();
` : ''}
        } catch (error) {
            this._data.error = error.message || '操作失敗';
            this.showMessage('操作失敗', 'error');
        } finally {
            this._data.submitting = false;
            this._scheduleUpdate();
        }
    }

    _validate() {
        const { form } = this._data;
`;

        // 生成驗證邏輯
        for (const field of definition.fields || []) {
            if (field.required) {
                code += `
        if (!form.${field.name}) {
            this._data.error = '請填寫${field.label || field.name}';
            this._scheduleUpdate();
            return false;
        }
`;
            }

            if (field.validation?.pattern) {
                code += `
        if (form.${field.name} && !/${field.validation.pattern}/.test(form.${field.name})) {
            this._data.error = '${field.label || field.name}格式不正確';
            this._scheduleUpdate();
            return false;
        }
`;
            }
        }

        code += `
        return true;
    }
`;

        // 生成欄位特定處理器
        for (const field of definition.fields || []) {
            if (field.type === FieldTypes.GEOLOCATION) {
                code += `
    async onGetLocation_${field.name}() {
        try {
            this.$('#${field.name}-display').textContent = '定位中...';
            const location = await this._geoService.getLocationInfo();
            this._data.${field.name} = location;
            this._data.form.${field.name} = \`\${location.latitude},\${location.longitude}\`;
            this._scheduleUpdate();
`;
                // 如果有天氣欄位且依賴此位置
                const weatherFields = (definition.fields || []).filter(f =>
                    f.type === FieldTypes.WEATHER && f.dependsOn === field.name
                );
                for (const wf of weatherFields) {
                    code += `
            // 自動取得天氣
            await this._getWeather_${wf.name}(location.latitude, location.longitude);
`;
                }
                code += `
        } catch (error) {
            this.showMessage(error.message || '定位失敗', 'error');
        }
    }
`;
            }

            if (field.type === FieldTypes.IMAGE) {
                code += `
    onSelectImage_${field.name}() {
        this.$('#${field.name}').click();
    }

    onImageSelected_${field.name}(event) {
        const file = event.target.files[0];
        if (file) {
            const reader = new FileReader();
            reader.onload = (e) => {
                this._data.form.${field.name} = e.target.result;
                if (this._${field.name}Viewer) {
                    this._${field.name}Viewer.setSrc(e.target.result);
                }
            };
            reader.readAsDataURL(file);
        }
    }
`;
            }
        }

        return code;
    }

    /**
     * 生成服務方法
     * @private
     */
    _generateServiceMethods(definition) {
        let code = '';

        // 天氣服務方法
        for (const field of definition.fields || []) {
            if (field.type === FieldTypes.WEATHER) {
                code += `
    async _getWeather_${field.name}(latitude, longitude) {
        try {
            const weather = await this._weatherService.getCurrentWeather(latitude, longitude);
            this._data.${field.name} = weather;
            this._scheduleUpdate();
        } catch (error) {
            console.error('[天氣服務] 取得失敗:', error);
        }
    }
`;
            }
        }

        return code;
    }

    /**
     * 生成 API 方法
     * @private
     */
    _generateApiMethods(definition) {
        const api = definition.api;
        let code = '';

        if (api.create || api.update) {
            code += `
    async _save() {
        const data = { ...this._data.form };

        // 判斷是新增還是更新
        const isNew = !this.params.id;
        const endpoint = isNew ? '${api.create || '/api/items'}' : \`${api.update || '/api/items'}/\${this.params.id}\`;
        const method = isNew ? 'post' : 'put';

        const response = await this.api[method](endpoint, data);
        return response;
    }
`;
        } else {
            code += `
    async _save() {
        // TODO: 實作儲存邏輯
        console.log('儲存資料:', this._data.form);
    }
`;
        }

        if (api.get) {
            code += `
    async _loadData() {
        if (!this.params.id) return;

        this._data.loading = true;
        this._scheduleUpdate();

        try {
            const response = await this.api.get(\`${api.get}/\${this.params.id}\`);
            this._data.form = response.data;
        } catch (error) {
            this._data.error = '載入資料失敗';
        } finally {
            this._data.loading = false;
            this._scheduleUpdate();
        }
    }
`;
        }

        if (api.delete) {
            code += `
    async _delete() {
        if (!this.params.id) return;

        const confirmed = await this.confirm({
            title: '確認刪除',
            message: '確定要刪除此項目嗎？此操作無法復原。'
        });

        if (!confirmed) return;

        try {
            await this.api.delete(\`${api.delete}/\${this.params.id}\`);
            this.showMessage('刪除成功', 'success');
${definition.behaviors?.onDelete ? `
            // 執行刪除後行為
            await this._${definition.behaviors.onDelete}();
` : `
            this.navigate(-1);
`}
        } catch (error) {
            this.showMessage('刪除失敗', 'error');
        }
    }
`;
        }

        return code;
    }

    /**
     * 生成自訂行為 stub 方法
     * @private
     */
    _generateBehaviorStubs(definition) {
        let code = '';
        const behaviors = definition.behaviors || {};
        const generatedMethods = new Set();

        // 收集需要生成 stub 的方法名
        const methodsToStub = [];

        if (behaviors.onInit && !generatedMethods.has(behaviors.onInit)) {
            methodsToStub.push({
                name: behaviors.onInit,
                description: '初始化行為',
                isAsync: true
            });
            generatedMethods.add(behaviors.onInit);
        }

        if (behaviors.onSave && !generatedMethods.has(behaviors.onSave)) {
            methodsToStub.push({
                name: behaviors.onSave,
                description: '儲存後行為',
                isAsync: true
            });
            generatedMethods.add(behaviors.onSave);
        }

        if (behaviors.onDelete && !generatedMethods.has(behaviors.onDelete)) {
            methodsToStub.push({
                name: behaviors.onDelete,
                description: '刪除後行為',
                isAsync: true
            });
            generatedMethods.add(behaviors.onDelete);
        }

        // 欄位觸發器
        if (behaviors.fieldTriggers) {
            for (const [fieldName, trigger] of Object.entries(behaviors.fieldTriggers)) {
                if (!generatedMethods.has(trigger)) {
                    methodsToStub.push({
                        name: trigger,
                        description: `欄位 ${fieldName} 的觸發器`,
                        isAsync: true
                    });
                    generatedMethods.add(trigger);
                }
            }
        }

        // 生成 stub
        for (const method of methodsToStub) {
            code += `
    /**
     * ${method.description}
     * TODO: 實作此方法
     */
    ${method.isAsync ? 'async ' : ''}_${method.name}() {
        // TODO: 請實作此方法
        console.log('[${definition.name}] _${method.name} 尚未實作');
    }
`;
        }

        return code;
    }

    /**
     * PascalCase 轉 kebab-case
     * @private
     */
    _toKebabCase(str) {
        return str.replace(/([A-Z])/g, '-$1').toLowerCase().slice(1);
    }
}

export default PageGenerator;
