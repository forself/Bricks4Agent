/**
 * ScatterChart — 散布圖/氣泡圖(CanvasChart 子類)。
 * 通道:x、y(連續)+ size(氣泡,選配)+ color(選配:字串=分類色、數值=連續色階)。
 * 2D=x,y|3D=+size 或 +color|4D=x,y,size,color。
 *
 * @example
 * new ScatterChart({
 *     container, title: '年齡×金額',
 *     points: [{ x: 34, y: 120000, s: 3, c: '詐欺', label: '王小明' }, ...],
 *     xLabel: '年齡', yLabel: '金額', xUnit: '歲', yUnit: '元',
 *     sizeLabel: '前科數', colorLabel: '案類'
 * }).mount(host);
 */
import { CanvasChart } from './CanvasChart.js';
import { sequentialScale, categoricalColor } from '../utils/color-scale.js';

export class ScatterChart extends CanvasChart {
    constructor(options = {}) {
        super({
            points: [],
            xLabel: '', yLabel: '', xUnit: '', yUnit: '',
            sizeLabel: '', colorLabel: '',
            hue: 'blue',                      // 連續色階色相
            rRange: [3.5, 16],                // 氣泡半徑範圍(px)
            opacity: 0.85,
            legend: true,
            padding: { top: 10, right: 16, bottom: 44, left: 56 },
            ...options
        });
    }

    _prep() {
        const pts = this.options.points.filter(p => p && Number.isFinite(Number(p.x)) && Number.isFinite(Number(p.y)));
        let colorMode = 'none', cats = [], cScale = null;
        const cVals = pts.map(p => p.c).filter(v => v != null);
        if (cVals.length) {
            if (cVals.every(v => typeof v === 'number' && Number.isFinite(v))) {
                colorMode = 'seq';
                cScale = sequentialScale(this.options.hue, [Math.min(...cVals), Math.max(...cVals)]);
            } else {
                colorMode = 'cat';
                for (const p of pts) {
                    const k = p.c == null ? '(空)' : String(p.c);
                    if (!cats.includes(k)) cats.push(k);      // 首次出現順序(與聚合引擎一致)
                }
            }
        }
        let rOf = () => 5;
        const sVals = pts.map(p => Number(p.s)).filter(Number.isFinite);
        if (sVals.length) {
            const [r0, r1] = this.options.rRange;
            const smin = Math.min(...sVals), smax = Math.max(...sVals);
            const span = smax - smin || 1;
            rOf = (p) => {
                const s = Number(p.s);
                if (!Number.isFinite(s)) return r0;
                return r0 + Math.sqrt((s - smin) / span) * (r1 - r0);   // 面積感知:開根號
            };
        }
        return { pts, colorMode, cats, cScale, rOf };
    }

