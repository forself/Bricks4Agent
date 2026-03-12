import { BaseChart } from './BaseChart.js';
import Locale from '../i18n/index.js';

export class RelationChart extends BaseChart {
    constructor(options) {
        super(options);
        this.nodes = options.nodes || [];
        this.links = options.links || [];
        // Physics params
        this.simulation = null;
        this.width = 0;
        this.height = 0;

        // Interaction State
        this.draggingNode = null;
        this.isDragging = false;
        this._dragBind = false;
        this.detailsEnabled = true;
    }

    render() {
        if (!this.nodes.length) return;

        // Reset if resizing
        this.svg.innerHTML = '';

        // 1. Controls (Toggle for Details)
        let controls = this.container.querySelector('.viz-controls');
        if (!controls) {
            controls = document.createElement('div');
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
            checkbox.checked = true; // Default enable
            checkbox.style.marginRight = '5px';
            checkbox.onchange = (e) => {
                this.detailsEnabled = e.target.checked;
            };

            label.appendChild(checkbox);
            label.appendChild(document.createTextNode(Locale.t('relationChart.hoverTooltip')));
            controls.appendChild(label);
            this.container.appendChild(controls);

            this.detailsEnabled = true; // Sync state
        }

        // Setup groups
        this.linkGroup = this.createSVGElement('g');
        this.nodeGroup = this.createSVGElement('g');
        this.svg.appendChild(this.linkGroup);
        this.svg.appendChild(this.nodeGroup);

        // Center force
        const cx = this.width / 2;
        const cy = this.height / 2;

        // Initialize positions if not present
        this.nodes.forEach(node => {
            if (node.x === undefined) {
                node.x = cx + (Math.random() - 0.5) * 50;
                node.y = cy + (Math.random() - 0.5) * 50;
            }
            node.vx = 0;
            node.vy = 0;
        });

        // Start simulation loop
        if (this.simulation) cancelAnimationFrame(this.simulation);
        this._runSimulation();

        // Draw Legend if needed
        if (this.options.showLegend) {
            const groups = [...new Set(this.nodes.map(n => n.group))];
            const legendItems = groups.map((g, i) => ({
                label: g,
                color: this.getColor(i)
            }));
            this._drawLegend(legendItems);
        }
    }

