import { createComponentState } from '../../utils/component-state.js';
import { FeatureCard } from '../FeatureCard/FeatureCard.js';

/**
 * CardGrid — 卡片網格(複合)。grid of FeatureCard。確定性。
 */
export class CardGrid {
    constructor(options = {}) {
        this.options = { cards: [], columns: 3, ...options };
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
        if (document.getElementById('cardgrid-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'cardgrid-component-styles';
        s.textContent = `.cl-cardgrid { display: grid; gap: 16px; }`;
        document.head.appendChild(s);
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-cardgrid';
        const cols = Math.max(1, parseInt(this.options.columns, 10) || 1);
        this.element.style.gridTemplateColumns = `repeat(${cols}, 1fr)`;
        (Array.isArray(this.options.cards) ? this.options.cards : []).forEach((card) => {
            const cell = document.createElement('div');
            const fc = new FeatureCard({ ...card });
            (fc.mount ? fc.mount(cell) : fc.render?.(cell));
            this.element.appendChild(cell);
            this._children.push(fc);
        });
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[CardGrid] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default CardGrid;
