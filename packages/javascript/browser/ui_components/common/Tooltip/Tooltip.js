/**
 * Tooltip - Hover/Focus Tooltip Overlay Component
 *
 * Provides tooltip overlays for target elements with configurable
 * position, trigger mode, delay, and content (plain text or HTML).
 * Auto-repositions to stay within the viewport.
 *
 * @author MAGI System
 * @version 1.0.0
 *
 * @example
 * // Attach via constructor
 * const tip = new Tooltip({ text: 'Hello!', position: 'top' });
 * tip.attach(document.getElementById('btn'));
 *
 * @example
 * // Static helper (one-liner)
 * Tooltip.create(myElement, 'Save changes', { position: 'bottom' });
 */

export class Tooltip {
    /* ------------------------------------------------------------------ */
    /*  Static constants                                                   */
    /* ------------------------------------------------------------------ */

    /** Supported tooltip positions */
    static POSITIONS = {
        TOP: 'top',
        BOTTOM: 'bottom',
        LEFT: 'left',
        RIGHT: 'right',
    };

    /** Supported trigger modes */
    static TRIGGERS = {
        HOVER: 'hover',
        CLICK: 'click',
        MANUAL: 'manual',
    };

    /** Gap (px) between the tooltip and the target element */
    static GAP = 8;

    /** Arrow / caret size (px) */
    static ARROW_SIZE = 6;

    /* ------------------------------------------------------------------ */
    /*  Constructor                                                        */
    /* ------------------------------------------------------------------ */

    /**
     * @param {Object}  options
     * @param {string}  options.text        - Tooltip text (plain text or HTML string)
     * @param {string}  [options.position='top']  - Preferred position: top | bottom | left | right
     * @param {string}  [options.trigger='hover']  - Trigger mode: hover | click | manual
     * @param {number}  [options.delay=200]        - Show delay in ms (hover mode only)
     * @param {number}  [options.hideDelay=150]    - Hide delay in ms (hover mode only)
     * @param {number}  [options.maxWidth=280]     - Max width in px
     * @param {boolean} [options.html=false]       - If true, render text as HTML
     */
    constructor(options = {}) {
        this.options = {
            text: '',
            position: Tooltip.POSITIONS.TOP,
            trigger: Tooltip.TRIGGERS.HOVER,
            delay: 200,
            hideDelay: 150,
            maxWidth: 280,
            html: false,
            ...options,
        };

        /** @type {HTMLElement|null} Target element the tooltip is attached to */
        this._target = null;

        /** @type {HTMLElement|null} The tooltip DOM element */
        this._el = null;

        /** @type {boolean} Whether the tooltip is currently visible */
        this._visible = false;

        /** @type {number|null} Pending show timeout id */
        this._showTimer = null;

        /** @type {number|null} Pending hide timeout id */
        this._hideTimer = null;

        /* Bound handlers (so we can removeEventListener later) */
        this._onMouseEnter = this._handleMouseEnter.bind(this);
        this._onMouseLeave = this._handleMouseLeave.bind(this);
        this._onFocusIn = this._handleFocusIn.bind(this);
        this._onFocusOut = this._handleFocusOut.bind(this);
        this._onClick = this._handleClick.bind(this);
        this._onDocClick = this._handleDocumentClick.bind(this);
        this._onKeyDown = this._handleKeyDown.bind(this);

        Tooltip._injectStyles();
    }

    /* ------------------------------------------------------------------ */
    /*  Public API                                                         */
    /* ------------------------------------------------------------------ */

    /**
     * Attach the tooltip to a target DOM element and bind event listeners.
     *
     * @param {HTMLElement} targetElement - The element that triggers the tooltip
     * @returns {Tooltip} this (for chaining)
     */
    attach(targetElement) {
        if (!targetElement || !(targetElement instanceof HTMLElement)) {
            console.warn('[Tooltip] attach() requires a valid HTMLElement.');
            return this;
        }

        // Detach from any previous target first
        if (this._target) {
            this._unbindEvents();
        }

        this._target = targetElement;

        // Ensure the target has an accessible label if none exists
        if (!targetElement.getAttribute('aria-describedby')) {
            // Will be set when the tooltip element is created
        }

        this._bindEvents();
        return this;
    }

