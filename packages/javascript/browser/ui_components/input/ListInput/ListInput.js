import Locale from '../../i18n/index.js';
import { createComponentState } from '../../utils/component-state.js';

const cloneItems = (items) => Array.isArray(items) ? items.map((item) => item && typeof item === 'object' ? { ...item } : item) : [];

export class ListInput {
    constructor(options = {}) {
        this.options = {
            title: '',
            minItems: 0,
            maxItems: 10,
            addButtonText: Locale.t('listInput.addButton'),
            fields: null,
            renderItem: null,
            onItemChange: null,
            onChange: null,
            ...options
        };

        this.items = [];
        this.itemElements = [];
        this.draggedIndex = null;
        this.element = null;
        this.listContainer = null;
        this.addButton = null;
        this.counter = null;

        this.element = this._createElement();
        this._state = createComponentState({
            lifecycle: 'created',
            visibility: 'visible',
            items: [],
            draggedIndex: null
        }, {
            MOUNT: (state) => ({ ...state, lifecycle: 'mounted' }),
            DESTROY: (state) => ({ ...state, lifecycle: 'destroyed', draggedIndex: null }),
            SHOW: (state) => ({ ...state, visibility: 'visible' }),
            HIDE: (state) => ({ ...state, visibility: 'hidden' }),
            SET_ITEMS: (state, payload) => ({ ...state, items: cloneItems(payload?.items ?? []) }),
            SET_DRAGGED_INDEX: (state, payload) => ({ ...state, draggedIndex: payload?.index ?? null })
        });

        for (let index = 0; index < this.options.minItems; index += 1) {
            this._addItem(null, true);
        }

        this._syncStateFromItems();
    }

    _createElement() {
        const container = document.createElement('div');
        container.className = 'list-input';
        container.style.cssText = 'border:1px solid var(--cl-border-light);border-radius:var(--cl-radius-lg);padding:16px;background:var(--cl-bg);';

        if (this.options.title) {
            const header = document.createElement('div');
            header.style.cssText = 'display:flex;justify-content:space-between;align-items:center;margin-bottom:12px;';

            const titleArea = document.createElement('div');
            titleArea.style.cssText = 'display:flex;align-items:center;gap:12px;';

            const title = document.createElement('h3');
            title.textContent = this.options.title;
            title.style.cssText = 'font-size:var(--cl-font-size-xl);color:var(--cl-text);margin:0;';
            titleArea.appendChild(title);

            this.counter = document.createElement('span');
            this.counter.style.cssText = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);';
            titleArea.appendChild(this.counter);
            header.appendChild(titleArea);

            if (this.options.fields?.length) {
                const templateBtn = document.createElement('button');
                templateBtn.type = 'button';
                templateBtn.textContent = 'CSV';
                templateBtn.title = Locale.t('listInput.csvTemplate');
                templateBtn.style.cssText = 'padding:6px 12px;border:1px solid var(--cl-primary-dark);background:var(--cl-bg);color:var(--cl-primary-dark);font-size:var(--cl-font-size-sm);border-radius:var(--cl-radius-sm);cursor:pointer;';
                templateBtn.addEventListener('click', () => this._downloadTemplate());
                header.appendChild(templateBtn);
            }

            container.appendChild(header);
        }

        this.listContainer = document.createElement('div');
        this.listContainer.className = 'list-input__items';
        this.listContainer.style.cssText = 'display:flex;flex-direction:column;gap:12px;';
        container.appendChild(this.listContainer);

        this.addButton = document.createElement('button');
        this.addButton.type = 'button';
        this.addButton.textContent = `+ ${this.options.addButtonText}`;
        this.addButton.style.cssText = 'margin-top:12px;width:100%;padding:8px;border:1px dashed var(--cl-grey-light);background:var(--cl-bg-input);color:var(--cl-text-secondary);cursor:pointer;border-radius:var(--cl-radius-sm);transition:all var(--cl-transition);';
        this.addButton.addEventListener('click', () => this._addItem());
        container.appendChild(this.addButton);

