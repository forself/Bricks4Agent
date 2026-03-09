/**
 * ImageViewer Component
 * 圖片展示器 - 支援放大縮小的燈箱效果（Modal）
 */

export class ImageViewer {
    static instance = null;

    /**
     * 開啟圖片展示器
     * @param {string} src - 圖片來源
     * @param {Object} options
     * @param {number} options.minZoom - 最小縮放比例（預設 0.1）
     * @param {number} options.maxZoom - 最大縮放比例（預設 3）
     * @param {number} options.zoomStep - 縮放步長（預設 0.2）
     */
    static open(src, options = {}) {
        if (ImageViewer.instance) {
            ImageViewer.instance.setOptions(options);
            ImageViewer.instance.setSrc(src);
        } else {
            ImageViewer.instance = new ImageViewer(src, options);
        }
    }

    static close() {
        if (ImageViewer.instance) {
            ImageViewer.instance.destroy();
            ImageViewer.instance = null;
        }
    }

    constructor(src, options = {}) {
        this.options = {
            minZoom: 0.1,
            maxZoom: 3,
            zoomStep: 0.2,
            onPrev: null, // 上一張回調
            onNext: null, // 下一張回調
            ...options
        };

        this.src = src;
        this.zoom = 1;
        this.panX = 0;
        this.panY = 0;
        this.isPanning = false;
        this.startX = 0;
        this.startY = 0;

        this._createElement();
        this._bindEvents();
    }

    setOptions(options) {
        this.options = { ...this.options, ...options };
        this._updateNavigation();
    }

    setSrc(src) {
        if (this.src === src) return;
        this.src = src;
        this.image.src = src;
        this._resetZoom();
    }

