// upgrade-project.mjs — 把既有專案的「機制腳本」升級到目前範本版(解決機制腳本艦隊漂移)。
// 只動機制檔,絕不碰 src/、docs/、b4a.lock.json;專案 scripts/mechanism.json 的 customized
// 清單內的檔案會跳過(如 tim-web 自訂的 dev.mjs)。升級後請以 git diff 審閱再 commit。
//   用法: node tools/create-project/upgrade-project.mjs --dest <專案路徑> [--dry-run]
import { existsSync, readFileSync, writeFileSync, copyFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const MECHANISM_FILES = [
    'resolve-b4a.mjs', 'dev-link.mjs', 'sync-lib.mjs', 'publish.mjs', 'dev.mjs', 'verify-sealed.mjs'
];

const args = process.argv.slice(2);
const opt = (k) => { const i = args.indexOf(k); return i > -1 ? args[i + 1] : ''; };
const dest = opt('--dest') ? path.resolve(opt('--dest')) : '';
const dryRun = args.includes('--dry-run');
if (!dest || !existsSync(path.join(dest, 'scripts'))) {
    console.error('用法:node upgrade-project.mjs --dest <專案路徑> [--dry-run](需含 scripts/ 目錄)');
    process.exit(1);
}

const here = path.dirname(fileURLToPath(import.meta.url));
const tplScripts = path.join(here, 'template', 'scripts');
const destScripts = path.join(dest, 'scripts');
const tplMeta = JSON.parse(readFileSync(path.join(tplScripts, 'mechanism.json'), 'utf8'));
const destMetaPath = path.join(destScripts, 'mechanism.json');
const destMeta = existsSync(destMetaPath) ? JSON.parse(readFileSync(destMetaPath, 'utf8')) : { mechanismVersion: '(pre-2.0 無版本)', customized: [] };
const customized = new Set(destMeta.customized || []);

console.log(`機制版本:${destMeta.mechanismVersion} → ${tplMeta.mechanismVersion}${dryRun ? '(dry-run)' : ''}`);
let changed = 0, skipped = 0, same = 0;
for (const f of MECHANISM_FILES) {
    const src = path.join(tplScripts, f);
    const dst = path.join(destScripts, f);
    if (customized.has(f)) { console.log(`  skip(customized): ${f}`); skipped++; continue; }
    const oldText = existsSync(dst) ? readFileSync(dst, 'utf8') : null;
    const newText = readFileSync(src, 'utf8');
    if (oldText === newText) { same++; continue; }
    console.log(`  ${oldText === null ? 'add ' : 'update'}: scripts/${f}`);
    if (!dryRun) copyFileSync(src, dst);
    changed++;
}
if (!dryRun) {
    writeFileSync(destMetaPath, JSON.stringify({ ...destMeta, mechanismVersion: tplMeta.mechanismVersion }, null, 2) + '\n');
}
console.log(`完成:更新 ${changed}、跳過(自訂)${skipped}、已同版 ${same}。${dryRun ? '' : '請 git diff 審閱後 commit。'}`);
