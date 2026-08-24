/**
 * FlameChart — 火焰圖/Icicle 倒焰圖(CanvasChart 版;SVG 禁用政策下的重寫,API 向後相容)。
 * 資料形狀:階層物件 { name, value, children: [...] }
 * Root 放底部(標準 Flame Graph:根最寬、子層往上收斂)。
 * 顏色:以層級為索引取 categoricalColor(熱色色相感),塊上文字亮度自適應。
 *
 * @example
 * new FlameChart({
 *     container: '#host',
 *     data: { name: 'Main', value: 100, children: [
 *         { name: 'Init', value: 20, children: [
 *             { name: 'Config Load', value: 10 },
 *             { name: 'DB Connect', value: 10 }
 *         ]}
 *     ]},
 *     title: 'Performance Trace'
 * });
 */
import { CanvasChart } from './CanvasChart.js';
import { categoricalColor } from '../utils/color-scale.js';
import { ModalPanel } from '../layout/Panel/index.js';
import { FALLBACK_PAINT } from '../utils/theme-bus.js';

/** 火焰圖專用熱色相序(橘→紅→紫,視覺對應「溫度/深度」) */
const FLAME_HUES = ['orange', 'deep-orange', 'red', 'pink', 'purple', 'indigo', 'blue', 'teal'];

function flameColor(level) {
    const hue = FLAME_HUES[level % FLAME_HUES.length];
    // 淺層亮(300)→深層深(700)循環
    const shades = [400, 500, 600, 700, 500, 400];
    return categoricalColor(
        FLAME_HUES.indexOf(hue),
        shades[level % shades.length]
    );
}