    /**
     * Programmatically show the tooltip.
     * Creates the DOM element if it does not exist yet.
     *
     * @returns {Tooltip} this
     */
    show() {
        if (!this._target) return this;

        this._clearTimers();

        if (!this._el) {
            this._createElement();
        }

        document.body.appendChild(this._el);

        // Force reflow so the transition fires
        // eslint-disable-next-line no-unused-expressions
        this._el.offsetHeight;

        this._position();
        this._el.classList.add('cl-tooltip--visible');
        this._visible = true;

        return this;
    }

    /**
     * Programmatically hide the tooltip.
     *
     * @returns {Tooltip} this
     */
    hide() {
        this._clearTimers();

        if (!this._el || !this._visible) return this;

        this._el.classList.remove('cl-tooltip--visible');
        this._visible = false;

        // Remove from DOM after transition
        const el = this._el;
        const onEnd = () => {
            el.removeEventListener('transitionend', onEnd);
            if (el.parentNode) el.parentNode.removeChild(el);
        };
        el.addEventListener('transitionend', onEnd);

        // Fallback removal if transitionend never fires
        setTimeout(() => {
            if (el.parentNode) el.parentNode.removeChild(el);
        }, 200);

        return this;
    }

    /**
     * Remove all event listeners and DOM references.
     * Call this when the tooltip is no longer needed.
     */
    destroy() {
        this.hide();
        this._unbindEvents();

        if (this._target) {
            this._target.removeAttribute('aria-describedby');
        }

        this._target = null;
        this._el = null;
    }

    /**
     * Update the tooltip text at runtime.
     *
     * @param {string} text - New tooltip text (or HTML if options.html is true)
     */
    setText(text) {
        this.options.text = text;
        if (this._el) {
            const body = this._el.querySelector('.cl-tooltip__body');
            if (body) {
                if (this.options.html) {
                    body.innerHTML = text;
                } else {
                    body.textContent = text;
                }
            }
        }
    }

    /* ------------------------------------------------------------------ */
    /*  Static helpers                                                     */
    /* ------------------------------------------------------------------ */

    /**
     * One-liner helper: create a Tooltip and immediately attach it.
     *
     * @param {HTMLElement} element - Target element
     * @param {string}      text    - Tooltip text
     * @param {Object}     [options] - Extra options forwarded to the constructor
     * @returns {Tooltip}
     */
    static create(element, text, options = {}) {
        return new Tooltip({ text, ...options }).attach(element);
    }

    /* ------------------------------------------------------------------ */
    /*  Style injection (singleton)                                        */
    /* ------------------------------------------------------------------ */

    /** @private */
    static _stylesInjected = false;

    /** @private Inject component CSS once into <head> */
    static _injectStyles() {
        if (Tooltip._stylesInjected) return;
        Tooltip._stylesInjected = true;

        const style = document.createElement('style');
        style.id = 'cl-tooltip-styles';
        style.textContent = `
            /* ========== Tooltip Container ========== */
            .cl-tooltip {
                position: fixed;
                z-index: 10000;
                pointer-events: none;
                opacity: 0;
                transition: opacity var(--cl-transition-fast);
                max-width: 280px;
                width: max-content;
            }
            .cl-tooltip--visible {
                opacity: 1;
            }

            /* ========== Tooltip Body ========== */
            .cl-tooltip__body {
                background: var(--cl-bg-dark);
                color: var(--cl-text-inverse);
                font-family: var(--cl-font-family);
                font-size: var(--cl-font-size-sm);
                line-height: 1.45;
                padding: 6px 10px;
                border-radius: var(--cl-radius-sm);
                box-shadow: var(--cl-shadow-md);
                word-wrap: break-word;
                overflow-wrap: break-word;
            }

            /* ========== Arrow / Caret ========== */
            .cl-tooltip__arrow {
                position: absolute;
                width: 0;
                height: 0;
                border-style: solid;
                border-color: transparent;
            }

            /* Arrow for position=top  (arrow points DOWN) */
            .cl-tooltip--top .cl-tooltip__arrow {
                bottom: -6px;
                left: 50%;
                transform: translateX(-50%);
                border-width: 6px 6px 0 6px;
                border-top-color: var(--cl-bg-dark);
            }

            /* Arrow for position=bottom (arrow points UP) */
            .cl-tooltip--bottom .cl-tooltip__arrow {
                top: -6px;
                left: 50%;
                transform: translateX(-50%);
                border-width: 0 6px 6px 6px;
                border-bottom-color: var(--cl-bg-dark);
            }

            /* Arrow for position=left (arrow points RIGHT) */
            .cl-tooltip--left .cl-tooltip__arrow {
                right: -6px;
                top: 50%;
                transform: translateY(-50%);
                border-width: 6px 0 6px 6px;
                border-left-color: var(--cl-bg-dark);
            }

            /* Arrow for position=right (arrow points LEFT) */
            .cl-tooltip--right .cl-tooltip__arrow {
                left: -6px;
                top: 50%;
                transform: translateY(-50%);
                border-width: 6px 6px 6px 0;
                border-right-color: var(--cl-bg-dark);
            }
        `;
        document.head.appendChild(style);
    }

