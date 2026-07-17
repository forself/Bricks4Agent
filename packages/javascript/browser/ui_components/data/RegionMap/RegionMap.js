/**
 * RegionMap — 台灣著色地圖(Canvas 版;SVG 禁用政策下的重寫,API 向後相容)。
 *
 * 架構:自管 Canvas(不繼承 CanvasChart,因 CanvasChart 預設 padding/title DOM 結構
 * 與地圖全出血置中需求衝突;但複用 theme-bus resolveTokens / onThemeChange 訂閱,
 * 以及與 CanvasChart 相同的 addRegion / _hitTest path 分支邏輯)。
 *
 * 技術要點:
 * - Path2D 直接吃原 SVG path 字串(d 屬性一字不改)
 * - viewBox 0 0 400 700 → canvas 等比縮放置中
 * - isPointInPath(path, x*dpr, y*dpr) 命中測試(同 CanvasChart._hitTest path 分支)
 * - ThemeBus onThemeChange 重繪;destroy 解除訂閱
 * - 顏色全走 resolveTokens token(禁硬編)
 */
import { onThemeChange, resolveTokens, FALLBACK_PAINT } from '../../utils/theme-bus.js';

/* ── 台灣區域資料(43 條 SVG path 字串,viewBox 0 0 400 700)────────────────── */
const REGIONS_DATA = [
    { code: 'LJC', name: '連江縣', paths: ['M30,40 L45,35 L50,45 L40,55 L28,50 Z', 'M55,50 L65,48 L68,58 L58,60 Z'] },
    { code: 'KMC', name: '金門縣', paths: ['M25,130 L55,120 L70,130 L65,145 L35,150 L20,140 Z', 'M75,140 L85,138 L88,148 L78,150 Z'] },
    { code: 'PHC', name: '澎湖縣', paths: ['M100,290 L115,280 L130,285 L135,300 L125,320 L110,325 L95,315 L90,300 Z', 'M140,295 L150,290 L155,305 L145,310 Z'] },
    { code: 'KLU', name: '基隆市', paths: ['M305,65 L325,60 L340,70 L335,85 L315,90 L300,80 Z'] },
    { code: 'TPE', name: '臺北市', paths: ['M270,80 L295,75 L310,90 L305,110 L280,115 L265,100 Z'] },
    { code: 'NWT', name: '新北市', paths: ['M245,60 L270,55 L305,60 L340,70 L345,95 L340,130 L310,150 L280,160 L250,145 L240,115 L230,90 L235,70 Z M265,100 L280,115 L305,110 L310,90 L295,75 L270,80 Z'] },
    { code: 'TYN', name: '桃園市', paths: ['M195,105 L230,95 L250,110 L255,145 L230,165 L200,160 L180,140 L185,115 Z'] },
    { code: 'HSZ', name: '新竹市', paths: ['M185,165 L210,160 L220,175 L210,190 L185,185 Z'] },
    { code: 'HSC', name: '新竹縣', paths: ['M200,160 L230,150 L265,155 L280,175 L270,200 L240,210 L210,205 L210,190 L220,175 L210,160 Z'] },
    { code: 'MLC', name: '苗栗縣', paths: ['M175,190 L210,185 L240,195 L270,210 L280,240 L260,270 L220,275 L180,260 L165,225 Z'] },
    { code: 'YLC', name: '宜蘭縣', paths: ['M310,130 L345,110 L365,130 L370,175 L350,215 L315,220 L290,200 L285,165 L295,145 Z'] },
    { code: 'TXG', name: '臺中市', paths: ['M160,240 L205,230 L260,250 L290,280 L285,320 L250,350 L200,345 L165,310 L145,270 Z'] },
    { code: 'HUA', name: '花蓮縣', paths: ['M295,200 L330,190 L360,220 L375,280 L370,360 L340,400 L305,380 L290,320 L280,260 Z'] },
    { code: 'CHW', name: '彰化縣', paths: ['M145,285 L175,270 L200,285 L210,320 L195,355 L160,360 L135,340 L130,305 Z'] },
    { code: 'NTC', name: '南投縣', paths: ['M195,300 L250,290 L290,310 L300,370 L280,420 L240,430 L200,410 L180,370 L180,330 Z'] },
    { code: 'YUN', name: '雲林縣', paths: ['M130,345 L165,335 L200,350 L210,385 L190,415 L150,420 L120,400 L115,365 Z'] },
    { code: 'CYI', name: '嘉義市', paths: ['M165,420 L185,415 L195,430 L185,445 L165,440 Z'] },
    { code: 'CYC', name: '嘉義縣', paths: ['M125,400 L160,385 L200,400 L240,420 L250,455 L230,485 L180,490 L140,470 L120,435 Z M165,420 L185,415 L195,430 L185,445 L165,440 Z'] },
    { code: 'TTT', name: '臺東縣', paths: ['M295,385 L340,370 L370,400 L375,480 L350,530 L300,520 L270,480 L275,420 Z'] },
    { code: 'TNN', name: '臺南市', paths: ['M125,450 L165,440 L210,455 L240,485 L235,530 L195,555 L150,545 L120,510 L115,470 Z'] },
    { code: 'KHH', name: '高雄市', paths: ['M155,520 L200,510 L250,480 L285,465 L300,500 L295,560 L260,590 L210,600 L170,580 L145,545 Z'] },
    { code: 'PIF', name: '屏東縣', paths: ['M200,565 L250,545 L290,530 L320,555 L335,610 L315,660 L270,680 L230,670 L200,640 L190,595 Z'] },
];

