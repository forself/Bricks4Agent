// sync-lib.mjs — 複本模式：把腳手架的已釘版內容複製進 lib/。
//   node scripts/sync-lib.mjs              # 依 b4a.lock.json 同步
//   node scripts/sync-lib.mjs --pin        # 以目前可達的 B4A tree 建立 v2 lock 並同步
//   node scripts/sync-lib.mjs --pin <ref>  # 驗證 ref 與目前 B4A tree 相同後釘版
// v2 直接釘 Git tree object；tree 由目前 branch 的 B4A 目錄可達，不依賴額外 tag 或
// subtree-split commit。舊 commit lock 僅保留遷移相容性。
import { existsSync, lstatSync, rmSync, cpSync, readFileSync, writeFileSync, mkdtempSync } from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { resolveB4a } from './resolve-b4a.mjs';
import { createB4aTreeLock, isFullGitObjectId, parseB4aLock } from './b4a-lock.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const LIBS = ['ui_components', 'page-generator', 'custom_components'];
const LOCK = path.join(root, 'b4a.lock.json');
const args = process.argv.slice(2);
const pinMode = args.includes('--pin');
const pinRef = pinMode ? (args[args.indexOf('--pin') + 1] || '') : '';

for (const directory of LIBS) {
    try {
        if (lstatSync(path.join(root, 'lib', directory)).isSymbolicLink()) {
            console.error(`lib/${directory} 是連結（開發模式）。先跑 node scripts/dev-link.mjs --unlink。`);
            process.exit(1);
        }
    } catch { /* 不存在：可複製 */ }
}

const { b4aRoot, b4aBrowser } = resolveB4a(root);
const git = (...gitArgs) => {
    const result = spawnSync('git', ['-C', b4aRoot, ...gitArgs], { encoding: 'utf8' });
    return result.status === 0 ? result.stdout.trim() : '';
};
const embeddedRoot = path.resolve(root, '..', 'packages', 'Bricks4Agent');
const isEmbedded = path.resolve(b4aRoot).toLowerCase() === embeddedRoot.toLowerCase();
const source = isEmbedded ? 'packages/Bricks4Agent' : '.';
const treeExpression = reference => isEmbedded
    ? `${reference}:packages/Bricks4Agent`
    : `${reference}^{tree}`;
const currentTree = () => git('rev-parse', treeExpression('HEAD'));
const sourceStatus = () => git('status', '--porcelain', '--', '.');
const skipNodeModules = src => path.basename(src) !== 'node_modules';

let lockInfo = null;
if (pinMode) {
    const reference = pinRef && !pinRef.startsWith('--') ? pinRef : 'HEAD';
    const targetTree = git('rev-parse', treeExpression(reference));
    const headTree = currentTree();
    if (!isFullGitObjectId(targetTree) || !isFullGitObjectId(headTree)) {
        console.error('釘版失敗：無法由目前 Git branch 解析 B4A tree。');
        process.exit(1);
    }
    if (targetTree !== headTree) {
        console.error(`釘版失敗：${reference} 的 B4A tree(${targetTree.slice(0, 12)})不是目前 branch tree(${headTree.slice(0, 12)})。`);
        console.error('請先 checkout 含該 B4A 內容的 branch/commit；lock 不得依賴 branch 外物件。');
        process.exit(1);
    }
    if (sourceStatus()) {
        console.error('釘版失敗：B4A 工作區 DIRTY。請先審查並 commit，不能把未提交內容偽裝成釘版。');
        process.exit(1);
    }
    const nextLock = createB4aTreeLock(headTree, source);
    writeFileSync(LOCK, JSON.stringify(nextLock, null, 2) + '\n');
    lockInfo = parseB4aLock(nextLock);
    console.log(`已釘版 tree:${headTree.slice(0, 12)} → b4a.lock.json（branch 自含，不依賴 tag）`);
} else if (existsSync(LOCK)) {
    try {
        lockInfo = parseB4aLock(JSON.parse(readFileSync(LOCK, 'utf8')));
    } catch (error) {
        console.error(error.message);
        process.exit(1);
    }
} else {
    console.warn('警告：無 b4a.lock.json——從腳手架工作區複製，不可重現。建議使用 --pin。');
}

let srcBrowser = b4aBrowser;
let tmpWorktree = '';
let pinnedTree = currentTree();
if (lockInfo?.kind === 'tree') {
    if (!isFullGitObjectId(pinnedTree) || pinnedTree !== lockInfo.tree) {
        console.error(`釘版同步失敗：目前 B4A tree(${String(pinnedTree).slice(0, 12)})≠ lock(${lockInfo.tree.slice(0, 12)})。`);
        console.error('請 checkout 含該 tree 的 branch；不要以 tag 或外部 split commit 補洞。');
        process.exit(1);
    }
    if (sourceStatus()) {
        console.error('釘版同步失敗：B4A 工作區 DIRTY，來源內容不等於 lock tree。');
        process.exit(1);
    }
} else if (lockInfo?.kind === 'legacy-commit') {
    pinnedTree = git('rev-parse', `${lockInfo.commit}^{tree}`);
    if (!isFullGitObjectId(pinnedTree)) {
        console.error(`舊版 lock commit ${lockInfo.commit} 不存在。請在含內嵌 B4A 的 branch 執行 node scripts/sync-lib.mjs --pin 遷移為 v2 tree lock。`);
        process.exit(1);
    }
    tmpWorktree = mkdtempSync(path.join(os.tmpdir(), 'b4a-pin-'));
    rmSync(tmpWorktree, { recursive: true, force: true });
    const result = spawnSync('git', ['-C', b4aRoot, 'worktree', 'add', '--detach', tmpWorktree, lockInfo.commit], { encoding: 'utf8' });
    if (result.status !== 0) {
        console.error('舊版 lock worktree 展開失敗：' + (result.stderr || '').trim());
        process.exit(1);
    }
    srcBrowser = path.join(tmpWorktree, 'packages', 'javascript', 'browser');
    console.warn('警告：正在讀取舊 commit lock；請用 --pin 遷移成 branch 可達的 v2 tree lock。');
}

try {
    for (const directory of LIBS) {
        const destination = path.join(root, 'lib', directory);
        if (existsSync(destination)) rmSync(destination, { recursive: true, force: true });
        cpSync(path.join(srcBrowser, directory), destination, { recursive: true, filter: skipNodeModules });
        console.log(`copy: lib/${directory}${lockInfo ? `(釘版 tree ${pinnedTree.slice(0, 12)})` : '(工作區)'}`);
    }
    const state = {
        mode: 'copy',
        pinned: !!lockInfo,
        ...(lockInfo?.kind === 'legacy-commit' ? { legacyCommit: lockInfo.commit } : {}),
        tree: isFullGitObjectId(pinnedTree) ? pinnedTree : '(no tree)',
        source: lockInfo?.source || source,
        dirtySource: lockInfo ? false : !!sourceStatus(),
        syncedAtUtc: new Date().toISOString()
    };
    writeFileSync(path.join(root, 'lib', '.sync-state.json'), JSON.stringify(state, null, 2) + '\n');
} finally {
    if (tmpWorktree) {
        spawnSync('git', ['-C', b4aRoot, 'worktree', 'remove', '--force', tmpWorktree], { stdio: 'ignore' });
        rmSync(tmpWorktree, { recursive: true, force: true });
    }
}
console.log(`lib/ 已同步（複本模式${lockInfo ? '，tree 釘版可重現' : ''}）。`);
