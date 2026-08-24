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
        const closeOnBackdrop = options.autoClose !== false;
        super({
            closable: true,
            autoClose: false,
            showHeader: true,
            visibility: BasePanel.VISIBILITY.NONE,
            position: DrawerPanel.POSITIONS.RIGHT,
            width: '320px',
            height: '100%',
            ...options,
            // Drawer owns its backdrop and therefore must not also register
            // BasePanel's document-wide outside-click listener: the opening
            // click would otherwise bubble to document and immediately close it.
            autoClose: false,
        });

        this._closeOnBackdrop = closeOnBackdrop;
        this._wrapWithBackdrop();
        this.element.setAttribute('role', 'dialog');
        this.element.setAttribute('aria-modal', 'true');
        this.element.setAttribute('tabindex', '-1');
        this._returnFocusTo = null;
        this._handleKeydown = event => {
            if (event.key === 'Escape' && this.options.visibility === BasePanel.VISIBILITY.VISIBLE) {
                event.preventDefault();
                this.close();
            }
        };
        document.addEventListener('keydown', this._handleKeydown);
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
        if (this._closeOnBackdrop) {
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
                this.element.style.display = '';
                this.element.style.visibility = 'visible';
                this.backdrop.style.visibility = 'visible';
                this.backdrop.style.opacity = '1';
                this.element.style.transform = positionStyles.visibleTransform;
                document.body.style.overflow = 'hidden';
                break;
            case BasePanel.VISIBILITY.HIDDEN:
                this.element.style.display = '';
                this.element.style.visibility = 'hidden';
                this.backdrop.style.visibility = 'hidden';
                this.backdrop.style.opacity = '0';
                this.element.style.transform = positionStyles.hiddenTransform;
                document.body.style.overflow = '';
                break;
            case BasePanel.VISIBILITY.NONE:
                this.element.style.display = 'none';
                this.element.style.visibility = 'hidden';
                this.backdrop.style.visibility = 'hidden';
                this.backdrop.style.opacity = '0';
                this.element.style.transform = positionStyles.hiddenTransform;
                document.body.style.overflow = '';
                break;
        }
    }

    open() {
        this._returnFocusTo = document.activeElement instanceof HTMLElement ? document.activeElement : null;
        this.setVisibility(BasePanel.VISIBILITY.VISIBLE);
        queueMicrotask(() => (this.element.querySelector('.panel__close') || this.element).focus?.());
        return this;
    }

    close() {
        super.close();
        const returnTarget = this._returnFocusTo;
        this._returnFocusTo = null;
        queueMicrotask(() => returnTarget?.isConnected && returnTarget.focus?.());
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
        document.removeEventListener('keydown', this._handleKeydown);
        document.body.style.overflow = '';

        if (this.backdrop?.parentNode) {
            this.backdrop.remove();
        }

        PanelManager.unregister(this);
    }
}

export default DrawerPanel;
