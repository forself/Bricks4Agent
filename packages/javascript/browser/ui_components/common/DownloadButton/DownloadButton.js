import Locale from '../../i18n/index.js';
import { Icon } from '../Icon/index.js';

/**
 * DownloadButton Component
 * 下載按鈕元件 - 支援 XLS、Word、PDF、Image、Portrait、JSON、CSS 七種類型
 */

export class DownloadButton {
    static TYPES = {
        XLS: 'xls',
        WORD: 'word',
        PDF: 'pdf',
        IMAGE: 'image',
        PORTRAIT: 'portrait',
        JSON: 'json',
        CSS: 'css'
    };

    static ICONS = {
        xls: {
            color: 'var(--cl-brand-excel)',
            label: 'XLS',
            icon: 'download'
        },
        word: {
            color: 'var(--cl-brand-word)',
            label: 'DOC',
            icon: 'download'
        },
        pdf: {
            color: 'var(--cl-danger)',
            label: 'PDF',
            icon: 'download'
        },
        image: {
            color: 'var(--cl-purple-dark)',
            label: 'IMG',
            icon: 'download'
        },
        portrait: {
            color: 'var(--cl-cyan-dark)',
            label: 'PHOTO',
            icon: 'download'
        },
        json: {
            color: 'var(--cl-amber-700)',
            label: 'JSON',
            icon: 'download'
        },
        css: {
            color: 'var(--cl-indigo-700)',
            label: 'CSS',
            icon: 'download'
        }
    };

    /**
     * 建立下載按鈕
     * @param {Object} options - 設定選項
     * @param {string} options.type - 按鈕類型 (xls, word, pdf, image, portrait, json, css)
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
            border-radius: var(--cl-radius-lg);
            cursor: pointer;
            transition: all var(--cl-transition);
            background: transparent;
            position: relative;
            overflow: hidden;
            display: inline-flex;
            align-items: center;
            justify-content: center;
            color: ${iconConfig.color};
        `;

        this._icon = new Icon({ name: iconConfig.icon, size: Math.round(size * 0.56), color: iconConfig.color });
        this._icon.mount(button);

        const formatBadge = document.createElement('span');
        formatBadge.className = 'download-btn__format';
        formatBadge.textContent = iconConfig.label;
        formatBadge.setAttribute('aria-hidden', 'true');
        formatBadge.style.cssText = `
            position: absolute;
            right: 2px;
            bottom: 2px;
            max-width: calc(100% - 4px);
            padding: 1px 2px;
            border-radius: var(--cl-radius-xs);
            background: var(--cl-bg);
            color: ${iconConfig.color};
            font: 700 ${Math.max(7, Math.round(size * 0.16))}px/1 system-ui, sans-serif;
            overflow: hidden;
            pointer-events: none;
        `;
        button.appendChild(formatBadge);

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
                font-size: var(--cl-font-size-2xs);
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
        this._icon?.destroy();
        this.element?.remove();
    }

    /**
     * 建立下載按鈕群組
     * @param {Array} buttons - 按鈕設定陣列
     * @param {Object} groupOptions - 群組選項
     */
    static createGroup(buttons, groupOptions = {}) {
        const group = document.createElement('div');
        const components = [];
        group.className = 'download-btn-group';
        group.style.cssText = `
            display: inline-flex;
            gap: ${groupOptions.gap || '8px'};
            align-items: flex-start;
        `;

        buttons.forEach(btnOptions => {
            const btn = new DownloadButton({ ...groupOptions, ...btnOptions });
            components.push(btn);
            group.appendChild(btn.element);
        });

        Object.defineProperty(group, '_components', {
            value: components,
            enumerable: false
        });
        let destroyed = false;
        Object.defineProperty(group, 'destroy', {
            enumerable: false,
            value: () => {
                if (destroyed) return;
                destroyed = true;
                components.splice(0).forEach(component => component.destroy());
                group.remove();
            }
        });

        return group;
    }
}

export default DownloadButton;
