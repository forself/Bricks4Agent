/**
 * RelationChart — 力導向關聯圖(CanvasChart 子類;庫政策:SVG 禁用,Canvas 全量重繪)。
 *
 * 遷移說明(自 BaseChart/SVG 版):
 *   - 幾何全走 canvas rAF:每 tick 跑一次原版土製模擬(中心引力 + O(n²) 斥力 +
 *     彈簧邊 + 阻尼積分 + 邊界 clamp),再 _renderNow() 重繪;節點數 ≤ 數百,
 *     刻意「沿用」原模擬參數與語意(repulsion 1000 / linkDist 120 / friction 0.85),
 *     不換 force-engine(alpha 退火會停格、世界座標需再加視圖變換,風險較高)。
 *   - 命中自管:圓形距離判定(_pickNode;由後往前=視覺最上層優先),停用基底 region。
 *   - tooltip/詳情卡:DOM + cssText + textContent(CSP 相容、免注入),含 200ms
 *     寬限期讓滑鼠移入互動 tooltip(Copy ID 按鈕行為保留)。
 *   - 圖例:canvas 直繪(底部置中);節點與圖例同一 categoricalColor(群首見序),
 *     修正舊版 group.length 撞色問題。顏色僅 token / categoricalColor,零硬編。
 *   - 拖曳:mousedown 抓節點即釘住(模擬跳過該節點),mousemove 直設座標,
 *     mouseup / mouseleave 釋放;拖曳中節點描邊 --cl-primary。
 */
import { CanvasChart } from './CanvasChart.js';
import { categoricalColor } from '../utils/color-scale.js';
import Locale from '../i18n/index.js';

const NODE_R = 25;      // 節點半徑(繪製與命中一致;沿用原版 circle r=25)
const BOUND_R = 20;     // 邊界內縮(沿用原版 clamp 值)

export class RelationChart extends CanvasChart {
    constructor(options = {}) {
        const opts = { ...options };
        if (typeof opts.width === 'number') opts.width = opts.width + 'px';
        if (typeof opts.height === 'number') opts.height = opts.height + 'px';
        super({ padding: { top: 0, right: 0, bottom: 0, left: 0 }, ...opts });

        this.nodes = this.options.nodes || [];
        this.links = this.options.links || [];

        // 模擬 / 互動狀態(欄位語意沿用原版)
        this.width = 0;              // 繪圖區 CSS px(draw 同步;物理邊界用)
        this.height = 0;
        this.simulation = 0;         // rAF handle(沿用原版欄位名)
        this.draggingNode = null;
        this.isDragging = false;
        this.detailsEnabled = true;
        this._hoverNode = null;
        this._hideTimer = null;
        this._controls = null;
        this._groupOrder = new Map();
        this._linkRefs = [];
        this.viewport = { x: 0, y: 0, scale: 1 };
        this.isPanning = false;
        this._pointerDown = null;
        this._moved = false;

        this._bindGraphEvents();
        this._bindTooltipHover();
        this._startLoop();
    }

    /* ── 確定性初始散佈(沿用原版 FNV-1a)── */

    _nodeSeed(node, index) {
        return String(node.id ?? node.label ?? node.name ?? index);
    }

    _deterministicUnit(seed) {
        let hash = 2166136261;
        for (let i = 0; i < seed.length; i++) {
            hash ^= seed.charCodeAt(i);
            hash = Math.imul(hash, 16777619);
        }
        return (hash >>> 0) / 0xFFFFFFFF;
    }

    _ensurePositions(w, h) {
        const cx = w / 2, cy = h / 2;
        this.nodes.forEach((node, index) => {
            if (node.x === undefined) {
                const key = this._nodeSeed(node, index);
                node.x = cx + (this._deterministicUnit(`${key}:x`) - 0.5) * 50;
                node.y = cy + (this._deterministicUnit(`${key}:y`) - 0.5) * 50;
                node.vx = 0;
                node.vy = 0;
            }
            if (node.vx === undefined) { node.vx = 0; node.vy = 0; }
        });
    }

    /* ── 每幀資料整備(id → 節點、群 → 首見序;≤ 數百節點,成本可忽略)── */

