/**
 * DrawingBoard - 純繪圖板元件
 * 專注於手繪功能：畫筆、橡皮擦、顏色、粗細
 * 不含編輯功能（文字、形狀、選擇等）
 */

import { BasicButton } from '../../common/BasicButton/BasicButton.js';
import { EditorButton } from '../../common/EditorButton/EditorButton.js';
import { ButtonGroup } from '../../common/ButtonGroup/ButtonGroup.js';
import { ColorPicker } from '../../common/ColorPicker/ColorPicker.js';
import { NumberInput } from '../../form/NumberInput/NumberInput.js';
import Locale from '../../i18n/index.js';

export class DrawingBoard {
    constructor(options = {}) {
        this.container = typeof options.container === 'string'
            ? document.querySelector(options.container)
            : options.container;

        this.width = options.width || 800;
        this.height = options.height || 600;

        // 繪圖設定
        this.settings = {
            strokeColor: options.strokeColor || 'var(--cl-text)',
            lineWidth: options.lineWidth || 3,
            opacity: options.opacity || 1.0,
            tool: 'pen' // 'pen' | 'eraser' | 'line' | 'highlighter'
        };

        // 繪圖狀態
        this.isDrawing = false;
        this.lastPoint = null;
        this.paths = []; // 儲存所有繪製的路徑
        this.currentPath = null;

        // 歷史記錄
        this.history = [];
        this.historyIndex = -1;
        this.maxHistory = 50;

        // 背景圖片
        this.backgroundImage = null;

        // 回調
        this.onDraw = options.onDraw || null;
        this.onClear = options.onClear || null;

        this._init();
    }

    _init() {
        this.element = this._createUI();
        this.container.appendChild(this.element);
        this._setupEventListeners();
        this._saveHistory();
    }

    _createUI() {
        const wrapper = document.createElement('div');
        wrapper.className = 'drawing-board';
        wrapper.style.cssText = `
            width: 100%;
            max-width: ${this.width + 40}px;
            margin: 0 auto;
            font-family: 'Microsoft JhengHei', -apple-system, BlinkMacSystemFont, sans-serif;
        `;

        // 工具列
        const toolbar = this._createToolbar();
        wrapper.appendChild(toolbar);

        // 畫布容器
        const canvasContainer = document.createElement('div');
        canvasContainer.className = 'drawing-board-canvas-container';
        canvasContainer.style.cssText = `
            position: relative;
            background: var(--cl-bg-subtle);
            border: 2px solid var(--cl-border);
            border-top: none;
            border-radius: 0 0 12px 12px;
            overflow: hidden;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
        `;

        // 主畫布
        const canvas = document.createElement('canvas');
        canvas.width = this.width;
        canvas.height = this.height;
        canvas.style.cssText = `
            background: var(--cl-bg);
            border-radius: 4px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
            cursor: crosshair;
            touch-action: none;
        `;
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');

        // 設定畫布初始樣式
        this.ctx.lineCap = 'round';
        this.ctx.lineJoin = 'round';

        canvasContainer.appendChild(canvas);
        wrapper.appendChild(canvasContainer);

        // 設定面板
        const settingsPanel = this._createSettingsPanel();
        wrapper.appendChild(settingsPanel);

        return wrapper;
    }

