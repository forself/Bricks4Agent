/**
 * SearchForm - 搜尋表單元件
 *
 * 自動產生搜尋表單，支援多種欄位類型、展開收合、驗證等功能
 *
 * @author MAGI System
 * @version 1.0.0
 */

import { escapeHtml } from '../../utils/security.js';
import { TextInput } from '../TextInput/TextInput.js';
import { Dropdown } from '../Dropdown/Dropdown.js';
import { DatePicker } from '../DatePicker/DatePicker.js';
import { NumberInput } from '../NumberInput/NumberInput.js';

import Locale from '../../i18n/index.js';
export class SearchForm {
    static FIELD_TYPES = {
        TEXT: 'text',
        NUMBER: 'number',
        SELECT: 'select',
        DATE: 'date',
        DATE_RANGE: 'dateRange',
        CHECKBOX: 'checkbox'
    };

    /**
     * @param {Object} options
     * @param {Array} options.fields - 欄位定義 [{key, label, type, placeholder?, options?, defaultValue?, required?, width?}]
     * @param {Object} options.values - 初始值
     * @param {number} options.columns - 每行欄位數
     * @param {boolean} options.collapsible - 是否可收合
     * @param {number} options.visibleRows - 收合時顯示行數
     * @param {boolean} options.showReset - 顯示重設按鈕
     * @param {string} options.searchText - 搜尋按鈕文字
     * @param {string} options.resetText - 重設按鈕文字
     * @param {Function} options.onSearch - 搜尋回調 (values)
     * @param {Function} options.onReset - 重設回調
     * @param {Function} options.onChange - 值變更回調 (key, value, allValues)
     */
    constructor(options = {}) {
        this.options = {
            fields: [],
            values: {},
            columns: 4,
            collapsible: true,
            visibleRows: 1,
            showReset: true,
            searchText: Locale.t('searchForm.searchText'),
            resetText: Locale.t('searchForm.resetText'),
            onSearch: null,
            onReset: null,
            onChange: null,
            ...options
        };

        this._values = { ...this.options.values };
        this._expanded = false;
        this._fieldComponents = new Map();
        this.element = null;

        this._injectStyles();
        this._create();
    }

    _injectStyles() {
        if (document.getElementById('search-form-styles')) return;

        const style = document.createElement('style');
        style.id = 'search-form-styles';
        style.textContent = `
            .search-form {
                background: var(--cl-bg);
                border: 1px solid var(--cl-border-light);
                border-radius: var(--cl-radius-lg);
                padding: 16px;
            }
            .search-form-fields {
                display: grid;
                gap: 16px;
                margin-bottom: 16px;
            }
            .search-form-field {
                display: flex;
                flex-direction: column;
                gap: 4px;
            }
            .search-form-field.hidden {
                display: none;
            }
            .search-form-label {
                font-size: var(--cl-font-size-md);
                font-weight: 500;
                color: var(--cl-text);
            }
            .search-form-label .required {
                color: var(--cl-danger);
                margin-left: 2px;
            }
            .search-form-input {
                min-height: 36px;
            }
            .search-form-date-range {
                display: flex;
                align-items: center;
                gap: 8px;
            }
            .search-form-date-range span {
                color: var(--cl-text-placeholder);
            }
            .search-form-actions {
                display: flex;
                align-items: center;
                gap: 12px;
                padding-top: 16px;
                border-top: 1px solid var(--cl-border-light);
            }
            .search-form-btn {
                padding: 8px 24px;
                border: 1px solid;
                border-radius: var(--cl-radius-sm);
                font-size: var(--cl-font-size-lg);
                cursor: pointer;
                transition: all var(--cl-transition);
            }
            .search-form-btn-primary {
                background: var(--cl-primary);
                border-color: var(--cl-primary);
                color: var(--cl-bg);
            }
            .search-form-btn-primary:hover {
                background: var(--cl-primary-dark);
                border-color: var(--cl-primary-dark);
            }
            .search-form-btn-default {
                background: var(--cl-bg);
                border-color: var(--cl-border);
                color: var(--cl-text);
            }
            .search-form-btn-default:hover {
                border-color: var(--cl-primary);
                color: var(--cl-primary);
            }
            .search-form-expand {
                margin-left: auto;
                background: transparent;
                border: none;
                color: var(--cl-primary);
                cursor: pointer;
                font-size: var(--cl-font-size-md);
                display: flex;
                align-items: center;
                gap: 4px;
            }
            .search-form-expand:hover {
                text-decoration: underline;
            }
            .search-form-checkbox-wrapper {
                display: flex;
                align-items: center;
                gap: 8px;
                height: 36px;
            }
            .search-form-checkbox-wrapper input {
                width: 16px;
                height: 16px;
                cursor: pointer;
            }
            .search-form-checkbox-wrapper label {
                cursor: pointer;
                font-size: var(--cl-font-size-lg);
                color: var(--cl-text);
            }
        `;
        document.head.appendChild(style);
    }

