import { createComponentState } from '../../utils/component-state.js';
import { StatCard } from '../../social/StatCard/StatCard.js';

/**
 * StatGrid — 指標網格(複合)。grid of StatCard。確定性。
 */
export class StatGrid {
    constructor(options = {}) {
        this.options = { stats: [], columns: 4, ...options };
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
        if (document.getElementById('statgrid-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'statgrid-component-styles';
        s.textContent = `.cl-statgrid { display: grid; gap: 12px; }`;
        document.head.appendChild(s);
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-statgrid';
        const cols = Math.max(1, parseInt(this.options.columns, 10) || 1);
        this.element.style.gridTemplateColumns = `repeat(${cols}, 1fr)`;
        (Array.isArray(this.options.stats) ? this.options.stats : []).forEach((stat) => {
            const card = new StatCard({ label: stat.label ?? '', value: stat.value ?? 0, ...(stat.detail ? { detail: stat.detail } : {}) });
            (card.mount ? card.mount(this.element) : card.render?.(this.element));
            this._children.push(card);
        });
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[StatGrid] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default StatGrid;
