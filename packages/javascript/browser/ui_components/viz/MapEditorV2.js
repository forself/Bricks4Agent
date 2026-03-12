/**
 * MapEditorV2 - 使用專案組件的圖片編輯器
 * 功能：上傳圖片、添加文字與形狀標註、匯出 PNG（含元數據）
 */

import { BasicButton } from '../common/BasicButton/BasicButton.js';
import { UploadButton } from '../common/UploadButton/UploadButton.js';
import { ColorPicker } from '../common/ColorPicker/ColorPicker.js';
import { NumberInput } from '../form/NumberInput/NumberInput.js';

import { ModalPanel } from '../layout/Panel/index.js';
import Locale from '../i18n/index.js';

export class MapEditorV2 {
    constructor(options = {}) {
        this.container = typeof options.container === 'string'
            ? document.querySelector(options.container)
            : options.container;

        this.width = options.width || 800;
        this.height = options.height || 600;
        
        // 當前工具: 'select', 'text', 'rect', 'circle', 'line', 'arrow'
        this.currentTool = 'select';
        
        // 畫布上的元素
        this.elements = [];
        this.selectedElement = null;
        this.hoveredElement = null;
        
        // 繪圖狀態
        this.isDrawing = false;
        this.drawStart = null;
        this.tempShape = null;
        
        // 歷史記錄 (復原功能)
        this.history = [];
        this.historyIndex = -1;
        
        // 背景圖片
        this.backgroundImage = null;
        
        // 圖層與縮放
        this.layers = [
            { id: 'layer-1', name: Locale.t('webPainter.defaultLayerName', { n: 1 }), visible: true, locked: false }
        ];
        this.currentLayerId = 'layer-1';
        this.zoom = 1;
        
        // 樣式設定
        this.settings = {
            fontSize: 16,
            fontFamily: 'Microsoft JhengHei',
            textColor: 'var(--cl-text)',
            strokeColor: 'var(--cl-canvas-red)',
            fillColor: 'rgba(var(--cl-danger-rgb), 0.2)',
            lineWidth: 2
        };
        
        this._init();
    }

    _init() {
        this.element = this._createUI();
        this.container.appendChild(this.element);
        
        this._setupCanvas();
        this._setupEventListeners();
        this._render();
        this._saveHistory(); // 保存初始空狀態
    }

    _createUI() {
        const wrapper = document.createElement('div');
        wrapper.className = 'map-editor-v2';
        wrapper.style.cssText = `
            width: 100%;
            max-width: ${this.width}px;
            margin: 0 auto;
            font-family: var(--cl-font-family-cjk);
        `;

        // 工具列
        const toolbar = this._createToolbar();
        
        // 設定面板
        const settingsPanel = this._createSettingsPanel();

        // 畫布容器
        const canvasContainer = document.createElement('div');
        canvasContainer.className = 'canvas-container';
        canvasContainer.style.cssText = `
            position: relative;
            background: var(--cl-border-light);
            border: 1px solid var(--cl-border);
            border-top: none;
            border-radius: 0 0 var(--cl-radius-lg) var(--cl-radius-lg);
            overflow: auto;
            cursor: crosshair;
            height: ${this.height}px;
        `;

        const canvas = document.createElement('canvas');
        canvas.width = this.width;
        canvas.height = this.height;
        canvas.style.cssText = `
            display: block;
            background: var(--cl-bg);
            box-shadow: var(--cl-shadow-sm);
            margin: auto;
        `;
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');
        canvasContainer.appendChild(canvas);

        // 圖層面板 (改為浮動)
        this.layerPanel = this._createLayerPanel();
        canvasContainer.appendChild(this.layerPanel);

        wrapper.appendChild(toolbar);
        wrapper.appendChild(settingsPanel);
        wrapper.appendChild(canvasContainer);

        return wrapper;
    }

    _createLayerPanel() {
        const panel = document.createElement('div');
        panel.className = 'map-editor-layers-floating';
        panel.style.cssText = `
            position: absolute;
            top: 10px;
            right: 10px;
            width: 250px;
            max-height: 300px;
            background: var(--cl-bg);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-lg);
            box-shadow: var(--cl-shadow-md);
            display: none; /* 預設隱藏 */
            flex-direction: column;
            z-index: 1000;
        `;

        const header = document.createElement('div');
        header.style.cssText = `
            padding: 10px 15px;
            background: var(--cl-border-light);
            font-weight: bold;
            display: flex;
            justify-content: space-between;
            align-items: center;
            font-size: var(--cl-font-size-lg);
        `;
        header.innerHTML = '<span>📜 圖層管理</span>';

        const addLayerBtn = new BasicButton({
            type: 'custom',
            customLabel: '➕',
            size: 'small',
            showIcon: false,
            variant: 'plain',
            onClick: () => {
                const name = prompt(Locale.t('webPainter.layerNameLabel'), Locale.t('webPainter.defaultLayerName', { n: this.layers.length + 1 }));
                if (name) {
                    const newId = `layer-${Date.now()}`;
                    this.layers.push({ id: newId, name: name, visible: true, locked: false });
                    this.currentLayerId = newId;
                    this._updateLayerList();
                }
            }
        });
        addLayerBtn.mount(header);

        this.layerListContainer = document.createElement('div');
        this.layerListContainer.style.cssText = `
            flex: 1;
            overflow-y: auto;
            padding: 10px;
        `;

        panel.appendChild(header);
        panel.appendChild(this.layerListContainer);

        // 初始更新列表
        setTimeout(() => this._updateLayerList(), 0);

        return panel;
    }

