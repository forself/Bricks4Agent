/**
 * Slider - 數值滑桿(range)
 *
 * 基礎表單控制項:拖曳選數值,適合圓角/透明度/字級/間距等連續調整。
 * 原生 <input type="range">(CSP 安全),主題色以 accent-color: var(--cl-primary)。
 *
 * @example
 * const s = new Slider({ label:'圓角', min:0, max:24, step:1, value:8, unit:'px',
 *                        onInput:(v)=>apply(v), onChange:(v)=>save(v) });
 * s.mount('#host'); s.getValue(); s.setValue(12);
 */
import { escapeHtml } from '../../utils/security.js';
import { createComponentState } from '../../utils/component-state.js';

export class Slider {
    constructor(options = {}) {
        this.options = {
            label: '',
            value: 0,
            min: 0,
            max: 100,
            step: 1,
            disabled: false,
            showValue: true,
            unit: '',
            width: '100%',
            onInput: null,   // 拖曳中即時
            onChange: null,  // 放開後
            ...options
        };

        this.input = null;
        this.valueLabel = null;
        this.element = this._create();
        this._state = createComponentState({
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            value: this._clamp(Number(this.options.value))
        }, {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' }),
            SHOW: (s) => ({ ...s, visibility: 'visible' }),
            HIDE: (s) => ({ ...s, visibility: 'hidden' }),
            SET_VALUE: (s, p) => ({ ...s, value: this._clamp(Number(p?.value)) }),
            SET_DISABLED: (s, p) => ({ ...s, availability: p?.disabled ? 'disabled' : 'enabled' })
        });
        this._apply();
    }

    _clamp(v) {
        if (!Number.isFinite(v)) return this.options.min;
        return Math.min(this.options.max, Math.max(this.options.min, v));
    }

    _create() {
        const { label, min, max, step, width, unit, showValue } = this.options;
        const container = document.createElement('div');
        container.className = 'cl-slider';
        container.style.cssText = `display:flex; flex-direction:column; gap:4px; width:${width};`;

        if (label) {
            const l = document.createElement('label');
            l.className = 'cl-slider__label';
            l.style.cssText = 'font-size:var(--cl-font-size-md); color:var(--cl-text-secondary);';
            l.textContent = label;
            container.appendChild(l);
        }

        const row = document.createElement('div');
        row.style.cssText = 'display:flex; align-items:center; gap:10px;';

        const input = document.createElement('input');
        input.type = 'range';
        input.className = 'cl-slider__input';
        input.min = String(min);
        input.max = String(max);
        input.step = String(step);
        input.style.cssText = 'flex:1; accent-color:var(--cl-primary); cursor:pointer;';
        input.addEventListener('input', () => {
            this._state.send('SET_VALUE', { value: input.value });
            this._syncLabel();
            if (typeof this.options.onInput === 'function') this.options.onInput(this.getValue());
        });
        input.addEventListener('change', () => {
            if (typeof this.options.onChange === 'function') this.options.onChange(this.getValue());
        });
        this.input = input;
        row.appendChild(input);

        if (showValue) {
            const v = document.createElement('span');
            v.className = 'cl-slider__value';
            v.style.cssText = 'min-width:48px; text-align:right; font-size:var(--cl-font-size-md); color:var(--cl-text); font-variant-numeric:tabular-nums;';
            this.valueLabel = v;
            row.appendChild(v);
        }

        container.appendChild(row);
        return container;
    }

    _syncLabel() {
        if (this.valueLabel) this.valueLabel.textContent = `${this.getValue()}${escapeHtml(String(this.options.unit || ''))}`;
    }

    _apply() {
        const s = this._state.snapshot();
        if (this.input) {
            this.input.value = String(s.value);
            this.input.disabled = s.availability === 'disabled';
        }
        this.element.style.display = s.visibility === 'hidden' ? 'none' : 'flex';
        this.element.style.opacity = s.availability === 'disabled' ? '0.6' : '1';
        this._syncLabel();
    }

    getValue() { return this._state.snapshot().value; }
    setValue(value) { this._state.send('SET_VALUE', { value }); this._apply(); return this; }
    setDisabled(disabled) { this._state.send('SET_DISABLED', { disabled }); this._apply(); return this; }
    show() { this._state.send('SHOW'); this._apply(); return this; }
    hide() { this._state.send('HIDE'); this._apply(); return this; }
    snapshot() { return this._state.snapshot(); }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) { target.appendChild(this.element); this._state.send('MOUNT'); }
        return this;
    }

    destroy() {
        this._state.send('DESTROY');
        if (this.element?.parentNode) this.element.remove();
    }
}

export default Slider;