    _create() {
        const form = document.createElement('form');
        form.className = 'search-form';
        form.addEventListener('submit', (e) => {
            e.preventDefault();
            this._handleSearch();
        });

        // 欄位區
        const fieldsContainer = document.createElement('div');
        fieldsContainer.className = 'search-form-fields';
        fieldsContainer.style.gridTemplateColumns = `repeat(${this.options.columns}, 1fr)`;

        this._renderFields(fieldsContainer);
        form.appendChild(fieldsContainer);

        // 按鈕區
        const actions = document.createElement('div');
        actions.className = 'search-form-actions';

        const searchBtn = document.createElement('button');
        searchBtn.type = 'submit';
        searchBtn.className = 'search-form-btn search-form-btn-primary';
        searchBtn.textContent = this.options.searchText;
        actions.appendChild(searchBtn);

        if (this.options.showReset) {
            const resetBtn = document.createElement('button');
            resetBtn.type = 'button';
            resetBtn.className = 'search-form-btn search-form-btn-default';
            resetBtn.textContent = this.options.resetText;
            resetBtn.addEventListener('click', () => this._handleReset());
            actions.appendChild(resetBtn);
        }

        // 展開/收合按鈕
        if (this.options.collapsible && this._shouldShowExpand()) {
            const expandBtn = document.createElement('button');
            expandBtn.type = 'button';
            expandBtn.className = 'search-form-expand';
            expandBtn.innerHTML = this._expanded ? Locale.t('searchForm.collapse') : Locale.t('searchForm.expand');
            expandBtn.addEventListener('click', () => {
                this._expanded = !this._expanded;
                expandBtn.innerHTML = this._expanded ? Locale.t('searchForm.collapse') : Locale.t('searchForm.expand');
                this._updateVisibility();
            });
            actions.appendChild(expandBtn);
            this._expandBtn = expandBtn;
        }

        form.appendChild(actions);

        this.element = form;
        this._fieldsContainer = fieldsContainer;

        this._updateVisibility();
    }

    _renderFields(container) {
        const { fields, columns, visibleRows } = this.options;

        fields.forEach((field, index) => {
            const fieldEl = document.createElement('div');
            fieldEl.className = 'search-form-field';
            fieldEl.dataset.index = index;

            // 計算是否在可見區域
            const row = Math.floor(index / columns);
            if (row >= visibleRows && !this._expanded) {
                fieldEl.classList.add('hidden');
            }

            // Label
            if (field.label) {
                const label = document.createElement('label');
                label.className = 'search-form-label';
                label.textContent = field.label;
                if (field.required) {
                    const required = document.createElement('span');
                    required.className = 'required';
                    required.textContent = '*';
                    label.appendChild(required);
                }
                fieldEl.appendChild(label);
            }

            // Input
            const inputContainer = document.createElement('div');
            inputContainer.className = 'search-form-input';
            this._createFieldInput(inputContainer, field);
            fieldEl.appendChild(inputContainer);

            container.appendChild(fieldEl);
        });
    }

