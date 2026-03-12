/**
 * Progress - 進度指示器元件
 *
 * 提供線性（bar）與圓形（circle）兩種進度顯示模式，
 * 支援確定值與不確定（indeterminate）動畫。
 *
 * @author MAGI System
 * @version 1.0.0
 *
 * @example
 *   const bar = new Progress({ value: 60, variant: 'success', showText: true });
 *   bar.render(document.getElementById('app'));
 *
 *   const circle = new Progress({ type: 'circle', value: 75, size: 'large' });
 *   circle.render(document.getElementById('app'));
 */

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
    primary: 'var(--cl-primary)',
    success: 'var(--cl-success)',
    warning: 'var(--cl-warning)',
    danger:  'var(--cl-danger)',
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

        this._injectStyles();
        this._create();
    }

    /* ------------------------------------------------------------------ */
    /*  Style injection (once per page)                                   */
    /* ------------------------------------------------------------------ */

    /** @private Inject shared CSS into &lt;head&gt; if not already present. */
    _injectStyles() {
        if (document.getElementById('cl-progress-styles')) return;

        const style = document.createElement('style');
        style.id = 'cl-progress-styles';
        style.textContent = `
            /* ---- Linear bar ---- */
            .cl-progress-bar-track {
                width: 100%;
                background: var(--cl-bg-subtle);
                border-radius: var(--cl-radius-pill);
                overflow: hidden;
                position: relative;
            }
            .cl-progress-bar-fill {
                height: 100%;
                border-radius: var(--cl-radius-pill);
                transition: width var(--cl-transition);
            }

            /* Indeterminate bar animation */
            @keyframes cl-progress-indeterminate-bar {
                0%   { left: -35%; width: 35%; }
                60%  { left: 100%; width: 35%; }
                100% { left: 100%; width: 35%; }
            }
            .cl-progress-bar-fill--indeterminate {
                position: absolute;
                top: 0;
                left: -35%;
                width: 35%;
                animation: cl-progress-indeterminate-bar 1.8s ease-in-out infinite;
            }

            /* ---- Circle (SVG) ---- */
            .cl-progress-circle {
                transform: rotate(-90deg);
            }
            .cl-progress-circle-track {
                fill: none;
                stroke: var(--cl-bg-subtle);
            }
            .cl-progress-circle-fill {
                fill: none;
                stroke-linecap: round;
                transition: stroke-dashoffset var(--cl-transition);
            }

            /* Indeterminate circle animation */
            @keyframes cl-progress-indeterminate-circle-rotate {
                0%   { transform: rotate(-90deg); }
                100% { transform: rotate(270deg); }
            }
            @keyframes cl-progress-indeterminate-circle-dash {
                0%   { stroke-dashoffset: var(--cl-progress-circumference); }
                50%  { stroke-dashoffset: calc(var(--cl-progress-circumference) * 0.25); }
                100% { stroke-dashoffset: var(--cl-progress-circumference); }
            }
            .cl-progress-circle--indeterminate {
                animation: cl-progress-indeterminate-circle-rotate 2s linear infinite;
            }
            .cl-progress-circle--indeterminate .cl-progress-circle-fill {
                animation: cl-progress-indeterminate-circle-dash 1.5s ease-in-out infinite;
                transition: none;
            }

            /* ---- Wrapper ---- */
            .cl-progress-wrapper {
                display: inline-flex;
                align-items: center;
                gap: 8px;
                width: 100%;
            }
            .cl-progress-circle-wrapper {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                position: relative;
            }
            .cl-progress-text {
                font-size: var(--cl-font-size-xs);
                color: var(--cl-text-secondary);
                white-space: nowrap;
                font-family: var(--cl-font-family);
            }
            .cl-progress-circle-text {
                position: absolute;
                font-size: var(--cl-font-size-xs);
                color: var(--cl-text-secondary);
                font-family: var(--cl-font-family);
                font-weight: 600;
            }
        `;
        document.head.appendChild(style);
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

    /** @private Create linear bar DOM. */
    _createBar() {
        const { variant, size, showText, indeterminate, value, max } = this.options;
        const height = BAR_SIZE[size] || BAR_SIZE.medium;
        const pct = this._pct();

        const wrapper = document.createElement('div');
        wrapper.className = 'cl-progress-wrapper';

        const track = document.createElement('div');
        track.className = 'cl-progress-bar-track';
        track.style.height = `${height}px`;
        track.setAttribute('role', 'progressbar');
        track.setAttribute('aria-valuemin', '0');
        track.setAttribute('aria-valuemax', String(max));

        const fill = document.createElement('div');
        fill.className = 'cl-progress-bar-fill';
        fill.style.background = VARIANT_MAP[variant] || VARIANT_MAP.primary;

        if (indeterminate) {
            fill.classList.add('cl-progress-bar-fill--indeterminate');
            track.removeAttribute('aria-valuenow');
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

    /** @private Create circular SVG DOM. */
    _createCircle() {
        const { variant, size, showText, indeterminate, value, max } = this.options;
        const diameter = CIRCLE_SIZE[size] || CIRCLE_SIZE.medium;
        const stroke = CIRCLE_STROKE[size] || CIRCLE_STROKE.medium;
        const radius = (diameter - stroke) / 2;
        const circumference = 2 * Math.PI * radius;
        const pct = this._pct();

        const wrapper = document.createElement('div');
        wrapper.className = 'cl-progress-circle-wrapper';
        wrapper.style.width = `${diameter}px`;
        wrapper.style.height = `${diameter}px`;

        const ns = 'http://www.w3.org/2000/svg';
        const svg = document.createElementNS(ns, 'svg');
        svg.setAttribute('width', String(diameter));
        svg.setAttribute('height', String(diameter));
        svg.setAttribute('viewBox', `0 0 ${diameter} ${diameter}`);
        svg.classList.add('cl-progress-circle');
        svg.setAttribute('role', 'progressbar');
        svg.setAttribute('aria-valuemin', '0');
        svg.setAttribute('aria-valuemax', String(max));

        if (indeterminate) {
            svg.classList.add('cl-progress-circle--indeterminate');
            svg.style.setProperty('--cl-progress-circumference', `${circumference}`);
            svg.removeAttribute('aria-valuenow');
        } else {
            svg.setAttribute('aria-valuenow', String(value));
        }

        const trackCircle = document.createElementNS(ns, 'circle');
        trackCircle.classList.add('cl-progress-circle-track');
        trackCircle.setAttribute('cx', String(diameter / 2));
        trackCircle.setAttribute('cy', String(diameter / 2));
        trackCircle.setAttribute('r', String(radius));
        trackCircle.setAttribute('stroke-width', String(stroke));

        const fillCircle = document.createElementNS(ns, 'circle');
        fillCircle.classList.add('cl-progress-circle-fill');
        fillCircle.setAttribute('cx', String(diameter / 2));
        fillCircle.setAttribute('cy', String(diameter / 2));
        fillCircle.setAttribute('r', String(radius));
        fillCircle.setAttribute('stroke-width', String(stroke));
        fillCircle.setAttribute('stroke', VARIANT_MAP[variant] || VARIANT_MAP.primary);
        fillCircle.setAttribute('stroke-dasharray', String(circumference));

        if (indeterminate) {
            fillCircle.setAttribute('stroke-dashoffset', String(circumference));
        } else {
            const offset = circumference - (pct / 100) * circumference;
            fillCircle.setAttribute('stroke-dashoffset', String(offset));
        }

        svg.appendChild(trackCircle);
        svg.appendChild(fillCircle);
        wrapper.appendChild(svg);

        if (showText && !indeterminate) {
            const text = document.createElement('span');
            text.className = 'cl-progress-circle-text';
            text.textContent = `${Math.round(pct)}%`;
            // Adjust font size based on circle size
            if (size === 'small') {
                text.style.fontSize = 'var(--cl-font-size-2xs)';
            } else if (size === 'large') {
                text.style.fontSize = 'var(--cl-font-size-lg)';
            }
            wrapper.appendChild(text);
        }

        this.element = wrapper;
        /** @private */
        this._fill = fillCircle;
        /** @private */
        this._track = svg;
        /** @private */
        this._textEl = wrapper.querySelector('.cl-progress-circle-text') || null;
        /** @private */
        this._circumference = circumference;
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
            const offset = this._circumference - (pct / 100) * this._circumference;
            this._fill.setAttribute('stroke-dashoffset', String(offset));
            this._track.setAttribute('aria-valuenow', String(clamped));
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

        const color = VARIANT_MAP[variant];
        if (this.options.type === 'circle') {
            this._fill.setAttribute('stroke', color);
        } else {
            this._fill.style.background = color;
        }
        return this;
    }

    /**
     * Remove the element from the DOM and clean up references.
     */
    destroy() {
        this.element?.remove();
        this.element = null;
        this._fill = null;
        this._track = null;
        this._textEl = null;
        this._container = null;
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
