/**
 * CanvasChart — Canvas 圖表基底(取代 SVG BaseChart 的新一代;庫政策:SVG 禁用)。
 *
 * 架構:混合分層——Canvas 只畫幾何,文字密集區(標題/tooltip)走 DOM(cssText),
 * 把排版留給瀏覽器排版引擎。子類只需實作 draw(ctx, w, h),其餘由基底處理:
 *   - DPR 背景儲存縮放(子類一律以 CSS px 座標繪製)
 *   - ResizeObserver 容器導向 RWD(rAF 合併重繪)
 *   - ThemeBus 訂閱:token / data-theme 變更自動重繪(Canvas 不會自己響應 CSS 變數)
 *   - 命中測試:draw 內以 addRegion() 註冊互動區(rect/circle/path),基底處理
 *     hover(tooltip)/click(onPointClick);tooltip 為 DOM,文字用 textContent(免跳脫風險)
 *   - 匯出:exportPNG(scale)離屏高倍重渲(列印銳利度的補償)
 *   - 排版輔助:tokens/ellipsis/wrapText/niceTicks/fmt/tween
 *   - debug:true 時描出所有 hit-region 外框
 *
 * 子類契約:
 *   class MyChart extends CanvasChart {
 *       draw(ctx, w, h) {                       // w/h = CSS px 繪圖區尺寸
 *           const { '--cl-primary': c } = this.tokens(['--cl-primary']);
 *           ctx.fillStyle = c; ctx.fillRect(...);
 *           this.addRegion({ shape: 'rect', x, y, w: bw, h: bh, data: {...} });
 *       }
 *       getTooltip(data) { return [{ label: '案類', value: data.name }]; }  // 選配
 *   }
 */
import { onThemeChange, resolveTokens } from '../utils/theme-bus.js';

export class CanvasChart {
    constructor(options = {}) {
        this.options = {
            container: null,
            width: '100%',
            height: '300px',
            title: '',
            ariaLabel: '',
            padding: { top: 16, right: 16, bottom: 28, left: 44 },
            onPointClick: null,
            debug: false,
            ...options
        };
        this.container = typeof this.options.container === 'string'
            ? document.querySelector(this.options.container)
            : this.options.container;

        this._regions = [];
        this._hoverRegion = null;
        this._renderScheduled = false;
        this._destroyed = false;
        this._offTheme = null;
        this._resizeObserver = null;
        this._raf = 0;

        this._buildDom();
        if (this.container) this.container.appendChild(this.element);
        this._bindEvents();
        this._offTheme = onThemeChange(() => this.render());
        if (typeof ResizeObserver !== 'undefined') {
            this._resizeObserver = new ResizeObserver(() => this.render());
            this._resizeObserver.observe(this._canvasWrap);
        }
        this.render();
    }

    _buildDom() {
        const el = document.createElement('div');
        el.className = 'cl-canvas-chart';
        el.style.cssText = `display: flex; flex-direction: column; gap: 6px; position: relative;` +
            ` width: ${this.options.width}; height: ${this.options.height}; min-width: 0; box-sizing: border-box;`;

        if (this.options.title) {
            const t = document.createElement('div');
            t.className = 'cl-canvas-chart__title';
            t.textContent = this.options.title;
            t.style.cssText = 'font-size: var(--cl-font-size-lg); font-weight: 600; color: var(--cl-text); flex: 0 0 auto;';
            el.appendChild(t);
            this._titleEl = t;
        }

        const wrap = document.createElement('div');
        wrap.className = 'cl-canvas-chart__body';
        wrap.style.cssText = 'position: relative; flex: 1 1 auto; min-height: 0; overflow: hidden;';
        const canvas = document.createElement('canvas');
        canvas.style.cssText = 'display: block; width: 100%; height: 100%;';
        canvas.setAttribute('role', 'img');
        canvas.setAttribute('aria-label', this.options.ariaLabel || this.options.title || 'chart');
        wrap.appendChild(canvas);

        const tip = document.createElement('div');
        tip.className = 'cl-canvas-chart__tooltip';
        tip.style.cssText = 'position: absolute; display: none; pointer-events: none; z-index: 10;' +
            ' background: var(--cl-bg); border: 1px solid var(--cl-border); border-radius: var(--cl-radius-md);' +
            ' box-shadow: var(--cl-shadow-md); padding: 8px 10px; max-width: 260px;' +
            ' font-size: var(--cl-font-size-sm); color: var(--cl-text);';
        wrap.appendChild(tip);

        el.appendChild(wrap);
        this.element = el;
        this._canvasWrap = wrap;
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        this._tooltip = tip;
    }

    _bindEvents() {
        this._onMove = (e) => this._handleMove(e);
        this._onLeave = () => this._setHover(null);
        this._onClick = (e) => {
            const r = this._hitTest(e.offsetX, e.offsetY);
            if (r && typeof this.options.onPointClick === 'function') this.options.onPointClick(r.data, r);
        };
        this.canvas.addEventListener('mousemove', this._onMove);
        this.canvas.addEventListener('mouseleave', this._onLeave);
        this.canvas.addEventListener('click', this._onClick);
    }

