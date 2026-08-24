/**
 * BarChart — 長條圖(CanvasChart 版;SVG 禁用政策下的重寫,API 向後相容)。
 * 資料形狀:{ labels:['A','B'], series:[{ name:'量', data:[10,20] }, ...] }
 * 支援:單/多系列(並列 grouped)、stacked 堆疊、legend、tooltip、unit、負值。
 */
import { CanvasChart } from './CanvasChart.js';
import { categoricalColor } from '../utils/color-scale.js';

const px = (v, d) => typeof v === 'number' ? v + 'px' : (v || d);

export class BarChart extends CanvasChart {
    constructor(options = {}) {
        super({
            ...options,
            width: px(options.width, '100%'),
            height: px(options.height, '260px'),
            data: options.data || { labels: [], series: [] },
            stacked: options.stacked || false,
            barWidth: options.barWidth || 0.6,     // 群寬占比 0~1
            unit: options.unit || '',
            colors: options.colors || null,
            padding: options.padding || { top: 14, right: 12, bottom: 30, left: 44 }
        });
    }

    _color(si) {
        const c = this.options.colors;
        return (c && c[si]) || categoricalColor(si);
    }

    draw(ctx, w, h) {
        const o = this.options;
        const t = this.tokens(['--cl-text', '--cl-text-secondary', '--cl-border-light', '--cl-text-dim']);
        const labels = o.data.labels || [];
        const series = (o.data.series || []).filter(s => Array.isArray(s.data));
        const p = o.padding;
        const multi = series.length > 1;
        const legendH = multi ? 18 : 0;
        const gx = p.left, gy = p.top + legendH;
        const gw = Math.max(10, w - p.left - p.right);
        const gh = Math.max(10, h - gy - p.bottom);

        if (!labels.length || !series.length) {
            ctx.font = this.font(13); ctx.fillStyle = t['--cl-text-dim'];
            ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }
        // 值域(堆疊=逐 label 正負各自加總)
        let maxV = 0, minV = 0;
        for (let li = 0; li < labels.length; li++) {
            if (o.stacked) {
                let pos = 0, neg = 0;
                for (const s of series) { const v = Number(s.data[li]) || 0; if (v >= 0) pos += v; else neg += v; }
                maxV = Math.max(maxV, pos); minV = Math.min(minV, neg);
            } else {
                for (const s of series) { const v = Number(s.data[li]) || 0; maxV = Math.max(maxV, v); minV = Math.min(minV, v); }
            }
        }
        const yT = this.niceTicks(Math.min(0, minV), Math.max(1, maxV), 5);
        const Y = (v) => gy + gh - ((v - yT.lo) / (yT.hi - yT.lo || 1)) * gh;

        // 網格 + y 刻度
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
        // 長條
        const slot = gw / labels.length;
        const groupW = slot * o.barWidth;
        const y0 = Y(0);
        for (let li = 0; li < labels.length; li++) {
            const cx0 = gx + li * slot + (slot - groupW) / 2;
            if (o.stacked) {
                let accPos = 0, accNeg = 0;
                for (let si = 0; si < series.length; si++) {
                    const v = Number(series[si].data[li]) || 0;
                    if (!v) continue;
                    const from = v >= 0 ? accPos : accNeg;
                    const to = from + v;
                    const yA = Y(from), yB = Y(to);
                    const by = Math.min(yA, yB), bh2 = Math.max(1, Math.abs(yA - yB));
                    ctx.fillStyle = this._color(si);
                    ctx.fillRect(cx0, by, groupW, bh2);
                    this.addRegion({ shape: 'rect', x: cx0, y: by, w: groupW, h: bh2, data: { label: labels[li], series: series[si].name, value: v } });
                    if (v >= 0) accPos = to; else accNeg = to;
                }
            } else {
                const bw = groupW / series.length;
                for (let si = 0; si < series.length; si++) {
                    const v = Number(series[si].data[li]) || 0;
                    const yV = Y(v);
                    const by = Math.min(y0, yV), bh2 = Math.max(1, Math.abs(y0 - yV));
                    ctx.fillStyle = this._color(si);
                    ctx.fillRect(cx0 + si * bw, by, Math.max(1, bw - 1), bh2);
                    this.addRegion({ shape: 'rect', x: cx0 + si * bw, y: by, w: Math.max(1, bw - 1), h: bh2, data: { label: labels[li], series: series[si].name, value: v } });
                }
            }
        }
        // x 標籤(抽稀)
        ctx.fillStyle = t['--cl-text-secondary'];
        ctx.textAlign = 'center'; ctx.textBaseline = 'top';
        const thin = Math.max(1, Math.ceil((labels.length * 52) / gw));
        for (let li = 0; li < labels.length; li += thin) {
            ctx.fillText(this.ellipsis(ctx, String(labels[li]), slot * thin - 4), gx + li * slot + slot / 2, gy + gh + 6);
        }
        // legend(多系列)
        if (multi) this._drawLegend(ctx, series.map(s => s.name), gx, p.top + 4, gw, t);
    }

    _drawLegend(ctx, names, x, y, maxW, t) {
        ctx.font = this.font(10);
        ctx.textBaseline = 'middle'; ctx.textAlign = 'left';
        let cx = x;
        for (let i = 0; i < names.length; i++) {
            const label = String(names[i] ?? '');
            const w = 14 + ctx.measureText(label).width + 14;
            if (cx + w > x + maxW) break;
            ctx.fillStyle = this._color(i);
            ctx.fillRect(cx, y - 4, 9, 9);
            ctx.fillStyle = t['--cl-text-secondary'];
            ctx.fillText(label, cx + 13, y);
            cx += w;
        }
    }

    getTooltip(d) {
        const u = this.options.unit ? ' ' + this.options.unit : '';
        const rows = [{ label: '', value: d.label }];
        if ((this.options.data.series || []).length > 1) rows.push({ label: '系列', value: d.series });
        rows.push({ label: '值', value: this.fmt(d.value) + u });
        return rows;
    }

    /** 更新資料並重繪(舊 API 相容)。 */
    setData(data) { this.options.data = data; this.render(); }
}

export default BarChart;
