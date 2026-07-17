/**
 * SunburstChart — 旭日環(CanvasChart 版;SVG 禁用政策下的重寫,API 向後相容)。
 * 資料形狀:{ name, children:[], value? } 階層樹(葉節點需有 value;非葉節點自動加總)
 * 佈局:半徑分層(每層等寬環帶);角度按 value 比例分配;根節點佔滿圓心圓盤。
 * Hover:getTooltip → CanvasChart DOM tooltip;點擊扇區保留 ModalPanel.alert 語意。
 */
import { CanvasChart } from './CanvasChart.js';
import { ModalPanel } from '../layout/Panel/index.js';
import { categoricalColor, hierarchicalColor } from '../utils/color-scale.js';
import { FALLBACK_PAINT } from '../utils/theme-bus.js';

const px = (v, d) => typeof v === 'number' ? v + 'px' : (v || d);
const TAU = Math.PI * 2;
const START = -Math.PI / 2;   // 12 點鐘方向開始

export class SunburstChart extends CanvasChart {
    constructor(options = {}) {
        super({
            ...options,
            width: px(options.width, '100%'),
            height: px(options.height, '300px'),
            padding: options.padding || { top: 8, right: 8, bottom: 8, left: 8 },
            data: options.data || {},
        });
    }

    /* ── 階層佈局計算 ── */

    /** 遞迴加總 value(葉節點預設 1)。 */
    _addValues(node) {
        if (!node.children || node.children.length === 0) {
            node._value = node.value != null ? Number(node.value) : 1;
        } else {
            node._value = node.children.reduce((acc, c) => acc + this._addValues(c), 0);
        }
        return node._value;
    }

    /**
     * 遞迴建立扇形節點清單。
     * 每個扇形帶有:{ name, value, depth, topIndex, a0, a1, r0, r1, path:Path2D, bounds }
     */
    _partition(root, cx, cy, ringW) {
        this._addValues(root);
        const out = [];

        const traverse = (node, depth, a0, a1, topIndex) => {
            const r0 = depth * ringW;
            const r1 = r0 + ringW;
            const ra0 = START + a0 * TAU;
            const ra1 = START + a1 * TAU;

            const path = new Path2D();
            if (depth === 0) {
                // 根:實心圓盤
                path.arc(cx, cy, r1, 0, TAU);
            } else {
                // 環帶扇形
                path.arc(cx, cy, r1, ra0, ra1);
                path.arc(cx, cy, r0, ra1, ra0, true);
                path.closePath();
            }

            // 包絡矩形(取外圓外接正方形)
            const bounds = {
                x: cx - r1, y: cy - r1,
                w: r1 * 2, h: r1 * 2
            };

            out.push({ name: node.name, value: node._value, depth, topIndex, a0, a1, path, bounds });

            if (node.children) {
                let curA = a0;
                node.children.forEach((child, ci) => {
                    const span = (a1 - a0) * (child._value / (node._value || 1));
                    // 頂層子節點編號決定色系
                    traverse(child, depth + 1, curA, curA + span, depth === 0 ? ci : topIndex);
                    curA += span;
                });
            }
        };

        traverse(root, 0, 0, 1, 0);
        return out;
    }

    /* ── 繪製 ── */

    draw(ctx, w, h) {
        const o = this.options;
        const t = this.tokens(['--cl-text', '--cl-text-secondary', '--cl-text-dim', '--cl-bg']);
        const data = o.data;

        if (!data || !data.name) {
            ctx.font = this.font(13);
            ctx.fillStyle = t['--cl-text-dim'];
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }

        const p = o.padding;
        const cw = w - p.left - p.right;
        const ch = h - p.top - p.bottom;
        const cx = p.left + cw / 2;
        const cy = p.top + ch / 2;

        // 偵測最大深度以決定環帶寬
        const maxDepth = this._maxDepth(data);
        const maxR = Math.max(10, Math.min(cw, ch) / 2);
        const ringW = maxR / Math.max(maxDepth, 1);

        const nodes = this._partition(data, cx, cy, ringW);

        nodes.forEach((n, ni) => {
            // 色彩:深度 0 用淺中性;深度 1+ 按 topIndex × depth shade
            let color;
            if (n.depth === 0) {
                color = t['--cl-text-dim'] || FALLBACK_PAINT;
            } else {
                color = hierarchicalColor(n.topIndex, n.depth - 1);
            }

            ctx.fillStyle = color;
            ctx.fill(n.path);
            ctx.strokeStyle = t['--cl-bg'];
            ctx.lineWidth = 1;
            ctx.stroke(n.path);

            this.addRegion({
                shape: 'path',
                path: n.path,
                bounds: n.bounds,
                data: {
                    name: n.name,
                    value: n.value,
                    depth: n.depth,
                    pct: (n.a1 - n.a0) * 100
                },
                clickable: true
            });

            // 扇形標籤(中心角度;夠寬才標)
            if (n.depth > 0) {
                const spanAngle = (n.a1 - n.a0) * TAU;
                if (spanAngle > 0.15) {
                    const midA = START + (n.a0 + n.a1) / 2 * TAU;
                    const midR = (n.depth - 0.5) * ringW + ringW / 2;
                    const lx = cx + Math.cos(midA) * midR;
                    const ly = cy + Math.sin(midA) * midR;
                    const maxLW = ringW * spanAngle * 0.9;
                    ctx.font = this.font(Math.min(11, ringW * 0.35));
                    ctx.textAlign = 'center';
                    ctx.textBaseline = 'middle';
                    ctx.fillStyle = '#ffffffcc';
                    ctx.fillText(this.ellipsis(ctx, String(n.name), maxLW), lx, ly);
                }
            }
        });

        // 根節點標籤(圓心)
        ctx.font = this.font(Math.min(13, ringW * 0.4), 600);
        ctx.fillStyle = t['--cl-text-secondary'];
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillText(this.ellipsis(ctx, String(data.name), ringW * 1.6), cx, cy);
    }

    _maxDepth(node, d = 0) {
        if (!node.children || node.children.length === 0) return d;
        return Math.max(...node.children.map(c => this._maxDepth(c, d + 1)));
    }

    /* ── Tooltip ── */

    getTooltip(d) {
        return [
            { label: '', value: d.name },
            { label: 'Count', value: this.fmt(d.value) },
            { label: 'Depth', value: d.depth },
            { label: '占比', value: d.pct.toFixed(1) + '%' }
        ];
    }

    /* ── 點擊行為(語意保留:ModalPanel.alert)── */

    _handleClick(d) {
        ModalPanel.alert({ message: 'Drilldown: ' + d.name });
    }

    /** 覆寫 CanvasChart 的 click 派送。 */
    _buildDom() {
        super._buildDom();
        this.canvas.addEventListener('click', (e) => {
            const r = this._hitTest(e.offsetX, e.offsetY);
            if (r && r.data && r.data.clickable !== false) {
                this._handleClick(r.data);
            }
        });
    }

    /* ── 公開 API(舊 API 相容)── */

    /** 設定資料並重繪。 */
    setData(data) {
        this.options.data = data;
        this.render();
    }
}

export default SunburstChart;
