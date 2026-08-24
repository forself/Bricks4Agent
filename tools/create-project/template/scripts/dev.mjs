// dev.mjs — 開發靜態伺服器(root=專案根,lib 連結與 src 同源)。跨平台零依賴:
// 沿用專案工具鏈的 python http.server(python/python3 自動偵測),不自造伺服器。
//   用法: node scripts/dev.mjs [--port 8123]
import { spawnSync, spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const i = process.argv.indexOf('--port');
const port = i > -1 ? Number(process.argv[i + 1]) : 8123;

const py = ['python', 'python3'].find(c => spawnSync(c, ['--version'], { stdio: 'ignore' }).status === 0);
if (!py) { console.error('找不到 python/python3(靜態伺服用)。'); process.exit(1); }

console.log(`App:  http://127.0.0.1:${port}/src/frontend/index.html`);
const child = spawn(py, ['-m', 'http.server', String(port), '--bind', '127.0.0.1', '--directory', root], { stdio: 'inherit' });
child.on('exit', (code) => process.exit(code ?? 0));
