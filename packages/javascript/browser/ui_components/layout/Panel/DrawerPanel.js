/**
 * DrawerPanel
 * 抽屜面板 - 從側邊滑入/滑出
 */

import { BasePanel } from './BasePanel.js';
import { PanelManager } from './PanelManager.js';

export class DrawerPanel extends BasePanel {
    static POSITIONS = {
        LEFT: 'left',
        RIGHT: 'right',
        TOP: 'top',
        BOTTOM: 'bottom'
    };

    constructor(options = {}) {
        super({
            closable: true,
            autoClose: true,
            showHeader: true,
            visibility: BasePanel.VISIBILITY.NONE,
            position: DrawerPanel.POSITIONS.RIGHT,
            width: '320px',
            height: '100%',
            ...options
        });

        this._wrapWithBackdrop();
    }

    _wrapWithBackdrop() {
        const { position, width, height } = this.options;

        // 遮罩
        this.backdrop = document.createElement('div');
        this.backdrop.className = 'drawer-backdrop';
        this.backdrop.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: var(--cl-bg-overlay-soft);
            z-index: ${PanelManager.calculateZIndex(this)};
            opacity: 0;
            visibility: hidden;
            transition: all var(--cl-transition-slow);
        `;

        // 位置樣式
        const positionStyles = this._getPositionStyles();

        this.element.style.cssText += `
            position: fixed;
            ${positionStyles.position}
            width: ${position === 'left' || position === 'right' ? width : '100%'};
            height: ${position === 'top' || position === 'bottom' ? height : '100%'};
            max-height: 100vh;
            overflow: auto;
            border-radius: 0;
            transform: ${positionStyles.hiddenTransform};
            transition: transform var(--cl-transition-slow);
            z-index: ${PanelManager.calculateZIndex(this) + 1};
        `;

        this.backdrop.appendChild(this.element);

        // 點擊遮罩關閉
        if (this.options.autoClose) {
            this.backdrop.addEventListener('click', (e) => {
                if (e.target === this.backdrop) {
                    this.close();
                }
            });
        }
    }

    _getPositionStyles() {
        const { position } = this.options;

        switch (position) {
            case 'left':
                return {
                    position: 'top: 0; left: 0; bottom: 0;',
                    hiddenTransform: 'translateX(-100%)',
                    visibleTransform: 'translateX(0)'
                };
            case 'top':
                return {
                    position: 'top: 0; left: 0; right: 0;',
                    hiddenTransform: 'translateY(-100%)',
                    visibleTransform: 'translateY(0)'
                };
            case 'bottom':
                return {
                    position: 'bottom: 0; left: 0; right: 0;',
                    hiddenTransform: 'translateY(100%)',
                    visibleTransform: 'translateY(0)'
                };
            case 'right':
            default:
                return {
                    position: 'top: 0; right: 0; bottom: 0;',
                    hiddenTransform: 'translateX(100%)',
                    visibleTransform: 'translateX(0)'
                };
        }
    }

    _applyVisibility() {
        const { visibility } = this.options;
        const positionStyles = this._getPositionStyles();

        if (!this.backdrop) {
            super._applyVisibility();
            return;
        }

        switch (visibility) {
            case BasePanel.VISIBILITY.VISIBLE:
                this.backdrop.style.visibility = 'visible';
                this.backdrop.style.opacity = '1';
                this.element.style.transform = positionStyles.visibleTransform;
                document.body.style.overflow = 'hidden';
                break;
            case BasePanel.VISIBILITY.HIDDEN:
            case BasePanel.VISIBILITY.NONE:
                this.backdrop.style.visibility = 'hidden';
                this.backdrop.style.opacity = '0';
                this.element.style.transform = positionStyles.hiddenTransform;
                document.body.style.overflow = '';
                break;
        }
    }

    open() {
        this.setVisibility(BasePanel.VISIBILITY.VISIBLE);
        return this;
    }

    close() {
        super.close();
        return this;
    }

    mount(container = document.body) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.backdrop);
        return this;
    }

    destroy() {
        document.body.style.overflow = '';

        if (this.backdrop?.parentNode) {
            this.backdrop.remove();
        }

        PanelManager.unregister(this);
    }
}

export default DrawerPanel;
