/**
 * CollapsiblePanel
 * 可收折容器
 */

import { BasePanel } from './BasePanel.js';

export class CollapsiblePanel extends BasePanel {
    constructor(options = {}) {
        super({
            collapsible: true,
            showHeader: true,
            ...options
        });

        this._applyCollapsibleStyle();

        // 應用初始收折狀態
        if (!this.expanded) {
            this.content.style.display = 'none';
        }
    }

    _applyCollapsibleStyle() {
        if (this.header) {
            this.header.style.cursor = 'pointer';

            // 點擊標題列也可展開/收折
            this.header.addEventListener('click', (e) => {
                // 避免點擊關閉按鈕時觸發
                if (!e.target.closest('.panel__close')) {
                    this.toggle();
                }
            });
        }
    }
}

export default CollapsiblePanel;
