/**
 * HeatmapChart — 網格熱圖(CanvasChart 子類;「顏色深淺/冷暖表一個維度」的旗艦)。
 * 典型場景:轄區 × 案類 × 件數。
 *
 * @example
 * new HeatmapChart({
 *     container, title: '轄區×案類 件數',
 *     xLabels: ['中山','大安'], yLabels: ['竊盜','詐欺'],
 *     matrix: [[3,5],[2,null]],          // matrix[yi][xi];null=無資料格
 *     unit: '件', hue: 'red',            // scale:'sequential'(預設)|'diverging'
 *     showValues: 'auto'                 // 'auto'(格夠大才標)|true|false
 * }).mount(host);
 */
import { CanvasChart } from './CanvasChart.js';
import { sequentialScale, divergingScale } from '../utils/color-scale.js';

export class HeatmapChart extends CanvasChart {
    constructor(options = {}) {
        super({
            xLabels: [], yLabels: [], matrix: [],
            unit: '', hue: 'red', negHue: 'blue',
            scale: 'sequential',
            showValues: 'auto',
            colorDomain: null,        // [min,max](sequential)/[min,mid,max](diverging);不給自算
            legend: true,
            padding: { top: 8, right: 12, bottom: 40, left: 64 },
            ...options
        });
    }

    _flat() {
        const out = [];
        for (const row of this.options.matrix) for (const v of row) if (v != null && !Number.isNaN(v)) out.push(v);
        return out;
    }

    _makeScale() {
        const o = this.options;
        const vals = this._flat();
        if (!vals.length) return null;
        if (o.scale === 'diverging') {
            const d = o.colorDomain || (() => {
                const min = Math.min(...vals), max = Math.max(...vals);
                return [min, (min + max) / 2, max];
            })();
            return divergingScale(o.negHue, o.hue, d);
        }
        const d = o.colorDomain || [Math.min(...vals), Math.max(...vals)];
        return sequentialScale(o.hue, d);
    }

    draw(ctx, w, h) {
        const o = this.options;
        const t = this.tokens(['--cl-text', '--cl-text-secondary', '--cl-border', '--cl-bg-secondary', '--cl-text-dim']);
        const legendH = o.legend ? 30 : 0;
        const p = o.padding;
        const gx = p.left, gy = p.top;
        const gw = Math.max(10, w - p.left - p.right);
        const gh = Math.max(10, h - p.top - p.bottom - legendH);
        const nx = o.xLabels.length, ny = o.yLabels.length;

        if (!nx || !ny || !this._flat().length) {
            ctx.font = this.font(13);
            ctx.fillStyle = t['--cl-text-dim'];
            ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }
        const scale = this._makeScale();
        const cw = gw / nx, ch = gh / ny;
        const gap = Math.min(2, cw * 0.06, ch * 0.06);

        // 格子
        for (let yi = 0; yi < ny; yi++) {
            for (let xi = 0; xi < nx; xi++) {
                const v = (o.matrix[yi] || [])[xi];
                const x = gx + xi * cw, y = gy + yi * ch;
                if (v == null || Number.isNaN(v)) {
                    ctx.fillStyle = t['--cl-bg-secondary'];
                    ctx.fillRect(x + gap / 2, y + gap / 2, cw - gap, ch - gap);
                    continue;
                }
                ctx.fillStyle = scale(v);
                ctx.fillRect(x + gap / 2, y + gap / 2, cw - gap, ch - gap);
                this.addRegion({
                    shape: 'rect', x: x, y: y, w: cw, h: ch,
                    data: { x: o.xLabels[xi], y: o.yLabels[yi], value: v }
                });
            }
        }
        // 格值標註(格夠大才標;亮度自適應文字色)
        const showV = o.showValues === true || (o.showValues === 'auto' && cw >= 34 && ch >= 20);
        if (showV) {
            ctx.font = this.font(Math.min(12, ch * 0.5));
            ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
            for (let yi = 0; yi < ny; yi++) for (let xi = 0; xi < nx; xi++) {
                const v = (o.matrix[yi] || [])[xi];
                if (v == null || Number.isNaN(v)) continue;
                const [r, g, b] = this._rgbOf(scale(v));
                ctx.fillStyle = (0.299 * r + 0.587 * g + 0.114 * b) > 150 ? '#00000099' : '#ffffffcc';
                ctx.fillText(this.fmt(v), gx + xi * cw + cw / 2, gy + yi * ch + ch / 2);
            }
        }
        // 軸標籤
        ctx.fillStyle = t['--cl-text-secondary'];
        ctx.font = this.font(11);
        ctx.textAlign = 'right'; ctx.textBaseline = 'middle';
        for (let yi = 0; yi < ny; yi++) {
            ctx.fillText(this.ellipsis(ctx, o.yLabels[yi], p.left - 10), gx - 6, gy + yi * ch + ch / 2);
        }
        ctx.textAlign = 'center'; ctx.textBaseline = 'top';
        const thin = Math.max(1, Math.ceil((nx * 56) / gw));      // 標籤過密抽稀
        for (let xi = 0; xi < nx; xi += thin) {
            ctx.fillText(this.ellipsis(ctx, o.xLabels[xi], cw * thin - 4), gx + xi * cw + cw / 2, gy + gh + 6);
        }
        // 色帶圖例
        if (o.legend) this._drawLegend(ctx, scale, gx, gy + gh + 22, gw, 10, t);
    }

    _rgbOf(hex) {
        const h = hex.replace('#', '');
        const v = h.length === 3 ? h.split('').map(c => c + c).join('') : h.slice(0, 6);
        const n = parseInt(v, 16);
        return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
    }

    _drawLegend(ctx, scale, x, y, w, barH, t) {
        const stops = scale.legendStops(24);
        const grad = ctx.createLinearGradient(x, 0, x + w, 0);
        for (const s of stops) grad.addColorStop(s.t, s.color);
        ctx.fillStyle = grad;
        ctx.fillRect(x, y, w, barH);
        ctx.strokeStyle = t['--cl-border'];
        ctx.strokeRect(x + 0.5, y + 0.5, w - 1, barH - 1);
        ctx.fillStyle = t['--cl-text-secondary'];
        ctx.font = this.font(10);
        const unit = this.options.unit ? ` ${this.options.unit}` : '';
        ctx.textBaseline = 'top';
        ctx.textAlign = 'left';
        ctx.fillText(this.fmt(stops[0].value) + unit, x, y + barH + 2);
        ctx.textAlign = 'right';
        ctx.fillText(this.fmt(stops[stops.length - 1].value) + unit, x + w, y + barH + 2);
    }

    getTooltip(d) {
        const u = this.options.unit || '';
        return [
            { label: '', value: `${d.y} × ${d.x}` },
            { label: '值', value: `${this.fmt(d.value)}${u ? ' ' + u : ''}` }
        ];
    }
}

export default HeatmapChart;