    _createFieldInput(container, field) {
        const { key, type, placeholder, options: fieldOptions, defaultValue, width } = field;
        const currentValue = this._values[key] ?? defaultValue ?? '';

        switch (type) {
            case SearchForm.FIELD_TYPES.SELECT:
                const dropdown = new Dropdown({
                    variant: 'searchable',
                    items: fieldOptions || [],
                    placeholder: placeholder || Locale.t('searchForm.selectPlaceholder'),
                    value: currentValue,
                    width: width || '100%',
                    onChange: (value) => this._handleChange(key, value)
                });
                dropdown.mount(container);
                this._fieldComponents.set(key, dropdown);
                break;

            case SearchForm.FIELD_TYPES.DATE:
                const datePicker = new DatePicker({
                    value: currentValue,
                    placeholder: placeholder || Locale.t('searchForm.datePlaceholder'),
                    onChange: (value) => this._handleChange(key, value)
                });
                datePicker.mount(container);
                this._fieldComponents.set(key, datePicker);
                break;

            case SearchForm.FIELD_TYPES.DATE_RANGE:
                const rangeContainer = document.createElement('div');
                rangeContainer.className = 'search-form-date-range';

                const startKey = `${key}_start`;
                const endKey = `${key}_end`;

                const startPicker = new DatePicker({
                    value: this._values[startKey] || '',
                    placeholder: Locale.t('searchForm.startDate'),
                    onChange: (value) => this._handleChange(startKey, value)
                });
                startPicker.mount(rangeContainer);

                const separator = document.createElement('span');
                separator.textContent = Locale.t('searchForm.dateSeparator');
                rangeContainer.appendChild(separator);

                const endPicker = new DatePicker({
                    value: this._values[endKey] || '',
                    placeholder: Locale.t('searchForm.endDate'),
                    onChange: (value) => this._handleChange(endKey, value)
                });
                endPicker.mount(rangeContainer);

                container.appendChild(rangeContainer);
                this._fieldComponents.set(startKey, startPicker);
                this._fieldComponents.set(endKey, endPicker);
                break;

            case SearchForm.FIELD_TYPES.NUMBER:
                const numberInput = new NumberInput({
                    value: currentValue,
                    placeholder: placeholder || '',
                    onChange: (value) => this._handleChange(key, value)
                });
                numberInput.mount(container);
                this._fieldComponents.set(key, numberInput);
                break;

            case SearchForm.FIELD_TYPES.CHECKBOX:
                const checkboxWrapper = document.createElement('div');
                checkboxWrapper.className = 'search-form-checkbox-wrapper';

                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.id = `search-form-${key}`;
                checkbox.checked = !!currentValue;
                checkbox.addEventListener('change', () => {
                    this._handleChange(key, checkbox.checked);
                });

                const checkboxLabel = document.createElement('label');
                checkboxLabel.htmlFor = checkbox.id;
                checkboxLabel.textContent = placeholder || '';

                checkboxWrapper.appendChild(checkbox);
                checkboxWrapper.appendChild(checkboxLabel);
                container.appendChild(checkboxWrapper);
                this._fieldComponents.set(key, { getValue: () => checkbox.checked, setValue: (v) => checkbox.checked = v });
                break;

            default: // TEXT
                const textInput = new TextInput({
                    type: 'text',
                    value: currentValue,
                    placeholder: placeholder || '',
                    width: width || '100%',
                    enableSecurity: true,
                    onChange: (value) => this._handleChange(key, value)
                });
                textInput.mount(container);
                this._fieldComponents.set(key, textInput);
        }
    }

    _shouldShowExpand() {
        const { fields, columns, visibleRows } = this.options;
        const totalRows = Math.ceil(fields.length / columns);
        return totalRows > visibleRows;
    }

    _updateVisibility() {
        const { columns, visibleRows } = this.options;
        const fields = this._fieldsContainer.querySelectorAll('.search-form-field');

        fields.forEach((field, index) => {
            const row = Math.floor(index / columns);
            if (row >= visibleRows && !this._expanded) {
                field.classList.add('hidden');
            } else {
                field.classList.remove('hidden');
            }
        });
    }

    _handleChange(key, value) {
        this._values[key] = value;

        if (this.options.onChange) {
            this.options.onChange(key, value, this._values);
        }
    }

    _handleSearch() {
        // 收集所有值
        const values = this.getValues();

        // 驗證必填
        const { fields } = this.options;
        for (const field of fields) {
            if (field.required) {
                const value = values[field.key];
                if (value === undefined || value === null || value === '') {
                    const component = this._fieldComponents.get(field.key);
                    if (component?.setError) {
                        component.setError(Locale.t('searchForm.requiredError'));
                    }
                    return;
                }
            }
        }

        if (this.options.onSearch) {
            this.options.onSearch(values);
        }
    }

    _handleReset() {
        // 重設所有值
        this._values = { ...this.options.values };

        // 更新元件
        this._fieldComponents.forEach((component, key) => {
            const defaultValue = this.options.values[key] ?? '';
            if (component.setValue) {
                component.setValue(defaultValue);
            }
            if (component.clearError) {
                component.clearError();
            }
        });

        if (this.options.onReset) {
            this.options.onReset();
        }
    }

    // Public API

    getValues() {
        const values = {};

        this._fieldComponents.forEach((component, key) => {
            if (component.getValue) {
                values[key] = component.getValue();
            }
        });

        return values;
    }

    setValues(values) {
        Object.entries(values).forEach(([key, value]) => {
            this._values[key] = value;
            const component = this._fieldComponents.get(key);
            if (component?.setValue) {
                component.setValue(value);
            }
        });
        return this;
    }

    getValue(key) {
        const component = this._fieldComponents.get(key);
        return component?.getValue?.() ?? this._values[key];
    }

    setValue(key, value) {
        this._values[key] = value;
        const component = this._fieldComponents.get(key);
        if (component?.setValue) {
            component.setValue(value);
        }
        return this;
    }

    reset() {
        this._handleReset();
        return this;
    }

    submit() {
        this._handleSearch();
        return this;
    }

    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._fieldComponents.forEach(component => {
            if (component?.destroy) {
                component.destroy();
            }
        });
        this._fieldComponents.clear();
        this.element?.remove();
        this.element = null;
    }
}

export default SearchForm;
