import { BaseChart } from './BaseChart.js';

import { ModalPanel } from '../layout/Panel/index.js';

export class TimelineChart extends BaseChart {
    constructor(options) {
        super(options);
        this.data = options.data || []; // Array of { id, group, start, end, label, details }
        this.resizeObserver = new ResizeObserver(() => this.render());
        this.resizeObserver.observe(this.container);
    }

    render() {
        this.svg.innerHTML = '';
        this.width = this.container.clientWidth;
        this.height = this.container.clientHeight;

        if (!this.data.length || this.width === 0 || this.height === 0) return;

        // 1. Process Data
        // Get unique groups (swimlanes)
        const groups = [...new Set(this.data.map(d => d.group))];
        const marginTop = 40;
        const marginBottom = 30;
        const marginLeft = 100; // Space for labels
        const marginRight = 20;

        const laneHeight = (this.height - marginTop - marginBottom) / groups.length;

        // Time scale
        const times = this.data.map(d => [d.start, d.end]).flat();
        const minTime = Math.min(...times);
        const maxTime = Math.max(...times);
        const timeRange = maxTime - minTime;

        const xScale = (t) => {
            return marginLeft + ((t - minTime) / timeRange) * (this.width - marginLeft - marginRight);
        };

        // Draw Swimlanes (Groups)
        groups.forEach((group, i) => {
            const y = marginTop + i * laneHeight;

            // Lane separator
            const line = this.createSVGElement('line');
            line.setAttribute('x1', 0);
            line.setAttribute('y1', y);
            line.setAttribute('x2', this.width);
            line.setAttribute('y2', y);
            line.setAttribute('stroke', 'var(--cl-border-light)');
            this.svg.appendChild(line);

            // Label
            const text = this.createSVGElement('text');
            text.setAttribute('x', 10);
            text.setAttribute('y', y + laneHeight / 2);
            text.setAttribute('dominant-baseline', 'middle');
            text.setAttribute('font-size', '12');
            text.setAttribute('font-weight', 'bold');
            text.setAttribute('fill', 'var(--cl-text)');
            text.textContent = group;
            this.svg.appendChild(text);
        });

        // Draw Events
        this.data.forEach((d, i) => {
            const groupIndex = groups.indexOf(d.group);
            const x = xScale(d.start);
            const w = Math.max(2, xScale(d.end) - x); // Min width 2px
            const y = marginTop + groupIndex * laneHeight + (laneHeight - 20) / 2;
            const h = 20;

            const g = this.createSVGElement('g');
            g.setAttribute('class', 'timeline-event');
            g.style.cursor = 'pointer';

            const rect = this.createSVGElement('rect');
            rect.setAttribute('x', x);
            rect.setAttribute('y', y);
            rect.setAttribute('width', w);
            rect.setAttribute('height', h);
            rect.setAttribute('rx', 4);
            rect.setAttribute('fill', this.getColor(groupIndex));
            rect.setAttribute('opacity', '0.8');

            // Add hover effect via CSS or JS? JS for simplicity

            g.appendChild(rect);
            this.svg.appendChild(g);

            // Interaction
            this._bindHover(g, d);
        });

        // Time Axis (Simple)
        const axisY = this.height - 10;
        const timeSteps = 5;
        for (let i = 0; i <= timeSteps; i++) {
            const t = minTime + (timeRange / timeSteps) * i;
            const x = xScale(t);
            const date = new Date(t).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

            const text = this.createSVGElement('text');
            text.setAttribute('x', x);
            text.setAttribute('y', axisY);
            text.setAttribute('text-anchor', 'middle');
            text.setAttribute('font-size', '10');
            text.setAttribute('fill', 'var(--cl-text-muted)');
            text.textContent = date;
            this.svg.appendChild(text);
        }
    }

    _bindHover(el, data) {
        let hideTimer = null;

        const show = (e) => {
            if (hideTimer) clearTimeout(hideTimer);
            this._showEventDetail(data, e);

            // Sticky logic
            const tip = this.tooltip;
            tip.onmouseenter = () => {
                if (hideTimer) clearTimeout(hideTimer);
            };
            tip.onmouseleave = () => {
                hideTimer = setTimeout(() => this.hideTooltip(), 200);
            };
        };

        const hide = () => {
            hideTimer = setTimeout(() => this.hideTooltip(), 200);
        };

        el.addEventListener('mouseenter', show);
        el.addEventListener('mouseleave', hide);
    }

    _showEventDetail(data, e) {
        const duration = ((data.end - data.start) / 1000).toFixed(1) + 's';
        const safeLabel = this.escapeHtml(data.label);
        const safeDetails = this.escapeHtml(data.details || 'No additional details.');
        const safeId = this.escapeHtml(data.id);

        const html = `
            <div data-part="root">
                <h3 data-part="title">${safeLabel}</h3>
                <div data-part="body">
                   <div><strong>Start:</strong> ${new Date(data.start).toLocaleTimeString()}</div>
                   <div><strong>End:</strong> ${new Date(data.end).toLocaleTimeString()}</div>
                   <div><strong>Duration:</strong> ${duration}</div>
                   <div data-part="details">
                        ${safeDetails}
                   </div>
                </div>
                <div data-part="actions">
                    <button data-action="analyze" data-event-id="${safeId}">Analyze</button>
                </div>
            </div>
        `;
        this.showTooltip(html, e, true);

        // CSP 相容:style-src 'self' 會剝掉 HTML 剖析出的 style 屬性,
        // 故於 tooltip 插入 DOM 後以 CSSOM(el.style.cssText)指派樣式
        const setStyle = (selector, cssText) => {
            const el = this.tooltip.querySelector(selector);
            if (el) el.style.cssText = cssText;
        };
        setStyle('[data-part="root"]', 'min-width:200px;');
        setStyle('[data-part="title"]', 'margin:0 0 8px 0; border-bottom:1px solid var(--cl-border-light); padding-bottom:8px; font-size:var(--cl-font-size-lg); color:var(--cl-text-dark);');
        setStyle('[data-part="body"]', 'font-size:var(--cl-font-size-sm); color:var(--cl-text-heading); line-height:1.6;');
        setStyle('[data-part="details"]', 'margin-top:8px; padding:6px; background:var(--cl-bg-secondary); border-radius:var(--cl-radius-sm);');
        setStyle('[data-part="actions"]', 'margin-top:8px; text-align:right;');

        // CSP 相容:tooltip 插入 DOM 後以 CSSOM 設定樣式,並用 addEventListener 綁定行為
        // ModalPanel 已於檔案頂部 import { ModalPanel } from '../layout/Panel/index.js'
        const btn = this.tooltip.querySelector('[data-action="analyze"]');
        if (btn) {
            btn.style.cssText = 'padding:2px 8px; font-size:var(--cl-font-size-xs); cursor:pointer;';
            btn.addEventListener('click', () => {
                ModalPanel.alert({ message: 'Drilldown: ' + btn.dataset.eventId });
            });
        }
    }
}
