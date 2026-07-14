// audit-csp.mjs — CSP + 視覺技術政策守門員(零依賴 node;不靠 rg)。
// 掃 runtime 原始碼(ui_components + page-generator,排除 vendor/demo/tests):
//   A. <style> 元素注入(style-src 擋)          B. setAttribute('style',…)(style-src 擋)
//   C. HTML 字串內 style="…"(style-src 擋)     D. HTML 字串內 on*= 事件(script-src 擋)
//   E. eval / new Function(unsafe-eval)          F. javascript: URL(security.js 的防禦性過濾除外)
//   G. SVG 使用(<svg / createElementNS / data:image/svg)——庫政策:全面 Canvas 化,SVG 禁用
// A–F:任何違規 → exit 1(硬零)。
// G:棘輪制——與 svg-baseline.json 比對,「新增檔案或處數增加」才 fail;
//    存量遞減屬遷移進行式(波 1-3),清零後基線歸空即與 A-F 同級硬零。
// 用法:node tools/scripts/audit-csp.mjs [--quiet] [--write-baseline]
import { readFileSync, readdirSync, statSync, writeFileSync, existsSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const roots = [
    path.join(repo, 'packages', 'javascript', 'browser', 'ui_components'),
    path.join(repo, 'packages', 'javascript', 'browser', 'page-generator')
];
const quiet = process.argv.includes('--quiet');
const writeBaseline = process.argv.includes('--write-baseline');
const baselinePath = path.join(repo, 'tools', 'scripts', 'svg-baseline.json');

const CLASSES = [
    { id: 'A', desc: '<style> 元素注入',       re: /createElement\(\s*['"]style['"]\s*\)/g },
    { id: 'B', desc: "setAttribute('style')",  re: /setAttribute\(\s*['"]style['"]/g },
    { id: 'C', desc: 'HTML 字串內 style="…"',  re: /style="[^"]/g },
    { id: 'D', desc: 'HTML 字串內 on*= 事件',  re: /\son(click|dblclick|load|error|change|input|submit|focus|blur|mouse\w+|key\w+)\s*=\s*"/g },
    { id: 'E', desc: 'eval / new Function',    re: /\beval\s*\(|new\s+Function\s*\(/g },
    { id: 'F', desc: 'javascript: URL',        re: /javascript:/g, exempt: (f) => f.endsWith(path.join('utils', 'security.js')) }
];

const stripComments = (s) => s.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
const skipFile = (f) =>
    f.includes(path.sep + 'vendor' + path.sep) || f.includes('__tests__') ||
    /(^|[\\/])demo[^\\/]*\.js$/i.test(f) || f.endsWith('.test.mjs') || !f.endsWith('.js');

function* walk(dir) {
    for (const name of readdirSync(dir)) {
        const p = path.join(dir, name);
        if (statSync(p).isDirectory()) yield* walk(p);
        else yield p;
    }
}

const hits = Object.fromEntries(CLASSES.map(c => [c.id, []]));
let files = 0;
for (const root of roots) {
    for (const f of walk(root)) {
        if (skipFile(f)) continue;
        files++;
        const text = stripComments(readFileSync(f, 'utf8'));
        for (const c of CLASSES) {
            if (c.exempt && c.exempt(f)) continue;
            const n = (text.match(c.re) || []).length;
            if (n > 0) hits[c.id].push({ file: path.relative(repo, f).replaceAll('\\', '/'), n });
        }
    }
}

// ── G 類:SVG 使用(棘輪制)──
const G_RE = [/<svg[\s>]/g, /createElementNS\s*\(/g, /data:image\/svg/g];
const gHits = new Map();   // file → count
for (const root of roots) {
    for (const f of walk(root)) {
        if (skipFile(f)) continue;
        const text = stripComments(readFileSync(f, 'utf8'));
        let n = 0;
        for (const re of G_RE) n += (text.match(re) || []).length;
        if (n > 0) gHits.set(path.relative(repo, f).replaceAll('\\', '/'), n);
    }
}
if (writeBaseline) {
    writeFileSync(baselinePath, JSON.stringify(Object.fromEntries([...gHits.entries()].sort()), null, 2) + '\n');
    console.log(`svg-baseline.json 已寫入:${gHits.size} 檔(遷移棘輪起點)`);
}
const baseline = existsSync(baselinePath) ? JSON.parse(readFileSync(baselinePath, 'utf8')) : {};
let gNew = [], gGrew = [], gRemain = 0, gSum = 0;
for (const [f, n] of gHits) {
    gSum += n;
    if (!(f in baseline)) gNew.push(`${f} ×${n}`);
    else if (n > baseline[f]) gGrew.push(`${f} ${baseline[f]}→${n}`);
    else gRemain++;
}
const gCleaned = Object.keys(baseline).filter(f => !gHits.has(f));

let total = 0;
for (const c of CLASSES) {
    const list = hits[c.id];
    const sum = list.reduce((a, b) => a + b.n, 0);
    total += sum;
    console.log(`${c.id}. ${c.desc}: ${list.length} 檔 / ${sum} 處`);
    if (!quiet) for (const h of list.sort((a, b) => b.n - a.n)) console.log(`     ${h.file} ×${h.n}`);
}
console.log(`G. SVG 使用(政策:禁用,Canvas 化中): ${gHits.size} 檔 / ${gSum} 處(基線 ${Object.keys(baseline).length} 檔)`);
if (gNew.length) { console.log('   ✗ 基線外新增 SVG(禁止):'); gNew.forEach(x => console.log('     ' + x)); }
if (gGrew.length) { console.log('   ✗ 既有檔 SVG 增量(禁止):'); gGrew.forEach(x => console.log('     ' + x)); }
if (gCleaned.length && !quiet) console.log(`   ✓ 已清零 ${gCleaned.length} 檔——請跑 --write-baseline 收緊棘輪`);

const gFail = gNew.length + gGrew.length;
console.log(`\n掃描 ${files} 檔;CSP 違規 ${total} 處、SVG 棘輪違規 ${gFail} 件 → ${total === 0 && gFail === 0 ? '合規 ✓' : '未合規 ✗'}`);
process.exit(total === 0 && gFail === 0 ? 0 : 1);
