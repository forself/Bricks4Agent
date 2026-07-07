import { Textarea } from '../TextArea/index.js';
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

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-command-composer';
        // CSP 合規:樣式走元素層 CSSOM(style.cssText),不注入 <style>。
        this.element.style.cssText = `
            display: grid;
            grid-template-columns: minmax(0, 1fr) 44px;
            gap: 8px;
            align-items: end;
            width: 100%;
            box-sizing: border-box;
            font-family: var(--cl-font-family);
        `;

        const inputWrap = document.createElement('div');
        inputWrap.className = 'cl-command-composer__input';
        inputWrap.style.cssText = 'min-width: 0;';

        this.textarea = new Textarea({
            value: this.options.value,
            placeholder: this.options.placeholder,
            rows: this.options.rows,
            maxLength: this.options.maxLength,
            disabled: this.options.disabled,
            onChange: (value) => this._handleInput(value)
        });
        // 原 .cl-command-composer__input .cl-textarea 規則 → 直接設在 TextArea 容器上
        this.textarea.element.style.setProperty('min-height', '84px');
        this.textarea.element.style.setProperty('max-height', '220px');
        this.textarea.element.style.setProperty('resize', 'vertical');
        inputWrap.appendChild(this.textarea.element);

        this.submitButton = document.createElement('button');
        this.submitButton.type = 'button';
        this.submitButton.className = 'cl-command-composer__submit';
        this.submitButton.title = this.options.submitLabel;
        this.submitButton.setAttribute('aria-label', this.options.ariaLabel || this.options.submitLabel);
        this.submitButton.innerHTML = SEND_ICON;
        this.submitButton.style.cssText = `
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
        `;
        const sendSvg = this.submitButton.querySelector('svg');
        if (sendSvg) sendSvg.style.cssText = 'width: 20px; height: 20px;';

        // :hover:not(:disabled) → 事件切換(CSP 合規)
        this.submitButton.addEventListener('mouseenter', () => {
            if (this.submitButton.disabled) return;
            this.submitButton.style.background = 'var(--cl-primary-dark)';
            this.submitButton.style.borderColor = 'var(--cl-primary-dark)';
        });
        this.submitButton.addEventListener('mouseleave', () => {
            this.submitButton.style.background = 'var(--cl-primary)';
            this.submitButton.style.borderColor = 'var(--cl-primary)';
        });
        // :focus-visible → focus/blur 事件切換
        this.submitButton.addEventListener('focus', () => {
            let focusVisible = true;
            try { focusVisible = this.submitButton.matches(':focus-visible'); } catch { /* 舊環境:一律顯示 */ }
            if (!focusVisible) return;
            this.submitButton.style.outline = '2px solid var(--cl-border-focus)';
            this.submitButton.style.outlineOffset = '2px';
        });
        this.submitButton.addEventListener('blur', () => {
            this.submitButton.style.outline = 'none';
            this.submitButton.style.outlineOffset = '0';
        });

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
            // :disabled 視覺(原 stylesheet 規則)→ 狀態更新處切換
            if (this.submitButton.disabled) {
                this.submitButton.style.opacity = '0.55';
                this.submitButton.style.cursor = 'not-allowed';
                this.submitButton.style.background = 'var(--cl-primary)';
                this.submitButton.style.borderColor = 'var(--cl-primary)';
            } else {
                this.submitButton.style.opacity = '1';
                this.submitButton.style.cursor = 'pointer';
            }
        }
        if (this.element) {
            this.element.hidden = state.visibility === 'hidden';
            // inline display:grid 會蓋過 [hidden] 的 UA 樣式,需一併切換
            this.element.style.display = state.visibility === 'hidden' ? 'none' : 'grid';
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
