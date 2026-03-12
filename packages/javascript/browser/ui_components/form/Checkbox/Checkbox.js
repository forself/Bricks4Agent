/**
 * Checkbox Component
 * 複選框元件 - 支援多選
 */

export class Checkbox {
    /**
     * @param {Object} options
     * @param {string} options.label - 標籤文字
     * @param {boolean} options.checked - 是否勾選
     * @param {any} options.value - 值
     * @param {boolean} options.disabled - 停用
     * @param {string} options.size - 尺寸
     * @param {Function} options.onChange - 變更回調
     */
    constructor(options = {}) {
        this.options = {
            label: '',
            checked: false,
            value: true,
            disabled: false,
            size: 'medium',
            onChange: null,
            ...options
        };

        this.checked = this.options.checked;
        this.element = this._createElement();
    }

    _getCheckmarkMarkup(size) {
        const iconSize = Math.max(10, size - 8);
        return `
            <svg viewBox="0 0 12 12" fill="none" style="width: ${iconSize}px; height: ${iconSize}px;">
                <path d="M2 6L5 9L10 3" stroke="var(--cl-text-inverse)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
        `;
    }

    _getSizeStyles() {
        const sizes = {
            small: { box: '14px', font: '12px', gap: '6px' },
            medium: { box: '18px', font: '14px', gap: '8px' },
            large: { box: '22px', font: '16px', gap: '10px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _createElement() {
        const { label, disabled } = this.options;
        const sizeStyles = this._getSizeStyles();

        const container = document.createElement('label');
        container.className = 'checkbox';
        container.style.cssText = `
            display: inline-flex;
            align-items: center;
            gap: ${sizeStyles.gap};
            cursor: ${disabled ? 'not-allowed' : 'pointer'};
            user-select: none;
            opacity: ${disabled ? '0.6' : '1'};
            transition: opacity var(--cl-transition-fast);
        `;

        // 隱藏的原生 checkbox
        const input = document.createElement('input');
        input.type = 'checkbox';
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
                this.options.onChange(this.checked, this.options.value);
            }
        });

        // 自訂外觀
        const box = document.createElement('span');
        box.className = 'checkbox__box';
        box.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: ${sizeStyles.box};
            height: ${sizeStyles.box};
            border: 2px solid ${this.checked ? 'var(--cl-primary)' : 'var(--cl-text-light)'};
            border-radius: var(--cl-radius-sm);
            background: ${this.checked ? 'var(--cl-primary)' : 'var(--cl-bg)'};
            transition: all var(--cl-transition);
            box-sizing: border-box;
        `;

        box.innerHTML = this.checked
            ? this._getCheckmarkMarkup(parseInt(sizeStyles.box, 10))
            : '';

        // 標籤
        const labelSpan = document.createElement('span');
        labelSpan.className = 'checkbox__label';
        labelSpan.textContent = label;
        labelSpan.style.cssText = `
            font-size: ${sizeStyles.font};
            font-family: var(--cl-font-family);
            color: var(--cl-text);
        `;

        container.appendChild(input);
        container.appendChild(box);
        container.appendChild(labelSpan);

        this.input = input;
        this.box = box;

        // Hover 效果
        if (!disabled) {
            container.addEventListener('mouseenter', () => {
                if (!this.checked) box.style.borderColor = 'var(--cl-primary)';
            });
            container.addEventListener('mouseleave', () => {
                if (!this.checked) box.style.borderColor = 'var(--cl-text-light)';
            });
        }

        return container;
    }

    _updateVisual() {
        this.box.style.borderColor = this.checked ? 'var(--cl-primary)' : 'var(--cl-text-light)';
        this.box.style.background = this.checked ? 'var(--cl-primary)' : 'var(--cl-bg)';
        this.box.innerHTML = this.checked
            ? this._getCheckmarkMarkup(parseInt(this._getSizeStyles().box, 10))
            : '';
    }

    isChecked() {
        return this.checked;
    }

    setChecked(checked) {
        this.checked = checked;
        this.input.checked = checked;
        this._updateVisual();
    }

    getValue() {
        return this.checked;
    }

    setValue(value) {
        this.setChecked(!!value);
    }

    clear() {
        this.setChecked(false);
    }

    toggle() {
        this.setChecked(!this.checked);
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
     * 建立多選群組
     */
    static createGroup(config = {}) {
        const {
            items = [],        // [{label, value, checked?, disabled?}]
            name = 'checkbox-group',
            direction = 'vertical', // 'vertical' | 'horizontal'
            onChange = () => { },
            ...options
        } = config;

        const group = document.createElement('div');
        group.className = 'checkbox-group';
        group.style.cssText = `
            display: flex;
            flex-direction: ${direction === 'vertical' ? 'column' : 'row'};
            gap: ${direction === 'vertical' ? '8px' : '16px'};
            flex-wrap: wrap;
        `;

        const checkboxes = [];

        items.forEach(item => {
            const cb = new Checkbox({
                label: item.label,
                value: item.value,
                checked: item.checked || false,
                disabled: item.disabled || false,
                ...options,
                onChange: (checked, value) => {
                    const selectedValues = checkboxes
                        .filter(c => c.isChecked())
                        .map(c => c.options.value);
                    onChange(selectedValues, { checked, value });
                }
            });
            checkboxes.push(cb);
            group.appendChild(cb.element);
        });

        group.getValues = () => checkboxes.filter(c => c.isChecked()).map(c => c.options.value);
        
        group.setValues = (values) => {
            checkboxes.forEach(cb => {
                cb.setChecked(values.includes(cb.options.value));
            });
        };

        // 全選
        group.selectAll = () => {
            checkboxes.forEach(cb => cb.setChecked(true));
            onChange(group.getValues());
        };

        // 全取消
        group.deselectAll = () => {
            checkboxes.forEach(cb => cb.setChecked(false));
            onChange(group.getValues());
        };

        // 全選/全取消切換
        group.toggleAll = () => {
            const allChecked = checkboxes.every(cb => cb.isChecked());
            if (allChecked) {
                group.deselectAll();
            } else {
                group.selectAll();
            }
        };

        // 反向選取
        group.invertSelection = () => {
            checkboxes.forEach(cb => cb.setChecked(!cb.isChecked()));
            onChange(group.getValues());
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

export default Checkbox;
