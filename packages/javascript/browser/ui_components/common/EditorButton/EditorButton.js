/**
 * EditorButton Component
 * 編輯器工具列專用按鈕 - 提供統一的風格與互動效果
 *
 * @module EditorButton
 * @version 1.0.0
 *
 * 特色：
 * - 統一的視覺風格，適合各類編輯器工具列
 * - 支援切換狀態 (active/inactive)
 * - 豐富的預設類型 (格式化、對齊、匯出等 50+ 種)
 * - 支援純圖示、圖示+文字、純文字三種模式
 * - 三種主題：light, dark, gradient
 * - 完整的無障礙設計支援 (ARIA)
 *
 * @example 基本使用
 * ```javascript
 * import { EditorButton } from './EditorButton.js';
 *
 * const boldBtn = new EditorButton({
 *     type: EditorButton.TYPES.BOLD,
 *     onClick: () => document.execCommand('bold')
 * });
 * boldBtn.mount('#toolbar');
 * ```
 *
 * @example 切換狀態
 * ```javascript
 * const btn = new EditorButton({
 *     type: EditorButton.TYPES.ITALIC,
 *     active: false,
 *     onClick: (e, button) => {
 *         button.active = !button.active;
 *     }
 * });
 * ```
 *
 * @example 自訂按鈕
 * ```javascript
 * const customBtn = new EditorButton({
 *     type: 'custom',
 *     label: '我的按鈕',
 *     iconName: 'flag',
 *     theme: 'gradient',
 *     onClick: () => console.log('clicked')
 * });
 * ```
 */
import Locale from '../../i18n/index.js';
import { Icon } from '../Icon/index.js';


export class EditorButton {
    // 按鈕類型
    static TYPES = {
        // 文字格式
        BOLD: 'bold',
        ITALIC: 'italic',
        UNDERLINE: 'underline',
        STRIKETHROUGH: 'strikethrough',
        SUBSCRIPT: 'subscript',
        SUPERSCRIPT: 'superscript',

        // 段落格式
        HEADING1: 'heading1',
        HEADING2: 'heading2',
        HEADING3: 'heading3',
        PARAGRAPH: 'paragraph',
        QUOTE: 'quote',
        CODE: 'code',

        // 對齊
        ALIGN_LEFT: 'alignLeft',
        ALIGN_CENTER: 'alignCenter',
        ALIGN_RIGHT: 'alignRight',
        ALIGN_JUSTIFY: 'alignJustify',

        // 列表
        LIST_BULLET: 'listBullet',
        LIST_NUMBER: 'listNumber',
        INDENT: 'indent',
        OUTDENT: 'outdent',

        // 歷史
        UNDO: 'undo',
        REDO: 'redo',

        // 插入
        LINK: 'link',
        INSERT_LINK: 'link',           // 別名
        IMAGE: 'image',
        TABLE: 'table',
        INSERT_TABLE: 'table',         // 別名
        LINE: 'line',
        HORIZONTAL_LINE: 'line',       // 別名
        PAGE_BREAK: 'pageBreak',
        INSERT_DRAWING: 'insertDrawing',
        INSERT_TOC: 'insertToc',

        // 繪圖工具
        PEN: 'pen',
        ERASER: 'eraser',
        LINE_TOOL: 'lineTool',
        HIGHLIGHTER: 'highlighter',
        RECT: 'rect',
        CIRCLE: 'circle',
        ARROW: 'arrow',
        TEXT: 'text',
        SELECT: 'select',

        // 測量工具
        MEASURE_DISTANCE: 'measureDistance',
        MEASURE_AREA: 'measureArea',
        COORDINATE: 'coordinate',

        // 匯出
        EXPORT_PDF: 'exportPdf',
        EXPORT_WORD: 'exportWord',
        EXPORT_MARKDOWN: 'exportMarkdown',
        EXPORT_PNG: 'exportPng',
        EXPORT_JSON: 'exportJson',

        // 其他
        SEARCH: 'search',
        REPLACE: 'replace',
        FULLSCREEN: 'fullscreen',
        CLEAR: 'clear',
        CLEAR_ALL: 'clearAll',
        REMOVE_FORMAT: 'removeFormat',
        COPY: 'copy',
        PASTE: 'paste',
        CUT: 'cut',
        TOC: 'toc',
        GENERATE_TOC: 'generateToc',
        SETTINGS: 'settings',
        LAYERS: 'layers',
        ZOOM_IN: 'zoomIn',
        ZOOM_OUT: 'zoomOut',

        // 版面配置
        HEADER: 'header',
        FOOTER: 'footer',
        PAGE_NUMBER: 'pageNumber',
        MARGIN: 'margin',

        // 匯出別名
        EXPORT_MD: 'exportMarkdown',

        // 通用
        CUSTOM: 'custom'
    };

