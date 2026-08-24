import { createComponentState } from '../../utils/component-state.js';
import { onThemeChange, resolveTokens, FALLBACK_PAINT } from '../../utils/theme-bus.js';

/**
 * Rating — 星等輸入 / 顯示原子。葉子:Canvas 自繪星形(SVG 禁用政策)。
 *
 * 架構:單一 canvas 涵蓋所有星星(canvas 寬 = max * STAR_TOTAL, 高 = STAR_TOTAL)。
 * 互動語意不變:hover 預覽(事件驅動)/ click 設值 / readonly 禁互動。
 * 顏色走 resolveTokens(--cl-warning / --cl-border-light),ThemeBus 訂閱重繪。
 * DPR 處理:canvas backing store = CSS px × dpr;繪圖座標以 CSS px 書寫。
 *
 * 星形:五角星 Path2D(外圓 R、內圓 r=R*0.4,10 個頂點)。
 */

/** CSS px dimensions for one star cell */
const STAR_SIZE = 18;   // CSS px (star outer bounding box)
const STAR_GAP  = 2;    // gap between stars (CSS px)
const STAR_R    = 8;    // outer radius (within 18px cell)
const STAR_r    = 3.2;  // inner radius

/** Build a Path2D for a 5-point star centred at (cx, cy) */
function starPath(cx, cy, R = STAR_R, r = STAR_r) {
    const p = new Path2D();
    const points = 5;
    for (let i = 0; i < points * 2; i++) {
        const angle = (i * Math.PI) / points - Math.PI / 2;
        const radius = i % 2 === 0 ? R : r;
        const x = cx + Math.cos(angle) * radius;
        const y = cy + Math.sin(angle) * radius;
        if (i === 0) p.moveTo(x, y); else p.lineTo(x, y);
    }
    p.closePath();
    return p;
}

export class Rating {
    constructor(options = {}) {
        this.options = { value: 0, max: 5, readonly: false, onChange: null, ...options };
        this.element = null;
        this._stars = [];       // { path: Path2D, cx, cy } per star
        this._canvas = null;
        this._ctx = null;
        this._hoverValue = -1;  // -1 = no hover
        this._offTheme = null;
        this._destroyed = false;

        this._state = createComponentState(this._buildInitialState(), {
            MOUNT:     (s) => ({ ...s, lifecycle: 'mounted' }),
            SHOW:      (s) => ({ ...s, visibility: 'visible' }),
            HIDE:      (s) => ({ ...s, visibility: 'hidden' }),
            SET_VALUE: (s, p) => ({ ...s, content: { ...s.content, value: this._clamp(Number(p?.value)) } }),
            DESTROY:   (s) => ({ ...s, lifecycle: 'destroyed' })
        });

        this._create();
        this._applyState();
    }

    /* ── helpers ──────────────────────────────────────────────────────── */

    _clamp(value) {
        if (Number.isNaN(value)) return 0;
        return Math.min(this.options.max, Math.max(0, Math.round(value)));
    }

    _buildInitialState() {
        return { lifecycle: 'created', visibility: 'visible', content: { value: this._clamp(Number(this.options.value)) } };
    }

    /* ── DOM + Canvas 建構 ────────────────────────────────────────────── */

