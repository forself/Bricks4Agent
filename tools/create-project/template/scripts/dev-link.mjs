// dev-link.mjs — 開發模式:lib\ 以連結直連活腳手架(改元件即時生效、零複製)。跨平台零依賴:
// Windows 用 NTFS junction(免系統管理員)、POSIX 用 symlink——皆由 fs.symlinkSync(type:'junction') 一行處理。
// 發佈時 scripts/publish.mjs 會把 lib 具體化為 dist 內真複本。機制見 docs/dev-and-publish.md。
//   用法: node scripts/dev-link.mjs             # 建立/重建連結
//         node scripts/dev-link.mjs --unlink    # 拆連結(改用 sync-lib.mjs 複本模式)
import { existsSync, lstatSync, symlinkSync, mkdirSync, rmdirSync, unlinkSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { resolveB4a } from './resolve-b4a.mjs';
import { parseB4aLock } from './b4a-lock.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const LIBS = ['ui_components', 'page-generator', 'custom_components'];
const unlinkMode = process.argv.includes('--unlink');

// 拆連結只拆連結本身(目標不動);真實複本(可再生)整批移除。
function removeDirOrLink(p) {
    if (!existsSync(p) && !isLink(p)) return;
    if (isLink(p)) {
        try { unlinkSync(p); } catch { rmdirSync(p); }   // POSIX symlink=unlink;Windows 目錄 junction=rmdir
    } else {
        rmSync(p, { recursive: true, force: true });      // Node rmSync 不會追進連結,較 PS5.1 安全
    }
}
function isLink(p) {
    try { return lstatSync(p).isSymbolicLink(); } catch { return false; }
}

if (unlinkMode) {
    for (const d of LIBS) removeDirOrLink(path.join(root, 'lib', d));
    console.log('連結已拆除。無腳手架機器可用 node scripts/sync-lib.mjs 複本模式。');
    process.exit(0);
}

const { b4aRoot, b4aBrowser } = resolveB4a(root);
// 連結=腳手架維護者模式(追工作區);若專案有釘版 lock,提示目前 HEAD 與 lock 的落差(publish 時才強制)
const lockPath = path.join(root, 'b4a.lock.json');
if (existsSync(lockPath)) {
    try {
        const { readFileSync } = await import('node:fs');
        const { spawnSync } = await import('node:child_process');
        const lock = parseB4aLock(JSON.parse(readFileSync(lockPath, 'utf8')));
        const head = (spawnSync('git', ['-C', b4aRoot, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).stdout || '').trim();
        const embeddedRoot = path.resolve(root, '..', 'packages', 'Bricks4Agent');
        const isEmbedded = path.resolve(b4aRoot).toLowerCase() === embeddedRoot.toLowerCase();
        const expression = isEmbedded ? 'HEAD:packages/Bricks4Agent' : 'HEAD^{tree}';
        const tree = (spawnSync('git', ['-C', b4aRoot, 'rev-parse', expression], { encoding: 'utf8' }).stdout || '').trim();
        if (lock.kind === 'tree' && tree && tree !== lock.tree) {
            console.warn(`提示：腳手架 tree(${tree.slice(0, 12)})≠ b4a.lock.json(${lock.tree.slice(0, 12)})；publish 會拒絕。`);
        } else if (lock.kind === 'legacy-commit' && !isEmbedded && head && head !== lock.commit) {
            console.warn(`提示：腳手架 HEAD(${head.slice(0, 12)})≠ 舊 lock(${lock.commit.slice(0, 12)})；請用 sync-lib.mjs --pin 遷移。`);
        }
    } catch { /* 提示性質,失敗不擋 */ }
}
mkdirSync(path.join(root, 'lib'), { recursive: true });
for (const d of LIBS) {
    const link = path.join(root, 'lib', d);
    removeDirOrLink(link);
    symlinkSync(path.join(b4aBrowser, d), link, 'junction');   // type 僅 Windows 生效;POSIX 為一般 symlink
    console.log(`link: lib/${d}  ->  ${path.join(b4aBrowser, d)}`);
}
console.log('開發模式就緒:lib/ 直連腳手架(編輯 Bricks4Agent 即時生效)。');
