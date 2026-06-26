import { Textarea } from '../Textarea/index.js';
import { createComponentState } from '../../utils/component-state.js';

const SEND_ICON = `<svg viewBox="0 0 24 24" fill="none" aria-hidden="true" xmlns="http://www.w3.org/2000/svg">
    <path d="M4 12L20 4L16 20L12 13L4 12Z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/>
    <path d="M12 13L20 4" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
</svg>`;

export class CommandComposer {
    constructor(options = {}) {
        this.options = {
            value: '',
            placeholder: '',
            rows: 3,
            maxLength: null,
            disabled: false,
            loading: false,
            clearOnSubmit: true,
            submitLabel: 'Send',
            ariaLabel: 'Send command',
            onChange: null,
            onSubmit: null,
            ...options
        };

        this.element = null;
        this.textarea = null;
        this.submitButton = null;
        this._onKeyDown = (event) => {
            if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
                event.preventDefault();
                this.submit();
            }
        };
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            SHOW: (s) => ({ ...s, visibility: 'visible' }),
            HIDE: (s) => ({ ...s, visibility: 'hidden' }),
            SET_VALUE: (s, p) => ({ ...s, content: { ...s.content, value: String(p?.value ?? '') } }),
            SET_DISABLED: (s, p) => ({ ...s, availability: p?.disabled ? 'disabled' : 'enabled' }),
            SET_LOADING: (s, p) => ({ ...s, operation: p?.loading ? 'loading' : 'idle' }),
            DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
        });

        this._injectStyles();
        this._create();
        this._applyState();
    }

    _buildInitialState() {
        return {
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            operation: this.options.loading ? 'loading' : 'idle',
            content: { value: String(this.options.value ?? '') }
        };
    }

    _injectStyles() {
        if (document.getElementById('command-composer-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'command-composer-component-styles';
        style.textContent = `
            .cl-command-composer {
                display: grid;
                grid-template-columns: minmax(0, 1fr) 44px;
                gap: 8px;
                align-items: end;
                width: 100%;
                box-sizing: border-box;
                font-family: var(--cl-font-family);
            }
            .cl-command-composer__input {
                min-width: 0;
            }
            .cl-command-composer__input .cl-textarea {
                min-height: 84px;
                max-height: 220px;
                resize: vertical;
            }
            .cl-command-composer__submit {
                width: 44px;
                height: 44px;
                display: inline-flex;
                align-items: center;
                justify-content: center;
                border: 1px solid var(--cl-primary);
                border-radius: var(--cl-radius-md);
                background: var(--cl-primary);
                color: var(--cl-text-inverse);
                cursor: pointer;
                transition: background var(--cl-transition), border-color var(--cl-transition), opacity var(--cl-transition);
            }
            .cl-command-composer__submit:hover:not(:disabled) {
                background: var(--cl-primary-dark);
                border-color: var(--cl-primary-dark);
            }
            .cl-command-composer__submit:focus-visible {
                outline: 2px solid var(--cl-border-focus);
                outline-offset: 2px;
            }
            .cl-command-composer__submit:disabled {
                opacity: 0.55;
                cursor: not-allowed;
            }
            .cl-command-composer__submit svg {
                width: 20px;
                height: 20px;
            }
            .cl-command-composer[hidden] {
                display: none;
            }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-command-composer';

        const inputWrap = document.createElement('div');
        inputWrap.className = 'cl-command-composer__input';

        this.textarea = new Textarea({
            value: this.options.value,
            placeholder: this.options.placeholder,
            rows: this.options.rows,
            maxLength: this.options.maxLength,
            disabled: this.options.disabled,
            onChange: (value) => this._handleInput(value)
        });
        inputWrap.appendChild(this.textarea.element);

        this.submitButton = document.createElement('button');
        this.submitButton.type = 'button';
        this.submitButton.className = 'cl-command-composer__submit';
        this.submitButton.title = this.options.submitLabel;
        this.submitButton.setAttribute('aria-label', this.options.ariaLabel || this.options.submitLabel);
        this.submitButton.innerHTML = SEND_ICON;
        this.submitButton.addEventListener('click', () => this.submit());

        this.textarea.textarea.addEventListener('keydown', this._onKeyDown);

        this.element.append(inputWrap, this.submitButton);
    }

    _handleInput(value) {
        this.send('SET_VALUE', { value });
        if (typeof this.options.onChange === 'function') {
            this.options.onChange(value);
        }
    }

    _applyState() {
        const state = this.snapshot();
        if (this.textarea) {
            this.textarea.setValue(state.content.value);
            this.textarea.setDisabled(state.availability === 'disabled' || state.operation === 'loading');
        }
        if (this.submitButton) {
            const empty = state.content.value.trim().length === 0;
            this.submitButton.disabled = state.availability === 'disabled' || state.operation === 'loading' || empty;
            this.submitButton.setAttribute('aria-busy', state.operation === 'loading' ? 'true' : 'false');
        }
        if (this.element) {
            this.element.hidden = state.visibility === 'hidden';
        }
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        const next = this._state.send(event, payload);
        this._applyState();
        return next;
    }

    submit() {
        const state = this.snapshot();
        const value = state.content.value;
        if (state.availability === 'disabled' || state.operation === 'loading' || value.trim().length === 0) {
            return this;
        }

        const detail = { value };
        this.element.dispatchEvent(new CustomEvent('commandcomposer:submit', { detail, bubbles: true }));
        const result = typeof this.options.onSubmit === 'function'
            ? this.options.onSubmit(value)
            : undefined;

        if (this.options.clearOnSubmit && result !== false) {
            this.clear();
        }

        return this;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) {
            console.warn('[CommandComposer] mount target not found:', container);
            return this;
        }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getValue() {
        return this.snapshot().content.value;
    }

    setValue(value) {
        this.send('SET_VALUE', { value });
        return this;
    }

    clear() {
        return this.setValue('');
    }

    setDisabled(disabled) {
        this.send('SET_DISABLED', { disabled });
        return this;
    }

    setLoading(loading) {
        this.send('SET_LOADING', { loading });
        return this;
    }

    focus() {
        this.textarea?.focus();
        return this;
    }

    show() {
        this.send('SHOW');
        return this;
    }

    hide() {
        this.send('HIDE');
        return this;
    }

    destroy() {
        this.send('DESTROY');
        this.textarea?.textarea?.removeEventListener('keydown', this._onKeyDown);
        this.textarea?.destroy();
        this.element?.remove();
        this.element = null;
        this.textarea = null;
        this.submitButton = null;
    }
}

export default CommandComposer;
