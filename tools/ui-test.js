/**
 * UI 測試工具 - 無第三方依賴
 * 使用 Chrome/Edge 內建的 headless 模式進行截圖和測試
 *
 * 用法：
 *   node tools/ui-test.js <html-file> [options]
 *
 * 選項：
 *   --screenshot <output.png>  截圖輸出路徑
 *   --width <number>           視窗寬度 (預設 1280)
 *   --height <number>          視窗高度 (預設 800)
 *   --wait <ms>                等待時間 (預設 3000)
 *   --click <selector>         點擊元素
 *   --console                  捕獲控制台日誌
 */

const { spawn, execSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const http = require('http');

// 尋找 Chrome/Edge 可執行檔
function findBrowser() {
    const possiblePaths = [
        // Windows Chrome
        'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
        'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
        process.env.LOCALAPPDATA + '\\Google\\Chrome\\Application\\chrome.exe',
        // Windows Edge
        'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
        'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe',
        // Linux
        '/usr/bin/google-chrome',
        '/usr/bin/chromium-browser',
        // macOS
        '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    ];

    for (const p of possiblePaths) {
        if (fs.existsSync(p)) {
            return p;
        }
    }

    // 嘗試用 where/which 命令
    try {
        const cmd = process.platform === 'win32' ? 'where' : 'which';
        const result = execSync(`${cmd} chrome 2>nul || ${cmd} google-chrome 2>nul || ${cmd} msedge 2>nul`, {
            encoding: 'utf8'
        }).trim().split('\n')[0];
        if (result && fs.existsSync(result)) {
            return result;
        }
    } catch (e) {}

    return null;
}

// 解析命令列參數
function parseArgs(args) {
    const options = {
        file: null,
        screenshot: null,
        width: 1280,
        height: 800,
        wait: 3000,
        click: null,
        console: false,
        serve: false,
        port: 8765
    };

    for (let i = 0; i < args.length; i++) {
        const arg = args[i];
        if (arg === '--screenshot' && args[i + 1]) {
            options.screenshot = args[++i];
        } else if (arg === '--width' && args[i + 1]) {
            options.width = parseInt(args[++i]);
        } else if (arg === '--height' && args[i + 1]) {
            options.height = parseInt(args[++i]);
        } else if (arg === '--wait' && args[i + 1]) {
            options.wait = parseInt(args[++i]);
        } else if (arg === '--click' && args[i + 1]) {
            options.click = args[++i];
        } else if (arg === '--console') {
            options.console = true;
        } else if (arg === '--serve') {
            options.serve = true;
        } else if (arg === '--port' && args[i + 1]) {
            options.port = parseInt(args[++i]);
        } else if (!arg.startsWith('--') && !options.file) {
            options.file = arg;
        }
    }

    return options;
}

// 建立簡易 HTTP 伺服器（用於 ES modules）
function createServer(rootDir, port) {
    return new Promise((resolve, reject) => {
        const mimeTypes = {
            '.html': 'text/html',
            '.js': 'application/javascript',
            '.mjs': 'application/javascript',
            '.css': 'text/css',
            '.json': 'application/json',
            '.png': 'image/png',
            '.jpg': 'image/jpeg',
            '.svg': 'image/svg+xml'
        };

        const server = http.createServer((req, res) => {
            let filePath = path.join(rootDir, req.url === '/' ? 'index.html' : req.url);
            filePath = filePath.split('?')[0]; // 移除 query string

            const ext = path.extname(filePath).toLowerCase();
            const contentType = mimeTypes[ext] || 'application/octet-stream';

            fs.readFile(filePath, (err, content) => {
                if (err) {
                    if (err.code === 'ENOENT') {
                        res.writeHead(404);
                        res.end('Not Found');
                    } else {
                        res.writeHead(500);
                        res.end('Server Error');
                    }
                } else {
                    res.writeHead(200, { 'Content-Type': contentType });
                    res.end(content);
                }
            });
        });

        server.listen(port, () => {
            console.log(`[Server] 啟動於 http://localhost:${port}`);
            resolve(server);
        });

        server.on('error', reject);
    });
}

// 使用 Chrome DevTools Protocol 進行進階操作
async function runWithCDP(browser, url, options) {
    const { spawn } = require('child_process');
    const net = require('net');

    // 找一個可用的 port
    const debugPort = 9222 + Math.floor(Math.random() * 1000);

    // 建立臨時使用者資料目錄
    const tempDir = path.join(require('os').tmpdir(), `chrome-test-${Date.now()}`);
    fs.mkdirSync(tempDir, { recursive: true });

    const args = [
        '--headless=new',
        `--remote-debugging-port=${debugPort}`,
        `--user-data-dir=${tempDir}`,
        '--disable-gpu',
        '--no-sandbox',
        '--disable-dev-shm-usage',
        `--window-size=${options.width},${options.height}`,
    ];

    if (options.screenshot) {
        args.push(`--screenshot=${path.resolve(options.screenshot)}`);
    }

    args.push(url);

    console.log(`[Browser] 啟動: ${browser}`);
    console.log(`[Browser] URL: ${url}`);
    console.log(`[Browser] 視窗: ${options.width}x${options.height}`);

    return new Promise((resolve, reject) => {
        const proc = spawn(browser, args, {
            stdio: ['ignore', 'pipe', 'pipe']
        });

        let stdout = '';
        let stderr = '';

        proc.stdout.on('data', (data) => {
            stdout += data.toString();
            if (options.console) {
                console.log('[Console]', data.toString().trim());
            }
        });

        proc.stderr.on('data', (data) => {
            stderr += data.toString();
            // Chrome 的日誌輸出在 stderr
            const lines = data.toString().split('\n');
            for (const line of lines) {
                if (line.includes('console.log') || line.includes('LOG:')) {
                    console.log('[Console]', line.trim());
                }
            }
        });

        // 等待指定時間後結束
        setTimeout(() => {
            proc.kill();
        }, options.wait + 2000);

        proc.on('close', (code) => {
            // 清理臨時目錄
            try {
                fs.rmSync(tempDir, { recursive: true, force: true });
            } catch (e) {}

            if (options.screenshot && fs.existsSync(options.screenshot)) {
                console.log(`[Screenshot] 已儲存: ${options.screenshot}`);
            }

            resolve({ stdout, stderr, code });
        });

        proc.on('error', reject);
    });
}

// 使用 CDP (Chrome DevTools Protocol) 進行進階測試
async function runWithCDPAdvanced(browser, url, options) {
    const net = require('net');
    const http = require('http');

    const debugPort = 9222 + Math.floor(Math.random() * 1000);
    const tempDir = path.join(require('os').tmpdir(), `chrome-cdp-${Date.now()}`);
    fs.mkdirSync(tempDir, { recursive: true });

    const args = [
        '--headless=new',
        `--remote-debugging-port=${debugPort}`,
        `--user-data-dir=${tempDir}`,
        '--disable-gpu',
        '--no-sandbox',
        '--disable-dev-shm-usage',
        `--window-size=${options.width},${options.height}`,
    ];

    console.log(`[CDP] 啟動瀏覽器，debug port: ${debugPort}`);

    const proc = spawn(browser, args, { stdio: ['ignore', 'pipe', 'pipe'] });

    // 等待瀏覽器啟動
    await new Promise(r => setTimeout(r, 2000));

    try {
        // 獲取 WebSocket URL
        const wsUrl = await new Promise((resolve, reject) => {
            http.get(`http://127.0.0.1:${debugPort}/json/version`, (res) => {
                let data = '';
                res.on('data', chunk => data += chunk);
                res.on('end', () => {
                    try {
                        const json = JSON.parse(data);
                        resolve(json.webSocketDebuggerUrl);
                    } catch (e) {
                        reject(e);
                    }
                });
            }).on('error', reject);
        });

        console.log(`[CDP] WebSocket: ${wsUrl}`);

        // 這裡需要 WebSocket 客戶端來進一步操作
        // 由於沒有第三方庫，我們使用簡化的方法

    } finally {
        proc.kill();
        try {
            fs.rmSync(tempDir, { recursive: true, force: true });
        } catch (e) {}
    }
}

// 簡易模式：直接使用 Chrome 的 --screenshot 參數
async function runSimple(browser, url, options) {
    const tempDir = path.join(require('os').tmpdir(), `chrome-test-${Date.now()}`);
    fs.mkdirSync(tempDir, { recursive: true });

    const screenshotPath = options.screenshot
        ? path.resolve(options.screenshot)
        : path.join(tempDir, 'screenshot.png');

    const args = [
        '--headless=new',
        `--screenshot=${screenshotPath}`,
        `--window-size=${options.width},${options.height}`,
        `--user-data-dir=${tempDir}`,
        '--disable-gpu',
        '--no-sandbox',
        '--hide-scrollbars',
        `--virtual-time-budget=${options.wait}`,
        url
    ];

    console.log(`[Browser] 啟動 headless 模式...`);
    console.log(`[URL] ${url}`);

    return new Promise((resolve, reject) => {
        const proc = spawn(browser, args, {
            stdio: ['ignore', 'pipe', 'pipe']
        });

        let output = '';

        proc.stdout.on('data', (data) => {
            output += data.toString();
        });

        proc.stderr.on('data', (data) => {
            output += data.toString();
        });

        proc.on('close', (code) => {
            // 清理臨時目錄（但保留截圖）
            setTimeout(() => {
                try {
                    if (!options.screenshot) {
                        // 如果沒指定輸出路徑，複製到當前目錄
                        const defaultOutput = 'ui-test-screenshot.png';
                        if (fs.existsSync(screenshotPath)) {
                            fs.copyFileSync(screenshotPath, defaultOutput);
                            console.log(`[Screenshot] 已儲存: ${defaultOutput}`);
                        }
                    } else if (fs.existsSync(screenshotPath)) {
                        console.log(`[Screenshot] 已儲存: ${screenshotPath}`);
                    }
                    fs.rmSync(tempDir, { recursive: true, force: true });
                } catch (e) {}
            }, 500);

            resolve({ output, code, screenshotPath: options.screenshot || 'ui-test-screenshot.png' });
        });

        proc.on('error', reject);
    });
}

// 主程式
async function main() {
    const args = process.argv.slice(2);

    if (args.length === 0 || args.includes('--help')) {
        console.log(`
UI 測試工具 - 使用 Chrome/Edge headless 模式

用法:
  node tools/ui-test.js <html-file> [options]

選項:
  --screenshot <path>  截圖輸出路徑 (預設: ui-test-screenshot.png)
  --width <number>     視窗寬度 (預設: 1280)
  --height <number>    視窗高度 (預設: 800)
  --wait <ms>          等待時間 (預設: 3000)
  --serve              啟動 HTTP 伺服器 (用於 ES modules)
  --port <number>      伺服器埠號 (預設: 8765)
  --console            顯示控制台輸出

範例:
  node tools/ui-test.js demos/viz/MapEditor.html --screenshot test.png --serve
  node tools/ui-test.js https://example.com --screenshot example.png
`);
        return;
    }

    const options = parseArgs(args);

    if (!options.file) {
        console.error('錯誤: 請指定 HTML 檔案或 URL');
        process.exit(1);
    }

    // 尋找瀏覽器
    const browser = findBrowser();
    if (!browser) {
        console.error('錯誤: 找不到 Chrome 或 Edge 瀏覽器');
        process.exit(1);
    }
    console.log(`[Browser] 找到: ${browser}`);

    let url = options.file;
    let server = null;

    // 如果是本地檔案且需要 ES modules，啟動伺服器
    if (!url.startsWith('http') && options.serve) {
        const absolutePath = path.resolve(options.file);
        const rootDir = path.dirname(absolutePath);
        const fileName = path.basename(absolutePath);

        // 往上找專案根目錄（有 package.json 的地方）
        let projectRoot = rootDir;
        while (projectRoot !== path.dirname(projectRoot)) {
            if (fs.existsSync(path.join(projectRoot, 'package.json'))) {
                break;
            }
            projectRoot = path.dirname(projectRoot);
        }

        server = await createServer(projectRoot, options.port);

        // 計算相對路徑
        const relativePath = path.relative(projectRoot, absolutePath).replace(/\\/g, '/');
        url = `http://localhost:${options.port}/${relativePath}`;
    } else if (!url.startsWith('http')) {
        // file:// 協議
        url = `file:///${path.resolve(options.file).replace(/\\/g, '/')}`;
    }

    try {
        const result = await runSimple(browser, url, options);
        console.log(`[完成] 退出碼: ${result.code}`);

        if (result.output) {
            console.log('\n[輸出]');
            console.log(result.output);
        }
    } finally {
        if (server) {
            server.close();
            console.log('[Server] 已關閉');
        }
    }
}

main().catch(err => {
    console.error('錯誤:', err.message);
    process.exit(1);
});