        return container;
    }

    _syncStateFromItems() {
        if (!this._state) return;
        this.send('SET_ITEMS', { items: this.items });
    }

    _syncLegacyFromState(state) {
        this.items = cloneItems(state.items);
        this.draggedIndex = state.draggedIndex;
    }

    _applyState() {
        const state = this.snapshot();
        this._syncLegacyFromState(state);
        if (this.element) {
            this.element.style.display = state.visibility === 'hidden' ? 'none' : 'block';
        }
        this._updateUI();
    }

    snapshot() {
        return this._state.snapshot();
    }

    send(event, payload = null) {
        this._state.send(event, payload);
        this._applyState();
        return this.snapshot();
    }

    _defaultItemValue() {
        return {};
    }

    _emitChange() {
        if (this.options.onChange) {
            this.options.onChange(this.getValues());
        }
    }

    _addItem(initialValue = null, silent = false) {
        if (this.items.length >= this.options.maxItems) return;

        const index = this.items.length;
        const itemContainer = document.createElement('div');
        itemContainer.className = 'list-input__item';
        itemContainer.setAttribute('data-index', String(index));
        itemContainer.draggable = true;
        itemContainer.style.cssText = 'display:flex;align-items:flex-start;gap:8px;padding:12px;background:var(--cl-bg-tertiary);border-radius:var(--cl-radius-md);position:relative;transition:background var(--cl-transition-fast),transform var(--cl-transition-fast);';

        const dragHandle = document.createElement('div');
        dragHandle.className = 'drag-handle';
        dragHandle.textContent = '::';
        dragHandle.title = Locale.t('listInput.dragToSort');
        dragHandle.style.cssText = 'cursor:grab;color:var(--cl-text-light);font-size:var(--cl-font-size-lg);padding:4px 2px;user-select:none;';
        itemContainer.appendChild(dragHandle);

        const indexLabel = document.createElement('div');
        indexLabel.className = 'index-label';
        indexLabel.textContent = `${index + 1}.`;
        indexLabel.style.cssText = 'font-size:var(--cl-font-size-lg);color:var(--cl-text-placeholder);padding-top:8px;min-width:20px;';
        itemContainer.appendChild(indexLabel);

        const content = document.createElement('div');
        content.style.cssText = 'flex:1;';
        itemContainer.appendChild(content);

        const actionBtns = document.createElement('div');
        actionBtns.className = 'action-btns';
        actionBtns.style.cssText = 'display:flex;flex-direction:column;gap:2px;';

        const moveUpBtn = document.createElement('button');
        moveUpBtn.className = 'move-up-btn';
        moveUpBtn.type = 'button';
        moveUpBtn.textContent = 'up';
        moveUpBtn.title = Locale.t('listInput.moveUp');
        moveUpBtn.style.cssText = 'width:24px;height:20px;border:none;background:transparent;color:var(--cl-text-placeholder);font-size:var(--cl-font-size-2xs);cursor:pointer;display:flex;align-items:center;justify-content:center;border-radius:var(--cl-radius-sm);padding:0;';
        moveUpBtn.addEventListener('click', () => this._moveItem(index, -1));
        actionBtns.appendChild(moveUpBtn);

        const moveDownBtn = document.createElement('button');
        moveDownBtn.className = 'move-down-btn';
        moveDownBtn.type = 'button';
        moveDownBtn.textContent = 'dn';
        moveDownBtn.title = Locale.t('listInput.moveDown');
        moveDownBtn.style.cssText = moveUpBtn.style.cssText;
        moveDownBtn.addEventListener('click', () => this._moveItem(index, 1));
        actionBtns.appendChild(moveDownBtn);
        itemContainer.appendChild(actionBtns);

        const deleteBtn = document.createElement('button');
        deleteBtn.className = 'delete-btn';
        deleteBtn.type = 'button';
        deleteBtn.textContent = 'x';
        deleteBtn.title = Locale.t('listInput.removeItem');
        deleteBtn.style.cssText = 'width:24px;height:24px;border:none;background:transparent;color:var(--cl-text-placeholder);font-size:20px;cursor:pointer;display:flex;align-items:center;justify-content:center;border-radius:var(--cl-radius-round);padding:0;line-height:1;';
        deleteBtn.addEventListener('click', () => this._removeItem(index));
        itemContainer.appendChild(deleteBtn);

        itemContainer.addEventListener('dragstart', (event) => {
            this.send('SET_DRAGGED_INDEX', { index });
            itemContainer.style.opacity = '0.5';
            if (event.dataTransfer) event.dataTransfer.effectAllowed = 'move';
        });
        itemContainer.addEventListener('dragend', () => {
            itemContainer.style.opacity = '1';
            this.send('SET_DRAGGED_INDEX', { index: null });
        });
        itemContainer.addEventListener('dragover', (event) => {
            event.preventDefault?.();
            if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
            itemContainer.style.background = 'var(--cl-primary-light)';
        });
        itemContainer.addEventListener('dragleave', () => {
            itemContainer.style.background = 'var(--cl-bg-tertiary)';
        });
        itemContainer.addEventListener('drop', (event) => {
            event.preventDefault?.();
            itemContainer.style.background = 'var(--cl-bg-tertiary)';
            if (this.draggedIndex !== null && this.draggedIndex !== index) {
                const draggedItem = this.items[this.draggedIndex];
                this.items.splice(this.draggedIndex, 1);
                this.items.splice(index, 0, draggedItem);
                this._rerender();
                this._emitChange();
            }
            this.send('SET_DRAGGED_INDEX', { index: null });
        });

        this.listContainer.appendChild(itemContainer);
        this.items.push(initialValue ?? this._defaultItemValue());
        this.itemElements.push(itemContainer);

        const updateValue = (newValue) => {
            this.items[index] = newValue;
            this._syncStateFromItems();
            if (this.options.onItemChange) this.options.onItemChange(index, newValue);
            this._emitChange();
        };

        if (this.options.renderItem) {
            this.options.renderItem(content, index, initialValue, updateValue);
        } else if (this.options.fields) {
            this._renderFieldsItem(content, index, initialValue, updateValue);
        }

        this._updateUI();
        if (!silent) {
            this._syncStateFromItems();
            this._emitChange();
        }
    }

    _moveItem(index, direction) {
        const nextIndex = index + direction;
        if (nextIndex < 0 || nextIndex >= this.items.length) return;
        [this.items[index], this.items[nextIndex]] = [this.items[nextIndex], this.items[index]];
        this._rerender();
        this._emitChange();
    }

    _removeItem(index) {
        if (this.items.length <= this.options.minItems) return;
        this.itemElements[index]?.remove();
        this.items.splice(index, 1);
        this.itemElements.splice(index, 1);
        this._rerender();
        this._emitChange();
    }

    _renderFieldsItem(container, index, value, updateValue) {
        container.style.cssText = 'display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;';
        const fieldInputs = {};
        const currentValue = value || {};

        const triggerUpdate = () => {
            const nextValue = {};
            for (const [name, input] of Object.entries(fieldInputs)) {
                nextValue[name] = input.type === 'checkbox' ? input.checked : input.value;
            }
            updateValue(nextValue);
        };

        this.options.fields.forEach((field) => {
            const wrapper = document.createElement('div');
            wrapper.style.cssText = `display:flex;flex-direction:column;gap:4px;${field.flex ? `flex:${field.flex};` : ''}${field.width ? `width:${field.width};` : 'min-width:80px;'}`;

            if (field.label) {
                const label = document.createElement('label');
                label.textContent = field.label;
                label.style.cssText = 'font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);';
                if (field.required) {
                    const asterisk = document.createElement('span');
                    asterisk.textContent = ' *';
                    asterisk.style.color = 'var(--cl-danger)';
                    label.appendChild(asterisk);
                }
                wrapper.appendChild(label);
            }

            let input;
            const baseStyle = 'height:36px;padding:0 10px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-lg);outline:none;box-sizing:border-box;width:100%;';
            switch (field.type) {
                case 'select':
                    input = document.createElement('select');
                    input.style.cssText = `${baseStyle}cursor:pointer;`;
                    {
                        const defaultOpt = document.createElement('option');
                        defaultOpt.value = '';
                        defaultOpt.textContent = field.placeholder || Locale.t('listInput.selectPlaceholder');
                        input.appendChild(defaultOpt);
                    }
                    (field.options || []).forEach((optionValue) => {
                        const option = document.createElement('option');
                        option.value = typeof optionValue === 'object' ? optionValue.value : optionValue;
                        option.textContent = typeof optionValue === 'object' ? optionValue.label : optionValue;
                        input.appendChild(option);
                    });
                    break;
                case 'number':
                    input = document.createElement('input');
                    input.type = 'number';
                    input.style.cssText = baseStyle;
                    if (field.min !== undefined) input.min = field.min;
                    if (field.max !== undefined) input.max = field.max;
                    break;
                case 'checkbox':
                    input = document.createElement('input');
                    input.type = 'checkbox';
                    input.style.cssText = 'width:18px;height:18px;margin:8px 0;';
                    break;
                case 'email':
                case 'tel':
                case 'date':
                    input = document.createElement('input');
                    input.type = field.type;
                    input.style.cssText = baseStyle;
                    break;
                case 'image':
                    input = this._createImageUploader(field, currentValue[field.name], (file, dataUrl) => {
                        currentValue[field.name] = { file, dataUrl };
                        triggerUpdate();
                    });
                    break;
                case 'file':
                    input = this._createFileUploader(field, currentValue[field.name] || [], (files) => {
                        currentValue[field.name] = files;
                        triggerUpdate();
                    });
                    break;
                default:
                    input = document.createElement('input');
                    input.type = 'text';
                    input.style.cssText = baseStyle;
                    if (field.maxLength) input.maxLength = field.maxLength;
                    break;
            }

            input.placeholder = field.placeholder || '';
            input.name = field.name;
            if (currentValue[field.name] !== undefined) {
                if (field.type === 'checkbox') input.checked = !!currentValue[field.name];
                else if (field.type !== 'image' && field.type !== 'file') input.value = currentValue[field.name];
            }

            input.addEventListener?.('change', triggerUpdate);
            input.addEventListener?.('input', triggerUpdate);
            input.addEventListener?.('focus', () => {
                input.style.borderColor = 'var(--cl-primary)';
                input.style.boxShadow = '0 0 0 2px rgba(var(--cl-primary-rgb), 0.1)';
            });
            input.addEventListener?.('blur', () => {
                input.style.borderColor = 'var(--cl-border)';
                input.style.boxShadow = 'none';
            });

            fieldInputs[field.name] = input;
            wrapper.appendChild(input);
            container.appendChild(wrapper);
        });
    }

    _rerender() {
        this.listContainer.innerHTML = '';
        const currentItems = cloneItems(this.items);
        this.items = [];
        this.itemElements = [];
        currentItems.forEach((value) => this._addItem(value, true));
        this._syncStateFromItems();
    }

    _updateUI() {
        const count = this.items.length;
        if (this.counter) this.counter.textContent = `${count} / ${this.options.maxItems}`;
        if (this.addButton) this.addButton.style.display = count >= this.options.maxItems ? 'none' : 'block';

        this.itemElements.forEach((element, index) => {
            const deleteBtn = element.querySelector('.delete-btn');
            const moveUpBtn = element.querySelector('.move-up-btn');
            const moveDownBtn = element.querySelector('.move-down-btn');
            const actionBtns = element.querySelector('.action-btns');
            const dragHandle = element.querySelector('.drag-handle');
            const indexLabel = element.querySelector('.index-label');

            if (indexLabel) indexLabel.textContent = `${index + 1}.`;
            if (deleteBtn) deleteBtn.style.visibility = count <= this.options.minItems ? 'hidden' : 'visible';

            if (count <= 1) {
                if (actionBtns) actionBtns.style.display = 'none';
                if (dragHandle) dragHandle.style.display = 'none';
            } else {
                if (actionBtns) actionBtns.style.display = 'flex';
                if (dragHandle) dragHandle.style.display = 'block';
                if (moveUpBtn) moveUpBtn.style.visibility = index === 0 ? 'hidden' : 'visible';
                if (moveDownBtn) moveDownBtn.style.visibility = index === count - 1 ? 'hidden' : 'visible';
            }
        });
    }

    getValues() {
        return cloneItems(this.items);
    }

    setValues(newItems) {
        if (!Array.isArray(newItems)) return;
        this.items = cloneItems(newItems);
        this._rerender();
    }

    _createImageUploader(field, currentValue, onChange) {
        const container = document.createElement('div');
        container.style.cssText = 'display:flex;flex-direction:column;gap:8px;width:100%;';
        const preview = document.createElement('div');
        preview.style.cssText = 'width:80px;height:80px;border:2px dashed var(--cl-border);border-radius:var(--cl-radius-sm);display:flex;align-items:center;justify-content:center;overflow:hidden;background:var(--cl-bg-input);cursor:pointer;';
        const img = document.createElement('img');
        img.style.cssText = 'max-width:100%;max-height:100%;display:none;';
        const placeholder = document.createElement('span');
        placeholder.textContent = '+';
        placeholder.style.cssText = 'font-size:var(--cl-font-size-3xl);color:var(--cl-text-light);';
        preview.appendChild(img);
        preview.appendChild(placeholder);
        if (currentValue?.dataUrl) {
            img.src = currentValue.dataUrl;
            img.style.display = 'block';
            placeholder.style.display = 'none';
        }
        const fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.accept = 'image/*';
        fileInput.style.display = 'none';
        fileInput.addEventListener('change', (event) => {
            const file = event.target.files?.[0];
            if (!file || typeof FileReader === 'undefined') return;
            const reader = new FileReader();
            reader.onload = (resultEvent) => {
                img.src = resultEvent.target.result;
                img.style.display = 'block';
                placeholder.style.display = 'none';
                onChange(file, resultEvent.target.result);
            };
            reader.readAsDataURL(file);
        });
        preview.addEventListener('click', () => fileInput.click?.());
        container.appendChild(preview);
        container.appendChild(fileInput);
        return container;
    }

    _createFileUploader(field, currentFiles, onChange) {
        const container = document.createElement('div');
        const fileList = document.createElement('div');
        fileList.style.cssText = 'display:flex;flex-direction:column;gap:4px;margin-bottom:8px;';
        let files = [...(currentFiles || [])];
        const renderFiles = () => {
            fileList.innerHTML = '';
            files.forEach((file, index) => {
                const item = document.createElement('div');
                item.style.cssText = 'display:flex;align-items:center;justify-content:space-between;padding:4px 8px;background:var(--cl-bg-secondary);border-radius:var(--cl-radius-sm);font-size:var(--cl-font-size-sm);';
                const label = document.createElement('span');
                label.textContent = file.name || file.fileName || 'file';
                item.appendChild(label);
                const removeBtn = document.createElement('button');
                removeBtn.type = 'button';
                removeBtn.textContent = 'x';
                removeBtn.style.cssText = 'border:none;background:none;color:var(--cl-text-placeholder);cursor:pointer;font-size:var(--cl-font-size-lg);';
                removeBtn.addEventListener('click', () => {
                    files.splice(index, 1);
                    renderFiles();
                    onChange([...files]);
                });
                item.appendChild(removeBtn);
                fileList.appendChild(item);
            });
        };
        renderFiles();

        const uploadBtn = document.createElement('button');
        uploadBtn.type = 'button';
        uploadBtn.textContent = 'Upload';
        uploadBtn.style.cssText = 'padding:6px 12px;border:1px dashed var(--cl-text-light);background:var(--cl-bg-input);border-radius:var(--cl-radius-sm);cursor:pointer;font-size:var(--cl-font-size-sm);width:100%;';
        const fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.multiple = true;
        fileInput.accept = field.accept || '*/*';
        fileInput.style.display = 'none';
        fileInput.addEventListener('change', (event) => {
            const nextFiles = Array.from(event.target.files || []).map((file) => ({ file, name: file.name, size: file.size }));
            files = [...files, ...nextFiles];
            renderFiles();
            onChange([...files]);
        });
        uploadBtn.addEventListener('click', () => fileInput.click?.());
        container.appendChild(fileList);
        container.appendChild(uploadBtn);
        container.appendChild(fileInput);
        return container;
    }

    _downloadTemplate() {
        if (!this.options.fields?.length || typeof Blob === 'undefined' || !document.createElement) return;
        const headers = this.options.fields.map((field) => field.label || field.name);
        const exampleRow = this.options.fields.map((field) => {
            if (field.type === 'image' || field.type === 'file') return '(upload separately)';
            if (field.type === 'select' && field.options?.length) {
                const firstOption = field.options[0];
                return typeof firstOption === 'object' ? firstOption.value : firstOption;
            }
            if (field.type === 'checkbox') return 'true';
            if (field.type === 'date') return 'YYYY-MM-DD';
            if (field.type === 'email') return 'example@email.com';
            if (field.type === 'tel') return '0912345678';
            if (field.type === 'number') return '0';
            return field.placeholder || '';
        });
        const escapeCsv = (value) => {
            const stringValue = String(value);
            return /[",\n]/.test(stringValue) ? `"${stringValue.replace(/"/g, '""')}"` : stringValue;
        };
        const csv = [headers.map(escapeCsv).join(','), exampleRow.map(escapeCsv).join(',')].join('\n');
        const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'list-input-template.csv';
        link.click?.();
        URL.revokeObjectURL(url);
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
        if (this.element?.parentNode) this.element.remove();
        this.itemElements = [];
        this.items = [];
    }
}

export default ListInput;
