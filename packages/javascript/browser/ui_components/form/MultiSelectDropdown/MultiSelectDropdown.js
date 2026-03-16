import { ModalPanel } from '../../layout/Panel/index.js';
import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';

const cloneItems = (items) => Array.isArray(items) ? items.map((item) => ({ ...item })) : [];
const normalizeValues = (values, items) => {
    const valid = new Set(items.map((item) => item.value));
    const next = [];
    for (const value of values || []) {
        if (valid.has(value) && !next.includes(value)) next.push(value);
    }
    return next;
};
const sameValueSet = (left, right) => left.length === right.length && left.every((value) => new Set(right).has(value));

export class MultiSelectDropdown {
    constructor(options = {}) {
        this.options = {
            items: [],
            placeholder: Locale.t('multiSelect.placeholder'),
            values: [],
            onChange: null,
            size: 'medium',
            disabled: false,
            width: '300px',
            emptyText: Locale.t('multiSelect.emptyText'),
            modalTitle: Locale.t('multiSelect.modalTitle'),
            maxCount: Infinity,
            minCount: 0,
            ...options
        };

        this.isOpen = false;
        this.selectedValues = new Set(normalizeValues(this.options.values, this.options.items));
        this.filteredItems = cloneItems(this.options.items);
        this.highlightIndex = -1;

        this.element = null;
        this._container = null;
        this._selector = null;
        this._tagsWrap = null;
        this._input = null;
        this._expandBtn = null;
        this._arrow = null;
        this._menu = null;

        this.element = this._createElement();
        this._state = createComponentState(this._buildInitialState(), {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed', open: false }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden', open: false }),
            OPEN: (state) => state.availability === 'disabled' || state.open ? state : { ...state, open: true, filteredItems: cloneItems(this.options.items), filterQuery: '', highlightIndex: -1 },
            CLOSE: (state) => ({ ...state, open: false, filteredItems: cloneItems(this.options.items), filterQuery: '', highlightIndex: -1 }),
            TOGGLE: (state) => state.availability === 'disabled' ? state : (state.open ? { ...state, open: false, filteredItems: cloneItems(this.options.items), filterQuery: '', highlightIndex: -1 } : { ...state, open: true, filteredItems: cloneItems(this.options.items), filterQuery: '', highlightIndex: -1 }),
            SET_DISABLED: (state, payload) => ({ ...state, availability: payload?.disabled ? 'disabled' : 'enabled', open: payload?.disabled ? false : state.open, filterQuery: payload?.disabled ? '' : state.filterQuery, highlightIndex: payload?.disabled ? -1 : state.highlightIndex, filteredItems: payload?.disabled ? cloneItems(this.options.items) : state.filteredItems }),
            FILTER: (state, payload) => {
                const query = String(payload?.query ?? '').trim().toLowerCase();
                const filteredItems = !query ? cloneItems(this.options.items) : this.options.items.filter((item) => String(item.label ?? '').toLowerCase().includes(query));
                return { ...state, filterQuery: String(payload?.query ?? ''), filteredItems, open: true, highlightIndex: -1 };
            },
            SET_HIGHLIGHT: (state, payload) => ({ ...state, highlightIndex: payload?.index ?? -1 }),
            TOGGLE_VALUE: (state, payload) => {
                const value = payload?.value;
                const item = this.options.items.find((entry) => entry.value === value);
                if (!item || item.disabled) return state;
                const selected = [...state.selectedValues];
                const index = selected.indexOf(value);
                if (index >= 0) {
                    if (selected.length <= this.options.minCount) return state;
                    selected.splice(index, 1);
                } else {
                    if (selected.length >= this.options.maxCount) return state;
                    selected.push(value);
                }
                return { ...state, selectedValues: selected };
            },
            SET_VALUES: (state, payload) => ({ ...state, selectedValues: normalizeValues(payload?.values ?? [], this.options.items) }),
            SET_ITEMS: (state, payload) => {
                const items = cloneItems(payload?.items ?? []);
                const selectedValues = normalizeValues(state.selectedValues, items);
                const query = String(state.filterQuery ?? '').trim().toLowerCase();
                const filteredItems = !query ? items : items.filter((item) => String(item.label ?? '').toLowerCase().includes(query));
                return { ...state, selectedValues, filteredItems, highlightIndex: -1 };
            },
            CLEAR: (state) => ({ ...state, selectedValues: [], filterQuery: '', filteredItems: cloneItems(this.options.items), highlightIndex: -1 })
        });

        this._bindEvents();
        this._applyState();
    }

    _buildInitialState() {
        const items = cloneItems(this.options.items);
        return {
            lifecycle: 'created',
            visibility: 'visible',
            availability: this.options.disabled ? 'disabled' : 'enabled',
            open: false,
            selectedValues: normalizeValues(this.options.values, items),
            filteredItems: items,
            highlightIndex: -1,
            filterQuery: ''
        };
    }

    _getSizeStyles() {
        return {
            small: { padding: '4px 8px', fontSize: 'var(--cl-font-size-sm)', minHeight: '30px', tagSize: '20px', tagFont: 'var(--cl-font-size-xs)' },
            medium: { padding: '6px 10px', fontSize: 'var(--cl-font-size-lg)', minHeight: '36px', tagSize: '24px', tagFont: 'var(--cl-font-size-sm)' },
            large: { padding: '8px 12px', fontSize: 'var(--cl-font-size-xl)', minHeight: '44px', tagSize: '28px', tagFont: 'var(--cl-font-size-md)' }
        }[this.options.size] || { padding: '6px 10px', fontSize: 'var(--cl-font-size-lg)', minHeight: '36px', tagSize: '24px', tagFont: 'var(--cl-font-size-sm)' };
    }

    _createElement() {
        const ss = this._getSizeStyles();
        const container = document.createElement('div');
        container.className = 'msd';
        container.tabIndex = -1;
        container.style.cssText = `position:relative;display:inline-block;width:${this.options.width};font-family:inherit;`;

        const selector = document.createElement('div');
        selector.className = 'msd__selector';
        selector.style.cssText = `display:flex;align-items:center;gap:6px;flex-wrap:wrap;min-height:${ss.minHeight};padding:${ss.padding};padding-right:56px;background:var(--cl-bg);border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);cursor:pointer;transition:border-color var(--cl-transition);position:relative;`;

        const tagsWrap = document.createElement('div');
        tagsWrap.className = 'msd__tags';
        tagsWrap.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;flex:1;align-items:center;min-width:0;';

        const input = document.createElement('input');
        input.className = 'msd__input';
        input.type = 'text';
        input.placeholder = this.options.placeholder;
        input.style.cssText = `border:none;outline:none;background:transparent;font-size:${ss.fontSize};min-width:60px;flex:1;cursor:text;`;
        tagsWrap.appendChild(input);

        const actions = document.createElement('div');
        actions.className = 'msd__actions';
        actions.style.cssText = 'position:absolute;right:4px;top:50%;transform:translateY(-50%);display:flex;gap:2px;align-items:center;';

        const expandBtn = document.createElement('button');
        expandBtn.className = 'msd__expand-btn';
        expandBtn.type = 'button';
        expandBtn.title = Locale.t('multiSelect.expandAll');
        expandBtn.innerHTML = `<svg width="16" height="16" viewBox="0 0 16 16" fill="none"><rect x="2" y="2" width="12" height="12" rx="2" stroke="var(--cl-text-secondary)" stroke-width="1.5" fill="none"/><path d="M5 6.5h6M5 9.5h6" stroke="var(--cl-text-secondary)" stroke-width="1.2" stroke-linecap="round"/></svg>`;
        expandBtn.style.cssText = 'display:flex;align-items:center;justify-content:center;width:26px;height:26px;border:none;background:transparent;border-radius:var(--cl-radius-sm);cursor:pointer;transition:background var(--cl-transition-fast);padding:0;';

        const arrow = document.createElement('span');
        arrow.className = 'msd__arrow';
        arrow.innerHTML = `<svg width="12" height="12" viewBox="0 0 12 12" fill="none"><path d="M3 4.5L6 7.5L9 4.5" stroke="var(--cl-text-secondary)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
        arrow.style.cssText = 'display:flex;transition:transform 0.2s;';

        actions.appendChild(expandBtn);
        actions.appendChild(arrow);
        selector.appendChild(tagsWrap);
        selector.appendChild(actions);

        const menu = document.createElement('div');
        menu.className = 'msd__menu';
        menu.style.cssText = 'position:absolute;top:100%;left:0;right:0;margin-top:4px;background:var(--cl-bg);border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);box-shadow:var(--cl-shadow-md);max-height:240px;overflow-y:auto;z-index:1000;display:none;';

        container.appendChild(selector);
        container.appendChild(menu);

        this._container = container;
        this._selector = selector;
        this._tagsWrap = tagsWrap;
        this._input = input;
        this._expandBtn = expandBtn;
        this._arrow = arrow;
        this._menu = menu;
        return container;
    }

    _getSortedItems(items = this.snapshot().filteredItems) {
        const selected = [];
        const unselected = [];
        const selectedSet = new Set(this.snapshot().selectedValues);
        for (const item of items) {
            (selectedSet.has(item.value) ? selected : unselected).push(item);
        }
        return [...selected, ...unselected];
    }

    _bindEvents() {
        this._selector.addEventListener('click', (event) => {
            if (this.snapshot().availability === 'disabled') return;
            if (event.target === this._expandBtn || this._expandBtn.contains(event.target)) return;
            if (!this.snapshot().open) this.open();
        });
        this._input.addEventListener('input', () => {
            if (this.snapshot().availability === 'disabled') return;
            this._filterItems(this._input.value);
        });
        this._input.addEventListener('focus', () => {
            if (this.snapshot().availability !== 'disabled' && !this.snapshot().open) this.open();
        });
        this._input.addEventListener('keydown', (event) => this._handleKeydown(event));
        this._boundHandleOutsideClick = (event) => {
            if (!this._container.contains(event.target)) this.close();
        };
        document.addEventListener('click', this._boundHandleOutsideClick);
        this._expandBtn.addEventListener('click', (event) => {
            event.stopPropagation?.();
            if (this.snapshot().availability !== 'disabled') this._openModal();
        });
        this._selector.addEventListener('mouseenter', () => {
            if (this.snapshot().availability !== 'disabled') this._selector.style.borderColor = 'var(--cl-primary)';
        });
        this._selector.addEventListener('mouseleave', () => {
            if (!this.snapshot().open) this._selector.style.borderColor = 'var(--cl-border)';
        });
    }

    _syncLegacyFields(state) {
        this.isOpen = state.open;
        this.selectedValues = new Set(state.selectedValues);
        this.filteredItems = cloneItems(state.filteredItems);
        this.highlightIndex = state.highlightIndex;
        this.options.disabled = state.availability === 'disabled';
    }

    _renderTags() {
        const state = this.snapshot();
        const ss = this._getSizeStyles();
        const selectedSet = new Set(state.selectedValues);
        const selectedItems = this.options.items.filter((item) => selectedSet.has(item.value));
        this._tagsWrap.innerHTML = '';
        for (const item of selectedItems) {
            const tag = document.createElement('span');
            tag.className = 'msd__tag';
            tag.style.cssText = `display:inline-flex;align-items:center;gap:3px;height:${ss.tagSize};padding:0 6px;background:var(--cl-primary-light);color:var(--cl-primary-dark);border-radius:var(--cl-radius-sm);font-size:${ss.tagFont};white-space:nowrap;max-width:120px;`;
            const label = document.createElement('span');
            label.textContent = item.label;
            label.style.cssText = 'overflow:hidden;text-overflow:ellipsis;';
            tag.appendChild(label);
            if (state.availability !== 'disabled') {
                const canRemove = state.selectedValues.length > this.options.minCount;
                const removeBtn = document.createElement('span');
                removeBtn.className = 'msd__tag-remove';
                removeBtn.innerHTML = '&times;';
                removeBtn.style.cssText = `cursor:${canRemove ? 'pointer' : 'not-allowed'};font-size:var(--cl-font-size-lg);line-height:1;margin-left:2px;opacity:${canRemove ? '0.7' : '0.3'};`;
                if (canRemove) {
                    removeBtn.addEventListener('click', (event) => {
                        event.stopPropagation?.();
                        this._toggleValue(item.value);
                    });
                }
                tag.appendChild(removeBtn);
            }
            this._tagsWrap.appendChild(tag);
        }
        this._input.placeholder = selectedItems.length === 0 ? this.options.placeholder : '';
        this._input.value = state.filterQuery;
        this._tagsWrap.appendChild(this._input);
    }

    _renderMenuItems() {
        const state = this.snapshot();
        const sorted = this._getSortedItems(state.filteredItems);
        const selectedSet = new Set(state.selectedValues);
        this._menu.innerHTML = '';
        if (sorted.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'msd__empty';
            empty.textContent = this.options.emptyText;
            empty.style.cssText = 'padding:12px;text-align:center;color:var(--cl-text-placeholder);font-size:var(--cl-font-size-md);';
            this._menu.appendChild(empty);
            return;
        }
        const atMax = state.selectedValues.length >= this.options.maxCount;
        const atMin = state.selectedValues.length <= this.options.minCount;
        sorted.forEach((item, index) => {
            const isSelected = selectedSet.has(item.value);
            const isItemDisabled = !!item.disabled || (!isSelected && atMax) || (isSelected && atMin);
            const option = document.createElement('div');
            option.className = 'msd__option';
            option.dataset.value = item.value;
            option.dataset.index = String(index);
            option.style.cssText = `padding:8px 12px;cursor:${isItemDisabled ? 'not-allowed' : 'pointer'};transition:background var(--cl-transition-fast);display:flex;align-items:center;gap:8px;font-size:var(--cl-font-size-lg);color:${isItemDisabled && !isSelected ? 'var(--cl-text-light)' : 'var(--cl-text)'};background:${state.highlightIndex === index ? 'var(--cl-bg-secondary)' : 'transparent'};`;
            const checkbox = document.createElement('span');
            checkbox.className = 'msd__checkbox';
            checkbox.style.cssText = `display:inline-flex;align-items:center;justify-content:center;width:16px;height:16px;border:2px solid ${isSelected ? 'var(--cl-primary)' : 'var(--cl-border-dark)'};border-radius:var(--cl-radius-sm);background:${isSelected ? 'var(--cl-primary)' : 'var(--cl-bg)'};transition:all var(--cl-transition-fast);flex-shrink:0;opacity:${isItemDisabled ? '0.5' : '1'};`;
            if (isSelected) checkbox.innerHTML = `<svg width="10" height="10" viewBox="0 0 10 10" fill="none"><path d="M2 5L4 7L8 3" stroke="var(--cl-text-inverse)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
            const label = document.createElement('span');
            label.textContent = item.label;
            label.style.cssText = 'flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
            option.appendChild(checkbox);
            option.appendChild(label);
            if (!isItemDisabled) {
                option.addEventListener('mouseenter', () => this.send('SET_HIGHLIGHT', { index }));
                option.addEventListener('click', (event) => {
                    event.stopPropagation?.();
                    this._toggleValue(item.value);
                });
            }
            this._menu.appendChild(option);
        });
    }

    _applyState() {
        const state = this.snapshot();
        this._syncLegacyFields(state);
        this._container.style.display = state.visibility === 'hidden' ? 'none' : 'inline-block';
        this._selector.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'pointer';
        this._selector.style.opacity = state.availability === 'disabled' ? '0.6' : '1';
        this._selector.style.background = state.availability === 'disabled' ? 'var(--cl-bg-secondary)' : 'var(--cl-bg)';
        this._selector.style.borderColor = state.open ? 'var(--cl-primary)' : 'var(--cl-border)';
        this._input.disabled = state.availability === 'disabled';
        this._input.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'text';
        this._input.value = state.filterQuery;
        this._expandBtn.disabled = state.availability === 'disabled';
        this._expandBtn.style.cursor = state.availability === 'disabled' ? 'not-allowed' : 'pointer';
        this._expandBtn.style.opacity = state.availability === 'disabled' ? '0.5' : '1';
        this._arrow.style.transform = state.open ? 'rotate(180deg)' : 'rotate(0deg)';
        this._menu.style.display = state.open ? 'block' : 'none';
        this._renderTags();
        this._renderMenuItems();
    }

    _fireChange(values = this.getValues()) {
        if (!this.options.onChange) return;
        const selectedSet = new Set(values);
        const items = this.options.items.filter((item) => selectedSet.has(item.value));
        this.options.onChange(values, items);
    }

    _toggleValue(value) {
        const previous = this.getValues();
        const next = this.send('TOGGLE_VALUE', { value });
        if (!sameValueSet(previous, next.selectedValues)) this._fireChange([...next.selectedValues]);
    }

    _filterItems(query) {
        this.send('FILTER', { query });
    }

    _handleKeydown(event) {
        const state = this.snapshot();
        const sorted = this._getSortedItems(state.filteredItems);
        const count = sorted.length;
        switch (event.key) {
            case 'ArrowDown':
                event.preventDefault?.();
                if (!state.open) this.open();
                this.send('SET_HIGHLIGHT', { index: Math.min(state.highlightIndex + 1, count - 1) });
                this._scrollToHighlight();
                break;
            case 'ArrowUp':
                event.preventDefault?.();
                this.send('SET_HIGHLIGHT', { index: Math.max(state.highlightIndex - 1, 0) });
                this._scrollToHighlight();
                break;
            case ' ':
            case 'Enter':
                if (state.filterQuery === '' && state.highlightIndex >= 0 && sorted[state.highlightIndex]) {
                    event.preventDefault?.();
                    this._toggleValue(sorted[state.highlightIndex].value);
                }
                break;
            case 'Escape':
                event.preventDefault?.();
                this.close();
                break;
            case 'Backspace':
                if (state.filterQuery === '' && state.selectedValues.length > this.options.minCount) {
                    const lastValue = state.selectedValues[state.selectedValues.length - 1];
                    if (lastValue !== undefined) this._toggleValue(lastValue);
                }
                break;
        }
    }

    _scrollToHighlight() {
        const options = this._menu?.querySelectorAll('.msd__option') || [];
        options[this.snapshot().highlightIndex]?.scrollIntoView?.({ block: 'nearest' });
    }

    _openModal() {
        let modalValues = new Set(this.snapshot().selectedValues);
        let modalFilteredItems = cloneItems(this.options.items);
        const modal = new ModalPanel({ title: this.options.modalTitle, closable: true, autoClose: true });
        const content = document.createElement('div');
        content.style.cssText = 'display:flex;flex-direction:column;width:480px;max-width:90vw;';
        const countLabel = document.createElement('span');
        countLabel.style.cssText = 'font-size:var(--cl-font-size-md);color:var(--cl-text-muted);margin-left:8px;font-weight:400;';
        const searchInput = document.createElement('input');
        searchInput.type = 'text';
        searchInput.placeholder = Locale.t('multiSelect.searchPlaceholder');
        searchInput.style.cssText = 'width:100%;padding:8px 12px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);font-size:var(--cl-font-size-lg);outline:none;box-sizing:border-box;';
        const searchRow = document.createElement('div');
        searchRow.style.cssText = 'padding:12px 0 8px;';
        searchRow.appendChild(searchInput);
        const listWrap = document.createElement('div');
        listWrap.style.cssText = 'overflow-y:auto;padding:8px 0;min-height:200px;max-height:400px;';
        const footer = document.createElement('div');
        footer.style.cssText = 'padding:12px 0 0;border-top:1px solid var(--cl-border-light);display:flex;justify-content:space-between;align-items:center;';
        const leftBtns = document.createElement('div');
        leftBtns.style.cssText = 'display:flex;gap:8px;';
        const rightBtns = document.createElement('div');
        rightBtns.style.cssText = 'display:flex;gap:8px;';
        const style = (bg, color) => `padding:8px 16px;border:none;border-radius:var(--cl-radius-md);font-size:var(--cl-font-size-md);cursor:pointer;background:${bg};color:${color};`;
        const selectAllBtn = document.createElement('button');
        selectAllBtn.type = 'button';
        selectAllBtn.textContent = Locale.t('multiSelect.selectAll');
        selectAllBtn.style.cssText = style('var(--cl-primary-light)', 'var(--cl-primary-dark)');
        const deselectAllBtn = document.createElement('button');
        deselectAllBtn.type = 'button';
        deselectAllBtn.textContent = Locale.t('multiSelect.deselectAll');
        deselectAllBtn.style.cssText = style('var(--cl-bg-danger-light)', 'var(--cl-danger)');
        const cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.textContent = Locale.t('multiSelect.cancel');
        cancelBtn.style.cssText = style('var(--cl-bg-secondary)', 'var(--cl-text-secondary)');
        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.textContent = Locale.t('multiSelect.confirm');
        confirmBtn.style.cssText = style('var(--cl-primary)', 'var(--cl-text-inverse)');
        leftBtns.appendChild(selectAllBtn);
        leftBtns.appendChild(deselectAllBtn);
        rightBtns.appendChild(cancelBtn);
        rightBtns.appendChild(confirmBtn);
        footer.appendChild(leftBtns);
        footer.appendChild(rightBtns);
        content.appendChild(countLabel);
        content.appendChild(searchRow);
        content.appendChild(listWrap);
        content.appendChild(footer);

        const updateCount = () => {
            const max = this.options.maxCount === Infinity ? '' : `/${this.options.maxCount}`;
            countLabel.textContent = Locale.t('multiSelect.selectedCount', { count: modalValues.size, max });
        };
        const renderList = () => {
            listWrap.innerHTML = '';
            const selected = [];
            const unselected = [];
            for (const item of modalFilteredItems) (modalValues.has(item.value) ? selected : unselected).push(item);
            const all = [...selected, ...unselected];
            if (all.length === 0) {
                const empty = document.createElement('div');
                empty.textContent = this.options.emptyText;
                empty.style.cssText = 'padding:20px;text-align:center;color:var(--cl-text-placeholder);';
                listWrap.appendChild(empty);
                return;
            }
            const atMax = modalValues.size >= this.options.maxCount;
            const atMin = modalValues.size <= this.options.minCount;
            all.forEach((item) => {
                const isSelected = modalValues.has(item.value);
                const isDisabled = !!item.disabled || (!isSelected && atMax) || (isSelected && atMin);
                const row = document.createElement('label');
                row.style.cssText = `display:flex;align-items:center;gap:10px;padding:8px 12px;cursor:${isDisabled ? 'not-allowed' : 'pointer'};font-size:var(--cl-font-size-lg);color:${isDisabled && !isSelected ? 'var(--cl-text-light)' : 'var(--cl-text)'};`;
                const checkbox = document.createElement('input');
                checkbox.type = 'checkbox';
                checkbox.checked = isSelected;
                checkbox.disabled = isDisabled;
                checkbox.style.cssText = 'width:16px;height:16px;accent-color:var(--cl-primary);cursor:inherit;';
                checkbox.addEventListener('change', () => {
                    if (checkbox.checked) {
                        if (modalValues.size < this.options.maxCount) modalValues.add(item.value);
                        else checkbox.checked = false;
                    } else if (modalValues.size > this.options.minCount) modalValues.delete(item.value);
                    else checkbox.checked = true;
                    updateCount();
                    renderList();
                });
                const label = document.createElement('span');
                label.textContent = item.label;
                label.style.cssText = 'flex:1;';
                row.appendChild(checkbox);
                row.appendChild(label);
                listWrap.appendChild(row);
            });
        };

        searchInput.addEventListener('input', () => {
            const query = searchInput.value.toLowerCase().trim();
            modalFilteredItems = !query ? cloneItems(this.options.items) : this.options.items.filter((item) => String(item.label ?? '').toLowerCase().includes(query));
            renderList();
        });
        selectAllBtn.addEventListener('click', () => {
            for (const item of modalFilteredItems) {
                if (item.disabled) continue;
                if (modalValues.size >= this.options.maxCount) break;
                modalValues.add(item.value);
            }
            updateCount();
            renderList();
        });
        deselectAllBtn.addEventListener('click', () => {
            for (const item of modalFilteredItems) {
                if (item.disabled) continue;
                if (modalValues.size <= this.options.minCount) break;
                modalValues.delete(item.value);
            }
            updateCount();
            renderList();
        });
        confirmBtn.addEventListener('click', () => {
            const before = this.getValues();
            const nextValues = [...modalValues];
            this.setValues(nextValues);
            if (!sameValueSet(before, nextValues)) this._fireChange(this.getValues());
            modal.close();
        });
        cancelBtn.addEventListener('click', () => modal.close());

        updateCount();
        renderList();
        modal.setContent(content);
        modal.mount();
        modal.open();
        setTimeout(() => searchInput.focus?.(), 0);
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        this._state.send(event, payload);
        if (event === 'SET_ITEMS') this.options.items = cloneItems(payload?.items ?? []);
        this._applyState();
        return this.snapshot();
    }

    getValues() {
        return [...this.selectedValues];
    }

    setValues(values) {
        this.send('SET_VALUES', { values });
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

    open() {
        if (this.options.disabled || this.isOpen) return;
        this.send('OPEN');
        this._input.focus?.();
    }

    close() {
        if (!this.isOpen) return;
        this.send('CLOSE');
    }

    toggle() {
        this.send('TOGGLE');
        if (this.snapshot().open) this._input.focus?.();
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
        document.removeEventListener('click', this._boundHandleOutsideClick);
        if (this.element?.parentNode) this.element.remove();
    }
}

export default MultiSelectDropdown;