function hexToRgbArr(hex) {
    const h = (hex || FALLBACK_PAINT).replace('#', '');
    const v = h.length === 3 ? h.split('').map(c => c + c).join('') : h.slice(0, 6);
    const n = parseInt(v, 16) || 0;
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

export class FlameChart extends CanvasChart {
    constructor(options = {}) {
        super({
            padding: { top: 8, right: 8, bottom: 30, left: 8 },
            ...options,
            width: typeof options.width === 'number' ? options.width + 'px' : (options.width || '100%'),
            height: typeof options.height === 'number' ? options.height + 'px' : (options.height || '300px'),
        });
        // data: hierarchical object { name, value, children: [] }
        this.data = options.data || {};
        // 舊版語意:點擊火焰框內建開詳情面板。使用者自傳 onPointClick 則以其為準。
        if (typeof this.options.onPointClick !== 'function') {
            this.options.onPointClick = (d) => this._handleClick(d);
        }
    }

    /** 更新資料並重繪(舊 API 相容)。 */
    setData(data) { this.data = data; this.render(); }

    /** 展平樹狀資料為帶位置的節點陣列。 */
    _flatten(w) {
        const totalValue = this.data.value || 1;
        const nodes = [];

        const traverse = (node, level, x, nodeW) => {
            nodes.push({ ...node, level, x, width: nodeW });
            if (node.children && node.children.length) {
                let cx = x;
                for (const child of node.children) {
                    const cw = (child.value / totalValue) * w;
                    traverse(child, level + 1, cx, cw);
                    cx += cw;
                }
            }
        };

        traverse(this.data, 0, 0, w);
        return nodes;
    }

    draw(ctx, w, h) {
        const data = this.data;
        const tok = this.tokens(['--cl-text-dim', '--cl-text-secondary']);

        if (!data || !data.name) {
            ctx.font = this.font(13);
            ctx.fillStyle = tok['--cl-text-dim'];
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }

        const p = this.options.padding;
        const drawW = Math.max(10, w - p.left - p.right);
        const drawH = Math.max(10, h - p.top - p.bottom);

        const BAR_H = 24;
        const GAP = 2;
        const STEP = BAR_H + GAP;

        const nodes = this._flatten(drawW);
        const maxLevel = Math.max(...nodes.map(n => n.level));
        // 計算顯示起始 Y(根在底部)
        const startY = p.top + drawH - BAR_H; // 根底部對齊

        ctx.font = this.font(10);
        ctx.textBaseline = 'middle';
        ctx.textAlign = 'left';

        for (const n of nodes) {
            const bx = p.left + n.x;
            const bw = Math.max(0, n.width - 1);
            if (bw < 1) continue;
            const by = startY - n.level * STEP;
            if (by < p.top - BAR_H) continue; // 超出可視區域不繪

            const fillHex = flameColor(n.level);
            ctx.fillStyle = fillHex;

            // 圓角矩形
            const radius = Math.min(2, bw / 2, BAR_H / 2);
            ctx.beginPath();
            if (ctx.roundRect) {
                ctx.roundRect(bx, by, bw, BAR_H, radius);
            } else {
                ctx.moveTo(bx + radius, by);
                ctx.lineTo(bx + bw - radius, by);
                ctx.arcTo(bx + bw, by, bx + bw, by + BAR_H, radius);
                ctx.lineTo(bx + bw, by + BAR_H - radius);
                ctx.arcTo(bx + bw, by + BAR_H, bx, by + BAR_H, radius);
                ctx.lineTo(bx + radius, by + BAR_H);
                ctx.arcTo(bx, by + BAR_H, bx, by, radius);
                ctx.lineTo(bx, by + radius);
                ctx.arcTo(bx, by, bx + bw, by, radius);
                ctx.closePath();
            }
            ctx.fill();

            // 文字(寬度 > 30px 才標;亮度自適應)
            if (bw > 30) {
                const [rr, gg, bb] = hexToRgbArr(fillHex);
                ctx.fillStyle = (0.299 * rr + 0.587 * gg + 0.114 * bb) > 150 ? '#000000aa' : '#ffffffdd';
                ctx.fillText(this.ellipsis(ctx, n.name, bw - 6), bx + 4, by + BAR_H / 2);
            }

            // hit region(記錄繪製寬度供 tooltip % 顯示)
            this.addRegion({
                shape: 'rect', x: bx, y: by, w: bw, h: BAR_H,
                data: { ...n, _drawW: drawW },
                clickable: true
            });
        }

        // 底部刻度標籤(可選;顯示 0%~100% 或 root value)
        ctx.fillStyle = tok['--cl-text-secondary'];
        ctx.font = this.font(10);
        ctx.textAlign = 'left';
        ctx.textBaseline = 'top';
        const axisY = p.top + drawH + 4;
        ctx.fillText('0', p.left, axisY);
        ctx.textAlign = 'right';
        ctx.fillText(String(this.data.value ?? 100), p.left + drawW, axisY);
    }

    getTooltip(d) {
        const pct = d._drawW ? ((d.width / d._drawW) * 100).toFixed(1) + '%' : '';
        return [
            { label: '函式', value: d.name },
            { label: 'Value', value: d.value + ' ms' },
            { label: '佔比', value: pct },
            { label: 'Level', value: String(d.level) },
        ];
    }

    /** 點擊詳情面板(ModalPanel;由 CanvasChart 的 onPointClick 或子類覆寫觸發)。 */
    _handleClick(node) {
        const drawW = node._drawW || 1;
        const pct = ((node.width / drawW) * 100).toFixed(1);

        const root = document.createElement('div');
        root.style.cssText = 'min-width:200px;';

        const title = document.createElement('h3');
        title.textContent = node.name;
        title.style.cssText = 'margin:0 0 5px 0; border-bottom:1px solid var(--cl-border-light);' +
            ' padding-bottom:5px;';
        root.appendChild(title);

        const body = document.createElement('div');
        body.style.cssText = 'font-size:var(--cl-font-size-sm); color:var(--cl-text-secondary);';
        const makeRow = (label, val) => {
            const div = document.createElement('div');
            const s = document.createElement('strong');
            s.textContent = label + ': ';
            div.appendChild(s);
            div.appendChild(document.createTextNode(val));
            return div;
        };
        body.appendChild(makeRow('Value', node.value + ' ms'));
        body.appendChild(makeRow('% of Total', pct + '%'));
        root.appendChild(body);

        const actions = document.createElement('div');
        actions.style.cssText = 'margin-top:8px; text-align:right;';
        const btn = document.createElement('button');
        btn.textContent = 'View Stack';
        btn.style.cssText = 'padding:2px 8px; font-size:var(--cl-font-size-xs);';
        btn.addEventListener('click', () => {
            ModalPanel.alert({ message: 'Stack Trace: ' + node.name });
        });
        actions.appendChild(btn);
        root.appendChild(actions);

        // ModalPanel.alert 只認 message 字串(content 會被 setContent 覆蓋)——
        // 取回 modal 後把 message <p> 換成 DOM 詳情(保留 OK 鈕列)。
        const modal = ModalPanel.alert({ message: '' });
        const msgEl = modal.content && modal.content.querySelector('p');
        if (msgEl) msgEl.replaceWith(root);
        else modal.setContent(root);
    }
}

export default FlameChart;
