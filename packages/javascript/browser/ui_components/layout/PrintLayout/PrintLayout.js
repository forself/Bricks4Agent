/**
 * PrintLayout — 列印版面（容器元件）
 *
 * 把一個區塊整理成可列印的版面（標題、副標題、自訂頁首／頁尾、列印時間），並提供 print()：
 * 只印這個區塊，不動宿主頁面的 DOM。
 *
 * print() 的做法（CSP 合規，無 <style> 注入）：
 * - 在 <html> 標上 data-b4a-printing、根元素標上 data-b4a-print-target，祖先鏈標上
 *   data-b4a-print-ancestor；同目錄 PrintLayout.css 的 @media print 規則據此只印目標區塊。
 * - 紙張大小／方向／邊界以 PrintLayout.css 內固定組合的具名頁面（@page b4a-print-…）表達，
 *   根元素的 data-b4a-page（例如 "a4-portrait-15mm"）選擇要用的頁面。
 * - 標記在 afterprint 時移除；瀏覽器沒送 afterprint 時由備援計時器（CLEANUP_FALLBACK_MS）移除。
 * - PrintLayout.css 以同源 <link rel="stylesheet"> 載入（比照 layout/DataTable），所有實例共用
 *   一個 link 並引用計數，最後一個實例 destroy 才移除。
 *
 * @example
 * const layout = new PrintLayout({
 *     title: '會議室使用月報',
 *     subtitle: '2026 年 9 月',
 *     preview: true,
 *     printButton: true
 * }).mount('#report');
 * table.mount(layout.getContentElement());
 */
import Locale from '../../i18n/index.js';
import { nextUid } from '../../utils/uid.js';
import { Icon } from '../../common/Icon/index.js';
import './locale.js';

const STYLE_LINK_ID = 'b4a-print-layout-styles';

// 紙張尺寸（mm，直向寬 × 高）
const PAGE_SIZES = {
    A4: [210, 297],
    A3: [297, 420],
    Letter: [215.9, 279.4],
    Legal: [215.9, 355.6]
};
const ORIENTATIONS = Object.freeze(['portrait', 'landscape']);
// 只支援 PrintLayout.css 內有對應具名頁面的邊界
const MARGINS = Object.freeze(['0', '10mm', '15mm', '20mm', '25mm']);

const ROOT_CSS = 'display:block;box-sizing:border-box;min-width:0;font-family:var(--cl-font-family);color:var(--cl-text);';
const TOOLBAR_CSS = 'display:flex;justify-content:flex-end;gap:8px;margin:0 0 12px;';
const PRINT_BUTTON_CSS = 'display:inline-flex;align-items:center;gap:6px;padding:6px 14px;border:1px solid var(--cl-border);border-radius:var(--cl-radius-md);background:var(--cl-bg);color:var(--cl-text);font:inherit;font-size:var(--cl-font-size-md);cursor:pointer;';
const SHEET_CSS = 'display:block;box-sizing:border-box;min-width:0;';
const HEADER_CSS = 'display:block;margin:0 0 12px;padding:0 0 8px;border-bottom:1px solid var(--cl-border);';
const HEADER_TOP_CSS = 'display:flex;flex-wrap:wrap;align-items:flex-start;justify-content:space-between;gap:4px 16px;';
const TITLE_CSS = 'margin:0;font-size:var(--cl-font-size-2xl);font-weight:700;color:var(--cl-text);overflow-wrap:anywhere;';
const SUBTITLE_CSS = 'margin:4px 0 0;font-size:var(--cl-font-size-md);color:var(--cl-text-secondary);overflow-wrap:anywhere;';
const PRINTED_AT_CSS = 'flex:0 0 auto;margin:0;font-size:var(--cl-font-size-sm);color:var(--cl-text-muted);white-space:nowrap;';
const HEADER_EXTRA_CSS = 'margin:8px 0 0;';
const CONTENT_CSS = 'display:block;min-width:0;';
const FOOTER_CSS = 'display:block;margin:16px 0 0;padding:8px 0 0;border-top:1px solid var(--cl-border);font-size:var(--cl-font-size-sm);color:var(--cl-text-secondary);';

