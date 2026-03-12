/**
 * Dropdown Component
 * 下拉選單元件 - 支援基本選擇與可搜尋模式
 */
import Locale from '../../i18n/index.js';


export class Dropdown {
    static VARIANTS = {
        BASIC: 'basic',        // 基本下拉選單
        SEARCHABLE: 'searchable' // 可搜尋下拉選單
    };

    /**
     * @param {Object} options
     * @param {string} options.variant - 'basic' 或 'searchable'
     * @param {Array} options.items - 選項 [{value, label, disabled?}]
     * @param {string} options.placeholder - 預設提示文字
     * @param {any} options.value - 初始值
     * @param {Function} options.onChange - 選擇變更回調
     * @param {string} options.size - 尺寸 (small, medium, large)
     * @param {boolean} options.disabled - 停用
     * @param {boolean} options.clearable - 可清除選擇
     * @param {string} options.width - 寬度
     * @param {string} options.emptyText - 無結果文字
     */
    constructor(options = {}) {
        this.options = {
            variant: 'basic',
            items: [],
            placeholder: Locale.t('dropdown.placeholder'),
            value: null,
            onChange: null,
            size: 'medium',
            disabled: false,
            clearable: false,
            width: '200px',
            emptyText: Locale.t('dropdown.emptyText'),
            ...options
        };

        this.isOpen = false;
        this.selectedValue = this.options.value;
        this.filteredItems = [...this.options.items];
        this.highlightIndex = -1;

        this.element = this._createElement();
        this._bindEvents();
    }

