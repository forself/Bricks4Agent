/**
 * CanvasChart — Canvas 圖表基底(取代 SVG BaseChart 的新一代;庫政策:SVG 禁用)。
 *
 * 架構:混合分層——Canvas 只畫幾何,文字密集區(標題/tooltip)走 DOM(cssText),
 * 把排版留給瀏覽器排版引擎。子類只需實作 draw(ctx, w, h),其餘由基底處理:
 *   - DPR 背景儲存縮放(子類一律以 CSS px 座標繪製)
 *   - ResizeObserver 容器導向 RWD(模組級共用 rAF:同幀先全量測、後全繪製,免交錯讀寫強制排版)
 *   - IntersectionObserver 離視口(±200px)釋放 canvas 背景儲存,回視口自動重繪
 *   - ThemeBus 訂閱:token / data-theme 變更自動重繪(Canvas 不會自己響應 CSS 變數)
 *   - 命中測試:draw 內以 addRegion() 註冊互動區(rect/circle/path),基底處理
 *     hover(tooltip)/click(onPointClick);tooltip 為 DOM,文字用 textContent(免跳脫風險)
 *   - 匯出:exportPNG(scale)離屏高倍重渲(列印銳利度的補償)
 *   - 排版輔助:tokens/ellipsis/wrapText/niceTicks/fmt/tween
 *   - debug:true 時描出所有 hit-region 外框
 *   - 無障礙資料表(accessibleTable,預設關閉):true=視覺隱藏 <table>、'visible'=圖下可見表格;
 *     內容來自 getDataTable()(子類覆寫),資料變更時以微任務合併重建(不隨動畫幀),
 *     詳見 ACCESSIBILITY.md
 *
 * 子類契約:
 *   class MyChart extends CanvasChart {
 *       draw(ctx, w, h) {                       // w/h = CSS px 繪圖區尺寸
 *           const { '--cl-primary': c } = this.tokens(['--cl-primary']);
 *           ctx.fillStyle = c; ctx.fillRect(...);
 *           this.addRegion({ shape: 'rect', x, y, w: bw, h: bh, data: {...} });
 *       }
 *       getTooltip(data) { return [{ label: '案類', value: data.name }]; }  // 選配
 *       getDataTable() { return { columns: [...], rows: [...] }; }       // 選配(accessibleTable 用)
 *   }
 */
import { onThemeChange, resolveTokens, FALLBACK_PAINT } from '../utils/theme-bus.js';
import Locale from '../i18n/index.js';
import { nextUid } from '../utils/uid.js';

/* ── 模組級共用排程器:同一幀 pass1 全部量測(讀 layout)、pass2 全部提交/繪製(寫 canvas),
   避免 N 張圖各自 rAF 交錯讀寫造成 N 次強制排版。 ── */
const _pending = new Set();
let _flushRaf = 0;

function _scheduleFlush() {
    if (_flushRaf) return;
    _flushRaf = requestAnimationFrame(() => {
        _flushRaf = 0;
        const batch = [..._pending];
        _pending.clear();
        const measured = [];
        for (const c of batch) {
            c._renderScheduled = false;
            if (c._destroyed) continue;
            if (c._released && c._offscreen) continue;   // 釋放中且離視口:回視口時 IO 會重新排程
            measured.push([c, c._measure()]);
        }
        for (const [c, m] of measured) {
            if (c._destroyed) continue;
            // 逐圖隔離:單一圖表 commit 擲錯(如 ctx 取得失敗)不得中斷同幀其他圖表
            try { c._commit(m); } catch (e) { console.error('[CanvasChart] commit 失敗:', e); }
        }
    });
}

/* ── 無障礙資料表(accessibleTable;預設關閉,關閉時不建任何 DOM、不動 ARIA)── */

/** 視覺隱藏(螢幕閱讀器仍可讀;刻意不用 display:none,否則會一併移出無障礙樹)。 */
const A11Y_HIDDEN_CSS = 'position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px;' +
    ' overflow: hidden; clip: rect(0, 0, 0, 0); clip-path: inset(50%); white-space: nowrap; border: 0;';
