// sync-lib.mjs — 複本模式:把腳手架複製進 lib\。跨平台零依賴。支援「釘版」:
//   node scripts/sync-lib.mjs              # 有 b4a.lock.json → 從釘住的 commit 複製(可重現);無 → 從工作區複製(附警告)
//   node scripts/sync-lib.mjs --pin        # 釘住腳手架目前 HEAD 進 b4a.lock.json,並從該 commit 同步
//   node scripts/sync-lib.mjs --pin <sha>  # 釘指定 commit
// 釘版原理:git worktree 在暫存目錄展開該 commit → cpSync 取內容 → 移除 worktree(不含未 commit 的髒改動)。
// b4a.lock.json 應入版控(全隊同版);lib/.sync-state.json 記錄實際同步來源(publish 據此強制)。
// 首選開發模式仍是 dev-link.mjs(連結=活腳手架,腳手架維護者用);本腳本給團隊成員/CI。
import { existsSync, lstatSync, rmSync, cpSync, readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { resolveB4a } from './resolve-b4a.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const LIBS = ['ui_components', 'page-generator'];
const LOCK = path.join(root, 'b4a.lock.json');
const args = process.argv.slice(2);
const pinMode = args.includes('--pin');
const pinSha = pinMode ? (args[args.indexOf('--pin') + 1] || '') : '';

for (const d of LIBS) {
    try {
        if (lstatSync(path.join(root, 'lib', d)).isSymbolicLink()) {
            console.error(`lib/${d} 是連結(開發模式)。先跑 node scripts/dev-link.mjs --unlink,或直接續用連結即可。`);
            process.exit(1);
        }
    } catch { /* 不存在:可複製 */ }
}

const { b4aRoot, b4aBrowser } = resolveB4a(root);
const git = (...a) => { const r = spawnSync('git', ['-C', b4aRoot, ...a], { encoding: 'utf8' }); return r.status === 0 ? r.stdout.trim() : ''; };
const hex40 = (s) => /^[0-9a-f]{40}$/.test(s);
const skipNM = (src) => path.basename(src) !== 'node_modules';

let commit = '';
let pinned = false;
if (pinMode) {
    commit = pinSha && !pinSha.startsWith('--') ? git('rev-parse', pinSha) : git('rev-parse', 'HEAD');
    if (!hex40(commit)) { console.error('釘版失敗:取不到 commit(腳手架 repo 無 commit 或 sha 無效)。'); process.exit(1); }
    if (git('status', '--porcelain')) console.warn('警告:腳手架工作區 DIRTY——釘版複本=該 commit 內容,未 commit 的改動不會包含。');
    writeFileSync(LOCK, JSON.stringify({ commit, pinnedAtUtc: new Date().toISOString() }, null, 2) + '\n');
    pinned = true;
    console.log('已釘版:' + commit.slice(0, 12) + ' → b4a.lock.json(請入版控)');
} else if (existsSync(LOCK)) {
    commit = JSON.parse(readFileSync(LOCK, 'utf8')).commit;
    if (!hex40(commit)) { console.error('b4a.lock.json 的 commit 無效:' + commit); process.exit(1); }
    pinned = true;
} else {
    console.warn('警告:無 b4a.lock.json——從腳手架「工作區」複製,不可重現。建議 node scripts/sync-lib.mjs --pin 釘版。');
}

let srcBrowser = b4aBrowser;
let tmpWorktree = '';
if (pinned) {
    // 從釘住的 commit 展開暫存 worktree(乾淨、可重現)
    tmpWorktree = mkdtempSync(path.join(os.tmpdir(), 'b4a-pin-'));
    rmSync(tmpWorktree, { recursive: true, force: true });   // worktree add 需要不存在的路徑
    const r = spawnSync('git', ['-C', b4aRoot, 'worktree', 'add', '--detach', tmpWorktree, commit], { encoding: 'utf8' });
    if (r.status !== 0) { console.error('git worktree 展開失敗:' + (r.stderr || '').trim()); process.exit(1); }
    srcBrowser = path.join(tmpWorktree, 'packages', 'javascript', 'browser');
}

try {
    for (const d of LIBS) {
        const dest = path.join(root, 'lib', d);
        if (existsSync(dest)) rmSync(dest, { recursive: true, force: true });
        cpSync(path.join(srcBrowser, d), dest, { recursive: true, filter: skipNM });
        console.log(`copy: lib/${d}${pinned ? '(釘版 ' + commit.slice(0, 12) + ')' : '(工作區)'}`);
    }
    const state = {
        mode: 'copy', pinned,
        commit: pinned ? commit : (hex40(git('rev-parse', 'HEAD')) ? git('rev-parse', 'HEAD') : '(no commit)'),
        dirtySource: pinned ? false : !!git('status', '--porcelain'),
        syncedAtUtc: new Date().toISOString()
    };
    writeFileSync(path.join(root, 'lib', '.sync-state.json'), JSON.stringify(state, null, 2) + '\n');
} finally {
    if (tmpWorktree) {
        spawnSync('git', ['-C', b4aRoot, 'worktree', 'remove', '--force', tmpWorktree], { stdio: 'ignore' });
        rmSync(tmpWorktree, { recursive: true, force: true });
    }
}
console.log('lib/ 已同步(複本模式' + (pinned ? ',釘版可重現' : '') + ')。');
