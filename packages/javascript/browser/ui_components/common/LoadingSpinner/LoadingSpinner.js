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
        /** @private Web Animations API handles */
        this._animations = [];
        /** @private Explicit display value used to restore visibility (never '') */
        this._displayMode = 'inline-flex';
        this._create();
    }

    /**
     * Start a Web Animations API animation (CSP-safe @keyframes replacement)
     * and track it for cleanup.
     * @private
     */
    _animate(el, keyframes, timing) {
        if (typeof el.animate !== 'function') return null;
        const anim = el.animate(keyframes, timing);
        this._animations.push(anim);
        return anim;
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
        this._displayMode = overlay ? 'flex' : 'inline-flex';
        if (overlay) {
            container.style.cssText = [
                'position: fixed;',
                'top: 0;',
                'left: 0;',
                'right: 0;',
                'bottom: 0;',
                'background: var(--cl-bg-surface-overlay);',
                'display: flex;',
                'align-items: center;',
                'justify-content: center;',
                'flex-direction: column;',
                'gap: 12px;',
                `z-index: ${safeZIndex};`
            ].join(' ');
        } else {
            container.style.cssText = [
                'display: inline-flex;',
                'align-items: center;',
                'justify-content: center;',
                'flex-direction: column;',
                'gap: 8px;'
            ].join(' ');
        }
        if (!visible) {
            container.classList.add('ls-hidden');
            container.style.display = 'none';
        }

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
                font-size: var(--cl-font-size-lg);
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
            border-radius: var(--cl-radius-round);
        `;
        // @keyframes ls-spin replacement (WAAPI, CSP-safe)
        this._animate(
            spinner,
            [{ transform: 'rotate(0deg)' }, { transform: 'rotate(360deg)' }],
            { duration: 800, iterations: Infinity, easing: 'linear' }
        );
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
                border-radius: var(--cl-radius-round);
            `;
            // @keyframes ls-dots replacement (WAAPI, CSP-safe)
            this._animate(
                dot,
                [
                    { transform: 'scale(0)', opacity: 0.5, offset: 0 },
                    { transform: 'scale(1)', opacity: 1, offset: 0.4 },
                    { transform: 'scale(0)', opacity: 0.5, offset: 0.8 },
                    { transform: 'scale(0)', opacity: 0.5, offset: 1 }
                ],
                { duration: 1400, iterations: Infinity, easing: 'ease-in-out', delay: i * 160 }
            );
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
            border-radius: var(--cl-radius-round);
        `;
        // @keyframes ls-pulse replacement (WAAPI, CSP-safe)
        this._animate(
            pulse,
            [
                { transform: 'scale(0.8)', opacity: 0.5, offset: 0 },
                { transform: 'scale(1)', opacity: 1, offset: 0.5 },
                { transform: 'scale(0.8)', opacity: 0.5, offset: 1 }
            ],
            { duration: 1500, iterations: Infinity, easing: 'ease-in-out' }
        );
        return pulse;
    }

    _createBar(width, color) {
        const container = document.createElement('div');
        container.style.cssText = `
            width: ${width}px;
            height: 4px;
            background: var(--cl-border-light);
            border-radius: var(--cl-radius-xs);
            overflow: hidden;
        `;

        const bar = document.createElement('div');
        bar.style.cssText = `
            height: 100%;
            background: ${color};
            border-radius: var(--cl-radius-xs);
        `;
        // @keyframes ls-bar replacement (WAAPI, CSP-safe)
        this._animate(
            bar,
            [
                { width: '0%', offset: 0 },
                { width: '70%', offset: 0.5 },
                { width: '100%', offset: 1 }
            ],
            { duration: 1500, iterations: Infinity, easing: 'ease-in-out' }
        );

        container.appendChild(bar);
        return container;
    }

    show() {
        if (this.element) {
            this.element.classList.remove('ls-hidden');
            // Must restore an explicit display value (not ''), base display lives in cssText
            this.element.style.display = this._displayMode;
        }
        return this;
    }

    hide() {
        if (this.element) {
            this.element.classList.add('ls-hidden');
            this.element.style.display = 'none';
        }
        return this;
    }

    toggle() {
        if (!this.element) return this;
        return this.isVisible() ? this.hide() : this.show();
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
        this._animations.forEach(anim => anim.cancel());
        this._animations = [];
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
