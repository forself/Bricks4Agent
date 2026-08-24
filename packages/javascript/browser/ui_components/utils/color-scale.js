/**
 * color-scale.js — 統計圖表色階引擎(零依賴純函式;node 可直測)。
 *
 * 單一色彩事實來源:editor/richtext-palette.js 的 MATERIAL(16 色相 × 50→900)。
 * 供 Canvas 圖表(Heatmap/Scatter/ClusterGraph…)把「一個資料維度」映射為顏色:
 *   - sequentialScale:單色相深淺(50→900),表量的強度
 *   - divergingScale:冷↔暖(負色相 700 ↔ 中性 ↔ 正色相 700),表偏離中心
 *   - categoricalColor:分類色(高區辨色相序 × 500 階),表離散群
 * 數據色刻意不隨深色主題翻轉(數據語意要穩定);圖表底/文字才用語意 token。
 */
import { MATERIAL, HUE_ORDER, STEPS } from '../editor/richtext-palette.js';

/* ── 基礎:hex ↔ rgb 與插值 ── */

export function hexToRgb(hex) {
    const h = hex.replace('#', '');
    const v = h.length === 3 ? h.split('').map(c => c + c).join('') : h;
    const n = parseInt(v, 16);
    return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

export function rgbToHex([r, g, b]) {
    const c = (x) => Math.round(Math.max(0, Math.min(255, x))).toString(16).padStart(2, '0');
    return '#' + c(r) + c(g) + c(b);
}

/** 兩色線性插值(t∈[0,1])。 */
export function mixHex(hexA, hexB, t) {
    const a = hexToRgb(hexA), b = hexToRgb(hexB);
    return rgbToHex([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t]);
}

/** 在色階陣列(如 blue 的 50..900 十色)上取位置 t∈[0,1] 的插值色。 */
export function sampleRamp(hexStops, t) {
    const n = hexStops.length;
    if (n === 1) return hexStops[0];
    const x = Math.max(0, Math.min(1, t)) * (n - 1);
    const i = Math.min(n - 2, Math.floor(x));
    return mixHex(hexStops[i], hexStops[i + 1], x - i);
}

/* ── 色階工廠 ── */

const hueRamp = (hue) => {
    const m = MATERIAL[hue];
    if (!m) throw new Error(`[color-scale] 未知色相:${hue}(可用:${HUE_ORDER.join(',')})`);
    return STEPS.map(s => m[String(s)]);
};

/**
 * 連續深淺色階(50→900)。
 * @returns {(v:number)=>string} 值→hex;domain 外自動夾擠
 */
export function sequentialScale(hue = 'blue', [min, max] = [0, 1]) {
    const ramp = hueRamp(hue);
    const span = max - min || 1;
    const fn = (v) => sampleRamp(ramp, (v - min) / span);
    fn.legendStops = (n = 6) => Array.from({ length: n }, (_, i) => ({
        t: i / (n - 1), value: min + span * (i / (n - 1)), color: sampleRamp(ramp, i / (n - 1))
    }));
    return fn;
}

/**
 * 冷暖發散色階:negHue-700 ↔ 中性 ↔ posHue-700(mid 預設 domain 中點)。
 * 中性用 grey-100(淺灰;白底深底皆可讀)。
 */
export function divergingScale(negHue = 'blue', posHue = 'red', [min, mid, max] = [-1, 0, 1]) {
    const neg = [MATERIAL[negHue]['700'], MATERIAL[negHue]['400'], MATERIAL.grey['100']];
    const pos = [MATERIAL.grey['100'], MATERIAL[posHue]['400'], MATERIAL[posHue]['700']];
    const lo = mid - min || 1, hi = max - mid || 1;
    const fn = (v) => v <= mid
        ? sampleRamp(neg, (v - min) / lo)
        : sampleRamp(pos, (v - mid) / hi);
    fn.legendStops = (n = 7) => Array.from({ length: n }, (_, i) => {
        const t = i / (n - 1);
        const value = t <= 0.5 ? min + lo * (t * 2) : mid + hi * ((t - 0.5) * 2);
        return { t, value, color: fn(value) };
    });
    return fn;
}

/** 高區辨分類色相序(鄰近索引色相差異大;× 500 階)。 */
export const CATEGORICAL_HUES = [
    'blue', 'red', 'green', 'orange', 'purple', 'teal', 'pink', 'brown',
    'indigo', 'lime', 'deep-orange', 'cyan', 'amber', 'light-green', 'blue-grey', 'grey'
];

/** 第 i 類的分類色(循環;shade 可調 300~700 做同群深淺變化)。 */
export function categoricalColor(i, shade = 500) {
    const hue = CATEGORICAL_HUES[((i % CATEGORICAL_HUES.length) + CATEGORICAL_HUES.length) % CATEGORICAL_HUES.length];
    return MATERIAL[hue][String(shade)];
}

/** 階層群色:頂層群給色相、子層同色相變深淺(level 0=500, 1=300, 2=700…)。 */
export function hierarchicalColor(topIndex, level = 0) {
    const shades = [500, 300, 700, 200, 800];
    return categoricalColor(topIndex, shades[Math.min(level, shades.length - 1)]);
}
