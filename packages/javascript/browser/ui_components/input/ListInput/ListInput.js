/**
 * ListInput - 列表輸入基底元件
 * 管理可新增/刪除的項目列表
 */
import Locale from '../../i18n/index.js';


export class ListInput {
    /**
     * @param {Object} options
     * @param {string} options.title - 標題
     * @param {number} options.minItems - 最小項目數
     * @param {number} options.maxItems - 最大項目數
     * @param {string} options.addButtonText - 新增按鈕文字
     * @param {Array} options.fields - 欄位 schema 定義 [{name, type, label, placeholder, flex, options}]
     * @param {Function} options.renderItem - 自訂渲染回調 (優先於 fields)
     * @param {Function} options.onItemChange - 單項變更回調 (index, value)
     * @param {Function} options.onChange - 列表變更回調 (items)
     */
    constructor(options = {}) {
        this.options = {
            title: '',
            minItems: 0,
            maxItems: 10,
            addButtonText: Locale.t('listInput.addButton'),
            fields: null, // 欄位 schema
            renderItem: null,
            onItemChange: null,
            onChange: null,
            ...options
        };

        this.items = []; // 儲存每個項目的資料
        this.itemElements = []; // 儲存每個項目的 DOM 容器
        this.element = this._createElement();

        // 如果有最小項目數，預先建立
        for (let i = 0; i < this.options.minItems; i++) {
            this._addItem();
        }
    }

