/**
 * ChainedInput - 相依輸入基底元件
 * 前一個欄位有值後，下一個欄位才能操作
 */
import Locale from '../../i18n/index.js';


export class ChainedInput {
    /**
     * @param {Object} options
     * @param {Array} options.fields - 欄位定義陣列
     * @param {Function} options.onChange - 值變更回調
     * @param {string} options.layout - 布局方式 'horizontal' | 'vertical'
     * @param {string} options.gap - 欄位間距
     */
    constructor(options = {}) {
        this.options = {
            fields: [],
            onChange: null,
            layout: 'horizontal',
            gap: '12px',
            ...options
        };

        this.values = {};
        this.fieldElements = new Map();
        this.element = this._createElement();

        // 初始化後觸發第一個欄位的載入
        this._initializeFields();
    }

    _createElement() {
        const container = document.createElement('div');
        container.className = 'chained-input';
        container.style.cssText = `
            display: flex;
            flex-direction: ${this.options.layout === 'vertical' ? 'column' : 'row'};
            gap: ${this.options.gap};
            align-items: flex-end;
            flex-wrap: wrap;
        `;

        this.options.fields.forEach((field, index) => {
            const fieldWrapper = this._createField(field, index);
            container.appendChild(fieldWrapper);
            this.fieldElements.set(field.name, {
                wrapper: fieldWrapper,
                input: fieldWrapper.querySelector('[data-chained-input]'),
                field
            });
        });

        return container;
    }

