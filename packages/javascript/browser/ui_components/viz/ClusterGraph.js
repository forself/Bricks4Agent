/**
 * ClusterGraph — 即時分群關係圖(CanvasChart 子類;5000 節點級)。
 * 資料形狀:人 → 團體 → 上層團體(多層)+ 選配跨群關聯邊。
 *
 * 兩種互動模式:
 *   - mode:'drill'(預設):頂層團體=聚合節點(大小=成員數),點擊展開子群/成員,
 *     再點收合。可見節點恆少 → 弱機也 60fps,資料量放大不怕。
 *   - mode:'overview':全節點呈現(Barnes-Hut O(n log n)),群心吸力聚攏,
 *     葉群畫凸包泡泡。5000 節點在 Canvas 預算內。
 * 互動:滾輪縮放(游標為中心)、背景拖曳平移、節點拖曳(釘住)、hover 詳情、
 *       點聚合節點展開/收合、點人員 onSelect 回拋;LOD 標籤(縮放/聚合節點/hover 才畫)。
 * 增量:setData() 保留既有座標,模擬回溫(reheat)就地演化。
 *
 * @example
 * new ClusterGraph({
 *     container, title: '幫派組織關聯',
 *     nodes:  [{ id:'p1', label:'王小明', group:'g堂A' }, ...],
 *     groups: [{ id:'g堂A', parent:'g幫X', label:'堂口A' }, { id:'g幫X', parent:null, label:'幫派X' }],
 *     edges:  [{ source:'p1', target:'p9', type:'通聯' }],
 *     onSelect: (payload) => {...}      // { type:'person'|'group', node, members? }
 * }).mount(host);
 */
import { CanvasChart } from './CanvasChart.js';
import { createSimulation } from '../utils/force-engine.js';
import { buildQuadtree, nearestBody } from '../utils/quadtree.js';
import { hierarchicalColor } from '../utils/color-scale.js';

export class ClusterGraph extends CanvasChart {
    constructor(options = {}) {
        super({
            nodes: [], groups: [], edges: [],
            mode: 'drill',
            seed: 7,
            onSelect: null,
            labelMinScale: 0.8,        // LOD:世界縮放低於此值不畫人員標籤
            height: '420px',
            padding: { top: 0, right: 0, bottom: 0, left: 0 },
            ...options
        });
        this._view = { tx: 0, ty: 0, scale: 1 };
        this._expanded = new Set();
        this._selected = null;
        this._sim = null;
        this._simNodes = [];
        this._simTree = null;
        this._running = false;
        this._drag = null;
        this._bindGraphEvents();
        this.setData({ nodes: this.options.nodes, groups: this.options.groups, edges: this.options.edges }, { keepView: false });
    }

    /* ── 資料/衍生 ── */

    setData({ nodes = [], groups = [], edges = [] }, { keepView = true } = {}) {
        const prev = new Map(this._simNodes.map(n => [n.vid, n]));   // 增量:保留既有座標
        this._people = nodes;
        this._groups = groups;
        this._edges = edges;
        this._gById = new Map(groups.map(g => [g.id, g]));
        this._children = new Map();                                  // groupId → 子群 id[]
        for (const g of groups) {
            if (g.parent != null) {
                if (!this._children.has(g.parent)) this._children.set(g.parent, []);
                this._children.get(g.parent).push(g.id);
            }
        }
        this._members = new Map();                                   // groupId → 直屬人員[]
        for (const p of nodes) {
            if (!this._members.has(p.group)) this._members.set(p.group, []);
            this._members.get(p.group).push(p);
        }
        this._tops = groups.filter(g => g.parent == null).map(g => g.id);
        this._topIndex = new Map(this._tops.map((id, i) => [id, i]));
        // 每群的頂層祖先與層級(配色/凸包用)
        this._topOf = new Map(); this._levelOf = new Map();
        const walk = (id, top, lv) => {
            this._topOf.set(id, top); this._levelOf.set(id, lv);
            for (const c of this._children.get(id) || []) walk(c, top, lv + 1);
        };
        for (const tid of this._tops) walk(tid, tid, 0);
        // 群的遞迴總人數(聚合節點大小)
        this._countOf = new Map();
        const count = (id) => {
            if (this._countOf.has(id)) return this._countOf.get(id);
            let n = (this._members.get(id) || []).length;
            for (const c of this._children.get(id) || []) n += count(c);
            this._countOf.set(id, n);
            return n;
        };
        for (const g of groups) count(g.id);
        if (!keepView) { this._view = { tx: 0, ty: 0, scale: 1 }; this._expanded.clear(); }
        this._rebuildSim(prev);
    }