    _createElement() {
        // 遮罩層（阻擋背景互動）
        this.overlay = document.createElement('div');
        this.overlay.className = 'image-viewer__overlay';
        this.overlay.style.cssText = `
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(0, 0, 0, 0.85);
            z-index: 10000;
            display: flex;
            align-items: center;
            justify-content: center;
            cursor: pointer;
        `;

        // 內容容器
        this.container = document.createElement('div');
        this.container.className = 'image-viewer__container';
        this.container.style.cssText = `
            position: relative;
            max-width: 90vw;
            max-height: 85vh;
            overflow: hidden;
            border-radius: 8px;
            background: var(--cl-text-dark);
            cursor: default;
        `;

        // 圖片包裹層（用於拖曳）
        this.imageWrapper = document.createElement('div');
        this.imageWrapper.className = 'image-viewer__wrapper';
        this.imageWrapper.style.cssText = `
            overflow: hidden;
            display: flex;
            align-items: center;
            justify-content: center;
            max-width: 90vw;
            max-height: calc(85vh - 50px);
            min-width: 300px;
            min-height: 200px;
        `;

        // 圖片
        this.image = document.createElement('img');
        this.image.src = this.src;
        this.image.className = 'image-viewer__image';
        this.image.draggable = false;
        this.image.style.cssText = `
            max-width: 100%;
            max-height: calc(85vh - 50px);
            transform-origin: center center;
            transition: transform 0.1s ease-out;
            cursor: grab;
            user-select: none;
        `;

        // 左箭頭
        this.prevBtn = this._createNavButton('prev', `
            <svg width="40" height="40" viewBox="0 0 24 24" fill="none">
                <path d="M15 18L9 12L15 6" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
        `, () => this.options.onPrev?.());

        // 右箭頭
        this.nextBtn = this._createNavButton('next', `
            <svg width="40" height="40" viewBox="0 0 24 24" fill="none">
                <path d="M9 18L15 12L9 6" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
        `, () => this.options.onNext?.());

        this._updateNavigation();

        // 工具列
        this.toolbar = document.createElement('div');
        this.toolbar.className = 'image-viewer__toolbar';
        this.toolbar.style.cssText = `
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 16px;
            padding: 12px;
            background: rgba(0, 0, 0, 0.5);
        `;

        // 縮小按鈕
        this.zoomOutBtn = this._createToolButton('zoom-out', `
            <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                <circle cx="9" cy="9" r="6" stroke="white" stroke-width="2"/>
                <path d="M13.5 13.5L18 18" stroke="white" stroke-width="2" stroke-linecap="round"/>
                <path d="M6 9H12" stroke="white" stroke-width="2" stroke-linecap="round"/>
            </svg>
        `, () => this._zoomOut());

        // 縮放顯示
        this.zoomDisplay = document.createElement('span');
        this.zoomDisplay.className = 'image-viewer__zoom-display';
        this.zoomDisplay.textContent = '100%';
        this.zoomDisplay.style.cssText = `
            color: var(--cl-text-inverse);
            font-size: 14px;
            min-width: 50px;
            text-align: center;
        `;

        // 放大按鈕
        this.zoomInBtn = this._createToolButton('zoom-in', `
            <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                <circle cx="9" cy="9" r="6" stroke="white" stroke-width="2"/>
                <path d="M13.5 13.5L18 18" stroke="white" stroke-width="2" stroke-linecap="round"/>
                <path d="M9 6V12M6 9H12" stroke="white" stroke-width="2" stroke-linecap="round"/>
            </svg>
        `, () => this._zoomIn());

        // 重設按鈕
        this.resetBtn = this._createToolButton('reset', `
            <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                <path d="M3 10C3 6.134 6.134 3 10 3C13.866 3 17 6.134 17 10C17 13.866 13.866 17 10 17" stroke="white" stroke-width="2" stroke-linecap="round"/>
                <path d="M10 13V17H6" stroke="white" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
        `, () => this._resetZoom());

        // 關閉按鈕
        this.closeBtn = this._createToolButton('close', `
            <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                <path d="M5 5L15 15M5 15L15 5" stroke="white" stroke-width="2" stroke-linecap="round"/>
            </svg>
        `, () => this.destroy());

        // 組裝
        this.toolbar.appendChild(this.zoomOutBtn);
        this.toolbar.appendChild(this.zoomDisplay);
        this.toolbar.appendChild(this.zoomInBtn);
        this.toolbar.appendChild(this.resetBtn);
        this.toolbar.appendChild(this.closeBtn);

        this.imageWrapper.appendChild(this.image);
        this.container.appendChild(this.imageWrapper);
        this.container.appendChild(this.toolbar);

        // 加入導航按鈕到 overlay (為了在圖片兩側)
        this.overlay.appendChild(this.prevBtn);
        this.overlay.appendChild(this.container);
        this.overlay.appendChild(this.nextBtn);

        document.body.appendChild(this.overlay);
        document.body.style.overflow = 'hidden';
    }

