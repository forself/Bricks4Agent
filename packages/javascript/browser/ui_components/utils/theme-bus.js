/**
 * theme-bus.js — 主題事件匯流排(零依賴)。
 *
 * 為什麼存在:Canvas 畫下去就是像素,不會像 DOM/CSS 那樣自動響應 var(--cl-*) 變化。
 * 全庫 Canvas 化後,「換膚即時生效」(Theme Studio 調 token、data-theme 深色切換)
 * 需要一個重繪時機來源——就是本模組。
 *
 * 偵測機制:MutationObserver 監看 <html> 的 data-theme / style / class 屬性
 * (Theme Studio 的 root.style.setProperty 會觸發 style 屬性變異,天然被捕捉,
 * 無需任何整合程式碼)。動態載入 theme.custom.css 這類「不動屬性」的變更,
 * 呼叫 notifyThemeChange() 手動廣播。通知以 rAF 合併(同幀多次變更只重繪一次)。
 *
 * 用法(CanvasChart 基底已內建,一般不需直接使用):
 *   import { onThemeChange, resolveTokens } from '../utils/theme-bus.js';
 *   const off = onThemeChange(() => this.render());   // 回傳解除函式,destroy 時呼叫
 *   const { '--cl-primary': primary } = resolveTokens(['--cl-primary']);
 */

const subs = new Set();
let scheduled = false;
let observer = null;

function ensureObserver() {
    if (observer || typeof MutationObserver === 'undefined' || typeof document === 'undefined') return;
    observer = new MutationObserver(() => schedule());
    observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['data-theme', 'style', 'class']
    });
}

function schedule() {
    if (scheduled || typeof requestAnimationFrame === 'undefined') return;
    scheduled = true;
    requestAnimationFrame(() => {
        scheduled = false;
        for (const fn of subs) {
            try { fn(); } catch (e) { console.error('[theme-bus] 訂閱者拋錯:', e); }
        }
    });
}

/** 訂閱主題變更;回傳解除訂閱函式(元件 destroy 時務必呼叫)。 */
export function onThemeChange(fn) {
    ensureObserver();
    subs.add(fn);
    return () => subs.delete(fn);
}

/** 手動廣播(屬性變異偵測不到的情境用,如動態載入樣式表後)。 */
export function notifyThemeChange() {
    schedule();
}

/**
 * 解析 token 目前的實際值(Canvas 繪製前呼叫;逐次解析確保吃到最新主題)。
 * @param {string[]} names - 如 ['--cl-primary', '--cl-bg']
 * @param {Element} [el] - 解析基準元素(預設 documentElement;放元件根可吃區域覆蓋)
 * @returns {Object} name → 計算值(trim 後;未定義為空字串)
 */
export function resolveTokens(names, el = document.documentElement) {
    const cs = getComputedStyle(el);
    const out = {};
    for (const n of names) out[n] = cs.getPropertyValue(n).trim();
    return out;
}
