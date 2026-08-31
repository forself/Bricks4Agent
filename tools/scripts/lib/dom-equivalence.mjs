/**
 * dom-equivalence.mjs — 定向更新 vs 完整重建的 DOM 等價比對
 *
 * 為什麼存在：本函式庫沒有虛擬 DOM，元件若要用「定向更新」取代「整批重建」，
 * 就必須自行證明兩條路徑產出的 DOM 無法區分。這個不變式很容易破，且破法隱晦
 * （屬性有同步但 property 沒有、class 被整個覆寫、順序不同…），因此抽成共用工具。
 *
 * innerHTML 比對不夠：它看不到 checked/value 這類 live property，也對 class 與
 * 屬性的順序敏感。serializeDom() 會正規化順序並一併納入表單控制項的實際狀態。
 *
 * 使用環境的兩個陷阱（實測踩過）：
 *   1. tools/scripts/lib/fake-dom.mjs 不解析 innerHTML，所以用 HTML 字串建樹的元件
 *      （DataTable 等）在該環境下沒有可走訪的子節點，序列化會是空的——會造成「空 == 空」
 *      的假通過。這類元件請在真實瀏覽器驗證。
 *   2. python -m http.server 把 .mjs 當 text/plain，瀏覽器會拒絕載入為模組；
 *      瀏覽器驗證時請用會回 application/javascript 的伺服器，或改用 .js 副本。
 *
 * 務必附負向對照：先確認工具在「刻意弄壞」時會報錯，再相信它說的等價。
 *
 * @example
 *   import { assertDomEquivalent } from '../../tools/scripts/lib/dom-equivalence.mjs';
 *   table.selectRow(2);                       // 定向更新路徑
 *   const targeted = table.element;
 *   const rebuilt = buildFreshTableWithSameState();
 *   assertDomEquivalent(targeted, rebuilt, { label: '勾選第 2 列' });
 */

const VOLATILE = [
    // 全域遞增 id（BasePanel 的 panel-N 等）在兩次建構間必然不同，比對前遮蔽
    { pattern: /\bpanel-\d+\b/g, replacement: 'panel-N' },
    { pattern: /\bdx-\d+\b/g, replacement: 'dx-N' },
];

function normalizeText(value) {
    return String(value ?? '').replace(/\s+/g, ' ').trim();
}

function maskVolatile(text, extraMasks) {
    let out = text;
    for (const { pattern, replacement } of [...VOLATILE, ...extraMasks]) {
        out = out.replace(pattern, replacement);
    }
    return out;
}

/**
 * 樣式簽章。只讀 cssText 是不夠的：真實 DOM 會把逐屬性賦值回寫進 cssText，
 * 但兩套 fake DOM 的 style 都是只有 cssText 的普通物件，於是
 * `el.style.background = ...` 對序列化完全隱形——序列化前後字串一模一樣，
 * 比對會沉默通過。EditableTable 正是逐屬性寫法，所以這不是理論問題。
 * 因此兩個來源都收，正規化成排序後的 prop:value 清單。
 */
function styleSignature(style) {
    if (!style) return '';
    const decls = new Map();
    const put = (prop, value) => {
        const name = String(prop).trim().replace(/[A-Z]/g, (c) => '-' + c.toLowerCase());
        const v = String(value ?? '').trim();
        if (name && v) decls.set(name, v);
    };

    if (typeof style.cssText === 'string' && style.cssText) {
        for (const decl of style.cssText.split(';')) {
            const i = decl.indexOf(':');
            if (i > 0) put(decl.slice(0, i), decl.slice(i + 1));
        }
    }

    if (typeof style.length === 'number' && typeof style.item === 'function') {
        // 真實 DOM 的 CSSStyleDeclaration：自有屬性是數字索引，要透過 item() 取名
        for (let i = 0; i < style.length; i += 1) {
            const name = style.item(i);
            if (name) put(name, style.getPropertyValue(name));
        }
    } else {
        // fake DOM：style 是普通物件，逐屬性賦值只會留下自有屬性
        for (const [k, v] of Object.entries(style)) {
            if (k === 'cssText' || typeof v === 'function') continue;
            put(k, v);
        }
    }

    return [...decls].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
        .map(([k, v]) => `${k}: ${v}`).join('; ');
}

/**
 * 把 DOM 子樹序列化成可比對的正規形式。
 * class 與屬性一律排序，避免 classList.toggle 與 className 指派造成的順序差異；
 * 表單控制項另外納入 checked/value/disabled 的「實際狀態」而非只有屬性。
 * @param {Element} root
 * @param {{ masks?: Array<{pattern: RegExp, replacement: string}>, includeProps?: boolean }} [options]
 * @returns {string}
 */