    _create() {
        const max = Math.max(1, parseInt(this.options.max, 10) || 5);
        const cssW = max * STAR_SIZE + (max - 1) * STAR_GAP;
        const cssH = STAR_SIZE;

        /* Outer wrapper (keeps same class/style contract as original span) */
        const el = document.createElement('span');
        el.className = 'cl-rating' + (this.options.readonly ? '' : ' cl-rating--interactive');
        el.style.cssText = 'display: inline-flex; gap: 0; color: var(--cl-warning); line-height: 1;';

        const dpr = (typeof window !== 'undefined' ? window.devicePixelRatio : 1) || 1;
        const canvas = document.createElement('canvas');
        canvas.width  = Math.round(cssW * dpr);
        canvas.height = Math.round(cssH * dpr);
        canvas.style.cssText = `width: ${cssW}px; height: ${cssH}px; display: block;` +
            (this.options.readonly ? '' : ' cursor: pointer;');
        el.appendChild(canvas);

        this.element = el;
        this._canvas = canvas;
        this._ctx = canvas.getContext('2d');

        /* Pre-build Path2D for each star (reused every frame) */
        this._stars = [];
        for (let i = 1; i <= max; i++) {
            const cx = (i - 1) * (STAR_SIZE + STAR_GAP) + STAR_SIZE / 2;
            const cy = STAR_SIZE / 2;
            this._stars.push({ index: i, path: starPath(cx, cy), cx, cy });
        }

        /* Events (if not readonly) */
        if (!this.options.readonly) {
            canvas.addEventListener('mousemove', (e) => {
                const hov = this._starAtX(e.offsetX);
                if (hov !== this._hoverValue) {
                    this._hoverValue = hov;
                    this._paintStars(hov > 0 ? hov : this.getValue());
                }
            });
            canvas.addEventListener('mouseleave', () => {
                this._hoverValue = -1;
                this._paintStars(this.getValue());
            });
            canvas.addEventListener('click', (e) => {
                const star = this._starAtX(e.offsetX);
                if (star > 0) {
                    this.send('SET_VALUE', { value: star });
                    if (typeof this.options.onChange === 'function') this.options.onChange(this.getValue());
                }
            });
        }

        /* ThemeBus: recolour when theme changes */
        this._offTheme = onThemeChange(() => this._paintStars(
            this._hoverValue > 0 ? this._hoverValue : this.getValue()
        ));
    }

    /** Return 1-based star index under cssX, or 0 if none. */
    _starAtX(cssX) {
        const cellW = STAR_SIZE + STAR_GAP;
        const idx = Math.floor(cssX / cellW) + 1;
        return (idx >= 1 && idx <= this._stars.length) ? idx : 0;
    }

    /* ── Canvas drawing ───────────────────────────────────────────────── */

    _paintStars(filledCount) {
        if (this._destroyed || !this._canvas) return;
        const dpr = window.devicePixelRatio || 1;
        const canvas = this._canvas;
        const cssW = canvas.width / dpr, cssH = canvas.height / dpr;
        const ctx = this._ctx;

        /* Re-sync backing store on DPR change */
        const needW = Math.round(cssW * dpr), needH = Math.round(cssH * dpr);
        if (canvas.width !== needW || canvas.height !== needH) {
            canvas.width = needW; canvas.height = needH;
        }

        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, cssW, cssH);

        const tok = resolveTokens(['--cl-warning', '--cl-border-light'], this.element);
        const filled  = tok['--cl-warning']       || FALLBACK_PAINT;
        const outline = tok['--cl-border-light']  || FALLBACK_PAINT;

        for (const { index, path } of this._stars) {
            const isFilled = index <= filledCount;
            ctx.beginPath();
            ctx.fillStyle   = isFilled ? filled : outline;
            ctx.strokeStyle = isFilled ? filled : outline;
            ctx.lineWidth   = 1;
            ctx.fill(path);
            /* subtle stroke to preserve star shape on light backgrounds */
            ctx.stroke(path);
        }
    }

    /* ── State machine bridge ─────────────────────────────────────────── */

    _applyState() {
        if (!this.element) return;
        const { content, visibility } = this.snapshot();
        this._paintStars(content.value);
        this.element.style.display = visibility === 'hidden' ? 'none' : 'inline-flex';
    }

    snapshot() { return this._state.snapshot(); }

    send(event, payload = null) {
        const next = this._state.send(event, payload);
        this._applyState();
        return next;
    }

    /* ── Public API (identical to original) ──────────────────────────── */

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) { console.warn('[Rating] mount target not found:', container); return this; }
        target.appendChild(this.element);
        this.send('MOUNT');
        return this;
    }

    getValue()      { return this.snapshot().content.value; }
    setValue(value) { this.send('SET_VALUE', { value }); return this; }
    show()          { this.send('SHOW'); return this; }
    hide()          { this.send('HIDE'); return this; }

    destroy() {
        this._destroyed = true;
        if (this._offTheme) this._offTheme();
        this.send('DESTROY');
        this.element?.remove();
        this.element = null;
        this._stars = [];
        this._canvas = null;
        this._ctx = null;
    }
}

export default Rating;
