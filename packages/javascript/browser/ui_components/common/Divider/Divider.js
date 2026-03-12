/**
 * Divider - 分隔線元件
 *
 * 提供水平/垂直分隔線，可在中間嵌入文字標籤，
 * 支援多種線條樣式與粗細設定。
 *
 * @author MAGI System
 * @version 1.0.0
 *
 * @example
 *   const hr = new Divider();
 *   hr.render(document.getElementById('app'));
 *
 *   const labeled = new Divider({ text: 'OR', textPosition: 'center' });
 *   labeled.render(document.getElementById('app'));
 *
 *   const vertical = new Divider({ orientation: 'vertical' });
 *   vertical.render(document.getElementById('sidebar'));
 */

/**
 * @typedef {'horizontal'|'vertical'} DividerOrientation
 * @typedef {'left'|'center'|'right'} DividerTextPosition
 * @typedef {'solid'|'dashed'|'dotted'} DividerLineStyle
 * @typedef {'thin'|'normal'|'thick'} DividerThickness
 */

/**
 * @typedef {Object} DividerOptions
 * @property {DividerOrientation}  [orientation='horizontal'] - Layout direction
 * @property {string}              [text='']                   - Label text
 * @property {DividerTextPosition} [textPosition='center']     - Where text sits
 * @property {DividerLineStyle}    [lineStyle='solid']         - Border style
 * @property {DividerThickness}    [thickness='thin']          - Line thickness
 * @property {string}              [margin='16px 0']           - Outer margin
 */

/** Thickness name to pixel value mapping */
const THICKNESS_MAP = { thin: 1, normal: 2, thick: 3 };

export class Divider {
    /**
     * Create a Divider instance.
     * @param {DividerOptions} options
     */
    constructor(options = {}) {
        /** @type {DividerOptions} */
        this.options = {
            orientation: 'horizontal',
            text: '',
            textPosition: 'center',
            lineStyle: 'solid',
            thickness: 'thin',
            margin: '16px 0',
            ...options,
        };

        /** @type {HTMLElement|null} */
        this.element = null;
        /** @private */
        this._container = null;

        this._injectStyles();
        this._create();
    }

    /* ------------------------------------------------------------------ */
    /*  Style injection (once per page)                                   */
    /* ------------------------------------------------------------------ */

    /** @private Inject shared CSS into &lt;head&gt; if not already present. */
    _injectStyles() {
        if (document.getElementById('cl-divider-styles')) return;

        const style = document.createElement('style');
        style.id = 'cl-divider-styles';
        style.textContent = `
            /* ---- Horizontal divider ---- */
            .cl-divider {
                display: flex;
                align-items: center;
                width: 100%;
            }
            .cl-divider--vertical {
                flex-direction: column;
                width: auto;
                height: 100%;
                align-self: stretch;
            }

            /* Horizontal line segments */
            .cl-divider__line {
                flex: 1;
                border: none;
                border-top-style: var(--cl-divider-style, solid);
                border-top-width: var(--cl-divider-width, 1px);
                border-top-color: var(--cl-border);
            }

            /* Vertical line segments */
            .cl-divider--vertical .cl-divider__line {
                flex: 1;
                border-top: none;
                border-left-style: var(--cl-divider-style, solid);
                border-left-width: var(--cl-divider-width, 1px);
                border-left-color: var(--cl-border);
                width: 0;
                min-height: 20px;
            }

            /* Text label */
            .cl-divider__text {
                padding: 0 12px;
                font-size: var(--cl-font-size-sm);
                color: var(--cl-text-muted);
                background: var(--cl-bg);
                white-space: nowrap;
                font-family: var(--cl-font-family);
                line-height: 1;
                user-select: none;
            }

            /* Vertical text */
            .cl-divider--vertical .cl-divider__text {
                padding: 8px 0;
            }

            /* Text position modifiers (horizontal) */
            .cl-divider--text-left > .cl-divider__line:first-child {
                flex: 0 0 24px;
            }
            .cl-divider--text-right > .cl-divider__line:last-child {
                flex: 0 0 24px;
            }
        `;
        document.head.appendChild(style);
    }

    /* ------------------------------------------------------------------ */
    /*  DOM creation                                                      */
    /* ------------------------------------------------------------------ */

    /** @private Build the element tree. */
    _create() {
        const { orientation, text, textPosition, lineStyle, thickness, margin } = this.options;
        const isVertical = orientation === 'vertical';
        const px = THICKNESS_MAP[thickness] || THICKNESS_MAP.thin;

        const wrapper = document.createElement('div');
        wrapper.className = 'cl-divider';
        wrapper.setAttribute('role', 'separator');
        wrapper.setAttribute('aria-orientation', isVertical ? 'vertical' : 'horizontal');

        if (isVertical) {
            wrapper.classList.add('cl-divider--vertical');
            wrapper.style.margin = isVertical ? '0 16px' : margin;
        } else {
            wrapper.style.margin = margin;
        }

        // Apply CSS custom properties for line style and width
        wrapper.style.setProperty('--cl-divider-style', lineStyle);
        wrapper.style.setProperty('--cl-divider-width', `${px}px`);

        if (text) {
            // Add text-position modifier class (horizontal only)
            if (!isVertical && textPosition !== 'center') {
                wrapper.classList.add(`cl-divider--text-${textPosition}`);
            }

            const lineBefore = document.createElement('div');
            lineBefore.className = 'cl-divider__line';

            const textEl = document.createElement('span');
            textEl.className = 'cl-divider__text';
            textEl.textContent = text;

            const lineAfter = document.createElement('div');
            lineAfter.className = 'cl-divider__line';

            wrapper.appendChild(lineBefore);
            wrapper.appendChild(textEl);
            wrapper.appendChild(lineAfter);
        } else {
            // No text: single line spanning full width/height
            const line = document.createElement('div');
            line.className = 'cl-divider__line';
            wrapper.appendChild(line);
        }

        this.element = wrapper;
    }

    /* ------------------------------------------------------------------ */
    /*  Public API                                                        */
    /* ------------------------------------------------------------------ */

    /**
     * Mount the divider element into a container.
     * @param {HTMLElement|string} container - DOM element or CSS selector
     * @returns {Divider} this
     */
    render(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target && this.element) {
            target.appendChild(this.element);
            this._container = target;
        }
        return this;
    }

    /**
     * Update the label text. Pass empty string to remove the label.
     * @param {string} text
     * @returns {Divider} this
     */
    setText(text) {
        const textEl = this.element?.querySelector('.cl-divider__text');
        if (textEl) {
            textEl.textContent = text;
        } else if (text && this.element) {
            // Currently no text node; rebuild
            this.options.text = text;
            const parent = this.element.parentNode;
            this.element.remove();
            this._create();
            if (parent) parent.appendChild(this.element);
        }
        return this;
    }

    /**
     * Remove the element from the DOM and clean up references.
     */
    destroy() {
        this.element?.remove();
        this.element = null;
        this._container = null;
    }
}

export default Divider;