    // 按鈕配置
    static CONFIG = {
        // 文字格式
        bold: {
            label: Locale.t('editorButton.bold'),
            shortLabel: 'B',
            icon: 'format-bold',
            shortcut: 'Ctrl+B'
        },
        italic: {
            label: Locale.t('editorButton.italic'),
            shortLabel: 'I',
            icon: 'format-italic',
            shortcut: 'Ctrl+I'
        },
        underline: {
            label: Locale.t('editorButton.underline'),
            shortLabel: 'U',
            icon: 'format-underlined',
            shortcut: 'Ctrl+U'
        },
        strikethrough: {
            label: Locale.t('editorButton.strikethrough'),
            shortLabel: 'S',
            icon: 'strikethrough-s'
        },
        subscript: {
            label: Locale.t('editorButton.subscript'),
            shortLabel: 'X₂',
            icon: 'subscript'
        },
        superscript: {
            label: Locale.t('editorButton.superscript'),
            shortLabel: 'X²',
            icon: 'superscript'
        },

        // 段落格式
        heading1: { label: Locale.t('editorButton.heading1'), shortLabel: 'H1', iconGlyph: 'H1' },
        heading2: { label: Locale.t('editorButton.heading2'), shortLabel: 'H2', iconGlyph: 'H2' },
        heading3: { label: Locale.t('editorButton.heading3'), shortLabel: 'H3', iconGlyph: 'H3' },
        paragraph: { label: Locale.t('editorButton.paragraph'), shortLabel: '¶', icon: 'format-paragraph' },
        quote: { label: Locale.t('editorButton.quote'), shortLabel: '"', icon: 'format-quote' },
        code: { label: Locale.t('editorButton.code'), shortLabel: '</>', icon: 'code' },

        // 對齊
        alignLeft: { label: Locale.t('editorButton.alignLeft'), icon: 'format-align-left' },
        alignCenter: { label: Locale.t('editorButton.alignCenter'), icon: 'format-align-center' },
        alignRight: { label: Locale.t('editorButton.alignRight'), icon: 'format-align-right' },
        alignJustify: { label: Locale.t('editorButton.alignJustify'), icon: 'format-align-justify' },

        // 列表
        listBullet: { label: Locale.t('editorButton.listBullet'), icon: 'format-list-bulleted' },
        listNumber: { label: Locale.t('editorButton.listNumber'), icon: 'format-list-numbered' },
        indent: { label: Locale.t('editorButton.indent'), icon: 'arrow-forward' },
        outdent: { label: Locale.t('editorButton.outdent'), icon: 'arrow-back' },

        // 歷史
        undo: { label: Locale.t('editorButton.undo'), icon: 'arrow-back', shortcut: 'Ctrl+Z' },
        redo: { label: Locale.t('editorButton.redo'), icon: 'arrow-forward', shortcut: 'Ctrl+Y' },

        // 插入
        link: { label: Locale.t('editorButton.link'), icon: 'attachment' },
        image: { label: Locale.t('editorButton.image'), icon: 'image' },
        table: { label: Locale.t('editorButton.table'), icon: 'table-chart' },
        line: { label: Locale.t('editorButton.line'), icon: 'horizontal-rule' },
        pageBreak: { label: Locale.t('editorButton.pageBreak'), icon: 'insert-page-break' },

        // 繪圖工具
        pen: { label: Locale.t('editorButton.pen'), icon: 'edit' },
        eraser: { label: Locale.t('editorButton.eraser'), icon: 'delete' },
        lineTool: { label: Locale.t('editorButton.lineTool'), icon: 'horizontal-rule' },
        highlighter: { label: Locale.t('editorButton.highlighter'), icon: 'format-underlined' },
        rect: { label: Locale.t('editorButton.rect'), icon: 'check-box-outline-blank' },
        circle: { label: Locale.t('editorButton.circle'), icon: 'remove-circle' },
        arrow: { label: Locale.t('editorButton.arrow'), icon: 'arrow-forward' },
        text: { label: Locale.t('editorButton.text'), icon: 'title' },
        select: { label: Locale.t('editorButton.select'), icon: 'touch-app' },

        // 測量工具
        measureDistance: { label: Locale.t('editorButton.measureDistance'), icon: 'straighten' },
        measureArea: { label: Locale.t('editorButton.measureArea'), icon: 'domain' },
        coordinate: { label: Locale.t('editorButton.coordinate'), icon: 'place' },

        // 匯出
        exportPdf: { label: Locale.t('editorButton.exportPdf'), shortLabel: 'PDF', icon: 'file' },
        exportWord: { label: Locale.t('editorButton.exportWord'), shortLabel: 'DOC', icon: 'file' },
        exportMarkdown: { label: Locale.t('editorButton.exportMarkdown'), shortLabel: 'MD', icon: 'file' },
        exportPng: { label: Locale.t('editorButton.exportPng'), shortLabel: 'PNG', icon: 'image' },
        exportJson: { label: Locale.t('editorButton.exportJson'), shortLabel: 'JSON', icon: 'file' },

        // 其他
        search: { label: Locale.t('editorButton.search'), icon: 'search', shortcut: 'Ctrl+F' },
        replace: { label: Locale.t('editorButton.replace'), icon: 'refresh', shortcut: 'Ctrl+H' },
        fullscreen: { label: Locale.t('editorButton.fullscreen'), icon: 'fullscreen' },
        clear: { label: Locale.t('editorButton.clear'), icon: 'delete' },
        copy: { label: Locale.t('editorButton.copy'), icon: 'content-copy', shortcut: 'Ctrl+C' },
        paste: { label: Locale.t('editorButton.paste'), icon: 'content-paste', shortcut: 'Ctrl+V' },
        cut: { label: Locale.t('editorButton.cut'), icon: 'content-cut', shortcut: 'Ctrl+X' },
        toc: { label: Locale.t('editorButton.toc'), icon: 'toc' },
        settings: { label: Locale.t('editorButton.settings'), icon: 'settings' },
        layers: { label: Locale.t('editorButton.layers'), icon: 'layers' },
        zoomIn: { label: Locale.t('editorButton.zoomIn'), icon: 'zoom-in' },
        zoomOut: { label: Locale.t('editorButton.zoomOut'), icon: 'zoom-out' },

        // 插入
        insertDrawing: { label: Locale.t('editorButton.insertDrawing'), icon: 'image' },
        insertToc: { label: Locale.t('editorButton.toc'), icon: 'toc' },

        // 版面配置
        header: { label: Locale.t('editorButton.header'), icon: 'vertical-align-top' },
        footer: { label: Locale.t('editorButton.footer'), icon: 'vertical-align-bottom' },
        pageNumber: { label: Locale.t('editorButton.pageNumber'), icon: 'file' },
        margin: { label: Locale.t('editorButton.margin'), icon: 'check-box-outline-blank' },

        // 清除相關
        clearAll: { label: Locale.t('editorButton.clearAll'), icon: 'delete' },
        removeFormat: { label: Locale.t('editorButton.removeFormat'), icon: 'format-clear' },
        generateToc: { label: Locale.t('editorButton.generateToc'), icon: 'toc' },

        // 通用
        custom: { label: '', icon: '' }
    };

