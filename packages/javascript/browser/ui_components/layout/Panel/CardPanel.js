/**
 * CardPanel
 * 卡片容器 - 帶陰影的基本容器
 */

import { BasePanel } from './BasePanel.js';

export class CardPanel extends BasePanel {
    constructor(options = {}) {
        super({
            showHeader: true,
            ...options
        });

        this._applyCardStyle();
    }

    _applyCardStyle() {
        this.element.style.cssText += `
            box-shadow: 0 2px 8px rgba(0, 0, 0, 0.1);
            border: none;
        `;

        if (this.header) {
            this.header.style.background = 'var(--cl-bg)';
            this.header.style.borderBottom = '1px solid var(--cl-border-light)';
        }
    }
}

export default CardPanel;