    _createToolbar() {
        const toolbar = document.createElement('div');
        toolbar.className = 'drawing-board-toolbar';
        toolbar.style.cssText = `
            background: linear-gradient(135deg, var(--cl-gradient-start) 0%, var(--cl-gradient-end) 100%);
            border-radius: 12px 12px 0 0;
            padding: 12px 16px;
            display: flex;
            gap: 8px;
            flex-wrap: wrap;
            align-items: center;
        `;

        // 儲存工具按鈕參考
        this.toolButtons = {};

        // 繪圖工具群組
        const toolGroup = new ButtonGroup({
            theme: 'gradient',
            gap: '4px',
            buttons: [
                this.toolButtons.pen = new EditorButton({
                    type: EditorButton.TYPES.PEN,
                    label: Locale.t('drawingBoard.pen'),
                    theme: 'gradient',
                    active: true,
                    onClick: () => this._setTool('pen')
                }),
                this.toolButtons.eraser = new EditorButton({
                    type: EditorButton.TYPES.ERASER,
                    label: Locale.t('drawingBoard.eraser'),
                    theme: 'gradient',
                    onClick: () => this._setTool('eraser')
                }),
                this.toolButtons.line = new EditorButton({
                    type: EditorButton.TYPES.LINE_TOOL,
                    label: Locale.t('drawingBoard.line'),
                    theme: 'gradient',
                    onClick: () => this._setTool('line')
                }),
                this.toolButtons.highlighter = new EditorButton({
                    type: EditorButton.TYPES.HIGHLIGHTER,
                    label: Locale.t('drawingBoard.highlighter'),
                    theme: 'gradient',
                    onClick: () => this._setTool('highlighter')
                })
            ],
            showSeparator: true
        });
        toolGroup.mount(toolbar);

        // 歷史群組
        const historyGroup = new ButtonGroup({
            theme: 'gradient',
            gap: '4px',
            buttons: [
                new EditorButton({
                    type: EditorButton.TYPES.UNDO,
                    theme: 'gradient',
                    iconOnly: true,
                    onClick: () => this.undo()
                }),
                new EditorButton({
                    type: EditorButton.TYPES.REDO,
                    theme: 'gradient',
                    iconOnly: true,
                    onClick: () => this.redo()
                })
            ],
            showSeparator: true
        });
        historyGroup.mount(toolbar);

        // 清除按鈕
        const clearGroup = new ButtonGroup({
            theme: 'gradient',
            gap: '4px',
            buttons: [
                new EditorButton({
                    type: EditorButton.TYPES.CLEAR,
                    label: Locale.t('drawingBoard.clear'),
                    theme: 'gradient',
                    onClick: () => this.clear()
                })
            ],
            showSeparator: true
        });
        clearGroup.mount(toolbar);

        // 匯出按鈕
        const exportBtn = new EditorButton({
            type: EditorButton.TYPES.EXPORT_PNG,
            label: Locale.t('drawingBoard.exportPng'),
            theme: 'gradient',
            variant: 'primary',
            onClick: () => this.exportPNG()
        });
        exportBtn.mount(toolbar);

        return toolbar;
    }

