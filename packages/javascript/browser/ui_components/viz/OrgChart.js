import { BaseChart } from './BaseChart.js';

import { ModalPanel } from '../layout/Panel/index.js';

export class OrgChart extends BaseChart {
    constructor(options) {
        super(options);
        this.root = options.root || null; // { id, label, title, children: [] }
        this.nodeWidth = 140;
        this.nodeHeight = 60;
        this.levelGap = 100;

        this.expandedNodes = new Set();
        if (this.root) this._expandAll(this.root);

        this.nodes = [];
        this.links = [];
        this._handleToggleClick = this._handleToggleClick.bind(this);
        this._handleNodeClick = this._handleNodeClick.bind(this);
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

    render() {
        if (!this.root) return;
        this.svg.innerHTML = '';

        // Groups
        this.gLinks = this.createSVGElement('g');
        this.gNodes = this.createSVGElement('g');
        this.svg.appendChild(this.gLinks);
        this.svg.appendChild(this.gNodes);

        // 1. Compute Layout
        this._computeLayout();

        // 2. Draw Links
        this._drawLinks();

        // 3. Draw Nodes
        this._drawNodes();

        // 4. Center View
        this._centerView();
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
                // Center children block?
                // If children width < node width, we need to center them relative to me?
                // Current logic: Children fill width. If width > node width, I am centered.
                // If width < node width (e.g. 1 child), logic holds (child center = my center).

                // However, we need to handle "gap" balancing.
                // Simplest: Just stack left to right.

                // Correction: If children narrower than parent, we need to center the children block?
                // measure() returns max(nodeWidth, childrenTotalWidth).
                // So "x" spans the full width.

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

    _drawLinks() {
        let html = '';
        this.links.forEach(l => {
            const midY = (l.source.y + this.nodeHeight / 2 + l.target.y - this.nodeHeight / 2) / 2;
            const path = `M ${l.source.x} ${l.source.y + this.nodeHeight / 2}
                          V ${midY}
                          H ${l.target.x}
                          V ${l.target.y - this.nodeHeight / 2}`;

            html += `<path d="${path}" fill="none" stroke="var(--cl-border-medium)" stroke-width="2"/>`;
        });
        this.gLinks.innerHTML = html;
    }

    _drawNodes() {
        let html = '';
        this.nodes.forEach(n => {
            const isExpanded = this.expandedNodes.has(n.id);
            const hasChildren = n.children && n.children.length > 0;

            // Standard Style
            const fill = 'var(--cl-bg)';
            const stroke = 'var(--cl-border)';

            html += `
                <g transform="translate(${n.x - n.w / 2}, ${n.y - n.h / 2})" 
                   class="org-node" 
                   data-id="${n.id}" 
                   style="cursor: pointer">
                    
                    <!-- Card Body Group -->
                    <g class="card-body">
                        <rect width="${n.w}" height="${n.h}" rx="6" fill="${fill}" stroke="${stroke}" stroke-width="2" 
                              filter="drop-shadow(0 2px 2px rgba(0,0,0,0.05))"/>
                        <text x="${n.w / 2}" y="20" text-anchor="middle" font-weight="bold" font-size="14" fill="var(--cl-text)" pointer-events="none">${n.title}</text>
                        <text x="${n.w / 2}" y="40" text-anchor="middle" font-size="12" fill="var(--cl-text-secondary)" pointer-events="none">${n.label}</text>
                    </g>
                    
                    <!-- Expander Button (Separate from Card Body) -->
                    ${hasChildren ? `
                        <g class="toggle-btn" transform="translate(${n.w / 2}, ${n.h})">
                            <circle cx="0" cy="0" r="10" fill="var(--cl-bg)" stroke="var(--cl-text-muted)" stroke-width="1"/>
                            <text x="0" y="4" text-anchor="middle" font-size="12" fill="var(--cl-text-secondary)" pointer-events="none">${isExpanded ? '-' : '+'}</text>
                        </g>
                    ` : ''}
                </g>
            `;
        });
        this.gNodes.innerHTML = html;

        // Bind Events
        this.gNodes.querySelectorAll('.org-node').forEach(el => {
            const toggleBtn = el.querySelector('.toggle-btn');
            const cardBody = el.querySelector('.card-body');

            if (toggleBtn) {
                toggleBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    this._handleToggleClick(e, el);
                });
            }

            if (cardBody) {
                cardBody.addEventListener('click', (e) => {
                    e.stopPropagation();
                    this._handleNodeClick(e, el);
                });
            }
        });
    }

    _handleToggleClick(e, el) {
        const id = el.dataset.id;
        if (this.expandedNodes.has(id)) {
            this.expandedNodes.delete(id);
        } else {
            this.expandedNodes.add(id);
        }
        this.render();
    }

    _handleNodeClick(e, el) {
        const id = el.dataset.id;
        const node = this._findNode(this.root, id);
        this._showNodeDetail(node);
    }

    _showNodeDetail(node) {
        const btnId = `org-action-btn-${Date.now()}`;
        // Default or Custom Action
        const action = this.options.actionButton || {
            label: 'Send Email',
            onClick: (n) => ModalPanel.alert({ message: `Sending email to ${n.title}...` })
        };

        const safeTitle = this.escapeHtml(node.title);
        const safeLabel = this.escapeHtml(node.label);
        const safeId = this.escapeHtml(node.id);

        this.showDetailCard(`
            <h3 style="margin:0 0 10px 0; border-bottom:1px solid var(--cl-border-light); padding-bottom:10px">${safeTitle}</h3>
            <span style="display:inline-block; background:var(--cl-bg-info-light); color:var(--cl-primary-dark); padding:2px 8px; border-radius:12px; font-size:12px; margin-bottom:15px">${safeLabel}</span>
            <div style="background:var(--cl-bg); padding:15px; border-radius:8px; font-size:14px; line-height:1.6">
                <p><strong>Employee ID:</strong> ${safeId}</p>
                <p><strong>Department:</strong> ${safeLabel || 'Engineering'}</p>
                <p><strong>Email:</strong> ${safeId.toLowerCase() || 'user'}@example.com</p>
                <p><strong>Phone:</strong> +886 912-345-678</p>
                <p><strong>Location:</strong> Taipei HQ, 4F</p>
            </div>
            <div style="margin-top:20px; text-align:right">
                 <button id="${btnId}" style="padding:8px 16px; background:var(--cl-primary); color:white; border:none; border-radius:4px; cursor:pointer">${this.escapeHtml(action.label)}</button>
            </div>
        `, `Details - ${safeTitle}`);

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

    _centerView() {
        if (!this.nodes.length) return;
        const padding = 40;
        const minX = Math.min(...this.nodes.map(n => n.x - n.w / 2)) - padding;
        const maxX = Math.max(...this.nodes.map(n => n.x + n.w / 2)) + padding;
        const minY = Math.min(...this.nodes.map(n => n.y - n.h / 2)) - padding;
        const maxY = Math.max(...this.nodes.map(n => n.y + n.h / 2)) + padding;
        const w = maxX - minX;
        const h = maxY - minY;
        this.svg.setAttribute('viewBox', `${minX} ${minY} ${w} ${h}`);
    }
}
