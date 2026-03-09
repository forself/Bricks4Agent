import { BaseChart } from './BaseChart.js';

import { ModalPanel } from '../layout/Panel/index.js';

export class SankeyChart extends BaseChart {
    constructor(options) {
        super(options);
        // data: { nodes: [{name}], links: [{source, target, value}] }
        this.data = options.data || { nodes: [], links: [] };
    }

    render() {
        this.svg.innerHTML = '';
        this.width = this.container.clientWidth;
        this.height = this.container.clientHeight;

        if (!this.data.nodes.length || !this.width) return;

        // Simple Layout Calculation (Column-based)
        // 1. Assign columns (X)
        const nodes = this.data.nodes.map((n, i) => ({ ...n, id: i, sourceLinks: [], targetLinks: [] }));
        const links = this.data.links.map(l => ({
            source: typeof l.source === 'object' ? l.source.id : l.source,
            target: typeof l.target === 'object' ? l.target.id : l.target,
            value: l.value
        }));

        // Build Graph
        links.forEach(l => {
            nodes[l.source].sourceLinks.push(l);
            nodes[l.target].targetLinks.push(l);
        });

        // Compute Depth (BFS)
        const assignDepth = () => {
            nodes.forEach(n => n.depth = 0);
            const queue = nodes.filter(n => n.targetLinks.length === 0); // Roots? Or just start from 0 and propagate
            // Actually, find nodes with no incoming links
            const roots = nodes.filter(n => !links.some(l => l.target === n.id));
            if (roots.length === 0 && nodes.length > 0) roots.push(nodes[0]); // Cycle fallback

            // For simplicity in this demo without D3-sankey:
            // We'll just define columns manually in data or simple greedy
            // Let's assume the user provides 'col' or we do simple layering
            if (nodes[0].col === undefined) {
                // Mock layering: 3 columns
                nodes.forEach((n, i) => n.col = i % 4);
            }
        };

        // If data has 'layer', use it. Else...
        // Let's rely on a computed layout for demonstration if not provided.
        // Quick topological sort-ish
        const visited = new Set();
        const computeCols = (node, col) => {
            if (visited.has(node.id)) return;
            visited.add(node.id);
            node.col = Math.max(node.col || 0, col);
            node.sourceLinks.forEach(l => {
                computeCols(nodes[l.target], col + 1);
            });
        };
        nodes.filter(n => n.targetLinks.length === 0).forEach(n => computeCols(n, 0));

        // 2. Assign Y positions
        const numCols = Math.max(...nodes.map(n => n.col)) + 1;
        const colWidth = (this.width - 100) / numCols; // 50px padding
        const nodeWidth = 20;

        // Group by column
        const columns = Array.from({ length: numCols }, () => []);
        nodes.forEach(n => columns[n.col].push(n));

        columns.forEach(colNodes => {
            const totalValue = colNodes.reduce((sum, n) => sum + Math.max(10, n.value || 10), 0); // Mock value
            const spacing = (this.height - totalValue) / (colNodes.length + 1);
            let currentY = spacing;
            colNodes.forEach(n => {
                n.x = 50 + n.col * colWidth;
                n.y = currentY;
                n.height = Math.max(20, (n.value || 20)); // Scale this properly in real app
                if (n.height > 100) n.height = 100; // Cap
                currentY += n.height + 20; // spacing
            });
        });

        // 3. Draw Links (Paths)
        links.forEach(l => {
            const source = nodes[l.source];
            const target = nodes[l.target];

            // Curvature
            const path = this.createSVGElement('path');
            const sx = source.x + nodeWidth;
            const sy = source.y + source.height / 2;
            const tx = target.x;
            const ty = target.y + target.height / 2;

            const d = `M ${sx} ${sy} C ${sx + colWidth / 2} ${sy}, ${tx - colWidth / 2} ${ty}, ${tx} ${ty}`;

            path.setAttribute('d', d);
            path.setAttribute('stroke', 'var(--cl-border-medium)'); // Slate 300
            path.setAttribute('stroke-width', Math.max(2, l.value / 2));
            path.setAttribute('fill', 'none');
            path.setAttribute('opacity', '0.5');
            this.svg.appendChild(path);

            // Hover link?
            path.onmouseenter = () => path.setAttribute('stroke', 'var(--cl-text-muted)');
            path.onmouseleave = () => path.setAttribute('stroke', 'var(--cl-border-medium)');
        });

        // 4. Draw Nodes
        nodes.forEach((n, i) => {
            const g = this.createSVGElement('g');
            g.style.cursor = 'pointer';

            const rect = this.createSVGElement('rect');
            rect.setAttribute('x', n.x);
            rect.setAttribute('y', n.y);
            rect.setAttribute('width', nodeWidth);
            rect.setAttribute('height', n.height);
            rect.setAttribute('rx', 2);
            rect.setAttribute('fill', this.getColor(n.col));
            g.appendChild(rect);

            const text = this.createSVGElement('text');
            text.setAttribute('x', n.x + nodeWidth + 5);
            text.setAttribute('y', n.y + n.height / 2);
            text.setAttribute('dominant-baseline', 'middle');
            text.setAttribute('font-size', '11');
            text.setAttribute('fill', 'var(--cl-text)');
            text.textContent = n.name;
            g.appendChild(text);

            this.svg.appendChild(g);
            this._bindHover(g, n);

            // Drag support could be added here similar to RelationChart
        });
    }

    _bindHover(el, data) {
        let hideTimer = null;
        const show = (e) => {
            if (hideTimer) clearTimeout(hideTimer);
            this._showDetail(data, e);
            const tip = this.tooltip;
            tip.onmouseenter = () => { if (hideTimer) clearTimeout(hideTimer); };
            tip.onmouseleave = () => { hideTimer = setTimeout(() => this.hideTooltip(), 200); };
        };
        const hide = () => { hideTimer = setTimeout(() => this.hideTooltip(), 200); };
        el.addEventListener('mouseenter', show);
        el.addEventListener('mouseleave', hide);
    }

    _showDetail(node, e) {
        const safeName = this.escapeHtml(node.name);
        const html = `
            <div style="min-width: 180px;">
                <h3 style="margin:0 0 5px 0; border-bottom:1px solid var(--cl-border-light); padding-bottom:5px;">${safeName}</h3>
                <div style="font-size:12px; color:var(--cl-text-secondary);">
                    Layer: ${node.col}<br/>
                    Flow Volume: ${node.value || 'N/A'}
                </div>
                 <div style="margin-top:8px; text-align:right;">
                    <button style="padding:2px 8px; font-size:11px;" onclick="ModalPanel.alert({ message: "Step Details: ${safeName}" })">View Step</button>
                </div>
            </div>
        `;
        this.showTooltip(html, e, true);
    }
}