    /**
     * 建立編輯器按鈕
     * @param {Object} options - 配置選項
     * @param {string} options.type - 按鈕類型
     * @param {Function} options.onClick - 點擊回調
     * @param {string} options.label - 自訂標籤
     * @param {string} options.iconName - 已註冊的 Canvas 圖示名稱
     * @param {string} options.icon - iconName 的向後相容別名；不再接受 SVG markup
     * @param {string} options.iconPath - CSP-safe Path2D 路徑資料
     * @param {boolean} options.active - 是否啟用狀態
     * @param {boolean} options.disabled - 是否停用
     * @param {string} options.size - 尺寸 (small, medium, large)
     * @param {string} options.variant - 樣式變體 (default, primary, ghost, outline)
     * @param {boolean} options.showLabel - 是否顯示文字標籤
     * @param {boolean} options.iconOnly - 僅顯示圖示
     * @param {string} options.tooltip - 提示文字
     * @param {string} options.theme - 主題 (light, dark, gradient)
     */
    constructor(options = {}) {
        this.options = {
            type: 'custom',
            onClick: null,
            label: null,
            icon: null,
            iconName: null,
            iconPath: null,
            active: false,
            disabled: false,
            size: 'medium',
            variant: 'default',
            showLabel: true,
            iconOnly: false,
            tooltip: null,
            theme: 'light',
            ...options
        };

        this._active = this.options.active;
        this.element = this._createElement();
    }

