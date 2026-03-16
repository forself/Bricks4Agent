import { escapeHtml, hasSqlInjectionRisk, hasPathTraversalRisk } from '../../utils/security.js';
import { createComponentState } from '../../utils/component-state.js';

export class TextInput {
    constructor(options = {}) {
        this.options = {
            type: 'text',
            placeholder: '',
            value: '',
            label: '',
            size: 'medium',
            disabled: false,
            readonly: false,
            required: false,
            error: '',
            hint: '',
            maxLength: null,
            width: '100%',
            enableSecurity: true,
            onChange: null,
            onBlur: null,
            ...options
        };

        this.input = null;
        this.message = null;
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
                value: String(payload?.value ?? '')
            }),
            CLEAR: (state) => ({
                ...state,
                value: '',
                validation: this._defaultValidationState()
            }),
            SET_DISABLED: (state, payload) => ({
                ...state,
                availability: payload?.disabled ? 'disabled' : 'enabled',
                interaction: payload?.disabled ? 'idle' : state.interaction
            }),
            SET_ERROR: (state, payload) => ({
                ...state,
                validation: {
                    status: 'error',
                    message: String(payload?.error ?? '')
                }
            }),
            CLEAR_ERROR: (state) => ({
                ...state,
                validation: this._defaultValidationState()
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
            value: String(this.options.value ?? ''),
            readonly: !!this.options.readonly,
            validation: this.options.error
                ? { status: 'error', message: this.options.error }
                : this._defaultValidationState()
        };
    }

    _defaultValidationState() {
        return this.options.hint
            ? { status: 'hint', message: this.options.hint }
            : { status: 'idle', message: '' };
    }

    _create() {
        const {
            label,
            type,
            placeholder,
            value,
            size,
            disabled,
            readonly,
            required,
            error,
            hint,
            maxLength,
            width
        } = this.options;

        const sizeStyles = {
            small: { height: '32px', padding: '0 8px', fontSize: 'var(--cl-font-size-md)' },
            medium: { height: '40px', padding: '0 12px', fontSize: 'var(--cl-font-size-lg)' },
            large: { height: '48px', padding: '0 16px', fontSize: 'var(--cl-font-size-xl)' }
        }[size] || { height: '40px', padding: '0 12px', fontSize: 'var(--cl-font-size-lg)' };

        const container = document.createElement('div');
        container.className = 'text-input-container';
        container.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 4px;
            width: ${width};
        `;

        if (label) {
            const labelEl = document.createElement('label');
            labelEl.className = 'text-input__label';
            labelEl.innerHTML = `${escapeHtml(label)}${required ? '<span style="color: var(--cl-danger); margin-left: 2px;">*</span>' : ''}`;
            labelEl.style.cssText = 'font-size: var(--cl-font-size-md); font-weight: 500; color: var(--cl-text);';
            container.appendChild(labelEl);
        }

        const input = document.createElement('input');
        input.className = 'text-input';
        input.type = type;
        input.placeholder = placeholder;
        input.value = value;
        input.disabled = disabled;
        input.readOnly = readonly;
        if (maxLength) input.maxLength = maxLength;
        input.style.cssText = `
            width: 100%;
            height: ${sizeStyles.height};
            padding: ${sizeStyles.padding};
            font-size: ${sizeStyles.fontSize};
            font-family: inherit;
            border: 1px solid ${error ? 'var(--cl-danger)' : 'var(--cl-border)'};
            border-radius: var(--cl-radius-md);
            outline: none;
            transition: all var(--cl-transition);
            background: ${disabled ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)'};
            color: ${disabled ? 'var(--cl-text-placeholder)' : 'var(--cl-text)'};
        `;

        input.addEventListener('focus', () => {
            this.send('FOCUS');
        });

        input.addEventListener('blur', () => {
            this.send('BLUR');

            if (this.options.enableSecurity) {
                this._validateSecurity(input.value);
            }

            if (this.options.onBlur) {
                this.options.onBlur(input.value);
            }
        });

        input.addEventListener('input', () => {
            if (this.snapshot().validation.status === 'error') {
                this.send('CLEAR_ERROR');
            }

            this.send('SET_VALUE', { value: input.value });

            if (this.options.onChange) {
                this.options.onChange(input.value);
            }
        });

        container.appendChild(input);
        this.input = input;

        if (error || hint) {
            const message = document.createElement('span');
            message.className = error ? 'text-input__error' : 'text-input__hint';
            message.textContent = error || hint;
            message.style.cssText = `
                font-size: var(--cl-font-size-sm);
                color: ${error ? 'var(--cl-danger)' : 'var(--cl-text-muted)'};
            `;
            container.appendChild(message);
            this.message = message;
        }

        return container;
    }

    _syncOptionsFromState(state) {
        this.options.value = state.value;
        this.options.disabled = state.availability === 'disabled';
        this.options.error = state.validation.status === 'error' ? state.validation.message : '';
    }

    _applyState() {
        const state = this.snapshot();

        if (this.element) {
            this.element.style.display = state.visibility === 'hidden' ? 'none' : '';
        }

        if (this.input) {
            this.input.value = state.value;
            this.input.disabled = state.availability === 'disabled';
            this.input.readOnly = state.readonly;
            this.input.style.background = state.availability === 'disabled' ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)';
            this.input.style.color = state.availability === 'disabled' ? 'var(--cl-text-placeholder)' : 'var(--cl-text)';
            this.input.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'text';

            if (state.validation.status === 'error') {
                this.input.style.borderColor = 'var(--cl-danger)';
                this.input.style.boxShadow = '0 0 0 3px rgba(var(--cl-danger-rgb), 0.1)';
            } else if (state.interaction === 'focused' && state.availability !== 'disabled') {
                this.input.style.borderColor = 'var(--cl-primary)';
                this.input.style.boxShadow = '0 0 0 3px rgba(var(--cl-primary-rgb), 0.1)';
            } else {
                this.input.style.borderColor = 'var(--cl-border)';
                this.input.style.boxShadow = 'none';
            }
        }

        const messageText = state.validation.message;
        if (!this.message && messageText) {
            const message = document.createElement('span');
            message.className = state.validation.status === 'error' ? 'text-input__error' : 'text-input__hint';
            message.style.cssText = `font-size: var(--cl-font-size-sm);`;
            this.element.appendChild(message);
            this.message = message;
        }

        if (this.message) {
            this.message.textContent = messageText;
            this.message.className = state.validation.status === 'error' ? 'text-input__error' : 'text-input__hint';
            this.message.style.color = state.validation.status === 'error' ? 'var(--cl-danger)' : 'var(--cl-text-muted)';
        }
    }

    _validateSecurity(value) {
        if (!value) return true;

        const sqlRisk = hasSqlInjectionRisk(value);
        const pathRisk = hasPathTraversalRisk(value);

        if (sqlRisk) {
            this.setError('Security Alert: SQL Injection risk detected.');
            console.warn('[Security] SQL Injection pattern found:', value);
            return false;
        }

        if (pathRisk) {
            this.setError('Security Alert: Path Traversal risk detected.');
            console.warn('[Security] Path Traversal pattern found:', value);
            return false;
        }

        this.clearError();
        return true;
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

    getValue() {
        return this.snapshot().value;
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

    setError(error) {
        this.send('SET_ERROR', { error });
    }

    clearError() {
        this.send('CLEAR_ERROR');
    }

    show() {
        this.send('SHOW');
    }

    hide() {
        this.send('HIDE');
    }

    focus() {
        this.input?.focus?.();
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

export default TextInput;
