import { createComponentState } from '../../utils/component-state.js';
import { Text } from '../Text/index.js';

/**
 * List — 通用平面清單(複合)。每筆 = Text(primary)+ Text(secondary)。完全由原子組成,確定性。
 */
export class List {
    constructor(options = {}) {
        this.options = { items: [], onItemClick: null, ...options };
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
        this._create();
        this._applyState();
    }

    _create() {
        this.element = document.createElement('ul');
        this.element.className = 'cl-list';
        this.element.style.cssText = 'list-style: none; margin: 0; padding: 0;';
        const items = Array.isArray(this.options.items) ? this.options.items : [];
        items.forEach((item, idx) => {
            const li = document.createElement('li');
            const clickable = !!this.options.onItemClick;
            li.className = 'cl-list-item' + (clickable ? ' cl-list-item--clickable' : '');
            li.style.cssText = `
                padding: 10px 12px;
                border-bottom: ${idx === items.length - 1 ? 'none' : '1px solid var(--cl-border)'};
                ${clickable ? 'cursor: pointer;' : ''}
            `;
            const primary = new Text({ text: String(item.primary ?? item ?? ''), variant: 'body' });
            primary.mount(li);
            this._children.push(primary);
            if (item && item.secondary) { const sec = new Text({ text: String(item.secondary), variant: 'caption' }); sec.mount(li); this._children.push(sec); }
            if (clickable) {
                li.addEventListener('mouseenter', () => { li.style.background = 'var(--cl-bg-secondary)'; });
                li.addEventListener('mouseleave', () => { li.style.background = ''; });
                li.addEventListener('click', () => this.options.onItemClick(item));
            }
            this.element.appendChild(li);
        });
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[List] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default List;
