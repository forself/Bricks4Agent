/**
 * Tag Component
 * 標籤/標記元件 - 用於分類、過濾與標示
 *
 * 支援多種色彩變體、可關閉模式、可點擊模式、圖示前綴
 * 所有樣式皆使用 CSS 變數 (--cl-* 前綴)，深色主題自動適配
 *
 * @example
 * // 基本用法
 * const tag = new Tag({ text: 'JavaScript', variant: 'primary' });
 * tag.render(document.getElementById('container'));
 *
 * // 可關閉標籤
 * const closable = new Tag({ text: 'Removable', closable: true });
 * closable.onClose(() => console.log('closed'));
 * closable.render(container);
 *
 * // 帶圖示
 * const iconic = new Tag({ text: 'Bug', variant: 'danger', icon: '\ud83d\udc1b' });
 * iconic.render(container);
 *
 * @author MAGI System
 * @version 1.0.0
 */

export class Tag {
    /**
     * Available variant names
     * @readonly
     * @enum {string}
     */
    static VARIANTS = {
        DEFAULT: 'default',
        PRIMARY: 'primary',
        SUCCESS: 'success',
        WARNING: 'warning',
        DANGER: 'danger',
        INFO: 'info',
        PURPLE: 'purple',
        TEAL: 'teal',
        PINK: 'pink'
    };

    /**
     * Available size names
     * @readonly
     * @enum {string}
     */
    static SIZES = {
        SM: 'sm',
        MD: 'md',
        LG: 'lg'
    };

    /**
     * Variant-to-CSS-variable mapping for background/border/text colours.
     * Each entry maps to existing --cl-* tokens from theme.css.
     * @private
     */
    static _VARIANT_STYLES = {
        default: {
            bg: 'var(--cl-bg-secondary)',
            border: 'var(--cl-border)',
            text: 'var(--cl-text)',
            hoverBg: 'var(--cl-bg-hover)'
        },
        primary: {
            bg: 'var(--cl-primary-light)',
            border: 'var(--cl-primary)',
            text: 'var(--cl-primary-dark)',
            hoverBg: 'var(--cl-primary)'
        },
        success: {
            bg: 'var(--cl-success-light)',
            border: 'var(--cl-success)',
            text: 'var(--cl-success-dark)',
            hoverBg: 'var(--cl-success)'
        },
        warning: {
            bg: 'var(--cl-warning-light)',
            border: 'var(--cl-warning)',
            text: 'var(--cl-warning-dark)',
            hoverBg: 'var(--cl-warning)'
        },
        danger: {
            bg: 'var(--cl-danger-light)',
            border: 'var(--cl-danger)',
            text: 'var(--cl-danger-dark)',
            hoverBg: 'var(--cl-danger)'
        },
        info: {
            bg: 'var(--cl-info-light)',
            border: 'var(--cl-info)',
            text: 'var(--cl-primary-dark)',
            hoverBg: 'var(--cl-info)'
        },
        purple: {
            bg: 'rgba(var(--cl-purple-rgb), 0.12)',
            border: 'var(--cl-purple)',
            text: 'var(--cl-purple-dark)',
            hoverBg: 'var(--cl-purple)'
        },
        teal: {
            bg: 'rgba(var(--cl-teal-rgb), 0.12)',
            border: 'var(--cl-teal)',
            text: 'var(--cl-teal-dark)',
            hoverBg: 'var(--cl-teal)'
        },
        pink: {
            bg: 'rgba(var(--cl-pink-rgb), 0.12)',
            border: 'var(--cl-pink)',
            text: 'var(--cl-pink-dark)',
            hoverBg: 'var(--cl-pink)'
        }
    };

    /**
     * Build a Tag instance
     * @param {Object}   options
     * @param {string}   options.text      - Display text (required)
     * @param {string}   [options.variant='default'] - Colour variant
     * @param {boolean}  [options.closable=false]    - Show close (x) button
     * @param {boolean}  [options.clickable=false]   - Enable click interaction
     * @param {string}   [options.icon]              - Icon prefix (emoji or text)
     * @param {string}   [options.size='md']         - Size: 'sm' | 'md' | 'lg'
     * @param {boolean}  [options.rounded=false]     - Use pill border-radius
     */
    constructor(options = {}) {
        this.options = {
            text: '',
            variant: Tag.VARIANTS.DEFAULT,
            closable: false,
            clickable: false,
            icon: null,
            size: Tag.SIZES.MD,
            rounded: false,
            ...options
        };

        /** @private */
        this._closeCallbacks = [];
        /** @private */
        this._clickCallbacks = [];
        /** @private */
        this.element = null;
        /** @private */
        this._textEl = null;

        Tag._injectStyles();
        this.element = this._createElement();
    }

