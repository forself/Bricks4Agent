import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';

export class Dropdown {
    static VARIANTS = {
        BASIC: 'basic',
        SEARCHABLE: 'searchable'
    };

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
        this.filterQuery = '';

        this.input = null;
        this.display = null;
        this.arrow = null;
        this.menu = null;
        this.selector = null;
        this.container = null;

        this.element = this._createElement();
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed', open: false }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden', open: false }),
            OPEN: (state) => {
                if (state.availability === 'disabled' || state.open) return state;
                return {
                    ...state,
                    open: true,
                    filteredItems: this.options.variant === Dropdown.VARIANTS.SEARCHABLE
                        ? [...this.options.items]
                        : state.filteredItems
                };
            },
            CLOSE: (state) => ({
                ...state,
                open: false,
                highlightIndex: -1
            }),
            TOGGLE: (state) => (
                state.open
                    ? { ...state, open: false, highlightIndex: -1 }
                    : (state.availability === 'disabled'
                        ? state
                        : {
                            ...state,
                            open: true,
                            filteredItems: this.options.variant === Dropdown.VARIANTS.SEARCHABLE
                                ? [...this.options.items]
                                : state.filteredItems
                        })
            ),
            SET_VALUE: (state, payload) => ({
                ...state,
                selectedValue: payload?.value ?? null,
                open: false,
                highlightIndex: -1
            }),
            CLEAR: (state) => ({
                ...state,
                selectedValue: null,
                filterQuery: '',
                filteredItems: [...this.options.items],
                open: false,
                highlightIndex: -1
            }),
            SET_ITEMS: (state, payload) => ({
                ...state,
                filteredItems: [...(payload?.items ?? [])],
                highlightIndex: -1
            }),
            SET_DISABLED: (state, payload) => ({
                ...state,
                availability: payload?.disabled ? 'disabled' : 'enabled',
                open: payload?.disabled ? false : state.open,
                highlightIndex: payload?.disabled ? -1 : state.highlightIndex
            }),
            FILTER: (state, payload) => {
                const query = String(payload?.query ?? '').toLowerCase().trim();
                const filteredItems = !query
                    ? [...this.options.items]
                    : this.options.items.filter((item) =>
                        String(item.label).toLowerCase().includes(query)
                    );
                return {
                    ...state,
                    filterQuery: query,
                    filteredItems,
                    open: true,
                    highlightIndex: -1
                };
            },
            SET_HIGHLIGHT: (state, payload) => ({
                ...state,
                highlightIndex: payload?.index ?? -1
            })
        });

        this._bindEvents();
        this._applyState();
    }

    _buildInitialState() {
        return {
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            open: false,
            selectedValue: this.options.value,
            filteredItems: [...this.options.items],
            highlightIndex: -1,
            filterQuery: ''
        };
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
        const isSearchable = variant === Dropdown.VARIANTS.SEARCHABLE;

        const container = document.createElement('div');
        container.className = `dropdown dropdown--${variant}`;
        container.style.cssText = `
            position: relative;
            display: inline-block;
            width: ${width};
            font-family: inherit;
        `;

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

        const arrow = document.createElement('span');
        arrow.className = 'dropdown__arrow';
        arrow.innerHTML = `<svg width="12" height="12" viewBox="0 0 12 12" fill="none">
            <path d="M3 4.5L6 7.5L9 4.5" stroke="var(--cl-text-secondary)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>`;
        arrow.style.cssText = 'display: flex; transition: transform var(--cl-transition);';
        icons.appendChild(arrow);
        selector.appendChild(icons);

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

        container.appendChild(selector);
        container.appendChild(menu);

        this.container = container;
        this.selector = selector;
        this.menu = menu;
        this.arrow = arrow;

        return container;
    }

    _syncLegacyFields(state) {
        this.isOpen = state.open;
        this.selectedValue = state.selectedValue;
        this.filteredItems = [...state.filteredItems];
        this.highlightIndex = state.highlightIndex;
        this.filterQuery = state.filterQuery;
        this.options.disabled = state.availability === 'disabled';
    }

    _applyState() {
        const state = this.snapshot();
        this._syncLegacyFields(state);

        if (this.container) {
            this.container.style.display = state.visibility === 'hidden' ? 'none' : 'inline-block';
        }

        if (this.selector) {
            this.selector.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'pointer';
            this.selector.style.opacity = state.availability === 'disabled' ? '0.6' : '1';
            this.selector.style.background = state.availability === 'disabled' ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)';
            this.selector.style.borderColor = state.open ? 'var(--cl-primary)' : 'var(--cl-border)';
        }

        if (this.input) {
            this.input.disabled = state.availability === 'disabled';
            this.input.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'text';
            const selectedItem = this._findItem(state.selectedValue);
            this.input.value = state.filterQuery || selectedItem?.label || '';
        }

        if (this.display) {
            const selectedItem = this._findItem(state.selectedValue);
            if (selectedItem) {
                this.display.textContent = selectedItem.label;
                this.display.style.color = 'var(--cl-text)';
            } else {
                this.display.textContent = this.options.placeholder;
                this.display.style.color = 'var(--cl-text-placeholder)';
            }
        }

        if (this.arrow) {
            this.arrow.style.transform = state.open ? 'rotate(180deg)' : 'rotate(0deg)';
        }

        if (this.menu) {
            this.menu.style.display = state.open ? 'block' : 'none';
        }

        this._renderItems();
    }

    _findItem(value) {
        return this.options.items.find((item) => item.value === value) || null;
    }

    _renderItems(menu = this.menu) {
        if (!menu) return;

        const state = this.snapshot();
        const { emptyText, placeholder } = this.options;
        menu.innerHTML = '';

        if (state.filteredItems.length === 0) {
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

        const emptyOption = document.createElement('div');
        emptyOption.className = 'dropdown__option dropdown__option--empty';
        emptyOption.dataset.value = '';
        emptyOption.dataset.index = '-1';
        const isEmptySelected = state.selectedValue === null || state.selectedValue === '' || state.selectedValue === undefined;
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
        emptyLabel.textContent = placeholder || '-- Select --';
        emptyOption.appendChild(emptyLabel);
        emptyOption.addEventListener('mouseenter', () => {
            if (!isEmptySelected) emptyOption.style.background = 'var(--cl-bg-secondary)';
        });
        emptyOption.addEventListener('mouseleave', () => {
            if (!isEmptySelected) emptyOption.style.background = 'transparent';
        });
        emptyOption.addEventListener('click', () => {
            if (state.availability === 'disabled') return;
            this._clearSelection();
        });
        menu.appendChild(emptyOption);

        state.filteredItems.forEach((item, index) => {
            const option = document.createElement('div');
            option.className = 'dropdown__option';
            option.dataset.value = item.value;
            option.dataset.index = String(index);

            const isSelected = item.value === state.selectedValue;
            const isDisabled = !!item.disabled;
            const isHighlighted = index === state.highlightIndex;

            option.style.cssText = `
                padding: 10px 12px;
                cursor: ${isDisabled ? 'not-allowed' : 'pointer'};
                transition: background var(--cl-transition-fast);
                display: flex;
                align-items: center;
                justify-content: space-between;
                font-size: var(--cl-font-size-lg);
                color: ${isDisabled ? 'var(--cl-text-light)' : 'var(--cl-text)'};
                background: ${isSelected ? 'var(--cl-primary-light)' : isHighlighted ? 'var(--cl-bg-secondary)' : 'transparent'};
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
                    if (!isSelected) {
                        option.style.background = 'var(--cl-bg-secondary)';
                    }
                });
                option.addEventListener('mouseleave', () => {
                    if (!isSelected) {
                        option.style.background = 'transparent';
                    }
                });
                option.addEventListener('click', () => {
                    this._selectItem(item);
                });
            }

            menu.appendChild(option);
        });
    }

    _clearSelection() {
        this.send('CLEAR');

        if (this.options.onChange) {
            this.options.onChange(null, null);
        }
    }

    _bindEvents() {
        this.selector.addEventListener('click', () => {
            if (this.snapshot().availability === 'disabled') return;
            if (this.options.variant === Dropdown.VARIANTS.SEARCHABLE && this.snapshot().open) return;
            this.toggle();
        });

        if (this.options.variant === Dropdown.VARIANTS.SEARCHABLE && this.input) {
            this.input.addEventListener('input', (event) => {
                if (this.snapshot().availability === 'disabled') return;
                this._filterItems(event.target.value);
            });

            this.input.addEventListener('focus', () => {
                if (this.snapshot().availability === 'disabled') return;
                this.open();
            });

            this.input.addEventListener('keydown', (event) => {
                if (this.snapshot().availability === 'disabled') return;
                this._handleKeydown(event);
            });
        }

        this._onDocumentClick = (event) => {
            if (!this.container.contains(event.target)) {
                this.close();
            }
        };
        document.addEventListener('click', this._onDocumentClick);

        this.selector.addEventListener('mouseenter', () => {
            if (this.snapshot().availability !== 'disabled') {
                this.selector.style.borderColor = 'var(--cl-primary)';
            }
        });

        this.selector.addEventListener('mouseleave', () => {
            if (!this.snapshot().open) {
                this.selector.style.borderColor = 'var(--cl-border)';
            }
        });
    }

    _handleKeydown(event) {
        const itemCount = this.snapshot().filteredItems.length;

        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault?.();
                if (!this.snapshot().open) this.open();
                this.send('SET_HIGHLIGHT', {
                    index: Math.min(this.snapshot().highlightIndex + 1, itemCount - 1)
                });
                this._scrollToHighlight();
                break;
            case 'ArrowUp':
                event.preventDefault?.();
                this.send('SET_HIGHLIGHT', {
                    index: Math.max(this.snapshot().highlightIndex - 1, 0)
                });
                this._scrollToHighlight();
                break;
            case 'Enter':
                event.preventDefault?.();
                if (this.snapshot().highlightIndex >= 0 && this.snapshot().filteredItems[this.snapshot().highlightIndex]) {
                    this._selectItem(this.snapshot().filteredItems[this.snapshot().highlightIndex]);
                }
                break;
            case 'Escape':
                this.close();
                break;
        }
    }

    _scrollToHighlight() {
        const options = this.menu?.querySelectorAll('.dropdown__option') || [];
        const option = options[this.snapshot().highlightIndex];
        option?.scrollIntoView?.({ block: 'nearest' });
    }

    _filterItems(query) {
        this.send('FILTER', { query });
    }

    _selectItem(item) {
        this.send('SET_VALUE', { value: item.value });

        if (this.options.onChange) {
            this.options.onChange(item.value, item);
        }
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        const nextState = this._state.send(event, payload);

        if (event === 'SET_ITEMS') {
            this.options.items = [...(payload?.items ?? [])];
            if (!this.options.items.some((item) => item.value === nextState.selectedValue)) {
                this._state.replace({
                    ...nextState,
                    selectedValue: null,
                    filteredItems: [...this.options.items]
                });
            }
        }

        this._applyState();
        return this.snapshot();
    }

    open() {
        if (this.options.disabled || this.isOpen) return;
        this.send('OPEN');
    }

    close() {
        if (!this.isOpen) return;
        this.send('CLOSE');
    }

    toggle() {
        this.send('TOGGLE');
    }

    getValue() {
        return this.selectedValue;
    }

    setValue(value) {
        this.send('SET_VALUE', { value });
    }

    setItems(items) {
        this.send('SET_ITEMS', { items });
    }

    setDisabled(disabled) {
        this.send('SET_DISABLED', { disabled });
    }

    clear() {
        this.send('CLEAR');
    }

    show() {
        this.send('SHOW');
    }

    hide() {
        this.send('HIDE');
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
            this.send('MOUNT');
        }
        return this;
    }

    destroy() {
        this.send('DESTROY');
        if (this._onDocumentClick) {
            document.removeEventListener('click', this._onDocumentClick);
        }
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default Dropdown;
