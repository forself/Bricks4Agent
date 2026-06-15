import { createComponentState } from '../../utils/component-state.js';

/**
 * Textarea — 多行文字輸入原子。葉子;值經 .value(無 innerHTML),確定性。
 */
export class Textarea {
    constructor(options = {}) {
        this.options = {
            value: '',
            placeholder: '',
            rows: 4,
            maxLength: null,
            disabled: false,
            readonly: false,
            required: false,
            onChange: null,
            onBlur: null,
            ...options
        };
        this.element = null;
        this.textarea = null;
        this._onInput = () => {
            this.send('SET_VALUE', { value: this.textarea.value });
            if (typeof this.options.onChange === 'function') this.options.onChange(this.textarea.value);
        };
        this._onBlur = () => { if (typeof this.options.onBlur === 'function') this.options.onBlur(this.textarea.value); };
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            SHOW: (s) => ({ ...s, visibility: 'visible' }),
            HIDE: (s) => ({ ...s, visibility: 'hidden' }),
            SET_VALUE: (s, p) => ({ ...s, content: { ...s.content, value: String(p?.value ?? '') } }),
            SET_DISABLED: (s, p) => ({ ...s, availability: p?.disabled ? 'disabled' : 'enabled' }),
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
            content: { value: String(this.options.value) }
        };
    }

    _injectStyles() {
        if (document.getElementById('textarea-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'textarea-component-styles';
        style.textContent = `
            .cl-textarea { width: 100%; box-sizing: border-box; font-family: var(--cl-font-family);
                font-size: var(--cl-font-size-sm); color: var(--cl-text-primary); background: var(--cl-bg-primary);
                border: 1px solid var(--cl-border); border-radius: var(--cl-radius-md); padding: 8px 10px; resize: vertical; }
            .cl-textarea:focus { outline: none; border-color: var(--cl-primary); }
            .cl-textarea:disabled { background: var(--cl-bg-secondary); color: var(--cl-text-disabled); cursor: not-allowed; }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-textarea-wrap';
        this.textarea = document.createElement('textarea');
        this.textarea.className = 'cl-textarea';
        this.textarea.placeholder = this.options.placeholder;
        this.textarea.rows = this.options.rows;
        if (this.options.maxLength != null) this.textarea.maxLength = this.options.maxLength;
        this.textarea.readOnly = !!this.options.readonly;
        this.textarea.required = !!this.options.required;
        this.textarea.addEventListener('input', this._onInput);
        this.textarea.addEventListener('blur', this._onBlur);
        this.element.appendChild(this.textarea);
    }

    _applyState() {
        if (!this.textarea) return;
        const s = this.snapshot();
        this.textarea.value = s.content.value;
        this.textarea.disabled = s.availability === 'disabled';
        this.element.style.display = s.visibility === 'hidden' ? 'none' : '';
    }

    snapshot() { return this._state.snapshot(); }

    send(event, payload = null) {
        const next = this._state.send(event, payload);
        this._applyState();
        return next;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Textarea] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getValue() { return this.snapshot().content.value; }
    setValue(value) { this.send('SET_VALUE', { value }); return this; }
    clear() { return this.setValue(''); }
    setDisabled(disabled) { this.send('SET_DISABLED', { disabled }); return this; }
    focus() { this.textarea?.focus(); return this; }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() {
        this.textarea?.removeEventListener('input', this._onInput);
        this.textarea?.removeEventListener('blur', this._onBlur);
        this.send('DESTROY');
        this.element?.remove();
        this.element = null; this.textarea = null;
    }
}

export default Textarea;
