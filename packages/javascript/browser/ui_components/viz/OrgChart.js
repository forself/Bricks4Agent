/**
 * OrgChart — 樹狀組織圖(CanvasChart 版;SVG 禁用政策下的重寫,公開 API 向後相容)。
 * 資料形狀:root = { id, title, label, children: [...] }
 * 互動(語意與舊版一致):
 *   - 點節點卡 → DOM 詳情卡 overlay(actionButton { label, onClick(node) } 可客製動作鈕)
 *   - 點卡底 +/- 圓鈕 → 收合/展開子樹並重繪
 * 佈局:原樹狀佈局演算法原樣保留(content 座標);繪製時等比縮放置中
 *       (等同舊 SVG viewBox「meet」行為,含 40px 邊距)。
 * 命中:draw 內以 addRegion 註冊卡片 rect 與 +/- 小 rect(data 帶 node 與 action),
 *       建構時掛內部 onPointClick 分派;使用者自帶的 onPointClick 照樣回拋。
 * 詳情卡:BaseChart 退場後,DOM overlay 程式碼(cssText + escapeHtml/textContent)
 *       內化於本類別;HierarchyChart 繼承即得。
 */
import { CanvasChart } from './CanvasChart.js';
import { ModalPanel } from '../layout/Panel/index.js';
import { nextUid } from '../utils/uid.js';

const px = (v, d) => typeof v === 'number' ? v + 'px' : (v || d);

export class OrgChart extends CanvasChart {
    constructor(options = {}) {
        super({
            ...options,
            width: px(options.width, '100%'),
            height: px(options.height, '300px')
        });
        this.root = options.root || null; // { id, label, title, children: [] }
        this.nodeWidth = 140;
        this.nodeHeight = 60;
        this.levelGap = 100;

        this.expandedNodes = new Set();
        if (this.root) this._expandAll(this.root);

        this.nodes = [];
        this.links = [];

        // 內建 click 分派:命中 region → 收合/詳情;使用者自帶 onPointClick 照樣收到回拋
        this._userPointClick = typeof this.options.onPointClick === 'function' ? this.options.onPointClick : null;
        this.options.onPointClick = (data, region) => {
            this._handleRegionClick(data);
            if (this._userPointClick) this._userPointClick(data, region);
        };
    }

    _expandAll(node) {
        this.expandedNodes.add(node.id);
        if (node.children) node.children.forEach(c => this._expandAll(c));
    }

    setData(root) {
        this.root = root;
        this.expandedNodes.clear();
        if (this.root) this.expandedNodes.add(this.root.id);
        this.render();
    }

    _computeLayout() {
        // Reset positions
        this.nodes = [];
        this.links = [];

        // Helper: Measure width of subtree
        const measure = (node) => {
            if (!this.expandedNodes.has(node.id) && node !== this.root) return 0; // Collapsed/Hidden

            // If leaf or collapsed children
            if (!node.children || node.children.length === 0 || !this.expandedNodes.has(node.id)) {
                return this.nodeWidth;
            }

            let w = 0;
            node.children.forEach(c => {
                w += measure(c);
            });
            w += (node.children.length - 1) * 20; // 20px gap
            return Math.max(this.nodeWidth, w);
        };

        // Recursive Layout
        let maxDepth = 0;

        const layout = (node, x, depth) => {
            maxDepth = Math.max(maxDepth, depth);
            const mySubtreeWidth = measure(node);

            // My X is center of my allotted space
            const myX = x + mySubtreeWidth / 2;

            const n = {
                ...node,
                x: myX,
                y: depth * 150 + 50, // Top margin
                w: this.nodeWidth,
                h: this.nodeHeight
            };
            this.nodes.push(n);

            if (node.children && this.expandedNodes.has(node.id)) {
                // measure() returns max(nodeWidth, childrenTotalWidth),
                // so "x" spans the full width; children block centered within it.
                let childrenTotalWidth = 0;
                node.children.forEach(c => childrenTotalWidth += measure(c));
                childrenTotalWidth += (node.children.length - 1) * 20;

                let startX = x + (mySubtreeWidth - childrenTotalWidth) / 2;

                let currentCx = startX;
                node.children.forEach(c => {
                    const cw = measure(c);
                    const childNode = layout(c, currentCx, depth + 1);
                    this.links.push({ source: n, target: childNode });
                    currentCx += cw + 20;
                });
            }
            return n;
        };

        const totalWidth = measure(this.root);
        layout(this.root, 0, 0);

        this.contentWidth = totalWidth;
        this.contentHeight = (maxDepth + 1) * 150;
    }