let styleRefCount = 0;
let styleLinkOwned = false;
let activePrintLayout = null;

function ensureStyleLink() {
    if (typeof document === 'undefined' || document.getElementById(STYLE_LINK_ID)) return;
    const link = document.createElement('link');
    link.id = STYLE_LINK_ID;
    link.rel = 'stylesheet';
    link.href = new URL('./PrintLayout.css', import.meta.url).href;
    (document.head || document.documentElement).appendChild(link);
    styleLinkOwned = true;
}

function acquireStyleSheet() {
    styleRefCount += 1;
    ensureStyleLink();
}

/** 最後一個實例釋放時才移除；宿主頁面自己放的同 id link 不移除 */
function releaseStyleSheet() {
    styleRefCount = Math.max(0, styleRefCount - 1);
    if (styleRefCount > 0 || typeof document === 'undefined') return;
    const link = document.getElementById(STYLE_LINK_ID);
    if (link && styleLinkOwned) link.remove();
    styleLinkOwned = false;
}

const timeFormatterCache = new Map();

function formatDateTime(date, lang) {
    if (!timeFormatterCache.has(lang)) {
        const style = { dateStyle: 'medium', timeStyle: 'short' };
        let formatter;
        try {
            formatter = new Intl.DateTimeFormat(lang, style);
        } catch {
            formatter = new Intl.DateTimeFormat(undefined, style);
        }
        timeFormatterCache.set(lang, formatter);
    }
    return timeFormatterCache.get(lang).format(date);
}

function normalizePageSize(value) {
    const wanted = String(value ?? '').trim().toLowerCase();
    return Object.keys(PAGE_SIZES).find((size) => size.toLowerCase() === wanted) || null;
}

function normalizeOrientation(value) {
    const wanted = String(value ?? '').trim().toLowerCase();
    return ORIENTATIONS.includes(wanted) ? wanted : null;
}

function normalizeMargin(value) {
    if (value === 0) return '0';
    const wanted = String(value ?? '').trim().toLowerCase().replace(/\s+/g, '');
    if (wanted === '0' || wanted === '0mm' || wanted === 'none') return '0';
    return MARGINS.includes(wanted) ? wanted : null;
}

function hasContent(value) {
    return value !== null && value !== undefined && value !== false && value !== '';
}

/**
 * Node 原樣使用；已建構的 B4A 元件取其根元素；其他一律當文字，不解析 HTML。
 * （模組層級函式刻意不寫成「四格縮排 + name(...) {」，以免被 metadata 擷取器誤認成公開方法）
 */
function toNode(value) {
    const NodeType = typeof Node === 'undefined' ? null : Node;
    if (NodeType && value instanceof NodeType) return value;
    if (NodeType && value && value.element instanceof NodeType) return value.element;
    return document.createTextNode(String(value));
}

function appendContent(target, value) {
    if (hasContent(value)) target.appendChild(toNode(value));
}

export class PrintLayout {
    static PAGE_SIZES = Object.freeze(Object.keys(PAGE_SIZES));
    static ORIENTATIONS = ORIENTATIONS;
    static MARGINS = MARGINS;
    /** 瀏覽器沒送 afterprint 時，print() 返回後多久移除列印標記（ms） */
    static CLEANUP_FALLBACK_MS = 60000;

