/**
 * TextArea - 多行純文字輸入
 *
 * 基礎表單控制項:多行純文字(非富文本)。適合備註、貼上 JSON/CSS、程式碼預覽。
 * 富文本請用 editor/WebTextEditor;本元件僅純文字,值即 textContent。
 *
 * @example
 * const ta = new TextArea({ label:'備註', rows:6, placeholder:'...', onChange:(v)=>{} });
 * ta.mount('#host'); ta.getValue(); ta.setValue('abc'); ta.setReadonly(true);
 */
import { createComponentState } from '../../utils/component-state.js';

export class TextArea {
    constructor(options = {}) {
        this.options = {
            label: '',
            value: '',
            placeholder: '',
            rows: 4,
            disabled: false,
            readonly: false,
            maxLength: null,
            width: '100%',
            monospace: false,
            resize: 'vertical', // none | vertical | horizontal | both
            required: false,    // 0626 Textarea 相容(合併統一為單一 TextArea)
            onChange: null,
            onInput: null,
            onBlur: null,       // 0626 Textarea 相容
            ...options
        };

        this.textarea = null;
        this.element = this._create();
        this._state = createComponentState({
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            readonly: !!this.options.readonly,
            value: String(this.options.value ?? '')
        }, {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' }),
            SHOW: (s) => ({ ...s, visibility: 'visible' }),
            HIDE: (s) => ({ ...s, visibility: 'hidden' }),
            SET_VALUE: (s, p) => ({ ...s, value: String(p?.value ?? '') }),
            CLEAR: (s) => ({ ...s, value: '' }),
            SET_DISABLED: (s, p) => ({ ...s, availability: p?.disabled ? 'disabled' : 'enabled' }),
            SET_READONLY: (s, p) => ({ ...s, readonly: !!p?.readonly })
        });
        this._apply();
    }

    _limitInputValue(value) {
        const text = String(value ?? '');
        const configured = this.options.maxLength;
        if (configured === null || configured === undefined || configured === '') return text;
        const limit = Number(configured);
        return Number.isInteger(limit) && limit >= 0 && text.length > limit
            ? text.slice(0, limit)
            : text;
    }

    _create() {
        const { label, placeholder, rows, maxLength, width, monospace, resize } = this.options;
        const container = document.createElement('div');
        container.className = 'cl-textarea';
        container.style.cssText = `display:flex; flex-direction:column; gap:4px; width:${width};`;

        if (label) {
            const l = document.createElement('label');
            l.style.cssText = 'font-size:var(--cl-font-size-md); color:var(--cl-text-secondary);';
            l.textContent = label;
            container.appendChild(l);
        }

        const ta = document.createElement('textarea');
        ta.className = 'cl-textarea__field';
        ta.rows = rows;
        if (placeholder) ta.placeholder = placeholder;
        if (maxLength != null) ta.maxLength = maxLength;
        if (this.options.required) ta.required = true;
        const fontStack = monospace ? 'var(--cl-font-family-mono)' : 'var(--cl-font-family-cjk), var(--cl-font-family)';
        ta.style.cssText = `
            width:100%; box-sizing:border-box; padding:8px 12px;
            font-family:${fontStack};
            font-size:var(--cl-font-size-md); line-height:1.5;
            color:var(--cl-text); background:var(--cl-bg); border:1px solid var(--cl-border);
            border-radius:var(--cl-radius-md); resize:${resize}; transition:border-color var(--cl-transition);
        `;
        ta.addEventListener('focus', () => { ta.style.borderColor = 'var(--cl-primary)'; });
        ta.addEventListener('blur', () => {
            ta.style.borderColor = 'var(--cl-border)';
            if (typeof this.options.onBlur === 'function') this.options.onBlur(ta.value);
        });
        ta.addEventListener('input', () => {
            const limitedValue = this._limitInputValue(ta.value);
            if (limitedValue !== ta.value) ta.value = limitedValue;
            this._state.replace({ ...this._state.snapshot(), value: limitedValue });
            if (typeof this.options.onInput === 'function') this.options.onInput(limitedValue);
        });
        ta.addEventListener('change', () => {
            if (typeof this.options.onChange === 'function') this.options.onChange(ta.value);
        });
        this.textarea = ta;
        container.appendChild(ta);
        return container;
    }

    _apply() {
        const s = this._state.snapshot();
        if (this.textarea) {
            if (this.textarea.value !== s.value) this.textarea.value = s.value;
            this.textarea.disabled = s.availability === 'disabled';
            this.textarea.readOnly = s.readonly;
        }
        this.element.style.display = s.visibility === 'hidden' ? 'none' : 'flex';
    }

    getValue() { return this._state.snapshot().value; }
    setValue(value) { this._state.send('SET_VALUE', { value }); this._apply(); return this; }
    clear() { this._state.send('CLEAR'); this._apply(); return this; }
    setDisabled(disabled) { this._state.send('SET_DISABLED', { disabled }); this._apply(); return this; }
    setReadonly(readonly) { this._state.send('SET_READONLY', { readonly }); this._apply(); return this; }
    focus() { this.textarea?.focus?.(); return this; }
    show() { this._state.send('SHOW'); this._apply(); return this; }
    hide() { this._state.send('HIDE'); this._apply(); return this; }
    snapshot() { return this._state.snapshot(); }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) { target.appendChild(this.element); this._state.send('MOUNT'); }
        return this;
    }

    destroy() {
        this._state.send('DESTROY');
        if (this.element?.parentNode) this.element.remove();
    }
}

export default TextArea;
