/**
 * ColorPicker Component
 * 顏色選擇器 - 選擇與輸入顏色值
 */

export class ColorPicker {
    /**
     * @param {Object} options
     * @param {string} options.value - 初始顏色值 (HEX)
     * @param {Function} options.onChange - 顏色改變回調
     * @param {string} options.label - 標籤文字
     * @param {boolean} options.showHex - 是否顯示 HEX 輸入框
     */
    constructor(options = {}) {
        this.value = options.value || '#000000';
        this.onChange = options.onChange || (() => {});
        this.label = options.label || '顏色';
        this.showHex = options.showHex !== false;
        
        this._createElement();
    }

    _createElement() {
        // Container
        this.element = document.createElement('div');
        this.element.className = 'color-picker';
        this.element.style.cssText = `
            display: inline-flex;
            align-items: center;
            gap: 8px;
        `;

        // Label
        if (this.label) {
            const label = document.createElement('label');
            label.textContent = this.label;
            label.style.cssText = `
                font-size: 14px;
                color: #666;
                user-select: none;
            `;
            this.element.appendChild(label);
        }

        // Color input
        this.colorInput = document.createElement('input');
        this.colorInput.type = 'color';
        this.colorInput.value = this.value;
        this.colorInput.style.cssText = `
            width: 40px;
            height: 32px;
            border: 1px solid #ddd;
            border-radius: 4px;
            cursor: pointer;
            padding: 2px;
        `;
        this.colorInput.oninput = (e) => {
            this.value = e.target.value;
            if (this.hexInput) {
                this.hexInput.value = this.value;
            }
            this.onChange(this.value);
        };

        this.element.appendChild(this.colorInput);

        // Hex input (optional)
        if (this.showHex) {
            this.hexInput = document.createElement('input');
            this.hexInput.type = 'text';
            this.hexInput.value = this.value;
            this.hexInput.maxLength = 7;
            this.hexInput.placeholder = '#000000';
            this.hexInput.style.cssText = `
                width: 80px;
                padding: 6px 8px;
                border: 1px solid #ddd;
                border-radius: 4px;
                font-family: 'Courier New', monospace;
                font-size: 13px;
            `;
            this.hexInput.oninput = (e) => {
                let value = e.target.value;
                if (!value.startsWith('#')) {
                    value = '#' + value;
                }
                if (/^#[0-9A-Fa-f]{6}$/.test(value)) {
                    this.value = value;
                    this.colorInput.value = value;
                    this.onChange(value);
                }
            };

            this.element.appendChild(this.hexInput);
        }
    }

    getValue() {
        return this.value;
    }

    setValue(value) {
        this.value = value;
        this.colorInput.value = value;
        if (this.hexInput) {
            this.hexInput.value = value;
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