    constructor(options = {}) {
        this.options = {
            title: '',               // 標題（h2）
            subtitle: '',            // 副標題
            header: null,            // 標題下方的自訂頁首：Node、B4A 元件（有 .element）或文字
            footer: null,            // 頁尾：Node、B4A 元件或文字
            content: null,           // 主要內容：Node、B4A 元件或文字；也可自行掛到 getContentElement()
            pageSize: 'A4',          // 'A4' | 'A3' | 'Letter' | 'Legal'
            orientation: 'portrait', // 'portrait' | 'landscape'
            margin: '15mm',          // '0' | '10mm' | '15mm' | '20mm' | '25mm'（PrintLayout.css 的固定組合）
            showPrintedAt: true,     // 顯示列印時間（Locale 格式；print() 時更新；非預覽的螢幕畫面由 PrintLayout.css 隱藏）
            preview: false,          // 螢幕上以實際紙張寬度、內距與陰影預覽
            printButton: false,      // 顯示「列印」按鈕（呼叫 print()）
            beforePrint: null,       // (layout) => void | false；回傳 false 取消這次列印
            afterPrint: null,        // (layout) => void；列印結束（afterprint 或備援計時器）後呼叫
            ...options
        };

        this.element = null;
        this._uid = nextUid('print-layout');
        this._destroyed = false;
        this._printing = false;
        this._markedAncestors = [];
        this._fallbackTimer = null;
        this._localeListening = false;
        this._printedAt = new Date();
        this._page = this._resolvePage();

        this._onAfterPrint = () => this._endPrint('afterprint');
        this._onPrintClick = () => this.print();
        this._onLocaleChange = () => this._handleLocaleChange();

        acquireStyleSheet();
        this._styleAcquired = true;
        this.element = this._createElement();
        this._renderHeader();
        this._renderFooter();
        this.setContent(this.options.content);
    }

    // ── 設定正規化 ────────────────────────────────────────

    _resolvePage() {
        const size = normalizePageSize(this.options.pageSize);
        const orientation = normalizeOrientation(this.options.orientation);
        const margin = normalizeMargin(this.options.margin);
        if (!size) console.warn(`[PrintLayout] 不支援的 pageSize "${this.options.pageSize}"，改用 A4`);
        if (!orientation) console.warn(`[PrintLayout] 不支援的 orientation "${this.options.orientation}"，改用 portrait`);
        if (!margin) console.warn(`[PrintLayout] 不支援的 margin "${this.options.margin}"，改用 15mm（可用值：${MARGINS.join(', ')}）`);
        const page = {
            size: size || 'A4',
            orientation: orientation || 'portrait',
            margin: margin || '15mm'
        };
        page.key = `${page.size.toLowerCase()}-${page.orientation}-${page.margin}`;
        return page;
    }

    // ── 建構與渲染 ────────────────────────────────────────

    _createElement() {
        const { size, orientation, margin, key } = this._page;
        const root = document.createElement('div');
        root.className = 'b4a-print-layout';
        root.dataset.pageSize = size;
        root.dataset.orientation = orientation;
        root.dataset.margin = margin;
        root.setAttribute('data-b4a-page', key);
        root.style.cssText = ROOT_CSS;

        this._printButton = null;
        this._printLabel = null;
        if (this.options.printButton) {
            const toolbar = document.createElement('div');
            toolbar.className = 'b4a-print-layout__toolbar';
            toolbar.style.cssText = TOOLBAR_CSS;
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'b4a-print-layout__print-button';
            button.style.cssText = PRINT_BUTTON_CSS;
            const iconHost = document.createElement('span');
            iconHost.setAttribute('aria-hidden', 'true');
            iconHost.style.cssText = 'display:inline-flex;';
            this._printIcon = new Icon({ name: 'print', size: 16, color: 'currentColor' });
            this._printIcon.mount(iconHost);
            const label = document.createElement('span');
            label.className = 'b4a-print-layout__print-label';
            label.textContent = Locale.t('printLayout.print');
            button.append(iconHost, label);
            button.addEventListener('click', this._onPrintClick);
            toolbar.appendChild(button);
            root.appendChild(toolbar);
            this._printButton = button;
            this._printLabel = label;
        }

        const sheet = document.createElement('div');
        sheet.className = 'b4a-print-layout__sheet';
        sheet.style.cssText = SHEET_CSS;

        const header = document.createElement('header');
        header.className = 'b4a-print-layout__header';
        header.style.cssText = HEADER_CSS;

        const content = document.createElement('div');
        content.className = 'b4a-print-layout__content';
        content.style.cssText = CONTENT_CSS;

        const footer = document.createElement('footer');
        footer.className = 'b4a-print-layout__footer';
        footer.style.cssText = FOOTER_CSS;

        sheet.append(header, content, footer);
        root.appendChild(sheet);
        this._sheet = sheet;
        this._header = header;
        this._content = content;
        this._footer = footer;
        this._applyPreview(root, sheet);
        return root;
    }

