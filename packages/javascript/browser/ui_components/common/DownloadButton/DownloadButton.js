import Locale from '../../i18n/index.js';

/**
 * DownloadButton Component
 * 下載按鈕元件 - 支援 XLS、Word、PDF、Image、Portrait 五種類型
 */

export class DownloadButton {
    static TYPES = {
        XLS: 'xls',
        WORD: 'word',
        PDF: 'pdf',
        IMAGE: 'image',
        PORTRAIT: 'portrait'
    };

    static ICONS = {
        xls: {
            color: 'var(--cl-brand-excel)',
            label: 'XLS',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-brand-excel)"/>
                <rect x="8" y="8" width="32" height="24" rx="2" fill="white" fill-opacity="0.9"/>
                <line x1="8" y1="16" x2="40" y2="16" stroke="var(--cl-brand-excel)" stroke-width="1.5"/>
                <line x1="8" y1="24" x2="40" y2="24" stroke="var(--cl-brand-excel)" stroke-width="1.5"/>
                <line x1="20" y1="8" x2="20" y2="32" stroke="var(--cl-brand-excel)" stroke-width="1.5"/>
                <line x1="32" y1="8" x2="32" y2="32" stroke="var(--cl-brand-excel)" stroke-width="1.5"/>
                <path d="M24 44L18 38H21V35H27V38H30L24 44Z" fill="white"/>
            </svg>`
        },
        word: {
            color: 'var(--cl-brand-word)',
            label: 'DOC',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-brand-word)"/>
                <rect x="10" y="8" width="28" height="24" rx="2" fill="white" fill-opacity="0.9"/>
                <line x1="14" y1="14" x2="34" y2="14" stroke="var(--cl-brand-word)" stroke-width="2" stroke-linecap="round"/>
                <line x1="14" y1="20" x2="30" y2="20" stroke="var(--cl-brand-word)" stroke-width="2" stroke-linecap="round"/>
                <line x1="14" y1="26" x2="26" y2="26" stroke="var(--cl-brand-word)" stroke-width="2" stroke-linecap="round"/>
                <path d="M24 44L18 38H21V35H27V38H30L24 44Z" fill="white"/>
            </svg>`
        },
        pdf: {
            color: 'var(--cl-danger)',
            label: 'PDF',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-danger)"/>
                <path d="M10 8H32L38 14V32H10V8Z" fill="white" fill-opacity="0.9"/>
                <path d="M32 8V14H38" fill="var(--cl-bg-danger-lighter)" stroke="var(--cl-bg-danger-lighter)" stroke-width="1"/>
                <text x="24" y="25" font-family="Arial" font-size="8" font-weight="bold" fill="var(--cl-danger)" text-anchor="middle">PDF</text>
                <path d="M24 44L18 38H21V35H27V38H30L24 44Z" fill="white"/>
            </svg>`
        },
        image: {
            color: 'var(--cl-purple-dark)',
            label: 'IMG',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-purple-dark)"/>
                <rect x="8" y="8" width="32" height="24" rx="3" fill="white" fill-opacity="0.9"/>
                <circle cx="16" cy="16" r="4" fill="var(--cl-warning)"/>
                <path d="M8 32L18 20L26 28L32 22L40 32H8Z" fill="var(--cl-purple-dark)"/>
                <path d="M24 44L18 38H21V35H27V38H30L24 44Z" fill="white"/>
            </svg>`
        },
        portrait: {
            color: 'var(--cl-cyan-dark)',
            label: 'PHOTO',
            svg: `<svg viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
                <rect width="48" height="48" rx="8" fill="var(--cl-cyan-dark)"/>
                <rect x="10" y="6" width="28" height="28" rx="3" fill="white" fill-opacity="0.9"/>
                <circle cx="24" cy="16" r="7" fill="var(--cl-cyan-dark)"/>
                <ellipse cx="24" cy="32" rx="10" ry="8" fill="var(--cl-cyan-dark)"/>
                <rect x="10" y="28" width="28" height="6" fill="white" fill-opacity="0.9"/>
                <path d="M14 34Q24 24 34 34Z" fill="var(--cl-cyan-dark)"/>
                <path d="M24 44L18 38H21V35H27V38H30L24 44Z" fill="white"/>
            </svg>`
        }
    };

    /**
     * 建立下載按鈕
     * @param {Object} options - 設定選項
     * @param {string} options.type - 按鈕類型 (xls, word, pdf, image, portrait)
     * @param {string} options.url - 下載連結
     * @param {string} options.filename - 下載檔名
     * @param {Function} options.onClick - 點擊回調
     * @param {string} options.size - 按鈕尺寸 (small: 32px, medium: 48px, large: 64px)
     * @param {boolean} options.showLabel - 是否顯示標籤
     * @param {string} options.tooltip - 滑鼠提示文字
     */
    constructor(options = {}) {
        this.options = {
            type: 'pdf',
            url: '#',
            filename: '',
            onClick: null,
            size: 'medium',
            showLabel: false,
            tooltip: '',
            ...options
        };
        
        this.element = this._createElement();
    }

    _getSizeValue() {
        const sizes = { small: 32, medium: 48, large: 64 };
        return sizes[this.options.size] || 48;
    }

    _createElement() {
        const { type, url, filename, onClick, showLabel, tooltip } = this.options;
        const iconConfig = DownloadButton.ICONS[type] || DownloadButton.ICONS.pdf;
        const size = this._getSizeValue();

        // 建立容器
        const container = document.createElement('div');
        container.className = 'download-btn-container';
        container.style.cssText = `
            display: inline-flex;
            flex-direction: column;
            align-items: center;
            gap: 4px;
        `;

        // 建立按鈕
        const button = document.createElement('button');
        button.className = `download-btn download-btn--${type}`;
        button.setAttribute('type', 'button');
        button.setAttribute('title', tooltip || Locale.t('download.downloadLabel', { label: iconConfig.label }));
        button.setAttribute('aria-label', Locale.t('download.downloadAriaLabel', { label: iconConfig.label }));
        
        button.style.cssText = `
            width: ${size}px;
            height: ${size}px;
            padding: 0;
            border: none;
            border-radius: 8px;
            cursor: pointer;
            transition: all 0.2s ease;
            background: transparent;
            position: relative;
            overflow: hidden;
        `;

        button.innerHTML = iconConfig.svg;
        
        // SVG 填滿按鈕
        const svg = button.querySelector('svg');
        if (svg) {
            svg.style.cssText = `
                width: 100%;
                height: 100%;
                display: block;
            `;
        }

        // Hover 效果
        button.addEventListener('mouseenter', () => {
            button.style.transform = 'translateY(-2px)';
            button.style.boxShadow = `0 4px 12px ${iconConfig.color}40`;
        });
        
        button.addEventListener('mouseleave', () => {
            button.style.transform = 'translateY(0)';
            button.style.boxShadow = 'none';
        });

        // 點擊效果
        button.addEventListener('mousedown', () => {
            button.style.transform = 'translateY(0) scale(0.95)';
        });
        
        button.addEventListener('mouseup', () => {
            button.style.transform = 'translateY(-2px)';
        });

        // 點擊處理
        button.addEventListener('click', (e) => {
            if (onClick) {
                onClick(e, { type, url, filename });
            } else if (url && url !== '#') {
                this._download(url, filename);
            }
        });

        container.appendChild(button);

        // 標籤
        if (showLabel) {
            const label = document.createElement('span');
            label.className = 'download-btn-label';
            label.textContent = iconConfig.label;
            label.style.cssText = `
                font-size: 10px;
                font-weight: 600;
                color: ${iconConfig.color};
                text-transform: uppercase;
            `;
            container.appendChild(label);
        }

        return container;
    }

    _download(url, filename) {
        const link = document.createElement('a');
        link.href = url;
        link.download = filename || '';
        link.style.display = 'none';
        document.body.appendChild(link);
        link.click();
        link.remove();
    }

    /**
     * 掛載到指定容器
     * @param {HTMLElement|string} container - 容器元素或選擇器
     */
    mount(container) {
        const target = typeof container === 'string' 
            ? document.querySelector(container) 
            : container;
        
        if (target) {
            target.appendChild(this.element);
        }
        return this;
    }

    /**
     * 移除元件
     */
    destroy() {
        this.element?.remove();
    }

    /**
     * 建立下載按鈕群組
     * @param {Array} buttons - 按鈕設定陣列
     * @param {Object} groupOptions - 群組選項
     */
    static createGroup(buttons, groupOptions = {}) {
        const group = document.createElement('div');
        group.className = 'download-btn-group';
        group.style.cssText = `
            display: inline-flex;
            gap: ${groupOptions.gap || '8px'};
            align-items: flex-start;
        `;

        buttons.forEach(btnOptions => {
            const btn = new DownloadButton({ ...groupOptions, ...btnOptions });
            group.appendChild(btn.element);
        });

        return group;
    }
}

export default DownloadButton;