    /** 依模式/展開狀態導出可見圖(聚合節點 or 人員),邊映射到可見代表並加權。 */
    _visibleGraph() {
        const vis = [];             // { vid, kind:'group'|'person', ref, r, color, clusterKey }
        const repOf = new Map();    // personId → vid(邊映射用)
        const pushPerson = (p, clusterKey) => {
            const v = { vid: 'p:' + p.id, kind: 'person', ref: p, r: 4.5, color: this._colorOfGroup(p.group), clusterKey };
            vis.push(v);
            repOf.set(p.id, v.vid);
        };
        if (this.options.mode === 'overview') {
            for (const p of this._people) pushPerson(p, p.group);
        } else {
            const expandGroup = (gid) => {
                if (this._expanded.has(gid)) {
                    for (const c of this._children.get(gid) || []) expandGroup(c);
                    for (const p of this._members.get(gid) || []) pushPerson(p, gid);
                } else {
                    const g = this._gById.get(gid);
                    const n = this._countOf.get(gid) || 0;
                    const v = {
                        vid: 'g:' + gid, kind: 'group', ref: g,
                        r: Math.max(10, Math.min(34, 8 + Math.sqrt(n) * 2.2)),
                        color: this._colorOfGroup(gid), clusterKey: this._topOf.get(gid)
                    };
                    vis.push(v);
                    // 此群(含子孫)所有人員以此聚合節點為代表
                    const mark = (id) => {
                        for (const p of this._members.get(id) || []) repOf.set(p.id, v.vid);
                        for (const c of this._children.get(id) || []) mark(c);
                    };
                    mark(gid);
                }
            };
            for (const tid of this._tops) expandGroup(tid);
        }
        // 邊 → 可見代表,聚掉自環、同對加權
        const agg = new Map();
        for (const e of this._edges) {
            const a = repOf.get(e.source), b = repOf.get(e.target);
            if (!a || !b || a === b) continue;
            const key = a < b ? a + '→' + b : b + '→' + a;
            const cur = agg.get(key);
            if (cur) cur.weight++;
            else agg.set(key, { source: a, target: b, weight: 1, type: e.type });
        }
        return { vis, links: [...agg.values()] };
    }

    _colorOfGroup(gid) {
        const top = this._topOf.get(gid);
        return hierarchicalColor(this._topIndex.get(top) ?? 0, this._levelOf.get(gid) ?? 0);
    }

    _rebuildSim(prev = new Map()) {
        const { vis, links } = this._visibleGraph();
        for (const v of vis) {
            const old = prev.get(v.vid);
            if (old) { v.x = old.x; v.y = old.y; v.vx = old.vx; v.vy = old.vy; }
            v.id = v.vid;
        }
        this._simNodes = vis;
        this._simLinks = links;
        const overview = this.options.mode === 'overview';
        this._sim = createSimulation(vis, links, {
            seed: this.options.seed,
            charge: overview ? -28 : -220,
            linkDistance: overview ? 26 : 70,
            linkStrength: overview ? 0.03 : 0.06,
            clusterOf: overview ? (n) => n.ref.group : (n) => n.clusterKey,
            clusterStrength: overview ? 0.10 : 0.04,
            centerStrength: 0.02,
            theta: 0.9,
            alphaDecay: overview ? 0.02 : 0.03
        });
        this._startLoop();
    }

    /* ── 模擬迴圈 ── */

    _startLoop() {
        if (this._running) return;
        this._running = true;
        const tick = () => {
            if (this._destroyed) return;
            const active = this._sim && (this._sim.alpha() > 0.011 || this._drag?.node);
            if (active) {
                this._sim.step();
                this._simTree = null;                    // 位置變了,命中樹失效
                this._renderNow();
                this._loopRaf = requestAnimationFrame(tick);
            } else {
                this._running = false;
                this._simTree = null;
                this._renderNow();                        // 最終幀(含完整標籤)
            }
        };
        this._loopRaf = requestAnimationFrame(tick);
    }