    /* ------------------------------------------------------------------ */
    /*  Internal: DOM creation                                             */
    /* ------------------------------------------------------------------ */

    /** @private Build the tooltip element tree */
    _createElement() {
        const wrapper = document.createElement('div');
        wrapper.className = 'cl-tooltip';
        wrapper.setAttribute('role', 'tooltip');

        // Unique id for aria-describedby
        const id = `cl-tooltip-${Date.now()}-${Math.random().toString(36).substr(2, 6)}`;
        wrapper.id = id;

        // Set max-width from options
        wrapper.style.maxWidth = `${this.options.maxWidth}px`;

        // Body
        const body = document.createElement('div');
        body.className = 'cl-tooltip__body';
        if (this.options.html) {
            body.innerHTML = this.options.text;
        } else {
            body.textContent = this.options.text;
        }
        wrapper.appendChild(body);

        // Arrow
        const arrow = document.createElement('div');
        arrow.className = 'cl-tooltip__arrow';
        wrapper.appendChild(arrow);

        this._el = wrapper;

        // Link target to tooltip for accessibility
        if (this._target) {
            this._target.setAttribute('aria-describedby', id);
        }
    }

    /* ------------------------------------------------------------------ */
    /*  Internal: Positioning                                              */
    /* ------------------------------------------------------------------ */

    /** @private Calculate and apply position; flip if overflowing viewport */
    _position() {
        if (!this._target || !this._el) return;

        const gap = Tooltip.GAP;
        const targetRect = this._target.getBoundingClientRect();
        const tipRect = this._el.getBoundingClientRect();
        const vw = window.innerWidth;
        const vh = window.innerHeight;

        let pos = this.options.position;
        let top = 0;
        let left = 0;

        // Calculate candidate position and check overflow; flip if needed
        const calc = (p) => {
            switch (p) {
                case 'top':
                    top = targetRect.top - tipRect.height - gap;
                    left = targetRect.left + (targetRect.width - tipRect.width) / 2;
                    break;
                case 'bottom':
                    top = targetRect.bottom + gap;
                    left = targetRect.left + (targetRect.width - tipRect.width) / 2;
                    break;
                case 'left':
                    top = targetRect.top + (targetRect.height - tipRect.height) / 2;
                    left = targetRect.left - tipRect.width - gap;
                    break;
                case 'right':
                    top = targetRect.top + (targetRect.height - tipRect.height) / 2;
                    left = targetRect.right + gap;
                    break;
            }
        };

        calc(pos);

        // Flip logic
        const opposite = { top: 'bottom', bottom: 'top', left: 'right', right: 'left' };

        if (
            (pos === 'top' && top < 0) ||
            (pos === 'bottom' && top + tipRect.height > vh) ||
            (pos === 'left' && left < 0) ||
            (pos === 'right' && left + tipRect.width > vw)
        ) {
            pos = opposite[pos];
            calc(pos);
        }

        // Clamp to viewport edges (horizontal)
        if (left < 4) left = 4;
        if (left + tipRect.width > vw - 4) left = vw - tipRect.width - 4;

        // Clamp to viewport edges (vertical)
        if (top < 4) top = 4;
        if (top + tipRect.height > vh - 4) top = vh - tipRect.height - 4;

        this._el.style.top = `${top}px`;
        this._el.style.left = `${left}px`;

        // Apply the position class (controls arrow direction)
        this._el.classList.remove(
            'cl-tooltip--top',
            'cl-tooltip--bottom',
            'cl-tooltip--left',
            'cl-tooltip--right',
        );
        this._el.classList.add(`cl-tooltip--${pos}`);

        // Adjust arrow position when tooltip is clamped horizontally
        const arrow = this._el.querySelector('.cl-tooltip__arrow');
        if (arrow && (pos === 'top' || pos === 'bottom')) {
            const targetCenter = targetRect.left + targetRect.width / 2;
            const tipLeft = parseFloat(this._el.style.left);
            const arrowLeft = targetCenter - tipLeft;
            // Clamp arrow within body bounds
            const min = Tooltip.ARROW_SIZE + 4;
            const max = tipRect.width - Tooltip.ARROW_SIZE - 4;
            arrow.style.left = `${Math.max(min, Math.min(max, arrowLeft))}px`;
            arrow.style.transform = 'translateX(-50%)';
        }
        if (arrow && (pos === 'left' || pos === 'right')) {
            const targetCenter = targetRect.top + targetRect.height / 2;
            const tipTop = parseFloat(this._el.style.top);
            const arrowTop = targetCenter - tipTop;
            const min = Tooltip.ARROW_SIZE + 4;
            const max = tipRect.height - Tooltip.ARROW_SIZE - 4;
            arrow.style.top = `${Math.max(min, Math.min(max, arrowTop))}px`;
            arrow.style.transform = 'translateY(-50%)';
        }
    }

