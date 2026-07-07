/**
 * Sparkline - 迷你趨勢圖(取代 react-sparklines)
 *
 * 純 SVG,適合放進 DataTable 儲存格、StatCard、儀表板摘要列。
 * 無座標軸、無圖例;要完整圖表請用 LineChart / BarChart。
 *
 * @example
 * const spark = new Sparkline({ data: [5, 3, 9, 6, 12, 8], type: 'line' });
 * spark.mount(cell);
 * spark.setData([1, 4, 2, 8]);   // 更新資料
 */

const SVG_NS = 'http://www.w3.org/2000/svg';

export class Sparkline {
    /**
     * @param {Object} options
     * @param {number[]} options.data - 數列
     * @param {string} options.type - 'line' | 'bar'(預設 line)
     * @param {number} options.width - 寬 px(預設 120)
     * @param {number} options.height - 高 px(預設 32)
     * @param {string} options.color - 線/柱顏色(預設 var(--cl-primary))
     * @param {boolean} options.fill - line 是否鋪面積(預設 true)
     * @param {boolean} options.showLast - 是否標記最後一點(預設 true)
     * @param {number} [options.min] - 固定最小值(預設取資料 min)
     * @param {number} [options.max] - 固定最大值(預設取資料 max)
     */
    constructor(options = {}) {
        this.options = {
            data: [],
            type: 'line',
            width: 120,
            height: 32,
            color: 'var(--cl-primary)',
            fill: true,
            showLast: true,
            min: undefined,
            max: undefined,
            ...options
        };

        this.element = document.createElement('span');
        this.element.className = 'cl-sparkline';
        this.element.style.cssText = `
            display: inline-block;
            line-height: 0;
            vertical-align: middle;
        `;
        this._render();
    }

    _scale() {
        const { data } = this.options;
        const min = this.options.min ?? Math.min(...data);
        const max = this.options.max ?? Math.max(...data);
        const span = max - min || 1;
        return { min, span };
    }

    _render() {
        const { data, type, width, height, color, fill, showLast } = this.options;
        this.element.textContent = '';

        const svg = document.createElementNS(SVG_NS, 'svg');
        svg.setAttribute('width', String(width));
        svg.setAttribute('height', String(height));
        svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        svg.setAttribute('aria-hidden', 'true');
        this.element.appendChild(svg);

        if (!Array.isArray(data) || data.length === 0) return;

        const pad = 2;
        const w = width - pad * 2;
        const h = height - pad * 2;
        const { min, span } = this._scale();
        const y = (v) => pad + h - ((v - min) / span) * h;

        if (type === 'bar') {
            const gap = 1;
            const barW = Math.max(1, w / data.length - gap);
            data.forEach((v, i) => {
                const rect = document.createElementNS(SVG_NS, 'rect');
                const x = pad + (w / data.length) * i + gap / 2;
                const top = y(Math.max(v, min));
                rect.setAttribute('x', x.toFixed(1));
                rect.setAttribute('y', top.toFixed(1));
                rect.setAttribute('width', barW.toFixed(1));
                rect.setAttribute('height', Math.max(1, pad + h - top).toFixed(1));
                rect.setAttribute('fill', color);
                rect.setAttribute('rx', '1');
                svg.appendChild(rect);
            });
            return;
        }

        // line
        const step = data.length > 1 ? w / (data.length - 1) : 0;
        const pts = data.map((v, i) => `${(pad + step * i).toFixed(1)},${y(v).toFixed(1)}`);

        if (fill) {
            const area = document.createElementNS(SVG_NS, 'polygon');
            area.setAttribute('points',
                `${pad},${pad + h} ${pts.join(' ')} ${pad + step * (data.length - 1)},${pad + h}`);
            area.setAttribute('fill', color);
            area.setAttribute('opacity', '0.15');
            svg.appendChild(area);
        }

        const line = document.createElementNS(SVG_NS, 'polyline');
        line.setAttribute('points', pts.join(' '));
        line.setAttribute('fill', 'none');
        line.setAttribute('stroke', color);
        line.setAttribute('stroke-width', '1.5');
        line.setAttribute('stroke-linejoin', 'round');
        line.setAttribute('stroke-linecap', 'round');
        svg.appendChild(line);

        if (showLast) {
            const dot = document.createElementNS(SVG_NS, 'circle');
            dot.setAttribute('cx', (pad + step * (data.length - 1)).toFixed(1));
            dot.setAttribute('cy', y(data[data.length - 1]).toFixed(1));
            dot.setAttribute('r', '2');
            dot.setAttribute('fill', color);
            svg.appendChild(dot);
        }
    }

    setData(data) {
        this.options.data = Array.isArray(data) ? data : [];
        this._render();
    }

    setColor(color) {
        this.options.color = color;
        this._render();
    }

    show() {
        this.element.style.display = 'inline-block';
    }

    hide() {
        this.element.style.display = 'none';
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        if (this.element?.parentNode) this.element.remove();
    }
}

export default Sparkline;