    _applyPreview(root, sheet) {
        const preview = !!this.options.preview;
        root.dataset.preview = String(preview);
        if (!preview) return;
        const { size, orientation, margin } = this._page;
        const [shortSide, longSide] = PAGE_SIZES[size];
        const width = orientation === 'landscape' ? longSide : shortSide;
        const height = orientation === 'landscape' ? shortSide : longSide;
        root.style.background = 'var(--cl-bg-secondary)';
        root.style.padding = '16px';
        root.style.overflowX = 'auto';
        sheet.style.width = `${width}mm`;
        sheet.style.minHeight = `${height}mm`;
        sheet.style.padding = margin === '0' ? '0' : margin;
        sheet.style.margin = '0 auto';
        sheet.style.background = 'var(--cl-bg)';
        sheet.style.boxShadow = 'var(--cl-shadow-md)';
    }

    _renderHeader() {
        const header = this._header;
        if (!header) return;
        const { title, subtitle, header: extra, showPrintedAt } = this.options;
        const titleText = hasContent(title) ? String(title) : '';
        const subtitleText = hasContent(subtitle) ? String(subtitle) : '';
        header.replaceChildren();
        this._titleElement = null;
        this._printedAtElement = null;

        if (!titleText && !subtitleText && !hasContent(extra) && !showPrintedAt) {
            header.hidden = true;
            header.style.display = 'none';
            return;
        }
        header.hidden = false;
        header.style.display = 'block';

        const top = document.createElement('div');
        top.className = 'b4a-print-layout__header-top';
        top.style.cssText = HEADER_TOP_CSS;
        const titles = document.createElement('div');
        titles.className = 'b4a-print-layout__titles';
        titles.style.cssText = 'min-width:0;';
        if (titleText) {
            const heading = document.createElement('h2');
            heading.className = 'b4a-print-layout__title';
            heading.id = `${this._uid}-title`;
            heading.style.cssText = TITLE_CSS;
            heading.textContent = titleText;
            titles.appendChild(heading);
            this._titleElement = heading;
        }
        if (subtitleText) {
            const sub = document.createElement('p');
            sub.className = 'b4a-print-layout__subtitle';
            sub.style.cssText = SUBTITLE_CSS;
            sub.textContent = subtitleText;
            titles.appendChild(sub);
        }
        top.appendChild(titles);
        if (showPrintedAt) {
            const printedAt = document.createElement('p');
            printedAt.className = 'b4a-print-layout__printed-at';
            printedAt.style.cssText = PRINTED_AT_CSS;
            printedAt.textContent = Locale.t('printLayout.printedAt', {
                time: formatDateTime(this._printedAt, Locale.getLang())
            });
            top.appendChild(printedAt);
            this._printedAtElement = printedAt;
        }
        header.appendChild(top);

        if (hasContent(extra)) {
            const extraHost = document.createElement('div');
            extraHost.className = 'b4a-print-layout__header-extra';
            extraHost.style.cssText = HEADER_EXTRA_CSS;
            appendContent(extraHost, extra);
            header.appendChild(extraHost);
        }
    }

    _renderFooter() {
        const footer = this._footer;
        if (!footer) return;
        footer.replaceChildren();
        const has = hasContent(this.options.footer);
        footer.hidden = !has;
        footer.style.display = has ? 'block' : 'none';
        if (has) appendContent(footer, this.options.footer);
    }

    _handleLocaleChange() {
        if (this._destroyed) return;
        if (this._printLabel) this._printLabel.textContent = Locale.t('printLayout.print');
        this._renderHeader();
    }

    // ── 列印流程 ──────────────────────────────────────────

    _beginPrint() {
        const doc = this.element.ownerDocument;
        doc.documentElement.setAttribute('data-b4a-printing', this._uid);
        this.element.setAttribute('data-b4a-print-target', '');
        this._markedAncestors = [];
        for (let node = this.element.parentElement; node && node !== doc.documentElement; node = node.parentElement) {
            node.setAttribute('data-b4a-print-ancestor', '');
            this._markedAncestors.push(node);
        }
        window.addEventListener('afterprint', this._onAfterPrint);
        this._printing = true;
        activePrintLayout = this;
    }

