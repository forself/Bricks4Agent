/**
 * LineChart — 折線圖(CanvasChart 版;SVG 禁用政策下的重寫,API 向後相容)。
 * 資料形狀:{ labels:['1月','2月'], series:[{ name:'量', data:[10,20] }, ...] }
 * 支援:多系列、資料點 hover、legend、unit、null 斷線。
 */
import { CanvasChart } from './CanvasChart.js';
import { categoricalColor } from '../utils/color-scale.js';

const px = (v, d) => typeof v === 'number' ? v + 'px' : (v || d);

export class LineChart extends CanvasChart {
    constructor(options = {}) {
        super({
            ...options,
            width: px(options.width, '100%'),
            height: px(options.height, '260px'),
            data: options.data || { labels: [], series: [] },
            unit: options.unit || '',
            colors: options.colors || null,
            showPoints: options.showPoints !== false,
            padding: options.padding || { top: 14, right: 14, bottom: 30, left: 44 }
        });
    }

    _color(si) {
        const c = this.options.colors;
        return (c && c[si]) || categoricalColor(si);
    }

    draw(ctx, w, h) {
        const o = this.options;
        const t = this.tokens(['--cl-text-secondary', '--cl-border-light', '--cl-text-dim', '--cl-bg']);
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
        const all = [];
        for (const s of series) for (const v of s.data) if (v != null && !Number.isNaN(Number(v))) all.push(Number(v));
        const yT = this.niceTicks(Math.min(0, ...all), Math.max(1, ...all), 5);
        const Y = (v) => gy + gh - ((v - yT.lo) / (yT.hi - yT.lo || 1)) * gh;
        const X = (li) => labels.length === 1 ? gx + gw / 2 : gx + (li / (labels.length - 1)) * gw;

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
        // 折線
        for (let si = 0; si < series.length; si++) {
            const color = this._color(si);
            ctx.strokeStyle = color;
            ctx.lineWidth = 2;
            ctx.lineJoin = 'round';
            ctx.beginPath();
            let pen = false;
            for (let li = 0; li < labels.length; li++) {
                const raw = series[si].data[li];
                if (raw == null || Number.isNaN(Number(raw))) { pen = false; continue; }   // null 斷線
                const x = X(li), y = Y(Number(raw));
                if (pen) ctx.lineTo(x, y); else { ctx.moveTo(x, y); pen = true; }
            }
            ctx.stroke();
            // 資料點
            if (o.showPoints) {
                for (let li = 0; li < labels.length; li++) {
                    const raw = series[si].data[li];
                    if (raw == null || Number.isNaN(Number(raw))) continue;
                    const x = X(li), y = Y(Number(raw));
                    ctx.fillStyle = color;
                    ctx.beginPath(); ctx.arc(x, y, 3, 0, Math.PI * 2); ctx.fill();
                    ctx.strokeStyle = t['--cl-bg'];
                    ctx.lineWidth = 1;
                    ctx.stroke();
                    this.addRegion({ shape: 'circle', cx: x, cy: y, r: 7, data: { label: labels[li], series: series[si].name, value: Number(raw) } });
                }
            }
        }
        // x 標籤(抽稀)
        ctx.fillStyle = t['--cl-text-secondary'];
        ctx.textAlign = 'center'; ctx.textBaseline = 'top';
        const thin = Math.max(1, Math.ceil((labels.length * 52) / gw));
        for (let li = 0; li < labels.length; li += thin) {
            ctx.fillText(this.ellipsis(ctx, String(labels[li]), Math.max(40, gw / labels.length * thin)), X(li), gy + gh + 6);
        }
        // legend
        if (multi) {
            ctx.font = this.font(10);
            ctx.textBaseline = 'middle'; ctx.textAlign = 'left';
            let cx = gx;
            for (let si = 0; si < series.length; si++) {
                const label = String(series[si].name ?? '');
                const lw = 18 + ctx.measureText(label).width + 14;
                if (cx + lw > gx + gw) break;
                ctx.strokeStyle = this._color(si);
                ctx.lineWidth = 2;
                ctx.beginPath(); ctx.moveTo(cx, p.top + 4); ctx.lineTo(cx + 12, p.top + 4); ctx.stroke();
                ctx.fillStyle = t['--cl-text-secondary'];
                ctx.fillText(label, cx + 16, p.top + 4);
                cx += lw;
            }
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

export default LineChart;
