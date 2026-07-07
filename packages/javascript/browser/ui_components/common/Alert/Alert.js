import { createComponentState } from '../../utils/component-state.js';
import { Icon } from '../Icon/index.js';
import { Text } from '../Text/index.js';

/**
 * Alert — 行內訊息條(複合元件)。
 * 契約:完全由原子組成(Icon + Text),展開見底到原子;狀態僅 visibility 佈線;確定性。
 */
const VARIANT_ICON = { info: 'info', success: 'check', warning: 'warning', danger: 'error' };
const VARIANT_STYLE = {
    info: { bg: 'var(--cl-info-bg, var(--cl-bg-secondary))', color: 'var(--cl-info)' },
    success: { bg: 'var(--cl-success-bg, var(--cl-bg-secondary))', color: 'var(--cl-success)' },
    warning: { bg: 'var(--cl-warning-bg, var(--cl-bg-secondary))', color: 'var(--cl-warning)' },
    danger: { bg: 'var(--cl-danger-bg, var(--cl-bg-secondary))', color: 'var(--cl-danger)' }
};

export class Alert {
    static VARIANTS = { INFO: 'info', SUCCESS: 'success', WARNING: 'warning', DANGER: 'danger' };

    constructor(options = {}) {
        this.options = {
            variant: Alert.VARIANTS.INFO,
            message: '',
            closable: false,
            onClose: null,
            ...options
        };
        this.element = null;
        this._children = [];
        this._closeBtn = null;
        this._onClose = () => { this.hide(); if (typeof this.options.onClose === 'function') this.options.onClose(); };
        this._state = createComponentState(
            { lifecycle: 'created', visibility: 'visible' },
            {
                MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
                SHOW: (s) => ({ ...s, visibility: 'visible' }),
                HIDE: (s) => ({ ...s, visibility: 'hidden' }),
                DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
            }
        );
        this._create();
        this._applyState();
    }

    _create() {
        const variant = Alert.VARIANTS[String(this.options.variant).toUpperCase()] ? this.options.variant : Alert.VARIANTS.INFO;
        const vs = VARIANT_STYLE[variant];
        this.element = document.createElement('div');
        this.element.className = `cl-alert cl-alert--${variant}`;
        this.element.setAttribute('role', 'alert');
        this.element.style.cssText = `
            display: flex;
            align-items: flex-start;
            gap: 8px;
            padding: 10px 12px;
            border-radius: var(--cl-radius-md);
            border: 1px solid ${vs.color};
            background: ${vs.bg};
            color: ${vs.color};
            font-family: var(--cl-font-family);
        `;

        const icon = new Icon({ name: VARIANT_ICON[variant], size: 'small' });
        const body = document.createElement('div');
        body.className = 'cl-alert-body';
        body.style.cssText = 'flex: 1; min-width: 0;';
        const text = new Text({ text: this.options.message, variant: 'body' });

        icon.mount(this.element);
        text.mount(body);
        text.element.style.color = 'var(--cl-text-primary)';
        this.element.appendChild(body);
        this._children.push(icon, text);

        if (this.options.closable) {
            this._closeBtn = document.createElement('button');
            this._closeBtn.type = 'button';
            this._closeBtn.className = 'cl-alert-close';
            this._closeBtn.setAttribute('aria-label', 'close');
            this._closeBtn.style.cssText = `
                background: none;
                border: none;
                cursor: pointer;
                color: inherit;
                padding: 0;
                display: inline-flex;
            `;
            const closeIcon = new Icon({ name: 'close', size: 'small' });
            closeIcon.mount(this._closeBtn);
            this._children.push(closeIcon);
            this._closeBtn.addEventListener('click', this._onClose);
            this.element.appendChild(this._closeBtn);
        }
    }

    _applyState() {
        if (!this.element) return;
        this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : 'flex';
    }

    snapshot() { return this._state.snapshot(); }
    send(event, payload = null) { const next = this._state.send(event, payload); this._applyState(); return next; }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Alert] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    setMessage(message) {
        const text = this._children.find((c) => c instanceof Text);
        text?.setText(message);
        return this;
    }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() {
        this._closeBtn?.removeEventListener('click', this._onClose);
        this._children.forEach((c) => c.destroy?.());
        this._children = [];
        this.send('DESTROY');
        this.element?.remove();
        this.element = null;
    }
}

export default Alert;