    /* ── 座標/命中(世界座標系;覆寫基底)── */

    _toWorld(sx, sy) {
        const v = this._view;
        return { x: (sx - v.tx) / v.scale, y: (sy - v.ty) / v.scale };
    }

    _hitNode(sx, sy) {
        const { x, y } = this._toWorld(sx, sy);
        if (!this._simTree) this._simTree = buildQuadtree(this._simNodes);
        const hit = nearestBody(this._simTree, x, y, 40 / this._view.scale);
        if (!hit) return null;
        const d = Math.hypot(hit.x - x, hit.y - y);
        return d <= Math.max(hit.r + 3, 8 / this._view.scale) ? hit : null;
    }

    _hitTest() { return null; }   // 停用基底 region 機制(本件全自管)

    /* ── 互動 ── */

    _bindGraphEvents() {
        const c = this.canvas;
        c.addEventListener('wheel', (e) => {
            e.preventDefault();
            const factor = e.deltaY < 0 ? 1.15 : 1 / 1.15;
            const v = this._view;
            const ns = Math.max(0.15, Math.min(6, v.scale * factor));
            const k = ns / v.scale;
            v.tx = e.offsetX - (e.offsetX - v.tx) * k;    // 以游標為縮放中心
            v.ty = e.offsetY - (e.offsetY - v.ty) * k;
            v.scale = ns;
            this._simTree = null;
            this._renderNow();
        }, { passive: false });

        c.addEventListener('mousedown', (e) => {
            const n = this._hitNode(e.offsetX, e.offsetY);
            if (n) {
                n.fixed = true;
                this._drag = { node: n, moved: false };
                this._sim.reheat(0.25);
                this._startLoop();
            } else {
                this._drag = { pan: { sx: e.offsetX, sy: e.offsetY, tx: this._view.tx, ty: this._view.ty }, moved: false };
            }
        });
        c.addEventListener('mousemove', (e) => {
            if (this._drag?.node) {
                const w = this._toWorld(e.offsetX, e.offsetY);
                this._drag.node.x = w.x; this._drag.node.y = w.y;
                this._drag.moved = true;
            } else if (this._drag?.pan) {
                this._view.tx = this._drag.pan.tx + (e.offsetX - this._drag.pan.sx);
                this._view.ty = this._drag.pan.ty + (e.offsetY - this._drag.pan.sy);
                this._drag.moved = true;
                this._simTree = null;
                this._renderNow();
            } else {
                const n = this._hitNode(e.offsetX, e.offsetY);
                if (n !== this._hover) {
                    this._hover = n;
                    c.style.cursor = n ? 'pointer' : 'default';
                    if (n) { this._fillTooltip(this._nodeTooltip(n)); this._tooltip.style.display = 'block'; }
                    else this._tooltip.style.display = 'none';
                    if (!this._running) this._renderNow();
                }
                if (n) this._positionTooltip(e.offsetX, e.offsetY);
            }
        });
        const endDrag = (e) => {
            if (!this._drag) return;
            const d = this._drag; this._drag = null;
            if (d.node) {
                d.node.fixed = false;
                if (!d.moved) this._clickNode(d.node);     // 原地放開=點擊
            }
        };
        c.addEventListener('mouseup', endDrag);
        c.addEventListener('mouseleave', (e) => { endDrag(e); this._hover = null; this._tooltip.style.display = 'none'; });
    }

    _clickNode(n) {
        if (n.kind === 'group') {
            const gid = n.ref.id;
            const expandable = (this._children.get(gid) || []).length || (this._members.get(gid) || []).length;
            if (this._expanded.has(gid)) this._collapse(gid);
            else if (expandable) {
                this._expanded.add(gid);
                // 展開的子項自聚合節點位置散出(視覺連續)
                this._seedPos = { x: n.x, y: n.y };
            }
            this._rebuildAt(n);
            if (typeof this.options.onSelect === 'function') {
                this.options.onSelect({ type: 'group', node: n.ref, count: this._countOf.get(gid), members: this._collectMembers(gid) });
            }
        } else {
            this._selected = n.vid;
            if (typeof this.options.onSelect === 'function') this.options.onSelect({ type: 'person', node: n.ref });
            if (!this._running) this._renderNow();
        }
    }

