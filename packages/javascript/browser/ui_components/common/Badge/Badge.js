/**
 * Badge - 徽章/標記元件
 *
 * 用於狀態指示、通知計數、分類標籤等場景。
 * 支援文字標籤、數字計數、圓點指示三種類型，
 * 可獨立使用或附著於其他元素上。
 *
 * @author MAGI System
 * @version 1.0.0
 *
 * @example
 * // 文字標籤
 * const badge = new Badge({ text: 'NEW', variant: 'primary' });
 * badge.render(document.getElementById('container'));
 *
 * @example
 * // 數字計數（超過 maxCount 顯示 99+）
 * const countBadge = new Badge({ text: '128', type: 'count', maxCount: 99 });
 * countBadge.render(document.getElementById('button-wrapper'));
 *
 * @example
 * // 圓點指示器
 * const dot = new Badge({ type: 'dot', variant: 'danger' });
 * dot.render(document.getElementById('icon-wrapper'));
 */

/**
 * @typedef {'default'|'primary'|'success'|'warning'|'danger'|'info'} BadgeVariant
 * @typedef {'small'|'medium'|'large'} BadgeSize
 * @typedef {'text'|'count'|'dot'} BadgeType
 */

/**
 * @typedef {Object} BadgeOptions
 * @property {string}       [text='']        - 顯示文字或數字
 * @property {BadgeVariant} [variant='default'] - 色彩變體
 * @property {BadgeSize}    [size='medium']  - 尺寸
 * @property {BadgeType}    [type='text']    - 類型：text 標籤 / count 計數 / dot 圓點
 * @property {number}       [maxCount=99]    - type='count' 時的最大顯示數字，超過顯示 N+
 * @property {boolean}      [attached=false] - 是否以絕對定位附著於父元素右上角
 */

export class Badge {
    /** 色彩變體列舉 */
    static VARIANTS = {
        DEFAULT: 'default',
        PRIMARY: 'primary',
        SUCCESS: 'success',
        WARNING: 'warning',
        DANGER: 'danger',
        INFO: 'info'
    };

    /** 尺寸列舉 */
    static SIZES = {
        SMALL: 'small',
        MEDIUM: 'medium',
        LARGE: 'large'
    };

    /** 類型列舉 */
    static TYPES = {
        TEXT: 'text',
        COUNT: 'count',
        DOT: 'dot'
    };

    /**
     * 建立 Badge 實例
     * @param {BadgeOptions} options - 設定選項
     */
    constructor(options = {}) {
        /** @type {BadgeOptions} */
        this.options = {
            text: '',
            variant: Badge.VARIANTS.DEFAULT,
            size: Badge.SIZES.MEDIUM,
            type: Badge.TYPES.TEXT,
            maxCount: 99,
            attached: false,
            ...options
        };

        /** @type {HTMLElement|null} */
        this.element = null;

        this._injectStyles();
        this._create();
    }

    /**
     * 注入元件所需的 CSS 樣式（單例模式，只注入一次）
     * @private
     */
    _injectStyles() {
        if (document.getElementById('badge-component-styles')) return;

        const style = document.createElement('style');
        style.id = 'badge-component-styles';
        style.textContent = `
            /* ── Badge base ── */
            .cl-badge {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                font-family: var(--cl-font-family);
                font-weight: 600;
                white-space: nowrap;
                vertical-align: middle;
                border-radius: var(--cl-radius-pill);
                transition: background var(--cl-transition-fast),
                            color var(--cl-transition-fast),
                            transform var(--cl-transition-fast);
                box-sizing: border-box;
                line-height: 1;
            }

            /* ── Size: small ── */
            .cl-badge--small {
                font-size: var(--cl-font-size-2xs);
                padding: 2px 6px;
                min-width: 16px;
                height: 16px;
            }
            /* ── Size: medium ── */
            .cl-badge--medium {
                font-size: var(--cl-font-size-xs);
                padding: 2px 8px;
                min-width: 20px;
                height: 20px;
            }
            /* ── Size: large ── */
            .cl-badge--large {
                font-size: var(--cl-font-size-sm);
                padding: 3px 10px;
                min-width: 24px;
                height: 24px;
            }

            /* ── Count type: pill shape ── */
            .cl-badge--count {
                border-radius: var(--cl-radius-pill);
                padding-left: 6px;
                padding-right: 6px;
            }

            /* ── Dot type ── */
            .cl-badge--dot {
                padding: 0;
                border-radius: var(--cl-radius-round);
            }
            .cl-badge--dot.cl-badge--small {
                width: 6px;
                height: 6px;
                min-width: 6px;
            }
            .cl-badge--dot.cl-badge--medium {
                width: 8px;
                height: 8px;
                min-width: 8px;
            }
            .cl-badge--dot.cl-badge--large {
                width: 10px;
                height: 10px;
                min-width: 10px;
            }

            /* ── Variant: default ── */
            .cl-badge--default {
                background: var(--cl-bg-secondary);
                color: var(--cl-text-secondary);
                border: 1px solid var(--cl-border);
            }
            /* ── Variant: primary ── */
            .cl-badge--primary {
                background: var(--cl-primary);
                color: var(--cl-text-inverse);
            }
            /* ── Variant: success ── */
            .cl-badge--success {
                background: var(--cl-success);
                color: var(--cl-text-inverse);
            }
            /* ── Variant: warning ── */
            .cl-badge--warning {
                background: var(--cl-warning);
                color: var(--cl-text-inverse);
            }
            /* ── Variant: danger ── */
            .cl-badge--danger {
                background: var(--cl-danger);
                color: var(--cl-text-inverse);
            }
            /* ── Variant: info ── */
            .cl-badge--info {
                background: var(--cl-info);
                color: var(--cl-text-inverse);
            }

            /* ── Attached positioning ── */
            .cl-badge--attached {
                position: absolute;
                top: 0;
                right: 0;
                transform: translate(50%, -50%);
                z-index: 1;
            }
            .cl-badge--attached.cl-badge--dot {
                top: 2px;
                right: 2px;
                transform: translate(50%, -50%);
            }
        `;
        document.head.appendChild(style);
    }

