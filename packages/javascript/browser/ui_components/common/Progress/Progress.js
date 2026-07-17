/**
 * Progress - 進度指示器元件
 *
 * 提供線性（bar）與圓形（circle）兩種進度顯示模式，
 * 支援確定值與不確定（indeterminate）動畫。
 *
 * SVG 禁用政策:環形變體已改為 Canvas arc 繪製。
 * 線性條(bar)維持 DOM(已合規,無 SVG)。
 *
 * 動畫策略:
 *   bar indeterminate  — 保留 WAAPI(DOM 元素,已合規)。
 *   circle indeterminate — rAF 補間旋轉角度;canvas 自行重繪(風險小:不依賴
 *     SVG WAAPI strokeDashoffset 動畫屬性,僅操作純數字狀態後 clearRect/arc)。
 *   circle determinate  — 靜態比例直接繪製(setValue 呼叫後立即重繪)。
 *
 * @author MAGI System
 * @version 2.0.0 (Canvas 版)
 *
 * @example
 *   const bar = new Progress({ value: 60, variant: 'success', showText: true });
 *   bar.render(document.getElementById('app'));
 *
 *   const circle = new Progress({ type: 'circle', value: 75, size: 'large' });
 *   circle.render(document.getElementById('app'));
 */
import { onThemeChange, resolveTokens, FALLBACK_PAINT } from '../../utils/theme-bus.js';

/**
 * @typedef {'bar'|'circle'} ProgressType
 * @typedef {'primary'|'success'|'warning'|'danger'} ProgressVariant
 * @typedef {'small'|'medium'|'large'} ProgressSize
 */

/**
 * @typedef {Object} ProgressOptions
 * @property {number}           [value=0]            - Current value (0-max)
 * @property {number}           [max=100]            - Maximum value
 * @property {ProgressVariant}  [variant='primary']  - Colour variant
 * @property {ProgressType}     [type='bar']         - Display type
 * @property {ProgressSize}     [size='medium']      - Size preset
 * @property {boolean}          [showText=false]     - Show percentage label
 * @property {boolean}          [indeterminate=false] - Indeterminate animation
 */

/** Variant name to CSS variable mapping */
const VARIANT_MAP = {
    primary: '--cl-primary',
    success: '--cl-success',
    warning: '--cl-warning',
    danger:  '--cl-danger',
};

/** Size presets for the bar type (track height in px) */
const BAR_SIZE = { small: 4, medium: 8, large: 12 };

/** Size presets for the circle type (diameter in px) */
const CIRCLE_SIZE = { small: 48, medium: 80, large: 120 };

/** Stroke width presets for circle type */
const CIRCLE_STROKE = { small: 4, medium: 6, large: 8 };

export class Progress {
    /**
     * Create a Progress instance.
     * @param {ProgressOptions} options
     */
    constructor(options = {}) {
        /** @type {ProgressOptions} */
        this.options = {
            value: 0,
            max: 100,
            variant: 'primary',
            type: 'bar',
            size: 'medium',
            showText: false,
            indeterminate: false,
            ...options,
        };

        /** @type {HTMLElement|null} */
        this.element = null;
        /** @private */
        this._container = null;
        /** @private Web Animations API handles (bar indeterminate only) */
        this._animations = [];
        /** @private canvas refs (circle type) */
        this._canvas = null;
        this._ctx = null;
        this._offTheme = null;
        /** @private rAF for circle indeterminate */
        this._indRaf = 0;
        this._indAngle = 0;   // rotating start angle (radians)
        this._destroyed = false;

        this._create();
    }

    /* ------------------------------------------------------------------ */
    /*  DOM creation                                                      */
    /* ------------------------------------------------------------------ */

    /** @private Build the element tree. */
    _create() {
        if (this.options.type === 'circle') {
            this._createCircle();
        } else {
            this._createBar();
        }
    }

