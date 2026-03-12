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
 *     icon: '<svg>...</svg>',
 *     theme: 'gradient',
 *     onClick: () => console.log('clicked')
 * });
 * ```
 */
import Locale from '../../i18n/index.js';


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
            icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M6 4h8a4 4 0 0 1 4 4 4 4 0 0 1-4 4H6z" fill="currentColor"/><path d="M6 12h9a4 4 0 0 1 4 4 4 4 0 0 1-4 4H6z" fill="currentColor"/></svg>',
            shortcut: 'Ctrl+B'
        },
        italic: {
            label: Locale.t('editorButton.italic'),
            shortLabel: 'I',
            icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M10 4h8M6 20h8M14.5 4L9.5 20" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
            shortcut: 'Ctrl+I'
        },
        underline: {
            label: Locale.t('editorButton.underline'),
            shortLabel: 'U',
            icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M6 4v6a6 6 0 1 0 12 0V4" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M4 20h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>',
            shortcut: 'Ctrl+U'
        },
        strikethrough: {
            label: Locale.t('editorButton.strikethrough'),
            shortLabel: 'S',
            icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 12h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M17.5 7.5c0-2-1.5-3.5-4-3.5H9c-2.5 0-4 1.5-4 3.5s1.5 3.5 4 3.5M6.5 16.5c0 2 1.5 3.5 4 3.5h4.5c2.5 0 4-1.5 4-3.5s-1.5-3.5-4-3.5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>'
        },
        subscript: {
            label: Locale.t('editorButton.subscript'),
            shortLabel: 'X₂',
            icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 5L12 19M12 5L4 19" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><text x="16" y="20" font-size="10" fill="currentColor">2</text></svg>'
        },
        superscript: {
            label: Locale.t('editorButton.superscript'),
            shortLabel: 'X²',
            icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 9L12 23M12 9L4 23" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><text x="16" y="10" font-size="10" fill="currentColor">2</text></svg>'
        },

        // 段落格式
        heading1: { label: Locale.t('editorButton.heading1'), shortLabel: 'H1', icon: '<svg viewBox="0 0 24 24" fill="none"><text x="4" y="18" font-size="16" font-weight="bold" fill="currentColor">H1</text></svg>' },
        heading2: { label: Locale.t('editorButton.heading2'), shortLabel: 'H2', icon: '<svg viewBox="0 0 24 24" fill="none"><text x="4" y="18" font-size="14" font-weight="bold" fill="currentColor">H2</text></svg>' },
        heading3: { label: Locale.t('editorButton.heading3'), shortLabel: 'H3', icon: '<svg viewBox="0 0 24 24" fill="none"><text x="4" y="18" font-size="12" font-weight="bold" fill="currentColor">H3</text></svg>' },
        paragraph: { label: Locale.t('editorButton.paragraph'), shortLabel: '¶', icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M13 4h6M13 20V4M17 4v16M9 12a4 4 0 1 1 0-8h4" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        quote: { label: Locale.t('editorButton.quote'), shortLabel: '"', icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M10 8c-2 0-3 1-3 3v5h4v-5H8c0-2 1-3 3-3zM19 8c-2 0-3 1-3 3v5h4v-5h-3c0-2 1-3 3-3z" fill="currentColor"/></svg>' },
        code: { label: Locale.t('editorButton.code'), shortLabel: '</>', icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M8 6L2 12L8 18M16 6L22 12L16 18" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>' },

        // 對齊
        alignLeft: { label: Locale.t('editorButton.alignLeft'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h16M4 10h10M4 14h16M4 18h10" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        alignCenter: { label: Locale.t('editorButton.alignCenter'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h16M7 10h10M4 14h16M7 18h10" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        alignRight: { label: Locale.t('editorButton.alignRight'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h16M10 10h10M4 14h16M10 18h10" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        alignJustify: { label: Locale.t('editorButton.alignJustify'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h16M4 10h16M4 14h16M4 18h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },

        // 列表
        listBullet: { label: Locale.t('editorButton.listBullet'), icon: '<svg viewBox="0 0 24 24" fill="none"><circle cx="5" cy="6" r="2" fill="currentColor"/><circle cx="5" cy="12" r="2" fill="currentColor"/><circle cx="5" cy="18" r="2" fill="currentColor"/><path d="M10 6h10M10 12h10M10 18h10" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        listNumber: { label: Locale.t('editorButton.listNumber'), icon: '<svg viewBox="0 0 24 24" fill="none"><text x="3" y="8" font-size="8" fill="currentColor">1.</text><text x="3" y="14" font-size="8" fill="currentColor">2.</text><text x="3" y="20" font-size="8" fill="currentColor">3.</text><path d="M10 6h10M10 12h10M10 18h10" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        indent: { label: Locale.t('editorButton.indent'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h16M10 10h10M10 14h10M4 18h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M4 10l4 2l-4 2z" fill="currentColor"/></svg>' },
        outdent: { label: Locale.t('editorButton.outdent'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h16M10 10h10M10 14h10M4 18h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M8 10l-4 2l4 2z" fill="currentColor"/></svg>' },

        // 歷史
        undo: { label: Locale.t('editorButton.undo'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 8h12a4 4 0 0 1 0 8H8" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M8 4L4 8l4 4" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>', shortcut: 'Ctrl+Z' },
        redo: { label: Locale.t('editorButton.redo'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M20 8H8a4 4 0 0 0 0 8h8" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M16 4l4 4-4 4" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>', shortcut: 'Ctrl+Y' },

        // 插入
        link: { label: Locale.t('editorButton.link'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        image: { label: Locale.t('editorButton.image'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="3" y="3" width="18" height="18" rx="2" stroke="currentColor" stroke-width="2"/><circle cx="8.5" cy="8.5" r="1.5" fill="currentColor"/><path d="M21 15l-5-5L5 21" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>' },
        table: { label: Locale.t('editorButton.table'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="3" y="3" width="18" height="18" rx="2" stroke="currentColor" stroke-width="2"/><path d="M3 9h18M3 15h18M9 3v18M15 3v18" stroke="currentColor" stroke-width="2"/></svg>' },
        line: { label: Locale.t('editorButton.line'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 12h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        pageBreak: { label: Locale.t('editorButton.pageBreak'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 12h16" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-dasharray="4 2"/><path d="M4 4v5M4 15v5M20 4v5M20 15v5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },

        // 繪圖工具
        pen: { label: Locale.t('editorButton.pen'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M12 19l7-7 3 3-7 7-3-3z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M18 13l-1.5-7.5L2 2l3.5 14.5L13 18l5-5z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M2 2l7.586 7.586" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><circle cx="11" cy="11" r="2" stroke="currentColor" stroke-width="2"/></svg>' },
        eraser: { label: Locale.t('editorButton.eraser'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M20 20H8.5l-5-5a1 1 0 0 1 0-1.4l11-11a1 1 0 0 1 1.4 0l6.1 6.1a1 1 0 0 1 0 1.4L12 20" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/><path d="M6 12l6 6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        lineTool: { label: Locale.t('editorButton.lineTool'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M5 19L19 5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        highlighter: { label: Locale.t('editorButton.highlighter'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M15 3l6 6-10 10H5v-6L15 3z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M5 21h14" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M9 13l6-6" stroke="currentColor" stroke-width="2"/></svg>' },
        rect: { label: Locale.t('editorButton.rect'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="3" y="5" width="18" height="14" rx="2" stroke="currentColor" stroke-width="2"/></svg>' },
        circle: { label: Locale.t('editorButton.circle'), icon: '<svg viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="9" stroke="currentColor" stroke-width="2"/></svg>' },
        arrow: { label: Locale.t('editorButton.arrow'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M5 19L19 5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M19 5v6M19 5h-6" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>' },
        text: { label: Locale.t('editorButton.text'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6V4h16v2M12 4v16M8 20h8" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        select: { label: Locale.t('editorButton.select'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M5 3l14 10-6 1-3 6-5-17z" fill="currentColor"/></svg>' },

        // 測量工具
        measureDistance: { label: Locale.t('editorButton.measureDistance'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 20h16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M4 16v4M20 16v4M8 20v-2M12 20v-3M16 20v-2" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><circle cx="4" cy="6" r="2" stroke="currentColor" stroke-width="2"/><circle cx="20" cy="6" r="2" stroke="currentColor" stroke-width="2"/><path d="M6 6h12" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-dasharray="4 2"/></svg>' },
        measureArea: { label: Locale.t('editorButton.measureArea'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 4l8 4 8-4v12l-8 4-8-4V4z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M4 4l8 8 8-8" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M12 12v8" stroke="currentColor" stroke-width="2"/></svg>' },
        coordinate: { label: Locale.t('editorButton.coordinate'), icon: '<svg viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="3" stroke="currentColor" stroke-width="2"/><path d="M12 2v4M12 18v4M2 12h4M18 12h4" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },

        // 匯出
        exportPdf: { label: Locale.t('editorButton.exportPdf'), shortLabel: 'PDF', icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="4" y="2" width="16" height="20" rx="2" stroke="currentColor" stroke-width="2"/><text x="7" y="15" font-size="7" font-weight="bold" fill="currentColor">PDF</text></svg>' },
        exportWord: { label: Locale.t('editorButton.exportWord'), shortLabel: 'DOC', icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="4" y="2" width="16" height="20" rx="2" stroke="currentColor" stroke-width="2"/><path d="M8 8l2 8 2-6 2 6 2-8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>' },
        exportMarkdown: { label: Locale.t('editorButton.exportMarkdown'), shortLabel: 'MD', icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="2" y="4" width="20" height="16" rx="2" stroke="currentColor" stroke-width="2"/><path d="M5 8v8l3-4 3 4V8M14 16V8l2.5 4 2.5-4v8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>' },
        exportPng: { label: Locale.t('editorButton.exportPng'), shortLabel: 'PNG', icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="3" y="3" width="18" height="18" rx="2" stroke="currentColor" stroke-width="2"/><circle cx="8" cy="8" r="2" fill="currentColor"/><path d="M21 15l-5-5L5 21" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        exportJson: { label: Locale.t('editorButton.exportJson'), shortLabel: 'JSON', icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M8 4c-2 0-3 1-3 3v3c0 1-1 2-2 2 1 0 2 1 2 2v3c0 2 1 3 3 3M16 4c2 0 3 1 3 3v3c0 1 1 2 2 2-1 0-2 1-2 2v3c0 2-1 3-3 3" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },

        // 其他
        search: { label: Locale.t('editorButton.search'), icon: '<svg viewBox="0 0 24 24" fill="none"><circle cx="10" cy="10" r="7" stroke="currentColor" stroke-width="2"/><path d="M15 15l6 6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>', shortcut: 'Ctrl+F' },
        replace: { label: Locale.t('editorButton.replace'), icon: '<svg viewBox="0 0 24 24" fill="none"><circle cx="8" cy="8" r="5" stroke="currentColor" stroke-width="2"/><circle cx="16" cy="16" r="5" stroke="currentColor" stroke-width="2"/><path d="M12 4l4 4-4 4M12 12l4 4-4 4" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/></svg>', shortcut: 'Ctrl+H' },
        fullscreen: { label: Locale.t('editorButton.fullscreen'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 4h6v2H6v4H4V4zM14 4h6v6h-2V6h-4V4zM4 14h2v4h4v2H4v-6zM18 14v4h-4v2h6v-6h-2z" fill="currentColor"/></svg>' },
        clear: { label: Locale.t('editorButton.clear'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h16M6 6v12a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V6M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M10 11v5M14 11v5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        copy: { label: Locale.t('editorButton.copy'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="8" y="8" width="12" height="12" rx="2" stroke="currentColor" stroke-width="2"/><path d="M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2" stroke="currentColor" stroke-width="2"/></svg>', shortcut: 'Ctrl+C' },
        paste: { label: Locale.t('editorButton.paste'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="8" y="4" width="8" height="4" rx="1" stroke="currentColor" stroke-width="2"/><path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" stroke="currentColor" stroke-width="2"/></svg>', shortcut: 'Ctrl+V' },
        cut: { label: Locale.t('editorButton.cut'), icon: '<svg viewBox="0 0 24 24" fill="none"><circle cx="6" cy="6" r="3" stroke="currentColor" stroke-width="2"/><circle cx="6" cy="18" r="3" stroke="currentColor" stroke-width="2"/><path d="M20 4L8.12 15.88M14.47 14.48L20 20M8.12 8.12L12 12" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>', shortcut: 'Ctrl+X' },
        toc: { label: Locale.t('editorButton.toc'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h2M4 12h2M4 18h2M10 6h10M10 12h8M10 18h10" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        settings: { label: Locale.t('editorButton.settings'), icon: '<svg viewBox="0 0 24 24" fill="none"><circle cx="12" cy="12" r="3" stroke="currentColor" stroke-width="2"/><path d="M12 1v4M12 19v4M4.22 4.22l2.83 2.83M16.95 16.95l2.83 2.83M1 12h4M19 12h4M4.22 19.78l2.83-2.83M16.95 7.05l2.83-2.83" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        layers: { label: Locale.t('editorButton.layers'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M12 2L2 7l10 5 10-5-10-5z" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M2 12l10 5 10-5" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/><path d="M2 17l10 5 10-5" stroke="currentColor" stroke-width="2" stroke-linejoin="round"/></svg>' },
        zoomIn: { label: Locale.t('editorButton.zoomIn'), icon: '<svg viewBox="0 0 24 24" fill="none"><circle cx="10" cy="10" r="7" stroke="currentColor" stroke-width="2"/><path d="M15 15l6 6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M10 7v6M7 10h6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        zoomOut: { label: Locale.t('editorButton.zoomOut'), icon: '<svg viewBox="0 0 24 24" fill="none"><circle cx="10" cy="10" r="7" stroke="currentColor" stroke-width="2"/><path d="M15 15l6 6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M7 10h6" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },

        // 插入
        insertDrawing: { label: Locale.t('editorButton.insertDrawing'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="3" y="3" width="18" height="18" rx="2" stroke="currentColor" stroke-width="2"/><path d="M7 15l3-3 3 3 4-4" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/><circle cx="8" cy="9" r="2" fill="currentColor"/></svg>' },
        insertToc: { label: Locale.t('editorButton.toc'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h2M4 12h2M4 18h2M10 6h10M10 12h8M10 18h10" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },

        // 版面配置
        header: { label: Locale.t('editorButton.header'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="3" y="3" width="18" height="18" rx="2" stroke="currentColor" stroke-width="2"/><path d="M3 9h18" stroke="currentColor" stroke-width="2"/><path d="M8 6h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>' },
        footer: { label: Locale.t('editorButton.footer'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="3" y="3" width="18" height="18" rx="2" stroke="currentColor" stroke-width="2"/><path d="M3 15h18" stroke="currentColor" stroke-width="2"/><path d="M8 18h8" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>' },
        pageNumber: { label: Locale.t('editorButton.pageNumber'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="4" y="2" width="16" height="20" rx="2" stroke="currentColor" stroke-width="2"/><text x="12" y="16" text-anchor="middle" font-size="10" fill="currentColor">1</text></svg>' },
        margin: { label: Locale.t('editorButton.margin'), icon: '<svg viewBox="0 0 24 24" fill="none"><rect x="3" y="3" width="18" height="18" rx="1" stroke="currentColor" stroke-width="2"/><rect x="6" y="6" width="12" height="12" rx="1" stroke="currentColor" stroke-width="1.5" stroke-dasharray="3 2"/></svg>' },

        // 清除相關
        clearAll: { label: Locale.t('editorButton.clearAll'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h16M6 6v12a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V6M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M10 11v5M14 11v5" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        removeFormat: { label: Locale.t('editorButton.removeFormat'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M6 4h12M10 4v12M14 4v8" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><path d="M4 20l16-16" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>' },
        generateToc: { label: Locale.t('editorButton.generateToc'), icon: '<svg viewBox="0 0 24 24" fill="none"><path d="M4 6h2M4 12h2M4 18h2M10 6h10M10 12h8M10 18h10" stroke="currentColor" stroke-width="2" stroke-linecap="round"/><circle cx="18" cy="18" r="4" stroke="currentColor" stroke-width="2"/><path d="M18 16v2h2" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg>' },

        // 通用
        custom: { label: '', icon: '' }
    };

    /**
     * 建立編輯器按鈕
     * @param {Object} options - 配置選項
     * @param {string} options.type - 按鈕類型
     * @param {Function} options.onClick - 點擊回調
     * @param {string} options.label - 自訂標籤
     * @param {string} options.icon - 自訂圖示 (SVG 字串)
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

        this.element = this._createElement();
        this._active = this.options.active;
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
        const { type, label, icon, disabled, iconOnly, showLabel, tooltip } = this.options;
        const config = EditorButton.CONFIG[type] || EditorButton.CONFIG.custom;
        const sizeStyles = this._getSizeStyles();
        const themeStyles = this._getThemeStyles();
        const variantStyles = this._getVariantStyles(themeStyles);

        const displayLabel = label || config.label || '';
        const displayIcon = icon || config.icon || '';
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
        const isIconOnlyMode = iconOnly || (!showLabel && displayIcon);
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
        if (displayIcon) {
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
            iconWrapper.innerHTML = displayIcon;
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
        if (this.element && this.element.parentNode) {
            this.element.parentNode.removeChild(this.element);
        }
    }
}

export default EditorButton;