    _runSimulation() {
        const tick = () => {
            const k = 0.05; // speed factor
            // Stronger Repulsion for spacing
            const repulsion = 1000;
            const linkDist = 120;
            const centerStrength = 0.005; // Weaker center pull to allow spreading
            const cx = this.width / 2;
            const cy = this.height / 2;

            // Forces
            this.nodes.forEach(node => {
                // If dragging, we set position manually in drag handler, but here we can dampen velocity?
                if (node === this.draggingNode) return;

                // Center Gravity
                node.vx += (cx - node.x) * centerStrength;
                node.vy += (cy - node.y) * centerStrength;

                // Repulsion
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

            // Link Attraction
            this.links.forEach(link => {
                const source = this.nodes.find(n => n.id === link.source);
                const target = this.nodes.find(n => n.id === link.target);
                if (!source || !target) return;

                const dx = target.x - source.x;
                const dy = target.y - source.y;
                const dist = Math.sqrt(dx * dx + dy * dy);
                const force = (dist - linkDist) * 0.05;

                const fx = (dx / dist) * force;
                const fy = (dy / dist) * force;

                if (source !== this.draggingNode) {
                    source.vx += fx;
                    source.vy += fy;
                }
                if (target !== this.draggingNode) {
                    target.vx -= fx;
                    target.vy -= fy;
                }
            });

            // Update Positions
            this.nodes.forEach(node => {
                if (node === this.draggingNode) return; // Skip update for dragged node

                node.vx *= 0.85; // Higher friction for stability
                node.vy *= 0.85;
                node.x += node.vx;
                node.y += node.vy;

                // Bounds
                const r = 20;
                node.x = Math.max(r, Math.min(this.width - r, node.x));
                node.y = Math.max(r, Math.min(this.height - r, node.y));
            });

            this._draw();
            this.simulation = requestAnimationFrame(tick);
        };

        this.simulation = requestAnimationFrame(tick);
    }

    _draw() {
        // Redraw content
        // Links
        let linkHtml = '';
        this.links.forEach(link => {
            const source = this.nodes.find(n => n.id === link.source);
            const target = this.nodes.find(n => n.id === link.target);
            if (!source || !target) return;
            linkHtml += `<line x1="${source.x}" y1="${source.y}" x2="${target.x}" y2="${target.y}" stroke="var(--cl-text-placeholder)" stroke-width="1.5" opacity="0.6"/>`;
        });
        this.linkGroup.innerHTML = linkHtml;

        // Nodes
        let nodeHtml = '';
        this.nodes.forEach((node, i) => {
            const color = this.getColor(node.group === undefined ? i : (typeof node.group === 'string' ? node.group.length : node.group));

            nodeHtml += `
                <g transform="translate(${node.x}, ${node.y})" style="cursor: pointer" class="node-group" data-id="${node.id}">
                    <circle r="25" fill="${color}" stroke="var(--cl-bg)" stroke-width="2" style="filter: drop-shadow(0 1px 2px var(--cl-bg-overlay-soft))"/>
                    <text dy="0.35em" text-anchor="middle" fill="var(--cl-bg)" font-size="11" font-weight="bold" pointer-events="none">${node.label || node.id}</text>
                </g>
            `;
        });
        this.nodeGroup.innerHTML = nodeHtml;

        // Re-bind events
        // Re-bind events
        this.nodeGroup.querySelectorAll('.node-group').forEach(el => {
            let hideTimer = null;

            const show = (e) => {
                if (!this.detailsEnabled || this.isDragging) return;
                if (hideTimer) clearTimeout(hideTimer);

                const id = el.getAttribute('data-id');
                const node = this.nodes.find(n => n.id === id);
                this._showNodeDetail(node, e);

                // Add mouseenter to tooltip itself to keep it open
                const tip = this.tooltip;
                tip.onmouseenter = () => {
                    if (hideTimer) clearTimeout(hideTimer);
                };
                tip.onmouseleave = () => {
                    hideTimer = setTimeout(() => this.hideTooltip(), 200);
                };
            };

            const hide = () => {
                // Delay hide to allow moving to tooltip
                hideTimer = setTimeout(() => {
                    this.hideTooltip();
                }, 200); // 200ms grace period
            };

            el.addEventListener('mouseenter', show);
            el.addEventListener('mouseleave', hide);

            // Drag Start
            el.addEventListener('mousedown', (e) => this._onDragStart(e, el));
        });

        // Bind global move/up for drag (Once)
        if (!this._dragBind) {
            this.svg.addEventListener('mousemove', (e) => this._onDragMove(e));
            this.svg.addEventListener('mouseup', (e) => this._onDragEnd(e));
            this.svg.addEventListener('mouseleave', (e) => this._onDragEnd(e));
            this._dragBind = true;
        }
    }

    _showNodeDetail(node, e) {
        // Content similar to OrgChart Detail Card
        const safeLabel = this.escapeHtml(node.label);
        const safeGroup = this.escapeHtml(node.group);
        const safeId = this.escapeHtml(node.id);

        const html = `
            <div style="min-width: 250px; max-width: 300px;">
                <h3 style="margin:0 0 10px 0; border-bottom:1px solid var(--cl-border-light); padding-bottom:10px; font-size:var(--cl-font-size-xl);">${safeLabel}</h3>
                <span style="display:inline-block; background:var(--cl-bg-info-light); color:var(--cl-primary-dark); padding:2px 8px; border-radius:var(--cl-radius-xl); font-size:var(--cl-font-size-sm); margin-bottom:15px">${safeGroup}</span>
                <div style="background:var(--cl-bg); padding:12px; border-radius:var(--cl-radius-lg); font-size:var(--cl-font-size-md); line-height:1.5; color:var(--cl-text);">
                    <p style="margin:4px 0;"><strong>ID:</strong> ${safeId}</p>
                    <p style="margin:4px 0;"><strong>Type:</strong> Entity Node</p>
                    <p style="margin:4px 0;"><strong>Status:</strong> Active</p>
                    <p style="margin:4px 0;"><strong>Description:</strong> Node representing ${safeLabel} in the network.</p>
                </div>
                <div style="margin-top:12px; text-align:right;">
                     <button style="padding:4px 10px; background:var(--cl-border-medium); color:var(--cl-text); border:none; border-radius:var(--cl-radius-sm); cursor:pointer; font-size:var(--cl-font-size-sm);" onclick="console.log('Action on ${safeId}')">Copy ID</button>
                </div>
            </div>
        `;

        // Use interactive tooltip
        this.showTooltip(html, e, true);
    }

    _onDragStart(e, el) {
        e.preventDefault(); // Prevent text selection
        if (this.draggingNode) return;

        const id = el.getAttribute('data-id');
        const node = this.nodes.find(n => n.id === id);
        if (node) {
            this.draggingNode = node;
            this.isDragging = true;
            this.hideTooltip(); // Hide tooltip when dragging starts

            // Visual feedback
            el.style.cursor = 'move';
            const circle = el.querySelector('circle');
            if (circle) circle.setAttribute('stroke', 'var(--cl-primary)');

            // Force position update immediately
            const rect = this.svg.getBoundingClientRect();
            node.x = e.clientX - rect.left;
            node.y = e.clientY - rect.top;
        }
        e.stopPropagation();
    }

    _onDragMove(e) {
        if (!this.draggingNode) return;
        e.preventDefault();

        const rect = this.svg.getBoundingClientRect();
        this.draggingNode.x = e.clientX - rect.left;
        this.draggingNode.y = e.clientY - rect.top;
    }

    _onDragEnd(e) {
        if (!this.draggingNode) return;

        // Reset visual
        const el = this.nodeGroup.querySelector(`[data-id="${this.draggingNode.id}"]`);
        if (el) {
            el.style.cursor = 'pointer';
            const circle = el.querySelector('circle');
            if (circle) circle.setAttribute('stroke', 'var(--cl-bg)');
        }

        this.draggingNode = null;
        this.isDragging = false;
    }

    destroy() {
        if (this.simulation) cancelAnimationFrame(this.simulation);
        if (this.resizeObserver) this.resizeObserver.disconnect();
    }
}