    /* ── Canvas 渲染(佈局 content 座標 → 等比縮放置中;命中區以螢幕座標註冊)── */

    draw(ctx, w, h) {
        if (!this.root) return;
        this._computeLayout();
        if (!this.nodes.length) return;

        const t = this.tokens([
            '--cl-bg', '--cl-border', '--cl-border-medium', '--cl-text',
            '--cl-text-secondary', '--cl-text-muted', '--cl-bg-overlay-soft'
        ]);

        // 視圖適配:內容界限 + 40px 邊距,等比縮放置中(等同舊 viewBox meet)
        const pad = 40;
        let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity;
        this.nodes.forEach(n => {
            minX = Math.min(minX, n.x - n.w / 2);
            maxX = Math.max(maxX, n.x + n.w / 2);
            minY = Math.min(minY, n.y - n.h / 2);
            maxY = Math.max(maxY, n.y + n.h / 2);
        });
        minX -= pad; maxX += pad; minY -= pad; maxY += pad;
        const vw = Math.max(1, maxX - minX);
        const vh = Math.max(1, maxY - minY);
        const k = Math.min(w / vw, h / vh);
        const ox = (w - vw * k) / 2 - minX * k;
        const oy = (h - vh * k) / 2 - minY * k;
        const sx = (x) => ox + x * k;   // content → 螢幕(CSS px;hit region 用)
        const sy = (y) => oy + y * k;

        ctx.save();
        ctx.translate(ox, oy);
        ctx.scale(k, k);

        // 1) 連接線(直角折線:下緣 → 中線 → 子卡上緣)
        ctx.strokeStyle = t['--cl-border-medium'];
        ctx.lineWidth = 2;
        this.links.forEach(l => {
            const midY = (l.source.y + this.nodeHeight / 2 + l.target.y - this.nodeHeight / 2) / 2;
            ctx.beginPath();
            ctx.moveTo(l.source.x, l.source.y + this.nodeHeight / 2);
            ctx.lineTo(l.source.x, midY);
            ctx.lineTo(l.target.x, midY);
            ctx.lineTo(l.target.x, l.target.y - this.nodeHeight / 2);
            ctx.stroke();
        });

        // 2) 節點卡 + +/- 收合鈕
        this.nodes.forEach(n => {
            const isExpanded = this.expandedNodes.has(n.id);
            const hasChildren = !!(n.children && n.children.length > 0);
            const x0 = n.x - n.w / 2;
            const y0 = n.y - n.h / 2;

            // 卡片本體(圓角 6;柔和投影近似舊 drop-shadow(0 1px 3px))
            ctx.save();
            if (t['--cl-bg-overlay-soft']) {
                ctx.shadowColor = t['--cl-bg-overlay-soft'];
                ctx.shadowBlur = 3;
                ctx.shadowOffsetY = 1;
            }
            ctx.fillStyle = t['--cl-bg'];
            this._roundRectPath(ctx, x0, y0, n.w, n.h, 6);
            ctx.fill();
            ctx.restore();
            ctx.strokeStyle = t['--cl-border'];
            ctx.lineWidth = 2;
            this._roundRectPath(ctx, x0, y0, n.w, n.h, 6);
            ctx.stroke();

            // 文字(與舊版同基線:title 於卡頂 +20、label +40;超寬截斷)
            ctx.textAlign = 'center';
            ctx.textBaseline = 'alphabetic';
            ctx.fillStyle = t['--cl-text'];
            ctx.font = this.font(14, 700);
            ctx.fillText(this.ellipsis(ctx, String(n.title ?? ''), n.w - 12), n.x, y0 + 20);
            ctx.fillStyle = t['--cl-text-secondary'];
            ctx.font = this.font(12);
            ctx.fillText(this.ellipsis(ctx, String(n.label ?? ''), n.w - 12), n.x, y0 + 40);

            // 卡片命中區(先卡片後 +/-;基底 hit-test 由後往前,重疊時 +/- 優先)
            this.addRegion({
                shape: 'rect', x: sx(x0), y: sy(y0), w: n.w * k, h: n.h * k,
                data: { action: 'node', id: n.id, node: n }
            });

            if (hasChildren) {
                const bx = n.x;
                const by = n.y + n.h / 2;   // 卡底邊中點(同舊版 toggle 位置)
                const r = 10;
                ctx.fillStyle = t['--cl-bg'];
                ctx.beginPath();
                ctx.arc(bx, by, r, 0, Math.PI * 2);
                ctx.fill();
                ctx.strokeStyle = t['--cl-text-muted'];
                ctx.lineWidth = 1;
                ctx.stroke();
                // +/- 記號以線段繪製(展開=−、收合=+;免字型基線誤差)
                ctx.strokeStyle = t['--cl-text-secondary'];
                ctx.lineWidth = 1.5;
                ctx.beginPath();
                ctx.moveTo(bx - 4.5, by);
                ctx.lineTo(bx + 4.5, by);
                if (!isExpanded) {
                    ctx.moveTo(bx, by - 4.5);
                    ctx.lineTo(bx, by + 4.5);
                }
                ctx.stroke();

                const hr = 12 * k;   // 命中小 rect(比視覺圓稍大,好點按)
                this.addRegion({
                    shape: 'rect', x: sx(bx) - hr, y: sy(by) - hr, w: hr * 2, h: hr * 2,
                    data: { action: 'toggle', id: n.id, node: n }
                });
            }
        });

        ctx.restore();
    }

