/**
 * BasicPanel
 * 基本容器 - 最簡單的容器，無標題列
 */

import { BasePanel } from './BasePanel.js';

export class BasicPanel extends BasePanel {
    constructor(options = {}) {
        super({
            showHeader: false,
            ...options
        });

        this._applyBasicStyle();
    }

    _applyBasicStyle() {
        this.element.style.border = '1px solid var(--cl-border-light)';
    }
}

export default BasicPanel;
