import { createComponentState } from '../../utils/component-state.js';

/**
 * CodeBlock — 程式碼區塊原子。葉子;textContent(無語法高亮 = 無依賴、確定性),language 僅當標籤。
 */
export class CodeBlock {
    constructor(options = {}) {
        this.options = { code: '', language: '', ...options };
        this.element = null;
        this.codeEl = null;
        this._state = createComponentState(
            { lifecycle: 'created', visibility: 'visible', content: { code: String(this.options.code) } },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                SHOW: (s) => ({ ...s, visibility: 'visible' }),
                HIDE: (s) => ({ ...s, visibility: 'hidden' }),
                SET_CODE: (s, p) => ({ ...s, content: { ...s.content, code: String(p?.code ?? '') } }),
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._injectStyles();
        this._create();
        this._applyState();
    }

    _injectStyles() {
        if (document.getElementById('codeblock-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'codeblock-component-styles';
        style.textContent = `
            .cl-code { position: relative; background: var(--cl-bg-secondary); border: 1px solid var(--cl-border);
                border-radius: var(--cl-radius-md); overflow: auto; }
            .cl-code pre { margin: 0; padding: 12px 14px; }
            .cl-code code { font-family: var(--cl-font-mono, ui-monospace, monospace); font-size: var(--cl-font-size-xs);
                color: var(--cl-text-primary); white-space: pre; }
            .cl-code-lang { position: absolute; top: 4px; right: 8px; font-size: var(--cl-font-size-2xs);
                color: var(--cl-text-secondary); text-transform: uppercase; }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-code';
        if (this.options.language) {
            const lang = document.createElement('span');
            lang.className = 'cl-code-lang';
            lang.textContent = String(this.options.language);
            this.element.appendChild(lang);
        }
        const pre = document.createElement('pre');
        this.codeEl = document.createElement('code');
        pre.appendChild(this.codeEl);
        this.element.appendChild(pre);
    }

    _applyState() {
        if (!this.codeEl) return;
        const s = this.snapshot();
        this.codeEl.textContent = s.content.code;
        this.element.style.display = s.visibility === 'hidden' ? 'none' : '';
    }

    snapshot() { return this._state.snapshot(); }
    send(event, payload = null) { const next = this._state.send(event, payload); this._applyState(); return next; }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[CodeBlock] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getCode() { return this.snapshot().content.code; }
    setCode(code) { this.send('SET_CODE', { code }); return this; }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this.send('DESTROY'); this.element?.remove(); this.element = null; this.codeEl = null; }
}

export default CodeBlock;
