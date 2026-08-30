/**
 * 簡易 HTTP 伺服器
 * 用於本地開發測試
 */
const http = require('http');
const fs = require('fs');
const path = require('path');

const PORT = process.argv[2] || 8765;
const ROOT = path.resolve(__dirname, '..');

// 預設只綁 loopback；指定非 loopback 位址等同自願把整個 repo 開放到區網
const BIND_HOST = process.argv[3] || process.env.SERVE_HOST || '127.0.0.1';

const mimeTypes = {
    '.html': 'text/html',
    '.js': 'application/javascript',
    '.mjs': 'application/javascript',
    '.css': 'text/css',
    '.json': 'application/json',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon'
};

function normalizeHostname(value) {
    if (typeof value !== 'string') return '';
    const trimmed = value.trim().toLowerCase();
    if (trimmed.startsWith('[')) {
        const end = trimmed.indexOf(']');
        return end === -1 ? '' : trimmed.slice(1, end);
    }
    const colon = trimmed.indexOf(':');
    return colon === -1 ? trimmed : trimmed.slice(0, colon);
}

function isLoopbackHostname(hostname) {
    if (!hostname) return false;
    if (hostname === 'localhost') return true;
    if (hostname === '::1' || hostname === '0:0:0:0:0:0:0:1') return true;
    if (hostname.startsWith('::ffff:127.')) return true;
    return /^127\.\d{1,3}\.\d{1,3}\.\d{1,3}$/.test(hostname);
}

const ALLOW_REMOTE_HOSTS = !isLoopbackHostname(normalizeHostname(BIND_HOST));

function isWithinRoot(root, target) {
    const resolvedRoot = path.resolve(root);
    const resolvedTarget = path.resolve(target);
    const prefix = resolvedRoot.endsWith(path.sep) ? resolvedRoot : resolvedRoot + path.sep;
    if (process.platform === 'win32') {
        const lowerTarget = resolvedTarget.toLowerCase();
        return lowerTarget === resolvedRoot.toLowerCase() || lowerTarget.startsWith(prefix.toLowerCase());
    }
    return resolvedTarget === resolvedRoot || resolvedTarget.startsWith(prefix);
}

const server = http.createServer((req, res) => {
    // Host 必須指向本機，擋掉 DNS rebinding
    if (!ALLOW_REMOTE_HOSTS && !isLoopbackHostname(normalizeHostname(req.headers.host))) {
        res.writeHead(403);
        res.end('Forbidden');
        return;
    }

    let filePath = path.join(ROOT, req.url === '/' ? 'index.html' : req.url);
    filePath = filePath.split('?')[0];

    // 只能讀 repo 根目錄底下的檔案（擋 /../ 這類點段跳脫）
    if (!isWithinRoot(ROOT, filePath)) {
        res.writeHead(403);
        res.end('Forbidden');
        return;
    }

    const ext = path.extname(filePath).toLowerCase();
    const contentType = mimeTypes[ext] || 'application/octet-stream';

    // 只回應本機的 Origin，避免任意網站讀取 repo 內容
    const origin = req.headers.origin;
    let originHostname = '';
    try {
        originHostname = origin ? normalizeHostname(new URL(origin).hostname) : '';
    } catch {
        originHostname = '';
    }

    fs.readFile(filePath, (err, content) => {
        if (err) {
            if (err.code === 'ENOENT') {
                res.writeHead(404);
                res.end('Not Found: ' + req.url);
            } else {
                res.writeHead(500);
                res.end('Server Error');
            }
        } else {
            const headers = { 'Content-Type': contentType, 'Vary': 'Origin' };
            if (isLoopbackHostname(originHostname)) {
                headers['Access-Control-Allow-Origin'] = origin;
            }
            res.writeHead(200, headers);
            res.end(content);
        }
    });
});

server.listen(PORT, BIND_HOST, () => {
    console.log(`伺服器啟動: http://localhost:${PORT}`);
    console.log(`根目錄: ${ROOT}`);
    if (ALLOW_REMOTE_HOSTS) {
        console.log(`警告: 已綁定 ${BIND_HOST}（非 loopback），整個 repo 將對區網可讀`);
    }
});