    _updateLayerList() {
        if (!this.layerListContainer) return;
        this.layerListContainer.innerHTML = '';

        // 由上而下顯示圖層 (順序反轉，因為最後畫的在最上面)
        [...this.layers].reverse().forEach(layer => {
            const item = document.createElement('div');
            const isActive = layer.id === this.currentLayerId;
            
            item.style.cssText = `
                padding: 8px 12px;
                margin-bottom: 5px;
                border-radius: var(--cl-radius-sm);
                border: 1px solid ${isActive ? 'var(--cl-primary)' : 'var(--cl-border-light)'};
                background: ${isActive ? 'var(--cl-primary-light)' : 'var(--cl-bg)'};
                display: flex;
                align-items: center;
                gap: 8px;
                cursor: pointer;
                font-size: var(--cl-font-size-md);
                transition: all var(--cl-transition);
            `;

            item.onclick = () => {
                this.currentLayerId = layer.id;
                this._updateLayerList();
            };

            // 隱藏/顯示按鈕
            const toggleVisible = document.createElement('span');
            toggleVisible.textContent = layer.visible ? '👁️' : '🕶️';
            toggleVisible.style.cursor = 'pointer';
            toggleVisible.onclick = (e) => {
                e.stopPropagation();
                layer.visible = !layer.visible;
                this._updateLayerList();
                this._render();
            };

            const name = document.createElement('span');
            name.textContent = layer.name;
            name.style.flex = '1';
            if (!layer.visible) name.style.opacity = '0.5';

            // 刪除圖層按鈕
            const deleteLayer = document.createElement('span');
            deleteLayer.textContent = '🗑️';
            deleteLayer.style.opacity = '0.4';
            deleteLayer.style.cursor = 'pointer';
            deleteLayer.onmouseover = () => deleteLayer.style.opacity = '1';
            deleteLayer.onmouseout = () => deleteLayer.style.opacity = '0.4';
            deleteLayer.onclick = (e) => {
                e.stopPropagation();
                if (this.layers.length > 1 && confirm(Locale.t('webPainter.confirmDeleteLayer', { name: layer.name }))) {
                    this.layers = this.layers.filter(l => l.id !== layer.id);
                    this.elements = this.elements.filter(el => el.layerId !== layer.id);
                    if (this.currentLayerId === layer.id) {
                        this.currentLayerId = this.layers[this.layers.length - 1].id;
                    }
                    this._updateLayerList();
                    this._render();
                }
            };

            item.appendChild(toggleVisible);
            item.appendChild(name);
            if (this.layers.length > 1) item.appendChild(deleteLayer);
            
            this.layerListContainer.appendChild(item);
        });
    }

    _toggleLayerPanel() {
        if (!this.layerPanel) return;
        const isVisible = this.layerPanel.style.display === 'flex';
        this.layerPanel.style.display = isVisible ? 'none' : 'flex';
    }

    _createToolbar() {
        const toolbar = document.createElement('div');
        toolbar.className = 'map-editor-toolbar';
        toolbar.style.cssText = `
            background: var(--cl-bg-secondary);
            border: 1px solid var(--cl-border);
            border-radius: var(--cl-radius-lg) var(--cl-radius-lg) 0 0;
            padding: 12px;
            display: flex;
            gap: 8px;
            flex-wrap: wrap;
            align-items: center;
        `;

        // 上傳圖片按鈕
        const uploadBtn = new UploadButton({
            type: UploadButton.TYPES.IMAGE,
            onSelect: (files) => this._handleImageUpload(files[0]),
            tooltip: Locale.t('webPainter.uploadBg')
        });
        uploadBtn.mount(toolbar);

        // 分隔線
        toolbar.appendChild(this._createSeparator());

        // 工具按鈕
        const tools = [
            { tool: 'select', icon: '↖️', label: Locale.t('webPainter.selectTool') },
            { tool: 'text', icon: '📝', label: Locale.t('webPainter.textTool') },
            { tool: 'marker', icon: '📍', label: Locale.t('webPainter.pinTool') },
            { tool: 'pen', icon: '🖌️', label: Locale.t('webPainter.penTool') },
            { tool: 'rect', icon: '⬜', label: Locale.t('webPainter.rectTool') },
            { tool: 'circle', icon: '⭕', label: Locale.t('webPainter.circleTool') },
            { tool: 'line', icon: '➖', label: Locale.t('webPainter.lineTool') },
            { tool: 'arrow', icon: '➡️', label: Locale.t('webPainter.arrowTool') }
        ];

        this.toolButtons = {};
        tools.forEach(({tool, icon, label}) => {
            const btn = new BasicButton({
                type: 'custom',
                variant: 'plain',  // 無底色樣式
                customLabel: `${icon} ${label}`,
                size: 'small',
                showIcon: false,
                onClick: () => {
                    this.currentTool = tool;
                    this._updateToolButtons();
                }
            });
            btn.mount(toolbar);
            this.toolButtons[tool] = btn.element;
        });

        toolbar.appendChild(this._createSeparator());

        // 刪除按鈕
        const deleteBtn = new BasicButton({
            type: 'no',
            customLabel: Locale.t('webPainter.deleteBtn'),
            size: 'small',
            showIcon: false,
            onClick: () => {
                if (this.selectedElement) {
                    this._deleteElement(this.selectedElement);
                }
            }
        });
        deleteBtn.mount(toolbar);

        // 清空按鈕
        const clearBtn = new BasicButton({
            type: 'clear',
            customLabel: Locale.t('webPainter.clearAllBtn'),
            size: 'small',
            showIcon: false,
            onClick: () => {
                if (confirm(Locale.t('webPainter.confirmClearAll'))) {
                    this.elements = [];
                    this._saveHistory();
                    this._render();
                }
            }
        });
        clearBtn.mount(toolbar);

        toolbar.appendChild(this._createSeparator());

        // 匯出按鈕
        const exportBtn = new BasicButton({
            type: 'save',
            customLabel: Locale.t('webPainter.exportPngBtn'),
            size: 'small',
            showIcon: false,
            variant: 'primary',
            onClick: () => this._exportImage()
        });
        exportBtn.mount(toolbar);

        const saveJsonBtn = new BasicButton({
            type: 'save',
            customLabel: Locale.t('webPainter.saveJsonBtn'),
            size: 'small',
            showIcon: false,
            variant: 'secondary',
            onClick: () => this._exportJSON()
        });
        saveJsonBtn.mount(toolbar);

        toolbar.appendChild(this._createSeparator());

        // 圖層開關按鈕
        const toggleLayerBtn = new BasicButton({
            type: 'custom',
            customLabel: Locale.t('webPainter.layerBtn'),
            size: 'small',
            showIcon: false,
            variant: 'secondary',
            onClick: () => this._toggleLayerPanel()
        });
        toggleLayerBtn.mount(toolbar);

        toolbar.appendChild(this._createSeparator());

        // 縮放控制
        const zoomOutBtn = new BasicButton({
            type: 'custom',
            customLabel: '➖',
            size: 'small',
            showIcon: false,
            variant: 'plain',
            onClick: () => {
                this.zoom = Math.max(0.1, this.zoom - 0.1);
                this._render();
                zoomText.textContent = `${Math.round(this.zoom * 100)}%`;
            }
        });
        zoomOutBtn.mount(toolbar);

        const zoomText = document.createElement('span');
        zoomText.textContent = '100%';
        zoomText.style.cssText = 'font-size: var(--cl-font-size-sm); min-width: 40px; text-align: center; font-weight: bold;';
        toolbar.appendChild(zoomText);

        const zoomInBtn = new BasicButton({
            type: 'custom',
            customLabel: '➕',
            size: 'small',
            showIcon: false,
            variant: 'plain',
            onClick: () => {
                this.zoom = Math.min(5, this.zoom + 0.1);
                this._render();
                zoomText.textContent = `${Math.round(this.zoom * 100)}%`;
            }
        });
        zoomInBtn.mount(toolbar);

        const zoomResetBtn = new BasicButton({
            type: 'custom',
            customLabel: '🔄 100%',
            size: 'small',
            showIcon: false,
            variant: 'plain',
            onClick: () => {
                this.zoom = 1;
                this._render();
                zoomText.textContent = '100%';
            }
        });
        zoomResetBtn.mount(toolbar);

        this._updateToolButtons();

        return toolbar;
    }

