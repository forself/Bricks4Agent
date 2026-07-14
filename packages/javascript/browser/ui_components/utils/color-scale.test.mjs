// color-scale.test.mjs — 零依賴 node 直測:node utils/color-scale.test.mjs
import { MATERIAL } from '../editor/richtext-palette.js';
import {
    hexToRgb, rgbToHex, mixHex, sampleRamp,
    sequentialScale, divergingScale, categoricalColor, hierarchicalColor, CATEGORICAL_HUES
} from './color-scale.js';

const results = [];
const t = (name, fn) => { try { fn(); results.push({ name, pass: true }); } catch (e) { results.push({ name, pass: false, detail: e.message }); } };
const eq = (a, b, msg) => { if (a !== b) throw new Error(`${msg || ''} 期望 ${b} 得到 ${a}`); };
const ok = (c, msg) => { if (!c) throw new Error(msg || 'assert failed'); };

t('hex↔rgb 往返', () => { eq(rgbToHex(hexToRgb('#2196F3')).toUpperCase(), '#2196F3'); eq(rgbToHex(hexToRgb('#abc')).toLowerCase(), '#aabbcc'); });
t('mixHex 端點與中點', () => {
    eq(mixHex('#000000', '#ffffff', 0), '#000000');
    eq(mixHex('#000000', '#ffffff', 1), '#ffffff');
    eq(mixHex('#000000', '#ffffff', 0.5), '#808080');
});
t('sampleRamp 端點=原色', () => {
    const ramp = ['#111111', '#555555', '#999999'];
    eq(sampleRamp(ramp, 0), '#111111'); eq(sampleRamp(ramp, 1), '#999999'); eq(sampleRamp(ramp, 0.5), '#555555');
});
t('sequential 端點=MATERIAL 50/900、域外夾擠', () => {
    const s = sequentialScale('red', [0, 100]);
    eq(s(0).toUpperCase(), MATERIAL.red['50'].toUpperCase());
    eq(s(100).toUpperCase(), MATERIAL.red['900'].toUpperCase());
    eq(s(-50), s(0)); eq(s(999), s(100));
});
t('sequential 單調變深(紅通道遞減)', () => {
    const s = sequentialScale('blue', [0, 1]);
    const rs = [0, 0.25, 0.5, 0.75, 1].map(v => hexToRgb(s(v))[2]);   // 藍相:B 通道應大致遞減(變深)
    for (let i = 1; i < rs.length; i++) ok(rs[i] <= rs[i - 1] + 8, `B 通道未遞減:${rs.join(',')}`);
});
t('diverging:min=負700、max=正700、mid≈中性灰', () => {
    const d = divergingScale('blue', 'red', [-10, 0, 10]);
    eq(d(-10).toUpperCase(), MATERIAL.blue['700'].toUpperCase());
    eq(d(10).toUpperCase(), MATERIAL.red['700'].toUpperCase());
    eq(d(0).toUpperCase(), MATERIAL.grey['100'].toUpperCase());
});
t('diverging 非對稱 domain(mid 偏移)', () => {
    const d = divergingScale('blue', 'red', [0, 80, 100]);
    eq(d(0).toUpperCase(), MATERIAL.blue['700'].toUpperCase());
    eq(d(80).toUpperCase(), MATERIAL.grey['100'].toUpperCase());
    eq(d(100).toUpperCase(), MATERIAL.red['700'].toUpperCase());
});
t('legendStops 數量與端點', () => {
    const s = sequentialScale('teal', [10, 20]);
    const stops = s.legendStops(5);
    eq(stops.length, 5); eq(stops[0].value, 10); eq(stops[4].value, 20);
    eq(stops[0].color.toUpperCase(), MATERIAL.teal['50'].toUpperCase());
});
t('categorical 前 16 類互不重複、循環一致', () => {
    const set = new Set(Array.from({ length: CATEGORICAL_HUES.length }, (_, i) => categoricalColor(i)));
    eq(set.size, CATEGORICAL_HUES.length, '有重複分類色');
    eq(categoricalColor(0), categoricalColor(CATEGORICAL_HUES.length));
});
t('hierarchicalColor 同頂層群同色相、層級變深淺', () => {
    const l0 = hierarchicalColor(2, 0), l1 = hierarchicalColor(2, 1);
    ok(l0 !== l1, '層級未變化');
    eq(l0.toUpperCase(), MATERIAL[CATEGORICAL_HUES[2]]['500'].toUpperCase());
    eq(l1.toUpperCase(), MATERIAL[CATEGORICAL_HUES[2]]['300'].toUpperCase());
});
t('未知色相擲錯', () => {
    let threw = false;
    try { sequentialScale('nope', [0, 1]); } catch { threw = true; }
    ok(threw);
});

let fail = 0;
for (const r of results) { if (!r.pass) fail++; console.log(`  ${r.pass ? 'ok ' : 'FAIL'} ${r.name}${r.pass ? '' : ' — ' + r.detail}`); }
console.log(`\n結果: ${results.length - fail} passed, ${fail} failed`);
process.exit(fail ? 1 : 0);
