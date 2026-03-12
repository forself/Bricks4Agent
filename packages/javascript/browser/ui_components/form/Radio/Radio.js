/**
 * Radio Component
 * 單選按鈕元件
 */

export class Radio {
    /**
     * @param {Object} options
     * @param {string} options.name - 群組名稱（同群組共用）
     * @param {string} options.label - 標籤文字
     * @param {any} options.value - 值
     * @param {boolean} options.checked - 是否選中
     * @param {boolean} options.disabled - 停用
     * @param {string} options.size - 尺寸
     * @param {Function} options.onChange - 變更回調
     */
    constructor(options = {}) {
        this.options = {
            name: 'radio-group',
            label: '',
            value: '',
            checked: false,
            disabled: false,
            size: 'medium',
            onChange: null,
            ...options
        };

        this.checked = this.options.checked;
        this.element = this._createElement();
    }

    _getSizeStyles() {
        const sizes = {
            small: { circle: '14px', font: '12px', gap: '6px' },
            medium: { circle: '18px', font: '14px', gap: '8px' },
            large: { circle: '22px', font: '16px', gap: '10px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _createElement() {
        const { name, label, value, disabled } = this.options;
        const sizeStyles = this._getSizeStyles();

        const container = document.createElement('label');
        container.className = 'radio';
        container.style.cssText = `
            display: inline-flex;
            align-items: center;
            gap: ${sizeStyles.gap};
            cursor: ${disabled ? 'not-allowed' : 'pointer'};
            user-select: none;
            opacity: ${disabled ? '0.6' : '1'};
        `;

        // 隱藏的原生 radio
        const input = document.createElement('input');
        input.type = 'radio';
        input.name = name;
        input.value = value;
        input.checked = this.checked;
        input.disabled = disabled;
        input.style.cssText = `
            position: absolute;
            opacity: 0;
            width: 0;
            height: 0;
        `;
        input.addEventListener('change', () => {
            this.checked = input.checked;
            this._updateVisual();
            if (this.options.onChange) {
                this.options.onChange(this.options.value);
            }
        });

        // 自訂外觀
        const circle = document.createElement('span');
        circle.className = 'radio__circle';
        circle.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: ${sizeStyles.circle};
            height: ${sizeStyles.circle};
            border: 2px solid ${this.checked ? 'var(--cl-primary)' : 'var(--cl-text-light)'};
            border-radius: var(--cl-radius-round);
            background: var(--cl-bg);
            transition: all var(--cl-transition);
        `;

        // 內部圓點
        const dot = document.createElement('span');
        dot.className = 'radio__dot';
        dot.style.cssText = `
            width: 50%;
            height: 50%;
            border-radius: var(--cl-radius-round);
            background: var(--cl-primary);
            transform: scale(${this.checked ? 1 : 0});
            transition: transform var(--cl-transition);
        `;
        circle.appendChild(dot);

        // 標籤
        const labelSpan = document.createElement('span');
        labelSpan.className = 'radio__label';
        labelSpan.textContent = label;
        labelSpan.style.cssText = `font-size: ${sizeStyles.font}; color: var(--cl-text);`;

        container.appendChild(input);
        container.appendChild(circle);
        container.appendChild(labelSpan);

        this.input = input;
        this.circle = circle;
        this.dot = dot;

        // Hover 效果
        if (!disabled) {
            container.addEventListener('mouseenter', () => {
                if (!this.checked) circle.style.borderColor = 'var(--cl-primary)';
            });
            container.addEventListener('mouseleave', () => {
                if (!this.checked) circle.style.borderColor = 'var(--cl-text-light)';
            });
        }

        return container;
    }

    _updateVisual() {
        this.circle.style.borderColor = this.checked ? 'var(--cl-primary)' : 'var(--cl-text-light)';
        this.dot.style.transform = `scale(${this.checked ? 1 : 0})`;
    }

    isChecked() {
        return this.checked;
    }

    setChecked(checked) {
        this.checked = checked;
        this.input.checked = checked;
        this._updateVisual();
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

    /**
     * 建立單選群組
     */
    static createGroup(config = {}) {
        const {
            name = 'radio-group',
            items = [],         // [{label, value, disabled?}]
            value = null,       // 初始選中值
            direction = 'vertical',
            onChange = () => { },
            ...options
        } = config;

        const group = document.createElement('div');
        group.className = 'radio-group';
        group.style.cssText = `
            display: flex;
            flex-direction: ${direction === 'vertical' ? 'column' : 'row'};
            gap: ${direction === 'vertical' ? '8px' : '16px'};
            flex-wrap: wrap;
        `;

        const radios = [];

        items.forEach(item => {
            const radio = new Radio({
                name,
                label: item.label,
                value: item.value,
                checked: item.value === value,
                disabled: item.disabled || false,
                ...options,
                onChange: (val) => {
                    // 更新其他 radio 的視覺狀態
                    radios.forEach(r => {
                        if (r.options.value !== val) {
                            r.checked = false;
                            r._updateVisual();
                        }
                    });
                    onChange(val);
                }
            });
            radios.push(radio);
            group.appendChild(radio.element);
        });

        group.getValue = () => {
            const checked = radios.find(r => r.isChecked());
            return checked ? checked.options.value : null;
        };

        group.setValue = (val) => {
            radios.forEach(r => {
                r.setChecked(r.options.value === val);
            });
        };

        // 添加 mount 方法
        group.mount = (container) => {
            const target = typeof container === 'string' ? document.querySelector(container) : container;
            if (target) {
                target.appendChild(group);
            }
            return group;
        };

        return group;
    }
}

export default Radio;