    _createSettingsPanel() {
        const panel = document.createElement('div');
        panel.style.cssText = `
            background: var(--cl-bg-input);
            border: 1px solid var(--cl-border);
            border-top: none;
            padding: 12px;
            display: flex;
            gap: 16px;
            flex-wrap: wrap;
            align-items: center;
            font-size: var(--cl-font-size-lg);
        `;

        // 字體大小
        panel.appendChild(this._createLabel(Locale.t('webPainter.fontSizeLabel')));
        const fontSizeInput = new NumberInput({
            value: this.settings.fontSize,
            min: 10,
            max: 72,
            step: 1,
            width: '80px',
            onChange: (val) => {
                this.settings.fontSize = val;
            }
        });
        fontSizeInput.mount(panel);

        // 字型選擇
        panel.appendChild(this._createLabel(Locale.t('webPainter.fontFamilyLabel')));
        const fontSelect = document.createElement('select');
        fontSelect.style.cssText = `
            padding: 4px 8px;
            border: 1px solid var(--cl-border-dark);
            border-radius: var(--cl-radius-sm);
            font-size: var(--cl-font-size-lg);
            height: 30px;
            outline: none;
        `;
        
        const fonts = [
            { value: 'Microsoft JhengHei', label: Locale.t('webPainter.fontMsJhengHei') },
            { value: 'PMingLiU', label: Locale.t('webPainter.fontMingLiU') },
            { value: 'Arial', label: 'Arial' },
            { value: 'Times New Roman', label: 'Times New Roman' },
            { value: 'Courier New', label: 'Courier New' },
            { value: 'Georgia', label: 'Georgia' },
            { value: 'Verdana', label: 'Verdana' }
        ];

        fonts.forEach(f => {
            const option = document.createElement('option');
            option.value = f.value;
            option.textContent = f.label;
            if (f.value === this.settings.fontFamily) option.selected = true;
            fontSelect.appendChild(option);
        });

        fontSelect.onchange = (e) => {
            this.settings.fontFamily = e.target.value;
            if (this.selectedElement && (this.selectedElement.type === 'text' || this.selectedElement.type === 'marker')) {
                this.selectedElement.fontFamily = this.settings.fontFamily;
                this._saveHistory();
                this._render();
            }
        };
        panel.appendChild(fontSelect);
        this.fontSelect = fontSelect; // 儲存參考以便更新

        // 文字顏色
        const textColorPicker = new ColorPicker({
            label: Locale.t('webPainter.textColorLabel'),
            value: this.settings.textColor,
            onChange: (val) => {
                this.settings.textColor = val;
            }
        });
        textColorPicker.mount(panel);

        // 線條顏色
        const strokeColorPicker = new ColorPicker({
            label: Locale.t('webPainter.strokeColorLabel'),
            value: this.settings.strokeColor,
            onChange: (val) => {
                this.settings.strokeColor = val;
            }
        });
        strokeColorPicker.mount(panel);

        // 填充顏色
        const fillColorPicker = new ColorPicker({
            label: Locale.t('webPainter.fillColorLabel'),
            value: this.settings.fillColor,
            onChange: (val) => {
                this.settings.fillColor = val;
            }
        });
        fillColorPicker.mount(panel);

        // 線條粗細
        panel.appendChild(this._createLabel(Locale.t('webPainter.strokeWidthLabel')));
        const lineWidthInput = new NumberInput({
            value: this.settings.lineWidth,
            min: 1,
            max: 20,
            step: 1,
            width: '80px',
            onChange: (val) => {
                this.settings.lineWidth = val;
            }
        });
        lineWidthInput.mount(panel);

        return panel;
    }

