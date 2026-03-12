import { ModalPanel } from '../layout/Panel/index.js';

/**
 * MapEditor - Interactive Map/Image Editor with Annotations
 * Features: Upload images, add text, shapes, export to PNG
 * Zero dependencies, pure Canvas implementation
 */

export class MapEditor {
    constructor(options = {}) {
        this.container = typeof options.container === 'string'
            ? document.querySelector(options.container)
            : options.container;

        this.width = options.width || 800;
        this.height = options.height || 600;
        
        // Current tool: 'select', 'text', 'rect', 'circle', 'line', 'arrow'
        this.currentTool = 'select';
        
        // Elements on canvas (text, shapes)
        this.elements = [];
        this.selectedElement = null;
        this.hoveredElement = null;
        
        // Drawing state
        this.isDrawing = false;
        this.drawStart = null;
        this.tempShape = null;
        
        // Pan/Zoom state
        this.offsetX = 0;
        this.offsetY = 0;
        this.scale = 1;
        
        // History for undo/redo
        this.history = [];
        this.historyIndex = -1;
        
        // Background image
        this.backgroundImage = null;
        
        // Style settings
        this.settings = {
            fontSize: 16,
            fontFamily: 'Arial',
            textColor: 'var(--cl-text)',
            strokeColor: 'var(--cl-canvas-red)',
            fillColor: 'color-mix(in srgb, var(--cl-canvas-red) 20%, transparent)',
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
    }

    _createUI() {
        const wrapper = document.createElement('div');
        wrapper.className = 'map-editor';
        wrapper.style.cssText = `
            width: 100%;
            max-width: ${this.width}px;
            margin: 0 auto;
            font-family: var(--cl-font-family);
        `;

        // Toolbar
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

        // File upload
        const uploadBtn = this._createButton('📁 Upload Image', () => {
            this.fileInput.click();
        });
        
        const fileInput = document.createElement('input');
        fileInput.type = 'file';
        fileInput.accept = 'image/*';
        fileInput.style.display = 'none';
        fileInput.onchange = (e) => this._handleImageUpload(e);
        this.fileInput = fileInput;

        // Tool buttons
        const selectBtn = this._createToolButton('↖️ Select', 'select');
        const textBtn = this._createToolButton('📝 Text', 'text');
        const rectBtn = this._createToolButton('⬜ Rect', 'rect');
        const circleBtn = this._createToolButton('⭕ Circle', 'circle');
        const lineBtn = this._createToolButton('➖ Line', 'line');
        const arrowBtn = this._createToolButton('➡️ Arrow', 'arrow');

        // Separator
        const sep1 = this._createSeparator();

        // Delete button
        const deleteBtn = this._createButton('🗑️ Delete', () => {
            if (this.selectedElement) {
                this._deleteElement(this.selectedElement);
            }
        });

        // Clear all
        const clearBtn = this._createButton('🧹 Clear All', () => {
            if (confirm('Clear all annotations?')) {
                this.elements = [];
                this._saveHistory();
                this._render();
            }
        });

        // Separator
        const sep2 = this._createSeparator();

        // Export
        const exportBtn = this._createButton('💾 Export PNG', () => {
            this._exportImage();
        });

        const saveBtn = this._createButton('📄 Save JSON', () => {
            this._exportJSON();
        });

        toolbar.appendChild(uploadBtn);
        toolbar.appendChild(fileInput);
        toolbar.appendChild(sep1.cloneNode());
        toolbar.appendChild(selectBtn);
        toolbar.appendChild(textBtn);
        toolbar.appendChild(rectBtn);
        toolbar.appendChild(circleBtn);
        toolbar.appendChild(lineBtn);
        toolbar.appendChild(arrowBtn);
        toolbar.appendChild(sep2.cloneNode());
        toolbar.appendChild(deleteBtn);
        toolbar.appendChild(clearBtn);
        toolbar.appendChild(sep2.cloneNode());
        toolbar.appendChild(exportBtn);
        toolbar.appendChild(saveBtn);

        this.toolButtons = {
            select: selectBtn,
            text: textBtn,
            rect: rectBtn,
            circle: circleBtn,
            line: lineBtn,
            arrow: arrowBtn
        };

        // Settings panel
        const settings = this._createSettingsPanel();

        // Canvas container
        const canvasContainer = document.createElement('div');
        canvasContainer.style.cssText = `
            position: relative;
            background: var(--cl-bg);
            border: 1px solid var(--cl-border);
            border-top: none;
            border-radius: 0 0 var(--cl-radius-lg) var(--cl-radius-lg);
            overflow: hidden;
            cursor: crosshair;
        `;

        const canvas = document.createElement('canvas');
        canvas.width = this.width;
        canvas.height = this.height;
        canvas.style.cssText = `
            display: block;
            max-width: 100%;
            height: auto;
        `;
        this.canvas = canvas;
        this.ctx = canvas.getContext('2d');

        canvasContainer.appendChild(canvas);

        wrapper.appendChild(toolbar);
        wrapper.appendChild(settings);
        wrapper.appendChild(canvasContainer);

        return wrapper;
    }

    _createButton(text, onClick) {
        const btn = document.createElement('button');
        btn.textContent = text;
        btn.style.cssText = `
            padding: 8px 12px;
            border: 1px solid var(--cl-border-dark);
            border-radius: var(--cl-radius-sm);
            background: var(--cl-bg);
            cursor: pointer;
            font-size: var(--cl-font-size-lg);
            transition: all var(--cl-transition);
        `;
        btn.onmouseover = () => {
            btn.style.background = 'var(--cl-bg-subtle)';
            btn.style.transform = 'translateY(-1px)';
        };
        btn.onmouseout = () => {
            btn.style.background = 'var(--cl-bg)';
            btn.style.transform = 'translateY(0)';
        };
        btn.onclick = onClick;
        return btn;
    }

    _createToolButton(text, tool) {
        const btn = this._createButton(text, () => {
            this.currentTool = tool;
            this._updateToolButtons();
        });
        return btn;
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

        // Font size
        panel.appendChild(this._createLabel('Font:'));
        const fontSize = this._createNumberInput(this.settings.fontSize, 10, 72, (val) => {
            this.settings.fontSize = val;
        });
        panel.appendChild(fontSize);

        // Text color
        panel.appendChild(this._createLabel('Text Color:'));
        const textColor = this._createColorInput(this.settings.textColor, (val) => {
            this.settings.textColor = val;
        });
        panel.appendChild(textColor);

        // Stroke color
        panel.appendChild(this._createLabel('Stroke:'));
        const strokeColor = this._createColorInput(this.settings.strokeColor, (val) => {
            this.settings.strokeColor = val;
        });
        panel.appendChild(strokeColor);

        // Fill color
        panel.appendChild(this._createLabel('Fill:'));
        const fillColor = this._createColorInput(this.settings.fillColor, (val) => {
            this.settings.fillColor = val;
        });
        panel.appendChild(fillColor);

        // Line width
        panel.appendChild(this._createLabel('Width:'));
        const lineWidth = this._createNumberInput(this.settings.lineWidth, 1, 20, (val) => {
            this.settings.lineWidth = val;
        });
        panel.appendChild(lineWidth);

        return panel;
    }

    _createLabel(text) {
        const label = document.createElement('span');
        label.textContent = text;
        label.style.color = 'var(--cl-text-secondary)';
        return label;
    }

    _createNumberInput(value, min, max, onChange) {
        const input = document.createElement('input');
        input.type = 'number';
        input.value = value;
        input.min = min;
        input.max = max;
        input.style.cssText = `
            width: 60px;
            padding: 4px 8px;
            border: 1px solid var(--cl-border-dark);
            border-radius: var(--cl-radius-sm);
        `;
        input.oninput = (e) => onChange(Number.parseInt(e.target.value, 10) || value);
        return input;
    }

    _createColorInput(value, onChange) {
        const input = document.createElement('input');
        input.type = 'color';
        input.value = this._resolveColorInputValue(value);
        input.style.cssText = `
            width: 40px;
            height: 28px;
            border: 1px solid var(--cl-border-dark);
            border-radius: var(--cl-radius-sm);
            cursor: pointer;
        `;
        input.oninput = (e) => onChange(e.target.value);
        return input;
    }

    _resolveColorInputValue(value) {
        if (typeof value !== 'string' || !value.trim()) {
            return ['#', '00', '00', '00'].join('');
        }

        const normalizedValue = value.trim();
        if (/^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test(normalizedValue)) {
            return normalizedValue.length === 4
                ? `#${normalizedValue.slice(1).split('').map((char) => char + char).join('')}`
                : normalizedValue;
        }

        if (typeof document === 'undefined') {
            return ['#', '00', '00', '00'].join('');
        }

        const probe = document.createElement('span');
        probe.style.color = normalizedValue;
        probe.style.position = 'absolute';
        probe.style.opacity = '0';
        probe.style.pointerEvents = 'none';

        const mountTarget = document.body || document.documentElement;
        if (!mountTarget) {
            return ['#', '00', '00', '00'].join('');
        }

        mountTarget.appendChild(probe);
        const resolvedColor = getComputedStyle(probe).color;
        probe.remove();

        const match = resolvedColor.match(/^rgba?\((\d+),\s*(\d+),\s*(\d+)/i);
        if (!match) {
            return ['#', '00', '00', '00'].join('');
        }

        return `#${[match[1], match[2], match[3]]
            .map((channel) => Number.parseInt(channel, 10).toString(16).padStart(2, '0'))
            .join('')}`;
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
                btn.style.color = 'var(--cl-text-dark)';
                btn.style.borderColor = 'var(--cl-border-dark)';
            }
        });
    }

    _setupCanvas() {
        // High DPI support
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
        
        // Keyboard shortcuts
        document.addEventListener('keydown', (e) => {
            if (e.key === 'Delete' && this.selectedElement) {
                this._deleteElement(this.selectedElement);
            }
            if (e.ctrlKey && e.key === 'z') {
                e.preventDefault();
                this._undo();
            }
        });
    }

    _getMousePos(e) {
        const rect = this.canvas.getBoundingClientRect();
        return {
            x: (e.clientX - rect.left) * (this.canvas.width / rect.width) / (window.devicePixelRatio || 1),
            y: (e.clientY - rect.top) * (this.canvas.height / rect.height) / (window.devicePixelRatio || 1)
        };
    }

    _handleImageUpload(e) {
        const file = e.target.files[0];
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
            // Try to select element
            this.selectedElement = this._getElementAtPos(pos.x, pos.y);
            if (this.selectedElement) {
                this.dragStart = { x: pos.x, y: pos.y };
                this.dragOffset = {
                    x: pos.x - this.selectedElement.x,
                    y: pos.y - this.selectedElement.y
                };
            }
        } else if (this.currentTool === 'text') {
            // Add text element
            const text = prompt('Enter text:');
            if (text) {
                this.elements.push({
                    type: 'text',
                    x: pos.x,
                    y: pos.y,
                    text: text,
                    fontSize: this.settings.fontSize,
                    fontFamily: this.settings.fontFamily,
                    color: this.settings.textColor
                });
                this._saveHistory();
            }
        } else {
            // Start drawing shape
            this.isDrawing = true;
            this.drawStart = pos;
        }
        
        this._render();
    }

    _handleMouseMove(e) {
        const pos = this._getMousePos(e);
        
        if (this.currentTool === 'select' && this.selectedElement && this.dragStart) {
            // Drag element
            this.selectedElement.x = pos.x - this.dragOffset.x;
            this.selectedElement.y = pos.y - this.dragOffset.y;
            this._render();
        } else if (this.isDrawing && this.drawStart) {
            // Draw temporary shape
            this.tempShape = {
                type: this.currentTool,
                x: this.drawStart.x,
                y: this.drawStart.y,
                x2: pos.x,
                y2: pos.y,
                strokeColor: this.settings.strokeColor,
                fillColor: this.settings.fillColor,
                lineWidth: this.settings.lineWidth
            };
            this._render();
        } else {
            // Hover detection
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
        
        if (element && element.type === 'text') {
            const newText = prompt('Edit text:', element.text);
            if (newText !== null) {
                element.text = newText;
                this._saveHistory();
                this._render();
            }
        }
    }

    _getElementAtPos(x, y) {
        // Check in reverse order (top to bottom)
        for (let i = this.elements.length - 1; i >= 0; i--) {
            const el = this.elements[i];
            if (this._isPointInElement(x, y, el)) {
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
        }
        return false;
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
        
        // Draw background image
        if (this.backgroundImage) {
            const scale = Math.min(
                this.canvas.width / this.backgroundImage.width,
                this.canvas.height / this.backgroundImage.height
            );
            const w = this.backgroundImage.width * scale;
            const h = this.backgroundImage.height * scale;
            const x = (this.canvas.width / (window.devicePixelRatio || 1) - w) / 2;
            const y = (this.canvas.height / (window.devicePixelRatio || 1) - h) / 2;
            ctx.drawImage(this.backgroundImage, x, y, w, h);
        }
        
        // Draw all elements
        this.elements.forEach(el => this._drawElement(el));
        
        // Draw temporary shape
        if (this.tempShape) {
            this._drawElement(this.tempShape, true);
        }
        
        // Draw selection
        if (this.selectedElement) {
            this._drawSelection(this.selectedElement);
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
        }
        
        ctx.setLineDash([]);
    }

    _saveHistory() {
        this.history = this.history.slice(0, this.historyIndex + 1);
        this.history.push(JSON.parse(JSON.stringify(this.elements)));
        this.historyIndex = this.history.length - 1;
    }

    _undo() {
        if (this.historyIndex > 0) {
            this.historyIndex--;
            this.elements = JSON.parse(JSON.stringify(this.history[this.historyIndex]));
            this._render();
        }
    }

    _exportImage() {
        // Get PNG data from canvas
        const dataUrl = this.canvas.toDataURL('image/png');
        
        // Embed metadata into PNG
        this._exportImageWithMetadata(dataUrl);
    }

    _exportImageWithMetadata(dataUrl) {
        // Prepare metadata
        const metadata = {
            elements: this.elements,
            settings: this.settings,
            timestamp: new Date().toISOString(),
            version: '1.0',
            canvasSize: {
                width: this.width,
                height: this.height
            }
        };

        // Convert data URL to binary
        const base64 = dataUrl.split(',')[1];
        const binary = atob(base64);
        const array = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            array[i] = binary.charCodeAt(i);
        }

        // Add tEXt chunk with metadata
        const pngWithMetadata = this._addPNGTextChunk(array, 'MapEditor', JSON.stringify(metadata));

        // Create download link
        const blob = new Blob([pngWithMetadata], { type: 'image/png' });
        const link = document.createElement('a');
        link.download = 'map-edited-' + Date.now() + '.png';
        link.href = URL.createObjectURL(blob);
        link.click();
        URL.revokeObjectURL(link.href);

        console.log('✅ PNG exported with embedded metadata');
        console.log('📊 Metadata:', metadata);
    }

    _addPNGTextChunk(pngData, keyword, text) {
        // PNG structure: Signature + Chunks + IEND
        // We'll insert our tEXt chunk before IEND
        
        // Find IEND chunk (last 12 bytes)
        const iendPosition = pngData.length - 12;
        
        // Create tEXt chunk
        const keywordBytes = new TextEncoder().encode(keyword);
        const textBytes = new TextEncoder().encode(text);
        const chunkData = new Uint8Array(keywordBytes.length + 1 + textBytes.length);
        
        chunkData.set(keywordBytes, 0);
        chunkData[keywordBytes.length] = 0; // Null separator
        chunkData.set(textBytes, keywordBytes.length + 1);
        
        const chunkType = new TextEncoder().encode('tEXt');
        const chunkLength = chunkData.length;
        
        // Create chunk with length, type, data, and CRC
        const chunk = new Uint8Array(12 + chunkLength);
        const view = new DataView(chunk.buffer);
        
        // Length (4 bytes, big-endian)
        view.setUint32(0, chunkLength, false);
        
        // Type (4 bytes)
        chunk.set(chunkType, 4);
        
        // Data
        chunk.set(chunkData, 8);
        
        // CRC (4 bytes)
        const crcData = new Uint8Array(4 + chunkLength);
        crcData.set(chunkType, 0);
        crcData.set(chunkData, 4);
        const crc = this._calculateCRC(crcData);
        view.setUint32(8 + chunkLength, crc, false);
        
        // Combine: original PNG (without IEND) + our chunk + IEND
        const result = new Uint8Array(pngData.length + chunk.length);
        result.set(pngData.subarray(0, iendPosition), 0);
        result.set(chunk, iendPosition);
        result.set(pngData.subarray(iendPosition), iendPosition + chunk.length);
        
        return result;
    }

    _calculateCRC(data) {
        // CRC-32 calculation for PNG
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

    async loadImageWithMetadata(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            
            reader.onload = async (event) => {
                const arrayBuffer = event.target.result;
                const uint8Array = new Uint8Array(arrayBuffer);
                
                // Load image
                const blob = new Blob([uint8Array], { type: 'image/png' });
                const imageUrl = URL.createObjectURL(blob);
                
                const img = new Image();
                img.onload = () => {
                    this.backgroundImage = img;
                    URL.revokeObjectURL(imageUrl);
                    
                    // Extract metadata
                    const metadata = this._extractPNGMetadata(uint8Array);
                    if (metadata) {
                        console.log('✅ Loaded PNG with embedded metadata');
                        console.log('📊 Metadata:', metadata);
                        
                        // Restore elements and settings
                        if (metadata.elements) {
                            this.elements = metadata.elements;
                        }
                        if (metadata.settings) {
                            Object.assign(this.settings, metadata.settings);
                        }
                        this._saveHistory();
                    } else {
                        console.log('ℹ️ No metadata found in PNG');
                    }
                    
                    this._render();
                    resolve(metadata);
                };
                
                img.onerror = () => {
                    reject(new Error('Failed to load image'));
                };
                
                img.src = imageUrl;
            };
            
            reader.onerror = () => {
                reject(new Error('Failed to read file'));
            };
            
            reader.readAsArrayBuffer(file);
        });
    }

    _extractPNGMetadata(pngData) {
        // PNG signature: 8 bytes
        let offset = 8;
        
        while (offset < pngData.length - 12) {
            const view = new DataView(pngData.buffer, pngData.byteOffset + offset);
            const chunkLength = view.getUint32(0, false);
            const chunkType = String.fromCharCode(...pngData.subarray(offset + 4, offset + 8));
            
            if (chunkType === 'IEND') break;
            
            if (chunkType === 'tEXt') {
                // Read chunk data
                const chunkData = pngData.subarray(offset + 8, offset + 8 + chunkLength);
                
                // Find null separator
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
                            console.error('Failed to parse metadata:', e);
                        }
                    }
                }
            }
            
            // Move to next chunk (length + type + data + CRC)
            offset += 12 + chunkLength;
        }
        
        return null;
    }

    _exportJSON() {
        const data = {
            elements: this.elements,
            settings: this.settings,
            timestamp: new Date().toISOString()
        };
        const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
        const link = document.createElement('a');
        link.download = 'map-config-' + Date.now() + '.json';
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
            console.error('Failed to load JSON:', error);
            ModalPanel.alert({ message: "Failed to load configuration" });
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
}
