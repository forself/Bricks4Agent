import { createComponentState } from '../../utils/component-state.js';

const SAFE_PROTOCOLS = new Set(['http:', 'https:', 'mailto:', 'tel:']);

/**
 * Link — 受控連結原子。
 * scope:internal / external / none。external 自動 target=_blank + rel=noopener noreferrer。
 * none = 純文字(無 href)。協定白名單;不安全 / 未知協定 = fail-closed(去 href)。
 */
export class Link {
    static SCOPES = { INTERNAL: 'internal', EXTERNAL: 'external', NONE: 'none' };

    constructor(options = {}) {
        this.options = {
            text: '',
            href: '',
            scope: Link.SCOPES.INTERNAL,
            onClick: null,
            disabled: false,
            ...options
        };
        this.element = null;
        this._onClick = (event) => {
            if (this.snapshot().content.disabled) { event.preventDefault(); return; }
            if (typeof this.options.onClick === 'function') this.options.onClick(event);
        };
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            SHOW: (s) => ({ ...s, visibility: 'visible' }),
            HIDE: (s) => ({ ...s, visibility: 'hidden' }),
            SET_DISABLED: (s, p) => ({ ...s, content: { ...s.content, disabled: !!p?.disabled } }),
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
                href: String(this.options.href ?? ''),
                scope: this.options.scope,
                disabled: !!this.options.disabled
            }
        };
    }

    static _sanitizeHref(href) {
        const value = String(href ?? '').trim();
        if (!value) return '';
        if (value.startsWith('/') || value.startsWith('#') || value.startsWith('?')) return value;
        try {
            const url = new URL(value, 'https://local.invalid');
            return SAFE_PROTOCOLS.has(url.protocol) ? value : '';
        } catch {
            return '';
        }
    }

    _injectStyles() {
        if (document.getElementById('link-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'link-component-styles';
        style.textContent = `
            .cl-link { font-family: var(--cl-font-family); color: var(--cl-primary); text-decoration: none; cursor: pointer; }
            .cl-link:hover { text-decoration: underline; }
            .cl-link--none { color: var(--cl-text-primary); cursor: default; text-decoration: none; }
            .cl-link--disabled { color: var(--cl-text-disabled); cursor: not-allowed; pointer-events: none; }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement('a');
        this.element.addEventListener('click', this._onClick);
    }

    _applyState() {
        if (!this.element) return;
        const { content, visibility } = this.snapshot();
        const classes = ['cl-link', `cl-link--${content.scope}`];
        if (content.disabled) classes.push('cl-link--disabled');
        this.element.className = classes.join(' ');
        this.element.style.display = visibility === 'hidden' ? 'none' : '';
        this.element.textContent = content.text;

        const safe = content.scope === Link.SCOPES.NONE ? '' : Link._sanitizeHref(content.href);
        if (safe && !content.disabled) {
            this.element.setAttribute('href', safe);
            if (content.scope === Link.SCOPES.EXTERNAL) {
                this.element.setAttribute('target', '_blank');
                this.element.setAttribute('rel', 'noopener noreferrer');
            } else {
                this.element.removeAttribute('target');
                this.element.removeAttribute('rel');
            }
        } else {
            this.element.removeAttribute('href');
            this.element.removeAttribute('target');
            this.element.removeAttribute('rel');
        }
    }

    snapshot() { return this._state.snapshot(); }

    send(event, payload = null) {
        const next = this._state.send(event, payload);
        this._applyState();
        return next;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Link] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getHref() { return this.element?.getAttribute('href') ?? null; }
    setDisabled(disabled) { this.send('SET_DISABLED', { disabled }); return this; }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() {
        this.element?.removeEventListener('click', this._onClick);
        this.send('DESTROY');
        this.element?.remove();
        this.element = null;
    }
}

export default Link;
