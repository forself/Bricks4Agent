// resolve-b4a.mjs — 定位 Bricks4Agent 腳手架(零依賴、零寫死路徑、跨平台)。
// 解析順序:
//   1. 環境變數 B4A_ROOT              (機器/CI 層級)
//   2. <專案根>\.b4a-root 檔          (單行路徑;每機自有、gitignored)
//   3. 同層搜尋:專案往上 3 層找 Bricks4Agent\
// 匯出 resolveB4a(projectRoot) → { b4aRoot, b4aBrowser };找不到擲出附設定教學的錯誤。
import { existsSync, readFileSync } from 'node:fs';
import path from 'node:path';

const MARKER = path.join('packages', 'javascript', 'browser', 'ui_components', 'theme.css');
const isB4aRoot = (p) => !!p && existsSync(path.join(p, MARKER));

export function resolveB4a(projectRoot) {
    let b4aRoot = null;
    if (isB4aRoot(process.env.B4A_ROOT)) {
        b4aRoot = path.resolve(process.env.B4A_ROOT);
    } else {
        const f = path.join(projectRoot, '.b4a-root');
        if (existsSync(f)) {
            const cand = readFileSync(f, 'utf8').split(/\r?\n/)[0].trim();
            if (isB4aRoot(cand)) b4aRoot = path.resolve(cand);
        }
    }
    if (!b4aRoot) {
        let up = projectRoot;
        for (let i = 0; i < 3; i++) {
            const parent = path.dirname(up);
            if (parent === up) break;
            up = parent;
            const cand = path.join(up, 'Bricks4Agent');
            if (isB4aRoot(cand)) { b4aRoot = cand; break; }
        }
    }
    if (!b4aRoot) {
        throw new Error(
            'Bricks4Agent scaffold not found. Fix one of:\n' +
            '  1) set env var B4A_ROOT to the Bricks4Agent repo root, or\n' +
            '  2) write the repo root path into <project>/.b4a-root (single line), or\n' +
            '  3) place this project as a sibling of the Bricks4Agent folder.');
    }
    return { b4aRoot, b4aBrowser: path.join(b4aRoot, 'packages', 'javascript', 'browser') };
}
