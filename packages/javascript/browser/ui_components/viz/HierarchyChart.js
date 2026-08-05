/**
 * HierarchyChart — 組織關聯階層圖(OrgChart 的 Canvas 版子類;SVG 禁用政策)。
 * 與 OrgChart 差異:卡片較大(160×70)、詳情卡內嵌該單位的巢狀 OrgChart
 * (OrgChart 已 Canvas 化,巢狀圖自然是 canvas)。
 * 樹狀佈局/收合/命中皆繼承 OrgChart;僅覆寫 _showNodeDetail。
 */
import { OrgChart } from './OrgChart.js';
import Locale from '../i18n/index.js';

export class HierarchyChart extends OrgChart {
    constructor(options) {
        super(options);
        this.nodeWidth = 160;
        this.nodeHeight = 70;
    }

    _showNodeDetail(node) {
        // Show Detail Card with Nested Org Chart
        // 1. Prepare container HTML
        const safeTitle = this.escapeHtml(node.title);
        const safeId = this.escapeHtml(node.id);

        const content = `
            <div class="hier-detail-header">
                <h3>${safeTitle} - 組織架構</h3>
                <span>單位 ID: ${safeId}</span>
            </div>
            <div class="hier-detail-note">
                此單位的下屬成員與職位結構
            </div>
            <div id="nested-org-chart-container"></div>
        `;

        // 2. Open Card with content
        this.showDetailCard(content, `${safeTitle} ${Locale.t('hierarchyChart.orgSuffix')}`);

        // CSP 相容:card 插入 DOM 後以 CSSOM(el.style.cssText)指派樣式,
        // 因 style-src 'self' 會剝掉 innerHTML 剖析出的 style 屬性
        const body = this._getDetailCardBody();
        if (body) {
            const header = body.querySelector('.hier-detail-header');
            if (header) header.style.cssText = 'display:flex; justify-content:space-between; align-items:center; border-bottom:1px solid var(--cl-border-light); padding-bottom:10px; margin-bottom:15px';
            const heading = body.querySelector('.hier-detail-header h3');
            if (heading) heading.style.cssText = 'margin:0';
            const idSpan = body.querySelector('.hier-detail-header span');
            if (idSpan) idSpan.style.cssText = 'font-size:var(--cl-font-size-sm); color:var(--cl-text-secondary)';
            const note = body.querySelector('.hier-detail-note');
            if (note) note.style.cssText = 'background:var(--cl-bg-info-light); padding:10px; border-radius:var(--cl-radius-md); margin-bottom:15px; font-size:var(--cl-font-size-md); color:var(--cl-primary-dark)';
            const nested = body.querySelector('#nested-org-chart-container');
            if (nested) nested.style.cssText = 'width:100%; height:400px; background:var(--cl-bg-input); border:1px solid var(--cl-border-medium); border-radius:var(--cl-radius-lg)';
        }

        // 3. Load the real node detail before instantiating the nested chart.
        requestAnimationFrame(async () => {
            const container = document.getElementById('nested-org-chart-container');
            if (container) {
                const loader = this.options?.loadNodeDetail;
                if (typeof loader !== 'function') {
                    container.dataset.dataError = 'missing-node-detail-loader';
                    container.textContent = '未設定組織明細資料來源。';
                    return;
                }
                try {
                    const root = await loader(node);
                    if (!root) throw new Error('組織明細沒有資料。');
                    this._nestedDetailChart?.destroy?.();
                    this._nestedDetailChart = new OrgChart({
                        container,
                        root,
                        width: '100%',
                        height: '100%'
                    });
                } catch (error) {
                    container.dataset.dataError = 'node-detail-load-failed';
                    container.textContent = error?.message || '組織明細載入失敗。';
                }
            }
        });
    }

    destroy() {
        this._nestedDetailChart?.destroy?.();
        this._nestedDetailChart = null;
        super.destroy?.();
    }
}
