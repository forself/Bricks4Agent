/**
 * HTML 跳脫字元，防止 XSS 攻擊
 * @param {string} str - 原始字串
 * @returns {string} - 經過跳脫處理的字串
 */
export function escapeHtml(str) {
    if (typeof str !== 'string') return str;
    return str
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#039;');
}

/**
 * 檢查是否存在 SQL Injection 風險
 * @param {string} str - 輸入字串
 * @returns {boolean} - 若包含風險特徵則返回 true
 */
export function hasSqlInjectionRisk(str) {
    if (typeof str !== 'string') return false;

    // 常見 SQL 關鍵字組合風險
    // 注意：這裡採取較寬鬆的檢查以避免誤殺正常文字，但仍需過濾明顯攻擊特徵
    const sqlPatterns = [
        /(\s|'|")OR(\s|'|")\d+=\d+/i,  // OR 1=1
        /(\s|'|")OR(\s|'|")'(\w+)'='(\w+)'/i, // OR 'a'='a'
        /;\s*DROP\s+TABLE/i,  // ; DROP TABLE
        /;\s*DELETE\s+FROM/i, // ; DELETE FROM
        /;\s*UPDATE\s+\w+\s+SET/i, // ; UPDATE SET
        /--\s/, // Comment style 1
        /#\s/,  // Comment style 2
        /UNION\s+SELECT/i // UNION SELECT
    ];

    return sqlPatterns.some(pattern => pattern.test(str));
}

/**
 * 檢查是否存在路徑遍歷 (Path Traversal) 風險
 * @param {string} str - 輸入字串 (通常為檔名或路徑)
 * @returns {boolean} - 若包含風險特徵則返回 true
 */
export function hasPathTraversalRisk(str) {
    if (typeof str !== 'string') return false;

    // 檢查常見路徑遍歷特徵
    const pathPatterns = [
        /\.\.[/\\]/, // ../ or ..\
        /%2e%2e[/\\]/i, // .. url encoded
        /^[/\\]/, // Absolute path start / or \
        /^[a-zA-Z]:[/\\]/ // Windows drive letter C:\
    ];

    return pathPatterns.some(pattern => pattern.test(str));
}

/**
 * 驗證並清理 URL，防止 JavaScript 偽協議攻擊
 * @param {string} url - 原始 URL
 * @returns {string} - 安全的 URL，若不安全則返回空字串
 */
export function sanitizeUrl(url) {
    if (!url) return '';
    const lower = url.toLowerCase().trim();
    // 禁止 javascript: 和 vbscript:
    if (lower.startsWith('javascript:') || lower.startsWith('vbscript:')) {
        return '';
    }
    return url;
}

/**
 * 簡易 HTML 消毒，移除危險標籤與屬性 (DOM-based)
 * @param {string} html - 原始 HTML
 * @returns {string} - 消毒後的 HTML
 */
export function sanitizeHTML(html) {
    if (!html) return '';
    if (globalThis.window === undefined || !globalThis.DOMParser) return html; // Non-browser safeguard

    const parser = new DOMParser();
    const doc = parser.parseFromString(html, 'text/html');
    
    // 允許的標籤白名單
    const allowedTags = new Set([
        'p', 'br', 'b', 'i', 'u', 's', 'span', 'div', 'a', 
        'img', 'ul', 'ol', 'li', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
        'blockquote', 'pre', 'code', 'font', 'table', 'thead', 'tbody', 'tr', 'th', 'td',
        'hr', 'strong', 'em'
    ]);

    // 遞迴清理
    function clean(node) {
        // 1. 移除註釋
        if (node.nodeType === 8) {
            node.remove();
            return;
        }
        
        // 2. 元素檢查
        if (node.nodeType === 1) {
            const tagName = node.tagName.toLowerCase();
            
            // 移除危險標籤
            if (!allowedTags.has(tagName)) {
                // 如果是 script/style/iframe，直接移除節點
                if (['script', 'style', 'iframe', 'object', 'embed', 'link', 'meta'].includes(tagName)) {
                   node.remove();
                   return;
                } else {
                   // 對於未知標籤 (如 custom element)，Unwrap (保留內容但移除標籤)
                   while(node.firstChild) {
                       node.parentNode.insertBefore(node.firstChild, node);
                   }
                   node.remove();
                   return;
                }
            }

            // 檢查屬性
            Array.from(node.attributes).forEach(attr => {
                const name = attr.name.toLowerCase();
                const value = attr.value.toLowerCase();

                // 移除 Event Handlers (on*)
                if (name.startsWith('on')) {
                    node.removeAttribute(name);
                }
                
                // 檢查 URL (href, src)
                if (['href', 'src'].includes(name)) {
                    // 禁止 javascript:
                    if (value.trim().startsWith('javascript:') || value.trim().startsWith('vbscript:')) {
                         node.removeAttribute(name);
                    }
                }
            });
        }

        // 遞迴子節點
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
