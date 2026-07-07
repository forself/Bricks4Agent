import { createComponentState } from '../../utils/component-state.js';

/**
 * Heading — 標題原子(h1–h6)。葉子原子:textContent,確定性,level 於建構時固定。
 */
export class Heading {
    static ALIGNS = { LEFT: 'left', CENTER: 'center', RIGHT: 'right' };

    constructor(options = {}) {
        this.options = { text: '', level: 2, align: Heading.ALIGNS.LEFT, ...options };
        this.element = null;
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            SHOW: (s) => ({ ...s, visibility: 'visible' }),
            HIDE: (s) => ({ ...s, visibility: 'hidden' }),
            SET_TEXT: (s, p) => ({ ...s, content: { ...s.content, text: String(p?.text ?? '') } }),
            DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
        });
        this._create();
        this._applyState();
    }

    _buildInitialState() {
        const level = Math.min(6, Math.max(1, parseInt(this.options.level, 10) || 2));
        return {
            lifecycle: 'created',
            visibility: 'visible',
            content: { text: String(this.options.text), level, align: this.options.align }
        };
    }

    // CSP 合規:樣式全走元素層 CSSOM(style.cssText),不注入 <style>。
    _composeCssText(content, visibility) {
        const LEVEL_SIZES = { 1: '2xl', 2: 'xl', 3: 'lg', 4: 'md', 5: 'sm', 6: 'xs' };
        const align = [Heading.ALIGNS.LEFT, Heading.ALIGNS.CENTER, Heading.ALIGNS.RIGHT].includes(content.align)
            ? content.align : Heading.ALIGNS.LEFT;
        const decl = [
            'font-family: var(--cl-font-family)',
            'color: var(--cl-text-primary)',
            'margin: 0',
            'font-weight: 700',
            'line-height: 1.3',
            `font-size: var(--cl-font-size-${LEVEL_SIZES[content.level] || 'xl'})`,
            `text-align: ${align}`
        ];
        if (visibility === 'hidden') decl.push('display: none');
        return decl.join('; ') + ';';
    }

    _create() {
        this.element = document.createElement(`h${this.snapshot().content.level}`);
    }

    _applyState() {
        if (!this.element) return;
        const { content, visibility } = this.snapshot();
        this.element.className = ['cl-heading', `cl-heading--${content.level}`, `cl-heading--${content.align}`].join(' ');
        this.element.style.cssText = this._composeCssText(content, visibility);
        this.element.textContent = content.text;
    }

    snapshot() { return this._state.snapshot(); }

    send(event, payload = null) {
        const next = this._state.send(event, payload);
        this._applyState();
        return next;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Heading] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getText() { return this.snapshot().content.text; }
    setText(text) { this.send('SET_TEXT', { text }); return this; }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default Heading;
