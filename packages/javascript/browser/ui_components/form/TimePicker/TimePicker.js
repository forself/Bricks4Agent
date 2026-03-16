import { escapeHtml } from '../../utils/security.js';
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';

function parseTime(value) {
    if (!value) return { hour: null, minute: null };

    const match = String(value).match(/^(\d{1,2}):(\d{2})$/);
    if (!match) return { hour: null, minute: null };

    const hour = Number.parseInt(match[1], 10);
    const minute = Number.parseInt(match[2], 10);

    if (Number.isNaN(hour) || Number.isNaN(minute) || hour < 0 || hour > 23 || minute < 0 || minute > 59) {
        return { hour: null, minute: null };
    }

    return { hour, minute };
}

function formatTime(hour, minute) {
    if (hour === null || minute === null) return '';
    return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`;
}

export class TimePicker {
    constructor(options = {}) {
        this.options = {
            label: '',
            placeholder: Locale.t('timePicker.placeholder'),
            value: null,
            disabled: false,
            required: false,
            size: 'medium',
            minuteStep: 1,
            className: '',
            onChange: null,
            ...options
        };

        if (options.step) {
            this.options.minuteStep = options.step;
        }

        const initial = parseTime(this.options.value);

        this.hour = initial.hour;
        this.minute = initial.minute;
        this.isOpen = false;

        this.element = null;
        this.container = null;
        this.inputWrapper = null;
        this.display = null;
        this.panel = null;
        this.confirmButton = null;
        this.hourColumn = null;
        this.minuteColumn = null;

        this.element = this._create();
        this._state = createComponentState(this._buildInitialState(initial), {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed', open: false }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden', open: false }),
            OPEN: (state) => {
                if (state.availability === 'disabled' || state.open) return state;
                return {
                    ...state,
                    open: true,
                    draftHour: state.hour,
                    draftMinute: state.minute
                };
            },
            CLOSE: (state) => ({
                ...state,
                open: false,
                draftHour: state.hour,
                draftMinute: state.minute
            }),
            TOGGLE: (state) => {
                if (state.availability === 'disabled') return state;
                return state.open
                    ? {
                        ...state,
                        open: false,
                        draftHour: state.hour,
                        draftMinute: state.minute
                    }
                    : {
                        ...state,
                        open: true,
                        draftHour: state.hour,
                        draftMinute: state.minute
                    };
            },
            SET_VALUE: (state, payload) => {
                if (payload?.value == null || payload?.value === '') {
                    return {
                        ...state,
                        hour: null,
                        minute: null,
                        draftHour: null,
                        draftMinute: null
                    };
                }

                const next = parseTime(payload?.value);
                if (next.hour === null || next.minute === null) return state;

                return {
                    ...state,
                    hour: next.hour,
                    minute: next.minute,
                    draftHour: next.hour,
                    draftMinute: next.minute
                };
            },
            CLEAR: (state) => ({
                ...state,
                hour: null,
                minute: null,
                draftHour: null,
                draftMinute: null
            }),
            SET_DISABLED: (state, payload) => ({
                ...state,
                availability: payload?.disabled ? 'disabled' : 'enabled',
                open: payload?.disabled ? false : state.open,
                draftHour: payload?.disabled ? state.hour : state.draftHour,
                draftMinute: payload?.disabled ? state.minute : state.draftMinute
            }),
            SELECT_HOUR: (state, payload) => {
                if (state.availability === 'disabled') return state;
                const value = Number.parseInt(payload?.value, 10);
                if (Number.isNaN(value) || value < 0 || value > 23) return state;
                return {
                    ...state,
                    draftHour: value
                };
            },
            SELECT_MINUTE: (state, payload) => {
                if (state.availability === 'disabled') return state;
                const value = Number.parseInt(payload?.value, 10);
                if (Number.isNaN(value) || value < 0 || value > 59) return state;
                return {
                    ...state,
                    draftMinute: value
                };
            },
            CONFIRM: (state) => {
                if (state.draftHour === null || state.draftMinute === null) {
                    return {
                        ...state,
                        open: false,
                        draftHour: state.hour,
                        draftMinute: state.minute
                    };
                }

                return {
                    ...state,
                    hour: state.draftHour,
                    minute: state.draftMinute,
                    open: false
                };
            }
        });

        this._bindEvents();
        this._applyState();
    }

    _buildInitialState(initial) {
        return {
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            open: false,
            hour: initial.hour,
            minute: initial.minute,
            draftHour: initial.hour,
            draftMinute: initial.minute
        };
    }

    _getSizeStyles() {
        const sizes = {
            small: { height: '32px', padding: '0 8px', fontSize: 'var(--cl-font-size-md)' },
            medium: { height: '40px', padding: '0 12px', fontSize: 'var(--cl-font-size-lg)' },
            large: { height: '48px', padding: '0 16px', fontSize: 'var(--cl-font-size-xl)' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _parseValue(value) {
        return parseTime(value);
    }

    _create() {
        const { label, required, className } = this.options;
        const sizeStyles = this._getSizeStyles();

        const container = document.createElement('div');
        container.className = `timepicker ${className || ''}`.trim();
        container.style.cssText = `
            position: relative;
            width: 100%;
            font-family: var(--cl-font-family);
        `;

        if (label) {
            const labelEl = document.createElement('label');
            labelEl.innerHTML = `${escapeHtml(label)}${required ? '<span style="color:var(--cl-danger);margin-left:2px;">*</span>' : ''}`;
            labelEl.style.cssText = `
                display: block;
                font-size: var(--cl-font-size-md);
                font-weight: 500;
                color: var(--cl-text);
                margin-bottom: 4px;
            `;
            container.appendChild(labelEl);
        }

        const inputWrapper = document.createElement('div');
        inputWrapper.className = 'timepicker__input-wrapper';
        inputWrapper.style.cssText = `
            display: flex;
            align-items: center;
            position: relative;
            height: ${sizeStyles.height};
            padding: ${sizeStyles.padding};
            padding-right: 32px;
            background: var(--cl-bg);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-md);
            cursor: pointer;
            transition: all var(--cl-transition);
        `;

        const display = document.createElement('span');
        display.className = 'timepicker__display';
        display.style.cssText = `
            flex: 1;
            font-size: ${sizeStyles.fontSize};
            color: var(--cl-text-placeholder);
        `;

        const icon = document.createElement('span');
        icon.className = 'timepicker__icon';
        icon.innerHTML = `<svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            <circle cx="8" cy="8" r="6" stroke="var(--cl-text-secondary)" stroke-width="1.5"/>
            <path d="M8 4V8L11 10" stroke="var(--cl-text-secondary)" stroke-width="1.5" stroke-linecap="round"/>
        </svg>`;
        icon.style.cssText = `
            position: absolute;
            right: 10px;
            top: 50%;
            transform: translateY(-50%);
            display: flex;
        `;

        inputWrapper.appendChild(display);
        inputWrapper.appendChild(icon);

        const panel = this._createPanel();

        container.appendChild(inputWrapper);
        container.appendChild(panel);

        this.container = container;
        this.inputWrapper = inputWrapper;
        this.display = display;
        this.panel = panel;

        return container;
    }

    _createPanel() {
        const panel = document.createElement('div');
        panel.className = 'timepicker__panel';
        panel.style.cssText = `
            position: absolute;
            top: 100%;
            left: 0;
            margin-top: 4px;
            background: var(--cl-bg);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-lg);
            box-shadow: var(--cl-shadow-md);
            padding: 12px;
            z-index: 1000;
            display: none;
            width: 200px;
        `;

        const selectorsWrapper = document.createElement('div');
        selectorsWrapper.className = 'timepicker__selectors';
        selectorsWrapper.style.cssText = `
            display: flex;
            gap: 8px;
            align-items: center;
        `;

        const hourColumn = this._createColumn(Locale.t('timePicker.hour'), 0, 23, 1, 'hour');
        const minuteColumn = this._createColumn(Locale.t('timePicker.minute'), 0, 59, this.options.minuteStep, 'minute');

        const separator = document.createElement('span');
        separator.textContent = ':';
        separator.style.cssText = `
            font-size: var(--cl-font-size-3xl);
            font-weight: bold;
            color: var(--cl-text);
        `;

        selectorsWrapper.appendChild(hourColumn.wrapper);
        selectorsWrapper.appendChild(separator);
        selectorsWrapper.appendChild(minuteColumn.wrapper);

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.className = 'timepicker__confirm';
        confirmBtn.textContent = Locale.t('timePicker.confirm');
        confirmBtn.style.cssText = `
            width: 100%;
            margin-top: 12px;
            padding: 8px;
            background: var(--cl-primary);
            color: var(--cl-text-inverse);
            border: none;
            border-radius: var(--cl-radius-md);
            cursor: pointer;
            font-size: var(--cl-font-size-lg);
            transition: background var(--cl-transition);
        `;
        confirmBtn.addEventListener('mouseenter', () => {
            confirmBtn.style.background = 'var(--cl-primary-dark)';
        });
        confirmBtn.addEventListener('mouseleave', () => {
            confirmBtn.style.background = 'var(--cl-primary)';
        });
        confirmBtn.addEventListener('click', () => this._confirm());

        panel.appendChild(selectorsWrapper);
        panel.appendChild(confirmBtn);

        this.hourColumn = hourColumn;
        this.minuteColumn = minuteColumn;
        this.confirmButton = confirmBtn;

        return panel;
    }

    _createColumn(label, min, max, step, type) {
        const wrapper = document.createElement('div');
        wrapper.className = `timepicker__column timepicker__column--${type}`;
        wrapper.style.cssText = `
            flex: 1;
            text-align: center;
        `;

        const labelEl = document.createElement('div');
        labelEl.textContent = label;
        labelEl.style.cssText = `
            font-size: var(--cl-font-size-xs);
            color: var(--cl-text-muted);
            margin-bottom: 6px;
        `;

        const scrollContainer = document.createElement('div');
        scrollContainer.className = 'timepicker__scroll';
        scrollContainer.style.cssText = `
            height: 150px;
            overflow-y: auto;
            border: 1px solid var(--cl-border-light);
            border-radius: var(--cl-radius-md);
        `;

        const items = [];
        for (let value = min; value <= max; value += step) {
            const item = document.createElement('div');
            item.className = 'timepicker__item';
            item.dataset.value = String(value);
            item.textContent = String(value).padStart(2, '0');
            item.style.cssText = `
                padding: 8px;
                cursor: pointer;
                transition: all var(--cl-transition-fast);
                font-size: var(--cl-font-size-lg);
                color: var(--cl-text);
            `;

            item.addEventListener('mouseenter', () => {
                if (!item.classList.contains('timepicker__item--selected')) {
                    item.style.background = 'var(--cl-bg-secondary)';
                }
            });
            item.addEventListener('mouseleave', () => {
                if (!item.classList.contains('timepicker__item--selected')) {
                    item.style.background = 'transparent';
                }
            });
            item.addEventListener('click', () => {
                if (type === 'hour') {
                    this.send('SELECT_HOUR', { value });
                } else {
                    this.send('SELECT_MINUTE', { value });
                }
            });

            scrollContainer.appendChild(item);
            items.push(item);
        }

        wrapper.appendChild(labelEl);
        wrapper.appendChild(scrollContainer);

        return {
            wrapper,
            scrollContainer,
            items
        };
    }

    _bindEvents() {
        this.inputWrapper.addEventListener('click', () => this.toggle());

        this._onDocumentClick = (event) => {
            if (!this.container.contains(event.target)) {
                this.close();
            }
        };
        document.addEventListener('click', this._onDocumentClick);

        this.inputWrapper.addEventListener('mouseenter', () => {
            if (this.snapshot().availability === 'disabled') return;
            this.inputWrapper.style.borderColor = 'var(--cl-primary)';
        });
        this.inputWrapper.addEventListener('mouseleave', () => {
            if (!this.snapshot().open) {
                this.inputWrapper.style.borderColor = 'var(--cl-border)';
            }
        });
    }

    _syncLegacyFields(state) {
        this.hour = state.hour;
        this.minute = state.minute;
        this.isOpen = state.open;
        this.options.disabled = state.availability === 'disabled';
    }

    _applyColumnSelection(column, selectedValue) {
        if (!column) return;

        for (const item of column.items) {
            const itemValue = Number.parseInt(item.dataset.value, 10);
            const isSelected = itemValue === selectedValue;

            item.classList[isSelected ? 'add' : 'remove']('timepicker__item--selected');
            item.style.background = isSelected ? 'var(--cl-primary)' : 'transparent';
            item.style.color = isSelected ? 'var(--cl-text-inverse)' : 'var(--cl-text)';
        }
    }

    _applyState() {
        const state = this.snapshot();
        this._syncLegacyFields(state);

        if (this.container) {
            this.container.style.display = state.visibility === 'hidden' ? 'none' : '';
        }

        if (this.inputWrapper) {
            this.inputWrapper.style.background = state.availability === 'disabled' ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)';
            this.inputWrapper.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'pointer';
            this.inputWrapper.style.opacity = state.availability === 'disabled' ? '0.6' : '1';
            this.inputWrapper.style.borderColor = state.open ? 'var(--cl-primary)' : 'var(--cl-border)';
        }

        if (this.display) {
            const value = this._formatTime();
            this.display.textContent = value || this.options.placeholder;
            this.display.style.color = value ? 'var(--cl-text)' : 'var(--cl-text-placeholder)';
        }

        if (this.panel) {
            this.panel.style.display = state.open ? 'block' : 'none';
        }

        this._applyColumnSelection(this.hourColumn, state.draftHour);
        this._applyColumnSelection(this.minuteColumn, state.draftMinute);
    }

    _confirm() {
        const previousValue = this.getValue();
        const nextState = this.send('CONFIRM');
        const nextValue = formatTime(nextState.hour, nextState.minute);

        if (nextValue && nextValue !== previousValue && this.options.onChange) {
            this.options.onChange(nextValue, {
                hour: nextState.hour,
                minute: nextState.minute
            });
        }
    }

    _formatTime() {
        return formatTime(this.hour, this.minute);
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        this._state.send(event, payload);
        this._applyState();
        return this.snapshot();
    }

    open() {
        if (this.options.disabled || this.isOpen) return;
        this.send('OPEN');
    }

    close() {
        if (!this.isOpen) return;
        this.send('CLOSE');
    }

    toggle() {
        this.send('TOGGLE');
    }

    getValue() {
        return this._formatTime();
    }

    setValue(value) {
        this.send('SET_VALUE', { value });
    }

    clear() {
        this.send('CLEAR');
    }

    setDisabled(disabled) {
        this.send('SET_DISABLED', { disabled });
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
        if (this._onDocumentClick) {
            document.removeEventListener('click', this._onDocumentClick);
        }
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default TimePicker;
