import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';
import { Icon } from '../../common/Icon/index.js';
import { setFieldError, clearFieldError, FIELD_ERROR_CONTRACT } from '../../utils/field-error.js';

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
            width: '100%', // RWD:未指定時跟隨容器寬(原固定 200px 在窄容器會溢出);呼叫端仍可傳固定寬
            menuMinWidth: null,
            emptyText: Locale.t('dropdown.emptyText'),
            footer: null,
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
                highlightIndex: -1,
                // Search text is only a filter, never a selectable value. Clearing it
                // when the menu closes restores the committed option label (or the
                // placeholder) instead of displaying an uncommitted arbitrary string.
                filterQuery: ''
            }),
            TOGGLE: (state) => (
                state.open
                    ? { ...state, open: false, highlightIndex: -1, filterQuery: '' }
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
                filterQuery: '',
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
                const query = String(payload?.query ?? '');
                const normalizedQuery = query.trim().toLocaleLowerCase();
                const filteredItems = !normalizedQuery
                    ? [...this.options.items]
                    : this.options.items.filter((item) =>
                        String(item.label).toLocaleLowerCase().includes(normalizedQuery)
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
        const { variant, placeholder, disabled, width, menuMinWidth } = this.options;
        const sizeStyles = this._getSizeStyles();
        const isSearchable = variant === Dropdown.VARIANTS.SEARCHABLE;

        const container = document.createElement('div');
        container.className = `dropdown dropdown--${variant}`;
        // RWD:max-width 鎖容器寬,即使呼叫端給固定寬也不溢出;min-width:0 允許 flex 情境收縮
        container.style.cssText = `
            position: relative;
            display: inline-block;
            width: ${width};
            max-width: 100%;
            min-width: 0;
            box-sizing: border-box;
            font-family: inherit;
        `;

        const selector = document.createElement('div');
        selector.className = 'dropdown__selector';
        selector.style.cssText = `
            display: flex;
            align-items: center;
            gap: 8px;
            min-height: ${sizeStyles.height};
            box-sizing: border-box;
            min-width: 0;
            padding: ${sizeStyles.padding};
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
                width: 0;
                min-width: 0;
                box-sizing: border-box;
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
                min-width: 0;
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
            position: static;
            transform: none;
            flex: 0 0 auto;
            display: flex;
            gap: 4px;
            align-items: center;
        `;

        const arrow = document.createElement('span');
        arrow.className = 'dropdown__arrow';
        this._arrowIcon = new Icon({ name: 'chevron-down', size: 12, color: 'var(--cl-text-secondary)' });
        this._arrowIcon.mount(arrow);
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
            min-width: ${menuMinWidth || '100%'};
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
            this._portalMenu(state.open);
            this.menu.style.display = state.open ? 'block' : 'none';
            if (state.open) this._positionMenu();
        }

        // destroy 後不得再掛回全域監聽（reducer 不檢查 lifecycle，這裡把關）
        this._syncGlobalListeners(state.open && state.lifecycle !== 'destroyed');

        // 可搜尋的清單可能有上千筆主檔資料。關閉時就建立全部選項節點，頁面上每個
        // Dropdown 都會各自多一份（表單重複出現上百張卡片時尤其明顯）。已選值保存在
        // state 裡，選項節點只在展開時才建立，收合時釋放。
        if (state.open) {
            this._renderItems();
        } else if (this.menu) {
            this._itemIcons?.forEach((icon) => icon.destroy());
            this._itemIcons = [];
            this.menu.replaceChildren();
        }
    }

    _valuesEqual(left, right) {
        if (left === null || left === undefined || right === null || right === undefined) {
            return left === right;
        }
        return String(left) === String(right);
    }

    _findItem(value) {
        return this.options.items.find((item) => this._valuesEqual(item.value, value)) || null;
    }

    _renderItems(menu = this.menu) {
        if (!menu) return;

        const state = this.snapshot();
        const { emptyText, placeholder } = this.options;
        this._itemIcons?.forEach((icon) => icon.destroy());
        this._itemIcons = [];
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
            this._appendFooter(menu);
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

            const isSelected = this._valuesEqual(item.value, state.selectedValue);
            const isDisabled = !!item.disabled;
            const isHighlighted = index === state.highlightIndex;

            option.style.cssText = `
                padding: 10px 12px;
                cursor: ${isDisabled ? 'not-allowed' : 'pointer'};
                transition: background var(--cl-transition-fast);
                display: flex;
                align-items: center;
                justify-content: space-between;
                gap: 8px;
                white-space: nowrap;
                font-size: var(--cl-font-size-lg);
                color: ${isDisabled ? 'var(--cl-text-light)' : 'var(--cl-text)'};
                background: ${isSelected ? 'var(--cl-primary-light)' : isHighlighted ? 'var(--cl-bg-secondary)' : 'transparent'};
            `;

            const labelSpan = document.createElement('span');
            labelSpan.textContent = item.label;
            option.appendChild(labelSpan);

            if (isSelected) {
                const check = document.createElement('span');
                const checkIcon = new Icon({ name: 'check', size: 14, color: 'var(--cl-primary)' });
                checkIcon.mount(check);
                this._itemIcons.push(checkIcon);
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
        this._appendFooter(menu);
    }

    _appendFooter(menu) {
        const footer = this.options.footer;
        if (!footer) return;
        const element = footer.element || footer;
        if (!(element instanceof Element)) return;
        element.classList.add('dropdown__footer');
        menu.appendChild(element);
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
            // 清單展開時以 fixed 定位浮出；點在清單內不算外部點擊。
            if (!this.container.contains(event.target) && !this.menu?.contains(event.target)) {
                this.close();
            }
        };
        this._onViewportChange = () => {
            if (this.snapshot().open) this._positionMenu();
        };
        this._globalListenersAttached = false;

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

    // 全域監聽只在選單展開期間掛載;開啟點擊 dispatch 中同步掛上(contains 守衛使其對本次點擊 no-op)。
    // 視窗縮放與捲動時重新定位浮出的清單。
    _syncGlobalListeners(open) {
        if (open === this._globalListenersAttached) return;
        this._globalListenersAttached = open;
        if (open) {
            document.addEventListener('click', this._onDocumentClick);
            window.addEventListener('resize', this._onViewportChange);
            window.addEventListener('scroll', this._onViewportChange, true);
        } else {
            document.removeEventListener('click', this._onDocumentClick);
            window.removeEventListener('resize', this._onViewportChange);
            window.removeEventListener('scroll', this._onViewportChange, true);
        }
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
        this.input?.blur?.();
        this.send('SET_VALUE', { value: item.value });

        if (this.options.onChange) {
            this.options.onChange(item.value, item);
        }
        // Searchable inputs can regain focus after the clicked option is re-rendered.
        // Close once more at the end of the event turn so a completed selection never
        // leaves the suggestion menu covering the following form fields.
        globalThis.queueMicrotask?.(() => this.close());
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        const nextState = this._state.send(event, payload);

        if (event === 'SET_ITEMS') {
            this.options.items = [...(payload?.items ?? [])];
            if (!this._findItem(nextState.selectedValue)) {
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

    /**
     * 標示欄位錯誤；空訊息等同 clearError()。
     * display:false 只標示錯誤狀態、不顯示文字，給自行顯示錯誤文字的外層（FormField、SearchForm）使用。
     */
    setError(message, { display = true } = {}) {
        setFieldError(this, message, { target: this.input || this.selector, visual: this.selector, container: this.element, display });
        return this;
    }

    /** 清除 setError 的標示與文字。 */
    clearError() {
        clearFieldError(this);
        return this;
    }

    get [FIELD_ERROR_CONTRACT]() {
        return true;
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
        this._arrowIcon?.destroy();
        this._arrowIcon = null;
        this._itemIcons?.forEach((icon) => icon.destroy());
        this._itemIcons = [];
        if (this._onDocumentClick) {
            document.removeEventListener('click', this._onDocumentClick);
        }
        if (this._onViewportChange) {
            window.removeEventListener('resize', this._onViewportChange);
            window.removeEventListener('scroll', this._onViewportChange, true);
        }
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }

    /**
     * 選項清單浮到最上層:打開時留在元件內(維持 DOM 契約),但改用 position:fixed 依選擇器
     * 座標定位,所以不受欄位寬度與上層容器 overflow/高度裁切、也不必調整上層元件高度;
     * 最小等於選擇器寬、依內容加寬(最多 560px 或視窗寬),視窗下方空間不足時翻到選擇器上方。
     * 若某個上層有 transform/filter(Modal、Drawer),fixed 的參考框是該元素,座標依其位置補償。
     */
    _portalMenu(open) {
        const menu = this.menu;
        if (!menu) return;
        if (open) {
            menu.dataset.floating = 'fixed';
        } else {
            delete menu.dataset.floating;
            delete menu.dataset.placement;
            menu.style.position = 'absolute';
            menu.style.top = '100%';
            menu.style.left = '0';
            menu.style.right = '0';
            menu.style.bottom = 'auto';
            menu.style.width = '';
            menu.style.minWidth = '';
            menu.style.maxWidth = '';
            menu.style.marginTop = '4px';
            menu.style.zIndex = '1000';
        }
    }

    _fixedContainingBlockOffset() {
        let ancestor = this.container?.parentElement;
        while (ancestor && ancestor !== document.body && ancestor !== document.documentElement) {
            const style = window.getComputedStyle(ancestor);
            const createsBlock = (style.transform && style.transform !== 'none')
                || (style.perspective && style.perspective !== 'none')
                || (style.filter && style.filter !== 'none')
                || /transform|perspective|filter/.test(style.willChange || '')
                || /paint|layout|strict|content/.test(style.contain || '');
            if (createsBlock) {
                const rect = ancestor.getBoundingClientRect();
                return {
                    top: rect.top + (parseFloat(style.borderTopWidth) || 0),
                    left: rect.left + (parseFloat(style.borderLeftWidth) || 0),
                };
            }
            ancestor = ancestor.parentElement;
        }
        return { top: 0, left: 0 };
    }

    _positionMenu() {
        const menu = this.menu;
        const anchor = this.selector;
        if (!menu || !anchor || !this.snapshot().open) return;
        const margin = 4;
        const viewportTop = 8;
        const viewportBottom = Math.max(viewportTop, window.innerHeight - 8);
        const viewportRight = Math.max(0, window.innerWidth - 8);
        const anchorRect = anchor.getBoundingClientRect();

        menu.style.position = 'fixed';
        menu.style.right = 'auto';
        menu.style.bottom = 'auto';
        menu.style.marginTop = '0';
        menu.style.zIndex = '10050';
        menu.style.width = 'max-content';
        menu.style.minWidth = `${Math.round(anchorRect.width)}px`;
        menu.style.maxWidth = `${Math.max(120, Math.min(560, viewportRight - 8))}px`;

        const rect = menu.getBoundingClientRect();
        const height = rect.height || menu.scrollHeight || 0;
        const width = rect.width || menu.scrollWidth || anchorRect.width;
        const spaceBelow = viewportBottom - anchorRect.bottom - margin;
        const spaceAbove = anchorRect.top - viewportTop - margin;
        const placeAbove = height > spaceBelow && spaceAbove > spaceBelow;
        const top = placeAbove ? Math.max(viewportTop, anchorRect.top - margin - height) : anchorRect.bottom + margin;
        const left = Math.max(8, Math.min(anchorRect.left, viewportRight - width));
        const offset = this._fixedContainingBlockOffset();
        menu.style.top = `${Math.round(top - offset.top)}px`;
        menu.style.left = `${Math.round(left - offset.left)}px`;
        menu.dataset.placement = placeAbove ? 'top' : 'bottom';
    }

}

export default Dropdown;
