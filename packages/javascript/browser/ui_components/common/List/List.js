import { createComponentState } from '../../utils/component-state.js';
import { Text } from '../Text/Text.js';

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
        this._injectStyles();
        this._create();
        this._applyState();
    }

    _injectStyles() {
        if (document.getElementById('list-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'list-component-styles';
        s.textContent = `
            .cl-list { list-style: none; margin: 0; padding: 0; }
            .cl-list-item { padding: 10px 12px; border-bottom: 1px solid var(--cl-border); }
            .cl-list-item:last-child { border-bottom: none; }
            .cl-list-item--clickable { cursor: pointer; }
            .cl-list-item--clickable:hover { background: var(--cl-bg-secondary); }
        `;
        document.head.appendChild(s);
    }

    _create() {
        this.element = document.createElement('ul');
        this.element.className = 'cl-list';
        (Array.isArray(this.options.items) ? this.options.items : []).forEach((item) => {
            const li = document.createElement('li');
            li.className = 'cl-list-item' + (this.options.onItemClick ? ' cl-list-item--clickable' : '');
            const primary = new Text({ text: String(item.primary ?? item ?? ''), variant: 'body' });
            primary.mount(li);
            this._children.push(primary);
            if (item && item.secondary) { const sec = new Text({ text: String(item.secondary), variant: 'caption' }); sec.mount(li); this._children.push(sec); }
            if (this.options.onItemClick) li.addEventListener('click', () => this.options.onItemClick(item));
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