    _collapse(gid) {
        this._expanded.delete(gid);
        const rec = (id) => { for (const c of this._children.get(id) || []) { this._expanded.delete(c); rec(c); } };
        rec(gid);
    }

    _collectMembers(gid) {
        const out = [];
        const rec = (id) => {
            for (const p of this._members.get(id) || []) out.push(p);
            for (const c of this._children.get(id) || []) rec(c);
        };
        rec(gid);
        return out;
    }

    _rebuildAt(originNode) {
        const prev = new Map(this._simNodes.map(n => [n.vid, n]));
        this._rebuildSim(prev);
        // 新節點(無舊座標者)從展開點散出
        if (this._seedPos) {
            for (const n of this._simNodes) {
                if (!prev.has(n.vid)) {
                    n.x = this._seedPos.x + this._deterministicJitter(n.vid, 'x');
                    n.y = this._seedPos.y + this._deterministicJitter(n.vid, 'y');
                }
            }
            this._seedPos = null;
        }
        this._sim.reheat(0.6);
        this._startLoop();
    }

    _deterministicJitter(value, axis) {
        const text = `${axis}:${String(value ?? '')}`;
        let hash = 2166136261;
        for (let i = 0; i < text.length; i++) {
            hash ^= text.charCodeAt(i);
            hash = Math.imul(hash, 16777619);
        }
        return ((hash >>> 0) / 0xffffffff - 0.5) * 30;
    }

    _nodeTooltip(n) {
        if (n.kind === 'group') {
            const lvl = this._levelOf.get(n.ref.id) ?? 0;
            return [
                { label: '', value: n.ref.label || n.ref.id },
                { label: '層級', value: lvl === 0 ? '頂層團體' : `第 ${lvl + 1} 層` },
                { label: '成員數', value: this.fmt(this._countOf.get(n.ref.id) || 0) },
                { label: '', value: this._expanded.has(n.ref.id) ? '(點擊收合)' : '(點擊展開)' }
            ];
        }
        const g = this._gById.get(n.ref.group);
        return [
            { label: '', value: n.ref.label || n.ref.id },
            { label: '團體', value: g ? (g.label || g.id) : String(n.ref.group ?? '') }
        ];
    }

    /* ── 繪製 ── */

    draw(ctx, w, h) {
        const t = this.tokens(['--cl-text', '--cl-text-secondary', '--cl-border-medium', '--cl-bg', '--cl-text-dim']);
        const v = this._view;
        if (!this._simNodes.length) {
            ctx.font = this.font(13); ctx.fillStyle = t['--cl-text-dim'];
            ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
            ctx.fillText('無資料', w / 2, h / 2);
            return;
        }
        // 首繪置中
        if (!this._centered) { v.tx = w / 2; v.ty = h / 2; this._centered = true; }

        ctx.save();
        ctx.translate(v.tx, v.ty);
        ctx.scale(v.scale, v.scale);

        // 凸包(overview:葉群泡泡)
        if (this.options.mode === 'overview') this._drawHulls(ctx);

        // 邊(LOD:寬度/透明度隨權重)
        ctx.strokeStyle = t['--cl-border-medium'];
        for (const l of this._simLinks) {
            ctx.globalAlpha = Math.min(0.75, 0.18 + l.weight * 0.08);
            ctx.lineWidth = Math.min(4, 0.6 + l.weight * 0.3) / v.scale;
            ctx.beginPath();
            ctx.moveTo(l.source.x, l.source.y);
            ctx.lineTo(l.target.x, l.target.y);
            ctx.stroke();
        }
        ctx.globalAlpha = 1;

        // 節點
        for (const n of this._simNodes) {
            ctx.fillStyle = n.color;
            ctx.beginPath();
            ctx.arc(n.x, n.y, n.r, 0, Math.PI * 2);
            ctx.fill();
            if (n === this._hover || n.vid === this._selected) {
                ctx.strokeStyle = t['--cl-text'];
                ctx.lineWidth = 2 / v.scale;
                ctx.stroke();
            } else if (n.kind === 'group') {
                ctx.strokeStyle = t['--cl-bg'];
                ctx.lineWidth = 1.5 / v.scale;
                ctx.stroke();
            }
        }
        // 標籤(LOD:聚合節點恆標;人員需夠大縮放或 hover/選取;繪製上限防爆)
        ctx.textAlign = 'center';
        ctx.fillStyle = t['--cl-text'];
        const showPerson = v.scale >= this.options.labelMinScale;
        let labelBudget = 400;
        ctx.font = this.font(Math.max(10, 11 / v.scale));
        for (const n of this._simNodes) {
            if (labelBudget <= 0) break;
            const isFocus = n === this._hover || n.vid === this._selected;
            if (n.kind === 'group') {
                ctx.textBaseline = 'middle';
                const label = `${n.ref.label || n.ref.id}(${this.fmt(this._countOf.get(n.ref.id) || 0)})`;
                ctx.fillText(this.ellipsis(ctx, label, n.r * 6), n.x, n.y - n.r - 8 / v.scale);
                labelBudget--;
            } else if (showPerson || isFocus) {
                ctx.textBaseline = 'top';
                ctx.fillText(this.ellipsis(ctx, n.ref.label || n.ref.id, 90), n.x, n.y + n.r + 2 / v.scale);
                labelBudget--;
            }
        }
        ctx.restore();

        // HUD(左下:模式/縮放/可見數)
        ctx.font = this.font(10);
        ctx.fillStyle = t['--cl-text-dim'];
        ctx.textAlign = 'left'; ctx.textBaseline = 'bottom';
        ctx.fillText(
            `${this.options.mode === 'overview' ? '全景' : '鑽取'}|節點 ${this._simNodes.length}|邊 ${this._simLinks.length}|${Math.round(v.scale * 100)}%`,
            8, h - 6
        );
    }