    get active() {
        return this._active;
    }

    set active(value) {
        this._active = value;
        this._updateActiveState();
    }

    _getSizeStyles() {
        const sizes = {
            small: { padding: '4px 8px', fontSize: 'var(--cl-font-size-sm)', iconSize: '14px', gap: '4px', minWidth: '24px' },
            medium: { padding: '6px 12px', fontSize: 'var(--cl-font-size-md)', iconSize: '16px', gap: '6px', minWidth: '32px' },
            large: { padding: '8px 16px', fontSize: 'var(--cl-font-size-lg)', iconSize: '18px', gap: '8px', minWidth: '40px' }
        };
        return sizes[this.options.size] || sizes.medium;
    }

    _getThemeStyles() {
        const themes = {
            light: {
                bg: 'var(--cl-bg-tertiary)',
                bgHover: 'var(--cl-border-subtle)',
                bgActive: 'var(--cl-border-medium)',
                color: 'var(--cl-text-heading)',
                colorActive: 'var(--cl-text)',
                border: '1px solid var(--cl-border-medium)'
            },
            dark: {
                bg: 'var(--cl-bg-inverse-soft)',
                bgHover: 'var(--cl-bg-inverse-soft-hover)',
                bgActive: 'var(--cl-bg-inverse-muted)',
                color: 'var(--cl-text-inverse)',
                colorActive: 'var(--cl-bg)',
                border: '1px solid var(--cl-divider-inverse)'
            },
            gradient: {
                bg: 'var(--cl-bg-inverse-soft-hover)',
                bgHover: 'var(--cl-bg-inverse-muted)',
                bgActive: 'var(--cl-bg-surface-overlay)',
                color: 'var(--cl-text-inverse)',
                colorActive: 'var(--cl-text)',
                border: 'none'
            }
        };
        return themes[this.options.theme] || themes.light;
    }

    _getVariantStyles(themeStyles) {
        const variants = {
            default: {
                bg: themeStyles.bg,
                bgHover: themeStyles.bgHover,
                border: themeStyles.border
            },
            primary: {
                bg: 'var(--cl-primary)',
                bgHover: 'var(--cl-primary-dark)',
                border: 'none',
                color: 'var(--cl-text-inverse)'
            },
            ghost: {
                bg: 'transparent',
                bgHover: themeStyles.bgHover,
                border: 'none'
            },
            outline: {
                bg: 'transparent',
                bgHover: themeStyles.bg,
                border: themeStyles.border
            }
        };
        return variants[this.options.variant] || variants.default;
    }