/* viewBox 尺寸常數 */
const VB_W = 400, VB_H = 700;

export class RegionMap {
    /**
     * @param {Object} options
     * @param {Object} options.data - 區域資料 { 'TPE': { value: 100, label: '台北' }, ... }
     * @param {string} options.width - 寬度
     * @param {string} options.height - 高度
     * @param {string} options.defaultColor - 預設顏色
     * @param {Function} options.colorScale - 顏色映射函式 (value) => color
     * @param {Function} options.onClick - 點擊回調 (regionCode) => void
     * @param {Function} options.onChange - 選取變更回調
     * @param {boolean} options.showLabels - 顯示標籤
     * @param {boolean} options.showValues - 顯示數值
     */
    constructor(options = {}) {
        this.options = {
            data: {},
            width: '100%',
            height: '500px',
            defaultColor: 'var(--cl-border-light)',
            hoverColor: 'var(--cl-primary-light)',
            selectedColor: 'var(--cl-primary)',
            colorScale: null,
            onClick: null,
            onChange: null,
            showLabels: true,
            showValues: false,
            labelFontSize: 12,
            valueFontSize: 10,
            ...options
        };

        this.regions = new Map();        // code → { name, paths: Path2D[] }
        this.selectedRegion = null;
        this._hoverCode = null;
        this._renderScheduled = false;
        this._destroyed = false;
        this._offTheme = null;
        this._resizeObserver = null;
        this._raf = 0;

        this._buildDom();
        this._buildPaths();
        this._bindEvents();
        this._offTheme = onThemeChange(() => this.render());
        if (typeof ResizeObserver !== 'undefined') {
            this._resizeObserver = new ResizeObserver(() => this.render());
            this._resizeObserver.observe(this.element);
        }
        this.render();
    }

    /* ── DOM 建構 ──────────────────────────────────────────────────────── */

    _buildDom() {
        const el = document.createElement('div');
        el.className = 'region-map';
        el.style.cssText = [
            'position: relative;',
            `width: ${this.options.width};`,
            `height: ${this.options.height};`,
            'background: var(--cl-bg-tertiary);',
            'border: 1px solid var(--cl-border);',
            'border-radius: var(--cl-radius-lg);',
            'display: flex;',
            'align-items: center;',
            'justify-content: center;',
            'overflow: hidden;'
        ].join(' ');

        const canvas = document.createElement('canvas');
        canvas.style.cssText = 'display: block; width: 100%; height: 100%;';
        canvas.setAttribute('role', 'img');
        canvas.setAttribute('aria-label', '台灣行政區地圖');
        el.appendChild(canvas);

        /* Tooltip:DOM 浮層(文字走 textContent,免 XSS;CSSOM cssText,免 inline style) */
        const tip = document.createElement('div');
        tip.style.cssText = [
            'position: absolute;',
            'background: var(--cl-bg-overlay-strong);',
            'color: var(--cl-text-inverse);',
            'padding: 8px 12px;',
            'border-radius: var(--cl-radius-sm);',
            'font-size: var(--cl-font-size-sm);',
            'pointer-events: none;',
            'opacity: 0;',
            'transition: opacity var(--cl-transition);',
            'z-index: 1000;',
            'white-space: nowrap;'
        ].join(' ');
        el.appendChild(tip);

        this.element = el;
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        this.tooltip = tip;
    }

