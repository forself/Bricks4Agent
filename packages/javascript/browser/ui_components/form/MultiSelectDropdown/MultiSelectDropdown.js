/**
 * MultiSelectDropdown Component
 * 多選下拉選單元件 - 支援搜尋篩選、選中置頂、展開 Modal 全選操作
 */

import { ModalPanel } from '../../layout/Panel/index.js';

import Locale from '../../i18n/index.js';
export class MultiSelectDropdown {
    /**
     * @param {Object} options
     * @param {Array} options.items - 選項 [{value, label, disabled?}]
     * @param {string} options.placeholder - 預設提示文字
     * @param {Array} options.values - 初始已選值陣列
     * @param {Function} options.onChange - (values[], items[]) => void
     * @param {string} options.size - 尺寸 (small, medium, large)
     * @param {boolean} options.disabled - 停用
     * @param {string} options.width - 寬度
     * @param {string} options.emptyText - 無結果文字
     * @param {string} options.modalTitle - Modal 標題
     * @param {number} options.maxCount - 最大可選數量
     * @param {number} options.minCount - 最小選取數量
     */
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
        this.selectedValues = new Set(this.options.values);
        this.filteredItems = [...this.options.items];
        this.highlightIndex = -1;
        this._boundHandleOutsideClick = this._handleOutsideClick.bind(this);

