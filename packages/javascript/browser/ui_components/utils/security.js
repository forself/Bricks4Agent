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
        .replaceAll("'", '&#039;');
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

/**
 * 標記字串為已知安全的 HTML（明確 opt-in）
 * 使用此函數表示「我知道這段 HTML 是安全的」。
 * @param {string} html - 已知安全的 HTML 字串
 * @returns {Readonly<{__html: string}>} raw HTML 標記物件
 */
export function raw(html) {
    return Object.freeze({ __html: String(html ?? '') });
}

/**
 * 檢查值是否為 raw HTML 標記
 * @param {any} value - 要檢查的值
 * @returns {boolean} 是否為 raw() 產生的標記
 */
export function isRawHtml(value) {
    return value !== null && typeof value === 'object' && '__html' in value;
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

    const allowedTags = new Set([
        'p', 'br', 'b', 'i', 'u', 's', 'span', 'div', 'a',
        'img', 'ul', 'ol', 'li', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
        'blockquote', 'pre', 'code', 'font', 'table', 'thead', 'tbody', 'tr', 'th', 'td',
        'hr', 'strong', 'em'
    ]);

    const safeUrlProtocols = ['http:', 'https:', 'mailto:'];

    function clean(node) {
        if (node.nodeType === 8) {
            node.remove();
            return;
        }

        if (node.nodeType === 1) {
            const tagName = node.tagName.toLowerCase();

            if (!allowedTags.has(tagName)) {
                if (['script', 'style', 'iframe', 'object', 'embed', 'link', 'meta'].includes(tagName)) {
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
                const valueLower = attr.value.toLowerCase().trim();

                if (name.startsWith('on')) {
                    node.removeAttribute(name);
                    return;
                }

                if (name === 'style') {
                    node.removeAttribute(name);
                    return;
                }

                if (['href', 'src'].includes(name)) {
                    const cleanValue = valueLower.replace(/[\x00-\x1f]/g, '');
                    const colonIdx = cleanValue.indexOf(':');
                    if (colonIdx > 0) {
                        const protocol = cleanValue.slice(0, colonIdx + 1);
                        if (!safeUrlProtocols.includes(protocol)) {
                            node.removeAttribute(name);
                        }
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

    clean(doc.body);
    return doc.body.innerHTML;
}
