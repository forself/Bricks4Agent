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
import { existsSync, rmSync, mkdirSync, cpSync, writeFileSync, readFileSync, readdirSync, statSync, lstatSync, renameSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import * as vm from 'node:vm';
import { buildLibraryContentManifest } from './snapshot-integrity.mjs';
import { resolveB4a } from './resolve-b4a.mjs';
import { isFullGitObjectId, parseB4aLock } from './b4a-lock.mjs';
import { copySelectedLibrary, findUnselectedAssets, planPublishedFiles, readPublishPolicy } from './publish-policy.mjs';

if (typeof vm.SourceTextModule !== 'function') {
    const resumed = spawnSync(process.execPath, ['--experimental-vm-modules', fileURLToPath(import.meta.url), ...process.argv.slice(2)], {
        stdio: 'inherit', env: process.env,
    });
    process.exit(resumed.status ?? 1);
}

const scriptsDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptsDir, '..');
const dist = path.join(root, 'dist');
const projectName = path.basename(root);
const policyArg = process.argv.findIndex((arg) => arg === '--policy');
if (process.argv.includes('--policy') && (!process.argv[policyArg + 1] || process.argv[policyArg + 1].startsWith('--'))) {
    console.error('用法: node scripts/publish.mjs [--policy <publish-policy.json>]');
    process.exit(2);
}
const policyPath = policyArg >= 0
    ? path.resolve(root, process.argv[policyArg + 1])
    : path.join(root, 'publish-policy.json');
const hasPolicy = existsSync(policyPath);
if (policyArg >= 0 && !hasPolicy) {
    console.error(`找不到發布 policy: ${policyPath}`);
    process.exit(2);
}
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

// Build in a sibling staging directory. A failed publish leaves the prior dist untouched.
const stage = path.join(root, `.dist-staging-${process.pid}-${Date.now()}`);
const backup = path.join(root, `.dist-backup-${process.pid}-${Date.now()}`);
let committed = false;
process.on('exit', () => { if (!committed) rmSync(stage, { recursive: true, force: true }); });
mkdirSync(stage, { recursive: true });

// 2. 腳手架快照(dereference 穿透連結;排除 node_modules / *.test.mjs / page-generator examples)
const notNodeModules = (src) => path.basename(src) !== 'node_modules';
const libSource = path.join(root, 'lib');
if (hasPolicy) {
    // policy 裁剪是實驗性功能:只沿靜態引用判斷可達,執行期才組出的路徑一律看不到。
    console.warn(`[實驗性] 以發布 policy 裁剪 lib:${path.relative(root, policyPath).split(path.sep).join('/')}`);
    console.warn('  只保留靜態 import/引用可達的檔案;非 JS 的執行期資產與動態組出的路徑須在 keep / dynamicImportKeep 明列(見 docs/dev-and-publish.md)。');
    const policy = readPublishPolicy(policyPath);
    const selected = planPublishedFiles({ libRoot: libSource, frontendRoot: path.join(root, 'src', 'frontend'), policy });
    const omittedAssets = findUnselectedAssets({ libRoot: libSource, selected });
    if (omittedAssets.length) {
        const byDirectory = new Map();
        for (const file of omittedAssets) {
            const directory = path.posix.dirname(file);
            byDirectory.set(directory, [...(byDirectory.get(directory) ?? []), file]);
        }
        const groups = [...byDirectory].sort((left, right) => right[1].length - left[1].length || (left[0] < right[0] ? -1 : 1));
        console.warn(`警告:${omittedAssets.length} 個資產檔位於已納入的目錄,但沒有靜態引用而未發布;執行期若會載入,請列入 policy 的 keep:`);
        for (const [directory, files] of groups.slice(0, 20)) {
            console.warn(files.length === 1 ? `  lib/${files[0]}` : `  lib/${directory}/ (${files.length} 檔)`);
        }
        if (groups.length > 20) console.warn(`  …另 ${groups.length - 20} 個目錄`);
    }
    for (const library of ['ui_components', 'page-generator', 'custom_components']) {
        const source = path.join(libSource, library);
        if (!existsSync(source)) continue;
        mkdirSync(path.join(stage, 'lib', library), { recursive: true });
        const selectedLibrary = new Set([...selected].filter((file) => file === source || file.startsWith(source + path.sep)));
        copySelectedLibrary({ sourceRoot: libSource, targetRoot: path.join(stage, 'lib'), selected: selectedLibrary });
    }
} else {
    cpSync(path.join(root, 'lib', 'ui_components'), path.join(stage, 'lib', 'ui_components'), {
        recursive: true, dereference: true,
        filter: (src) => notNodeModules(src) && !src.endsWith('.test.mjs')
    });
    cpSync(path.join(root, 'lib', 'page-generator'), path.join(stage, 'lib', 'page-generator'), {
        recursive: true, dereference: true,
        filter: (src) => notNodeModules(src) && path.basename(src) !== 'examples'
    });
    cpSync(path.join(root, 'lib', 'custom_components'), path.join(stage, 'lib', 'custom_components'), {
        recursive: true, dereference: true,
        filter: (src) => notNodeModules(src) && !src.endsWith('.test.mjs')
    });
}

// 3. 應用(同幾何:src/frontend)
cpSync(path.join(root, 'src', 'frontend'), path.join(stage, 'src', 'frontend'), { recursive: true, dereference: true });

// 4. 根進入點轉導
writeFileSync(path.join(stage, 'index.html'),
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
})(path.join(stage, 'lib'));
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
    content: buildLibraryContentManifest(path.join(stage, 'lib')),
    publishPolicy: hasPolicy ? path.relative(root, policyPath).split(path.sep).join('/') : null,
    snapshotTimeUtc: new Date().toISOString(),
    fileCount, totalBytes
};
writeFileSync(path.join(stage, 'lib', 'SNAPSHOT.json'), JSON.stringify(snapshot, null, 2) + '\n');

// 6. 封閉性驗證(違規=發佈失敗)
const v = spawnSync(process.execPath, [path.join(scriptsDir, 'verify-sealed.mjs'), stage], { stdio: 'inherit' });
if (v.status !== 0) {
    console.error('封閉性驗證失敗——dist 非自含,發佈中止。');
    process.exit(1);
}

// Replace dist only after a complete, verified candidate exists. Restore on rename failure.
if (existsSync(dist)) renameSync(dist, backup);
try {
    renameSync(stage, dist);
    committed = true;
    rmSync(backup, { recursive: true, force: true });
} catch (error) {
    if (existsSync(backup) && !existsSync(dist)) renameSync(backup, dist);
    throw error;
}

console.log('');
console.log(`dist/ 就緒：lib ${fileCount} 檔(${(totalBytes / 1048576).toFixed(1)} MB)，腳手架 tree ` +
    `${snapshot.bricks4agent.tree.slice(0, 8)}${snapshot.bricks4agent.dirty ? '（工作區 DIRTY!）' : ''}`);
console.log('任何靜態伺服器指向 dist/ 即可;進入點 /src/frontend/index.html(根 index.html 會轉導)。');
if (hasPolicy) console.log('[實驗性] 本次為 policy 裁剪發布:上線前請實際操作各頁面,確認執行期資產沒有缺漏。');
