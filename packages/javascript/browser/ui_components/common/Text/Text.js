import { createComponentState } from '../../utils/component-state.js';

/**
 * Text — 排版原子(段落 / 行內文字)。
 * 葉子原子:只用 textContent,無 innerHTML、無 random/Date,展開確定性。
 */
export class Text {
    static VARIANTS = { BODY: 'body', CAPTION: 'caption', LABEL: 'label', MUTED: 'muted' };
    static ALIGNS = { LEFT: 'left', CENTER: 'center', RIGHT: 'right' };

    constructor(options = {}) {
        this.options = {
            text: '',
            variant: Text.VARIANTS.BODY,
            align: Text.ALIGNS.LEFT,
            tag: 'span',
            truncate: false,
            ...options
        };
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
        return {
            lifecycle: 'created',
            visibility: 'visible',
            content: {
                text: String(this.options.text),
                variant: this.options.variant,
                align: this.options.align,
                truncate: !!this.options.truncate
            }
        };
    }

    _injectStyles() {
        if (document.getElementById('text-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'text-component-styles';
        style.textContent = `
            .cl-text { font-family: var(--cl-font-family); color: var(--cl-text-primary); margin: 0; }
            .cl-text--body { font-size: var(--cl-font-size-sm); line-height: 1.6; }
            .cl-text--caption { font-size: var(--cl-font-size-xs); color: var(--cl-text-secondary); }
            .cl-text--label { font-size: var(--cl-font-size-sm); font-weight: 600; }
            .cl-text--muted { color: var(--cl-text-secondary); }
            .cl-text--left { text-align: left; }
            .cl-text--center { text-align: center; }
            .cl-text--right { text-align: right; }
            .cl-text--truncate { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        `;
        document.head.appendChild(style);
    }

    _create() {
        const tag = ['span', 'p', 'div'].includes(this.options.tag) ? this.options.tag : 'span';
        this.element = document.createElement(tag);
    }

    _applyState() {
        if (!this.element) return;
        const { content, visibility } = this.snapshot();
        const classes = ['cl-text', `cl-text--${content.variant}`, `cl-text--${content.align}`];
        if (content.truncate) classes.push('cl-text--truncate');
        this.element.className = classes.join(' ');
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
        if (!target) { console.warn('[Text] mount target not found:', container); return this; }
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

export default Text;
