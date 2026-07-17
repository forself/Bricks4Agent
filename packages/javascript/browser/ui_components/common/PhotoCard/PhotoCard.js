/**
 * PhotoCard Component
 * 照片卡片元件 - 支援人像(4:3)與地點(3:4)兩種比例
 */

import { ImageViewer } from '../ImageViewer/index.js';
import { Icon } from '../Icon/index.js';

export class PhotoCard {
    static TYPES = {
        PORTRAIT: 'portrait',  // 人像 4:3
        LOCATION: 'location'   // 地點 3:4
    };

    // 預設圖示 SVG
    /**
     * @param {Object} options
     * @param {string} options.type - 'portrait' 或 'location'
     * @param {string} options.src - 縮圖來源（可選）
     * @param {string} options.oriSrc - 原圖來源（可選，點擊時優先使用）
     * @param {Function} options.getImg - 取得圖片函式（可選，動態取得原圖）
     * @param {string} options.alt - 替代文字
     * @param {string} options.width - 寬度
     * @param {boolean} options.clickable - 是否可點擊展示
     */
    constructor(options = {}) {
        this.options = {
            type: 'portrait',
            src: null,
            oriSrc: null, // 原圖 URL
            getImg: null, // 動態取得原圖的函式
            alt: '',
            width: 'auto',
            clickable: true,
            ...options
        };

        this._icons = [];
        this.element = this._createElement();
    }

    _getAspectRatio() {
        // portrait 4:3 (寬:高)，location 3:4 (寬:高)
        return this.options.type === 'portrait'
            ? { w: 4, h: 3 }  // 人像: 較寬
            : { w: 3, h: 4 }; // 地點: 較高
    }

    _createElement() {
        const { type, src, alt, width, clickable } = this.options;
        const ratio = this._getAspectRatio();
        const hasImage = !!src;

        // 容器
        const container = document.createElement('div');
        container.className = `photo-card photo-card--${type}`;
        container.style.cssText = `
            position: relative;
            width: ${width};
            aspect-ratio: ${ratio.w} / ${ratio.h};
            border-radius: var(--cl-radius-lg);
            overflow: hidden;
            background: var(--cl-bg-secondary);
            cursor: ${clickable && hasImage ? 'pointer' : 'default'};
            transition: all var(--cl-transition);
        `;

        if (hasImage) {
            // 實際圖片
            const img = document.createElement('img');
            img.src = src;
            img.alt = alt;
            img.className = 'photo-card__image';
            img.draggable = false;
            img.style.cssText = `
                width: 100%;
                height: 100%;
                object-fit: cover;
                display: block;
            `;

            // 錯誤處理：圖片載入失敗時顯示預設圖示
            img.onerror = () => {
                this._destroyIcons();
                container.innerHTML = '';
                container.appendChild(this._createPlaceholder());
            };

            container.appendChild(img);
            this.image = img;

            // Hover 效果
            if (clickable) {
                container.addEventListener('mouseenter', () => {
                    container.style.transform = 'scale(1.02)';
                    container.style.boxShadow = 'var(--cl-shadow-md)';
                });
                container.addEventListener('mouseleave', () => {
                    container.style.transform = 'scale(1)';
                    container.style.boxShadow = 'none';
                });

                // 點擊開啟展示
                container.addEventListener('click', async () => {
                    // 優先順序：getImg 函式 > oriSrc 原圖 > src 縮圖
                    let displaySrc = src;
                    
                    if (this.options.getImg) {
                        // 透過 getImg 函式動態取得原圖
                        try {
                            const oriImg = await this.options.getImg(this);
                            if (oriImg) displaySrc = oriImg;
                        } catch (e) {
                            console.warn('getImg 取得原圖失敗:', e);
                        }
                    } else if (this.options.oriSrc) {
                        // 使用預設的 oriSrc 原圖
                        displaySrc = this.options.oriSrc;
                    }
                    
                    ImageViewer.open(displaySrc);
                });
            }
        } else {
            // 無圖片時顯示預設圖示
            container.appendChild(this._createPlaceholder());
        }

        return container;
    }

    _createPlaceholder() {
        const { type } = this.options;

        const placeholder = document.createElement('div');
        placeholder.className = 'photo-card__placeholder';
        placeholder.style.cssText = `
            width: 100%;
            height: 100%;
            display: flex;
            align-items: center;
            justify-content: center;
            background: var(--cl-bg-subtle);
        `;

        const iconWrapper = document.createElement('div');
        iconWrapper.style.cssText = `
            width: 50%;
            max-width: 80px;
            opacity: 0.6;
        `;
        const icon = new Icon({
            name: type === PhotoCard.TYPES.LOCATION ? 'place' : 'account-circle',
            size: 64,
            color: 'var(--cl-text-muted)'
        });
        this._icons.push(icon);
        icon.mount(iconWrapper);

        placeholder.appendChild(iconWrapper);
        return placeholder;
    }

    /**
     * 設定圖片
     */
    setSrc(src) {
        this.options.src = src;
        this._destroyIcons();

        if (src) {
            // 移除舊內容
            this.element.innerHTML = '';

            const img = document.createElement('img');
            img.src = src;
            img.alt = this.options.alt;
            img.className = 'photo-card__image';
            img.draggable = false;
            img.style.cssText = `
                width: 100%;
                height: 100%;
                object-fit: cover;
                display: block;
            `;

            img.onerror = () => {
                this._destroyIcons();
                this.element.innerHTML = '';
                this.element.appendChild(this._createPlaceholder());
            };

            this.element.appendChild(img);
            this.image = img;
            this.element.style.cursor = this.options.clickable ? 'pointer' : 'default';

            if (this.options.clickable) {
                this.element.onclick = () => ImageViewer.open(src);
            }
        } else {
            this.element.innerHTML = '';
            this.element.appendChild(this._createPlaceholder());
            this.element.style.cursor = 'default';
            this.element.onclick = null;
        }
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    destroy() {
        this._destroyIcons();
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }

    _destroyIcons() {
        this._icons.forEach((icon) => icon.destroy());
        this._icons = [];
    }
}

export default PhotoCard;