export function serializeDom(root, options = {}) {
    const { masks = [], includeProps = true } = options;
    const lines = [];

    const walk = (node, depth) => {
        if (!node) return;
        const pad = '  '.repeat(depth);

        // 同時支援真實 DOM 與 tools/scripts/lib/fake-dom.mjs：後者的節點沒有 nodeType，
        // 元素以 tagName 判定，子節點存在 children（沒有獨立的 text node）。
        const isElement = node.nodeType === 1 || (node.nodeType === undefined && node.tagName);
        if (!isElement) {
            if (node.nodeType === 3) {
                const text = normalizeText(node.textContent);
                if (text) lines.push(`${pad}#text ${text}`);
            }
            return;
        }

        const tag = String(node.tagName || '').toLowerCase();
        const classes = String(node.className || '').split(/\s+/).filter(Boolean).sort();

        // attributes 在真實 DOM 是 NamedNodeMap，在 fake-dom 是 Map，兩者都要吃
        const attrs = [];
        const bag = node.attributes;
        if (bag instanceof Map) {
            for (const [name, value] of bag) {
                if (name === 'class') continue;
                attrs.push(`${name}=${JSON.stringify(String(value ?? ''))}`);
            }
        } else if (bag && typeof bag.length === 'number') {
            for (let i = 0; i < bag.length; i += 1) {
                const a = bag[i];
                if (!a || !a.name || a.name === 'class') continue;
                attrs.push(`${a.name}=${JSON.stringify(String(a.value ?? ''))}`);
            }
        }
        // dataset 在 fake-dom 是普通物件、不回寫 attributes，需單獨納入；
        // 真實 DOM 的 dataset 由屬性支撐，已在上面收過，靠 seen 去重避免重複輸出。
        const seen = new Set(attrs.map(a => a.slice(0, a.indexOf('='))));
        for (const [k, v] of Object.entries(node.dataset || {})) {
            const name = `data-${k.replace(/[A-Z]/g, (c) => '-' + c.toLowerCase())}`;
            if (seen.has(name)) continue;
            attrs.push(`${name}=${JSON.stringify(String(v ?? ''))}`);
        }
        const style = styleSignature(node.style);
        if (style) attrs.push(`style=${JSON.stringify(style)}`);
        attrs.sort();

        // live property：屬性同步了但 property 沒同步（或反之）是定向更新最常見的漏洞
        const props = [];
        if (includeProps) {
            if (tag === 'input' || tag === 'select' || tag === 'textarea') {
                if (node.checked !== undefined) props.push(`:checked=${Boolean(node.checked)}`);
                if (node.value !== undefined) props.push(`:value=${JSON.stringify(String(node.value ?? ''))}`);
                if (node.disabled !== undefined) props.push(`:disabled=${Boolean(node.disabled)}`);
            }
            if (node.hidden !== undefined && node.hidden) props.push(':hidden=true');
        }

        const head = [tag];
        if (classes.length) head.push(`.${classes.join('.')}`);
        const tail = [...attrs, ...props];
        lines.push(`${pad}<${head.join('')}${tail.length ? ' ' + tail.join(' ') : ''}>`);

        const kids = node.childNodes && node.childNodes.length !== undefined
            ? Array.from(node.childNodes)
            : Array.from(node.children || []);
        if (!kids.length) {
            const text = normalizeText(node.textContent);
            if (text) lines.push(`${pad}  #text ${text}`);
        }
        for (const child of kids) walk(child, depth + 1);
    };

    walk(root, 0);
    return maskVolatile(lines.join('\n'), masks);
}

/**
 * 比對兩棵子樹，回傳第一個差異點（相同則 equal: true）。
 * @param {Element} actual - 定向更新後的樹
 * @param {Element} expected - 完整重建後的樹
 * @param {{ masks?: Array<{pattern: RegExp, replacement: string}>, includeProps?: boolean }} [options]
 * @returns {{ equal: boolean, line?: number, actual?: string, expected?: string, context?: string }}
 */
export function compareDom(actual, expected, options = {}) {
    const a = serializeDom(actual, options).split('\n');
    const b = serializeDom(expected, options).split('\n');
    const max = Math.max(a.length, b.length);

    for (let i = 0; i < max; i += 1) {
        if (a[i] === b[i]) continue;
        const from = Math.max(0, i - 2);
        const context = b.slice(from, i).map(l => `   ${l}`).join('\n');
        return {
            equal: false,
            line: i + 1,
            actual: a[i] === undefined ? '(缺少此節點)' : a[i],
            expected: b[i] === undefined ? '(多出此節點)' : b[i],
            context,
        };
    }
    return { equal: true };
}

/**
 * 斷言定向更新產出的 DOM 與完整重建無法區分；不同則擲出可讀的差異說明。
 * @param {Element} actual
 * @param {Element} expected
 * @param {{ label?: string, masks?: Array<{pattern: RegExp, replacement: string}>, includeProps?: boolean }} [options]
 */
export function assertDomEquivalent(actual, expected, options = {}) {
    const { label = 'DOM 等價' } = options;
    const result = compareDom(actual, expected, options);
    if (result.equal) return;

    const message = [
        `${label}：定向更新與完整重建的結果不一致（第 ${result.line} 行）`,
        result.context ? `  前文：\n${result.context}` : '',
        `  定向更新：${result.actual}`,
        `  完整重建：${result.expected}`,
    ].filter(Boolean).join('\n');

    const error = new Error(message);
    error.name = 'DomEquivalenceError';
    throw error;
}

export default { serializeDom, compareDom, assertDomEquivalent };
