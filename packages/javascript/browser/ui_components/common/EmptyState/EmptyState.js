import { createComponentState } from '../../utils/component-state.js';
import { Icon } from '../Icon/Icon.js';
import { Heading } from '../Heading/Heading.js';
import { Text } from '../Text/Text.js';
import { BasicButton } from '../BasicButton/BasicButton.js';

/**
 * EmptyState — 空狀態(複合元件)。
 * 契約:完全由原子組成(Icon + Heading + Text + BasicButton),確定性;狀態僅 visibility。
 */
export class EmptyState {
    constructor(options = {}) {
        this.options = {
            icon: 'info',
            title: '',
            description: '',
            actionLabel: '',
            onAction: null,
            ...options
        };
        this.element = null;
        this._children = [];
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
        if (document.getElementById('emptystate-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'emptystate-component-styles';
        style.textContent = `
            .cl-empty { display: flex; flex-direction: column; align-items: center; text-align: center;
                gap: 8px; padding: 32px 16px; color: var(--cl-text-secondary); }
            .cl-empty .cl-icon { color: var(--cl-text-secondary); }
            .cl-empty-action { margin-top: 8px; }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-empty';

        const icon = new Icon({ name: this.options.icon, size: 'large' });
        icon.mount(this.element);
        this._children.push(icon);

        if (this.options.title) {
            const heading = new Heading({ text: this.options.title, level: 4, align: 'center' });
            heading.mount(this.element);
            this._children.push(heading);
        }
        if (this.options.description) {
            const desc = new Text({ text: this.options.description, variant: 'caption', align: 'center' });
            desc.mount(this.element);
            this._children.push(desc);
        }
        if (this.options.actionLabel) {
            const wrap = document.createElement('div');
            wrap.className = 'cl-empty-action';
            const btn = new BasicButton({ type: 'custom', customLabel: this.options.actionLabel, showIcon: false, variant: 'primary', onClick: this.options.onAction });
            (btn.mount ? btn.mount(wrap) : btn.render?.(wrap));
            this.element.appendChild(wrap);
            this._children.push(btn);
        }
    }

    _applyState() {
        if (!this.element) return;
        this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : '';
    }

    snapshot() { return this._state.snapshot(); }
    send(event, payload = null) { const next = this._state.send(event, payload); this._applyState(); return next; }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[EmptyState] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() {
        this._children.forEach((c) => c.destroy?.());
        this._children = [];
        this.send('DESTROY');
        this.element?.remove();
        this.element = null;
    }
}

export default EmptyState;