    _createSettingsPanel() {
        const panel = document.createElement('div');
        panel.className = 'drawing-board-settings';
        panel.style.cssText = `
            background: var(--cl-bg-tertiary);
            border: 1px solid var(--cl-border);
            border-top: none;
            border-radius: 0 0 12px 12px;
            padding: 12px 16px;
            display: flex;
            gap: 20px;
            flex-wrap: wrap;
            align-items: center;
        `;

        // 顏色選擇
        const colorGroup = document.createElement('div');
        colorGroup.style.cssText = 'display: flex; align-items: center; gap: 8px;';

        const colorLabel = document.createElement('span');
        colorLabel.textContent = Locale.t('drawingBoard.colorLabel');
        colorLabel.style.cssText = 'font-size: 14px; color: var(--cl-text-secondary);';
        colorGroup.appendChild(colorLabel);

        // 預設顏色快捷按鈕
        const colors = ['var(--cl-text)', '#ff0000', '#0066ff', '#00aa00', '#ff9900', '#9900ff'];
        colors.forEach(color => {
            const colorBtn = document.createElement('button');
            colorBtn.style.cssText = `
                width: 28px;
                height: 28px;
                border-radius: 50%;
                border: 2px solid ${color === this.settings.strokeColor ? 'var(--cl-text)' : 'transparent'};
                background: ${color};
                cursor: pointer;
                transition: transform 0.2s;
            `;
            colorBtn.onclick = () => {
                this.settings.strokeColor = color;
                this._updateColorButtons();
            };
            colorBtn.onmouseover = () => colorBtn.style.transform = 'scale(1.1)';
            colorBtn.onmouseout = () => colorBtn.style.transform = 'scale(1)';
            colorGroup.appendChild(colorBtn);
        });

        // 自訂顏色
        const customColor = document.createElement('input');
        customColor.type = 'color';
        customColor.value = this.settings.strokeColor;
        customColor.style.cssText = `
            width: 28px;
            height: 28px;
            border: none;
            border-radius: 4px;
            cursor: pointer;
        `;
        customColor.onchange = (e) => {
            this.settings.strokeColor = e.target.value;
            this._updateColorButtons();
        };
        colorGroup.appendChild(customColor);
        this.colorButtons = colorGroup.querySelectorAll('button');
        this.customColorInput = customColor;

        panel.appendChild(colorGroup);

        // 分隔線
        const sep = document.createElement('div');
        sep.style.cssText = 'width: 1px; height: 24px; background: var(--cl-border);';
        panel.appendChild(sep);

        // 粗細選擇
        const sizeGroup = document.createElement('div');
        sizeGroup.style.cssText = 'display: flex; align-items: center; gap: 8px;';

        const sizeLabel = document.createElement('span');
        sizeLabel.textContent = Locale.t('drawingBoard.thicknessLabel');
        sizeLabel.style.cssText = 'font-size: 14px; color: var(--cl-text-secondary);';
        sizeGroup.appendChild(sizeLabel);

        // 粗細滑桿
        const sizeSlider = document.createElement('input');
        sizeSlider.type = 'range';
        sizeSlider.min = '1';
        sizeSlider.max = '50';
        sizeSlider.value = this.settings.lineWidth;
        sizeSlider.style.cssText = 'width: 120px;';
        sizeSlider.oninput = (e) => {
            this.settings.lineWidth = parseInt(e.target.value);
            sizeValue.textContent = `${this.settings.lineWidth}px`;
            this._updateCursor();
        };
        sizeGroup.appendChild(sizeSlider);

        const sizeValue = document.createElement('span');
        sizeValue.textContent = `${this.settings.lineWidth}px`;
        sizeValue.style.cssText = 'font-size: 14px; color: var(--cl-text); min-width: 40px;';
        sizeGroup.appendChild(sizeValue);

        this.sizeSlider = sizeSlider;
        this.sizeValue = sizeValue;

        panel.appendChild(sizeGroup);

        // 分隔線
        const sep2 = document.createElement('div');
        sep2.style.cssText = 'width: 1px; height: 24px; background: var(--cl-border);';
        panel.appendChild(sep2);

        // 透明度選擇
        const opacityGroup = document.createElement('div');
        opacityGroup.style.cssText = 'display: flex; align-items: center; gap: 8px;';

        const opacityLabel = document.createElement('span');
        opacityLabel.textContent = Locale.t('drawingBoard.opacityLabel');
        opacityLabel.style.cssText = 'font-size: 14px; color: var(--cl-text-secondary);';
        opacityGroup.appendChild(opacityLabel);

        const opacitySlider = document.createElement('input');
        opacitySlider.type = 'range';
        opacitySlider.min = '10';
        opacitySlider.max = '100';
        opacitySlider.value = this.settings.opacity * 100;
        opacitySlider.style.cssText = 'width: 80px;';
        opacitySlider.oninput = (e) => {
            this.settings.opacity = parseInt(e.target.value) / 100;
            opacityValue.textContent = `${e.target.value}%`;
        };
        opacityGroup.appendChild(opacitySlider);

        const opacityValue = document.createElement('span');
        opacityValue.textContent = `${Math.round(this.settings.opacity * 100)}%`;
        opacityValue.style.cssText = 'font-size: 14px; color: var(--cl-text); min-width: 40px;';
        opacityGroup.appendChild(opacityValue);

        this.opacitySlider = opacitySlider;
        this.opacityValue = opacityValue;

        panel.appendChild(opacityGroup);

        return panel;
    }

