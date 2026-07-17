/**
 * RoseChart — 南丁格爾玫瑰圖(CanvasChart 版;SVG 禁用政策下的重寫,API 向後相容)。
 * 資料形狀:{ labels: ['東','南','西'], series: [{ name:'風', data:[20,35,15] }, ...] }
 * 支援:多系列堆疊環(radius stack)、Path2D 扇形區、legend、tooltip。
 */
import { CanvasChart } from './CanvasChart.js';
import { categoricalColor } from '../utils/color-scale.js';
import { FALLBACK_PAINT } from '../utils/theme-bus.js';

const px = (v, d) => typeof v === 'number' ? v + 'px' : (v || d);

export class RoseChart extends CanvasChart {
    constructor(options = {}) {
        super({
            ...options,
            width: px(options.width, '100%'),
            height: px(options.height, '280px'),
            data: options.data || { labels: [], series: [] },
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
        const t = this.tokens(['--cl-text', '--cl-text-secondary', '--cl-text-dim', '--cl-bg']);
        const labels = (o.data && o.data.labels) || [];
        const series = (o.data && o.data.series) || [];

        if (!labels.length || !series.length) {
            ctx.font = this.font(13);
            ctx.fillStyle = t['--cl-text-dim'];
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }

        // legend 高度(多系列時)
        const multi = series.length > 1;
        const legendH = multi ? 18 : 0;

        const cx = w / 2;
        const cy = (h + legendH) / 2;
        const maxRadius = Math.min(cx, cy - legendH) * 0.88;

        // 每個 label 的總量(用於計算各分類半徑上限)
        const totalValues = new Array(labels.length).fill(0);
        series.forEach(s => {
            s.data.forEach((val, i) => {
                totalValues[i] += (Number(val) || 0);
            });
        });
        const maxTotalVal = Math.max(...totalValues, 1);

        const angleStep = (2 * Math.PI) / labels.length;
        const strokeColor = t['--cl-bg'] || FALLBACK_PAINT;

        // 繪製每個扇形
        labels.forEach((label, i) => {
            let currentRadius = 0;
            const startAngle = i * angleStep - Math.PI / 2;
            const endAngle = (i + 1) * angleStep - Math.PI / 2;

            series.forEach((s, sIndex) => {
                const val = Number(s.data[i]) || 0;
                if (val === 0) return;

                const radiusIncrement = (val / maxTotalVal) * maxRadius;
                const innerRadius = currentRadius;
                const outerRadius = currentRadius + radiusIncrement;

                const path = new Path2D();
                if (innerRadius === 0) {
                    // 純扇形(從圓心出發)
                    path.moveTo(cx, cy);
                    path.arc(cx, cy, outerRadius, startAngle, endAngle);
                    path.closePath();
                } else {
                    // 環形扇形(annular sector)
                    path.arc(cx, cy, outerRadius, startAngle, endAngle);
                    path.arc(cx, cy, innerRadius, endAngle, startAngle, true);
                    path.closePath();
                }

                ctx.globalAlpha = 0.9;
                ctx.fillStyle = this._color(sIndex);
                ctx.fill(path);
                ctx.globalAlpha = 1;
                ctx.strokeStyle = strokeColor;
                ctx.lineWidth = 1;
                ctx.stroke(path);

                // 命中區域:用扇形外包矩形 + Path2D 精確命中
                this.addRegion({
                    shape: 'path',
                    path,
                    bounds: {
                        x: cx - outerRadius,
                        y: cy - outerRadius,
                        w: outerRadius * 2,
                        h: outerRadius * 2
                    },
                    data: { label, seriesName: s.name, value: val, sIndex }
                });

                currentRadius = outerRadius;
            });
        });

        // legend(多系列)
        if (multi) {
            ctx.font = this.font(10);
            ctx.textBaseline = 'middle';
            ctx.textAlign = 'left';
            const lh = 16;
            const totalLegendW = series.reduce((acc, s) => acc + 14 + ctx.measureText(String(s.name ?? '')).width + 14, 0);
            let lx = Math.max(8, (w - totalLegendW) / 2);
            const ly = legendH / 2 + 2;
            for (let i = 0; i < series.length; i++) {
                const label = String(series[i].name ?? '');
                ctx.fillStyle = this._color(i);
                ctx.fillRect(lx, ly - 4, 9, 9);
                ctx.fillStyle = t['--cl-text-secondary'];
                ctx.fillText(label, lx + 13, ly);
                lx += 14 + ctx.measureText(label).width + 14;
            }
        }
    }

    getTooltip(d) {
        return [
            { label: '', value: d.label },
            { label: '系列', value: d.seriesName },
            { label: '值', value: this.fmt(d.value) }
        ];
    }

    /** 更新資料並重繪(舊 API 相容)。 */
    setData(data) {
        this.options.data = data;
        this.render();
    }
}

export default RoseChart;
