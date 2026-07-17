import Locale from '../../i18n/index.js';
import { Icon } from '../../common/Icon/index.js';

/**
 * DocumentCard Component
 * 文件卡片元件 - 顯示文件圖示、說明與下載按鈕
 */

export class DocumentCard {
    static TYPES = {
        PDF: 'pdf',
        DOC: 'doc',
        XLS: 'xls',
        PPT: 'ppt',
        IMAGE: 'image',
        OTHER: 'other'
    };

    // 預設圖示 SVG
    static ICONS = {
        pdf: 'var(--cl-danger)',
        doc: 'var(--cl-indigo)',
        xls: 'var(--cl-success)',
        ppt: 'var(--cl-warning)',
        image: 'var(--cl-purple)',
        other: 'var(--cl-grey)'
    };

    static TYPE_LABELS = {
        pdf: 'PDF',
        doc: 'DOC',
        xls: 'XLS',
        ppt: 'PPT',
        image: 'IMG',
        other: 'FILE'
    };

    /**
     * @param {Object} options
     * @param {string} options.title - 文件標題
     * @param {string} options.type - 文件類型 (pdf, doc, xls, ppt, image, other)
     * @param {string} options.src - 文件連結 (用於預覽)
     * @param {string} options.width - 寬度
     * @param {boolean} options.selected - 是否選取
     * @param {Function} options.onSelect - 選取回調
     * @param {Function} options.onEdit - 編輯按鈕回調
     * @param {Function} options.onDescription - 說明按鈕回調
     * @param {Function} options.onDownload - 下載按鈕回調
     * @param {Function} options.onDelete - 刪除按鈕回調
     */
    constructor(options = {}) {
        this.options = {
            title: 'Untitled',
            type: 'other',
            src: '',
            width: '100%',
            selected: false,
            onSelect: null,
            onEdit: null,
            onDescription: null,
            onDownload: null,
            onDelete: null,
            ...options
        };

        this._icons = [];
        this._selectIcon = null;
        this.element = this._createElement();
    }