    /** 圓角矩形路徑(ctx.roundRect 有就用,否則 arcTo 退化)。 */
    _roundRectPath(ctx, x, y, w, h, r) {
        ctx.beginPath();
        if (typeof ctx.roundRect === 'function') {
            ctx.roundRect(x, y, w, h, r);
            return;
        }
        const rr = Math.min(r, w / 2, h / 2);
        ctx.moveTo(x + rr, y);
        ctx.arcTo(x + w, y, x + w, y + h, rr);
        ctx.arcTo(x + w, y + h, x, y + h, rr);
        ctx.arcTo(x, y + h, x, y, rr);
        ctx.arcTo(x, y, x + w, y, rr);
        ctx.closePath();
    }

    /* ── 互動(region 分派;語意同舊 DOM 版)── */

    _handleRegionClick(data) {
        if (!data || !data.action) return;
        if (data.action === 'toggle') this._handleToggleClick(data.id);
        else if (data.action === 'node') this._handleNodeClick(data.id);
    }

    _handleToggleClick(id) {
        if (this.expandedNodes.has(id)) {
            this.expandedNodes.delete(id);
        } else {
            this.expandedNodes.add(id);
        }
        this.render();
    }

    _handleNodeClick(id) {
        const node = this._findNode(this.root, id);
        if (node) this._showNodeDetail(node);
    }

    _showNodeDetail(node) {
        const btnId = nextUid('org-action-btn');
        // Default or Custom Action
        const action = this.options.actionButton || {
            label: 'Send Email',
            onClick: (n) => ModalPanel.alert({ message: `Sending email to ${n.title}...` })
        };

        const safeTitle = this.escapeHtml(node.title);
        const safeLabel = this.escapeHtml(node.label);
        const safeId = this.escapeHtml(node.id);

        this.showDetailCard(`
            <h3 class="org-detail-title">${safeTitle}</h3>
            <span class="org-detail-badge">${safeLabel}</span>
            <div class="org-detail-info">
                <p><strong>Employee ID:</strong> ${safeId}</p>
                <p><strong>Department:</strong> ${safeLabel || 'Engineering'}</p>
                <p><strong>Email:</strong> ${safeId.toLowerCase() || 'user'}@example.com</p>
                <p><strong>Phone:</strong> +886 912-345-678</p>
                <p><strong>Location:</strong> Taipei HQ, 4F</p>
            </div>
            <div class="org-detail-actions">
                 <button id="${btnId}">${this.escapeHtml(action.label)}</button>
            </div>
        `, `Details - ${safeTitle}`);

        // CSP 相容:card 插入 DOM 後以 CSSOM(el.style.cssText)指派樣式,
        // 因 style-src 'self' 會剝掉 innerHTML 剖析出的 style 屬性
        const body = this._getDetailCardBody();
        if (body) {
            const title = body.querySelector('.org-detail-title');
            if (title) title.style.cssText = 'margin:0 0 10px 0; border-bottom:1px solid var(--cl-border-light); padding-bottom:10px';
            const badge = body.querySelector('.org-detail-badge');
            if (badge) badge.style.cssText = 'display:inline-block; background:var(--cl-bg-info-light); color:var(--cl-primary-dark); padding:2px 8px; border-radius:var(--cl-radius-xl); font-size:var(--cl-font-size-sm); margin-bottom:15px';
            const info = body.querySelector('.org-detail-info');
            if (info) info.style.cssText = 'background:var(--cl-bg); padding:15px; border-radius:var(--cl-radius-lg); font-size:var(--cl-font-size-lg); line-height:1.6';
            const actions = body.querySelector('.org-detail-actions');
            if (actions) actions.style.cssText = 'margin-top:20px; text-align:right';
            const actionBtn = document.getElementById(btnId);
            if (actionBtn) actionBtn.style.cssText = 'padding:8px 16px; background:var(--cl-primary); color:var(--cl-text-inverse); border:none; border-radius:var(--cl-radius-sm); cursor:pointer';
        }

        // Attach Event Listener
        // Note: The card is in the DOM now.
        setTimeout(() => {
            const btn = document.getElementById(btnId);
            if (btn) {
                btn.onclick = () => action.onClick(node);
            }
        }, 0);
    }

