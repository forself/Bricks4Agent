/**
 * LoadingSpinner - 載入指示器元件
 *
 * 提供多種載入動畫樣式，支援全螢幕遮罩和行內模式
 *
 * @author MAGI System
 * @version 1.0.0
 */

import { escapeHtml } from '../../utils/security.js';

import Locale from '../../i18n/index.js';
/** 驗證 CSS 色值（防止 CSS 注入） */
const isSafeColor = (color) => {
    if (typeof color !== 'string') return false;
    // 允許 hex、rgb/rgba/hsl/hsla 函式、CSS 具名色
    return /^(#[0-9a-fA-F]{3,8}|(?:rgb|rgba|hsl|hsla)\([^()]*\)|[a-zA-Z]{1,30})$/.test(color.trim());
};

export class LoadingSpinner {
    static VARIANTS = {
        SPINNER: 'spinner',      // 旋轉圓圈
        DOTS: 'dots',            // 跳動圓點
        PULSE: 'pulse',          // 脈衝效果
        BAR: 'bar'               // 進度條
    };

    static SIZES = {
        SMALL: 'small',
        MEDIUM: 'medium',
        LARGE: 'large'
    };

    /**
     * @param {Object} options
     * @param {string} options.variant - 樣式類型
     * @param {string} options.size - 尺寸
     * @param {string} options.color - 主色彩
     * @param {string} options.text - 載入文字
     * @param {boolean} options.overlay - 是否顯示遮罩
     * @param {boolean} options.visible - 初始可見狀態
     */
    constructor(options = {}) {
        this.options = {
            variant: LoadingSpinner.VARIANTS.SPINNER,
            size: LoadingSpinner.SIZES.MEDIUM,
            color: 'var(--cl-primary)',
            text: '',
            overlay: false,
            visible: true,
            zIndex: 9999,
            ...options
        };

        this.element = null;
        this._injectStyles();
        this._create();
    }

    _injectStyles() {
        if (document.getElementById('loading-spinner-styles')) return;

        const style = document.createElement('style');
        style.id = 'loading-spinner-styles';
        style.textContent = `
            @keyframes ls-spin {
                0% { transform: rotate(0deg); }
                100% { transform: rotate(360deg); }
            }
            @keyframes ls-dots {
                0%, 80%, 100% { transform: scale(0); opacity: 0.5; }
                40% { transform: scale(1); opacity: 1; }
            }
            @keyframes ls-pulse {
                0% { transform: scale(0.8); opacity: 0.5; }
                50% { transform: scale(1); opacity: 1; }
                100% { transform: scale(0.8); opacity: 0.5; }
            }
            @keyframes ls-bar {
                0% { width: 0%; }
                50% { width: 70%; }
                100% { width: 100%; }
            }
            .ls-overlay {
                position: fixed;
                top: 0;
                left: 0;
                right: 0;
                bottom: 0;
                background: rgba(255, 255, 255, 0.85);
                display: flex;
                align-items: center;
                justify-content: center;
                flex-direction: column;
                gap: 12px;
            }
            .ls-inline {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                flex-direction: column;
                gap: 8px;
            }
            .ls-hidden { display: none !important; }
        `;
        document.head.appendChild(style);
    }

    _getSizeValue() {
        const sizes = {
            small: { spinner: 24, dot: 6, bar: 100 },
            medium: { spinner: 40, dot: 10, bar: 200 },
            large: { spinner: 60, dot: 14, bar: 300 }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _create() {
        const { variant, text, overlay, visible, zIndex } = this.options;
        const color = isSafeColor(this.options.color) ? this.options.color : 'var(--cl-primary)';
        const safeZIndex = Number.isFinite(Number(zIndex)) ? Number(zIndex) : 9999;
        const size = this._getSizeValue();

        const container = document.createElement('div');
        container.className = overlay ? 'ls-overlay' : 'ls-inline';
        if (overlay) container.style.zIndex = safeZIndex;
        if (!visible) container.classList.add('ls-hidden');

        let spinnerEl;

        switch (variant) {
            case LoadingSpinner.VARIANTS.DOTS:
                spinnerEl = this._createDots(size.dot, color);
                break;
            case LoadingSpinner.VARIANTS.PULSE:
                spinnerEl = this._createPulse(size.spinner, color);
                break;
            case LoadingSpinner.VARIANTS.BAR:
                spinnerEl = this._createBar(size.bar, color);
                break;
            default:
                spinnerEl = this._createSpinner(size.spinner, color);
        }

        container.appendChild(spinnerEl);

        if (text) {
            const textEl = document.createElement('span');
            textEl.className = 'ls-text';
            textEl.textContent = text;
            textEl.style.cssText = `
                font-size: 14px;
                color: var(--cl-text-secondary);
                margin-top: 8px;
            `;
            container.appendChild(textEl);
        }

        this.element = container;
    }

    _createSpinner(size, color) {
        const spinner = document.createElement('div');
        spinner.className = 'ls-spinner';
        spinner.style.cssText = `
            width: ${size}px;
            height: ${size}px;
            border: ${Math.max(2, size / 10)}px solid var(--cl-border-light);
            border-top-color: ${color};
            border-radius: 50%;
            animation: ls-spin 0.8s linear infinite;
        `;
        return spinner;
    }

    _createDots(dotSize, color) {
        const container = document.createElement('div');
        container.style.cssText = `
            display: flex;
            gap: ${dotSize / 2}px;
        `;

        for (let i = 0; i < 3; i++) {
            const dot = document.createElement('div');
            dot.style.cssText = `
                width: ${dotSize}px;
                height: ${dotSize}px;
                background: ${color};
                border-radius: 50%;
                animation: ls-dots 1.4s ease-in-out infinite;
                animation-delay: ${i * 0.16}s;
            `;
            container.appendChild(dot);
        }

        return container;
    }

    _createPulse(size, color) {
        const pulse = document.createElement('div');
        pulse.style.cssText = `
            width: ${size}px;
            height: ${size}px;
            background: ${color};
            border-radius: 50%;
            animation: ls-pulse 1.5s ease-in-out infinite;
        `;
        return pulse;
    }

    _createBar(width, color) {
        const container = document.createElement('div');
        container.style.cssText = `
            width: ${width}px;
            height: 4px;
            background: var(--cl-border-light);
            border-radius: 2px;
            overflow: hidden;
        `;

        const bar = document.createElement('div');
        bar.style.cssText = `
            height: 100%;
            background: ${color};
            border-radius: 2px;
            animation: ls-bar 1.5s ease-in-out infinite;
        `;

        container.appendChild(bar);
        return container;
    }

    show() {
        this.element?.classList.remove('ls-hidden');
        return this;
    }

    hide() {
        this.element?.classList.add('ls-hidden');
        return this;
    }

    toggle() {
        this.element?.classList.toggle('ls-hidden');
        return this;
    }

    isVisible() {
        return !this.element?.classList.contains('ls-hidden');
    }

    setText(text) {
        const textEl = this.element?.querySelector('.ls-text');
        if (textEl) {
            textEl.textContent = text;
        }
        return this;
    }

    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this.element?.remove();
        this.element = null;
    }

    /**
     * 靜態方法：顯示全螢幕載入
     */
    static showOverlay(text = Locale.t('loadingSpinner.text'), options = {}) {
        const spinner = new LoadingSpinner({
            overlay: true,
            text,
            ...options
        });
        spinner.mount(document.body);
        return spinner;
    }
}

export default LoadingSpinner;
