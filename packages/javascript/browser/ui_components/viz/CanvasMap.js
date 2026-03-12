export class CanvasMap {
    constructor(options) {
        this.container = typeof options.container === 'string'
            ? document.querySelector(options.container)
            : options.container;

        this.options = {
            width: 800,
            height: 1000,
            images: [], // { id, src, label }
            onHover: null,
            onClick: null,
            ...options
        };

        this.canvas = document.createElement('canvas');
        this.canvas.width = this.options.width;
        this.canvas.height = this.options.height;
        this.canvas.style.maxWidth = '100%';
        this.canvas.style.height = 'auto';
        this.ctx = this.canvas.getContext('2d');

        // Offscreen buffers for hit testing
        this.layers = [];
        this.hoveredLayer = null;
        this.isLoading = true;

        this.container.appendChild(this.canvas);
        this._init();
    }

    async _init() {
        // Load all images
        const promises = this.options.images.map(imgData => {
            return new Promise((resolve, reject) => {
                const img = new Image();
                img.crossOrigin = "Anonymous";
                img.src = imgData.src;
                img.onload = () => {
                    // Create offscreen canvas for this layer
                    const canvas = document.createElement('canvas');
                    canvas.width = this.options.width;
                    canvas.height = this.options.height;
                    const ctx = canvas.getContext('2d');
                    ctx.drawImage(img, 0, 0, this.options.width, this.options.height);

                    resolve({
                        ...imgData,
                        img,
                        canvas,
                        ctx,
                        data: null // Lazy load pixel data if needed, or get single pixel on demand
                    });
                };
                img.onerror = reject;
            });
        });

        try {
            this.layers = await Promise.all(promises);
            this.isLoading = false;
            this._render();
            this._bindEvents();
        } catch (e) {
            console.error('Failed to load map images', e);
        }
    }

    _getThemeColor(tokenName, fallback = 'currentColor') {
        const root = this.container || document.documentElement;
        const value = getComputedStyle(root).getPropertyValue(tokenName).trim();
        return value || fallback;
    }

    _render() {
        // Clear main canvas
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);

        // Draw all layers
        this.layers.forEach(layer => {
            this.ctx.drawImage(layer.img, 0, 0, this.options.width, this.options.height);
        });

        // Draw highlight overlay separately
        if (this.hoveredLayer) {
            this.ctx.save();
            this.ctx.globalCompositeOperation = 'source-over';
            this.ctx.drawImage(this.hoveredLayer.img, 0, 0, this.options.width, this.options.height);
            this.ctx.globalCompositeOperation = 'source-in';
            this.ctx.globalAlpha = 0.5;
            this.ctx.fillStyle = this._getThemeColor('--cl-warning');
            this.ctx.fillRect(0, 0, this.options.width, this.options.height);
            this.ctx.restore();
        }
    }

    _bindEvents() {
        this.canvas.addEventListener('mousemove', (e) => {
            if (this.isLoading) return;

            const rect = this.canvas.getBoundingClientRect();
            // Correct scale if canvas is resized via CSS
            const scaleX = this.canvas.width / rect.width;
            const scaleY = this.canvas.height / rect.height;

            const x = Math.floor((e.clientX - rect.left) * scaleX);
            const y = Math.floor((e.clientY - rect.top) * scaleY);

            // Check hit
            let hitLayer = null;
            // Iterate reverse (top to bottom) if z-index matters, 
            // but here regions shouldn't overlap much. 
            // However, bounding boxes overlap.

            for (const layer of this.layers) {
                // Get pixel alpha
                const pixel = layer.ctx.getImageData(x, y, 1, 1).data;
                if (pixel[3] > 10) { // Non-transparent
                    hitLayer = layer;
                    break;
                }
            }

            if (this.hoveredLayer !== hitLayer) {
                this.hoveredLayer = hitLayer;
                // Optimize: partial redraw? No, full redraw for simplicity first.
                // Actually full redraw is expensive with 20+ large images.
                // Optimization: Draw Base Map once to a buffer. 
                // But let's verify functionality first.
                this._renderBaseAndHighlight();

                if (this.options.onHover) {
                    this.options.onHover(hitLayer ? hitLayer.id : null, e);
                }

                this.canvas.style.cursor = hitLayer ? 'pointer' : 'default';
            }
        });

        this.canvas.addEventListener('click', (e) => {
            if (this.hoveredLayer && this.options.onClick) {
                this.options.onClick(this.hoveredLayer.id, this.hoveredLayer);
            }
        });
    }

    _renderBaseAndHighlight() {
        // Optimization: Cache the base composite
        if (!this.baseCanvas) {
            this.baseCanvas = document.createElement('canvas');
            this.baseCanvas.width = this.options.width;
            this.baseCanvas.height = this.options.height;
            const ctx = this.baseCanvas.getContext('2d');
            this.layers.forEach(layer => {
                ctx.drawImage(layer.img, 0, 0, this.options.width, this.options.height);
            });
        }

        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        this.ctx.drawImage(this.baseCanvas, 0, 0);

        if (this.hoveredLayer) {
            // Draw Highlight
            // Standard approach to tint non-transparent pixels of an image:
            // 1. Draw image
            // 2. Composite color with source-in

            // Create a temp canvas for the highlight to avoid messing up main stack
            if (!this.tempCanvas) {
                this.tempCanvas = document.createElement('canvas');
                this.tempCanvas.width = this.options.width;
                this.tempCanvas.height = this.options.height;
            }
            const tCtx = this.tempCanvas.getContext('2d');
            tCtx.clearRect(0, 0, this.options.width, this.options.height);

            tCtx.drawImage(this.hoveredLayer.img, 0, 0, this.options.width, this.options.height);
            tCtx.globalCompositeOperation = 'source-in';
            tCtx.globalAlpha = 0.3;
            tCtx.fillStyle = this._getThemeColor('--cl-danger');
            tCtx.fillRect(0, 0, this.options.width, this.options.height);
            tCtx.globalAlpha = 1;
            tCtx.globalCompositeOperation = 'source-over';

            this.ctx.drawImage(this.tempCanvas, 0, 0);
        }
    }
}
