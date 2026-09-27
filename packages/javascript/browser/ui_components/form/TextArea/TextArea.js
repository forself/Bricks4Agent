/**
 * TextArea - 多行純文字輸入
 *
 * 基礎表單控制項:多行純文字(非富文本)。適合備註、貼上 JSON/CSS、程式碼預覽。
 * 富文本請用 editor/WebTextEditor;本元件僅純文字,值即 textContent。
 *
 * 高度是共通行為:預設固定 `rows`(5)行並顯示捲軸,框內右下角有切換鈕,可改為
 * 「超過 rows 行時依內容自動加高」。使用者的選擇記在 localStorage,成為其他
 * TextArea 的預設。想要不同高度的引用者自行傳 `rows`;不想要切換鈕傳
 * `sizingToggle:false`;要一開始就自動加高傳 `sizing:'auto'`。
 *
 * @example
 * const ta = new TextArea({ label:'備註', rows:6, placeholder:'...', onChange:(v)=>{} });
 * ta.mount('#host'); ta.getValue(); ta.setValue('abc'); ta.setReadonly(true);
 */
import { createComponentState } from '../../utils/component-state.js';
import Locale from '../../i18n/index.js';

export const TEXTAREA_SIZING_MODES = Object.freeze(['fixed', 'auto']);
export const TEXTAREA_SIZING_STORAGE_KEY = 'b4a-textarea-sizing';
const SIZING_LABELS = Object.freeze({
    fixed: { icon: '⇕', title: rows => Locale.t('textArea.sizingFixedTitle', { rows }) },
    auto: { icon: '⇳', title: rows => Locale.t('textArea.sizingAutoTitle', { rows }) },
});

const sizingStore = {
    get() { try { return localStorage.getItem(TEXTAREA_SIZING_STORAGE_KEY); } catch { return null; } },
    set(value) { try { localStorage.setItem(TEXTAREA_SIZING_STORAGE_KEY, value); } catch { /* storage optional */ } },
};

/** 使用者上次選的高度模式(沒有就 fixed)。 */
export function preferredTextAreaSizing() {
    const saved = sizingStore.get();
    return TEXTAREA_SIZING_MODES.includes(saved) ? saved : 'fixed';
}

