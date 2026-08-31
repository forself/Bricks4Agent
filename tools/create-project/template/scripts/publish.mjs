// publish.mjs — 發佈:腳手架具體化為完全自含的靜態產物。跨平台零依賴。
//   dist/
//     index.html          (轉導 src/frontend/)
//     lib/                (腳手架真快照;cpSync dereference 穿透連結取實體檔)
//     lib/SNAPSHOT.json   (來源憑證:tree/dirty/時間/檔數)
//     src/frontend/       (應用,與 repo 同幾何 → import 原樣可用)
// 引用紀律:原始碼永遠以相對路徑 import 'lib/…';開發經連結、發佈帶此複本。
// verify-sealed.mjs 強制產物封閉(違規=發佈失敗)。機制見 docs/dev-and-publish.md。
//   用法: node scripts/publish.mjs
// 釘版強制：v2 lock 直接驗 Git tree（branch 自含、不依賴 tag/split commit）；
// 連結模式另要求 B4A 工作區乾淨，複本模式要求 sync-state tree 與 lock 相同。
import { existsSync, rmSync, mkdirSync, cpSync, writeFileSync, readFileSync, readdirSync, statSync, lstatSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import { buildLibraryContentManifest } from './snapshot-integrity.mjs';
import { resolveB4a } from './resolve-b4a.mjs';
import { isFullGitObjectId, parseB4aLock } from './b4a-lock.mjs';

const scriptsDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptsDir, '..');
const dist = path.join(root, 'dist');
const projectName = path.basename(root);
if (process.argv.includes('--allow-drift')) {
    console.error('--allow-drift 已停用：正式產物不得略過 B4A tree lock 與工作區完整性檢查。');
    process.exit(2);
}
const { b4aRoot } = resolveB4a(root);

if (!existsSync(path.join(root, 'lib', 'ui_components', 'theme.css'))) {
    console.error('lib/ 是空的——先跑 node scripts/dev-link.mjs(連結)或 node scripts/sync-lib.mjs(複本)。');
    process.exit(1);
}

// ── 釘版強制(lock 存在才啟動;無 lock=警告模式,維持個人開發便利)──
function gitB4a(...a) {
    const r = spawnSync('git', ['-C', b4aRoot, ...a], { encoding: 'utf8' });
    return r.status === 0 ? r.stdout.trim() : '';
}
const lockPath = path.join(root, 'b4a.lock.json');
let lock = null;
if (existsSync(lockPath)) {
    try {
        lock = parseB4aLock(JSON.parse(readFileSync(lockPath, 'utf8')));
    } catch (error) {
        console.error('釘版強制失敗：' + error.message);
        process.exit(1);
    }
}
const embeddedB4aRoot = path.resolve(root, '..', 'packages', 'Bricks4Agent');
const b4aIsEmbedded = path.resolve(b4aRoot).toLowerCase() === embeddedB4aRoot.toLowerCase();
const b4aCurrentTree = () => gitB4a('rev-parse', b4aIsEmbedded ? 'HEAD:packages/Bricks4Agent' : 'HEAD^{tree}');
const b4aWorkingTreeStatus = () => gitB4a('status', '--porcelain', '--', '.');
const libIsLink = (() => { try { return lstatSync(path.join(root, 'lib', 'ui_components')).isSymbolicLink(); } catch { return false; } })();
const syncStatePath = path.join(root, 'lib', '.sync-state.json');
const syncState = (!libIsLink && existsSync(syncStatePath)) ? JSON.parse(readFileSync(syncStatePath, 'utf8')) : null;
let enforcement = 'none';
let enforcedTree = '';
if (lock) {
    let problem = '';
    const lockedTree = lock.kind === 'tree'
        ? lock.tree
        : gitB4a('rev-parse', `${lock.commit}^{tree}`);
    if (!isFullGitObjectId(lockedTree)) {
        problem = lock.kind === 'legacy-commit'
            ? `舊 lock commit ${lock.commit} 不存在；請執行 sync-lib.mjs --pin 遷移為 v2 tree lock`
            : 'lock tree 無法解析';
    }
    if (libIsLink) {
        const currentTree = b4aCurrentTree();
        if (!problem && (!isFullGitObjectId(currentTree) || currentTree !== lockedTree)) {
            problem = `連結模式：B4A tree(${currentTree.slice(0, 12)})≠ lock tree(${lockedTree.slice(0, 12)})`;
        } else if (!problem && b4aWorkingTreeStatus()) {
            problem = '連結模式：腳手架工作區 DIRTY（lock 強制要求乾淨）';
        }
    } else {
        if (!problem && (!syncState || !syncState.pinned)) problem = '複本模式：lib 非釘版同步（跑 node scripts/sync-lib.mjs 以 lock 重建）';
        else if (!problem && syncState.tree !== lockedTree) problem = `複本模式：lib tree(${String(syncState.tree).slice(0, 12)})≠ lock tree(${lockedTree.slice(0, 12)})`;
        else if (!problem && lock.kind === 'tree' && syncState.source !== lock.source) problem = `複本模式：lib source(${syncState.source})≠ lock source(${lock.source})`;
    }
    if (problem) {
        console.error('釘版強制失敗:' + problem);
        console.error('修正：sync-lib.mjs（依 lock 重建），或經審查後使用 sync-lib.mjs --pin 更新 tree lock。');
        process.exit(1);
    }
    enforcedTree = lockedTree;
    enforcement = 'enforced';
}