    _createElement() {
        const { title, type, src, width, selected } = this.options;
        const iconColor = DocumentCard.ICONS[type] || DocumentCard.ICONS.other;

        const container = document.createElement('div');
        container.className = 'document-card';
        container.style.cssText = `
            position: relative;
            width: ${width};
            background: var(--cl-bg);
            border: 1px solid var(--cl-border-light);
            border-radius: var(--cl-radius-lg);
            overflow: hidden;
            transition: all var(--cl-transition);
            display: flex;
            flex-direction: column;
            box-shadow: var(--cl-shadow-sm);
        `;

        container.addEventListener('mouseenter', () => {
            container.style.transform = 'translateY(-2px)';
            container.style.boxShadow = 'var(--cl-shadow-md)';
        });
        container.addEventListener('mouseleave', () => {
            container.style.transform = 'translateY(0)';
            container.style.boxShadow = 'var(--cl-shadow-sm)';
        });

        // 預覽區 (圖示或圖片)
        const preview = document.createElement('div');
        preview.className = 'document-card-preview';
        preview.style.cssText = `
            flex: 1;
            min-height: 120px;
            display: flex;
            align-items: center;
            justify-content: center;
            background: var(--cl-bg);
            padding: 16px;
            position: relative;
            overflow: hidden;
        `;

        let content;
        // 如果是圖片且有 src，顯示圖片
        if (type === 'image' && src && src !== '#') {
            content = document.createElement('img');
            content.src = src;
            content.style.cssText = `
                width: 100%;
                height: 100%;
                object-fit: cover; 
                position: absolute;
                top: 0;
                left: 0;
             `;
            preview.style.padding = '0'; // 圖片模式移除 padding
        } else {
            // 顯示圖示
            content = document.createElement('div');
            const normalizedType = DocumentCard.TYPE_LABELS[type] ? type : 'other';
            content.className = `document-card__type-badge document-card__type-badge--${normalizedType}`;
            content.textContent = DocumentCard.TYPE_LABELS[normalizedType];
            content.setAttribute('aria-label', content.textContent);
            content.style.cssText = `
                width: 64px;
                height: 64px;
                display: flex;
                align-items: center;
                justify-content: center;
                border-radius: var(--cl-radius-md);
                background: ${iconColor};
                color: var(--cl-text-inverse);
                font-family: var(--cl-font-family);
                font-size: var(--cl-font-size-lg);
                font-weight: 700;
                letter-spacing: 0.04em;
                box-shadow: var(--cl-shadow-sm);
            `;
        }

        preview.appendChild(content);
        container.appendChild(preview);

        // 標題
        const titleEl = document.createElement('div');
        titleEl.textContent = title;
        titleEl.title = title;
        titleEl.style.cssText = `
            padding: 8px 12px;
            font-size: var(--cl-font-size-lg);
            color: var(--cl-text);
            text-align: center;
            white-space: nowrap;
            overflow: hidden;
            text-overflow: ellipsis;
            border-top: 1px solid var(--cl-bg-subtle);
            background: var(--cl-bg);
        `;
        container.appendChild(titleEl);

        // 按鈕區
        const actions = document.createElement('div');
        actions.style.cssText = `
            display: flex;
            border-top: 1px solid var(--cl-border-light);
        `;

        const createBtn = (text, color, onClick) => {
            const btn = document.createElement('button');
            btn.textContent = text;
            btn.style.cssText = `
                flex: 1;
                border: none;
                background: transparent;
                padding: 8px 0;
                font-size: var(--cl-font-size-md);
                color: ${color};
                cursor: pointer;
                border-right: 1px solid var(--cl-border-light);
                transition: background var(--cl-transition);
            `;
            btn.onmouseenter = () => btn.style.background = 'var(--cl-bg-secondary)';
            btn.onmouseleave = () => btn.style.background = 'transparent';
            btn.onclick = (e) => {
                e.stopPropagation();
                if (onClick) onClick();
            };
            return btn;
        };

        // 編輯按鈕
        const editBtn = createBtn(Locale.t('documentWall.editBtn'), 'var(--cl-success)', this.options.onEdit);

        // 說明按鈕
        const descBtn = createBtn(Locale.t('documentWall.descBtn'), 'var(--cl-text-secondary)', this.options.onDescription);

        // 下載按鈕 (最後一個不需右邊框)
        const downloadBtn = createBtn(Locale.t('documentWall.downloadBtn'), 'var(--cl-primary)', this.options.onDownload);
        downloadBtn.style.borderRight = 'none';

        actions.appendChild(editBtn);
        actions.appendChild(descBtn);
        actions.appendChild(downloadBtn);
        container.appendChild(actions);

        // 選取框 (右上角)
        const selectBox = document.createElement('div');
        selectBox.style.cssText = `
            position: absolute;
            top: 8px;
            right: 8px;
            width: 20px;
            height: 20px;
            border: 2px solid var(--cl-border);
            background: var(--cl-bg-surface-overlay);
            border-radius: var(--cl-radius-sm);
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all var(--cl-transition);
            z-index: 2;
        `;

        // 選取狀態樣式
        const updateSelectState = (isSelected) => {
            this._selectIcon?.destroy();
            this._selectIcon = null;
            if (isSelected) {
                selectBox.style.background = 'var(--cl-primary)';
                selectBox.style.borderColor = 'var(--cl-primary)';
                selectBox.replaceChildren();
                this._selectIcon = new Icon({ name: 'check', size: 14, color: 'var(--cl-text-inverse)' });
                this._selectIcon.mount(selectBox);
            } else {
                selectBox.style.background = 'var(--cl-bg-surface-overlay)';
                selectBox.style.borderColor = 'var(--cl-border)';
                selectBox.innerHTML = '';
            }
        };

        updateSelectState(selected);

        selectBox.onclick = (e) => {
            e.stopPropagation();
            if (this.options.onSelect) {
                this.options.onSelect();
            }
        };

        // 為了讓外部控制選取狀態，我們可能需要這部分的參照或重繪機制
        // 這邊設計為簡單的重繪或外部設定
        this.updateSelectState = updateSelectState;

        container.appendChild(selectBox);

        // 刪除按鈕 (左上角，類似 PhotoWall)
        if (this.options.onDelete) {
            const deleteBtn = document.createElement('div');
            this._mountIcon(deleteBtn, { name: 'close', size: 14, color: 'currentColor' });
            deleteBtn.style.cssText = `
                position: absolute;
                top: 8px;
                left: 8px;
                width: 24px;
                height: 24px;
                background: var(--cl-danger);
                color: var(--cl-text-inverse);
                border-radius: var(--cl-radius-round);
                display: flex;
                align-items: center;
                justify-content: center;
                cursor: pointer;
                box-shadow: var(--cl-shadow-sm);
                z-index: 10;
                opacity: 0;
                transition: opacity var(--cl-transition);
            `;

            // 懸停時顯示
            container.addEventListener('mouseenter', () => deleteBtn.style.opacity = '1');
            container.addEventListener('mouseleave', () => deleteBtn.style.opacity = '0');

            deleteBtn.onclick = (e) => {
                e.stopPropagation();
                if (this.options.onDelete) this.options.onDelete();
            };

            container.appendChild(deleteBtn);
        }

        return container;
    }

    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) target.appendChild(this.element);
        return this;
    }

    _mountIcon(container, options) {
        const icon = new Icon(options);
        this._icons.push(icon);
        icon.mount(container);
        return icon;
    }

    destroy() {
        this._selectIcon?.destroy();
        this._selectIcon = null;
        this._icons.forEach((icon) => icon.destroy());
        this._icons = [];
        this.element?.remove();
    }
}