    _prepFrame() {
        const order = new Map();
        for (const n of this.nodes) {
            if (n.group != null && !order.has(n.group)) order.set(n.group, order.size);
        }
        this._groupOrder = order;

        const byId = new Map(this.nodes.map(n => [n.id, n]));
        this._linkRefs = [];
        for (const l of this.links) {
            const s = byId.get(l.source);
            const t = byId.get(l.target);
            if (s && t && s !== t) this._linkRefs.push([s, t, l]);
        }
    }

    _nodeColor(node, index) {
        const custom = this.options.getNodeColor?.(node, index);
        if (custom) return custom;
        return node.group == null
            ? categoricalColor(index)
            : categoricalColor(this._groupOrder.get(node.group) ?? 0);
    }

    _nodeRadius() {
        return Math.max(4, Number(this.options.nodeRadius) || NODE_R);
    }

    /* ── 模擬迴圈(tick → 重繪;destroy 停 rAF)── */

    _startLoop() {
        const tick = () => {
            if (this._destroyed) return;
            this._tickPhysics();
            this._renderNow();
            this.simulation = requestAnimationFrame(tick);
        };
        this.simulation = requestAnimationFrame(tick);
    }

    _tickPhysics() {
        const w = this.width, h = this.height;
        if (!w || !h || !this.nodes.length) return;    // 首繪前(尺寸未知)不積分
        this._prepFrame();
        this._ensurePositions(w, h);

        const repulsion = 1000;
        const linkDist = 120;
        const centerStrength = 0.005;   // 弱向心,讓版面能撐開
        const cx = w / 2, cy = h / 2;

        // 力:中心引力 + 近距斥力(cutoff 60000,沿用原版)
        this.nodes.forEach(node => {
            if (node === this.draggingNode) return;    // 拖曳=釘住:不受力

            node.vx += (cx - node.x) * centerStrength;
            node.vy += (cy - node.y) * centerStrength;

            this.nodes.forEach(other => {
                if (node === other) return;
                const dx = node.x - other.x;
                const dy = node.y - other.y;
                let distSq = dx * dx + dy * dy;
                if (distSq === 0) distSq = 0.1;

                if (distSq < 60000) {
                    const f = repulsion / distSq;
                    const dist = Math.sqrt(distSq);
                    node.vx += (dx / dist) * f;
                    node.vy += (dy / dist) * f;
                }
            });
        });

        // 彈簧邊(同點防除零)
        for (const [source, target] of this._linkRefs) {
            const dx = target.x - source.x;
            const dy = target.y - source.y;
            const dist = Math.sqrt(dx * dx + dy * dy) || 1e-6;
            const force = (dist - linkDist) * 0.05;
            const fx = (dx / dist) * force;
            const fy = (dy / dist) * force;

            if (source !== this.draggingNode) { source.vx += fx; source.vy += fy; }
            if (target !== this.draggingNode) { target.vx -= fx; target.vy -= fy; }
        }

        // 積分 + 邊界
        this.nodes.forEach(node => {
            if (node === this.draggingNode) return;    // 拖曳節點位置由滑鼠直設

            node.vx *= 0.85;    // 高阻尼求穩定
            node.vy *= 0.85;
            node.x += node.vx;
            node.y += node.vy;

            node.x = Math.max(BOUND_R, Math.min(w - BOUND_R, node.x));
            node.y = Math.max(BOUND_R, Math.min(h - BOUND_R, node.y));
        });
    }

    /* ── 繪製(CanvasChart 子類契約)── */