        this.element = this._createElement();
        this._bindEvents();
    }

    // ─── 尺寸 ───

    _getSizeStyles() {
        const sizes = {
            small: { padding: '4px 8px', fontSize: 'var(--cl-font-size-sm)', minHeight: '30px', tagSize: '20px', tagFont: 'var(--cl-font-size-xs)' },
            medium: { padding: '6px 10px', fontSize: 'var(--cl-font-size-lg)', minHeight: '36px', tagSize: '24px', tagFont: 'var(--cl-font-size-sm)' },
            large: { padding: '8px 12px', fontSize: 'var(--cl-font-size-xl)', minHeight: '44px', tagSize: '28px', tagFont: 'var(--cl-font-size-md)' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    // ─── DOM 建立 ───

    _createElement() {
        const { disabled, width } = this.options;
        const ss = this._getSizeStyles();

        // 根容器
        const container = document.createElement('div');
        container.className = 'msd';
        container.tabIndex = -1;
        container.style.cssText = `position:relative;display:inline-block;width:${width};font-family:inherit;`;

        // 選擇器
        const selector = document.createElement('div');
        selector.className = 'msd__selector';
        selector.style.cssText = `
            display:flex;align-items:center;gap:6px;flex-wrap:wrap;
            min-height:${ss.minHeight};padding:${ss.padding};padding-right:56px;
            background: var(--cl-bg);border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);
            cursor:${disabled ? 'not-allowed' : 'pointer'};
            transition:border-color var(--cl-transition);opacity:${disabled ? '0.6' : '1'};
            position:relative;
        `;

        // Tags 容器
        const tagsWrap = document.createElement('div');
        tagsWrap.className = 'msd__tags';
        tagsWrap.style.cssText = 'display:flex;flex-wrap:wrap;gap:4px;flex:1;align-items:center;min-width:0;';
        selector.appendChild(tagsWrap);
        this._tagsWrap = tagsWrap;

        // 搜尋輸入
        const input = document.createElement('input');
        input.className = 'msd__input';
        input.type = 'text';
        input.placeholder = this.options.placeholder;
        input.disabled = disabled;
        input.style.cssText = `
            border:none;outline:none;background:transparent;
            font-size:${ss.fontSize};min-width:60px;flex:1;
            cursor:${disabled ? 'not-allowed' : 'text'};
        `;
        tagsWrap.appendChild(input);
        this._input = input;

        // 右側按鈕區
        const actions = document.createElement('div');
        actions.style.cssText = 'position:absolute;right:4px;top:50%;transform:translateY(-50%);display:flex;gap:2px;align-items:center;';

        // 展開按鈕
        const expandBtn = document.createElement('button');
        expandBtn.className = 'msd__expand-btn';
        expandBtn.type = 'button';
        expandBtn.disabled = disabled;
        expandBtn.title = Locale.t('multiSelect.expandAll');
        expandBtn.innerHTML = `<svg width="16" height="16" viewBox="0 0 16 16" fill="none">
            <rect x="2" y="2" width="12" height="12" rx="2" stroke="var(--cl-text-secondary)" stroke-width="1.5" fill="none"/>
            <path d="M5 6.5h6M5 9.5h6" stroke="var(--cl-text-secondary)" stroke-width="1.2" stroke-linecap="round"/>
        </svg>`;
        expandBtn.style.cssText = `
            display:flex;align-items:center;justify-content:center;
            width:26px;height:26px;border:none;background:transparent;
            border-radius:var(--cl-radius-sm);cursor:pointer;transition:background var(--cl-transition-fast);padding:0;
        `;

        // 箭頭
        const arrow = document.createElement('span');
        arrow.className = 'msd__arrow';
        arrow.innerHTML = `<svg width="12" height="12" viewBox="0 0 12 12" fill="none">
            <path d="M3 4.5L6 7.5L9 4.5" stroke="var(--cl-text-secondary)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
        </svg>`;
        arrow.style.cssText = 'display:flex;transition:transform 0.2s;';

        actions.appendChild(expandBtn);
        actions.appendChild(arrow);
        selector.appendChild(actions);
        this._expandBtn = expandBtn;
        this._arrow = arrow;

        // 下拉選單
        const menu = document.createElement('div');
        menu.className = 'msd__menu';
        menu.style.cssText = `
            position:absolute;top:100%;left:0;right:0;margin-top:4px;
            background: var(--cl-bg);border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);
            box-shadow:var(--cl-shadow-md);max-height:240px;
            overflow-y:auto;z-index:1000;display:none;
        `;

        container.appendChild(selector);
        container.appendChild(menu);

        this._container = container;
        this._selector = selector;
        this._menu = menu;

        // 渲染初始狀態
        this._renderTags();
        this._renderMenuItems();

        return container;
    }

    // ─── Tags 渲染 ───

    _renderTags() {
        const ss = this._getSizeStyles();
        // 移除舊 tags（保留 input）
        const oldTags = this._tagsWrap.querySelectorAll('.msd__tag');
        oldTags.forEach(t => t.remove());

        if (this.selectedValues.size === 0) {
            this._input.placeholder = this.options.placeholder;
            return;
        }

        this._input.placeholder = '';

        // 依原始順序排列已選項目
        const selectedItems = this.options.items.filter(item => this.selectedValues.has(item.value));
        selectedItems.forEach(item => {
            const tag = document.createElement('span');
            tag.className = 'msd__tag';
            tag.style.cssText = `
                display:inline-flex;align-items:center;gap:3px;
                height:${ss.tagSize};padding:0 6px;
                background:var(--cl-primary-light);color:var(--cl-primary-dark);border-radius:var(--cl-radius-sm);
                font-size:${ss.tagFont};white-space:nowrap;max-width:120px;
            `;

            const labelSpan = document.createElement('span');
            labelSpan.textContent = item.label;
            labelSpan.style.cssText = 'overflow:hidden;text-overflow:ellipsis;';
            tag.appendChild(labelSpan);

            if (!this.options.disabled) {
                const canRemove = this.selectedValues.size > this.options.minCount;
                const removeBtn = document.createElement('span');
                removeBtn.className = 'msd__tag-remove';
                removeBtn.innerHTML = '&times;';
                removeBtn.style.cssText = `
                    cursor:${canRemove ? 'pointer' : 'not-allowed'};
                    font-size:var(--cl-font-size-lg);line-height:1;margin-left:2px;
                    opacity:${canRemove ? '0.7' : '0.3'};
                `;
                if (canRemove) {
                    removeBtn.addEventListener('click', (e) => {
                        e.stopPropagation();
                        this._toggleValue(item.value);
                    });
                    removeBtn.addEventListener('mouseenter', () => { removeBtn.style.opacity = '1'; });
                    removeBtn.addEventListener('mouseleave', () => { removeBtn.style.opacity = '0.7'; });
                }
                tag.appendChild(removeBtn);
            }

            // 插入 input 之前
            this._tagsWrap.insertBefore(tag, this._input);
        });
    }

    // ─── 選單項目渲染 ───

    _getSortedItems(items) {
        // 選中置頂，同狀態保持原始順序
        const selected = [];
        const unselected = [];
        items.forEach(item => {
            if (this.selectedValues.has(item.value)) {
                selected.push(item);
            } else {
                unselected.push(item);
            }
        });
        return [...selected, ...unselected];
    }

    _renderMenuItems() {
        this._menu.innerHTML = '';
        const sorted = this._getSortedItems(this.filteredItems);

        if (sorted.length === 0) {
            const empty = document.createElement('div');
            empty.className = 'msd__empty';
            empty.textContent = this.options.emptyText;
            empty.style.cssText = 'padding:12px;text-align:center;color:var(--cl-text-placeholder);font-size:var(--cl-font-size-md);';
            this._menu.appendChild(empty);
            return;
        }

        const atMax = this.selectedValues.size >= this.options.maxCount;
        const atMin = this.selectedValues.size <= this.options.minCount;

        sorted.forEach((item, index) => {
            const isSelected = this.selectedValues.has(item.value);
            const isItemDisabled = item.disabled || (!isSelected && atMax) || (isSelected && atMin);

            const option = document.createElement('div');
            option.className = 'msd__option';
            option.dataset.value = item.value;
            option.dataset.index = index;
            option.style.cssText = `
                padding:8px 12px;cursor:${isItemDisabled ? 'not-allowed' : 'pointer'};
                transition:background var(--cl-transition-fast);display:flex;align-items:center;gap:8px;
                font-size:var(--cl-font-size-lg);color:${isItemDisabled && !isSelected ? 'var(--cl-text-light)' : 'var(--cl-text)'};
                background:${this.highlightIndex === index ? 'var(--cl-bg-secondary)' : 'transparent'};
            `;

            // Checkbox
            const cb = document.createElement('span');
            cb.className = 'msd__checkbox';
            cb.style.cssText = `
                display:inline-flex;align-items:center;justify-content:center;
                width:16px;height:16px;border:2px solid ${isSelected ? 'var(--cl-primary)' : 'var(--cl-border-dark)'};
                border-radius:var(--cl-radius-sm);background:${isSelected ? 'var(--cl-primary)' : 'var(--cl-bg)'};
                transition:all var(--cl-transition-fast);flex-shrink:0;
                opacity:${isItemDisabled ? '0.5' : '1'};
            `;
            if (isSelected) {
                cb.innerHTML = `<svg width="10" height="10" viewBox="0 0 10 10" fill="none">
                    <path d="M2 5L4 7L8 3" stroke="var(--cl-text-inverse)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                </svg>`;
            }
            option.appendChild(cb);

            const label = document.createElement('span');
            label.textContent = item.label;
            label.style.cssText = 'flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;';
            option.appendChild(label);

            if (!isItemDisabled) {
                option.addEventListener('mouseenter', () => {
                    option.style.background = 'var(--cl-bg-secondary)';
                    this.highlightIndex = index;
                });
                option.addEventListener('mouseleave', () => {
                    option.style.background = 'transparent';
                });
                option.addEventListener('click', (e) => {
                    e.stopPropagation();
                    this._toggleValue(item.value);
                });
            }

            this._menu.appendChild(option);
        });
    }

    // ─── 事件綁定 ───

    _bindEvents() {
        if (this.options.disabled) return;

        // 點擊選擇器開關
        this._selector.addEventListener('click', (e) => {
            if (e.target === this._expandBtn || this._expandBtn.contains(e.target)) return;
            if (!this.isOpen) this.open();
        });

        // 搜尋
        this._input.addEventListener('input', () => {
            this._filterItems(this._input.value);
        });

        this._input.addEventListener('focus', () => {
            if (!this.isOpen) this.open();
        });

        // 鍵盤
        this._input.addEventListener('keydown', (e) => this._handleKeydown(e));

        // 失去焦點自動關閉
        this._container.addEventListener('focusout', (e) => {
            // 檢查新焦點是否仍在容器內
            requestAnimationFrame(() => {
                if (!this._container.contains(document.activeElement)) {
                    this.close();
                }
            });
        });

        // 點擊外部關閉
        document.addEventListener('click', this._boundHandleOutsideClick);

        // 展開按鈕
        this._expandBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            this._openModal();
        });

        // Hover 效果
        this._selector.addEventListener('mouseenter', () => {
            this._selector.style.borderColor = 'var(--cl-primary)';
        });
        this._selector.addEventListener('mouseleave', () => {
            if (!this.isOpen) this._selector.style.borderColor = 'var(--cl-border)';
        });
    }

    _handleOutsideClick(e) {
        if (!this._container.contains(e.target)) {
            this.close();
        }
    }

    _handleKeydown(e) {
        const sorted = this._getSortedItems(this.filteredItems);
        const count = sorted.length;

        switch (e.key) {
            case 'ArrowDown':
                e.preventDefault();
                if (!this.isOpen) this.open();
                this.highlightIndex = Math.min(this.highlightIndex + 1, count - 1);
                this._scrollToHighlight();
                break;
            case 'ArrowUp':
                e.preventDefault();
                this.highlightIndex = Math.max(this.highlightIndex - 1, 0);
                this._scrollToHighlight();
                break;
            case ' ':
                // Space 切換選中（只在非輸入內容時）
                if (this._input.value === '' && this.highlightIndex >= 0 && sorted[this.highlightIndex]) {
                    e.preventDefault();
                    const item = sorted[this.highlightIndex];
                    const isSelected = this.selectedValues.has(item.value);
                    const atMax = this.selectedValues.size >= this.options.maxCount;
                    const atMin = this.selectedValues.size <= this.options.minCount;
                    if (!item.disabled && !((!isSelected && atMax) || (isSelected && atMin))) {
                        this._toggleValue(item.value);
                    }
                }
                break;
            case 'Escape':
                e.preventDefault();
                this.close();
                break;
            case 'Backspace':
                // 搜尋框為空時，刪除最後一個 tag
                if (this._input.value === '' && this.selectedValues.size > this.options.minCount) {
                    const vals = [...this.selectedValues];
                    if (vals.length > 0) {
                        this._toggleValue(vals[vals.length - 1]);
                    }
                }
                break;
        }
    }

    _scrollToHighlight() {
        this._renderMenuItems();
        const options = this._menu.querySelectorAll('.msd__option');
        if (options[this.highlightIndex]) {
            options[this.highlightIndex].scrollIntoView({ block: 'nearest' });
        }
    }

    // ─── 篩選 ───

    _filterItems(query) {
        const q = query.toLowerCase().trim();
        if (!q) {
            this.filteredItems = [...this.options.items];
        } else {
            this.filteredItems = this.options.items.filter(item =>
                item.label.toLowerCase().includes(q)
            );
        }
        this.highlightIndex = -1;
        this._renderMenuItems();
    }

    // ─── 選取切換 ───

    _toggleValue(value) {
        if (this.selectedValues.has(value)) {
            if (this.selectedValues.size <= this.options.minCount) return;
            this.selectedValues.delete(value);
        } else {
            if (this.selectedValues.size >= this.options.maxCount) return;
            this.selectedValues.add(value);
        }

        this._renderTags();
        this._renderMenuItems();
        this._fireChange();
    }

    _fireChange() {
        if (this.options.onChange) {
            const vals = [...this.selectedValues];
            const items = this.options.items.filter(i => this.selectedValues.has(i.value));
            this.options.onChange(vals, items);
        }
    }

    // ─── 開關 ───

    open() {
        if (this.options.disabled || this.isOpen) return;
        this.isOpen = true;
        this._menu.style.display = 'block';
        this._selector.style.borderColor = 'var(--cl-primary)';
        this._arrow.style.transform = 'rotate(180deg)';
        this.filteredItems = [...this.options.items];
        this._input.value = '';
        this.highlightIndex = -1;
        this._renderMenuItems();
        this._input.focus();
    }

    close() {
        if (!this.isOpen) return;
        this.isOpen = false;
        this._menu.style.display = 'none';
        this._selector.style.borderColor = 'var(--cl-border)';
        this._arrow.style.transform = 'rotate(0deg)';
        this._input.value = '';
        this.highlightIndex = -1;
        this.filteredItems = [...this.options.items];
    }

    // ─── Modal ───

    _openModal() {
        // 暫存當前狀態（取消時恢復）
        let modalValues = new Set(this.selectedValues);
        let modalFilteredItems = [...this.options.items];

        // 建立 ModalPanel
        const modal = new ModalPanel({
            title: this.options.modalTitle,
            closable: true,
            autoClose: true
        });

        // ── 建立 Modal 內容 ──

        const content = document.createElement('div');
        content.style.cssText = 'display:flex;flex-direction:column;width:480px;max-width:90vw;';

        // 計數標籤
        const countLabel = document.createElement('span');
        countLabel.style.cssText = 'font-size:var(--cl-font-size-md);color:var(--cl-text-muted);margin-left:8px;font-weight:400;';

        // 搜尋列
        const searchRow = document.createElement('div');
        searchRow.style.cssText = 'padding:12px 0 8px;';
        const searchInput = document.createElement('input');
        searchInput.type = 'text';
        searchInput.placeholder = Locale.t('multiSelect.searchPlaceholder');
        searchInput.style.cssText = `
            width:100%;padding:8px 12px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);
            font-size:var(--cl-font-size-lg);outline:none;transition:border-color var(--cl-transition);box-sizing:border-box;
        `;
        searchInput.addEventListener('focus', () => { searchInput.style.borderColor = 'var(--cl-primary)'; });
        searchInput.addEventListener('blur', () => { searchInput.style.borderColor = 'var(--cl-border)'; });
        searchRow.appendChild(searchInput);

        // 選項列表
        const listWrap = document.createElement('div');
        listWrap.style.cssText = 'overflow-y:auto;padding:8px 0;min-height:200px;max-height:400px;';

        // 底部按鈕
        const footer = document.createElement('div');
        footer.style.cssText = `
            padding:12px 0 0;border-top:1px solid var(--cl-border-light);
            display:flex;justify-content:space-between;align-items:center;
        `;

        const leftBtns = document.createElement('div');
        leftBtns.style.cssText = 'display:flex;gap:8px;';

        const btnStyle = (bg, color) => `
            padding:8px 16px;border:none;border-radius:var(--cl-radius-md);font-size:var(--cl-font-size-md);
            cursor:pointer;background:${bg};color:${color};transition:opacity var(--cl-transition-fast);
        `;

        const selectAllBtn = document.createElement('button');
        selectAllBtn.type = 'button';
        selectAllBtn.textContent = Locale.t('multiSelect.selectAll');
        selectAllBtn.style.cssText = btnStyle('var(--cl-primary-light)', 'var(--cl-primary-dark)');

        const deselectAllBtn = document.createElement('button');
        deselectAllBtn.type = 'button';
        deselectAllBtn.textContent = Locale.t('multiSelect.deselectAll');
        deselectAllBtn.style.cssText = btnStyle('var(--cl-bg-danger-light)', 'var(--cl-danger)');

        leftBtns.appendChild(selectAllBtn);
        leftBtns.appendChild(deselectAllBtn);

        const rightBtns = document.createElement('div');
        rightBtns.style.cssText = 'display:flex;gap:8px;';

        const cancelBtn = document.createElement('button');
        cancelBtn.type = 'button';
        cancelBtn.textContent = Locale.t('multiSelect.cancel');
        cancelBtn.style.cssText = btnStyle('var(--cl-bg-secondary)', 'var(--cl-text-secondary)');

        const confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.textContent = Locale.t('multiSelect.confirm');
        confirmBtn.style.cssText = btnStyle('var(--cl-primary)', 'var(--cl-text-inverse)');

        rightBtns.appendChild(cancelBtn);
        rightBtns.appendChild(confirmBtn);

        footer.appendChild(leftBtns);
        footer.appendChild(rightBtns);

        content.appendChild(countLabel);
        content.appendChild(searchRow);
        content.appendChild(listWrap);
        content.appendChild(footer);

        // ── Modal 內部方法 ──

        const updateCount = () => {
            const max = this.options.maxCount === Infinity ? '' : `/${this.options.maxCount}`;
            countLabel.textContent = Locale.t('multiSelect.selectedCount', { count: modalValues.size, max });
        };

        const renderModalList = () => {
            listWrap.innerHTML = '';
            // 選中置頂
            const sorted = [];
            const unsel = [];
            modalFilteredItems.forEach(item => {
                if (modalValues.has(item.value)) sorted.push(item);
                else unsel.push(item);
            });
            const all = [...sorted, ...unsel];

            if (all.length === 0) {
                const empty = document.createElement('div');
                empty.textContent = this.options.emptyText;
                empty.style.cssText = 'padding:20px;text-align:center;color:var(--cl-text-placeholder);';
                listWrap.appendChild(empty);
                return;
            }

            const atMax = modalValues.size >= this.options.maxCount;
            const atMin = modalValues.size <= this.options.minCount;

            all.forEach(item => {
                const isSelected = modalValues.has(item.value);
                const isDisabled = item.disabled || (!isSelected && atMax) || (isSelected && atMin);

                const row = document.createElement('label');
                row.style.cssText = `
                    display:flex;align-items:center;gap:10px;padding:8px 12px;
                    cursor:${isDisabled ? 'not-allowed' : 'pointer'};
                    transition:background 0.1s;font-size:var(--cl-font-size-lg);color:${isDisabled && !isSelected ? 'var(--cl-text-light)' : 'var(--cl-text)'};
                `;
                if (!isDisabled) {
                    row.addEventListener('mouseenter', () => { row.style.background = 'var(--cl-bg-tertiary)'; });
                    row.addEventListener('mouseleave', () => { row.style.background = 'transparent'; });
                }

                const cb = document.createElement('input');
                cb.type = 'checkbox';
                cb.checked = isSelected;
                cb.disabled = isDisabled;
                cb.style.cssText = 'width:16px;height:16px;accent-color:var(--cl-primary);cursor:inherit;';
                cb.addEventListener('change', () => {
                    if (cb.checked) {
                        if (modalValues.size < this.options.maxCount) {
                            modalValues.add(item.value);
                        } else {
                            cb.checked = false;
                        }
                    } else {
                        if (modalValues.size > this.options.minCount) {
                            modalValues.delete(item.value);
                        } else {
                            cb.checked = true;
                        }
                    }
                    updateCount();
                    renderModalList();
                });

                const labelText = document.createElement('span');
                labelText.textContent = item.label;
                labelText.style.cssText = 'flex:1;';

                row.appendChild(cb);
                row.appendChild(labelText);
                listWrap.appendChild(row);
            });
        };

        // 搜尋
        searchInput.addEventListener('input', () => {
            const q = searchInput.value.toLowerCase().trim();
            if (!q) {
                modalFilteredItems = [...this.options.items];
            } else {
                modalFilteredItems = this.options.items.filter(i =>
                    i.label.toLowerCase().includes(q)
                );
            }
            renderModalList();
        });

        // 全選（僅篩選結果內且不超過 maxCount）
        selectAllBtn.addEventListener('click', () => {
            modalFilteredItems.filter(i => !i.disabled).forEach(item => {
                if (modalValues.size < this.options.maxCount) {
                    modalValues.add(item.value);
                }
            });
            updateCount();
            renderModalList();
        });

        // 全不選（僅篩選結果內且不低於 minCount）
        deselectAllBtn.addEventListener('click', () => {
            modalFilteredItems.filter(i => !i.disabled).forEach(item => {
                if (modalValues.size > this.options.minCount) {
                    modalValues.delete(item.value);
                }
            });
            updateCount();
            renderModalList();
        });

        // 確定
        confirmBtn.addEventListener('click', () => {
            this.selectedValues = modalValues;
            this._renderTags();
            this._renderMenuItems();
            this._fireChange();
            modal.close();
        });

        // 取消
        cancelBtn.addEventListener('click', () => {
            modal.close();
        });

        // 初始渲染
        updateCount();
        renderModalList();

        // 掛載並開啟
        modal.setContent(content);
        modal.mount();
        modal.open();

        // 自動聚焦搜尋框
        setTimeout(() => searchInput.focus(), 100);
    }

    // ─── 公開 API ───

    getValues() {
        return [...this.selectedValues];
    }

    setValues(values) {
        this.selectedValues = new Set(values);
        this._renderTags();
        this._renderMenuItems();
    }

    setItems(items) {
        this.options.items = items;
        this.filteredItems = [...items];
        // 移除不再存在的選中值
        const validValues = new Set(items.map(i => i.value));
        this.selectedValues = new Set([...this.selectedValues].filter(v => validValues.has(v)));
        this._renderTags();
        this._renderMenuItems();
    }

    clear() {
        this.selectedValues = new Set();
        this._input.value = '';
        this.filteredItems = [...this.options.items];
        this._renderTags();
        this._renderMenuItems();
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        document.removeEventListener('click', this._boundHandleOutsideClick);
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default MultiSelectDropdown;
