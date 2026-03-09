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

    _render() {
        // Clear main canvas
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);

        // Draw all layers
        this.layers.forEach(layer => {
            // Draw base image
            this.ctx.drawImage(layer.img, 0, 0, this.options.width, this.options.height);

            // Draw highlight if hovered
            if (this.hoveredLayer && this.hoveredLayer.id === layer.id) {
                this.ctx.save();
                this.ctx.globalCompositeOperation = 'source-atop';
                this.ctx.fillStyle = 'rgba(255, 215, 0, 0.4)'; // Highlight color
                // This fillRect covers the whole canvas but masked by source-atop? 
                // No, source-atop composites onto EXISTING content.
                // But we drew ALL layers first. This approach is wrong for single layer highlight.
                // Correct approach: Draw layer on temp canvas, colorize it, draw on top?
                // Simpler: Draw Highlight Overlay AFTER all layers
            }
        });

        // Draw highlight overlay separately
        if (this.hoveredLayer) {
            this.ctx.save();
            // Use the offscreen canvas of the hovered layer as a mask?
            // Expensive to process full image per frame.
            // Alternative: Draw the hovered image again with a tint.

            // 1. Draw hovered image
            this.ctx.globalCompositeOperation = 'source-over';
            // Actually standard draw is fine, just draw it again on top
            this.ctx.drawImage(this.hoveredLayer.img, 0, 0, this.options.width, this.options.height);

            // 2. Tint it
            this.ctx.globalCompositeOperation = 'source-in';
            this.ctx.fillStyle = 'rgba(255, 220, 50, 0.5)';
            this.ctx.fillRect(0, 0, this.options.width, this.options.height);

            // Restore
            this.ctx.restore();

            // Re-draw the image boundaries (optional stroke effect)
            // Hard with just canvas.
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
            tCtx.fillStyle = 'rgba(255, 0, 0, 0.3)'; // Red tint
            tCtx.fillRect(0, 0, this.options.width, this.options.height);

            // Reset
            tCtx.globalCompositeOperation = 'source-over';

            // Draw temp canvas to main
            this.ctx.drawImage(this.tempCanvas, 0, 0);
        }
    }
}