    draw(ctx, w, h) {
        this.width = w;
        this.height = h;
        const nodes = this.nodes || [];
        if (!nodes.length) return;
        const nodeRadius = this._nodeRadius();

        this._ensureControls();
        this._prepFrame();
        this._ensurePositions(w, h);

        const t = this.tokens([
            '--cl-text-placeholder', '--cl-bg', '--cl-primary',
            '--cl-bg-overlay-soft', '--cl-text-heading'
        ]);

        ctx.save();
        ctx.translate(this.viewport.x, this.viewport.y);
        ctx.scale(this.viewport.scale, this.viewport.scale);

        // 連線
        ctx.save();
        ctx.strokeStyle = t['--cl-text-placeholder'];
        ctx.lineWidth = 1.5;
        ctx.globalAlpha = 0.6;
        ctx.beginPath();
        for (const [s, tg] of this._linkRefs) {
            ctx.moveTo(s.x, s.y);
            ctx.lineTo(tg.x, tg.y);
        }
        ctx.stroke();
        ctx.restore();

        // 節點(投影模擬原版 drop-shadow)
        ctx.save();
        ctx.shadowColor = t['--cl-bg-overlay-soft'];
        ctx.shadowBlur = 2;
        ctx.shadowOffsetY = 1;
        nodes.forEach((node, i) => {
            ctx.beginPath();
            ctx.arc(node.x, node.y, nodeRadius, 0, Math.PI * 2);
            ctx.fillStyle = this._nodeColor(node, i);
            ctx.fill();
            ctx.lineWidth = 2;
            ctx.strokeStyle = node === this.draggingNode ? t['--cl-primary'] : t['--cl-bg'];
            ctx.stroke();
        });
        ctx.restore();

        // 節點標籤(碟面反白字,沿用原版 11px 粗體置中)
        ctx.font = this.font(Number(this.options.labelFontSize) || 11, 700);
        ctx.textAlign = 'center';
        ctx.textBaseline = 'middle';
        ctx.fillStyle = t['--cl-bg'];
        for (const node of nodes) {
            ctx.fillText(String(node.label || node.id || ''), node.x, node.y);
        }

        if (typeof this.options.getNodeBadges === 'function') {
            ctx.font = this.font(12);
            ctx.fillStyle = t['--cl-text-heading'];
            for (const node of nodes) {
                const badges = this.options.getNodeBadges(node);
                const label = Array.isArray(badges) ? badges.filter(Boolean).join('') : String(badges || '');
                if (label) ctx.fillText(label, node.x, node.y + nodeRadius + 10);
            }
        }

        ctx.font = this.font(8);
        ctx.fillStyle = t['--cl-text-heading'];
        for (const [source, target, link] of this._linkRefs) {
            const value = link?.value;
            if (value !== undefined && value !== null && value !== '') {
                ctx.fillText(String(value), (source.x + target.x) / 2, (source.y + target.y) / 2);
            }
        }

        ctx.restore();

        // 圖例
        if (this.options.showLegend) this._drawLegend(ctx, w, h, t);
    }

    _drawLegend(ctx, w, h, t) {
        const items = [...this._groupOrder.entries()]
            .map(([label, idx]) => ({ label: String(label), color: categoricalColor(idx) }));
        if (!items.length) return;

        const iconSize = 10, gap = 6, itemSpacing = 20;
        ctx.font = this.font(12);
        const widths = items.map(it => iconSize + gap + ctx.measureText(it.label).width);
        const total = widths.reduce((a, b) => a + b, 0) + itemSpacing * (items.length - 1);

        let x = Math.max(20, (w - total) / 2);
        const y = h - 15;
        ctx.textAlign = 'left';
        ctx.textBaseline = 'middle';
        items.forEach((it, i) => {
            ctx.fillStyle = it.color;
            ctx.fillRect(x, y - iconSize / 2 - 2, iconSize, iconSize);
            ctx.fillStyle = t['--cl-text-heading'];
            ctx.fillText(it.label, x + iconSize + gap, y);
            x += widths[i] + itemSpacing;
        });
    }

    /* ── 控制列(hover 詳情開關;DOM + cssText,沿用原版)── */

    _ensureControls() {
        if (this._controls || !this.nodes.length) return;

        const controls = document.createElement('div');
        controls.className = 'viz-controls';
        controls.style.cssText = `
            position: absolute; top: 10px; right: 10px;
            background: var(--cl-bg-surface-overlay); padding: 5px 10px;
            border-radius: var(--cl-radius-sm); box-shadow: var(--cl-shadow-sm);
            font-size: var(--cl-font-size-sm); display: flex; align-items: center; z-index: 10;
        `;

        const label = document.createElement('label');
        label.style.display = 'flex';
        label.style.alignItems = 'center';
        label.style.cursor = 'pointer';

        const checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.checked = true;    // 預設開啟
        checkbox.style.marginRight = '5px';
        checkbox.addEventListener('change', (e) => {
            this.detailsEnabled = e.target.checked;
        });

        label.appendChild(checkbox);
        label.appendChild(document.createTextNode(Locale.t('relationChart.hoverTooltip')));
        controls.appendChild(label);

        const makeButton = (text, title, action) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.textContent = text;
            button.title = title;
            button.setAttribute('aria-label', title);
            button.style.cssText = 'margin-left:6px;padding:2px 7px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);background:var(--cl-bg);color:var(--cl-text);cursor:pointer;';
            button.addEventListener('click', action);
            controls.appendChild(button);
        };
        makeButton('＋', '放大', () => this.zoomBy(1.2));
        makeButton('－', '縮小', () => this.zoomBy(1 / 1.2));
        makeButton('重設', '重設檢視', () => this.resetViewport());
        this._canvasWrap.appendChild(controls);