    _createLabel(text) {
        const label = document.createElement('span');
        label.textContent = text;
        label.style.cssText = `
            color: var(--cl-text-secondary);
            font-size: var(--cl-font-size-lg);
        `;
        return label;
    }

    _createSeparator() {
        const sep = document.createElement('div');
        sep.style.cssText = `
            width: 1px;
            height: 24px;
            background: var(--cl-border);
            margin: 0 4px;
        `;
        return sep;
    }

    _updateToolButtons() {
        Object.keys(this.toolButtons).forEach(tool => {
            const btn = this.toolButtons[tool];
            if (tool === this.currentTool) {
                btn.style.background = 'var(--cl-success)';
                btn.style.color = 'var(--cl-text-inverse)';
                btn.style.borderColor = 'var(--cl-success)';
            } else {
                btn.style.background = 'var(--cl-bg)';
                btn.style.color = 'var(--cl-text)';
                btn.style.borderColor = 'var(--cl-border)';
            }
        });
    }

    _setupCanvas() {
        const dpr = window.devicePixelRatio || 1;
        const rect = this.canvas.getBoundingClientRect();
        this.canvas.width = rect.width * dpr;
        this.canvas.height = rect.height * dpr;
        this.ctx.scale(dpr, dpr);
        this.canvas.style.width = rect.width + 'px';
        this.canvas.style.height = rect.height + 'px';
    }

    _setupEventListeners() {
        this.canvas.addEventListener('mousedown', (e) => this._handleMouseDown(e));
        this.canvas.addEventListener('mousemove', (e) => this._handleMouseMove(e));
        this.canvas.addEventListener('mouseup', (e) => this._handleMouseUp(e));
        this.canvas.addEventListener('dblclick', (e) => this._handleDoubleClick(e));
        
        document.addEventListener('keydown', (e) => {
            // 忽略在輸入框中的按鍵事件
            if (e.target.tagName === 'INPUT' || e.target.tagName === 'TEXTAREA') return;

            if (e.key === 'Delete' && this.selectedElement) {
                this._deleteElement(this.selectedElement);
            }
            
            // Undo: Ctrl+Z
            if (e.ctrlKey && !e.shiftKey && e.key.toLowerCase() === 'z') {
                e.preventDefault();
                this._undo();
            }
            
            // Redo: Ctrl+Y or Ctrl+Shift+Z
            if ((e.ctrlKey && e.key.toLowerCase() === 'y') || (e.ctrlKey && e.shiftKey && e.key.toLowerCase() === 'z')) {
                e.preventDefault();
                this._redo();
            }

            // Copy: Ctrl+C
            if (e.ctrlKey && e.key.toLowerCase() === 'c') {
                e.preventDefault();
                this._copy();
            }

            // Paste: Ctrl+V
            if (e.ctrlKey && e.key.toLowerCase() === 'v') {
                e.preventDefault();
                this._paste();
            }
        });
    }

    _getMousePos(e) {
        const rect = this.canvas.getBoundingClientRect();
        const dpr = window.devicePixelRatio || 1;
        return {
            x: ((e.clientX - rect.left) * (this.canvas.width / rect.width) / dpr) / this.zoom,
            y: ((e.clientY - rect.top) * (this.canvas.height / rect.height) / dpr) / this.zoom
        };
    }

    _handleImageUpload(file) {
        if (!file) return;

        const reader = new FileReader();
        reader.onload = (event) => {
            const img = new Image();
            img.onload = () => {
                this.backgroundImage = img;
                this._render();
            };
            img.src = event.target.result;
        };
        reader.readAsDataURL(file);
    }

    _handleMouseDown(e) {
        const pos = this._getMousePos(e);
        
        if (this.currentTool === 'select') {
            this.selectedElement = this._getElementAtPos(pos.x, pos.y);
            if (this.selectedElement) {
                this.dragStart = { x: pos.x, y: pos.y };
                this.dragOffset = {
                    x: pos.x - this.selectedElement.x,
                    y: pos.y - this.selectedElement.y
                };
            }
        } else if (this.currentTool === 'text') {
            const text = prompt(Locale.t('webPainter.promptText'));
            if (text) {
                this.elements.push({
                    type: 'text',
                    layerId: this.currentLayerId,
                    x: pos.x,
                    y: pos.y,
                    text: text,
                    fontSize: this.settings.fontSize,
                    fontFamily: this.settings.fontFamily,
                    color: this.settings.textColor
                });
                this._saveHistory();
            }
        } else if (this.currentTool === 'marker') {
            const text = prompt(Locale.t('webPainter.promptPin'));
            if (text !== null) {
                this.elements.push({
                    type: 'marker',
                    layerId: this.currentLayerId,
                    x: pos.x,
                    y: pos.y,
                    text: text,
                    fontSize: this.settings.fontSize,
                    fontFamily: this.settings.fontFamily,
                    color: this.settings.textColor,
                    strokeColor: this.settings.strokeColor
                });
                this._saveHistory();
            }
        } else if (this.currentTool === 'pen') {
            this.isDrawing = true;
            this.tempShape = {
                type: 'path',
                layerId: this.currentLayerId,
                points: [{ x: pos.x, y: pos.y }],
                strokeColor: this.settings.strokeColor,
                lineWidth: this.settings.lineWidth
            };
        } else {
            this.isDrawing = true;
            this.drawStart = pos;
        }
        
        this._render();
    }