    _createField(field, index) {
        const wrapper = document.createElement('div');
        wrapper.className = 'chained-input__field';
        wrapper.setAttribute('data-field-name', field.name);
        wrapper.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 4px;
            min-width: ${field.minWidth || '120px'};
            ${field.flex ? `flex: ${field.flex};` : ''}
        `;

        // 標籤 (永遠顯示，即使沒有 label 也佔位)
        const label = document.createElement('label');
        label.className = 'chained-input__label';
        label.style.cssText = `
            font-size: var(--cl-font-size-md);
            font-weight: 500;
            color: var(--cl-text);
            min-height: 20px;
            display: block;
        `;
        if (field.label) {
            label.textContent = field.label;
            if (field.required) {
                const asterisk = document.createElement('span');
                asterisk.textContent = ' *';
                asterisk.style.color = 'var(--cl-danger)';
                label.appendChild(asterisk);
            }
        }
        wrapper.appendChild(label);

        // 輸入元素
        const input = this._createInputElement(field, index);
        wrapper.appendChild(input);

        return wrapper;
    }

    _createInputElement(field, index) {
        let input;
        const isDisabled = index > 0; // 第一個以外都先禁用

        const baseStyle = `
            height: 40px;
            padding: 0 12px;
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-md);
            font-size: var(--cl-font-size-lg);
            font-family: inherit;
            transition: all var(--cl-transition);
            outline: none;
            background: ${isDisabled ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)'};
            box-sizing: border-box;
        `;

        switch (field.type) {
            case 'select':
                input = document.createElement('select');
                input.style.cssText = baseStyle + 'cursor: pointer; min-width: 100px; width: 100%;';

                // 預設選項
                const defaultOpt = document.createElement('option');
                defaultOpt.value = '';
                defaultOpt.textContent = field.placeholder || Locale.t('chainedInput.placeholder');
                input.appendChild(defaultOpt);

                // 如果有靜態選項
                if (field.options) {
                    field.options.forEach(opt => {
                        const option = document.createElement('option');
                        if (typeof opt === 'object') {
                            option.value = opt.value;
                            option.textContent = opt.label;
                        } else {
                            option.value = opt;
                            option.textContent = opt;
                        }
                        input.appendChild(option);
                    });
                }
                break;

            case 'checkbox':
                const checkWrapper = document.createElement('div');
                checkWrapper.style.cssText = 'display: flex; align-items: center; gap: 8px; padding: 8px 0;';

                input = document.createElement('input');
                input.type = 'checkbox';
                input.style.cssText = 'width: 18px; height: 18px; cursor: pointer;';

                const checkLabel = document.createElement('span');
                checkLabel.textContent = field.checkboxLabel || Locale.t('chainedInput.checkboxYes');
                checkLabel.style.cssText = 'font-size: var(--cl-font-size-lg); color: var(--cl-text); cursor: pointer;';
                checkLabel.addEventListener('click', () => {
                    if (!input.disabled) {
                        input.checked = !input.checked;
                        input.dispatchEvent(new Event('change'));
                    }
                });

                checkWrapper.appendChild(input);
                checkWrapper.appendChild(checkLabel);

                // 返回包裝器而非 input
                checkWrapper.querySelector = () => input;
                input.setAttribute('data-chained-input', '');
                input.disabled = isDisabled;
                input.addEventListener('change', () => this._handleFieldChange(field.name, input.checked));
                input.addEventListener('focus', () => {
                    if (!input.disabled) input.style.borderColor = 'var(--cl-primary)';
                });
                input.addEventListener('blur', () => {
                    input.style.borderColor = 'var(--cl-border)';
                });
                return checkWrapper;

            case 'roc-date':
                input = document.createElement('input');
                input.type = 'text';
                input.style.cssText = baseStyle;
                input.placeholder = 'YYY/MM/DD';
                input.maxLength = 9; // e.g. 113/01/01
                // 簡單自動格式化: 輸入 7 碼數字自動加斜線? 這裡先保持純文字
                input.addEventListener('input', (e) => {
                    let val = e.target.value.replaceAll(/[^\d/]/g, '');
                    e.target.value = val;
                });
                break;

            case 'date':
                input = document.createElement('input');
                input.type = 'date';
                input.style.cssText = baseStyle;
                break;

            case 'time':
                input = document.createElement('input');
                input.type = 'time';
                input.style.cssText = baseStyle;
                break;

            case 'number':
                input = document.createElement('input');
                input.type = 'number';
                input.style.cssText = baseStyle;
                if (field.min !== undefined) input.min = field.min;
                if (field.max !== undefined) input.max = field.max;
                break;

            case 'text':
            default:
                input = document.createElement('input');
                input.type = 'text';
                input.style.cssText = baseStyle;
                if (field.maxLength) input.maxLength = field.maxLength;
                break;
        }

        input.setAttribute('data-chained-input', '');
        input.placeholder = field.placeholder || '';
        input.disabled = isDisabled;

        // 事件監聽
        input.addEventListener('change', () => {
            const value = field.type === 'checkbox' ? input.checked : input.value;
            this._handleFieldChange(field.name, value);
        });

        input.addEventListener('focus', () => {
            if (!input.disabled) {
                input.style.borderColor = 'var(--cl-primary)';
                input.style.boxShadow = '0 0 0 3px rgba(var(--cl-primary-rgb), 0.1)';
            }
        });

        input.addEventListener('blur', () => {
            input.style.borderColor = 'var(--cl-border)';
            input.style.boxShadow = 'none';
        });

        return input;
    }

    async _initializeFields() {
        const firstField = this.options.fields[0];
        if (firstField && firstField.loadOptions) {
            await this._loadFieldOptions(firstField.name, '');
        }
    }

    async _handleFieldChange(fieldName, value) {
        this.values[fieldName] = value;

        // 找到當前欄位的索引
        const fieldIndex = this.options.fields.findIndex(f => f.name === fieldName);

        // 重置後續欄位
        for (let i = fieldIndex + 1; i < this.options.fields.length; i++) {
            const nextField = this.options.fields[i];
            const nextElement = this.fieldElements.get(nextField.name);

            if (nextElement) {
                const input = nextElement.input;
                if (nextField.type === 'checkbox') {
                    input.checked = false;
                } else if (nextField.type === 'select') {
                    // 清空選項（保留預設）
                    while (input.options.length > 1) {
                        input.remove(1);
                    }
                    input.value = '';
                } else {
                    input.value = '';
                }

                // 禁用
                input.disabled = true;
                input.style.background = 'var(--cl-bg-secondary)';

                // 隱藏（如果配置了 hideWhenDisabled）
                if (nextField.hideWhenDisabled) {
                    nextElement.wrapper.style.display = 'none';
                }

                this.values[nextField.name] = nextField.type === 'checkbox' ? false : '';
            }
        }

        // 判斷是否有值
        const hasValue = field => {
            const f = this.options.fields.find(x => x.name === fieldName);
            if (f.type === 'checkbox') return value === true;
            return value !== '' && value !== null && value !== undefined;
        };

        // 啟用下一個欄位
        if (hasValue() && fieldIndex < this.options.fields.length - 1) {
            const nextField = this.options.fields[fieldIndex + 1];
            const nextElement = this.fieldElements.get(nextField.name);

            if (nextElement) {
                const input = nextElement.input;

                // 載入選項（如果需要）
                if (nextField.loadOptions) {
                    await this._loadFieldOptions(nextField.name, value);
                }

                // 啟用
                input.disabled = false;
                input.style.background = 'var(--cl-bg)';

                // 顯示
                if (nextField.hideWhenDisabled) {
                    nextElement.wrapper.style.display = '';
                }
            }
        }

        // 觸發 onChange
        if (this.options.onChange) {
            this.options.onChange(this.getValues());
        }
    }

    async _loadFieldOptions(fieldName, parentValue) {
        const field = this.options.fields.find(f => f.name === fieldName);
        const element = this.fieldElements.get(fieldName);

        if (!field || !element || !field.loadOptions) return;

        const input = element.input;

        // 顯示載入中
        if (field.type === 'select') {
            input.disabled = true;
            const loadingOpt = input.options[0];
            loadingOpt.textContent = Locale.t('chainedInput.loading');
        }

        try {
            const options = await field.loadOptions(parentValue);

            if (field.type === 'select') {
                // 清空現有選項
                while (input.options.length > 1) {
                    input.remove(1);
                }

                // 新增選項
                options.forEach(opt => {
                    const option = document.createElement('option');
                    if (typeof opt === 'object') {
                        option.value = opt.value;
                        option.textContent = opt.label;
                    } else {
                        option.value = opt;
                        option.textContent = opt;
                    }
                    input.appendChild(option);
                });

                // 恢復預設文字
                input.options[0].textContent = field.placeholder || Locale.t('chainedInput.placeholder');

                // 如果沒有選項，保持禁用；否則啟用
                if (options.length === 0) {
                    input.disabled = true;
                    input.options[0].textContent = Locale.t('chainedInput.noOptions');
                    if (field.hideWhenEmpty) {
                        element.wrapper.style.display = 'none';
                    }
                } else {
                    input.disabled = false;
                    element.wrapper.style.display = '';
                }
            }
        } catch (error) {
            console.error(`載入 ${fieldName} 選項失敗:`, error);
            if (field.type === 'select') {
                input.options[0].textContent = Locale.t('chainedInput.loadError');
            }
        }
    }

    /**
     * 取得所有值
     */
    getValues() {
        return { ...this.values };
    }

    /**
     * 設定值
     */
    async setValues(values) {
        for (const [fieldName, value] of Object.entries(values)) {
            const element = this.fieldElements.get(fieldName);
            const field = this.options.fields.find(f => f.name === fieldName);

            if (element && field) {
                const input = element.input;
                if (field.type === 'checkbox') {
                    input.checked = value;
                } else {
                    input.value = value;
                }
                await this._handleFieldChange(fieldName, value);
            }
        }
    }

    /**
     * 重置
     */
    reset() {
        this.options.fields.forEach((field, index) => {
            const element = this.fieldElements.get(field.name);
            if (element) {
                const input = element.input;
                if (field.type === 'checkbox') {
                    input.checked = false;
                } else {
                    input.value = '';
                }

                if (index > 0) {
                    input.disabled = true;
                    input.style.background = 'var(--cl-bg-secondary)';
                }
            }
            this.values[field.name] = field.type === 'checkbox' ? false : '';
        });

        this._initializeFields();
    }

    /**
     * 驗證
     */
    validate() {
        const errors = [];
        this.options.fields.forEach(field => {
            if (field.required) {
                const value = this.values[field.name];
                const isEmpty = field.type === 'checkbox' ? !value : (!value || value === '');
                if (isEmpty) {
                    errors.push({ field: field.name, message: `${field.label || field.name} 為必填` });
                }
            }
        });
        return errors;
    }

    /**
     * 掛載
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    /**
     * 移除
     */
    destroy() {
        if (this.element?.parentNode) {
            this.element.remove();
        }
        this.fieldElements.clear();
    }

    /**
     * 靜態方法：綁定自定義相依
     */
    static bindDependency(config) {
        const { source, target, condition = (v) => !!v } = config;

        const sourceEl = typeof source === 'string'
            ? document.querySelector(source)
            : source;
        const targetEl = typeof target === 'string'
            ? document.querySelector(target)
            : target;

        if (!sourceEl || !targetEl) {
            console.error('ChainedInput.bindDependency: source 或 target 未找到');
            return;
        }

        const updateTarget = () => {
            const value = sourceEl.type === 'checkbox' ? sourceEl.checked : sourceEl.value;
            const shouldEnable = condition(value);

            targetEl.disabled = !shouldEnable;
            targetEl.style.background = shouldEnable ? 'var(--cl-bg)' : 'var(--cl-bg-secondary)';

            if (!shouldEnable && targetEl.type !== 'checkbox') {
                targetEl.value = '';
            }
        };

        // 初始觸發
        updateTarget();

        // 監聽變化
        sourceEl.addEventListener('change', updateTarget);
        sourceEl.addEventListener('input', updateTarget);

        return {
            unbind: () => {
                sourceEl.removeEventListener('change', updateTarget);
                sourceEl.removeEventListener('input', updateTarget);
            }
        };
    }
}

export default ChainedInput;
