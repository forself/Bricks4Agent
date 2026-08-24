/**
 * Sparkline - 迷你趨勢圖(取代 react-sparklines)
 *
 * 純 Canvas,適合放進 DataTable 儲存格、StatCard、儀表板摘要列。
 * 無座標軸、無圖例;要完整圖表請用 LineChart / BarChart。
 *
 * @example
 * const spark = new Sparkline({ data: [5, 3, 9, 6, 12, 8], type: 'line' });
 * spark.mount(cell);
 * spark.setData([1, 4, 2, 8]);   // 更新資料
 */
import { CanvasChart } from './CanvasChart.js';
import { FALLBACK_PAINT } from '../utils/theme-bus.js';

const px = (v, d) => typeof v === 'number' ? v + 'px' : (v || d);

export class Sparkline extends CanvasChart {
    /**
     * @param {Object} options
     * @param {number[]} options.data - 數列
     * @param {string} options.type - 'line' | 'bar'(預設 line)
     * @param {number} options.width - 寬 px(預設 120)
     * @param {number} options.height - 高 px(預設 32)
     * @param {string} options.color - 線/柱顏色 token(預設 '--cl-primary')
     * @param {boolean} options.fill - line 是否鋪面積(預設 true)
     * @param {boolean} options.showLast - 是否標記最後一點(預設 true)
     * @param {number} [options.min] - 固定最小值(預設取資料 min)
     * @param {number} [options.max] - 固定最大值(預設取資料 max)
     */
    constructor(options = {}) {
        super({
            container: options.container || null,
            width: px(options.width, '120px'),
            height: px(options.height, '32px'),
            ariaLabel: options.ariaLabel || 'sparkline',
            // 不用 CanvasChart 的預設 padding;自己管
            padding: { top: 0, right: 0, bottom: 0, left: 0 },
            data: Array.isArray(options.data) ? options.data : [],
            type: options.type || 'line',
            color: options.color || '--cl-primary',
            fill: options.fill !== undefined ? options.fill : true,
            showLast: options.showLast !== undefined ? options.showLast : true,
            min: options.min,
            max: options.max
        });
        // 讓容器本身輕量行內呈現(不改 CanvasChart 的 element div)
        this.element.style.cssText =
            'display: inline-block; line-height: 0; vertical-align: middle;' +
            ' width: ' + px(options.width, '120px') + '; height: ' + px(options.height, '32px') + ';';
    }

    _resolveColor() {
        const raw = this.options.color || '--cl-primary';
        // 若傳入的是 CSS 自定義屬性名稱(以 -- 開頭),透過 tokens() 解析
        if (raw.startsWith('--')) {
            return this.tokens([raw])[raw] || FALLBACK_PAINT;
        }
        // 否則當作直接色碼(hex / rgb / etc.)
        return raw;
    }

    _scale() {
        const data = this.options.data;
        const minOpt = this.options.min;
        const maxOpt = this.options.max;
        const min = minOpt !== undefined ? minOpt : Math.min(...data);
        const max = maxOpt !== undefined ? maxOpt : Math.max(...data);
        const span = max - min || 1;
        return { min, span };
    }

    draw(ctx, w, h) {
        const { data, type, fill, showLast } = this.options;
        const color = this._resolveColor();

        if (!Array.isArray(data) || data.length === 0) return;

        const pad = 2;
        const iw = w - pad * 2;
        const ih = h - pad * 2;
        const { min, span } = this._scale();
        const yPx = (v) => pad + ih - ((v - min) / span) * ih;

        if (type === 'bar') {
            const gap = 1;
            const barW = Math.max(1, iw / data.length - gap);
            data.forEach((v, i) => {
                const x = pad + (iw / data.length) * i + gap / 2;
                const top = yPx(Math.max(v, min));
                const bh = Math.max(1, pad + ih - top);
                ctx.fillStyle = color;
                ctx.beginPath();
                ctx.roundRect
                    ? ctx.roundRect(x, top, barW, bh, 1)
                    : ctx.rect(x, top, barW, bh);
                ctx.fill();
            });
            return;
        }

        // line
        const step = data.length > 1 ? iw / (data.length - 1) : 0;
        const pts = data.map((v, i) => [pad + step * i, yPx(v)]);

        if (fill) {
            ctx.beginPath();
            ctx.moveTo(pad, pad + ih);
            pts.forEach(([px2, py]) => ctx.lineTo(px2, py));
            ctx.lineTo(pad + step * (data.length - 1), pad + ih);
            ctx.closePath();
            ctx.globalAlpha = 0.15;
            ctx.fillStyle = color;
            ctx.fill();
            ctx.globalAlpha = 1;
        }

        ctx.beginPath();
        pts.forEach(([px2, py], i) => i === 0 ? ctx.moveTo(px2, py) : ctx.lineTo(px2, py));
        ctx.strokeStyle = color;
        ctx.lineWidth = 1.5;
        ctx.lineJoin = 'round';
        ctx.lineCap = 'round';
        ctx.stroke();

        if (showLast && pts.length > 0) {
            const [lx, ly] = pts[pts.length - 1];
            ctx.beginPath();
            ctx.arc(lx, ly, 2, 0, Math.PI * 2);
            ctx.fillStyle = color;
            ctx.fill();
        }
    }

    /** 更新資料並重繪。 */
    setData(data) {
        this.options.data = Array.isArray(data) ? data : [];
        this.render();
    }

    /** 更新線條顏色並重繪。 */
    setColor(color) {
        this.options.color = color;
        this.render();
    }

    show() { this.element.style.display = 'inline-block'; }
    hide() { this.element.style.display = 'none'; }
}

export default Sparkline;
