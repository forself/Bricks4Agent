import Locale from '../../i18n/index.js';
import { Checkbox } from '../../form/Checkbox/index.js';
import { DatePicker } from '../../form/DatePicker/index.js';
import { Dropdown } from '../../form/Dropdown/index.js';
import { NumberInput } from '../../form/NumberInput/index.js';
import { TextInput } from '../../form/TextInput/index.js';
import { TimePicker } from '../../form/TimePicker/index.js';
import { createComponentState } from '../../utils/component-state.js';

const defaultFieldValue = (field) => field.type === 'checkbox' ? false : '';

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
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            SET_FIELD_VALUE: (state, payload) => ({
                ...state,
                values: {
                    ...state.values,
                    [payload?.fieldName]: payload?.value
                }
            }),
            SET_FIELD_AVAILABILITY: (state, payload) => ({
                ...state,
                fields: {
                    ...state.fields,
                    [payload?.fieldName]: {
                        ...state.fields[payload?.fieldName],
                        availability: payload?.disabled ? 'disabled' : 'enabled'
                    }
                }
            }),
            SET_FIELD_VISIBILITY: (state, payload) => ({
                ...state,
                fields: {
                    ...state.fields,
                    [payload?.fieldName]: {
                        ...state.fields[payload?.fieldName],
                        visibility: payload?.visible === false ? 'hidden' : 'visible'
                    }
                }
            }),
            SET_FIELD_LOADING: (state, payload) => ({
                ...state,
                fields: {
                    ...state.fields,
                    [payload?.fieldName]: {
                        ...state.fields[payload?.fieldName],
                        loading: payload?.loading ? 'loading' : 'idle'
                    }
                }
            }),
            RESET_FROM_INDEX: (state, payload) => {
                const fromIndex = payload?.fromIndex ?? -1;
                const nextValues = { ...state.values };
                const nextFields = { ...state.fields };
                this.options.fields.forEach((field, index) => {
                    if (index <= fromIndex) return;
                    nextValues[field.name] = defaultFieldValue(field);
                    nextFields[field.name] = {
                        ...nextFields[field.name],
                        availability: 'disabled',
                        visibility: field.hideWhenDisabled ? 'hidden' : 'visible',
                        loading: 'idle'
                    };
                });
                return { ...state, values: nextValues, fields: nextFields };
            },
            RESET_ALL: (state) => {
                const nextValues = {};
                const nextFields = {};
                this.options.fields.forEach((field, index) => {
                    nextValues[field.name] = defaultFieldValue(field);
                    nextFields[field.name] = {
                        availability: index === 0 ? 'enabled' : 'disabled',
                        visibility: index === 0 || !field.hideWhenDisabled ? 'visible' : 'hidden',
                        loading: 'idle'
                    };
                });
                return { ...state, values: nextValues, fields: nextFields };
            }
        });

        this._applyState();
        this._initializeFields();
    }

    _buildInitialState() {
        const values = {};
        const fields = {};
        this.options.fields.forEach((field, index) => {
            values[field.name] = defaultFieldValue(field);
            fields[field.name] = {
                availability: index === 0 ? 'enabled' : 'disabled',
                visibility: index === 0 || !field.hideWhenDisabled ? 'visible' : 'hidden',
                loading: 'idle'
            };
        });
        return {
            lifecycle: 'created',
            visibility: 'visible',
            values,
            fields
        };
    }

    _createElement() {
        const container = document.createElement('div');
        container.className = 'chained-input';
        container.style.cssText = `display:flex;flex-direction:${this.options.layout === 'vertical' ? 'column' : 'row'};gap:${this.options.gap};align-items:flex-end;flex-wrap:wrap;`;

        this.options.fields.forEach((field, index) => {
            const entry = this._createField(field, index);
            container.appendChild(entry.wrapper);
            this.fieldElements.set(field.name, entry);
            this.values[field.name] = defaultFieldValue(field);
        });

        return container;
    }

    _createField(field, index) {
        const wrapper = document.createElement('div');
        wrapper.className = 'chained-input__field';
        wrapper.setAttribute('data-field-name', field.name);
        wrapper.style.cssText = `display:flex;flex-direction:column;gap:4px;min-width:${field.minWidth || '120px'};${field.flex ? `flex:${field.flex};` : ''}`;

        const label = document.createElement('label');
        label.className = 'chained-input__label';
        label.style.cssText = 'font-size:var(--cl-font-size-md);font-weight:500;color:var(--cl-text);min-height:20px;display:block;';
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

        return { wrapper, host, component, field };
    }

    _createFieldComponent(field, index) {
        const disabled = index > 0;
        const textType = ['text', 'email', 'tel', 'url', 'password'].includes(field.type) ? field.type : 'text';

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
                        if (sanitized !== value) component.setValue(sanitized);
                        this._handleFieldChange(field.name, sanitized);
                    }
                });
                return component;
            }
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
        return (options || []).map((option) => typeof option === 'object'
            ? { value: option.value, label: option.label, disabled: option.disabled }
            : { value: option, label: option });
    }

    _toIsoDate(date) {
        if (!(date instanceof Date) || Number.isNaN(date.getTime())) return '';
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    }

    _syncLegacyValues(state) {
        this.values = { ...state.values };
    }

    _applyState() {
        const state = this.snapshot();
        this._syncLegacyValues(state);
        if (this.element) {
            this.element.style.display = state.visibility === 'hidden' ? 'none' : 'flex';
        }
        this.fieldElements.forEach((entry, fieldName) => {
            const fieldState = state.fields[fieldName];
            if (!fieldState) return;
            entry.wrapper.style.display = fieldState.visibility === 'hidden' ? 'none' : 'flex';
            entry.component.setDisabled?.(fieldState.availability === 'disabled');
        });
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        this._state.send(event, payload);
        this._applyState();
        return this.snapshot();
    }

    async _initializeFields() {
        const firstField = this.options.fields[0];
        if (firstField?.loadOptions) {
            await this._loadFieldOptions(firstField.name, '');
        }
    }

    async _handleFieldChange(fieldName, value) {
        this.send('SET_FIELD_VALUE', { fieldName, value });

        const fieldIndex = this.options.fields.findIndex((field) => field.name === fieldName);
        this.send('RESET_FROM_INDEX', { fromIndex: fieldIndex });

        for (let index = fieldIndex + 1; index < this.options.fields.length; index += 1) {
            const nextField = this.options.fields[index];
            const nextEntry = this.fieldElements.get(nextField.name);
            if (!nextEntry) continue;
            this._clearField(nextEntry);
            if (nextField.hideWhenDisabled) {
                this.send('SET_FIELD_VISIBILITY', { fieldName: nextField.name, visible: false });
            }
        }

        const currentField = this.options.fields[fieldIndex];
        const hasValue = currentField?.type === 'checkbox'
            ? value === true
            : value !== '' && value !== null && value !== undefined;

        if (hasValue && fieldIndex < this.options.fields.length - 1) {
            const nextField = this.options.fields[fieldIndex + 1];
            const nextEntry = this.fieldElements.get(nextField.name);
            if (nextEntry) {
                if (nextField.loadOptions) await this._loadFieldOptions(nextField.name, value);
                else this._restoreStaticOptions(nextEntry);
                this.send('SET_FIELD_AVAILABILITY', { fieldName: nextField.name, disabled: false });
                this.send('SET_FIELD_VISIBILITY', { fieldName: nextField.name, visible: true });
            }
        }

        if (this.options.onChange) {
            this.options.onChange(this.getValues());
        }
    }

    _restoreStaticOptions(entry) {
        if (entry.field.type === 'select') {
            entry.component.setItems(this._normalizeOptions(entry.field.options));
        }
    }

    _clearField(entry) {
        const { field, component } = entry;
        if (field.type === 'select') {
            component.clear();
            component.setItems(field.loadOptions ? [] : this._normalizeOptions(field.options));
            return;
        }
        component.clear?.();
        if (field.type === 'checkbox') component.setValue(false);
    }

    async _loadFieldOptions(fieldName, parentValue) {
        const field = this.options.fields.find((item) => item.name === fieldName);
        const entry = this.fieldElements.get(fieldName);
        if (!field || !entry || !field.loadOptions || field.type !== 'select') return;

        this.send('SET_FIELD_LOADING', { fieldName, loading: true });
        this.send('SET_FIELD_AVAILABILITY', { fieldName, disabled: true });
        entry.component.setItems([]);

        try {
            const options = await field.loadOptions(parentValue);
            const normalized = this._normalizeOptions(options);
            entry.component.setItems(normalized);

            if (normalized.length === 0) {
                this.send('SET_FIELD_AVAILABILITY', { fieldName, disabled: true });
                if (field.hideWhenEmpty) {
                    this.send('SET_FIELD_VISIBILITY', { fieldName, visible: false });
                }
            } else {
                this.send('SET_FIELD_AVAILABILITY', { fieldName, disabled: false });
                this.send('SET_FIELD_VISIBILITY', { fieldName, visible: true });
            }
        } catch (error) {
            console.error(`Failed to load options for ${fieldName}:`, error);
            entry.component.setItems([]);
            this.send('SET_FIELD_AVAILABILITY', { fieldName, disabled: true });
        } finally {
            this.send('SET_FIELD_LOADING', { fieldName, loading: false });
        }
    }

    getValues() {
        return { ...this.values };
    }

    async setValues(values) {
        for (const field of this.options.fields) {
            if (!(field.name in values)) continue;
            const value = values[field.name];
            const entry = this.fieldElements.get(field.name);
            if (!entry) continue;
            if (field.type === 'select' && field.loadOptions) {
                await this._loadFieldOptions(field.name, this._getParentFieldValue(field.name));
            }
            entry.component.setValue?.(value);
            await this._handleFieldChange(field.name, value);
        }
    }

    _getParentFieldValue(fieldName) {
        const fieldIndex = this.options.fields.findIndex((item) => item.name === fieldName);
        if (fieldIndex <= 0) return '';
        const parentField = this.options.fields[fieldIndex - 1];
        return this.values[parentField.name] ?? '';
    }

    reset() {
        this.send('RESET_ALL');
        this.options.fields.forEach((field, index) => {
            const entry = this.fieldElements.get(field.name);
            if (!entry) return;
            this._clearField(entry);
            if (index === 0 && field.type === 'select') {
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
                errors.push({ field: field.name, message: `${field.label || field.name} is required` });
            }
        });
        return errors;
    }

    show() {
        this.send('SHOW');
    }

    hide() {
        this.send('HIDE');
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
            this.send('MOUNT');
        }
        return this;
    }

    destroy() {
        this.send('DESTROY');
        this.fieldElements.forEach(({ component }) => component.destroy?.());
        if (this.element?.parentNode) this.element.remove();
        this.fieldElements.clear();
    }

    static bindDependency(config) {
        const { source, target, condition = (value) => !!value } = config;
        const sourceEl = typeof source === 'string' ? document.querySelector(source) : source;
        const targetEl = typeof target === 'string' ? document.querySelector(target) : target;
        if (!sourceEl || !targetEl) {
            console.error('ChainedInput.bindDependency: source or target was not found');
            return;
        }

        const updateTarget = () => {
            const value = sourceEl.type === 'checkbox' ? sourceEl.checked : sourceEl.value;
            const shouldEnable = condition(value);
            targetEl.disabled = !shouldEnable;
            targetEl.style.background = shouldEnable ? 'var(--cl-bg)' : 'var(--cl-bg-secondary)';
            if (!shouldEnable && targetEl.type !== 'checkbox') targetEl.value = '';
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