export class TextArea {
    constructor(options = {}) {
        this.options = {
            label: '',
            value: '',
            placeholder: '',
            rows: 5,
            disabled: false,
            readonly: false,
            maxLength: null,
            width: '100%',
            monospace: false,
            resize: 'none',     // none | vertical | horizontal | both(高度由 sizing 管理,預設不可拖拉)
            sizing: null,       // 'fixed' | 'auto';null = 使用者上次的選擇(預設 fixed)
            sizingToggle: true, // 框內右下角的固定/自動切換鈕
            autoResize: false,  // 舊參數:true 等同 sizing:'auto'
            required: false,    // 0626 Textarea 相容(合併統一為單一 TextArea)
            onChange: null,
            onInput: null,
            onBlur: null,       // 0626 Textarea 相容
            onSizingChange: null,
            ...options
        };
        const rows = Number(this.options.rows);
        this.options.rows = Number.isInteger(rows) && rows > 0 ? rows : 5;
        const requested = this.options.sizing || (this.options.autoResize ? 'auto' : null);
        this._sizing = TEXTAREA_SIZING_MODES.includes(requested) ? requested : preferredTextAreaSizing();

        this.textarea = null;
        this.toggle = null;
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

    // 依 maxLength 設定截斷輸入值(未設定或非法時原樣返回)
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
        const { label, placeholder, rows, maxLength, width, monospace, resize, sizingToggle } = this.options;
        const container = document.createElement('div');
        container.className = 'cl-textarea';
        container.style.cssText = `display:flex; flex-direction:column; gap:4px; width:${width};`;

        if (label) {
            const l = document.createElement('label');
            l.style.cssText = 'font-size:var(--cl-font-size-md); color:var(--cl-text-secondary);';
            l.textContent = label;
            container.appendChild(l);
        }

        // 框:textarea 與切換鈕共用的定位容器
        const box = document.createElement('div');
        box.className = 'cl-textarea__box';
        box.style.cssText = 'position:relative; display:block; width:100%; min-width:0;';

        const ta = document.createElement('textarea');
        ta.className = 'cl-textarea__field';
        ta.rows = rows;
        if (placeholder) ta.placeholder = placeholder;
        if (maxLength != null) ta.maxLength = maxLength;
        if (this.options.required) ta.required = true;
        const fontStack = monospace ? 'var(--cl-font-family-mono)' : 'var(--cl-font-family-cjk), var(--cl-font-family)';
        ta.style.cssText = `
            display:block; width:100%; box-sizing:border-box; padding:8px ${sizingToggle ? '30px' : '12px'} 8px 12px;
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
            this._resizeToContent();
            this._state.replace({ ...this._state.snapshot(), value: limitedValue });
            if (typeof this.options.onInput === 'function') this.options.onInput(limitedValue);
        });
        ta.addEventListener('change', () => {
            if (typeof this.options.onChange === 'function') this.options.onChange(ta.value);
        });
        this.textarea = ta;
        box.appendChild(ta);

        if (sizingToggle) {
            const toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'cl-textarea__sizing';
            toggle.tabIndex = -1;
            toggle.style.cssText = `
                position:absolute; right:4px; bottom:4px; width:22px; height:22px; padding:0;
                display:inline-flex; align-items:center; justify-content:center;
                border:1px solid var(--cl-border); border-radius:var(--cl-radius-sm);
                background:var(--cl-bg); color:var(--cl-primary); font-size:var(--cl-font-size-md); line-height:1;
                cursor:pointer; opacity:0.75; z-index:1;
            `;
            toggle.addEventListener('mousedown', (e) => e.preventDefault()); // 焦點留在 textarea
            toggle.addEventListener('mouseenter', () => { toggle.style.opacity = '1'; });
            toggle.addEventListener('mouseleave', () => { toggle.style.opacity = this._sizing === 'auto' ? '1' : '0.75'; });
            toggle.addEventListener('click', () => {
                const next = this._sizing === 'auto' ? 'fixed' : 'auto';
                sizingStore.set(next);
                this.setSizing(next);
            });
            this.toggle = toggle;
            box.appendChild(toggle);
        }

        container.appendChild(box);
        this._applySizing();
        return container;
    }

    _applySizing() {
        const ta = this.textarea;
        if (!ta) return;
        const mode = this._sizing;
        const rows = this.options.rows;
        ta.rows = rows;
        ta.dataset.sizing = mode;
        if (mode === 'fixed') {
            ta.style.height = '';
            ta.style.maxHeight = '';
            ta.style.overflowY = 'auto';
        } else {
            ta.style.maxHeight = 'none';
            ta.style.overflowY = 'hidden';
        }
        if (this.toggle) {
            const label = SIZING_LABELS[mode];
            this.toggle.textContent = label.icon;
            this.toggle.title = label.title(rows);
            this.toggle.setAttribute('aria-label', label.title(rows));
            this.toggle.setAttribute('aria-pressed', String(mode === 'auto'));
            this.toggle.style.opacity = mode === 'auto' ? '1' : '0.75';
        }
        this._resizeToContent();
    }

    /** 自動模式:高度跟著內容,但不小於 rows 行。 */
    _resizeToContent() {
        const ta = this.textarea;
        if (!ta || this._sizing !== 'auto') return;
        ta.style.height = '';
        const minHeight = ta.getBoundingClientRect().height;
        const cs = getComputedStyle(ta);
        const border = (parseFloat(cs.borderTopWidth) || 0) + (parseFloat(cs.borderBottomWidth) || 0);
        if (ta.scrollHeight > 0) ta.style.height = `${Math.max(minHeight, ta.scrollHeight + border)}px`;
    }

    getSizing() { return this._sizing; }
    setSizing(mode) {
        this._sizing = TEXTAREA_SIZING_MODES.includes(mode) ? mode : 'fixed';
        this._applySizing();
        if (typeof this.options.onSizingChange === 'function') this.options.onSizingChange(this._sizing);
        return this;
    }

    _apply() {
        const s = this._state.snapshot();
        if (this.textarea) {
            if (this.textarea.value !== s.value) this.textarea.value = s.value;
            this.textarea.disabled = s.availability === 'disabled';
            this.textarea.readOnly = s.readonly;
            if (this.toggle) this.toggle.style.display = (s.availability === 'disabled' || s.readonly) ? 'none' : '';
            this._resizeToContent();
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
        if (target) {
            target.appendChild(this.element);
            this._state.send('MOUNT');
            this._resizeToContent();
        }
        return this;
    }

    destroy() {
        this._state.send('DESTROY');
        if (this.element?.parentNode) this.element.remove();
    }
}

export default TextArea;
