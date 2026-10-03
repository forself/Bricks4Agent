/**
 * symbol-paint.js — QR Code 與條碼共用的配色。
 *
 * 掃描器要求「深色模組在淺色底上」。顏色預設取主題 token（深色 --cl-text、淺色 --cl-bg），
 * 但深色主題下兩者亮度相反；ensureContrast 開啟時（預設）依亮度自動對調，
 * 讓模組永遠是兩色中較暗的那個，換膚後仍可掃描。
 */
import { FALLBACK_PAINT } from '../utils/theme-bus.js';

const HEX6 = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i;
const RGB_FN = /^rgba?\(([^)]+)\)$/i;

/** 借用 canvas 把任意 CSS 顏色正規化成 [r, g, b]；無法解析時回傳 null。 */
function toRgb(ctx, color) {
    const prev = ctx.fillStyle;
    ctx.fillStyle = FALLBACK_PAINT;
    ctx.fillStyle = color;
    const normalized = String(ctx.fillStyle);
    ctx.fillStyle = prev;
    let m = HEX6.exec(normalized);
    if (m) return [parseInt(m[1], 16), parseInt(m[2], 16), parseInt(m[3], 16)];
    m = RGB_FN.exec(normalized);
    if (m) return m[1].split(/[\s,/]+/).slice(0, 3).map((s) => parseFloat(s));
    return null;
}

function relativeLuminance([r, g, b]) {
    const channel = (c) => {
        const v = c / 255;
        return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4;
    };
    return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/**
 * 解析模組色與底色。
 * @param {import('./CanvasChart.js').CanvasChart} chart - 用它的 tokens() 解析 token（吃區域覆蓋）
 * @param {CanvasRenderingContext2D} ctx
 * @param {string} darkOption - token 名稱（-- 開頭）或 CSS 顏色
 * @param {string} lightOption - token 名稱（-- 開頭）或 CSS 顏色
 * @param {boolean} [ensureContrast=true]
 * @returns {{dark:string, light:string}}
 */
export function resolveSymbolPaint(chart, ctx, darkOption, lightOption, ensureContrast = true) {
    const isToken = (v) => typeof v === 'string' && v.startsWith('--');
    const names = [darkOption, lightOption].filter(isToken);
    const tok = names.length ? chart.tokens(names) : {};
    const resolve = (v) => (isToken(v) ? (tok[v] || FALLBACK_PAINT) : (v || FALLBACK_PAINT));
    let dark = resolve(darkOption);
    let light = resolve(lightOption);
    if (ensureContrast) {
        const d = toRgb(ctx, dark);
        const l = toRgb(ctx, light);
        if (d && l && relativeLuminance(d) > relativeLuminance(l)) [dark, light] = [light, dark];
    }
    return { dark, light };
}