    _setTool(tool) {
        this.settings.tool = tool;

        // 更新 EditorButton 啟用狀態
        if (this.toolButtons) {
            Object.keys(this.toolButtons).forEach(key => {
                if (this.toolButtons[key]) {
                    this.toolButtons[key].active = (key === tool);
                }
            });
        }

        // 螢光筆自動設定透明度
        if (tool === 'highlighter') {
            this.settings.opacity = 0.4;
            if (this.opacitySlider) this.opacitySlider.value = 40;
            if (this.opacityValue) this.opacityValue.textContent = '40%';
        } else if (tool !== 'eraser') {
            this.settings.opacity = 1.0;
            if (this.opacitySlider) this.opacitySlider.value = 100;
            if (this.opacityValue) this.opacityValue.textContent = '100%';
        }

        this._updateCursor();
    }

    _updateCursor() {
        const size = Math.max(this.settings.lineWidth, 8);

        if (this.settings.tool === 'eraser') {
            // 橡皮擦游標
            this.canvas.style.cursor = `url('data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}"><circle cx="${size/2}" cy="${size/2}" r="${size/2-1}" fill="white" stroke="black" stroke-width="1"/></svg>') ${size/2} ${size/2}, crosshair`;
        } else if (this.settings.tool === 'line') {
            // 直線游標
            this.canvas.style.cursor = 'crosshair';
        } else if (this.settings.tool === 'highlighter') {
            // 螢光筆游標
            const color = this.settings.strokeColor.replace('#', '%23');
            this.canvas.style.cursor = `url('data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}"><rect x="1" y="1" width="${size-2}" height="${size-2}" fill="${color}" fill-opacity="0.4" stroke="${color}" stroke-width="1"/></svg>') ${size/2} ${size/2}, crosshair`;
        } else {
            // 畫筆游標
            this.canvas.style.cursor = 'crosshair';
        }
    }

    _updateColorButtons() {
        const colors = ['var(--cl-text)', '#ff0000', '#0066ff', '#00aa00', '#ff9900', '#9900ff'];
        this.colorButtons.forEach((btn, i) => {
            btn.style.borderColor = colors[i] === this.settings.strokeColor ? 'var(--cl-text)' : 'transparent';
        });
        this.customColorInput.value = this.settings.strokeColor;
    }

    _setupEventListeners() {
        // 滑鼠事件
        this.canvas.addEventListener('mousedown', (e) => this._startDrawing(e));
        this.canvas.addEventListener('mousemove', (e) => this._draw(e));
        this.canvas.addEventListener('mouseup', () => this._stopDrawing());
        this.canvas.addEventListener('mouseleave', () => this._stopDrawing());

        // 觸控事件
        this.canvas.addEventListener('touchstart', (e) => {
            e.preventDefault();
            this._startDrawing(e.touches[0]);
        });
        this.canvas.addEventListener('touchmove', (e) => {
            e.preventDefault();
            this._draw(e.touches[0]);
        });
        this.canvas.addEventListener('touchend', () => this._stopDrawing());

        // 鍵盤快捷鍵
        document.addEventListener('keydown', (e) => {
            if (e.ctrlKey && e.key === 'z') {
                e.preventDefault();
                this.undo();
            } else if (e.ctrlKey && e.key === 'y') {
                e.preventDefault();
                this.redo();
            }
        });
    }

    _getCanvasPoint(e) {
        const rect = this.canvas.getBoundingClientRect();
        return {
            x: e.clientX - rect.left,
            y: e.clientY - rect.top
        };
    }

    _startDrawing(e) {
        this.isDrawing = true;
        const point = this._getCanvasPoint(e);
        this.lastPoint = point;
        this.startPoint = point; // 用於直線工具

        // 儲存當前畫布狀態（用於直線預覽）
        if (this.settings.tool === 'line') {
            this.previewImageData = this.ctx.getImageData(0, 0, this.width, this.height);
        }

        // 開始新路徑
        this.currentPath = {
            tool: this.settings.tool,
            color: this.settings.strokeColor,
            lineWidth: this.settings.lineWidth,
            opacity: this.settings.opacity,
            points: [point]
        };
    }

