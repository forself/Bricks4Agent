import { OrgChart } from './OrgChart.js';
import Locale from '../i18n/index.js';

export class HierarchyChart extends OrgChart {
    constructor(options) {
        super(options);
        this.nodeWidth = 160;
        this.nodeHeight = 70;
    }

    _drawNodes() {
        super._drawNodes();

        // Add specific class for easy styling if needed
        this.gNodes.classList.add('hierarchy-nodes');
    }

    _showNodeDetail(node) {
        // Show Detail Card with Nested Org Chart
        // 1. Prepare container HTML
        const safeTitle = this.escapeHtml(node.title);
        const safeId = this.escapeHtml(node.id);

        const content = `
            <div style="display:flex; justify-content:space-between; align-items:center; border-bottom:1px solid var(--cl-border-light); padding-bottom:10px; margin-bottom:15px">
                <h3 style="margin:0">${safeTitle} - 組織架構</h3>
                <span style="font-size:12px; color:var(--cl-text-secondary)">單位 ID: ${safeId}</span>
            </div>
            <div style="background:var(--cl-bg-info-light); padding:10px; border-radius:6px; margin-bottom:15px; font-size:13px; color:var(--cl-primary-dark)">
                此單位的下屬成員與職位結構
            </div>
            <div id="nested-org-chart-container" style="width:100%; height:400px; background:var(--cl-bg-input); border:1px solid var(--cl-border-medium); border-radius:8px"></div>
        `;

        // 2. Open Card with content
        this.showDetailCard(content, `${safeTitle} ${Locale.t('hierarchyChart.orgSuffix')}`);

        // 3. Instantiate Org Chart inside the container
        // Need requestAnimationFrame to ensure DOM is ready
        requestAnimationFrame(() => {
            const container = document.getElementById('nested-org-chart-container');
            if (container) {
                // Mock Data for the Unit
                // In real app, fetch by node.id
                const mockMemberData = {
                    id: 'M1', label: '部門經理', title: 'Manager One',
                    children: [
                        { id: 'S1', label: '資深專員', title: 'Senior A' },
                        {
                            id: 'S2', label: '資深專員', title: 'Senior B',
                            children: [
                                { id: 'J1', label: '助理', title: 'Junior X' }
                            ]
                        }
                    ]
                };

                new OrgChart({
                    container: container,
                    root: mockMemberData,
                    width: '100%',
                    height: '100%'
                });
            }
        });
    }
}
