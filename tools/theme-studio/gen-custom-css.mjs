// tokens.json → theme.custom.css 生成器(AI/CI 管道用;UI 內建同等匯出)
// 讓 AI 代理直接編輯 theme.tokens.json 再產出客製樣式表,與人用 UI 匯出結果一致。
// 用法:node gen-custom-css.mjs [tokens.json] [out.css]
//   預設:./theme.tokens.json → ./theme.custom.css
import { readFileSync, writeFileSync } from 'node:fs';
import process from 'node:process';

const inPath = process.argv[2] || 'theme.tokens.json';
const outPath = process.argv[3] || 'theme.custom.css';

const data = JSON.parse(readFileSync(inPath, 'utf8'));
// 結構:{ tokens:{ "--cl-primary":"#123", ... }, tokensDark?:{...},
//        components?:{ 元件名:{ className:"b4a-c-元件名", tokens:{...} } }, meta?:{...} }
const tokens = data.tokens || {};
const tokensDark = data.tokensDark || {};
const components = data.components || {};

const decl = (obj) => Object.entries(obj)
    .filter(([k, v]) => k && v !== '' && v != null)
    .map(([k, v]) => `    ${k.startsWith('--') ? k : '--' + k}: ${v};`)
    .join('\n');

const out = [];
out.push('/*');
out.push(' * theme.custom.css —— 客製化樣式覆蓋(由 Theme Studio 產生)');
out.push(' * 載入順序:palette.css(@import via theme.css) → theme.css → 【本檔】。');
out.push(' * 只覆蓋有調整的 token;未列者沿用 theme.css 預設。');
if (data.meta?.name) out.push(` * 主題名稱:${data.meta.name}`);
out.push(' */');
out.push(':root {');
out.push(decl(tokens));
out.push('}');
if (Object.keys(tokensDark).length) {
    out.push('');
    out.push('[data-theme="dark"] {');
    out.push(decl(tokensDark));
    out.push('}');
}
// 各元件覆蓋:置於 :root 之後 → cascade 讓具名 class 覆蓋全域(於正式站把 class 加到元件根元素即套用)
const compNames = Object.keys(components);
if (compNames.length) {
    out.push('');
    out.push('/* 各元件覆蓋(具名 class;同元件可有多種變體)*/');
    for (const name of compNames) {
        const c = components[name] || {};
        const cls = (c.className || ('b4a-c-' + name)).replace(/^\.+/, '');
        out.push(`.${cls} {`);
        out.push(decl(c.tokens || {}));
        out.push('}');
    }
}
out.push('');

writeFileSync(outPath, out.join('\n'), 'utf8');
console.log(`theme.custom.css 生成:${Object.keys(tokens).length} 個 light token` +
    (Object.keys(tokensDark).length ? ` + ${Object.keys(tokensDark).length} 個 dark token` : '') +
    (compNames.length ? ` + ${compNames.length} 個元件覆蓋` : '') +
    ` → ${outPath}`);
