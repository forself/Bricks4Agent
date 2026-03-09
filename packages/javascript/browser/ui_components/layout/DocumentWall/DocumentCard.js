import Locale from '../../i18n/index.js';

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
        pdf: `
            <svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M14 8H42L54 20V56C54 58.2091 52.2091 60 50 60H14C11.7909 60 10 58.2091 10 56V12C10 9.79086 11.7909 8 14 8Z" fill="var(--cl-danger)"/>
                <path d="M42 8V20H54" fill="var(--cl-danger-light)"/>
                <text x="32" y="45" font-family="Arial" font-size="14" fill="white" text-anchor="middle" font-weight="bold">PDF</text>
            </svg>
        `,
        doc: `
            <svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M14 8H42L54 20V56C54 58.2091 52.2091 60 50 60H14C11.7909 60 10 58.2091 10 56V12C10 9.79086 11.7909 8 14 8Z" fill="var(--cl-indigo)"/>
                <path d="M42 8V20H54" fill="var(--cl-indigo)"/>
                <text x="32" y="45" font-family="Arial" font-size="14" fill="white" text-anchor="middle" font-weight="bold">DOC</text>
            </svg>
        `,
        xls: `
            <svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M14 8H42L54 20V56C54 58.2091 52.2091 60 50 60H14C11.7909 60 10 58.2091 10 56V12C10 9.79086 11.7909 8 14 8Z" fill="var(--cl-success)"/>
                <path d="M42 8V20H54" fill="var(--cl-success-light)"/>
                <text x="32" y="45" font-family="Arial" font-size="14" fill="white" text-anchor="middle" font-weight="bold">XLS</text>
            </svg>
        `,
        ppt: `
            <svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M14 8H42L54 20V56C54 58.2091 52.2091 60 50 60H14C11.7909 60 10 58.2091 10 56V12C10 9.79086 11.7909 8 14 8Z" fill="var(--cl-warning)"/>
                <path d="M42 8V20H54" fill="var(--cl-warning-light)"/>
                <text x="32" y="45" font-family="Arial" font-size="14" fill="white" text-anchor="middle" font-weight="bold">PPT</text>
            </svg>
        `,
        image: `
            <svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M14 8H42L54 20V56C54 58.2091 52.2091 60 50 60H14C11.7909 60 10 58.2091 10 56V12C10 9.79086 11.7909 8 14 8Z" fill="var(--cl-purple)"/>
                <path d="M42 8V20H54" fill="var(--cl-purple-light)"/>
                <text x="32" y="45" font-family="Arial" font-size="14" fill="white" text-anchor="middle" font-weight="bold">IMG</text>
            </svg>
        `,
        other: `
            <svg viewBox="0 0 64 64" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M14 8H42L54 20V56C54 58.2091 52.2091 60 50 60H14C11.7909 60 10 58.2091 10 56V12C10 9.79086 11.7909 8 14 8Z" fill="var(--cl-grey)"/>
                <path d="M42 8V20H54" fill="var(--cl-border-light)"/>
                <text x="32" y="45" font-family="Arial" font-size="14" fill="white" text-anchor="middle" font-weight="bold">FILE</text>
            </svg>
        `
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

        this.element = this._createElement();
    }

    _createElement() {
        const { title, type, src, width, selected } = this.options;
        const iconSvg = DocumentCard.ICONS[type] || DocumentCard.ICONS.other;

        const container = document.createElement('div');
        container.className = 'document-card';
        container.style.cssText = `
            position: relative;
            width: ${width};
            background: var(--cl-bg);
            border: 1px solid var(--cl-border-light);
            border-radius: 8px;
            overflow: hidden;
            transition: all 0.2s;
            display: flex;
            flex-direction: column;
            box-shadow: 0 1px 3px rgba(0,0,0,0.1);
        `;

        container.addEventListener('mouseenter', () => {
            container.style.transform = 'translateY(-2px)';
            container.style.boxShadow = '0 4px 12px rgba(0,0,0,0.15)';
        });
        container.addEventListener('mouseleave', () => {
            container.style.transform = 'translateY(0)';
            container.style.boxShadow = '0 1px 3px rgba(0,0,0,0.1)';
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
            content.style.width = '64px';
            content.style.height = '64px';
            content.innerHTML = iconSvg;
        }

        preview.appendChild(content);
        container.appendChild(preview);

        // 標題
        const titleEl = document.createElement('div');
        titleEl.textContent = title;
        titleEl.title = title;
        titleEl.style.cssText = `
            padding: 8px 12px;
            font-size: 14px;
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
                font-size: 13px;
                color: ${color};
                cursor: pointer;
                border-right: 1px solid var(--cl-border-light);
                transition: background 0.2s;
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
            background: rgba(255, 255, 255, 0.9);
            border-radius: 4px;
            cursor: pointer;
            display: flex;
            align-items: center;
            justify-content: center;
            transition: all 0.2s;
            z-index: 2;
        `;

        // 選取狀態樣式
        const updateSelectState = (isSelected) => {
            if (isSelected) {
                selectBox.style.background = 'var(--cl-primary)';
                selectBox.style.borderColor = 'var(--cl-primary)';
                selectBox.innerHTML = `<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>`;
            } else {
                selectBox.style.background = 'rgba(255, 255, 255, 0.9)';
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
            deleteBtn.innerHTML = `
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                    <line x1="18" y1="6" x2="6" y2="18"></line>
                    <line x1="6" y1="6" x2="18" y2="18"></line>
                </svg>
            `;
            deleteBtn.style.cssText = `
                position: absolute;
                top: 8px;
                left: 8px;
                width: 24px;
                height: 24px;
                background: var(--cl-danger);
                color: var(--cl-text-inverse);
                border-radius: 50%;
                display: flex;
                align-items: center;
                justify-content: center;
                cursor: pointer;
                box-shadow: 0 2px 4px rgba(0,0,0,0.2);
                z-index: 10;
                opacity: 0;
                transition: opacity 0.2s;
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
}
