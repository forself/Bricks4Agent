// 由 richtext-palette.js 單一資料來源生成 palette.css(token + 文字色 class + opacity)。
// 零依賴。執行:node editor/gen-palette-css.mjs
import { writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';
import { MATERIAL, HUE_ORDER, STEPS, EXTRA_COLORS, OPACITY_SCALE } from './richtext-palette.js';

const out = [];
out.push('/*');
out.push(' * palette.css —— 全站共用色彩系統(自動生成,勿手改)');
out.push(' * 來源:editor/richtext-palette.js;重生:node editor/gen-palette-css.mjs');
out.push(' * 提供:--cl-<hue>-<step> tokens(全站設計可用)+ .rt-color-* 文字色 class + .opacity-* 工具');
out.push(' */');

// 1) 色階 tokens
out.push(':root {');
for (const hue of HUE_ORDER) {
    for (const step of STEPS) out.push(`    --cl-${hue}-${step}: ${MATERIAL[hue][step]};`);
}
out.push('}');
out.push('');

// 2) 文字色 class(hue-step 走 token)
for (const hue of HUE_ORDER) {
    out.push(STEPS.map(step => `.rt-color-${hue}-${step} { color: var(--cl-${hue}-${step}); }`).join('\n'));
}
out.push('');

// 3) 特殊色
for (const c of EXTRA_COLORS) {
    const val = c.token ? `var(${c.token})` : c.hex;
    out.push(`.${c.cls} { color: ${val}; }`);
}
out.push('.rt-color-transparent { color: transparent; }');
out.push('');

// 4) 透明度工具階(可套任意元素)
for (const o of OPACITY_SCALE) out.push(`.opacity-${o} { opacity: ${o / 100}; }`);
out.push('');

// palette.css 是全站 foundation(色階 token),輸出到 ui_components 根,供 theme.css @import
const target = join(dirname(fileURLToPath(import.meta.url)), '..', 'palette.css');
writeFileSync(target, out.join('\n') + '\n', 'utf8');
const colorCount = HUE_ORDER.length * STEPS.length + EXTRA_COLORS.length + 1;
console.log(`palette.css 生成:${HUE_ORDER.length} 色相 × ${STEPS.length} 階 + ${EXTRA_COLORS.length} 特殊 + transparent = ${colorCount} 色;opacity ${OPACITY_SCALE.length} 階`);