    /**
     * 建立 DOM 元素
     * @private
     */
    _create() {
        const { type, variant, size, attached } = this.options;

        const el = document.createElement('span');

        // 組合 CSS class
        const classes = ['cl-badge', `cl-badge--${size}`, `cl-badge--${variant}`];

        if (type === Badge.TYPES.COUNT) {
            classes.push('cl-badge--count');
        } else if (type === Badge.TYPES.DOT) {
            classes.push('cl-badge--dot');
        }

        if (attached) {
            classes.push('cl-badge--attached');
        }

        el.className = classes.join(' ');

        // dot 類型不顯示文字
        if (type !== Badge.TYPES.DOT) {
            el.textContent = this._formatText();
        }

        this.element = el;
    }

    /**
     * 格式化顯示文字（處理 count 類型的 maxCount 邏輯）
     * @private
     * @returns {string} 格式化後的文字
     */
    _formatText() {
        const { text, type, maxCount } = this.options;

        if (type === Badge.TYPES.COUNT) {
            const num = parseInt(text, 10);
            if (!isNaN(num) && num > maxCount) {
                return `${maxCount}+`;
            }
        }

        return String(text);
    }

    /**
     * 將 Badge 渲染至指定容器
     * @param {HTMLElement|string} container - DOM 元素或 CSS 選擇器
     * @returns {Badge} 自身實例，支援鏈式呼叫
     */
    render(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;

        if (!target) {
            console.warn('[Badge] render target not found:', container);
            return this;
        }

        // 附著模式需要父元素為定位容器
        if (this.options.attached) {
            const pos = getComputedStyle(target).position;
            if (pos === 'static' || pos === '') {
                target.style.position = 'relative';
            }
        }

        target.appendChild(this.element);
        return this;
    }

    /**
     * 更新顯示文字
     * @param {string|number} text - 新文字
     * @returns {Badge} 自身實例，支援鏈式呼叫
     */
    setText(text) {
        this.options.text = String(text);

        if (this.options.type !== Badge.TYPES.DOT && this.element) {
            this.element.textContent = this._formatText();
        }

        return this;
    }

    /**
     * 切換色彩變體
     * @param {BadgeVariant} variant - 新的色彩變體
     * @returns {Badge} 自身實例，支援鏈式呼叫
     */
    setVariant(variant) {
        if (!this.element) return this;

        // 移除舊 variant class
        this.element.classList.remove(`cl-badge--${this.options.variant}`);
        this.options.variant = variant;
        // 加入新 variant class
        this.element.classList.add(`cl-badge--${variant}`);

        return this;
    }

    /**
     * 顯示 Badge
     * @returns {Badge} 自身實例，支援鏈式呼叫
     */
    show() {
        if (this.element) {
            this.element.style.display = '';
        }
        return this;
    }

    /**
     * 隱藏 Badge
     * @returns {Badge} 自身實例，支援鏈式呼叫
     */
    hide() {
        if (this.element) {
            this.element.style.display = 'none';
        }
        return this;
    }

    /**
     * 銷毀元件，移除 DOM 元素
     */
    destroy() {
        this.element?.remove();
        this.element = null;
    }
}

export default Badge;
