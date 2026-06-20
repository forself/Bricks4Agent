import { createComponentState } from '../../utils/component-state.js';
import { TextInput } from '../TextInput/index.js';
import { Textarea } from '../Textarea/index.js';
import { NumberInput } from '../NumberInput/index.js';
import { Dropdown } from '../Dropdown/index.js';
import { Checkbox } from '../Checkbox/index.js';
import { Text } from '../../common/Text/index.js';
import { BasicButton } from '../../common/BasicButton/index.js';

/**
 * Form — 通用資料輸入表單(複合)。每欄由原子組成(label + 對應輸入原子 + error),加送出/重設 + 必填驗證。
 * 完全展開到原子;確定性。
 */
export class Form {
    constructor(options = {}) {
        this.options = {
            fields: [],
            submitLabel: '送出',
            resetLabel: '重設',
            onSubmit: null,
            onReset: null,
            ...options
        };
        this.element = null;
        this._fields = [];     // { def, input, errorText }
        this._buttons = [];
        this._state = createComponentState(
            { lifecycle: 'created', visibility: 'visible' },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                SHOW: (s) => ({ ...s, visibility: 'visible' }),
                HIDE: (s) => ({ ...s, visibility: 'hidden' }),
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._injectStyles();
        this._create();
        this._applyState();
    }

    _injectStyles() {
        if (document.getElementById('form-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'form-component-styles';
        s.textContent = `
            .cl-form { display: flex; flex-direction: column; gap: 14px; }
            .cl-form-field { display: flex; flex-direction: column; gap: 4px; }
            .cl-form-error .cl-text { color: var(--cl-danger); }
            .cl-form-actions { display: flex; gap: 8px; }
        `;
        document.head.appendChild(s);
    }

    _buildInput(def) {
        const common = { value: def.defaultValue ?? '', placeholder: def.placeholder ?? '' };
        switch (def.type) {
            case 'textarea': return new Textarea(common);
            case 'number': return new NumberInput({ value: def.defaultValue ?? null });
            case 'select': return new Dropdown({ options: def.options ?? [], value: def.defaultValue ?? null });
            case 'checkbox': return new Checkbox({ label: def.label ?? '', value: !!def.defaultValue });
            default: return new TextInput(common);
        }
    }

    _create() {
        this.element = document.createElement('form');
        this.element.className = 'cl-form';
        this.element.addEventListener('submit', (e) => { e.preventDefault(); this._submit(); });

        (Array.isArray(this.options.fields) ? this.options.fields : []).forEach((def) => {
            const wrap = document.createElement('div');
            wrap.className = 'cl-form-field';
            if (def.label && def.type !== 'checkbox') {
                const label = new Text({ text: def.required ? `${def.label} *` : def.label, variant: 'label' });
                label.mount(wrap);
                this._fields.push({ def: null, input: label });
            }
            const input = this._buildInput(def);
            (input.mount ? input.mount(wrap) : input.render?.(wrap));
            const errWrap = document.createElement('div');
            errWrap.className = 'cl-form-error';
            wrap.appendChild(errWrap);
            const errorText = new Text({ text: '', variant: 'caption' });
            errorText.mount(errWrap);
            this.element.appendChild(wrap);
            this._fields.push({ def, input, errorText });
        });

        const actions = document.createElement('div');
        actions.className = 'cl-form-actions';
        const submit = new BasicButton({ type: 'custom', customLabel: this.options.submitLabel, showIcon: false, variant: 'primary', onClick: () => this._submit() });
        const reset = new BasicButton({ type: 'custom', customLabel: this.options.resetLabel, showIcon: false, variant: 'secondary', onClick: () => this.reset() });
        (submit.mount ? submit.mount(actions) : submit.render?.(actions));
        (reset.mount ? reset.mount(actions) : reset.render?.(actions));
        this.element.appendChild(actions);
        this._buttons.push(submit, reset);
    }

    _fieldValue(input) {
        if (typeof input.getValue === 'function') return input.getValue();
        if (typeof input.getChecked === 'function') return input.getChecked();
        return null;
    }

    getValues() {
        const out = {};
        this._fields.filter((f) => f.def).forEach(({ def, input }) => { out[def.name] = this._fieldValue(input); });
        return out;
    }

    validate() {
        let ok = true;
        this._fields.filter((f) => f.def).forEach(({ def, input, errorText }) => {
            const v = this._fieldValue(input);
            const empty = v === '' || v === null || v === undefined;
            if (def.required && empty) { errorText?.setText(`${def.label || def.name} 為必填`); ok = false; }
            else errorText?.setText('');
        });
        return ok;
    }

    _submit() {
        if (!this.validate()) return;
        if (typeof this.options.onSubmit === 'function') this.options.onSubmit(this.getValues());
    }

    reset() {
        this._fields.filter((f) => f.def).forEach(({ def, input, errorText }) => {
            if (typeof input.setValue === 'function') input.setValue(def.defaultValue ?? '');
            errorText?.setText('');
        });
        if (typeof this.options.onReset === 'function') this.options.onReset();
        return this;
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[Form] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() {
        this._fields.forEach((f) => f.input?.destroy?.());
        this._fields.forEach((f) => f.errorText?.destroy?.());
        this._buttons.forEach((b) => b.destroy?.());
        this._fields = []; this._buttons = [];
        this.send('DESTROY'); this.element?.remove(); this.element = null;
    }
}

export default Form;
