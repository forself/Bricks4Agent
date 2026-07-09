// audit-csp.mjs — CSP 合規守門員(零依賴 node;不靠 rg)。
// 掃 runtime 原始碼(ui_components + page-generator,排除 vendor/demo/tests)六類違規:
//   A. <style> 元素注入(style-src 擋)          B. setAttribute('style',…)(style-src 擋)
//   C. HTML 字串內 style="…"(style-src 擋)     D. HTML 字串內 on*= 事件(script-src 擋)
//   E. eval / new Function(unsafe-eval)          F. javascript: URL(security.js 的防禦性過濾除外)
// 任何違規 → exit 1。JS 註解先剝除再比對(避免註解誤報)。
// 用法:node tools/scripts/audit-csp.mjs [--quiet]
import { readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const roots = [
    path.join(repo, 'packages', 'javascript', 'browser', 'ui_components'),
    path.join(repo, 'packages', 'javascript', 'browser', 'page-generator')
];
const quiet = process.argv.includes('--quiet');

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

let total = 0;
for (const c of CLASSES) {
    const list = hits[c.id];
    const sum = list.reduce((a, b) => a + b.n, 0);
    total += sum;
    console.log(`${c.id}. ${c.desc}: ${list.length} 檔 / ${sum} 處${list.length && !quiet ? '' : ''}`);
    if (!quiet) for (const h of list.sort((a, b) => b.n - a.n)) console.log(`     ${h.file} ×${h.n}`);
}
console.log(`\n掃描 ${files} 檔;違規總計 ${total} 處 → ${total === 0 ? '嚴格 CSP(script-src+style-src \'self\')合規 ✓' : '未合規 ✗'}`);
process.exit(total === 0 ? 1 - 1 : 1);
