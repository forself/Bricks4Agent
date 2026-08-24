/**
 * PieChart — 圓餅/甜甜圈(CanvasChart 版;SVG 禁用政策下的重寫,API 向後相容)。
 * 資料形狀:[{ name:'甲', value:60 }, { name:'乙', value:40 }]
 * 支援:donut、百分比標註(扇區夠大才標)、legend chips、tooltip。
 */
import { CanvasChart } from './CanvasChart.js';
import { categoricalColor } from '../utils/color-scale.js';

const px = (v, d) => typeof v === 'number' ? v + 'px' : (v || d);

export class PieChart extends CanvasChart {
    constructor(options = {}) {
        super({
            ...options,
            width: px(options.width, '100%'),
            height: px(options.height, '240px'),
            data: options.data || [],
            donut: options.donut || false,
            unit: options.unit || '',
            colors: options.colors || null,
            padding: options.padding || { top: 8, right: 8, bottom: 8, left: 8 }
        });
    }

    _color(i) {
        const c = this.options.colors;
        return (c && c[i]) || categoricalColor(i);
    }

    draw(ctx, w, h) {
        const o = this.options;
        const t = this.tokens(['--cl-text-secondary', '--cl-text-dim', '--cl-bg', '--cl-text']);
        const data = (o.data || []).filter(d => d && Number(d.value) > 0);
        const total = data.reduce((s, d) => s + Number(d.value), 0);

        if (!data.length || total <= 0) {
            ctx.font = this.font(13); ctx.fillStyle = t['--cl-text-dim'];
            ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }
        // 圓在左、legend 在右(空間夠時)
        const legendW = w >= 220 ? Math.min(120, w * 0.38) : 0;
        const cw = w - legendW;
        const R = Math.max(20, Math.min(cw, h) / 2 - Math.max(o.padding.top, 8));
        const cx = cw / 2, cy = h / 2;
        const r0 = o.donut ? R * 0.55 : 0;

        let angle = -Math.PI / 2;
        data.forEach((d, i) => {
            const frac = Number(d.value) / total;
            const a2 = angle + frac * Math.PI * 2;
            const path = new Path2D();
            path.moveTo(cx + Math.cos(angle) * r0, cy + Math.sin(angle) * r0);
            path.arc(cx, cy, R, angle, a2);
            path.lineTo(cx + Math.cos(a2) * r0, cy + Math.sin(a2) * r0);
            if (r0) path.arc(cx, cy, r0, a2, angle, true);
            path.closePath();
            ctx.fillStyle = this._color(i);
            ctx.fill(path);
            ctx.strokeStyle = t['--cl-bg'];
            ctx.lineWidth = 1.5;
            ctx.stroke(path);
            this.addRegion({
                shape: 'path', path,
                bounds: { x: cx - R, y: cy - R, w: R * 2, h: R * 2 },
                data: { name: d.name, value: Number(d.value), pct: frac * 100 }
            });
            // 百分比標註(≥6% 才標;亮度自適應)
            if (frac >= 0.06) {
                const mid = (angle + a2) / 2;
                const lr = r0 ? (r0 + R) / 2 : R * 0.62;
                const [rr, gg, bb] = this._rgbOf(this._color(i));
                ctx.fillStyle = (0.299 * rr + 0.587 * gg + 0.114 * bb) > 150 ? '#00000099' : '#ffffffcc';
                ctx.font = this.font(Math.max(9, Math.min(12, R * 0.14)), 600);
                ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
                ctx.fillText(Math.round(frac * 100) + '%', cx + Math.cos(mid) * lr, cy + Math.sin(mid) * lr);
            }
            angle = a2;
        });
        // legend
        if (legendW) {
            ctx.font = this.font(10);
            ctx.textAlign = 'left'; ctx.textBaseline = 'middle';
            const lh = 16;
            const maxRows = Math.floor(h / lh);
            let ly = Math.max(10, cy - (Math.min(data.length, maxRows) * lh) / 2);
            for (let i = 0; i < data.length && i < maxRows; i++) {
                ctx.fillStyle = this._color(i);
                ctx.fillRect(cw + 4, ly - 4, 9, 9);
                ctx.fillStyle = t['--cl-text-secondary'];
                ctx.fillText(this.ellipsis(ctx, String(data[i].name), legendW - 22), cw + 17, ly);
                ly += lh;
            }
        }
    }

    _rgbOf(hex) {
        const s = hex.replace('#', '');
        const v = s.length === 3 ? s.split('').map(c => c + c).join('') : s.slice(0, 6);
        const n = parseInt(v, 16);
        return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
    }

    getTooltip(d) {
        const u = this.options.unit ? ' ' + this.options.unit : '';
        return [
            { label: '', value: d.name },
            { label: '值', value: this.fmt(d.value) + u },
            { label: '占比', value: d.pct.toFixed(1) + '%' }
        ];
    }

    /** 更新資料並重繪(舊 API 相容)。 */
    setData(data) { this.options.data = data; this.render(); }
}

export default PieChart;