    draw(ctx, w, h) {
        const o = this.options;
        const t = this.tokens(['--cl-text', '--cl-text-secondary', '--cl-border', '--cl-border-light', '--cl-text-dim', '--cl-primary']);
        const { pts, colorMode, cats, cScale, rOf } = this._prep();
        const p = o.padding;
        const legendH = (o.legend && colorMode !== 'none') ? 24 : 0;
        const gx = p.left, gy = p.top;
        const gw = Math.max(10, w - p.left - p.right);
        const gh = Math.max(10, h - p.top - p.bottom - legendH);

        if (!pts.length) {
            ctx.font = this.font(13); ctx.fillStyle = t['--cl-text-dim'];
            ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }
        const xs = pts.map(pt => Number(pt.x)), ys = pts.map(pt => Number(pt.y));
        const xT = this.niceTicks(Math.min(...xs), Math.max(...xs), 6);
        const yT = this.niceTicks(Math.min(...ys), Math.max(...ys), 5);
        const X = (v) => gx + ((v - xT.lo) / (xT.hi - xT.lo || 1)) * gw;
        const Y = (v) => gy + gh - ((v - yT.lo) / (yT.hi - yT.lo || 1)) * gh;

        // 網格與軸
        ctx.strokeStyle = t['--cl-border-light'];
        ctx.fillStyle = t['--cl-text-secondary'];
        ctx.font = this.font(10);
        ctx.lineWidth = 1;
        ctx.textAlign = 'right'; ctx.textBaseline = 'middle';
        for (const v of yT.ticks) {
            const y = Math.round(Y(v)) + 0.5;
            ctx.beginPath(); ctx.moveTo(gx, y); ctx.lineTo(gx + gw, y); ctx.stroke();
            ctx.fillText(this.fmt(v), gx - 6, y);
        }
        ctx.textAlign = 'center'; ctx.textBaseline = 'top';
        for (const v of xT.ticks) {
            const x = Math.round(X(v)) + 0.5;
            ctx.beginPath(); ctx.moveTo(x, gy + gh); ctx.lineTo(x, gy + gh + 4); ctx.stroke();
            ctx.fillText(this.fmt(v), x, gy + gh + 6);
        }
        // 軸標題
        ctx.fillStyle = t['--cl-text'];
        ctx.font = this.font(11, 600);
        if (o.xLabel) ctx.fillText(`${o.xLabel}${o.xUnit ? `(${o.xUnit})` : ''}`, gx + gw / 2, gy + gh + 22);
        if (o.yLabel) {
            ctx.save();
            ctx.translate(14, gy + gh / 2);
            ctx.rotate(-Math.PI / 2);
            ctx.textBaseline = 'middle';
            ctx.fillText(`${o.yLabel}${o.yUnit ? `(${o.yUnit})` : ''}`, 0, 0);
            ctx.restore();
        }
        // 點
        ctx.globalAlpha = o.opacity;
        pts.forEach((pt, i) => {
            const cx = X(Number(pt.x)), cy = Y(Number(pt.y)), r = rOf(pt);
            let color = t['--cl-primary'];
            if (colorMode === 'cat') color = categoricalColor(cats.indexOf(pt.c == null ? '(空)' : String(pt.c)));
            else if (colorMode === 'seq' && pt.c != null) color = cScale(Number(pt.c));
            ctx.fillStyle = color;
            ctx.beginPath(); ctx.arc(cx, cy, r, 0, Math.PI * 2); ctx.fill();
            this.addRegion({ shape: 'circle', cx, cy, r: Math.max(r, 6), data: { ...pt, _color: color, _i: i } });
        });
        ctx.globalAlpha = 1;

        // 圖例
        if (legendH) {
            const ly = gy + gh + 30;
            if (colorMode === 'cat') this._drawCatLegend(ctx, cats, gx, ly, gw, t);
            else this._drawSeqLegend(ctx, cScale, gx, ly + 2, Math.min(gw, 220), 8, t);
        }
    }

    _drawCatLegend(ctx, cats, x, y, maxW, t) {
        ctx.font = this.font(10);
        ctx.textBaseline = 'middle'; ctx.textAlign = 'left';
        let cx = x;
        for (let i = 0; i < cats.length; i++) {
            const label = cats[i];
            const w = 12 + ctx.measureText(label).width + 14;
            if (cx + w > x + maxW) break;                        // 超寬截斷(tooltip 仍有完整資訊)
            ctx.fillStyle = categoricalColor(i);
            ctx.beginPath(); ctx.arc(cx + 4, y + 4, 4, 0, Math.PI * 2); ctx.fill();
            ctx.fillStyle = t['--cl-text-secondary'];
            ctx.fillText(label, cx + 12, y + 4);
            cx += w;
        }
    }

    _drawSeqLegend(ctx, scale, x, y, w, barH, t) {
        const stops = scale.legendStops(16);
        const grad = ctx.createLinearGradient(x, 0, x + w, 0);
        for (const s of stops) grad.addColorStop(s.t, s.color);
        ctx.fillStyle = grad;
        ctx.fillRect(x, y, w, barH);
        ctx.strokeStyle = t['--cl-border'];
        ctx.strokeRect(x + 0.5, y + 0.5, w - 1, barH - 1);
        ctx.fillStyle = t['--cl-text-secondary'];
        ctx.font = this.font(9);
        ctx.textBaseline = 'middle';
        ctx.textAlign = 'left';
        ctx.fillText(this.fmt(stops[0].value), x + w + 6, y + barH / 2);
        ctx.textAlign = 'right';
        ctx.fillText(this.fmt(stops[stops.length - 1].value), x - 6, y + barH / 2);
    }

    getTooltip(d) {
        const o = this.options;
        const rows = [];
        if (d.label != null) rows.push({ label: '', value: String(d.label) });
        rows.push({ label: o.xLabel || 'x', value: `${this.fmt(Number(d.x))}${o.xUnit ? ' ' + o.xUnit : ''}` });
        rows.push({ label: o.yLabel || 'y', value: `${this.fmt(Number(d.y))}${o.yUnit ? ' ' + o.yUnit : ''}` });
        if (d.c != null) rows.push({ label: o.colorLabel || '分類', value: String(d.c) });
        if (d.s != null) rows.push({ label: o.sizeLabel || '大小', value: this.fmt(Number(d.s)) });
        return rows;
    }
}

export default ScatterChart;
