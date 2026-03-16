/**
 * ChainedInput composes public single-field components with dependency flow.
 */
import Locale from '../../i18n/index.js';
import { Checkbox } from '../../form/Checkbox/index.js';
import { DatePicker } from '../../form/DatePicker/index.js';
import { Dropdown } from '../../form/Dropdown/index.js';
import { NumberInput } from '../../form/NumberInput/index.js';
import { TextInput } from '../../form/TextInput/index.js';
import { TimePicker } from '../../form/TimePicker/index.js';

export class ChainedInput {
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
            const entry = this._createField(field, index);
            container.appendChild(entry.wrapper);
            this.fieldElements.set(field.name, entry);
            this.values[field.name] = field.type === 'checkbox' ? false : '';
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

        const host = document.createElement('div');
        host.className = 'chained-input__host';
        wrapper.appendChild(host);

        const component = this._createFieldComponent(field, index);
        component.mount(host);

        if (index > 0 && field.hideWhenDisabled) {
            wrapper.style.display = 'none';
        }

        return { wrapper, host, component, field };
    }

    _createFieldComponent(field, index) {
        const disabled = index > 0;
        const textType = ['text', 'email', 'tel', 'url', 'password'].includes(field.type)
            ? field.type
            : 'text';

        switch (field.type) {
            case 'select':
                return new Dropdown({
                    items: this._normalizeOptions(field.options),
                    placeholder: field.placeholder || Locale.t('chainedInput.placeholder'),
                    value: null,
                    disabled,
                    width: '100%',
                    onChange: (value) => this._handleFieldChange(field.name, value)
                });

            case 'checkbox':
                return new Checkbox({
                    label: field.checkboxLabel || Locale.t('chainedInput.checkboxYes'),
                    checked: false,
                    disabled,
                    onChange: (checked) => this._handleFieldChange(field.name, checked)
                });

            case 'date':
                return new DatePicker({
                    placeholder: field.placeholder || '',
                    value: null,
                    disabled,
                    onChange: (date) => this._handleFieldChange(field.name, this._toIsoDate(date))
                });

            case 'time':
                return new TimePicker({
                    placeholder: field.placeholder || '',
                    value: null,
                    disabled,
                    onChange: (value) => this._handleFieldChange(field.name, value)
                });

            case 'number':
                return new NumberInput({
                    value: null,
                    min: field.min ?? Number.NEGATIVE_INFINITY,
                    max: field.max ?? Number.POSITIVE_INFINITY,
                    showButtons: false,
                    placeholder: field.placeholder || '',
                    width: '100%',
                    disabled,
                    onChange: (value) => this._handleFieldChange(field.name, value ?? '')
                });

            case 'roc-date': {
                let component = null;
                component = new TextInput({
                    type: 'text',
                    placeholder: field.placeholder || 'YYY/MM/DD',
                    value: '',
                    maxLength: 9,
                    width: '100%',
                    disabled,
                    onChange: (value) => {
                        const sanitized = String(value ?? '').replace(/[^\d/]/g, '');
                        if (sanitized !== value) {
                            component.setValue(sanitized);
                        }
                        this._handleFieldChange(field.name, sanitized);
                    }
                });
                return component;
            }

            case 'text':
            case 'email':
            case 'tel':
            case 'url':
            case 'password':
            default:
                return new TextInput({
                    type: textType,
                    placeholder: field.placeholder || '',
                    value: '',
                    maxLength: field.maxLength || null,
                    width: '100%',
                    disabled,
                    onChange: (value) => this._handleFieldChange(field.name, value)
                });
        }
    }

    _normalizeOptions(options = []) {
        return (options || []).map((opt) => {
            if (typeof opt === 'object') {
                return { value: opt.value, label: opt.label, disabled: opt.disabled };
            }
            return { value: opt, label: opt };
        });
    }

    _toIsoDate(date) {
        if (!(date instanceof Date) || Number.isNaN(date.getTime())) {
            return '';
        }

        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    }

    async _initializeFields() {
        const firstField = this.options.fields[0];
        if (firstField && firstField.loadOptions) {
            await this._loadFieldOptions(firstField.name, '');
        }
    }

    async _handleFieldChange(fieldName, value) {
        this.values[fieldName] = value;

        const fieldIndex = this.options.fields.findIndex((field) => field.name === fieldName);

        for (let i = fieldIndex + 1; i < this.options.fields.length; i++) {
            const nextField = this.options.fields[i];
            const nextElement = this.fieldElements.get(nextField.name);

            if (!nextElement) continue;

            this._clearField(nextElement);
            this._setDisabled(nextElement, true);

            if (nextField.hideWhenDisabled) {
                nextElement.wrapper.style.display = 'none';
            }

            this.values[nextField.name] = nextField.type === 'checkbox' ? false : '';
        }

        const currentField = this.options.fields[fieldIndex];
        const hasValue = currentField?.type === 'checkbox'
            ? value === true
            : value !== '' && value !== null && value !== undefined;

        if (hasValue && fieldIndex < this.options.fields.length - 1) {
            const nextField = this.options.fields[fieldIndex + 1];
            const nextElement = this.fieldElements.get(nextField.name);

            if (nextElement) {
                if (nextField.loadOptions) {
                    await this._loadFieldOptions(nextField.name, value);
                } else {
                    this._restoreStaticOptions(nextElement);
                }

                this._setDisabled(nextElement, false);

                if (nextField.hideWhenDisabled) {
                    nextElement.wrapper.style.display = '';
                }
            }
        }

        if (this.options.onChange) {
            this.options.onChange(this.getValues());
        }
    }

    _restoreStaticOptions(entry) {
        if (entry.field.type !== 'select') return;
        entry.component.setItems(this._normalizeOptions(entry.field.options));
    }

    _clearField(entry) {
        const { field, component } = entry;

        if (field.type === 'select') {
            component.clear();
            if (field.loadOptions) {
                component.setItems([]);
            } else {
                component.setItems(this._normalizeOptions(field.options));
            }
            return;
        }

        component.clear?.();
        if (field.type === 'checkbox') {
            component.setValue(false);
        }
    }

    _setDisabled(entry, disabled) {
        entry.component.setDisabled?.(disabled);
    }

    async _loadFieldOptions(fieldName, parentValue) {
        const field = this.options.fields.find((item) => item.name === fieldName);
        const entry = this.fieldElements.get(fieldName);

        if (!field || !entry || !field.loadOptions || field.type !== 'select') return;

        this._setDisabled(entry, true);
        entry.component.setItems([]);

        try {
            const options = await field.loadOptions(parentValue);
            const normalizedOptions = this._normalizeOptions(options);
            entry.component.setItems(normalizedOptions);

            if (normalizedOptions.length === 0) {
                this._setDisabled(entry, true);
                if (field.hideWhenEmpty) {
                    entry.wrapper.style.display = 'none';
                }
            } else {
                this._setDisabled(entry, false);
                entry.wrapper.style.display = '';
            }
        } catch (error) {
            console.error(`Failed to load options for ${fieldName}:`, error);
            entry.component.setItems([]);
            this._setDisabled(entry, true);
        }
    }

    getValues() {
        return { ...this.values };
    }

    async setValues(values) {
        for (const [fieldName, value] of Object.entries(values)) {
            const entry = this.fieldElements.get(fieldName);
            const field = this.options.fields.find((item) => item.name === fieldName);

            if (!entry || !field) continue;

            if (field.type === 'select' && field.loadOptions) {
                await this._loadFieldOptions(fieldName, this._getParentFieldValue(fieldName));
            }

            entry.component.setValue?.(value);
            await this._handleFieldChange(fieldName, value);
        }
    }

    _getParentFieldValue(fieldName) {
        const fieldIndex = this.options.fields.findIndex((item) => item.name === fieldName);
        if (fieldIndex <= 0) return '';
        const parentField = this.options.fields[fieldIndex - 1];
        return this.values[parentField.name] ?? '';
    }

    reset() {
        this.options.fields.forEach((field, index) => {
            const entry = this.fieldElements.get(field.name);
            if (!entry) return;

            this._clearField(entry);
            this.values[field.name] = field.type === 'checkbox' ? false : '';

            if (index > 0) {
                this._setDisabled(entry, true);
                if (field.hideWhenDisabled) {
                    entry.wrapper.style.display = 'none';
                }
            } else if (field.type === 'select') {
                this._restoreStaticOptions(entry);
            }
        });

        this._initializeFields();
    }

    validate() {
        const errors = [];
        this.options.fields.forEach((field) => {
            if (!field.required) return;

            const value = this.values[field.name];
            const isEmpty = field.type === 'checkbox' ? !value : (!value && value !== 0);
            if (isEmpty) {
                errors.push({
                    field: field.name,
                    message: `${field.label || field.name} is required`
                });
            }
        });
        return errors;
    }

    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this.fieldElements.forEach(({ component }) => {
            component.destroy?.();
        });

        if (this.element?.parentNode) {
            this.element.remove();
        }

        this.fieldElements.clear();
    }

    static bindDependency(config) {
        const { source, target, condition = (value) => !!value } = config;

        const sourceEl = typeof source === 'string'
            ? document.querySelector(source)
            : source;
        const targetEl = typeof target === 'string'
            ? document.querySelector(target)
            : target;

        if (!sourceEl || !targetEl) {
            console.error('ChainedInput.bindDependency: source or target was not found');
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

        updateTarget();
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