    /* ── Path2D 建構(每個 region 的每條 path 轉 Path2D)──────────────── */

    _buildPaths() {
        for (const r of REGIONS_DATA) {
            const path2ds = r.paths.map(d => new Path2D(d));
            this.regions.set(r.code, { name: r.name, paths: path2ds });
        }
    }

    /* ── 事件 ─────────────────────────────────────────────────────────── */

    _bindEvents() {
        this._onMove = (e) => this._handleMove(e);
        this._onLeave = () => {
            if (this._hoverCode !== null) {
                this._hoverCode = null;
                this.canvas.style.cursor = 'default';
                this.tooltip.style.opacity = '0';
                this.render();
            }
        };
        this._onClick = (e) => this._handleClick(e);
        this.canvas.addEventListener('mousemove', this._onMove);
        this.canvas.addEventListener('mouseleave', this._onLeave);
        this.canvas.addEventListener('click', this._onClick);
    }

    _handleMove(e) {
        const hit = this._hitCode(e.offsetX, e.offsetY);
        if (hit !== this._hoverCode) {
            this._hoverCode = hit;
            this.canvas.style.cursor = (hit && this.options.onClick) ? 'pointer' : 'default';
            this.render();
        }
        if (hit) {
            this._showTooltip(e, hit);
        } else {
            this.tooltip.style.opacity = '0';
        }
    }

    _handleClick(e) {
        const hit = this._hitCode(e.offsetX, e.offsetY);
        if (!hit) return;
        const prev = this.selectedRegion;
        this.selectedRegion = hit;
        if (prev !== hit) this.render();
        const r = this.regions.get(hit);
        if (this.options.onClick) this.options.onClick(hit);
        if (this.options.onChange) this.options.onChange({ code: hit, name: r ? r.name : hit });
    }

    /* ── 命中測試(與 CanvasChart._hitTest path 分支相同 DPR 乘法)──────── */

    _hitCode(cssX, cssY) {
        const { scale, offX, offY } = this._transform || { scale: 1, offX: 0, offY: 0 };
        const dpr = window.devicePixelRatio || 1;
        /* offsetX/Y 是 CSS px;isPointInPath 期望 backing-store px */
        const bx = cssX * dpr;
        const by = cssY * dpr;
        const ctx = this.ctx;
        ctx.save();
        /* 反向套用 viewBox → canvas 變換(DPR 已包在 setTransform 裡) */
        ctx.setTransform(scale * dpr, 0, 0, scale * dpr, offX * dpr, offY * dpr);
        for (const [code, { paths }] of this.regions) {
            for (const p of paths) {
                if (ctx.isPointInPath(p, bx, by)) {
                    ctx.restore();
                    return code;
                }
            }
        }
        ctx.restore();
        return null;
    }

    /* ── 渲染管線 ─────────────────────────────────────────────────────── */

    render() {
        if (this._renderScheduled || this._destroyed) return;
        this._renderScheduled = true;
        this._raf = requestAnimationFrame(() => {
            this._renderScheduled = false;
            this._draw();
        });
    }

    _draw() {
        if (this._destroyed) return;
        const cssW = Math.max(1, this.canvas.clientWidth);
        const cssH = Math.max(1, this.canvas.clientHeight);
        const dpr = window.devicePixelRatio || 1;
        const bw = Math.round(cssW * dpr), bh = Math.round(cssH * dpr);
        if (this.canvas.width !== bw || this.canvas.height !== bh) {
            this.canvas.width = bw;
            this.canvas.height = bh;
        }

        const ctx = this.ctx;
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, cssW, cssH);

        /* 等比縮放 viewBox 置中 */
        const scaleX = cssW / VB_W, scaleY = cssH / VB_H;
        const scale = Math.min(scaleX, scaleY);
        const offX = (cssW - VB_W * scale) / 2;
        const offY = (cssH - VB_H * scale) / 2;
        this._transform = { scale, offX, offY };

        /* 解析 token */
        const tok = resolveTokens([
            '--cl-border-light', '--cl-primary-light', '--cl-primary',
            '--cl-bg', '--cl-text', '--cl-text-light', '--cl-primary-dark',
            '--cl-border', '--cl-font-family-cjk', '--cl-font-family'
        ], this.element);

