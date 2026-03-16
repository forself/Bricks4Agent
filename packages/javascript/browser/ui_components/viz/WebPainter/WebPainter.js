/**
 * WebPainter - 獨立的可重用繪圖板元件
 * 功能：支援圖層、縮放、多種繪圖工具、匯出 PNG/JSON
 */

import { EditorButton } from '../../common/EditorButton/index.js';
import { BasicButton } from '../../common/BasicButton/index.js';
import { ButtonGroup } from '../../common/ButtonGroup/index.js';
import { UploadButton } from '../../common/UploadButton/index.js';
import { ColorPicker } from '../../common/ColorPicker/index.js';
import { NumberInput } from '../../form/NumberInput/index.js';
import { SimpleDialog } from '../../common/Dialog/index.js';

import { ModalPanel } from '../../layout/Panel/index.js';
import Locale from '../../i18n/index.js';

export class WebPainter {
    constructor(options = {}) {
        this.container = typeof options.container === 'string'
            ? document.querySelector(options.container)
            : options.container;

        this.width = options.width || 800;
        this.height = options.height || 600;
        
        // 功能開關配置 (預設全開)
        this.features = {
            header: true,       // 顯示上方工具列
            settings: true,     // 顯示設定面板
            
            // 工具列細項
            upload: true,       // 上傳底圖
            tools: true,        // 繪圖工具 (選擇、畫筆、形狀等)
            delete: true,       // 刪除選中
            clear: true,        // 清空畫布
            export: true,       // 匯出/存檔
            zoom: true,         // 縮放控制
            layers: true,       // 圖層管理
            ...options.features
        };

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
            fillColor: 'color-mix(in srgb, var(--cl-danger) 20%, transparent)',
            lineWidth: 2
        };
        
        // 裁切模式狀態
        this.cropMode = false;
        this.cropRect = null;  // { x, y, width, height }
        this.cropHandle = null; // 當前拖拉的控制點
        
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
        wrapper.className = 'web-painter';
        wrapper.style.cssText = `
            width: 100%;
            max-width: ${this.width}px;
            margin: 0 auto;
            font-family: var(--cl-font-family-cjk);
        `;

        // 1. 工具列 (依配置顯示)
        let toolbar = null;
        if (this.features.header) {
            toolbar = this._createToolbar();
            wrapper.appendChild(toolbar);
        }
        
        // 2. 設定面板 (依配置顯示)
        let settingsPanel = null;
        if (this.features.settings) {
            settingsPanel = this._createSettingsPanel();
            wrapper.appendChild(settingsPanel);
        }

        // 3. 畫布容器
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
        this.canvasContainer = canvasContainer; // 儲存引用供裁切 UI 使用
        canvasContainer.appendChild(canvas);