    /** reason：afterprint｜fallback｜superseded｜restart｜error｜destroy（destroy 不呼叫 afterPrint） */
    _endPrint(reason) {
        if (!this._printing) return;
        this._printing = false;
        window.removeEventListener('afterprint', this._onAfterPrint);
        if (this._fallbackTimer !== null) {
            clearTimeout(this._fallbackTimer);
            this._fallbackTimer = null;
        }
        const root = (this.element?.ownerDocument || document).documentElement;
        if (root.getAttribute('data-b4a-printing') === this._uid) root.removeAttribute('data-b4a-printing');
        this.element?.removeAttribute('data-b4a-print-target');
        for (const node of this._markedAncestors) node.removeAttribute('data-b4a-print-ancestor');
        this._markedAncestors = [];
        if (activePrintLayout === this) activePrintLayout = null;
        if (reason !== 'destroy' && typeof this.options.afterPrint === 'function') this.options.afterPrint(this);
    }

    // ── 公開 API ─────────────────────────────────────────

    /**
     * 只列印這個版面。需要先 mount 到文件中。
     * window.print() 在多數桌面瀏覽器會阻塞到列印對話框關閉並送出 afterprint；
     * 沒送 afterprint 時，CLEANUP_FALLBACK_MS 後由備援計時器移除標記。
     */
    print() {
        if (this._destroyed || typeof window === 'undefined' || typeof window.print !== 'function') return this;
        if (!this.element.isConnected) {
            console.warn('[PrintLayout] print() 前請先 mount 到文件中');
            return this;
        }
        if (typeof this.options.beforePrint === 'function' && this.options.beforePrint(this) === false) return this;
        // beforePrint 是呼叫端程式碼：可能已 destroy 或把版面移出文件
        if (this._destroyed || !this.element.isConnected) return this;
        if (activePrintLayout && activePrintLayout !== this) activePrintLayout._endPrint('superseded');
        if (this._printing) this._endPrint('restart');
        ensureStyleLink();
        this._printedAt = new Date();
        this._renderHeader();
        this._beginPrint();
        try {
            window.print();
        } catch (error) {
            this._endPrint('error');
            throw error;
        }
        if (this._printing) {
            this._fallbackTimer = setTimeout(() => this._endPrint('fallback'), PrintLayout.CLEANUP_FALLBACK_MS);
        }
        return this;
    }

    /** 以 Node、B4A 元件或文字取代主要內容；null 清空 */
    setContent(node) {
        if (this._destroyed) return this;
        this.options.content = node;
        this._content.replaceChildren();
        appendContent(this._content, node);
        return this;
    }

    /** 主要內容容器：可直接把其他元件 mount 進來 */
    getContentElement() {
        return this._content || null;
    }

    /** 更新標題；空字串移除標題 */
    setTitle(title) {
        if (this._destroyed) return this;
        this.options.title = title;
        this._renderHeader();
        return this;
    }

    mount(container) {
        if (this._destroyed) return this;
        const target = typeof container === 'string' ? document.querySelector(container) : container;
        if (!target) {
            console.warn('[PrintLayout] mount target not found:', container);
            return this;
        }
        target.appendChild(this.element);
        if (!this._localeListening && typeof window !== 'undefined') {
            window.addEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = true;
        }
        return this;
    }

    destroy() {
        if (this._destroyed) return;
        this._endPrint('destroy');
        this._destroyed = true;
        if (this._localeListening) {
            window.removeEventListener('locale-changed', this._onLocaleChange);
            this._localeListening = false;
        }
        this._printButton?.removeEventListener('click', this._onPrintClick);
        this._printIcon?.destroy();
        this._printIcon = null;
        this.element?.remove();
        if (this._styleAcquired) {
            this._styleAcquired = false;
            releaseStyleSheet();
        }
    }
}

export default PrintLayout;