    _createElement() {
        const container = document.createElement('div');
        container.className = 'list-input';
        container.style.cssText = `
            border: 1px solid var(--cl-border-light);
            border-radius: 8px;
            padding: 16px;
            background: var(--cl-bg);
        `;

        // 標題區
        if (this.options.title) {
            const header = document.createElement('div');
            header.style.cssText = `
                display: flex;
                justify-content: space-between;
                align-items: center;
                margin-bottom: 12px;
            `;

            // 左側：標題
            const titleArea = document.createElement('div');
            titleArea.style.cssText = 'display: flex; align-items: center; gap: 12px;';
            
            const title = document.createElement('h3');
            title.textContent = this.options.title;
            title.style.cssText = `
                font-size: 16px;
                color: var(--cl-text);
                margin: 0;
            `;
            titleArea.appendChild(title);

            // 計數器
            this.counter = document.createElement('span');
            this.counter.style.cssText = 'font-size: 12px; color: var(--cl-text-secondary);';
            titleArea.appendChild(this.counter);

            header.appendChild(titleArea);

            // 右側：下載範本按鈕 (僅在有 fields schema 時顯示)
            if (this.options.fields && this.options.fields.length > 0) {
                const templateBtn = document.createElement('button');
                templateBtn.textContent = '📥 下載範本';
                templateBtn.title = Locale.t('listInput.csvTemplate');
                templateBtn.style.cssText = `
                    padding: 6px 12px;
                    border: 1px solid var(--cl-primary-dark);
                    background: var(--cl-bg);
                    color: var(--cl-primary-dark);
                    font-size: 12px;
                    border-radius: 4px;
                    cursor: pointer;
                    transition: all 0.2s;
                `;
                templateBtn.addEventListener('mouseenter', () => {
                    templateBtn.style.background = 'var(--cl-primary-dark)';
                    templateBtn.style.color = 'var(--cl-text-inverse)';
                });
                templateBtn.addEventListener('mouseleave', () => {
                    templateBtn.style.background = 'var(--cl-bg)';
                    templateBtn.style.color = 'var(--cl-primary-dark)';
                });
                templateBtn.addEventListener('click', () => this._downloadTemplate());
                header.appendChild(templateBtn);
            }

            container.appendChild(header);
        }

        // 列表容器
        this.listContainer = document.createElement('div');
        this.listContainer.className = 'list-input__items';
        this.listContainer.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 12px;
        `;
        container.appendChild(this.listContainer);

        // 新增按鈕
        this.addButton = document.createElement('button');
        this.addButton.textContent = `+ ${this.options.addButtonText}`;
        this.addButton.style.cssText = `
            margin-top: 12px;
            width: 100%;
            padding: 8px;
            border: 1px dashed var(--cl-grey-light);
            background: var(--cl-bg-input);
            color: var(--cl-text-secondary);
            cursor: pointer;
            border-radius: 4px;
            transition: all 0.2s;
        `;
        this.addButton.addEventListener('mouseenter', () => {
            this.addButton.style.background = 'var(--cl-bg-subtle)';
            this.addButton.style.borderColor = 'var(--cl-text-placeholder)';
        });
        this.addButton.addEventListener('mouseleave', () => {
            this.addButton.style.background = 'var(--cl-bg-input)';
            this.addButton.style.borderColor = 'var(--cl-grey-light)';
        });
        this.addButton.addEventListener('click', () => this._addItem());

        container.appendChild(this.addButton);

        this._updateUI();
        return container;
    }

    _addItem(initialValue = null) {
        if (this.items.length >= this.options.maxItems) return;

        const index = this.items.length;
        const itemContainer = document.createElement('div');
        itemContainer.className = 'list-input__item';
        itemContainer.setAttribute('data-index', index);
        itemContainer.draggable = true;
        itemContainer.style.cssText = `
            display: flex;
            align-items: flex-start;
            gap: 8px;
            padding: 12px;
            background: var(--cl-bg-tertiary);
            border-radius: 6px;
            position: relative;
            transition: background 0.15s, transform 0.15s;
        `;

        // 拖曳手柄
        const dragHandle = document.createElement('div');
        dragHandle.className = 'drag-handle';
        dragHandle.innerHTML = '⋮⋮';
        dragHandle.title = Locale.t('listInput.dragToSort');
        dragHandle.style.cssText = `
            cursor: grab;
            color: var(--cl-text-light);
            font-size: 14px;
            padding: 4px 2px;
            user-select: none;
            letter-spacing: -2px;
        `;
        itemContainer.appendChild(dragHandle);

        // 序號
        const indexLabel = document.createElement('div');
        indexLabel.className = 'index-label';
        indexLabel.textContent = `${index + 1}.`;
        indexLabel.style.cssText = `
            font-size: 14px;
            color: var(--cl-text-placeholder);
            padding-top: 8px;
            min-width: 20px;
        `;
        itemContainer.appendChild(indexLabel);

        // 內容區
        const content = document.createElement('div');
        content.style.cssText = 'flex: 1;';
        itemContainer.appendChild(content);

        // 操作按鈕區
        const actionBtns = document.createElement('div');
        actionBtns.className = 'action-btns';
        actionBtns.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 2px;
        `;

        // 上移按鈕
        const moveUpBtn = document.createElement('button');
        moveUpBtn.className = 'move-up-btn';
        moveUpBtn.innerHTML = '▲';
        moveUpBtn.title = Locale.t('listInput.moveUp');
        moveUpBtn.style.cssText = `
            width: 24px;
            height: 20px;
            border: none;
            background: transparent;
            color: var(--cl-text-placeholder);
            font-size: 10px;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            border-radius: 4px;
            padding: 0;
        `;
        moveUpBtn.addEventListener('mouseenter', () => {
            moveUpBtn.style.background = 'var(--cl-primary-light)';
            moveUpBtn.style.color = 'var(--cl-primary-dark)';
        });
        moveUpBtn.addEventListener('mouseleave', () => {
            moveUpBtn.style.background = 'transparent';
            moveUpBtn.style.color = 'var(--cl-text-placeholder)';
        });
        moveUpBtn.addEventListener('click', () => this._moveItem(index, -1));
        actionBtns.appendChild(moveUpBtn);

        // 下移按鈕
        const moveDownBtn = document.createElement('button');
        moveDownBtn.className = 'move-down-btn';
        moveDownBtn.innerHTML = '▼';
        moveDownBtn.title = Locale.t('listInput.moveDown');
        moveDownBtn.style.cssText = `
            width: 24px;
            height: 20px;
            border: none;
            background: transparent;
            color: var(--cl-text-placeholder);
            font-size: 10px;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            border-radius: 4px;
            padding: 0;
        `;
        moveDownBtn.addEventListener('mouseenter', () => {
            moveDownBtn.style.background = 'var(--cl-primary-light)';
            moveDownBtn.style.color = 'var(--cl-primary-dark)';
        });
        moveDownBtn.addEventListener('mouseleave', () => {
            moveDownBtn.style.background = 'transparent';
            moveDownBtn.style.color = 'var(--cl-text-placeholder)';
        });
        moveDownBtn.addEventListener('click', () => this._moveItem(index, 1));
        actionBtns.appendChild(moveDownBtn);

        itemContainer.appendChild(actionBtns);

        // 刪除按鈕
        const deleteBtn = document.createElement('button');
        deleteBtn.className = 'delete-btn';
        deleteBtn.innerHTML = '×';
        deleteBtn.style.cssText = `
            width: 24px;
            height: 24px;
            border: none;
            background: transparent;
            color: var(--cl-text-placeholder);
            font-size: 20px;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            border-radius: 50%;
            padding: 0;
            line-height: 1;
        `;
        deleteBtn.title = Locale.t('listInput.removeItem');
        deleteBtn.addEventListener('mouseenter', () => {
            deleteBtn.style.background = 'var(--cl-bg-danger-lighter)';
            deleteBtn.style.color = 'var(--cl-danger)';
        });
        deleteBtn.addEventListener('mouseleave', () => {
            deleteBtn.style.background = 'transparent';
            deleteBtn.style.color = 'var(--cl-text-placeholder)';
        });
        deleteBtn.addEventListener('click', () => this._removeItem(index));
        itemContainer.appendChild(deleteBtn);

        // 拖曳事件
        itemContainer.addEventListener('dragstart', (e) => {
            this.draggedIndex = index;
            itemContainer.style.opacity = '0.5';
            e.dataTransfer.effectAllowed = 'move';
        });

        itemContainer.addEventListener('dragend', () => {
            itemContainer.style.opacity = '1';
            this.draggedIndex = null;
        });

        itemContainer.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'move';
            itemContainer.style.background = 'var(--cl-primary-light)';
        });

        itemContainer.addEventListener('dragleave', () => {
            itemContainer.style.background = 'var(--cl-bg-tertiary)';
        });

        itemContainer.addEventListener('drop', (e) => {
            e.preventDefault();
            itemContainer.style.background = 'var(--cl-bg-tertiary)';
            
            if (this.draggedIndex !== null && this.draggedIndex !== index) {
                // 移動項目
                const draggedItem = this.items[this.draggedIndex];
                this.items.splice(this.draggedIndex, 1);
                this.items.splice(index, 0, draggedItem);
                
                this._rerender();
                
                if (this.options.onChange) {
                    this.options.onChange([...this.items]);
                }
            }
        });

        this.listContainer.appendChild(itemContainer);

        // 保存狀態
        this.items.push(initialValue || {});
        this.itemElements.push(itemContainer);

        // 渲染內容
        const updateValue = (newValue) => {
            this.items[index] = newValue;
            if (this.options.onItemChange) {
                this.options.onItemChange(index, newValue);
            }
            if (this.options.onChange) {
                this.options.onChange([...this.items]);
            }
        };

        if (this.options.renderItem) {
            // 使用自訂 renderItem
            this.options.renderItem(content, index, initialValue, updateValue);
        } else if (this.options.fields) {
            // 使用 fields schema 自動產生欄位
            this._renderFieldsItem(content, index, initialValue, updateValue);
        }

        this._updateUI();
    }

    /**
     * 移動項目 (上移或下移)
     * @param {number} index - 當前索引
     * @param {number} direction - -1 上移 / +1 下移
     */
    _moveItem(index, direction) {
        const newIndex = index + direction;
        
        // 邊界檢查
        if (newIndex < 0 || newIndex >= this.items.length) return;

        // 交換資料
        [this.items[index], this.items[newIndex]] = [this.items[newIndex], this.items[index]];

        // 重繪列表
        this._rerender();

        if (this.options.onChange) {
            this.options.onChange([...this.items]);
        }
    }

    _removeItem(index) {
        if (this.items.length <= this.options.minItems) return;

        // 移除 DOM
        const element = this.itemElements[index];
        element.remove();

        // 移除資料
        this.items.splice(index, 1);
        this.itemElements.splice(index, 1);

        // 重新渲染剩下的項目，因為索引變了
        // 為了簡單起見，這裡我們不清空重建，而是只更新序號和事件綁定
        // 但因為 renderItem 可能依賴 index，最好的方式是重繪或要求 renderItem 不依賴 index
        // 這裡我們簡單重繪整個列表
        this._rerender();

        if (this.options.onChange) {
            this.options.onChange([...this.items]);
        }
    }

    /**
     * 根據 fields schema 自動渲染欄位
     */
    _renderFieldsItem(container, index, value, updateValue) {
        container.style.cssText = 'display: flex; gap: 8px; flex-wrap: wrap; align-items: flex-end;';
        
        const fieldInputs = {};
        const currentValue = value || {};

        const triggerUpdate = () => {
            const newValue = {};
            for (const [name, input] of Object.entries(fieldInputs)) {
                if (input.type === 'checkbox') {
                    newValue[name] = input.checked;
                } else {
                    newValue[name] = input.value;
                }
            }
            updateValue(newValue);
        };

        this.options.fields.forEach(field => {
            const wrapper = document.createElement('div');
            wrapper.style.cssText = `
                display: flex;
                flex-direction: column;
                gap: 4px;
                ${field.flex ? `flex: ${field.flex};` : ''}
                ${field.width ? `width: ${field.width};` : 'min-width: 80px;'}
            `;

            // 標籤
            if (field.label) {
                const label = document.createElement('label');
                label.textContent = field.label;
                label.style.cssText = 'font-size: 12px; color: var(--cl-text-secondary);';
                if (field.required) {
                    const asterisk = document.createElement('span');
                    asterisk.textContent = ' *';
                    asterisk.style.color = 'var(--cl-danger)';
                    label.appendChild(asterisk);
                }
                wrapper.appendChild(label);
            }

            // 輸入元素
            let input;
            const baseStyle = `
                height: 36px;
                padding: 0 10px;
                border: 1px solid var(--cl-border);
                border-radius: 4px;
                font-size: 14px;
                outline: none;
                box-sizing: border-box;
                width: 100%;
            `;

            switch (field.type) {
                case 'select':
                    input = document.createElement('select');
                    input.style.cssText = baseStyle + 'cursor: pointer;';
                    
                    // 預設選項
                    const defaultOpt = document.createElement('option');
                    defaultOpt.value = '';
                    defaultOpt.textContent = field.placeholder || Locale.t('listInput.selectPlaceholder');
                    input.appendChild(defaultOpt);

                    // 選項
                    (field.options || []).forEach(opt => {
                        const option = document.createElement('option');
                        if (typeof opt === 'object') {
                            option.value = opt.value;
                            option.textContent = opt.label;
                        } else {
                            option.value = opt;
                            option.textContent = opt;
                        }
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
                    input.style.cssText = 'width: 18px; height: 18px; margin: 8px 0;';
                    break;

                case 'email':
                    input = document.createElement('input');
                    input.type = 'email';
                    input.style.cssText = baseStyle;
                    break;

                case 'tel':
                    input = document.createElement('input');
                    input.type = 'tel';
                    input.style.cssText = baseStyle;
                    break;

                case 'date':
                    input = document.createElement('input');
                    input.type = 'date';
                    input.style.cssText = baseStyle;
                    break;

                case 'image':
                    // 單一圖片上傳
                    input = this._createImageUploader(field, currentValue[field.name], (file, dataUrl) => {
                        currentValue[field.name] = { file, dataUrl };
                        triggerUpdate();
                    });
                    break;

                case 'file':
                    // 檔案列表上傳
                    input = this._createFileUploader(field, currentValue[field.name] || [], (files) => {
                        currentValue[field.name] = files;
                        triggerUpdate();
                    });
                    break;

                case 'text':
                default:
                    input = document.createElement('input');
                    input.type = 'text';
                    input.style.cssText = baseStyle;
                    if (field.maxLength) input.maxLength = field.maxLength;
                    break;
            }

            input.placeholder = field.placeholder || '';
            input.name = field.name;
            
            // 設定初始值
            if (currentValue[field.name] !== undefined) {
                if (field.type === 'checkbox') {
                    input.checked = !!currentValue[field.name];
                } else {
                    input.value = currentValue[field.name];
                }
            }

            // 監聽變更
            input.addEventListener('change', triggerUpdate);
            input.addEventListener('input', triggerUpdate);

            // focus/blur 樣式
            input.addEventListener('focus', () => {
                input.style.borderColor = 'var(--cl-primary)';
                input.style.boxShadow = '0 0 0 2px rgba(var(--cl-primary-rgb), 0.1)';
            });
            input.addEventListener('blur', () => {
                input.style.borderColor = 'var(--cl-border)';
                input.style.boxShadow = 'none';
            });

            fieldInputs[field.name] = input;
            wrapper.appendChild(input);
            container.appendChild(wrapper);
        });
    }

    _rerender() {
        // 清空列表容器
        this.listContainer.innerHTML = '';
        const currentItems = [...this.items];

        // 重置狀態
        this.items = [];
        this.itemElements = [];

        // 重新加入
        currentItems.forEach(value => {
            this._addItem(value);
        });
    }

    _updateUI() {
        const count = this.items.length;

        // 更新計數器
        if (this.counter) {
            this.counter.textContent = `${count} / ${this.options.maxItems}`;
        }

        // 更新按鈕狀態
        this.addButton.style.display = count >= this.options.maxItems ? 'none' : 'block';

        // 更新各項目的按鈕狀態
        this.itemElements.forEach((el, idx) => {
            // 刪除按鈕
            const deleteBtn = el.querySelector('.delete-btn');
            if (deleteBtn) {
                deleteBtn.style.visibility = count <= this.options.minItems ? 'hidden' : 'visible';
            }

            // 上移/下移按鈕
            const moveUpBtn = el.querySelector('.move-up-btn');
            const moveDownBtn = el.querySelector('.move-down-btn');
            const actionBtns = el.querySelector('.action-btns');
            const dragHandle = el.querySelector('.drag-handle');

            // 只有一筆時，隱藏所有排序控制
            if (count <= 1) {
                if (actionBtns) actionBtns.style.display = 'none';
                if (dragHandle) dragHandle.style.display = 'none';
            } else {
                if (actionBtns) actionBtns.style.display = 'flex';
                if (dragHandle) dragHandle.style.display = 'block';
                
                // 第一個隱藏上移
                if (moveUpBtn) {
                    moveUpBtn.style.visibility = idx === 0 ? 'hidden' : 'visible';
                }
                // 最後一個隱藏下移
                if (moveDownBtn) {
                    moveDownBtn.style.visibility = idx === count - 1 ? 'hidden' : 'visible';
                }
            }
        });
    }

    /**
     * 取得所有值
     */
    getValues() {
        return [...this.items];
    }

    /**
     * 設定值
     */
    setValues(newItems) {
        if (!Array.isArray(newItems)) return;
        this.items = newItems; // 暫時儲存以供 _rerender 使用
        this._rerender(); // 利用 _rerender 重新建立 DOM
    }

    /**
     * 建立圖片上傳器 (單一圖片)
     */
    _createImageUploader(field, currentValue, onChange) {
        const container = document.createElement('div');
        container.style.cssText = `
            display: flex;
            flex-direction: column;
            gap: 8px;
            width: 100%;
        `;

        // 預覽區
        const preview = document.createElement('div');
        preview.style.cssText = `
            width: 80px;
            height: 80px;
            border: 2px dashed var(--cl-border);
            border-radius: 4px;
            display: flex;
            align-items: center;
            justify-content: center;
            overflow: hidden;
            background: var(--cl-bg-input);
            cursor: pointer;
        `;

        const img = document.createElement('img');
        img.style.cssText = 'max-width: 100%; max-height: 100%; display: none;';
        preview.appendChild(img);

        const placeholder = document.createElement('span');
        placeholder.textContent = '📷';
        placeholder.style.cssText = 'font-size: 24px; color: var(--cl-text-light);';
        preview.appendChild(placeholder);

        // 如果有初始值
        if (currentValue?.dataUrl) {
            img.src = currentValue.dataUrl;
            img.style.display = 'block';
            placeholder.style.display = 'none';
        }

        // 隱藏的檔案輸入
        const fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.accept = 'image/*';
        fileInput.style.display = 'none';

        fileInput.addEventListener('change', (e) => {
            const file = e.target.files[0];
            if (file) {
                const reader = new FileReader();
                reader.onload = (ev) => {
                    img.src = ev.target.result;
                    img.style.display = 'block';
                    placeholder.style.display = 'none';
                    onChange(file, ev.target.result);
                };
                reader.readAsDataURL(file);
            }
        });

        preview.addEventListener('click', () => fileInput.click());
        container.appendChild(preview);
        container.appendChild(fileInput);

        return container;
    }

    /**
     * 建立檔案上傳器 (檔案列表)
     */
    _createFileUploader(field, currentFiles, onChange) {
        const container = document.createElement('div');
        container.style.cssText = 'width: 100%;';

        // 檔案列表顯示
        const fileList = document.createElement('div');
        fileList.style.cssText = 'display: flex; flex-direction: column; gap: 4px; margin-bottom: 8px;';

        const renderFiles = (files) => {
            fileList.innerHTML = '';
            files.forEach((f, idx) => {
                const item = document.createElement('div');
                item.style.cssText = `
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    padding: 4px 8px;
                    background: var(--cl-bg-secondary);
                    border-radius: 4px;
                    font-size: 12px;
                `;
                item.innerHTML = `<span>📎 ${f.name || f.fileName}</span>`;
                
                const removeBtn = document.createElement('button');
                removeBtn.textContent = '×';
                removeBtn.style.cssText = 'border: none; background: none; color: var(--cl-text-placeholder); cursor: pointer; font-size: 14px;';
                removeBtn.addEventListener('click', () => {
                    files.splice(idx, 1);
                    renderFiles(files);
                    onChange([...files]);
                });
                item.appendChild(removeBtn);
                fileList.appendChild(item);
            });
        };

        if (currentFiles.length > 0) {
            renderFiles(currentFiles);
        }

        // 上傳按鈕
        const uploadBtn = document.createElement('button');
        uploadBtn.textContent = '📁 選擇檔案';
        uploadBtn.style.cssText = `
            padding: 6px 12px;
            border: 1px dashed var(--cl-text-light);
            background: var(--cl-bg-input);
            border-radius: 4px;
            cursor: pointer;
            font-size: 12px;
            width: 100%;
        `;

        const fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.multiple = true;
        fileInput.accept = field.accept || '*/*';
        fileInput.style.display = 'none';

        fileInput.addEventListener('change', (e) => {
            const newFiles = Array.from(e.target.files).map(f => ({
                file: f,
                name: f.name,
                size: f.size
            }));
            const allFiles = [...(currentFiles || []), ...newFiles];
            currentFiles = allFiles;
            renderFiles(allFiles);
            onChange(allFiles);
        });

        uploadBtn.addEventListener('click', () => fileInput.click());
        container.appendChild(fileList);
        container.appendChild(uploadBtn);
        container.appendChild(fileInput);

        return container;
    }

    /**
     * 下載 CSV 範本
     */
    _downloadTemplate() {
        if (!this.options.fields || this.options.fields.length === 0) return;

        // 建立表頭 (欄位名稱)
        const headers = this.options.fields.map(f => f.label || f.name);
        
        // 建立範例資料行
        const exampleRow = this.options.fields.map(f => {
            // 圖片和檔案欄位標註不支援
            if (f.type === 'image' || f.type === 'file') {
                return '(不支援，請手動上傳)';
            }
            // 其他欄位給予範例提示
            if (f.type === 'select' && f.options?.length > 0) {
                const opt = f.options[0];
                return typeof opt === 'object' ? opt.value : opt;
            }
            if (f.type === 'checkbox') return 'true 或 false';
            if (f.type === 'date') return 'YYYY-MM-DD';
            if (f.type === 'email') return 'example@email.com';
            if (f.type === 'tel') return '0912345678';
            if (f.type === 'number') return '0';
            return f.placeholder || '';
        });

        // 組合 CSV
        const escapeCSV = (val) => {
            const str = String(val);
            if (str.includes(',') || str.includes('"') || str.includes('\n')) {
                return `"${str.replace(/"/g, '""')}"`;
            }
            return str;
        };

        const csvContent = [
            headers.map(escapeCSV).join(','),
            exampleRow.map(escapeCSV).join(',')
        ].join('\n');

        // 加入 BOM 以支援 Excel 正確顯示中文
        const BOM = '\uFEFF';
        const blob = new Blob([BOM + csvContent], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);

        const a = document.createElement('a');
        a.href = url;
        a.download = `${this.options.title || 'template'}.csv`;
        a.click();

        URL.revokeObjectURL(url);
    }

    /**
     * 掛載
     */
    mount(container) {
        const target = typeof container === 'string'
            ? document.querySelector(container)
            : container;
        if (target) target.appendChild(this.element);
        return this;
    }
}

export default ListInput;