    _findNode(node, id) {
        if (node.id === id) return node;
        if (node.children) {
            for (let c of node.children) {
                const found = this._findNode(c, id);
                if (found) return found;
            }
        }
        return null;
    }

    /* ── DOM 詳情卡 overlay(原 BaseChart helper 內化;Canvas 基底不提供)── */

    escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    showDetailCard(htmlContent, title = 'Detail') {
        // Create a NEW overlay for stacking
        const card = document.createElement('div');
        card.className = 'viz-detail-overlay';

        // Calculate z-index based on existing overlays to stack properly
        const existingOverlays = document.querySelectorAll('.viz-detail-overlay');
        const zIndex = 10000 + (existingOverlays.length * 10);

        card.style.cssText = `
            position: fixed; top: 0; left: 0; width: 100%; height: 100%;
            background: var(--cl-bg-overlay); z-index: ${zIndex};
            display: flex; justify-content: center; align-items: center;
            opacity: 0; transition: opacity var(--cl-transition);
        `;

        const content = document.createElement('div');
        content.className = 'viz-card-content';
        content.style.cssText = `
            background: var(--cl-bg); width: 600px; max-width: 90%; max-height: 90vh;
            border-radius: var(--cl-radius-xl); padding: 24px; box-shadow: var(--cl-shadow-lg);
            overflow-y: auto; transform: scale(0.95); transition: transform var(--cl-transition);
            position: relative;
        `;

        const closeBtn = document.createElement('button');
        closeBtn.textContent = '×';
        closeBtn.style.cssText = `
            position: absolute; top: 15px; right: 15px; font-size: var(--cl-font-size-3xl);
            background: none; border: none; cursor: pointer; color: var(--cl-text-placeholder);
        `;

        const body = document.createElement('div');
        body.innerHTML = htmlContent;

        content.appendChild(closeBtn);
        content.appendChild(body);
        card.appendChild(content);
        document.body.appendChild(card);

        // Close Logic (specific to this card instance)
        const close = () => {
            card.style.opacity = '0';
            content.style.transform = 'scale(0.95)';
            setTimeout(() => {
                if (card.parentNode) card.remove();
            }, 200);
        };

        closeBtn.onclick = close;
        card.addEventListener('click', (e) => {
            if (e.target === card) close();
        });

        // Animate In
        // Force reflow to ensure the transition works
        // eslint-disable-next-line no-unused-expressions
        card.offsetHeight;
        card.style.opacity = '1';
        content.style.transform = 'scale(1)';
    }

    hideDetailCard() {
        // Find the top-most overlay (last added)
        const overlays = document.querySelectorAll('.viz-detail-overlay');
        if (overlays.length > 0) {
            const card = overlays[overlays.length - 1];
            // Trigger the same close animation
            const content = card.querySelector('.viz-card-content');
            card.style.opacity = '0';
            if (content) content.style.transform = 'scale(0.95)';
            setTimeout(() => {
                if (card.parentNode) card.remove();
            }, 200);
        }
    }

    // 取最上層(最新開啟)detail card 的內容容器,供渲染後以 CSSOM 指派樣式
    _getDetailCardBody() {
        const overlays = document.querySelectorAll('.viz-detail-overlay');
        if (!overlays.length) return null;
        return overlays[overlays.length - 1].querySelector('.viz-card-content');
    }
}