        // 4. 圖層面板 (依配置顯示)
        if (this.features.layers) {
            this.layerPanel = this._createLayerPanel();
            canvasContainer.appendChild(this.layerPanel);
        }

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
            onClick: async () => {
                const name = await this._prompt(Locale.t('webPainter.layerNameLabel'), Locale.t('webPainter.defaultLayerName', { n: this.layers.length + 1 }));
                if (name) {
                    const safeName = name.trim().substring(0, 50);
                    const newId = `layer-${Date.now()}`;
                    this.layers.push({ id: newId, name: safeName, visible: true, locked: false });
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
            deleteLayer.onclick = async (e) => {
                e.stopPropagation();
                if (this.layers.length > 1 && await this._confirm(Locale.t('webPainter.confirmDeleteLayer', { name: layer.name }))) {
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
        const T = EditorButton.TYPES;
        const toolbar = document.createElement('div');
        toolbar.className = 'map-editor-toolbar';
        toolbar.style.cssText = `
            background: linear-gradient(135deg, var(--cl-gradient-start) 0%, var(--cl-gradient-end) 100%);
            border: 1px solid var(--cl-indigo);
            border-radius: var(--cl-radius-lg) var(--cl-radius-lg) 0 0;
            padding: 12px;
            display: flex;
            gap: 8px;
            flex-wrap: wrap;
            align-items: center;
        `;

        // 上傳圖片按鈕
        if (this.features.upload) {
            const uploadBtn = new UploadButton({
                type: UploadButton.TYPES.IMAGE,
                onSelect: (files) => this._handleImageUpload(files[0]),
                tooltip: Locale.t('webPainter.uploadBg')
            });
            uploadBtn.mount(toolbar);
            this._addSeparator(toolbar);
        }

        // 工具按鈕
        if (this.features.tools) {
            const tools = [
                { tool: 'select', type: T.SELECT },
                { tool: 'text', type: T.TEXT },
                { tool: 'pen', type: T.PEN },
                { tool: 'rect', type: T.RECT },
                { tool: 'circle', type: T.CIRCLE },
                { tool: 'line', type: T.LINE_TOOL },
                { tool: 'arrow', type: T.ARROW }
            ];

            this.toolButtons = {};
            this.editorToolButtons = {};

            const toolGroup = new ButtonGroup({
                theme: 'gradient',
                buttons: tools.map(({tool, type}) => {
                    const btn = new EditorButton({
                        type: type,
                        theme: 'gradient',
                        active: tool === this.currentTool,
                        onClick: () => {
                            this.currentTool = tool;
                            this._updateToolButtons();
                        }
                    });
                    this.toolButtons[tool] = btn.element;
                    this.editorToolButtons[tool] = btn;
                    return btn;
                })
            });
            toolGroup.mount(toolbar);
            this._addSeparator(toolbar);
        }

        // 刪除與清空按鈕
        if (this.features.delete || this.features.clear) {
            const actionButtons = [];

            if (this.features.delete) {
                const deleteBtn = new EditorButton({
                    type: T.CLEAR,
                    label: Locale.t('webPainter.deleteBtn'),
                    theme: 'gradient',
                    onClick: () => {
                        if (this.selectedElement) {
                            this._deleteElement(this.selectedElement);
                        }
                    }
                });
                actionButtons.push(deleteBtn);
            }

            if (this.features.clear) {
                const clearBtn = new EditorButton({
                    type: T.CLEAR_ALL,
                    theme: 'gradient',
                    onClick: async () => {
                        if (await this._confirm(Locale.t('webPainter.confirmClearAll'))) {
                            this.elements = [];
                            this._saveHistory();
                            this._render();
                        }
                    }
                });
                actionButtons.push(clearBtn);
            }

            if (actionButtons.length > 0) {
                const actionGroup = new ButtonGroup({
                    theme: 'gradient',
                    buttons: actionButtons
                });
                actionGroup.mount(toolbar);
            }
        }

        if (this.features.export) {
            this._addSeparator(toolbar);

            const exportGroup = new ButtonGroup({
                theme: 'gradient',
                buttons: [
                    new EditorButton({
                        type: T.EXPORT_PNG,
                        theme: 'gradient',
                        onClick: () => this._exportImage()
                    }),
                    new EditorButton({
                        type: T.EXPORT_JSON,
                        theme: 'gradient',
                        onClick: () => this._exportJSON()
                    })
                ]
            });
            exportGroup.mount(toolbar);
        }

        if (this.features.layers) {
            this._addSeparator(toolbar);

            const layerBtn = new EditorButton({
                type: T.LAYERS,
                theme: 'gradient',
                onClick: () => this._toggleLayerPanel()
            });
            layerBtn.mount(toolbar);
        }

        if (this.features.zoom) {
            this._addSeparator(toolbar);

            const zoomText = document.createElement('span');
            zoomText.textContent = '100%';
            zoomText.style.cssText = 'font-size: var(--cl-font-size-sm); min-width: 40px; text-align: center; font-weight: bold; color: var(--cl-text-inverse);';

            const zoomGroup = new ButtonGroup({
                theme: 'gradient',
                buttons: [
                    new EditorButton({
                        type: T.ZOOM_OUT,
                        theme: 'gradient',
                        onClick: () => {
                            this.zoom = Math.max(0.1, this.zoom - 0.1);
                            this._render();
                            zoomText.textContent = `${Math.round(this.zoom * 100)}%`;
                        }
                    }),
                    new EditorButton({
                        type: T.ZOOM_IN,
                        theme: 'gradient',
                        onClick: () => {
                            this.zoom = Math.min(5, this.zoom + 0.1);
                            this._render();
                            zoomText.textContent = `${Math.round(this.zoom * 100)}%`;
                        }
                    })
                ]
            });
            zoomGroup.mount(toolbar);
            toolbar.appendChild(zoomText);

            const zoomResetBtn = new EditorButton({
                type: 'custom',
                label: '100%',
                theme: 'gradient',
                onClick: () => {
                    this.zoom = 1;
                    this._render();
                    zoomText.textContent = '100%';
                }
            });
            zoomResetBtn.mount(toolbar);
        }

        if (this.features.tools && this.toolButtons) {
            this._updateToolButtons();
        }

        return toolbar;
    }

    _addSeparator(parent) {
        const sep = document.createElement('div');
        sep.style.cssText = `width: 1px; height: 24px; background: var(--cl-divider-inverse); margin: 0 4px;`;
        parent.appendChild(sep);
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
            width: '120px',
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
            width: '120px',
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

    _updateToolButtons() {
        // 使用 EditorButton 的 active 屬性
        if (this.editorToolButtons) {
            Object.keys(this.editorToolButtons).forEach(tool => {
                const btn = this.editorToolButtons[tool];
                if (btn) {
                    btn.active = (tool === this.currentTool);
                }
            });
        }
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

    async _handleMouseDown(e) {
        const pos = this._getMousePos(e);
        
        // 裁切模式特殊處理
        if (this.cropMode) {
            this.cropHandle = this._getCropHandleAtPoint(pos.x, pos.y);
            if (this.cropHandle) {
                this.dragStart = { x: pos.x, y: pos.y };
            }
            return;
        }
        
        // 裁切工具被選取時進入裁切模式
        if (this.currentTool === 'crop') {
            this._enterCropMode();
            return;
        }
        
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
            const rawText = await this._prompt(Locale.t('webPainter.promptText'));
            if (rawText) {
                this.elements.push({
                    type: 'text',
                    layerId: this.currentLayerId,
                    x: pos.x,
                    y: pos.y,
                    text: rawText.substring(0, 500),
                    fontSize: this.settings.fontSize,
                    fontFamily: this.settings.fontFamily,
                    color: this.settings.textColor
                });
                this._saveHistory();
            }
        } else if (this.currentTool === 'marker') {
            const rawText = await this._prompt(Locale.t('webPainter.promptPin'));
            if (rawText !== null) {
                this.elements.push({
                    type: 'marker',
                    layerId: this.currentLayerId,
                    x: pos.x,
                    y: pos.y,
                    text: rawText.substring(0, 500),
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
            this.tempShape = {
                type: this.currentTool,
                layerId: this.currentLayerId,
                x: pos.x,
                y: pos.y,
                x2: pos.x,
                y2: pos.y,
                strokeColor: this.settings.strokeColor,
                fillColor: this.settings.fillColor,
                lineWidth: this.settings.lineWidth
            };
        }
        
        this._render();
    }

    _handleMouseMove(e) {
        const pos = this._getMousePos(e);
        
        // 裁切模式拖拉處理
        if (this.cropMode && this.cropHandle && this.dragStart) {
            const dx = pos.x - this.dragStart.x;
            const dy = pos.y - this.dragStart.y;
            this._updateCropRect(this.cropHandle, dx, dy);
            this.dragStart = { x: pos.x, y: pos.y };
            this._render();
            return;
        }
        
        // 裁切模式游標樣式
        if (this.cropMode) {
            const handle = this._getCropHandleAtPoint(pos.x, pos.y);
            if (handle) {
                const cursors = {
                    'nw': 'nw-resize', 'ne': 'ne-resize', 'sw': 'sw-resize', 'se': 'se-resize',
                    'n': 'n-resize', 's': 's-resize', 'w': 'w-resize', 'e': 'e-resize',
                    'move': 'move'
                };
                this.canvas.style.cursor = cursors[handle] || 'default';
            } else {
                this.canvas.style.cursor = 'default';
            }
            return;
        }
        
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
        // 裁切模式
        if (this.cropMode) {
            this.cropHandle = null;
            this.dragStart = null;
            return;
        }
        
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

    async _handleDoubleClick(e) {
        const pos = this._getMousePos(e);
        const element = this._getElementAtPos(pos.x, pos.y);
        
        if (element?.type === 'text') {
            const newText = await this._prompt(Locale.t('webPainter.editTextTitle'), element.text);
            if (newText !== null) {
                element.text = newText.substring(0, 500);
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
        
        // 5. 繪製裁切覆蓋層 (在 zoom 之外繪製)
        if (this.cropMode) {
            this._renderCropOverlay();
        }
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
        this.history.push(JSON.parse(JSON.stringify(this.elements)));
        this.historyIndex = this.history.length - 1;
    }

    _undo() {
        if (this.historyIndex > 0) {
            this.historyIndex--;
            this.elements = JSON.parse(JSON.stringify(this.history[this.historyIndex]));
            this.selectedElement = null; // Undo 後取消選取，避免引用錯誤
            this._render();
        }
    }

    _redo() {
        if (this.historyIndex < this.history.length - 1) {
            this.historyIndex++;
            this.elements = JSON.parse(JSON.stringify(this.history[this.historyIndex]));
            this.selectedElement = null;
            this._render();
        }
    }

    _copy() {
        if (this.selectedElement) {
            this.clipboard = JSON.parse(JSON.stringify(this.selectedElement));
            // 提示一下複製成功? (可選)
        }
    }

    _paste() {
        if (this.clipboard) {
            const newElement = JSON.parse(JSON.stringify(this.clipboard));
            
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
        // 使用 iTXt (International Text) 支援 UTF-8
        // 參見 PNG Spec: https://www.w3.org/TR/PNG/#11iTXt
        
        const iendPosition = pngData.length - 12;
        
        // Keyword 必須是 Latin-1，這裡我們傳入的是 'MapEditor'
        const keywordBytes = new TextEncoder().encode(keyword);
        const textBytes = new TextEncoder().encode(text);
        
        // iTXt Structure:
        // Keyword (null-terminated)
        // Compression flag (1 byte, 0=uncompressed)
        // Compression method (1 byte, 0)
        // Language tag (null-terminated) - empty
        // Translated keyword (null-terminated) - empty
        // Text (UTF-8)

        // 計算長度: keyword + null(1) + flag(1) + method(1) + lang_tag_null(1) + trans_key_null(1) + text
        const chunkDataLength = keywordBytes.length + 5 + textBytes.length;
        const chunkData = new Uint8Array(chunkDataLength);
        
        let cursor = 0;
        
        // 1. Keyword
        chunkData.set(keywordBytes, cursor);
        cursor += keywordBytes.length;
        chunkData[cursor++] = 0; // Null separator
        
        // 2-5. Headers
        chunkData[cursor++] = 0; // Compression flag (0 = uncompressed)
        chunkData[cursor++] = 0; // Compression method (0)
        chunkData[cursor++] = 0; // Language tag (empty -> null)
        chunkData[cursor++] = 0; // Translated keyword (empty -> null)
        
        // 6. Text
        chunkData.set(textBytes, cursor);
        
        // Construct Chunk
        const chunkType = new TextEncoder().encode('iTXt');
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

    /**
     * 取得當前編輯器的完整狀態數據
     * @returns {Object} 數據物件
     */
    getData() {
        // 將背景圖片轉為 Base64 (如果有)
        let bgImageData = null;
        if (this.backgroundImage) {
            try {
                const tempCanvas = document.createElement('canvas');
                tempCanvas.width = this.backgroundImage.width;
                tempCanvas.height = this.backgroundImage.height;
                tempCanvas.getContext('2d').drawImage(this.backgroundImage, 0, 0);
                bgImageData = tempCanvas.toDataURL('image/png');
            } catch(e) {
                console.warn('Failed to serialize backgroundImage:', e);
            }
        }
        
        return {
            version: "2.0",
            width: this.width,
            height: this.height,
            zoom: this.zoom,
            layers: this.layers,
            currentLayerId: this.currentLayerId,
            elements: this.elements,
            settings: this.settings,
            backgroundImage: bgImageData,
            timestamp: new Date().toISOString()
        };
    }

    /**
     * 載入狀態數據 - 資安加固版
     * 實作嚴格的輸入驗證、型別檢查與長度限制，防禦 DoS 與 Prototype Pollution。
     * @param {Object|string} data 數據物件或 JSON 字串
     */
    loadData(data) {
        try {
            const state = typeof data === 'string' ? JSON.parse(data) : data;
            if (!state || typeof state !== 'object') throw new Error(Locale.t('webPainter.invalidFormat'));
            
            // 1. 嚴格驗證基礎設定 (防禦 DoS)
            if (Number.isFinite(state.width) && state.width > 10 && state.width <= 10000) {
                this.width = Math.floor(state.width);
            }
            if (Number.isFinite(state.height) && state.height > 10 && state.height <= 10000) {
                this.height = Math.floor(state.height);
            }
            if (Number.isFinite(state.zoom) && state.zoom >= 0.1 && state.zoom <= 10) {
                this.zoom = Number(state.zoom);
            }
            
            // 2. 安全還原圖層 (結構消毒)
            if (Array.isArray(state.layers)) {
                this.layers = state.layers.map(l => ({
                    id: String(l.id),
                    name: String(l.name || Locale.t('webPainter.unnamed')).substring(0, 50), // 限制名稱長度
                    visible: !!l.visible,
                    locked: !!l.locked
                }));
            }
            // 驗證 Layer ID 是否存在
            if (state.currentLayerId && typeof state.currentLayerId === 'string' && 
                this.layers.some(l => l.id === state.currentLayerId)) {
                this.currentLayerId = state.currentLayerId;
            }

            // 3. 安全過濾所有元素 (防止惡意 Payload)
            if (Array.isArray(state.elements)) {
                this.elements = state.elements.map(el => {
                    // 僅解構需要的屬性，丟棄多餘屬性
                    return {
                        type: String(el.type),
                        layerId: String(el.layerId || 'layer-1'),
                        
                        // 座標數值化
                        x: Number(el.x) || 0,
                        y: Number(el.y) || 0,
                        x2: Number(el.x2) || 0,
                        y2: Number(el.y2) || 0,
                        
                        // 內容消毒
                        text: String(el.text || '').substring(0, 500),
                        
                        // 路徑點陣列處理
                        points: Array.isArray(el.points) 
                            ? el.points.map(p => ({ x: Number(p.x) || 0, y: Number(p.y) || 0 })) 
                            : [],
                        
                        // 樣式屬性消毒
                        color: this._sanitizeColor(el.color),
                        fillColor: this._sanitizeColor(el.fillColor),
                        strokeColor: this._sanitizeColor(el.strokeColor),
                        lineWidth: Math.min(Math.max(Number(el.lineWidth) || 1, 1), 50),
                        fontSize: Math.min(Math.max(Number(el.fontSize) || 12, 8), 200),
                        fontFamily: this._sanitizeFont(el.fontFamily)
                    };
                });
            } else {
                this.elements = [];
            }
            
            // 4. 設定值白名單 (防止 Prototype Pollution)
            if (state.settings && typeof state.settings === 'object') {
                const allowedVars = ['fontSize', 'textColor', 'strokeColor', 'fillColor', 'lineWidth', 'fontFamily'];
                allowedVars.forEach(key => {
                    if (Object.prototype.hasOwnProperty.call(state.settings, key)) {
                        const val = state.settings[key];
                        if (key.includes('Color')) {
                            this.settings[key] = this._sanitizeColor(val);
                        } else if (key === 'fontFamily') {
                            this.settings[key] = this._sanitizeFont(val);
                        } else {
                            this.settings[key] = Number(val) || this.settings[key];
                        }
                    }
                });
            }
            
            // 處理舊版資料相容
            if (!this.layers || this.layers.length === 0) {
                 this.layers = [{ id: 'layer-1', name: Locale.t('webPainter.defaultLayer'), visible: true }];
                 this.currentLayerId = 'layer-1';
                 this.elements.forEach(el => { if(!el.layerId) el.layerId = 'layer-1'; });
            }

            // 5. 還原背景圖片 (Base64 -> Image)
            if (state.backgroundImage && typeof state.backgroundImage === 'string') {
                const bgImg = new Image();
                bgImg.onload = () => {
                    this.backgroundImage = bgImg;
                    this._render(); // 重新渲染以顯示背景
                };
                bgImg.onerror = () => {
                    console.warn('Failed to load background image');
                };
                bgImg.src = state.backgroundImage;
            }

            // 若畫布尺寸變更，重新設定
            const canvas = this.canvas;
            if (canvas.width !== this.width * (window.devicePixelRatio || 1) || 
                canvas.height !== this.height * (window.devicePixelRatio || 1)) {
                 this._setupCanvas();
            }
            
            // 更新 UI 與歷史記錄
            if (this.layerListContainer) this._updateLayerList();
            this._saveHistory();
            this._render();
            
            // 若為 Features 啟用狀態，同步 UI 設定值
            if (this.features.settings && this.fontSelect) {
                this.fontSelect.value = this.settings.fontFamily;
            }
            
        } catch (error) {
            console.error(Locale.t('webPainter.loadFailed'), error);
            throw error;
        }
    }

    /**
     * 簡易顏色消毒
     * 僅允許 hex、RGB/RGBA 色彩函式、theme token、color-mix，或基本顏色名稱
     */
    _sanitizeColor(color) {
        if (!color || typeof color !== 'string') return 'var(--cl-text-dark)';
        // 限制長度，避免超長字串
        const c = color.trim().substring(0, 50);
        // 簡單驗證是否包含非法字符 (大致檢查)
        const rgbPattern = new RegExp(`^${'rgb'}a?\\([\\d\\s,.]+\\)$`);
        const colorMixPattern = new RegExp(`^${'color-mix'}\\(.+\\)$`);
        if (/^#[0-9a-fA-F]{3,8}$/.test(c) || 
            rgbPattern.test(c) ||
            colorMixPattern.test(c) ||
            /^var\(--cl-[\w-]+\)$/.test(c) ||
            /^[a-zA-Z]+$/.test(c)) {
            return c;
        }
        return 'var(--cl-text-dark)';
    }

    /**
     * 字型名稱消毒
     * 僅允許字母、數字、空格、引號
     */
    _sanitizeFont(font) {
        if (!font || typeof font !== 'string') return 'Microsoft JhengHei';
        const f = font.trim().substring(0, 50);
        // 只允許安全字符
        return f.replace(/[^\w\s\-,."']/g, '');
    }

    _exportJSON() {
        const data = this.getData();
        const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
        const link = document.createElement('a');
        link.download = Locale.t('webPainter.configFilename') + Date.now() + '.json';
        link.href = URL.createObjectURL(blob);
        link.click();
    }

    loadJSON(jsonData) {
        this.loadData(jsonData);
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
                        
                        try {
                            this.loadData(metadata);
                        } catch (e) {
                            console.warn(Locale.t('webPainter.restoreFailed'), e);
                            // 降級處理：只載入標註
                            if (metadata.elements) this.elements = metadata.elements;
                            this._saveHistory();
                            this._render();
                        }
                    } else {
                        console.log(Locale.t('webPainter.pngNoMeta'));
                    }
                    
                    // 不需要這裡再 render，loadData 已經做了，或是上面的降級處理做了
                    if (!metadata) this._render();
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
        const decoder = new TextDecoder();
        
        while (offset < pngData.length - 12) {
            const view = new DataView(pngData.buffer, pngData.byteOffset + offset);
            const chunkLength = view.getUint32(0, false);
            const chunkType = String.fromCharCode(...pngData.subarray(offset + 4, offset + 8));
            
            if (chunkType === 'IEND') break;
            
            if (chunkType === 'tEXt' || chunkType === 'iTXt') {
                const chunkData = pngData.subarray(offset + 8, offset + 8 + chunkLength);
                
                // Find Keyword Null Separator
                let nullIndex = -1;
                for (let i = 0; i < chunkData.length; i++) {
                    if (chunkData[i] === 0) {
                        nullIndex = i;
                        break;
                    }
                }
                
                if (nullIndex !== -1) {
                    const keyword = decoder.decode(chunkData.subarray(0, nullIndex));
                    
                    if (keyword === 'MapEditor') {
                        let textData = null;
                        
                        if (chunkType === 'tEXt') {
                            // tEXt: Keyword + Null + Text
                            textData = chunkData.subarray(nullIndex + 1);
                        } else if (chunkType === 'iTXt') {
                            // iTXt: Keyword(null-term) + Flag(1) + Method(1) + Lang(null-term) + TransKey(null-term) + Text
                            // 我們需要跳過這些 headers
                            
                            let cursor = nullIndex + 3; // Skip Null(1), Flag(1), Method(1)
                            
                            // Skip Language Tag
                            while (cursor < chunkData.length && chunkData[cursor] !== 0) cursor++;
                            cursor++; // Skip Null
                            
                            // Skip Translated Keyword
                            while (cursor < chunkData.length && chunkData[cursor] !== 0) cursor++;
                            cursor++; // Skip Null
                            
                            if (cursor < chunkData.length) {
                                textData = chunkData.subarray(cursor);
                            }
                        }

                        if (textData) {
                            try {
                                const text = decoder.decode(textData);
                                return JSON.parse(text);
                            } catch (e) {
                                console.error(Locale.t('webPainter.parseMetaFailed'), e);
                            }
                        }
                    }
                }
            }
            
            offset += 12 + chunkLength;
        }
        
        return null;
    }

    // --- 自定義對話框 (Modal) ---
    // --- 自定義對話框 (Modal) - 轉接 SimpleDialog ---
    _prompt(message, defaultValue = '') {
        return SimpleDialog.prompt(message, defaultValue, this.container);
    }

    _confirm(message) {
         return SimpleDialog.confirm(message, this.container);
    }

    _alert(message) {
        return ModalPanel.alert({ message: message });
    }

    // --- 裁切功能 ---
    
    _enterCropMode() {
        this.cropMode = true;
        // 預設裁切框為畫布的 80%
        const margin = 0.1;
        this.cropRect = {
            x: this.width * margin,
            y: this.height * margin,
            width: this.width * (1 - 2 * margin),
            height: this.height * (1 - 2 * margin)
        };
        this._showCropUI();
        this._render();
    }

    _exitCropMode() {
        this.cropMode = false;
        this.cropRect = null;
        this.cropHandle = null;
        this._hideCropUI();
        this._render();
    }

    _showCropUI() {
        // 建立裁切確認/取消按鈕
        if (this.cropUIContainer) return;
        
        const container = document.createElement('div');
        container.className = 'crop-ui-container';
        container.style.cssText = `
            position: absolute; top: 10px; left: 50%; transform: translateX(-50%);
            background: var(--cl-bg-overlay-strong); padding: 8px 16px; border-radius: var(--cl-radius-lg);
            display: flex; gap: 10px; z-index: 100;
        `;
        
        const applyBtn = document.createElement('button');
        applyBtn.textContent = Locale.t('webPainter.applyCrop');
        applyBtn.style.cssText = `
            background: var(--cl-success); color: var(--cl-text-inverse); border: none; padding: 8px 16px;
            border-radius: var(--cl-radius-sm); cursor: pointer; font-weight: bold;
        `;
        applyBtn.onclick = () => this._applyCrop();
        
        const cancelBtn = document.createElement('button');
        cancelBtn.textContent = Locale.t('webPainter.cancelCrop');
        cancelBtn.style.cssText = `
            background: var(--cl-text-secondary); color: var(--cl-text-inverse); border: none; padding: 8px 12px;
            border-radius: var(--cl-radius-sm); cursor: pointer;
        `;
        cancelBtn.onclick = () => {
            this._exitCropMode();
            this.currentTool = 'select';
            this._updateToolButtons();
        };
        
        container.appendChild(applyBtn);
        container.appendChild(cancelBtn);
        
        this.canvasContainer.appendChild(container);
        this.cropUIContainer = container;
    }

    _hideCropUI() {
        if (this.cropUIContainer) {
            this.cropUIContainer.remove();
            this.cropUIContainer = null;
        }
    }

    _applyCrop() {
        if (!this.cropRect) return;
        
        const { x, y, width, height } = this.cropRect;
        
        // 1. 調整所有元素的座標
        this.elements = this.elements.map(el => {
            const newEl = { ...el };
            if ('x' in newEl) newEl.x -= x;
            if ('y' in newEl) newEl.y -= y;
            if ('x2' in newEl) newEl.x2 -= x;
            if ('y2' in newEl) newEl.y2 -= y;
            if (Array.isArray(newEl.points)) {
                newEl.points = newEl.points.map(p => ({ x: p.x - x, y: p.y - y }));
            }
            return newEl;
        });
        
        // 2. 裁切背景圖片 (如果有)
        if (this.backgroundImage) {
            const tempCanvas = document.createElement('canvas');
            tempCanvas.width = width;
            tempCanvas.height = height;
            const tempCtx = tempCanvas.getContext('2d');
            
            // 計算背景圖在畫布上的實際位置與大小
            const scale = Math.min(
                this.width / this.backgroundImage.width,
                this.height / this.backgroundImage.height
            );
            const bgW = this.backgroundImage.width * scale;
            const bgH = this.backgroundImage.height * scale;
            const bgX = (this.width - bgW) / 2;
            const bgY = (this.height - bgH) / 2;
            
            // 在臨時畫布上繪製裁切區域
            tempCtx.drawImage(
                this.backgroundImage,
                (x - bgX) / scale, (y - bgY) / scale, width / scale, height / scale,
                0, 0, width, height
            );
            
            // 將裁切結果轉為新的背景圖
            const newBgImg = new Image();
            newBgImg.src = tempCanvas.toDataURL('image/png');
            newBgImg.onload = () => {
                this.backgroundImage = newBgImg;
                this._render();
            };
        }
        
        // 3. 更新畫布尺寸
        this.width = Math.round(width);
        this.height = Math.round(height);
        this._setupCanvas();
        
        // 4. 退出裁切模式
        this._exitCropMode();
        this.currentTool = 'select';
        this._updateToolButtons();
        this._saveHistory();
        this._render();
    }

    _renderCropOverlay() {
        if (!this.cropMode || !this.cropRect) return;
        
        const ctx = this.ctx;
        const { x, y, width, height } = this.cropRect;
        const cw = this.canvas.width / (window.devicePixelRatio || 1);
        const ch = this.canvas.height / (window.devicePixelRatio || 1);
        
        // 繪製半透明遮罩 (裁切區外)
        ctx.save();
        ctx.fillStyle = 'var(--cl-bg-overlay)';
        // 上
        ctx.fillRect(0, 0, cw, y);
        // 下
        ctx.fillRect(0, y + height, cw, ch - y - height);
        // 左
        ctx.fillRect(0, y, x, height);
        // 右
        ctx.fillRect(x + width, y, cw - x - width, height);
        
        // 繪製裁切框邊界
        ctx.strokeStyle = 'var(--cl-text-inverse)';
        ctx.lineWidth = 2;
        ctx.setLineDash([5, 5]);
        ctx.strokeRect(x, y, width, height);
        
        // 繪製三分線 (Rule of Thirds)
        ctx.strokeStyle = 'var(--cl-bg-inverse-muted)';
        ctx.lineWidth = 1;
        ctx.beginPath();
        // 垂直線
        ctx.moveTo(x + width / 3, y);
        ctx.lineTo(x + width / 3, y + height);
        ctx.moveTo(x + width * 2 / 3, y);
        ctx.lineTo(x + width * 2 / 3, y + height);
        // 水平線
        ctx.moveTo(x, y + height / 3);
        ctx.lineTo(x + width, y + height / 3);
        ctx.moveTo(x, y + height * 2 / 3);
        ctx.lineTo(x + width, y + height * 2 / 3);
        ctx.stroke();
        ctx.setLineDash([]);
        
        // 繪製控制點
        const handleSize = 10;
        ctx.fillStyle = 'var(--cl-primary)';
        const handles = [
            { pos: 'nw', cx: x, cy: y },
            { pos: 'ne', cx: x + width, cy: y },
            { pos: 'sw', cx: x, cy: y + height },
            { pos: 'se', cx: x + width, cy: y + height },
            { pos: 'n', cx: x + width / 2, cy: y },
            { pos: 's', cx: x + width / 2, cy: y + height },
            { pos: 'w', cx: x, cy: y + height / 2 },
            { pos: 'e', cx: x + width, cy: y + height / 2 }
        ];
        handles.forEach(h => {
            ctx.fillRect(h.cx - handleSize / 2, h.cy - handleSize / 2, handleSize, handleSize);
        });
        
        ctx.restore();
    }

    _getCropHandleAtPoint(px, py) {
        if (!this.cropRect) return null;
        const { x, y, width, height } = this.cropRect;
        const threshold = 12;
        
        const handles = [
            { pos: 'nw', cx: x, cy: y },
            { pos: 'ne', cx: x + width, cy: y },
            { pos: 'sw', cx: x, cy: y + height },
            { pos: 'se', cx: x + width, cy: y + height },
            { pos: 'n', cx: x + width / 2, cy: y },
            { pos: 's', cx: x + width / 2, cy: y + height },
            { pos: 'w', cx: x, cy: y + height / 2 },
            { pos: 'e', cx: x + width, cy: y + height / 2 }
        ];
        
        for (const h of handles) {
            if (Math.abs(px - h.cx) < threshold && Math.abs(py - h.cy) < threshold) {
                return h.pos;
            }
        }
        
        // 檢查是否在裁切框內部 (可整體移動)
        if (px > x && px < x + width && py > y && py < y + height) {
            return 'move';
        }
        
        return null;
    }

    _updateCropRect(handle, dx, dy) {
        if (!this.cropRect) return;
        
        const r = this.cropRect;
        const minSize = 50;
        
        switch (handle) {
            case 'nw':
                r.x += dx; r.y += dy; r.width -= dx; r.height -= dy;
                break;
            case 'ne':
                r.y += dy; r.width += dx; r.height -= dy;
                break;
            case 'sw':
                r.x += dx; r.width -= dx; r.height += dy;
                break;
            case 'se':
                r.width += dx; r.height += dy;
                break;
            case 'n':
                r.y += dy; r.height -= dy;
                break;
            case 's':
                r.height += dy;
                break;
            case 'w':
                r.x += dx; r.width -= dx;
                break;
            case 'e':
                r.width += dx;
                break;
            case 'move':
                r.x += dx; r.y += dy;
                break;
        }
        
        // 確保最小尺寸
        if (r.width < minSize) r.width = minSize;
        if (r.height < minSize) r.height = minSize;
        
        // 確保不超出畫布
        if (r.x < 0) r.x = 0;
        if (r.y < 0) r.y = 0;
        if (r.x + r.width > this.width) r.x = this.width - r.width;
        if (r.y + r.height > this.height) r.y = this.height - r.height;
    }
}


