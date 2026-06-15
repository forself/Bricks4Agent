import { createComponentState } from '../../utils/component-state.js';
import { Text } from '../Text/Text.js';

/**
 * DescriptionList — 鍵值表(複合)。每對 = Text(label)+ Text(value)。完全由原子組成,確定性。
 */
export class DescriptionList {
    constructor(options = {}) {
        this.options = { items: [], ...options };
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
        if (document.getElementById('desclist-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'desclist-component-styles';
        s.textContent = `
            .cl-desclist { display: grid; grid-template-columns: minmax(80px, 30%) 1fr; gap: 6px 12px; }
            .cl-desclist dt { margin: 0; }
            .cl-desclist dd { margin: 0; }
            .cl-desclist dt .cl-text { color: var(--cl-text-secondary); }
        `;
        document.head.appendChild(s);
    }

    _create() {
        this.element = document.createElement('dl');
        this.element.className = 'cl-desclist';
        (Array.isArray(this.options.items) ? this.options.items : []).forEach((item) => {
            const dt = document.createElement('dt');
            const dd = document.createElement('dd');
            const label = new Text({ text: String(item.label ?? ''), variant: 'label' });
            const value = new Text({ text: String(item.value ?? ''), variant: 'body' });
            label.mount(dt); value.mount(dd);
            this.element.appendChild(dt); this.element.appendChild(dd);
            this._children.push(label, value);
        });
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[DescriptionList] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default DescriptionList;