/** 可見模式外層:接在繪圖區下方;寬表橫向捲動(外層 role=region + tabindex=0 供鍵盤捲動)。 */
const A11Y_VISIBLE_CSS = 'position: static; flex: 0 0 auto; min-width: 0; max-width: 100%;' +
    ' overflow-x: auto; line-height: 1.5;';
const A11Y_TABLE_CSS = 'width: 100%; border-collapse: collapse; font-size: var(--cl-font-size-sm);' +
    ' color: var(--cl-text); background: var(--cl-bg);';
const A11Y_CAPTION_CSS = 'caption-side: top; text-align: start; font-weight: 600; padding: 0 0 4px;' +
    ' color: var(--cl-text);';
const A11Y_TH_COL_CSS = 'text-align: start; padding: 4px 8px; font-weight: 600; color: var(--cl-text);' +
    ' background: var(--cl-bg-secondary); border-bottom: 1px solid var(--cl-border);';
const A11Y_TH_ROW_CSS = 'text-align: start; padding: 4px 8px; font-weight: 600; color: var(--cl-text);' +
    ' border-bottom: 1px solid var(--cl-border-light);';
const A11Y_TD_CSS = 'text-align: start; padding: 4px 8px; color: var(--cl-text);' +
    ' border-bottom: 1px solid var(--cl-border-light);';
const A11Y_NUM_CSS = ' text-align: end; font-variant-numeric: tabular-nums;';
const A11Y_NOTE_CSS = 'padding: 4px 8px; color: var(--cl-text-secondary); font-style: italic;';
const A11Y_DEFAULT_MAX_ROWS = 500;

/** accessibleTable 正規化:true → 'hidden'(視覺隱藏)、'visible' → 可見;其餘一律關閉(null)。 */
function a11yMode(value) {
    if (value === 'visible') return 'visible';
    return value === true ? 'hidden' : null;
}

function sameRefs(a, b) {
    return a.length === b.length && a.every((v, i) => v === b[i]);
}

const _a11yFormatters = new Map();

/** Intl 格式器(依語系快取;語系代碼不合法時退回執行環境預設語系,不擲錯)。 */
function a11yFormatter(kind, lang) {
    const key = `${kind}|${lang}`;
    let f = _a11yFormatters.get(key);
    if (f) return f;
    const opts = kind === 'percent' ? { style: 'percent', maximumFractionDigits: 1 }
        : kind === 'datetime' ? { dateStyle: 'medium', timeStyle: 'medium' } : {};
    const Ctor = kind === 'datetime' ? Intl.DateTimeFormat : Intl.NumberFormat;
    try { f = new Ctor(lang, opts); } catch (_e) { f = new Ctor(undefined, opts); }
    _a11yFormatters.set(key, f);
    return f;
}

function a11yNumericColumn(col) {
    return col.format === 'number' || col.format === 'percent';
}

/**
 * 儲存格文字:數字走 Intl.NumberFormat(Locale.getLang());percent 欄值為 0~1 比例;
 * datetime 欄接受 epoch 毫秒 / Date / 可解析字串;無法轉換者原樣以字串呈現。
 */