        this._controls = controls;
        this.detailsEnabled = true;
    }

    /* ── 命中(自管;圓形距離判定,由後往前=最上層優先)── */

    _pickNode(x, y) {
        const point = this._screenToWorld(x, y);
        const radius = this._nodeRadius();
        for (let i = this.nodes.length - 1; i >= 0; i--) {
            const n = this.nodes[i];
            if (n.x === undefined) continue;
            const dx = point.x - n.x, dy = point.y - n.y;
            if (dx * dx + dy * dy <= radius * radius) return n;
        }
        return null;
    }

    _hitTest() { return null; }    // 停用基底 region 機制(本件全自管)

    /* ── 互動:拖曳(釘住)+ hover 詳情(200ms 寬限可移入 tooltip)── */

    _bindGraphEvents() {
        const c = this.canvas;

        c.addEventListener('mousedown', (e) => {
            const node = this._pickNode(e.offsetX, e.offsetY);
            if (this.draggingNode || this.isPanning) return;
            e.preventDefault();
            this._pointerDown = { x: e.offsetX, y: e.offsetY, node };
            this._moved = false;
            if (node) {
                this.draggingNode = node;
                this.isDragging = true;
            } else {
                this.isPanning = true;
            }
            this._cancelHide();
            this._hideDetail();
            c.style.cursor = node ? 'move' : 'grabbing';

            if (node) {
                const point = this._screenToWorld(e.offsetX, e.offsetY);
                node.x = point.x;
                node.y = point.y;
            }
        });

        c.addEventListener('mousemove', (e) => {
            if (this._pointerDown) {
                const dx = e.offsetX - this._pointerDown.x;
                const dy = e.offsetY - this._pointerDown.y;
                if (dx * dx + dy * dy > 9) this._moved = true;
            }
            if (this.draggingNode) {
                e.preventDefault();
                const point = this._screenToWorld(e.offsetX, e.offsetY);
                this.draggingNode.x = point.x;
                this.draggingNode.y = point.y;
                return;
            }
            if (this.isPanning && this._pointerDown) {
                e.preventDefault();
                this.viewport.x += e.offsetX - this._pointerDown.x;
                this.viewport.y += e.offsetY - this._pointerDown.y;
                this._pointerDown.x = e.offsetX;
                this._pointerDown.y = e.offsetY;
                return;
            }
            const node = this._pickNode(e.offsetX, e.offsetY);
            if (node) {
                this._cancelHide();
                if (node !== this._hoverNode) {
                    this._hoverNode = node;
                    if (this.detailsEnabled && !this.isDragging) {
                        this._showNodeDetail(node, e.offsetX, e.offsetY);
                    }
                }
                c.style.cursor = 'pointer';
            } else {
                if (this._hoverNode) {
                    this._hoverNode = null;
                    this._scheduleHide();              // 寬限期:可移入 tooltip
                }
                c.style.cursor = 'default';
            }
        });

        c.addEventListener('mouseup', () => this._endPointer());
        c.addEventListener('mouseleave', () => {
            this._endPointer(true);
            if (this._hoverNode) {
                this._hoverNode = null;
                this._scheduleHide();
            }
        });
        c.addEventListener('wheel', (e) => {
            e.preventDefault();
            this.zoomBy(e.deltaY < 0 ? 1.12 : 1 / 1.12, e.offsetX, e.offsetY);
        }, { passive: false });
    }

    _bindTooltipHover() {
        // 移入互動 tooltip 時保持開啟;移出後再走寬限期關閉
        this._tooltip.addEventListener('mouseenter', () => this._cancelHide());
        this._tooltip.addEventListener('mouseleave', () => this._scheduleHide());
    }

    _endPointer(cancelClick = false) {
        const clickedNode = this.draggingNode;
        const shouldClick = !cancelClick && clickedNode && !this._moved;
        this.draggingNode = null;
        this.isDragging = false;
        this.isPanning = false;
        this._pointerDown = null;
        this.canvas.style.cursor = 'default';
        if (shouldClick) this.options.onNodeClick?.(clickedNode);
    }

    _screenToWorld(x, y) {
        return {
            x: (x - this.viewport.x) / this.viewport.scale,
            y: (y - this.viewport.y) / this.viewport.scale,
        };
    }

    _scheduleHide() {
        this._cancelHide();
        this._hideTimer = setTimeout(() => {
            this._hideTimer = null;
            this._hideDetail();
        }, 200);    // 200ms 寬限期(沿用原版)
    }

    _cancelHide() {
        if (this._hideTimer) {
            clearTimeout(this._hideTimer);
            this._hideTimer = null;
        }
    }

    _hideDetail() {
        this._tooltip.style.display = 'none';
        this._tooltip.style.pointerEvents = 'none';
    }

    /* ── 詳情卡(DOM + textContent + cssText;CSP 相容、免注入)── */

    _showNodeDetail(node, ex, ey) {
        const tip = this._tooltip;
        tip.textContent = '';

        const labelText = node.label ? String(node.label) : '';
        const groupText = node.group == null ? '' : String(node.group);
        const idText = node.id == null ? '' : String(node.id);
        const detail = this.options.getNodeTooltip?.(node);

        const wrap = document.createElement('div');
        wrap.className = 'rc-tip';
        wrap.style.cssText = 'min-width: 250px; max-width: 300px;';

        const heading = document.createElement('h3');
        heading.textContent = labelText;
        heading.style.cssText = 'margin:0 0 10px 0; border-bottom:1px solid var(--cl-border-light); padding-bottom:10px; font-size:var(--cl-font-size-xl);';
        wrap.appendChild(heading);

        const badge = document.createElement('span');
        badge.className = 'rc-tip-badge';
        badge.textContent = groupText;
        badge.style.cssText = 'display:inline-block; background:var(--cl-bg-info-light); color:var(--cl-primary-dark); padding:2px 8px; border-radius:var(--cl-radius-xl); font-size:var(--cl-font-size-sm); margin-bottom:15px';
        wrap.appendChild(badge);

        const info = document.createElement('div');
        info.className = 'rc-tip-info';
        info.style.cssText = 'background:var(--cl-bg); padding:12px; border-radius:var(--cl-radius-lg); font-size:var(--cl-font-size-md); line-height:1.5; color:var(--cl-text);';
        const row = (k, v) => {
            const p = document.createElement('p');
            p.style.cssText = 'margin:4px 0;';
            const strong = document.createElement('strong');
            strong.textContent = k + ':';
            p.appendChild(strong);
            p.appendChild(document.createTextNode(' ' + v));
            info.appendChild(p);
        };
        const rows = Array.isArray(detail)
            ? detail
            : (detail && typeof detail === 'object' ? Object.entries(detail) : [['ID', idText]]);
        rows.forEach(([key, value]) => row(String(key), value == null || value === '' ? '-' : String(value)));
        wrap.appendChild(info);

        tip.appendChild(wrap);
        tip.style.display = 'block';
        tip.style.pointerEvents = 'auto';    // 互動 tooltip(Copy ID 可點)
        this._positionTooltip(ex, ey);
    }

    /* ── 公開 API ── */

    /** 更新資料/選項後重繪(新節點自動確定性散佈並入模擬)。 */
    update(patch = {}) {
        if (patch.nodes) this.nodes = patch.nodes;
        if (patch.links) this.links = patch.links;
        super.update(patch);
    }

    zoomBy(factor, centerX = this.width / 2, centerY = this.height / 2) {
        const oldScale = this.viewport.scale;
        const nextScale = Math.max(0.25, Math.min(4, oldScale * Number(factor || 1)));
        const worldX = (centerX - this.viewport.x) / oldScale;
        const worldY = (centerY - this.viewport.y) / oldScale;
        this.viewport.scale = nextScale;
        this.viewport.x = centerX - worldX * nextScale;
        this.viewport.y = centerY - worldY * nextScale;
        return this;
    }

    resetViewport() {
        this.viewport = { x: 0, y: 0, scale: 1 };
        return this;
    }

    destroy() {
        if (this.simulation) cancelAnimationFrame(this.simulation);
        this.simulation = 0;
        this._cancelHide();
        super.destroy();    // 停基底 rAF / theme / resize,並移除 DOM(含控制列與 tooltip)
    }
}

export default RelationChart;