    /* ------------------------------------------------------------------
     *  Static helpers
     * ----------------------------------------------------------------*/

    /**
     * Inject scoped CSS into the document head (once).
     * @private
     */
    static _injectStyles() {
        if (document.getElementById('cl-tag-styles')) return;

        const style = document.createElement('style');
        style.id = 'cl-tag-styles';
        style.textContent = `
            .cl-tag {
                display: inline-flex;
                align-items: center;
                gap: 4px;
                font-family: var(--cl-font-family);
                font-weight: 500;
                line-height: 1;
                white-space: nowrap;
                border: 1px solid transparent;
                vertical-align: middle;
                transition: all var(--cl-transition-fast);
                box-sizing: border-box;
                max-width: 100%;
            }

            /* ---- Sizes ---- */
            .cl-tag--sm {
                padding: 2px 6px;
                font-size: var(--cl-font-size-xs);
                border-radius: var(--cl-radius-xs);
            }
            .cl-tag--md {
                padding: 3px 8px;
                font-size: var(--cl-font-size-sm);
                border-radius: var(--cl-radius-sm);
            }
            .cl-tag--lg {
                padding: 5px 10px;
                font-size: var(--cl-font-size-lg);
                border-radius: var(--cl-radius-md);
            }

            /* Rounded (pill) override */
            .cl-tag--rounded.cl-tag--sm { border-radius: var(--cl-radius-pill); }
            .cl-tag--rounded.cl-tag--md { border-radius: var(--cl-radius-pill); }
            .cl-tag--rounded.cl-tag--lg { border-radius: var(--cl-radius-pill); }

            /* ---- Clickable ---- */
            .cl-tag--clickable {
                cursor: pointer;
                user-select: none;
            }
            .cl-tag--clickable:active {
                transform: scale(0.95);
            }

            /* ---- Icon ---- */
            .cl-tag__icon {
                display: inline-flex;
                align-items: center;
                flex-shrink: 0;
            }

            /* ---- Text ---- */
            .cl-tag__text {
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            }

            /* ---- Close button ---- */
            .cl-tag__close {
                display: inline-flex;
                align-items: center;
                justify-content: center;
                border: none;
                background: transparent;
                padding: 0;
                margin-left: 2px;
                cursor: pointer;
                border-radius: var(--cl-radius-round);
                transition: background var(--cl-transition-fast), color var(--cl-transition-fast);
                line-height: 1;
                flex-shrink: 0;
                font-family: var(--cl-font-family);
            }
            .cl-tag--sm .cl-tag__close {
                width: 14px;
                height: 14px;
                font-size: var(--cl-font-size-2xs);
            }
            .cl-tag--md .cl-tag__close {
                width: 16px;
                height: 16px;
                font-size: var(--cl-font-size-xs);
            }
            .cl-tag--lg .cl-tag__close {
                width: 18px;
                height: 18px;
                font-size: var(--cl-font-size-sm);
            }
            .cl-tag__close:hover {
                background: rgba(0, 0, 0, 0.08);
            }
        `;

        document.head.appendChild(style);
    }

    /* ------------------------------------------------------------------
     *  Private instance methods
     * ----------------------------------------------------------------*/

