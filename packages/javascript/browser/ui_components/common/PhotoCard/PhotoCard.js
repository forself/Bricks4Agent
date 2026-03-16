/**
 * PhotoCard Component
 * 照片卡片元件 - 支援人像(4:3)與地點(3:4)兩種比例
 */

import { ImageViewer } from '../ImageViewer/index.js';

export class PhotoCard {
    static TYPES = {
        PORTRAIT: 'portrait',  // 人像 4:3
        LOCATION: 'location'   // 地點 3:4
    };

    // 預設圖示 SVG
    static DEFAULT_ICONS = {
        portrait: `
            <svg viewBox="0 0 80 80" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="80" height="80" fill="var(--cl-border-light)"/>
                <circle cx="40" cy="28" r="14" fill="var(--cl-grey-light)"/>
                <ellipse cx="40" cy="65" rx="22" ry="16" fill="var(--cl-grey-light)"/>
            </svg>
        `,
        location: `
            <svg viewBox="0 0 80 80" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="80" height="80" fill="var(--cl-border-light)"/>
                <path d="M40 20C33.373 20 28 25.373 28 32C28 41 40 55 40 55C40 55 52 41 52 32C52 25.373 46.627 20 40 20Z" stroke="var(--cl-grey-light)" stroke-width="3" fill="none"/>
                <circle cx="40" cy="32" r="5" fill="var(--cl-grey-light)"/>
            </svg>
        `
    };

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
        iconWrapper.innerHTML = PhotoCard.DEFAULT_ICONS[type];

        placeholder.appendChild(iconWrapper);
        return placeholder;
    }

    /**
     * 設定圖片
     */
    setSrc(src) {
        this.options.src = src;

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
        if (this.element?.parentNode) {
            this.element.remove();
        }
    }
}

export default PhotoCard;