    _createNavButton(type, svg, onClick) {
        const btn = document.createElement('div');
        btn.className = `image-viewer__nav image-viewer__nav--${type}`;
        btn.innerHTML = svg;
        btn.style.cssText = `
            position: absolute;
            top: 50%;
            transform: translateY(-50%);
            ${type === 'prev' ? 'left: 20px;' : 'right: 20px;'}
            width: 50px;
            height: 50px;
            display: flex;
            align-items: center;
            justify-content: center;
            background: rgba(0, 0, 0, 0.3);
            border-radius: 50%;
            cursor: pointer;
            color: var(--cl-text-inverse);
            transition: all 0.2s;
            z-index: 10001;
        `;

        btn.addEventListener('mouseenter', () => btn.style.background = 'rgba(0, 0, 0, 0.6)');
        btn.addEventListener('mouseleave', () => btn.style.background = 'rgba(0, 0, 0, 0.3)');
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            onClick();
        });

        return btn;
    }

    _updateNavigation() {
        if (this.prevBtn) this.prevBtn.style.display = this.options.onPrev ? 'flex' : 'none';
        if (this.nextBtn) this.nextBtn.style.display = this.options.onNext ? 'flex' : 'none';
    }

    _createToolButton(name, svg, onClick) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = `image-viewer__btn image-viewer__btn--${name}`;
        btn.innerHTML = svg;
        btn.style.cssText = `
            display: flex;
            align-items: center;
            justify-content: center;
            width: 36px;
            height: 36px;
            border: none;
            border-radius: 50%;
            background: rgba(255, 255, 255, 0.1);
            cursor: pointer;
            transition: background 0.2s;
        `;

        btn.addEventListener('mouseenter', () => btn.style.background = 'rgba(255, 255, 255, 0.2)');
        btn.addEventListener('mouseleave', () => btn.style.background = 'rgba(255, 255, 255, 0.1)');
        btn.addEventListener('click', (e) => {
            e.stopPropagation();
            onClick();
        });

        return btn;
    }

    _bindEvents() {
        // 點擊遮罩關閉
        this.overlay.addEventListener('click', (e) => {
            if (e.target === this.overlay) {
                this.destroy();
            }
        });

        // 阻止容器點擊冒泡
        this.container.addEventListener('click', (e) => e.stopPropagation());

        // 鍵盤事件
        this._handleKeydown = (e) => {
            if (e.key === 'Escape') {
                this.destroy();
            } else if (e.key === 'ArrowLeft' && this.options.onPrev) {
                this.options.onPrev();
            } else if (e.key === 'ArrowRight' && this.options.onNext) {
                this.options.onNext();
            }
        };
        document.addEventListener('keydown', this._handleKeydown);

        // 滾輪縮放
        this.imageWrapper.addEventListener('wheel', (e) => {
            e.preventDefault();
            if (e.deltaY < 0) {
                this._zoomIn();
            } else {
                this._zoomOut();
            }
        });

        // 拖曳平移
        this.image.addEventListener('mousedown', (e) => {
            if (this.zoom > 1) {
                this.isPanning = true;
                this.startX = e.clientX - this.panX;
                this.startY = e.clientY - this.panY;
                this.image.style.cursor = 'grabbing';
            }
        });

        document.addEventListener('mousemove', this._handleMouseMove = (e) => {
            if (this.isPanning) {
                this.panX = e.clientX - this.startX;
                this.panY = e.clientY - this.startY;
                this._updateTransform();
            }
        });

        document.addEventListener('mouseup', this._handleMouseUp = () => {
            this.isPanning = false;
            this.image.style.cursor = 'grab';
        });
    }

    _zoomIn() {
        const { maxZoom, zoomStep } = this.options;
        this.zoom = Math.min(maxZoom, this.zoom + zoomStep);
        this._updateTransform();
    }

    _zoomOut() {
        const { minZoom, zoomStep } = this.options;
        this.zoom = Math.max(minZoom, this.zoom - zoomStep);
        // 縮小時重設平移
        if (this.zoom <= 1) {
            this.panX = 0;
            this.panY = 0;
        }
        this._updateTransform();
    }

    _resetZoom() {
        this.zoom = 1;
        this.panX = 0;
        this.panY = 0;
        this._updateTransform();
    }

    _updateTransform() {
        this.image.style.transform = `scale(${this.zoom}) translate(${this.panX / this.zoom}px, ${this.panY / this.zoom}px)`;
        this.zoomDisplay.textContent = `${Math.round(this.zoom * 100)}%`;

        // 更新游標
        this.image.style.cursor = this.zoom > 1 ? 'grab' : 'default';
    }

    destroy() {
        document.removeEventListener('keydown', this._handleKeydown);
        document.removeEventListener('mousemove', this._handleMouseMove);
        document.removeEventListener('mouseup', this._handleMouseUp);

        this.overlay?.remove();

        document.body.style.overflow = '';
        ImageViewer.instance = null;
    }
}

export default ImageViewer;
