import { BaseChart } from './BaseChart.js';

import { ModalPanel } from '../layout/Panel/index.js';

export class FlameChart extends BaseChart {
    constructor(options) {
        super(options);
        // data: hierarchical object { name, value, children: [] }
        this.data = options.data || {};
    }

    render() {
        this.svg.innerHTML = '';
        this.width = this.container.clientWidth;
        this.height = this.container.clientHeight;
        if (!this.data.name) return;

        // Flatten data into "flame" bars
        // Root is at bottom (Icicle) or top (Flame). Flame usually grows up.
        // Let's do "Icicle" style (Root at top) as it's easier to read for timelines sometimes, OR Flame (Root bottom).
        // Standard Flame Graph: Root at bottom.
        // Let's do Root at Bottom to match "Flame".

        const nodes = [];
        const levels = {};

        // 1. Calculate Positions recursively
        // We need total value to normalize width
        const totalValue = this.data.value || 100;

        const traverse = (node, level, x, width) => {
            nodes.push({ ...node, level, x, width });

            if (!levels[level]) levels[level] = [];
            levels[level].push(node);

            if (node.children) {
                let currentX = x;
                node.children.forEach(child => {
                    const childWidth = (child.value / totalValue) * this.width;
                    traverse(child, level + 1, currentX, childWidth);
                    currentX += childWidth;
                });
            }
        };

        traverse(this.data, 0, 0, this.width);

        const maxLevel = Math.max(...nodes.map(n => n.level));
        const barHeight = 25;
        const startY = this.height - 30; // Start from bottom

        nodes.forEach(n => {
            const y = startY - (n.level * (barHeight + 2));

            const g = this.createSVGElement('g');
            g.style.cursor = 'pointer';

            const rect = this.createSVGElement('rect');
            rect.setAttribute('x', n.x);
            rect.setAttribute('y', y);
            rect.setAttribute('width', Math.max(0, n.width - 1)); // -1 for gap
            rect.setAttribute('height', barHeight);
            rect.setAttribute('fill', this.getHeatColor(n.level));
            rect.setAttribute('rx', 2);
            g.appendChild(rect);

            if (n.width > 30) {
                const text = this.createSVGElement('text');
                text.setAttribute('x', n.x + 2);
                text.setAttribute('y', y + barHeight / 2);
                text.setAttribute('dominant-baseline', 'middle');
                text.setAttribute('font-size', '10');
                text.setAttribute('fill', 'white');
                text.style.pointerEvents = 'none';

                // Truncate text
                const chars = Math.floor(n.width / 7);
                text.textContent = n.name.length > chars ? n.name.substring(0, chars) + '..' : n.name;

                g.appendChild(text);
            }

            this.svg.appendChild(g);
            this._bindHover(g, n);

            // Click to zoom could be added later
        });
    }

    getHeatColor(level) {
        const hue = Math.max(12, 48 - level * 6);
        const lightness = Math.max(42, 62 - level * 3);
        return `hsl(${hue} 100% ${lightness}%)`;
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
            <div style="min-width: 200px;">
                <h3 style="margin:0 0 5px 0; border-bottom:1px solid var(--cl-border-light); padding-bottom:5px;">${safeName}</h3>
                <div style="font-size:var(--cl-font-size-sm); color:var(--cl-text-secondary);">
                    <strong>Value:</strong> ${node.value} ms<br/>
                    <strong>% of Total:</strong> ${((node.width / this.width) * 100).toFixed(1)}%
                </div>
                 <div style="margin-top:8px; text-align:right;">
                    <button style="padding:2px 8px; font-size:var(--cl-font-size-xs);" onclick="ModalPanel.alert({ message: "Stack Trace: ${safeName}" })">View Stack</button>
                </div>
            </div>
        `;
        this.showTooltip(html, e, true);
    }
}
