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
        this._injectStyles();
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

    _injectStyles() {
        if (document.getElementById('heading-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'heading-component-styles';
        style.textContent = `
            .cl-heading { font-family: var(--cl-font-family); color: var(--cl-text-primary); margin: 0; font-weight: 700; line-height: 1.3; }
            .cl-heading--1 { font-size: var(--cl-font-size-2xl); }
            .cl-heading--2 { font-size: var(--cl-font-size-xl); }
            .cl-heading--3 { font-size: var(--cl-font-size-lg); }
            .cl-heading--4 { font-size: var(--cl-font-size-md); }
            .cl-heading--5 { font-size: var(--cl-font-size-sm); }
            .cl-heading--6 { font-size: var(--cl-font-size-xs); }
            .cl-heading--left { text-align: left; }
            .cl-heading--center { text-align: center; }
            .cl-heading--right { text-align: right; }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement(`h${this.snapshot().content.level}`);
    }

    _applyState() {
        if (!this.element) return;
        const { content, visibility } = this.snapshot();
        this.element.className = ['cl-heading', `cl-heading--${content.level}`, `cl-heading--${content.align}`].join(' ');
        this.element.style.display = visibility === 'hidden' ? 'none' : '';
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
