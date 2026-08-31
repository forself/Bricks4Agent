/**
 * 安全性工具函數 — 統一安全原語
 *
 * 提供 XSS 防護、輸入驗證、raw HTML 標記等安全功能。
 * 此模組為 Bricks4Agent 的權威安全來源。
 *
 * @module security
 */

/**
 * HTML 跳脫字元，防止 XSS 攻擊
 * @param {any} str - 原始值
 * @returns {string} 經過跳脫處理的安全字串
 */
export function escapeHtml(str) {
    if (str === null || str === undefined) return '';
    if (typeof str !== 'string') str = String(str);
    return str
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

/**
 * HTML 屬性值跳脫
 * @param {any} str - 原始值
 * @returns {string} 跳脫後的安全字串
 */
export function escapeAttr(str) {
    if (str === null || str === undefined) return '';
    if (typeof str !== 'string') str = String(str);
    return str
        .replace(/&/g, '&amp;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#x27;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;');
}

// opt-in 品牌：JSON.parse 永遠產不出 symbol 鍵，故 API 回傳的純資料無法偽造成 raw() 標記。
// 固定用 Symbol.for 讓不同 bundle/fork 的副本互相辨識；__html 僅為相容保留，不再是授權依據。
const RAW_HTML_BRAND = Symbol.for('bricks4agent.rawHtml');

// 未加品牌的 { __html } 只提示一次，避免在渲染迴圈中洗版
let unbrandedWarned = false;

/**
 * DOM id / 錨點識別碼安全字元驗證。
 * 僅允許 [A-Za-z0-9_-];清洗器與 TOC/錨點產生器共用此單一事實來源。
 * 內容可控且含引號、角括號的 id 會破壞屬性脈絡,於下游把 id 內插進
 * data-id="…" / href="#…" 等屬性時造成屬性跳脫 XSS,故一律以此收斂。
 * @param {any} value - 待驗證的識別碼
 * @returns {boolean} 是否為安全的 id 字元集
 */
const ID_SAFE_RE = /^[A-Za-z0-9_-]+$/;
export function isSafeId(value) {
    return typeof value === 'string' && ID_SAFE_RE.test(value);
}

/**
 * 標記字串為已知安全的 HTML（明確 opt-in）
 * 使用此函數表示「我知道這段 HTML 是安全的」。
 * @param {string} html - 已知安全的 HTML 字串
 * @returns {Readonly<{__html: string}>} raw HTML 標記物件
 */
export function raw(html) {
    const html_ = String(html ?? '');
    return Object.freeze({ [RAW_HTML_BRAND]: html_, __html: html_ });
}

/**
 * 檢查值是否為 raw HTML 標記
 * @param {any} value - 要檢查的值
 * @returns {boolean} 是否為 raw() 產生的標記
 */
export function isRawHtml(value) {
    if (value === null || typeof value !== 'object') return false;
    const hasOwn = Object.prototype.hasOwnProperty;
    // 一律查自身屬性（不用 in）：in 會走原型鏈，原型污染即可讓任意物件冒充標記。
    if (hasOwn.call(value, RAW_HTML_BRAND)
        && hasOwn.call(value, '__html')
        && typeof value.__html === 'string') {
        return true;
    }
    // 未經 raw() 產生的 { __html } 會被當一般值跳脫。這是刻意的（JSON 可偽造），
    // 但靜默失敗難以診斷，故提示一次；不改變回傳值。
    if (!unbrandedWarned && hasOwn.call(value, '__html') && typeof value.__html === 'string') {
        unbrandedWarned = true;
        console.warn('[security] 收到未經 raw() 標記的 { __html } 物件，已當作一般文字跳脫。'
            + '若內容確定安全，請改用 raw(html)；直接傳 { __html } 不再視為 opt-in。');
    }
    return false;
}

/**
 * 安全的模板標籤函數 — 自動跳脫所有插值
 * @example
 * const html = safeHtml`<div>${userName}</div>`;
 */
export function safeHtml(strings, ...values) {
    return strings.reduce((result, str, i) => {
        const value = i < values.length ? escapeHtml(values[i]) : '';
        return result + str + value;
    }, '');
}

/**
 * 檢查是否存在 SQL Injection 風險
 * @param {string} str - 輸入字串
 * @returns {boolean} 若包含風險特徵則返回 true
 */
export function hasSqlInjectionRisk(str) {
    if (typeof str !== 'string') return false;

    const sqlPatterns = [
        /(\s|'|")OR(\s|'|")\d+=\d+/i,
        /(\s|'|")OR(\s|'|")'(\w+)'='(\w+)'/i,
        /;\s*DROP\s+TABLE/i,
        /;\s*DELETE\s+FROM/i,
        /;\s*UPDATE\s+\w+\s+SET/i,
        /--\s/,
        /#\s/,
        /UNION\s+SELECT/i
    ];

    return sqlPatterns.some(pattern => pattern.test(str));
}

/**
 * 檢查是否存在路徑遍歷風險
 * @param {string} str - 輸入字串
 * @returns {boolean} 若包含風險特徵則返回 true
 */
export function hasPathTraversalRisk(str) {
    if (typeof str !== 'string') return false;

    const pathPatterns = [
        /\.\.[/\\]/,
        /%2e%2e[/\\]/i,
        /^[/\\]/,
        /^[a-zA-Z]:[/\\]/
    ];

    return pathPatterns.some(pattern => pattern.test(str));
}

/**
 * 驗證並清理 URL，防止危險協議攻擊
 * 使用白名單方式，只允許安全的 URL 協議
 * @param {string} url - 原始 URL
 * @returns {string} 安全的 URL，若不安全則返回空字串
 */
export function sanitizeUrl(url) {
    if (!url || typeof url !== 'string') return '';

    // 移除控制字元和零寬字元
    const cleaned = url.replace(/[\x00-\x1f\u200b-\u200f\u2028-\u202f\ufeff]/g, '').trim();
    if (!cleaned) return '';

    const lower = cleaned.toLowerCase();

    // 白名單：允許的協議
    const safeProtocols = ['http:', 'https:', 'mailto:', 'tel:'];
    // 允許相對路徑和錨點
    // 阻擋協定相對 URL:除 //host 外,也擋反斜線變體(/\、\/、\\);
    // 瀏覽器會把 \ 正規化為 /,只擋 // 會漏掉 /\evil.com 造成開放重定向。
    if (/^[\\/]{2}/.test(cleaned)) return '';
    if (cleaned.startsWith('/') || cleaned.startsWith('#') || cleaned.startsWith('?')) {
        return cleaned;
    }
    // 檢查是否為含協議的 URL
    const colonIdx = lower.indexOf(':');
    if (colonIdx > 0) {
        const protocol = lower.slice(0, colonIdx + 1);
        if (!safeProtocols.includes(protocol)) {
            return '';
        }
    }

    return cleaned;
}

/**
 * 簡易 HTML 消毒，移除危險標籤與屬性 (DOM-based)
 * @param {string} html - 原始 HTML
 * @returns {string} 消毒後的 HTML
 */
export function sanitizeHTML(html) {
    if (!html) return '';
    // 非瀏覽器環境：跳脫所有 HTML 而非原樣回傳
    if (globalThis.window === undefined || !globalThis.DOMParser) {
        return escapeHtml(html);
    }

    const parser = new DOMParser();
    const doc = parser.parseFromString(html, 'text/html');

    // 白名單標籤(對齊 WebTextEditor 產出:格式化 + 表格 + 連結 + 圖片)
    const allowedTags = new Set([
        'p', 'br', 'b', 'i', 'u', 's', 'strike', 'span', 'div', 'a',
        'img', 'ul', 'ol', 'li', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
        'blockquote', 'pre', 'code', 'font', 'table', 'thead', 'tbody', 'tr', 'th', 'td', 'col', 'colgroup',
        'hr', 'strong', 'em', 'sub', 'sup'
    ]);
    // 危險標籤:直接移除(含內容)
    const dropWithContent = ['script', 'style', 'iframe', 'object', 'embed', 'link', 'meta', 'form', 'input', 'button', 'base', 'svg', 'math', 'template', 'noscript'];
    // 屬性正向白名單(其餘一律移除,含 on*/style)
    const allowedAttrs = new Set(['class', 'id', 'title', 'href', 'src', 'alt', 'width', 'height', 'colspan', 'rowspan', 'span']);
    // class 白名單:富文本 rt-* 群(色階/字級/對齊/行距)+ opacity 工具 + 結構性 class
    // 單一事實來源 = editor/richtext-palette.js ALLOWED_CLASS_PATTERN,此處內聯以保 security.js 自足
    const allowedClassRe = /^(rt-color-([a-z-]+-(50|100|200|300|400|500|600|700|800|900)|default|black|white|transparent)|rt-size-[a-z0-9]+|rt-align-(left|center|right|justify)|rt-lh-(1|15|2|3)|opacity-(0|5|10|20|40|60|80|100)|web-painter-embed|rich-content)$/;

    const safeUrlProtocols = ['http:', 'https:', 'mailto:'];
    // <img> 內的 data:image 不會執行 script,允許內嵌圖片
    // SVG remains active XML when opened directly and is unnecessary for the
    // TIM rich-text/image surfaces. Keep raster data URLs only.
    const imgDataRe = /^data:image\/(png|jpe?g|gif|webp);base64,/i;

    function clean(node) {
        if (node.nodeType === 8) {
            node.remove();
            return;
        }

        if (node.nodeType === 1) {
            const tagName = node.tagName.toLowerCase();

            if (!allowedTags.has(tagName)) {
                if (dropWithContent.includes(tagName)) {
                    node.remove();
                    return;
                } else {
                    while (node.firstChild) {
                        node.parentNode.insertBefore(node.firstChild, node);
                    }
                    node.remove();
                    return;
                }
            }

            Array.from(node.attributes).forEach(attr => {
                const name = attr.name.toLowerCase();

                // 正向白名單:未列者(含 on*、style)一律移除
                if (!allowedAttrs.has(name)) {
                    node.removeAttribute(attr.name);
                    return;
                }

                // class:只留白名單 pattern 內的 class
                if (name === 'class') {
                    const kept = attr.value.split(/\s+/).filter(c => c && allowedClassRe.test(c));
                    if (kept.length) node.setAttribute('class', kept.join(' '));
                    else node.removeAttribute('class');
                    return;
                }

                // id:值必須為安全字元([A-Za-z0-9_-]);否則移除。
                // 白名單放行 id 是為了保留 TOC/錨點跳轉,但含引號、角括號的 id 會在
                // WebTextEditor TOC 產生器(data-id="…"、href="#…")造成屬性跳脫 XSS。
                // 對齊 html-sanitizer.js RICH_TEXT_POLICY 對 id 的收斂策略。
                if (name === 'id') {
                    if (!isSafeId(attr.value)) node.removeAttribute(attr.name);
                    return;
                }

                // href/src:協定白名單;img src 另允許 data:image
                if (name === 'href' || name === 'src') {
                    const cleanValue = attr.value.replace(/[\x00-\x1f]/g, '').trim();
                    const lower = cleanValue.toLowerCase();
                    // 阻擋協定相對 URL(含反斜線變體 /\、\/、\\);只擋 // 會漏開放重定向
                    if (/^[\\/]{2}/.test(cleanValue)) {
                        node.removeAttribute(attr.name);
                        return;
                    }
                    if (name === 'src' && tagName === 'img' && imgDataRe.test(lower)) return;
                    const colonIdx = lower.indexOf(':');
                    if (colonIdx > 0 && !safeUrlProtocols.includes(lower.slice(0, colonIdx + 1))) {
                        node.removeAttribute(attr.name);
                    }
                }
            });
        }

        let child = node.firstChild;
        while (child) {
            const next = child.nextSibling;
            clean(child);
            child = next;
        }
    }

    // 清洗 body 的子節點(不對 body 本身套標籤白名單,否則 body 會被當非白名單標籤拆殼移除)
    Array.from(doc.body.childNodes).forEach(clean);
    return doc.body.innerHTML;
}
