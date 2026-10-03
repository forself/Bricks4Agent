/**
 * Progress - 進度指示器元件
 *
 * 提供線性（bar）與圓形（circle）兩種進度顯示模式，
 * 支援確定值與不確定（indeterminate）動畫，以及多段堆疊（segments，預設關閉）。
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
 * 分段模式（segments 為陣列時啟用）:
 *   bar    — 軌道內以絕對定位的 DOM 區段依序堆疊；軌道改為 role="img" + aria-label 摘要。
 *   circle — Canvas 依序畫多段弧，顏色經 theme-bus 解析 token；canvas 為 role="img" + aria-label。
 *   分段一律為確定值（忽略 indeterminate）；setSegments(null) 還原單一數值模式的 DOM 與 ARIA。
 *
 * @author MAGI System
 * @version 2.1.0 (Canvas 版 + segments)
 *
 * @example
 *   const bar = new Progress({ value: 60, variant: 'success', showText: true });
 *   bar.render(document.getElementById('app'));
 *
 *   const circle = new Progress({ type: 'circle', value: 75, size: 'large' });
 *   circle.render(document.getElementById('app'));
 *
 *   const stacked = new Progress({
 *       segments: [
 *           { value: 40, variant: 'success', label: 'Done' },
 *           { value: 25, variant: 'warning', label: 'In review' }
 *       ],
 *       showLegend: true
 *   });
 *   stacked.mount(document.getElementById('app'));
 */
import { onThemeChange, resolveTokens, FALLBACK_PAINT } from '../../utils/theme-bus.js';
import Locale from '../../i18n/index.js';

/**
 * @typedef {'bar'|'circle'} ProgressType
 * @typedef {'primary'|'success'|'warning'|'danger'} ProgressVariant
 * @typedef {'primary'|'success'|'warning'|'danger'|'info'|'neutral'} ProgressSegmentVariant
 * @typedef {'small'|'medium'|'large'} ProgressSize
 */

/**
 * @typedef {Object} ProgressSegment
 * @property {number}                 value     - Part of `max`; the running total is clamped to `max`
 * @property {ProgressSegmentVariant} [variant] - Colour variant (defaults rotate by index)
 * @property {string}                 [color]   - Exactly one token reference, e.g. 'var(--cl-success)'; anything else is ignored
 * @property {string}                 [label]   - Plain-text name used by the legend and the aria-label
 */

/**
 * @typedef {Object} ProgressOptions
 * @property {number}           [value=0]            - Current value (0-max)
 * @property {number}           [max=100]            - Maximum value
 * @property {ProgressVariant}  [variant='primary']  - Colour variant
 * @property {ProgressType}     [type='bar']         - Display type
 * @property {ProgressSize}     [size='medium']      - Size preset
 * @property {boolean}          [showText=false]     - Show percentage label (segment mode: total percentage)
 * @property {boolean}          [indeterminate=false] - Indeterminate animation (ignored in segment mode)
 * @property {ProgressSegment[]|null} [segments=null] - An array switches to stacked segment mode
 * @property {boolean}          [showLegend=false]   - Segment mode only: legend with label, value and percentage
 */

/** Variant name to CSS variable mapping */
const VARIANT_MAP = {
    primary: '--cl-primary',
    success: '--cl-success',
    warning: '--cl-warning',
    danger:  '--cl-danger',
};

/** Segment variants: the single-value variants plus info / neutral */
const SEGMENT_VARIANT_MAP = {
    ...VARIANT_MAP,
    info:    '--cl-info',
    neutral: '--cl-grey',
};

/** Rotation for segments naming neither variant nor color, so neighbours stay distinguishable */
const SEGMENT_VARIANT_CYCLE = ['primary', 'success', 'warning', 'danger', 'info', 'neutral'];

/** A segment colour must be exactly one custom-property reference: var(--name) */
const TOKEN_COLOR_RE = /^var\(\s*(--[A-Za-z0-9_-]+)\s*\)$/;

/** ARIA value attributes owned by the single-value progressbar */
const PROGRESSBAR_VALUE_ATTRS = ['aria-valuemin', 'aria-valuemax', 'aria-valuenow'];