    /** @private Create linear bar DOM (no SVG — already compliant). */
    _createBar() {
        const { variant, size, showText, indeterminate, value, max } = this.options;
        const height = BAR_SIZE[size] || BAR_SIZE.medium;
        const pct = this._pct();

        const wrapper = document.createElement('div');
        wrapper.className = 'cl-progress-wrapper';
        wrapper.style.cssText = 'display: inline-flex; align-items: center; gap: 8px; width: 100%;';

        const track = document.createElement('div');
        track.className = 'cl-progress-bar-track';
        track.style.cssText = [
            'width: 100%;',
            'background: var(--cl-bg-subtle);',
            'border-radius: var(--cl-radius-pill);',
            'overflow: hidden;',
            'position: relative;',
            `height: ${height}px;`
        ].join(' ');
        track.setAttribute('role', 'progressbar');
        track.setAttribute('aria-valuemin', '0');
        track.setAttribute('aria-valuemax', String(max));

        const fill = document.createElement('div');
        fill.className = 'cl-progress-bar-fill';
        fill.style.cssText = [
            'height: 100%;',
            'border-radius: var(--cl-radius-pill);',
            'transition: width var(--cl-transition);',
            `background: var(${VARIANT_MAP[variant] || VARIANT_MAP.primary});`
        ].join(' ');

        if (indeterminate) {
            fill.classList.add('cl-progress-bar-fill--indeterminate');
            fill.style.position = 'absolute';
            fill.style.top = '0';
            fill.style.left = '-35%';
            fill.style.width = '35%';
            fill.style.transition = 'none';
            track.removeAttribute('aria-valuenow');

            // WAAPI (CSP-safe, DOM element — no SVG)
            if (typeof fill.animate === 'function') {
                this._animations.push(fill.animate(
                    [
                        { left: '-35%', width: '35%', offset: 0 },
                        { left: '100%', width: '35%', offset: 0.6 },
                        { left: '100%', width: '35%', offset: 1 }
                    ],
                    { duration: 1800, iterations: Infinity, easing: 'ease-in-out' }
                ));
            }
        } else {
            fill.style.width = `${pct}%`;
            track.setAttribute('aria-valuenow', String(value));
        }

        track.appendChild(fill);
        wrapper.appendChild(track);

        if (showText && !indeterminate) {
            const text = document.createElement('span');
            text.className = 'cl-progress-text';
            text.textContent = `${Math.round(pct)}%`;
            text.style.cssText = 'font-size: var(--cl-font-size-xs); color: var(--cl-text-secondary); white-space: nowrap; font-family: var(--cl-font-family);';
            wrapper.appendChild(text);
        }

        this.element = wrapper;
        /** @private */
        this._fill = fill;
        /** @private */
        this._track = track;
        /** @private */
        this._textEl = wrapper.querySelector('.cl-progress-text') || null;
    }

    /** @private Create circular Canvas DOM. */
    _createCircle() {
        const { size, showText, indeterminate, value, max } = this.options;
        const diameter = CIRCLE_SIZE[size] || CIRCLE_SIZE.medium;

        const wrapper = document.createElement('div');
        wrapper.className = 'cl-progress-circle-wrapper';
        wrapper.style.cssText = [
            'display: inline-flex;',
            'align-items: center;',
            'justify-content: center;',
            'position: relative;',
            `width: ${diameter}px;`,
            `height: ${diameter}px;`
        ].join(' ');

        /* aria on wrapper div (canvas has no progressbar role equivalent) */
        wrapper.setAttribute('role', 'progressbar');
        wrapper.setAttribute('aria-valuemin', '0');
        wrapper.setAttribute('aria-valuemax', String(max));
        if (!indeterminate) wrapper.setAttribute('aria-valuenow', String(value));

        const dpr = typeof window !== 'undefined' ? (window.devicePixelRatio || 1) : 1;
        const canvas = document.createElement('canvas');
        canvas.width  = Math.round(diameter * dpr);
        canvas.height = Math.round(diameter * dpr);
        canvas.style.cssText = `width: ${diameter}px; height: ${diameter}px; display: block;`;
        wrapper.appendChild(canvas);

        if (showText && !indeterminate) {
            const text = document.createElement('span');
            text.className = 'cl-progress-circle-text';
            text.textContent = `${Math.round(this._pct())}%`;
            text.style.cssText = [
                'position: absolute;',
                'font-size: var(--cl-font-size-xs);',
                'color: var(--cl-text-secondary);',
                'font-family: var(--cl-font-family);',
                'font-weight: 600;'
            ].join(' ');
            if (size === 'small') text.style.fontSize = 'var(--cl-font-size-2xs)';
            else if (size === 'large') text.style.fontSize = 'var(--cl-font-size-lg)';
            wrapper.appendChild(text);
        }

        this.element = wrapper;
        this._canvas = canvas;
        this._ctx = canvas.getContext('2d');
        this._textEl = wrapper.querySelector('.cl-progress-circle-text') || null;
        /* keep _track / _fill aliases pointing to element/wrapper for setValue/setVariant compat */
        this._track = wrapper;
        this._fill = null;   // canvas — no DOM fill node

        /* ThemeBus: canvas colour tokens must re-resolve on theme change */
        this._offTheme = onThemeChange(() => this._drawCircle());

        this._drawCircle();

        if (indeterminate) this._startIndeterminate();
    }

    /* ------------------------------------------------------------------ */
    /*  Canvas circle drawing                                             */
    /* ------------------------------------------------------------------ */

    _drawCircle() {
        if (this._destroyed || !this._canvas) return;
        const { size, variant, indeterminate } = this.options;
        const diameter = CIRCLE_SIZE[size] || CIRCLE_SIZE.medium;
        const stroke   = CIRCLE_STROKE[size] || CIRCLE_STROKE.medium;
        const radius   = (diameter - stroke) / 2;
        const dpr = window.devicePixelRatio || 1;
        const canvas = this._canvas;
        const ctx = this._ctx;

        /* Re-sync backing store size on DPR change */
        const bw = Math.round(diameter * dpr), bh = Math.round(diameter * dpr);
        if (canvas.width !== bw || canvas.height !== bh) {
            canvas.width = bw; canvas.height = bh;
        }

        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, diameter, diameter);

