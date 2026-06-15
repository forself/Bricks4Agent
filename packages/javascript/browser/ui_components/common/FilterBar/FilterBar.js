import { createComponentState } from '../../utils/component-state.js';
import { Dropdown } from '../../form/Dropdown/Dropdown.js';
import { Checkbox } from '../../form/Checkbox/Checkbox.js';
import { Text } from '../Text/Text.js';

/**
 * FilterBar — 篩選列(複合)。每個篩選 = Text(label)+ Dropdown(select)/Checkbox。確定性。
 */
export class FilterBar {
    constructor(options = {}) {
        this.options = { filters: [], onChange: null, ...options };
        this.element = null;
        this._items = [];   // { def, control }
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
        if (document.getElementById('filterbar-component-styles')) return;
        const s = document.createElement('style');
        s.id = 'filterbar-component-styles';
        s.textContent = `
            .cl-filterbar { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; }
            .cl-filterbar-item { display: flex; gap: 6px; align-items: center; }
        `;
        document.head.appendChild(s);
    }

    _emit() { if (typeof this.options.onChange === 'function') this.options.onChange(this.getValues()); }

    _create() {
        this.element = document.createElement('div');
        this.element.className = 'cl-filterbar';
        (Array.isArray(this.options.filters) ? this.options.filters : []).forEach((def) => {
            const item = document.createElement('div');
            item.className = 'cl-filterbar-item';
            let control;
            if (def.type === 'checkbox') {
                control = new Checkbox({ label: def.label ?? '', value: !!def.defaultValue });
            } else {
                const label = new Text({ text: def.label ?? '', variant: 'label' });
                label.mount(item);
                this._items.push({ def: null, control: label });
                control = new Dropdown({ options: def.options ?? [], value: def.defaultValue ?? null });
            }
            (control.mount ? control.mount(item) : control.render?.(item));
            this.element.appendChild(item);
            this._items.push({ def, control });
        });
        this.element.addEventListener('change', () => this._emit());
        this.element.addEventListener('click', (e) => { if (e.target.closest('.cl-dropdown, .cl-checkbox')) this._emit(); });
    }

    getValues() {
        const out = {};
        this._items.filter((i) => i.def).forEach(({ def, control }) => {
            out[def.key] = typeof control.getValue === 'function' ? control.getValue() : null;
        });
        return out;
    }

    _applyState() { if (this.element) this.element.style.display = this.snapshot().visibility === 'hidden' ? 'none' : ''; }
    snapshot() { return this._state.snapshot(); }
    send(e, p = null) { const n = this._state.send(e, p); this._applyState(); return n; }

    mount(container) {
        const t = typeof container === 'string' ? document.querySelector(container) : container;
        if (!t) { console.warn('[FilterBar] mount target not found:', container); return this; }
        t.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this._items.forEach((i) => i.control?.destroy?.()); this._items = []; this.send('DESTROY'); this.element?.remove(); this.element = null; }
}

export default FilterBar;