    _drawHulls(ctx) {
        // 依葉群聚點 → 凸包 → 半透明泡泡(Andrew's monotone chain)
        const byGroup = new Map();
        for (const n of this._simNodes) {
            let a = byGroup.get(n.ref.group);
            if (!a) { a = []; byGroup.set(n.ref.group, a); }
            a.push(n);
        }
        for (const [gid, pts] of byGroup) {
            if (pts.length < 3) continue;
            const hull = convexHull(pts.map(p => [p.x, p.y]));
            if (hull.length < 3) continue;
            ctx.beginPath();
            ctx.moveTo(hull[0][0], hull[0][1]);
            for (let i = 1; i < hull.length; i++) ctx.lineTo(hull[i][0], hull[i][1]);
            ctx.closePath();
            const color = this._colorOfGroup(gid);
            ctx.globalAlpha = 0.10;
            ctx.fillStyle = color;
            ctx.fill();
            ctx.globalAlpha = 0.5;
            ctx.strokeStyle = color;
            ctx.lineWidth = 1.5 / this._view.scale;
            ctx.stroke();
            ctx.globalAlpha = 1;
        }
    }

    /* ── 公開 API ── */

    setMode(mode) {
        if (mode === this.options.mode) return;
        this.options.mode = mode;
        this._rebuildSim(new Map(this._simNodes.map(n => [n.vid, n])));
    }

    expandAll() {
        for (const g of this._groups) this._expanded.add(g.id);
        this._rebuildAt(null);
    }

    collapseAll() {
        this._expanded.clear();
        this._rebuildAt(null);
    }

    destroy() {
        cancelAnimationFrame(this._loopRaf);
        this._running = false;
        if (this._sim) this._sim.stop();
        super.destroy();
    }
}

/** Andrew's monotone chain 凸包(輸入 [x,y][];輸出逆時針頂點)。 */
export function convexHull(points) {
    const pts = [...points].sort((a, b) => a[0] - b[0] || a[1] - b[1]);
    if (pts.length < 3) return pts;
    const cross = (o, a, b) => (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0]);
    const lower = [];
    for (const p of pts) {
        while (lower.length >= 2 && cross(lower[lower.length - 2], lower[lower.length - 1], p) <= 0) lower.pop();
        lower.push(p);
    }
    const upper = [];
    for (let i = pts.length - 1; i >= 0; i--) {
        const p = pts[i];
        while (upper.length >= 2 && cross(upper[upper.length - 2], upper[upper.length - 1], p) <= 0) upper.pop();
        upper.push(p);
    }
    lower.pop(); upper.pop();
    return lower.concat(upper);
}

export default ClusterGraph;