/** CSS percentage rounded to 4 decimals (sub-pixel; avoids float noise such as 99.99999999999999%) */
const toPercent = (n) => `${Math.round(n * 10000) / 10000}%`;

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
            segments: null,      // 分段模式：[{ value, variant?, color?, label? }]；null＝單一數值模式
            showLegend: false,   // 分段模式下顯示圖例（標籤＋數值／百分比）
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

        /** @private segment mode: normalized segments, or null in single-value mode */
        this._segments = null;
        /** @private bar: one absolutely positioned fill per segment */
        this._segmentEls = [];
        /** @private <ul> legend (showLegend) */
        this._legendEl = null;
        /** @private circle + legend: box keeping the canvas and the centre text together */
        this._ringEl = null;
        /** @private the text element was created by segment mode (single mode had none) */
        this._segmentOwnsText = false;

        this._create();
        if (Array.isArray(this.options.segments)) this.setSegments(this.options.segments);
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

            this._startBarIndeterminate(fill);
        } else {
            fill.style.width = `${pct}%`;
            track.setAttribute('aria-valuenow', String(value));
        }

        track.appendChild(fill);
        wrapper.appendChild(track);

        if (showText && !indeterminate) {
            wrapper.appendChild(this._createBarText(pct));
        }

        this.element = wrapper;
        /** @private */
        this._fill = fill;
        /** @private */
        this._track = track;
        /** @private */
        this._textEl = wrapper.querySelector('.cl-progress-text') || null;
    }

    /** @private WAAPI sweep for the indeterminate bar (CSP-safe, DOM element — no SVG). */
    _startBarIndeterminate(fill) {
        if (typeof fill.animate !== 'function') return;
        this._animations.push(fill.animate(
            [
                { left: '-35%', width: '35%', offset: 0 },
                { left: '100%', width: '35%', offset: 0.6 },
                { left: '100%', width: '35%', offset: 1 }
            ],
            { duration: 1800, iterations: Infinity, easing: 'ease-in-out' }
        ));
    }

    /** @private Percentage label beside the bar. */
    _createBarText(pct) {
        const text = document.createElement('span');
        text.className = 'cl-progress-text';
        text.textContent = `${Math.round(pct)}%`;
        text.style.cssText = 'font-size: var(--cl-font-size-xs); color: var(--cl-text-secondary); white-space: nowrap; font-family: var(--cl-font-family);';
        return text;
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
            wrapper.appendChild(this._createCircleText(this._pct()));
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

    /** @private Percentage label centred over the circle. */
    _createCircleText(pct) {
        const { size } = this.options;
        const text = document.createElement('span');
        text.className = 'cl-progress-circle-text';
        text.textContent = `${Math.round(pct)}%`;
        text.style.cssText = [
            'position: absolute;',
            'font-size: var(--cl-font-size-xs);',
            'color: var(--cl-text-secondary);',
            'font-family: var(--cl-font-family);',
            'font-weight: 600;'
        ].join(' ');
        if (size === 'small') text.style.fontSize = 'var(--cl-font-size-2xs)';
        else if (size === 'large') text.style.fontSize = 'var(--cl-font-size-lg)';
        return text;
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
        const segments = this._segments;
        const names = [varName, '--cl-bg-subtle'];
        if (segments) segments.forEach(segment => names.push(segment.token));
        const tok = resolveTokens(names, this.element);
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
        if (segments) {
            /* Segment arcs: consecutive from 12 o'clock; butt caps so neighbours do not overlap */
            let start = -Math.PI / 2;
            for (const segment of segments) {
                if (segment.percent <= 0) continue;
                const end = start + (segment.percent / 100) * Math.PI * 2;
                ctx.beginPath();
                ctx.arc(cx, cy, radius, start, end);
                ctx.strokeStyle = tok[segment.token] || FALLBACK_PAINT;
                ctx.lineWidth = stroke;
                ctx.lineCap = 'butt';
                ctx.stroke();
                start = end;
            }
        } else if (indeterminate) {
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
            /* Canvas colours resolve against the live document: repaint once attached */
            if (this._canvas) this._drawCircle();
        }
        return this;
    }

    /**
     * Component-contract alias of render().
     * @param {HTMLElement|string} container - DOM element or CSS selector
     * @returns {Progress} this
     */
    mount(container) {
        return this.render(container);
    }

    /**
     * Update the current progress value.
     * In segment mode the value is only stored; it shows again after setSegments(null).
     * @param {number} value - New value (clamped between 0 and max)
     * @returns {Progress} this
     */
    setValue(value) {
        const clamped = Math.max(0, Math.min(Number(value) || 0, this.options.max));
        this.options.value = clamped;

        if (this._destroyed || this.options.indeterminate) return this;

        const pct = this._pct();

        if (this._segments) {
            /* detached single fill keeps the latest width for when segment mode ends */
            if (this._fill) this._fill.style.width = `${pct}%`;
            return this;
        }

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
     * Switch the colour variant (single-value mode; segments keep their own colours).
     * @param {ProgressVariant} variant
     * @returns {Progress} this
     */
    setVariant(variant) {
        if (!VARIANT_MAP[variant]) return this;
        this.options.variant = variant;
        if (this._destroyed) return this;

        if (this.options.type === 'circle') {
            this._drawCircle();
        } else {
            this._fill.style.background = `var(${VARIANT_MAP[variant]})`;
        }
        return this;
    }

    /**
     * Show stacked segments. An array (even empty) switches to segment mode;
     * null or a non-array restores the single-value bar/circle exactly as before.
     * @param {ProgressSegment[]|null} segments
     * @returns {Progress} this
     */
    setSegments(segments) {
        const list = Array.isArray(segments) ? segments : null;
        this.options.segments = list;
        if (this._destroyed || !this.element) return this;

        if (list) {
            const entering = !this._segments;
            this._segments = this._normalizeSegments(list);
            if (entering) this._enterSegmentMode();
            this._renderSegments();
        } else if (this._segments) {
            this._segments = null;
            this._exitSegmentMode();
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
        this._indRaf = 0;
        if (this._offTheme) this._offTheme();
        this._offTheme = null;
        this.element?.remove();
        this.element = null;
        this._fill = null;
        this._track = null;
        this._textEl = null;
        this._container = null;
        this._canvas = null;
        this._ctx = null;
        this._segments = null;
        this._segmentEls = [];
        this._legendEl = null;
        this._ringEl = null;
    }

    /* ------------------------------------------------------------------ */
    /*  Segment mode                                                      */
    /* ------------------------------------------------------------------ */

    /**
     * @private
     * Clamp each value so the running total never exceeds max; resolve colour tokens.
     */
    _normalizeSegments(list) {
        const max = Number(this.options.max) > 0 ? Number(this.options.max) : 0;
        let remaining = max;
        return list.map((entry, index) => {
            const seg = entry !== null && typeof entry === 'object' ? entry : {};
            const raw = Number(seg.value);
            const value = Math.min(Number.isFinite(raw) && raw > 0 ? raw : 0, remaining);
            remaining -= value;
            if (remaining <= max * 1e-9) remaining = 0;   // drop floating-point residue (33.3 + 33.3 + 33.4)
            const variant = Object.hasOwn(SEGMENT_VARIANT_MAP, seg.variant)
                ? seg.variant
                : SEGMENT_VARIANT_CYCLE[index % SEGMENT_VARIANT_CYCLE.length];
            const custom = typeof seg.color === 'string' ? TOKEN_COLOR_RE.exec(seg.color.trim()) : null;
            return {
                index,
                value,
                percent: max > 0 ? (value / max) * 100 : 0,
                token: custom ? custom[1] : SEGMENT_VARIANT_MAP[variant],
                label: seg.label == null || seg.label === '' ? null : String(seg.label),
            };
        });
    }

    /** @private Swap the single-value progressbar semantics for role="img". */
    _enterSegmentMode() {
        /* segments are always determinate: stop indeterminate animation */
        this._animations.forEach(anim => anim.cancel());
        this._animations = [];
        cancelAnimationFrame(this._indRaf);
        this._indRaf = 0;

        const holder = this._track;   // bar: track; circle: wrapper — where the progressbar role lives
        PROGRESSBAR_VALUE_ATTRS.forEach(name => holder.removeAttribute(name));
        if (this.options.type === 'circle') {
            holder.removeAttribute('role');
            this._canvas.setAttribute('role', 'img');
        } else {
            holder.setAttribute('role', 'img');
            holder.classList.add('cl-progress-bar-track--segmented');
            this._fill.remove();
        }
    }

    /** @private Restore the single-value DOM, ARIA and animation. */
    _exitSegmentMode() {
        const { type, max, value, indeterminate } = this.options;
        const holder = this._track;

        this._syncLegend(null);

        if (this._segmentOwnsText) {
            this._textEl.remove();
            this._textEl = null;
            this._segmentOwnsText = false;
        } else if (this._textEl) {
            this._textEl.textContent = `${Math.round(this._pct())}%`;
        }

        if (type === 'circle') {
            this._canvas.removeAttribute('role');
            this._canvas.removeAttribute('aria-label');
            holder.setAttribute('role', 'progressbar');
        } else {
            this._segmentEls.forEach(el => el.remove());
            this._segmentEls = [];
            holder.classList.remove('cl-progress-bar-track--segmented');
            holder.removeAttribute('aria-label');
            holder.setAttribute('role', 'progressbar');
            holder.prepend(this._fill);
        }
        holder.setAttribute('aria-valuemin', '0');
        holder.setAttribute('aria-valuemax', String(max));
        if (!indeterminate) holder.setAttribute('aria-valuenow', String(value));

        if (type === 'circle') {
            this._drawCircle();
            if (indeterminate) this._startIndeterminate();
        } else if (indeterminate) {
            this._startBarIndeterminate(this._fill);
        }
    }

    /** @private Paint segments (bar DOM or circle canvas), total text, legend and aria-label. */
    _renderSegments() {
        const segments = this._segments;
        const isCircle = this.options.type === 'circle';
        const graphic = isCircle ? this._canvas : this._track;
        graphic.setAttribute('aria-label', this._segmentSummary(segments));

        if (isCircle) this._drawCircle();
        else this._renderBarSegments(segments);

        if (this.options.showText) {
            const total = segments.reduce((sum, segment) => sum + segment.percent, 0);
            this._showSegmentText(total);
        }
        this._syncLegend(segments);
    }

    /** @private Reuse one absolutely positioned fill per segment so width changes animate. */
    _renderBarSegments(segments) {
        let offset = 0;
        segments.forEach((segment, i) => {
            let el = this._segmentEls[i];
            if (!el) {
                el = document.createElement('div');
                el.className = 'cl-progress-bar-segment';
                el.dataset.segmentIndex = String(i);
                el.style.cssText = [
                    'position: absolute;',
                    'top: 0;',
                    'bottom: 0;',
                    'transition: left var(--cl-transition), width var(--cl-transition);'
                ].join(' ');
                this._track.appendChild(el);
                this._segmentEls.push(el);
            }
            el.style.left = toPercent(offset);
            el.style.width = toPercent(segment.percent);
            el.style.background = `var(${segment.token})`;
            offset += segment.percent;
        });
        this._segmentEls.splice(segments.length).forEach(el => el.remove());
    }

    /** @private Total percentage label (created here when single mode had none). */
    _showSegmentText(totalPct) {
        if (!this._textEl) {
            const isCircle = this.options.type === 'circle';
            this._textEl = isCircle ? this._createCircleText(totalPct) : this._createBarText(totalPct);
            (isCircle ? this._canvas : this._track).after(this._textEl);
            this._segmentOwnsText = true;
        }
        this._textEl.textContent = `${Math.round(totalPct)}%`;
    }

    /** @private "{label}: {percent}%" per segment, joined — the accessible name of the graphic. */
    _segmentSummary(segments) {
        if (segments.length === 0) return Locale.t('progress.noSegments');
        return segments
            .map(segment => Locale.t('progress.segmentSummary', {
                label: this._segmentLabel(segment),
                percent: Math.round(segment.percent),
            }))
            .join(Locale.t('progress.segmentSeparator'));
    }

    /** @private Caller label, or the localized "Segment {n}" fallback. */
    _segmentLabel(segment) {
        return segment.label ?? Locale.t('progress.segmentFallbackLabel', { index: segment.index + 1 });
    }

    /** @private Create, refresh or remove the legend (segment mode + showLegend only). */
    _syncLegend(segments) {
        if (!segments || !this.options.showLegend) {
            if (this._legendEl) {
                this._legendEl.remove();
                this._legendEl = null;
                this._setLegendLayout(false);
            }
            return;
        }
        if (!this._legendEl) {
            const legend = document.createElement('ul');
            legend.className = 'cl-progress-legend';
            legend.style.cssText = [
                'display: flex;',
                'flex-wrap: wrap;',
                'gap: 4px 12px;',
                'flex: 1 0 100%;',
                'margin: 0;',
                'padding: 0;',
                'list-style: none;',
                'font-size: var(--cl-font-size-xs);',
                'color: var(--cl-text-secondary);',
                'font-family: var(--cl-font-family);'
            ].join(' ');
            this._setLegendLayout(true);
            this.element.appendChild(legend);
            this._legendEl = legend;
        }
        this._legendEl.replaceChildren(...segments.map(segment => this._createLegendItem(segment)));
    }

    /**
     * @private
     * Make room for the legend below the graphic; `on = false` restores the single-mode layout.
     */
    _setLegendLayout(on) {
        const wrapper = this.element;
        if (this.options.type !== 'circle') {
            /* wrap the legend onto its own line; the track grows from 0 so bar + text stay on one line.
               Longhands (not the flex shorthand) so removal restores the original style exactly. */
            if (on) {
                wrapper.style.setProperty('flex-wrap', 'wrap');
                this._track.style.setProperty('flex-grow', '1');
                this._track.style.setProperty('flex-basis', '0%');
            } else {
                wrapper.style.removeProperty('flex-wrap');
                this._track.style.removeProperty('flex-grow');
                this._track.style.removeProperty('flex-basis');
            }
            return;
        }

        const diameter = CIRCLE_SIZE[this.options.size] || CIRCLE_SIZE.medium;
        if (on && !this._ringEl) {
            const ring = document.createElement('div');
            ring.className = 'cl-progress-circle-ring';
            ring.style.cssText = [
                'display: inline-flex;',
                'align-items: center;',
                'justify-content: center;',
                'position: relative;',
                `width: ${diameter}px;`,
                `height: ${diameter}px;`
            ].join(' ');
            ring.append(...[this._canvas, this._textEl].filter(Boolean));
            wrapper.prepend(ring);
            wrapper.style.setProperty('flex-direction', 'column');
            wrapper.style.setProperty('gap', '8px');
            wrapper.style.setProperty('width', 'auto');
            wrapper.style.setProperty('height', 'auto');
            this._ringEl = ring;
        } else if (!on && this._ringEl) {
            wrapper.prepend(...this._ringEl.childNodes);
            this._ringEl.remove();
            this._ringEl = null;
            wrapper.style.removeProperty('flex-direction');
            wrapper.style.removeProperty('gap');
            wrapper.style.setProperty('width', `${diameter}px`);
            wrapper.style.setProperty('height', `${diameter}px`);
        }
    }

    /** @private One legend row: swatch (decorative) + label + "value (percent%)". */
    _createLegendItem(segment) {
        const item = document.createElement('li');
        item.className = 'cl-progress-legend-item';
        item.dataset.segmentIndex = String(segment.index);
        item.style.cssText = 'display: inline-flex; align-items: center; gap: 4px;';

        const swatch = document.createElement('span');
        swatch.className = 'cl-progress-legend-swatch';
        swatch.setAttribute('aria-hidden', 'true');
        swatch.style.cssText = 'display: inline-block; width: 8px; height: 8px; border-radius: var(--cl-radius-xs); flex: 0 0 auto;';
        swatch.style.background = `var(${segment.token})`;

        const label = document.createElement('span');
        label.className = 'cl-progress-legend-label';
        label.style.color = 'var(--cl-text)';
        label.textContent = this._segmentLabel(segment);

        const value = document.createElement('span');
        value.className = 'cl-progress-legend-value';
        value.textContent = Locale.t('progress.legendValue', {
            value: this._formatNumber(segment.value),
            percent: Math.round(segment.percent),
        });

        item.append(swatch, label, value);
        return item;
    }

    /** @private Locale-aware number (at most two decimals). */
    _formatNumber(value) {
        try {
            return new Intl.NumberFormat(Locale.getLang(), { maximumFractionDigits: 2 }).format(value);
        } catch {
            return String(Math.round(value * 100) / 100);
        }
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
