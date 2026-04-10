import { escapeHtml } from '../../utils/security.js';
import { createComponentState } from '../../utils/component-state.js';

export class NumberInput {
    constructor(options = {}) {
        this.options = {
            label: '',
            value: null,
            min: 0,
            max: 100,
            step: 1,
            precision: 0,
            disabled: false,
            showButtons: true,
            placeholder: '',
            width: '100%',
            size: 'medium',
            onChange: null,
            className: '',
            ...options
        };

        this.value = this.options.value !== null ? Number.parseFloat(this.options.value) : null;
        this.input = null;
        this.wrapper = null;
        this.decreaseBtn = null;
        this.increaseBtn = null;
        this.element = this._create();
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed' }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            FOCUS: (state) => (
                state.availability === 'disabled'
                    ? state
                    : { ...state, interaction: 'focused' }
            ),
            BLUR: (state) => ({ ...state, interaction: 'idle' }),
            SET_VALUE: (state, payload) => ({
                ...state,
                value: payload?.value === null || payload?.value === undefined
                    ? null
                    : Number.parseFloat(payload.value)
            }),
            CLEAR: (state) => ({ ...state, value: null }),
            SET_DISABLED: (state, payload) => ({
                ...state,
                availability: payload?.disabled ? 'disabled' : 'enabled',
                interaction: payload?.disabled ? 'idle' : state.interaction
            }),
            INCREASE: (state) => ({
                ...state,
                value: this._clampValue((state.value ?? this.options.min) + this.options.step)
            }),
            DECREASE: (state) => ({
                ...state,
                value: this._clampValue((state.value ?? this.options.min) - this.options.step)
            })
        });
        this._applyState();
    }

    _buildInitialState() {
        return {
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            interaction: 'idle',
            value: this.value
        };
    }

    _getSizeStyles() {
        const sizes = {
            small: { height: '32px', fontSize: 'var(--cl-font-size-md)', btnWidth: '28px' },
            medium: { height: '40px', fontSize: 'var(--cl-font-size-lg)', btnWidth: '32px' },
            large: { height: '48px', fontSize: 'var(--cl-font-size-xl)', btnWidth: '40px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _create() {
        const { label, required, disabled, width, placeholder, showButtons, className } = this.options;
        const sizeStyles = this._getSizeStyles();

        const container = document.createElement('div');
        container.className = `number-input-container ${className}`;
        container.style.cssText = `display: flex; flex-direction: column; gap: 4px; width: ${width};`;

        if (label) {
            const labelEl = document.createElement('label');
            labelEl.innerHTML = `${escapeHtml(label)}${required ? '<span style="color: var(--cl-danger); margin-left: 2px;">*</span>' : ''}`;
            labelEl.style.cssText = 'font-size: var(--cl-font-size-md); font-weight: 500; color: var(--cl-text);';
            container.appendChild(labelEl);
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'number-input__wrapper';
        wrapper.style.cssText = `
            display: flex;
            align-items: stretch;
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-md);
            overflow: hidden;
            height: ${sizeStyles.height};
            transition: all var(--cl-transition);
            background: ${disabled ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)'};
        `;

        if (showButtons) {
            const decreaseBtn = this._createButton('-', () => this._decrease());
            wrapper.appendChild(decreaseBtn);
            this.decreaseBtn = decreaseBtn;
        }

        const input = document.createElement('input');
        input.type = 'text';
        input.inputMode = 'numeric';
        input.placeholder = placeholder;
        input.value = this.value !== null ? this._formatValue(this.value) : '';
        input.disabled = disabled;
        input.style.cssText = `
            flex: 1;
            min-width: 0;
            border: none;
            outline: none;
            text-align: center;
            font-size: ${sizeStyles.fontSize};
            font-family: inherit;
            background: transparent;
            color: ${disabled ? 'var(--cl-text-placeholder)' : 'var(--cl-text)'};
        `;

        input.addEventListener('focus', () => this.send('FOCUS'));
        input.addEventListener('blur', () => {
            const nextValue = input.value;
            this.send('BLUR');
            this._validateAndUpdate(nextValue);
        });
        input.addEventListener('keydown', (event) => {
            if (this.snapshot().availability === 'disabled') return;
            if (event.key === 'ArrowUp') {
                event.preventDefault?.();
                this._increase();
            } else if (event.key === 'ArrowDown') {
                event.preventDefault?.();
                this._decrease();
            } else if (event.key === 'Enter') {
                this._validateAndUpdate(input.value);
            }
        });
        input.addEventListener('input', () => {
            this._adjustFontSize(input.value);
        });

        wrapper.appendChild(input);
        this.input = input;

        if (showButtons) {
            const increaseBtn = this._createButton('+', () => this._increase());
            wrapper.appendChild(increaseBtn);
            this.increaseBtn = increaseBtn;
        }

        this.wrapper = wrapper;
        container.appendChild(wrapper);
        return container;
    }

    _createButton(text, onClick) {
        const sizeStyles = this._getSizeStyles();
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.textContent = text;
        btn.disabled = this.options.disabled;
        btn.style.cssText = `
            width: ${sizeStyles.btnWidth};
            border: none;
            background: var(--cl-bg-secondary);
            color: var(--cl-text-secondary);
            font-size: var(--cl-font-size-xl);
            font-weight: bold;
            cursor: ${this.options.disabled ? 'not-allowed' : 'pointer'};
            transition: background var(--cl-transition-fast);
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 0;
            margin: 0;
        `;

        btn.addEventListener('mouseenter', () => {
            if (this.snapshot().availability === 'disabled') return;
            btn.style.background = 'var(--cl-border-light)';
        });

        btn.addEventListener('mouseleave', () => {
            btn.style.background = 'var(--cl-bg-secondary)';
        });

        btn.addEventListener('mousedown', (event) => {
            event.preventDefault?.();
        });

        btn.addEventListener('click', (event) => {
            event.preventDefault?.();
            if (this.snapshot().availability === 'disabled') return;
            onClick();
        });

        return btn;
    }

    _formatValue(value) {
        if (value === null || value === undefined || isNaN(value)) return '';
        return this.options.precision > 0
            ? Number(value).toFixed(this.options.precision)
            : String(Math.round(value));
    }

    _adjustFontSize(valueString) {
        if (!this.input) return;

        const len = valueString.length;
        const sizeStyles = this._getSizeStyles();
        const baseSize = Number.parseInt(sizeStyles.fontSize, 10);

        if (len > 12) {
            const reduction = Math.ceil((len - 12) / 2);
            const nextSize = Math.max(8, baseSize - reduction);
            this.input.style.fontSize = `${nextSize}px`;
        } else {
            this.input.style.fontSize = sizeStyles.fontSize;
        }
    }

    _clampValue(value) {
        return Math.max(this.options.min, Math.min(this.options.max, value));
    }

    _validateAndUpdate(inputValue) {
        let num = Number.parseFloat(inputValue);

        if (Number.isNaN(num)) {
            if (inputValue.trim() === '') {
                this.setValue(null);
            } else if (this.input) {
                this.input.value = this._formatValue(this.value);
            }
        } else {
            this.setValue(this._clampValue(num));
        }
    }

    _syncOptionsFromState(state) {
        this.value = state.value;
        this.options.value = state.value;
        this.options.disabled = state.availability === 'disabled';
    }

    _applyState() {
        const state = this.snapshot();

        if (this.element) {
            this.element.style.display = state.visibility === 'hidden' ? 'none' : '';
        }

        if (this.input) {
            const text = this._formatValue(state.value);
            this.input.value = text;
            this.input.disabled = state.availability === 'disabled';
            this.input.style.color = state.availability === 'disabled' ? 'var(--cl-text-placeholder)' : 'var(--cl-text)';
            this.input.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'text';
            this._adjustFontSize(text);
        }

        if (this.wrapper) {
            this.wrapper.style.background = state.availability === 'disabled' ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)';
            if (state.availability === 'disabled') {
                this.wrapper.style.borderColor = 'var(--cl-border)';
                this.wrapper.style.boxShadow = 'none';
            } else if (state.interaction === 'focused') {
                this.wrapper.style.borderColor = 'var(--cl-primary)';
                this.wrapper.style.boxShadow = '0 0 0 3px rgba(var(--cl-primary-rgb), 0.1)';
            } else {
                this.wrapper.style.borderColor = 'var(--cl-border)';
                this.wrapper.style.boxShadow = 'none';
            }
        }

        [this.decreaseBtn, this.increaseBtn].filter(Boolean).forEach((btn) => {
            btn.disabled = state.availability === 'disabled';
            btn.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'pointer';
            btn.style.color = state.availability === 'disabled' ? 'var(--cl-text-placeholder)' : 'var(--cl-text-secondary)';
            btn.style.background = 'var(--cl-bg-secondary)';
        });
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        const nextState = this._state.send(event, payload);
        this._syncOptionsFromState(nextState);
        this._applyState();
        return nextState;
    }

    _increase() {
        if (this.snapshot().availability === 'disabled') return;
        this.send('INCREASE');
        if (this.options.onChange) {
            this.options.onChange(this.value);
        }
    }

    _decrease() {
        if (this.snapshot().availability === 'disabled') return;
        this.send('DECREASE');
        if (this.options.onChange) {
            this.options.onChange(this.value);
        }
    }

    getValue() {
        return this.value;
    }

    setValue(value) {
        const normalizedValue = value === null || value === undefined
            ? null
            : Number.parseFloat(value);
        this.send('SET_VALUE', { value: normalizedValue });
        if (this.options.onChange) {
            this.options.onChange(this.value);
        }
    }

    clear() {
        this.send('CLEAR');
        if (this.options.onChange) {
            this.options.onChange(this.value);
        }
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
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default NumberInput;
