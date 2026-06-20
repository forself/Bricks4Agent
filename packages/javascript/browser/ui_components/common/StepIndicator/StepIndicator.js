import { createComponentState } from '../../utils/component-state.js';
import { Text } from '../Text/index.js';

/**
 * StepIndicator — 步驟指示(複合)。每步 = 編號圈 + Text(label);確定性。
 */
export class StepIndicator {
    constructor(options = {}) {
        this.options = { steps: [], current: 1, ...options };
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
        if (document.getElementById('stepindicator-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'stepindicator-component-styles';
        s.textContent = `
            .cl-steps { display: flex; align-items: center; gap: 8px; }
            .cl-step { display: flex; flex-direction: column; align-items: center; gap: 4px; }
            .cl-step-dot { width: 24px; height: 24px; border-radius: var(--cl-radius-round); display: flex; align-items: center; justify-content: center;
                font-size: var(--cl-font-size-xs); background: var(--cl-bg-secondary); color: var(--cl-text-secondary); border: 1px solid var(--cl-border); }
            .cl-step--done .cl-step-dot, .cl-step--current .cl-step-dot { background: var(--cl-primary); color: var(--cl-text-inverse); border-color: var(--cl-primary); }
            .cl-step-line { flex: 1; height: 1px; background: var(--cl-border); min-width: 24px; }
        `;
        document.head.appendChild(s);
    }

    _stepStatus(index, def) {
        if (def.status) return def.status;
        const cur = parseInt(this.options.current, 10) || 1;
        if (index + 1 < cur) return 'done';
        if (index + 1 === cur) return 'current';
        return 'upcoming';
    }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-steps';
        const steps = Array.isArray(this.options.steps) ? this.options.steps : [];
        steps.forEach((def, i) => {
            const step = document.createElement('div');
            const status = this._stepStatus(i, def);
            step.className = `cl-step cl-step--${status}`;
            const dot = document.createElement('div');
            dot.className = 'cl-step-dot';
            dot.textContent = String(i + 1);
            step.appendChild(dot);
            const label = new Text({ text: String(def.label ?? ''), variant: 'caption', align: 'center' });
            label.mount(step);
            this._children.push(label);
            this.element.appendChild(step);
            if (i < steps.length - 1) {
                const line = document.createElement('div');
                line.className = 'cl-step-line';
                this.element.appendChild(line);
            }
        });
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[StepIndicator] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._children.forEach((c) => c.destroy?.()); this._children = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default StepIndicator;
