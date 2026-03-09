
export class BaseChart {
    constructor(options = {}) {
        this.options = {
            container: '',
            width: '100%',
            height: '300px',
            padding: { top: 20, right: 20, bottom: 60, left: 40 },
            colors: [
                'var(--cl-primary)', 'var(--cl-success)', 'var(--cl-warning)', 'var(--cl-danger)',
                'var(--cl-purple)', 'var(--cl-pink)', 'var(--cl-cyan)', 'var(--cl-lime)'
            ],
            title: '',
            ...options
        };

        this.container = typeof this.options.container === 'string'
            ? document.querySelector(this.options.container)
            : this.options.container;

        if (!this.container) {
            console.error('Chart container not found');
            return;
        }

        this.svg = null;
        this.width = 0;
        this.height = 0;

        // Tooltip element (singleton per page or one per chart)
        this.tooltip = this._createTooltip();

        this._init();

        // Resize observer
        this.resizeObserver = new ResizeObserver(() => this.resize());
        this.resizeObserver.observe(this.container);

        // Delay first resize/render
        requestAnimationFrame(() => this.resize());
    }

    _init() {
        // Set style
        this.container.style.position = 'relative';
        this.container.style.width = this.options.width;
        this.container.style.height = this.options.height;
        this.container.style.display = 'flex';
        this.container.style.justifyContent = 'center';
        this.container.style.alignItems = 'center';
        this.container.style.background = 'var(--cl-bg)';
        this.container.style.borderRadius = '8px';
        this.container.style.boxShadow = '0 2px 4px rgba(0,0,0,0.05)';

        // Create SVG
        const ns = 'http://www.w3.org/2000/svg';
        this.svg = document.createElementNS(ns, 'svg');
        this.svg.style.width = '100%';
        this.svg.style.height = '100%';
        this.svg.style.overflow = 'visible';

        this.container.appendChild(this.svg);
    }

    _createTooltip() {
        let tip = document.getElementById('viz-tooltip');
        if (!tip) {
            tip = document.createElement('div');
            tip.id = 'viz-tooltip';
            tip.style.cssText = `
                position: absolute;
                display: none;
                padding: 8px 12px;
                background: rgba(255, 255, 255, 0.95);
                backdrop-filter: blur(4px);
                border: 1px solid var(--cl-border-light);
                border-radius: 6px;
                box-shadow: 0 4px 12px rgba(0,0,0,0.1);
                font-family: -apple-system, sans-serif;
                font-size: 13px;
                color: var(--cl-text);
                pointer-events: none;
                z-index: 9999;
                transition: opacity 0.1s;
            `;
            document.body.appendChild(tip);
        }
        return tip;
    }

    resize() {
        const rect = this.container.getBoundingClientRect();
        this.width = rect.width;
        this.height = rect.height;

        if (this.width > 0 && this.height > 0) {
            this.svg.innerHTML = '';
            this.render();
        }
    }

    // Abstract methods
    render() {
        console.warn('render() method should be implemented by subclass');
    }

    // Utilities
    getColor(index) {
        return this.options.colors[index % this.options.colors.length];
    }

    showTooltip(html, e, isInteractive = false) {
        const tip = this.tooltip;
        tip.innerHTML = html;
        tip.style.display = 'block';

        // Position logic (using viewport coordinates for clamping, then adding scroll)
        const scrollX = window.pageXOffset || document.documentElement.scrollLeft;
        const scrollY = window.pageYOffset || document.documentElement.scrollTop;

        // Default target position in viewport
        let vx = e.clientX + 10;
        let vy = e.clientY + 10;

        // Ensure within viewport
        const rect = tip.getBoundingClientRect();
        const winW = window.innerWidth;
        const winH = window.innerHeight;

        // Clamp to viewport
        if (vx + rect.width > winW) vx = winW - rect.width - 20;
        if (vy + rect.height > winH) vy = winH - rect.height - 20;

        // Convert key coordinates back to document for absolute positioning
        tip.style.left = (vx + scrollX) + 'px';
        tip.style.top = (vy + scrollY) + 'px';

        if (isInteractive) {
            tip.style.pointerEvents = 'auto';

            // Handle sticky logic:
            // If interactive, we assume the caller manages hide, OR we implement
            // a global mousemove to detect if we left both target and tooltip.
            // For simplicity here, we clear any auto-hide timer if exists.
        } else {
            tip.style.pointerEvents = 'none';
        }
    }

    hideTooltip() {
        this.tooltip.style.display = 'none';
    }

    escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&var(--cl-primary-dark);');
    }

    createSVGElement(type) {
        return document.createElementNS('http://www.w3.org/2000/svg', type);
    }

    _drawLegend(items) {
        // items: [{ label, color }]
        const legendG = this.createSVGElement('g');
        const itemSpacing = 20;
        const iconSize = 10;
        let currentX = 0;

        items.forEach((item, i) => {
            const g = this.createSVGElement('g');
            const rect = this.createSVGElement('rect');
            rect.setAttribute('x', 0);
            rect.setAttribute('y', -iconSize / 2 - 2);
            rect.setAttribute('width', iconSize);
            rect.setAttribute('height', iconSize);
            rect.setAttribute('rx', 2);
            rect.setAttribute('fill', item.color);
            g.appendChild(rect);

            const text = this.createSVGElement('text');
            text.setAttribute('x', iconSize + 6);
            text.setAttribute('y', 0);
            text.setAttribute('font-size', '12');
            text.setAttribute('fill', 'var(--cl-text-heading)');
            text.setAttribute('dominant-baseline', 'middle');
            text.textContent = item.label;
            g.appendChild(text);

            g.setAttribute('transform', `translate(${currentX}, 0)`);
            legendG.appendChild(g);

            const textLength = item.label.length * 14;
            currentX += iconSize + 6 + textLength + itemSpacing;
        });

        const totalLegendWidth = currentX - itemSpacing;
        const startX = (this.width - totalLegendWidth) / 2;
        const startY = this.height - 15;

        legendG.setAttribute('transform', `translate(${Math.max(20, startX)}, ${startY})`);
        this.svg.appendChild(legendG);
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
            background: rgba(0,0,0,0.5); z-index: ${zIndex};
            display: flex; justify-content: center; align-items: center;
            opacity: 0; transition: opacity 0.2s;
        `;

        const content = document.createElement('div');
        content.className = 'viz-card-content';
        content.style.cssText = `
            background: var(--cl-bg); width: 600px; max-width: 90%; max-height: 90vh;
            border-radius: 12px; padding: 24px; box-shadow: 0 10px 25px rgba(0,0,0,0.2);
            overflow-y: auto; transform: scale(0.95); transition: transform 0.2s;
            position: relative;
        `;

        const closeBtn = document.createElement('button');
        closeBtn.innerHTML = '×';
        closeBtn.style.cssText = `
            position: absolute; top: 15px; right: 15px; font-size: 24px;
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
}
