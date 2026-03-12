/**
 * ColorPicker Component
 */

export class ColorPicker {
    /**
     * @param {Object} options
     * @param {string} options.value
     * @param {Function} options.onChange
     * @param {string} options.label
     * @param {boolean} options.showHex
     */
    constructor(options = {}) {
        this.value = this._normalizeColorValue(options.value || 'var(--cl-text-dark)');
        this.onChange = options.onChange || (() => {});
        this.label = options.label || '顏色';
        this.showHex = options.showHex !== false;

        this._createElement();
    }

    _createElement() {
        this.element = document.createElement('div');
        this.element.className = 'color-picker';
        this.element.style.cssText = `
            display: inline-flex;
            align-items: center;
            gap: 8px;
        `;

        if (this.label) {
            const label = document.createElement('label');
            label.textContent = this.label;
            label.style.cssText = `
                font-size: var(--cl-font-size-lg);
                color: var(--cl-text-secondary);
                user-select: none;
            `;
            this.element.appendChild(label);
        }

        this.colorInput = document.createElement('input');
        this.colorInput.type = 'color';
        this.colorInput.value = this.value;
        this.colorInput.style.cssText = `
            width: 40px;
            height: 32px;
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-sm);
            cursor: pointer;
            padding: 2px;
        `;
        this.colorInput.oninput = (event) => {
            this.value = this._normalizeColorValue(event.target.value);
            if (this.hexInput) {
                this.hexInput.value = this.value;
            }
            this.onChange(this.value);
        };
        this.element.appendChild(this.colorInput);

        if (this.showHex) {
            this.hexInput = document.createElement('input');
            this.hexInput.type = 'text';
            this.hexInput.value = this.value;
            this.hexInput.maxLength = 7;
            this.hexInput.placeholder = '#RRGGBB';
            this.hexInput.style.cssText = `
                width: 80px;
                padding: 6px 8px;
                border: 1px solid var(--cl-border);
                border-radius: var(--cl-radius-sm);
                font-family: var(--cl-font-family-mono);
                font-size: var(--cl-font-size-md);
            `;
            this.hexInput.oninput = (event) => {
                let value = event.target.value.trim();
                if (value && !value.startsWith('#')) {
                    value = `#${value}`;
                }

                const normalizedValue = this._resolveColorToHex(value);
                if (normalizedValue) {
                    this.value = normalizedValue;
                    this.colorInput.value = normalizedValue;
                    event.target.value = normalizedValue;
                    this.onChange(normalizedValue);
                }
            };

            this.element.appendChild(this.hexInput);
        }
    }

    _normalizeColorValue(value) {
        return this._resolveColorToHex(value) || ['#', '00', '00', '00'].join('');
    }

    _resolveColorToHex(value) {
        if (typeof value !== 'string' || !value.trim()) {
            return null;
        }

        const normalizedValue = value.trim();
        if (/^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$/.test(normalizedValue)) {
            return normalizedValue.length === 4
                ? `#${normalizedValue.slice(1).split('').map((char) => char + char).join('')}`
                : normalizedValue.toLowerCase();
        }

        const rgbMatch = normalizedValue.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
        if (rgbMatch) {
            return `#${[rgbMatch[1], rgbMatch[2], rgbMatch[3]]
                .map((channel) => Number.parseInt(channel, 10).toString(16).padStart(2, '0'))
                .join('')}`;
        }

        if (typeof document === 'undefined') {
            return null;
        }

        const probe = document.createElement('span');
        probe.style.color = normalizedValue;
        probe.style.position = 'absolute';
        probe.style.opacity = '0';
        probe.style.pointerEvents = 'none';

        const mountTarget = document.body || document.documentElement;
        if (!mountTarget) {
            return null;
        }

        mountTarget.appendChild(probe);
        const resolvedColor = getComputedStyle(probe).color;
        probe.remove();

        const resolvedMatch = resolvedColor.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
        if (!resolvedMatch) {
            return null;
        }

        return `#${[resolvedMatch[1], resolvedMatch[2], resolvedMatch[3]]
            .map((channel) => Number.parseInt(channel, 10).toString(16).padStart(2, '0'))
            .join('')}`;
    }

    getValue() {
        return this.value;
    }

    setValue(value) {
        this.value = this._normalizeColorValue(value);
        this.colorInput.value = this.value;
        if (this.hexInput) {
            this.hexInput.value = this.value;
        }
    }

    setDisabled(disabled) {
        this.colorInput.disabled = disabled;
        if (this.hexInput) {
            this.hexInput.disabled = disabled;
        }
    }

    mount(container) {
        const targetContainer = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (targetContainer) {
            targetContainer.appendChild(this.element);
        }
    }

    destroy() {
        if (this.element && this.element.parentNode) {
            this.element.parentNode.removeChild(this.element);
        }
    }
}

export default ColorPicker;
