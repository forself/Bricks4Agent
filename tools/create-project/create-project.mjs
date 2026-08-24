// create-project.mjs — 產生接好「連結開發 + 發佈快照」機制的新專案。跨平台零依賴。
// 動作:
//   1. 複製 template/ → <dest>(gitignore → .gitignore)
//   2. 烙入 {{PROJECT_NAME}};寫入 <dest>/.b4a-root(每機腳手架指標,gitignored;
//      腳本經 resolve-b4a.mjs 解析:env B4A_ROOT > .b4a-root > 同層搜尋——腳本內零寫死路徑)
//   3. git init(--no-git 跳過)
//   4. node scripts/dev-link.mjs 建連結(--no-junction 改複本模式)
//   5. 檢核 lib/ui_components/theme.css 可達
// 用法:
//   node tools/create-project/create-project.mjs --name my-app
//   node tools/create-project/create-project.mjs --name my-app --dest D:\proj\my-app
//   選項:--no-junction(複本模式)、--no-git
import { existsSync, readdirSync, statSync, mkdirSync, cpSync, renameSync, readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';

// --- 參數 ---
const args = process.argv.slice(2);
const opt = (k) => { const i = args.indexOf(k); return i > -1 ? args[i + 1] : ''; };
const name = opt('--name');
const noJunction = args.includes('--no-junction');
const noGit = args.includes('--no-git');
if (!/^[A-Za-z][A-Za-z0-9_-]*$/.test(name || '')) {
    console.error('用法:node create-project.mjs --name my-app [--dest 路徑] [--no-junction] [--no-git]');
    console.error('名稱限英數/-/_,字母開頭。');
    process.exit(1);
}

const here = path.dirname(fileURLToPath(import.meta.url));
const b4aRoot = path.resolve(here, '..', '..');
const template = path.join(here, 'template');
if (!existsSync(path.join(b4aRoot, 'packages', 'javascript', 'browser', 'ui_components', 'theme.css'))) {
    console.error('腳手架自檢失敗:' + b4aRoot);
    process.exit(1);
}
const dest = opt('--dest') ? path.resolve(opt('--dest')) : path.join(path.dirname(b4aRoot), name);
if (existsSync(dest) && readdirSync(dest).length > 0) {
    console.error('目的地已存在且非空:' + dest);
    process.exit(1);
}

// 1. 複製範本
cpSync(template, dest, { recursive: true });
renameSync(path.join(dest, 'gitignore'), path.join(dest, '.gitignore'));

// 2. 烙入專案名 + 每機腳手架指標
(function bake(dir) {
    for (const n of readdirSync(dir)) {
        const p = path.join(dir, n);
        if (statSync(p).isDirectory()) { bake(p); continue; }
        if (!/\.(mjs|js|html|css|md|ps1)$|^\.gitignore$/.test(n)) continue;
        const text = readFileSync(p, 'utf8');
        if (text.includes('{{PROJECT_NAME}}')) writeFileSync(p, text.replaceAll('{{PROJECT_NAME}}', name));
    }
})(dest);
writeFileSync(path.join(dest, '.b4a-root'), b4aRoot + '\n');

// 3. git init
if (!noGit) {
    let r = spawnSync('git', ['-C', dest, 'init', '-b', 'main'], { stdio: 'ignore' });
    if (r.status !== 0) spawnSync('git', ['-C', dest, 'init'], { stdio: 'ignore' });   // 舊版 git 無 -b
}

// 4. 開發模式:連結(腳手架維護者)或釘版複本(團隊成員/CI 預設可重現)
const cmd = noJunction
    ? [path.join(dest, 'scripts', 'sync-lib.mjs'), '--pin']   // 複本模式一律先釘版(b4a.lock.json 入版控)
    : [path.join(dest, 'scripts', 'dev-link.mjs')];
const link = spawnSync(process.execPath, cmd, { stdio: 'inherit' });
if (link.status !== 0) process.exit(link.status ?? 1);

// 5. 檢核
if (!existsSync(path.join(dest, 'lib', 'ui_components', 'theme.css'))) {
    console.error('設定完成後 lib/ 不可達——請檢查上方訊息。');
    process.exit(1);
}

console.log('');
console.log(`專案「${name}」就緒:${dest}`);
console.log('  開發:   node scripts/dev.mjs        ->  http://127.0.0.1:8123/src/frontend/index.html');
console.log('  發佈:   node scripts/publish.mjs    ->  dist/(密封產物 + SNAPSHOT.json)');
console.log('  文件:   docs/dev-and-publish.md');