    _draw(e) {
        if (!this.isDrawing) return;

        const point = this._getCanvasPoint(e);

        // 直線工具特殊處理
        if (this.settings.tool === 'line') {
            // 還原到繪製前的狀態
            this.ctx.putImageData(this.previewImageData, 0, 0);

            // 繪製預覽直線
            this.ctx.beginPath();
            this.ctx.moveTo(this.startPoint.x, this.startPoint.y);
            this.ctx.lineTo(point.x, point.y);
            this.ctx.globalCompositeOperation = 'source-over';
            this.ctx.globalAlpha = this.settings.opacity;
            this.ctx.strokeStyle = this.settings.strokeColor;
            this.ctx.lineWidth = this.settings.lineWidth;
            this.ctx.stroke();
            this.ctx.globalAlpha = 1.0;

            // 更新終點
            this.currentPath.points = [this.startPoint, point];
            return;
        }

        // 設定繪製樣式
        if (this.settings.tool === 'eraser') {
            this.ctx.globalCompositeOperation = 'destination-out';
            this.ctx.strokeStyle = 'rgba(0,0,0,1)';
            this.ctx.globalAlpha = 1.0;
        } else if (this.settings.tool === 'highlighter') {
            this.ctx.globalCompositeOperation = 'multiply';
            this.ctx.strokeStyle = this.settings.strokeColor;
            this.ctx.globalAlpha = this.settings.opacity;
        } else {
            this.ctx.globalCompositeOperation = 'source-over';
            this.ctx.strokeStyle = this.settings.strokeColor;
            this.ctx.globalAlpha = this.settings.opacity;
        }
        this.ctx.lineWidth = this.settings.lineWidth;

        // 一般繪製 - 直接從上一點畫到當前點，確保連續
        this.ctx.beginPath();
        this.ctx.moveTo(this.lastPoint.x, this.lastPoint.y);
        this.ctx.lineTo(point.x, point.y);
        this.ctx.stroke();

        this.ctx.globalAlpha = 1.0;

        // 記錄點位
        this.currentPath.points.push(point);
        this.lastPoint = point;

        // 觸發回調
        if (this.onDraw) {
            this.onDraw(point, this.settings);
        }
    }

    _stopDrawing() {
        if (!this.isDrawing) return;

        this.isDrawing = false;

        // 直線工具清理
        if (this.settings.tool === 'line') {
            this.previewImageData = null;
        }

        if (this.currentPath && this.currentPath.points.length > 1) {
            this.paths.push(this.currentPath);
            this._saveHistory();
        }

        this.currentPath = null;
        this.lastPoint = null;
        this.startPoint = null;
    }

    _saveHistory() {
        // 移除之後的歷史記錄
        this.history = this.history.slice(0, this.historyIndex + 1);

        // 儲存當前狀態
        this.history.push({
            paths: JSON.parse(JSON.stringify(this.paths)),
            imageData: this.canvas.toDataURL()
        });

        this.historyIndex = this.history.length - 1;

        // 限制歷史記錄數量
        if (this.history.length > this.maxHistory) {
            this.history.shift();
            this.historyIndex--;
        }
    }

    _restoreFromHistory(index) {
        if (index < 0 || index >= this.history.length) return;

        const state = this.history[index];
        this.paths = JSON.parse(JSON.stringify(state.paths));

        // 還原畫布
        const img = new Image();
        img.onload = () => {
            this.ctx.clearRect(0, 0, this.width, this.height);
            if (this.backgroundImage) {
                this.ctx.drawImage(this.backgroundImage, 0, 0, this.width, this.height);
            }
            this.ctx.drawImage(img, 0, 0);
        };
        img.src = state.imageData;

        this.historyIndex = index;
    }