    /* ------------------------------------------------------------------ */
    /*  Internal: Event binding                                            */
    /* ------------------------------------------------------------------ */

    /** @private */
    _bindEvents() {
        if (!this._target) return;

        const { trigger } = this.options;

        if (trigger === 'hover') {
            this._target.addEventListener('mouseenter', this._onMouseEnter);
            this._target.addEventListener('mouseleave', this._onMouseLeave);
            this._target.addEventListener('focusin', this._onFocusIn);
            this._target.addEventListener('focusout', this._onFocusOut);
        } else if (trigger === 'click') {
            this._target.addEventListener('click', this._onClick);
            document.addEventListener('click', this._onDocClick, true);
        }

        // Escape always hides
        document.addEventListener('keydown', this._onKeyDown);
    }

    /** @private */
    _unbindEvents() {
        if (!this._target) return;

        this._target.removeEventListener('mouseenter', this._onMouseEnter);
        this._target.removeEventListener('mouseleave', this._onMouseLeave);
        this._target.removeEventListener('focusin', this._onFocusIn);
        this._target.removeEventListener('focusout', this._onFocusOut);
        this._target.removeEventListener('click', this._onClick);
        document.removeEventListener('click', this._onDocClick, true);
        document.removeEventListener('keydown', this._onKeyDown);
    }

    /* ------------------------------------------------------------------ */
    /*  Internal: Event handlers                                           */
    /* ------------------------------------------------------------------ */

    /** @private */
    _handleMouseEnter() {
        this._clearTimers();
        this._showTimer = setTimeout(() => this.show(), this.options.delay);
    }

    /** @private */
    _handleMouseLeave() {
        this._clearTimers();
        this._hideTimer = setTimeout(() => this.hide(), this.options.hideDelay);
    }

    /** @private */
    _handleFocusIn() {
        this._clearTimers();
        this.show();
    }

    /** @private */
    _handleFocusOut() {
        this._clearTimers();
        this.hide();
    }

    /** @private */
    _handleClick(e) {
        e.stopPropagation();
        if (this._visible) {
            this.hide();
        } else {
            this.show();
        }
    }

    /** @private Close click-triggered tooltip when clicking outside */
    _handleDocumentClick(e) {
        if (!this._visible) return;
        if (this._target && this._target.contains(e.target)) return;
        if (this._el && this._el.contains(e.target)) return;
        this.hide();
    }

    /** @private Hide on Escape key */
    _handleKeyDown(e) {
        if (e.key === 'Escape' && this._visible) {
            this.hide();
        }
    }

    /* ------------------------------------------------------------------ */
    /*  Internal: Timers                                                   */
    /* ------------------------------------------------------------------ */

    /** @private */
    _clearTimers() {
        if (this._showTimer) {
            clearTimeout(this._showTimer);
            this._showTimer = null;
        }
        if (this._hideTimer) {
            clearTimeout(this._hideTimer);
            this._hideTimer = null;
        }
    }
}

export default Tooltip;
