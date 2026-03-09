import { BaseChart } from './BaseChart.js';

export class PieChart extends BaseChart {
    constructor(options) {
        super(options);
        // data format: [{ name: 'A', value: 30 }, { name: 'B', value: 70 }]
        this.data = options.data || [];
        this.innerRadiusRatio = options.donut ? 0.6 : 0;
    }

    render() {
        if (!this.width || !this.height) return;

        const centerX = this.width / 2;
        const centerY = this.height / 2;
        const radius = Math.min(centerX, centerY) * 0.8;
        const innerRadius = radius * this.innerRadiusRatio;

        const g = this.createSVGElement('g');
        g.setAttribute('transform', `translate(${centerX}, ${centerY})`);
        this.svg.appendChild(g);

        const total = this.data.reduce((sum, item) => sum + item.value, 0);
        let currentAngle = 0;

        this.data.forEach((item, index) => {
            const sliceAngle = (item.value / total) * 2 * Math.PI;
            const startAngle = currentAngle;
            const endAngle = currentAngle + sliceAngle;

            // Calculate Path
            const x1 = Math.sin(startAngle) * radius;
            const y1 = -Math.cos(startAngle) * radius;
            const x2 = Math.sin(endAngle) * radius;
            const y2 = -Math.cos(endAngle) * radius;

            // Inner circle points (for donut)
            const x3 = Math.sin(endAngle) * innerRadius;
            const y3 = -Math.cos(endAngle) * innerRadius;
            const x4 = Math.sin(startAngle) * innerRadius;
            const y4 = -Math.cos(startAngle) * innerRadius;

            const largeArc = sliceAngle > Math.PI ? 1 : 0;

            let d = `M ${x4} ${y4} L ${x1} ${y1} A ${radius} ${radius} 0 ${largeArc} 1 ${x2} ${y2} L ${x3} ${y3} A ${innerRadius} ${innerRadius} 0 ${largeArc} 0 ${x4} ${y4} Z`;

            if (innerRadius === 0) {
                d = `M 0 0 L ${x1} ${y1} A ${radius} ${radius} 0 ${largeArc} 1 ${x2} ${y2} Z`;
            }

            const path = this.createSVGElement('path');
            path.setAttribute('d', d);
            path.setAttribute('fill', this.getColor(index));
            path.setAttribute('stroke', 'var(--cl-bg)');
            path.setAttribute('stroke-width', '2');

            // Interaction
            path.onmouseenter = (e) => {
                path.setAttribute('opacity', 0.8);
                // Slight expand animation could go here
                const safeName = this.escapeHtml(item.name);
                this.showTooltip(`
                    <strong>${safeName}</strong><br/>
                    ${item.value} (${Math.round(item.value / total * 100)}%)
                `, e);
            };
            path.onmouseleave = () => {
                path.setAttribute('opacity', 1);
                this.hideTooltip();
            };

            g.appendChild(path);
            currentAngle += sliceAngle;
        });

        // Legend
        this._drawLegend(this.data.map((item, i) => ({
            label: item.name,
            color: this.getColor(i)
        })));
    }
}
