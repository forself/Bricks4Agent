import { BaseChart } from './BaseChart.js';

import { ModalPanel } from '../layout/Panel/index.js';

export class SunburstChart extends BaseChart {
    constructor(options) {
        super(options);
        // data: { name, children: [] }
        this.data = options.data || {};
    }

    render() {
        this.svg.innerHTML = '';
        this.width = this.container.clientWidth;
        this.height = this.container.clientHeight;
        if (!this.data.name) return;

        const radius = Math.min(this.width, this.height) / 2;
        const cx = this.width / 2;
        const cy = this.height / 2;

        const g = this.createSVGElement('g');
        g.setAttribute('transform', `translate(${cx}, ${cy})`);
        this.svg.appendChild(g);

        // Partition Layout Calculation
        // Simple radial partition
        const hierarchy = this._partition(this.data);

        // Draw Arcs
        hierarchy.forEach((node, i) => {
            const arcPath = this.createSVGElement('path');
            const d = this._describeArc(0, 0, node.r0 * radius, node.r1 * radius, node.a0 * 2 * Math.PI, node.a1 * 2 * Math.PI);

            arcPath.setAttribute('d', d);
            arcPath.setAttribute('fill', this.getColor(node.depth));
            arcPath.setAttribute('stroke', 'var(--cl-bg)');
            arcPath.setAttribute('stroke-width', '1');
            g.appendChild(arcPath);

            this._bindHover(arcPath, node);
        });
    }

    // Quick partition calc (0..1 space)
    _partition(root) {
        const nodes = [];

        // Add value to root
        const addValues = (node) => {
            if (!node.children || node.children.length === 0) {
                node.value = node.value || 1;
            } else {
                node.value = node.children.reduce((acc, c) => acc + addValues(c), 0);
            }
            return node.value;
        };
        addValues(root);

        const traverse = (node, depth, a0, a1) => {
            const r0 = depth * 0.25; // Ring width factor
            const r1 = (depth + 1) * 0.25;

            nodes.push({ ...node, depth, r0, r1, a0, a1 });

            if (node.children) {
                let currentA = a0;
                node.children.forEach(child => {
                    const angleSpan = (a1 - a0) * (child.value / node.value);
                    traverse(child, depth + 1, currentA, currentA + angleSpan);
                    currentA += angleSpan;
                });
            }
        };

        traverse(root, 0, 0, 1);
        return nodes;
    }

    // Polar to Cartesian
    _polarToCartesian(centerX, centerY, radius, angleInRadians) {
        return {
            x: centerX + (radius * Math.cos(angleInRadians - Math.PI / 2)),
            y: centerY + (radius * Math.sin(angleInRadians - Math.PI / 2))
        };
    }

    _describeArc(x, y, innerRadius, outerRadius, startAngle, endAngle) {
        const start = this._polarToCartesian(x, y, outerRadius, endAngle);
        const end = this._polarToCartesian(x, y, outerRadius, startAngle);
        const startInner = this._polarToCartesian(x, y, innerRadius, endAngle);
        const endInner = this._polarToCartesian(x, y, innerRadius, startAngle);

        const largeArcFlag = endAngle - startAngle <= Math.PI ? "0" : "1";

        const d = [
            "M", start.x, start.y,
            "A", outerRadius, outerRadius, 0, largeArcFlag, 0, end.x, end.y,
            "L", endInner.x, endInner.y,
            "A", innerRadius, innerRadius, 0, largeArcFlag, 1, startInner.x, startInner.y,
            "Z"
        ].join(" ");

        return d;
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
                    Count: ${node.value}<br/>
                    Depth: ${node.depth}
                </div>
                 <div style="margin-top:8px; text-align:right;">
                    <button style="padding:2px 8px; font-size:11px;" onclick="ModalPanel.alert({ message: "Drilldown: ${safeName}" })">Zoom In</button>
                </div>
            </div>
        `;
        this.showTooltip(html, e, true);
    }
}
