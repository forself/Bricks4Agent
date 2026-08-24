/**
 * richtext-palette — 全站共用色彩系統 + 富文本樣式白名單(顏色/字級/對齊/行距/透明度)
 *
 * 設計原則:樣式一律以 Bricks4Agent 全域 CSS class / --cl-* token 表達,禁止自由 inline CSS。
 * - 顏色 = Material 級完整色階(16 色相 × 50→900)+ 灰階 + 黑/白 + transparent;
 *   選色可用光譜/色票,但存檔吸附到「最相近的顏色 class」。
 * - 透明 = 一組固定 opacity 工具階(可套任意元素)+ rt-color-transparent。
 * - 這份資料是單一事實來源:driver 生成 palette.css(token + class),也供編輯器 normalizer 吸附。
 */

// Material Design 色階(絕對 hex,不隨主題變;語意 token 如 --cl-primary 才隨主題)
export const MATERIAL = {
    red: { 50: '#FFEBEE', 100: '#FFCDD2', 200: '#EF9A9A', 300: '#E57373', 400: '#EF5350', 500: '#F44336', 600: '#E53935', 700: '#D32F2F', 800: '#C62828', 900: '#B71C1C' },
    pink: { 50: '#FCE4EC', 100: '#F8BBD0', 200: '#F48FB1', 300: '#F06292', 400: '#EC407A', 500: '#E91E63', 600: '#D81B60', 700: '#C2185B', 800: '#AD1457', 900: '#880E4F' },
    purple: { 50: '#F3E5F5', 100: '#E1BEE7', 200: '#CE93D8', 300: '#BA68C8', 400: '#AB47BC', 500: '#9C27B0', 600: '#8E24AA', 700: '#7B1FA2', 800: '#6A1B9A', 900: '#4A148C' },
    indigo: { 50: '#E8EAF6', 100: '#C5CAE9', 200: '#9FA8DA', 300: '#7986CB', 400: '#5C6BC0', 500: '#3F51B5', 600: '#3949AB', 700: '#303F9F', 800: '#283593', 900: '#1A237E' },
    blue: { 50: '#E3F2FD', 100: '#BBDEFB', 200: '#90CAF9', 300: '#64B5F6', 400: '#42A5F5', 500: '#2196F3', 600: '#1E88E5', 700: '#1976D2', 800: '#1565C0', 900: '#0D47A1' },
    cyan: { 50: '#E0F7FA', 100: '#B2EBF2', 200: '#80DEEA', 300: '#4DD0E1', 400: '#26C6DA', 500: '#00BCD4', 600: '#00ACC1', 700: '#0097A7', 800: '#00838F', 900: '#006064' },
    teal: { 50: '#E0F2F1', 100: '#B2DFDB', 200: '#80CBC4', 300: '#4DB6AC', 400: '#26A69A', 500: '#009688', 600: '#00897B', 700: '#00796B', 800: '#00695C', 900: '#004D40' },
    green: { 50: '#E8F5E9', 100: '#C8E6C9', 200: '#A5D6A7', 300: '#81C784', 400: '#66BB6A', 500: '#4CAF50', 600: '#43A047', 700: '#388E3C', 800: '#2E7D32', 900: '#1B5E20' },
    'light-green': { 50: '#F1F8E9', 100: '#DCEDC8', 200: '#C5E1A5', 300: '#AED581', 400: '#9CCC65', 500: '#8BC34A', 600: '#7CB342', 700: '#689F38', 800: '#558B2F', 900: '#33691E' },
    lime: { 50: '#F9FBE7', 100: '#F0F4C3', 200: '#E6EE9C', 300: '#DCE775', 400: '#D4E157', 500: '#CDDC39', 600: '#C0CA33', 700: '#AFB42B', 800: '#9E9D24', 900: '#827717' },
    amber: { 50: '#FFF8E1', 100: '#FFECB3', 200: '#FFE082', 300: '#FFD54F', 400: '#FFCA28', 500: '#FFC107', 600: '#FFB300', 700: '#FFA000', 800: '#FF8F00', 900: '#FF6F00' },
    orange: { 50: '#FFF3E0', 100: '#FFE0B2', 200: '#FFCC80', 300: '#FFB74D', 400: '#FFA726', 500: '#FF9800', 600: '#FB8C00', 700: '#F57C00', 800: '#EF6C00', 900: '#E65100' },
    'deep-orange': { 50: '#FBE9E7', 100: '#FFCCBC', 200: '#FFAB91', 300: '#FF8A65', 400: '#FF7043', 500: '#FF5722', 600: '#F4511E', 700: '#E64A19', 800: '#D84315', 900: '#BF360C' },
    brown: { 50: '#EFEBE9', 100: '#D7CCC8', 200: '#BCAAA4', 300: '#A1887F', 400: '#8D6E63', 500: '#795548', 600: '#6D4C41', 700: '#5D4037', 800: '#4E342E', 900: '#3E2723' },
    grey: { 50: '#FAFAFA', 100: '#F5F5F5', 200: '#EEEEEE', 300: '#E0E0E0', 400: '#BDBDBD', 500: '#9E9E9E', 600: '#757575', 700: '#616161', 800: '#424242', 900: '#212121' },
    'blue-grey': { 50: '#ECEFF1', 100: '#CFD8DC', 200: '#B0BEC5', 300: '#90A4AE', 400: '#78909C', 500: '#607D8B', 600: '#546E7A', 700: '#455A64', 800: '#37474F', 900: '#263238' }
};

export const HUE_ORDER = Object.keys(MATERIAL);
export const STEPS = [50, 100, 200, 300, 400, 500, 600, 700, 800, 900];