        const defaultFill = tok['--cl-border-light'] || FALLBACK_PAINT;
        const hoverFill   = tok['--cl-primary-light'] || FALLBACK_PAINT;
        const selectedFill = tok['--cl-primary'] || FALLBACK_PAINT;
        const strokeColor = tok['--cl-bg'] || FALLBACK_PAINT;
        const fontFam = [tok['--cl-font-family-cjk'], tok['--cl-font-family']].filter(Boolean).join(', ') || 'sans-serif';

        ctx.save();
        ctx.translate(offX, offY);
        ctx.scale(scale, scale);

        for (const [code, { name, paths }] of this.regions) {
            const rData = this.options.data[code];

            /* 決定填色 */
            let fill = defaultFill;
            if (code === this.selectedRegion) {
                fill = selectedFill;
            } else if (code === this._hoverCode) {
                fill = hoverFill;
            } else if (rData) {
                if (rData.color) {
                    fill = rData.color;
                } else if (this.options.colorScale) {
                    fill = this.options.colorScale(rData.value);
                }
            }

            for (const p of paths) {
                ctx.fillStyle = fill;
                ctx.fill(p);
                ctx.strokeStyle = strokeColor;
                ctx.lineWidth = 0.5 / scale;   /* 視覺常寬 0.5px */
                ctx.stroke(p);
            }

            /* 標籤:取 path 的 bounding box 近似中心(viewBox 座標) */
            if ((this.options.showLabels || this.options.showValues) && rData) {
                this._drawLabel(ctx, code, name, rData, paths, tok, fontFam, scale);
            }
        }