    /**
     * Build the DOM element tree.
     * @private
     * @returns {HTMLElement}
     */
    _createElement() {
        const { text, variant, closable, clickable, icon, size, rounded } = this.options;
        const vs = Tag._VARIANT_STYLES[variant] || Tag._VARIANT_STYLES.default;

        // Root element
        const el = document.createElement('span');
        el.className = this._buildClassName();
        el.style.cssText = `
            background: ${vs.bg};
            border-color: ${vs.border};
            color: ${vs.text};
        `;

        // Icon
        if (icon) {
            const iconEl = document.createElement('span');
            iconEl.className = 'cl-tag__icon';
            iconEl.textContent = icon;
            el.appendChild(iconEl);
        }

        // Text
        this._textEl = document.createElement('span');
        this._textEl.className = 'cl-tag__text';
        this._textEl.textContent = text;
        el.appendChild(this._textEl);

        // Close button
        if (closable) {
            const closeBtn = document.createElement('button');
            closeBtn.className = 'cl-tag__close';
            closeBtn.type = 'button';
            closeBtn.setAttribute('aria-label', 'Remove tag');
            closeBtn.innerHTML = '&times;';
            closeBtn.style.color = vs.text;

            closeBtn.addEventListener('click', (e) => {
                e.stopPropagation();
                this._fireClose();
            });

            el.appendChild(closeBtn);
        }

        // Clickable / hover effects
        if (clickable) {
            el.addEventListener('click', () => this._fireClick());

            el.addEventListener('mouseenter', () => {
                el.style.background = vs.hoverBg;
                el.style.color = 'var(--cl-text-inverse)';
                el.style.borderColor = vs.hoverBg;
                const close = el.querySelector('.cl-tag__close');
                if (close) close.style.color = 'var(--cl-text-inverse)';
            });

            el.addEventListener('mouseleave', () => {
                el.style.background = vs.bg;
                el.style.color = vs.text;
                el.style.borderColor = vs.border;
                const close = el.querySelector('.cl-tag__close');
                if (close) close.style.color = vs.text;
            });
        }

        return el;
    }

    /**
     * Build the CSS class string.
     * @private
     * @returns {string}
     */
    _buildClassName() {
        const { variant, closable, clickable, size, rounded } = this.options;
        const parts = [
            'cl-tag',
            `cl-tag--${variant}`,
            `cl-tag--${size}`
        ];
        if (rounded) parts.push('cl-tag--rounded');
        if (clickable) parts.push('cl-tag--clickable');
        if (closable) parts.push('cl-tag--closable');
        return parts.join(' ');
    }

    /**
     * Invoke all registered close callbacks then remove the element.
     * @private
     */
    _fireClose() {
        this._closeCallbacks.forEach(fn => fn(this));
        this.destroy();
    }

    /**
     * Invoke all registered click callbacks.
     * @private
     */
    _fireClick() {
        this._clickCallbacks.forEach(fn => fn(this));
    }

    /* ------------------------------------------------------------------
     *  Public API
     * ----------------------------------------------------------------*/

    /**
     * Mount the tag into a container element.
     * @param {HTMLElement|string} container - DOM element or CSS selector
     * @returns {Tag} this (for chaining)
     */
    render(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target && this.element) {
            target.appendChild(this.element);
        }
        return this;
    }

    /**
     * Update the display text.
     * @param {string} text
     * @returns {Tag}
     */
    setText(text) {
        this.options.text = text;
        if (this._textEl) this._textEl.textContent = text;
        return this;
    }

    /**
     * Switch to a different colour variant at runtime.
     * @param {string} variant - One of Tag.VARIANTS values
     * @returns {Tag}
     */
    setVariant(variant) {
        this.options.variant = variant;
        const vs = Tag._VARIANT_STYLES[variant] || Tag._VARIANT_STYLES.default;
        if (this.element) {
            this.element.className = this._buildClassName();
            this.element.style.background = vs.bg;
            this.element.style.borderColor = vs.border;
            this.element.style.color = vs.text;

            const close = this.element.querySelector('.cl-tag__close');
            if (close) close.style.color = vs.text;
        }
        return this;
    }

    /**
     * Register a callback fired when the close button is clicked.
     * @param {Function} callback - Receives the Tag instance
     * @returns {Tag}
     */
    onClose(callback) {
        if (typeof callback === 'function') {
            this._closeCallbacks.push(callback);
        }
        return this;
    }

    /**
     * Register a callback fired when the tag is clicked (clickable mode only).
     * @param {Function} callback - Receives the Tag instance
     * @returns {Tag}
     */
    onClick(callback) {
        if (typeof callback === 'function') {
            this._clickCallbacks.push(callback);
        }
        return this;
    }

    /**
     * Get the current display text.
     * @returns {string}
     */
    getText() {
        return this.options.text;
    }

    /**
     * Get the current variant.
     * @returns {string}
     */
    getVariant() {
        return this.options.variant;
    }

    /**
     * Remove the tag element from the DOM and clean up.
     */
    destroy() {
        if (this.element && this.element.parentNode) {
            this.element.remove();
        }
        this._closeCallbacks = [];
        this._clickCallbacks = [];
        this.element = null;
        this._textEl = null;
    }
}

export default Tag;