    // 公開方法
    undo() {
        if (this.historyIndex > 0) {
            this._restoreFromHistory(this.historyIndex - 1);
        }
    }

    redo() {
        if (this.historyIndex < this.history.length - 1) {
            this._restoreFromHistory(this.historyIndex + 1);
        }
    }

    clear() {
        this.ctx.clearRect(0, 0, this.width, this.height);

        // 重繪背景
        if (this.backgroundImage) {
            this.ctx.drawImage(this.backgroundImage, 0, 0, this.width, this.height);
        } else {
            this.ctx.fillStyle = 'white';
            this.ctx.fillRect(0, 0, this.width, this.height);
        }

        this.paths = [];
        this._saveHistory();

        if (this.onClear) {
            this.onClear();
        }
    }

    setBackgroundImage(src) {
        return new Promise((resolve, reject) => {
            const img = new Image();
            img.onload = () => {
                this.backgroundImage = img;
                this._redraw();
                resolve(img);
            };
            img.onerror = reject;

            if (src instanceof Blob || src instanceof File) {
                img.src = URL.createObjectURL(src);
            } else {
                img.src = src;
            }
        });
    }

    _redraw() {
        this.ctx.clearRect(0, 0, this.width, this.height);

        // 繪製背景
        if (this.backgroundImage) {
            this.ctx.drawImage(this.backgroundImage, 0, 0, this.width, this.height);
        } else {
            this.ctx.fillStyle = 'white';
            this.ctx.fillRect(0, 0, this.width, this.height);
        }

        // 重繪所有路徑
        this.paths.forEach(path => {
            if (path.points.length < 2) return;

            // 設定樣式
            if (path.tool === 'eraser') {
                this.ctx.globalCompositeOperation = 'destination-out';
                this.ctx.strokeStyle = 'rgba(0,0,0,1)';
                this.ctx.globalAlpha = 1.0;
            } else if (path.tool === 'highlighter') {
                this.ctx.globalCompositeOperation = 'multiply';
                this.ctx.strokeStyle = path.color;
                this.ctx.globalAlpha = path.opacity || 0.4;
            } else {
                this.ctx.globalCompositeOperation = 'source-over';
                this.ctx.strokeStyle = path.color;
                this.ctx.globalAlpha = path.opacity || 1.0;
            }
            this.ctx.lineWidth = path.lineWidth;

            this.ctx.beginPath();

            // 直線工具：只有兩點
            if (path.tool === 'line') {
                this.ctx.moveTo(path.points[0].x, path.points[0].y);
                this.ctx.lineTo(path.points[1].x, path.points[1].y);
            } else {
                // 手繪路徑：連接所有點
                this.ctx.moveTo(path.points[0].x, path.points[0].y);
                for (let i = 1; i < path.points.length; i++) {
                    this.ctx.lineTo(path.points[i].x, path.points[i].y);
                }
            }

            this.ctx.stroke();
        });

        this.ctx.globalCompositeOperation = 'source-over';
        this.ctx.globalAlpha = 1.0;
    }

    exportPNG(filename = 'drawing.png') {
        const link = document.createElement('a');
        link.download = filename;
        link.href = this.canvas.toDataURL('image/png');
        link.click();
    }

    getDataURL() {
        return this.canvas.toDataURL('image/png');
    }

    // 取得/設定畫布尺寸
    resize(width, height) {
        // 儲存當前內容
        const imageData = this.ctx.getImageData(0, 0, this.width, this.height);

        this.width = width;
        this.height = height;
        this.canvas.width = width;
        this.canvas.height = height;

        // 還原內容
        this.ctx.putImageData(imageData, 0, 0);
        this.ctx.lineCap = 'round';
        this.ctx.lineJoin = 'round';
    }

    destroy() {
        if (this.element && this.element.parentNode) {
            this.element.parentNode.removeChild(this.element);
        }
    }
}

export default DrawingBoard;