// 額外的中性/特殊色(有 hex 者參與吸附;transparent 無 hex,不參與)
export const EXTRA_COLORS = [
    { cls: 'rt-color-default', hex: '#333333', token: '--cl-text', absolute: false },
    { cls: 'rt-color-black', hex: '#000000', token: null, absolute: true },
    { cls: 'rt-color-white', hex: '#FFFFFF', token: null, absolute: true }
];

// 透明度工具階(可套任意元素);transparent 為「無色」選項
export const OPACITY_SCALE = [0, 5, 10, 20, 40, 60, 80, 100];

// 扁平色表(供最近色吸附):所有 hue-step + extra(有 hex 者)
export const COLOR_PALETTE = [
    ...HUE_ORDER.flatMap(hue => STEPS.map(step => ({ cls: `rt-color-${hue}-${step}`, hex: MATERIAL[hue][step] }))),
    ...EXTRA_COLORS.filter(c => c.hex).map(c => ({ cls: c.cls, hex: c.hex }))
];

// 同群 class(normalizer 換色/字級時先清同群)
export const RT_GROUPS = {
    color: COLOR_PALETTE.map(c => c.cls).concat('rt-color-transparent'),
    size: ['rt-size-xs', 'rt-size-sm', 'rt-size-md', 'rt-size-lg', 'rt-size-xl', 'rt-size-2xl', 'rt-size-3xl', 'rt-size-4xl', 'rt-size-5xl'],
    align: ['rt-align-left', 'rt-align-center', 'rt-align-right', 'rt-align-justify'],
    lh: ['rt-lh-1', 'rt-lh-15', 'rt-lh-2', 'rt-lh-3']
};

export const SIZE_SCALE = [
    { cls: 'rt-size-xs', px: 11 }, { cls: 'rt-size-sm', px: 12 }, { cls: 'rt-size-md', px: 13 },
    { cls: 'rt-size-lg', px: 14 }, { cls: 'rt-size-xl', px: 16 }, { cls: 'rt-size-2xl', px: 18 },
    { cls: 'rt-size-3xl', px: 24 }, { cls: 'rt-size-4xl', px: 28 }, { cls: 'rt-size-5xl', px: 36 }
];
export const ALIGN_CLASSES = { left: 'rt-align-left', center: 'rt-align-center', right: 'rt-align-right', justify: 'rt-align-justify' };
export const LH_SCALE = [{ cls: 'rt-lh-1', v: 1 }, { cls: 'rt-lh-15', v: 1.5 }, { cls: 'rt-lh-2', v: 2 }, { cls: 'rt-lh-3', v: 3 }];

// sanitizeHTML 的 class 白名單:富文本 rt-* 群 + opacity 工具 + 少數結構性 class
export const ALLOWED_CLASS_PATTERN = /^(rt-color-([a-z-]+-(50|100|200|300|400|500|600|700|800|900)|default|black|white|transparent)|rt-size-[a-z0-9]+|rt-align-(left|center|right|justify)|rt-lh-(1|15|2|3)|opacity-(0|5|10|20|40|60|80|100)|web-painter-embed|rich-content)$/;

function parseColor(input) {
    if (!input) return null;
    const s = String(input).trim().toLowerCase();
    let m = /^#([0-9a-f]{3})$/.exec(s);
    if (m) { const h = m[1]; return [parseInt(h[0] + h[0], 16), parseInt(h[1] + h[1], 16), parseInt(h[2] + h[2], 16)]; }
    m = /^#([0-9a-f]{6})$/.exec(s);
    if (m) return [parseInt(m[1].slice(0, 2), 16), parseInt(m[1].slice(2, 4), 16), parseInt(m[1].slice(4, 6), 16)];
    m = /^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)/.exec(s);
    if (m) return [Math.round(+m[1]), Math.round(+m[2]), Math.round(+m[3])];
    return null;
}

const _rgbCache = COLOR_PALETTE.map(c => ({ cls: c.cls, rgb: parseColor(c.hex) }));

/** 顏色 → 最近的調色盤 class(RGB 歐氏距離);無法解析回 rt-color-default */
export function nearestColorClass(input) {
    const rgb = parseColor(input);
    if (!rgb) return 'rt-color-default';
    let best = 'rt-color-default', bestD = Infinity;
    for (const p of _rgbCache) {
        if (!p.rgb) continue;
        const dr = rgb[0] - p.rgb[0], dg = rgb[1] - p.rgb[1], db = rgb[2] - p.rgb[2];
        const d = dr * dr + dg * dg + db * db;
        if (d < bestD) { bestD = d; best = p.cls; }
    }
    return best;
}

/** px 字級 → 最近字級 class */
export function nearestSizeClass(px) {
    const n = parseFloat(px);
    if (!Number.isFinite(n)) return null;
    let best = SIZE_SCALE[0].cls, bestD = Infinity;
    for (const s of SIZE_SCALE) { const d = Math.abs(s.px - n); if (d < bestD) { bestD = d; best = s.cls; } }
    return best;
}

/** text-align → align class */
export function alignClass(value) {
    return ALIGN_CLASSES[String(value || '').trim().toLowerCase()] || null;
}

/** line-height → 最近行距 class */
export function nearestLhClass(value) {
    const n = parseFloat(value);
    if (!Number.isFinite(n)) return null;
    let best = null, bestD = Infinity;
    for (const l of LH_SCALE) { const d = Math.abs(l.v - n); if (d < bestD) { bestD = d; best = l.cls; } }
    return best;
}

export default {
    MATERIAL, HUE_ORDER, STEPS, EXTRA_COLORS, OPACITY_SCALE, COLOR_PALETTE, RT_GROUPS,
    SIZE_SCALE, ALIGN_CLASSES, LH_SCALE, ALLOWED_CLASS_PATTERN,
    nearestColorClass, nearestSizeClass, alignClass, nearestLhClass
};