    _handleMouseMove(e) {
        const pos = this._getMousePos(e);
        
        if (this.currentTool === 'select' && this.selectedElement && this.dragStart) {
            const dx = pos.x - this.dragStart.x;
            const dy = pos.y - this.dragStart.y;

            if (this.selectedElement.type === 'path') {
                // 移動路徑中的所有點
                this.selectedElement.points.forEach(p => {
                    p.x += dx;
                    p.y += dy;
                });
                this.dragStart = { x: pos.x, y: pos.y };
            } else {
                this.selectedElement.x = pos.x - this.dragOffset.x;
                this.selectedElement.y = pos.y - this.dragOffset.y;
            }
            this._render();
        } else if (this.isDrawing && this.tempShape) {
            if (this.currentTool === 'pen') {
                this.tempShape.points.push({ x: pos.x, y: pos.y });
            } else {
                this.tempShape = {
                    type: this.currentTool,
                    layerId: this.currentLayerId,
                    x: this.drawStart.x,
                    y: this.drawStart.y,
                    x2: pos.x,
                    y2: pos.y,
                    strokeColor: this.settings.strokeColor,
                    fillColor: this.settings.fillColor,
                    lineWidth: this.settings.lineWidth
                };
            }
            this._render();
        } else {
            const hovered = this._getElementAtPos(pos.x, pos.y);
            if (hovered !== this.hoveredElement) {
                this.hoveredElement = hovered;
                this.canvas.style.cursor = hovered ? 'move' : 'crosshair';
            }
        }
    }

    _handleMouseUp(e) {
        if (this.isDrawing && this.tempShape) {
            this.elements.push(this.tempShape);
            this._saveHistory();
            this.tempShape = null;
        }
        
        this.isDrawing = false;
        this.drawStart = null;
        this.dragStart = null;
        this._render();
    }

    _handleDoubleClick(e) {
        const pos = this._getMousePos(e);
        const element = this._getElementAtPos(pos.x, pos.y);
        
        if (element?.type === 'text') {
            const newText = prompt(Locale.t('webPainter.editTextTitle'), element.text);
            if (newText !== null) {
                element.text = newText;
                this._saveHistory();
                this._render();
            }
        }
    }

    _getElementAtPos(x, y) {
        const visibleLayerIds = new Set(
            this.layers.filter(l => l.visible).map(l => l.id)
        );

        for (let i = this.elements.length - 1; i >= 0; i--) {
            const el = this.elements[i];
            // 只允許選取可見圖層的元素
            if (visibleLayerIds.has(el.layerId) && this._isPointInElement(x, y, el)) {
                return el;
            }
        }
        return null;
    }

    _isPointInElement(x, y, el) {
        if (el.type === 'text') {
            this.ctx.font = `${el.fontSize}px ${el.fontFamily}`;
            const metrics = this.ctx.measureText(el.text);
            const width = metrics.width;
            const height = el.fontSize;
            return x >= el.x && x <= el.x + width &&
                   y >= el.y - height && y <= el.y;
        } else if (el.type === 'rect') {
            const minX = Math.min(el.x, el.x2);
            const maxX = Math.max(el.x, el.x2);
            const minY = Math.min(el.y, el.y2);
            const maxY = Math.max(el.y, el.y2);
            return x >= minX && x <= maxX && y >= minY && y <= maxY;
        } else if (el.type === 'circle') {
            const cx = (el.x + el.x2) / 2;
            const cy = (el.y + el.y2) / 2;
            const rx = Math.abs(el.x2 - el.x) / 2;
            const ry = Math.abs(el.y2 - el.y) / 2;
            return Math.pow((x - cx) / rx, 2) + Math.pow((y - cy) / ry, 2) <= 1;
        } else if (el.type === 'marker') {
            // 點擊中心點或文字區域
            const dist = Math.sqrt(Math.pow(x - el.x, 2) + Math.pow(y - el.y, 2));
            if (dist <= 10) return true;

            // 文字區域檢測 (簡化版)
            if (el.text) {
                this.ctx.font = `bold ${el.fontSize}px ${el.fontFamily}`;
                const metrics = this.ctx.measureText(el.text);
                const tx = el.x + 10;
                const ty = el.y - 10;
                return x >= tx - 4 && x <= tx + metrics.width + 4 &&
                       y >= ty - el.fontSize && y <= ty + 4;
            }
        } else if (el.type === 'path') {
            // 偵測是否點擊在路徑線段附近
            if (!el.points || el.points.length < 2) return false;
            
            const threshold = 10; // 判定範圍
            for (let i = 0; i < el.points.length - 1; i++) {
                const p1 = el.points[i];
                const p2 = el.points[i+1];
                
                // 計算點到線段的距離
                const dist = this._getPointToSegmentDistance(x, y, p1.x, p1.y, p2.x, p2.y);
                if (dist <= threshold) return true;
            }
        }
        return false;
    }

    _getPointToSegmentDistance(px, py, x1, y1, x2, y2) {
        const dx = x2 - x1;
        const dy = y2 - y1;
        if (dx === 0 && dy === 0) return Math.sqrt(Math.pow(px - x1, 2) + Math.pow(py - y1, 2));

        const t = ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy);
        if (t < 0) return Math.sqrt(Math.pow(px - x1, 2) + Math.pow(py - y1, 2));
        if (t > 1) return Math.sqrt(Math.pow(px - x2, 2) + Math.pow(py - y2, 2));

