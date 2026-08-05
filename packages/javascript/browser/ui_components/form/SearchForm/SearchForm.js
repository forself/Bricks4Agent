/**
 * SearchForm - 搜尋表單元件
 *
 * 自動產生搜尋表單，支援多種欄位類型、展開收合、驗證等功能
 *
 * @author MAGI System
 * @version 1.0.0
 */

import { escapeHtml } from '../../utils/security.js';
import { TextInput } from '../TextInput/index.js';
import { Dropdown } from '../Dropdown/index.js';
import { MultiSelectDropdown } from '../MultiSelectDropdown/index.js';
import { DatePicker } from '../DatePicker/index.js';
import { NumberInput } from '../NumberInput/index.js';

import Locale from '../../i18n/index.js';
export class SearchForm {
    static FIELD_TYPES = {
        TEXT: 'text',
        NUMBER: 'number',
        SELECT: 'select',
        MULTISELECT: 'multiselect',
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
            requiredMark: '*',
            onSearch: null,
            onReset: null,
            onChange: null,
            onValidationError: null,
            ...options
        };

        this._values = { ...this.options.values };
        this._expanded = false;
        this._fieldComponents = new Map();
        this._fieldErrorElements = new Map();
        this.element = null;

        this._create();
    }

    _create() {
        const form = document.createElement('form');
        form.className = 'search-form';
        form.style.cssText = 'background: var(--cl-bg); border: 1px solid var(--cl-border-light); border-radius: var(--cl-radius-lg); padding: 16px;';
        form.addEventListener('submit', (e) => {
            e.preventDefault();
            this._handleSearch();
        });

        // 欄位區
        const fieldsContainer = document.createElement('div');
        fieldsContainer.className = 'search-form-fields';
        fieldsContainer.style.cssText = 'display: grid; gap: 16px; margin-bottom: 16px;';
        fieldsContainer.style.gridTemplateColumns = `repeat(${this.options.columns}, 1fr)`;

        this._renderFields(fieldsContainer);
        form.appendChild(fieldsContainer);

        // 按鈕區
        const actions = document.createElement('div');
        actions.className = 'search-form-actions';
        actions.style.cssText = 'display: flex; align-items: center; gap: 12px; padding-top: 16px; border-top: 1px solid var(--cl-border-light);';

        const searchBtn = document.createElement('button');
        searchBtn.type = 'submit';
        searchBtn.className = 'search-form-btn search-form-btn-primary';
        searchBtn.style.cssText = 'padding: 8px 24px; border: 1px solid var(--cl-primary); border-radius: var(--cl-radius-sm); font-size: var(--cl-font-size-lg); cursor: pointer; transition: all var(--cl-transition); background: var(--cl-primary); color: var(--cl-bg);';
        searchBtn.textContent = this.options.searchText;
        // :hover 效果（CSP 相容，改用事件）
        searchBtn.addEventListener('mouseenter', () => {
            searchBtn.style.background = 'var(--cl-primary-dark)';
            searchBtn.style.borderColor = 'var(--cl-primary-dark)';
        });
        searchBtn.addEventListener('mouseleave', () => {
            searchBtn.style.background = 'var(--cl-primary)';
            searchBtn.style.borderColor = 'var(--cl-primary)';
        });
        actions.appendChild(searchBtn);

        if (this.options.showReset) {
            const resetBtn = document.createElement('button');
            resetBtn.type = 'button';
            resetBtn.className = 'search-form-btn search-form-btn-default';
            resetBtn.style.cssText = 'padding: 8px 24px; border: 1px solid var(--cl-border); border-radius: var(--cl-radius-sm); font-size: var(--cl-font-size-lg); cursor: pointer; transition: all var(--cl-transition); background: var(--cl-bg); color: var(--cl-text);';
            resetBtn.textContent = this.options.resetText;
            // :hover 效果（CSP 相容，改用事件）
            resetBtn.addEventListener('mouseenter', () => {
                resetBtn.style.borderColor = 'var(--cl-primary)';
                resetBtn.style.color = 'var(--cl-primary)';
            });
            resetBtn.addEventListener('mouseleave', () => {
                resetBtn.style.borderColor = 'var(--cl-border)';
                resetBtn.style.color = 'var(--cl-text)';
            });
            resetBtn.addEventListener('click', () => this._handleReset());
            actions.appendChild(resetBtn);
        }

        // 展開/收合按鈕
        if (this.options.collapsible && this._shouldShowExpand()) {
            const expandBtn = document.createElement('button');
            expandBtn.type = 'button';
            expandBtn.className = 'search-form-expand';
            expandBtn.style.cssText = 'margin-left: auto; background: transparent; border: none; color: var(--cl-primary); cursor: pointer; font-size: var(--cl-font-size-md); display: flex; align-items: center; gap: 4px;';
            expandBtn.innerHTML = this._expanded ? Locale.t('searchForm.collapse') : Locale.t('searchForm.expand');
            // :hover 效果（CSP 相容，改用事件）
            expandBtn.addEventListener('mouseenter', () => {
                expandBtn.style.textDecoration = 'underline';
            });
            expandBtn.addEventListener('mouseleave', () => {
                expandBtn.style.textDecoration = 'none';
            });
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
            fieldEl.style.cssText = 'display: flex; flex-direction: column; gap: 4px;';
            fieldEl.dataset.index = index;

            // 計算是否在可見區域
            const row = Math.floor(index / columns);
            if (row >= visibleRows && !this._expanded) {
                fieldEl.classList.add('hidden');
                fieldEl.style.display = 'none';
            }

            // Label
            if (field.label) {
                const label = document.createElement('label');
                label.className = 'search-form-label';
                label.style.cssText = 'font-size: var(--cl-font-size-md); font-weight: 500; color: var(--cl-text);';
                label.textContent = field.label;
                if (field.required) {
                    const required = document.createElement('span');
                    required.className = 'required';
                    required.style.cssText = 'color: var(--cl-danger); margin-left: 2px;';
                    required.textContent = this.options.requiredMark;
                    label.appendChild(required);
                }
                fieldEl.appendChild(label);
            }

            // Input
            const inputContainer = document.createElement('div');
            inputContainer.className = 'search-form-input';
            inputContainer.style.cssText = 'min-height: 36px;';
            this._createFieldInput(inputContainer, field);
            fieldEl.appendChild(inputContainer);

            const errorEl = document.createElement('span');
            errorEl.className = 'search-form-field-error';
            errorEl.style.cssText = 'display:none;font-size:var(--cl-font-size-sm);color:var(--cl-danger);';
            fieldEl.appendChild(errorEl);
            if (field.key) this._fieldErrorElements.set(field.key, errorEl);

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

            case SearchForm.FIELD_TYPES.MULTISELECT:
                const multiSelect = new MultiSelectDropdown({
                    items: fieldOptions || [],
                    placeholder: placeholder || Locale.t('searchForm.selectPlaceholder'),
                    values: Array.isArray(currentValue) ? currentValue : [],
                    width: width || '100%',
                    onChange: (values) => this._handleChange(key, values)
                });
                multiSelect.mount(container);
                this._fieldComponents.set(key, multiSelect);
                break;

            case SearchForm.FIELD_TYPES.DATE:
                const datePicker = new DatePicker({
                    value: currentValue,
                    placeholder: placeholder || Locale.t('searchForm.datePlaceholder'),
                    format: field.format,
                    min: field.min,
                    max: field.max,
                    required: field.required === true,
                    onChange: (value) => this._handleChange(key, value)
                });
                datePicker.mount(container);
                this._fieldComponents.set(key, datePicker);
                break;

            case SearchForm.FIELD_TYPES.DATE_RANGE:
                const rangeContainer = document.createElement('div');
                rangeContainer.className = 'search-form-date-range';
                rangeContainer.style.cssText = 'display: flex; align-items: center; gap: 8px;';

                const startKey = `${key}_start`;
                const endKey = `${key}_end`;

                const startPicker = new DatePicker({
                    value: this._values[startKey] || '',
                    placeholder: Locale.t('searchForm.startDate'),
                    onChange: (value) => this._handleChange(startKey, value)
                });
                startPicker.mount(rangeContainer);

                const separator = document.createElement('span');
                separator.style.cssText = 'color: var(--cl-text-placeholder);';
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
                checkboxWrapper.style.cssText = 'display: flex; align-items: center; gap: 8px; height: 36px;';

                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.id = `search-form-${key}`;
                checkbox.style.cssText = 'width: 16px; height: 16px; cursor: pointer;';
                checkbox.checked = !!currentValue;
                checkbox.addEventListener('change', () => {
                    this._handleChange(key, checkbox.checked);
                });

                const checkboxLabel = document.createElement('label');
                checkboxLabel.htmlFor = checkbox.id;
                checkboxLabel.style.cssText = 'cursor: pointer; font-size: var(--cl-font-size-lg); color: var(--cl-text);';
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
                field.style.display = 'none';
            } else {
                field.classList.remove('hidden');
                // 還原顯示需用明確 display 值（CSP 下無 class 樣式可回退）
                field.style.display = 'flex';
            }
        });
    }

    _handleChange(key, value) {
        this._values[key] = value;
        this._clearFieldError(key);

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
            this._clearFieldError(field.key);
            if (field.required) {
                const value = values[field.key];
                if (value === undefined || value === null || value === '') {
                    this._setFieldError(
                        field.key,
                        field.requiredMessage || this.options.requiredMessage || Locale.t('searchForm.requiredError')
                    );
                    this.options.onValidationError?.(field);
                    return;
                }
            }
        }

        if (this.options.onSearch) {
            this.options.onSearch(values);
        }
    }

    _handleReset() {
        // Restore the form's internal state before projecting defaults back to controls.
        this._values = { ...this.options.values };
        // 重設所有值
        this._values = { ...this.options.values };

        // 更新元件
        this._fieldComponents.forEach((component, key) => {
            const defaultValue = this.options.values[key] ?? '';
            if (component.setValue) {
                component.setValue(defaultValue);
            } else if (component.setValues) {
                component.setValues(Array.isArray(defaultValue) ? defaultValue : []);
            }
            this._clearFieldError(key);
        });

        this._updateVisibility();

        if (this.options.onReset) {
            this.options.onReset();
        }
    }

    _setFieldError(key, message) {
        const component = this._fieldComponents.get(key);
        if (component?.setError) {
            component.setError(message);
        }
        const errorEl = this._fieldErrorElements.get(key);
        if (errorEl) {
            errorEl.textContent = message || '';
            errorEl.style.display = message ? 'block' : 'none';
        }
    }

    _clearFieldError(key) {
        const component = this._fieldComponents.get(key);
        if (component?.clearError) {
            component.clearError();
        }
        const errorEl = this._fieldErrorElements.get(key);
        if (errorEl) {
            errorEl.textContent = '';
            errorEl.style.display = 'none';
        }
    }

    // Public API

    getValues() {
        const values = {};

        this._fieldComponents.forEach((component, key) => {
            if (component.getValue) {
                values[key] = component.getValue();
            } else if (component.getValues) {
                values[key] = component.getValues();
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
            } else if (component?.setValues) {
                component.setValues(Array.isArray(value) ? value : []);
            }
        });
        return this;
    }

    getValue(key) {
        const component = this._fieldComponents.get(key);
        return component?.getValue?.() ?? component?.getValues?.() ?? this._values[key];
    }

    setValue(key, value) {
        this._values[key] = value;
        const component = this._fieldComponents.get(key);
        if (component?.setValue) {
            component.setValue(value);
        } else if (component?.setValues) {
            component.setValues(Array.isArray(value) ? value : []);
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
        this._fieldErrorElements.clear();
        this.element?.remove();
        this.element = null;
    }
}

export default SearchForm;