    _createElement() {
        const { type, label, icon, iconName, iconPath, disabled, iconOnly, showLabel, tooltip } = this.options;
        const config = EditorButton.CONFIG[type] || EditorButton.CONFIG.custom;
        const sizeStyles = this._getSizeStyles();
        const themeStyles = this._getThemeStyles();
        const variantStyles = this._getVariantStyles(themeStyles);

        const displayLabel = label || config.label || '';
        let displayIcon = iconName || icon || config.icon || '';
        let displayPath = iconPath || '';
        const displayGlyph = config.iconGlyph || '';
        if (typeof displayIcon === 'string' && /<\/?(?:svg|path)\b/i.test(displayIcon)) {
            console.warn('[EditorButton] SVG markup is not supported; use iconName or iconPath.');
            displayIcon = config.icon || 'help';
        }
        if (displayPath && (typeof displayPath !== 'string' || /[<>]/.test(displayPath))) {
            console.warn('[EditorButton] Invalid iconPath; falling back to a registered Canvas icon.');
            displayPath = '';
        }
        if (displayIcon && !displayPath && !Icon.has(displayIcon)) {
            console.warn(`[EditorButton] Unknown icon "${displayIcon}"; falling back to "help".`);
            displayIcon = 'help';
        }
        const tooltipText = tooltip || displayLabel + (config.shortcut ? ` (${config.shortcut})` : '');

        const button = document.createElement('button');
        button.className = `editor-btn editor-btn--${type} editor-btn--${this.options.size} editor-btn--theme-${this.options.theme}`;
        button.setAttribute('type', 'button');
        button.setAttribute('title', tooltipText);

        // 無障礙設計 (A11y)
        button.setAttribute('aria-label', displayLabel);
        button.setAttribute('role', 'button');
        if (this._active) {
            button.setAttribute('aria-pressed', 'true');
            button.classList.add('is-active');
        }
        button.disabled = disabled;
        if (disabled) {
            button.setAttribute('aria-disabled', 'true');
        }

        // 判斷是否只顯示圖示
        const isIconOnlyMode = iconOnly || (!showLabel && (displayIcon || displayPath || displayGlyph));
        if (isIconOnlyMode) {
            button.classList.add('editor-btn--icon-only');
        }

        button.style.cssText = `
            display: inline-flex;
            align-items: center;
            justify-content: center;
            gap: ${sizeStyles.gap};
            padding: ${isIconOnlyMode ? sizeStyles.padding.split(' ')[0] : sizeStyles.padding};
            min-width: ${isIconOnlyMode ? sizeStyles.minWidth : 'auto'};
            min-height: ${sizeStyles.minWidth};
            font-size: ${sizeStyles.fontSize};
            font-weight: 500;
            font-family: inherit;
            border-radius: var(--cl-radius-md);
            cursor: pointer;
            transition: all var(--cl-transition-fast);
            background: ${this._active ? themeStyles.bgActive : variantStyles.bg};
            color: ${variantStyles.color || (this._active ? themeStyles.colorActive : themeStyles.color)};
            border: ${variantStyles.border};
            white-space: nowrap;
            user-select: none;
            ${disabled ? 'opacity: 0.5; cursor: not-allowed;' : ''}
        `;

        // 圖示
        if (displayIcon || displayPath || displayGlyph) {
            const iconWrapper = document.createElement('span');
            iconWrapper.className = 'editor-btn__icon';
            iconWrapper.style.cssText = `
                display: inline-flex;
                align-items: center;
                justify-content: center;
                width: ${sizeStyles.iconSize};
                height: ${sizeStyles.iconSize};
                flex-shrink: 0;
            `;
            this._icon = new Icon({
                name: displayIcon || 'custom',
                pathData: displayPath || null,
                glyph: displayGlyph || null,
                size: Number.parseFloat(sizeStyles.iconSize)
            });
            this._icon.mount(iconWrapper);
            button.appendChild(iconWrapper);
        }

        // 文字標籤
        if (!isIconOnlyMode && displayLabel) {
            const labelSpan = document.createElement('span');
            labelSpan.className = 'editor-btn__label';
            labelSpan.textContent = config.shortLabel || displayLabel;
            button.appendChild(labelSpan);
        }

        // 互動效果
        if (!disabled) {
            button.addEventListener('mouseenter', () => {
                if (!this._active) {
                    button.style.background = variantStyles.bgHover;
                }
            });
            button.addEventListener('mouseleave', () => {
                button.style.background = this._active ? themeStyles.bgActive : variantStyles.bg;
                button.style.color = variantStyles.color || (this._active ? themeStyles.colorActive : themeStyles.color);
                this._icon?.redraw();
            });
            button.addEventListener('click', (e) => {
                if (this.options.onClick) {
                    this.options.onClick(e, this);
                }
            });
        }

        this._button = button;
        this._themeStyles = themeStyles;
        this._variantStyles = variantStyles;

        return button;
    }

    _updateActiveState() {
        if (!this._button) return;
        this._button.style.background = this._active ? this._themeStyles.bgActive : this._variantStyles.bg;
        this._button.style.color = this._variantStyles.color || (this._active ? this._themeStyles.colorActive : this._themeStyles.color);

        // 更新無障礙狀態
        this._button.setAttribute('aria-pressed', this._active ? 'true' : 'false');
        this._button.classList.toggle('is-active', this._active);
        this._icon?.redraw();
    }

    /**
     * 設定停用狀態
     * @param {boolean} disabled - 是否停用
     */
    setDisabled(disabled) {
        this.options.disabled = disabled;
        this._button.disabled = disabled;
        this._button.style.opacity = disabled ? '0.5' : '1';
        this._button.style.cursor = disabled ? 'not-allowed' : 'pointer';
        this._button.setAttribute('aria-disabled', disabled ? 'true' : 'false');
    }

    /**
     * 掛載到容器
     */
    mount(container) {
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (target) {
            target.appendChild(this.element);
        }
        return this;
    }

    /**
     * 銷毀元件
     */
    destroy() {
        this._icon?.destroy();
        if (this.element && this.element.parentNode) {
            this.element.parentNode.removeChild(this.element);
        }
    }
}

export default EditorButton;