        const closestX = x1 + t * dx;
        const closestY = y1 + t * dy;
        return Math.sqrt(Math.pow(px - closestX, 2) + Math.pow(py - closestY, 2));
    }

    _deleteElement(element) {
        const index = this.elements.indexOf(element);
        if (index > -1) {
            this.elements.splice(index, 1);
            this.selectedElement = null;
            this._saveHistory();
            this._render();
        }
    }

    _render() {
        const ctx = this.ctx;
        ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        
        ctx.save();
        ctx.scale(this.zoom, this.zoom);

        // 1. 繪製背景圖片
        if (this.backgroundImage) {
            const scale = Math.min(
                this.canvas.width / this.backgroundImage.width,
                this.canvas.height / this.backgroundImage.height
            ) / (window.devicePixelRatio || 1);
            const w = this.backgroundImage.width * scale;
            const h = this.backgroundImage.height * scale;
            const x = (this.canvas.width / (window.devicePixelRatio || 1) / this.zoom - w) / 2;
            const y = (this.canvas.height / (window.devicePixelRatio || 1) / this.zoom - h) / 2;
            ctx.drawImage(this.backgroundImage, x, y, w, h);
        }
        
        // 2. 依圖層順序繪製元素
        const visibleLayerIds = new Set(
            this.layers.filter(l => l.visible).map(l => l.id)
        );

        this.elements.forEach(el => {
            if (visibleLayerIds.has(el.layerId)) {
                this._drawElement(el);
            }
        });
        
        // 3. 繪製臨時形狀
        if (this.tempShape) {
            this._drawElement(this.tempShape, true);
        }
        
        // 4. 繪製選擇框
        if (this.selectedElement) {
            this._drawSelection(this.selectedElement);
        }

        ctx.restore();
    }

    _drawElement(el, isTemp = false) {
        const ctx = this.ctx;
        
        if (el.type === 'text') {
            ctx.font = `${el.fontSize}px ${el.fontFamily}`;
            ctx.fillStyle = el.color;
            ctx.fillText(el.text, el.x, el.y);
        } else if (el.type === 'rect') {
            ctx.strokeStyle = el.strokeColor;
            ctx.fillStyle = el.fillColor;
            ctx.lineWidth = el.lineWidth;
            const w = el.x2 - el.x;
            const h = el.y2 - el.y;
            ctx.fillRect(el.x, el.y, w, h);
            ctx.strokeRect(el.x, el.y, w, h);
        } else if (el.type === 'circle') {
            ctx.strokeStyle = el.strokeColor;
            ctx.fillStyle = el.fillColor;
            ctx.lineWidth = el.lineWidth;
            ctx.beginPath();
            const cx = (el.x + el.x2) / 2;
            const cy = (el.y + el.y2) / 2;
            const rx = Math.abs(el.x2 - el.x) / 2;
            const ry = Math.abs(el.y2 - el.y) / 2;
            ctx.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2);
            ctx.fill();
            ctx.stroke();
        } else if (el.type === 'line' || el.type === 'arrow') {
            ctx.strokeStyle = el.strokeColor;
            ctx.lineWidth = el.lineWidth;
            ctx.beginPath();
            ctx.moveTo(el.x, el.y);
            ctx.lineTo(el.x2, el.y2);
            ctx.stroke();
            
            if (el.type === 'arrow') {
                this._drawArrowHead(ctx, el.x, el.y, el.x2, el.y2, el.strokeColor, el.lineWidth);
            }
        } else if (el.type === 'marker') {
            // 繪製定位點 (紅圈)
            ctx.beginPath();
            ctx.arc(el.x, el.y, 6, 0, Math.PI * 2);
            ctx.fillStyle = 'var(--cl-danger)';
            ctx.fill();
            ctx.strokeStyle = 'var(--cl-text-inverse)';
            ctx.lineWidth = 2;
            ctx.stroke();

            // 繪製標註文字
            if (el.text) {
                ctx.font = `bold ${el.fontSize}px ${el.fontFamily}`;
                
                // 計算文字背景
                const metrics = ctx.measureText(el.text);
                const padding = 4;
                const tx = el.x + 10;
                const ty = el.y - 10;
                
                // 文字框背景
                ctx.fillStyle = 'var(--cl-bg-surface-overlay)';
                ctx.fillRect(tx - padding, ty - el.fontSize, metrics.width + padding * 2, el.fontSize + padding);
                ctx.strokeStyle = el.strokeColor;
                ctx.lineWidth = 1;
                ctx.strokeRect(tx - padding, ty - el.fontSize, metrics.width + padding * 2, el.fontSize + padding);
                
                // 文字內容
                ctx.fillStyle = el.color;
                ctx.fillText(el.text, tx, ty);
            }
        } else if (el.type === 'path') {
            if (el.points && el.points.length > 0) {
                ctx.strokeStyle = el.strokeColor;
                ctx.lineWidth = el.lineWidth;
                ctx.lineCap = 'round';
                ctx.lineJoin = 'round';
                ctx.beginPath();
                ctx.moveTo(el.points[0].x, el.points[0].y);
                for (let i = 1; i < el.points.length; i++) {
                    ctx.lineTo(el.points[i].x, el.points[i].y);
                }
                ctx.stroke();
            }
        }
    }

    _drawArrowHead(ctx, x1, y1, x2, y2, color, width) {
        const angle = Math.atan2(y2 - y1, x2 - x1);
        const arrowLength = 15;
        const arrowAngle = Math.PI / 6;
        
        ctx.fillStyle = color;
        ctx.beginPath();
        ctx.moveTo(x2, y2);
        ctx.lineTo(
            x2 - arrowLength * Math.cos(angle - arrowAngle),
            y2 - arrowLength * Math.sin(angle - arrowAngle)
        );
        ctx.lineTo(
            x2 - arrowLength * Math.cos(angle + arrowAngle),
            y2 - arrowLength * Math.sin(angle + arrowAngle)
        );
        ctx.closePath();
        ctx.fill();
    }

    _drawSelection(el) {
        const ctx = this.ctx;
        ctx.strokeStyle = 'var(--cl-canvas-blue)';
        ctx.lineWidth = 2;
        ctx.setLineDash([5, 5]);
        
        if (el.type === 'text') {
            ctx.font = `${el.fontSize}px ${el.fontFamily}`;
            const metrics = ctx.measureText(el.text);
            ctx.strokeRect(el.x - 2, el.y - el.fontSize - 2, metrics.width + 4, el.fontSize + 4);
        } else if (el.type === 'rect') {
            const w = el.x2 - el.x;
            const h = el.y2 - el.y;
            ctx.strokeRect(el.x - 2, el.y - 2, w + 4, h + 4);
        } else if (el.type === 'circle') {
            const cx = (el.x + el.x2) / 2;
            const cy = (el.y + el.y2) / 2;
            const rx = Math.abs(el.x2 - el.x) / 2;
            const ry = Math.abs(el.y2 - el.y) / 2;
            ctx.beginPath();
            ctx.ellipse(cx, cy, rx + 2, ry + 2, 0, 0, Math.PI * 2);
            ctx.stroke();
        } else if (el.type === 'marker') {
            const tx = el.x + 10;
            const ty = el.y - 10;
            ctx.font = `bold ${el.fontSize}px ${el.fontFamily}`;
            const metrics = ctx.measureText(el.text);
            ctx.strokeRect(el.x - 8, el.y - 8, 16, 16);
            if (el.text) {
                ctx.strokeRect(tx - 6, ty - el.fontSize - 2, metrics.width + 12, el.fontSize + 6);
            }
        } else if (el.type === 'path') {
            if (el.points && el.points.length > 0) {
                let minX = el.points[0].x, maxX = el.points[0].x;
                let minY = el.points[0].y, maxY = el.points[0].y;
                
                el.points.forEach(p => {
                    minX = Math.min(minX, p.x);
                    maxX = Math.max(maxX, p.x);
                    minY = Math.min(minY, p.y);
                    maxY = Math.max(maxY, p.y);
                });
                
                ctx.strokeRect(minX - 4, minY - 4, (maxX - minX) + 8, (maxY - minY) + 8);
            }
        }
        
        ctx.setLineDash([]);
    }

    _saveHistory() {
        // 如果歷史記錄過多，移除最早的
        if (this.history.length > 50) {
            this.history.shift();
            this.historyIndex--;
        }
        
        this.history = this.history.slice(0, this.historyIndex + 1);
        this.history.push(structuredClone(this.elements));
        this.historyIndex = this.history.length - 1;
    }

    _undo() {
        if (this.historyIndex > 0) {
            this.historyIndex--;
            this.elements = structuredClone(this.history[this.historyIndex]);
            this.selectedElement = null; // Undo 後取消選取，避免引用錯誤
            this._render();
        }
    }

    _redo() {
        if (this.historyIndex < this.history.length - 1) {
            this.historyIndex++;
            this.elements = structuredClone(this.history[this.historyIndex]);
            this.selectedElement = null;
            this._render();
        }
    }

    _copy() {
        if (this.selectedElement) {
            this.clipboard = structuredClone(this.selectedElement);
            // 提示一下複製成功? (可選)
        }
    }

    _paste() {
        if (this.clipboard) {
            const newElement = structuredClone(this.clipboard);
            
            // 偏移位置，讓貼上的物件顯而易見
            const offset = 20;
            newElement.x += offset;
            newElement.y += offset;
            
            // 如果是矩形、圓形、線條，終點也要偏移
            if (newElement.x2 !== undefined) newElement.x2 += offset;
            if (newElement.y2 !== undefined) newElement.y2 += offset;
            
            // 如果是路徑，所有點都要偏移
            if (newElement.type === 'path' && newElement.points) {
                newElement.points.forEach(p => {
                    p.x += offset;
                    p.y += offset;
                });
            }

            // 貼上到當前圖層
            newElement.layerId = this.currentLayerId;
            
            this.elements.push(newElement);
            this.selectedElement = newElement;
            this._saveHistory();
            this._render();
        }
    }

    // 與 MapEditor 相同的匯出方法...
    _exportImage() {
        const dataUrl = this.canvas.toDataURL('image/png');
        this._exportImageWithMetadata(dataUrl);
    }

    _exportImageWithMetadata(dataUrl) {
        const metadata = {
            elements: this.elements,
            settings: this.settings,
            timestamp: new Date().toISOString(),
            version: '2.0',
            canvasSize: {
                width: this.width,
                height: this.height
            }
        };

        const base64 = dataUrl.split(',')[1];
        const binary = atob(base64);
        const array = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            array[i] = binary.charCodeAt(i);
        }

        const pngWithMetadata = this._addPNGTextChunk(array, 'MapEditor', JSON.stringify(metadata));

        const blob = new Blob([pngWithMetadata], { type: 'image/png' });
        const link = document.createElement('a');
        link.download = Locale.t('webPainter.exportFilename') + Date.now() + '.png';
        link.href = URL.createObjectURL(blob);
        link.click();
        URL.revokeObjectURL(link.href);

        console.log(Locale.t('webPainter.exportSuccess'));
        console.log(Locale.t('webPainter.metadataLabel'), metadata);
    }

    _addPNGTextChunk(pngData, keyword, text) {
        const iendPosition = pngData.length - 12;
        
        const keywordBytes = new TextEncoder().encode(keyword);
        const textBytes = new TextEncoder().encode(text);
        const chunkData = new Uint8Array(keywordBytes.length + 1 + textBytes.length);
        
        chunkData.set(keywordBytes, 0);
        chunkData[keywordBytes.length] = 0;
        chunkData.set(textBytes, keywordBytes.length + 1);
        
        const chunkType = new TextEncoder().encode('tEXt');
        const chunkLength = chunkData.length;
        
        const chunk = new Uint8Array(12 + chunkLength);
        const view = new DataView(chunk.buffer);
        
        view.setUint32(0, chunkLength, false);
        chunk.set(chunkType, 4);
        chunk.set(chunkData, 8);
        
        const crcData = new Uint8Array(4 + chunkLength);
        crcData.set(chunkType, 0);
        crcData.set(chunkData, 4);
        const crc = this._calculateCRC(crcData);
        view.setUint32(8 + chunkLength, crc, false);
        
        const result = new Uint8Array(pngData.length + chunk.length);
        result.set(pngData.subarray(0, iendPosition), 0);
        result.set(chunk, iendPosition);
        result.set(pngData.subarray(iendPosition), iendPosition + chunk.length);
        
        return result;
    }

    _calculateCRC(data) {
        const table = this._getCRCTable();
        let crc = 0xFFFFFFFF;
        
        for (let i = 0; i < data.length; i++) {
            const byte = data[i];
            const index = (crc ^ byte) & 0xFF;
            crc = (crc >>> 8) ^ table[index];
        }
        
        return (crc ^ 0xFFFFFFFF) >>> 0;
    }

    _getCRCTable() {
        if (this._crcTable) return this._crcTable;
        
        const table = new Uint32Array(256);
        for (let n = 0; n < 256; n++) {
            let c = n;
            for (let k = 0; k < 8; k++) {
                c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
            }
            table[n] = c;
        }
        
        this._crcTable = table;
        return table;
    }

    _exportJSON() {
        const data = {
            elements: this.elements,
            settings: this.settings,
            timestamp: new Date().toISOString()
        };
        const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
        const link = document.createElement('a');
        link.download = Locale.t('webPainter.configFilename') + Date.now() + '.json';
        link.href = URL.createObjectURL(blob);
        link.click();
    }

    loadJSON(jsonData) {
        try {
            const data = typeof jsonData === 'string' ? JSON.parse(jsonData) : jsonData;
            this.elements = data.elements || [];
            if (data.settings) {
                Object.assign(this.settings, data.settings);
            }
            this._saveHistory();
            this._render();
        } catch (error) {
            console.error(Locale.t('webPainter.loadFailed'), error);
            ModalPanel.alert({ message: "載入配置失敗" });
        }
    }

    setBackgroundImage(imageUrl) {
        const img = new Image();
        img.onload = () => {
            this.backgroundImage = img;
            this._render();
        };
        img.src = imageUrl;
    }

    clear() {
        this.elements = [];
        this.backgroundImage = null;
        this._saveHistory();
        this._render();
    }

    async loadImageWithMetadata(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            
            reader.onload = async (event) => {
                const arrayBuffer = event.target.result;
                const uint8Array = new Uint8Array(arrayBuffer);
                
                const blob = new Blob([uint8Array], { type: 'image/png' });
                const imageUrl = URL.createObjectURL(blob);
                
                const img = new Image();
                img.onload = () => {
                    this.backgroundImage = img;
                    URL.revokeObjectURL(imageUrl);
                    
                    const metadata = this._extractPNGMetadata(uint8Array);
                    if (metadata) {
                        console.log(Locale.t('webPainter.loadPngSuccess'));
                        console.log(Locale.t('webPainter.metadataLabel'), metadata);
                        
                        if (metadata.elements) {
                            this.elements = metadata.elements;
                        }
                        if (metadata.settings) {
                            Object.assign(this.settings, metadata.settings);
                        }
                        this._saveHistory();
                    } else {
                        console.log(Locale.t('webPainter.pngNoMeta'));
                    }
                    
                    this._render();
                    resolve(metadata);
                };
                
                img.onerror = () => reject(new Error(Locale.t('webPainter.loadImageFailed')));
                img.src = imageUrl;
            };
            
            reader.onerror = () => reject(new Error(Locale.t('webPainter.readFileFailed')));
            reader.readAsArrayBuffer(file);
        });
    }

    _extractPNGMetadata(pngData) {
        let offset = 8;
        
        while (offset < pngData.length - 12) {
            const view = new DataView(pngData.buffer, pngData.byteOffset + offset);
            const chunkLength = view.getUint32(0, false);
            const chunkType = String.fromCharCode(...pngData.subarray(offset + 4, offset + 8));
            
            if (chunkType === 'IEND') break;
            
            if (chunkType === 'tEXt') {
                const chunkData = pngData.subarray(offset + 8, offset + 8 + chunkLength);
                
                let nullIndex = -1;
                for (let i = 0; i < chunkData.length; i++) {
                    if (chunkData[i] === 0) {
                        nullIndex = i;
                        break;
                    }
                }
                
                if (nullIndex !== -1) {
                    const keyword = new TextDecoder().decode(chunkData.subarray(0, nullIndex));
                    
                    if (keyword === 'MapEditor') {
                        const text = new TextDecoder().decode(chunkData.subarray(nullIndex + 1));
                        try {
                            return JSON.parse(text);
                        } catch (e) {
                            console.error(Locale.t('webPainter.parseMetaFailed'), e);
                        }
                    }
                }
            }
            
            offset += 12 + chunkLength;
        }
        
        return null;
    }
}

export default MapEditorV2;