    /* ── 渲染管線 ── */

    /** 排程重繪(rAF 合併;resize/theme/資料更新皆走這裡)。 */
    render() {
        if (this._renderScheduled || this._destroyed) return;
        this._renderScheduled = true;
        this._raf = requestAnimationFrame(() => {
            this._renderScheduled = false;
            this._renderNow();
        });
    }

    _renderNow() {
        if (this._destroyed) return;
        const cssW = Math.max(1, this._canvasWrap.clientWidth);
        const cssH = Math.max(1, this._canvasWrap.clientHeight);
        const dpr = window.devicePixelRatio || 1;
        const bw = Math.round(cssW * dpr), bh = Math.round(cssH * dpr);
        if (this.canvas.width !== bw || this.canvas.height !== bh) {
            this.canvas.width = bw;
            this.canvas.height = bh;
        }
        const ctx = this.ctx;
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, cssW, cssH);
        this._regions = [];
        try {
            this.draw(ctx, cssW, cssH);
        } catch (e) {
            console.error('[CanvasChart] draw 失敗:', e);
        }
        if (this.options.debug) this._drawDebugRegions(ctx);
        this._lastSize = { w: cssW, h: cssH };
    }

    /** 子類實作:以 CSS px 繪製。 */
    draw(_ctx, _w, _h) { /* override */ }

    /* ── 命中測試 ── */

    /**
     * 於 draw() 內註冊互動區。
     * @param {Object} r - { shape:'rect', x,y,w,h } | { shape:'circle', cx,cy,r } |
     *                     { shape:'path', path:Path2D, bounds:{x,y,w,h} },外加 data(回拋用)
     */
    addRegion(r) { this._regions.push(r); }

    _hitTest(x, y) {
        for (let i = this._regions.length - 1; i >= 0; i--) {
            const r = this._regions[i];
            if (r.shape === 'rect') {
                if (x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h) return r;
            } else if (r.shape === 'circle') {
                const dx = x - r.cx, dy = y - r.cy;
                if (dx * dx + dy * dy <= r.r * r.r) return r;
            } else if (r.shape === 'path') {
                const b = r.bounds;
                if (!b || (x >= b.x && x <= b.x + b.w && y >= b.y && y <= b.y + b.h)) {
                    if (this.ctx.isPointInPath(r.path, x * (window.devicePixelRatio || 1), y * (window.devicePixelRatio || 1))) return r;
                }
            }
        }
        return null;
    }

    _handleMove(e) {
        const r = this._hitTest(e.offsetX, e.offsetY);
        this._setHover(r, e);
    }

    _setHover(r, e) {
        if (r !== this._hoverRegion) {
            this._hoverRegion = r;
            this.canvas.style.cursor = r && (this.options.onPointClick || r.clickable) ? 'pointer' : 'default';
            if (!r) { this._tooltip.style.display = 'none'; return; }
            const content = this.getTooltip ? this.getTooltip(r.data, r) : null;
            if (!content) { this._tooltip.style.display = 'none'; return; }
            this._fillTooltip(content);
            this._tooltip.style.display = 'block';
        }
        if (r && e) this._positionTooltip(e.offsetX, e.offsetY);
    }

    _fillTooltip(content) {
        const tip = this._tooltip;
        tip.textContent = '';
        const rows = typeof content === 'string' ? [{ label: '', value: content }] : content;
        for (const row of rows) {
            const line = document.createElement('div');
            line.style.cssText = 'display: flex; gap: 8px; justify-content: space-between; align-items: baseline;';
            if (row.label) {
                const l = document.createElement('span');
                l.textContent = row.label;                  // textContent:天然免注入
                l.style.cssText = 'color: var(--cl-text-secondary); flex: 0 0 auto;';
                line.appendChild(l);
            }
            const v = document.createElement('span');
            v.textContent = row.value != null ? String(row.value) : '';
            v.style.cssText = 'font-weight: 600; color: var(--cl-text); overflow-wrap: anywhere;';
            line.appendChild(v);
            tip.appendChild(line);
        }
    }

    _positionTooltip(x, y) {
        const tip = this._tooltip;
        const pad = 12;
        const bw = this._canvasWrap.clientWidth, bh = this._canvasWrap.clientHeight;
        const tw = tip.offsetWidth, th = tip.offsetHeight;
        let left = x + pad, top = y + pad;
        if (left + tw > bw - 4) left = Math.max(4, x - tw - pad);
        if (top + th > bh - 4) top = Math.max(4, y - th - pad);
        tip.style.left = left + 'px';
        tip.style.top = top + 'px';
    }

    _drawDebugRegions(ctx) {
        ctx.save();
        ctx.strokeStyle = this.tokens(['--cl-danger'])['--cl-danger'] || 'red';
        ctx.lineWidth = 1;
        for (const r of this._regions) {
            if (r.shape === 'rect') ctx.strokeRect(r.x, r.y, r.w, r.h);
            else if (r.shape === 'circle') { ctx.beginPath(); ctx.arc(r.cx, r.cy, r.r, 0, Math.PI * 2); ctx.stroke(); }
            else if (r.shape === 'path' && r.bounds) ctx.strokeRect(r.bounds.x, r.bounds.y, r.bounds.w, r.bounds.h);
        }
        ctx.restore();
    }

    /* ── 排版/繪圖輔助(子類共用)── */

    /** 解析 token 目前實際值(每次 draw 時呼叫,吃到最新主題)。 */
    tokens(names) { return resolveTokens(names, this.element); }

    /** 取字型字串(canvas ctx.font 用;吃 --cl-font-family 系 token)。 */
    font(sizePx = 12, weight = 400) {
        const t = this.tokens(['--cl-font-family-cjk', '--cl-font-family']);
        const fam = [t['--cl-font-family-cjk'], t['--cl-font-family']].filter(Boolean).join(', ') || 'sans-serif';
        return `${weight} ${sizePx}px ${fam}`;
    }

    /** 文字截斷(measureText;超寬加 …)。 */
    ellipsis(ctx, text, maxWidth) {
        if (ctx.measureText(text).width <= maxWidth) return text;
        let s = String(text);
        while (s.length > 1 && ctx.measureText(s + '…').width > maxWidth) s = s.slice(0, -1);
        return s + '…';
    }

    /** 文字換行(回傳行陣列)。 */
    wrapText(ctx, text, maxWidth) {
        const out = [];
        let line = '';
        for (const ch of String(text)) {
            if (ctx.measureText(line + ch).width > maxWidth && line) { out.push(line); line = ch; }
            else line += ch;
        }
        if (line) out.push(line);
        return out;
    }

    /** 好看的軸刻度(1/2/5 步進)。 */
    niceTicks(min, max, count = 5) {
        if (min === max) { max = min + 1; }
        const span = max - min;
        const step0 = Math.pow(10, Math.floor(Math.log10(span / count)));
        const err = span / count / step0;
        const step = step0 * (err >= 7.5 ? 10 : err >= 3 ? 5 : err >= 1.5 ? 2 : 1);
        const lo = Math.floor(min / step) * step;
        const hi = Math.ceil(max / step) * step;
        const ticks = [];
        for (let v = lo; v <= hi + step / 2; v += step) ticks.push(Number(v.toFixed(10)));
        return { ticks, lo, hi, step };
    }

    /** 數字格式化(千分位;>=1e4 縮寫)。 */
    fmt(n) {
        if (n == null || Number.isNaN(n)) return '';
        const abs = Math.abs(n);
        if (abs >= 1e8) return (n / 1e8).toFixed(abs >= 1e9 ? 0 : 1) + '億';
        if (abs >= 1e4) return (n / 1e4).toFixed(abs >= 1e5 ? 0 : 1) + '萬';
        return Number(n).toLocaleString('en-US');
    }

    /** 簡易補間(rAF;回傳取消函式)。 */
    tween(from, to, ms, onFrame, onDone) {
        const start = performance.now();
        let raf = 0;
        const step = (now) => {
            const t = Math.min(1, (now - start) / ms);
            const e = 1 - Math.pow(1 - t, 3);   // easeOutCubic
            onFrame(from + (to - from) * e, t);
            if (t < 1) raf = requestAnimationFrame(step);
            else if (onDone) onDone();
        };
        raf = requestAnimationFrame(step);
        return () => cancelAnimationFrame(raf);
    }

    /* ── 匯出 ── */

    /**
     * 高倍離屏重渲匯出 PNG(列印銳利度補償;預設 2 倍)。
     * @returns {string} dataURL
     */
    exportPNG(scale = 2) {
        const w = this._lastSize ? this._lastSize.w : this._canvasWrap.clientWidth;
        const h = this._lastSize ? this._lastSize.h : this._canvasWrap.clientHeight;
        const off = document.createElement('canvas');
        off.width = Math.round(w * scale);
        off.height = Math.round(h * scale);
        const ctx = off.getContext('2d');
        ctx.setTransform(scale, 0, 0, scale, 0, 0);
        const { '--cl-bg': bg } = this.tokens(['--cl-bg']);
        ctx.fillStyle = bg || '#ffffff';
        ctx.fillRect(0, 0, w, h);
        const savedRegions = this._regions;
        this._regions = [];
        try { this.draw(ctx, w, h); } finally { this._regions = savedRegions; }
        return off.toDataURL('image/png');
    }

    /* ── 生命週期 ── */

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target && this.element.parentNode !== target) target.appendChild(this.element);
        this.render();
        return this;
    }

    /** 更新資料/選項後重繪(子類可覆寫吸收 options)。 */
    update(patch = {}) {
        Object.assign(this.options, patch);
        this.render();
    }

    destroy() {
        this._destroyed = true;
        cancelAnimationFrame(this._raf);
        if (this._offTheme) this._offTheme();
        if (this._resizeObserver) this._resizeObserver.disconnect();
        this.canvas.removeEventListener('mousemove', this._onMove);
        this.canvas.removeEventListener('mouseleave', this._onLeave);
        this.canvas.removeEventListener('click', this._onClick);
        if (this.element && this.element.parentNode) this.element.parentNode.removeChild(this.element);
        this.element = null;
    }
}

export default CanvasChart;
