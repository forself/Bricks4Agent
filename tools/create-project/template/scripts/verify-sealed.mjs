// verify-sealed.mjs — 發佈產物「封閉性」驗證(零依賴 node)。
// 走訪 dist 內全部 .js/.html/.css,抽出字面相對引用(import/from/import()/new URL/src/href/@import/url()),
// 逐一解析:必須 (a) 不逃出 dist 根 (b) 目標檔案存在;另掃禁止樣式(腳手架外部路徑)。
// 用法:node scripts/verify-sealed.mjs <distDir>
import { readFileSync, readdirSync, statSync, existsSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const distRoot = path.resolve(process.argv[2] || 'dist');
if (!existsSync(distRoot)) { console.error('dist 不存在:' + distRoot); process.exit(2); }

const FORBIDDEN = [/packages\/javascript\/browser/, /Bricks4Agent/];
const violations = [];
let filesScanned = 0, refsChecked = 0;

function* walk(dir) {
    for (const name of readdirSync(dir)) {
        const p = path.join(dir, name);
        if (statSync(p).isDirectory()) yield* walk(p);
        else yield p;
    }
}

// JS 剝註解(JSDoc @type{import('...')} 之類非執行期引用不驗;整行 // 與 /* */ 區塊)
function stripJsComments(s) {
    return s.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
}

// 各檔型的「字面相對引用」抽取器(變數組字串無法靜態解析者略過)
function extractRefs(file, text) {
    const refs = [];
    const push = (m) => { if (m) refs.push(m); };
    if (file.endsWith('.js') || file.endsWith('.mjs')) {
        text = stripJsComments(text);
        for (const re of [
            /(?:import|export)\s[^'"]*from\s*['"]([^'"]+)['"]/g,   // import ... from / export ... from
            /import\s*\(\s*['"]([^'"]+)['"]\s*\)/g,                 // dynamic import('...')
            /import\s*['"]([^'"]+)['"]/g,                           // side-effect import '...'
            /new URL\(\s*['"]([^'"]+)['"]\s*,\s*import\.meta\.url/g // new URL('...', import.meta.url)
        ]) for (const m of text.matchAll(re)) push(m[1]);
    } else if (file.endsWith('.html')) {
        for (const m of text.matchAll(/(?:src|href)\s*=\s*["']([^"']+)["']/g)) push(m[1]);
    } else if (file.endsWith('.css')) {
        for (const m of text.matchAll(/@import\s+(?:url\()?\s*['"]([^'"]+)['"]/g)) push(m[1]);
        for (const m of text.matchAll(/url\(\s*['"]?([^'")]+)['"]?\s*\)/g)) push(m[1]);
    }
    return refs;
}

const skip = (r) =>
    /^(https?:|data:|blob:|#|mailto:|javascript:|node:)/i.test(r) ||   // 外部協定/錨點(node:=快照內附帶的建置工具,非瀏覽器路徑)
    r.includes('${') ||                                           // 模板字串:非靜態引用,無從驗證(生成器 emit 用)
    r === '';

for (const file of walk(distRoot)) {
    if (!/\.(js|mjs|html|css)$/.test(file)) continue;
    filesScanned++;
    const text = readFileSync(file, 'utf8');
    const rel = path.relative(distRoot, file);

    for (const ref of extractRefs(file, text)) {
        if (skip(ref)) continue;
        // 禁止樣式只針對「實際引用」(全文掃會誤傷註解)
        if (FORBIDDEN.some(re => re.test(ref))) { violations.push(`[外部腳手架引用] ${rel} → ${ref}`); continue; }
        if (ref.startsWith('/')) { violations.push(`[根絕對路徑] ${rel} → ${ref}(封裝產物禁用,改相對)`); continue; }
        // bare 匯入只對 JS 模組是問題;HTML/CSS 無 ./ 前綴即為合法相對路徑,照樣解析驗證
        if (/\.(js|mjs)$/.test(file) && !ref.startsWith('./') && !ref.startsWith('../')) {
            violations.push(`[bare 匯入] ${rel} → ${ref}(瀏覽器無法解析)`); continue;
        }
        refsChecked++;
        const target = path.resolve(path.dirname(file), ref.split('?')[0].split('#')[0]);
        if (!target.startsWith(distRoot + path.sep) && target !== distRoot) {
            violations.push(`[逃出產物根] ${rel} → ${ref}`);
        } else if (!existsSync(target)) {
            violations.push(`[目標不存在] ${rel} → ${ref}`);
        }
    }
}

console.log(`封閉性驗證:掃 ${filesScanned} 檔、解析 ${refsChecked} 個相對引用`);
if (violations.length) {
    console.error(`\n違規 ${violations.length} 項:`);
    for (const v of violations.slice(0, 30)) console.error('  ' + v);
    process.exit(1);
}
console.log('產物完全封閉(無外部腳手架引用、無逃逸、引用皆存在)。');
