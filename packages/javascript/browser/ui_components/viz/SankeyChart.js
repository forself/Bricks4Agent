/**
 * SankeyChart — 桑基流量圖(CanvasChart 版;SVG 禁用政策下的重寫,API 向後相容)。
 * 資料形狀:{ nodes:[{name}], links:[{source,target,value}] }(source/target 為數字索引)
 * 佈局:拓撲排序欄位分層;貝茲流帶(bezierCurveTo)填半透明色;節點矩形+標籤。
 * Hover:getTooltip → CanvasChart DOM tooltip;點擊節點保留 ModalPanel.alert 語意。
 */
import { CanvasChart } from './CanvasChart.js';
import { ModalPanel } from '../layout/Panel/index.js';
import { categoricalColor } from '../utils/color-scale.js';

const px = (v, d) => typeof v === 'number' ? v + 'px' : (v || d);

export class SankeyChart extends CanvasChart {
    constructor(options = {}) {
        super({
            ...options,
            width: px(options.width, '100%'),
            height: px(options.height, '300px'),
            padding: options.padding || { top: 16, right: 80, bottom: 16, left: 16 },
            onPointClick: options.onPointClick || null,
            data: options.data || { nodes: [], links: [] },
        });
    }

    /* ── 佈局計算 ── */

    _buildLayout(w, h) {
        const o = this.options;
        const raw = o.data || { nodes: [], links: [] };
        if (!raw.nodes || raw.nodes.length === 0) return null;

        const p = this.options.padding;
        const gw = Math.max(10, w - p.left - p.right);
        const gh = Math.max(10, h - p.top - p.bottom);

        // 節點清單(複製,附 id/連結清單)
        const nodes = raw.nodes.map((n, i) => ({
            ...n, id: i,
            sourceLinks: [],  // link 物件(目標索引)
            targetLinks: []
        }));

        // 連結清單(數字索引)
        const links = raw.links.map(l => ({
            source: typeof l.source === 'object' ? l.source.id : Number(l.source),
            target: typeof l.target === 'object' ? l.target.id : Number(l.target),
            value: Number(l.value) || 0
        }));

        links.forEach(l => {
            if (nodes[l.source]) nodes[l.source].sourceLinks.push(l);
            if (nodes[l.target]) nodes[l.target].targetLinks.push(l);
        });

        // 拓撲排序欄位指派(BFS/迭代)
        nodes.forEach(n => { n.col = 0; });
        const visited = new Set();
        const dfs = (n, col) => {
            if (visited.has(n.id)) return;
            visited.add(n.id);
            n.col = Math.max(n.col, col);
            n.sourceLinks.forEach(l => {
                if (nodes[l.target]) dfs(nodes[l.target], col + 1);
            });
        };
        // 從無 incoming 的節點出發
        const roots = nodes.filter(n => n.targetLinks.length === 0);
        if (roots.length === 0 && nodes.length > 0) roots.push(nodes[0]);
        roots.forEach(n => dfs(n, 0));

        const numCols = Math.max(...nodes.map(n => n.col)) + 1;
        const nodeW = Math.max(8, Math.min(20, gw / numCols / 3));
        const colSpan = gw / numCols;

        // 群組按欄
        const columns = Array.from({ length: numCols }, () => []);
        nodes.forEach(n => columns[n.col].push(n));

        // 計算節點高度(按 flow 量);每欄等比例縮放
        const PAD_NODE = 6;       // 節點間距
        columns.forEach(colNodes => {
            // 節點值 = max(incoming, outgoing, 10)
            colNodes.forEach(n => {
                const inV = n.targetLinks.reduce((s, l) => s + l.value, 0);
                const outV = n.sourceLinks.reduce((s, l) => s + l.value, 0);
                n._rawH = Math.max(inV, outV, 10);
            });
            const totalRaw = colNodes.reduce((s, n) => s + n._rawH, 0);
            const availH = gh - PAD_NODE * (colNodes.length - 1);
            const scale = availH / (totalRaw || 1);
            let curY = p.top;
            colNodes.forEach(n => {
                n.height = Math.max(8, n._rawH * scale);
                n.x = p.left + n.col * colSpan + (colSpan - nodeW) / 2;
                n.y = curY;
                curY += n.height + PAD_NODE;
            });
        });

        // 連結起止 Y 分配(source 節點右側、target 節點左側;各自堆疊)
        nodes.forEach(n => {
            n._srcY = n.y;    // 下一條 link 出發 Y
            n._tgtY = n.y;    // 下一條 link 到達 Y
        });
        links.forEach(l => {
            const s = nodes[l.source], t = nodes[l.target];
            if (!s || !t) return;
            const sH = s.height, tH = t.height;
            const sTotal = s.sourceLinks.reduce((a, x) => a + x.value, 0) || 1;
            const tTotal = t.targetLinks.reduce((a, x) => a + x.value, 0) || 1;
            l._lh = l.value / sTotal * sH;
            l._rh = l.value / tTotal * tH;
            l._sy = s._srcY;
            l._ty = t._tgtY;
            s._srcY += l._lh;
            t._tgtY += l._rh;
        });

        return { nodes, links, nodeW, numCols, colSpan };
    }