function a11yCellText(value, col, lang) {
    if (value == null || value === '') return '';
    const format = col.format;
    if (format === 'text') return String(value);
    if (format === 'datetime' || value instanceof Date) {
        const d = value instanceof Date ? value : new Date(value);
        return Number.isNaN(d.getTime()) ? String(value) : a11yFormatter('datetime', lang).format(d);
    }
    if (a11yNumericColumn(col) || typeof value === 'number') {
        const n = typeof value === 'number' ? value : Number(value);
        if (!Number.isFinite(n)) return typeof value === 'number' ? '' : String(value);
        if (format === 'percent') return a11yFormatter('percent', lang).format(n);
        const text = a11yFormatter('number', lang).format(n);
        return col.unit ? `${text} ${col.unit}` : text;
    }
    return String(value);
}

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
            accessibleTable: false,           // true=視覺隱藏資料表(供輔助科技)、'visible'=圖下可見資料表
            accessibleTableMaxRows: 500,      // 資料表列數上限;超過時以末列註明省略筆數
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
        this._io = null;
        this._released = false;
        this._offscreen = false;
        this._a11y = null;               // 無障礙資料表狀態(accessibleTable 開啟時才建立)

        this._buildDom();
        if (this.container) this.container.appendChild(this.element);
        this._bindEvents();
        this._offTheme = onThemeChange(() => this.render());
        if (typeof ResizeObserver !== 'undefined') {
            this._resizeObserver = new ResizeObserver(() => this.render());
            this._resizeObserver.observe(this._canvasWrap);
        }
        if (typeof IntersectionObserver !== 'undefined') {
            this._io = new IntersectionObserver((entries) => {
                if (this._destroyed) return;
                this._offscreen = !entries[entries.length - 1].isIntersecting;
                if (this._offscreen) this._releaseBackingStore();
                else if (this._released) this.render();
            }, { rootMargin: '200px' });
            this._io.observe(this._canvasWrap);
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

    /** 排程重繪(共用 rAF 合併;resize/theme/資料更新皆走這裡)。 */
    render() {
        if (this._destroyed) return;
        this._syncAccessibleTable();     // 資料表:模式切換/資料參照變更才排程重建(關閉時為空操作)
        if (this._renderScheduled) return;
        this._renderScheduled = true;
        _pending.add(this);
        _scheduleFlush();
    }

    /** 同步重繪(量測+繪製;動畫迴圈可直呼)。 */
    _renderNow() {
        if (this._destroyed) return;
        this._commit(this._measure());
    }

    /** 讀取階段:只量測 layout,不碰 canvas(供排程器批次先讀後寫)。 */
    _measure() {
        // 例外:可見資料表首次量測時固定繪圖區高度(每次進入可見模式只寫一次樣式)
        if (this._a11y && this._a11y.mode === 'visible' && !this._a11y.pin) this._a11yPin();
        const cssW = Math.max(1, this._canvasWrap.clientWidth);
        const cssH = Math.max(1, this._canvasWrap.clientHeight);
        const dpr = window.devicePixelRatio || 1;
        return { cssW, cssH, dpr, bw: Math.round(cssW * dpr), bh: Math.round(cssH * dpr) };
    }

    /** 寫入階段:套用背景儲存尺寸並繪製。 */
    _commit({ cssW, cssH, dpr, bw, bh }) {
        if (this._destroyed) return;
        if (this.canvas.width !== bw || this.canvas.height !== bh) {
            this.canvas.width = bw;
            this.canvas.height = bh;
        }
        this._released = false;
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

    /** 離視口:釋放背景儲存(canvas CSS 尺寸不變,不會回饋觸發 ResizeObserver)。 */
    _releaseBackingStore() {
        if (this._released || this._destroyed) return;
        this._released = true;
        this.canvas.width = 0;
        this.canvas.height = 0;
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

    /* ── 無障礙資料表(accessibleTable)── */

    /**
     * 圖表內容的資料表模型(accessibleTable 用;子類覆寫以回傳有意義的表格)。
     * 基底涵蓋 options.data 的常見形狀:{ labels, series }、數字陣列、[{ name|label, value }]、
     * { nodes, links }、階層 { name, children }；其餘形狀回傳 null(資料表顯示「無資料」)。
     * 欄位:key(列物件鍵)、label(表頭)、format('number'|'percent'|'datetime'|'text';
     * 省略時依值型別推斷)、unit(數值後綴)、rowHeader(該欄儲存格以 <th scope="row"> 呈現)。
     * 列物件存原始值(percent 為 0~1 比例、datetime 為 epoch 毫秒或 Date),格式化由基底負責。
     * @returns {{ caption?: string, columns: Array<{ key: string, label: string, format?: string, unit?: string, rowHeader?: boolean }>, rows: Array<Object> } | null}
     */
    getDataTable() {
        const data = this.options.data;
        if (Array.isArray(data)) {
            if (data.every((v) => v == null || typeof v !== 'object')) {
                return {
                    columns: [
                        { key: 'index', label: Locale.t('canvasChart.index') },
                        { key: 'value', label: Locale.t('canvasChart.value'), format: 'number', unit: this.options.unit }
                    ],
                    rows: data.map((value, i) => ({ index: i + 1, value }))
                };
            }
            return {
                columns: [
                    { key: 'name', label: Locale.t('canvasChart.name'), rowHeader: true },
                    { key: 'value', label: Locale.t('canvasChart.value'), format: 'number', unit: this.options.unit }
                ],
                rows: data.filter((d) => d && typeof d === 'object')
                    .map((d) => ({ name: d.name ?? d.label ?? '', value: d.value }))
            };
        }
        if (!data || typeof data !== 'object') return null;
        if (Array.isArray(data.labels) && Array.isArray(data.series)) {
            return this._a11ySeriesTable(data, { unit: this.options.unit });
        }
        if (Array.isArray(data.nodes) && Array.isArray(data.links)) {
            const nodes = data.nodes;
            const byId = new Map(nodes.filter((n) => n && n.id != null).map((n) => [n.id, n]));
            const nameOf = (ref) => {
                const n = byId.has(ref) ? byId.get(ref)
                    : typeof ref === 'number' ? nodes[ref]
                        : (ref && typeof ref === 'object' ? ref : null);
                return n ? String(n.name ?? n.label ?? n.id ?? '') : String(ref ?? '');
            };
            return {
                columns: [
                    { key: 'source', label: Locale.t('canvasChart.source') },
                    { key: 'target', label: Locale.t('canvasChart.target') },
                    { key: 'value', label: Locale.t('canvasChart.value'), format: 'number' }
                ],
                rows: data.links.filter((l) => l && typeof l === 'object')
                    .map((l) => ({ source: nameOf(l.source), target: nameOf(l.target), value: l.value }))
            };
        }
        if (data.name != null || Array.isArray(data.children)) {
            const rows = [];
            this._a11yWalkTree(data, (n) => n.name ?? n.label ?? n.id, (node, path) => rows.push({ path, value: node.value }));
            return {
                columns: [
                    { key: 'path', label: Locale.t('canvasChart.path'), rowHeader: true },
                    { key: 'value', label: Locale.t('canvasChart.value'), format: 'number' }
                ],
                rows
            };
        }
        return null;
    }

    /** 資料來源參照(render() 以參照比對偵測「資料被換掉」;資料不在 options.data 的子類覆寫)。 */
    _a11ySources() { return [this.options.data]; }

    /** { labels, series } → 類別欄 + 每系列一欄(子類共用)。 */
    _a11ySeriesTable(data, { unit = '' } = {}) {
        const labels = data && Array.isArray(data.labels) ? data.labels : [];
        const series = (data && Array.isArray(data.series) ? data.series : [])
            .filter((s) => s && Array.isArray(s.data));
        const columns = [
            { key: 'category', label: Locale.t('canvasChart.category'), rowHeader: true },
            ...series.map((s, i) => ({
                key: `s${i}`,
                label: s.name != null && s.name !== '' ? String(s.name) : Locale.t('canvasChart.series', { index: i + 1 }),
                format: 'number',
                unit
            }))
        ];
        if (!series.length) return { columns, rows: [] };
        const rows = labels.map((category, li) => {
            const row = { category };
            series.forEach((s, i) => { row[`s${i}`] = s.data[li]; });
            return row;
        });
        return { columns, rows };
    }

    /**
     * 階層前序走訪(祖先集合防環;不修改原資料)。
     * @param {Object} root
     * @param {(node:Object)=>*} nameOf - 路徑節段名稱
     * @param {(node:Object, path:string, depth:number)=>void} visit - path 以「 / 」串接
     */
    _a11yWalkTree(root, nameOf, visit) {
        const ancestors = new Set();
        const walk = (node, prefix, depth) => {
            if (!node || typeof node !== 'object' || ancestors.has(node)) return;
            const name = nameOf(node);
            const segment = name == null ? '' : String(name);
            const path = depth === 0 ? segment : `${prefix} / ${segment}`;
            visit(node, path, depth);
            const kids = Array.isArray(node.children) ? node.children : [];
            if (!kids.length) return;
            ancestors.add(node);
            for (const kid of kids) walk(kid, path, depth + 1);
            ancestors.delete(node);
        };
        walk(root, '', 0);
    }

    /** render() 入口:偵測開關/模式變更與資料參照變更(參照比對 O(1),不隨動畫幀重建)。 */
    _syncAccessibleTable() {
        const mode = a11yMode(this.options.accessibleTable);
        const a = this._a11y;
        if (mode !== (a ? a.mode : null)) {
            this._a11yApply(mode);
            return;
        }
        if (a && a.sources && !sameRefs(a.sources, this._a11ySources())) this._a11yMarkDirty();
    }

    /** 建立/切換/移除資料表(mode:'hidden'|'visible'|null)。 */
    _a11yApply(mode) {
        if (!mode) { this._a11yTeardown(); return; }
        if (this._destroyed || !this.element || !this.canvas) return;
        let a = this._a11y;
        if (!a) {
            const tableId = nextUid('cl-chart-table');
            const wrap = document.createElement('div');
            wrap.className = 'cl-canvas-chart__a11y';
            const table = document.createElement('table');
            table.id = tableId;
            table.className = 'cl-canvas-chart__table';
            wrap.appendChild(table);
            this.element.appendChild(wrap);             // 根元素內、canvas(繪圖區)之後
            a = this._a11y = {
                mode: null, wrap, table, tableId, captionId: `${tableId}-caption`,
                dirty: false, queued: false, sources: null, pin: null,
                addedRole: false, addedLabel: false, onLocale: null
            };
            // 語系切換 → 重建標題/表頭/數字格式(只在資料表存在期間監聽,teardown 移除)
            a.onLocale = () => this._a11yMarkDirty();
            if (typeof window !== 'undefined') window.addEventListener('locale-changed', a.onLocale);
            // canvas:保留既有 role / aria-label(基底預設即有),只補缺;aria-describedby 指向資料表
            const c = this.canvas;
            if (!c.getAttribute('role')) { c.setAttribute('role', 'img'); a.addedRole = true; }
            if (!c.getAttribute('aria-label')) { c.setAttribute('aria-label', this._a11yCaption(null)); a.addedLabel = true; }
            const ids = (c.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
            if (!ids.includes(tableId)) c.setAttribute('aria-describedby', [...ids, tableId].join(' '));
        }
        if (a.mode === mode) return;
        if (a.mode === 'visible') this._a11yUnpin(a);
        a.mode = mode;
        const w = a.wrap;
        w.dataset.mode = mode;
        if (mode === 'visible') {
            w.style.cssText = A11Y_VISIBLE_CSS;
            w.setAttribute('role', 'region');            // 可捲動區:可聚焦 + 以 caption 命名
            w.setAttribute('tabindex', '0');
            w.setAttribute('aria-labelledby', a.captionId);
            a.table.style.cssText = A11Y_TABLE_CSS;
        } else {
            w.style.cssText = A11Y_HIDDEN_CSS;
            w.removeAttribute('role');                   // 隱藏時不可成為 Tab 停駐點
            w.removeAttribute('tabindex');
            w.removeAttribute('aria-labelledby');
            a.table.style.cssText = '';
        }
        this._a11yMarkDirty();                           // 儲存格樣式依模式而異 → 重建
    }

    /** 移除資料表、語系監聽、可見模式的高度固定,並還原 canvas ARIA(destroy / 關閉時)。 */
    _a11yTeardown() {
        const a = this._a11y;
        if (!a) return;
        this._a11y = null;
        this._a11yUnpin(a);
        if (typeof window !== 'undefined' && a.onLocale) window.removeEventListener('locale-changed', a.onLocale);
        if (a.wrap.parentNode) a.wrap.parentNode.removeChild(a.wrap);
        const c = this.canvas;
        if (!c) return;
        const ids = (c.getAttribute('aria-describedby') || '').split(/\s+/).filter((id) => id && id !== a.tableId);
        if (ids.length) c.setAttribute('aria-describedby', ids.join(' '));
        else c.removeAttribute('aria-describedby');
        if (a.addedRole) c.removeAttribute('role');
        if (a.addedLabel) c.removeAttribute('aria-label');
    }

    /** 標記資料表需重建;同一輪事件內多次變更以微任務合併為一次(與繪製排程脫鉤,離視口亦同步)。 */
    _a11yMarkDirty() {
        const a = this._a11y;
        if (!a || this._destroyed) return;
        a.dirty = true;
        if (a.queued) return;
        a.queued = true;
        const run = () => {
            a.queued = false;
            if (this._destroyed || this._a11y !== a || !a.dirty) return;
            a.dirty = false;
            this._a11yBuild(a);
        };
        if (typeof queueMicrotask === 'function') queueMicrotask(run);
        else Promise.resolve().then(run);
    }

    /** caption:getDataTable().caption → title → ariaLabel → Locale 預設。 */
    _a11yCaption(model) {
        const o = this.options;
        const text = (v) => (v != null && v !== '' ? String(v) : '');
        return text(model && model.caption) || text(o.title) || text(o.ariaLabel) || Locale.t('canvasChart.tableCaption');
    }

    /** 依 getDataTable() 重建 caption / thead / tbody(一律 textContent;超過上限以末列註明省略筆數)。 */
    _a11yBuild(a) {
        let model = null;
        try {
            model = this.getDataTable();
        } catch (e) {
            console.error('[CanvasChart] getDataTable 失敗:', e);
        }
        a.sources = this._a11ySources();
        const lang = Locale.getLang();
        const visible = a.mode === 'visible';
        const columns = (model && Array.isArray(model.columns) ? model.columns : [])
            .filter((c) => c && c.key != null && c.key !== '');
        const rows = model && Array.isArray(model.rows) ? model.rows : [];
        let max = Number(this.options.accessibleTableMaxRows);
        if (Number.isNaN(max) || max < 0) max = A11Y_DEFAULT_MAX_ROWS;
        const shown = Math.min(rows.length, Math.floor(max));
        const span = String(Math.max(1, columns.length));

        const table = a.table;
        table.textContent = '';
        const caption = document.createElement('caption');
        caption.id = a.captionId;
        caption.textContent = this._a11yCaption(model);
        if (visible) caption.style.cssText = A11Y_CAPTION_CSS;
        table.appendChild(caption);
        if (a.addedLabel && this.canvas) this.canvas.setAttribute('aria-label', caption.textContent);

        if (columns.length) {
            const thead = document.createElement('thead');
            const tr = document.createElement('tr');
            for (const col of columns) {
                const th = document.createElement('th');
                th.setAttribute('scope', 'col');
                th.textContent = col.label != null && col.label !== '' ? String(col.label) : String(col.key);
                if (visible) th.style.cssText = A11Y_TH_COL_CSS + (a11yNumericColumn(col) ? A11Y_NUM_CSS : '');
                tr.appendChild(th);
            }
            thead.appendChild(tr);
            table.appendChild(thead);
        }

        const tbody = document.createElement('tbody');
        const note = (text) => {
            const tr = document.createElement('tr');
            tr.className = 'cl-canvas-chart__table-note';
            const td = document.createElement('td');
            td.setAttribute('colspan', span);
            td.textContent = text;
            if (visible) td.style.cssText = A11Y_NOTE_CSS;
            tr.appendChild(td);
            return tr;
        };
        if (!columns.length || !rows.length) {
            tbody.appendChild(note(Locale.t('canvasChart.empty')));
        } else {
            for (let i = 0; i < shown; i++) {
                const row = rows[i] && typeof rows[i] === 'object' ? rows[i] : {};
                const tr = document.createElement('tr');
                for (const col of columns) {
                    const value = row[col.key];
                    const cell = document.createElement(col.rowHeader ? 'th' : 'td');
                    if (col.rowHeader) cell.setAttribute('scope', 'row');
                    cell.textContent = a11yCellText(value, col, lang);
                    if (visible) {
                        const numeric = a11yNumericColumn(col) || typeof value === 'number';
                        cell.style.cssText = (col.rowHeader ? A11Y_TH_ROW_CSS : A11Y_TD_CSS) + (numeric ? A11Y_NUM_CSS : '');
                    }
                    tr.appendChild(cell);
                }
                tbody.appendChild(tr);
            }
            if (rows.length > shown) {
                const count = a11yFormatter('number', lang).format(rows.length - shown);
                tbody.appendChild(note(Locale.t('canvasChart.truncated', { count })));
            }
        }
        table.appendChild(tbody);
    }

    /**
     * 可見模式:把繪圖區高度固定為目前像素高、根元素改 height:auto,資料表接在下方撐高根元素,
     * 圖表本身不被擠壓。量測時暫時隱藏資料表;尚未排版(離線/隱藏)時略過,下次量測再試。
     */
    _a11yPin() {
        const a = this._a11y;
        const body = this._canvasWrap;
        if (!a || !this.element || !body || !body.clientHeight) return;
        const prevDisplay = a.wrap.style.display;
        a.wrap.style.display = 'none';
        const h = body.clientHeight;
        a.wrap.style.display = prevDisplay;
        if (!(h > 0)) return;
        a.pin = { rootHeight: this.element.style.height, bodyFlex: body.style.flex, bodyHeight: body.style.height };
        this.element.style.height = 'auto';
        body.style.flex = '0 0 auto';
        body.style.height = `${h}px`;
    }

    _a11yUnpin(a = this._a11y) {
        if (!a || !a.pin) return;
        const pin = a.pin;
        a.pin = null;
        if (this.element) this.element.style.height = pin.rootHeight;
        if (this._canvasWrap) {
            this._canvasWrap.style.flex = pin.bodyFlex;
            this._canvasWrap.style.height = pin.bodyHeight;
        }
    }

    /* ── 匯出 ── */

    /**
     * 高倍離屏重渲匯出 PNG(列印銳利度補償;預設 2 倍)。
     * @returns {string} dataURL
     */
    exportPNG(scale = 2) {
        // 釋放中或尚未繪過:先同步重渲,避免匯出空白
        if (this._released || !this.canvas.width || !this.canvas.height) this._renderNow();
        const w = this._lastSize ? this._lastSize.w : this._canvasWrap.clientWidth;
        const h = this._lastSize ? this._lastSize.h : this._canvasWrap.clientHeight;
        const off = document.createElement('canvas');
        off.width = Math.round(w * scale);
        off.height = Math.round(h * scale);
        const ctx = off.getContext('2d');
        ctx.setTransform(scale, 0, 0, scale, 0, 0);
        const { '--cl-bg': bg } = this.tokens(['--cl-bg']);
        ctx.fillStyle = bg || FALLBACK_PAINT;
        ctx.fillRect(0, 0, w, h);
        const savedRegions = this._regions;
        this._regions = [];
        try { this.draw(ctx, w, h); } finally { this._regions = savedRegions; }
        const dataUrl = off.toDataURL('image/png');
        // 匯出走離屏 canvas，主背存於離視口時無需保留，釋回記憶體
        if (this._offscreen) this._releaseBackingStore();
        return dataUrl;
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
        if (this._a11y) this._a11yMarkDirty();   // 選項/資料更新 → 資料表重建一次(同一輪合併)
        this.render();                            // render() 內含 accessibleTable 開關偵測
    }

    destroy() {
        this._destroyed = true;
        _pending.delete(this);
        this._a11yTeardown();
        if (this._offTheme) this._offTheme();
        if (this._resizeObserver) this._resizeObserver.disconnect();
        if (this._io) this._io.disconnect();
        this.canvas.removeEventListener('mousemove', this._onMove);
        this.canvas.removeEventListener('mouseleave', this._onLeave);
        this.canvas.removeEventListener('click', this._onClick);
        if (this.element && this.element.parentNode) this.element.parentNode.removeChild(this.element);
        this.element = null;
    }
}

export default CanvasChart;