        ctx.restore();
    }

    _drawLabel(ctx, code, name, rData, paths, tok, fontFam, scale) {
        /* 近似中心:取所有 path 合併後 boundingRect 估算(Canvas 無 getBBox;用採樣點估算) */
        let xMin = Infinity, xMax = -Infinity, yMin = Infinity, yMax = -Infinity;
        /* 掃描 REGIONS_DATA 中對應 paths 的座標字串取數字 */
        const meta = REGIONS_DATA.find(r => r.code === code);
        if (meta) {
            for (const dStr of meta.paths) {
                const nums = dStr.match(/-?[\d.]+/g);
                if (!nums) continue;
                for (let i = 0; i + 1 < nums.length; i += 2) {
                    const x = parseFloat(nums[i]), y = parseFloat(nums[i + 1]);
                    if (x < xMin) xMin = x; if (x > xMax) xMax = x;
                    if (y < yMin) yMin = y; if (y > yMax) yMax = y;
                }
            }
        }
        if (!isFinite(xMin)) return;
        const cx = (xMin + xMax) / 2, cy = (yMin + yMax) / 2;

        const { labelFontSize, valueFontSize, showLabels, showValues } = this.options;
        const textColor = tok['--cl-text'] || FALLBACK_PAINT;
        const valColor  = tok['--cl-primary-dark'] || FALLBACK_PAINT;
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.save();
        /* label 字縮放補償 canvas scale,讓 font size 對應 CSS px */
        const fs = (labelFontSize || 12) / scale;
        let yOff = 0;
        if (showLabels && name) {
            ctx.font = `500 ${fs}px ${fontFam}`;
            ctx.fillStyle = textColor;
            ctx.fillText(name, cx, cy + yOff);
            yOff += fs + 2 / scale;
        }
        if (showValues && rData?.value !== undefined) {
            const vfs = (valueFontSize || 10) / scale;
            ctx.font = `bold ${vfs}px ${fontFam}`;
            ctx.fillStyle = valColor;
            ctx.fillText(String(rData.value), cx, cy + yOff);
        }
        ctx.restore();
    }

    /* ── Tooltip ──────────────────────────────────────────────────────── */

    _showTooltip(e, code) {
        const r = this.regions.get(code);
        const rData = this.options.data[code];
        const tip = this.tooltip;
        tip.textContent = '';

        const titleEl = document.createElement('div');
        titleEl.style.cssText = 'font-weight:600; margin-bottom:4px;';
        titleEl.textContent = r ? r.name : code;
        tip.appendChild(titleEl);

        if (rData) {
            if (rData.value !== undefined) {
                const valueRow = document.createElement('div');
                valueRow.appendChild(document.createTextNode('數值: '));
                const valueEl = document.createElement('span');
                valueEl.style.cssText = 'color:var(--cl-primary-light)';
                valueEl.textContent = String(rData.value);
                valueRow.appendChild(valueEl);
                tip.appendChild(valueRow);
            }
            for (const key of Object.keys(rData)) {
                if (key !== 'value' && key !== 'color' && key !== 'label' && typeof rData[key] !== 'object') {
                    const extraRow = document.createElement('div');
                    extraRow.style.cssText = 'font-size:var(--cl-font-size-xs); color:var(--cl-text-light)';
                    extraRow.textContent = `${key}: ${rData[key]}`;
                    tip.appendChild(extraRow);
                }
            }
        }

        tip.style.opacity = '1';
        this._moveTooltip(e);
    }

    _moveTooltip(e) {
        const rect = this.element.getBoundingClientRect();
        let x = e.clientX - rect.left + 15;
        let y = e.clientY - rect.top - 10;
        const tipRect = this.tooltip.getBoundingClientRect();
        if (x + tipRect.width > rect.width) x = e.clientX - rect.left - tipRect.width - 15;
        this.tooltip.style.left = `${x}px`;
        this.tooltip.style.top = `${y}px`;
    }

    /* ── 公開 API(與舊版完全相容)──────────────────────────────────── */

    /** 更新統計資料並重繪。 */
    setData(data) {
        this.options.data = data;
        this.render();
    }

    /** 設定顏色比例尺並重繪。 */
    setColorScale(colorScale) {
        this.options.colorScale = colorScale;
        this.render();
    }

    /** 高亮指定區域(以選取色描邊;canvas 版透過重繪實現)。 */
    highlightRegion(code) {
        this.selectedRegion = code;
        this.render();
    }

    /** 清除高亮。 */
    clearHighlight() {
        this.selectedRegion = null;
        this.render();
    }

    /** 建立顏色比例尺(靜態工具方法;保持原有 API)。 */
    static createColorScale(min, max, colors = ['var(--cl-primary-light)', 'var(--cl-primary-dark)']) {
        const startColor = RegionMap._resolveColorToRgb(colors[0]) || { r: 220, g: 235, b: 255 };
        const endColor   = RegionMap._resolveColorToRgb(colors[1]) || { r: 26,  g: 115, b: 232 };
        return (value) => {
            if (value === undefined || value === null) return RegionMap._rgbToHex(startColor);
            const range = max - min;
            const ratio = range === 0 ? 1 : (value - min) / range;
            const t = Math.max(0, Math.min(1, ratio));
            return RegionMap._rgbToHex({
                r: Math.round(startColor.r + (endColor.r - startColor.r) * t),
                g: Math.round(startColor.g + (endColor.g - startColor.g) * t),
                b: Math.round(startColor.b + (endColor.b - startColor.b) * t),
            });
        };
    }

    static _resolveColorToRgb(color) {
        if (typeof color !== 'string' || !color.trim()) return null;
        const n = color.trim();
        const hexM = n.match(/^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/);
        if (hexM) {
            const h = hexM[1].length === 3 ? hexM[1].split('').map(c => c + c).join('') : hexM[1];
            return { r: parseInt(h.slice(0, 2), 16), g: parseInt(h.slice(2, 4), 16), b: parseInt(h.slice(4, 6), 16) };
        }
        const rgbM = n.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
        if (rgbM) return { r: parseInt(rgbM[1], 10), g: parseInt(rgbM[2], 10), b: parseInt(rgbM[3], 10) };
        if (typeof document === 'undefined') return null;
        const probe = document.createElement('span');
        probe.style.cssText = `color:${n}; position:absolute; opacity:0; pointer-events:none;`;
        (document.body || document.documentElement).appendChild(probe);
        const resolved = getComputedStyle(probe).color;
        probe.remove();
        const rm = resolved.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
        return rm ? { r: parseInt(rm[1], 10), g: parseInt(rm[2], 10), b: parseInt(rm[3], 10) } : null;
    }

    static _rgbToHex({ r, g, b }) {
        return '#' + [r, g, b].map(v => v.toString(16).padStart(2, '0')).join('');
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._destroyed = true;
        cancelAnimationFrame(this._raf);
        if (this._offTheme) this._offTheme();
        if (this._resizeObserver) this._resizeObserver.disconnect();
        this.canvas.removeEventListener('mousemove', this._onMove);
        this.canvas.removeEventListener('mouseleave', this._onLeave);
        this.canvas.removeEventListener('click', this._onClick);
        if (this.element?.parentNode) this.element.remove();
        this.element = null;
    }
}

export default RegionMap;