    /* ── 繪製 ── */

    draw(ctx, w, h) {
        const o = this.options;
        const t = this.tokens(['--cl-text', '--cl-text-secondary', '--cl-text-dim', '--cl-bg']);
        const raw = o.data || { nodes: [], links: [] };

        if (!raw.nodes || raw.nodes.length === 0) {
            ctx.font = this.font(13);
            ctx.fillStyle = t['--cl-text-dim'];
            ctx.textAlign = 'center';
            ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }

        const layout = this._buildLayout(w, h);
        if (!layout) return;
        const { nodes, links, nodeW } = layout;

        // 1. 繪製流帶(貝茲曲線;先填半透明色)
        links.forEach((l, li) => {
            const s = nodes[l.source], tg = nodes[l.target];
            if (!s || !tg) return;
            const sx = s.x + nodeW;
            const tx = tg.x;
            const sy0 = l._sy, sy1 = l._sy + l._lh;
            const ty0 = l._ty, ty1 = l._ty + l._rh;
            const mx = (sx + tx) / 2;

            const path = new Path2D();
            path.moveTo(sx, sy0);
            path.bezierCurveTo(mx, sy0, mx, ty0, tx, ty0);
            path.lineTo(tx, ty1);
            path.bezierCurveTo(mx, ty1, mx, sy1, sx, sy1);
            path.closePath();

            // 連結色 = source 節點色(半透明)
            const baseColor = categoricalColor(s.col);
            ctx.fillStyle = baseColor + '66';   // ~40% alpha
            ctx.fill(path);

            // region(bounds = 包絡矩形)
            const bx = Math.min(sx, tx);
            const bw2 = Math.max(sx, tx) - bx;
            const by = Math.min(sy0, ty0);
            const bh = Math.max(sy1, ty1) - by;
            this.addRegion({
                shape: 'path', path,
                bounds: { x: bx, y: by, w: bw2, h: bh },
                data: {
                    type: 'link',
                    source: s.name,
                    target: tg.name,
                    value: l.value
                }
            });
        });

        // 2. 繪製節點(矩形)
        nodes.forEach((n, i) => {
            const color = categoricalColor(n.col);
            const path = new Path2D();
            path.roundRect
                ? path.roundRect(n.x, n.y, nodeW, n.height, 2)
                : path.rect(n.x, n.y, nodeW, n.height);
            ctx.fillStyle = color;
            ctx.fill(path);

            this.addRegion({
                shape: 'path', path,
                bounds: { x: n.x, y: n.y, w: nodeW, h: n.height },
                data: {
                    type: 'node',
                    name: n.name,
                    col: n.col,
                    value: Math.max(
                        n.sourceLinks.reduce((s, l) => s + l.value, 0),
                        n.targetLinks.reduce((s, l) => s + l.value, 0)
                    )
                },
                clickable: true
            });

            // 節點標籤(右側)
            const labelX = n.x + nodeW + 4;
            const labelY = n.y + n.height / 2;
            const maxLabelW = this.options.padding.right - 6;
            ctx.fillStyle = t['--cl-text-secondary'];
            ctx.font = this.font(10);
            ctx.textAlign = 'left';
            ctx.textBaseline = 'middle';
            ctx.fillText(this.ellipsis(ctx, String(n.name), maxLabelW), labelX, labelY);
        });
    }

    /* ── Tooltip ── */

    getTooltip(d) {
        if (d.type === 'link') {
            return [
                { label: '來源', value: d.source },
                { label: '目標', value: d.target },
                { label: '流量', value: this.fmt(d.value) }
            ];
        }
        // node
        return [
            { label: '', value: d.name },
            { label: 'Layer', value: d.col },
            { label: 'Flow Volume', value: d.value != null ? this.fmt(d.value) : 'N/A' }
        ];
    }

    /* ── 點擊行為(語意保留:ModalPanel.alert)── */

    _handleClick(d) {
        if (d.type !== 'node') return;
        ModalPanel.alert({ message: 'Step Details: ' + d.name });
    }

    /* ── 公開 API(舊 API 相容)── */

    /** 設定資料並重繪。 */
    setData(data) {
        this.options.data = data;
        this.render();
    }

    /** 覆寫 CanvasChart 的 click 派送,插入 node 點擊語意。 */
    _buildDom() {
        super._buildDom();
        // 覆寫 click:若命中 node → ModalPanel
        this.canvas.addEventListener('click', (e) => {
            const r = this._hitTest(e.offsetX, e.offsetY);
            if (r && r.data && r.data.type === 'node') {
                this._handleClick(r.data);
            }
        });
    }
}

export default SankeyChart;
