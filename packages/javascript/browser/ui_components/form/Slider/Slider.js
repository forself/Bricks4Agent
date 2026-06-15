import { createComponentState } from '../../utils/component-state.js';

/**
 * Slider — 範圍滑桿原子(input[type=range])。葉子,確定性。
 */
export class Slider {
    constructor(options = {}) {
        this.options = {
            min: 0, max: 100, step: 1, value: 0,
            disabled: false, onChange: null, ...options
        };
        this.element = null;
        this.input = null;
        this._onInput = () => {
            this.send('SET_VALUE', { value: Number(this.input.value) });
            if (typeof this.options.onChange === 'function') this.options.onChange(this.getValue());
        };
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (s) => ({ ...s, lifecycle: 'mounted' }),
            SHOW: (s) => ({ ...s, visibility: 'visible' }),
            HIDE: (s) => ({ ...s, visibility: 'hidden' }),
            SET_VALUE: (s, p) => ({ ...s, content: { ...s.content, value: this._clamp(Number(p?.value)) } }),
            SET_DISABLED: (s, p) => ({ ...s, availability: p?.disabled ? 'disabled' : 'enabled' }),
            DESTROY: (s) => ({ ...s, lifecycle: 'destroyed' })
        });
        this._injectStyles();
        this._create();
        this._applyState();
    }

    _clamp(value) {
        if (Number.isNaN(value)) return this.options.min;
        return Math.min(this.options.max, Math.max(this.options.min, value));
    }

    _buildInitialState() {
        return {
            lifecycle: 'created', visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            content: { value: this._clamp(Number(this.options.value)) }
        };
    }

    _injectStyles() {
        if (document.getElementById('slider-component-styles')) return;
        const style = document.createElement('style');
        style.id = 'slider-component-styles';
        style.textContent = `
            .cl-slider { width: 100%; accent-color: var(--cl-primary); cursor: pointer; }
            .cl-slider:disabled { cursor: not-allowed; opacity: 0.5; }
        `;
        document.head.appendChild(style);
    }

    _create() {
        this.element = document.createElement('input');
        this.input = this.element;
        this.input.type = 'range';
        this.input.className = 'cl-slider';
        this.input.min = this.options.min;
        this.input.max = this.options.max;
        this.input.step = this.options.step;
        this.input.addEventListener('input', this._onInput);
    }

    _applyState() {
        if (!this.input) return;
        const s = this.snapshot();
        this.input.value = String(s.content.value);
        this.input.disabled = s.availability === 'disabled';
        this.input.style.display = s.visibility === 'hidden' ? 'none' : '';
    }

    snapshot() { return this._state.snapshot(); }

    send(event, payload = null) {
        const next = this._state.send(event, payload);
        this._applyState();
        return next;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Slider] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getValue() { return this.snapshot().content.value; }
    setValue(value) { this.send('SET_VALUE', { value }); return this; }
    setDisabled(disabled) { this.send('SET_DISABLED', { disabled }); return this; }
    show() { this.send('SHOW'); return this; }
    hide() { this.send('HIDE'); return this; }
    destroy() { this.input?.removeEventListener('input', this._onInput); this.send('DESTROY'); this.element?.remove(); this.element = null; this.input = null; }
}

export default Slider;
