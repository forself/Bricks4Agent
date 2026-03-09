/**
 * FocusPanel
 * 聚焦區塊 - 點擊後其他區塊禁用
 */

import { BasePanel } from './BasePanel.js';

export class FocusPanel extends BasePanel {
    constructor(options = {}) {
        super({
            focusMode: true,
            focusScope: 'siblings',
            showHeader: true,
            ...options
        });

        this._applyFocusStyle();
    }

    _applyFocusStyle() {
        this.element.style.cursor = 'pointer';

        // Hover 效果
        this.element.addEventListener('mouseenter', () => {
            if (!this.isFocused) {
                this.element.style.boxShadow = '0 0 0 2px rgba(var(--cl-primary-rgb), 0.2)';
            }
        });

        this.element.addEventListener('mouseleave', () => {
            if (!this.isFocused) {
                this.element.style.boxShadow = '';
            }
        });
    }

    /**
     * 點擊外部失焦
     */
    _bindEvents() {
        super._bindEvents();

        document.addEventListener('click', this._handleDocClick = (e) => {
            if (this.isFocused && !this.element.contains(e.target)) {
                this.blur();
            }
        });
    }

    destroy() {
        document.removeEventListener('click', this._handleDocClick);
        super.destroy();
    }
}

export default FocusPanel;
