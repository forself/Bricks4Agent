// 富文本調色盤純函式測試 —— 零依賴,直接 node 執行:
//   node packages/javascript/browser/ui_components/editor/richtext-palette.test.mjs
// (需 DOM 的 sanitizeHTML / 編輯器 normalizer 測試在 tim-web 以真實 Edge 執行)
import assert from 'node:assert';
import {
    nearestColorClass, nearestSizeClass, alignClass, nearestLhClass, ALLOWED_CLASS_PATTERN, COLOR_PALETTE
} from './richtext-palette.js';

let pass = 0, fail = 0;
const t = (name, fn) => { try { fn(); pass++; console.log('  ok  ' + name); } catch (e) { fail++; console.log('  FAIL ' + name + ' — ' + e.message); } };

const hexOf = (cls) => (COLOR_PALETTE.find(c => c.cls === cls) || {}).hex;
console.log('== 最近色吸附(164 色階) ==');
t('#ff0000 → 紅色系', () => assert.ok(/^rt-color-red-\d/.test(nearestColorClass('#ff0000'))));
t('rgb(33,150,243) → 恰為 blue-500', () => assert.equal(nearestColorClass('rgb(33,150,243)'), 'rt-color-blue-500'));
t('#010101 → rt-color-black', () => assert.equal(nearestColorClass('#010101'), 'rt-color-black'));
t('#00e000 → 綠色系', () => assert.ok(/^rt-color-(green|light-green|lime)-\d/.test(nearestColorClass('#00e000'))));
t('無法解析 → default', () => assert.equal(nearestColorClass('nonsense'), 'rt-color-default'));
t('#ff00ff → 紫/粉系', () => assert.ok(/^rt-color-(purple|pink)-\d/.test(nearestColorClass('#ff00ff'))));
t('每個色階吸附回同 hex 的 class', () => COLOR_PALETTE.forEach(c => assert.equal(hexOf(nearestColorClass(c.hex)).toUpperCase(), c.hex.toUpperCase(), c.cls)));
t('吸附色數 = 163(160 色階 + 黑/白/default;transparent 無 hex 不吸附)', () => assert.equal(COLOR_PALETTE.length, 163));

console.log('== 字級/對齊/行距 ==');
t('11px → rt-size-xs', () => assert.equal(nearestSizeClass(11), 'rt-size-xs'));
t('72px → 最大 rt-size-5xl', () => assert.equal(nearestSizeClass('72px'), 'rt-size-5xl'));
t('20px → 2xl/3xl', () => assert.ok(['rt-size-2xl', 'rt-size-3xl'].includes(nearestSizeClass('20px'))));
t('center → rt-align-center', () => assert.equal(alignClass('center'), 'rt-align-center'));
t('JUSTIFY(大寫) → rt-align-justify', () => assert.equal(alignClass('JUSTIFY'), 'rt-align-justify'));
t('未知對齊 → null', () => assert.equal(alignClass('diagonal'), null));
t('lineHeight 1.5 → rt-lh-15', () => assert.equal(nearestLhClass('1.5'), 'rt-lh-15'));
t('lineHeight 2.9 → rt-lh-3', () => assert.equal(nearestLhClass(2.9), 'rt-lh-3'));

console.log('== class 白名單 pattern ==');
t('rt-color-red-500 合法', () => assert.ok(ALLOWED_CLASS_PATTERN.test('rt-color-red-500')));
t('rt-color-blue-grey-900 合法', () => assert.ok(ALLOWED_CLASS_PATTERN.test('rt-color-blue-grey-900')));
t('rt-color-transparent 合法', () => assert.ok(ALLOWED_CLASS_PATTERN.test('rt-color-transparent')));
t('opacity-40 合法', () => assert.ok(ALLOWED_CLASS_PATTERN.test('opacity-40')));
t('rt-size-2xl 合法', () => assert.ok(ALLOWED_CLASS_PATTERN.test('rt-size-2xl')));
t('web-painter-embed 合法', () => assert.ok(ALLOWED_CLASS_PATTERN.test('web-painter-embed')));
t('rt-color-danger 非法(舊命名已淘汰)', () => assert.ok(!ALLOWED_CLASS_PATTERN.test('rt-color-danger')));
t('rt-color-red-501 非法(非合法階)', () => assert.ok(!ALLOWED_CLASS_PATTERN.test('rt-color-red-501')));
t('opacity-33 非法(非合法階)', () => assert.ok(!ALLOWED_CLASS_PATTERN.test('opacity-33')));
t('evil-class 非法', () => assert.ok(!ALLOWED_CLASS_PATTERN.test('evil-class')));
t('rt-color-<script> 非法', () => assert.ok(!ALLOWED_CLASS_PATTERN.test('rt-color-<script>')));

console.log(`\n結果: ${pass} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