        const cx = diameter / 2, cy = diameter / 2;

        /* Resolve colour tokens from wrapper element for correct theme scope */
        const varName = VARIANT_MAP[variant] || VARIANT_MAP.primary;
        const tok = resolveTokens([varName, '--cl-bg-subtle'], this.element);
        const trackColor = tok['--cl-bg-subtle'] || FALLBACK_PAINT;
        const fillColor  = tok[varName]           || FALLBACK_PAINT;

        /* Track ring */
        ctx.beginPath();
        ctx.arc(cx, cy, radius, 0, Math.PI * 2);
        ctx.strokeStyle = trackColor;
        ctx.lineWidth = stroke;
        ctx.lineCap = 'butt';
        ctx.stroke();

        /* Fill arc */
        if (indeterminate) {
            /* Rotating arc of fixed 0.75 turn — driven by _indAngle */
            const start = this._indAngle;
            const end   = start + Math.PI * 1.5;   // 270° arc
            ctx.beginPath();
            ctx.arc(cx, cy, radius, start, end);
            ctx.strokeStyle = fillColor;
            ctx.lineWidth = stroke;
            ctx.lineCap = 'round';
            ctx.stroke();
        } else {
            const pct = this._pct();
            const startAngle = -Math.PI / 2;
            const endAngle   = startAngle + (pct / 100) * Math.PI * 2;
            ctx.beginPath();
            ctx.arc(cx, cy, radius, startAngle, endAngle);
            ctx.strokeStyle = fillColor;
            ctx.lineWidth = stroke;
            ctx.lineCap = 'round';
            ctx.stroke();
        }
    }

    /** @private rAF loop for indeterminate circle animation. */
    _startIndeterminate() {
        let last = 0;
        const step = (now) => {
            if (this._destroyed) return;
            const delta = last ? (now - last) / 1000 : 0;
            last = now;
            this._indAngle = (this._indAngle + delta * Math.PI) % (Math.PI * 2);  // 1 full turn/2s
            this._drawCircle();
            this._indRaf = requestAnimationFrame(step);
        };
        this._indRaf = requestAnimationFrame(step);
    }

    /* ------------------------------------------------------------------ */
    /*  Public API                                                        */
    /* ------------------------------------------------------------------ */

    /**
     * Mount the progress element into a container.
     * @param {HTMLElement|string} container - DOM element or CSS selector
     * @returns {Progress} this
     */
    render(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target && this.element) {
            target.appendChild(this.element);
            this._container = target;
        }
        return this;
    }

    /**
     * Update the current progress value.
     * @param {number} value - New value (clamped between 0 and max)
     * @returns {Progress} this
     */
    setValue(value) {
        const clamped = Math.max(0, Math.min(Number(value) || 0, this.options.max));
        this.options.value = clamped;

        if (this.options.indeterminate) return this;

        const pct = this._pct();

        if (this.options.type === 'circle') {
            this._track.setAttribute('aria-valuenow', String(clamped));
            this._drawCircle();
        } else {
            this._fill.style.width = `${pct}%`;
            this._track.setAttribute('aria-valuenow', String(clamped));
        }

        if (this._textEl) {
            this._textEl.textContent = `${Math.round(pct)}%`;
        }
        return this;
    }

    /**
     * Switch the colour variant.
     * @param {ProgressVariant} variant
     * @returns {Progress} this
     */
    setVariant(variant) {
        if (!VARIANT_MAP[variant]) return this;
        this.options.variant = variant;

        if (this.options.type === 'circle') {
            this._drawCircle();
        } else {
            this._fill.style.background = `var(${VARIANT_MAP[variant]})`;
        }
        return this;
    }

    /**
     * Remove the element from the DOM and clean up references.
     */
    destroy() {
        this._destroyed = true;
        this._animations.forEach(anim => anim.cancel());
        this._animations = [];
        cancelAnimationFrame(this._indRaf);
        if (this._offTheme) this._offTheme();
        this.element?.remove();
        this.element = null;
        this._fill = null;
        this._track = null;
        this._textEl = null;
        this._container = null;
        this._canvas = null;
        this._ctx = null;
    }

    /* ------------------------------------------------------------------ */
    /*  Internals                                                         */
    /* ------------------------------------------------------------------ */

    /**
     * @private
     * @returns {number} Percentage value (0-100)
     */
    _pct() {
        const { value, max } = this.options;
        if (max <= 0) return 0;
        return Math.max(0, Math.min(100, (value / max) * 100));
    }
}

export default Progress;
