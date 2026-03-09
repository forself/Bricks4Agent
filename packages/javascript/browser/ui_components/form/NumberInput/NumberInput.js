import { escapeHtml } from '../../utils/security.js';

export class NumberInput {
    constructor(options = {}) {
        this.options = {
            label: '',
            value: null,
            min: 0,
            max: 100,
            step: 1,
            precision: 0,
            disabled: false,
            showButtons: true,
            placeholder: '',
            width: '100%',
            size: 'medium', // small, medium, large
            onChange: null,
            className: '',
            ...options
        };

        this.value = this.options.value !== null ? Number.parseFloat(this.options.value) : null;
        this.element = this._create();
    }

    _getSizeStyles() {
        const sizes = {
            small: { height: '32px', fontSize: '13px', btnWidth: '28px' },
            medium: { height: '40px', fontSize: '14px', btnWidth: '32px' },
            large: { height: '48px', fontSize: '16px', btnWidth: '40px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _create() {
        const { label, required, disabled, width, placeholder, showButtons, className } = this.options;
        const sizeStyles = this._getSizeStyles();

        const container = document.createElement('div');
        container.className = `number-input-container ${className}`;
        container.style.cssText = `display: flex; flex-direction: column; gap: 4px; width: ${width};`;

        // 1. Label
        if (label) {
            const labelEl = document.createElement('label');
            labelEl.innerHTML = `${escapeHtml(label)}${required ? '<span style="color: var(--cl-danger); margin-left: 2px;">*</span>' : ''}`;
            labelEl.style.cssText = `font-size: 13px; font-weight: 500; color: var(--cl-text);`;
            container.appendChild(labelEl);
        }

        // 2. Wrapper
        const wrapper = document.createElement('div');
        wrapper.className = 'number-input__wrapper';
        wrapper.style.cssText = `
            display: flex;
            align-items: stretch;
            border: 1px solid var(--cl-border);
            border-radius: 6px;
            overflow: hidden;
            height: ${sizeStyles.height};
            transition: all 0.2s;
            background: ${disabled ? 'var(--cl-bg-secondary)' : 'white'};
        `;

        // 3. Decrease Button
        if (showButtons) {
            const decreaseBtn = this._createButton('-', () => this._decrease());
            wrapper.appendChild(decreaseBtn);
            this.decreaseBtn = decreaseBtn;
        }

        // 4. Input
        const input = document.createElement('input');
        input.type = 'text';
        input.inputMode = 'numeric';
        input.placeholder = placeholder;
        input.value = this.value !== null ? this._formatValue(this.value) : '';
        input.disabled = disabled;
        input.style.cssText = `
            flex: 1;
            min-width: 0;
            border: none;
            outline: none;
            text-align: center;
            font-size: ${sizeStyles.fontSize};
            font-family: inherit;
            background: transparent;
            color: ${disabled ? 'var(--cl-text-placeholder)' : 'var(--cl-text)'};
        `;

        // Events
        input.addEventListener('focus', () => {
            if (this.options.disabled) return;
            wrapper.style.borderColor = 'var(--cl-primary)';
            wrapper.style.boxShadow = '0 0 0 3px rgba(var(--cl-primary-rgb), 0.1)';
        });

        input.addEventListener('blur', () => {
            if (this.options.disabled) return;
            wrapper.style.borderColor = 'var(--cl-border)';
            wrapper.style.boxShadow = 'none';
            this._validateAndUpdate(input.value);
        });

        input.addEventListener('keydown', (e) => {
            if (this.options.disabled) return;
            if (e.key === 'ArrowUp') {
                e.preventDefault();
                this._increase();
            } else if (e.key === 'ArrowDown') {
                e.preventDefault();
                this._decrease();
            } else if (e.key === 'Enter') {
                this._validateAndUpdate(input.value);
            }
        });

        input.addEventListener('input', () => {
            this._adjustFontSize(input.value);
        });

        wrapper.appendChild(input);
        this.input = input;

        // 5. Increase Button
        if (showButtons) {
            const increaseBtn = this._createButton('+', () => this._increase());
            wrapper.appendChild(increaseBtn);
            this.increaseBtn = increaseBtn;
        }

        this.wrapper = wrapper;
        container.appendChild(wrapper);
        return container;
    }

    _createButton(text, onClick) {
        const sizeStyles = this._getSizeStyles();
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.textContent = text;
        btn.disabled = this.options.disabled;
        btn.style.cssText = `
            width: ${sizeStyles.btnWidth};
            border: none;
            background: var(--cl-bg-secondary);
            color: var(--cl-text-secondary);
            font-size: 16px;
            font-weight: bold;
            cursor: ${this.options.disabled ? 'not-allowed' : 'pointer'};
            transition: background 0.15s;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 0;
            margin: 0;
        `;

        if (!this.options.disabled) {
            btn.addEventListener('mouseenter', () => { btn.style.background = 'var(--cl-border-light)'; });
            btn.addEventListener('mouseleave', () => { btn.style.background = 'var(--cl-bg-secondary)'; });
            
            // 重要：在 mousedown 阻止 focus 轉移，這對編輯器體驗至關重要
            btn.addEventListener('mousedown', (e) => {
                e.preventDefault();
            });

            btn.addEventListener('click', (e) => {
                e.preventDefault(); 
                onClick();
            });
        }

        return btn;
    }

    _formatValue(value) {
        if (value === null || value === undefined || isNaN(value)) return '';
        const str = this.options.precision > 0
            ? Number(value).toFixed(this.options.precision)
            : String(Math.round(value));
        return str;
    }

    _adjustFontSize(valStr) {
        if (!this.input) return;
        
        const len = valStr.length;
        const sizeStyles = this._getSizeStyles();
        const baseSize = Number.parseInt(sizeStyles.fontSize);
        
        // 寬度以12位數能正常顯示為準，位數高於12之後，每兩位數字級大小縮減1px
        if (len > 12) {
             const reduction = Math.ceil((len - 12) / 2);
             const newSize = Math.max(8, baseSize - reduction); // 最小 8px 防止不可讀
             this.input.style.fontSize = `${newSize}px`;
        } else {
             this.input.style.fontSize = sizeStyles.fontSize;
        }
    }

    _validateAndUpdate(inputValue) {
        let num = Number.parseFloat(inputValue);

        if (Number.isNaN(num)) {
            // Revert to old valid value or clear if allowable? 
            // Here we allow null if input is empty
            if (inputValue.trim() === '') {
                this.setValue(null);
            } else {
                // Invalid input, revert to current value
                this.input.value = this._formatValue(this.value);
            }
        } else {
            num = Math.max(this.options.min, Math.min(this.options.max, num));
            this.setValue(num);
        }
    }

    _increase() {
        if (this.options.disabled) return;
        const current = this.value ?? this.options.min;
        const newValue = Math.min(this.options.max, current + this.options.step);
        this.setValue(newValue);
    }

    _decrease() {
        if (this.options.disabled) return;
        const current = this.value ?? this.options.min;
        const newValue = Math.max(this.options.min, current - this.options.step);
        this.setValue(newValue);
    }

    getValue() {
        return this.value;
    }

    setValue(value) {
        this.value = value;
        if (this.input) {
            const str = this._formatValue(value);
            this.input.value = str;
            this._adjustFontSize(str);
        }
        if (this.options.onChange) {
            this.options.onChange(value);
        }
    }

    clear() {
        this.setValue(null);
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default NumberInput;