// 1. 清 dist
rmSync(dist, { recursive: true, force: true });
mkdirSync(dist, { recursive: true });

// 2. 腳手架快照(dereference 穿透連結;排除 node_modules / *.test.mjs / page-generator examples)
const notNodeModules = (src) => path.basename(src) !== 'node_modules';
cpSync(path.join(root, 'lib', 'ui_components'), path.join(dist, 'lib', 'ui_components'), {
    recursive: true, dereference: true,
    filter: (src) => notNodeModules(src) && !src.endsWith('.test.mjs')
});
cpSync(path.join(root, 'lib', 'page-generator'), path.join(dist, 'lib', 'page-generator'), {
    recursive: true, dereference: true,
    filter: (src) => notNodeModules(src) && path.basename(src) !== 'examples'
});
cpSync(path.join(root, 'lib', 'custom_components'), path.join(dist, 'lib', 'custom_components'), {
    recursive: true, dereference: true,
    filter: (src) => notNodeModules(src) && !src.endsWith('.test.mjs')
});

// 3. 應用(同幾何:src/frontend)
cpSync(path.join(root, 'src', 'frontend'), path.join(dist, 'src', 'frontend'), { recursive: true, dereference: true });

// 4. 根進入點轉導
writeFileSync(path.join(dist, 'index.html'),
    '<!DOCTYPE html><html><head><meta charset="utf-8">\n' +
    '<meta http-equiv="refresh" content="0; url=./src/frontend/index.html">\n' +
    `<title>${projectName}</title></head><body><a href="./src/frontend/index.html">${projectName}</a></body></html>\n`);

// 5. 來源憑證 SNAPSHOT.json(git 資訊;repo 可能尚無 commit)
function git(cwd, ...args) {
    const r = spawnSync('git', ['-C', cwd, ...args], { encoding: 'utf8' });
    return r.status === 0 ? r.stdout.trim() : '';
}
const b4aRepositoryCommit = git(b4aRoot, 'rev-parse', 'HEAD');
const b4aTree = enforcedTree || (libIsLink ? b4aCurrentTree() : syncState?.tree) || b4aCurrentTree();
const appCommit = git(root, 'rev-parse', 'HEAD');
let fileCount = 0, totalBytes = 0;
(function walk(dir) {
    for (const name of readdirSync(dir)) {
        const p = path.join(dir, name);
        const st = statSync(p);
        if (st.isDirectory()) walk(p);
        else { fileCount++; totalBytes += st.size; }
    }
})(path.join(dist, 'lib'));
const mechanism = JSON.parse(readFileSync(path.join(scriptsDir, 'mechanism.json'), 'utf8'));
const snapshot = {
    source: path.join(b4aRoot, 'packages', 'javascript', 'browser') + (libIsLink ? ' (via lib link)' : ' (via lib copy)'),
    mechanismVersion: mechanism.mechanismVersion,
    libMode: libIsLink ? 'link' : 'copy',
    lock: lock ? {
        version: lock.version,
        ...(lock.kind === 'tree' ? { source: lock.source, tree: lock.tree } : { legacyCommit: lock.commit }),
        enforcement
    } : null,
    bricks4agent: {
        tree: isFullGitObjectId(b4aTree) ? b4aTree : '(no tree)',
        repositoryCommit: isFullGitObjectId(b4aRepositoryCommit) ? b4aRepositoryCommit : '(no commit)',
        dirty: libIsLink ? !!b4aWorkingTreeStatus() : !!syncState?.dirtySource
    },
    project: { name: projectName, commit: isFullGitObjectId(appCommit) ? appCommit : '(no commit)' },
    content: buildLibraryContentManifest(path.join(dist, 'lib')),
    snapshotTimeUtc: new Date().toISOString(),
    fileCount, totalBytes
};
writeFileSync(path.join(dist, 'lib', 'SNAPSHOT.json'), JSON.stringify(snapshot, null, 2) + '\n');

// 6. 封閉性驗證(違規=發佈失敗)
const v = spawnSync(process.execPath, [path.join(scriptsDir, 'verify-sealed.mjs'), dist], { stdio: 'inherit' });
if (v.status !== 0) {
    console.error('封閉性驗證失敗——dist 非自含,發佈中止。');
    process.exit(1);
}

console.log('');
console.log(`dist/ 就緒：lib ${fileCount} 檔(${(totalBytes / 1048576).toFixed(1)} MB)，腳手架 tree ` +
    `${snapshot.bricks4agent.tree.slice(0, 8)}${snapshot.bricks4agent.dirty ? '（工作區 DIRTY!）' : ''}`);
console.log('任何靜態伺服器指向 dist/ 即可;進入點 /src/frontend/index.html(根 index.html 會轉導)。');
