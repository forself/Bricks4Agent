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
        this._create();
        this._applyState();
    }

    // CSP 合規:樣式全走元素層 CSSOM(style.cssText),不注入 <style>。
    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-code';
        this.element.style.cssText = [
            'position: relative',
            'background: var(--cl-bg-secondary)',
            'border: 1px solid var(--cl-border)',
            'border-radius: var(--cl-radius-md)',
            'overflow: auto'
        ].join('; ') + ';';
        if (this.options.language) {
            const lang = document.createElement('span');
            lang.className = 'cl-code-lang';
            lang.style.cssText = [
                'position: absolute',
                'top: 4px',
                'right: 8px',
                'font-size: var(--cl-font-size-2xs)',
                'color: var(--cl-text-secondary)',
                'text-transform: uppercase'
            ].join('; ') + ';';
            lang.textContent = String(this.options.language);
            this.element.appendChild(lang);
        }
        const pre = document.createElement('pre');
        pre.style.cssText = 'margin: 0; padding: 12px 14px;';
        this.codeEl = document.createElement('code');
        this.codeEl.style.cssText = [
            'font-family: var(--cl-font-family-mono)',
            'font-size: var(--cl-font-size-xs)',
            'color: var(--cl-text-primary)',
            'white-space: pre'
        ].join('; ') + ';';
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