    _getSizeStyles() {
        const sizes = {
            small: { padding: '6px 10px', fontSize: 'var(--cl-font-size-sm)', height: '30px' },
            medium: { padding: '8px 12px', fontSize: 'var(--cl-font-size-lg)', height: '36px' },
            large: { padding: '10px 14px', fontSize: 'var(--cl-font-size-xl)', height: '44px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _createElement() {
        const { variant, placeholder, disabled, width } = this.options;
        const sizeStyles = this._getSizeStyles();
        const isSearchable = variant === 'searchable';

        // 容器
        const container = document.createElement('div');
        container.className = `dropdown dropdown--${variant}`;
        container.style.cssText = `
            position: relative;
            display: inline-block;
            width: ${width};
            font-family: inherit;
        `;

        // 選擇器區域
        const selector = document.createElement('div');
        selector.className = 'dropdown__selector';
        selector.style.cssText = `
            display: flex;
            align-items: center;
            gap: 8px;
            height: ${sizeStyles.height};
            padding: ${sizeStyles.padding};
            padding-right: 32px;
            background: var(--cl-bg);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-md);
            cursor: ${disabled ? 'not-allowed' : 'pointer'};
            transition: all var(--cl-transition);
            opacity: ${disabled ? '0.6' : '1'};
        `;

        // 顯示文字或輸入框
        if (isSearchable) {
            const input = document.createElement('input');
            input.className = 'dropdown__input';
            input.type = 'text';
            input.placeholder = placeholder;
            input.disabled = disabled;
            input.style.cssText = `
                flex: 1;
                border: none;
                outline: none;
                font-size: ${sizeStyles.fontSize};
                background: transparent;
                cursor: ${disabled ? 'not-allowed' : 'text'};
            `;
            selector.appendChild(input);
            this.input = input;
        } else {
            const display = document.createElement('span');
            display.className = 'dropdown__display';
            display.textContent = placeholder;
            display.style.cssText = `
                flex: 1;
                font-size: ${sizeStyles.fontSize};
                color: var(--cl-text-placeholder);
                overflow: hidden;
                text-overflow: ellipsis;
                white-space: nowrap;
            `;
            selector.appendChild(display);
            this.display = display;
        }

        // 圖示區域
        const icons = document.createElement('div');
        icons.className = 'dropdown__icons';
        icons.style.cssText = `
            position: absolute;
            right: 8px;
            top: 50%;
            transform: translateY(-50%);
            display: flex;
            gap: 4px;
            align-items: center;
        `;

        // 箭頭圖示
        const arrow = document.createElement('span');
        arrow.className = 'dropdown__arrow';
        arrow.innerHTML = `<svg width="12" height="12" viewBox="0 0 12 12" fill="none">
            <path d="M3 4.5L6 7.5L9 4.5" stroke="var(--cl-text-secondary)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>`;
        arrow.style.cssText = `
            display: flex;
            transition: transform var(--cl-transition);
        `;
        icons.appendChild(arrow);
        this.arrow = arrow;

        selector.appendChild(icons);

        // 下拉選單區域
        const menu = document.createElement('div');
        menu.className = 'dropdown__menu';
        menu.style.cssText = `
            position: absolute;
            top: 100%;
            left: 0;
            right: 0;
            margin-top: 4px;
            background: var(--cl-bg);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-md);
            box-shadow: var(--cl-shadow-md);
            max-height: 240px;
            overflow-y: auto;
            z-index: 1000;
            display: none;
        `;

        this._renderItems(menu);

        container.appendChild(selector);
        container.appendChild(menu);

        this.container = container;
        this.selector = selector;
        this.menu = menu;

        // 設定初始值
        if (this.selectedValue !== null) {
            this._setDisplayValue(this.selectedValue);
        }

        return container;
    }

    _renderItems(menu = this.menu) {
        const { emptyText, placeholder } = this.options;
        menu.innerHTML = '';

        if (this.filteredItems.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'dropdown__empty';
            empty.textContent = emptyText;
            empty.style.cssText = `
                padding: 12px;
                text-align: center;
                color: var(--cl-text-placeholder);
                font-size: var(--cl-font-size-md);
            `;
            menu.appendChild(empty);
            return;
        }

        // 先加入空白選項 (placeholder)
        const emptyOption = document.createElement('div');
        emptyOption.className = 'dropdown__option dropdown__option--empty';
        emptyOption.dataset.value = '';
        emptyOption.dataset.index = -1;
        
        const isEmptySelected = this.selectedValue === null || this.selectedValue === '' || this.selectedValue === undefined;
        
        emptyOption.style.cssText = `
            padding: 10px 12px;
            cursor: pointer;
            transition: background var(--cl-transition-fast);
            display: flex;
            align-items: center;
            justify-content: space-between;
            font-size: var(--cl-font-size-lg);
            color: var(--cl-text-placeholder);
            font-style: italic;
            background: ${isEmptySelected ? 'var(--cl-primary-light)' : 'transparent'};
        `;
        
        const emptyLabel = document.createElement('span');
        emptyLabel.textContent = placeholder || '-- 請選擇 --';
        emptyOption.appendChild(emptyLabel);
        
        emptyOption.addEventListener('mouseenter', () => {
            if (!isEmptySelected) emptyOption.style.background = 'var(--cl-bg-secondary)';
        });
        emptyOption.addEventListener('mouseleave', () => {
            if (!isEmptySelected) emptyOption.style.background = 'transparent';
        });
        emptyOption.addEventListener('click', () => {
            this._clearSelection();
        });
        
        menu.appendChild(emptyOption);

        // 渲染其他選項
        this.filteredItems.forEach((item, index) => {
            const option = document.createElement('div');
            option.className = 'dropdown__option';
            option.dataset.value = item.value;
            option.dataset.index = index;

            const isSelected = item.value === this.selectedValue;
            const isDisabled = item.disabled;

            option.style.cssText = `
                padding: 10px 12px;
                cursor: ${isDisabled ? 'not-allowed' : 'pointer'};
                transition: background var(--cl-transition-fast);
                display: flex;
                align-items: center;
                justify-content: space-between;
                font-size: var(--cl-font-size-lg);
                color: ${isDisabled ? 'var(--cl-text-light)' : 'var(--cl-text)'};
                background: ${isSelected ? 'var(--cl-primary-light)' : 'transparent'};
            `;

            const labelSpan = document.createElement('span');
            labelSpan.textContent = item.label;
            option.appendChild(labelSpan);

            if (isSelected) {
                const check = document.createElement('span');
                check.innerHTML = `<svg width="14" height="14" viewBox="0 0 14 14" fill="none">
                    <path d="M3 7L6 10L11 4" stroke="var(--cl-primary)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>`;
                option.appendChild(check);
            }

            if (!isDisabled) {
                option.addEventListener('mouseenter', () => {
                    if (!isSelected) option.style.background = 'var(--cl-bg-secondary)';
                    this.highlightIndex = index;
                });
                option.addEventListener('mouseleave', () => {
                    if (!isSelected) option.style.background = 'transparent';
                });
                option.addEventListener('click', () => {
                    this._selectItem(item);
                });
            }

            menu.appendChild(option);
        });
    }

    _clearSelection() {
        this.selectedValue = null;
        
        if (this.options.variant === 'searchable' && this.input) {
            this.input.value = '';
        } else if (this.display) {
            this.display.textContent = this.options.placeholder;
            this.display.style.color = 'var(--cl-text-placeholder)';
        }
        
        this.close();
        this._renderItems();
        
        if (this.options.onChange) {
            this.options.onChange(null, null);
        }
    }

    _bindEvents() {
        const { variant, disabled } = this.options;

        if (disabled) return;

        // 點擊選擇器
        this.selector.addEventListener('click', (e) => {
            if (variant === 'searchable' && this.isOpen) return;
            this.toggle();
        });

        // 搜尋輸入
        if (variant === 'searchable' && this.input) {
            this.input.addEventListener('input', (e) => {
                this._filterItems(e.target.value);
            });

            this.input.addEventListener('focus', () => {
                this.open();
            });

            // 鍵盤導航
            this.input.addEventListener('keydown', (e) => {
                this._handleKeydown(e);
            });
        }

        // 點擊外部關閉
        this._onDocumentClick = (e) => {
            if (!this.container.contains(e.target)) {
                this.close();
            }
        };
        document.addEventListener('click', this._onDocumentClick);

        // Hover 效果
        this.selector.addEventListener('mouseenter', () => {
            if (!disabled) {
                this.selector.style.borderColor = 'var(--cl-primary)';
            }
        });
        this.selector.addEventListener('mouseleave', () => {
            if (!this.isOpen) {
                this.selector.style.borderColor = 'var(--cl-border)';
            }
        });
    }

    _handleKeydown(e) {
        const itemCount = this.filteredItems.length;

        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                if (!this.isOpen) this.open();
                this.highlightIndex = Math.min(this.highlightIndex + 1, itemCount - 1);
                this._scrollToHighlight();
                break;
            case 'ArrowUp':
                e.preventDefault();
                this.highlightIndex = Math.max(this.highlightIndex - 1, 0);
                this._scrollToHighlight();
                break;
            case 'Enter':
                e.preventDefault();
                if (this.highlightIndex >= 0 && this.filteredItems[this.highlightIndex]) {
                    this._selectItem(this.filteredItems[this.highlightIndex]);
                }
                break;
            case 'Escape':
                this.close();
                break;
        }
    }

    _scrollToHighlight() {
        const options = this.menu.querySelectorAll('.dropdown__option');
        if (options[this.highlightIndex]) {
            options[this.highlightIndex].scrollIntoView({ block: 'nearest' });
            // 視覺反饋
            options.forEach((opt, i) => {
                opt.style.background = i === this.highlightIndex ? 'var(--cl-bg-secondary)' : 'transparent';
            });
        }
    }

    _filterItems(query) {
        const q = query.toLowerCase().trim();
        if (!q) {
            this.filteredItems = [...this.options.items];
        } else {
            this.filteredItems = this.options.items.filter(item =>
                item.label.toLowerCase().includes(q)
            );
        }
        this._renderItems();
        this.highlightIndex = -1;
    }

    _selectItem(item) {
        this.selectedValue = item.value;
        this._setDisplayValue(item.value);
        this.close();

        if (this.options.onChange) {
            this.options.onChange(item.value, item);
        }
    }

    _setDisplayValue(value) {
        const item = this.options.items.find(i => i.value === value);
        if (!item) return;

        if (this.options.variant === 'searchable' && this.input) {
            this.input.value = item.label;
        } else if (this.display) {
            this.display.textContent = item.label;
            this.display.style.color = 'var(--cl-text)';
        }
    }

    open() {
        if (this.options.disabled || this.isOpen) return;

        this.isOpen = true;
        this.menu.style.display = 'block';
        this.selector.style.borderColor = 'var(--cl-primary)';
        this.arrow.style.transform = 'rotate(180deg)';

        if (this.options.variant === 'searchable') {
            this.filteredItems = [...this.options.items];
            this._renderItems();
        }
    }

    close() {
        if (!this.isOpen) return;

        this.isOpen = false;
        this.menu.style.display = 'none';
        this.selector.style.borderColor = 'var(--cl-border)';
        this.arrow.style.transform = 'rotate(0deg)';
        this.highlightIndex = -1;

        // 恢復顯示
        if (this.options.variant === 'searchable' && this.input && this.selectedValue !== null) {
            this._setDisplayValue(this.selectedValue);
        }
    }

    toggle() {
        this.isOpen ? this.close() : this.open();
    }

    getValue() {
        return this.selectedValue;
    }

    setValue(value) {
        this.selectedValue = value;
        this._setDisplayValue(value);
        this._renderItems();
    }

    setItems(items) {
        this.options.items = items;
        this.filteredItems = [...items];
        this._renderItems();
    }

    clear() {
        this.selectedValue = null;
        this.filteredItems = [...this.options.items];

        if (this.options.variant === 'searchable' && this.input) {
            this.input.value = '';
        } else if (this.display) {
            this.display.textContent = this.options.placeholder;
            this.display.style.color = 'var(--cl-text-placeholder)';
        }

        this._renderItems();
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        if (this._onDocumentClick) {
            document.removeEventListener('click', this._onDocumentClick);
        }
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default Dropdown;
